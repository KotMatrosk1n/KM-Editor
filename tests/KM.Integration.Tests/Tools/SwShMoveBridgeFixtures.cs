// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShMoveBridgeFixtures
{
    public static void WriteBaseMoves(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza_033.bin",
            SwShMoveDataFile.Write(CreateMoveRecord()));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedTextTable((33, "Tackle")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazainfo.dat",
            CreateIndexedTextTable((33, "A physical attack in which the user charges and slams into the target.")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/typename.dat",
            CreateTextTable(
                "Normal",
                "Fighting",
                "Flying",
                "Poison",
                "Ground",
                "Rock",
                "Bug",
                "Ghost",
                "Steel",
                "Fire",
                "Water",
                "Grass",
                "Electric",
                "Psychic",
                "Ice",
                "Dragon",
                "Dark",
                "Fairy"));
    }

    private static SwShMoveDataRecord CreateMoveRecord()
    {
        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: 33,
            CanUseMove: true,
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
                Inflict: 1,
                InflictPercent: 10,
                RawInflictCount: 1,
                Flinch: 0,
                EffectSequence: 12,
                Recoil: -25,
                RawHealing: 0),
            [
                new SwShMoveStatChange(1, Stat: 1, Stage: -1, Percent: 30),
                new SwShMoveStatChange(2, Stat: 2, Stage: 1, Percent: 40),
                new SwShMoveStatChange(3, Stat: 0, Stage: 0, Percent: 0),
            ],
            new SwShMoveFlags(
                MakesContact: true,
                Charge: false,
                Recharge: false,
                Protect: true,
                Reflectable: false,
                Snatch: false,
                Mirror: false,
                Punch: true,
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

    private static byte[] CreateIndexedTextTable(params (int Index, string Text)[] overrides)
    {
        var maxIndex = overrides.Max(overrideValue => overrideValue.Index);
        var lines = Enumerable.Range(0, maxIndex + 1)
            .Select(index => string.Empty)
            .ToArray();

        foreach (var (index, text) in overrides)
        {
            lines[index] = text;
        }

        return CreateTextTable(lines);
    }

    private static byte[] CreateTextTable(params string[] lines)
    {
        return SwShGameTextFile.Write(
            lines.Select(line => new SwShGameTextLine(line, Flags: 0)).ToArray());
    }
}
