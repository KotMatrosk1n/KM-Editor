// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Moves;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Moves;

public sealed class SwShMovesWorkflowServiceTests
{
    [Fact]
    public void LoadReadsMovesFromRealMoveDataAndTextTables()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBaseMoves(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShMovesWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var move = Assert.Single(workflow.Moves);
        Assert.Equal(33, move.MoveId);
        Assert.Equal("Tackle", move.Name);
        Assert.Equal("A physical attack in which the user charges and slams into the target.", move.Description);
        Assert.True(move.CanUseMove);
        Assert.Equal(0, move.Type);
        Assert.Equal("Normal", move.TypeName);
        Assert.Equal(1, move.Category);
        Assert.Equal("Physical", move.CategoryName);
        Assert.Equal(40, move.Power);
        Assert.Equal(100, move.Accuracy);
        Assert.Equal(35, move.PP);
        Assert.Equal(3, move.Target);
        Assert.Equal("Opponent", move.TargetName);
        Assert.Equal(1, move.Inflict);
        Assert.Equal("Paralyze", move.InflictName);
        Assert.Equal(10, move.InflictPercent);
        Assert.Equal(-25, move.Recoil);
        Assert.Collection(
            move.StatChanges,
            stat =>
            {
                Assert.Equal(1, stat.Slot);
                Assert.Equal("Attack", stat.StatName);
                Assert.Equal(-1, stat.Stage);
            },
            stat =>
            {
                Assert.Equal(2, stat.Slot);
                Assert.Equal("Defense", stat.StatName);
                Assert.Equal(1, stat.Stage);
            },
            stat =>
            {
                Assert.Equal(3, stat.Slot);
                Assert.Equal("None", stat.StatName);
            });
        Assert.Contains(move.Flags, flag => flag.Field == "makesContact" && flag.Enabled);
        Assert.Contains(move.Flags, flag => flag.Field == "protect" && flag.Enabled);
        Assert.Contains(move.Flags, flag => flag.Field == "punch" && flag.Enabled);
        Assert.Equal(ProjectFileLayer.Base, move.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, move.Provenance.FileState);
        Assert.Equal("romfs/bin/pml/waza/waza_033.bin", move.Provenance.SourceFile);
        Assert.Equal(1, workflow.Stats.TotalMoveCount);
        Assert.Equal(1, workflow.Stats.EnabledMoveCount);
        Assert.Equal(4, workflow.Stats.SourceFileCount);
        Assert.Equal(3, workflow.Stats.ActiveFlagCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadPrefersLayeredMoveDataWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBaseMoves(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteOutputFile(
            "romfs/bin/pml/waza/waza_033.bin",
            SwShMoveDataFile.Write(CreateMoveRecord(power: 80)));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShMovesWorkflowService().Load(project);

        var move = Assert.Single(workflow.Moves);
        Assert.Equal(80, move.Power);
        Assert.Equal(ProjectFileLayer.Layered, move.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, move.Provenance.FileState);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenMoveDataIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShMovesWorkflowService().Load(project);

        Assert.Empty(workflow.Moves);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.moves");
    }

    internal static void WriteBaseMoves(TemporarySwShProject temp)
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

    private static SwShMoveDataRecord CreateMoveRecord(byte power = 40)
    {
        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: 33,
            CanUseMove: true,
            new SwShMoveCoreStats(
                Type: 0,
                Quality: 2,
                Category: 1,
                Power: power,
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
