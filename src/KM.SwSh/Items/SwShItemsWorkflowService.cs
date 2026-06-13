// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Moves;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Items;

public sealed class SwShItemsWorkflowService
{
    public const string BuyPriceField = "buyPrice";
    public const string SellPriceField = "sellPrice";
    public const string WattsPriceField = "wattsPrice";
    public const string AlternatePriceField = "alternatePrice";
    public const string PouchField = "pouch";
    public const string PouchFlagsField = "pouchFlags";
    public const string FlingPowerField = "flingPower";
    public const string FieldUseTypeField = "fieldUseType";
    public const string FieldFlagsField = "fieldFlags";
    public const string CanUseOnPokemonField = "canUseOnPokemon";
    public const string ItemTypeField = "itemType";
    public const string SortIndexField = "sortIndex";
    public const string ItemSpriteField = "itemSprite";
    public const string GroupTypeField = "groupType";
    public const string GroupIndexField = "groupIndex";
    public const string CureStatusFlagsField = "cureStatusFlags";
    public const string CureSleepField = "cureSleep";
    public const string CurePoisonField = "curePoison";
    public const string CureBurnField = "cureBurn";
    public const string CureFreezeField = "cureFreeze";
    public const string CureParalysisField = "cureParalysis";
    public const string CureConfusionField = "cureConfusion";
    public const string CureInfatuationField = "cureInfatuation";
    public const string GuardSpecField = "guardSpec";
    public const string CanTargetFaintedPokemonField = "canTargetFaintedPokemon";
    public const string RevivesWholePartyField = "revivesWholeParty";
    public const string LevelUpItemField = "levelUpItem";
    public const string EvolutionItemField = "evolutionItem";
    public const string AttackBoostField = "attackBoost";
    public const string DefenseBoostField = "defenseBoost";
    public const string SpecialAttackBoostField = "specialAttackBoost";
    public const string SpecialDefenseBoostField = "specialDefenseBoost";
    public const string SpeedBoostField = "speedBoost";
    public const string AccuracyBoostField = "accuracyBoost";
    public const string CriticalHitBoostField = "criticalHitBoost";
    public const string PpUpFlagField = "ppUpFlag";
    public const string PpMaxFlagField = "ppMaxFlag";
    public const string UseFlags1Field = "useFlags1";
    public const string UseFlags2Field = "useFlags2";
    public const string RestorePpFlagField = "restorePpFlag";
    public const string RestoreAllPpFlagField = "restoreAllPpFlag";
    public const string RestoreHpFlagField = "restoreHpFlag";
    public const string HpEvFlagField = "hpEvFlag";
    public const string AttackEvFlagField = "attackEvFlag";
    public const string DefenseEvFlagField = "defenseEvFlag";
    public const string SpeedEvFlagField = "speedEvFlag";
    public const string SpecialAttackEvFlagField = "specialAttackEvFlag";
    public const string SpecialDefenseEvFlagField = "specialDefenseEvFlag";
    public const string EvAbove100FlagField = "evAbove100Flag";
    public const string Friendship1FlagField = "friendship1Flag";
    public const string Friendship2FlagField = "friendship2Flag";
    public const string Friendship3FlagField = "friendship3Flag";
    public const string EvHpField = "evHp";
    public const string EvAttackField = "evAttack";
    public const string EvDefenseField = "evDefense";
    public const string EvSpeedField = "evSpeed";
    public const string EvSpecialAttackField = "evSpecialAttack";
    public const string EvSpecialDefenseField = "evSpecialDefense";
    public const string HealAmountField = "healAmount";
    public const string PpGainField = "ppGain";
    public const string FriendshipGain1Field = "friendshipGain1";
    public const string FriendshipGain2Field = "friendshipGain2";
    public const string FriendshipGain3Field = "friendshipGain3";
    public const string MachineMoveIdField = "machineMoveId";
    public const int MaximumBuyPrice = 999_999;
    public const int MaximumSellPrice = MaximumBuyPrice / 2;
    public const int MaximumWattsPrice = 999_999;
    public const int MaximumAlternatePrice = 999_999;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MaximumPouchValue = 8;
    public const int MaximumPouchFlagsValue = 0x0F;
    public const int MaximumBoostValue = 0x0F;
    public const int MaximumCriticalHitBoostValue = 0x03;
    public const int MinimumSignedByteValue = sbyte.MinValue;
    public const int MaximumSignedByteValue = sbyte.MaxValue;
    public const int MaximumMoveId = ushort.MaxValue;
    public const string ItemDataPath = SwShItemTable.ItemDataRelativePath;
    public const string EnglishItemNamePath = "romfs/bin/message/English/common/itemname.dat";
    public const string EnglishMoveNamePath = "romfs/bin/message/English/common/wazaname.dat";

    private const int TechnicalRecordMachineGroup = 4;
    private const int TechnicalRecordFieldUseType = 2;
    private const int TechnicalRecordTrSlotStart = 100;
    private const int TechnicalRecordLastSlot = 199;

    private static readonly IReadOnlyList<SwShItemEditableFieldOption> BooleanOptions =
    [
        new SwShItemEditableFieldOption(0, "No"),
        new SwShItemEditableFieldOption(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SwShItemEditableFieldOption> BoostStageOptions =
        Enumerable.Range(0, MaximumBoostValue + 1)
            .Select(value => new SwShItemEditableFieldOption(value, FormatBoostStageOption(value)))
            .ToArray();

    private static readonly IReadOnlyList<SwShItemEditableFieldOption> CriticalHitBoostOptions =
        Enumerable.Range(0, MaximumCriticalHitBoostValue + 1)
            .Select(value => new SwShItemEditableFieldOption(value, FormatPositiveBoostStage(value)))
            .ToArray();

    private static readonly IReadOnlyList<SwShItemEditableFieldOption> PouchOptions =
        Enumerable.Range(0, MaximumPouchValue + 1)
            .Select(value => new SwShItemEditableFieldOption(value, FormatPouch(value)))
            .ToArray();

    private static readonly IReadOnlyList<SwShItemEditableFieldOption> FieldUseTypeOptions =
    [
        new SwShItemEditableFieldOption(0, "Inert"),
        new SwShItemEditableFieldOption(1, "Medicine"),
        new SwShItemEditableFieldOption(2, "TM/TR"),
        new SwShItemEditableFieldOption(5, "Spray"),
        new SwShItemEditableFieldOption(6, "Evolution"),
        new SwShItemEditableFieldOption(7, "Escape Rope"),
        new SwShItemEditableFieldOption(12, "Berry"),
        new SwShItemEditableFieldOption(15, "Form Change"),
    ];

    private static readonly IReadOnlyList<SwShItemEditableField> BaseEditableFields =
    [
        new SwShItemEditableField(
            BuyPriceField,
            "Buy price",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumBuyPrice,
            Options: []),
        new SwShItemEditableField(
            SellPriceField,
            "Sell price",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumSellPrice,
            Options: []),
        new SwShItemEditableField(
            WattsPriceField,
            "Watts price",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumWattsPrice,
            Options: []),
        new SwShItemEditableField(
            AlternatePriceField,
            "Alternate price",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumAlternatePrice,
            Options: []),
        new SwShItemEditableField(
            PouchField,
            "Pouch",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumPouchValue,
            Options: PouchOptions),
        new SwShItemEditableField(
            PouchFlagsField,
            "Pouch flags",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumPouchFlagsValue,
            Options: []),
        new SwShItemEditableField(
            FlingPowerField,
            "Fling power",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        new SwShItemEditableField(
            FieldUseTypeField,
            "Field use type",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: FieldUseTypeOptions),
        new SwShItemEditableField(
            FieldFlagsField,
            "Field flags (unknown raw)",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        new SwShItemEditableField(
            CanUseOnPokemonField,
            "Can use on Pokemon",
            "boolean",
            MinimumValue: 0,
            MaximumValue: 1,
            Options: BooleanOptions),
        new SwShItemEditableField(
            ItemTypeField,
            "Item type",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        new SwShItemEditableField(
            SortIndexField,
            "Sort index",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        new SwShItemEditableField(
            ItemSpriteField,
            "Sprite",
            "integer",
            MinimumValue: short.MinValue,
            MaximumValue: short.MaxValue,
            Options: []),
        new SwShItemEditableField(
            GroupTypeField,
            "Group type",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        new SwShItemEditableField(
            GroupIndexField,
            "Group index",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        new SwShItemEditableField(
            CureStatusFlagsField,
            "Cure status flags",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        CreateBooleanEditableField(CureSleepField, "Cures sleep"),
        CreateBooleanEditableField(CurePoisonField, "Cures poison"),
        CreateBooleanEditableField(CureBurnField, "Cures burn"),
        CreateBooleanEditableField(CureFreezeField, "Cures freeze"),
        CreateBooleanEditableField(CureParalysisField, "Cures paralysis"),
        CreateBooleanEditableField(CureConfusionField, "Cures confusion"),
        CreateBooleanEditableField(CureInfatuationField, "Cures infatuation"),
        CreateBooleanEditableField(GuardSpecField, "Guard Spec. effect"),
        CreateBooleanEditableField(CanTargetFaintedPokemonField, "Can target fainted Pokemon"),
        CreateBooleanEditableField(RevivesWholePartyField, "Revives whole party"),
        CreateBooleanEditableField(LevelUpItemField, "Level up item"),
        CreateBooleanEditableField(EvolutionItemField, "Evolution item"),
        CreateBoostEditableField(AttackBoostField, "Attack boost"),
        CreateBoostEditableField(DefenseBoostField, "Defense boost"),
        CreateBoostEditableField(SpecialAttackBoostField, "Sp. Atk boost"),
        CreateBoostEditableField(SpecialDefenseBoostField, "Sp. Def boost"),
        CreateBoostEditableField(SpeedBoostField, "Speed boost"),
        CreateBoostEditableField(AccuracyBoostField, "Accuracy boost"),
        new SwShItemEditableField(
            CriticalHitBoostField,
            "Critical hit boost",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumCriticalHitBoostValue,
            Options: CriticalHitBoostOptions),
        CreateBooleanEditableField(PpUpFlagField, "PP Up flag"),
        CreateBooleanEditableField(PpMaxFlagField, "PP Max flag"),
        new SwShItemEditableField(
            UseFlags1Field,
            "Use flags 1 (raw)",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        new SwShItemEditableField(
            UseFlags2Field,
            "Use flags 2 (raw)",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        CreateBooleanEditableField(RestorePpFlagField, "Restore PP flag"),
        CreateBooleanEditableField(RestoreAllPpFlagField, "Restore all PP flag"),
        CreateBooleanEditableField(RestoreHpFlagField, "Restore HP flag"),
        CreateBooleanEditableField(HpEvFlagField, "HP EV flag"),
        CreateBooleanEditableField(AttackEvFlagField, "Attack EV flag"),
        CreateBooleanEditableField(DefenseEvFlagField, "Defense EV flag"),
        CreateBooleanEditableField(SpeedEvFlagField, "Speed EV flag"),
        CreateBooleanEditableField(SpecialAttackEvFlagField, "Sp. Atk EV flag"),
        CreateBooleanEditableField(SpecialDefenseEvFlagField, "Sp. Def EV flag"),
        CreateBooleanEditableField(EvAbove100FlagField, "EV above 100 flag"),
        CreateBooleanEditableField(Friendship1FlagField, "Friendship 1 flag"),
        CreateBooleanEditableField(Friendship2FlagField, "Friendship 2 flag"),
        CreateBooleanEditableField(Friendship3FlagField, "Friendship 3 flag"),
        new SwShItemEditableField(
            EvHpField,
            "HP EV gain",
            "integer",
            MinimumValue: MinimumSignedByteValue,
            MaximumValue: MaximumSignedByteValue,
            Options: []),
        new SwShItemEditableField(
            EvAttackField,
            "Attack EV gain",
            "integer",
            MinimumValue: MinimumSignedByteValue,
            MaximumValue: MaximumSignedByteValue,
            Options: []),
        new SwShItemEditableField(
            EvDefenseField,
            "Defense EV gain",
            "integer",
            MinimumValue: MinimumSignedByteValue,
            MaximumValue: MaximumSignedByteValue,
            Options: []),
        new SwShItemEditableField(
            EvSpeedField,
            "Speed EV gain",
            "integer",
            MinimumValue: MinimumSignedByteValue,
            MaximumValue: MaximumSignedByteValue,
            Options: []),
        new SwShItemEditableField(
            EvSpecialAttackField,
            "Sp. Atk EV gain",
            "integer",
            MinimumValue: MinimumSignedByteValue,
            MaximumValue: MaximumSignedByteValue,
            Options: []),
        new SwShItemEditableField(
            EvSpecialDefenseField,
            "Sp. Def EV gain",
            "integer",
            MinimumValue: MinimumSignedByteValue,
            MaximumValue: MaximumSignedByteValue,
            Options: []),
        new SwShItemEditableField(
            HealAmountField,
            "Heal amount",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        new SwShItemEditableField(
            PpGainField,
            "PP gain",
            "integer",
            MinimumValue: 0,
            MaximumValue: MaximumByteValue,
            Options: []),
        new SwShItemEditableField(
            FriendshipGain1Field,
            "Friendship gain 1",
            "integer",
            MinimumValue: MinimumSignedByteValue,
            MaximumValue: MaximumSignedByteValue,
            Options: []),
        new SwShItemEditableField(
            FriendshipGain2Field,
            "Friendship gain 2",
            "integer",
            MinimumValue: MinimumSignedByteValue,
            MaximumValue: MaximumSignedByteValue,
            Options: []),
        new SwShItemEditableField(
            FriendshipGain3Field,
            "Friendship gain 3",
            "integer",
            MinimumValue: MinimumSignedByteValue,
            MaximumValue: MaximumSignedByteValue,
            Options: []),
    ];

    private static SwShItemEditableField CreateBooleanEditableField(string field, string label) =>
        new(field, label, "boolean", MinimumValue: 0, MaximumValue: 1, Options: BooleanOptions);

    private static SwShItemEditableField CreateBoostEditableField(string field, string label) =>
        new(field, label, "integer", MinimumValue: 0, MaximumValue: MaximumBoostValue, Options: BoostStageOptions);

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Items requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShItemsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<SwShItemRecord>(),
                CreateEditableFields(Array.Empty<string>(), new HashSet<int>()),
                sourceFileCount: 0,
                diagnostics);
        }

        var itemDataSource = ResolveWorkflowFile(project, ItemDataPath);
        if (itemDataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Items data is not available for this project.",
                expected: ItemDataPath));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShItemRecord>(),
                CreateEditableFields(Array.Empty<string>(), new HashSet<int>()),
                sourceFileCount: 0,
                diagnostics);
        }

        var itemNamesSource = ResolveItemNamesSource(project, diagnostics);
        var itemNames = itemNamesSource is null
            ? Array.Empty<string>()
            : LoadItemNames(itemNamesSource, diagnostics);
        var moveNamesSource = ResolveMoveNamesSource(project);
        var moveNames = moveNamesSource is null
            ? Array.Empty<string>()
            : LoadMoveNames(moveNamesSource, diagnostics);
        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);
        var editableFields = CreateEditableFields(moveNames, usableMoveIds);

        try
        {
            var itemTable = SwShItemTable.Parse(File.ReadAllBytes(itemDataSource.AbsolutePath));
            var provenance = CreateProvenance(itemDataSource.GraphEntry);
            var items = itemTable.Records
                .OrderBy(item => item.ItemId)
                .Select(item => ToItemRecord(item, itemNames, moveNames, provenance))
                .ToArray();
            var sourceFileCount = 1
                + (itemNamesSource is null ? 0 : 1)
                + (moveNamesSource is null ? 0 : 1)
                + (usableMoveIds.Count > 0 ? 1 : 0);

            return CreateWorkflow(summary, items, editableFields, sourceFileCount, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items data source is not a supported Sword/Shield item table: {exception.Message}",
                file: itemDataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield item.dat"));
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), editableFields, sourceFileCount: 1, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items data source could not be read: {exception.Message}",
                file: itemDataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield item.dat"));
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), editableFields, sourceFileCount: 1, diagnostics);
        }
    }

    internal static WorkflowFileSource? ResolveItemDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, ItemDataPath);
    }

    internal static IReadOnlyList<string> CreateItemDisplayNames(
        OpenedProject project,
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string>? moveNames = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(itemNames);

        var itemDataSource = ResolveItemDataSource(project);
        if (itemDataSource is null)
        {
            return itemNames;
        }

        var effectiveMoveNames = moveNames ?? LoadMoveNamesForDisplay(project);

        try
        {
            var itemTable = SwShItemTable.Parse(File.ReadAllBytes(itemDataSource.AbsolutePath));
            return CreateItemDisplayNames(itemNames, effectiveMoveNames, itemTable.Records);
        }
        catch (InvalidDataException)
        {
            return itemNames;
        }
        catch (IOException)
        {
            return itemNames;
        }
        catch (UnauthorizedAccessException)
        {
            return itemNames;
        }
    }

    internal static IReadOnlyList<string> CreateItemDisplayNames(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> moveNames,
        IReadOnlyList<SwShItemTableRecord> itemRecords)
    {
        ArgumentNullException.ThrowIfNull(itemNames);
        ArgumentNullException.ThrowIfNull(moveNames);
        ArgumentNullException.ThrowIfNull(itemRecords);

        if (itemRecords.Count == 0)
        {
            return itemNames;
        }

        var itemCount = Math.Max(itemNames.Count, itemRecords.Max(item => item.ItemId) + 1);
        var displayNames = new string[itemCount];
        for (var itemId = 0; itemId < itemNames.Count && itemId < displayNames.Length; itemId++)
        {
            displayNames[itemId] = itemNames[itemId];
        }

        foreach (var item in itemRecords)
        {
            displayNames[item.ItemId] = FormatItemDisplayName(item, itemNames, moveNames);
        }

        return displayNames;
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var outputRootWithSeparator = outputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;

        return targetPath.StartsWith(outputRootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? targetPath
            : null;
    }

    private static SwShItemsWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShItemRecord> items,
        IReadOnlyList<SwShItemEditableField> editableFields,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShItemsWorkflow(
            summary,
            items,
            editableFields,
            new SwShItemsWorkflowStats(items.Count, sourceFileCount),
            diagnostics);
    }

    private static WorkflowFileSource? ResolveItemNamesSource(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var englishNames = ResolveWorkflowFile(project, EnglishItemNamePath);
        if (englishNames is not null)
        {
            return englishNames;
        }

        var fallback = project.FileGraph.Entries
            .Where(entry =>
                entry.RelativePath.StartsWith("romfs/bin/message/", StringComparison.OrdinalIgnoreCase)
                && entry.RelativePath.EndsWith("/common/itemname.dat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(entry => ResolveWorkflowFile(project, entry.RelativePath))
            .FirstOrDefault(source => source is not null);

        if (fallback is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Item names are not available; item IDs will be shown as fallback names.",
                expected: "romfs/bin/message/{language}/common/itemname.dat"));
            return null;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Warning,
            "English item names are not available; using another available item name table.",
            file: fallback.GraphEntry.RelativePath,
            expected: EnglishItemNamePath));

        return fallback;
    }

    private static string[] LoadItemNames(
        WorkflowFileSource itemNamesSource,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(itemNamesSource.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item name table could not be decoded: {exception.Message}",
                file: itemNamesSource.GraphEntry.RelativePath,
                expected: "Sword/Shield itemname.dat"));
            return Array.Empty<string>();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Item name table could not be read: {exception.Message}",
                file: itemNamesSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield itemname.dat"));
            return Array.Empty<string>();
        }
    }

    private static WorkflowFileSource? ResolveMoveNamesSource(OpenedProject project)
    {
        return ResolveWorkflowFile(project, EnglishMoveNamePath);
    }

    private static string[] LoadMoveNamesForDisplay(OpenedProject project)
    {
        var source = ResolveMoveNamesSource(project);
        if (source is null)
        {
            return [];
        }

        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] LoadMoveNames(
        WorkflowFileSource moveNamesSource,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(moveNamesSource.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Move name table could not be decoded: {exception.Message}",
                file: moveNamesSource.GraphEntry.RelativePath,
                expected: "Sword/Shield wazaname.dat"));
            return Array.Empty<string>();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Move name table could not be read: {exception.Message}",
                file: moveNamesSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield wazaname.dat"));
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<SwShItemEditableField> CreateEditableFields(
        IReadOnlyList<string> moveNames,
        IReadOnlySet<int> usableMoveIds)
    {
        return BaseEditableFields
            .Append(new SwShItemEditableField(
                MachineMoveIdField,
                "Taught move",
                "integer",
                MinimumValue: 0,
                MaximumValue: MaximumMoveId,
                Options: CreateMoveOptions(moveNames, usableMoveIds)))
            .ToArray();
    }

    private static IReadOnlyList<SwShItemEditableFieldOption> CreateMoveOptions(
        IReadOnlyList<string> moveNames,
        IReadOnlySet<int> usableMoveIds)
    {
        return SwShMoveAvailability.CreateMoveOptions(
            moveNames,
            usableMoveIds,
            (value, label) => new SwShItemEditableFieldOption(value, label),
            includeNone: true);
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShItemProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShItemProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShItemRecord ToItemRecord(
        SwShItemTableRecord item,
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> moveNames,
        SwShItemProvenance provenance)
    {
        var metadata = CreateMetadata(item, moveNames);

        return new SwShItemRecord(
            item.ItemId,
            FormatItemDisplayName(item, itemNames, moveNames),
            FormatPouch(metadata.Pouch),
            checked((int)item.BuyPrice),
            checked((int)(item.BuyPrice / 2)),
            checked((int)item.WattsPrice),
            checked((int)item.AlternatePrice),
            metadata,
            item.SharedItemIds,
            CreateDetailGroups(metadata),
            provenance);
    }

    private static SwShItemMetadata CreateMetadata(
        SwShItemTableRecord item,
        IReadOnlyList<string> moveNames)
    {
        return new SwShItemMetadata(
            (int)item.Pouch,
            item.PouchFlags,
            item.FlingPower,
            item.FieldUseType,
            item.FieldFlags,
            item.CanUseOnPokemon,
            item.ItemType,
            item.SortIndex,
            item.ItemSprite,
            item.GroupType,
            item.GroupIndex,
            item.CureStatusFlags,
            item.Boost0,
            item.Boost1,
            item.Boost2,
            item.Boost3,
            item.UseFlags1,
            item.UseFlags2,
            item.EvHp,
            item.EvAttack,
            item.EvDefense,
            item.EvSpeed,
            item.EvSpecialAttack,
            item.EvSpecialDefense,
            item.HealAmount,
            item.PpGain,
            item.FriendshipGain1,
            item.FriendshipGain2,
            item.FriendshipGain3,
            item.MachineSlot,
            item.MachineMoveId,
            item.MachineMoveId is null ? null : GetMoveName(item.MachineMoveId.Value, moveNames));
    }

    internal static string FormatItemDisplayName(
        SwShItemTableRecord item,
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> moveNames)
    {
        var itemName = GetItemName(item.ItemId, itemNames);
        if (item.MachineSlot is null || item.MachineMoveId is null)
        {
            return itemName;
        }

        return FormatMachineItemDisplayName(
            itemName,
            item.MachineSlot.Value,
            item.MachineMoveId.Value,
            moveNames);
    }

    private static string FormatMachineItemDisplayName(
        string itemName,
        int machineSlot,
        int moveId,
        IReadOnlyList<string> moveNames)
    {
        var machineLabel = FormatMachineLabel(machineSlot, includeSlot: false);
        if (string.IsNullOrWhiteSpace(machineLabel))
        {
            return itemName;
        }

        var moveName = GetMoveName(moveId, moveNames);
        return string.Create(CultureInfo.InvariantCulture, $"{machineLabel} ({moveName})");
    }

    internal static string GetItemName(int itemId, IReadOnlyList<string> itemNames)
    {
        if ((uint)itemId < (uint)itemNames.Count && !string.IsNullOrWhiteSpace(itemNames[itemId]))
        {
            return itemNames[itemId];
        }

        return $"Item {itemId}";
    }

    internal static string GetMoveName(int moveId, IReadOnlyList<string> moveNames)
    {
        if ((uint)moveId < (uint)moveNames.Count && !string.IsNullOrWhiteSpace(moveNames[moveId]))
        {
            return moveNames[moveId];
        }

        return $"Move {moveId.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string FormatMoveOptionLabel(int moveId, string moveName)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{moveId:000} {moveName}");
    }

    internal static string FormatPouch(int pouch)
    {
        return (SwShItemPouch)pouch switch
        {
            SwShItemPouch.Medicine => "Medicine",
            SwShItemPouch.Balls => "Balls",
            SwShItemPouch.BattleItems => "Battle Items",
            SwShItemPouch.Berries => "Berries",
            SwShItemPouch.Items => "Items",
            SwShItemPouch.TMs => "TMs",
            SwShItemPouch.Treasures => "Treasures",
            SwShItemPouch.Ingredients => "Ingredients",
            SwShItemPouch.KeyItems => "Key Items",
            _ => $"Pouch {pouch.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    internal static IReadOnlyList<SwShItemDetailGroup> CreateDetailGroups(SwShItemMetadata item)
    {
        return
        [
            new SwShItemDetailGroup(
                "Inventory",
                [
                    new SwShItemDetail("Pouch", $"{FormatPouch(item.Pouch)} ({item.Pouch.ToString(CultureInfo.InvariantCulture)})"),
                    new SwShItemDetail("Pouch flags", FormatHex(item.PouchFlags)),
                    new SwShItemDetail("Item type", item.ItemType.ToString(CultureInfo.InvariantCulture)),
                    new SwShItemDetail("Sort index", item.SortIndex.ToString(CultureInfo.InvariantCulture)),
                    new SwShItemDetail("Sprite", item.ItemSprite.ToString(CultureInfo.InvariantCulture)),
                    new SwShItemDetail("Group", $"{FormatGroupType(item.GroupType)} ({item.GroupType.ToString(CultureInfo.InvariantCulture)})"),
                    new SwShItemDetail("Group index", item.GroupIndex.ToString(CultureInfo.InvariantCulture)),
                    new SwShItemDetail("Machine", FormatMachineSummary(item)),
                ]),
            new SwShItemDetailGroup(
                "Field Use",
                [
                    new SwShItemDetail("Field use type", $"{FormatFieldUseType(item.FieldUseType)} ({item.FieldUseType.ToString(CultureInfo.InvariantCulture)})"),
                    new SwShItemDetail("Field flags (unknown raw)", FormatHex(item.FieldFlags)),
                    new SwShItemDetail("Can use on Pokemon", FormatBool(item.CanUseOnPokemon)),
                    new SwShItemDetail("Can target fainted Pokemon", FormatBool((item.Boost0 & 0x01) != 0)),
                    new SwShItemDetail("Revives whole party", FormatBool((item.Boost0 & 0x02) != 0)),
                    new SwShItemDetail("Level up item", FormatBool((item.Boost0 & 0x04) != 0)),
                    new SwShItemDetail("Evolution item", FormatBool((item.Boost0 & 0x08) != 0)),
                    new SwShItemDetail("Use flags 1 (decoded)", FormatFlags(
                        item.UseFlags1,
                        (0x01, "Restore PP"),
                        (0x02, "Restore all PP"),
                        (0x04, "Restore HP"),
                        (0x08, "HP EV"),
                        (0x10, "Attack EV"),
                        (0x20, "Defense EV"),
                        (0x40, "Speed EV"),
                        (0x80, "Sp. Atk EV"))),
                    new SwShItemDetail("Use flags 2 (decoded; bits 5-7 unknown)", FormatFlags(
                        item.UseFlags2,
                        (0x01, "Sp. Def EV"),
                        (0x02, "EV above 100"),
                        (0x04, "Friendship 1"),
                        (0x08, "Friendship 2"),
                        (0x10, "Friendship 3"),
                        (0x20, "Unknown 5"),
                        (0x40, "Unknown 6"),
                        (0x80, "Unknown 7"))),
                ]),
            new SwShItemDetailGroup(
                "Battle",
                [
                    new SwShItemDetail("Fling power", item.FlingPower.ToString(CultureInfo.InvariantCulture)),
                    new SwShItemDetail("Cures status", FormatFlags(
                        item.CureStatusFlags,
                        (0x01, "Sleep"),
                        (0x02, "Poison"),
                        (0x04, "Burn"),
                        (0x08, "Freeze"),
                        (0x10, "Paralysis"),
                        (0x20, "Confusion"),
                        (0x40, "Infatuation"),
                        (0x80, "Guard Spec."))),
                    new SwShItemDetail("Battle boosts", FormatBattleBoosts(item)),
                    new SwShItemDetail("Critical hit boost", FormatPositiveBoostStage((item.Boost3 >> 4) & 0x03)),
                    new SwShItemDetail("PP Up flag", FormatBool((item.Boost3 & 0x40) != 0)),
                    new SwShItemDetail("PP Max flag", FormatBool((item.Boost3 & 0x80) != 0)),
                ]),
            new SwShItemDetailGroup(
                "Pokemon Effects",
                [
                    new SwShItemDetail("EV gain", FormatEvGain(item)),
                    new SwShItemDetail("Heal", FormatHealAmount(item.HealAmount)),
                    new SwShItemDetail("PP gain", item.PpGain.ToString(CultureInfo.InvariantCulture)),
                    new SwShItemDetail("Friendship gains", FormatFriendshipGains(item)),
                ]),
        ];
    }

    private static string FormatGroupType(int value)
    {
        return value switch
        {
            0 => "None",
            1 => "Ball",
            3 => "Berries",
            4 => "TM/TR",
            5 => "Gems",
            _ => $"Group {value.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string FormatFieldUseType(int value)
    {
        return value switch
        {
            0 => "Inert",
            1 => "Medicine",
            2 => "TM/TR",
            5 => "Spray",
            6 => "Evolution",
            7 => "Escape Rope",
            12 => "Berry",
            15 => "Form Change",
            _ => $"Field use {value.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string FormatMachineSummary(SwShItemMetadata item)
    {
        if (item.GroupType != TechnicalRecordMachineGroup
            || item.FieldUseType != TechnicalRecordFieldUseType
            || item.GroupIndex > TechnicalRecordLastSlot)
        {
            return "No machine link";
        }

        var machineLabel = FormatMachineLabel(item.GroupIndex, includeSlot: true);

        if (item.MachineMoveId is null)
        {
            return $"{machineLabel}, move table unavailable";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{machineLabel} -> {item.MachineMoveName ?? GetMoveName(item.MachineMoveId.Value, Array.Empty<string>())} ({item.MachineMoveId.Value})");
    }

    private static string FormatMachineLabel(int machineSlot, bool includeSlot)
    {
        if (machineSlot < 0 || machineSlot > TechnicalRecordLastSlot)
        {
            return string.Empty;
        }

        var isTr = machineSlot >= TechnicalRecordTrSlotStart;
        var number = isTr ? machineSlot - TechnicalRecordTrSlotStart : machineSlot;
        var machineLabel = string.Create(CultureInfo.InvariantCulture, $"{(isTr ? "TR" : "TM")}{number:00}");

        return includeSlot
            ? string.Create(CultureInfo.InvariantCulture, $"{machineLabel} (slot {machineSlot})")
            : machineLabel;
    }

    private static string FormatFlags(int value, params (int Flag, string Label)[] flags)
    {
        if (value == 0)
        {
            return "None";
        }

        var labels = flags
            .Where(flag => (value & flag.Flag) != 0)
            .Select(flag => flag.Label)
            .ToArray();

        return labels.Length == 0
            ? FormatHex(value)
            : string.Join(", ", labels);
    }

    private static string FormatBattleBoosts(SwShItemMetadata item)
    {
        return string.Join(
            " / ",
            [
                $"Atk {FormatBoostStageValue(item.Boost0 >> 4)}",
                $"Def {FormatBoostStageValue(item.Boost1 & 0x0F)}",
                $"SpA {FormatBoostStageValue(item.Boost1 >> 4)}",
                $"SpD {FormatBoostStageValue(item.Boost2 & 0x0F)}",
                $"Spe {FormatBoostStageValue(item.Boost2 >> 4)}",
                $"Acc {FormatBoostStageValue(item.Boost3 & 0x0F)}",
            ]);
    }

    private static string FormatBoostStageOption(int value)
    {
        return value is >= 0 and <= 6
            ? FormatPositiveBoostStage(value)
            : string.Create(CultureInfo.InvariantCulture, $"Raw {value} (unused/unknown)");
    }

    private static string FormatPositiveBoostStage(int value)
    {
        return value == 0
            ? "0 stages"
            : string.Create(CultureInfo.InvariantCulture, $"+{value} stage{(value == 1 ? string.Empty : "s")}");
    }

    private static string FormatBoostStageValue(int value)
    {
        return value is >= 0 and <= 6
            ? FormatPositiveBoostStage(value)
            : string.Create(CultureInfo.InvariantCulture, $"raw {value}");
    }

    private static string FormatEvGain(SwShItemMetadata item)
    {
        var values = new (string Label, int Value)[]
        {
            ("HP", item.EvHp),
            ("Atk", item.EvAttack),
            ("Def", item.EvDefense),
            ("Spe", item.EvSpeed),
            ("SpA", item.EvSpecialAttack),
            ("SpD", item.EvSpecialDefense),
        };
        var gains = values
            .Where(value => value.Value != 0)
            .Select(value => $"{value.Label} {FormatSigned(value.Value)}")
            .ToArray();

        return gains.Length == 0 ? "None" : string.Join(", ", gains);
    }

    private static string FormatHealAmount(int value)
    {
        return value switch
        {
            253 => "Quarter HP",
            254 => "Half HP",
            255 => "Full HP",
            _ => $"{value.ToString(CultureInfo.InvariantCulture)} HP",
        };
    }

    private static string FormatFriendshipGains(SwShItemMetadata item)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatSigned(item.FriendshipGain1)} / {FormatSigned(item.FriendshipGain2)} / {FormatSigned(item.FriendshipGain3)}");
    }

    private static string FormatSigned(int value)
    {
        return value > 0
            ? $"+{value.ToString(CultureInfo.InvariantCulture)}"
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatBool(bool value)
    {
        return value ? "Yes" : "No";
    }

    private static string FormatHex(int value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"0x{value:X2}");
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Items,
            "Items",
            "Item records, names, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: "workflow.items",
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
