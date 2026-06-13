// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;

namespace KM.SwSh.Tests.DynamaxAdventures;

internal static class SwShDynamaxAdventureTestFixtures
{
    public static void WriteBaseDynamaxAdventures(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            CreateArchive().Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateTextTable(133, (25, "Pikachu"), (133, "Eevee")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateTextTable(4, (4, "Poke Ball")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateTextTable(85, (1, "Tackle"), (2, "Growl"), (10, "Vine Whip"), (20, "Razor Leaf"), (85, "Thunderbolt")));
    }

    public static SwShDynamaxAdventureArchive CreateArchive()
    {
        return new SwShDynamaxAdventureArchive(
        [
            new SwShDynamaxAdventureRecord(
                0,
                IsSingleCapture: true,
                SingleCaptureFlagBlock: 0x1122334455667788UL,
                Field02: 0,
                Form: 1,
                GigantamaxState: 1,
                BallItemId: 4,
                AdventureIndex: 100,
                Level: 65,
                Species: 133,
                UiMessageId: 0x8877665544332211UL,
                OtGender: 1,
                Version: 1,
                ShinyRoll: 1,
                new SwShDynamaxAdventureIvs(-4, -1, -1, -1, -1, -1),
                Ability: 1,
                IsStoryProgressGated: true,
                Moves: [1, 2, 10, 20]),
            new SwShDynamaxAdventureRecord(
                1,
                IsSingleCapture: false,
                SingleCaptureFlagBlock: 0x0102030405060708UL,
                Field02: 0,
                Form: 0,
                GigantamaxState: 0,
                BallItemId: 4,
                AdventureIndex: 101,
                Level: 60,
                Species: 25,
                UiMessageId: 0x0807060504030201UL,
                OtGender: 1,
                Version: 0,
                ShinyRoll: 1,
                new SwShDynamaxAdventureIvs(-1, 0, 1, 2, 3, 31),
                Ability: 0,
                IsStoryProgressGated: false,
                Moves: [3, 4, 5, 6]),
        ]);
    }

    public static byte[] CreateCompatibleMain()
    {
        var archive = CreateArchive();
        var text = new byte[SwShDynamaxAdventuresMainPatcher.DaiGigantamaxMismatchBranchOffset + sizeof(uint)];
        var ro = new byte[SwShDynamaxAdventuresMainPatcher.SummaryOffset
            + (archive.Entries.Count * SwShDynamaxAdventuresMainPatcher.SummaryEntrySize)];

        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalSpeciesPresentMismatchBranchOffset, 0x1400001C);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalSpeciesMissingMismatchBranchOffset, 0x540002E1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalFormPresentMismatchBranchOffset, 0x1400000A);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalFormMissingMismatchBranchOffset, 0x540000A1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalGigantamaxMismatchBranchOffset, 0x35000068);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestSpeciesPresentMismatchBranchOffset, 0x1400001C);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestSpeciesMissingMismatchBranchOffset, 0x540002E1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestFormPresentMismatchBranchOffset, 0x1400000A);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestFormMissingMismatchBranchOffset, 0x540000A1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestGigantamaxMismatchBranchOffset, 0x35000068);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiSpeciesPresentMismatchBranchOffset, 0x1400001C);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiSpeciesMissingMismatchBranchOffset, 0x540002E1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiFormPresentMismatchBranchOffset, 0x1400000A);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiFormMissingMismatchBranchOffset, 0x540000A1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiGigantamaxMismatchBranchOffset, 0x35000068);
        SwShDynamaxAdventuresMainPatcher.WriteSummary(ro, archive.Entries);

        return CreateNso(text, ro, []);
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(_ => new SwShGameTextLine(string.Empty, Flags: 0))
            .ToArray();

        foreach (var (index, value) in entries)
        {
            lines[index] = new SwShGameTextLine(value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data)
    {
        var textOffset = SwShNsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), SwShNsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Fill(0xAB);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        SwShNsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        SwShNsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        SwShNsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));

        return output;
    }

    private static void WriteSegmentHeader(byte[] output, int offset, int fileOffset, int memoryOffset, int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
