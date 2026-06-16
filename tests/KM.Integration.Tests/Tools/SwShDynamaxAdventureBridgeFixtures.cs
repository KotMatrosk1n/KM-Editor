// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;

namespace KM.Integration.Tests.Tools;

internal static class SwShDynamaxAdventureBridgeFixtures
{
    private const int BossStartRow = 226;

    public static void WriteBaseDynamaxAdventures(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            CreateArchive().Write());
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(133, (25, "Pikachu"), (133, "Eevee")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(85, (1, "Tackle"), (2, "Growl"), (10, "Vine Whip"), (20, "Razor Leaf"), (85, "Thunderbolt")));
    }

    public static void WriteSeedPlanningDynamaxAdventures(TemporaryBridgeProject temp, int rowCount)
    {
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(
                Enumerable.Range(0, rowCount).Select(row => new SwShDynamaxAdventureRecord(
                    row,
                    IsSingleCapture: row >= BossStartRow,
                    SingleCaptureFlagBlock: (ulong)(row + 1),
                    Field02: 0,
                    Form: 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: row + 1,
                    Level: row >= BossStartRow ? 70 : 65,
                    Species: row + 1,
                    UiMessageId: (ulong)(row + 1),
                    OtGender: 1,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-5, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [1, 2, 3, 4])).ToArray()).Write());
    }

    public static void WriteBossTargetDynamaxAdventures(TemporaryBridgeProject temp)
    {
        var normalEntries = Enumerable.Range(0, BossStartRow)
            .Select(row => CreateRecord(
                row,
                adventureIndex: row,
                species: row + 1,
                version: 0,
                isBoss: false,
                isStoryProgressGated: false));
        var bossEntries = new[]
        {
            CreateRecord(226, adventureIndex: 1003, species: 144, version: 0, isBoss: true, isStoryProgressGated: false),
            CreateRecord(227, adventureIndex: 1004, species: 150, version: 0, isBoss: true, isStoryProgressGated: false),
            CreateRecord(228, adventureIndex: 1019, species: 484, version: 2, isBoss: true, isStoryProgressGated: false),
            CreateRecord(229, adventureIndex: 1038, species: 800, version: 0, isBoss: true, isStoryProgressGated: true),
        };

        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(normalEntries.Concat(bossEntries).ToArray()).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateTextTable(800, (25, "Pikachu"), (144, "Articuno"), (150, "Mewtwo"), (484, "Palkia"), (800, "Necrozma")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(4, (1, "Tackle"), (2, "Growl"), (3, "Water Gun"), (4, "Ember")));
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

    public static void WriteBasePersonalData(TemporaryBridgeProject temp, int count = 200)
    {
        temp.WriteBaseRomFsFile(
            SwShPersonalTable.PersonalDataRelativePath["romfs/".Length..],
            CreatePersonalTable(Enumerable.Range(0, count).Select(_ =>
            {
                var record = CreatePersonalRecord(type1: 0, type2: 0);
                record[0x21] |= 0x40;
                return record;
            })));
    }

    public static void WriteBaseMoveLegalityData(TemporaryBridgeProject temp)
    {
        WriteMoveData(temp, (10, true), (85, true));
        WriteLearnsetData(temp, recordCount: 200, (25, [(85, 50), (10, 70)]));
    }

    private static SwShDynamaxAdventureRecord CreateRecord(
        int entryIndex,
        int adventureIndex,
        int species,
        int version,
        bool isBoss,
        bool isStoryProgressGated)
    {
        return new SwShDynamaxAdventureRecord(
            entryIndex,
            IsSingleCapture: isBoss,
            SingleCaptureFlagBlock: 0x1000000000000000UL + (uint)entryIndex,
            Field02: 0,
            Form: 0,
            GigantamaxState: isBoss ? 1 : 0,
            BallItemId: 4,
            AdventureIndex: adventureIndex,
            Level: isBoss ? 70 : 65,
            Species: species,
            UiMessageId: 0x2000000000000000UL + (uint)entryIndex,
            OtGender: 1,
            Version: version,
            ShinyRoll: 1,
            new SwShDynamaxAdventureIvs(-5, -1, -1, -1, -1, -1),
            Ability: isBoss ? 0 : 1,
            IsStoryProgressGated: isStoryProgressGated,
            Moves: [1, 2, 3, 4]);
    }

    private static byte[] CreatePersonalTable(IEnumerable<byte[]> records)
    {
        var rows = records.ToArray();
        var data = new byte[rows.Length * SwShPersonalTable.RecordSize];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index].CopyTo(data.AsSpan(index * SwShPersonalTable.RecordSize));
        }

        return data;
    }

    private static byte[] CreatePersonalRecord(int type1, int type2)
    {
        var record = new byte[SwShPersonalTable.RecordSize];
        record[0x06] = checked((byte)type1);
        record[0x07] = checked((byte)type2);
        record[0x20] = 1;
        return record;
    }

    private static void WriteMoveData(TemporaryBridgeProject temp, params (int MoveId, bool CanUseMove)[] moves)
    {
        foreach (var (moveId, canUseMove) in moves)
        {
            temp.WriteBaseRomFsFile(
                $"{SwShMoveDataFile.MoveDataRelativeDirectory["romfs/".Length..]}/waza{moveId:0000}.wazabin",
                SwShMoveDataFile.Write(CreateMoveRecord(moveId, canUseMove)));
        }
    }

    private static SwShMoveDataRecord CreateMoveRecord(int moveId, bool canUseMove)
    {
        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: checked((uint)moveId),
            CanUseMove: canUseMove,
            new SwShMoveCoreStats(
                Type: 0,
                Quality: 2,
                Category: 1,
                Power: 40,
                Accuracy: 100,
                PP: 35,
                Priority: 0,
                CritStage: 0,
                GigantamaxPower: 90),
            new SwShMoveTargeting(
                RawTarget: 3,
                HitMin: 1,
                HitMax: 1,
                TurnMin: 0,
                TurnMax: 0),
            new SwShMoveSecondaryEffects(
                Inflict: 0,
                InflictPercent: 0,
                RawInflictCount: 0,
                Flinch: 0,
                EffectSequence: 0,
                Recoil: 0,
                RawHealing: 0),
            [
                new SwShMoveStatChange(1, Stat: 0, Stage: 0, Percent: 0),
                new SwShMoveStatChange(2, Stat: 0, Stage: 0, Percent: 0),
                new SwShMoveStatChange(3, Stat: 0, Stage: 0, Percent: 0),
            ],
            new SwShMoveFlags(
                MakesContact: false,
                Charge: false,
                Recharge: false,
                Protect: true,
                Reflectable: false,
                Snatch: false,
                Mirror: false,
                Punch: false,
                Sound: false,
                Gravity: false,
                Defrost: false,
                DistanceTriple: false,
                Heal: false,
                IgnoreSubstitute: false,
                FailSkyBattle: false,
                AnimateAlly: false,
                Dance: false,
                Metronome: false));
    }

    private static void WriteLearnsetData(
        TemporaryBridgeProject temp,
        int recordCount,
        params (int PersonalId, (int MoveId, int Level)[] Moves)[] learnsets)
    {
        var data = new byte[recordCount * SwShPokemonLearnsetTable.RecordSize];
        data.AsSpan().Fill(byte.MaxValue);
        foreach (var (personalId, moves) in learnsets)
        {
            SwShPokemonLearnsetTable.WriteRecord(
                new SwShPokemonLearnsetRecord(
                    personalId,
                    moves.Select((move, index) => new SwShPokemonLearnsetMoveRecord(index, move.MoveId, move.Level)).ToArray()),
                data.AsSpan(personalId * SwShPokemonLearnsetTable.RecordSize, SwShPokemonLearnsetTable.RecordSize));
        }

        temp.WriteBaseRomFsFile(
            SwShPokemonLearnsetTable.LearnsetDataRelativePath["romfs/".Length..],
            data);
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(index => new SwShGameTextLine($"Value {index}", Flags: 0))
            .ToArray();

        foreach (var (index, value) in entries)
        {
            lines[index] = new SwShGameTextLine(value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }
}
