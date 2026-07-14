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
        var typeField = Assert.Single(
            workflow.EditableFields,
            field => field.Field == SwShMovesWorkflowService.TypeField);
        Assert.Contains(typeField.Options, option => option.Value == 0 && option.Label == "000 Normal");
        var categoryField = Assert.Single(
            workflow.EditableFields,
            field => field.Field == SwShMovesWorkflowService.CategoryField);
        Assert.Contains(categoryField.Options, option => option.Value == 1 && option.Label == "001 Physical");
        var targetField = Assert.Single(
            workflow.EditableFields,
            field => field.Field == SwShMovesWorkflowService.TargetField);
        Assert.Contains(targetField.Options, option => option.Value == 3 && option.Label == "003 Opponent");
        var inflictField = Assert.Single(
            workflow.EditableFields,
            field => field.Field == SwShMovesWorkflowService.InflictField);
        Assert.Contains(inflictField.Options, option => option.Value == 1 && option.Label == "001 Paralyze");
        Assert.Contains(
            inflictField.Options,
            option => option.Value == ushort.MaxValue
                && option.Label == "65535 Move-defined / scripted effect");
        var inflictDurationField = Assert.Single(
            workflow.EditableFields,
            field => field.Field == SwShMovesWorkflowService.RawInflictCountField);
        Assert.Equal("Inflict duration", inflictDurationField.Label);
        Assert.Contains(
            inflictDurationField.Options,
            option => option.Value == 1 && option.Label == "001 Permanent");
        var healingField = Assert.Single(
            workflow.EditableFields,
            field => field.Field == SwShMovesWorkflowService.RawHealingField);
        Assert.Equal("HP recovery (+) / HP cost (-) (%) (raw)", healingField.Label);
        Assert.Equal(-128, healingField.MinimumValue);
        Assert.Equal(127, healingField.MaximumValue);
        Assert.Empty(healingField.Options);
        var qualityField = Assert.Single(
            workflow.EditableFields,
            field => field.Field == SwShMovesWorkflowService.QualityField);
        Assert.Equal(0, qualityField.MinimumValue);
        Assert.Equal(13, qualityField.MaximumValue);
        Assert.Contains(qualityField.Options, option => option.Value == 8 && option.Label == "008 Damage Drain");
        Assert.Equal(
            "Stat Change 1: Stat",
            workflow.EditableFields.Single(field => field.Field == SwShMovesWorkflowService.Stat1Field).Label);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadMapsRawStatEightToAllStats()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBaseMoves(temp, CreateMoveRecord(stat1: 8), moveName: "Ancient Power");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShMovesWorkflowService().Load(project);

        var move = Assert.Single(workflow.Moves);
        Assert.Equal("Ancient Power", move.Name);
        Assert.Equal(8, move.StatChanges[0].Stat);
        Assert.Equal("All Stats", move.StatChanges[0].StatName);
        var statField = workflow.EditableFields.Single(field =>
            field.Field == SwShMovesWorkflowService.Stat1Field);
        Assert.Contains(statField.Options, option => option.Value == 8 && option.Label == "008 All Stats");
        Assert.DoesNotContain(statField.Options, option => option.Value == 9);
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
    public void LoadReadsRealSwordShieldWazabinMoveFiles()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza0033.wazabin",
            SwShMoveDataFile.Write(CreateMoveRecord()));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedTextTable((33, "Tackle")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazainfo.dat",
            CreateIndexedTextTable((33, "A physical attack in which the user charges and slams into the target.")));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShMovesWorkflowService().Load(project);

        var move = Assert.Single(workflow.Moves);
        Assert.Equal(33, move.MoveId);
        Assert.Equal("romfs/bin/pml/waza/waza0033.wazabin", move.Provenance.SourceFile);
        Assert.DoesNotContain(
            workflow.Diagnostics,
            diagnostic => diagnostic.Message.Contains("Moves data is not available", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadFillsMissingAndBlankLocalizedTypeNamesFromSwShDefaults()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBaseMoves(temp);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/typename.dat",
            CreateTextTable("Normal Localized", string.Empty));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShMovesWorkflowService().Load(project);

        Assert.Equal("Normal Localized", Assert.Single(workflow.Moves).TypeName);
        var options = workflow.EditableFields
            .Single(field => field.Field == SwShMovesWorkflowService.TypeField)
            .Options;
        Assert.Equal(18, options.Count);
        Assert.Equal("000 Normal Localized", options[0].Label);
        Assert.Equal("001 Fighting", options[1].Label);
        Assert.Equal("017 Fairy", options[17].Label);
    }

    [Fact]
    public void LoadIgnoresLocalizedTypeNamesBeyondSwShDomain()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBaseMoves(temp, CreateMoveRecord(type: 17));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/typename.dat",
            CreateTextTable(
                "Normal Localized",
                "Fighting Localized",
                "Flying Localized",
                "Poison Localized",
                "Ground Localized",
                "Rock Localized",
                "Bug Localized",
                "Ghost Localized",
                "Steel Localized",
                "Fire Localized",
                "Water Localized",
                "Grass Localized",
                "Electric Localized",
                "Psychic Localized",
                "Ice Localized",
                "Dragon Localized",
                "Dark Localized",
                "Fairy Localized",
                "Invalid Extra Type"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShMovesWorkflowService().Load(project);

        Assert.Equal("Fairy Localized", Assert.Single(workflow.Moves).TypeName);
        var options = workflow.EditableFields
            .Single(field => field.Field == SwShMovesWorkflowService.TypeField)
            .Options;
        Assert.Equal(18, options.Count);
        Assert.DoesNotContain(options, option => option.Value >= 18);
        Assert.DoesNotContain(options, option => option.Label.Contains("Invalid Extra", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadDeduplicatesMoveIdsAndPrefersWazabinSources()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza_033.bin",
            SwShMoveDataFile.Write(CreateMoveRecord(power: 40)));
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza0033.wazabin",
            SwShMoveDataFile.Write(CreateMoveRecord(power: 80)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedTextTable((33, "Tackle")));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShMovesWorkflowService().Load(project);

        var move = Assert.Single(workflow.Moves);
        Assert.Equal(33, move.MoveId);
        Assert.Equal(80, move.Power);
        Assert.Equal("romfs/bin/pml/waza/waza0033.wazabin", move.Provenance.SourceFile);
        Assert.Equal(1, workflow.Stats.TotalMoveCount);
    }

    [Fact]
    public void LoadPrefersCanonicalWazabinForEmbeddedMoveId()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza0000.wazabin",
            SwShMoveDataFile.Write(CreateMoveRecord(power: 40, moveId: 1)));
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza0001.wazabin",
            SwShMoveDataFile.Write(CreateMoveRecord(power: 80, moveId: 1)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedTextTable((1, "Pound")));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var move = Assert.Single(new SwShMovesWorkflowService().Load(project).Moves);

        Assert.Equal(80, move.Power);
        Assert.Equal("romfs/bin/pml/waza/waza0001.wazabin", move.Provenance.SourceFile);
    }

    [Fact]
    public void LoadReportsAndSkipsMoveIdsOutsideEditorRange()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza9999.wazabin",
            SwShMoveDataFile.Write(CreateMoveRecord(moveId: uint.MaxValue)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShMovesWorkflowService().Load(project);

        Assert.Empty(workflow.Moves);
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("unsupported move ID", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadHandlesMaximumSupportedSparseMoveIdWithoutIndexedAllocation()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza2147483647.wazabin",
            SwShMoveDataFile.Write(CreateMoveRecord(moveId: int.MaxValue)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShMovesWorkflowService().Load(project);

        var move = Assert.Single(workflow.Moves);
        Assert.Equal(int.MaxValue, move.MoveId);
        Assert.Equal("Move 2147483647", move.Name);
        Assert.Equal(
            "romfs/bin/pml/waza/waza2147483647.wazabin",
            move.Provenance.SourceFile);
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

    internal static void WriteBaseMoves(
        TemporarySwShProject temp,
        SwShMoveDataRecord? record = null,
        string moveName = "Tackle")
    {
        record ??= CreateMoveRecord();
        var moveId = checked((int)record.MoveId);
        temp.WriteBaseRomFsFile(
            $"bin/pml/waza/waza_{moveId:000}.bin",
            SwShMoveDataFile.Write(record));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedTextTable((moveId, moveName)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazainfo.dat",
            CreateIndexedTextTable((moveId, "A physical attack in which the user charges and slams into the target.")));
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

    internal static SwShMoveDataRecord CreateMoveRecord(
        byte power = 40,
        uint moveId = 33,
        bool canUseMove = true,
        byte type = 0,
        byte stat1 = 1,
        byte hitMin = 1,
        byte hitMax = 1,
        byte turnMin = 0,
        byte turnMax = 0)
    {
        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: moveId,
            CanUseMove: canUseMove,
            new SwShMoveCoreStats(
                Type: type,
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
                HitMin: hitMin,
                HitMax: hitMax,
                TurnMin: turnMin,
                TurnMax: turnMax),
            new SwShMoveSecondaryEffects(
                Inflict: 1,
                InflictPercent: 10,
                RawInflictCount: 1,
                Flinch: 0,
                EffectSequence: 12,
                Recoil: -25,
                RawHealing: 0),
            [
                new SwShMoveStatChange(1, Stat: stat1, Stage: -1, Percent: 30),
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

    internal static byte[] CreateTextTable(params string[] lines)
    {
        return SwShGameTextFile.Write(
            lines.Select(line => new SwShGameTextLine(line, Flags: 0)).ToArray());
    }
}
