// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Items;

public sealed class SwShItemsEditSessionService
{
    public const string BuyPriceField = SwShItemsWorkflowService.BuyPriceField;
    public const string SellPriceField = SwShItemsWorkflowService.SellPriceField;
    public const string WattsPriceField = SwShItemsWorkflowService.WattsPriceField;
    public const string AlternatePriceField = SwShItemsWorkflowService.AlternatePriceField;
    public const string PouchField = SwShItemsWorkflowService.PouchField;
    public const string PouchFlagsField = SwShItemsWorkflowService.PouchFlagsField;
    public const string FlingPowerField = SwShItemsWorkflowService.FlingPowerField;
    public const string FieldUseTypeField = SwShItemsWorkflowService.FieldUseTypeField;
    public const string BattlePouchField = SwShItemsWorkflowService.BattlePouchField;
    public const string FieldFlagsField = SwShItemsWorkflowService.FieldFlagsField;
    public const string CanUseOnPokemonField = SwShItemsWorkflowService.CanUseOnPokemonField;
    public const string ItemTypeField = SwShItemsWorkflowService.ItemTypeField;
    public const string SortIndexField = SwShItemsWorkflowService.SortIndexField;
    public const string ItemSpriteField = SwShItemsWorkflowService.ItemSpriteField;
    public const string GroupTypeField = SwShItemsWorkflowService.GroupTypeField;
    public const string GroupIndexField = SwShItemsWorkflowService.GroupIndexField;
    public const string CureStatusFlagsField = SwShItemsWorkflowService.CureStatusFlagsField;
    public const string CureSleepField = SwShItemsWorkflowService.CureSleepField;
    public const string CurePoisonField = SwShItemsWorkflowService.CurePoisonField;
    public const string CureBurnField = SwShItemsWorkflowService.CureBurnField;
    public const string CureFreezeField = SwShItemsWorkflowService.CureFreezeField;
    public const string CureParalysisField = SwShItemsWorkflowService.CureParalysisField;
    public const string CureConfusionField = SwShItemsWorkflowService.CureConfusionField;
    public const string CureInfatuationField = SwShItemsWorkflowService.CureInfatuationField;
    public const string GuardSpecField = SwShItemsWorkflowService.GuardSpecField;
    public const string CanTargetFaintedPokemonField = SwShItemsWorkflowService.CanTargetFaintedPokemonField;
    public const string RevivesWholePartyField = SwShItemsWorkflowService.RevivesWholePartyField;
    public const string LevelUpItemField = SwShItemsWorkflowService.LevelUpItemField;
    public const string EvolutionItemField = SwShItemsWorkflowService.EvolutionItemField;
    public const string AttackBoostField = SwShItemsWorkflowService.AttackBoostField;
    public const string DefenseBoostField = SwShItemsWorkflowService.DefenseBoostField;
    public const string SpecialAttackBoostField = SwShItemsWorkflowService.SpecialAttackBoostField;
    public const string SpecialDefenseBoostField = SwShItemsWorkflowService.SpecialDefenseBoostField;
    public const string SpeedBoostField = SwShItemsWorkflowService.SpeedBoostField;
    public const string AccuracyBoostField = SwShItemsWorkflowService.AccuracyBoostField;
    public const string CriticalHitBoostField = SwShItemsWorkflowService.CriticalHitBoostField;
    public const string PpUpFlagField = SwShItemsWorkflowService.PpUpFlagField;
    public const string PpMaxFlagField = SwShItemsWorkflowService.PpMaxFlagField;
    public const string UseFlags1Field = SwShItemsWorkflowService.UseFlags1Field;
    public const string UseFlags2Field = SwShItemsWorkflowService.UseFlags2Field;
    public const string RestorePpFlagField = SwShItemsWorkflowService.RestorePpFlagField;
    public const string RestoreAllPpFlagField = SwShItemsWorkflowService.RestoreAllPpFlagField;
    public const string RestoreHpFlagField = SwShItemsWorkflowService.RestoreHpFlagField;
    public const string HpEvFlagField = SwShItemsWorkflowService.HpEvFlagField;
    public const string AttackEvFlagField = SwShItemsWorkflowService.AttackEvFlagField;
    public const string DefenseEvFlagField = SwShItemsWorkflowService.DefenseEvFlagField;
    public const string SpeedEvFlagField = SwShItemsWorkflowService.SpeedEvFlagField;
    public const string SpecialAttackEvFlagField = SwShItemsWorkflowService.SpecialAttackEvFlagField;
    public const string SpecialDefenseEvFlagField = SwShItemsWorkflowService.SpecialDefenseEvFlagField;
    public const string EvAbove100FlagField = SwShItemsWorkflowService.EvAbove100FlagField;
    public const string Friendship1FlagField = SwShItemsWorkflowService.Friendship1FlagField;
    public const string Friendship2FlagField = SwShItemsWorkflowService.Friendship2FlagField;
    public const string Friendship3FlagField = SwShItemsWorkflowService.Friendship3FlagField;
    public const string EvHpField = SwShItemsWorkflowService.EvHpField;
    public const string EvAttackField = SwShItemsWorkflowService.EvAttackField;
    public const string EvDefenseField = SwShItemsWorkflowService.EvDefenseField;
    public const string EvSpeedField = SwShItemsWorkflowService.EvSpeedField;
    public const string EvSpecialAttackField = SwShItemsWorkflowService.EvSpecialAttackField;
    public const string EvSpecialDefenseField = SwShItemsWorkflowService.EvSpecialDefenseField;
    public const string HealAmountField = SwShItemsWorkflowService.HealAmountField;
    public const string PpGainField = SwShItemsWorkflowService.PpGainField;
    public const string FriendshipGain1Field = SwShItemsWorkflowService.FriendshipGain1Field;
    public const string FriendshipGain2Field = SwShItemsWorkflowService.FriendshipGain2Field;
    public const string FriendshipGain3Field = SwShItemsWorkflowService.FriendshipGain3Field;
    public const string MachineMoveIdField = SwShItemsWorkflowService.MachineMoveIdField;

    private const string ItemsEditDomain = "workflow.items";

    private readonly SwShItemsWorkflowService itemsWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly Action<string, byte[]> temporaryFileWriter;

    public SwShItemsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
        temporaryFileWriter = File.WriteAllBytes;
    }

    internal SwShItemsEditSessionService(
        Action<string, byte[]> temporaryFileWriter,
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        ArgumentNullException.ThrowIfNull(temporaryFileWriter);

        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
        this.temporaryFileWriter = temporaryFileWriter;
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShItemsEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int itemId,
        string field,
        string value)
    {
        return UpdateFields(paths, session, [new SwShItemFieldUpdate(itemId, field, value)]);
    }

    public SwShItemsEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShItemFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var originalSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = itemsWorkflowService.Load(project);
        var originalWorkflow = CreateEffectiveWorkflow(project, loadedWorkflow, originalSession);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditItems(project, loadedWorkflow, diagnostics))
        {
            return new SwShItemsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        if (updates.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Update at least one Items field.",
                field: "updates",
                expected: "One or more Items field updates"));
            return new SwShItemsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        foreach (var update in updates)
        {
            if (update is null || string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Items field update is missing a field or value.",
                    field: "updates",
                    expected: "Complete Items field update"));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShItemsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        var candidateSession = originalSession;
        var effectiveWorkflow = originalWorkflow;
        var batchAssignments = new List<StorageAssignment>();

        foreach (var update in updates.Where(update => !IsMachineMoveField(update.Field)))
        {
            if (!TryStageUpdate(
                    effectiveWorkflow,
                    candidateSession,
                    update,
                    batchAssignments,
                    diagnostics,
                    out candidateSession))
            {
                break;
            }

            effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, candidateSession.PendingEdits);
        }

        var rowCandidateSession = candidateSession with
        {
            PendingEdits = candidateSession.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, ItemsEditDomain, StringComparison.Ordinal)
                    || !IsMachineMoveField(edit.Field))
                .ToArray(),
        };
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error)
            && !TryMaterializeWorkflow(project, loadedWorkflow, rowCandidateSession, diagnostics, out effectiveWorkflow))
        {
            // The formatter diagnostic below explains why the candidate row/linkage edits are unsafe.
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            candidateSession = ReconcilePendingMachineEdits(
                loadedWorkflow,
                effectiveWorkflow,
                candidateSession,
                diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            foreach (var update in updates.Where(update => IsMachineMoveField(update.Field)))
            {
                if (!TryStageUpdate(
                        effectiveWorkflow,
                        candidateSession,
                        update,
                        batchAssignments,
                        diagnostics,
                        out candidateSession))
                {
                    break;
                }

                effectiveWorkflow = OverlayPendingEdits(effectiveWorkflow, [candidateSession.PendingEdits.Last()]);
            }
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            ValidateLoadedSession(project, loadedWorkflow, candidateSession, diagnostics, addSuccessDiagnostic: false);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || !TryMaterializeWorkflow(project, loadedWorkflow, candidateSession, diagnostics, out effectiveWorkflow))
        {
            return new SwShItemsEditResult(originalWorkflow, originalSession, diagnostics);
        }

        return new SwShItemsEditResult(
            effectiveWorkflow,
            candidateSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = itemsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditItems(project, workflow, diagnostics);
        ValidateLoadedSession(project, workflow, session, diagnostics, addSuccessDiagnostic: true);

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
        var itemEdits = GetItemEdits(session.PendingEdits);

        if (itemEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Items edit before reviewing a change plan.",
                expected: "Pending item edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = itemEdits.Count == 0
            ? Array.Empty<PlannedFileWrite>()
            : [CreatePlannedWrite(paths, itemEdits)];

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Length} target file{(writes.Length == 1 ? string.Empty : "s")}."));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
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

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Items change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));

        var targetPath = ResolveOutputPath(paths, SwShItemsWorkflowService.ItemDataPath, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) || targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var itemDataSource = SwShItemsWorkflowService.ResolveItemDataSource(project);
        if (itemDataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items apply could not resolve the source item table.",
                expected: SwShItemsWorkflowService.ItemDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var itemTable = SwShItemTable.Parse(File.ReadAllBytes(itemDataSource.AbsolutePath));
            var itemTableEdits = OrderPendingEdits(GetItemEdits(session.PendingEdits))
                .Select(edit => ToItemTableEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var output = itemTable.WriteEdits(itemTableEdits);
            WriteOutputAtomically(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShItemsWorkflowService.ItemDataPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Items change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items source file could not be decoded: {exception.Message}",
                expected: "Sword/Shield item.dat"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items output file could not be written: {exception.Message}",
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items output file could not be written: {exception.Message}",
                expected: "Writable output root"));
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items change could not be encoded safely: {exception.Message}",
                expected: "Supported Sword/Shield item field values"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditItems(
        OpenedProject project,
        SwShItemsWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static bool TryStageUpdate(
        SwShItemsWorkflow workflow,
        EditSession session,
        SwShItemFieldUpdate update,
        ICollection<StorageAssignment> batchAssignments,
        ICollection<ValidationDiagnostic> diagnostics,
        out EditSession updatedSession)
    {
        updatedSession = session;
        var selectedItem = workflow.Items.FirstOrDefault(item => item.ItemId == update.ItemId);
        if (selectedItem is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item {update.ItemId} is not present in the loaded Items workflow.",
                field: "itemId",
                expected: "Existing item record"));
            return false;
        }

        var pendingEdit = CreatePendingEdit(workflow, selectedItem, update.Field, update.Value, diagnostics);
        if (pendingEdit is null)
        {
            return false;
        }

        var assignment = CreateStorageAssignment(workflow, pendingEdit, diagnostics);
        if (assignment is null || !TryRegisterBatchAssignment(assignment, batchAssignments, diagnostics))
        {
            return false;
        }

        var canonicalPendingEdit = assignment.StorageUnit == "buyPrice"
            ? batchAssignments
                .LastOrDefault(candidate =>
                    string.Equals(candidate.Owner, assignment.Owner, StringComparison.Ordinal)
                    && candidate.StorageUnit == assignment.StorageUnit
                    && candidate.Edit.Field == BuyPriceField)
                ?.Edit ?? pendingEdit
            : pendingEdit;
        updatedSession = ReplacePendingItemEdit(session, canonicalPendingEdit, workflow);
        return true;
    }

    private static EditSession ReconcilePendingMachineEdits(
        SwShItemsWorkflow loadedWorkflow,
        SwShItemsWorkflow finalRowWorkflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var pendingMachineEdits = GetItemEdits(session.PendingEdits)
            .Where(edit => IsMachineMoveField(edit.Field))
            .ToArray();
        if (pendingMachineEdits.Length == 0)
        {
            return session;
        }

        var reconciledSession = session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !string.Equals(edit.Domain, ItemsEditDomain, StringComparison.Ordinal)
                    || !IsMachineMoveField(edit.Field))
                .ToArray(),
        };
        var assignments = new List<StorageAssignment>();
        foreach (var edit in pendingMachineEdits)
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var originalItemId)
                || loadedWorkflow.Items.FirstOrDefault(item => item.ItemId == originalItemId) is not { } originalItem
                || finalRowWorkflow.Items.FirstOrDefault(item => item.ItemId == originalItemId) is not { } finalItem)
            {
                reconciledSession = reconciledSession with
                {
                    PendingEdits = reconciledSession.PendingEdits.Append(edit).ToArray(),
                };
                continue;
            }

            var physicalRowIds = originalItem.SharedItemIds
                .Concat(finalItem.SharedItemIds)
                .Append(originalItemId)
                .ToHashSet();
            var finalOwners = finalRowWorkflow.Items
                .Where(item => physicalRowIds.Contains(item.ItemId)
                    && item.Metadata.MachineSlot is not null
                    && item.Metadata.MachineMoveId is not null)
                .ToArray();
            if (finalOwners.Length != 1 || edit.NewValue is null)
            {
                reconciledSession = reconciledSession with
                {
                    PendingEdits = reconciledSession.PendingEdits.Append(edit).ToArray(),
                };
                continue;
            }

            var reconciledEdit = CreatePendingEdit(
                finalRowWorkflow,
                finalOwners[0],
                MachineMoveIdField,
                edit.NewValue,
                diagnostics);
            if (reconciledEdit is null)
            {
                continue;
            }

            var assignment = CreateStorageAssignment(finalRowWorkflow, reconciledEdit, diagnostics);
            if (assignment is null
                || !TryRegisterBatchAssignment(assignment, assignments, diagnostics))
            {
                continue;
            }

            reconciledSession = ReplacePendingItemEdit(
                reconciledSession,
                reconciledEdit,
                finalRowWorkflow);
        }

        return reconciledSession;
    }

    private static bool TryRegisterBatchAssignment(
        StorageAssignment assignment,
        ICollection<StorageAssignment> assignments,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var existing in assignments.Where(existing =>
            string.Equals(existing.Owner, assignment.Owner, StringComparison.Ordinal)
            && string.Equals(existing.StorageUnit, assignment.StorageUnit, StringComparison.Ordinal)))
        {
            var overlap = existing.Mask & assignment.Mask;
            if (overlap != 0
                && (existing.Bits & overlap) != (assignment.Bits & overlap)
                && !AreCompatiblePriceAssignments(existing, assignment))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Items fields '{existing.Edit.Field}' and '{assignment.Edit.Field}' assign incompatible values to the same stored data.",
                    field: assignment.Edit.Field,
                    expected: "One consistent value for each stored item field"));
                return false;
            }
        }

        assignments.Add(assignment);
        return true;
    }

    private static bool AreCompatiblePriceAssignments(
        StorageAssignment first,
        StorageAssignment second)
    {
        if (first.StorageUnit != "buyPrice" || second.StorageUnit != "buyPrice")
        {
            return false;
        }

        var buyEdit = first.Edit.Field == BuyPriceField
            ? first.Edit
            : second.Edit.Field == BuyPriceField
                ? second.Edit
                : null;
        var sellEdit = first.Edit.Field == SellPriceField
            ? first.Edit
            : second.Edit.Field == SellPriceField
                ? second.Edit
                : null;
        return buyEdit is not null
            && sellEdit is not null
            && int.TryParse(buyEdit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var buyPrice)
            && int.TryParse(sellEdit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var sellPrice)
            && buyPrice / 2 == sellPrice;
    }

    private static StorageAssignment? CreateStorageAssignment(
        SwShItemsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || workflow.Items.FirstOrDefault(item => item.ItemId == itemId) is not { } item
            || GetEditableField(edit.Field) is not { } itemField
            || TryParsePendingEditValue(edit, diagnostics) is not { } value)
        {
            return null;
        }

        if (itemField.Field == MachineMoveIdField)
        {
            if (item.Metadata.MachineSlot is not { } slot)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "A taught move edit must resolve to a final TM/TR slot.",
                    field: itemField.Field,
                    expected: "Item linked to a writable machine slot"));
                return null;
            }

            return new StorageAssignment(
                Owner: "machine",
                StorageUnit: slot.ToString(CultureInfo.InvariantCulture),
                Mask: ulong.MaxValue,
                Bits: checked((ulong)value),
                Edit: edit);
        }

        var canonicalItemId = item.SharedItemIds.DefaultIfEmpty(item.ItemId).Min();
        var (storageUnit, mask, shift) = GetStorageDescriptor(itemField.Field);
        var storedValue = checked(value * itemField.ActualValueMultiplier);
        var bits = unchecked((ulong)((long)storedValue << shift)) & mask;
        return new StorageAssignment(
            Owner: canonicalItemId.ToString(CultureInfo.InvariantCulture),
            StorageUnit: storageUnit,
            Mask: mask,
            Bits: bits,
            Edit: edit);
    }

    private static (string StorageUnit, ulong Mask, int Shift) GetStorageDescriptor(string field)
    {
        return field switch
        {
            BuyPriceField or SellPriceField => ("buyPrice", ulong.MaxValue, 0),
            PouchField => ("pouch", 0x0F, 0),
            PouchFlagsField => ("pouch", 0xF0, 4),
            CureSleepField => ("cureStatus", 1UL << 0, 0),
            CurePoisonField => ("cureStatus", 1UL << 1, 1),
            CureBurnField => ("cureStatus", 1UL << 2, 2),
            CureFreezeField => ("cureStatus", 1UL << 3, 3),
            CureParalysisField => ("cureStatus", 1UL << 4, 4),
            CureConfusionField => ("cureStatus", 1UL << 5, 5),
            CureInfatuationField => ("cureStatus", 1UL << 6, 6),
            GuardSpecField => ("cureStatus", 1UL << 7, 7),
            CanTargetFaintedPokemonField => ("boost0", 1UL << 0, 0),
            RevivesWholePartyField => ("boost0", 1UL << 1, 1),
            LevelUpItemField => ("boost0", 1UL << 2, 2),
            EvolutionItemField => ("boost0", 1UL << 3, 3),
            AttackBoostField => ("boost0", 0xF0, 4),
            DefenseBoostField => ("boost1", 0x0F, 0),
            SpecialAttackBoostField => ("boost1", 0xF0, 4),
            SpecialDefenseBoostField => ("boost2", 0x0F, 0),
            SpeedBoostField => ("boost2", 0xF0, 4),
            AccuracyBoostField => ("boost3", 0x0F, 0),
            CriticalHitBoostField => ("boost3", 0x30, 4),
            PpUpFlagField => ("boost3", 1UL << 6, 6),
            PpMaxFlagField => ("boost3", 1UL << 7, 7),
            RestorePpFlagField => ("useFlags1", 1UL << 0, 0),
            RestoreAllPpFlagField => ("useFlags1", 1UL << 1, 1),
            RestoreHpFlagField => ("useFlags1", 1UL << 2, 2),
            HpEvFlagField => ("useFlags1", 1UL << 3, 3),
            AttackEvFlagField => ("useFlags1", 1UL << 4, 4),
            DefenseEvFlagField => ("useFlags1", 1UL << 5, 5),
            SpeedEvFlagField => ("useFlags1", 1UL << 6, 6),
            SpecialAttackEvFlagField => ("useFlags1", 1UL << 7, 7),
            SpecialDefenseEvFlagField => ("useFlags2", 1UL << 0, 0),
            EvAbove100FlagField => ("useFlags2", 1UL << 1, 1),
            Friendship1FlagField => ("useFlags2", 1UL << 2, 2),
            Friendship2FlagField => ("useFlags2", 1UL << 3, 3),
            Friendship3FlagField => ("useFlags2", 1UL << 4, 4),
            _ => (field, ulong.MaxValue, 0),
        };
    }

    private static string NormalizeFieldName(string field)
    {
        return field == FieldFlagsField ? BattlePouchField : field;
    }

    private static bool IsMachineMoveField(string? field)
    {
        return field is not null
            && NormalizeFieldName(field.Trim()) == MachineMoveIdField;
    }

    private static bool IsStructuralMachineLink(SwShItemMetadata item)
    {
        return item.GroupType == 4
            && item.FieldUseType == 2
            && item.GroupIndex is >= 0 and < 200;
    }

    private static ValidationDiagnostic CreateReadOnlyFieldDiagnostic(SwShItemEditableField field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            field.ReadOnlyReason ?? $"Item field '{field.Field}' is read-only.",
            field: field.Field,
            expected: "Writable named item field");
    }

    private static void ValidateLoadedSession(
        OpenedProject project,
        SwShItemsWorkflow loadedWorkflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        bool addSuccessDiagnostic)
    {
        var itemEdits = GetItemEdits(session.PendingEdits);
        foreach (var edit in itemEdits)
        {
            ValidatePendingEdit(loadedWorkflow, edit, diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        var rowSession = session with
        {
            PendingEdits = itemEdits.Where(edit => !IsMachineMoveField(edit.Field)).ToArray(),
        };
        if (!TryMaterializeWorkflow(project, loadedWorkflow, rowSession, diagnostics, out var rowWorkflow))
        {
            return;
        }

        var linkageRows = itemEdits
            .Where(edit => edit.Field is GroupTypeField or FieldUseTypeField or GroupIndexField)
            .Select(edit => int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
                ? itemId
                : -1)
            .Where(itemId => itemId >= 0)
            .ToHashSet();
        foreach (var itemId in linkageRows)
        {
            var item = rowWorkflow.Items.FirstOrDefault(candidate => candidate.ItemId == itemId);
            if (item is null || !IsStructuralMachineLink(item.Metadata))
            {
                continue;
            }

            var sharedIds = item.SharedItemIds.ToHashSet();
            var ownedLink = rowWorkflow.Items.FirstOrDefault(candidate =>
                sharedIds.Contains(candidate.ItemId)
                && candidate.Metadata.MachineSlot == item.Metadata.GroupIndex
                && candidate.Metadata.MachineMoveId is not null);
            if (ownedLink is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Item {itemId} cannot link to machine slot {item.Metadata.GroupIndex} because that slot is owned by a different item or has no writable entry.",
                    field: GroupIndexField,
                    expected: "Machine slot entry owned by the edited item"));
                continue;
            }

            var physicalRowsAtSlot = rowWorkflow.Items
                .Where(candidate => IsStructuralMachineLink(candidate.Metadata)
                    && candidate.Metadata.GroupIndex == item.Metadata.GroupIndex)
                .Select(candidate => candidate.SharedItemIds.DefaultIfEmpty(candidate.ItemId).Min())
                .Distinct()
                .ToArray();
            if (physicalRowsAtSlot.Length > 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"More than one item row converges on machine slot {item.Metadata.GroupIndex}.",
                    field: GroupIndexField,
                    expected: "One physical item row per machine slot"));
            }
        }

        var assignments = new List<StorageAssignment>();
        foreach (var edit in OrderPendingEdits(itemEdits))
        {
            var assignmentWorkflow = IsMachineMoveField(edit.Field) ? rowWorkflow : loadedWorkflow;
            var assignment = CreateStorageAssignment(assignmentWorkflow, edit, diagnostics);
            if (assignment is null)
            {
                continue;
            }

            foreach (var existing in assignments.Where(existing =>
                string.Equals(existing.Owner, assignment.Owner, StringComparison.Ordinal)
                && string.Equals(existing.StorageUnit, assignment.StorageUnit, StringComparison.Ordinal)))
            {
                var overlap = existing.Mask & assignment.Mask;
                if (overlap == 0)
                {
                    continue;
                }

                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    (existing.Bits & overlap) == (assignment.Bits & overlap)
                        ? $"Items session contains duplicate writes to '{assignment.Edit.Field}'."
                        : $"Items session contains conflicting writes to '{existing.Edit.Field}' and '{assignment.Edit.Field}'.",
                    field: assignment.Edit.Field,
                    expected: "One pending value for each stored item field"));
            }

            assignments.Add(assignment);
        }

        foreach (var edit in itemEdits.Where(edit => IsMachineMoveField(edit.Field)))
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
                || rowWorkflow.Items.FirstOrDefault(item => item.ItemId == itemId) is not { } item
                || TryParsePendingEditValue(edit, diagnostics) is not { } moveId)
            {
                continue;
            }

            CanEditMachineMove(
                rowWorkflow,
                item,
                GetEditableField(MachineMoveIdField)!,
                moveId,
                diagnostics);
            var canonicalOwner = rowWorkflow.Items
                .Where(candidate => candidate.Metadata.MachineSlot == item.Metadata.MachineSlot
                    && candidate.Metadata.MachineMoveId is not null)
                .Select(candidate => candidate.ItemId)
                .DefaultIfEmpty(item.ItemId)
                .Min();
            if (canonicalOwner != itemId)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending taught move edit no longer targets the canonical owner of its final machine slot.",
                    field: "itemId",
                    expected: "Canonical item ID for the final machine slot"));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || !TryMaterializeWorkflow(project, loadedWorkflow, session, diagnostics, out _))
        {
            return;
        }

        if (itemEdits.Count > 0 && addSuccessDiagnostic)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending item change is valid."));
        }
    }

    private static IReadOnlyList<PendingEdit> OrderPendingEdits(IEnumerable<PendingEdit> edits)
    {
        return edits
            .OrderBy(edit => IsMachineMoveField(edit.Field) ? 1 : 0)
            .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
            .ThenBy(edit => edit.Field, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<PendingEdit> GetItemEdits(IEnumerable<PendingEdit> edits)
    {
        return edits
            .Where(edit => string.Equals(edit.Domain, ItemsEditDomain, StringComparison.Ordinal))
            .ToArray();
    }

    private SwShItemsWorkflow CreateEffectiveWorkflow(
        OpenedProject project,
        SwShItemsWorkflow loadedWorkflow,
        EditSession session)
    {
        var ignoredDiagnostics = new List<ValidationDiagnostic>();
        return TryMaterializeWorkflow(project, loadedWorkflow, session, ignoredDiagnostics, out var workflow)
            ? workflow
            : OverlayPendingEdits(loadedWorkflow, session.PendingEdits);
    }

    private static bool TryMaterializeWorkflow(
        OpenedProject project,
        SwShItemsWorkflow loadedWorkflow,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics,
        out SwShItemsWorkflow workflow)
    {
        workflow = loadedWorkflow;
        var itemEdits = GetItemEdits(session.PendingEdits);
        if (itemEdits.Count == 0)
        {
            return true;
        }

        var source = SwShItemsWorkflowService.ResolveItemDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items edit preflight could not resolve the source item table.",
                expected: SwShItemsWorkflowService.ItemDataPath));
            return false;
        }

        try
        {
            var table = SwShItemTable.Parse(File.ReadAllBytes(source.AbsolutePath));
            var edits = OrderPendingEdits(itemEdits)
                .Select(edit => ToItemTableEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return false;
            }

            var materialized = SwShItemTable.Parse(table.WriteEdits(edits));
            workflow = MergeMaterializedRecords(loadedWorkflow, materialized.Records);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items edit cannot be encoded safely in the source table: {exception.Message}",
                expected: "Compatible stored item targets and values",
                file: source.GraphEntry.RelativePath));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items edit preflight could not read the source table: {exception.Message}",
                expected: "Readable Sword/Shield item.dat",
                file: source.GraphEntry.RelativePath));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items edit preflight could not read the source table: {exception.Message}",
                expected: "Readable Sword/Shield item.dat",
                file: source.GraphEntry.RelativePath));
        }

        return false;
    }

    private static SwShItemsWorkflow MergeMaterializedRecords(
        SwShItemsWorkflow workflow,
        IReadOnlyList<SwShItemTableRecord> records)
    {
        var recordsById = records.ToDictionary(record => record.ItemId);
        var items = workflow.Items.Select(item =>
        {
            if (!recordsById.TryGetValue(item.ItemId, out var record))
            {
                return item;
            }

            var moveId = record.MachineMoveId is null ? null : checked((int?)record.MachineMoveId.Value);
            var moveName = moveId is null ? null : ResolveMoveName(workflow, moveId.Value);
            var metadata = new SwShItemMetadata(
                (int)record.Pouch,
                record.PouchFlags,
                record.FlingPower,
                record.FieldUseType,
                record.BattlePouch,
                record.CanUseOnPokemon,
                record.ItemType,
                record.SortIndex,
                record.ItemSprite,
                record.GroupType,
                record.GroupIndex,
                record.CureStatusFlags,
                record.Boost0,
                record.Boost1,
                record.Boost2,
                record.Boost3,
                record.UseFlags1,
                record.UseFlags2,
                record.EvHp,
                record.EvAttack,
                record.EvDefense,
                record.EvSpeed,
                record.EvSpecialAttack,
                record.EvSpecialDefense,
                record.HealAmount,
                record.PpGain,
                record.FriendshipGain1,
                record.FriendshipGain2,
                record.FriendshipGain3,
                record.MachineSlot,
                moveId,
                moveName);
            var name = item.BaseName;
            if (record.MachineSlot is { } slot && moveId is { } taughtMove)
            {
                var machineLabel = SwShItemsWorkflowService.FormatMachineLabel(slot, includeSlot: false);
                name = string.IsNullOrWhiteSpace(machineLabel)
                    ? name
                    : $"{machineLabel} ({moveName ?? taughtMove.ToString(CultureInfo.InvariantCulture)})";
            }

            return RefreshFieldValues(item with
            {
                Name = name,
                Category = SwShItemsWorkflowService.FormatPouch(metadata.Pouch),
                BuyPrice = checked((int)record.BuyPrice),
                SellPrice = checked((int)(record.BuyPrice / 2)),
                WattsPrice = checked((int)record.WattsPrice),
                AlternatePrice = checked((int)record.AlternatePrice),
                Metadata = metadata,
                SharedItemIds = record.SharedItemIds,
                DetailGroups = SwShItemsWorkflowService.CreateDetailGroups(metadata),
            });
        }).ToArray();

        return workflow with { Items = items };
    }

    private static void ValidatePendingEdit(
        SwShItemsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ItemsEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Items workflow.",
                expected: ItemsEditDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit targets a record that is not loaded.",
                field: "itemId",
                expected: "Existing item record"));
            return;
        }

        var selectedItem = workflow.Items.FirstOrDefault(item => item.ItemId == itemId);
        if (selectedItem is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit targets a record that is not loaded.",
                field: "itemId",
                expected: "Existing item record"));
            return;
        }

        var itemValue = TryParsePendingEditValue(edit, diagnostics);
        var itemField = GetEditableField(edit.Field);
        if (itemValue is null || itemField is null)
        {
            return;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(field => field.Field == itemField.Field);
        if (editableField?.IsReadOnly == true)
        {
            diagnostics.Add(CreateReadOnlyFieldDiagnostic(editableField));
            return;
        }

        if (itemField.Field != MachineMoveIdField
            && selectedItem.SharedItemIds.DefaultIfEmpty(selectedItem.ItemId).Min() != itemId)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit no longer targets the canonical owner of its shared row.",
                field: "itemId",
                expected: "Canonical item ID for the current physical row"));
        }
    }

    private static PendingEdit? CreatePendingEdit(
        SwShItemsWorkflow workflow,
        SwShItemRecord selectedItem,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = NormalizeFieldName(field.Trim());
        var itemField = GetEditableField(normalizedField);
        if (itemField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate => candidate.Field == itemField.Field);
        if (editableField?.IsReadOnly == true)
        {
            diagnostics.Add(CreateReadOnlyFieldDiagnostic(editableField));
            return null;
        }

        if (!TryParseItemValue(value, itemField.MinimumValue, itemField.MaximumValue, out var itemValue))
        {
            diagnostics.Add(CreateItemValueRangeDiagnostic(itemField));
            return null;
        }

        if (!string.Equals(value, itemValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item {itemField.DisplayName} must use canonical integer text without whitespace, a plus sign, or leading zeroes.",
                field: itemField.Field,
                expected: itemValue.ToString(CultureInfo.InvariantCulture)));
            return null;
        }

        if (itemField.Field == MachineMoveIdField
            && !CanEditMachineMove(workflow, selectedItem, itemField, itemValue, diagnostics))
        {
            return null;
        }

        var canonicalItemId = itemField.Field == MachineMoveIdField
            ? workflow.Items
                .Where(item => item.Metadata.MachineSlot == selectedItem.Metadata.MachineSlot
                    && item.Metadata.MachineMoveId is not null)
                .Select(item => item.ItemId)
                .DefaultIfEmpty(selectedItem.ItemId)
                .Min()
            : selectedItem.SharedItemIds.DefaultIfEmpty(selectedItem.ItemId).Min();
        var canonicalItem = workflow.Items.FirstOrDefault(item => item.ItemId == canonicalItemId) ?? selectedItem;

        return new PendingEdit(
            ItemsEditDomain,
            CreatePendingEditSummary(canonicalItem, itemField, itemValue),
            [new ProjectFileReference(canonicalItem.Provenance.SourceLayer, canonicalItem.Provenance.SourceFile)],
            RecordId: canonicalItemId.ToString(CultureInfo.InvariantCulture),
            Field: itemField.Field,
            NewValue: itemValue.ToString(CultureInfo.InvariantCulture));
    }

    private static int? TryParsePendingEditValue(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemField = GetEditableField(edit.Field);
        if (itemField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return null;
        }

        if (!TryParseItemValue(edit.NewValue, itemField.MinimumValue, itemField.MaximumValue, out var itemValue))
        {
            diagnostics.Add(CreateItemValueRangeDiagnostic(itemField));
            return null;
        }

        return itemValue;
    }

    private static bool TryParseItemValue(string? value, int minimumValue, int maximumValue, out int itemValue)
    {
        return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out itemValue)
            && itemValue >= minimumValue
            && itemValue <= maximumValue;
    }

    private static bool CanEditMachineMove(
        SwShItemsWorkflow workflow,
        SwShItemRecord selectedItem,
        ItemField itemField,
        int moveId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (itemField.Field != MachineMoveIdField)
        {
            return true;
        }

        if (selectedItem.Metadata.MachineSlot is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Only TM/TR item records can edit taught moves.",
                field: itemField.Field,
                expected: "Item with a machine slot"));
            return false;
        }

        if (selectedItem.Metadata.MachineMoveId is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "This item table does not expose a writable TM/TR move table.",
                field: itemField.Field,
                expected: "Available item machine move table"));
            return false;
        }

        var moveField = workflow.EditableFields.FirstOrDefault(field => field.Field == MachineMoveIdField);
        if (moveField is null || !moveField.Options.Any(option => option.Value == moveId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} is not available for TM/TR assignment.",
                field: itemField.Field,
                expected: "Available move option"));
            return false;
        }

        return true;
    }

    private static ItemField? GetEditableField(string? field)
    {
        return field is null ? null : NormalizeFieldName(field) switch
        {
            BuyPriceField => new ItemField(
                BuyPriceField,
                "buy price",
                MinimumValue: 0,
                MaximumValue: SwShItemsWorkflowService.MaximumBuyPrice,
                TableField: SwShItemTableField.BuyPrice,
                ActualValueMultiplier: 1),
            SellPriceField => new ItemField(
                SellPriceField,
                "sell price",
                MinimumValue: 0,
                MaximumValue: SwShItemsWorkflowService.MaximumSellPrice,
                TableField: SwShItemTableField.BuyPrice,
                ActualValueMultiplier: 2),
            WattsPriceField => new ItemField(
                WattsPriceField,
                "Watts price",
                MinimumValue: 0,
                MaximumValue: SwShItemsWorkflowService.MaximumWattsPrice,
                TableField: SwShItemTableField.WattsPrice,
                ActualValueMultiplier: 1),
            AlternatePriceField => new ItemField(
                AlternatePriceField,
                "alternate price",
                MinimumValue: 0,
                MaximumValue: SwShItemsWorkflowService.MaximumAlternatePrice,
                TableField: SwShItemTableField.AlternatePrice,
                ActualValueMultiplier: 1),
            PouchField => CreateByteField(
                PouchField,
                "pouch",
                SwShItemTableField.Pouch,
                maximumValue: SwShItemsWorkflowService.MaximumPouchValue),
            PouchFlagsField => CreateByteField(
                PouchFlagsField,
                "pouch flags",
                SwShItemTableField.PouchFlags,
                maximumValue: SwShItemsWorkflowService.MaximumPouchFlagsValue),
            FlingPowerField => CreateByteField(FlingPowerField, "fling power", SwShItemTableField.FlingPower),
            FieldUseTypeField => CreateByteField(
                FieldUseTypeField,
                "field use type",
                SwShItemTableField.FieldUseType),
            BattlePouchField => CreateByteField(
                BattlePouchField,
                "battle pouch",
                SwShItemTableField.BattlePouch,
                maximumValue: 2),
            CanUseOnPokemonField => CreateByteField(
                CanUseOnPokemonField,
                "can use on Pokemon",
                SwShItemTableField.CanUseOnPokemon,
                maximumValue: 1),
            ItemTypeField => CreateByteField(ItemTypeField, "item type", SwShItemTableField.ItemType),
            SortIndexField => CreateByteField(SortIndexField, "sort index", SwShItemTableField.SortIndex),
            ItemSpriteField => new ItemField(
                ItemSpriteField,
                "sprite",
                MinimumValue: short.MinValue,
                MaximumValue: short.MaxValue,
                TableField: SwShItemTableField.ItemSprite,
                ActualValueMultiplier: 1),
            GroupTypeField => CreateByteField(GroupTypeField, "group type", SwShItemTableField.GroupType),
            GroupIndexField => CreateByteField(GroupIndexField, "group index", SwShItemTableField.GroupIndex),
            CureStatusFlagsField => CreateByteField(
                CureStatusFlagsField,
                "cure status flags",
                SwShItemTableField.CureStatusFlags),
            CureSleepField => CreateBooleanField(CureSleepField, "cures sleep", SwShItemTableField.CureSleep),
            CurePoisonField => CreateBooleanField(CurePoisonField, "cures poison", SwShItemTableField.CurePoison),
            CureBurnField => CreateBooleanField(CureBurnField, "cures burn", SwShItemTableField.CureBurn),
            CureFreezeField => CreateBooleanField(CureFreezeField, "cures freeze", SwShItemTableField.CureFreeze),
            CureParalysisField => CreateBooleanField(
                CureParalysisField,
                "cures paralysis",
                SwShItemTableField.CureParalysis),
            CureConfusionField => CreateBooleanField(
                CureConfusionField,
                "cures confusion",
                SwShItemTableField.CureConfusion),
            CureInfatuationField => CreateBooleanField(
                CureInfatuationField,
                "cures infatuation",
                SwShItemTableField.CureInfatuation),
            GuardSpecField => CreateBooleanField(GuardSpecField, "Guard Spec. effect", SwShItemTableField.GuardSpec),
            CanTargetFaintedPokemonField => CreateBooleanField(
                CanTargetFaintedPokemonField,
                "can target fainted Pokemon",
                SwShItemTableField.CanTargetFaintedPokemon),
            RevivesWholePartyField => CreateBooleanField(
                RevivesWholePartyField,
                "revives whole party",
                SwShItemTableField.RevivesWholeParty),
            LevelUpItemField => CreateBooleanField(LevelUpItemField, "level up item", SwShItemTableField.LevelUpItem),
            EvolutionItemField => CreateBooleanField(
                EvolutionItemField,
                "evolution item",
                SwShItemTableField.EvolutionItem),
            AttackBoostField => CreateBoostField(AttackBoostField, "Attack boost", SwShItemTableField.AttackBoost),
            DefenseBoostField => CreateBoostField(DefenseBoostField, "Defense boost", SwShItemTableField.DefenseBoost),
            SpecialAttackBoostField => CreateBoostField(
                SpecialAttackBoostField,
                "Sp. Atk boost",
                SwShItemTableField.SpecialAttackBoost),
            SpecialDefenseBoostField => CreateBoostField(
                SpecialDefenseBoostField,
                "Sp. Def boost",
                SwShItemTableField.SpecialDefenseBoost),
            SpeedBoostField => CreateBoostField(SpeedBoostField, "Speed boost", SwShItemTableField.SpeedBoost),
            AccuracyBoostField => CreateBoostField(
                AccuracyBoostField,
                "Accuracy boost",
                SwShItemTableField.AccuracyBoost),
            CriticalHitBoostField => new ItemField(
                CriticalHitBoostField,
                "critical hit boost",
                MinimumValue: 0,
                MaximumValue: SwShItemsWorkflowService.MaximumCriticalHitBoostValue,
                TableField: SwShItemTableField.CriticalHitBoost,
                ActualValueMultiplier: 1),
            PpUpFlagField => CreateBooleanField(PpUpFlagField, "PP Up flag", SwShItemTableField.PpUpFlag),
            PpMaxFlagField => CreateBooleanField(PpMaxFlagField, "PP Max flag", SwShItemTableField.PpMaxFlag),
            UseFlags1Field => CreateByteField(UseFlags1Field, "use flags 1", SwShItemTableField.UseFlags1),
            UseFlags2Field => CreateByteField(UseFlags2Field, "use flags 2", SwShItemTableField.UseFlags2),
            RestorePpFlagField => CreateBooleanField(
                RestorePpFlagField,
                "restore PP flag",
                SwShItemTableField.RestorePpFlag),
            RestoreAllPpFlagField => CreateBooleanField(
                RestoreAllPpFlagField,
                "restore all PP flag",
                SwShItemTableField.RestoreAllPpFlag),
            RestoreHpFlagField => CreateBooleanField(
                RestoreHpFlagField,
                "restore HP flag",
                SwShItemTableField.RestoreHpFlag),
            HpEvFlagField => CreateBooleanField(HpEvFlagField, "HP EV flag", SwShItemTableField.HpEvFlag),
            AttackEvFlagField => CreateBooleanField(
                AttackEvFlagField,
                "Attack EV flag",
                SwShItemTableField.AttackEvFlag),
            DefenseEvFlagField => CreateBooleanField(
                DefenseEvFlagField,
                "Defense EV flag",
                SwShItemTableField.DefenseEvFlag),
            SpeedEvFlagField => CreateBooleanField(SpeedEvFlagField, "Speed EV flag", SwShItemTableField.SpeedEvFlag),
            SpecialAttackEvFlagField => CreateBooleanField(
                SpecialAttackEvFlagField,
                "Sp. Atk EV flag",
                SwShItemTableField.SpecialAttackEvFlag),
            SpecialDefenseEvFlagField => CreateBooleanField(
                SpecialDefenseEvFlagField,
                "Sp. Def EV flag",
                SwShItemTableField.SpecialDefenseEvFlag),
            EvAbove100FlagField => CreateBooleanField(
                EvAbove100FlagField,
                "EV above 100 flag",
                SwShItemTableField.EvAbove100Flag),
            Friendship1FlagField => CreateBooleanField(
                Friendship1FlagField,
                "friendship 1 flag",
                SwShItemTableField.Friendship1Flag),
            Friendship2FlagField => CreateBooleanField(
                Friendship2FlagField,
                "friendship 2 flag",
                SwShItemTableField.Friendship2Flag),
            Friendship3FlagField => CreateBooleanField(
                Friendship3FlagField,
                "friendship 3 flag",
                SwShItemTableField.Friendship3Flag),
            EvHpField => CreateSignedByteField(EvHpField, "HP EV gain", SwShItemTableField.EvHp),
            EvAttackField => CreateSignedByteField(
                EvAttackField,
                "Attack EV gain",
                SwShItemTableField.EvAttack),
            EvDefenseField => CreateSignedByteField(
                EvDefenseField,
                "Defense EV gain",
                SwShItemTableField.EvDefense),
            EvSpeedField => CreateSignedByteField(EvSpeedField, "Speed EV gain", SwShItemTableField.EvSpeed),
            EvSpecialAttackField => CreateSignedByteField(
                EvSpecialAttackField,
                "Sp. Atk EV gain",
                SwShItemTableField.EvSpecialAttack),
            EvSpecialDefenseField => CreateSignedByteField(
                EvSpecialDefenseField,
                "Sp. Def EV gain",
                SwShItemTableField.EvSpecialDefense),
            HealAmountField => CreateByteField(HealAmountField, "heal amount", SwShItemTableField.HealAmount),
            PpGainField => CreateByteField(PpGainField, "PP gain", SwShItemTableField.PpGain),
            FriendshipGain1Field => CreateSignedByteField(
                FriendshipGain1Field,
                "friendship gain 1",
                SwShItemTableField.FriendshipGain1),
            FriendshipGain2Field => CreateSignedByteField(
                FriendshipGain2Field,
                "friendship gain 2",
                SwShItemTableField.FriendshipGain2),
            FriendshipGain3Field => CreateSignedByteField(
                FriendshipGain3Field,
                "friendship gain 3",
                SwShItemTableField.FriendshipGain3),
            MachineMoveIdField => new ItemField(
                MachineMoveIdField,
                "taught move",
                MinimumValue: 0,
                MaximumValue: SwShItemsWorkflowService.MaximumMoveId,
                TableField: SwShItemTableField.MachineMove,
                ActualValueMultiplier: 1),
            _ => null,
        };
    }

    private static ItemField CreateByteField(
        string field,
        string displayName,
        SwShItemTableField tableField,
        int maximumValue = SwShItemsWorkflowService.MaximumByteValue)
    {
        return new ItemField(
            field,
            displayName,
            MinimumValue: 0,
            MaximumValue: maximumValue,
            TableField: tableField,
            ActualValueMultiplier: 1);
    }

    private static ItemField CreateBooleanField(
        string field,
        string displayName,
        SwShItemTableField tableField)
    {
        return new ItemField(
            field,
            displayName,
            MinimumValue: 0,
            MaximumValue: 1,
            TableField: tableField,
            ActualValueMultiplier: 1);
    }

    private static ItemField CreateBoostField(
        string field,
        string displayName,
        SwShItemTableField tableField)
    {
        return new ItemField(
            field,
            displayName,
            MinimumValue: 0,
            MaximumValue: SwShItemsWorkflowService.MaximumBoostValue,
            TableField: tableField,
            ActualValueMultiplier: 1);
    }

    private static ItemField CreateSignedByteField(
        string field,
        string displayName,
        SwShItemTableField tableField)
    {
        return new ItemField(
            field,
            displayName,
            MinimumValue: SwShItemsWorkflowService.MinimumSignedByteValue,
            MaximumValue: SwShItemsWorkflowService.MaximumSignedByteValue,
            TableField: tableField,
            ActualValueMultiplier: 1);
    }

    private static EditSession ReplacePendingItemEdit(
        EditSession session,
        PendingEdit pendingEdit,
        SwShItemsWorkflow workflow)
    {
        var replacementTarget = CreateStorageAssignment(
            workflow,
            pendingEdit,
            new List<ValidationDiagnostic>());
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameStoredTarget(workflow, edit, replacementTarget))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameStoredTarget(
        SwShItemsWorkflow workflow,
        PendingEdit candidate,
        StorageAssignment? replacementTarget)
    {
        if (replacementTarget is null
            || !string.Equals(candidate.Domain, ItemsEditDomain, StringComparison.Ordinal))
        {
            return false;
        }

        var candidateTarget = CreateStorageAssignment(
            workflow,
            candidate,
            new List<ValidationDiagnostic>());

        return candidateTarget is not null
            && string.Equals(candidateTarget.Owner, replacementTarget.Owner, StringComparison.Ordinal)
            && string.Equals(candidateTarget.StorageUnit, replacementTarget.StorageUnit, StringComparison.Ordinal)
            && candidateTarget.Mask == replacementTarget.Mask;
    }

    private static SwShItemsWorkflow OverlayPendingEdit(
        SwShItemsWorkflow workflow,
        PendingEdit edit)
    {
        var itemField = GetEditableField(edit.Field);
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || itemField is null
            || !TryParseItemValue(edit.NewValue, itemField.MinimumValue, itemField.MaximumValue, out var itemValue))
        {
            return workflow;
        }

        return itemField.Field switch
        {
            BuyPriceField => OverlayItem(workflow, itemId, item => item with
            {
                BuyPrice = itemValue,
                SellPrice = itemValue / 2,
            }),
            SellPriceField => OverlayItem(workflow, itemId, item => item with
            {
                BuyPrice = itemValue * itemField.ActualValueMultiplier,
                SellPrice = itemValue,
            }),
            WattsPriceField => OverlayItem(workflow, itemId, item => item with { WattsPrice = itemValue }),
            AlternatePriceField => OverlayItem(workflow, itemId, item => item with { AlternatePrice = itemValue }),
            PouchField => OverlayItemMetadata(workflow, itemId, item => item with { Pouch = itemValue }),
            PouchFlagsField => OverlayItemMetadata(workflow, itemId, item => item with { PouchFlags = itemValue }),
            FlingPowerField => OverlayItemMetadata(workflow, itemId, item => item with { FlingPower = itemValue }),
            FieldUseTypeField => OverlayItemMetadata(workflow, itemId, item => item with { FieldUseType = itemValue }),
            BattlePouchField => OverlayItemMetadata(workflow, itemId, item => item with { FieldFlags = itemValue }),
            CanUseOnPokemonField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CanUseOnPokemon = itemValue != 0 }),
            ItemTypeField => OverlayItemMetadata(workflow, itemId, item => item with { ItemType = itemValue }),
            SortIndexField => OverlayItemMetadata(workflow, itemId, item => item with { SortIndex = itemValue }),
            ItemSpriteField => OverlayItemMetadata(workflow, itemId, item => item with { ItemSprite = itemValue }),
            GroupTypeField => OverlayItemMetadata(workflow, itemId, item => item with { GroupType = itemValue }),
            GroupIndexField => OverlayItemMetadata(workflow, itemId, item => item with { GroupIndex = itemValue }),
            CureStatusFlagsField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CureStatusFlags = itemValue }),
            CureSleepField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CureStatusFlags = SetBit(item.CureStatusFlags, 0, itemValue != 0) }),
            CurePoisonField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CureStatusFlags = SetBit(item.CureStatusFlags, 1, itemValue != 0) }),
            CureBurnField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CureStatusFlags = SetBit(item.CureStatusFlags, 2, itemValue != 0) }),
            CureFreezeField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CureStatusFlags = SetBit(item.CureStatusFlags, 3, itemValue != 0) }),
            CureParalysisField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CureStatusFlags = SetBit(item.CureStatusFlags, 4, itemValue != 0) }),
            CureConfusionField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CureStatusFlags = SetBit(item.CureStatusFlags, 5, itemValue != 0) }),
            CureInfatuationField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CureStatusFlags = SetBit(item.CureStatusFlags, 6, itemValue != 0) }),
            GuardSpecField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { CureStatusFlags = SetBit(item.CureStatusFlags, 7, itemValue != 0) }),
            CanTargetFaintedPokemonField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost0 = SetBit(item.Boost0, 0, itemValue != 0) }),
            RevivesWholePartyField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost0 = SetBit(item.Boost0, 1, itemValue != 0) }),
            LevelUpItemField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost0 = SetBit(item.Boost0, 2, itemValue != 0) }),
            EvolutionItemField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost0 = SetBit(item.Boost0, 3, itemValue != 0) }),
            AttackBoostField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost0 = SetHighNibble(item.Boost0, itemValue) }),
            DefenseBoostField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost1 = SetLowNibble(item.Boost1, itemValue) }),
            SpecialAttackBoostField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost1 = SetHighNibble(item.Boost1, itemValue) }),
            SpecialDefenseBoostField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost2 = SetLowNibble(item.Boost2, itemValue) }),
            SpeedBoostField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost2 = SetHighNibble(item.Boost2, itemValue) }),
            AccuracyBoostField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost3 = SetLowNibble(item.Boost3, itemValue) }),
            CriticalHitBoostField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost3 = SetPackedBits(item.Boost3, bitOffset: 4, bitCount: 2, itemValue) }),
            PpUpFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost3 = SetBit(item.Boost3, 6, itemValue != 0) }),
            PpMaxFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { Boost3 = SetBit(item.Boost3, 7, itemValue != 0) }),
            UseFlags1Field => OverlayItemMetadata(workflow, itemId, item => item with { UseFlags1 = itemValue }),
            UseFlags2Field => OverlayItemMetadata(workflow, itemId, item => item with { UseFlags2 = itemValue }),
            RestorePpFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags1 = SetBit(item.UseFlags1, 0, itemValue != 0) }),
            RestoreAllPpFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags1 = SetBit(item.UseFlags1, 1, itemValue != 0) }),
            RestoreHpFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags1 = SetBit(item.UseFlags1, 2, itemValue != 0) }),
            HpEvFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags1 = SetBit(item.UseFlags1, 3, itemValue != 0) }),
            AttackEvFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags1 = SetBit(item.UseFlags1, 4, itemValue != 0) }),
            DefenseEvFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags1 = SetBit(item.UseFlags1, 5, itemValue != 0) }),
            SpeedEvFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags1 = SetBit(item.UseFlags1, 6, itemValue != 0) }),
            SpecialAttackEvFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags1 = SetBit(item.UseFlags1, 7, itemValue != 0) }),
            SpecialDefenseEvFlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags2 = SetBit(item.UseFlags2, 0, itemValue != 0) }),
            EvAbove100FlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags2 = SetBit(item.UseFlags2, 1, itemValue != 0) }),
            Friendship1FlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags2 = SetBit(item.UseFlags2, 2, itemValue != 0) }),
            Friendship2FlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags2 = SetBit(item.UseFlags2, 3, itemValue != 0) }),
            Friendship3FlagField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { UseFlags2 = SetBit(item.UseFlags2, 4, itemValue != 0) }),
            EvHpField => OverlayItemMetadata(workflow, itemId, item => item with { EvHp = itemValue }),
            EvAttackField => OverlayItemMetadata(workflow, itemId, item => item with { EvAttack = itemValue }),
            EvDefenseField => OverlayItemMetadata(workflow, itemId, item => item with { EvDefense = itemValue }),
            EvSpeedField => OverlayItemMetadata(workflow, itemId, item => item with { EvSpeed = itemValue }),
            EvSpecialAttackField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { EvSpecialAttack = itemValue }),
            EvSpecialDefenseField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { EvSpecialDefense = itemValue }),
            HealAmountField => OverlayItemMetadata(workflow, itemId, item => item with { HealAmount = itemValue }),
            PpGainField => OverlayItemMetadata(workflow, itemId, item => item with { PpGain = itemValue }),
            FriendshipGain1Field => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { FriendshipGain1 = itemValue }),
            FriendshipGain2Field => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { FriendshipGain2 = itemValue }),
            FriendshipGain3Field => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with { FriendshipGain3 = itemValue }),
            MachineMoveIdField => OverlayMachineMove(workflow, itemId, itemValue),
            _ => workflow,
        };
    }

    private static SwShItemsWorkflow OverlayItem(
        SwShItemsWorkflow workflow,
        int itemId,
        Func<SwShItemRecord, SwShItemRecord> update)
    {
        var selectedItem = workflow.Items.FirstOrDefault(item => item.ItemId == itemId);
        if (selectedItem is null)
        {
            return workflow;
        }

        var affectedIds = selectedItem.SharedItemIds.ToHashSet();
        var items = workflow.Items
            .Select(item => affectedIds.Contains(item.ItemId) ? RefreshFieldValues(update(item)) : item)
            .ToArray();

        return workflow with { Items = items };
    }

    private static SwShItemsWorkflow OverlayMachineMove(
        SwShItemsWorkflow workflow,
        int itemId,
        int moveId)
    {
        var selectedItem = workflow.Items.FirstOrDefault(item => item.ItemId == itemId);
        if (selectedItem?.Metadata.MachineSlot is not { } slot)
        {
            return workflow;
        }

        var moveName = ResolveMoveName(workflow, moveId);
        var machineLabel = SwShItemsWorkflowService.FormatMachineLabel(slot, includeSlot: false);
        var items = workflow.Items.Select(item =>
        {
            if (item.Metadata.MachineSlot != slot || item.Metadata.MachineMoveId is null)
            {
                return item;
            }

            var metadata = item.Metadata with
            {
                MachineMoveId = moveId,
                MachineMoveName = moveName,
            };
            return RefreshFieldValues(item with
            {
                Name = string.IsNullOrWhiteSpace(machineLabel)
                    ? item.Name
                    : $"{machineLabel} ({moveName ?? moveId.ToString(CultureInfo.InvariantCulture)})",
                Metadata = metadata,
                DetailGroups = SwShItemsWorkflowService.CreateDetailGroups(metadata),
            });
        }).ToArray();

        return workflow with { Items = items };
    }

    private static SwShItemRecord RefreshFieldValues(SwShItemRecord item)
    {
        return item with
        {
            FieldValues = SwShItemsWorkflowService.CreateFieldValues(
                item.BuyPrice,
                item.SellPrice,
                item.WattsPrice,
                item.AlternatePrice,
                item.Metadata),
        };
    }

    private static string? ResolveMoveName(SwShItemsWorkflow workflow, int moveId)
    {
        var label = workflow.EditableFields
            .FirstOrDefault(field => field.Field == MachineMoveIdField)
            ?.Options
            .FirstOrDefault(option => option.Value == moveId)
            ?.Label;
        var prefix = string.Create(CultureInfo.InvariantCulture, $"{moveId:000} ");

        return label is not null && label.StartsWith(prefix, StringComparison.Ordinal)
            ? label[prefix.Length..]
            : null;
    }

    private static int SetBit(int value, int bitOffset, bool enabled)
    {
        var mask = 1 << bitOffset;
        return enabled ? value | mask : value & ~mask;
    }

    private static int SetLowNibble(int value, int nibble) => (value & 0xF0) | (nibble & 0x0F);

    private static int SetHighNibble(int value, int nibble) => (value & 0x0F) | ((nibble & 0x0F) << 4);

    private static int SetPackedBits(int value, int bitOffset, int bitCount, int newValue)
    {
        var mask = ((1 << bitCount) - 1) << bitOffset;
        return (value & ~mask) | ((newValue << bitOffset) & mask);
    }

    private static SwShItemsWorkflow OverlayItemMetadata(
        SwShItemsWorkflow workflow,
        int itemId,
        Func<SwShItemMetadata, SwShItemMetadata> update)
    {
        return OverlayItem(workflow, itemId, item =>
        {
            var metadata = update(item.Metadata);
            return item with
            {
                Category = SwShItemsWorkflowService.FormatPouch(metadata.Pouch),
                Metadata = metadata,
                DetailGroups = SwShItemsWorkflowService.CreateDetailGroups(metadata),
            };
        });
    }

    private static SwShItemsWorkflow OverlayPendingEdits(
        SwShItemsWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in GetItemEdits(edits))
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static PlannedFileWrite CreatePlannedWrite(ProjectPaths paths, IReadOnlyList<PendingEdit> edits)
    {
        var targetRelativePath = SwShItemsWorkflowService.ItemDataPath;
        var targetPath = SwShItemsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        var sources = edits
            .SelectMany(edit => edit.Sources)
            .Distinct()
            .ToArray();
        var reason = edits.Count == 1
            ? $"Apply pending Items edit: {edits[0].Summary}"
            : $"Apply {edits.Count} pending Items edits: {string.Join(" ", edits.Select(edit => edit.Summary))}";

        return new PlannedFileWrite(
            targetRelativePath,
            sources,
            !string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath),
            reason);
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
                "Items apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items apply target must be relative to the output root.",
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShItemsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items apply target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static SwShItemTableEdit? ToItemTableEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemField = GetEditableField(edit.Field);
        var itemValue = TryParsePendingEditValue(edit, diagnostics);

        if (itemField is null || itemValue is null)
        {
            return null;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit does not include a valid item ID.",
                field: "itemId",
                expected: "Existing item record"));
            return null;
        }

        return new SwShItemTableEdit(
            itemId,
            itemField.TableField,
            checked(itemValue.Value * itemField.ActualValueMultiplier));
    }

    private void WriteOutputAtomically(string targetPath, byte[] contents)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("Items output target is a directory.");
        }

        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Items output target directory could not be resolved.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            temporaryFileWriter(tempPath, contents);
            if (!File.Exists(tempPath)
                || !File.ReadAllBytes(tempPath).AsSpan().SequenceEqual(contents))
            {
                throw new IOException("Items temporary output verification failed.");
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A failed temporary cleanup never alters the existing destination.
            }
        }
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

    private static string CreatePendingEditSummary(
        SwShItemRecord item,
        ItemField itemField,
        int itemValue)
    {
        var sharedRowSuffix = item.SharedItemIds.Count > 1
            ? $" Shared row also affects item IDs {string.Join(", ", item.SharedItemIds.Where(id => id != item.ItemId))}."
            : string.Empty;
        var derivedSuffix = itemField.Field == SellPriceField
            ? $" Stored buy price will become {itemValue * itemField.ActualValueMultiplier}."
            : string.Empty;

        return $"Set {item.Name} {itemField.DisplayName} to {itemValue}.{derivedSuffix}{sharedRowSuffix}";
    }

    private static ValidationDiagnostic CreateItemValueRangeDiagnostic(ItemField itemField)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Item {itemField.DisplayName} must be between {itemField.MinimumValue} and {itemField.MaximumValue}.",
            field: itemField.Field,
            expected: $"Safe item {itemField.DisplayName}");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Item field '{field}' is not supported by the Items workflow yet.",
            field: "field",
            expected: string.Join(", ", SupportedFieldNames()));
    }

    private static IReadOnlyList<string> SupportedFieldNames()
    {
        return
        [
            BuyPriceField,
            SellPriceField,
            WattsPriceField,
            AlternatePriceField,
            PouchField,
            PouchFlagsField,
            FlingPowerField,
            FieldUseTypeField,
            BattlePouchField,
            FieldFlagsField,
            CanUseOnPokemonField,
            ItemTypeField,
            SortIndexField,
            ItemSpriteField,
            GroupTypeField,
            GroupIndexField,
            CureStatusFlagsField,
            CureSleepField,
            CurePoisonField,
            CureBurnField,
            CureFreezeField,
            CureParalysisField,
            CureConfusionField,
            CureInfatuationField,
            GuardSpecField,
            CanTargetFaintedPokemonField,
            RevivesWholePartyField,
            LevelUpItemField,
            EvolutionItemField,
            AttackBoostField,
            DefenseBoostField,
            SpecialAttackBoostField,
            SpecialDefenseBoostField,
            SpeedBoostField,
            AccuracyBoostField,
            CriticalHitBoostField,
            PpUpFlagField,
            PpMaxFlagField,
            UseFlags1Field,
            UseFlags2Field,
            RestorePpFlagField,
            RestoreAllPpFlagField,
            RestoreHpFlagField,
            HpEvFlagField,
            AttackEvFlagField,
            DefenseEvFlagField,
            SpeedEvFlagField,
            SpecialAttackEvFlagField,
            SpecialDefenseEvFlagField,
            EvAbove100FlagField,
            Friendship1FlagField,
            Friendship2FlagField,
            Friendship3FlagField,
            EvHpField,
            EvAttackField,
            EvDefenseField,
            EvSpeedField,
            EvSpecialAttackField,
            EvSpecialDefenseField,
            HealAmountField,
            PpGainField,
            FriendshipGain1Field,
            FriendshipGain2Field,
            FriendshipGain3Field,
            MachineMoveIdField,
        ];
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: ItemsEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record ItemField(
        string Field,
        string DisplayName,
        int MinimumValue,
        int MaximumValue,
        SwShItemTableField TableField,
        int ActualValueMultiplier);

    private sealed record StorageAssignment(
        string Owner,
        string StorageUnit,
        ulong Mask,
        ulong Bits,
        PendingEdit Edit);
}
