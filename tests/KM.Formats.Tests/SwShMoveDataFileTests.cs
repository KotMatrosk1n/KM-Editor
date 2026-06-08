// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShMoveDataFileTests
{
    [Fact]
    public void ParseReadsMoveDataFields()
    {
        var input = CreateMoveRecord();

        var file = SwShMoveDataFile.Parse(SwShMoveDataFile.Write(input));

        Assert.Equal(1u, file.Record.Version);
        Assert.Equal(33u, file.Record.MoveId);
        Assert.True(file.Record.CanUseMove);
        Assert.Equal(1, file.Record.Core.Type);
        Assert.Equal(2, file.Record.Core.Category);
        Assert.Equal(40, file.Record.Core.Power);
        Assert.Equal(100, file.Record.Core.Accuracy);
        Assert.Equal(35, file.Record.Core.PP);
        Assert.Equal(-1, file.Record.Core.Priority);
        Assert.Equal(130, file.Record.Core.GigantamaxPower);
        Assert.Equal(4, file.Record.Targeting.RawTarget);
        Assert.Equal(1, file.Record.Targeting.HitMin);
        Assert.Equal(2, file.Record.Targeting.HitMax);
        Assert.Equal(1, file.Record.Secondary.Inflict);
        Assert.Equal(10, file.Record.Secondary.InflictPercent);
        Assert.Equal(-25, file.Record.Secondary.Recoil);
        Assert.Equal(-50, file.Record.Secondary.RawHealing);
        Assert.Collection(
            file.Record.StatChanges,
            stat =>
            {
                Assert.Equal(1, stat.Slot);
                Assert.Equal(1, stat.Stat);
                Assert.Equal(-1, stat.Stage);
                Assert.Equal(30, stat.Percent);
            },
            stat =>
            {
                Assert.Equal(2, stat.Slot);
                Assert.Equal(2, stat.Stat);
                Assert.Equal(1, stat.Stage);
                Assert.Equal(40, stat.Percent);
            },
            stat =>
            {
                Assert.Equal(3, stat.Slot);
                Assert.Equal(0, stat.Stat);
            });
        Assert.True(file.Record.Flags.MakesContact);
        Assert.True(file.Record.Flags.Protect);
        Assert.True(file.Record.Flags.Punch);
        Assert.True(file.Record.Flags.Metronome);
        Assert.False(file.Record.Flags.Sound);
    }

    [Fact]
    public void WriteRoundTripsCompleteMoveRecord()
    {
        var input = CreateMoveRecord() with
        {
            Core = CreateMoveRecord().Core with
            {
                Type = 9,
                Category = 1,
                Power = 120,
                Priority = 2,
            },
            Flags = CreateMoveRecord().Flags with
            {
                Sound = true,
                Metronome = false,
            },
        };

        var output = SwShMoveDataFile.Write(input);
        var parsed = SwShMoveDataFile.Parse(output);

        Assert.Equal(input.Version, parsed.Record.Version);
        Assert.Equal(input.MoveId, parsed.Record.MoveId);
        Assert.Equal(input.CanUseMove, parsed.Record.CanUseMove);
        Assert.Equal(input.Core, parsed.Record.Core);
        Assert.Equal(input.Targeting, parsed.Record.Targeting);
        Assert.Equal(input.Secondary, parsed.Record.Secondary);
        Assert.Equal(input.StatChanges, parsed.Record.StatChanges);
        Assert.Equal(input.Flags, parsed.Record.Flags);
    }

    private static SwShMoveDataRecord CreateMoveRecord()
    {
        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: 33,
            CanUseMove: true,
            new SwShMoveCoreStats(
                Type: 1,
                Quality: 5,
                Category: 2,
                Power: 40,
                Accuracy: 100,
                PP: 35,
                Priority: -1,
                CritStage: 1,
                GigantamaxPower: 130),
            new SwShMoveTargeting(
                RawTarget: 4,
                HitMin: 1,
                HitMax: 2,
                TurnMin: 0,
                TurnMax: 0),
            new SwShMoveSecondaryEffects(
                Inflict: 1,
                InflictPercent: 10,
                RawInflictCount: 3,
                Flinch: 20,
                EffectSequence: 77,
                Recoil: -25,
                RawHealing: -50),
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
                Metronome: true));
    }
}
