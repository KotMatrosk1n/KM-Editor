// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.BattleMoves;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;

namespace KM.ZA.Items;

internal static class ZaTechnicalMachineIconResolver
{
    private const int TypeCount = 18;

    public static bool TryResolve(
        byte[] baseItemBytes,
        byte[] moveBytes,
        ushort moveId,
        out string iconName)
    {
        iconName = string.Empty;
        var moveTypes = ReadMoveTypes(moveBytes);
        if (!moveTypes.TryGetValue(moveId, out var targetType))
        {
            return false;
        }

        var baseTable = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(baseItemBytes));
        var assignments = new List<(byte Type, string Icon)>();
        for (var index = 0; index < baseTable.ValuesLength; index++)
        {
            if (baseTable.Values(index) is not { } item
                || !ZaTechnicalMachineCatalog.IsTechnicalMachine(item)
                || string.IsNullOrWhiteSpace(item.IconName)
                || !moveTypes.TryGetValue(item.MachineWaza, out var type))
            {
                continue;
            }

            assignments.Add((type, item.IconName!));
        }

        var typeGroups = assignments.GroupBy(assignment => assignment.Type).ToArray();
        if (typeGroups.Length != TypeCount
            || !typeGroups.Select(group => (int)group.Key).Order().SequenceEqual(Enumerable.Range(0, TypeCount))
            || typeGroups.Any(group => group.Select(value => value.Icon).Distinct(StringComparer.Ordinal).Count() != 1))
        {
            return false;
        }

        iconName = typeGroups.Single(group => group.Key == targetType).First().Icon;
        return !string.IsNullOrWhiteSpace(iconName);
    }

    private static IReadOnlyDictionary<ushort, byte> ReadMoveTypes(byte[] bytes)
    {
        var table = ZaBattleMoveParameterArrayT.DeserializeFromBinary(bytes);
        return table.Values
            .Where(group => group?.Root is not null)
            .SelectMany(group => group.Root)
            .GroupBy(move => move.MoveId)
            .Select(group => group.FirstOrDefault(move => move.VariantType == 0) ?? group.First())
            .Where(move => move.MoveId <= ushort.MaxValue)
            .ToDictionary(move => checked((ushort)move.MoveId), move => move.Type);
    }
}
