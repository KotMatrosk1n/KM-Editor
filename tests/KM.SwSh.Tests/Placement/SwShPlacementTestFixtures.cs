// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;

namespace KM.SwSh.Tests.Placement;

internal static class SwShPlacementTestFixtures
{
    public const string AreaName = "a_test";
    public const string AreaMember = "a_test.bin";
    public const string StaticAreaName = "a_static";
    public const string StaticAreaMember = "a_static.bin";
    public const ulong ZoneHash = 0x1122334455667788;
    public const ulong StaticZoneHash = 0x2233445566778899;
    public const ulong ObjectHash = 0x8877665544332211;
    public const ulong PotionHash = 0xAABBCCDD00112233;
    public const ulong GreatBallHash = 0xAABBCCDD00112244;
    public const ulong StaticEncounterHash = 0x0102030405060708;
    public const ulong SecondStaticEncounterHash = 0x1112131415161718;
    public const ulong Wr02HoeruoObjectHash = 0x12E3C0CA0F529035;

    public static void WriteBasePlacement(
        TemporarySwShProject temp,
        bool includeStaticObject = false,
        bool includeRuntimeOwnedWailord = false,
        IReadOnlyList<ulong>? staticSpawnIds = null,
        IReadOnlyList<SwShPlacementHiddenItemChance>? hiddenItemChances = null)
    {
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            CreatePlacementPack(
                includeStaticObject: includeStaticObject,
                includeRuntimeOwnedWailord: includeRuntimeOwnedWailord,
                staticSpawnIds: staticSpawnIds,
                hiddenItemChances: hiddenItemChances));
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
        IReadOnlyList<uint>? fieldItemRawItems = null,
        bool includeStaticObject = false,
        bool includeFieldItemHash = true,
        bool omitFieldItemCanonicalStorage = false,
        float fieldItemX = 10.5f,
        bool includeRuntimeOwnedWailord = false,
        IReadOnlyList<ulong>? staticSpawnIds = null,
        IReadOnlyList<SwShPlacementHiddenItemChance>? hiddenItemChances = null)
    {
        includeStaticObject |= includeRuntimeOwnedWailord;
        var areaNames = new List<SwShAhtbEntry>
        {
            new(SwShGfPackFile.HashFnv1a64(AreaName), AreaName),
        };
        var zoneNames = new List<SwShAhtbEntry>
        {
            new(ZoneHash, "Route 1"),
        };
        var objectNames = new List<SwShAhtbEntry>
        {
            new(ObjectHash, "objects/hidden_item.gfbmdl"),
        };

        if (includeStaticObject)
        {
            areaNames.Add(new SwShAhtbEntry(SwShGfPackFile.HashFnv1a64(StaticAreaName), StaticAreaName));
            zoneNames.Add(new SwShAhtbEntry(StaticZoneHash, "Test Cave"));
        }

        if (includeRuntimeOwnedWailord)
        {
            objectNames.Add(new SwShAhtbEntry(
                Wr02HoeruoObjectHash,
                "z_wr02onload_SymbolEncountPokemonGimmickSpawner_WR02_Hoeruo_0"));
        }

        if (staticSpawnIds?.Contains(SecondStaticEncounterHash) == true)
        {
            objectNames.Add(new SwShAhtbEntry(SecondStaticEncounterHash, "Second static encounter"));
        }

        var areaArchive = CreatePlacementArchive(
            fieldItemHash,
            fieldItemRawItems,
            includeFieldItemHash,
            fieldItemX,
            hiddenItemChances).Write();
        if (omitFieldItemCanonicalStorage)
        {
            OmitFieldItemCanonicalStorage(areaArchive);
        }

        var files = new List<SwShGfPackNamedFile>
        {
            new SwShGfPackNamedFile(
                "AreaNameHashTable.tbl",
                new SwShAhtbFile(areaNames).Write()),
            new SwShGfPackNamedFile(
                "ZoneNameHashTable.tbl",
                new SwShAhtbFile(zoneNames).Write()),
            new SwShGfPackNamedFile(
                "ObjectNameHashTable.tbl",
                new SwShAhtbFile(objectNames).Write()),
            new SwShGfPackNamedFile(AreaMember, areaArchive),
        };

        if (includeStaticObject)
        {
            files.Add(new SwShGfPackNamedFile(
                StaticAreaMember,
                CreateStaticPlacementArchive(
                    includeRuntimeOwnedWailord ? Wr02HoeruoObjectHash : 0,
                    staticSpawnIds ?? [StaticEncounterHash])));
        }

        return SwShGfPackFile.Create(files).Write();
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
        IReadOnlyList<uint>? fieldItemRawItems = null,
        bool includeFieldItemHash = true,
        float fieldItemX = 10.5f,
        IReadOnlyList<SwShPlacementHiddenItemChance>? hiddenItemChances = null)
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
                        Transform: new SwShPlacementTransform(fieldItemX, 0, -4.25f, 90),
                        ItemHashes: includeFieldItemHash ? [fieldItemHash ?? PotionHash] : [],
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
                        Chances: hiddenItemChances ??
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

    private static void OmitFieldItemCanonicalStorage(byte[] data)
    {
        var root = ReadUOffset(data, 0);
        var zones = ReadTableUOffset(data, root, 0);
        var zone = ReadUOffset(data, zones + sizeof(int));
        var fieldItems = ReadTableUOffset(data, zone, 6);
        var holder = ReadUOffset(data, fieldItems + sizeof(int));
        var item = ReadTableUOffset(data, holder, 0);
        var transform = ReadTableUOffset(data, item, 0);

        ClearTableField(data, transform, 1);
        ClearTableField(data, transform, 2);
        ClearTableField(data, transform, 4);
        ClearTableField(data, item, 8);
    }

    private static int ReadTableUOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        var valueOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        return valueOffset == 0 ? 0 : ReadUOffset(data, valueOffset);
    }

    private static int ReadTableFieldOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var entryOffset = vtableOffset + (sizeof(ushort) * (2 + fieldIndex));
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(vtableOffset, sizeof(ushort)));
        if (entryOffset + sizeof(ushort) > vtableOffset + vtableLength)
        {
            return 0;
        }

        var relativeOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryOffset, sizeof(ushort)));
        return relativeOffset == 0 ? 0 : tableOffset + relativeOffset;
    }

    private static int ReadUOffset(byte[] data, int offset)
    {
        return checked(offset + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint))));
    }

    private static void ClearTableField(byte[] data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var entryOffset = vtableOffset + (sizeof(ushort) * (2 + fieldIndex));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset, sizeof(ushort)), 0);
    }

    private static byte[] CreateStaticPlacementArchive(
        ulong objectHash,
        IReadOnlyList<ulong> spawnIds)
    {
        var writer = new StaticPlacementWriter(objectHash, spawnIds);
        return writer.Write();
    }

    private sealed class StaticPlacementWriter
    {
        private readonly MemoryStream stream = new();
        private readonly BinaryWriter writer;
        private readonly ulong objectHash;
        private readonly IReadOnlyList<ulong> spawnIds;

        public StaticPlacementWriter(ulong objectHash, IReadOnlyList<ulong> spawnIds)
        {
            writer = new BinaryWriter(stream);
            this.objectHash = objectHash;
            this.spawnIds = spawnIds;
        }

        public byte[] Write()
        {
            writer.Write(0);
            var root = WriteTable(1, objectSize: 8, Offsets(1, (0, 4)));
            PatchUOffset(0, root);

            var zones = WriteTableVector(1);
            PatchUOffset(root + 4, zones);
            var zone = WriteZone();
            PatchUOffset(zones + sizeof(int), zone);

            return stream.ToArray();
        }

        private int WriteZone()
        {
            var zone = WriteTable(27, objectSize: 12, Offsets(27, (0, 4), (26, 8)));
            var meta = WriteZoneMeta();
            PatchUOffset(zone + 4, meta);
            var staticObjects = WriteTableVector(1);
            PatchUOffset(zone + 8, staticObjects);
            var staticObject = WriteStaticObjectHolder();
            PatchUOffset(staticObjects + sizeof(int), staticObject);
            return zone;
        }

        private int WriteZoneMeta()
        {
            var meta = WriteTable(2, objectSize: 16, Offsets(2, (0, 4), (1, 8)));
            var transform = WriteTransform(0, 0, 0, 0, objectHash: 0);
            PatchUOffset(meta + 4, transform);
            WriteUInt64(meta + 8, StaticZoneHash);
            return meta;
        }

        private int WriteStaticObjectHolder()
        {
            var holder = WriteTable(1, objectSize: 8, Offsets(1, (0, 4)));
            var staticObject = WriteStaticObject();
            PatchUOffset(holder + 4, staticObject);
            return holder;
        }

        private int WriteStaticObject()
        {
            var staticObject = WriteTable(8, objectSize: 12, Offsets(8, (0, 4), (5, 8)));
            var identifier = WriteTransform(24, 1, -32, 90, objectHash);
            PatchUOffset(staticObject + 4, identifier);
            var spawns = WriteTableVector(spawnIds.Count);
            PatchUOffset(staticObject + 8, spawns);
            for (var index = 0; index < spawnIds.Count; index++)
            {
                var spawn = WriteStaticObjectSpawn(spawnIds[index]);
                PatchUOffset(spawns + sizeof(int) + (index * sizeof(uint)), spawn);
            }

            return staticObject;
        }

        private int WriteStaticObjectSpawn(ulong spawnId)
        {
            var spawn = WriteTable(5, objectSize: 16, Offsets(5, (0, 8)));
            WriteUInt64(spawn + 8, spawnId);
            return spawn;
        }

        private int WriteTransform(float x, float y, float z, float rotationY, ulong objectHash)
        {
            var transform = WriteTable(12, objectSize: 32, Offsets(12, (0, 4), (1, 8), (2, 12), (4, 16), (9, 24)));
            WriteSingle(transform + 4, x);
            WriteSingle(transform + 8, y);
            WriteSingle(transform + 12, z);
            WriteSingle(transform + 16, rotationY);
            WriteUInt64(transform + 24, objectHash);
            return transform;
        }

        private int WriteTableVector(int count)
        {
            var offset = checked((int)stream.Position);
            writer.Write(count);
            for (var index = 0; index < count; index++)
            {
                writer.Write(0);
            }

            return offset;
        }

        private int WriteTable(int fieldCount, ushort objectSize, IReadOnlyList<ushort> fieldOffsets)
        {
            var vtableStart = checked((int)stream.Position);
            writer.Write((ushort)(sizeof(ushort) * (2 + fieldCount)));
            writer.Write(objectSize);
            for (var index = 0; index < fieldCount; index++)
            {
                writer.Write(index < fieldOffsets.Count ? fieldOffsets[index] : (ushort)0);
            }

            var tableStart = checked((int)stream.Position);
            writer.Write(tableStart - vtableStart);
            for (var index = sizeof(int); index < objectSize; index++)
            {
                writer.Write((byte)0);
            }

            return tableStart;
        }

        private static ushort[] Offsets(int fieldCount, params (int FieldIndex, ushort Offset)[] values)
        {
            var offsets = new ushort[fieldCount];
            foreach (var value in values)
            {
                offsets[value.FieldIndex] = value.Offset;
            }

            return offsets;
        }

        private void PatchUOffset(int sourceOffset, int targetOffset)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = sourceOffset;
            writer.Write((uint)(targetOffset - sourceOffset));
            writer.BaseStream.Position = position;
        }

        private void WriteUInt64(int offset, ulong value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }

        private void WriteSingle(int offset, float value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }
    }
}
