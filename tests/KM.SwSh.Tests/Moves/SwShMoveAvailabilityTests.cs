// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Moves;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Moves;

public sealed class SwShMoveAvailabilityTests
{
    [Fact]
    public void CreateMoveOptionsFiltersToMovesMarkedUsable()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza_001.bin",
            SwShMoveDataFile.Write(CreateMoveRecord(moveId: 1, canUseMove: true)));
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza_002.bin",
            SwShMoveDataFile.Write(CreateMoveRecord(moveId: 2, canUseMove: false)));
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);
        var options = SwShMoveAvailability.CreateMoveOptions(
            ["", "Scratch", "Illegal Move"],
            usableMoveIds,
            (value, label) => new TestOption(value, label));
        var optionalOptions = SwShMoveAvailability.CreateMoveOptions(
            ["", "Scratch", "Illegal Move"],
            usableMoveIds,
            (value, label) => new TestOption(value, label),
            includeNone: true);

        Assert.Contains(options, option => option.Value == 1 && option.Label == "001 Scratch");
        Assert.DoesNotContain(options, option => option.Value == 0);
        Assert.DoesNotContain(options, option => option.Value == 2);
        Assert.Contains(optionalOptions, option => option.Value == 0 && option.Label == "000 None");
    }

    [Fact]
    public void LoadUsableMoveIdsPrefersCanonicalWazabinForEmbeddedMoveId()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza0000.wazabin",
            SwShMoveDataFile.Write(CreateMoveRecord(moveId: 1, canUseMove: false)));
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza0001.wazabin",
            SwShMoveDataFile.Write(CreateMoveRecord(moveId: 1, canUseMove: true)));
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var usableMoveIds = SwShMoveAvailability.LoadUsableMoveIds(project);

        Assert.Contains(1, usableMoveIds);
    }

    [Fact]
    public void CreateMoveOptionsHandlesSparseMaximumIntegerMoveId()
    {
        IReadOnlySet<int> usableMoveIds = new HashSet<int> { int.MaxValue };

        var options = SwShMoveAvailability.CreateMoveOptions(
            ["", "Named But Filtered Move"],
            usableMoveIds,
            (value, label) => new TestOption(value, label),
            includeNone: true);

        Assert.Collection(
            options,
            option =>
            {
                Assert.Equal(0, option.Value);
                Assert.Equal("000 None", option.Label);
            },
            option =>
            {
                Assert.Equal(int.MaxValue, option.Value);
                Assert.Equal("2147483647 Move 2147483647", option.Label);
            });
    }

    [Fact]
    public void CreateMoveOptionsUsesAllNamedIndicesWhenNoAvailabilityFilterExists()
    {
        var options = SwShMoveAvailability.CreateMoveOptions(
            ["", "Scratch", "Growl"],
            new HashSet<int>(),
            (value, label) => new TestOption(value, label));

        Assert.Equal([1, 2], options.Select(option => option.Value));
        Assert.Equal(["001 Scratch", "002 Growl"], options.Select(option => option.Label));
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

    private sealed record TestOption(int Value, string Label);
}
