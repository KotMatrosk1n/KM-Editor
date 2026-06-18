// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Scripts;
using KM.SwSh.Workflows;
using System.Globalization;

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
        var project = projectWorkspaceService.Open(paths);
        var workflow = npcItemGiftWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, NpcItemGiftEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift needs its own edit session before staging.",
                expected: "An NPC Item Gift-only edit session"));
            return new SwShNpcItemGiftEditResult(workflow, currentSession, diagnostics);
        }

        var game = SwShNpcItemGiftWorkflowService.ResolveGame(paths.SelectedGame);
        var normalizedSelections = NormalizeSelections(game, workflow, selections, diagnostics);
        if (!CanStage(project, workflow, normalizedSelections, diagnostics)
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShNpcItemGiftEditResult(workflow, currentSession, diagnostics);
        }

        var payload = EncodeSelections(normalizedSelections);
        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, NpcItemGiftEditDomain, StringComparison.Ordinal))
                .Append(CreatePendingEdit(payload, CreateSourceReferences(project, normalizedSelections)))
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

        var project = projectWorkspaceService.Open(paths);
        var workflow = npcItemGiftWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

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

            var selections = DecodeSelections(SwShNpcItemGiftWorkflowService.ResolveGame(paths.SelectedGame), workflow, edit.NewValue, diagnostics);
            CanStage(project, workflow, selections, diagnostics);
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

        var project = projectWorkspaceService.Open(paths);
        var workflow = npcItemGiftWorkflowService.Load(project);
        var selections = DecodeSelections(SwShNpcItemGiftWorkflowService.ResolveGame(paths.SelectedGame), workflow, session.PendingEdits.Single().NewValue, diagnostics);
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
        foreach (var fileGroup in fileGroups)
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
                [CreateSourceReference(source.Entry)],
                File.Exists(targetPath),
                "Update NPC item gift cells in the AMX script while preserving unrelated cells."));
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"NPC Item Gift change plan preview contains {writes.Count:N0} target file(s)."));

        return new ChangePlan(session.Id, writes, diagnostics);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed NPC Item Gift change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed NPC Item Gift change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = npcItemGiftWorkflowService.Load(project);
        var selections = DecodeSelections(SwShNpcItemGiftWorkflowService.ResolveGame(paths.SelectedGame), workflow, session.PendingEdits.Single().NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        foreach (var fileGroup in CreateChangedFileGroups(workflow, selections))
        {
            ApplyFileGroup(project, paths, fileGroup, writtenFiles, diagnostics);
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static void ApplyFileGroup(
        OpenedProject project,
        ProjectPaths paths,
        SwShNpcItemGiftFileGroup fileGroup,
        ICollection<ProjectFileReference> writtenFiles,
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
            return;
        }

        try
        {
            var patches = CreateCellPatches(fileGroup, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return;
            }

            var output = SwShAmxCellPatcher.ApplyCodeCellPatches(
                File.ReadAllBytes(source.AbsolutePath),
                patches);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, fileGroup.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Applied NPC Item Gift changes to {fileGroup.RelativePath}.",
                file: fileGroup.RelativePath));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"NPC Item Gift source file could not be patched: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Supported Sword/Shield AMX script file"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"NPC Item Gift output file could not be written: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"NPC Item Gift output file could not be written: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Writable output root"));
        }
    }

    private static IReadOnlyList<SwShAmxCellPatch> CreateCellPatches(
        SwShNpcItemGiftFileGroup fileGroup,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var patches = new Dictionary<int, int>();
        foreach (var selectionPatch in fileGroup.Selections)
        {
            AddPatch(selectionPatch.Definition.QuantityCell, selectionPatch.Selection.Quantity);
            foreach (var slot in selectionPatch.Definition.Items)
            {
                var selectedItem = selectionPatch.Selection.Items
                    .First(item => string.Equals(item.SlotId, slot.SlotId, StringComparison.Ordinal));
                AddPatch(slot.ItemCell, selectedItem.ItemId);
            }
        }

        return patches
            .OrderBy(pair => pair.Key)
            .Select(pair => new SwShAmxCellPatch(pair.Key, pair.Value))
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
        IReadOnlyList<SwShNpcItemGiftSelection> selections,
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

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        if (workflow.ItemOptions.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "NPC Item Gift needs item options before gifts can be staged.",
                expected: "Readable item table"));
        }

        foreach (var relativePath in selections
            .Select(selection => SwShNpcItemGiftWorkflowService.FindGift(selection.GiftId, SwShNpcItemGiftWorkflowService.ResolveGame(project.Paths.SelectedGame))?.RelativePath)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal))
        {
            var source = workflow.Sources.FirstOrDefault(source => string.Equals(source.RelativePath, relativePath, StringComparison.Ordinal));
            if (source is null || source.Status != "available")
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{Path.GetFileName(relativePath)} is required before NPC Item Gift can be staged.",
                    file: relativePath,
                    expected: "Available AMX script file"));
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
        var itemIds = workflow.ItemOptions
            .Select(item => item.ItemId)
            .ToHashSet();
        var byGiftId = new Dictionary<string, SwShNpcItemGiftSelection>(StringComparer.Ordinal);
        string? npcId = null;

        foreach (var selection in selections)
        {
            var normalized = NormalizeSelection(game, itemIds, selection, diagnostics);
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
        ISet<int> itemIds,
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
        if (definition is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"NPC Item Gift selection '{selection.GiftId}' is not recognized for this game.",
                field: GiftsField,
                expected: "Known gift id"));
            return null;
        }

        if (selection.Quantity is < 1 or > 999)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{definition.Label} quantity must be between 1 and 999.",
                field: GiftsField,
                expected: "Quantity 1-999"));
        }

        var selectedItems = new Dictionary<string, SwShNpcItemGiftItemSelection>(StringComparer.Ordinal);
        foreach (var item in selection.Items)
        {
            if (!definition.Items.Any(slot => string.Equals(slot.SlotId, item.SlotId, StringComparison.Ordinal)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{definition.Label} item slot '{item.SlotId}' is not recognized.",
                    field: GiftsField,
                    expected: "Known gift item slot"));
                continue;
            }

            if (!itemIds.Contains(item.ItemId))
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

        return new SwShNpcItemGiftSelection(
            definition.GiftId,
            Math.Clamp(selection.Quantity, 1, 999),
            definition.Items
                .Select(slot => selectedItems.TryGetValue(slot.SlotId, out var selected)
                    ? selected
                    : new SwShNpcItemGiftItemSelection(slot.SlotId, slot.ItemId))
                .ToArray());
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
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('|');
            if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "NPC Item Gift pending edit payload is malformed.",
                    field: GiftsField,
                    expected: "giftId|quantity|slotId=itemId entries"));
                continue;
            }

            var items = new List<SwShNpcItemGiftItemSelection>();
            foreach (var itemEntry in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var itemParts = itemEntry.Split('=');
                if (itemParts.Length != 2 || !int.TryParse(itemParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
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
            .Where(selection => IsChanged(currentByGiftId[selection.GiftId], selection))
            .Select(selection => new SwShNpcItemGiftSelectionPatch(
                definitionsByGiftId[selection.GiftId],
                selection))
            .GroupBy(selection => selection.Definition.RelativePath, StringComparer.Ordinal)
            .Select(group => new SwShNpcItemGiftFileGroup(group.Key, group.ToArray()))
            .ToArray();
    }

    private static bool IsChanged(SwShNpcItemGiftRecord current, SwShNpcItemGiftSelection selection)
    {
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

    private static IReadOnlyList<ProjectFileReference> CreateSourceReferences(
        OpenedProject project,
        IReadOnlyList<SwShNpcItemGiftSelection> selections)
    {
        return selections
            .Select(selection => SwShNpcItemGiftWorkflowService.FindGift(selection.GiftId, SwShNpcItemGiftWorkflowService.ResolveGame(project.Paths.SelectedGame))?.RelativePath)
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(path => SwShNpcItemGiftWorkflowService.ResolveWorkflowFile(project, path))
            .Where(source => source is not null)
            .Select(source => CreateSourceReference(source!.Entry))
            .ToArray();
    }

    private static ProjectFileReference CreateSourceReference(ProjectFileGraphEntry entry)
    {
        var layer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
        return new ProjectFileReference(layer, entry.RelativePath);
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

    private static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        if (!reviewedPlan.CanApply
            || reviewedPlan.SessionId != currentPlan.SessionId
            || reviewedPlan.Writes.Count != currentPlan.Writes.Count)
        {
            return false;
        }

        var reviewedTargets = reviewedPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currentTargets = currentPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return reviewedTargets.SequenceEqual(currentTargets, StringComparer.Ordinal);
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
}
