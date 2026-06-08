// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;

namespace KM.Integration.Tests.Tools;

internal static class SwShPokemonBridgeFixtures
{
    public static void WriteBasePokemonData(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            CreatePersonalTable(CreateEmptyPersonalRecord(), CreateBulbasaurPersonalRecord()));
        temp.WriteBaseRomFsFile(
            "bin/pml/waza_oboe/wazaoboe_total.bin",
            CreateLearnsetTable([], [(33, 1), (45, 3)]));
        temp.WriteBaseRomFsFile(
            "bin/pml/evolution/evo_001.bin",
            CreateEvolutionFile((4, 0, 2, 0, 16)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/pokelist.dat",
            CreateTextTable("None", "Bulbasaur"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedMoveNames());
    }

    private static byte[] CreatePersonalTable(params byte[][] records)
    {
        var data = new byte[records.Length * SwShPersonalTable.RecordSize];
        for (var index = 0; index < records.Length; index++)
        {
            records[index].CopyTo(data.AsSpan(index * SwShPersonalTable.RecordSize));
        }

        return data;
    }

    private static byte[] CreateEmptyPersonalRecord()
    {
        return new byte[SwShPersonalTable.RecordSize];
    }

    private static byte[] CreateBulbasaurPersonalRecord()
    {
        var record = new byte[SwShPersonalTable.RecordSize];
        record[0x00] = 45;
        record[0x01] = 49;
        record[0x02] = 49;
        record[0x03] = 45;
        record[0x04] = 65;
        record[0x05] = 65;
        record[0x06] = 11;
        record[0x07] = 3;
        record[0x08] = 45;
        record[0x09] = 1;
        record[0x12] = 31;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x18), 65);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1C), 34);
        record[0x20] = 1;
        record[0x21] = 12 | (1 << 6);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x22), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x24), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x26), 69);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x56), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5C), 1);

        return record;
    }

    private static byte[] CreateLearnsetTable(params (ushort MoveId, ushort Level)[][] learnsets)
    {
        var data = new byte[learnsets.Length * SwShPokemonLearnsetTable.RecordSize];
        for (var recordIndex = 0; recordIndex < learnsets.Length; recordIndex++)
        {
            var recordOffset = recordIndex * SwShPokemonLearnsetTable.RecordSize;
            var moves = learnsets[recordIndex];
            for (var moveIndex = 0; moveIndex < moves.Length; moveIndex++)
            {
                var moveOffset = recordOffset + (moveIndex * 4);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(moveOffset), moves[moveIndex].MoveId);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(moveOffset + 2), moves[moveIndex].Level);
            }

            var sentinelOffset = recordOffset + (moves.Length * 4);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(sentinelOffset), ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(sentinelOffset + 2), ushort.MaxValue);
        }

        return data;
    }

    private static byte[] CreateEvolutionFile(params (ushort Method, ushort Argument, ushort Species, byte Form, byte Level)[] evolutions)
    {
        var data = new byte[SwShEvolutionSet.FileSize];
        for (var index = 0; index < evolutions.Length; index++)
        {
            var evolution = evolutions[index];
            var offset = index * SwShEvolutionSet.RecordSize;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), evolution.Method);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), evolution.Argument);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 4), evolution.Species);
            data[offset + 6] = evolution.Form;
            data[offset + 7] = evolution.Level;
        }

        return data;
    }

    private static byte[] CreateIndexedMoveNames()
    {
        var names = Enumerable.Range(0, 46)
            .Select(index => $"Move {index}")
            .ToArray();
        names[33] = "Tackle";
        names[45] = "Growl";

        return CreateTextTable(names);
    }

    private static byte[] CreateTextTable(params string[] lines)
    {
        return SwShGameTextFile.Write(
            lines.Select(line => new SwShGameTextLine(line, Flags: 0)).ToArray());
    }
}
