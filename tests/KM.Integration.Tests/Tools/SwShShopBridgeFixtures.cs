// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShShopBridgeFixtures
{
    public const ulong SingleShopHash = 0x1F3FF031A3A24490;
    public const ulong MultiShopHash = 0x66CA73B2966BB871;

    public static void WriteBaseShops(TemporaryBridgeProject temp)
    {
        SwShItemBridgeFixtures.WriteBaseItems(temp);
        temp.WriteBaseRomFsFile(
            "bin/app/shop/shop_data.bin",
            CreateShopData([1, 2], [[1]]));
    }

    public static byte[] CreateShopData(int[] singleShopItems, int[][] multiShopInventories)
    {
        return new SwShShopDataFile(
            singleShopItems.Length == 0
                ? Array.Empty<SwShSingleShopRecord>()
                : [new SwShSingleShopRecord(SingleShopHash, new SwShShopInventory(singleShopItems))],
            multiShopInventories.Length == 0
                ? Array.Empty<SwShMultiShopRecord>()
                : [new SwShMultiShopRecord(
                    MultiShopHash,
                    multiShopInventories
                        .Select(items => new SwShShopInventory(items))
                        .ToArray())])
            .Write();
    }
}
