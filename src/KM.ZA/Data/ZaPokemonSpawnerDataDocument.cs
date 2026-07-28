// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.ZA.Data;

/// <summary>
/// Preserves a Pokémon spawner FlatBuffer byte-for-byte while allowing edits to
/// materialized scalar fields and verified owned vector lengths whose storage
/// already exists.
/// </summary>
internal sealed class ZaPokemonSpawnerDataDocument
{
    private readonly byte[] originalBytes;
    private readonly Dictionary<int, int> pendingInt32Writes = [];

    private ZaPokemonSpawnerDataDocument(
        byte[] originalBytes,
        IReadOnlyList<ZaPokemonSpawnerDataGroup?> groups)
    {
        this.originalBytes = originalBytes;
        Groups = groups;
    }

    public IReadOnlyList<ZaPokemonSpawnerDataGroup?> Groups { get; }

    public IEnumerable<ZaPokemonSpawnerDataEntry> Entries => Groups
        .OfType<ZaPokemonSpawnerDataGroup>()
        .SelectMany(group => group.Spawners)
        .OfType<ZaPokemonSpawnerDataEntry>();

    public bool HasChanges => pendingInt32Writes.Count > 0;

    public static ZaPokemonSpawnerDataDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var originalBytes = bytes.ToArray();
        var reader = new ZaPokemonSpawnerFlatBufferReader(originalBytes);
        var rootPosition = reader.GetRootTablePosition();
        var groupVector = reader.GetTableVector(rootPosition, fieldIndex: 0);
        if (groupVector is null)
        {
            return new ZaPokemonSpawnerDataDocument(
                originalBytes,
                Array.Empty<ZaPokemonSpawnerDataGroup?>());
        }

        var groups = new List<ZaPokemonSpawnerDataGroup?>(groupVector.Value.Length);
        var sourceIndex = 0;
        for (var groupIndex = 0; groupIndex < groupVector.Value.Length; groupIndex++)
        {
            var groupPosition = reader.GetTableVectorElement(groupVector.Value, groupIndex);
            if (groupPosition is null)
            {
                groups.Add(null);
                continue;
            }

            var spawnerVector = reader.GetTableVector(groupPosition.Value, fieldIndex: 0);
            if (spawnerVector is null)
            {
                groups.Add(new ZaPokemonSpawnerDataGroup(
                    groupIndex,
                    Array.Empty<ZaPokemonSpawnerDataEntry?>()));
                continue;
            }

            var spawners = new List<ZaPokemonSpawnerDataEntry?>(spawnerVector.Value.Length);
            for (var spawnerIndex = 0; spawnerIndex < spawnerVector.Value.Length; spawnerIndex++)
            {
                var spawnerPosition = reader.GetTableVectorElement(spawnerVector.Value, spawnerIndex);
                if (spawnerPosition is null)
                {
                    spawners.Add(null);
                    sourceIndex++;
                    continue;
                }

                spawners.Add(ReadSpawner(
                    reader,
                    spawnerPosition.Value,
                    sourceIndex,
                    groupIndex,
                    spawnerIndex));
                sourceIndex++;
            }

            groups.Add(new ZaPokemonSpawnerDataGroup(groupIndex, spawners));
        }

        var document = new ZaPokemonSpawnerDataDocument(originalBytes, groups);
        document.DisableAliasedEditableFields(reader);
        return document;
    }

    public byte[] Write()
    {
        var output = originalBytes.ToArray();
        foreach (var (position, value) in pendingInt32Writes)
        {
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(position, sizeof(int)), value);
        }

        return output;
    }

    public bool TrySetSlotWeight(
        int groupIndex,
        int spawnerIndex,
        int slotIndex,
        int value)
    {
        return TrySetSlotWeight(groupIndex, spawnerIndex, slotIndex, value, out _);
    }

    public bool TrySetSlotWeight(
        int groupIndex,
        int spawnerIndex,
        int slotIndex,
        int value,
        out string? error)
    {
        if (!TryGetSlot(groupIndex, spawnerIndex, slotIndex, out var slot, out error))
        {
            return false;
        }

        if (slot.WeightPosition is not { } position)
        {
            error = "The spawn weight uses an omitted FlatBuffer default and cannot be patched safely.";
            return false;
        }

        SetInt32(position, value);
        slot.Weight = value;
        error = null;
        return true;
    }

    public bool TrySetSlotMaxCount(
        int groupIndex,
        int spawnerIndex,
        int slotIndex,
        int value)
    {
        return TrySetSlotMaxCount(groupIndex, spawnerIndex, slotIndex, value, out _);
    }

    public bool TrySetSlotMaxCount(
        int groupIndex,
        int spawnerIndex,
        int slotIndex,
        int value,
        out string? error)
    {
        if (!TryGetSlot(groupIndex, spawnerIndex, slotIndex, out var slot, out error))
        {
            return false;
        }

        if (slot.MaxCountPosition is not { } position)
        {
            error = "The slot maximum count uses an omitted FlatBuffer default and cannot be patched safely.";
            return false;
        }

        SetInt32(position, value);
        slot.MaxCount = value;
        error = null;
        return true;
    }

    public bool TrySetAppearanceMinCount(
        int groupIndex,
        int spawnerIndex,
        int appearanceIndex,
        int value)
    {
        return TrySetAppearanceMinCount(groupIndex, spawnerIndex, appearanceIndex, value, out _);
    }

    public bool TrySetAppearanceMinCount(
        int groupIndex,
        int spawnerIndex,
        int appearanceIndex,
        int value,
        out string? error)
    {
        if (!TryGetAppearanceInfo(
                groupIndex,
                spawnerIndex,
                appearanceIndex,
                out var appearanceInfo,
                out error))
        {
            return false;
        }

        if (appearanceInfo.MinCountPosition is not { } position)
        {
            error = "The appearance minimum count uses an omitted FlatBuffer default and cannot be patched safely.";
            return false;
        }

        SetInt32(position, value);
        appearanceInfo.MinCount = value;
        error = null;
        return true;
    }

    public bool TrySetAppearanceMaxCount(
        int groupIndex,
        int spawnerIndex,
        int appearanceIndex,
        int value)
    {
        return TrySetAppearanceMaxCount(groupIndex, spawnerIndex, appearanceIndex, value, out _);
    }

    public bool TrySetAppearanceMaxCount(
        int groupIndex,
        int spawnerIndex,
        int appearanceIndex,
        int value,
        out string? error)
    {
        if (!TryGetAppearanceInfo(
                groupIndex,
                spawnerIndex,
                appearanceIndex,
                out var appearanceInfo,
                out error))
        {
            return false;
        }

        if (appearanceInfo.MaxCountPosition is not { } position)
        {
            error = "The appearance maximum count uses an omitted FlatBuffer default and cannot be patched safely.";
            return false;
        }

        SetInt32(position, value);
        appearanceInfo.MaxCount = value;
        error = null;
        return true;
    }

    public bool TrySetSlotWebSpawnEnabled(
        int groupIndex,
        int spawnerIndex,
        int slotIndex,
        ZaPokemonSpawnerEncountDataInfo baseSlot,
        bool enabled,
        out bool changed,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(baseSlot);

        changed = false;
        if (!TryGetSlot(groupIndex, spawnerIndex, slotIndex, out var slot, out error))
        {
            return false;
        }

        if (baseSlot.PopActionId == 0
            || baseSlot.Tags.Count != 2
            || baseSlot.Tags.Any(string.IsNullOrEmpty))
        {
            error = "The verified base slot does not have a recoverable web-spawn signature.";
            return false;
        }

        if (slot.PopActionId != 0 && slot.PopActionId != baseSlot.PopActionId)
        {
            error = "The current slot uses an unrelated pop action and cannot be converted safely.";
            return false;
        }

        var hasFullTagList = slot.Tags.SequenceEqual(baseSlot.Tags, StringComparer.Ordinal);
        var hasNormalizedTagList = slot.Tags.Count == 1
            && string.Equals(slot.Tags[0], baseSlot.Tags[0], StringComparison.Ordinal);
        if (!hasFullTagList && !hasNormalizedTagList)
        {
            error = "The current slot tags do not match the verified web or normalized spawn shape.";
            return false;
        }

        var targetPopActionId = enabled ? baseSlot.PopActionId : 0;
        var targetTagCount = enabled ? baseSlot.Tags.Count : 1;
        var changesPopAction = slot.PopActionId != targetPopActionId;
        var changesTagCount = slot.Tags.Count != targetTagCount;

        if (changesPopAction && slot.PopActionIdPosition is null)
        {
            error = "The slot pop action uses an omitted or shared FlatBuffer field and cannot be patched safely.";
            return false;
        }

        if (changesTagCount && slot.TagListLengthPosition is null)
        {
            error = "The slot tag list uses shared FlatBuffer storage and cannot be patched safely.";
            return false;
        }

        if (enabled
            && changesTagCount
            && !CanRestoreTrailingTag(slot, baseSlot))
        {
            error = "The preserved web tag cannot be verified against the clean base slot.";
            return false;
        }

        if (changesPopAction)
        {
            SetInt32(slot.PopActionIdPosition!.Value, targetPopActionId);
            slot.PopActionId = targetPopActionId;
        }

        if (changesTagCount)
        {
            SetInt32(slot.TagListLengthPosition!.Value, targetTagCount);
            slot.Tags = enabled
                ? baseSlot.Tags.ToArray()
                : [baseSlot.Tags[0]];
        }

        changed = changesPopAction || changesTagCount;
        error = null;
        return true;
    }

    private static ZaPokemonSpawnerDataEntry ReadSpawner(
        ZaPokemonSpawnerFlatBufferReader reader,
        int spawnerPosition,
        int sourceIndex,
        int groupIndex,
        int spawnerIndex)
    {
        var appearances = ReadAppearances(reader, spawnerPosition);
        var slots = ReadSlots(reader, spawnerPosition);
        return new ZaPokemonSpawnerDataEntry(
            sourceIndex,
            groupIndex,
            spawnerIndex,
            reader.GetString(spawnerPosition, fieldIndex: 0),
            appearances,
            slots);
    }

    private static IReadOnlyList<ZaPokemonSpawnerAppearanceSpawnerObjectInfo?> ReadAppearances(
        ZaPokemonSpawnerFlatBufferReader reader,
        int spawnerPosition)
    {
        var vector = reader.GetTableVector(spawnerPosition, fieldIndex: 1);
        if (vector is null)
        {
            return Array.Empty<ZaPokemonSpawnerAppearanceSpawnerObjectInfo?>();
        }

        var appearances = new List<ZaPokemonSpawnerAppearanceSpawnerObjectInfo?>(vector.Value.Length);
        for (var appearanceIndex = 0; appearanceIndex < vector.Value.Length; appearanceIndex++)
        {
            var appearancePosition = reader.GetTableVectorElement(vector.Value, appearanceIndex);
            if (appearancePosition is null)
            {
                appearances.Add(null);
                continue;
            }

            _ = reader.GetStringVector(appearancePosition.Value, fieldIndex: 5);
            var appearanceInfoPosition = reader.GetTable(
                appearancePosition.Value,
                fieldIndex: 8);
            ZaPokemonSpawnerAppearanceInfo? appearanceInfo = null;
            if (appearanceInfoPosition is not null)
            {
                var minCount = reader.GetInt32(
                    appearanceInfoPosition.Value,
                    fieldIndex: 0);
                var maxCount = reader.GetInt32(
                    appearanceInfoPosition.Value,
                    fieldIndex: 1);
                appearanceInfo = new ZaPokemonSpawnerAppearanceInfo(
                    minCount.Value,
                    maxCount.Value,
                    minCount.Position,
                    maxCount.Position);
            }

            appearances.Add(new ZaPokemonSpawnerAppearanceSpawnerObjectInfo(
                appearanceIndex,
                reader.GetString(appearancePosition.Value, fieldIndex: 0),
                appearanceInfo));
        }

        return appearances;
    }

    private static IReadOnlyList<ZaPokemonSpawnerEncountDataInfo?> ReadSlots(
        ZaPokemonSpawnerFlatBufferReader reader,
        int spawnerPosition)
    {
        var vector = reader.GetTableVector(spawnerPosition, fieldIndex: 5);
        if (vector is null)
        {
            return Array.Empty<ZaPokemonSpawnerEncountDataInfo?>();
        }

        var slots = new List<ZaPokemonSpawnerEncountDataInfo?>(vector.Value.Length);
        for (var slotIndex = 0; slotIndex < vector.Value.Length; slotIndex++)
        {
            var slotPosition = reader.GetTableVectorElement(vector.Value, slotIndex);
            if (slotPosition is null)
            {
                slots.Add(null);
                continue;
            }

            var weight = reader.GetInt32(slotPosition.Value, fieldIndex: 1);
            var maxCount = reader.GetInt32(slotPosition.Value, fieldIndex: 2);
            var tagVector = reader.GetStringVector(slotPosition.Value, fieldIndex: 4);
            var tags = tagVector is null
                ? Array.Empty<string?>()
                : Enumerable
                    .Range(0, tagVector.Value.Length)
                    .Select(index => reader.GetStringVectorElement(tagVector.Value, index))
                    .ToArray();
            var aiInfoPosition = reader.GetTable(slotPosition.Value, fieldIndex: 8);
            var popActionId = aiInfoPosition is null
                ? new ZaPokemonSpawnerInt32Field(0, null)
                : reader.GetInt32(aiInfoPosition.Value, fieldIndex: 5);
            slots.Add(new ZaPokemonSpawnerEncountDataInfo(
                slotIndex,
                reader.GetString(slotPosition.Value, fieldIndex: 0),
                weight.Value,
                maxCount.Value,
                weight.Position,
                maxCount.Position,
                tags,
                tagVector?.LengthPosition,
                tagVector?.DataPosition,
                popActionId.Value,
                popActionId.Position));
        }

        return slots;
    }

    private bool TryGetSlot(
        int groupIndex,
        int spawnerIndex,
        int slotIndex,
        out ZaPokemonSpawnerEncountDataInfo slot,
        out string? error)
    {
        slot = null!;
        if (!TryGetSpawner(groupIndex, spawnerIndex, out var spawner, out error))
        {
            return false;
        }

        if ((uint)slotIndex >= (uint)spawner.EncountDataInfoList.Count)
        {
            error = $"Encounter slot index {slotIndex} is outside spawner {spawnerIndex}.";
            return false;
        }

        var candidate = spawner.EncountDataInfoList[slotIndex];
        if (candidate is null)
        {
            error = $"Encounter slot {slotIndex} is absent from spawner {spawnerIndex}.";
            return false;
        }

        slot = candidate;
        error = null;
        return true;
    }

    private bool TryGetAppearanceInfo(
        int groupIndex,
        int spawnerIndex,
        int appearanceIndex,
        out ZaPokemonSpawnerAppearanceInfo appearanceInfo,
        out string? error)
    {
        appearanceInfo = null!;
        if (!TryGetSpawner(groupIndex, spawnerIndex, out var spawner, out error))
        {
            return false;
        }

        if ((uint)appearanceIndex >= (uint)spawner.AppearanceSpawnerObjectInfoList.Count)
        {
            error = $"Appearance index {appearanceIndex} is outside spawner {spawnerIndex}.";
            return false;
        }

        var appearance = spawner.AppearanceSpawnerObjectInfoList[appearanceIndex];
        if (appearance is null)
        {
            error = $"Appearance {appearanceIndex} is absent from spawner {spawnerIndex}.";
            return false;
        }

        if (appearance.AppearanceInfo is not { } candidate)
        {
            error = $"Appearance {appearanceIndex} has no appearance-count table.";
            return false;
        }

        appearanceInfo = candidate;
        error = null;
        return true;
    }

    private bool TryGetSpawner(
        int groupIndex,
        int spawnerIndex,
        out ZaPokemonSpawnerDataEntry spawner,
        out string? error)
    {
        spawner = null!;
        if ((uint)groupIndex >= (uint)Groups.Count)
        {
            error = $"Spawner group index {groupIndex} is outside the document.";
            return false;
        }

        var group = Groups[groupIndex];
        if (group is null)
        {
            error = $"Spawner group {groupIndex} is absent from the document.";
            return false;
        }

        if ((uint)spawnerIndex >= (uint)group.Spawners.Count)
        {
            error = $"Spawner index {spawnerIndex} is outside group {groupIndex}.";
            return false;
        }

        var candidate = group.Spawners[spawnerIndex];
        if (candidate is null)
        {
            error = $"Spawner {spawnerIndex} is absent from group {groupIndex}.";
            return false;
        }

        spawner = candidate;
        error = null;
        return true;
    }

    private void SetInt32(int position, int value)
    {
        var originalValue = BinaryPrimitives.ReadInt32LittleEndian(
            originalBytes.AsSpan(position, sizeof(int)));
        if (value == originalValue)
        {
            pendingInt32Writes.Remove(position);
        }
        else
        {
            pendingInt32Writes[position] = value;
        }
    }

    private bool CanRestoreTrailingTag(
        ZaPokemonSpawnerEncountDataInfo slot,
        ZaPokemonSpawnerEncountDataInfo baseSlot)
    {
        if (slot.TagListLengthPosition is null
            || slot.TagListDataPosition is null
            || baseSlot.TagListLengthPosition is null
            || baseSlot.TagListDataPosition is null
            || slot.TagListLengthPosition != baseSlot.TagListLengthPosition
            || slot.TagListDataPosition != baseSlot.TagListDataPosition)
        {
            return false;
        }

        try
        {
            var reader = new ZaPokemonSpawnerFlatBufferReader(originalBytes);
            return string.Equals(
                reader.GetStringVectorElement(
                    new ZaPokemonSpawnerStringVector(
                        slot.TagListLengthPosition.Value,
                        slot.TagListDataPosition.Value,
                        baseSlot.Tags.Count),
                    index: 1),
                baseSlot.Tags[1],
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return false;
        }
    }

    private void DisableAliasedEditableFields(ZaPokemonSpawnerFlatBufferReader reader)
    {
        var owners = new List<(
            int Position,
            string Scope,
            bool OwnsLengthPrefix,
            Action Disable)>();

        foreach (var group in Groups.OfType<ZaPokemonSpawnerDataGroup>())
        {
            foreach (var spawner in group.Spawners.OfType<ZaPokemonSpawnerDataEntry>())
            {
                foreach (var slot in spawner.EncountDataInfoList.OfType<ZaPokemonSpawnerEncountDataInfo>())
                {
                    if (slot.WeightPosition is { } weightPosition)
                    {
                        owners.Add((
                            weightPosition,
                            $"slot:{group.GroupIndex}:{spawner.SpawnerIndex}:{slot.SlotIndex}:weight",
                            false,
                            () => slot.WeightPosition = null));
                    }

                    if (slot.MaxCountPosition is { } maxCountPosition)
                    {
                        owners.Add((
                            maxCountPosition,
                            $"slot:{group.GroupIndex}:{spawner.SpawnerIndex}:{slot.SlotIndex}:max",
                            false,
                            () => slot.MaxCountPosition = null));
                    }

                    if (slot.PopActionIdPosition is { } popActionIdPosition)
                    {
                        owners.Add((
                            popActionIdPosition,
                            $"slot:{group.GroupIndex}:{spawner.SpawnerIndex}:{slot.SlotIndex}:pop-action",
                            false,
                            () => slot.PopActionIdPosition = null));
                    }

                    if (slot.TagListLengthPosition is { } tagListLengthPosition)
                    {
                        owners.Add((
                            tagListLengthPosition,
                            $"slot:{group.GroupIndex}:{spawner.SpawnerIndex}:{slot.SlotIndex}:tag-list",
                            true,
                            () => slot.TagListLengthPosition = null));
                    }
                }

                foreach (var appearance in spawner.AppearanceSpawnerObjectInfoList
                             .OfType<ZaPokemonSpawnerAppearanceSpawnerObjectInfo>())
                {
                    if (appearance.AppearanceInfo?.MinCountPosition is { } minCountPosition)
                    {
                        var appearanceInfo = appearance.AppearanceInfo;
                        owners.Add((
                            minCountPosition,
                            $"appearance:{group.GroupIndex}:{spawner.SpawnerIndex}:min",
                            false,
                            () => appearanceInfo.MinCountPosition = null));
                    }

                    if (appearance.AppearanceInfo?.MaxCountPosition is { } maxCountPosition)
                    {
                        var appearanceInfo = appearance.AppearanceInfo;
                        owners.Add((
                            maxCountPosition,
                            $"appearance:{group.GroupIndex}:{spawner.SpawnerIndex}:max",
                            false,
                            () => appearanceInfo.MaxCountPosition = null));
                    }
                }
            }
        }

        foreach (var positionOwners in owners.GroupBy(owner => owner.Position))
        {
            var aliasesEditableField = positionOwners
                .Select(owner => owner.Scope)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any();
            foreach (var owner in positionOwners)
            {
                var lengthPrefixReferenceCount =
                    reader.GetLengthPrefixReferenceCount(owner.Position);
                if (aliasesEditableField
                    || owner.OwnsLengthPrefix && lengthPrefixReferenceCount != 1
                    || !owner.OwnsLengthPrefix && lengthPrefixReferenceCount != 0)
                {
                    owner.Disable();
                }
            }
        }
    }
}

internal sealed class ZaPokemonSpawnerDataGroup
{
    public ZaPokemonSpawnerDataGroup(
        int groupIndex,
        IReadOnlyList<ZaPokemonSpawnerDataEntry?> spawners)
    {
        GroupIndex = groupIndex;
        Spawners = spawners;
    }

    public int GroupIndex { get; }

    public IReadOnlyList<ZaPokemonSpawnerDataEntry?> Spawners { get; }

    public IReadOnlyList<ZaPokemonSpawnerDataEntry?> Rows => Spawners;
}

internal sealed class ZaPokemonSpawnerDataEntry
{
    public ZaPokemonSpawnerDataEntry(
        int sourceIndex,
        int groupIndex,
        int spawnerIndex,
        string? id,
        IReadOnlyList<ZaPokemonSpawnerAppearanceSpawnerObjectInfo?> appearanceSpawnerObjectInfoList,
        IReadOnlyList<ZaPokemonSpawnerEncountDataInfo?> encountDataInfoList)
    {
        SourceIndex = sourceIndex;
        GroupIndex = groupIndex;
        SpawnerIndex = spawnerIndex;
        Id = id;
        AppearanceSpawnerObjectInfoList = appearanceSpawnerObjectInfoList;
        EncountDataInfoList = encountDataInfoList;
    }

    public int SourceIndex { get; }

    public int GroupIndex { get; }

    public int SpawnerIndex { get; }

    public string? Id { get; }

    public IReadOnlyList<ZaPokemonSpawnerAppearanceSpawnerObjectInfo?> AppearanceSpawnerObjectInfoList { get; }

    public IReadOnlyList<ZaPokemonSpawnerEncountDataInfo?> EncountDataInfoList { get; }

    public bool CanEditAppearanceCounts
    {
        get
        {
            if (AppearanceSpawnerObjectInfoList.Count == 0
                || AppearanceSpawnerObjectInfoList[0]?.AppearanceInfo is not { CanEditCounts: true } first)
            {
                return false;
            }

            return AppearanceSpawnerObjectInfoList.All(appearance =>
                appearance?.AppearanceInfo is { CanEditCounts: true } info
                && info.MinCount == first.MinCount
                && info.MaxCount == first.MaxCount);
        }
    }
}

internal sealed class ZaPokemonSpawnerAppearanceSpawnerObjectInfo
{
    public ZaPokemonSpawnerAppearanceSpawnerObjectInfo(
        int appearanceIndex,
        string? objectName,
        ZaPokemonSpawnerAppearanceInfo? appearanceInfo)
    {
        AppearanceIndex = appearanceIndex;
        ObjectName = objectName;
        AppearanceInfo = appearanceInfo;
    }

    public int AppearanceIndex { get; }

    public string? ObjectName { get; }

    public ZaPokemonSpawnerAppearanceInfo? AppearanceInfo { get; }
}

internal sealed class ZaPokemonSpawnerAppearanceInfo
{
    public ZaPokemonSpawnerAppearanceInfo(
        int minCount,
        int maxCount,
        int? minCountPosition,
        int? maxCountPosition)
    {
        MinCount = minCount;
        MaxCount = maxCount;
        MinCountPosition = minCountPosition;
        MaxCountPosition = maxCountPosition;
    }

    public int MinCount { get; internal set; }

    public int MaxCount { get; internal set; }

    public bool CanEditMinCount => MinCountPosition is not null;

    public bool CanEditMaxCount => MaxCountPosition is not null;

    public bool CanEditCounts => CanEditMinCount && CanEditMaxCount;

    internal int? MinCountPosition { get; set; }

    internal int? MaxCountPosition { get; set; }
}

internal sealed class ZaPokemonSpawnerEncountDataInfo
{
    public ZaPokemonSpawnerEncountDataInfo(
        int slotIndex,
        string? encountDataId,
        int weight,
        int maxCount,
        int? weightPosition,
        int? maxCountPosition,
        IReadOnlyList<string?> tags,
        int? tagListLengthPosition,
        int? tagListDataPosition,
        int popActionId,
        int? popActionIdPosition)
    {
        SlotIndex = slotIndex;
        EncountDataId = encountDataId;
        Weight = weight;
        MaxCount = maxCount;
        WeightPosition = weightPosition;
        MaxCountPosition = maxCountPosition;
        Tags = tags;
        TagListLengthPosition = tagListLengthPosition;
        TagListDataPosition = tagListDataPosition;
        PopActionId = popActionId;
        PopActionIdPosition = popActionIdPosition;
    }

    public int SlotIndex { get; }

    public string? EncountDataId { get; }

    public int Weight { get; internal set; }

    public int MaxCount { get; internal set; }

    public IReadOnlyList<string?> Tags { get; internal set; }

    public int PopActionId { get; internal set; }

    public bool CanEditWeight => WeightPosition is not null;

    public bool CanEditMaxCount => MaxCountPosition is not null;

    internal int? WeightPosition { get; set; }

    internal int? MaxCountPosition { get; set; }

    internal int? TagListLengthPosition { get; set; }

    internal int? TagListDataPosition { get; }

    internal int? PopActionIdPosition { get; set; }
}

internal readonly record struct ZaPokemonSpawnerInt32Field(int Value, int? Position);

internal readonly record struct ZaPokemonSpawnerTableVector(int DataPosition, int Length);

internal readonly record struct ZaPokemonSpawnerStringVector(
    int LengthPosition,
    int DataPosition,
    int Length);

internal sealed class ZaPokemonSpawnerFlatBufferReader
{
    private const int UOffsetSize = sizeof(uint);
    private const int VTableHeaderSize = sizeof(ushort) * 2;
    private const int VTableFieldSize = sizeof(ushort);

    private readonly byte[] bytes;
    private readonly Dictionary<int, int> lengthPrefixReferenceCounts = [];

    public ZaPokemonSpawnerFlatBufferReader(byte[] bytes)
    {
        this.bytes = bytes;
    }

    public int GetRootTablePosition()
    {
        EnsureRange(0, UOffsetSize, "root offset");
        var rootOffset = ReadUInt32(0);
        if (rootOffset == 0)
        {
            throw new InvalidDataException("The Pokémon spawner data has no root table.");
        }

        var rootPosition = AddOffset(0, rootOffset, "root table");
        ValidateTable(rootPosition);
        return rootPosition;
    }

    public ZaPokemonSpawnerTableVector? GetTableVector(int tablePosition, int fieldIndex)
    {
        var fieldPosition = GetFieldPosition(tablePosition, fieldIndex);
        if (fieldPosition is null)
        {
            return null;
        }

        var vectorPosition = FollowOffset(fieldPosition.Value, "table vector");
        RecordLengthPrefixReference(vectorPosition);
        EnsureRange(vectorPosition, sizeof(uint), "table vector length");
        var lengthValue = ReadUInt32(vectorPosition);
        if (lengthValue > int.MaxValue)
        {
            throw new InvalidDataException("A Pokémon spawner vector is too large to load.");
        }

        var length = (int)lengthValue;
        var dataPosition = checked(vectorPosition + sizeof(uint));
        EnsureElementRange(dataPosition, length, UOffsetSize, "table vector data");
        return new ZaPokemonSpawnerTableVector(dataPosition, length);
    }

    public int? GetTableVectorElement(ZaPokemonSpawnerTableVector vector, int index)
    {
        if ((uint)index >= (uint)vector.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var elementPosition = checked(vector.DataPosition + (index * UOffsetSize));
        var elementOffset = ReadUInt32(elementPosition);
        if (elementOffset == 0)
        {
            return null;
        }

        var tablePosition = AddOffset(elementPosition, elementOffset, "table vector element");
        ValidateTable(tablePosition);
        return tablePosition;
    }

    public ZaPokemonSpawnerStringVector? GetStringVector(int tablePosition, int fieldIndex)
    {
        var fieldPosition = GetFieldPosition(tablePosition, fieldIndex);
        if (fieldPosition is null)
        {
            return null;
        }

        var vectorPosition = FollowOffset(fieldPosition.Value, "string vector");
        RecordLengthPrefixReference(vectorPosition);
        if ((vectorPosition & (sizeof(uint) - 1)) != 0)
        {
            throw new InvalidDataException("A Pokémon spawner string vector is unaligned.");
        }

        EnsureRange(vectorPosition, sizeof(uint), "string vector length");
        var lengthValue = ReadUInt32(vectorPosition);
        if (lengthValue > int.MaxValue)
        {
            throw new InvalidDataException("A Pokémon spawner string vector is too large to load.");
        }

        var length = (int)lengthValue;
        var dataPosition = checked(vectorPosition + sizeof(uint));
        EnsureElementRange(dataPosition, length, UOffsetSize, "string vector data");
        return new ZaPokemonSpawnerStringVector(vectorPosition, dataPosition, length);
    }

    public string? GetStringVectorElement(ZaPokemonSpawnerStringVector vector, int index)
    {
        if ((uint)index >= (uint)vector.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var elementPosition = checked(vector.DataPosition + (index * UOffsetSize));
        EnsureRange(elementPosition, UOffsetSize, "string vector element");
        var elementOffset = ReadUInt32(elementPosition);
        if (elementOffset == 0)
        {
            return null;
        }

        return ReadString(AddOffset(elementPosition, elementOffset, "string vector element"));
    }

    public int? GetTable(int tablePosition, int fieldIndex)
    {
        var fieldPosition = GetFieldPosition(tablePosition, fieldIndex);
        if (fieldPosition is null)
        {
            return null;
        }

        var nestedTablePosition = FollowOffset(fieldPosition.Value, "nested table");
        ValidateTable(nestedTablePosition);
        return nestedTablePosition;
    }

    public string? GetString(int tablePosition, int fieldIndex)
    {
        var fieldPosition = GetFieldPosition(tablePosition, fieldIndex);
        if (fieldPosition is null)
        {
            return null;
        }

        return ReadString(FollowOffset(fieldPosition.Value, "string"));
    }

    private string ReadString(int stringPosition)
    {
        RecordLengthPrefixReference(stringPosition);
        EnsureRange(stringPosition, sizeof(uint), "string length");
        var lengthValue = ReadUInt32(stringPosition);
        if (lengthValue > int.MaxValue)
        {
            throw new InvalidDataException("A Pokémon spawner string is too large to load.");
        }

        var length = (int)lengthValue;
        var dataPosition = checked(stringPosition + sizeof(uint));
        EnsureRange(dataPosition, length, "string data");
        return Encoding.UTF8.GetString(bytes, dataPosition, length);
    }

    public int GetLengthPrefixReferenceCount(int position)
    {
        return lengthPrefixReferenceCounts.GetValueOrDefault(position);
    }

    private void RecordLengthPrefixReference(int position)
    {
        lengthPrefixReferenceCounts[position] =
            lengthPrefixReferenceCounts.GetValueOrDefault(position) + 1;
    }

    public ZaPokemonSpawnerInt32Field GetInt32(int tablePosition, int fieldIndex)
    {
        var fieldPosition = GetFieldPosition(tablePosition, fieldIndex);
        if (fieldPosition is null)
        {
            return new ZaPokemonSpawnerInt32Field(0, null);
        }

        if (fieldPosition.Value < tablePosition + sizeof(int)
            || (fieldPosition.Value & (sizeof(int) - 1)) != 0)
        {
            throw new InvalidDataException(
                "A Pokémon spawner 32-bit field is unaligned or overlaps its table header.");
        }

        return new ZaPokemonSpawnerInt32Field(
            ReadInt32(fieldPosition.Value),
            HasExclusiveInlineRange(tablePosition, fieldIndex, sizeof(int))
                ? fieldPosition.Value
                : null);
    }

    private bool HasExclusiveInlineRange(int tablePosition, int fieldIndex, int fieldSize)
    {
        var (vTablePosition, vTableLength, _) = GetTableLayout(tablePosition);
        var fieldEntryCount = (vTableLength - VTableHeaderSize) / VTableFieldSize;
        var fieldInlineOffset = ReadUInt16(
            checked(vTablePosition + VTableHeaderSize + (fieldIndex * VTableFieldSize)));
        var fieldEnd = checked(fieldInlineOffset + fieldSize);

        for (var otherFieldIndex = 0; otherFieldIndex < fieldEntryCount; otherFieldIndex++)
        {
            if (otherFieldIndex == fieldIndex)
            {
                continue;
            }

            var otherInlineOffset = ReadUInt16(
                checked(vTablePosition + VTableHeaderSize + (otherFieldIndex * VTableFieldSize)));
            if (otherInlineOffset != 0
                && otherInlineOffset < fieldEnd
                && fieldInlineOffset < otherInlineOffset + sizeof(int))
            {
                return false;
            }
        }

        return true;
    }

    private int? GetFieldPosition(int tablePosition, int fieldIndex)
    {
        if (fieldIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldIndex));
        }

        var (vTablePosition, vTableLength, objectLength) = GetTableLayout(tablePosition);
        var fieldEntryOffset = checked(VTableHeaderSize + (fieldIndex * VTableFieldSize));
        if (fieldEntryOffset + VTableFieldSize > vTableLength)
        {
            return null;
        }

        var inlineOffset = ReadUInt16(checked(vTablePosition + fieldEntryOffset));
        if (inlineOffset == 0)
        {
            return null;
        }

        if (inlineOffset + sizeof(int) > objectLength)
        {
            throw new InvalidDataException("A Pokémon spawner field lies outside its table.");
        }

        var fieldPosition = checked(tablePosition + inlineOffset);
        EnsureRange(fieldPosition, sizeof(int), "table field");
        return fieldPosition;
    }

    private void ValidateTable(int tablePosition)
    {
        _ = GetTableLayout(tablePosition);
    }

    private (int VTablePosition, int VTableLength, int ObjectLength) GetTableLayout(int tablePosition)
    {
        EnsureRange(tablePosition, sizeof(int), "table header");
        var vTableDistance = ReadInt32(tablePosition);
        if (vTableDistance == 0)
        {
            throw new InvalidDataException("A Pokémon spawner table has an invalid vtable offset.");
        }

        var vTablePositionValue = (long)tablePosition - vTableDistance;
        if (vTablePositionValue is < 0 or > int.MaxValue)
        {
            throw new InvalidDataException("A Pokémon spawner table has an invalid vtable offset.");
        }

        var vTablePosition = (int)vTablePositionValue;
        EnsureRange(vTablePosition, VTableHeaderSize, "vtable header");
        var vTableLength = ReadUInt16(vTablePosition);
        var objectLength = ReadUInt16(checked(vTablePosition + sizeof(ushort)));
        if (vTableLength < VTableHeaderSize || (vTableLength & 1) != 0)
        {
            throw new InvalidDataException("A Pokémon spawner table has an invalid vtable length.");
        }

        if (objectLength < sizeof(int))
        {
            throw new InvalidDataException("A Pokémon spawner table has an invalid object length.");
        }

        EnsureRange(vTablePosition, vTableLength, "vtable");
        EnsureRange(tablePosition, objectLength, "table");
        return (vTablePosition, vTableLength, objectLength);
    }

    private int FollowOffset(int offsetPosition, string description)
    {
        EnsureRange(offsetPosition, UOffsetSize, description);
        var relativeOffset = ReadUInt32(offsetPosition);
        if (relativeOffset == 0)
        {
            throw new InvalidDataException($"A Pokémon spawner {description} has a null offset.");
        }

        return AddOffset(offsetPosition, relativeOffset, description);
    }

    private static int AddOffset(int position, uint relativeOffset, string description)
    {
        var target = (long)position + relativeOffset;
        if (target > int.MaxValue)
        {
            throw new InvalidDataException($"A Pokémon spawner {description} offset is too large.");
        }

        return (int)target;
    }

    private int ReadInt32(int position)
    {
        EnsureRange(position, sizeof(int), "32-bit value");
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, sizeof(int)));
    }

    private uint ReadUInt32(int position)
    {
        EnsureRange(position, sizeof(uint), "32-bit value");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, sizeof(uint)));
    }

    private ushort ReadUInt16(int position)
    {
        EnsureRange(position, sizeof(ushort), "16-bit value");
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position, sizeof(ushort)));
    }

    private void EnsureElementRange(
        int position,
        int elementCount,
        int elementSize,
        string description)
    {
        var length = (long)elementCount * elementSize;
        if (length > int.MaxValue)
        {
            throw new InvalidDataException($"The Pokémon spawner {description} is too large.");
        }

        EnsureRange(position, (int)length, description);
    }

    private void EnsureRange(int position, int length, string description)
    {
        if (position < 0 || length < 0 || position > bytes.Length - length)
        {
            throw new InvalidDataException($"The Pokémon spawner {description} lies outside the file.");
        }
    }
}
