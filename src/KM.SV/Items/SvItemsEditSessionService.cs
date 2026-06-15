// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Items;

internal sealed class SvItemsEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvItemsWorkflowService itemsWorkflowService;

    public SvItemsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvItemsWorkflowService? itemsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.itemsWorkflowService = itemsWorkflowService ?? new SvItemsWorkflowService(this.fileSource);
    }

    public SwShItemsEditResult UpdateField(
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

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.ItemsDomain,
                diagnostics))
        {
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var item = workflow.Items.FirstOrDefault(candidate => candidate.ItemId == itemId);
        if (item is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item {itemId} is not present in the loaded Items workflow.",
                SvEditSessionSupport.ItemsDomain,
                field: "itemId",
                expected: "Existing item record"));
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, item, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new SwShItemsEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = itemsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.ItemsDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Items change is valid.",
                SvEditSessionSupport.ItemsDomain));
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
        return SvEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            session,
            SvEditSessionSupport.ItemsDomain,
            SvDataPaths.ItemDataArray,
            "Items",
            validation.Diagnostics);
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

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                SvEditSessionSupport.ItemsDomain,
                expected: "Current reviewed Items change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, SvDataPaths.ItemDataArray);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            SvWorkflowFileSource.Write(paths, SvDataPaths.ItemDataArray, WriteRows(rows));
            writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.ItemDataArray));
            writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Items change plan and patched the Scarlet/Violet Trinity descriptor.",
                SvEditSessionSupport.ItemsDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items output could not be written: {exception.Message}",
                SvEditSessionSupport.ItemsDomain,
                file: $"romfs/{SvDataPaths.ItemDataArray}",
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShItemsWorkflow workflow,
        SwShItemRecord item,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, normalizedField, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item field '{normalizedField}' is not supported by Scarlet/Violet Items yet.",
                SvEditSessionSupport.ItemsDomain,
                field: "field",
                expected: "Supported S/V item field"));
            return null;
        }

        var parsedValue = SvEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            SvEditSessionSupport.ItemsDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.ItemsDomain,
            $"Set {item.Name} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(item.Provenance.SourceLayer, item.Provenance.SourceFile),
            item.ItemId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SwShItemsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.ItemsDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Items.",
                SvEditSessionSupport.ItemsDomain,
                expected: SvEditSessionSupport.ItemsDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || workflow.Items.All(item => item.ItemId != itemId))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit targets a record that is not loaded.",
                SvEditSessionSupport.ItemsDomain,
                field: "itemId",
                expected: "Existing item record"));
            return;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item field '{edit.Field}' is not supported by Scarlet/Violet Items yet.",
                SvEditSessionSupport.ItemsDomain,
                field: "field",
                expected: "Supported S/V item field"));
            return;
        }

        _ = SvEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            SvEditSessionSupport.ItemsDomain,
            diagnostics);
    }

    private static SwShItemsWorkflow OverlayPendingEdits(SwShItemsWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShItemsWorkflow OverlayPendingEdit(SwShItemsWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.ItemsDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || SvEditSessionSupport.TryParseInt(
                edit.NewValue,
                int.MinValue,
                int.MaxValue,
                edit.Field,
                SvEditSessionSupport.ItemsDomain,
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

    private static SwShItemRecord OverlayItem(SwShItemRecord item, string? field, int value)
    {
        var metadata = item.Metadata;
        return field switch
        {
            SwShItemsWorkflowService.BuyPriceField => item with { BuyPrice = value, SellPrice = value / 2 },
            SwShItemsWorkflowService.WattsPriceField => item with { WattsPrice = value },
            SwShItemsWorkflowService.PouchField => item with { Metadata = metadata with { Pouch = value } },
            SwShItemsWorkflowService.FlingPowerField => item with { Metadata = metadata with { FlingPower = value } },
            SwShItemsWorkflowService.FieldUseTypeField => item with { Metadata = metadata with { FieldUseType = value } },
            SwShItemsWorkflowService.CanUseOnPokemonField => item with { Metadata = metadata with { CanUseOnPokemon = value != 0 } },
            SwShItemsWorkflowService.ItemTypeField => item with { Metadata = metadata with { ItemType = value } },
            SwShItemsWorkflowService.SortIndexField => item with { Metadata = metadata with { SortIndex = value } },
            SwShItemsWorkflowService.GroupTypeField => item with { Metadata = metadata with { GroupType = value } },
            SwShItemsWorkflowService.GroupIndexField => item with { Metadata = metadata with { GroupIndex = value } },
            SwShItemsWorkflowService.AttackBoostField => item with { Metadata = metadata with { Boost0 = value } },
            SwShItemsWorkflowService.DefenseBoostField => item with { Metadata = metadata with { Boost1 = value } },
            SwShItemsWorkflowService.SpecialAttackBoostField => item with { Metadata = metadata with { Boost2 = value } },
            SwShItemsWorkflowService.SpecialDefenseBoostField => item with { Metadata = metadata with { Boost3 = value } },
            SwShItemsWorkflowService.SpeedBoostField => item with { Metadata = metadata with { UseFlags1 = value } },
            SwShItemsWorkflowService.AccuracyBoostField => item with { Metadata = metadata with { UseFlags2 = value } },
            SwShItemsWorkflowService.CriticalHitBoostField => item with { Metadata = metadata with { CureStatusFlags = value } },
            SwShItemsWorkflowService.EvHpField => item with { Metadata = metadata with { EvHp = value } },
            SwShItemsWorkflowService.EvAttackField => item with { Metadata = metadata with { EvAttack = value } },
            SwShItemsWorkflowService.EvDefenseField => item with { Metadata = metadata with { EvDefense = value } },
            SwShItemsWorkflowService.EvSpeedField => item with { Metadata = metadata with { EvSpeed = value } },
            SwShItemsWorkflowService.EvSpecialAttackField => item with { Metadata = metadata with { EvSpecialAttack = value } },
            SwShItemsWorkflowService.EvSpecialDefenseField => item with { Metadata = metadata with { EvSpecialDefense = value } },
            SwShItemsWorkflowService.HealAmountField => item with { Metadata = metadata with { HealAmount = value } },
            SwShItemsWorkflowService.PpGainField => item with { Metadata = metadata with { PpGain = value } },
            SwShItemsWorkflowService.FriendshipGain1Field => item with { Metadata = metadata with { FriendshipGain1 = value } },
            SwShItemsWorkflowService.FriendshipGain2Field => item with { Metadata = metadata with { FriendshipGain2 = value } },
            SwShItemsWorkflowService.FriendshipGain3Field => item with { Metadata = metadata with { FriendshipGain3 = value } },
            SwShItemsWorkflowService.MachineMoveIdField => item with
            {
                Metadata = metadata with
                {
                    MachineMoveId = value > 0 ? value : null,
                    MachineMoveName = value > 0 ? SvLabels.Move(value) : null,
                },
            },
            _ => item,
        };
    }

    private static void ApplyEdit(
        IReadOnlyList<ItemRow> rows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.ItemsDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending item edit is not valid for apply.",
                SvEditSessionSupport.ItemsDomain,
                expected: "Valid item edit"));
            return;
        }

        var row = rows.FirstOrDefault(candidate => candidate.Id == itemId);
        if (row is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item {itemId} is not present in the source item array.",
                SvEditSessionSupport.ItemsDomain,
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
            case SwShItemsWorkflowService.BuyPriceField:
                row.Price = value;
                break;
            case SwShItemsWorkflowService.WattsPriceField:
                row.BP = value;
                break;
            case SwShItemsWorkflowService.PouchField:
                row.FieldPocket = (global::FieldPocket)value;
                break;
            case SwShItemsWorkflowService.FlingPowerField:
                row.ThrowPower = value;
                break;
            case SwShItemsWorkflowService.FieldUseTypeField:
                row.FieldFunctionType = (global::FieldFunctionType)value;
                break;
            case SwShItemsWorkflowService.CanUseOnPokemonField:
                row.SetToPoke = value != 0;
                break;
            case SwShItemsWorkflowService.ItemTypeField:
                row.ItemType = (global::ItemType)value;
                break;
            case SwShItemsWorkflowService.SortIndexField:
                row.SortNum = value;
                break;
            case SwShItemsWorkflowService.GroupTypeField:
                row.ItemGroup = (global::ItemGroup)value;
                break;
            case SwShItemsWorkflowService.GroupIndexField:
                row.GroupID = value;
                break;
            case SwShItemsWorkflowService.AttackBoostField:
                row.WorkAttack = value;
                break;
            case SwShItemsWorkflowService.DefenseBoostField:
                row.WorkDefense = value;
                break;
            case SwShItemsWorkflowService.SpecialAttackBoostField:
                row.WorkSpAttack = value;
                break;
            case SwShItemsWorkflowService.SpecialDefenseBoostField:
                row.WorkSpDefense = value;
                break;
            case SwShItemsWorkflowService.SpeedBoostField:
                row.WorkSpeed = value;
                break;
            case SwShItemsWorkflowService.AccuracyBoostField:
                row.WorkAccuracy = value;
                break;
            case SwShItemsWorkflowService.CriticalHitBoostField:
                row.WorkCritical = value;
                break;
            case SwShItemsWorkflowService.EvHpField:
                row.WorkStatusHp = value;
                break;
            case SwShItemsWorkflowService.EvAttackField:
                row.WorkStatusAtk = value;
                break;
            case SwShItemsWorkflowService.EvDefenseField:
                row.WorkStatusDef = value;
                break;
            case SwShItemsWorkflowService.EvSpeedField:
                row.WorkStatusSpd = value;
                break;
            case SwShItemsWorkflowService.EvSpecialAttackField:
                row.WorkStatusSAtk = value;
                break;
            case SwShItemsWorkflowService.EvSpecialDefenseField:
                row.WorkStatusSDef = value;
                break;
            case SwShItemsWorkflowService.HealAmountField:
                row.WorkStatusHp = value;
                break;
            case SwShItemsWorkflowService.PpGainField:
                row.WorkPpRcv = value;
                break;
            case SwShItemsWorkflowService.FriendshipGain1Field:
                row.WorkFriendly1 = value;
                break;
            case SwShItemsWorkflowService.FriendshipGain2Field:
                row.WorkFriendly2 = value;
                break;
            case SwShItemsWorkflowService.FriendshipGain3Field:
                row.WorkFriendly3 = value;
                break;
            case SwShItemsWorkflowService.MachineMoveIdField:
                row.MachineWaza = (global::pml.common.WazaID)checked((ushort)value);
                break;
        }
    }

    private static IReadOnlyList<ItemRow> ReadRows(byte[] bytes)
    {
        var table = global::ItemDataArray.GetRootAsItemDataArray(new ByteBuffer(bytes));
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
        var offsets = rows
            .Select(row => row.Write(builder))
            .ToArray();
        var vector = global::ItemDataArray.CreateValuesVector(builder, offsets);
        var root = global::ItemDataArray.CreateItemDataArray(builder, vector);
        global::ItemDataArray.FinishItemDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private sealed class ItemRow
    {
        public int Id { get; init; }
        public global::ItemType ItemType { get; set; }
        public string? IconName { get; init; }
        public int Price { get; set; }
        public int BP { get; set; }
        public global::EquipEffect EquipEffect { get; init; }
        public int EquipPower { get; init; }
        public int ThrowPower { get; set; }
        public bool ThrowEffect { get; init; }
        public int NaturalGiftPower { get; init; }
        public int NaturalGiftType { get; init; }
        public global::PluckEffect PluckEffect { get; init; }
        public global::pml.common.WazaID MachineWaza { get; set; }
        public int SortNum { get; set; }
        public global::ItemGroup ItemGroup { get; set; }
        public int GroupID { get; set; }
        public global::FieldPocket FieldPocket { get; set; }
        public global::FieldFunctionType FieldFunctionType { get; set; }
        public global::BattleFunctionType BattleFunctionType { get; init; }
        public bool BattleUseLost { get; init; }
        public bool BattleBagSelect { get; init; }
        public bool BattleBagSelectTarget { get; init; }
        public bool NoSpend { get; init; }
        public bool SetToPoke { get; set; }
        public int SlotMaxNum { get; init; }
        public global::WorkType WorkType { get; init; }
        public int WorkCommon { get; init; }
        public int WorkEffectGuard { get; init; }
        public int WorkCritical { get; set; }
        public int WorkAttack { get; set; }
        public int WorkDefense { get; set; }
        public int WorkSpeed { get; set; }
        public int WorkAccuracy { get; set; }
        public int WorkSpAttack { get; set; }
        public int WorkSpDefense { get; set; }
        public int WorkLevel { get; init; }
        public global::WorkPpSelTgt WorkPpSelTgt { get; init; }
        public int WorkPpRcv { get; set; }
        public int WorkPpUp { get; init; }
        public int WorkStatusLimitCtrl { get; init; }
        public int WorkStatusHp { get; set; }
        public int WorkStatusAtk { get; set; }
        public int WorkStatusDef { get; set; }
        public int WorkStatusSpd { get; set; }
        public int WorkStatusSAtk { get; set; }
        public int WorkStatusSDef { get; set; }
        public int WorkFriendly1 { get; set; }
        public int WorkFriendly2 { get; set; }
        public int WorkFriendly3 { get; set; }
        public int WorkRecvSleep { get; init; }
        public int WorkRecvPoison { get; init; }
        public int WorkRecvBurn { get; init; }
        public int WorkRecvFreeze { get; init; }
        public int WorkRecvParalyze { get; init; }
        public int WorkRecvConfuse { get; init; }
        public int WorkRecvMero { get; init; }
        public int WorkRecvPower { get; init; }
        public int WorkRevival { get; init; }
        public int WorkEvolutional { get; init; }
        public int WorkRecvNemuke { get; init; }
        public int WorkRecvTousyou { get; init; }
        public int WorkWazaDrunk { get; init; }
        public int WorkAvoidUp { get; init; }
        public int WorkOffenseUp { get; init; }
        public int WorkOffDefInv { get; init; }

        public static ItemRow From(global::ItemData row)
        {
            return new ItemRow
            {
                Id = row.Id,
                ItemType = row.ItemType,
                IconName = row.IconName,
                Price = row.Price,
                BP = row.BP,
                EquipEffect = row.EquipEffect,
                EquipPower = row.EquipPower,
                ThrowPower = row.ThrowPower,
                ThrowEffect = row.ThrowEffect,
                NaturalGiftPower = row.NaturalGiftPower,
                NaturalGiftType = row.NaturalGiftType,
                PluckEffect = row.PluckEffect,
                MachineWaza = row.MachineWaza,
                SortNum = row.SortNum,
                ItemGroup = row.ItemGroup,
                GroupID = row.GroupID,
                FieldPocket = row.FieldPocket,
                FieldFunctionType = row.FieldFunctionType,
                BattleFunctionType = row.BattleFunctionType,
                BattleUseLost = row.BattleUseLost,
                BattleBagSelect = row.BattleBagSelect,
                BattleBagSelectTarget = row.BattleBagSelectTarget,
                NoSpend = row.NoSpend,
                SetToPoke = row.SetToPoke,
                SlotMaxNum = row.SlotMaxNum,
                WorkType = row.WorkType,
                WorkCommon = row.WorkCommon,
                WorkEffectGuard = row.WorkEffectGuard,
                WorkCritical = row.WorkCritical,
                WorkAttack = row.WorkAttack,
                WorkDefense = row.WorkDefense,
                WorkSpeed = row.WorkSpeed,
                WorkAccuracy = row.WorkAccuracy,
                WorkSpAttack = row.WorkSpAttack,
                WorkSpDefense = row.WorkSpDefense,
                WorkLevel = row.WorkLevel,
                WorkPpSelTgt = row.WorkPpSelTgt,
                WorkPpRcv = row.WorkPpRcv,
                WorkPpUp = row.WorkPpUp,
                WorkStatusLimitCtrl = row.WorkStatusLimitCtrl,
                WorkStatusHp = row.WorkStatusHp,
                WorkStatusAtk = row.WorkStatusAtk,
                WorkStatusDef = row.WorkStatusDef,
                WorkStatusSpd = row.WorkStatusSpd,
                WorkStatusSAtk = row.WorkStatusSAtk,
                WorkStatusSDef = row.WorkStatusSDef,
                WorkFriendly1 = row.WorkFriendly1,
                WorkFriendly2 = row.WorkFriendly2,
                WorkFriendly3 = row.WorkFriendly3,
                WorkRecvSleep = row.WorkRecvSleep,
                WorkRecvPoison = row.WorkRecvPoison,
                WorkRecvBurn = row.WorkRecvBurn,
                WorkRecvFreeze = row.WorkRecvFreeze,
                WorkRecvParalyze = row.WorkRecvParalyze,
                WorkRecvConfuse = row.WorkRecvConfuse,
                WorkRecvMero = row.WorkRecvMero,
                WorkRecvPower = row.WorkRecvPower,
                WorkRevival = row.WorkRevival,
                WorkEvolutional = row.WorkEvolutional,
                WorkRecvNemuke = row.WorkRecvNemuke,
                WorkRecvTousyou = row.WorkRecvTousyou,
                WorkWazaDrunk = row.WorkWazaDrunk,
                WorkAvoidUp = row.WorkAvoidUp,
                WorkOffenseUp = row.WorkOffenseUp,
                WorkOffDefInv = row.WorkOffDefInv,
            };
        }

        public Offset<global::ItemData> Write(FlatBufferBuilder builder)
        {
            var iconNameOffset = string.IsNullOrEmpty(IconName)
                ? default
                : builder.CreateString(IconName);

            return global::ItemData.CreateItemData(
                builder,
                Id,
                ItemType,
                iconNameOffset,
                Price,
                BP,
                EquipEffect,
                EquipPower,
                ThrowPower,
                ThrowEffect,
                NaturalGiftPower,
                NaturalGiftType,
                PluckEffect,
                MachineWaza,
                SortNum,
                ItemGroup,
                GroupID,
                FieldPocket,
                FieldFunctionType,
                BattleFunctionType,
                BattleUseLost,
                BattleBagSelect,
                BattleBagSelectTarget,
                NoSpend,
                SetToPoke,
                SlotMaxNum,
                WorkType,
                WorkCommon,
                WorkEffectGuard,
                WorkCritical,
                WorkAttack,
                WorkDefense,
                WorkSpeed,
                WorkAccuracy,
                WorkSpAttack,
                WorkSpDefense,
                WorkLevel,
                WorkPpSelTgt,
                WorkPpRcv,
                WorkPpUp,
                WorkStatusLimitCtrl,
                WorkStatusHp,
                WorkStatusAtk,
                WorkStatusDef,
                WorkStatusSpd,
                WorkStatusSAtk,
                WorkStatusSDef,
                WorkFriendly1,
                WorkFriendly2,
                WorkFriendly3,
                WorkRecvSleep,
                WorkRecvPoison,
                WorkRecvBurn,
                WorkRecvFreeze,
                WorkRecvParalyze,
                WorkRecvConfuse,
                WorkRecvMero,
                WorkRecvPower,
                WorkRevival,
                WorkEvolutional,
                WorkRecvNemuke,
                WorkRecvTousyou,
                WorkWazaDrunk,
                WorkAvoidUp,
                WorkOffenseUp,
                WorkOffDefInv);
        }
    }
}
