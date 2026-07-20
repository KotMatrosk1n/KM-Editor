// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Items;

internal sealed class ZaItemsWorkflowService
{
    internal const string LegacyTechnicalMachineNumberingWarningPrefix =
        "A legacy KM Editor TM-numbering output was detected.";
    internal const string LegacyMachineWazaLayoutWarningPrefix =
        "A legacy KM Editor TM pickup layout was detected.";

    private const string WorkflowLabel = "Items";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A item data, prices, pockets, stack caps, and TM assignments.";

    public const string ItemTypeField = "itemType";
    public const string PriceField = "price";
    public const string MegaShardPriceField = "megaShardPrice";
    public const string ColorfulScrewPriceField = "colorfulScrewPrice";
    public const string PocketField = "pocket";
    public const string StackCapField = "stackCap";
    public const string SortOrderField = "sortOrder";
    public const string CanNotHoldField = "canNotHold";
    public const string MachineMoveIdField = "machineMoveId";
    public const string TechnicalMachineNumberField = "tmNumber";
    public const string CureSleepField = "cureSleep";
    public const string CurePoisonField = "curePoison";
    public const string CureBurnField = "cureBurn";
    public const string CureFreezeField = "cureFreeze";
    public const string CureParalyzeField = "cureParalyze";
    public const string CureConfuseField = "cureConfuse";
    public const string CureInfatuationField = "cureInfatuation";
    public const string AttackBoostField = "attackBoost";
    public const string DefenseBoostField = "defenseBoost";
    public const string SpecialAttackBoostField = "specialAttackBoost";
    public const string SpecialDefenseBoostField = "specialDefenseBoost";
    public const string SpeedBoostField = "speedBoost";
    public const string AccuracyBoostField = "accuracyBoost";
    public const string CriticalHitBoostField = "criticalHitBoost";
    public const string EffectGuardField = "effectGuard";
    public const string MintNatureField = "mintNature";
    public const string HealPowerField = "healPower";
    public const string HealPercentageField = "healPercentage";
    public const string RevivalCountField = "revivalCount";
    public const string RevivePercentageField = "revivePercentage";
    public const string ExpPointGainField = "expPointGain";
    public const string MaxUseLevelField = "maxUseLevel";
    public const string FriendshipGain1Field = "friendshipGain1";
    public const string FriendshipGain2Field = "friendshipGain2";
    public const string FriendshipGain3Field = "friendshipGain3";
    public const string CanUseOnPokemonField = "canUseOnPokemon";
    public const string EvolutionItemField = "evolutionItem";
    public const string FormChangeItemField = "formChangeItem";
    public const string EvHpField = "evHp";
    public const string EvAttackField = "evAttack";
    public const string EvDefenseField = "evDefense";
    public const string EvSpeedField = "evSpeed";
    public const string EvSpecialAttackField = "evSpecialAttack";
    public const string EvSpecialDefenseField = "evSpecialDefense";
    public const string EquipPowerField = "equipPower";
    public const string AutoHealPriorityField = "autoHealPriority";
    public const string CanUseInBattleField = "canUseInBattle";
    public const string SwapIntoItemField = "swapIntoItem";

    private static readonly IReadOnlyList<string> ItemTypeNames =
    [
        "Key Item",
        "Berry",
        "Medicine",
        "Treasure",
        "Ball",
        "Technical Machine",
        "Unused",
        "Pokemon Item",
        "Unknown",
        "Special Item",
    ];

    private static readonly IReadOnlyDictionary<int, string> PocketNames = new Dictionary<int, string>
    {
        [0] = "None",
        [1] = "Balls",
        [2] = "Items",
        [3] = "Treasures",
        [4] = "Key Items",
        [5] = "Berries",
        [6] = "Technical Machines",
        [7] = "Mega Stones",
    };

    private static readonly IReadOnlyList<string> NatureNames =
    [
        "Hardy",
        "Lonely",
        "Brave",
        "Adamant",
        "Naughty",
        "Bold",
        "Docile",
        "Relaxed",
        "Impish",
        "Lax",
        "Timid",
        "Hasty",
        "Serious",
        "Jolly",
        "Naive",
        "Modest",
        "Mild",
        "Quiet",
        "Bashful",
        "Rash",
        "Calm",
        "Gentle",
        "Sassy",
        "Careful",
        "Quirky",
    ];

    private static readonly IReadOnlyList<ZaItemEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<ZaItemEditableFieldOption> ItemTypeOptions =
        CreateIndexedOptions(ItemTypeNames);

    private static readonly IReadOnlyList<ZaItemEditableFieldOption> PocketOptions =
        PocketNames
            .OrderBy(entry => entry.Key)
            .Select(entry => new ZaItemEditableFieldOption(entry.Key, $"{entry.Key.ToString(CultureInfo.InvariantCulture)} {entry.Value}"))
            .ToArray();

    private static readonly IReadOnlyList<ZaItemEditableFieldOption> NatureOptions =
    [
        new(-1, "-1 None"),
        .. CreateIndexedOptions(NatureNames),
    ];

    private static readonly IReadOnlyList<ZaItemEditableField> BaseEditableFields =
    [
        Field(ItemTypeField, "Item type", "integer", 0, 9, ItemTypeOptions),
        Field(PriceField, "Price", "integer", 0, 9_999_999),
        Field(MegaShardPriceField, "Mega Shard price", "integer", 0, 9_999_999),
        Field(ColorfulScrewPriceField, "Colorful Screw price", "integer", 0, 9_999_999),
        Field(PocketField, "Bag pocket", "integer", 0, 7, PocketOptions),
        Field(StackCapField, "Stack cap", "integer", 1, 9_999),
        Field(SortOrderField, "Sort order", "integer", 0, int.MaxValue),
        Field(CanUseOnPokemonField, "Can use on Pokemon", "boolean", 0, 1, BooleanOptions),
        Field(EvolutionItemField, "Evolution Item", "boolean", 0, 1, BooleanOptions),
        Field(CanNotHoldField, "Cannot be held", "boolean", 0, 1, BooleanOptions),
        Field(MachineMoveIdField, "TM move", "integer", 0, ushort.MaxValue),
        Field(TechnicalMachineNumberField, "TM number", "integer", 1, int.MaxValue),
        Field(CureSleepField, "Cures sleep", "boolean", 0, 1, BooleanOptions),
        Field(CurePoisonField, "Cures poison", "boolean", 0, 1, BooleanOptions),
        Field(CureBurnField, "Cures burn", "boolean", 0, 1, BooleanOptions),
        Field(CureFreezeField, "Cures freeze", "boolean", 0, 1, BooleanOptions),
        Field(CureParalyzeField, "Cures paralysis", "boolean", 0, 1, BooleanOptions),
        Field(CureConfuseField, "Cures confusion", "boolean", 0, 1, BooleanOptions),
        Field(CureInfatuationField, "Cures infatuation", "boolean", 0, 1, BooleanOptions),
        Field(AttackBoostField, "Attack boost", "integer", int.MinValue, int.MaxValue),
        Field(DefenseBoostField, "Defense boost", "integer", int.MinValue, int.MaxValue),
        Field(SpecialAttackBoostField, "Sp. Atk boost", "integer", int.MinValue, int.MaxValue),
        Field(SpecialDefenseBoostField, "Sp. Def boost", "integer", int.MinValue, int.MaxValue),
        Field(SpeedBoostField, "Speed boost", "integer", int.MinValue, int.MaxValue),
        Field(AccuracyBoostField, "Accuracy boost", "integer", int.MinValue, int.MaxValue),
        Field(CriticalHitBoostField, "Critical hit boost", "integer", int.MinValue, int.MaxValue),
        Field(EffectGuardField, "Effect guard", "integer", int.MinValue, int.MaxValue),
        Field(MintNatureField, "Mint nature", "integer", -1, NatureNames.Count - 1, NatureOptions),
        Field(HealPowerField, "Healing power", "integer", int.MinValue, int.MaxValue),
        Field(HealPercentageField, "Heal percentage", "integer", 0, 100),
        Field(RevivalCountField, "Revival count", "integer", int.MinValue, int.MaxValue),
        Field(RevivePercentageField, "Revive percentage", "integer", 0, 100),
        Field(ExpPointGainField, "EXP gain", "integer", 0, int.MaxValue),
        Field(MaxUseLevelField, "Max use level", "integer", 0, 100),
        Field(FriendshipGain1Field, "Friendship 1", "integer", int.MinValue, int.MaxValue),
        Field(FriendshipGain2Field, "Friendship 2", "integer", int.MinValue, int.MaxValue),
        Field(FriendshipGain3Field, "Friendship 3", "integer", int.MinValue, int.MaxValue),
        Field(FormChangeItemField, "Form change item", "boolean", 0, 1, BooleanOptions),
        Field(EvHpField, "HP EV", "integer", int.MinValue, int.MaxValue),
        Field(EvAttackField, "Attack EV", "integer", int.MinValue, int.MaxValue),
        Field(EvDefenseField, "Defense EV", "integer", int.MinValue, int.MaxValue),
        Field(EvSpeedField, "Speed EV", "integer", int.MinValue, int.MaxValue),
        Field(EvSpecialAttackField, "Sp. Atk EV", "integer", int.MinValue, int.MaxValue),
        Field(EvSpecialDefenseField, "Sp. Def EV", "integer", int.MinValue, int.MaxValue),
        Field(EquipPowerField, "Equip power", "integer", int.MinValue, int.MaxValue),
        Field(AutoHealPriorityField, "Auto-heal priority", "integer", int.MinValue, int.MaxValue),
        Field(CanUseInBattleField, "Can use in battle", "boolean", 0, 1, BooleanOptions),
        Field(SwapIntoItemField, "Swap into item", "integer", 0, int.MaxValue),
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaItemsWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Items,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaItemsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        ZaWorkflowFile? source = null;
        var labels = ZaTextLabelLookup.None();
        var items = Array.Empty<ZaItemRecord>();

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            source = fileSource.Read(project, ZaDataPaths.ItemDataArray);
            DetectMachineWazaLayout(source, diagnostics);
            var mintNatureRecovery = DetectMintNatureRecovery(project, source, diagnostics);
            var technicalMachineRecovery = DetectTechnicalMachineLegacyRecovery(project, source, diagnostics);
            items = LoadRecords(
                    source,
                    labels,
                    mintNatureRecovery.ItemIds,
                    technicalMachineRecovery)
                .ToArray();
            var inconsistentMachineCount = items.Count(item =>
                IsTechnicalMachineRecord(item)
                && item.FieldValues.GetValueOrDefault(TechnicalMachineNumberField) is null);
            if (inconsistentMachineCount > 0)
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"{inconsistentMachineCount} TM number assignment(s) have mismatched sort and index metadata. "
                    + "Choose a TM number on each affected item to repair both values together.",
                    $"romfs/{ZaDataPaths.ItemDataArray}",
                    TechnicalMachineNumberField,
                    "TM number N stored as sort order N and machine index N - 1"));
            }

            var machines = items.Where(IsTechnicalMachineRecord).ToArray();
            var assignedNumbers = machines
                .Select(item => item.Metadata.MachineSlot ?? 0)
                .ToArray();
            var duplicateNumbers = assignedNumbers
                .Where(number => number > 0)
                .GroupBy(number => number)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .Order()
                .ToArray();
            var missingNumbers = Enumerable
                .Range(1, machines.Length)
                .Except(assignedNumbers)
                .ToArray();
            var outOfRangeNumbers = assignedNumbers
                .Where(number => number < 1 || number > machines.Length)
                .Distinct()
                .Order()
                .ToArray();
            if (duplicateNumbers.Length > 0
                || missingNumbers.Length > 0
                || outOfRangeNumbers.Length > 0)
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    "TM number assignments need repair. "
                    + FormatMachineNumberIssue("Duplicate", duplicateNumbers)
                    + FormatMachineNumberIssue("Missing", missingNumbers)
                    + FormatMachineNumberIssue("Out of range", outOfRangeNumbers)
                    + "Assign a missing number to one item using a duplicate or out-of-range number; KM will stage the safe repair.",
                    $"romfs/{ZaDataPaths.ItemDataArray}",
                    TechnicalMachineNumberField,
                    $"Each TM number from 1 through {machines.Length} assigned exactly once"));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Items could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.ItemDataArray}"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Items,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new ZaItemsWorkflow(
            summary,
            items,
            CreateEditableFields(labels, items),
            new ZaItemsWorkflowStats(items.Length, source is null ? 0 : 1),
            diagnostics);
    }

    internal static string FormatItemType(int value) => FormatIndexed(value, ItemTypeNames, "Item type");

    internal static string FormatPocket(int value)
    {
        return PocketNames.TryGetValue(value, out var label)
            ? label
            : $"Pocket {value.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string FormatNature(int value) => value < 0
        ? "None"
        : FormatIndexed(value, NatureNames, "Nature");

    private static void DetectMachineWazaLayout(
        ZaWorkflowFile source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var inspection = ZaMachineWazaLayoutDetector.Analyze(source.Bytes);
        if (!inspection.RequiresRepair)
        {
            return;
        }

        diagnostics.Add(ZaWorkflowSupport.Warning(
            $"{LegacyMachineWazaLayoutWarningPrefix} "
            + $"The game reads the TM move as a 32-bit value, and {inspection.UnsafeRowCount} "
            + "record(s) contain adjacent field data in its upper half. The next Items output will "
            + "repair those rows while preserving TM numbers, moves, prices, icons, and unrelated item edits.",
            $"romfs/{ZaDataPaths.ItemDataArray}",
            TechnicalMachineNumberField,
            "TM move IDs stored with a zero upper 16-bit half for game compatibility"));
    }

    private ZaItemMintNatureRecovery DetectMintNatureRecovery(
        OpenedProject project,
        ZaWorkflowFile source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (source.SourceLayer != ProjectFileLayer.Layered)
        {
            return ZaItemMintNatureRecovery.None;
        }

        try
        {
            var baseSource = fileSource.ReadBase(project, ZaDataPaths.ItemDataArray);
            var recovery = ZaItemMintNatureRecoveryDetector.Analyze(source.Bytes, baseSource.Bytes);
            if (recovery.Status == ZaItemMintNatureRecoveryStatus.Detected)
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    "A legacy KM output rewrote unused mint sentinels and made unrelated items target Pokemon. "
                    + "Affected rows are treated as no-mint values and will be repaired on the next Items output.",
                    $"romfs/{ZaDataPaths.ItemDataArray}",
                    MintNatureField,
                    "Only items with a real use effect or an enabled evolution-item flag target Pokemon"));
            }
            else if (recovery.Status == ZaItemMintNatureRecoveryStatus.Ambiguous)
            {
                diagnostics.Add(ZaWorkflowSupport.Error(
                    "The layered item table contains a partial legacy mint-sentinel pattern. "
                    + "KM will not rewrite the mixed values because doing so would require guessing.",
                    $"romfs/{ZaDataPaths.ItemDataArray}",
                    MintNatureField,
                    "Restore a clean item table or remove the affected layered item output before editing"));
            }

            return recovery;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Legacy item mint-sentinel recovery could not compare clean base data: {exception.Message}",
                $"romfs/{ZaDataPaths.ItemDataArray}",
                MintNatureField,
                "Readable clean base and layered item tables"));
            return ZaItemMintNatureRecovery.None;
        }
    }

    private ZaTechnicalMachineLegacyRecovery DetectTechnicalMachineLegacyRecovery(
        OpenedProject project,
        ZaWorkflowFile source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (source.SourceLayer != ProjectFileLayer.Layered)
        {
            return ZaTechnicalMachineLegacyRecovery.None;
        }

        try
        {
            var baseSource = fileSource.ReadBase(project, ZaDataPaths.ItemDataArray);
            var recovery = ZaTechnicalMachineLegacyRecoveryDetector.Analyze(
                source.Bytes,
                baseSource.Bytes);
            if (!recovery.IsBlocked
                && recovery.HasChanges
                && recovery.BaseSlot101OwnerItemId is not null)
            {
                try
                {
                    recovery = ZaTechnicalMachineLegacyRecoveryDetector.AnalyzeWithMoveData(
                        source.Bytes,
                        baseSource.Bytes,
                        fileSource.Read(project, ZaDataPaths.MoveDataArray).Bytes,
                        fileSource.ReadBase(project, ZaDataPaths.MoveDataArray).Bytes);
                }
                catch (Exception exception) when (
                    exception is IOException
                        or InvalidDataException
                        or ArgumentException
                        or UnauthorizedAccessException)
                {
                    recovery = recovery with
                    {
                        IconRepairWarning =
                            "Legacy TM numbering can still be repaired, but KM will leave the affected disc icon unchanged "
                            + "because the active and clean move tables are not both readable.",
                    };
                }
            }

            if (recovery.IsBlocked)
            {
                diagnostics.Add(ZaWorkflowSupport.Error(
                    recovery.BlockingReason!,
                    $"romfs/{ZaDataPaths.ItemDataArray}",
                    TechnicalMachineNumberField,
                    "An exact KM-generated legacy row or the clean physical item table"));
            }
            else if (recovery.HasChanges)
            {
                var action = recovery.RemoveSyntheticRow && recovery.RepairItemId is not null
                    ? $"remove KM's legacy synthetic item 2161 and restore physical TM{recovery.RepairTechnicalMachineNumber!.Value:000}"
                    : recovery.RemoveSyntheticRow
                        ? "remove KM's legacy synthetic item 2161"
                        : $"restore physical TM{recovery.RepairTechnicalMachineNumber!.Value:000}";
                var iconAction = recovery.IconRepairs.Count == 0
                    ? string.Empty
                    : $" and synchronize {recovery.IconRepairs.Count} unchanged stale disc icon(s)";
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"{LegacyTechnicalMachineNumberingWarningPrefix} "
                    + $"The next Items output will {action}{iconAction} while preserving moves, prices, custom icons, and unrelated item edits.",
                    $"romfs/{ZaDataPaths.ItemDataArray}",
                    TechnicalMachineNumberField,
                    $"Physical TM numbers 1 through {recovery.PhysicalTechnicalMachineCount} assigned exactly once"));
            }

            if (!string.IsNullOrWhiteSpace(recovery.IconRepairWarning))
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    recovery.IconRepairWarning,
                    $"romfs/{ZaDataPaths.MoveDataArray}",
                    MachineMoveIdField,
                    "Unique clean TM type-to-icon mapping and readable active move types"));
            }

            return recovery;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Legacy TM-numbering recovery could not compare clean base data: {exception.Message}",
                $"romfs/{ZaDataPaths.ItemDataArray}",
                TechnicalMachineNumberField,
                "Readable clean base and layered item tables"));
            return ZaTechnicalMachineLegacyRecovery.None;
        }
    }

    private static IEnumerable<ZaItemRecord> LoadRecords(
        ZaWorkflowFile source,
        ZaTextLabelLookup labels,
        IReadOnlySet<int> recoveredMintNatureItemIds,
        ZaTechnicalMachineLegacyRecovery technicalMachineRecovery)
    {
        var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(source.Bytes));
        var iconRepairs = technicalMachineRecovery.IconRepairs.ToDictionary(
            repair => repair.ItemId,
            repair => repair.RepairedIconName);
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var item = table.Values(index);
            if (item is null
                || technicalMachineRecovery.RemoveSyntheticRow
                && item.Value.Id == ZaTechnicalMachineCatalog.LegacySyntheticTechnicalMachineItemId)
            {
                continue;
            }

            var record = ToRecord(
                item.Value,
                labels,
                source,
                recoveredMintNatureItemIds.Contains(item.Value.Id) ? -1 : item.Value.MintNature,
                iconRepairs.GetValueOrDefault(item.Value.Id));
            yield return technicalMachineRecovery.RepairItemId == item.Value.Id
                ? WithTechnicalMachineNumber(
                    record,
                    technicalMachineRecovery.RepairTechnicalMachineNumber!.Value)
                : record;
        }
    }

    private static ZaItemRecord ToRecord(
        ZaItemData item,
        ZaTextLabelLookup labels,
        ZaWorkflowFile source,
        int mintNature,
        string? iconNameOverride)
    {
        var machineMoveId = item.MachineWaza;
        var machineMoveName = machineMoveId > 0 ? labels.Move(machineMoveId) : null;
        var sourceItemName = labels.Item(item.Id);
        var machineSlot = ZaTechnicalMachineCatalog.IsTechnicalMachine(item)
            && ZaTechnicalMachineCatalog.TryResolveMachineSlot(item, sourceItemName, out var resolvedSlot)
                ? resolvedSlot
                : (int?)null;
        var itemName = ResolveItemName(item, sourceItemName, machineSlot, machineMoveName);
        var metadata = new ZaItemMetadata(
            item.Pocket,
            PouchFlags: 0,
            FlingPower: 0,
            item.CanUseInBattle ? 1 : 0,
            FieldFlags: 0,
            CanUseOnPokemon: CanUseOnPokemon(item, mintNature),
            item.ItemType,
            item.SortNum,
            item.Id,
            item.Pocket,
            item.MachineIndex,
            CreateCureStatusFlags(item),
            item.WorkAttack,
            item.WorkDefense,
            item.WorkSpAttack,
            item.WorkSpDefense,
            item.WorkSpeed,
            item.WorkAccuracy,
            item.WorkStatusHp,
            item.WorkStatusAtk,
            item.WorkStatusDef,
            item.WorkStatusSpd,
            item.WorkStatusSAtk,
            item.WorkStatusSDef,
            item.WorkRecvPower != 0 ? item.WorkRecvPower : item.HealPercentage,
            PpGain: 0,
            item.WorkFriendly1,
            item.WorkFriendly2,
            item.WorkFriendly3,
            MachineSlot: machineSlot,
            MachineMoveId: machineMoveId > 0 ? machineMoveId : null,
            MachineMoveName: machineMoveName);

        return new ZaItemRecord(
            item.Id,
            itemName,
            FormatPocket(item.Pocket),
            item.Price,
            item.Price / 2,
            item.PriceMegaShard,
            item.PriceColorfulScrew,
            CreateFieldValues(item, mintNature),
            metadata,
            SharedItemIds: [],
            CreateDetailGroups(item, labels, mintNature, iconNameOverride),
            new ZaItemProvenance(source.RelativePath, source.SourceLayer, source.FileState));
    }

    private static ZaItemRecord WithTechnicalMachineNumber(
        ZaItemRecord item,
        int number)
    {
        var fieldValues = new Dictionary<string, int?>(item.FieldValues, StringComparer.Ordinal)
        {
            [SortOrderField] = number,
            [TechnicalMachineNumberField] = number,
        };
        var detailGroups = item.DetailGroups
            .Select(group => group with
            {
                Details = group.Details
                    .Select(detail => string.Equals(detail.Label, "TM number", StringComparison.Ordinal)
                        ? detail with { Value = number.ToString(CultureInfo.InvariantCulture) }
                        : detail)
                    .ToArray(),
            })
            .ToArray();
        var updated = item with
        {
            FieldValues = fieldValues,
            Metadata = item.Metadata with
            {
                SortIndex = number,
                GroupIndex = number - 1,
                MachineSlot = number,
            },
            DetailGroups = detailGroups,
        };
        return updated.Metadata.MachineMoveName is { Length: > 0 } moveName
            ? updated with { Name = FormatTechnicalMachineName(number, moveName) }
            : updated;
    }

    internal static IReadOnlyDictionary<string, int?> CreateFieldValues(ZaItemData item, int mintNature)
    {
        return new Dictionary<string, int?>
        {
            [ItemTypeField] = item.ItemType,
            [PriceField] = item.Price,
            [MegaShardPriceField] = item.PriceMegaShard,
            [ColorfulScrewPriceField] = item.PriceColorfulScrew,
            [PocketField] = item.Pocket,
            [StackCapField] = item.SlotMaxNum,
            [SortOrderField] = item.SortNum,
            [CanNotHoldField] = item.CanNotHold ? 1 : 0,
            [MachineMoveIdField] = item.MachineWaza,
            [TechnicalMachineNumberField] = GetTechnicalMachineNumber(item),
            [CureSleepField] = item.WorkRecvSleep ? 1 : 0,
            [CurePoisonField] = item.WorkRecvPoison ? 1 : 0,
            [CureBurnField] = item.WorkRecvBurn ? 1 : 0,
            [CureFreezeField] = item.WorkRecvFreeze ? 1 : 0,
            [CureParalyzeField] = item.WorkRecvParalyze ? 1 : 0,
            [CureConfuseField] = item.WorkRecvConfuse ? 1 : 0,
            [CureInfatuationField] = item.WorkRecvMero ? 1 : 0,
            [AttackBoostField] = item.WorkAttack,
            [DefenseBoostField] = item.WorkDefense,
            [SpecialAttackBoostField] = item.WorkSpAttack,
            [SpecialDefenseBoostField] = item.WorkSpDefense,
            [SpeedBoostField] = item.WorkSpeed,
            [AccuracyBoostField] = item.WorkAccuracy,
            [CriticalHitBoostField] = item.WorkCritical,
            [EffectGuardField] = item.WorkEffectGuard,
            [MintNatureField] = mintNature,
            [HealPowerField] = item.WorkRecvPower,
            [HealPercentageField] = item.HealPercentage,
            [RevivalCountField] = item.WorkRevival,
            [RevivePercentageField] = item.RevivePercentage,
            [ExpPointGainField] = item.ExpPointGain,
            [MaxUseLevelField] = item.MaxUseLevel,
            [FriendshipGain1Field] = item.WorkFriendly1,
            [FriendshipGain2Field] = item.WorkFriendly2,
            [FriendshipGain3Field] = item.WorkFriendly3,
            [CanUseOnPokemonField] = CanUseOnPokemon(item, mintNature) ? 1 : 0,
            [EvolutionItemField] = item.WorkEvolutional ? 1 : 0,
            [FormChangeItemField] = item.WorkFormChange ? 1 : 0,
            [EvHpField] = item.WorkStatusHp,
            [EvAttackField] = item.WorkStatusAtk,
            [EvDefenseField] = item.WorkStatusDef,
            [EvSpeedField] = item.WorkStatusSpd,
            [EvSpecialAttackField] = item.WorkStatusSAtk,
            [EvSpecialDefenseField] = item.WorkStatusSDef,
            [EquipPowerField] = item.EquipPower,
            [AutoHealPriorityField] = item.AutoHealPriority,
            [CanUseInBattleField] = item.CanUseInBattle ? 1 : 0,
            [SwapIntoItemField] = item.SwapIntoId,
        };
    }

    private static string ResolveItemName(
        ZaItemData item,
        string itemName,
        int? machineSlot,
        string? machineMoveName)
    {
        if (ZaTechnicalMachineCatalog.IsTechnicalMachine(item)
            && machineSlot is not null
            && machineMoveName is not null)
        {
            return FormatTechnicalMachineName(machineSlot.Value, machineMoveName);
        }

        return itemName;
    }

    internal static string FormatTechnicalMachineName(int machineSlot, string machineMoveName) =>
        $"{ZaTechnicalMachineCatalog.FormatMachineLabel(machineSlot)} {machineMoveName}";

    private static bool CanUseOnPokemon(ZaItemData item, int mintNature)
    {
        return item.WorkRecvSleep
            || item.WorkRecvPoison
            || item.WorkRecvBurn
            || item.WorkRecvFreeze
            || item.WorkRecvParalyze
            || item.WorkRecvConfuse
            || item.WorkRecvMero
            || item.WorkRecvPower != 0
            || item.HealPercentage != 0
            || item.WorkRevival != 0
            || item.RevivePercentage != 0
            || item.ExpPointGain != 0
            || mintNature >= 0
            || item.WorkEvolutional
            || item.WorkFormChange
            || item.WorkStatusHp != 0
            || item.WorkStatusAtk != 0
            || item.WorkStatusDef != 0
            || item.WorkStatusSpd != 0
            || item.WorkStatusSAtk != 0
            || item.WorkStatusSDef != 0;
    }

    private static int CreateCureStatusFlags(ZaItemData item)
    {
        var flags = 0;
        if (item.WorkRecvSleep)
        {
            flags |= 1 << 0;
        }

        if (item.WorkRecvPoison)
        {
            flags |= 1 << 1;
        }

        if (item.WorkRecvBurn)
        {
            flags |= 1 << 2;
        }

        if (item.WorkRecvFreeze)
        {
            flags |= 1 << 3;
        }

        if (item.WorkRecvParalyze)
        {
            flags |= 1 << 4;
        }

        if (item.WorkRecvConfuse)
        {
            flags |= 1 << 5;
        }

        if (item.WorkRecvMero)
        {
            flags |= 1 << 6;
        }

        return flags;
    }

    private static IReadOnlyList<ZaItemDetailGroup> CreateDetailGroups(
        ZaItemData item,
        ZaTextLabelLookup labels,
        int mintNature,
        string? iconNameOverride)
    {
        return
        [
            new ZaItemDetailGroup(
                "Pokemon Legends Z-A",
                [
                    Detail("Internal token", item.InternalName ?? string.Empty),
                    Detail("Icon", iconNameOverride ?? item.IconName ?? string.Empty),
                    Detail("Item type", $"{item.ItemType.ToString(CultureInfo.InvariantCulture)} {FormatItemType(item.ItemType)}"),
                    Detail("Bag pocket", $"{item.Pocket.ToString(CultureInfo.InvariantCulture)} {FormatPocket(item.Pocket)}"),
                    Detail("Stack cap", item.SlotMaxNum),
                    Detail(
                        ZaTechnicalMachineCatalog.IsTechnicalMachine(item) ? "TM number" : "Sort order",
                        item.SortNum),
                    Detail("Cannot be held", ZaLabels.Bool(item.CanNotHold)),
                    Detail("Can use in battle", ZaLabels.Bool(item.CanUseInBattle)),
                ]),
            new ZaItemDetailGroup(
                "Prices",
                [
                    Detail("Money", item.Price),
                    Detail("Sell estimate", item.Price / 2),
                    Detail("Mega Shards", item.PriceMegaShard),
                    Detail("Colorful Screws", item.PriceColorfulScrew),
                ]),
            new ZaItemDetailGroup(
                "TM Assignment",
                [
                    Detail("TM move", item.MachineWaza > 0 ? $"{item.MachineWaza.ToString(CultureInfo.InvariantCulture)} {labels.Move(item.MachineWaza)}" : "None"),
                ]),
            new ZaItemDetailGroup(
                "Effects",
                [
                    Detail("Cures sleep", ZaLabels.Bool(item.WorkRecvSleep)),
                    Detail("Cures poison", ZaLabels.Bool(item.WorkRecvPoison)),
                    Detail("Cures burn", ZaLabels.Bool(item.WorkRecvBurn)),
                    Detail("Cures freeze", ZaLabels.Bool(item.WorkRecvFreeze)),
                    Detail("Cures paralysis", ZaLabels.Bool(item.WorkRecvParalyze)),
                    Detail("Cures confusion", ZaLabels.Bool(item.WorkRecvConfuse)),
                    Detail("Cures infatuation", ZaLabels.Bool(item.WorkRecvMero)),
                    Detail("Healing power", item.WorkRecvPower),
                    Detail("Heal percentage", item.HealPercentage),
                    Detail("Revival count", item.WorkRevival),
                    Detail("Revive percentage", item.RevivePercentage),
                    Detail("EXP gain", item.ExpPointGain),
                    Detail("Max use level", item.MaxUseLevel),
                    Detail("Mint nature", $"{mintNature.ToString(CultureInfo.InvariantCulture)} {FormatNature(mintNature)}"),
                    Detail("Can use on Pokemon", ZaLabels.Bool(CanUseOnPokemon(item, mintNature))),
                    Detail("Evolution Item", ZaLabels.Bool(item.WorkEvolutional)),
                    Detail("Form change item", ZaLabels.Bool(item.WorkFormChange)),
                    Detail("Swap into item", item.SwapIntoId > 0 ? $"{item.SwapIntoId.ToString(CultureInfo.InvariantCulture)} {labels.Item(item.SwapIntoId)}" : "None"),
                ]),
        ];
    }

    private static IReadOnlyList<ZaItemEditableField> CreateEditableFields(
        ZaTextLabelLookup labels,
        IReadOnlyList<ZaItemRecord> items)
    {
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true);
        var technicalMachineCount = items.Count(IsTechnicalMachineRecord);
        return BaseEditableFields
            .Select(field => field.Field switch
            {
                MachineMoveIdField => field with
                {
                    MaximumValue = moveOptions.Count > 1 ? moveOptions.Max(option => option.Value) : field.MaximumValue,
                    Options = moveOptions,
                },
                TechnicalMachineNumberField => field with
                {
                    MaximumValue = Math.Max(technicalMachineCount, 1),
                },
                _ => field,
            })
            .ToArray();
    }

    private static ZaItemDetail Detail(string label, int value) =>
        new(label, value.ToString(CultureInfo.InvariantCulture));

    private static ZaItemDetail Detail(string label, string value) =>
        new(label, value);

    internal static bool IsTechnicalMachineRecord(ZaItemRecord item) =>
        item.Metadata.Pouch == 6
        && item.Metadata.ItemType == 5
        && item.Metadata.MachineMoveId is > 0;

    private static string FormatMachineNumberIssue(
        string label,
        IReadOnlyList<int> numbers)
    {
        return numbers.Count == 0
            ? string.Empty
            : $"{label}: {string.Join(", ", numbers.Take(8).Select(ZaTechnicalMachineCatalog.FormatMachineLabel))}"
                + (numbers.Count > 8 ? $" (+{numbers.Count - 8} more)" : string.Empty)
                + ". ";
    }

    private static int? GetTechnicalMachineNumber(ZaItemData item)
    {
        return ZaTechnicalMachineCatalog.IsTechnicalMachine(item)
            && item.SortNum > 0
            && item.MachineIndex == item.SortNum - 1
                ? item.SortNum
                : null;
    }

    private static ZaItemEditableField Field(
        string field,
        string label,
        string valueKind,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<ZaItemEditableFieldOption>? options = null)
    {
        return new ZaItemEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? []);
    }

    private static IReadOnlyList<ZaItemEditableFieldOption> CreateIndexedOptions(IReadOnlyList<string> names)
    {
        return names
            .Select((name, index) => new ZaItemEditableFieldOption(
                index,
                $"{index.ToString(CultureInfo.InvariantCulture)} {name}"))
            .ToArray();
    }

    private static IReadOnlyList<ZaItemEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : [];
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new ZaItemEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static string FormatIndexed(int value, IReadOnlyList<string> names, string fallbackPrefix)
    {
        return value >= 0 && value < names.Count && !string.IsNullOrWhiteSpace(names[value])
            ? names[value]
            : $"{fallbackPrefix} {value.ToString(CultureInfo.InvariantCulture)}";
    }
}
