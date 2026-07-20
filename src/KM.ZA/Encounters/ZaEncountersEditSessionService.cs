// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.ZA.Encounters;

internal sealed class ZaEncountersEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaEncountersWorkflowService encountersWorkflowService;

    public ZaEncountersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaEncountersWorkflowService? encountersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.encountersWorkflowService = encountersWorkflowService ?? new ZaEncountersWorkflowService(this.fileSource);
    }

    public ZaEncountersEditResult UpdateSlotField(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = encountersWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.EncountersDomain,
                diagnostics))
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        var slotRecord = table?.Slots.FirstOrDefault(candidate => candidate.Slot == slot);
        if (table is null || slotRecord is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounter edit targets a table or slot that is not loaded.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing Pokemon Legends Z-A encounter table slot"));
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, table, slotRecord, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var candidateWorkflow = OverlayPendingEdit(workflow, pendingEdit);
        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        var pairErrorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        ValidateFinalSpeciesFormPairs(loadedWorkflow, candidateWorkflow, diagnostics);
        if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > pairErrorCount)
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        if (AffectsSharedLevelRange(pendingEdit.Field))
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidateFinalSharedLevelRanges(candidateWorkflow, [slotRecord.PokemonDataSourceIndex], diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount)
            {
                return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
            }
        }

        if (AffectsSpawnerData(pendingEdit.Field))
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidateFinalSpawnerCounts(candidateWorkflow, updatedSession.PendingEdits, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) > errorCount)
            {
                return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
            }
        }

        return new ZaEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEncountersEditResult UpdateSlotFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaEncounterSlotFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = encountersWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.EncountersDomain,
                diagnostics))
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        var sharedLevelRangeSources = new HashSet<int>();
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.TableId)
                || string.IsNullOrWhiteSpace(update.Field)
                || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Encounter batch update is missing a table, field, or value.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: "updates",
                    expected: "Complete Pokemon Legends Z-A encounter slot field update"));
                continue;
            }

            var table = effectiveWorkflow.Tables.FirstOrDefault(candidate => candidate.TableId == update.TableId);
            var slot = table?.Slots.FirstOrDefault(candidate => candidate.Slot == update.Slot);
            if (table is null || slot is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Encounter edit targets a table or slot that is not loaded.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: "slot",
                    expected: "Existing Pokemon Legends Z-A encounter table slot"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(
                effectiveWorkflow,
                table,
                slot,
                update.Field,
                update.Value,
                diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
            if (AffectsSharedLevelRange(pendingEdit.Field))
            {
                sharedLevelRangeSources.Add(slot.PokemonDataSourceIndex);
            }

        }

        ValidateFinalSpeciesFormPairs(loadedWorkflow, effectiveWorkflow, diagnostics);
        ValidateFinalSharedLevelRanges(effectiveWorkflow, sharedLevelRangeSources, diagnostics);
        ValidateFinalSpawnerCounts(effectiveWorkflow, updatedSession.PendingEdits, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaEncountersEditResult(workflow, currentSession, diagnostics);
        }

        return new ZaEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = encountersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);

        var effectiveWorkflow = workflow;
        var sharedLevelRangeSources = new HashSet<int>();
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(effectiveWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
                if (AffectsSharedLevelRange(edit.Field)
                    && TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out var sourceIndex))
                {
                    sharedLevelRangeSources.Add(sourceIndex);
                }
            }
        }

        ValidateFinalSpeciesFormPairs(workflow, effectiveWorkflow, diagnostics);
        ValidateFinalSharedLevelRanges(effectiveWorkflow, sharedLevelRangeSources, diagnostics);
        ValidateFinalSpawnerCounts(effectiveWorkflow, session.PendingEdits, diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Wild Encounters change is valid.",
                ZaEditSessionSupport.EncountersDomain));
        }

        return new ZaEditSessionValidation(
            session,
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

        var diagnostics = Validate(paths, session).Diagnostics.ToList();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Wild Encounters edit before reviewing a change plan.",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Pending Wild Encounters edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var semanticSources = ReadPlanSemanticSources(project);
            var semanticSourceReferences = semanticSources
                .Where(source => source.Layer is not null)
                .Select(source => new ProjectFileReference(
                    source.Layer!.Value,
                    source.SourceIdentity))
                .ToArray();
            var plannedVirtualPaths = session.PendingEdits
                .Select(edit => GetSourcePathForField(edit.Field))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var writes = session.PendingEdits
                .GroupBy(edit => GetSourcePathForField(edit.Field), StringComparer.Ordinal)
                .Select(group =>
                {
                    var plannedEdits = group
                        .OrderBy(edit => edit.RecordId, StringComparer.Ordinal)
                        .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                        .ThenBy(edit => edit.NewValue, StringComparer.Ordinal)
                        .ThenBy(edit => edit.Summary, StringComparer.Ordinal)
                        .ToArray();
                    var sources = plannedEdits
                        .SelectMany(edit => edit.Sources)
                        .Concat(semanticSourceReferences)
                        .Distinct()
                        .OrderBy(source => source.Layer)
                        .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                        .ToArray();
                    var writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                        paths,
                        group.Key,
                        sources,
                        outputMode);
                    var editCount = plannedEdits.Length;
                    var changeSetFingerprint = CreatePlanChangeSetFingerprint(plannedEdits);
                    var reason = editCount == 1
                        ? $"Apply pending Wild Encounters edit: {plannedEdits[0].Summary} "
                            + $"Change set SHA-256 {changeSetFingerprint}."
                        : $"Apply {editCount.ToString(CultureInfo.InvariantCulture)} pending Wild Encounters edits: "
                            + $"change set SHA-256 {changeSetFingerprint}.";
                    return new PlannedFileWrite(
                        writeInfo.TargetRelativePath,
                        writeInfo.Sources,
                        writeInfo.ReplacesExistingOutput,
                        reason,
                        CreatePlanSourceFingerprint(
                            paths,
                            group.Key,
                            outputMode,
                            semanticSources));
                })
                .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
                .ToList();

            if (outputMode == ZaOutputMode.Standalone)
            {
                var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides.",
                    CreatePlanSourceFingerprint(
                        paths,
                        ZaWorkflowFileSource.DescriptorVirtualPath,
                        ZaOutputMode.Standalone,
                        [
                            new PlanFingerprintSource(
                                ZaWorkflowFileSource.DescriptorVirtualPath,
                                ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                                    paths,
                                    plannedVirtualPaths),
                                "DescriptorPreview",
                                ZaWorkflowFileSource.DescriptorVirtualPath),
                        ])));
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Change plan preview contains {writes.Count.ToString(CultureInfo.InvariantCulture)} target files.",
                ZaEditSessionSupport.EncountersDomain));

            return new ChangePlan(session.Id, writes, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Wild Encounters change plan could not resolve the output target: {exception.Message}",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Writable output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
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
                    $"Wild Encounters output is busy or unavailable: {exception.Message}",
                    ZaEditSessionSupport.EncountersDomain,
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
                ZaEditSessionSupport.EncountersDomain,
                expected: "Current reviewed Wild Encounters change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var workflow = encountersWorkflowService.Load(project);
            var writesEncounterData = session.PendingEdits.Any(edit => AffectsSharedPokemonData(edit.Field));
            var writesSpawnerData = session.PendingEdits.Any(edit => AffectsSpawnerData(edit.Field));
            var encounterSource = fileSource.Read(project, ZaDataPaths.EncountDataArray);
            var spawnerSource = fileSource.Read(project, ZaDataPaths.PokemonSpawnerDataArray);
            var capturedSemanticSources = CreatePlanSemanticSources(
                encounterSource,
                spawnerSource);
            if ((writesEncounterData && !CapturedSourcesMatchPlan(
                    paths,
                    currentPlan,
                    ZaDataPaths.EncountDataArray,
                    outputMode,
                    capturedSemanticSources))
                || (writesSpawnerData && !CapturedSourcesMatchPlan(
                    paths,
                    currentPlan,
                    ZaDataPaths.PokemonSpawnerDataArray,
                    outputMode,
                    capturedSemanticSources)))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Wild Encounters source or destination changed after review. Review the change plan again before applying.",
                    ZaEditSessionSupport.EncountersDomain,
                    expected: "The exact reviewed Wild Encounters source and output target"));
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            byte[]? reviewedStandaloneDescriptorBytes = null;
            if (outputMode == ZaOutputMode.Standalone)
            {
                var plannedVirtualPaths = session.PendingEdits
                    .Select(edit => GetSourcePathForField(edit.Field))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                reviewedStandaloneDescriptorBytes =
                    ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                        paths,
                        plannedVirtualPaths);
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
                        "The standalone Trinity descriptor changed after review. "
                        + "Review the change plan again before applying.",
                        ZaEditSessionSupport.EncountersDomain,
                        expected: "The exact reviewed standalone Trinity descriptor"));
                    return ZaEditSessionSupport.CreateApplyResult(
                        applyId,
                        appliedAt,
                        currentPlan,
                        writtenFiles,
                        diagnostics);
                }
            }

            var encounterDocument = writesEncounterData
                ? ZaEncounterDataDocument.Parse(encounterSource.Bytes)
                : null;
            var spawnerDocument = writesSpawnerData
                ? ZaPokemonSpawnerDataDocument.Parse(spawnerSource.Bytes)
                : null;
            foreach (var edit in session.PendingEdits)
            {
                if (AffectsSpawnerData(edit.Field))
                {
                    ApplySpawnerEdit(workflow, spawnerDocument!, edit, diagnostics);
                }
                else
                {
                    ApplyEdit(workflow, encounterDocument!, edit, diagnostics);
                }
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var outputWrites = new List<ZaWorkflowFileWrite>();
            if (encounterDocument is not null)
            {
                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.EncountDataArray,
                    encounterDocument.Write()));
            }

            if (spawnerDocument is not null)
            {
                outputWrites.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.PokemonSpawnerDataArray,
                    spawnerDocument.Write()));
            }

            ZaWorkflowFileSource.WriteBatch(
                paths,
                outputWrites,
                outputMode,
                reviewedStandaloneDescriptorBytes);
            if (encounterDocument is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.EncountDataArray, outputMode));
            }

            if (spawnerDocument is not null)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.PokemonSpawnerDataArray, outputMode));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Wild Encounters", outputMode),
                ZaEditSessionSupport.EncountersDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Wild Encounters output could not be written: {exception.Message}",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static string CreatePlanChangeSetFingerprint(
        IReadOnlyList<PendingEdit> edits)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintValue(hash, "KM.ZA.Encounters.ChangeSet.v1");
        AppendFingerprintValue(hash, edits.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var edit in edits
            .OrderBy(candidate => candidate.Domain, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.RecordId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Field, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.NewValue, StringComparer.Ordinal))
        {
            AppendFingerprintValue(hash, edit.Domain);
            AppendFingerprintValue(hash, edit.RecordId);
            AppendFingerprintValue(hash, edit.Field);
            AppendFingerprintValue(hash, edit.NewValue);
            var sources = edit.Sources
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .ToArray();
            AppendFingerprintValue(hash, sources.Length.ToString(CultureInfo.InvariantCulture));
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

    private static string CreatePlanSourceFingerprint(
        ProjectPaths paths,
        string virtualPath,
        ZaOutputMode outputMode,
        IReadOnlyList<PlanFingerprintSource> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintValue(hash, "KM.ZA.Encounters.Source.v2");
        AppendFingerprintValue(hash, virtualPath.Replace('\\', '/'));
        AppendFingerprintValue(hash, outputMode.ToString());
        AppendFingerprintValue(
            hash,
            NormalizeFingerprintPath(
                ZaWorkflowFileSource.ResolveOutputPath(paths, virtualPath, outputMode)));
        foreach (var source in sources.OrderBy(
                     source => source.VirtualPath,
                     StringComparer.Ordinal))
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
        IReadOnlyList<PlanFingerprintSource> sources)
    {
        var targetRelativePath = ZaWorkflowFileSource.CreatePlannedWrite(
            paths,
            virtualPath,
            Array.Empty<ProjectFileReference>(),
            outputMode).TargetRelativePath;
        var plannedWrite = plan.Writes.FirstOrDefault(write =>
            string.Equals(
                write.TargetRelativePath,
                targetRelativePath,
                StringComparison.Ordinal));
        return plannedWrite is not null
            && string.Equals(
                plannedWrite.SourceFingerprint,
                CreatePlanSourceFingerprint(
                    paths,
                    virtualPath,
                    outputMode,
                    sources),
                StringComparison.Ordinal);
    }

    private IReadOnlyList<PlanFingerprintSource> ReadPlanSemanticSources(OpenedProject project)
    {
        return CreatePlanSemanticSources(
            fileSource.Read(project, ZaDataPaths.EncountDataArray),
            fileSource.Read(project, ZaDataPaths.PokemonSpawnerDataArray));
    }

    private static IReadOnlyList<PlanFingerprintSource> CreatePlanSemanticSources(
        ZaWorkflowFile encounterSource,
        ZaWorkflowFile spawnerSource)
    {
        return
        [
            new PlanFingerprintSource(
                ZaDataPaths.EncountDataArray,
                encounterSource.Bytes,
                encounterSource.SourceLayer.ToString(),
                encounterSource.RelativePath,
                encounterSource.SourceLayer),
            new PlanFingerprintSource(
                ZaDataPaths.PokemonSpawnerDataArray,
                spawnerSource.Bytes,
                spawnerSource.SourceLayer.ToString(),
                spawnerSource.RelativePath,
                spawnerSource.SourceLayer),
        ];
    }

    private sealed record PlanFingerprintSource(
        string VirtualPath,
        byte[] Bytes,
        string SourceKind,
        string SourceIdentity,
        ProjectFileLayer? Layer = null);

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

    private static PendingEdit? CreatePendingEdit(
        ZaEncountersWorkflow workflow,
        ZaEncounterTableRecord table,
        ZaEncounterSlotRecord slot,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = ZaEncountersWorkflowService.GetEditableField(workflow, normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (AffectsSharedPokemonData(normalizedField) && slot.PokemonDataSourceIndex < 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounter slot is missing its linked encounter data row and cannot be edited.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Encounter slot linked to Encount Data"));
            return null;
        }

        if (AffectsSpawnerData(normalizedField)
            && !ValidateSpawnerFieldEditability(slot, normalizedField, diagnostics))
        {
            return null;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        if (AffectsSharedPokemonData(normalizedField)
            && !ValidateSharedAlphaChance(
                workflow,
                slot.PokemonDataSourceIndex,
                normalizedField,
                parsedValue.Value,
                diagnostics))
        {
            return null;
        }

        if (AffectsSharedPokemonData(normalizedField)
            && !ValidateSharedAlphaLevelBonus(
                workflow,
                slot.PokemonDataSourceIndex,
                normalizedField,
                diagnostics))
        {
            return null;
        }

        var sourceProvenance = AffectsSpawnerData(normalizedField)
            ? table.Provenance
            : slot.PokemonProvenance;
        var recordId = AffectsAppearanceCounts(normalizedField)
            ? ZaEncountersWorkflowService.CreateAppearanceRecordId(table.TableId)
            : AffectsSpawnerSlot(normalizedField)
                ? ZaEncountersWorkflowService.CreateSlotRecordId(table.TableId, slot.Slot)
                : ZaEncountersWorkflowService.CreatePokemonDataRecordId(slot.PokemonDataSourceIndex);

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.EncountersDomain,
            CreateSummary(table, slot, editableField, parsedValue.Value),
            new ProjectFileReference(sourceProvenance.SourceLayer, sourceProvenance.SourceFile),
            recordId,
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        ZaEncountersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.EncountersDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Wild Encounters.",
                ZaEditSessionSupport.EncountersDomain,
                expected: ZaEditSessionSupport.EncountersDomain));
            return;
        }

        var editableField = ZaEncountersWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        var sourceIndex = -1;
        ZaEncounterSlotRecord? slot = null;
        if (AffectsSharedPokemonData(edit.Field)
            && !TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out sourceIndex))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit targets an encounter data row that is not loaded.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing Pokemon Legends Z-A encounter data row"));
            return;
        }

        if (AffectsSpawnerSlot(edit.Field)
            && !TryResolveSpawnerSlot(workflow, edit.RecordId, out _, out slot))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit targets a spawner slot that is not loaded.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing Pokemon Legends Z-A spawner slot"));
            return;
        }

        if (AffectsAppearanceCounts(edit.Field))
        {
            if (!TryResolveAppearanceTable(workflow, edit.RecordId, out var table))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending encounter edit targets a spawner appearance that is not loaded.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: "slot",
                    expected: "Existing Pokemon Legends Z-A spawner appearance"));
                return;
            }

            slot = table.Slots.FirstOrDefault();
            if (slot is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending encounter edit targets a spawner appearance that has no encounter slots.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: edit.Field,
                    expected: "Existing Pokemon Legends Z-A spawner appearance"));
                return;
            }
        }

        if (slot is not null
            && AffectsSpawnerData(edit.Field)
            && !ValidateSpawnerFieldEditability(slot, edit.Field, diagnostics))
        {
            return;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.EncountersDomain,
            diagnostics);
        if (parsedValue is not null)
        {
            if (AffectsSharedPokemonData(edit.Field))
            {
                ValidateSharedAlphaChance(
                    workflow,
                    sourceIndex,
                    edit.Field,
                    parsedValue.Value,
                    diagnostics);
                ValidateSharedAlphaLevelBonus(
                    workflow,
                    sourceIndex,
                    edit.Field,
                    diagnostics);
            }
        }
    }

    private static bool ValidateSharedAlphaChance(
        ZaEncountersWorkflow workflow,
        int sourceIndex,
        string? field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(field, ZaEncountersWorkflowService.AlphaChancePercentField, StringComparison.Ordinal))
        {
            return true;
        }

        var linkedSlots = workflow.Tables
            .SelectMany(table => table.Slots)
            .Where(slot => slot.PokemonDataSourceIndex == sourceIndex)
            .ToArray();
        if (linkedSlots.Any(slot => slot.AlphaChancePercent is null))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shared Alpha chance is read-only because the source encounter row does not contain a whole-number percentage from 0 through 100.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "Preserve the source value or restore a whole-number shared Alpha chance before editing"));
            return false;
        }

        var hasStructuralAlphaReference = linkedSlots.Any(slot => slot.IsAlpha);
        var hasOrdinaryReference = linkedSlots.Any(slot => !slot.IsAlpha);
        if (hasStructuralAlphaReference && hasOrdinaryReference)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Shared Alpha chance cannot be edited because this encounter row is linked by both structural _Alpha and ordinary references.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "Encounter row linked only by structural _Alpha references or only by ordinary references"));
            return false;
        }

        var hasGuaranteedAlphaChance = linkedSlots.Any(slot => slot.AlphaChancePercent == 100);
        if ((hasStructuralAlphaReference || hasGuaranteedAlphaChance) && value != 100)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Guaranteed Alpha encounter rows must keep their shared Alpha chance at 100 percent.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "100 for a structural _Alpha reference or an existing 100-percent encounter row"));
            return false;
        }

        if (!hasStructuralAlphaReference && !hasGuaranteedAlphaChance && value > 99)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Ordinary encounter rows must keep their shared Alpha chance between 0 and 99 percent.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaChancePercentField,
                expected: "Whole-number percent from 0 through 99 for an ordinary encounter row"));
            return false;
        }

        return true;
    }

    private static bool AffectsSharedLevelRange(string? field)
    {
        return field is ZaEncountersWorkflowService.LevelMaxField
            or ZaEncountersWorkflowService.LevelMinField
            or ZaEncountersWorkflowService.AlphaChancePercentField
            or ZaEncountersWorkflowService.AlphaLevelBonusField;
    }

    private static bool AffectsSharedPokemonData(string? field)
    {
        return field is ZaEncountersWorkflowService.SpeciesIdField
            or ZaEncountersWorkflowService.FormField
            or ZaEncountersWorkflowService.LevelMinField
            or ZaEncountersWorkflowService.LevelMaxField
            or ZaEncountersWorkflowService.AlphaChancePercentField
            or ZaEncountersWorkflowService.AlphaLevelBonusField;
    }

    private static bool AffectsSpawnerSlot(string? field)
    {
        return field is ZaEncountersWorkflowService.WeightField
            or ZaEncountersWorkflowService.SlotMaxCountField;
    }

    private static bool AffectsAppearanceCounts(string? field)
    {
        return field is ZaEncountersWorkflowService.AppearanceMinCountField
            or ZaEncountersWorkflowService.AppearanceMaxCountField;
    }

    private static bool AffectsSpawnerData(string? field)
    {
        return AffectsSpawnerSlot(field) || AffectsAppearanceCounts(field);
    }

    private static bool ValidateSpawnerFieldEditability(
        ZaEncounterSlotRecord slot,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        string? message = field switch
        {
            ZaEncountersWorkflowService.WeightField when !slot.CanEditWeight =>
                "Spawn weight is read-only because its source FlatBuffer scalar is omitted.",
            ZaEncountersWorkflowService.SlotMaxCountField when !slot.CanEditSlotMaxCount =>
                "Slot maximum count is read-only because its source FlatBuffer scalar is omitted.",
            ZaEncountersWorkflowService.AppearanceMinCountField
                or ZaEncountersWorkflowService.AppearanceMaxCountField
                when slot.AppearanceObjectCount == 0
                    || slot.AppearanceMinCount is null
                    || slot.AppearanceMaxCount is null =>
                "Overall encounter counts are read-only because this spawner has missing or mixed appearance count values.",
            ZaEncountersWorkflowService.AppearanceMinCountField
                or ZaEncountersWorkflowService.AppearanceMaxCountField
                when !slot.CanEditAppearanceCounts =>
                "Overall encounter counts are read-only because at least one source FlatBuffer scalar is omitted.",
            _ => null,
        };
        if (message is null)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.EncountersDomain,
            field: field,
            expected: "Materialized source scalar storage for the requested spawner field"));
        return false;
    }

    private static string GetSourcePathForField(string? field)
    {
        return AffectsSpawnerData(field)
            ? ZaDataPaths.PokemonSpawnerDataArray
            : ZaDataPaths.EncountDataArray;
    }

    private static bool ValidateSharedAlphaLevelBonus(
        ZaEncountersWorkflow workflow,
        int sourceIndex,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(field, ZaEncountersWorkflowService.AlphaLevelBonusField, StringComparison.Ordinal))
        {
            return true;
        }

        var hasUnsupportedBonus = workflow.Tables
            .SelectMany(table => table.Slots)
            .Where(slot => slot.PokemonDataSourceIndex == sourceIndex)
            .Any(slot => slot.AlphaLevelBonus is null);
        if (!hasUnsupportedBonus)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Shared Alpha level bonus is read-only because the source encounter row is outside the supported range from 0 through 100.",
            ZaEditSessionSupport.EncountersDomain,
            field: ZaEncountersWorkflowService.AlphaLevelBonusField,
            expected: "Preserve the unsupported source value"));
        return false;
    }

    private static void ValidateFinalSharedLevelRanges(
        ZaEncountersWorkflow workflow,
        IEnumerable<int> sourceIndexes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var slotsBySourceIndex = workflow.Tables
            .SelectMany(table => table.Slots)
            .GroupBy(slot => slot.PokemonDataSourceIndex)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var sourceIndex in sourceIndexes.Distinct())
        {
            if (!slotsBySourceIndex.TryGetValue(sourceIndex, out var slot))
            {
                continue;
            }

            if (slot.LevelMin > slot.LevelMax)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Shared encounter level range is invalid: minimum {slot.LevelMin.ToString(CultureInfo.InvariantCulture)} "
                    + $"is greater than maximum {slot.LevelMax.ToString(CultureInfo.InvariantCulture)} for every linked placement.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.LevelMinField,
                    expected: "Shared minimum level less than or equal to shared maximum level after all batch updates"));
                continue;
            }

            if (!slot.HasAlphaChance)
            {
                continue;
            }

            if (slot.AlphaLevelBonus is not int alphaLevelBonus)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Shared Alpha level range cannot be changed while its source Alpha level bonus is outside the supported range from 0 through 100.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.AlphaLevelBonusField,
                    expected: "Preserve the unsupported source bonus or disable Alpha chance before changing the shared Alpha level range"));
                continue;
            }

            var alphaLevelMaximum = (long)slot.LevelMax + alphaLevelBonus;
            if (alphaLevelMaximum <= 100)
            {
                continue;
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Shared Alpha level range is invalid: base maximum {slot.LevelMax.ToString(CultureInfo.InvariantCulture)} "
                + $"plus bonus {alphaLevelBonus.ToString(CultureInfo.InvariantCulture)} would produce level {alphaLevelMaximum.ToString(CultureInfo.InvariantCulture)} "
                + "for every linked placement.",
                ZaEditSessionSupport.EncountersDomain,
                field: ZaEncountersWorkflowService.AlphaLevelBonusField,
                expected: "When shared Alpha chance is above 0 percent, base maximum level plus Alpha level bonus must be at most 100"));
        }
    }

    private static void ValidateFinalSpawnerCounts(
        ZaEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targets = edits
            .Where(edit => AffectsSpawnerData(edit.Field))
            .Select(edit => TryResolveSpawnerTableId(workflow, edit.RecordId, out var tableId)
                ? (TableId: tableId, edit.Field, edit.RecordId)
                : (TableId: string.Empty, edit.Field, edit.RecordId))
            .Where(target => !string.IsNullOrWhiteSpace(target.TableId))
            .GroupBy(target => target.TableId, StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var table = workflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, target.Key, StringComparison.Ordinal));
            if (table is null)
            {
                continue;
            }

            var validatesSlotCounts = target.Any(candidate =>
                candidate.Field is ZaEncountersWorkflowService.SlotMaxCountField);
            var validatesAppearanceCounts = target.Any(candidate =>
                candidate.Field is ZaEncountersWorkflowService.AppearanceMinCountField
                    or ZaEncountersWorkflowService.AppearanceMaxCountField);
            var validatesCounts = validatesSlotCounts || validatesAppearanceCounts;
            var validatesWeights = target.Any(candidate =>
                candidate.Field is ZaEncountersWorkflowService.WeightField);
            var changedSlots = target
                .Where(candidate =>
                    candidate.Field is ZaEncountersWorkflowService.WeightField
                        or ZaEncountersWorkflowService.SlotMaxCountField)
                .Select(candidate => TryResolveSpawnerSlot(
                    workflow,
                    candidate.RecordId,
                    out _,
                    out var slot)
                        ? slot
                        : null)
                .OfType<ZaEncounterSlotRecord>()
                .DistinctBy(slot => slot.Slot)
                .ToArray();

            if (validatesWeights && table.Slots.All(slot => slot.Weight == 0))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "Every slot in this spawner has weight 0, so no weighted candidate may be selectable.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.WeightField,
                    expected: "At least one positive slot weight when the spawner should remain active"));
            }

            if (validatesSlotCounts)
            {
                var highSlotCount = table.Slots
                    .Where(slot => slot.SlotMaxCount > 6)
                    .Select(slot => slot.SlotMaxCount)
                    .DefaultIfEmpty()
                    .Max();
                if (highSlotCount > 6)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Slot maximum count {highSlotCount.ToString(CultureInfo.InvariantCulture)} exceeds the highest slot count observed in vanilla data (6). "
                        + "KM Editor will preserve the requested raw value.",
                        ZaEditSessionSupport.EncountersDomain,
                        field: ZaEncountersWorkflowService.SlotMaxCountField,
                    expected: "Counts through 6 match the vanilla-observed range"));
                }
            }

            foreach (var slot in changedSlots)
            {
                var displayedSlot = slot.Slot + 1;
                if (slot.Weight == 0 && slot.SlotMaxCount > 0)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Slot {displayedSlot.ToString(CultureInfo.InvariantCulture)} has weight 0 but a positive maximum count, "
                        + "so it normally will not be selected.",
                        ZaEditSessionSupport.EncountersDomain,
                        field: ZaEncountersWorkflowService.WeightField,
                        expected: "Positive weight when a slot should contribute spawns"));
                }

                if (slot.Weight > 0 && slot.SlotMaxCount == 0)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Slot {displayedSlot.ToString(CultureInfo.InvariantCulture)} has a positive weight but maximum count 0, "
                        + "so it may not contribute a spawn.",
                        ZaEditSessionSupport.EncountersDomain,
                        field: ZaEncountersWorkflowService.SlotMaxCountField,
                        expected: "Positive slot maximum count when a weighted slot should contribute spawns"));
                }
            }

            var firstSlot = table.Slots.FirstOrDefault();
            if (!validatesCounts
                || firstSlot?.AppearanceMinCount is not int minimum
                || firstSlot.AppearanceMaxCount is not int maximum)
            {
                continue;
            }

            if (minimum > maximum)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Overall encounter count range is invalid: minimum {minimum.ToString(CultureInfo.InvariantCulture)} "
                    + $"is greater than maximum {maximum.ToString(CultureInfo.InvariantCulture)}.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.AppearanceMinCountField,
                    expected: "Overall minimum count less than or equal to overall maximum count"));
            }

            if (validatesAppearanceCounts && (minimum > 6 || maximum > 6))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Overall encounter count range {minimum.ToString(CultureInfo.InvariantCulture)} through "
                    + $"{maximum.ToString(CultureInfo.InvariantCulture)} exceeds the highest per-appearance count observed in vanilla data (6). "
                    + "KM Editor will preserve the requested raw values.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.AppearanceMaxCountField,
                    expected: "Counts through 6 match the vanilla-observed range"));
            }

            if (maximum == 0)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "Overall maximum count is 0, so this spawner may not create any Pokemon.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.AppearanceMaxCountField,
                    expected: "Positive overall maximum count when the spawner should remain active"));
            }

            var capacityCandidates = validatesAppearanceCounts
                ? table.Slots
                : changedSlots;
            var slotAboveOverallMaximum = capacityCandidates
                .Where(slot => slot.SlotMaxCount > maximum)
                .OrderByDescending(slot => slot.SlotMaxCount)
                .FirstOrDefault();
            if (slotAboveOverallMaximum is not null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Slot {(slotAboveOverallMaximum.Slot + 1).ToString(CultureInfo.InvariantCulture)} maximum count "
                    + $"{slotAboveOverallMaximum.SlotMaxCount.ToString(CultureInfo.InvariantCulture)} is above the overall maximum "
                    + $"{maximum.ToString(CultureInfo.InvariantCulture)}. The overall population cap is reached first.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.SlotMaxCountField,
                    expected: "Slot maximum count no greater than the overall maximum count when both caps should be reachable"));
            }

            var totalSlotCapacity = table.Slots.Sum(slot => (long)slot.SlotMaxCount);
            if (totalSlotCapacity < minimum)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Combined slot capacity {totalSlotCapacity.ToString(CultureInfo.InvariantCulture)} "
                    + $"is below overall minimum count {minimum.ToString(CultureInfo.InvariantCulture)}. "
                    + "KM Editor will preserve the requested raw values.",
                    ZaEditSessionSupport.EncountersDomain,
                    field: ZaEncountersWorkflowService.SlotMaxCountField,
                    expected: "Combined slot maximum counts at least as large as the overall minimum count"));
            }
        }
    }

    private static void ValidateFinalSpeciesFormPairs(
        ZaEncountersWorkflow sourceWorkflow,
        ZaEncountersWorkflow projectedWorkflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var projectedSlotsBySourceIndex = projectedWorkflow.Tables
            .SelectMany(table => table.Slots)
            .Where(slot => slot.PokemonDataSourceIndex >= 0)
            .GroupBy(slot => slot.PokemonDataSourceIndex)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var sourceGroup in sourceWorkflow.Tables
                     .SelectMany(table => table.Slots)
                     .Where(slot => slot.PokemonDataSourceIndex >= 0)
                     .GroupBy(slot => slot.PokemonDataSourceIndex))
        {
            var sourceSlot = sourceGroup.First();
            if (!projectedSlotsBySourceIndex.TryGetValue(
                    sourceSlot.PokemonDataSourceIndex,
                    out var projectedSlot))
            {
                continue;
            }

            ZaSpeciesFormPairValidation.ValidateChangedPair(
                sourceWorkflow.PokemonAvailability,
                sourceSlot.SpeciesId,
                sourceSlot.Form,
                projectedSlot.SpeciesId,
                projectedSlot.Form,
                ZaEditSessionSupport.EncountersDomain,
                $"Encounter data row '{sourceSlot.EncounterDataId}'",
                diagnostics,
                sourceSlot.PokemonProvenance.SourceFile,
                ZaEncountersWorkflowService.FormField);
        }
    }

    private static ZaEncountersWorkflow OverlayPendingEdits(
        ZaEncountersWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static ZaEncountersWorkflow OverlayPendingEdit(ZaEncountersWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.EncountersDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        if (AffectsSharedPokemonData(edit.Field)
            && TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out var sourceIndex))
        {
            return workflow with
            {
                Tables = workflow.Tables
                    .Select(table => table with
                    {
                        Slots = table.Slots
                            .Select(row => row.PokemonDataSourceIndex == sourceIndex
                                ? OverlaySlot(workflow, row, edit.Field, value)
                                : row)
                            .ToArray(),
                    })
                    .ToArray(),
            };
        }

        if (AffectsSpawnerSlot(edit.Field)
            && TryResolveSpawnerSlot(workflow, edit.RecordId, out var targetTable, out var targetSlot))
        {
            return workflow with
            {
                Tables = workflow.Tables
                    .Select(table => string.Equals(table.TableId, targetTable.TableId, StringComparison.Ordinal)
                        ? table with
                        {
                            Slots = table.Slots
                                .Select(slot => slot.Slot == targetSlot.Slot
                                    ? OverlaySlot(workflow, slot, edit.Field, value)
                                    : slot)
                                .ToArray(),
                        }
                        : table)
                    .ToArray(),
            };
        }

        if (!AffectsAppearanceCounts(edit.Field)
            || !TryResolveAppearanceTable(workflow, edit.RecordId, out var appearanceTable))
        {
            return workflow;
        }

        return workflow with
        {
            Tables = workflow.Tables
                .Select(table => string.Equals(table.TableId, appearanceTable.TableId, StringComparison.Ordinal)
                    ? table with
                    {
                        Slots = table.Slots
                            .Select(slot => OverlaySlot(workflow, slot, edit.Field, value))
                            .ToArray(),
                    }
                    : table)
                .ToArray(),
        };
    }

    private static ZaEncounterSlotRecord OverlaySlot(
        ZaEncountersWorkflow workflow,
        ZaEncounterSlotRecord slot,
        string? field,
        int value)
    {
        var updatedSlot = field switch
        {
            ZaEncountersWorkflowService.SpeciesIdField => slot with
            {
                SpeciesId = value,
                Species = ZaEncountersWorkflowService.FormatEncounterSpeciesLabel(
                    value,
                    slot.Form,
                    ResolveSpeciesName(workflow, value)),
            },
            ZaEncountersWorkflowService.FormField => slot with
            {
                Form = value,
                Species = ZaEncountersWorkflowService.FormatEncounterSpeciesLabel(
                    slot.SpeciesId,
                    value,
                    ResolveSpeciesName(workflow, slot.SpeciesId)),
            },
            ZaEncountersWorkflowService.LevelMinField => slot with { LevelMin = value },
            ZaEncountersWorkflowService.LevelMaxField => slot with { LevelMax = value },
            ZaEncountersWorkflowService.AlphaChancePercentField => slot with
            {
                AlphaChancePercent = value,
                HasAlphaChance = value > 0,
                EncounterKind = value switch
                {
                    100 => "Guaranteed Alpha",
                    > 0 => "Alpha Chance",
                    _ => "Wild",
                },
            },
            ZaEncountersWorkflowService.AlphaLevelBonusField => slot with { AlphaLevelBonus = value },
            ZaEncountersWorkflowService.WeightField => slot with { Weight = value },
            ZaEncountersWorkflowService.SlotMaxCountField => slot with { SlotMaxCount = value },
            ZaEncountersWorkflowService.AppearanceMinCountField => slot with { AppearanceMinCount = value },
            ZaEncountersWorkflowService.AppearanceMaxCountField => slot with { AppearanceMaxCount = value },
            _ => slot,
        };

        return field is ZaEncountersWorkflowService.SpeciesIdField
            or ZaEncountersWorkflowService.FormField
            ? updatedSlot with
            {
                FormOptions = ZaEncountersWorkflowService.CreateFormOptions(
                    updatedSlot.SpeciesId,
                    ResolveSpeciesName(workflow, updatedSlot.SpeciesId),
                    workflow.PokemonAvailability),
            }
            : updatedSlot;
    }

    private static string ResolveSpeciesName(ZaEncountersWorkflow workflow, int speciesId)
    {
        if (speciesId == 0)
        {
            return "Empty";
        }

        var speciesField = workflow.EditableFields.FirstOrDefault(field =>
            string.Equals(field.Field, ZaEncountersWorkflowService.SpeciesIdField, StringComparison.Ordinal));
        var option = speciesField?.Options.FirstOrDefault(candidate => candidate.Value == speciesId);
        if (option is null)
        {
            return ZaLabels.Pokemon(speciesId);
        }

        var prefix = speciesId.ToString(CultureInfo.InvariantCulture);
        return option.Label.StartsWith(prefix, StringComparison.Ordinal)
            ? option.Label[prefix.Length..].Trim()
            : option.Label;
    }

    private static void ApplySpawnerEdit(
        ZaEncountersWorkflow workflow,
        ZaPokemonSpawnerDataDocument document,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.EncountersDomain, StringComparison.Ordinal)
            || !AffectsSpawnerData(edit.Field)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending spawner edit is not valid for apply.",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Valid Pokemon Legends Z-A spawner edit"));
            return;
        }

        if (AffectsSpawnerSlot(edit.Field))
        {
            if (!TryResolveSpawnerSlot(workflow, edit.RecordId, out var table, out var slot)
                || !ZaEncountersWorkflowService.TryParseTableId(
                    table.TableId,
                    out var groupIndex,
                    out var spawnerIndex))
            {
                diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
                return;
            }

            var entry = document.Entries.FirstOrDefault(candidate =>
                candidate.GroupIndex == groupIndex && candidate.SpawnerIndex == spawnerIndex);
            var sourceSlot = entry?.EncountDataInfoList.FirstOrDefault(candidate =>
                candidate is not null && candidate.SlotIndex == slot.Slot);
            if (sourceSlot is null)
            {
                diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
                return;
            }

            bool changed;
            string? error;
            switch (edit.Field)
            {
                case ZaEncountersWorkflowService.WeightField:
                    if (sourceSlot.Weight == value)
                    {
                        return;
                    }

                    changed = document.TrySetSlotWeight(
                        groupIndex,
                        spawnerIndex,
                        slot.Slot,
                        value,
                        out error);
                    break;
                case ZaEncountersWorkflowService.SlotMaxCountField:
                    if (sourceSlot.MaxCount == value)
                    {
                        return;
                    }

                    changed = document.TrySetSlotMaxCount(
                        groupIndex,
                        spawnerIndex,
                        slot.Slot,
                        value,
                        out error);
                    break;
                default:
                    diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
                    return;
            }

            if (!changed)
            {
                diagnostics.Add(CreateSpawnerPatchDiagnostic(edit.Field, error));
            }

            return;
        }

        if (!TryResolveAppearanceTable(workflow, edit.RecordId, out var appearanceTable)
            || !ZaEncountersWorkflowService.TryParseTableId(
                appearanceTable.TableId,
                out var appearanceGroupIndex,
                out var appearanceSpawnerIndex))
        {
            diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
            return;
        }

        var spawnerEntry = document.Entries.FirstOrDefault(candidate =>
            candidate.GroupIndex == appearanceGroupIndex
            && candidate.SpawnerIndex == appearanceSpawnerIndex);
        if (spawnerEntry is null || spawnerEntry.AppearanceSpawnerObjectInfoList.Count == 0)
        {
            diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
            return;
        }

        foreach (var appearance in spawnerEntry.AppearanceSpawnerObjectInfoList)
        {
            if (appearance?.AppearanceInfo is null)
            {
                diagnostics.Add(CreateMissingSpawnerTargetDiagnostic(edit.Field));
                return;
            }

            bool changed;
            string? error;
            switch (edit.Field)
            {
                case ZaEncountersWorkflowService.AppearanceMinCountField:
                    if (appearance.AppearanceInfo.MinCount == value)
                    {
                        continue;
                    }

                    changed = document.TrySetAppearanceMinCount(
                        appearanceGroupIndex,
                        appearanceSpawnerIndex,
                        appearance.AppearanceIndex,
                        value,
                        out error);
                    break;
                case ZaEncountersWorkflowService.AppearanceMaxCountField:
                    if (appearance.AppearanceInfo.MaxCount == value)
                    {
                        continue;
                    }

                    changed = document.TrySetAppearanceMaxCount(
                        appearanceGroupIndex,
                        appearanceSpawnerIndex,
                        appearance.AppearanceIndex,
                        value,
                        out error);
                    break;
                default:
                    diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
                    return;
            }

            if (!changed)
            {
                diagnostics.Add(CreateSpawnerPatchDiagnostic(edit.Field, error));
                return;
            }
        }
    }

    private static ValidationDiagnostic CreateMissingSpawnerTargetDiagnostic(string? field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pending encounter edit target is not present in the source spawner data array.",
            ZaEditSessionSupport.EncountersDomain,
            field: field,
            expected: "Existing spawner slot or appearance object");
    }

    private static ValidationDiagnostic CreateSpawnerPatchDiagnostic(string? field, string? error)
    {
        var detail = string.IsNullOrWhiteSpace(error)
            ? "The source scalar is not safely editable."
            : error.Trim();
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Spawner field '{field}' could not be changed. {detail}",
            ZaEditSessionSupport.EncountersDomain,
            field: field,
            expected: "Materialized 32-bit spawner scalar in the source data");
    }

    private static void ApplyEdit(
        ZaEncountersWorkflow workflow,
        ZaEncounterDataDocument document,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.EncountersDomain, StringComparison.Ordinal)
            || !TryResolvePokemonDataSourceIndex(workflow, edit.RecordId, out var sourceIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit is not valid for apply.",
                ZaEditSessionSupport.EncountersDomain,
                expected: "Valid Pokemon Legends Z-A encounter edit"));
            return;
        }

        var row = document.Entries.FirstOrDefault(candidate => candidate.SourceIndex == sourceIndex);
        if (row is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit target is not present in the source encounter data array.",
                ZaEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing linked encounter data row"));
            return;
        }

        ApplyField(row, edit.Field, value);
    }

    private static bool TryResolveSpawnerSlot(
        ZaEncountersWorkflow workflow,
        string? recordId,
        out ZaEncounterTableRecord table,
        out ZaEncounterSlotRecord slot)
    {
        table = null!;
        slot = null!;
        if (!ZaEncountersWorkflowService.TryParseSlotRecordId(recordId, out var tableId, out var slotIndex))
        {
            return false;
        }

        var resolvedTable = workflow.Tables.FirstOrDefault(candidate =>
            string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
        var resolvedSlot = resolvedTable?.Slots.FirstOrDefault(candidate => candidate.Slot == slotIndex);
        if (resolvedTable is null || resolvedSlot is null)
        {
            return false;
        }

        table = resolvedTable;
        slot = resolvedSlot;
        return true;
    }

    private static bool TryResolveAppearanceTable(
        ZaEncountersWorkflow workflow,
        string? recordId,
        out ZaEncounterTableRecord table)
    {
        table = null!;
        if (!ZaEncountersWorkflowService.TryParseAppearanceRecordId(recordId, out var tableId))
        {
            return false;
        }

        var resolvedTable = workflow.Tables.FirstOrDefault(candidate =>
            string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
        if (resolvedTable is null)
        {
            return false;
        }

        table = resolvedTable;
        return true;
    }

    private static bool TryResolveSpawnerTableId(
        ZaEncountersWorkflow workflow,
        string? recordId,
        out string tableId)
    {
        if (TryResolveSpawnerSlot(workflow, recordId, out var slotTable, out _))
        {
            tableId = slotTable.TableId;
            return true;
        }

        if (TryResolveAppearanceTable(workflow, recordId, out var appearanceTable))
        {
            tableId = appearanceTable.TableId;
            return true;
        }

        tableId = string.Empty;
        return false;
    }

    private static bool TryResolvePokemonDataSourceIndex(
        ZaEncountersWorkflow workflow,
        string? recordId,
        out int sourceIndex)
    {
        if (ZaEncountersWorkflowService.TryParsePokemonDataRecordId(recordId, out sourceIndex))
        {
            var resolvedSourceIndex = sourceIndex;
            return workflow.Tables
                .SelectMany(table => table.Slots)
                .Any(slot => slot.PokemonDataSourceIndex == resolvedSourceIndex);
        }

        if (ZaEncountersWorkflowService.TryParseSlotRecordId(recordId, out var tableId, out var slot))
        {
            sourceIndex = workflow.Tables
                .FirstOrDefault(candidate => string.Equals(candidate.TableId, tableId, StringComparison.Ordinal))
                ?.Slots
                .FirstOrDefault(candidate => candidate.Slot == slot)
                ?.PokemonDataSourceIndex ?? -1;
            return sourceIndex >= 0;
        }

        sourceIndex = -1;
        return false;
    }

    private static void ApplyField(
        ZaPokemonDataEntry row,
        string? field,
        int value)
    {
        switch (field)
        {
            case ZaEncountersWorkflowService.SpeciesIdField:
                row.DevNo = value;
                break;
            case ZaEncountersWorkflowService.FormField:
                row.FormNo = value;
                break;
            case ZaEncountersWorkflowService.LevelMinField:
                row.MinLevel = value;
                break;
            case ZaEncountersWorkflowService.LevelMaxField:
                row.MaxLevel = value;
                break;
            case ZaEncountersWorkflowService.AlphaChancePercentField:
                row.OyabunProbability = value;
                break;
            case ZaEncountersWorkflowService.AlphaLevelBonusField:
                row.OyabunAdditionalLevel = value;
                break;
        }
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Encounter field '{field}' is not supported by Pokemon Legends Z-A Wild Encounters yet.",
            ZaEditSessionSupport.EncountersDomain,
            field: "field",
            expected: "speciesId, form, levelMin, levelMax, alphaChancePercent, alphaLevelBonus, weight, slotMaxCount, appearanceMinCount, or appearanceMaxCount");
    }

    private static string CreateSummary(
        ZaEncounterTableRecord table,
        ZaEncounterSlotRecord slot,
        ZaEncounterEditableField field,
        int value)
    {
        return field.Field switch
        {
            ZaEncountersWorkflowService.SpeciesIdField =>
                $"Set {table.Location} slot {slot.Slot} species ID to {value}.",
            ZaEncountersWorkflowService.FormField =>
                $"Set {table.Location} slot {slot.Slot} form to {value}.",
            ZaEncountersWorkflowService.LevelMinField =>
                $"Set {table.Location} slot {slot.Slot} minimum level to {value}.",
            ZaEncountersWorkflowService.LevelMaxField =>
                $"Set {table.Location} slot {slot.Slot} maximum level to {value}.",
            ZaEncountersWorkflowService.AlphaChancePercentField =>
                $"Set the shared Alpha chance to {value} percent for every placement linked to {slot.EncounterRecordId}.",
            ZaEncountersWorkflowService.AlphaLevelBonusField =>
                $"Set the shared Alpha level bonus to +{value} for every placement linked to {slot.EncounterRecordId}.",
            ZaEncountersWorkflowService.WeightField =>
                $"Set {table.Location} slot {slot.Slot + 1} weight to {value}.",
            ZaEncountersWorkflowService.SlotMaxCountField =>
                $"Set {table.Location} slot {slot.Slot + 1} maximum count to {value}.",
            ZaEncountersWorkflowService.AppearanceMinCountField =>
                $"Set {table.Location} overall minimum count to {value} for every appearance object.",
            ZaEncountersWorkflowService.AppearanceMaxCountField =>
                $"Set {table.Location} overall maximum count to {value} for every appearance object.",
            _ => $"Set {table.Location} slot {slot.Slot + 1} {field.Label.ToLowerInvariant()} to {value}.",
        };
    }
}
