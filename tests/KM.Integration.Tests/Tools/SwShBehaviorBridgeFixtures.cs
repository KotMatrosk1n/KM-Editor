// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;

namespace KM.Integration.Tests.Tools;

internal static class SwShBehaviorBridgeFixtures
{
    public const string BehaviorDataPath =
        "romfs/bin/field/param/symbol_encount_mons_param/symbol_encount_mons_param.bin";

    public static void WriteBaseBehavior(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            BehaviorDataPath["romfs/".Length..],
            CreateBehaviorArchive().Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateSpeciesNames());
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            CreatePersonalData());
    }

    public static SwShSymbolBehaviorArchive CreateBehaviorArchive()
    {
        return new SwShSymbolBehaviorArchive(
        [
            CreateEntry(
                index: 0,
                speciesId: 25,
                form: 0,
                behavior: "Common",
                modelPart: "body",
                internalSpeciesName: "PIKACHU",
                hitboxRadius: 1.5f,
                grassShakeRadius: 2.25f,
                hash1: 0x0102030405060708UL,
                hash2: 0x1112131415161718UL),
            CreateEntry(
                index: 1,
                speciesId: 133,
                form: 1,
                behavior: "WaterDash",
                modelPart: "head",
                internalSpeciesName: "EEVEE",
                hitboxRadius: 3.5f,
                grassShakeRadius: 4.25f,
                hash1: 0x2122232425262728UL,
                hash2: 0x3132333435363738UL),
        ]);
    }

    private static SwShSymbolBehaviorEntry CreateEntry(
        int index,
        int speciesId,
        int form,
        string behavior,
        string modelPart,
        string internalSpeciesName,
        float hitboxRadius,
        float grassShakeRadius,
        ulong hash1,
        ulong hash2)
    {
        return new SwShSymbolBehaviorEntry(
            index,
            SwShSymbolBehaviorArchive.FieldSpecs
                .Select(spec => new SwShSymbolBehaviorFieldValue(
                    spec.Field,
                    spec.FieldIndex,
                    spec.FieldType,
                    CreateValue(
                        spec,
                        speciesId,
                        form,
                        behavior,
                        modelPart,
                        internalSpeciesName,
                        hitboxRadius,
                        grassShakeRadius,
                        hash1,
                        hash2)))
                .ToArray());
    }

    private static object CreateValue(
        SwShSymbolBehaviorFieldSpec spec,
        int speciesId,
        int form,
        string behavior,
        string modelPart,
        string internalSpeciesName,
        float hitboxRadius,
        float grassShakeRadius,
        ulong hash1,
        ulong hash2)
    {
        return spec.Field switch
        {
            SwShSymbolBehaviorArchive.ModelPartField => modelPart,
            SwShSymbolBehaviorArchive.Hash1Field => hash1,
            SwShSymbolBehaviorArchive.Hash2Field => hash2,
            SwShSymbolBehaviorArchive.HitboxRadiusField => hitboxRadius,
            SwShSymbolBehaviorArchive.FormField => form,
            SwShSymbolBehaviorArchive.SpeciesIdField => speciesId,
            SwShSymbolBehaviorArchive.InternalSpeciesNameField => internalSpeciesName,
            SwShSymbolBehaviorArchive.GrassShakeRadiusField => grassShakeRadius,
            SwShSymbolBehaviorArchive.BehaviorField => behavior,
            _ => spec.FieldType switch
            {
                SwShSymbolBehaviorFieldType.Single => 0f,
                SwShSymbolBehaviorFieldType.Int32 => 0,
                SwShSymbolBehaviorFieldType.Byte => (byte)0,
                SwShSymbolBehaviorFieldType.UInt64 => 0UL,
                SwShSymbolBehaviorFieldType.String => string.Empty,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(spec),
                    $"Unsupported symbol behavior field type '{spec.FieldType}'."),
            },
        };
    }

    private static byte[] CreateSpeciesNames()
    {
        var lines = Enumerable.Range(0, 134)
            .Select(index => new SwShGameTextLine($"Species {index}", Flags: 0))
            .ToArray();
        lines[25] = new SwShGameTextLine("Pikachu", Flags: 0);
        lines[133] = new SwShGameTextLine("Eevee", Flags: 0);
        return SwShGameTextFile.Write(lines);
    }

    private static byte[] CreatePersonalData()
    {
        var data = new byte[134 * SwShPersonalTable.RecordSize];
        WritePersonalRecord(data, speciesId: 25, formCount: 1);
        WritePersonalRecord(data, speciesId: 133, formCount: 2);
        return data;
    }

    private static void WritePersonalRecord(byte[] data, int speciesId, byte formCount)
    {
        var record = data.AsSpan(speciesId * SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize);
        record[0] = 1;
        record[0x20] = formCount;
        record[0x21] = 1 << 6;
        BinaryPrimitives.WriteUInt16LittleEndian(record[0x56..], checked((ushort)speciesId));
    }
}
