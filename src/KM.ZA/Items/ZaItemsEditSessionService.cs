// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

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

        var pendingEdit = CreatePendingEdit(item, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaItemsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
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

            var pendingEdit = CreatePendingEdit(item, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
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
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.ItemsDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Items change is valid.",
                ZaEditSessionSupport.ItemsDomain));
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

        var validation = Validate(paths, session);
        return ZaEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            session,
            ZaEditSessionSupport.ItemsDomain,
            ZaDataPaths.ItemDataArray,
            "Items",
            validation.Diagnostics,
            outputMode);
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
            var source = fileSource.Read(project, ZaDataPaths.ItemDataArray);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaWorkflowFileSource.Write(paths, ZaDataPaths.ItemDataArray, WriteRows(rows), outputMode);
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.ItemDataArray, outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Items", outputMode),
                ZaEditSessionSupport.ItemsDomain));
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

    private static PendingEdit? CreatePendingEdit(
        ZaItemRecord item,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var parsedValue = TryParseEditableValue(normalizedField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        var editableField = ZaItemsWorkflowService.GetEditableField(normalizedField)!;
        if (!CanEditTechnicalMachineField(item, editableField, diagnostics))
        {
            return null;
        }

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.ItemsDomain,
            $"Set {item.Name} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(item.Provenance.SourceLayer, item.Provenance.SourceFile),
            item.ItemId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
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

        var editableField = ZaItemsWorkflowService.GetEditableField(edit.Field);
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

        _ = TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
    }

    private static bool CanEditTechnicalMachineField(
        ZaItemRecord item,
        ZaItemEditableField field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field.Field is not ZaItemsWorkflowService.MachineMoveIdField
            and not ZaItemsWorkflowService.MachineIndexField)
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

    private static int? TryParseEditableValue(
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = ZaItemsWorkflowService.GetEditableField(field);
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
                edit.Field,
                edit.NewValue,
                new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        return workflow with
        {
            Items = workflow.Items
                .Select(item => item.ItemId == itemId ? OverlayItem(item, edit.Field, value) : item)
                .ToArray(),
        };
    }

    private static ZaItemRecord OverlayItem(ZaItemRecord item, string? field, int value)
    {
        var metadata = item.Metadata;
        return field switch
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
            ZaItemsWorkflowService.SortOrderField => item with { Metadata = metadata with { SortIndex = value } },
            ZaItemsWorkflowService.CanNotHoldField => item,
            ZaItemsWorkflowService.MachineMoveIdField => item with
            {
                Metadata = metadata with
                {
                    MachineMoveId = value > 0 ? value : null,
                    MachineMoveName = value > 0 ? ZaLabels.Move(value) : null,
                },
            },
            ZaItemsWorkflowService.MachineIndexField => item with
            {
                Metadata = metadata with { MachineSlot = value >= 0 ? value : null, GroupIndex = value },
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
    }

    private static int SetFlag(int flags, int bit, bool enabled)
    {
        return enabled
            ? flags | (1 << bit)
            : flags & ~(1 << bit);
    }

    private static void ApplyEdit(
        IReadOnlyList<ItemRow> rows,
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
            case ZaItemsWorkflowService.MachineIndexField:
                row.MachineIndex = value;
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

    private static IReadOnlyList<ItemRow> ReadRows(byte[] bytes)
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
        public string IconName { get; init; } = string.Empty;
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
