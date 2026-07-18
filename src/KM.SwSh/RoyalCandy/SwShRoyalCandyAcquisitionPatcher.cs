// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.RoyalCandy;

internal sealed record SwShRoyalCandyAcquisitionConflict(
    string Location,
    ulong CurrentValue,
    ulong OriginalValue,
    ulong ReplacementValue);

internal sealed record SwShRoyalCandyAcquisitionAnalysis(
    int BaseOccurrenceCount,
    int OriginalOccurrenceCount,
    int ReplacementOccurrenceCount,
    IReadOnlyList<SwShRoyalCandyAcquisitionConflict> Conflicts)
{
    public int ConflictOccurrenceCount => Conflicts.Count;

    public bool HasConflicts => Conflicts.Count > 0;
}

internal sealed record SwShRoyalCandyAcquisitionPatchResult(
    byte[] Output,
    SwShRoyalCandyAcquisitionAnalysis Before,
    SwShRoyalCandyAcquisitionAnalysis After,
    int ChangedOccurrenceCount);

internal static class SwShRoyalCandyAcquisitionPatcher
{
    private const uint RareCandyItemId = 50;
    private const uint RoyalCandyItemId = 1128;
    private const string AreaNameHashTableMember = "AreaNameHashTable.tbl";
    private const string BerryTreeItemTableName = "PlacementZoneBerryTreeRandom";

    private static readonly string[] RewardArchiveMembers =
    [
        "nest_hole_drop_rewards.bin",
        "nest_hole_bonus_rewards.bin",
    ];

    public static SwShRoyalCandyAcquisitionAnalysis AnalyzeRaidRewards(
        byte[] targetPackBytes,
        byte[] basePackBytes)
    {
        return AnalyzeRaidRewardsCore(targetPackBytes, basePackBytes).Analysis;
    }

    public static SwShRoyalCandyAcquisitionPatchResult ApplyRaidRewards(
        byte[] targetPackBytes,
        byte[] basePackBytes)
    {
        return PatchRaidRewards(targetPackBytes, basePackBytes, restore: false);
    }

    public static SwShRoyalCandyAcquisitionPatchResult RestoreRaidRewards(
        byte[] targetPackBytes,
        byte[] basePackBytes)
    {
        return PatchRaidRewards(targetPackBytes, basePackBytes, restore: true);
    }

    public static SwShRoyalCandyAcquisitionAnalysis AnalyzePlacement(
        byte[] targetPackBytes,
        byte[] basePackBytes,
        byte[] baseItemHashBytes)
    {
        return AnalyzePlacementCore(targetPackBytes, basePackBytes, baseItemHashBytes).Analysis;
    }

    public static SwShRoyalCandyAcquisitionPatchResult ApplyPlacement(
        byte[] targetPackBytes,
        byte[] basePackBytes,
        byte[] baseItemHashBytes)
    {
        return PatchPlacement(targetPackBytes, basePackBytes, baseItemHashBytes, restore: false);
    }

    public static SwShRoyalCandyAcquisitionPatchResult RestorePlacement(
        byte[] targetPackBytes,
        byte[] basePackBytes,
        byte[] baseItemHashBytes)
    {
        return PatchPlacement(targetPackBytes, basePackBytes, baseItemHashBytes, restore: true);
    }

    private static SwShRoyalCandyAcquisitionPatchResult PatchRaidRewards(
        byte[] targetPackBytes,
        byte[] basePackBytes,
        bool restore)
    {
        var context = AnalyzeRaidRewardsCore(targetPackBytes, basePackBytes);
        var isExactCanonicalInstall = restore
            && context.Analysis.BaseOccurrenceCount > 0
            && context.Analysis.OriginalOccurrenceCount == 0
            && context.Analysis.ReplacementOccurrenceCount == context.Analysis.BaseOccurrenceCount
            && !context.Analysis.HasConflicts
            && targetPackBytes.AsSpan().SequenceEqual(
                PatchRaidRewards(basePackBytes, basePackBytes, restore: false).Output);
        var stateToPatch = restore
            ? AcquisitionOccurrenceState.Replacement
            : AcquisitionOccurrenceState.Original;
        var replacementValue = restore ? RoyalCandyItemId : RareCandyItemId;
        var changed = 0;

        foreach (var member in context.Members.OrderBy(member => member.FileName, StringComparer.Ordinal))
        {
            var edits = member.Occurrences
                .Where(occurrence => occurrence.State == stateToPatch)
                .Select(occurrence => new SwShNestHoleRewardEdit(
                    occurrence.TableIndex,
                    occurrence.RewardIndex,
                    SwShNestHoleRewardField.ItemId,
                    replacementValue))
                .ToArray();
            if (edits.Length == 0)
            {
                continue;
            }

            context.TargetPack.SetFileByName(member.FileName, member.Archive.WriteEdits(edits));
            changed += edits.Length;
        }

        var output = isExactCanonicalInstall
            ? basePackBytes.ToArray()
            : changed == 0
            ? targetPackBytes.ToArray()
            : context.TargetPack.Write();
        var after = AnalyzeRaidRewardsCore(output, basePackBytes).Analysis;
        VerifyTransition(context.Analysis, after, changed, restore, "raid reward");
        return new SwShRoyalCandyAcquisitionPatchResult(
            output,
            context.Analysis,
            after,
            changed);
    }

    private static RaidAnalysisContext AnalyzeRaidRewardsCore(
        byte[] targetPackBytes,
        byte[] basePackBytes)
    {
        ArgumentNullException.ThrowIfNull(targetPackBytes);
        ArgumentNullException.ThrowIfNull(basePackBytes);

        var targetPack = SwShGfPackFile.Parse(targetPackBytes);
        var basePack = SwShGfPackFile.Parse(basePackBytes);
        var members = new List<RaidMemberAnalysis>();
        var occurrences = new List<AnalyzedOccurrence>();

        foreach (var memberName in RewardArchiveMembers)
        {
            RequireMember(basePack, memberName, "base raid reward");
            RequireMember(targetPack, memberName, "target raid reward");

            var baseArchive = SwShNestHoleRewardArchive.Parse(basePack.GetFileByName(memberName));
            var targetArchive = SwShNestHoleRewardArchive.Parse(targetPack.GetFileByName(memberName));
            var memberOccurrences = new List<RaidOccurrence>();

            for (var tableIndex = 0; tableIndex < baseArchive.Tables.Count; tableIndex++)
            {
                var baseTable = baseArchive.Tables[tableIndex];
                var ownedRewardIndexes = baseTable.Rewards
                    .Select((reward, rewardIndex) => (reward, rewardIndex))
                    .Where(entry => entry.reward.ItemId == RoyalCandyItemId)
                    .Select(entry => entry.rewardIndex)
                    .ToArray();
                if (ownedRewardIndexes.Length == 0)
                {
                    continue;
                }

                if ((uint)tableIndex >= (uint)targetArchive.Tables.Count)
                {
                    throw new InvalidDataException(
                        $"Target raid reward member '{memberName}' is missing base table index {tableIndex}.");
                }

                var targetTable = targetArchive.Tables[tableIndex];
                if (targetTable.TableId != baseTable.TableId)
                {
                    throw new InvalidDataException(
                        $"Target raid reward member '{memberName}' table index {tableIndex} does not retain base table ID 0x{baseTable.TableId:X16}.");
                }

                if (targetTable.Rewards.Count != baseTable.Rewards.Count)
                {
                    throw new InvalidDataException(
                        $"Target raid reward member '{memberName}' table 0x{baseTable.TableId:X16} has {targetTable.Rewards.Count} rows; expected {baseTable.Rewards.Count}.");
                }

                foreach (var rewardIndex in ownedRewardIndexes)
                {
                    var baseReward = baseTable.Rewards[rewardIndex];
                    var targetReward = targetTable.Rewards[rewardIndex];
                    if (targetReward.EntryId != baseReward.EntryId)
                    {
                        throw new InvalidDataException(
                            $"Target raid reward member '{memberName}' table 0x{baseTable.TableId:X16} row {rewardIndex} does not retain base entry ID {baseReward.EntryId}.");
                    }

                    var location = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{memberName}/table {tableIndex}/reward {rewardIndex}");
                    var analyzed = AnalyzeOccurrence(
                        location,
                        targetReward.ItemId,
                        RoyalCandyItemId,
                        RareCandyItemId);
                    occurrences.Add(analyzed);
                    memberOccurrences.Add(new RaidOccurrence(
                        tableIndex,
                        rewardIndex,
                        analyzed.State));
                }
            }

            members.Add(new RaidMemberAnalysis(memberName, targetArchive, memberOccurrences));
        }

        return new RaidAnalysisContext(
            targetPack,
            members,
            CreateAnalysis(occurrences));
    }

    private static SwShRoyalCandyAcquisitionPatchResult PatchPlacement(
        byte[] targetPackBytes,
        byte[] basePackBytes,
        byte[] baseItemHashBytes,
        bool restore)
    {
        var context = AnalyzePlacementCore(targetPackBytes, basePackBytes, baseItemHashBytes);
        var isExactCanonicalInstall = restore
            && context.Analysis.BaseOccurrenceCount > 0
            && context.Analysis.OriginalOccurrenceCount == 0
            && context.Analysis.ReplacementOccurrenceCount == context.Analysis.BaseOccurrenceCount
            && !context.Analysis.HasConflicts
            && targetPackBytes.AsSpan().SequenceEqual(
                PatchPlacement(
                    basePackBytes,
                    basePackBytes,
                    baseItemHashBytes,
                    restore: false).Output);
        var stateToPatch = restore
            ? AcquisitionOccurrenceState.Replacement
            : AcquisitionOccurrenceState.Original;
        var changed = 0;

        foreach (var member in context.Members.OrderBy(member => member.FileName, StringComparer.Ordinal))
        {
            var edits = member.Occurrences
                .Where(occurrence => occurrence.State == stateToPatch)
                .OrderBy(occurrence => occurrence.Offset)
                .ToArray();
            if (edits.Length == 0)
            {
                continue;
            }

            var output = member.Data.ToArray();
            foreach (var edit in edits)
            {
                if (edit.Width == sizeof(ulong))
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(
                        output.AsSpan(edit.Offset, sizeof(ulong)),
                        restore ? context.ItemHashes.RoyalCandyHash : context.ItemHashes.RareCandyHash);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        output.AsSpan(edit.Offset, sizeof(uint)),
                        restore ? RoyalCandyItemId : RareCandyItemId);
                }
            }

            _ = SwShPlacementZoneArchive.Parse(output, context.ItemHashes.ItemIdsByHash);
            context.TargetPack.SetFileByName(member.FileName, output);
            changed += edits.Length;
        }

        var packOutput = isExactCanonicalInstall
            ? basePackBytes.ToArray()
            : changed == 0
            ? targetPackBytes.ToArray()
            : context.TargetPack.Write();
        var after = AnalyzePlacementCore(packOutput, basePackBytes, baseItemHashBytes).Analysis;
        VerifyTransition(context.Analysis, after, changed, restore, "placement");
        return new SwShRoyalCandyAcquisitionPatchResult(
            packOutput,
            context.Analysis,
            after,
            changed);
    }

    private static PlacementAnalysisContext AnalyzePlacementCore(
        byte[] targetPackBytes,
        byte[] basePackBytes,
        byte[] baseItemHashBytes)
    {
        ArgumentNullException.ThrowIfNull(targetPackBytes);
        ArgumentNullException.ThrowIfNull(basePackBytes);
        ArgumentNullException.ThrowIfNull(baseItemHashBytes);

        var itemHashes = ResolveItemHashes(baseItemHashBytes);
        var targetPack = SwShGfPackFile.Parse(targetPackBytes);
        var basePack = SwShGfPackFile.Parse(basePackBytes);
        var baseAreaMembers = ReadAreaMembers(basePack, "base");
        var targetAreaMembers = ReadAreaMembers(targetPack, "target")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var members = new List<PlacementMemberAnalysis>();
        var occurrences = new List<AnalyzedOccurrence>();

        foreach (var memberName in baseAreaMembers)
        {
            RequireMember(basePack, memberName, "base placement");
            var baseData = basePack.GetFileByName(memberName);
            var baseArchive = SwShPlacementZoneArchive.Parse(baseData, itemHashes.ItemIdsByHash);
            var baseOccurrences = FindBasePlacementOccurrences(
                memberName,
                baseArchive,
                itemHashes.RoyalCandyHash);
            if (baseOccurrences.Count == 0)
            {
                continue;
            }

            if (!targetAreaMembers.Contains(memberName))
            {
                throw new InvalidDataException(
                    $"Target placement area table is missing base member '{memberName}' with Royal Candy-owned item references.");
            }

            RequireMember(targetPack, memberName, "target placement");
            var targetData = targetPack.GetFileByName(memberName);
            var targetArchive = SwShPlacementZoneArchive.Parse(targetData, itemHashes.ItemIdsByHash);
            var memberOccurrences = ResolveTargetPlacementOccurrences(
                memberName,
                baseArchive,
                targetArchive,
                baseOccurrences,
                itemHashes);
            foreach (var occurrence in memberOccurrences)
            {
                occurrences.Add(occurrence.Analyzed);
            }

            members.Add(new PlacementMemberAnalysis(memberName, targetData, memberOccurrences));
        }

        return new PlacementAnalysisContext(
            targetPack,
            members,
            itemHashes,
            CreateAnalysis(occurrences));
    }

    private static IReadOnlyList<BasePlacementOccurrence> FindBasePlacementOccurrences(
        string memberName,
        SwShPlacementZoneArchive archive,
        ulong royalCandyHash)
    {
        var occurrences = new List<BasePlacementOccurrence>();
        var storage = new HashSet<(int Offset, int Width)>();

        foreach (var zone in archive.Zones)
        {
            foreach (var item in zone.FieldItems)
            {
                for (var index = 0; index < item.ItemHashes.Count; index++)
                {
                    if (item.ItemHashes[index] != royalCandyHash)
                    {
                        continue;
                    }

                    AddPlacementOccurrence(
                        occurrences,
                        storage,
                        new BasePlacementOccurrence(
                            memberName,
                            zone.ZoneIndex,
                            zone.ZoneId,
                            PlacementOccurrenceKind.FieldItemHash,
                            item.ObjectIndex,
                            index,
                            RawField: null,
                            item.ItemHashOffsets[index],
                            sizeof(ulong)));
                }

                for (var index = 0; index < item.ItemIds.Count; index++)
                {
                    if (item.ItemIds[index] != RoyalCandyItemId)
                    {
                        continue;
                    }

                    AddPlacementOccurrence(
                        occurrences,
                        storage,
                        new BasePlacementOccurrence(
                            memberName,
                            zone.ZoneIndex,
                            zone.ZoneId,
                            PlacementOccurrenceKind.FieldItemDirectId,
                            item.ObjectIndex,
                            index,
                            RawField: null,
                            item.ItemIdOffsets[index],
                            sizeof(uint)));
                }
            }

            foreach (var item in zone.HiddenItems)
            {
                foreach (var chance in item.Chances.Where(chance => chance.ItemHash == royalCandyHash))
                {
                    AddPlacementOccurrence(
                        occurrences,
                        storage,
                        new BasePlacementOccurrence(
                            memberName,
                            zone.ZoneIndex,
                            zone.ZoneId,
                            PlacementOccurrenceKind.HiddenItemHash,
                            item.ObjectIndex,
                            chance.ChanceIndex,
                            RawField: null,
                            chance.ItemHashOffset,
                            sizeof(ulong)));
                }
            }

            foreach (var rawObject in zone.RawObjects.Where(rawObject =>
                string.Equals(rawObject.ObjectType, "BerryTree", StringComparison.Ordinal)))
            {
                foreach (var field in rawObject.Fields.Where(field =>
                    string.Equals(field.TableName, BerryTreeItemTableName, StringComparison.Ordinal)
                    && string.Equals(field.StorageKind, "ulong", StringComparison.Ordinal)
                    && TryParseHash(field.Value, out var value)
                    && value == royalCandyHash))
                {
                    AddPlacementOccurrence(
                        occurrences,
                        storage,
                        new BasePlacementOccurrence(
                            memberName,
                            zone.ZoneIndex,
                            zone.ZoneId,
                            PlacementOccurrenceKind.BerryTreeHash,
                            rawObject.ObjectIndex,
                            ElementIndex: -1,
                            field.Field,
                            field.ValueOffset,
                            sizeof(ulong)));
                }
            }
        }

        return occurrences;
    }

    private static void AddPlacementOccurrence(
        ICollection<BasePlacementOccurrence> occurrences,
        ISet<(int Offset, int Width)> storage,
        BasePlacementOccurrence occurrence)
    {
        if (occurrence.BaseOffset <= 0)
        {
            throw new InvalidDataException(
                $"Base placement reference '{FormatPlacementLocation(occurrence)}' does not expose writable storage.");
        }

        if (storage.Add((occurrence.BaseOffset, occurrence.Width)))
        {
            occurrences.Add(occurrence);
        }
    }

    private static IReadOnlyList<ResolvedPlacementOccurrence> ResolveTargetPlacementOccurrences(
        string memberName,
        SwShPlacementZoneArchive baseArchive,
        SwShPlacementZoneArchive targetArchive,
        IReadOnlyList<BasePlacementOccurrence> baseOccurrences,
        ItemHashSemantics itemHashes)
    {
        var result = new List<ResolvedPlacementOccurrence>(baseOccurrences.Count);
        var targetStorage = new HashSet<(int Offset, int Width)>();

        foreach (var occurrence in baseOccurrences)
        {
            if ((uint)occurrence.ZoneIndex >= (uint)targetArchive.Zones.Count)
            {
                throw new InvalidDataException(
                    $"Target placement member '{memberName}' is missing base zone index {occurrence.ZoneIndex}.");
            }

            var baseZone = baseArchive.Zones[occurrence.ZoneIndex];
            var targetZone = targetArchive.Zones[occurrence.ZoneIndex];
            if (targetZone.ZoneId != occurrence.ZoneId)
            {
                throw new InvalidDataException(
                    $"Target placement member '{memberName}' zone index {occurrence.ZoneIndex} does not retain base zone ID 0x{occurrence.ZoneId:X16}.");
            }

            var target = occurrence.Kind switch
            {
                PlacementOccurrenceKind.FieldItemHash => ResolveFieldItemOccurrence(
                    occurrence,
                    baseZone,
                    targetZone,
                    useHashStorage: true),
                PlacementOccurrenceKind.FieldItemDirectId => ResolveFieldItemOccurrence(
                    occurrence,
                    baseZone,
                    targetZone,
                    useHashStorage: false),
                PlacementOccurrenceKind.HiddenItemHash => ResolveHiddenItemOccurrence(
                    occurrence,
                    baseZone,
                    targetZone),
                PlacementOccurrenceKind.BerryTreeHash => ResolveBerryTreeOccurrence(
                    occurrence,
                    baseZone,
                    targetZone),
                _ => throw new InvalidDataException("Placement acquisition reference kind is not supported."),
            };

            if (target.Offset <= 0)
            {
                throw new InvalidDataException(
                    $"Target placement reference '{FormatPlacementLocation(occurrence)}' does not expose writable storage.");
            }

            if (!targetStorage.Add((target.Offset, target.Width)))
            {
                throw new InvalidDataException(
                    $"Target placement member '{memberName}' maps multiple vanilla item references to the same physical storage.");
            }

            var originalValue = target.Width == sizeof(ulong)
                ? itemHashes.RoyalCandyHash
                : RoyalCandyItemId;
            var replacementValue = target.Width == sizeof(ulong)
                ? itemHashes.RareCandyHash
                : RareCandyItemId;
            var location = FormatPlacementLocation(occurrence);
            var analyzed = AnalyzeOccurrence(
                location,
                target.CurrentValue,
                originalValue,
                replacementValue);
            result.Add(new ResolvedPlacementOccurrence(
                target.Offset,
                target.Width,
                analyzed.State,
                analyzed));
        }

        return result;
    }

    private static TargetPlacementStorage ResolveFieldItemOccurrence(
        BasePlacementOccurrence occurrence,
        SwShPlacementZone baseZone,
        SwShPlacementZone targetZone,
        bool useHashStorage)
    {
        if (targetZone.FieldItems.Count != baseZone.FieldItems.Count
            || (uint)occurrence.ObjectIndex >= (uint)targetZone.FieldItems.Count)
        {
            throw new InvalidDataException(
                $"Target placement reference '{FormatPlacementLocation(occurrence)}' does not retain the base field-item topology.");
        }

        var baseItem = baseZone.FieldItems[occurrence.ObjectIndex];
        var targetItem = targetZone.FieldItems[occurrence.ObjectIndex];
        if (useHashStorage)
        {
            if (targetItem.ItemHashes.Count != baseItem.ItemHashes.Count
                || targetItem.ItemHashOffsets.Count != targetItem.ItemHashes.Count
                || (uint)occurrence.ElementIndex >= (uint)targetItem.ItemHashes.Count)
            {
                throw new InvalidDataException(
                    $"Target placement reference '{FormatPlacementLocation(occurrence)}' does not retain the base hash-vector topology.");
            }

            return new TargetPlacementStorage(
                targetItem.ItemHashOffsets[occurrence.ElementIndex],
                sizeof(ulong),
                targetItem.ItemHashes[occurrence.ElementIndex]);
        }

        if (targetItem.ItemIds.Count != baseItem.ItemIds.Count
            || targetItem.ItemIdOffsets.Count != targetItem.ItemIds.Count
            || (uint)occurrence.ElementIndex >= (uint)targetItem.ItemIds.Count)
        {
            throw new InvalidDataException(
                $"Target placement reference '{FormatPlacementLocation(occurrence)}' does not retain the base direct-ID vector topology.");
        }

        return new TargetPlacementStorage(
            targetItem.ItemIdOffsets[occurrence.ElementIndex],
            sizeof(uint),
            targetItem.ItemIds[occurrence.ElementIndex]);
    }

    private static TargetPlacementStorage ResolveHiddenItemOccurrence(
        BasePlacementOccurrence occurrence,
        SwShPlacementZone baseZone,
        SwShPlacementZone targetZone)
    {
        if (targetZone.HiddenItems.Count != baseZone.HiddenItems.Count
            || (uint)occurrence.ObjectIndex >= (uint)targetZone.HiddenItems.Count)
        {
            throw new InvalidDataException(
                $"Target placement reference '{FormatPlacementLocation(occurrence)}' does not retain the base hidden-item topology.");
        }

        var baseItem = baseZone.HiddenItems[occurrence.ObjectIndex];
        var targetItem = targetZone.HiddenItems[occurrence.ObjectIndex];
        if (targetItem.Chances.Count != baseItem.Chances.Count
            || (uint)occurrence.ElementIndex >= (uint)targetItem.Chances.Count)
        {
            throw new InvalidDataException(
                $"Target placement reference '{FormatPlacementLocation(occurrence)}' does not retain the base hidden-item chance topology.");
        }

        var chance = targetItem.Chances[occurrence.ElementIndex];
        if (chance.ChanceIndex != occurrence.ElementIndex)
        {
            throw new InvalidDataException(
                $"Target placement reference '{FormatPlacementLocation(occurrence)}' does not retain the base chance index.");
        }

        return new TargetPlacementStorage(
            chance.ItemHashOffset,
            sizeof(ulong),
            chance.ItemHash);
    }

    private static TargetPlacementStorage ResolveBerryTreeOccurrence(
        BasePlacementOccurrence occurrence,
        SwShPlacementZone baseZone,
        SwShPlacementZone targetZone)
    {
        var baseBerryCount = baseZone.RawObjects.Count(item =>
            string.Equals(item.ObjectType, "BerryTree", StringComparison.Ordinal));
        var targetBerryCount = targetZone.RawObjects.Count(item =>
            string.Equals(item.ObjectType, "BerryTree", StringComparison.Ordinal));
        if (targetBerryCount != baseBerryCount)
        {
            throw new InvalidDataException(
                $"Target placement reference '{FormatPlacementLocation(occurrence)}' does not retain the base berry-tree topology.");
        }

        var targetObject = targetZone.RawObjects
            .Where(item => string.Equals(item.ObjectType, "BerryTree", StringComparison.Ordinal)
                && item.ObjectIndex == occurrence.ObjectIndex)
            .SingleOrDefault()
            ?? throw new InvalidDataException(
                $"Target placement reference '{FormatPlacementLocation(occurrence)}' is missing its base berry-tree object.");
        var targetField = targetObject.Fields
            .Where(field => string.Equals(field.Field, occurrence.RawField, StringComparison.Ordinal)
                && string.Equals(field.TableName, BerryTreeItemTableName, StringComparison.Ordinal)
                && string.Equals(field.StorageKind, "ulong", StringComparison.Ordinal))
            .SingleOrDefault()
            ?? throw new InvalidDataException(
                $"Target placement reference '{FormatPlacementLocation(occurrence)}' is missing its base berry-tree item field.");
        if (targetField.ValueOffset <= 0 || !TryParseHash(targetField.Value, out var value))
        {
            throw new InvalidDataException(
                $"Target placement reference '{FormatPlacementLocation(occurrence)}' does not expose readable hash storage.");
        }

        return new TargetPlacementStorage(
            targetField.ValueOffset,
            sizeof(ulong),
            value);
    }

    private static IReadOnlyList<string> ReadAreaMembers(
        SwShGfPackFile pack,
        string label)
    {
        RequireMember(pack, AreaNameHashTableMember, $"{label} placement");
        var table = SwShAhtbFile.Parse(pack.GetFileByName(AreaNameHashTableMember));
        var members = table.Entries
            .Select(entry => entry.Name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                ? name
                : name + ".bin")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (members.Length == 0)
        {
            throw new InvalidDataException(
                $"{label} placement area table does not contain any area members.");
        }

        return members;
    }

    private static ItemHashSemantics ResolveItemHashes(byte[] itemHashBytes)
    {
        var table = SwShItemHashTable.Parse(itemHashBytes);
        var hashes = table.ToHashByItemId();
        if (!hashes.TryGetValue((int)RoyalCandyItemId, out var royalCandyHash)
            || !hashes.TryGetValue((int)RareCandyItemId, out var rareCandyHash)
            || royalCandyHash == 0
            || rareCandyHash == 0
            || royalCandyHash == rareCandyHash)
        {
            throw new InvalidDataException(
                "Base item hash table must contain distinct nonzero mappings for item 1128 and item 50.");
        }

        return new ItemHashSemantics(
            royalCandyHash,
            rareCandyHash,
            table.ToItemIdByHash());
    }

    private static void RequireMember(
        SwShGfPackFile pack,
        string memberName,
        string label)
    {
        if (!pack.ContainsFileName(memberName))
        {
            throw new InvalidDataException(
                $"{label} GFPAK does not contain required member '{memberName}'.");
        }
    }

    private static AnalyzedOccurrence AnalyzeOccurrence(
        string location,
        ulong currentValue,
        ulong originalValue,
        ulong replacementValue)
    {
        var state = currentValue == originalValue
            ? AcquisitionOccurrenceState.Original
            : currentValue == replacementValue
                ? AcquisitionOccurrenceState.Replacement
                : AcquisitionOccurrenceState.Conflict;
        return new AnalyzedOccurrence(
            location,
            currentValue,
            originalValue,
            replacementValue,
            state);
    }

    private static SwShRoyalCandyAcquisitionAnalysis CreateAnalysis(
        IReadOnlyList<AnalyzedOccurrence> occurrences)
    {
        return new SwShRoyalCandyAcquisitionAnalysis(
            occurrences.Count,
            occurrences.Count(occurrence => occurrence.State == AcquisitionOccurrenceState.Original),
            occurrences.Count(occurrence => occurrence.State == AcquisitionOccurrenceState.Replacement),
            occurrences
                .Where(occurrence => occurrence.State == AcquisitionOccurrenceState.Conflict)
                .Select(occurrence => new SwShRoyalCandyAcquisitionConflict(
                    occurrence.Location,
                    occurrence.CurrentValue,
                    occurrence.OriginalValue,
                    occurrence.ReplacementValue))
                .OrderBy(conflict => conflict.Location, StringComparer.Ordinal)
                .ToArray());
    }

    private static void VerifyTransition(
        SwShRoyalCandyAcquisitionAnalysis before,
        SwShRoyalCandyAcquisitionAnalysis after,
        int changed,
        bool restore,
        string label)
    {
        var expectedChanged = restore
            ? before.ReplacementOccurrenceCount
            : before.OriginalOccurrenceCount;
        var expectedOriginal = restore
            ? before.OriginalOccurrenceCount + before.ReplacementOccurrenceCount
            : 0;
        var expectedReplacement = restore
            ? 0
            : before.OriginalOccurrenceCount + before.ReplacementOccurrenceCount;
        if (changed != expectedChanged
            || after.BaseOccurrenceCount != before.BaseOccurrenceCount
            || after.OriginalOccurrenceCount != expectedOriginal
            || after.ReplacementOccurrenceCount != expectedReplacement
            || !after.Conflicts.SequenceEqual(before.Conflicts))
        {
            throw new InvalidDataException(
                $"Royal Candy {label} output did not preserve and transition every verified vanilla acquisition reference.");
        }
    }

    private static string FormatPlacementLocation(BasePlacementOccurrence occurrence)
    {
        var suffix = occurrence.Kind switch
        {
            PlacementOccurrenceKind.FieldItemHash => $"FieldItem {occurrence.ObjectIndex}/hash {occurrence.ElementIndex}",
            PlacementOccurrenceKind.FieldItemDirectId => $"FieldItem {occurrence.ObjectIndex}/item {occurrence.ElementIndex}",
            PlacementOccurrenceKind.HiddenItemHash => $"HiddenItem {occurrence.ObjectIndex}/chance {occurrence.ElementIndex}",
            PlacementOccurrenceKind.BerryTreeHash => $"BerryTree {occurrence.ObjectIndex}/{occurrence.RawField}",
            _ => "unknown",
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{occurrence.MemberName}/zone {occurrence.ZoneIndex}/{suffix}");
    }

    private static bool TryParseHash(string value, out ulong parsed)
    {
        parsed = 0;
        var trimmed = value.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(
                trimmed[2..],
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out parsed)
            : ulong.TryParse(
                trimmed,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed);
    }

    private enum AcquisitionOccurrenceState
    {
        Original,
        Replacement,
        Conflict,
    }

    private enum PlacementOccurrenceKind
    {
        FieldItemHash,
        FieldItemDirectId,
        HiddenItemHash,
        BerryTreeHash,
    }

    private sealed record AnalyzedOccurrence(
        string Location,
        ulong CurrentValue,
        ulong OriginalValue,
        ulong ReplacementValue,
        AcquisitionOccurrenceState State);

    private sealed record RaidOccurrence(
        int TableIndex,
        int RewardIndex,
        AcquisitionOccurrenceState State);

    private sealed record RaidMemberAnalysis(
        string FileName,
        SwShNestHoleRewardArchive Archive,
        IReadOnlyList<RaidOccurrence> Occurrences);

    private sealed record RaidAnalysisContext(
        SwShGfPackFile TargetPack,
        IReadOnlyList<RaidMemberAnalysis> Members,
        SwShRoyalCandyAcquisitionAnalysis Analysis);

    private sealed record BasePlacementOccurrence(
        string MemberName,
        int ZoneIndex,
        ulong ZoneId,
        PlacementOccurrenceKind Kind,
        int ObjectIndex,
        int ElementIndex,
        string? RawField,
        int BaseOffset,
        int Width);

    private sealed record TargetPlacementStorage(
        int Offset,
        int Width,
        ulong CurrentValue);

    private sealed record ResolvedPlacementOccurrence(
        int Offset,
        int Width,
        AcquisitionOccurrenceState State,
        AnalyzedOccurrence Analyzed);

    private sealed record PlacementMemberAnalysis(
        string FileName,
        byte[] Data,
        IReadOnlyList<ResolvedPlacementOccurrence> Occurrences);

    private sealed record ItemHashSemantics(
        ulong RoyalCandyHash,
        ulong RareCandyHash,
        IReadOnlyDictionary<ulong, int> ItemIdsByHash);

    private sealed record PlacementAnalysisContext(
        SwShGfPackFile TargetPack,
        IReadOnlyList<PlacementMemberAnalysis> Members,
        ItemHashSemantics ItemHashes,
        SwShRoyalCandyAcquisitionAnalysis Analysis);
}
