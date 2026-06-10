// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;

namespace KM.SwSh.Tests.Placement;

internal static class SwShPlacementTestFixtures
{
    public const string AreaName = "a_test";
    public const string AreaMember = "a_test.bin";
    public const ulong ZoneHash = 0x1122334455667788;
    public const ulong ObjectHash = 0x8877665544332211;
    public const ulong PotionHash = 0xAABBCCDD00112233;
    public const ulong GreatBallHash = 0xAABBCCDD00112244;

    public static void WriteBasePlacement(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            CreatePlacementPack());
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item_hash_to_index.dat",
            CreateItemHashTable());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames(
                "",
                "Potion",
                "Great Ball"));
    }

    public static byte[] CreatePlacementPack(
        ulong? fieldItemHash = null,
        IReadOnlyList<uint>? fieldItemRawItems = null)
    {
        return SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile(
                "AreaNameHashTable.tbl",
                new SwShAhtbFile(
                [
                    new SwShAhtbEntry(SwShGfPackFile.HashFnv1a64(AreaName), AreaName),
                ]).Write()),
            new SwShGfPackNamedFile(
                "ZoneNameHashTable.tbl",
                new SwShAhtbFile(
                [
                    new SwShAhtbEntry(ZoneHash, "Route 1"),
                ]).Write()),
            new SwShGfPackNamedFile(
                "ObjectNameHashTable.tbl",
                new SwShAhtbFile(
                [
                    new SwShAhtbEntry(ObjectHash, "objects/hidden_item.gfbmdl"),
                ]).Write()),
            new SwShGfPackNamedFile(AreaMember, CreatePlacementArchive(fieldItemHash, fieldItemRawItems).Write()),
        ]).Write();
    }

    public static byte[] CreateItemHashTable()
    {
        return new SwShItemHashTable(
        [
            new SwShItemHashEntry(1, PotionHash),
            new SwShItemHashEntry(2, GreatBallHash),
        ]).Write();
    }

    public static SwShPlacementZoneArchive CreatePlacementArchive(
        ulong? fieldItemHash = null,
        IReadOnlyList<uint>? fieldItemRawItems = null)
    {
        return new SwShPlacementZoneArchive(
        [
            new SwShPlacementZone(
                ZoneIndex: 0,
                ZoneId: ZoneHash,
                ObjectHash: ObjectHash,
                Transform: new SwShPlacementTransform(0, 0, 0, 0),
                FieldItems:
                [
                    new SwShPlacementFieldItem(
                        ObjectIndex: 0,
                        Model: "objects/visible_potion.gfbmdl",
                        Transform: new SwShPlacementTransform(10.5f, 0, -4.25f, 90),
                        ItemHashes: [fieldItemHash ?? PotionHash],
                        ItemHashOffsets: [],
                        ItemIds: fieldItemRawItems ?? [],
                        ItemIdOffsets: [],
                        Quantity: 1,
                        QuantityOffset: 0,
                        TransformOffsets: new PlacementTransformOffsets(0, 0, 0, 0)),
                ],
                HiddenItems:
                [
                    new SwShPlacementHiddenItem(
                        ObjectIndex: 0,
                        Transform: new SwShPlacementTransform(12, 0, -5, 180),
                        Chances:
                        [
                            new SwShPlacementHiddenItemChance(
                                ChanceIndex: 0,
                                ItemHash: PotionHash,
                                ItemId: 1,
                                Chance: 50,
                                Quantity: 2,
                                ItemHashOffset: 0,
                                ChanceOffset: 0,
                                QuantityOffset: 0),
                        ],
                        TransformOffsets: new PlacementTransformOffsets(0, 0, 0, 0)),
                ]),
        ],
        Hash: 0x0123456789ABCDEF,
        Description: "sanitized placement test archive",
        SourceData: []);
    }
}
