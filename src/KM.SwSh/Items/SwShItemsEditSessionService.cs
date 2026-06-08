// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
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
    public const string FieldFlagsField = SwShItemsWorkflowService.FieldFlagsField;
    public const string CanUseOnPokemonField = SwShItemsWorkflowService.CanUseOnPokemonField;
    public const string ItemTypeField = SwShItemsWorkflowService.ItemTypeField;
    public const string SortIndexField = SwShItemsWorkflowService.SortIndexField;
    public const string ItemSpriteField = SwShItemsWorkflowService.ItemSpriteField;
    public const string GroupTypeField = SwShItemsWorkflowService.GroupTypeField;
    public const string GroupIndexField = SwShItemsWorkflowService.GroupIndexField;
    public const string CureStatusFlagsField = SwShItemsWorkflowService.CureStatusFlagsField;
    public const string UseFlags1Field = SwShItemsWorkflowService.UseFlags1Field;
    public const string UseFlags2Field = SwShItemsWorkflowService.UseFlags2Field;
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

    public SwShItemsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
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
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = itemsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditItems(project, workflow, diagnostics))
        {
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var selectedItem = workflow.Items.FirstOrDefault(item => item.ItemId == itemId);
        if (selectedItem is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item {itemId} is not present in the loaded Items workflow.",
                field: "itemId",
                expected: "Existing item record"));
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedItem, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShItemsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingItemEdit(currentSession, pendingEdit);

        return new SwShItemsEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
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

        CanEditItems(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending item change is valid."));
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

        if (session.PendingEdits.Count == 0)
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

        var writes = session.PendingEdits.Count == 0
            ? Array.Empty<PlannedFileWrite>()
            : [CreatePlannedWrite(paths, session.PendingEdits)];

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Length} target file{(writes.Length == 1 ? string.Empty : "s")}."));

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
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Items change plan"));
        }

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
            var itemTableEdits = session.PendingEdits
                .Select(edit => ToItemTableEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var output = itemTable.WriteEdits(itemTableEdits);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
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

        CanEditMachineMove(selectedItem, itemField, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShItemRecord selectedItem,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var itemField = GetEditableField(normalizedField);
        if (itemField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (!TryParseItemValue(value, itemField.MinimumValue, itemField.MaximumValue, out var itemValue))
        {
            diagnostics.Add(CreateItemValueRangeDiagnostic(itemField));
            return null;
        }

        if (!CanEditMachineMove(selectedItem, itemField, diagnostics))
        {
            return null;
        }

        return new PendingEdit(
            ItemsEditDomain,
            CreatePendingEditSummary(selectedItem, itemField, itemValue),
            [new ProjectFileReference(selectedItem.Provenance.SourceLayer, selectedItem.Provenance.SourceFile)],
            RecordId: selectedItem.ItemId.ToString(CultureInfo.InvariantCulture),
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
        SwShItemRecord selectedItem,
        ItemField itemField,
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

        return true;
    }

    private static ItemField? GetEditableField(string? field)
    {
        return field switch
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
            FieldFlagsField => CreateByteField(FieldFlagsField, "field flags", SwShItemTableField.FieldFlags),
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
            UseFlags1Field => CreateByteField(UseFlags1Field, "use flags 1", SwShItemTableField.UseFlags1),
            UseFlags2Field => CreateByteField(UseFlags2Field, "use flags 2", SwShItemTableField.UseFlags2),
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

    private static EditSession ReplacePendingItemEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameItemTableFieldEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameItemTableFieldEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        var candidateField = GetEditableField(candidate.Field);
        var pendingField = GetEditableField(pendingEdit.Field);

        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && candidateField is not null
            && pendingField is not null
            && candidateField.TableField == pendingField.TableField;
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
            FieldFlagsField => OverlayItemMetadata(workflow, itemId, item => item with { FieldFlags = itemValue }),
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
            UseFlags1Field => OverlayItemMetadata(workflow, itemId, item => item with { UseFlags1 = itemValue }),
            UseFlags2Field => OverlayItemMetadata(workflow, itemId, item => item with { UseFlags2 = itemValue }),
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
            MachineMoveIdField => OverlayItemMetadata(
                workflow,
                itemId,
                item => item with
                {
                    MachineMoveId = itemValue,
                    MachineMoveName = ResolveMoveName(workflow, itemValue),
                }),
            _ => workflow,
        };
    }

    private static SwShItemsWorkflow OverlayItem(
        SwShItemsWorkflow workflow,
        int itemId,
        Func<SwShItemRecord, SwShItemRecord> update)
    {
        var items = workflow.Items
            .Select(item => item.ItemId == itemId ? update(item) : item)
            .ToArray();

        return workflow with { Items = items };
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

        foreach (var edit in edits)
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
            FieldFlagsField,
            CanUseOnPokemonField,
            ItemTypeField,
            SortIndexField,
            ItemSpriteField,
            GroupTypeField,
            GroupIndexField,
            CureStatusFlagsField,
            UseFlags1Field,
            UseFlags2Field,
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
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
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
}
