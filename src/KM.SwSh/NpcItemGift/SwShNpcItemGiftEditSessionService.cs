// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Scripts;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.NpcItemGift;

public sealed class SwShNpcItemGiftEditSessionService
{
    public const string NpcItemGiftEditDomain = SwShNpcItemGiftWorkflowService.NpcItemGiftEditDomain;

    private const string RecordId = "npc-item-gift";
    private const string GiftsField = "gifts";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShNpcItemGiftWorkflowService npcItemGiftWorkflowService;

    public SwShNpcItemGiftEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShNpcItemGiftWorkflowService? npcItemGiftWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.npcItemGiftWorkflowService = npcItemGiftWorkflowService ?? new SwShNpcItemGiftWorkflowService();
    }

    public SwShNpcItemGiftEditResult StageGifts(
        ProjectPaths paths,
        IReadOnlyList<SwShNpcItemGiftSelection> selections,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(selections);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = npcItemGiftWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.Add(CreateWrongGameDiagnostic());
            return new SwShNpcItemGiftEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, NpcItemGiftEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift needs its own edit session before staging.",
                expected: "An NPC Item Gift-only edit session"));
            return new SwShNpcItemGiftEditResult(workflow, currentSession, diagnostics);
        }

        var game = paths.SelectedGame!.Value;
        var normalizedSelections = NormalizeSelections(game, workflow, selections, diagnostics);
        var changedFileGroups = CreateChangedFileGroups(workflow, normalizedSelections).ToArray();
        if (!CanStage(project, workflow, changedFileGroups, diagnostics)
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShNpcItemGiftEditResult(workflow, currentSession, diagnostics);
        }

        if (changedFileGroups.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift has no changed or repairable gifts to stage.",
                field: GiftsField,
                expected: "At least one changed or repairable gift"));
            return new SwShNpcItemGiftEditResult(workflow, currentSession, diagnostics);
        }

        var payload = EncodeSelections(normalizedSelections);
        var sourceReferences = npcItemGiftWorkflowService
            .GetPlanSources(project, changedFileGroups.Select(group => group.RelativePath))
            .Append(CreatePendingPayloadSource(payload))
            .ToArray();
        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, NpcItemGiftEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(payload, sourceReferences))
                .ToArray(),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "NPC Item Gift changes are staged for change-plan review."));

        return new SwShNpcItemGiftEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = npcItemGiftWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.Add(CreateWrongGameDiagnostic());
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage NPC Item Gift changes before validating.",
                expected: "Pending NPC Item Gift edit"));
            return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift expects exactly one staged NPC edit.",
                expected: "One pending NPC Item Gift edit"));
        }

        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, NpcItemGiftEditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by NPC Item Gift.",
                    expected: NpcItemGiftEditDomain));
                continue;
            }

            if (!string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending NPC Item Gift edit '{edit.RecordId}' is not supported.",
                    expected: "NPC Item Gift selections"));
                continue;
            }

            if (!string.Equals(edit.Field, GiftsField, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending NPC Item Gift field '{edit.Field}' is not supported.",
                    field: edit.Field,
                    expected: GiftsField));
                continue;
            }

            var selections = DecodeSelections(paths.SelectedGame.GetValueOrDefault(), workflow, edit.NewValue, diagnostics);
            var canonicalPayload = EncodeSelections(selections);
            if (!string.Equals(edit.NewValue, canonicalPayload, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending NPC Item Gift selections are not in the canonical staged format.",
                    field: GiftsField,
                    expected: "Unique ordered selections produced by NPC Item Gift staging"));
            }

            var changedFileGroups = CreateChangedFileGroups(workflow, selections).ToArray();
            CanStage(project, workflow, changedFileGroups, diagnostics);
            if (changedFileGroups.Length == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending NPC Item Gift selections contain no changed or repairable gifts.",
                    field: GiftsField,
                    expected: "At least one changed or repairable gift"));
            }
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending NPC Item Gift change is valid for change-plan review."));
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = npcItemGiftWorkflowService.Load(project);
        var canonicalPayload = session.PendingEdits.Single().NewValue ?? string.Empty;
        var selections = DecodeSelections(paths.SelectedGame!.Value, workflow, canonicalPayload, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var fileGroups = CreateChangedFileGroups(workflow, selections).ToArray();
        if (fileGroups.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "NPC Item Gift has no changed item gifts to write."));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        foreach (var fileGroup in fileGroups.OrderBy(group => group.RelativePath, StringComparer.Ordinal))
        {
            var source = SwShNpcItemGiftWorkflowService.ResolveWorkflowFile(project, fileGroup.RelativePath);
            var targetPath = ResolveOutputPath(paths, fileGroup.RelativePath, diagnostics);
            if (source is null || targetPath is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "NPC Item Gift source or output target could not be resolved.",
                    file: fileGroup.RelativePath,
                    expected: "Readable AMX source and writable LayeredFS target"));
                continue;
            }

            writes.Add(new PlannedFileWrite(
                fileGroup.RelativePath,
                npcItemGiftWorkflowService
                    .GetPlanSources(project, [fileGroup.RelativePath])
                    .Append(CreatePendingPayloadSource(canonicalPayload))
                    .Distinct()
                    .OrderBy(reference => reference.Layer)
                    .ThenBy(reference => reference.RelativePath, StringComparer.Ordinal)
                    .ToArray(),
                File.Exists(targetPath),
                "Update reviewed NPC item gift operands in the AMX script while preserving unrelated cells."));
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"NPC Item Gift change plan preview contains {writes.Count:N0} target file(s)."));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        projectWorkspaceService.ClearMemoryCache();
        try
        {
            return ApplyChangePlanCore(paths, session, reviewedPlan);
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
        }
    }

    private ApplyResult ApplyChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed NPC Item Gift change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed NPC Item Gift change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                currentPlan,
                out var applyScope,
                out var acquireDiagnostics))
        {
            return CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                acquireDiagnostics);
        }

        var verifiedScope = applyScope!;
        using (verifiedScope)
        {
            var snapshotPlan = CreateChangePlan(verifiedScope.ApplyPaths, session);
            if (!verifiedScope.TryPrepareSnapshotPlan(snapshotPlan, out var preparedPlan))
            {
                var staleDiagnostics = preparedPlan.Diagnostics.ToList();
                staleDiagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "NPC Item Gift sources changed while preparing the verified apply snapshot.",
                    expected: "Sources matching the reviewed change plan"));
                return CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    staleDiagnostics);
            }

            var snapshotResult = ApplyPreparedPlan(
                verifiedScope.ApplyPaths,
                session,
                preparedPlan,
                applyId,
                appliedAt);
            return verifiedScope.Commit(snapshotResult);
        }
    }

    private ApplyResult ApplyPreparedPlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan preparedPlan,
        string applyId,
        DateTimeOffset appliedAt)
    {
        projectWorkspaceService.ClearMemoryCache();
        var diagnostics = preparedPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        var project = projectWorkspaceService.Open(paths);
        var workflow = npcItemGiftWorkflowService.Load(project);
        var selections = DecodeSelections(
            paths.SelectedGame!.Value,
            workflow,
            session.PendingEdits.Single().NewValue,
            diagnostics);
        var fileGroups = CreateChangedFileGroups(workflow, selections)
            .OrderBy(group => group.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var preparedOutputs = new List<PreparedNpcItemGiftOutput>(fileGroups.Length);

        foreach (var fileGroup in fileGroups)
        {
            var prepared = PrepareFileGroup(project, paths, fileGroup, diagnostics);
            if (prepared is not null)
            {
                preparedOutputs.Add(prepared);
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        foreach (var prepared in preparedOutputs)
        {
            try
            {
                WriteOutputAtomically(
                    prepared.TargetPath,
                    prepared.Output,
                    roundTrip => VerifyPatchedOutput(prepared.FileGroup, roundTrip));
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, prepared.FileGroup.RelativePath));
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    $"Applied NPC Item Gift changes to {prepared.FileGroup.RelativePath}.",
                    file: prepared.FileGroup.RelativePath));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"NPC Item Gift verified output could not be written: {exception.Message}",
                    file: prepared.FileGroup.RelativePath,
                    expected: "Writable output and reviewed AMX operands"));
                break;
            }
        }

        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
    }

    private static PreparedNpcItemGiftOutput? PrepareFileGroup(
        OpenedProject project,
        ProjectPaths paths,
        SwShNpcItemGiftFileGroup fileGroup,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShNpcItemGiftWorkflowService.ResolveWorkflowFile(project, fileGroup.RelativePath);
        var targetPath = ResolveOutputPath(paths, fileGroup.RelativePath, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift source or output target could not be resolved.",
                file: fileGroup.RelativePath,
                expected: "Readable AMX source and writable LayeredFS target"));
            return null;
        }

        try
        {
            var patches = CreateCellPatches(fileGroup, diagnostics);
            if (patches.Count == 0
                || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                if (patches.Count == 0)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "NPC Item Gift did not produce any reviewed operand patches.",
                        file: fileGroup.RelativePath,
                        expected: "At least one changed or repairable operand"));
                }

                return null;
            }

            var output = SwShAmxCellPatcher.ApplyCodeCellPatches(
                File.ReadAllBytes(source.AbsolutePath),
                patches);
            VerifyPatchedOutput(fileGroup, output);
            return new PreparedNpcItemGiftOutput(fileGroup, targetPath, output);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"NPC Item Gift source file could not be patched safely: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Reviewed Sword/Shield AMX packed operands"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"NPC Item Gift source file could not be read: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Readable Sword/Shield AMX script file"));
        }

        return null;
    }

    private static void VerifyPatchedOutput(
        SwShNpcItemGiftFileGroup fileGroup,
        byte[] output)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var patch in CreateCellPatches(fileGroup, diagnostics))
        {
            var actual = SwShAmxCellPatcher.ReadPackedCodeCellInt(output, patch.Cell);
            if (actual != patch.Value)
            {
                throw new InvalidDataException(
                    $"AMX code cell {patch.Cell} round-tripped as {actual} instead of {patch.Value}.");
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidDataException("Reviewed NPC Item Gift operand patches conflict.");
        }
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("NPC Item Gift output directory could not be resolved.");
        Directory.CreateDirectory(directoryPath);
        var temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, output);
            var roundTrip = File.ReadAllBytes(temporaryPath);
            if (!roundTrip.AsSpan().SequenceEqual(output))
            {
                throw new IOException("NPC Item Gift temporary output did not round-trip byte-for-byte.");
            }

            verifyRoundTrip(roundTrip);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static IReadOnlyList<SwShAmxCellPatch> CreateCellPatches(
        SwShNpcItemGiftFileGroup fileGroup,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var patches = new Dictionary<int, int>();
        foreach (var selectionPatch in fileGroup.Selections)
        {
            var repairAllOwnedOperands = string.Equals(
                selectionPatch.Current.Status,
                "repairable",
                StringComparison.Ordinal);
            if (selectionPatch.Definition.CanEditQuantity
                && selectionPatch.Definition.QuantityCell is int quantityCell
                && (repairAllOwnedOperands
                    || selectionPatch.Current.Quantity != selectionPatch.Selection.Quantity))
            {
                AddPatch(quantityCell, selectionPatch.Selection.Quantity);
                foreach (var companionCell in selectionPatch.Definition.CompanionQuantityCells)
                {
                    AddPatch(companionCell, selectionPatch.Selection.Quantity);
                }
            }

            foreach (var slot in selectionPatch.Definition.Items)
            {
                var selectedItem = selectionPatch.Selection.Items
                    .First(item => string.Equals(item.SlotId, slot.SlotId, StringComparison.Ordinal));
                var currentItem = selectionPatch.Current.Items
                    .First(item => string.Equals(item.SlotId, slot.SlotId, StringComparison.Ordinal));
                if (!repairAllOwnedOperands && currentItem.ItemId == selectedItem.ItemId)
                {
                    continue;
                }

                AddPatch(slot.ItemCell, selectedItem.ItemId);
                foreach (var companionCell in slot.CompanionItemCells)
                {
                    AddPatch(companionCell, selectedItem.ItemId);
                }
            }
        }

        return patches
            .OrderBy(pair => pair.Key)
            .Select(pair => new SwShAmxCellPatch(
                pair.Key,
                pair.Value,
                RequirePackedConstantOperand: true))
            .ToArray();

        void AddPatch(int cell, int value)
        {
            if (patches.TryGetValue(cell, out var existing) && existing != value)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"NPC Item Gift has conflicting staged values for AMX cell {cell}.",
                    file: fileGroup.RelativePath,
                    expected: "One value per AMX cell"));
                return;
            }

            patches[cell] = value;
        }
    }

    private static bool CanStage(
        OpenedProject project,
        SwShNpcItemGiftWorkflow workflow,
        IReadOnlyList<SwShNpcItemGiftFileGroup> fileGroups,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift apply requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        var changedPaths = fileGroups
            .Select(group => group.RelativePath)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && (string.IsNullOrWhiteSpace(diagnostic.File) || changedPaths.Contains(diagnostic.File))))
        {
            diagnostics.Add(diagnostic);
        }

        var changesAnItem = fileGroups
            .SelectMany(group => group.Selections)
            .Any(selectionPatch => selectionPatch.Selection.Items.Any(selected =>
                selectionPatch.Current.Items.Any(current =>
                    string.Equals(current.SlotId, selected.SlotId, StringComparison.Ordinal)
                    && current.ItemId != selected.ItemId)));
        if (changesAnItem && workflow.ItemOptions.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift needs item options before gifts can be staged.",
                expected: "Readable item table"));
        }

        foreach (var selectionPatch in fileGroups.SelectMany(group => group.Selections))
        {
            if (selectionPatch.Current.Status is not ("available" or "repairable"))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{selectionPatch.Current.Label} cannot be staged while its mapped operands are {selectionPatch.Current.Status}.",
                    file: selectionPatch.Current.RelativePath,
                    expected: "Available or safely repairable mapped gift operands"));
            }
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static IReadOnlyList<SwShNpcItemGiftSelection> NormalizeSelections(
        ProjectGame game,
        SwShNpcItemGiftWorkflow workflow,
        IReadOnlyList<SwShNpcItemGiftSelection> selections,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemOptions = workflow.ItemOptions.ToDictionary(item => item.ItemId);
        var currentByGiftId = workflow.Npcs
            .SelectMany(npc => npc.Gifts)
            .ToDictionary(gift => gift.GiftId, StringComparer.Ordinal);
        var byGiftId = new Dictionary<string, SwShNpcItemGiftSelection>(StringComparer.Ordinal);
        string? npcId = null;

        foreach (var selection in selections)
        {
            if (selection is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "NPC Item Gift selection is missing.",
                    field: GiftsField,
                    expected: "A complete gift selection"));
                continue;
            }

            var normalized = NormalizeSelection(
                game,
                itemOptions,
                currentByGiftId,
                selection,
                diagnostics);
            if (normalized is null)
            {
                continue;
            }

            var definition = SwShNpcItemGiftWorkflowService.FindGift(normalized.GiftId, game)!;
            if (npcId is null)
            {
                npcId = definition.NpcId;
            }
            else if (!string.Equals(npcId, definition.NpcId, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "NPC Item Gift can only stage one NPC at a time.",
                    field: GiftsField,
                    expected: "Selections for one NPC"));
            }

            if (!byGiftId.TryAdd(normalized.GiftId, normalized))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"NPC Item Gift selection '{normalized.GiftId}' is duplicated.",
                    field: GiftsField,
                    expected: "One selection per gift"));
            }
        }

        if (byGiftId.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift needs at least one gift selection.",
                field: GiftsField,
                expected: "One or more gift selections"));
        }

        return byGiftId.Values
            .OrderBy(selection => SwShNpcItemGiftWorkflowService.FindGift(selection.GiftId, game)?.DisplayOrder ?? int.MaxValue)
            .ThenBy(selection => selection.GiftId, StringComparer.Ordinal)
            .ToArray();
    }

    private static SwShNpcItemGiftSelection? NormalizeSelection(
        ProjectGame game,
        IReadOnlyDictionary<int, SwShNpcItemGiftItemOptionRecord> itemOptions,
        IReadOnlyDictionary<string, SwShNpcItemGiftRecord> currentByGiftId,
        SwShNpcItemGiftSelection selection,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(selection.GiftId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift selection is missing a gift id.",
                field: GiftsField,
                expected: "Known gift id"));
            return null;
        }

        var definition = SwShNpcItemGiftWorkflowService.FindGift(selection.GiftId, game);
        if (definition is null || !currentByGiftId.TryGetValue(selection.GiftId, out var current))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"NPC Item Gift selection '{selection.GiftId}' is not recognized for {game}.",
                field: GiftsField,
                expected: "Known gift id"));
            return null;
        }

        var quantityChanged = selection.Quantity != current.Quantity;
        if (!definition.CanEditQuantity && quantityChanged)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{definition.Label} uses a fixed helper quantity and only its item can be edited.",
                field: GiftsField,
                expected: $"Quantity {current.Quantity.ToString(CultureInfo.InvariantCulture)}"));
        }
        else if (definition.CanEditQuantity && quantityChanged && selection.Quantity is < 1 or > 999)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{definition.Label} quantity must be between 1 and 999.",
                field: GiftsField,
                expected: "Quantity 1-999"));
        }

        var selectedItems = new Dictionary<string, SwShNpcItemGiftItemSelection>(StringComparer.Ordinal);
        if (selection.Items is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{definition.Label} is missing its item selections.",
                field: GiftsField,
                expected: "One item per mapped slot"));
            return null;
        }

        var currentItems = current.Items.ToDictionary(item => item.SlotId, StringComparer.Ordinal);
        foreach (var item in selection.Items)
        {
            if (item is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{definition.Label} contains a missing item selection.",
                    field: GiftsField,
                    expected: "One complete item selection per mapped slot"));
                continue;
            }

            if (!definition.Items.Any(slot => string.Equals(slot.SlotId, item.SlotId, StringComparison.Ordinal)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{definition.Label} item slot '{item.SlotId}' is not recognized.",
                    field: GiftsField,
                    expected: "Known gift item slot"));
                continue;
            }

            var itemChanged = currentItems.TryGetValue(item.SlotId, out var currentItem)
                && currentItem.ItemId != item.ItemId;
            if (itemChanged && !itemOptions.ContainsKey(item.ItemId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{definition.Label} item {item.ItemId} is not selectable for this project.",
                    field: GiftsField,
                    expected: "Selectable item id"));
            }

            if (!selectedItems.TryAdd(item.SlotId, item))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{definition.Label} item slot '{item.SlotId}' is duplicated.",
                    field: GiftsField,
                    expected: "One item per slot"));
            }
        }

        foreach (var slot in definition.Items)
        {
            if (!selectedItems.ContainsKey(slot.SlotId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{definition.Label} item slot '{slot.SlotId}' is missing.",
                    field: GiftsField,
                    expected: "One item per slot"));
            }
        }

        var orderedItems = definition.Items
            .Select(slot => selectedItems.TryGetValue(slot.SlotId, out var selected)
                ? selected
                : new SwShNpcItemGiftItemSelection(
                    slot.SlotId,
                    currentItems.TryGetValue(slot.SlotId, out var currentItem)
                        ? currentItem.ItemId
                        : slot.ItemId))
            .ToArray();
        var changesAnItem = orderedItems.Any(selected =>
            currentItems.TryGetValue(selected.SlotId, out var currentItem)
            && currentItem.ItemId != selected.ItemId);
        var normalizedQuantity = definition.CanEditQuantity
            ? selection.Quantity
            : current.Quantity;
        if (definition.CanEditQuantity
            && (quantityChanged || changesAnItem)
            && orderedItems.Any(item => itemOptions.TryGetValue(item.ItemId, out var option) && option.IsKeyItem))
        {
            normalizedQuantity = 1;
        }

        return new SwShNpcItemGiftSelection(
            definition.GiftId,
            normalizedQuantity,
            orderedItems);
    }

    private static string EncodeSelections(IReadOnlyList<SwShNpcItemGiftSelection> selections)
    {
        return string.Join(
            ';',
            selections.Select(selection =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{selection.GiftId}|{selection.Quantity}|{string.Join(',', selection.Items.Select(item => $"{item.SlotId}={item.ItemId.ToString(CultureInfo.InvariantCulture)}"))}")));
    }

    private static IReadOnlyList<SwShNpcItemGiftSelection> DecodeSelections(
        ProjectGame game,
        SwShNpcItemGiftWorkflow workflow,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift pending edit has no gift payload.",
                field: GiftsField,
                expected: "Encoded NPC Item Gift selections"));
            return [];
        }

        var selections = new List<SwShNpcItemGiftSelection>();
        foreach (var entry in value.Split(';', StringSplitOptions.None))
        {
            var parts = entry.Split('|');
            if (parts.Length != 3
                || string.IsNullOrEmpty(parts[0])
                || !TryParseCanonicalInt(parts[1], out var quantity)
                || string.IsNullOrEmpty(parts[2]))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "NPC Item Gift pending edit payload is malformed.",
                    field: GiftsField,
                    expected: "giftId|quantity|slotId=itemId entries"));
                continue;
            }

            var items = new List<SwShNpcItemGiftItemSelection>();
            foreach (var itemEntry in parts[2].Split(',', StringSplitOptions.None))
            {
                var itemParts = itemEntry.Split('=');
                if (itemParts.Length != 2
                    || string.IsNullOrEmpty(itemParts[0])
                    || !TryParseCanonicalInt(itemParts[1], out var itemId))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "NPC Item Gift pending edit item payload is malformed.",
                        field: GiftsField,
                        expected: "slotId=itemId entries"));
                    continue;
                }

                items.Add(new SwShNpcItemGiftItemSelection(itemParts[0], itemId));
            }

            selections.Add(new SwShNpcItemGiftSelection(parts[0], quantity, items));
        }

        return NormalizeSelections(game, workflow, selections, diagnostics);
    }

    private static bool TryParseCanonicalInt(string value, out int result)
    {
        return int.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out result)
            && string.Equals(
                value,
                result.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }

    private static IReadOnlyList<SwShNpcItemGiftFileGroup> CreateChangedFileGroups(
        SwShNpcItemGiftWorkflow workflow,
        IReadOnlyList<SwShNpcItemGiftSelection> selections)
    {
        var currentByGiftId = workflow.Npcs
            .SelectMany(npc => npc.Gifts)
            .ToDictionary(gift => gift.GiftId, StringComparer.Ordinal);
        var definitionsByGiftId = SwShNpcItemGiftWorkflowService.Gifts
            .ToDictionary(gift => gift.GiftId, StringComparer.Ordinal);

        return selections
            .Where(selection => currentByGiftId.ContainsKey(selection.GiftId)
                && IsChanged(currentByGiftId[selection.GiftId], selection))
            .Select(selection => new SwShNpcItemGiftSelectionPatch(
                definitionsByGiftId[selection.GiftId],
                currentByGiftId[selection.GiftId],
                selection))
            .GroupBy(selection => selection.Definition.RelativePath, StringComparer.Ordinal)
            .Select(group => new SwShNpcItemGiftFileGroup(group.Key, group.ToArray()))
            .ToArray();
    }

    private static bool IsChanged(SwShNpcItemGiftRecord current, SwShNpcItemGiftSelection selection)
    {
        if (string.Equals(current.Status, "repairable", StringComparison.Ordinal))
        {
            return true;
        }

        if (current.Quantity != selection.Quantity)
        {
            return true;
        }

        var currentItems = current.Items.ToDictionary(item => item.SlotId, StringComparer.Ordinal);
        return selection.Items.Any(item => currentItems[item.SlotId].ItemId != item.ItemId);
    }

    private static PendingEdit CreatePendingEdit(
        string payload,
        IReadOnlyList<ProjectFileReference> sourceReferences)
    {
        return new PendingEdit(
            NpcItemGiftEditDomain,
            "Stage NPC Item Gift changes.",
            sourceReferences,
            RecordId,
            GiftsField,
            payload);
    }

    private static ProjectFileReference CreatePendingPayloadSource(string canonicalPayload)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/npc-item-gift/{hash}");
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift apply requires a configured output root.",
                file: targetRelativePath,
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShNpcItemGiftWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static bool IsSupportedGame(ProjectGame? game)
    {
        return game == ProjectGame.Sword || game == ProjectGame.Shield;
    }

    private static ValidationDiagnostic CreateWrongGameDiagnostic()
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "NPC Item Gift only supports Pokemon Sword and Pokemon Shield projects.",
            expected: "Pokemon Sword or Pokemon Shield project");
    }

    private static ApplyResult CreateApplyResult(
        string applyId,
        DateTimeOffset appliedAt,
        ChangePlan currentPlan,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, currentPlan.Writes),
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: NpcItemGiftEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record PreparedNpcItemGiftOutput(
        SwShNpcItemGiftFileGroup FileGroup,
        string TargetPath,
        byte[] Output);
}
