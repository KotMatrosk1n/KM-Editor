// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;

namespace KM.ZA.Items;

internal enum ZaItemMintNatureRecoveryStatus
{
    None,
    Detected,
    Ambiguous,
}

internal sealed record ZaItemMintNatureRecovery(
    ZaItemMintNatureRecoveryStatus Status,
    IReadOnlySet<int> ItemIds)
{
    public static ZaItemMintNatureRecovery None { get; } =
        new(ZaItemMintNatureRecoveryStatus.None, new HashSet<int>());
}

internal static class ZaItemMintNatureRecoveryDetector
{
    // A real Z-A item table has more than five hundred no-mint sentinels. Requiring
    // a sizeable, complete rewrite keeps this migration specific to the legacy KM
    // whole-table normalization bug instead of guessing about ordinary item mods.
    private const int MinimumLegacySentinelRows = 32;
    private const int MinimumZeroPercent = 90;

    public static ZaItemMintNatureRecovery Analyze(byte[] activeBytes, byte[] baseBytes)
    {
        ArgumentNullException.ThrowIfNull(activeBytes);
        ArgumentNullException.ThrowIfNull(baseBytes);

        var activeRows = ReadMintNatures(activeBytes);
        var baseRows = ReadMintNatures(baseBytes);
        if (baseRows.Count == 0 || baseRows.Keys.Any(itemId => !activeRows.ContainsKey(itemId)))
        {
            return ZaItemMintNatureRecovery.None;
        }

        var eligibleItemIds = baseRows
            .Where(entry => entry.Value == -1)
            .Select(entry => entry.Key)
            .ToArray();
        if (eligibleItemIds.Length < MinimumLegacySentinelRows)
        {
            return ZaItemMintNatureRecovery.None;
        }

        var zeroItemIds = eligibleItemIds
            .Where(itemId => activeRows[itemId] == 0)
            .ToHashSet();
        if (zeroItemIds.Count < MinimumLegacySentinelRows)
        {
            return ZaItemMintNatureRecovery.None;
        }

        var stillHasNoMintSentinel = eligibleItemIds.Any(itemId => activeRows[itemId] < 0);
        var zeroPercent = zeroItemIds.Count * 100 / eligibleItemIds.Length;
        if (stillHasNoMintSentinel || zeroPercent < MinimumZeroPercent)
        {
            return new ZaItemMintNatureRecovery(
                ZaItemMintNatureRecoveryStatus.Ambiguous,
                new HashSet<int>());
        }

        return new ZaItemMintNatureRecovery(
            ZaItemMintNatureRecoveryStatus.Detected,
            zeroItemIds);
    }

    private static Dictionary<int, int> ReadMintNatures(byte[] bytes)
    {
        var table = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(bytes));
        var rows = new Dictionary<int, int>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            if (table.Values(index) is not { } item)
            {
                continue;
            }

            if (!rows.TryAdd(item.Id, item.MintNature))
            {
                throw new InvalidDataException($"Z-A item {item.Id} is duplicated in the item table.");
            }
        }

        return rows;
    }
}
