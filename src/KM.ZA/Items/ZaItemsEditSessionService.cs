// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.EvolutionItems;
using KM.ZA.Shops;
using KM.ZA.Workflows;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.ZA.Items;

internal sealed class ZaItemsEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaItemsWorkflowService itemsWorkflowService;

    public ZaItemsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaItemsWorkflowService? itemsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.itemsWorkflowService = itemsWorkflowService ?? new ZaItemsWorkflowService(this.fileSource);
    }

    public ZaItemsEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int itemId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = itemsWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.ItemsDomain,
                diagnostics))
        {
            return new ZaItemsEditResult(workflow, currentSession, diagnostics);
        }

        var item = workflow.Items.FirstOrDefault(candidate => candidate.ItemId == itemId);
        if (item is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item {itemId} is not present in the loaded Items workflow.",
                ZaEditSessionSupport.ItemsDomain,
                field: "itemId",
                expected: "Existing Z-A item record"));
            return new ZaItemsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, item, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaItemsEditResult(workflow, currentSession, diagnostics);
        }

        if (!TryStagePendingEdit(
                workflow,
                currentSession,
                pendingEdit,
                diagnostics,
                out var updatedSession,
                out _))
        {
            return new ZaItemsEditResult(workflow, currentSession, diagnostics);
        }

        updatedSession = RemoveSourceEquivalentPendingEdits(loadedWorkflow, updatedSession);
        return new ZaItemsEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaItemsEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaItemFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = itemsWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.ItemsDomain,
                diagnostics))
        {
            return new ZaItemsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Item batch update is missing a field or value.",
                    ZaEditSessionSupport.ItemsDomain,
                    field: "updates",
                    expected: "Complete item field update"));
                continue;
            }

            var item = effectiveWorkflow.Items.FirstOrDefault(candidate => candidate.ItemId == update.ItemId);
            if (item is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Item {update.ItemId} is not present in the loaded Items workflow.",
                    ZaEditSessionSupport.ItemsDomain,
                    field: "itemId",
                    expected: "Existing Z-A item record"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(
                effectiveWorkflow,
                item,
                update.Field,
                update.Value,
                diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            if (!TryStagePendingEdit(
                    effectiveWorkflow,
                    updatedSession,
                    pendingEdit,
                    diagnostics,
                    out updatedSession,
                    out effectiveWorkflow))
            {
                continue;
            }

            updatedSession = RemoveSourceEquivalentPendingEdits(loadedWorkflow, updatedSession);
            effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits);
        }

        return new ZaItemsEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = itemsWorkflowService.Load(project);
        var effectiveSession = RemoveSourceEquivalentPendingEdits(workflow, session);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.ItemsDomain,
            diagnostics);

        ValidateUniquePendingEditTargets(effectiveSession, diagnostics);
        foreach (var edit in effectiveSession.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            ValidateTechnicalMachineNumberAssignments(workflow, effectiveSession, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            ValidateEvolutionItemUseCompatibility(workflow, effectiveSession, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            ValidateEvolutionItemConversions(project, effectiveSession, diagnostics);
        }

        if (effectiveSession.PendingEdits.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Items change is valid.",
                ZaEditSessionSupport.ItemsDomain));
        }

        return new ZaEditSessionValidation(
            effectiveSession,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var effectiveSession = validation.Session;
        var plan = ZaEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            effectiveSession,
            ZaEditSessionSupport.ItemsDomain,
            ZaDataPaths.ItemDataArray,
            "Items",
            validation.Diagnostics,
            outputMode);
        plan = AddItemSourceFingerprint(
            paths,
            plan,
            outputMode,
            effectiveSession.PendingEdits);
        plan = AddLegacyTechnicalMachineShopMigrationPlan(paths, plan, outputMode);
        if (!plan.CanApply || !HasEnabledEvolutionItemEdit(effectiveSession))
        {
            return AddStandaloneDescriptorFingerprint(paths, plan, outputMode);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var conversionState = ZaEvolutionItemConversionState.Load(project, fileSource);
            PrepareEvolutionItemConversions(effectiveSession, conversionState);
            if (!conversionState.Modified)
            {
                return AddStandaloneDescriptorFingerprint(paths, plan, outputMode);
            }

            var writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.EvolutionItemConversionArray,
                [conversionState.SourceReference()],
                outputMode);
            var conversionWrite = new PlannedFileWrite(
                writeInfo.TargetRelativePath,
                writeInfo.Sources,
                writeInfo.ReplacesExistingOutput,
                "Assign enabled evolution items to approved game conversion parameters.",
                CreatePlanSourceFingerprint(
                    paths,
                    ZaDataPaths.EvolutionItemConversionArray,
                    outputMode,
                    ReadEvolutionPlanSemanticSources(project)));
            plan = new ChangePlan(
                plan.SessionId,
                [conversionWrite, .. plan.Writes],
                plan.Diagnostics);
            return AddStandaloneDescriptorFingerprint(paths, plan, outputMode);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            var diagnostics = plan.Diagnostics
                .Append(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Items could not prepare evolution item conversions: {exception.Message}",
                    ZaEditSessionSupport.ItemsDomain,
                    file: $"romfs/{ZaDataPaths.EvolutionItemConversionArray}",
                    expected: "Readable conversion table with an approved allocation slot"))
                .ToArray();
            return new ChangePlan(plan.SessionId, Array.Empty<PlannedFileWrite>(), diagnostics);
        }
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        IDisposable outputLock;
        try
        {
            outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
        }
        catch (Exception exception)
        {
            var lockDiagnostics = new[]
            {
                ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Items output is busy or unavailable: {exception.Message}",
                    ZaEditSessionSupport.ItemsDomain,
                    expected: "Exclusive access to the selected output root"),
            };
            return ZaEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                reviewedPlan,
                Array.Empty<ProjectFileReference>(),
                lockDiagnostics);
        }

        using var acquiredOutputLock = outputLock;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                ZaEditSessionSupport.ItemsDomain,
                expected: "Current reviewed Items change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var effectiveSession = RemoveSourceEquivalentPendingEdits(
                itemsWorkflowService.Load(project),
                session);
            var source = fileSource.Read(project, ZaDataPaths.ItemDataArray);
            var baseItemSource = source.SourceLayer == ProjectFileLayer.Layered
                ? fileSource.ReadBase(project, ZaDataPaths.ItemDataArray)
                : null;
            var itemSemanticState = CaptureItemPlanSemanticState(
                project,
                source,
                baseItemSource);
            if (!CapturedSourcesMatchPlan(
                    paths,
                    currentPlan,
                    ZaDataPaths.ItemDataArray,
                    outputMode,
                    itemSemanticState.Sources,
                    CreatePlanChangeSetFingerprint(effectiveSession.PendingEdits)))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Items source or destination changed after review. Review the change plan again before applying.",
                    ZaEditSessionSupport.ItemsDomain,
                    expected: "The exact reviewed Items source and output target"));
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            var rows = ReadRows(source.Bytes);
            var mintNatureRecovery = baseItemSource is null
                ? ZaItemMintNatureRecovery.None
                : ZaItemMintNatureRecoveryDetector.Analyze(
                    source.Bytes,
                    baseItemSource.Bytes);
            var technicalMachineRecovery = itemSemanticState.Recovery;
            RestoreMintNatureSentinels(rows, mintNatureRecovery.ItemIds);
            ApplyTechnicalMachineLegacyRecovery(rows, technicalMachineRecovery, diagnostics);
            ZaWorkflowFile? migratedShopLineupSource = null;
            if (technicalMachineRecovery.HasChanges
                && PlanContainsVirtualWrite(
                    paths,
                    currentPlan,
                    ZaDataPaths.ShopItemLineupArray,
                    outputMode))
            {
                migratedShopLineupSource = fileSource.Read(project, ZaDataPaths.ShopItemLineupArray);
                if (!CapturedSourcesMatchPlan(
                        paths,
                        currentPlan,
                        ZaDataPaths.ShopItemLineupArray,
                        outputMode,
                        [CreatePlanFingerprintSource(
                            ZaDataPaths.ShopItemLineupArray,
                            migratedShopLineupSource)]))
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Shop lineup source or destination changed after review. Review the change plan again before applying.",
                        ZaEditSessionSupport.ItemsDomain,
                        file: $"romfs/{ZaDataPaths.ShopItemLineupArray}",
                        expected: "The exact reviewed shop lineup and output target"));
                    return ZaEditSessionSupport.CreateApplyResult(
                        applyId,
                        appliedAt,
                        currentPlan,
                        writtenFiles,
                        diagnostics);
                }
            }

            var migratedShopReferenceCount = 0;
            var migratedShopLineupBytes = migratedShopLineupSource is null
                ? null
                : CreateLegacyTechnicalMachineShopMigration(
                    rows,
                    technicalMachineRecovery,
                    migratedShopLineupSource,
                    diagnostics,
                    out migratedShopReferenceCount);
            var expectedTechnicalMachines = rows
                .Where(IsTechnicalMachine)
                .ToDictionary(
                    row => row.Id,
                    row => new PhysicalTechnicalMachineIdentity(
                        row.MachineWaza,
                        row.IconName));
            foreach (var edit in effectiveSession.PendingEdits)
            {
                ApplyEdit(rows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaEvolutionItemConversionState? conversionState = null;
            if (HasEnabledEvolutionItemEdit(effectiveSession))
            {
                var writesEvolutionItemConversions = PlanContainsVirtualWrite(
                    paths,
                    currentPlan,
                    ZaDataPaths.EvolutionItemConversionArray,
                    outputMode);
                if (writesEvolutionItemConversions
                    && !CapturedSourcesMatchPlan(
                        paths,
                        currentPlan,
                        ZaDataPaths.EvolutionItemConversionArray,
                        outputMode,
                        ReadEvolutionPlanSemanticSources(project)))
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Evolution-item conversion source or destination changed after review. Review the change plan again before applying.",
                        ZaEditSessionSupport.ItemsDomain,
                        file: $"romfs/{ZaDataPaths.EvolutionItemConversionArray}",
                        expected: "The exact reviewed conversion inputs and output target"));
                    return ZaEditSessionSupport.CreateApplyResult(
                        applyId,
                        appliedAt,
                        currentPlan,
                        writtenFiles,
                        diagnostics);
                }

                conversionState = ZaEvolutionItemConversionState.Load(project, fileSource);
                PrepareEvolutionItemConversions(effectiveSession, conversionState);
                if (conversionState.Modified != writesEvolutionItemConversions)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Evolution-item conversion requirements changed after review. Review the change plan again before applying.",
                        ZaEditSessionSupport.ItemsDomain,
                        file: $"romfs/{ZaDataPaths.EvolutionItemConversionArray}",
                        expected: "The reviewed conversion write set"));
                    return ZaEditSessionSupport.CreateApplyResult(
                        applyId,
                        appliedAt,
                        currentPlan,
                        writtenFiles,
                        diagnostics);
                }
            }

            ValidatePhysicalTechnicalMachineRows(
                rows,
                expectedTechnicalMachines,
                diagnostics,
                "staged item rows");
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var itemBytes = WriteRows(rows);
            ValidatePhysicalTechnicalMachineRows(
                ReadRows(itemBytes),
                expectedTechnicalMachines,
                diagnostics,
                "serialized item output");
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var conversionBytes = conversionState?.Modified == true
                ? conversionState.Write()
                : null;

            var outputWrites = new List<ZaWorkflowFileWrite>
            {
                new(ZaDataPaths.ItemDataArray, itemBytes),
            };
            if (conversionBytes is not null)
            {
                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.EvolutionItemConversionArray,
                    conversionBytes));
            }

            if (migratedShopLineupBytes is not null)
            {
                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.ShopItemLineupArray,
                    migratedShopLineupBytes));
            }

            byte[]? reviewedStandaloneDescriptorBytes = null;
            if (outputMode == ZaOutputMode.Standalone)
            {
                reviewedStandaloneDescriptorBytes =
                    ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                        paths,
                        outputWrites.Select(write => write.VirtualPath));
                if (!CapturedSourcesMatchPlan(
                        paths,
                        currentPlan,
                        ZaWorkflowFileSource.DescriptorVirtualPath,
                        ZaOutputMode.Standalone,
                        [
                            new PlanFingerprintSource(
                                ZaWorkflowFileSource.DescriptorVirtualPath,
                                reviewedStandaloneDescriptorBytes,
                                "DescriptorPreview",
                                ZaWorkflowFileSource.DescriptorVirtualPath),
                        ]))
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "The standalone Trinity descriptor changed after review. Review the change plan again before applying.",
                        ZaEditSessionSupport.ItemsDomain,
                        expected: "The exact reviewed standalone Trinity descriptor"));
                    return ZaEditSessionSupport.CreateApplyResult(
                        applyId,
                        appliedAt,
                        currentPlan,
                        writtenFiles,
                        diagnostics);
                }
            }

            ZaWorkflowFileSource.WriteBatch(
                paths,
                outputWrites,
                outputMode,
                reviewedStandaloneDescriptorBytes);
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.ItemDataArray, outputMode));
            if (conversionBytes is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.EvolutionItemConversionArray,
                    outputMode));
            }

            if (migratedShopLineupBytes is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    ZaDataPaths.ShopItemLineupArray,
                    outputMode));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Items", outputMode),
                ZaEditSessionSupport.ItemsDomain));
            if (mintNatureRecovery.Status == ZaItemMintNatureRecoveryStatus.Detected)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    $"Repaired {mintNatureRecovery.ItemIds.Count} legacy no-mint item sentinels while preserving other item edits.",
                    ZaEditSessionSupport.ItemsDomain,
                    field: ZaItemsWorkflowService.MintNatureField));
            }

            if (technicalMachineRecovery.HasChanges)
            {
                var iconRepairMessage = technicalMachineRecovery.IconRepairs.Count == 0
                    ? string.Empty
                    : $" Synchronized {technicalMachineRecovery.IconRepairs.Count} unchanged stale disc icon(s) with the preserved move type.";
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    "Repaired legacy KM Editor TM numbering while preserving physical item IDs, moves, prices, custom icons, and unrelated item fields."
                    + iconRepairMessage,
                    ZaEditSessionSupport.ItemsDomain,
                    field: ZaItemsWorkflowService.TechnicalMachineNumberField));
            }

            if (migratedShopReferenceCount > 0)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    $"Migrated {migratedShopReferenceCount} legacy shop reference(s) from synthetic item 2161 to the physical TM101 owner.",
                    ZaEditSessionSupport.ItemsDomain,
                    file: $"romfs/{ZaDataPaths.ShopItemLineupArray}"));
            }
        }
        catch (Exception exception)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items output could not be written: {exception.Message}",
                ZaEditSessionSupport.ItemsDomain,
                file: $"romfs/{ZaDataPaths.ItemDataArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static void RestoreMintNatureSentinels(
        IEnumerable<ItemRow> rows,
        IReadOnlySet<int> recoveredItemIds)
    {
        foreach (var row in rows)
        {
            if (recoveredItemIds.Contains(row.Id) && row.MintNature == 0)
            {
                row.MintNature = -1;
            }
        }
    }

    private ChangePlan AddItemSourceFingerprint(
        ProjectPaths paths,
        ChangePlan plan,
        ZaOutputMode outputMode,
        IReadOnlyList<PendingEdit> pendingEdits)
    {
        if (!plan.CanApply || plan.Writes.Count == 0)
        {
            return plan;
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, ZaDataPaths.ItemDataArray);
            var baseSource = source.SourceLayer == ProjectFileLayer.Layered
                ? fileSource.ReadBase(project, ZaDataPaths.ItemDataArray)
                : null;
            var writes = plan.Writes.ToArray();
            var itemWriteIndex = FindPlannedWriteIndex(
                paths,
                writes,
                ZaDataPaths.ItemDataArray,
                outputMode);
            if (itemWriteIndex < 0)
            {
                throw new InvalidDataException("The Items change plan is missing its item-data target.");
            }

            var itemSemanticState = CaptureItemPlanSemanticState(
                project,
                source,
                baseSource);
            writes[itemWriteIndex] = writes[itemWriteIndex] with
            {
                SourceFingerprint = CreatePlanSourceFingerprint(
                    paths,
                    ZaDataPaths.ItemDataArray,
                    outputMode,
                    itemSemanticState.Sources,
                    CreatePlanChangeSetFingerprint(pendingEdits)),
            };
            var recovery = itemSemanticState.Recovery;
            if (!recovery.HasChanges)
            {
                return plan with { Writes = writes };
            }

            var iconRepairReason = recovery.IconRepairs.Count == 0
                ? string.Empty
                : $" Synchronize {recovery.IconRepairs.Count} unchanged stale TM disc icon(s) with the preserved move type.";
            writes[itemWriteIndex] = writes[itemWriteIndex] with
            {
                Reason =
                    $"{writes[itemWriteIndex].Reason} Repair legacy KM Editor TM numbering without changing moves, prices, custom icons, or unrelated item fields."
                    + iconRepairReason,
            };
            var diagnostics = plan.Diagnostics
                .Append(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    recovery.IconRepairs.Count == 0
                        ? "The reviewed Items output includes the detected legacy TM-numbering repair."
                        : $"The reviewed Items output includes the detected legacy TM-numbering repair and {recovery.IconRepairs.Count} stale disc-icon synchronization(s).",
                    ZaEditSessionSupport.ItemsDomain,
                    field: ZaItemsWorkflowService.TechnicalMachineNumberField))
                .ToArray();
            return plan with
            {
                Writes = writes,
                Diagnostics = diagnostics,
            };
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or ArgumentException
                or UnauthorizedAccessException)
        {
            var diagnostics = plan.Diagnostics
                .Append(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Items source fingerprint could not be created: {exception.Message}",
                    ZaEditSessionSupport.ItemsDomain,
                    file: $"romfs/{ZaDataPaths.ItemDataArray}",
                    expected: "Stable readable active and clean base item tables"))
                .ToArray();
            return plan with
            {
                Writes = Array.Empty<PlannedFileWrite>(),
                Diagnostics = diagnostics,
            };
        }
    }

    private ChangePlan AddLegacyTechnicalMachineShopMigrationPlan(
        ProjectPaths paths,
        ChangePlan plan,
        ZaOutputMode outputMode)
    {
        if (!plan.CanApply)
        {
            return plan;
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var activeSource = fileSource.Read(project, ZaDataPaths.ItemDataArray);
            if (activeSource.SourceLayer != ProjectFileLayer.Layered)
            {
                return plan;
            }

            var baseSource = fileSource.ReadBase(project, ZaDataPaths.ItemDataArray);
            var recovery = ZaTechnicalMachineLegacyRecoveryDetector.Analyze(
                activeSource.Bytes,
                baseSource.Bytes);
            if (!recovery.HasChanges)
            {
                return plan;
            }

            if (!fileSource.Exists(project, ZaDataPaths.ShopItemLineupArray))
            {
                return plan;
            }

            var lineupSource = fileSource.Read(project, ZaDataPaths.ShopItemLineupArray);
            var referenceCount = ZaShopsWorkflowService.ReadLineupRows(lineupSource.Bytes)
                .SelectMany(row => row.Inventory)
                .Count(row => row.ItemId == ZaTechnicalMachineCatalog.LegacySyntheticTechnicalMachineItemId);
            if (referenceCount == 0)
            {
                return plan;
            }

            if (recovery.BaseSlot101OwnerItemId is not { } ownerItemId
                || !ReadRows(activeSource.Bytes).Any(row =>
                    row.Id == ownerItemId
                    && IsTechnicalMachine(row)))
            {
                return AddPlanError(
                    plan,
                    "Legacy shop references to item 2161 cannot be migrated because the clean physical TM101 owner is not uniquely available.",
                    $"romfs/{ZaDataPaths.ShopItemLineupArray}",
                    "Unique physical TM101 owner from the clean base item table");
            }

            var writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                paths,
                ZaDataPaths.ShopItemLineupArray,
                [ZaWorkflowFileSource.CreateReference(lineupSource)],
                outputMode);
            var lineupWrite = new PlannedFileWrite(
                writeInfo.TargetRelativePath,
                writeInfo.Sources,
                writeInfo.ReplacesExistingOutput,
                $"Replace {referenceCount} legacy shop reference(s) to synthetic item 2161 with physical item {ownerItemId}.",
                CreatePlanSourceFingerprint(
                    paths,
                    ZaDataPaths.ShopItemLineupArray,
                    outputMode,
                    [CreatePlanFingerprintSource(
                        ZaDataPaths.ShopItemLineupArray,
                        lineupSource)]));
            var diagnostics = plan.Diagnostics
                .Append(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    $"The plan will migrate {referenceCount} legacy shop reference(s) from item 2161 to physical TM101 item {ownerItemId}.",
                    ZaEditSessionSupport.ItemsDomain,
                    file: $"romfs/{ZaDataPaths.ShopItemLineupArray}"))
                .ToArray();
            return plan with
            {
                Writes = [.. plan.Writes, lineupWrite],
                Diagnostics = diagnostics,
            };
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or ArgumentException
                or UnauthorizedAccessException)
        {
            return AddPlanError(
                plan,
                $"Legacy TM shop-reference inspection failed: {exception.Message}",
                $"romfs/{ZaDataPaths.ShopItemLineupArray}",
                "Readable shop lineup and clean base item table");
        }
    }

    private static ChangePlan AddPlanError(
        ChangePlan plan,
        string message,
        string file,
        string expected)
    {
        var diagnostics = plan.Diagnostics
            .Append(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                message,
                ZaEditSessionSupport.ItemsDomain,
                file: file,
                expected: expected))
            .ToArray();
        return plan with
        {
            Writes = Array.Empty<PlannedFileWrite>(),
            Diagnostics = diagnostics,
        };
    }

    private ChangePlan AddStandaloneDescriptorFingerprint(
        ProjectPaths paths,
        ChangePlan plan,
        ZaOutputMode outputMode)
    {
        if (!plan.CanApply || outputMode != ZaOutputMode.Standalone)
        {
            return plan;
        }

        try
        {
            var plannedVirtualPaths = GetPlannedDataVirtualPaths(paths, plan, outputMode);
            var descriptorWriteIndex = FindPlannedWriteIndex(
                paths,
                plan.Writes,
                ZaWorkflowFileSource.DescriptorVirtualPath,
                ZaOutputMode.Standalone);
            if (descriptorWriteIndex < 0)
            {
                throw new InvalidDataException(
                    "The standalone Items change plan is missing its Trinity descriptor target.");
            }

            var descriptorBytes = ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                paths,
                plannedVirtualPaths);
            var writes = plan.Writes.ToArray();
            writes[descriptorWriteIndex] = writes[descriptorWriteIndex] with
            {
                SourceFingerprint = CreatePlanSourceFingerprint(
                    paths,
                    ZaWorkflowFileSource.DescriptorVirtualPath,
                    ZaOutputMode.Standalone,
                    [
                        new PlanFingerprintSource(
                            ZaWorkflowFileSource.DescriptorVirtualPath,
                            descriptorBytes,
                            "DescriptorPreview",
                            ZaWorkflowFileSource.DescriptorVirtualPath),
                    ]),
            };
            return plan with { Writes = writes };
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or UnauthorizedAccessException)
        {
            return AddPlanError(
                plan,
                $"Standalone Trinity descriptor preview could not be fingerprinted: {exception.Message}",
                $"romfs/{ZaWorkflowFileSource.DescriptorVirtualPath}",
                "Stable readable descriptor and output target");
        }
    }

    private ItemPlanSemanticState CaptureItemPlanSemanticState(
        OpenedProject project,
        ZaWorkflowFile? activeSource = null,
        ZaWorkflowFile? baseSource = null)
    {
        var active = activeSource ?? fileSource.Read(project, ZaDataPaths.ItemDataArray);
        var sources = new List<PlanFingerprintSource>
        {
            CreatePlanFingerprintSource($"{ZaDataPaths.ItemDataArray}#active", active),
        };
        var recovery = ZaTechnicalMachineLegacyRecovery.None;
        if (active.SourceLayer == ProjectFileLayer.Layered)
        {
            var cleanBase = baseSource
                ?? fileSource.ReadBase(project, ZaDataPaths.ItemDataArray);
            sources.Add(CreatePlanFingerprintSource(
                $"{ZaDataPaths.ItemDataArray}#base",
                cleanBase));
            recovery = ZaTechnicalMachineLegacyRecoveryDetector.Analyze(
                active.Bytes,
                cleanBase.Bytes);
            if (!recovery.IsBlocked
                && recovery.HasChanges
                && recovery.BaseSlot101OwnerItemId is not null)
            {
                var activeMoves = CaptureOptionalPlanFingerprintSource(
                    project,
                    ZaDataPaths.MoveDataArray,
                    $"{ZaDataPaths.MoveDataArray}#active");
                var baseMoves = CaptureOptionalPlanFingerprintSource(
                    project,
                    ZaDataPaths.MoveDataArray,
                    $"{ZaDataPaths.MoveDataArray}#base",
                    readBase: true);
                sources.Add(activeMoves.Fingerprint);
                sources.Add(baseMoves.Fingerprint);
                recovery = activeMoves.Source is not null && baseMoves.Source is not null
                    ? ZaTechnicalMachineLegacyRecoveryDetector.AnalyzeWithMoveData(
                        active.Bytes,
                        cleanBase.Bytes,
                        activeMoves.Source.Bytes,
                        baseMoves.Source.Bytes)
                    : recovery with
                    {
                        IconRepairWarning =
                            "Legacy TM numbering can still be repaired, but KM will leave the affected disc icon unchanged "
                            + "because the active and clean move tables are not both readable.",
                    };
            }
        }

        return new ItemPlanSemanticState(sources, recovery);
    }

    private IReadOnlyList<PlanFingerprintSource> ReadEvolutionPlanSemanticSources(
        OpenedProject project)
    {
        return
        [
            CreatePlanFingerprintSource(
                ZaDataPaths.EvolutionItemConversionArray,
                fileSource.Read(project, ZaDataPaths.EvolutionItemConversionArray)),
            CreatePlanFingerprintSource(
                ZaDataPaths.ItemDataArray,
                fileSource.Read(project, ZaDataPaths.ItemDataArray)),
            ReadOptionalPlanFingerprintSource(project, ZaDataPaths.PersonalArray),
        ];
    }

    private PlanFingerprintSource ReadOptionalPlanFingerprintSource(
        OpenedProject project,
        string virtualPath,
        string? fingerprintVirtualPath = null,
        bool readBase = false)
    {
        return CaptureOptionalPlanFingerprintSource(
            project,
            virtualPath,
            fingerprintVirtualPath,
            readBase).Fingerprint;
    }

    private CapturedOptionalPlanFingerprintSource CaptureOptionalPlanFingerprintSource(
        OpenedProject project,
        string virtualPath,
        string? fingerprintVirtualPath = null,
        bool readBase = false)
    {
        var fingerprintPath = fingerprintVirtualPath ?? virtualPath;
        try
        {
            var source = readBase
                ? fileSource.ReadBase(project, virtualPath)
                : fileSource.Read(project, virtualPath);
            return new CapturedOptionalPlanFingerprintSource(
                CreatePlanFingerprintSource(fingerprintPath, source),
                source);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or ArgumentException
                or UnauthorizedAccessException)
        {
            return new CapturedOptionalPlanFingerprintSource(
                new PlanFingerprintSource(
                    fingerprintPath,
                    Array.Empty<byte>(),
                    $"Unavailable:{exception.GetType().Name}",
                    virtualPath),
                null);
        }
    }

    private static PlanFingerprintSource CreatePlanFingerprintSource(
        string virtualPath,
        ZaWorkflowFile source)
    {
        return new PlanFingerprintSource(
            virtualPath,
            source.Bytes,
            $"{source.SourceLayer}:{source.Origin}",
            source.RelativePath);
    }

    private static IReadOnlyList<string> GetPlannedDataVirtualPaths(
        ProjectPaths paths,
        ChangePlan plan,
        ZaOutputMode outputMode)
    {
        return new[]
            {
                ZaDataPaths.ItemDataArray,
                ZaDataPaths.EvolutionItemConversionArray,
                ZaDataPaths.ShopItemLineupArray,
            }
            .Where(virtualPath => PlanContainsVirtualWrite(
                paths,
                plan,
                virtualPath,
                outputMode))
            .ToArray();
    }

    private static bool PlanContainsVirtualWrite(
        ProjectPaths paths,
        ChangePlan plan,
        string virtualPath,
        ZaOutputMode outputMode)
    {
        return FindPlannedWriteIndex(
            paths,
            plan.Writes,
            virtualPath,
            outputMode) >= 0;
    }

    private static int FindPlannedWriteIndex(
        ProjectPaths paths,
        IReadOnlyList<PlannedFileWrite> writes,
        string virtualPath,
        ZaOutputMode outputMode)
    {
        var targetRelativePath = ZaWorkflowFileSource.CreatePlannedWrite(
            paths,
            virtualPath,
            Array.Empty<ProjectFileReference>(),
            outputMode).TargetRelativePath;
        for (var index = 0; index < writes.Count; index++)
        {
            if (string.Equals(
                    writes[index].TargetRelativePath,
                    targetRelativePath,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string CreatePlanSourceFingerprint(
        ProjectPaths paths,
        string virtualPath,
        ZaOutputMode outputMode,
        IReadOnlyList<PlanFingerprintSource> sources,
        string? changeSetFingerprint = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintValue(hash, "KM.ZA.Items.Source.v3");
        AppendFingerprintValue(hash, virtualPath.Replace('\\', '/'));
        AppendFingerprintValue(hash, outputMode.ToString());
        AppendFingerprintValue(
            hash,
            NormalizeFingerprintPath(
                ZaWorkflowFileSource.ResolveOutputPath(paths, virtualPath, outputMode)));
        AppendFingerprintValue(hash, changeSetFingerprint);
        foreach (var source in sources
            .OrderBy(source => source.VirtualPath, StringComparer.Ordinal)
            .ThenBy(source => source.SourceKind, StringComparer.Ordinal)
            .ThenBy(source => source.SourceIdentity, StringComparer.Ordinal))
        {
            AppendFingerprintValue(hash, source.VirtualPath.Replace('\\', '/'));
            AppendFingerprintValue(hash, source.SourceKind);
            AppendFingerprintValue(hash, source.SourceIdentity.Replace('\\', '/'));
            AppendFingerprintBytes(hash, source.Bytes);
        }

        var targetPath = ZaWorkflowFileSource.ResolveOutputPath(paths, virtualPath, outputMode);
        if (File.Exists(targetPath))
        {
            AppendFingerprintValue(hash, "TargetFile");
            AppendFingerprintBytes(hash, File.ReadAllBytes(targetPath));
        }
        else if (Directory.Exists(targetPath))
        {
            AppendFingerprintValue(hash, "TargetDirectory");
        }
        else
        {
            AppendFingerprintValue(hash, "TargetMissing");
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool CapturedSourcesMatchPlan(
        ProjectPaths paths,
        ChangePlan plan,
        string virtualPath,
        ZaOutputMode outputMode,
        IReadOnlyList<PlanFingerprintSource> sources,
        string? changeSetFingerprint = null)
    {
        var writeIndex = FindPlannedWriteIndex(
            paths,
            plan.Writes,
            virtualPath,
            outputMode);
        return writeIndex >= 0
            && string.Equals(
                plan.Writes[writeIndex].SourceFingerprint,
                CreatePlanSourceFingerprint(
                    paths,
                    virtualPath,
                    outputMode,
                    sources,
                    changeSetFingerprint),
                StringComparison.Ordinal);
    }

    private static string CreatePlanChangeSetFingerprint(
        IReadOnlyList<PendingEdit> edits)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintValue(hash, "KM.ZA.Items.ChangeSet.v2");
        AppendFingerprintValue(hash, edits.Count.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            AppendFingerprintValue(hash, index.ToString(CultureInfo.InvariantCulture));
            AppendFingerprintValue(hash, edit.Domain);
            AppendFingerprintValue(hash, edit.RecordId);
            AppendFingerprintValue(hash, edit.Field);
            AppendFingerprintValue(hash, edit.NewValue);
            var sources = edit.Sources
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .ToArray();
            AppendFingerprintValue(
                hash,
                sources.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var source in sources)
            {
                AppendFingerprintValue(
                    hash,
                    ((int)source.Layer).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintValue(hash, source.RelativePath);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed record PlanFingerprintSource(
        string VirtualPath,
        byte[] Bytes,
        string SourceKind,
        string SourceIdentity);

    private sealed record ItemPlanSemanticState(
        IReadOnlyList<PlanFingerprintSource> Sources,
        ZaTechnicalMachineLegacyRecovery Recovery);

    private sealed record CapturedOptionalPlanFingerprintSource(
        PlanFingerprintSource Fingerprint,
        ZaWorkflowFile? Source);

    private readonly record struct PhysicalTechnicalMachineIdentity(
        ushort MoveId,
        string IconName);

    private static string NormalizeFingerprintPath(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }

    private static void AppendFingerprintValue(
        IncrementalHash hash,
        string? value)
    {
        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, -1);
            hash.AppendData(lengthBytes);
            return;
        }

        var valueBytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, valueBytes.Length);
        hash.AppendData(lengthBytes);
        hash.AppendData(valueBytes);
    }

    private static void AppendFingerprintBytes(
        IncrementalHash hash,
        byte[] value)
    {
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, value.LongLength);
        hash.AppendData(lengthBytes);
        hash.AppendData(value);
    }

    private byte[]? CreateLegacyTechnicalMachineShopMigration(
        IReadOnlyList<ItemRow> itemRows,
        ZaTechnicalMachineLegacyRecovery recovery,
        ZaWorkflowFile reviewedLineupSource,
        ICollection<ValidationDiagnostic> diagnostics,
        out int migratedReferenceCount)
    {
        migratedReferenceCount = 0;
        if (!recovery.HasChanges)
        {
            return null;
        }

        var lineupRows = ZaShopsWorkflowService.ReadLineupRows(
            reviewedLineupSource.Bytes).ToArray();
        var legacyReferences = lineupRows
            .SelectMany(row => row.Inventory)
            .Where(row => row.ItemId == ZaTechnicalMachineCatalog.LegacySyntheticTechnicalMachineItemId)
            .ToArray();
        if (legacyReferences.Length == 0)
        {
            return null;
        }

        if (recovery.BaseSlot101OwnerItemId is not { } ownerItemId
            || !itemRows.Any(row =>
                row.Id == ownerItemId
                && IsTechnicalMachine(row)))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Legacy shop references to item 2161 cannot be migrated because the physical TM101 owner is not uniquely available.",
                ZaEditSessionSupport.ItemsDomain,
                file: $"romfs/{ZaDataPaths.ShopItemLineupArray}",
                expected: "Unique physical TM101 owner from the clean base item table"));
            return null;
        }

        foreach (var reference in legacyReferences)
        {
            reference.ItemId = checked((uint)ownerItemId);
        }

        var bytes = ZaShopsWorkflowService.WriteLineupRows(lineupRows);
        if (ZaShopsWorkflowService.ReadLineupRows(bytes)
            .SelectMany(row => row.Inventory)
            .Any(row => row.ItemId == ZaTechnicalMachineCatalog.LegacySyntheticTechnicalMachineItemId))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Legacy shop-reference migration did not remove every synthetic item 2161 reference.",
                ZaEditSessionSupport.ItemsDomain,
                file: $"romfs/{ZaDataPaths.ShopItemLineupArray}",
                expected: $"Physical item {ownerItemId} for every former item 2161 reference"));
            return null;
        }

        migratedReferenceCount = legacyReferences.Length;
        return bytes;
    }

    private static void ApplyTechnicalMachineLegacyRecovery(
        List<ItemRow> rows,
        ZaTechnicalMachineLegacyRecovery recovery,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (recovery.IsBlocked)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                recovery.BlockingReason!,
                ZaEditSessionSupport.ItemsDomain,
                field: ZaItemsWorkflowService.TechnicalMachineNumberField,
                expected: "Unmodified KM-generated legacy row or clean physical item data"));
            return;
        }

        if (recovery.RemoveSyntheticRow)
        {
            var removedCount = rows.RemoveAll(row =>
                row.Id == ZaTechnicalMachineCatalog.LegacySyntheticTechnicalMachineItemId);
            if (removedCount != 1)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Legacy TM recovery could not identify exactly one synthetic item 2161 row at apply time.",
                    ZaEditSessionSupport.ItemsDomain,
                    field: "itemId",
                    expected: "Exactly one reviewed KM-generated synthetic row"));
                return;
            }
        }

        if (recovery.RepairItemId is { } repairItemId
            && recovery.RepairTechnicalMachineNumber is { } repairNumber)
        {
            var repairRow = rows.SingleOrDefault(row => row.Id == repairItemId);
            if (repairRow is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Legacy TM recovery could not find the reviewed out-of-range physical TM row.",
                    ZaEditSessionSupport.ItemsDomain,
                    field: "itemId",
                    expected: $"Physical item {repairItemId}"));
                return;
            }

            repairRow.SortNum = repairNumber;
            repairRow.MachineIndex = repairNumber - 1;
        }

        foreach (var iconRepair in recovery.IconRepairs)
        {
            var iconRow = rows.SingleOrDefault(row => row.Id == iconRepair.ItemId);
            if (iconRow is null
                || !string.Equals(
                    iconRow.IconName,
                    iconRepair.PreviousIconName,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Legacy TM recovery could not match the reviewed stale disc icon at apply time.",
                    ZaEditSessionSupport.ItemsDomain,
                    field: ZaItemsWorkflowService.MachineMoveIdField,
                    expected: $"Unchanged reviewed icon for physical item {iconRepair.ItemId}"));
                return;
            }

            iconRow.IconName = iconRepair.RepairedIconName;
        }
    }

    private void ValidateEvolutionItemConversions(
        OpenedProject project,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!HasEnabledEvolutionItemEdit(session))
        {
            return;
        }

        try
        {
            var conversionState = ZaEvolutionItemConversionState.Load(project, fileSource);
            PrepareEvolutionItemConversions(session, conversionState);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Evolution item conversion allocation is not valid: {exception.Message}",
                ZaEditSessionSupport.ItemsDomain,
                file: $"romfs/{ZaDataPaths.EvolutionItemConversionArray}",
                expected: "Approved conversion capacity for every enabled item"));
        }
    }

    private static void ValidateEvolutionItemUseCompatibility(
        ZaItemsWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var effectiveWorkflow = OverlayPendingEdits(workflow, session.PendingEdits);
        foreach (var itemId in GetEnabledEvolutionItemIds(session))
        {
            var originalItem = workflow.Items.FirstOrDefault(candidate => candidate.ItemId == itemId);
            var effectiveItem = effectiveWorkflow.Items.FirstOrDefault(candidate => candidate.ItemId == itemId);
            if (originalItem is null
                || effectiveItem is null
                || !HasConflictingPokemonUseEffect(originalItem)
                    && !HasConflictingPokemonUseEffect(effectiveItem))
            {
                continue;
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{effectiveItem.Name} already has a direct Pokemon-use effect and cannot safely be converted without replacing that behavior.",
                ZaEditSessionSupport.ItemsDomain,
                field: ZaItemsWorkflowService.EvolutionItemField,
                expected: "Held or inert item with no healing, status, mint, form-change, experience, revival, or EV use effect"));
        }
    }

    private static bool HasConflictingPokemonUseEffect(ZaItemRecord item)
    {
        string[] fields =
        [
            ZaItemsWorkflowService.CureSleepField,
            ZaItemsWorkflowService.CurePoisonField,
            ZaItemsWorkflowService.CureBurnField,
            ZaItemsWorkflowService.CureFreezeField,
            ZaItemsWorkflowService.CureParalyzeField,
            ZaItemsWorkflowService.CureConfuseField,
            ZaItemsWorkflowService.CureInfatuationField,
            ZaItemsWorkflowService.HealPowerField,
            ZaItemsWorkflowService.HealPercentageField,
            ZaItemsWorkflowService.RevivalCountField,
            ZaItemsWorkflowService.RevivePercentageField,
            ZaItemsWorkflowService.ExpPointGainField,
            ZaItemsWorkflowService.MachineMoveIdField,
            ZaItemsWorkflowService.FormChangeItemField,
            ZaItemsWorkflowService.EvHpField,
            ZaItemsWorkflowService.EvAttackField,
            ZaItemsWorkflowService.EvDefenseField,
            ZaItemsWorkflowService.EvSpeedField,
            ZaItemsWorkflowService.EvSpecialAttackField,
            ZaItemsWorkflowService.EvSpecialDefenseField,
        ];
        return HasMintNature(item.FieldValues)
            || fields.Any(field => item.FieldValues.GetValueOrDefault(field) is { } value && value != 0);
    }

    private static void PrepareEvolutionItemConversions(
        EditSession session,
        ZaEvolutionItemConversionState conversionState)
    {
        foreach (var itemId in GetEnabledEvolutionItemIds(session))
        {
            conversionState.Encode(itemId);
        }
    }

    private static bool HasEnabledEvolutionItemEdit(EditSession session)
    {
        return GetEnabledEvolutionItemIds(session).Count > 0;
    }

    private static IReadOnlyList<int> GetEnabledEvolutionItemIds(EditSession session)
    {
        var finalValues = new Dictionary<int, int>();
        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, ZaEditSessionSupport.ItemsDomain, StringComparison.Ordinal)
                || !string.Equals(edit.Field, ZaItemsWorkflowService.EvolutionItemField, StringComparison.Ordinal)
                || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
                || !int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            finalValues[itemId] = value;
        }

        return finalValues
            .Where(entry => entry.Value != 0)
            .Select(entry => entry.Key)
            .Order()
            .ToArray();
    }

    private static PendingEdit? CreatePendingEdit(
        ZaItemsWorkflow workflow,
        ZaItemRecord item,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var parsedValue = TryParseEditableValue(workflow, normalizedField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        var editableField = GetEditableField(workflow, normalizedField)!;
        if (!CanEditTechnicalMachineField(item, editableField, diagnostics))
        {
            return null;
        }

        if (!CanStageTechnicalMachineShapeEdit(
                item,
                normalizedField,
                parsedValue.Value,
                diagnostics))
        {
            return null;
        }

        if (!CanEditDerivedField(editableField, diagnostics))
        {
            return null;
        }

        var stagesLegacyTechnicalMachineRepair =
            string.Equals(
                normalizedField,
                ZaItemsWorkflowService.TechnicalMachineNumberField,
                StringComparison.Ordinal)
            && item.FieldValues.GetValueOrDefault(normalizedField) == parsedValue.Value
            && HasLegacyTechnicalMachineRecovery(workflow);
        var summary = stagesLegacyTechnicalMachineRepair
            ? "Apply the detected legacy KM Editor TM-numbering recovery."
            : $"Set {item.Name} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.";
        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.ItemsDomain,
            summary,
            new ProjectFileReference(item.Provenance.SourceLayer, item.Provenance.SourceFile),
            item.ItemId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool TryStagePendingEdit(
        ZaItemsWorkflow workflow,
        EditSession session,
        PendingEdit pendingEdit,
        ICollection<ValidationDiagnostic> diagnostics,
        out EditSession updatedSession,
        out ZaItemsWorkflow updatedWorkflow)
    {
        updatedSession = session;
        updatedWorkflow = workflow;

        if (!string.Equals(
                pendingEdit.Field,
                ZaItemsWorkflowService.TechnicalMachineNumberField,
                StringComparison.Ordinal))
        {
            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(session, pendingEdit);
            updatedWorkflow = OverlayPendingEdit(workflow, pendingEdit);
            return true;
        }

        if (!int.TryParse(
                pendingEdit.RecordId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var itemId)
            || !int.TryParse(
                pendingEdit.NewValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var newNumber)
            || workflow.Items.FirstOrDefault(candidate => candidate.ItemId == itemId) is not { } item
            || item.Metadata.MachineSlot is not { } previousNumber
            || previousNumber <= 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The selected TM does not have a recoverable current number.",
                ZaEditSessionSupport.ItemsDomain,
                field: ZaItemsWorkflowService.TechnicalMachineNumberField,
                expected: "TM with a positive current number"));
            return false;
        }

        if (newNumber == previousNumber)
        {
            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(session, pendingEdit);
            updatedWorkflow = OverlayPendingEdit(workflow, pendingEdit);
            return true;
        }

        var targetOwners = workflow.Items
            .Where(candidate =>
                candidate.ItemId != itemId
                && ZaItemsWorkflowService.IsTechnicalMachineRecord(candidate)
                && candidate.Metadata.MachineSlot == newNumber)
            .ToArray();
        if (targetOwners.Length == 0)
        {
            var previousNumberOwners = workflow.Items.Count(candidate =>
                ZaItemsWorkflowService.IsTechnicalMachineRecord(candidate)
                && candidate.Metadata.MachineSlot == previousNumber);
            var technicalMachineCount = workflow.Items.Count(
                ZaItemsWorkflowService.IsTechnicalMachineRecord);
            if (previousNumberOwners <= 1 && previousNumber <= technicalMachineCount)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"TM number {newNumber} is not currently assigned. Choose another occupied number so the two assignments can be swapped.",
                    ZaEditSessionSupport.ItemsDomain,
                    field: ZaItemsWorkflowService.TechnicalMachineNumberField,
                    expected: "Occupied TM number, or an unoccupied number that repairs a duplicate or out-of-range assignment"));
                return false;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(session, pendingEdit);
            updatedWorkflow = OverlayPendingEdit(workflow, pendingEdit);
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Staged a TM number repair for {item.Name}.",
                ZaEditSessionSupport.ItemsDomain,
                field: ZaItemsWorkflowService.TechnicalMachineNumberField));
            return true;
        }

        if (targetOwners.Length > 1)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"TM number {newNumber} is already assigned to more than one item and cannot be swapped safely.",
                ZaEditSessionSupport.ItemsDomain,
                field: ZaItemsWorkflowService.TechnicalMachineNumberField,
                expected: "Exactly one current TM owner for the selected number"));
            return false;
        }

        var target = targetOwners[0];
        var reciprocalEdit = ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.ItemsDomain,
            $"Swap {target.Name} TM number to {previousNumber}.",
            new ProjectFileReference(target.Provenance.SourceLayer, target.Provenance.SourceFile),
            target.ItemId.ToString(CultureInfo.InvariantCulture),
            ZaItemsWorkflowService.TechnicalMachineNumberField,
            previousNumber.ToString(CultureInfo.InvariantCulture));

        updatedSession = ZaEditSessionSupport.ReplacePendingEdit(session, pendingEdit);
        updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, reciprocalEdit);
        updatedWorkflow = OverlayPendingEdit(workflow, pendingEdit);
        updatedWorkflow = OverlayPendingEdit(updatedWorkflow, reciprocalEdit);
        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Staged a TM number swap between {item.Name} and {target.Name}.",
            ZaEditSessionSupport.ItemsDomain,
            field: ZaItemsWorkflowService.TechnicalMachineNumberField));
        return true;
    }

    private static void ValidatePendingEdit(
        ZaItemsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.ItemsDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Items.",
                ZaEditSessionSupport.ItemsDomain,
                expected: ZaEditSessionSupport.ItemsDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit targets a record that is not loaded.",
                ZaEditSessionSupport.ItemsDomain,
                field: "itemId",
                expected: "Existing Z-A item record"));
            return;
        }

        var item = workflow.Items.FirstOrDefault(candidate => candidate.ItemId == itemId);
        if (item is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit targets a record that is not loaded.",
                ZaEditSessionSupport.ItemsDomain,
                field: "itemId",
                expected: "Existing Z-A item record"));
            return;
        }

        var editableField = GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item field '{edit.Field}' is not supported by Pokemon Legends Z-A Items yet.",
                ZaEditSessionSupport.ItemsDomain,
                field: "field",
                expected: "Supported Z-A item field"));
            return;
        }

        if (!CanEditTechnicalMachineField(item, editableField, diagnostics))
        {
            return;
        }

        if (!CanEditDerivedField(editableField, diagnostics))
        {
            return;
        }

        if (TryParseEditableValue(workflow, edit.Field, edit.NewValue, diagnostics) is { } value)
        {
            _ = CanStageTechnicalMachineShapeEdit(
                item,
                editableField.Field,
                value,
                diagnostics);
        }
    }

    private static void ValidateUniquePendingEditTargets(
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var hasDuplicateTarget = session.PendingEdits
            .GroupBy(edit => (
                edit.Domain,
                RecordId: int.TryParse(
                    edit.RecordId,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var itemId)
                        ? itemId.ToString(CultureInfo.InvariantCulture)
                        : edit.RecordId,
                edit.Field))
            .Any(group => group.Count() > 1);
        if (!hasDuplicateTarget)
        {
            return;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pending Items edits contain more than one value for the same item field.",
            ZaEditSessionSupport.ItemsDomain,
            field: "pendingEdits",
            expected: "At most one pending edit per item and field"));
    }

    private static void ValidateTechnicalMachineNumberAssignments(
        ZaItemsWorkflow workflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits.Count == 0)
        {
            return;
        }

        var effectiveWorkflow = OverlayPendingEdits(workflow, session.PendingEdits);
        var effectiveMachines = effectiveWorkflow.Items
            .Where(ZaItemsWorkflowService.IsTechnicalMachineRecord)
            .ToArray();
        var assignments = effectiveMachines
            .Select(item => new ZaTechnicalMachineNumberAssignment(
                item.ItemId,
                item.Metadata.SortIndex,
                item.Metadata.GroupIndex))
            .ToArray();
        if (effectiveMachines.Any(item =>
                item.FieldValues.GetValueOrDefault(
                    ZaItemsWorkflowService.TechnicalMachineNumberField) is null)
            || !ZaTechnicalMachineCatalog.HasCompleteNumbering(assignments))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items output requires a complete physical TM permutation. Every number from 1 through the loaded TM count must belong to exactly one item.",
                ZaEditSessionSupport.ItemsDomain,
                field: ZaItemsWorkflowService.TechnicalMachineNumberField,
                expected: "Unique one-to-one TM number assignments"));
        }
    }

    private static bool CanEditTechnicalMachineField(
        ZaItemRecord item,
        ZaItemEditableField field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var isTechnicalMachine = ZaItemsWorkflowService.IsTechnicalMachineRecord(item);
        if (field.Field == ZaItemsWorkflowService.SortOrderField && isTechnicalMachine)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "TM sort order cannot be edited independently. Use TM number so both stored values stay paired.",
                ZaEditSessionSupport.ItemsDomain,
                field: field.Field,
                expected: "TM number edit"));
            return false;
        }

        if (field.Field == ZaItemsWorkflowService.TechnicalMachineNumberField)
        {
            if (isTechnicalMachine)
            {
                return true;
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "TM numbers can only be edited on Pokemon Legends Z-A TM item records.",
                ZaEditSessionSupport.ItemsDomain,
                field: field.Field,
                expected: "Item in the Technical Machines pocket with a mapped move"));
            return false;
        }

        if (field.Field != ZaItemsWorkflowService.MachineMoveIdField)
        {
            return true;
        }

        if (item.Metadata.Pouch == 6 || item.Metadata.MachineMoveId is not null)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "TM fields can only be edited on Pokemon Legends Z-A TM item records.",
            ZaEditSessionSupport.ItemsDomain,
            field: field.Field,
            expected: "Item in the Technical Machines pocket"));
        return false;
    }

    private static bool CanEditDerivedField(
        ZaItemEditableField field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field.Field != ZaItemsWorkflowService.CanUseOnPokemonField)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Can use on Pokemon is derived from item effects and cannot be edited directly.",
            ZaEditSessionSupport.ItemsDomain,
            field: field.Field,
            expected: "Edit the underlying use effect or Evolution Item field"));
        return false;
    }

    private static ZaItemEditableField? GetEditableField(
        ZaItemsWorkflow workflow,
        string? field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    private static int? TryParseEditableValue(
        ZaItemsWorkflow workflow,
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = GetEditableField(workflow, field);
        if (editableField is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item field '{field ?? "(missing)"}' is not supported by Pokemon Legends Z-A Items yet.",
                ZaEditSessionSupport.ItemsDomain,
                field: "field",
                expected: "Supported Z-A item field"));
            return null;
        }

        var parsedValue = editableField.ValueKind == "boolean"
            ? TryParseBooleanValue(value, out var booleanValue) ? booleanValue : (int?)null
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue)
                ? integerValue
                : (int?)null;

        if (parsedValue is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be a valid {editableField.ValueKind} value.",
                ZaEditSessionSupport.ItemsDomain,
                field: editableField.Field,
                expected: $"Safe item {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (parsedValue.Value < (editableField.MinimumValue ?? int.MinValue)
            || parsedValue.Value > (editableField.MaximumValue ?? int.MaxValue))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                ZaEditSessionSupport.ItemsDomain,
                field: editableField.Field,
                expected: $"Safe item {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        return parsedValue.Value;
    }

    private static bool TryParseBooleanValue(string? value, out int parsedValue)
    {
        parsedValue = 0;
        if (string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = 1;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.Ordinal)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool CanStageTechnicalMachineShapeEdit(
        ZaItemRecord item,
        string field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (item.FieldValues.GetValueOrDefault(field) == value)
        {
            return true;
        }

        var currentlyTechnicalMachine = ZaItemsWorkflowService.IsTechnicalMachineRecord(item);
        var pocket = field == ZaItemsWorkflowService.PocketField
            ? value
            : item.Metadata.Pouch;
        var itemType = field == ZaItemsWorkflowService.ItemTypeField
            ? value
            : item.Metadata.ItemType;
        var moveId = field == ZaItemsWorkflowService.MachineMoveIdField
            ? value
            : item.Metadata.MachineMoveId ?? 0;
        var wouldBeTechnicalMachine = pocket == 6 && itemType == 5 && moveId > 0;
        if (currentlyTechnicalMachine != wouldBeTechnicalMachine)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Changing whether an item is a TM is not supported because it would invalidate the physical TM-number permutation.",
                ZaEditSessionSupport.ItemsDomain,
                field: field,
                expected: "Preserve the loaded set of physical TM item rows"));
            return false;
        }

        if (currentlyTechnicalMachine
            && field == ZaItemsWorkflowService.MachineMoveIdField)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Changing a TM's move is not supported until Pokemon compatibility can be migrated with it.",
                ZaEditSessionSupport.ItemsDomain,
                field: field,
                expected: "Preserve the TM move ID while changing its number or other independent fields"));
            return false;
        }

        return true;
    }

    private static EditSession RemoveSourceEquivalentPendingEdits(
        ZaItemsWorkflow sourceWorkflow,
        EditSession session)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSourceEquivalentEdit(sourceWorkflow, edit))
            .ToArray();
        return pendingEdits.Length == session.PendingEdits.Count
            ? session
            : session with { PendingEdits = pendingEdits };
    }

    private static bool IsSourceEquivalentEdit(
        ZaItemsWorkflow sourceWorkflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.ItemsDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || sourceWorkflow.Items.FirstOrDefault(item => item.ItemId == itemId) is not { } sourceItem
            || edit.Field is null)
        {
            return false;
        }

        if (string.Equals(
                edit.Field,
                ZaItemsWorkflowService.TechnicalMachineNumberField,
                StringComparison.Ordinal)
            && HasLegacyTechnicalMachineRecovery(sourceWorkflow))
        {
            // A source-equivalent TM number is the explicit, no-data-loss marker used by
            // the desktop to request the reviewed legacy repair without inventing an
            // unrelated item change.
            return false;
        }

        return sourceItem.FieldValues.TryGetValue(edit.Field, out var sourceValue)
            && sourceValue == value;
    }

    private static bool HasLegacyTechnicalMachineRecovery(ZaItemsWorkflow workflow)
    {
        return workflow.Diagnostics.Any(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning
            && string.Equals(
                diagnostic.Field,
                ZaItemsWorkflowService.TechnicalMachineNumberField,
                StringComparison.Ordinal)
            && diagnostic.Message.StartsWith(
                "A legacy KM Editor TM-numbering output was detected.",
                StringComparison.Ordinal));
    }

    private static ZaItemsWorkflow OverlayPendingEdits(ZaItemsWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static ZaItemsWorkflow OverlayPendingEdit(ZaItemsWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.ItemsDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || TryParseEditableValue(
                workflow,
                edit.Field,
                edit.NewValue,
                new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        return workflow with
        {
            Items = workflow.Items
                .Select(item => item.ItemId == itemId ? OverlayItem(workflow, item, edit.Field, value) : item)
                .ToArray(),
        };
    }

    private static ZaItemRecord OverlayItem(
        ZaItemsWorkflow workflow,
        ZaItemRecord item,
        string? field,
        int value)
    {
        var metadata = item.Metadata;
        var updated = field switch
        {
            ZaItemsWorkflowService.ItemTypeField => item with { Metadata = metadata with { ItemType = value } },
            ZaItemsWorkflowService.PriceField => item with { BuyPrice = value, SellPrice = value / 2 },
            ZaItemsWorkflowService.MegaShardPriceField => item with { WattsPrice = value },
            ZaItemsWorkflowService.ColorfulScrewPriceField => item with { AlternatePrice = value },
            ZaItemsWorkflowService.PocketField => item with
            {
                Category = ZaItemsWorkflowService.FormatPocket(value),
                Metadata = metadata with { Pouch = value, GroupType = value },
            },
            ZaItemsWorkflowService.StackCapField => item,
            ZaItemsWorkflowService.SortOrderField => item with
            {
                Metadata = metadata with { SortIndex = value },
            },
            ZaItemsWorkflowService.CanNotHoldField => item,
            ZaItemsWorkflowService.MachineMoveIdField => item with
            {
                Metadata = metadata with
                {
                    MachineMoveId = value > 0 ? value : null,
                    MachineMoveName = value > 0 ? ResolveMoveName(workflow, value) : null,
                },
            },
            ZaItemsWorkflowService.TechnicalMachineNumberField => item with
            {
                Metadata = metadata with
                {
                    SortIndex = value,
                    GroupIndex = value - 1,
                    MachineSlot = value,
                },
            },
            ZaItemsWorkflowService.CureSleepField => item with { Metadata = metadata with { CureStatusFlags = SetFlag(metadata.CureStatusFlags, 0, value != 0) } },
            ZaItemsWorkflowService.CurePoisonField => item with { Metadata = metadata with { CureStatusFlags = SetFlag(metadata.CureStatusFlags, 1, value != 0) } },
            ZaItemsWorkflowService.CureBurnField => item with { Metadata = metadata with { CureStatusFlags = SetFlag(metadata.CureStatusFlags, 2, value != 0) } },
            ZaItemsWorkflowService.CureFreezeField => item with { Metadata = metadata with { CureStatusFlags = SetFlag(metadata.CureStatusFlags, 3, value != 0) } },
            ZaItemsWorkflowService.CureParalyzeField => item with { Metadata = metadata with { CureStatusFlags = SetFlag(metadata.CureStatusFlags, 4, value != 0) } },
            ZaItemsWorkflowService.CureConfuseField => item with { Metadata = metadata with { CureStatusFlags = SetFlag(metadata.CureStatusFlags, 5, value != 0) } },
            ZaItemsWorkflowService.CureInfatuationField => item with { Metadata = metadata with { CureStatusFlags = SetFlag(metadata.CureStatusFlags, 6, value != 0) } },
            ZaItemsWorkflowService.AttackBoostField => item with { Metadata = metadata with { Boost0 = value } },
            ZaItemsWorkflowService.DefenseBoostField => item with { Metadata = metadata with { Boost1 = value } },
            ZaItemsWorkflowService.SpecialAttackBoostField => item with { Metadata = metadata with { Boost2 = value } },
            ZaItemsWorkflowService.SpecialDefenseBoostField => item with { Metadata = metadata with { Boost3 = value } },
            ZaItemsWorkflowService.SpeedBoostField => item with { Metadata = metadata with { UseFlags1 = value } },
            ZaItemsWorkflowService.AccuracyBoostField => item with { Metadata = metadata with { UseFlags2 = value } },
            ZaItemsWorkflowService.HealPowerField => item with { Metadata = metadata with { HealAmount = value } },
            ZaItemsWorkflowService.EvHpField => item with { Metadata = metadata with { EvHp = value } },
            ZaItemsWorkflowService.EvAttackField => item with { Metadata = metadata with { EvAttack = value } },
            ZaItemsWorkflowService.EvDefenseField => item with { Metadata = metadata with { EvDefense = value } },
            ZaItemsWorkflowService.EvSpeedField => item with { Metadata = metadata with { EvSpeed = value } },
            ZaItemsWorkflowService.EvSpecialAttackField => item with { Metadata = metadata with { EvSpecialAttack = value } },
            ZaItemsWorkflowService.EvSpecialDefenseField => item with { Metadata = metadata with { EvSpecialDefense = value } },
            ZaItemsWorkflowService.FriendshipGain1Field => item with { Metadata = metadata with { FriendshipGain1 = value } },
            ZaItemsWorkflowService.FriendshipGain2Field => item with { Metadata = metadata with { FriendshipGain2 = value } },
            ZaItemsWorkflowService.FriendshipGain3Field => item with { Metadata = metadata with { FriendshipGain3 = value } },
            ZaItemsWorkflowService.CanUseInBattleField => item with { Metadata = metadata with { FieldUseType = value != 0 ? 1 : 0 } },
            _ => item,
        };

        updated = UpdateTechnicalMachineName(updated);
        if (field is null)
        {
            return updated;
        }

        var fieldValues = SetFieldValue(updated.FieldValues, field, value);
        if (field == ZaItemsWorkflowService.TechnicalMachineNumberField)
        {
            fieldValues = SetFieldValue(
                fieldValues,
                ZaItemsWorkflowService.SortOrderField,
                value);
        }

        var canUseOnPokemon = CanUseOnPokemon(fieldValues);
        fieldValues = SetFieldValue(fieldValues, ZaItemsWorkflowService.CanUseOnPokemonField, canUseOnPokemon ? 1 : 0);
        return updated with
        {
            FieldValues = fieldValues,
            Metadata = updated.Metadata with { CanUseOnPokemon = canUseOnPokemon },
        };
    }

    private static ZaItemRecord UpdateTechnicalMachineName(ZaItemRecord item)
    {
        return item.Metadata.MachineSlot is { } machineSlot
            && item.Metadata.MachineMoveName is { Length: > 0 } machineMoveName
                ? item with { Name = ZaItemsWorkflowService.FormatTechnicalMachineName(machineSlot, machineMoveName) }
                : item;
    }

    private static string ResolveMoveName(ZaItemsWorkflow workflow, int moveId)
    {
        var option = workflow.EditableFields
            .FirstOrDefault(field => field.Field == ZaItemsWorkflowService.MachineMoveIdField)
            ?.Options
            .FirstOrDefault(candidate => candidate.Value == moveId);
        if (option is not null)
        {
            var prefix = moveId.ToString(CultureInfo.InvariantCulture);
            return option.Label.StartsWith(prefix, StringComparison.Ordinal)
                ? option.Label[prefix.Length..].TrimStart()
                : option.Label;
        }

        return ZaLabels.Move(moveId);
    }

    private static IReadOnlyDictionary<string, int?> SetFieldValue(
        IReadOnlyDictionary<string, int?> values,
        string field,
        int value)
    {
        var updated = new Dictionary<string, int?>(values, StringComparer.Ordinal)
        {
            [field] = value,
        };
        return updated;
    }

    private static bool CanUseOnPokemon(IReadOnlyDictionary<string, int?> fieldValues)
    {
        return IsEnabled(fieldValues, ZaItemsWorkflowService.CureSleepField)
            || IsEnabled(fieldValues, ZaItemsWorkflowService.CurePoisonField)
            || IsEnabled(fieldValues, ZaItemsWorkflowService.CureBurnField)
            || IsEnabled(fieldValues, ZaItemsWorkflowService.CureFreezeField)
            || IsEnabled(fieldValues, ZaItemsWorkflowService.CureParalyzeField)
            || IsEnabled(fieldValues, ZaItemsWorkflowService.CureConfuseField)
            || IsEnabled(fieldValues, ZaItemsWorkflowService.CureInfatuationField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.HealPowerField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.HealPercentageField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.RevivalCountField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.RevivePercentageField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.ExpPointGainField)
            || HasMintNature(fieldValues)
            || IsEnabled(fieldValues, ZaItemsWorkflowService.EvolutionItemField)
            || IsEnabled(fieldValues, ZaItemsWorkflowService.FormChangeItemField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.EvHpField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.EvAttackField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.EvDefenseField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.EvSpeedField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.EvSpecialAttackField)
            || IsNonZero(fieldValues, ZaItemsWorkflowService.EvSpecialDefenseField);
    }

    private static bool IsEnabled(IReadOnlyDictionary<string, int?> fieldValues, string field) =>
        fieldValues.TryGetValue(field, out var value) && value == 1;

    private static bool IsNonZero(IReadOnlyDictionary<string, int?> fieldValues, string field) =>
        fieldValues.TryGetValue(field, out var value) && value is not null && value != 0;

    private static bool HasMintNature(IReadOnlyDictionary<string, int?> fieldValues) =>
        fieldValues.TryGetValue(ZaItemsWorkflowService.MintNatureField, out var value)
        && value is not null
        && value >= 0;

    private static int SetFlag(int flags, int bit, bool enabled)
    {
        return enabled
            ? flags | (1 << bit)
            : flags & ~(1 << bit);
    }

    private static void ApplyEdit(
        List<ItemRow> rows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.ItemsDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit is not valid for apply.",
                ZaEditSessionSupport.ItemsDomain,
                expected: "Valid item edit"));
            return;
        }

        var row = rows.FirstOrDefault(candidate => candidate.Id == itemId);
        if (row is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item {itemId} is not present in the source item array.",
                ZaEditSessionSupport.ItemsDomain,
                field: "itemId",
                expected: "Existing item source row"));
            return;
        }

        ApplyField(row, edit.Field, value);
    }

    private static void ApplyField(ItemRow row, string? field, int value)
    {
        switch (field)
        {
            case ZaItemsWorkflowService.ItemTypeField:
                row.ItemType = value;
                break;
            case ZaItemsWorkflowService.PriceField:
                row.Price = value;
                break;
            case ZaItemsWorkflowService.MegaShardPriceField:
                row.PriceMegaShard = value;
                break;
            case ZaItemsWorkflowService.ColorfulScrewPriceField:
                row.PriceColorfulScrew = value;
                break;
            case ZaItemsWorkflowService.PocketField:
                row.Pocket = value;
                break;
            case ZaItemsWorkflowService.StackCapField:
                row.SlotMaxNum = value;
                break;
            case ZaItemsWorkflowService.SortOrderField:
                row.SortNum = value;
                break;
            case ZaItemsWorkflowService.CanNotHoldField:
                row.CanNotHold = value != 0;
                break;
            case ZaItemsWorkflowService.MachineMoveIdField:
                row.MachineWaza = checked((ushort)value);
                break;
            case ZaItemsWorkflowService.TechnicalMachineNumberField:
                row.SortNum = value;
                row.MachineIndex = checked(value - 1);
                break;
            case ZaItemsWorkflowService.CureSleepField:
                row.WorkRecvSleep = value != 0;
                break;
            case ZaItemsWorkflowService.CurePoisonField:
                row.WorkRecvPoison = value != 0;
                break;
            case ZaItemsWorkflowService.CureBurnField:
                row.WorkRecvBurn = value != 0;
                break;
            case ZaItemsWorkflowService.CureFreezeField:
                row.WorkRecvFreeze = value != 0;
                break;
            case ZaItemsWorkflowService.CureParalyzeField:
                row.WorkRecvParalyze = value != 0;
                break;
            case ZaItemsWorkflowService.CureConfuseField:
                row.WorkRecvConfuse = value != 0;
                break;
            case ZaItemsWorkflowService.CureInfatuationField:
                row.WorkRecvMero = value != 0;
                break;
            case ZaItemsWorkflowService.AttackBoostField:
                row.WorkAttack = value;
                break;
            case ZaItemsWorkflowService.DefenseBoostField:
                row.WorkDefense = value;
                break;
            case ZaItemsWorkflowService.SpecialAttackBoostField:
                row.WorkSpAttack = value;
                break;
            case ZaItemsWorkflowService.SpecialDefenseBoostField:
                row.WorkSpDefense = value;
                break;
            case ZaItemsWorkflowService.SpeedBoostField:
                row.WorkSpeed = value;
                break;
            case ZaItemsWorkflowService.AccuracyBoostField:
                row.WorkAccuracy = value;
                break;
            case ZaItemsWorkflowService.CriticalHitBoostField:
                row.WorkCritical = value;
                break;
            case ZaItemsWorkflowService.EffectGuardField:
                row.WorkEffectGuard = value;
                break;
            case ZaItemsWorkflowService.MintNatureField:
                row.MintNature = value;
                break;
            case ZaItemsWorkflowService.HealPowerField:
                row.WorkRecvPower = value;
                break;
            case ZaItemsWorkflowService.HealPercentageField:
                row.HealPercentage = value;
                break;
            case ZaItemsWorkflowService.RevivalCountField:
                row.WorkRevival = value;
                break;
            case ZaItemsWorkflowService.RevivePercentageField:
                row.RevivePercentage = value;
                break;
            case ZaItemsWorkflowService.ExpPointGainField:
                row.ExpPointGain = value;
                break;
            case ZaItemsWorkflowService.MaxUseLevelField:
                row.MaxUseLevel = value;
                break;
            case ZaItemsWorkflowService.FriendshipGain1Field:
                row.WorkFriendly1 = value;
                break;
            case ZaItemsWorkflowService.FriendshipGain2Field:
                row.WorkFriendly2 = value;
                break;
            case ZaItemsWorkflowService.FriendshipGain3Field:
                row.WorkFriendly3 = value;
                break;
            case ZaItemsWorkflowService.EvolutionItemField:
                row.WorkEvolutional = value != 0;
                break;
            case ZaItemsWorkflowService.FormChangeItemField:
                row.WorkFormChange = value != 0;
                break;
            case ZaItemsWorkflowService.EvHpField:
                row.WorkStatusHp = value;
                break;
            case ZaItemsWorkflowService.EvAttackField:
                row.WorkStatusAtk = value;
                break;
            case ZaItemsWorkflowService.EvDefenseField:
                row.WorkStatusDef = value;
                break;
            case ZaItemsWorkflowService.EvSpeedField:
                row.WorkStatusSpd = value;
                break;
            case ZaItemsWorkflowService.EvSpecialAttackField:
                row.WorkStatusSAtk = value;
                break;
            case ZaItemsWorkflowService.EvSpecialDefenseField:
                row.WorkStatusSDef = value;
                break;
            case ZaItemsWorkflowService.EquipPowerField:
                row.EquipPower = value;
                break;
            case ZaItemsWorkflowService.AutoHealPriorityField:
                row.AutoHealPriority = value;
                break;
            case ZaItemsWorkflowService.CanUseInBattleField:
                row.CanUseInBattle = value != 0;
                break;
            case ZaItemsWorkflowService.SwapIntoItemField:
                row.SwapIntoId = value;
                break;
        }
    }

    private static List<ItemRow> ReadRows(byte[] bytes)
    {
        var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(bytes));
        var rows = new List<ItemRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null)
            {
                rows.Add(ItemRow.From(row.Value));
            }
        }

        return rows;
    }

    private static bool IsTechnicalMachine(ItemRow row)
    {
        return row.Pocket == 6
            && row.ItemType == 5
            && row.MachineWaza > 0;
    }

    private static void ValidatePhysicalTechnicalMachineRows(
        IReadOnlyList<ItemRow> rows,
        IReadOnlyDictionary<int, PhysicalTechnicalMachineIdentity> expectedTechnicalMachines,
        ICollection<ValidationDiagnostic> diagnostics,
        string context)
    {
        var machines = rows.Where(IsTechnicalMachine).ToArray();
        var assignments = machines
            .Select(row => new ZaTechnicalMachineNumberAssignment(
                row.Id,
                row.SortNum,
                row.MachineIndex))
            .ToArray();
        var actualMachineItemIds = machines.Select(row => row.Id).Order().ToArray();
        var expectedMachineItemIds = expectedTechnicalMachines.Keys.Order().ToArray();
        var valid = rows.Select(row => row.Id).Distinct().Count() == rows.Count
            && actualMachineItemIds.SequenceEqual(expectedMachineItemIds)
            && ZaTechnicalMachineCatalog.HasCompleteNumbering(assignments)
            && machines.All(row =>
                expectedTechnicalMachines.TryGetValue(row.Id, out var expected)
                && row.MachineWaza == expected.MoveId
                && string.Equals(
                    row.IconName,
                    expected.IconName,
                    StringComparison.Ordinal));
        if (valid)
        {
            return;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"The {context} failed physical TM validation. No output was written.",
            ZaEditSessionSupport.ItemsDomain,
            field: ZaItemsWorkflowService.TechnicalMachineNumberField,
            expected: "Unique item IDs, unchanged physical TM membership, move assignments, and reviewed icons, plus paired number/index permutations"));
    }

    private static byte[] WriteRows(IReadOnlyList<ItemRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = ZaItemDataArray.CreateValuesVector(builder, offsets);
        var root = ZaItemDataArray.CreateZaItemDataArray(builder, vector);
        ZaItemDataArray.FinishZaItemDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private sealed class ItemRow
    {
        public int Id { get; init; }
        public int ItemType { get; set; }
        public string InternalName { get; init; } = string.Empty;
        public string IconName { get; set; } = string.Empty;
        public int Price { get; set; }
        public int Pocket { get; set; }
        public int SlotMaxNum { get; set; }
        public int SortNum { get; set; }
        public int PriceMegaShard { get; set; }
        public int PriceColorfulScrew { get; set; }
        public bool CanNotHold { get; set; }
        public ushort MachineWaza { get; set; }
        public int MachineIndex { get; set; }
        public bool WorkRecvSleep { get; set; }
        public bool WorkRecvPoison { get; set; }
        public bool WorkRecvBurn { get; set; }
        public bool WorkRecvFreeze { get; set; }
        public bool WorkRecvParalyze { get; set; }
        public bool WorkRecvConfuse { get; set; }
        public bool WorkRecvMero { get; set; }
        public int WorkAttack { get; set; }
        public int WorkDefense { get; set; }
        public int WorkSpAttack { get; set; }
        public int WorkSpDefense { get; set; }
        public int WorkSpeed { get; set; }
        public int WorkAccuracy { get; set; }
        public int WorkCritical { get; set; }
        public int WorkEffectGuard { get; set; }
        public int MintNature { get; set; }
        public int WorkRecvPower { get; set; }
        public int HealPercentage { get; set; }
        public int WorkRevival { get; set; }
        public int RevivePercentage { get; set; }
        public int ExpPointGain { get; set; }
        public int MaxUseLevel { get; set; }
        public int WorkFriendly1 { get; set; }
        public int WorkFriendly2 { get; set; }
        public int WorkFriendly3 { get; set; }
        public bool WorkEvolutional { get; set; }
        public bool WorkFormChange { get; set; }
        public int WorkStatusHp { get; set; }
        public int WorkStatusAtk { get; set; }
        public int WorkStatusDef { get; set; }
        public int WorkStatusSpd { get; set; }
        public int WorkStatusSAtk { get; set; }
        public int WorkStatusSDef { get; set; }
        public int EquipPower { get; set; }
        public int AutoHealPriority { get; set; }
        public bool CanUseInBattle { get; set; }
        public int SwapIntoId { get; set; }

        public static ItemRow From(ZaItemData row)
        {
            return new ItemRow
            {
                Id = row.Id,
                ItemType = row.ItemType,
                InternalName = row.InternalName ?? string.Empty,
                IconName = row.IconName ?? string.Empty,
                Price = row.Price,
                Pocket = row.Pocket,
                SlotMaxNum = row.SlotMaxNum,
                SortNum = row.SortNum,
                PriceMegaShard = row.PriceMegaShard,
                PriceColorfulScrew = row.PriceColorfulScrew,
                CanNotHold = row.CanNotHold,
                MachineWaza = row.MachineWaza,
                MachineIndex = row.MachineIndex,
                WorkRecvSleep = row.WorkRecvSleep,
                WorkRecvPoison = row.WorkRecvPoison,
                WorkRecvBurn = row.WorkRecvBurn,
                WorkRecvFreeze = row.WorkRecvFreeze,
                WorkRecvParalyze = row.WorkRecvParalyze,
                WorkRecvConfuse = row.WorkRecvConfuse,
                WorkRecvMero = row.WorkRecvMero,
                WorkAttack = row.WorkAttack,
                WorkDefense = row.WorkDefense,
                WorkSpAttack = row.WorkSpAttack,
                WorkSpDefense = row.WorkSpDefense,
                WorkSpeed = row.WorkSpeed,
                WorkAccuracy = row.WorkAccuracy,
                WorkCritical = row.WorkCritical,
                WorkEffectGuard = row.WorkEffectGuard,
                // Keep the game's raw sentinel. Most non-mint rows use -1 here; normalizing
                // that to the display value 0 makes every rewritten item behave like it has
                // a Pokemon-use effect. Display normalization belongs in the workflow model,
                // never in the lossless row used to rebuild the game file.
                MintNature = row.MintNature,
                WorkRecvPower = row.WorkRecvPower,
                HealPercentage = row.HealPercentage,
                WorkRevival = row.WorkRevival,
                RevivePercentage = row.RevivePercentage,
                ExpPointGain = row.ExpPointGain,
                MaxUseLevel = row.MaxUseLevel,
                WorkFriendly1 = row.WorkFriendly1,
                WorkFriendly2 = row.WorkFriendly2,
                WorkFriendly3 = row.WorkFriendly3,
                WorkEvolutional = row.WorkEvolutional,
                WorkFormChange = row.WorkFormChange,
                WorkStatusHp = row.WorkStatusHp,
                WorkStatusAtk = row.WorkStatusAtk,
                WorkStatusDef = row.WorkStatusDef,
                WorkStatusSpd = row.WorkStatusSpd,
                WorkStatusSAtk = row.WorkStatusSAtk,
                WorkStatusSDef = row.WorkStatusSDef,
                EquipPower = row.EquipPower,
                AutoHealPriority = row.AutoHealPriority,
                CanUseInBattle = row.CanUseInBattle,
                SwapIntoId = row.SwapIntoId,
            };
        }

        public Offset<ZaItemData> Write(FlatBufferBuilder builder)
        {
            var internalNameOffset = builder.CreateString(InternalName);
            var iconNameOffset = builder.CreateString(IconName);

            ZaItemData.StartZaItemData(builder);
            ZaItemData.AddSwapIntoId(builder, SwapIntoId);
            ZaItemData.AddCanUseInBattle(builder, CanUseInBattle);
            ZaItemData.AddAutoHealPriority(builder, AutoHealPriority);
            ZaItemData.AddEquipPower(builder, EquipPower);
            ZaItemData.AddWorkStatusSDef(builder, WorkStatusSDef);
            ZaItemData.AddWorkStatusSAtk(builder, WorkStatusSAtk);
            ZaItemData.AddWorkStatusSpd(builder, WorkStatusSpd);
            ZaItemData.AddWorkStatusDef(builder, WorkStatusDef);
            ZaItemData.AddWorkStatusAtk(builder, WorkStatusAtk);
            ZaItemData.AddWorkStatusHp(builder, WorkStatusHp);
            ZaItemData.AddWorkFormChange(builder, WorkFormChange);
            ZaItemData.AddWorkEvolutional(builder, WorkEvolutional);
            ZaItemData.AddWorkFriendly3(builder, WorkFriendly3);
            ZaItemData.AddWorkFriendly2(builder, WorkFriendly2);
            ZaItemData.AddWorkFriendly1(builder, WorkFriendly1);
            ZaItemData.AddMaxUseLevel(builder, MaxUseLevel);
            ZaItemData.AddExpPointGain(builder, ExpPointGain);
            ZaItemData.AddRevivePercentage(builder, RevivePercentage);
            ZaItemData.AddWorkRevival(builder, WorkRevival);
            ZaItemData.AddHealPercentage(builder, HealPercentage);
            ZaItemData.AddWorkRecvPower(builder, WorkRecvPower);
            ZaItemData.AddMintNature(builder, MintNature);
            ZaItemData.AddWorkEffectGuard(builder, WorkEffectGuard);
            ZaItemData.AddWorkCritical(builder, WorkCritical);
            ZaItemData.AddWorkAccuracy(builder, WorkAccuracy);
            ZaItemData.AddWorkSpeed(builder, WorkSpeed);
            ZaItemData.AddWorkSpDefense(builder, WorkSpDefense);
            ZaItemData.AddWorkSpAttack(builder, WorkSpAttack);
            ZaItemData.AddWorkDefense(builder, WorkDefense);
            ZaItemData.AddWorkAttack(builder, WorkAttack);
            ZaItemData.AddWorkRecvMero(builder, WorkRecvMero);
            ZaItemData.AddWorkRecvConfuse(builder, WorkRecvConfuse);
            ZaItemData.AddWorkRecvParalyze(builder, WorkRecvParalyze);
            ZaItemData.AddWorkRecvFreeze(builder, WorkRecvFreeze);
            ZaItemData.AddWorkRecvBurn(builder, WorkRecvBurn);
            ZaItemData.AddWorkRecvPoison(builder, WorkRecvPoison);
            ZaItemData.AddWorkRecvSleep(builder, WorkRecvSleep);
            ZaItemData.AddMachineIndex(builder, MachineIndex);
            ZaItemData.AddMachineWaza(builder, MachineWaza);
            ZaItemData.AddCanNotHold(builder, CanNotHold);
            ZaItemData.AddPriceColorfulScrew(builder, PriceColorfulScrew);
            ZaItemData.AddPriceMegaShard(builder, PriceMegaShard);
            ZaItemData.AddSortNum(builder, SortNum);
            ZaItemData.AddSlotMaxNum(builder, SlotMaxNum);
            ZaItemData.AddPocket(builder, Pocket);
            ZaItemData.AddPrice(builder, Price);
            ZaItemData.AddIconName(builder, iconNameOffset);
            ZaItemData.AddInternalName(builder, internalNameOffset);
            ZaItemData.AddItemType(builder, ItemType);
            ZaItemData.AddId(builder, Id);
            return ZaItemData.EndZaItemData(builder);
        }
    }
}
