// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Items;

public sealed class SwShItemsWorkflowService
{
    public const string BuyPriceField = "buyPrice";
    public const string SellPriceField = "sellPrice";
    public const string WattsPriceField = "wattsPrice";
    public const string AlternatePriceField = "alternatePrice";
    public const int MaximumBuyPrice = 999_999;
    public const int MaximumSellPrice = MaximumBuyPrice / 2;
    public const int MaximumWattsPrice = 999_999;
    public const int MaximumAlternatePrice = 999_999;
    public const string ItemDataPath = SwShItemTable.ItemDataRelativePath;
    public const string EnglishItemNamePath = "romfs/bin/message/English/common/itemname.dat";

    private const int TechnicalRecordMachineGroup = 4;
    private const int TechnicalRecordFieldUseType = 2;
    private const int TechnicalRecordTrSlotStart = 100;
    private const int TechnicalRecordLastSlot = 199;

    private static readonly IReadOnlyList<SwShItemEditableField> EditableFields =
    [
        new SwShItemEditableField(
            BuyPriceField,
            "Buy price",
            "integer",
            MinimumValue: 0,
            MaximumBuyPrice),
        new SwShItemEditableField(
            SellPriceField,
            "Sell price",
            "integer",
            MinimumValue: 0,
            MaximumSellPrice),
        new SwShItemEditableField(
            WattsPriceField,
            "Watts price",
            "integer",
            MinimumValue: 0,
            MaximumWattsPrice),
        new SwShItemEditableField(
            AlternatePriceField,
            "Alternate price",
            "integer",
            MinimumValue: 0,
            MaximumAlternatePrice),
    ];

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
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), sourceFileCount: 0, diagnostics);
        }

        var itemDataSource = ResolveWorkflowFile(project, ItemDataPath);
        if (itemDataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Items data is not available for this project.",
                expected: ItemDataPath));
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), sourceFileCount: 0, diagnostics);
        }

        var itemNamesSource = ResolveItemNamesSource(project, diagnostics);
        var itemNames = itemNamesSource is null
            ? Array.Empty<string>()
            : LoadItemNames(itemNamesSource, diagnostics);

        try
        {
            var itemTable = SwShItemTable.Parse(File.ReadAllBytes(itemDataSource.AbsolutePath));
            var provenance = CreateProvenance(itemDataSource.GraphEntry);
            var items = itemTable.Records
                .OrderBy(item => item.ItemId)
                .Select(item => ToItemRecord(item, itemNames, provenance))
                .ToArray();
            var sourceFileCount = itemNamesSource is null ? 1 : 2;

            return CreateWorkflow(summary, items, sourceFileCount, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items data source is not a supported Sword/Shield item table: {exception.Message}",
                file: itemDataSource.GraphEntry.RelativePath,
                expected: "Sword/Shield item.dat"));
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), sourceFileCount: 1, diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Items data source could not be read: {exception.Message}",
                file: itemDataSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield item.dat"));
            return CreateWorkflow(summary, Array.Empty<SwShItemRecord>(), sourceFileCount: 1, diagnostics);
        }
    }

    internal static WorkflowFileSource? ResolveItemDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, ItemDataPath);
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
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShItemsWorkflow(
            summary,
            items,
            EditableFields,
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
        SwShItemProvenance provenance)
    {
        return new SwShItemRecord(
            item.ItemId,
            GetItemName(item.ItemId, itemNames),
            FormatPouch(item.Pouch),
            checked((int)item.BuyPrice),
            checked((int)(item.BuyPrice / 2)),
            checked((int)item.WattsPrice),
            checked((int)item.AlternatePrice),
            item.SharedItemIds,
            CreateDetailGroups(item),
            provenance);
    }

    private static string GetItemName(int itemId, IReadOnlyList<string> itemNames)
    {
        if ((uint)itemId < (uint)itemNames.Count && !string.IsNullOrWhiteSpace(itemNames[itemId]))
        {
            return itemNames[itemId];
        }

        return $"Item {itemId}";
    }

    private static string FormatPouch(SwShItemPouch pouch)
    {
        return pouch switch
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
            _ => $"Pouch {(byte)pouch}",
        };
    }

    private static IReadOnlyList<SwShItemDetailGroup> CreateDetailGroups(SwShItemTableRecord item)
    {
        return
        [
            new SwShItemDetailGroup(
                "Inventory",
                [
                    new SwShItemDetail("Pouch", $"{FormatPouch(item.Pouch)} ({(byte)item.Pouch})"),
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
                    new SwShItemDetail("Field flags", FormatHex(item.FieldFlags)),
                    new SwShItemDetail("Can use on Pokemon", FormatBool(item.CanUseOnPokemon)),
                    new SwShItemDetail("Can target fainted Pokemon", FormatBool((item.Boost0 & 0x01) != 0)),
                    new SwShItemDetail("Revives whole party", FormatBool((item.Boost0 & 0x02) != 0)),
                    new SwShItemDetail("Level up item", FormatBool((item.Boost0 & 0x04) != 0)),
                    new SwShItemDetail("Evolution item", FormatBool((item.Boost0 & 0x08) != 0)),
                    new SwShItemDetail("Use flags 1", FormatFlags(
                        item.UseFlags1,
                        (0x01, "Restore PP"),
                        (0x02, "Restore all PP"),
                        (0x04, "Restore HP"),
                        (0x08, "HP EV"),
                        (0x10, "Attack EV"),
                        (0x20, "Defense EV"),
                        (0x40, "Speed EV"),
                        (0x80, "Sp. Atk EV"))),
                    new SwShItemDetail("Use flags 2", FormatFlags(
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
                    new SwShItemDetail("Critical hit boost", ((item.Boost3 >> 4) & 0x03).ToString(CultureInfo.InvariantCulture)),
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

    private static string FormatGroupType(byte value)
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

    private static string FormatFieldUseType(byte value)
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

    private static string FormatMachineSummary(SwShItemTableRecord item)
    {
        if (item.GroupType != TechnicalRecordMachineGroup
            || item.FieldUseType != TechnicalRecordFieldUseType
            || item.GroupIndex > TechnicalRecordLastSlot)
        {
            return "No machine link";
        }

        var isTr = item.GroupIndex >= TechnicalRecordTrSlotStart;
        var number = isTr ? item.GroupIndex - TechnicalRecordTrSlotStart : item.GroupIndex;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(isTr ? "TR" : "TM")}{number:00} (slot {item.GroupIndex})");
    }

    private static string FormatFlags(byte value, params (int Flag, string Label)[] flags)
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

    private static string FormatBattleBoosts(SwShItemTableRecord item)
    {
        return string.Join(
            " / ",
            [
                $"Atk {(item.Boost0 >> 4).ToString(CultureInfo.InvariantCulture)}",
                $"Def {(item.Boost1 & 0x0F).ToString(CultureInfo.InvariantCulture)}",
                $"SpA {(item.Boost1 >> 4).ToString(CultureInfo.InvariantCulture)}",
                $"SpD {(item.Boost2 & 0x0F).ToString(CultureInfo.InvariantCulture)}",
                $"Spe {(item.Boost2 >> 4).ToString(CultureInfo.InvariantCulture)}",
                $"Acc {(item.Boost3 & 0x0F).ToString(CultureInfo.InvariantCulture)}",
            ]);
    }

    private static string FormatEvGain(SwShItemTableRecord item)
    {
        var values = new (string Label, sbyte Value)[]
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

    private static string FormatHealAmount(byte value)
    {
        return value switch
        {
            253 => "Quarter HP",
            254 => "Half HP",
            255 => "Full HP",
            _ => $"{value.ToString(CultureInfo.InvariantCulture)} HP",
        };
    }

    private static string FormatFriendshipGains(SwShItemTableRecord item)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatSigned(item.FriendshipGain1)} / {FormatSigned(item.FriendshipGain2)} / {FormatSigned(item.FriendshipGain3)}");
    }

    private static string FormatSigned(sbyte value)
    {
        return value > 0
            ? $"+{value.ToString(CultureInfo.InvariantCulture)}"
            : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatBool(bool value)
    {
        return value ? "Yes" : "No";
    }

    private static string FormatHex(byte value)
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
