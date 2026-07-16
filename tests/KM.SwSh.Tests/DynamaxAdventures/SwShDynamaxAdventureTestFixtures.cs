// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.Core.Projects;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Moves;
using KM.SwSh.Pokemon;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;

namespace KM.SwSh.Tests.DynamaxAdventures;

internal static class SwShDynamaxAdventureTestFixtures
{
    public static void WriteBaseDynamaxAdventures(
        TemporarySwShProject temp,
        bool includeDependencies = true)
    {
        temp.SelectedGame = ProjectGame.Sword;
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            CreateArchive().Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateTextTable(898, (25, "Pikachu"), (133, "Eevee"), (467, "Magmortar")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateTextTable(4, (4, "Poke Ball")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateTextTable(85, (1, "Tackle"), (2, "Growl"), (10, "Vine Whip"), (20, "Razor Leaf"), (85, "Thunderbolt")));
        temp.WriteBaseExeFsFile("main", CreateCompatibleMain());

        if (includeDependencies)
        {
            WriteBaseSafetyDependencies(temp);
        }
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

    public static SwShDynamaxAdventureArchive CreateRowCountArchive(int rowCount)
    {
        var normalSpecies = Enumerable.Range(1, SwShDynamaxAdventureSafetyRules.MaximumVerifiedNormalReplacementSpecies)
            .Where(species => !SwShDynamaxAdventureSafetyRules.IsSpecialNormalRouteSpecies(species))
            .Take(SwShDynamaxAdventureSafetyRules.BossEntryStartIndex)
            .ToArray();
        int[] bossSpecies = [144, 145, 146, 150];
        return new SwShDynamaxAdventureArchive(
            Enumerable.Range(0, rowCount)
                .Select(index => new SwShDynamaxAdventureRecord(
                    index,
                    IsSingleCapture: index >= SwShDynamaxAdventureSafetyRules.BossEntryStartIndex,
                    SingleCaptureFlagBlock: (ulong)(index + 1),
                    Field02: 0,
                    Form: 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: index + 1,
                    Level: index >= SwShDynamaxAdventureSafetyRules.BossEntryStartIndex ? 70 : 65,
                    Species: index < SwShDynamaxAdventureSafetyRules.BossEntryStartIndex
                        ? normalSpecies[index]
                        : bossSpecies[(index - SwShDynamaxAdventureSafetyRules.BossEntryStartIndex) % bossSpecies.Length],
                    UiMessageId: (ulong)(index + 1),
                    OtGender: 0,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-2, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [1, 2, 3, 4]))
                .ToArray());
    }

    public static void WriteBasePersonalData(TemporarySwShProject temp, int count = 200)
    {
        temp.WriteBaseRomFsFile(
            SwShPersonalTable.PersonalDataRelativePath["romfs/".Length..],
            CreatePersonalTable(Enumerable.Range(0, count).Select(index =>
            {
                var record = CreatePersonalRecord(type1: 0, type2: 0);
                record[0x21] |= 0x40;
                if (index == 133 && count > 134)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1E), checked((ushort)(count - 1)));
                    record[0x20] = 2;
                }
                else if (index == count - 1 && count > 134)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5E), 1);
                }
                return record;
            })));
    }

    private static void WriteBaseSafetyDependencies(TemporarySwShProject temp)
    {
        const int recordCount = 900;
        temp.WriteBaseRomFsFile(
            SwShPersonalTable.PersonalDataRelativePath["romfs/".Length..],
            CreatePersonalTable(Enumerable.Range(0, recordCount).Select(index =>
            {
                var record = CreatePersonalRecord(type1: 0, type2: 0);
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x18), 1);
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1A), 2);
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1C), 3);
                record[0x21] |= 0x40;
                if (index == 133)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1E), checked((ushort)(recordCount - 1)));
                    record[0x20] = 2;
                }
                else if (index == recordCount - 1)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5E), 1);
                }
                return record;
            })));

        var moveIds = new[] { 1, 2, 3, 4, 5, 6, 10, 20, 85 };
        foreach (var moveId in moveIds)
        {
            temp.WriteBaseRomFsFile(
                $"bin/pml/waza/waza{moveId:0000}.wazabin",
                SwShMoveDataFile.Write(CreateMoveRecord(moveId)));
        }

        var learnsets = new byte[recordCount * SwShPokemonLearnsetTable.RecordSize];
        learnsets.AsSpan().Fill(byte.MaxValue);
        for (var personalId = 0; personalId < recordCount; personalId++)
        {
            SwShPokemonLearnsetTable.WriteRecord(
                new SwShPokemonLearnsetRecord(
                    personalId,
                    moveIds.Select((moveId, index) =>
                        new SwShPokemonLearnsetMoveRecord(index, moveId, Level: 1)).ToArray()),
                learnsets.AsSpan(
                    personalId * SwShPokemonLearnsetTable.RecordSize,
                    SwShPokemonLearnsetTable.RecordSize));
        }

        temp.WriteBaseRomFsFile(
            SwShPokemonLearnsetTable.LearnsetDataRelativePath["romfs/".Length..],
            learnsets);
    }

    private static SwShMoveDataRecord CreateMoveRecord(int moveId)
    {
        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: checked((uint)moveId),
            CanUseMove: true,
            new SwShMoveCoreStats(0, 2, 1, 40, 100, 35, 0, 0, 90),
            new SwShMoveTargeting(3, 1, 1, 0, 0),
            new SwShMoveSecondaryEffects(0, 0, 0, 0, 0, 0, 0),
            [
                new SwShMoveStatChange(1, 0, 0, 0),
                new SwShMoveStatChange(2, 0, 0, 0),
                new SwShMoveStatChange(3, 0, 0, 0),
            ],
            new SwShMoveFlags(
                false, false, false, true, false, false, false, false, false,
                false, false, false, false, false, false, false, false, false));
    }

    public static byte[] CreatePersonalTable(IEnumerable<byte[]> records)
    {
        var rows = records.ToArray();
        var data = new byte[rows.Length * SwShPersonalTable.RecordSize];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index].CopyTo(data.AsSpan(index * SwShPersonalTable.RecordSize));
        }

        return data;
    }

    public static byte[] CreatePersonalRecord(int type1, int type2)
    {
        var record = new byte[SwShPersonalTable.RecordSize];
        record[0x06] = checked((byte)type1);
        record[0x07] = checked((byte)type2);
        record[0x20] = 1;
        return record;
    }

    public static void ClearTableField(byte[] data, int entryIndex, int fieldIndex)
    {
        var tableOffset = ReadEntryTableOffset(data, entryIndex);
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
        var fieldEntryOffset = sizeof(ushort) * 2 + (fieldIndex * sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(vtableOffset + fieldEntryOffset, sizeof(ushort)), 0);
    }

    private static int ReadEntryTableOffset(ReadOnlySpan<byte> data, int entryIndex)
    {
        var rootTableOffset = ReadUOffset(data, offset: 0);
        var vectorFieldOffset = ReadTableFieldOffset(data, rootTableOffset, fieldIndex: 0);
        var vectorOffset = ReadUOffset(data, rootTableOffset + vectorFieldOffset);
        var elementOffset = vectorOffset + sizeof(uint) + (entryIndex * sizeof(uint));

        return ReadUOffset(data, elementOffset);
    }

    private static int ReadTableFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        var fieldEntryOffset = sizeof(ushort) * 2 + (fieldIndex * sizeof(ushort));
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableOffset, sizeof(ushort)));

        return fieldEntryOffset + sizeof(ushort) <= vtableLength
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableOffset + fieldEntryOffset, sizeof(ushort)))
            : 0;
    }

    private static int ReadUOffset(ReadOnlySpan<byte> data, int offset)
    {
        return checked(offset + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint))));
    }

    public static byte[] CreateCompatibleMain(
        int commandValidatorOffsetDelta = 0,
        string? buildId = null,
        SwShDynamaxAdventureArchive? sourceArchive = null)
    {
        var archive = sourceArchive ?? CreateArchive();
        var text = new byte[SwShDynamaxAdventuresMainPatcher.DaiGigantamaxMismatchBranchOffset
            + commandValidatorOffsetDelta
            + sizeof(uint)];
        var ro = new byte[SwShDynamaxAdventuresMainPatcher.SummaryOffset
            + (archive.Entries.Count * SwShDynamaxAdventuresMainPatcher.SummaryEntrySize)];

        WriteCommandValidators(text, commandValidatorOffsetDelta);
        SwShDynamaxAdventuresMainPatcher.WriteSummary(ro, archive.Entries);

        return CreateNso(text, ro, [], buildId);
    }

    public static byte[] CreateProductCompatibleMain(
        SwShDynamaxAdventureArchive archive,
        int commandValidatorOffsetDelta = 0,
        string? buildId = null)
    {
        var textLength = Math.Max(
            SwShDynamaxAdventuresMainPatcher.DaiGigantamaxMismatchBranchOffset
                + commandValidatorOffsetDelta
                + sizeof(uint),
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset
                + (commandValidatorOffsetDelta == 0 ? 0 : SwShDynamaxAdventuresBossTargetPatcher.ShieldCallSiteOffsetDelta)
                + sizeof(uint));
        var text = new byte[textLength];
        var ro = new byte[SwShDynamaxAdventuresMainPatcher.SummaryOffset
            + (archive.Entries.Count * SwShDynamaxAdventuresMainPatcher.SummaryEntrySize)];
        WriteCommandValidators(text, commandValidatorOffsetDelta);
        var bossDelta = commandValidatorOffsetDelta == 0
            ? 0
            : SwShDynamaxAdventuresBossTargetPatcher.ShieldCallSiteOffsetDelta;
        WriteInstruction(
            text,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset + bossDelta,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteAVanillaInstruction);
        WriteInstruction(
            text,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset + bossDelta,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBVanillaInstruction);
        SwShDynamaxAdventuresMainPatcher.WriteSummary(ro, archive.Entries);
        return CreateNso(text, ro, [], buildId);
    }

    private static void WriteCommandValidators(byte[] text, int commandValidatorOffsetDelta)
    {
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalSpeciesPresentMismatchBranchOffset + commandValidatorOffsetDelta, 0x1400001C);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalSpeciesMissingMismatchBranchOffset + commandValidatorOffsetDelta, 0x540002E1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalFormPresentMismatchBranchOffset + commandValidatorOffsetDelta, 0x1400000A);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalFormMissingMismatchBranchOffset + commandValidatorOffsetDelta, 0x540000A1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.LocalGigantamaxMismatchBranchOffset + commandValidatorOffsetDelta, 0x35000068);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestSpeciesPresentMismatchBranchOffset + commandValidatorOffsetDelta, 0x1400001C);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestSpeciesMissingMismatchBranchOffset + commandValidatorOffsetDelta, 0x540002E1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestFormPresentMismatchBranchOffset + commandValidatorOffsetDelta, 0x1400000A);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestFormMissingMismatchBranchOffset + commandValidatorOffsetDelta, 0x540000A1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.NestGigantamaxMismatchBranchOffset + commandValidatorOffsetDelta, 0x35000068);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiSpeciesPresentMismatchBranchOffset + commandValidatorOffsetDelta, 0x1400001C);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiSpeciesMissingMismatchBranchOffset + commandValidatorOffsetDelta, 0x540002E1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiFormPresentMismatchBranchOffset + commandValidatorOffsetDelta, 0x1400000A);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiFormMissingMismatchBranchOffset + commandValidatorOffsetDelta, 0x540000A1);
        WriteInstruction(text, SwShDynamaxAdventuresMainPatcher.DaiGigantamaxMismatchBranchOffset + commandValidatorOffsetDelta, 0x35000068);
    }

    public static byte[] CreateBossTargetCompatibleMain(int callSiteOffsetDelta = 0, string? buildId = null)
    {
        var text = new byte[SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset
            + callSiteOffsetDelta
            + sizeof(uint)];
        var ro = new byte[] { 1, 2, 3, 4 };
        var data = new byte[] { 5, 6, 7, 8 };

        WriteInstruction(
            text,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset + callSiteOffsetDelta,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteAVanillaInstruction);
        WriteInstruction(
            text,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset + callSiteOffsetDelta,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBVanillaInstruction);

        return CreateNso(text, ro, data, buildId);
    }

    public static byte[] CreateBossTargetAndSummaryCompatibleMain(
        int entryCount,
        int callSiteOffsetDelta = 0,
        string? buildId = null)
    {
        var text = new byte[SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset
            + callSiteOffsetDelta
            + sizeof(uint)];
        var ro = new byte[SwShDynamaxAdventuresMainPatcher.SummaryOffset
            + (entryCount * SwShDynamaxAdventuresMainPatcher.SummaryEntrySize)];
        var data = new byte[] { 5, 6, 7, 8 };

        WriteInstruction(
            text,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset + callSiteOffsetDelta,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteAVanillaInstruction);
        WriteInstruction(
            text,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset + callSiteOffsetDelta,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBVanillaInstruction);

        return CreateNso(text, ro, data, buildId);
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

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, string? buildId = null)
    {
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var roMemoryOffset = Align(text.Length, 0x1000);
        var dataMemoryOffset = Align(roMemoryOffset + ro.Length, 0x1000);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, roMemoryOffset, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, dataMemoryOffset, data.Length);
        if (buildId is null)
        {
            Convert.FromHexString(SwShDynamaxAdventuresMainPatcher.SwordBuildId)
                .CopyTo(output.AsSpan(0x40, 0x20));
        }
        else
        {
            Convert.FromHexString(buildId).CopyTo(output.AsSpan(0x40, 0x20));
        }
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
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
