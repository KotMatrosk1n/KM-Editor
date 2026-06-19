// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Encounters;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Encounters;

public sealed class SwShEncountersWorkflowServiceTests
{
    [Fact]
    public void LoadReadsEncounterTablesFromRealWildDataPack()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Tables.Count);
        var table = workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        Assert.StartsWith("sword:symbol:0:", table.TableId, StringComparison.Ordinal);
        Assert.Equal($"Zone 0x{SwShEncounterTestFixtures.ZoneId:X16}", table.Location);
        Assert.Equal("Symbol", table.Area);
        Assert.Equal("Normal", table.EncounterType);
        Assert.Equal("Sword", table.GameVersion);
        Assert.Equal("encount_symbol_k.bin", table.ArchiveMember);
        Assert.Equal(2, table.Slots.Count);
        Assert.Equal(1, table.Slots[0].SpeciesId);
        Assert.Equal("Bulbasaur", table.Slots[0].Species);
        Assert.Equal(0, table.Slots[0].Form);
        Assert.Equal(3, table.Slots[0].LevelMin);
        Assert.Equal(8, table.Slots[0].LevelMax);
        Assert.Equal(35, table.Slots[0].Weight);
        Assert.Equal(ProjectFileLayer.Base, table.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, table.Provenance.FileState);
        Assert.Equal(2, workflow.Stats.TotalTableCount);
        Assert.Equal(4, workflow.Stats.TotalSlotCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Contains(workflow.EditableFields, field => field.Field == "speciesId");
    }

    [Fact]
    public void LoadFormatsRegionalEncounterSlotSpeciesNames()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    "encount_symbol_k.bin",
                    SwShEncounterTestFixtures.CreateArchive(firstSlotSpecies: 80, firstSlotForm: 2).Write()),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateSpeciesNameTable(80, (80, "Slowbro")));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        var table = Assert.Single(workflow.Tables);
        Assert.Equal(80, table.Slots[0].SpeciesId);
        Assert.Equal(2, table.Slots[0].Form);
        Assert.Equal("Slowbro (Galarian)", table.Slots[0].Species);
    }

    [Fact]
    public void LoadFormatsKnownEncounterZoneNames()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    "encount_symbol_k.bin",
                    SwShEncounterTestFixtures.CreateArchive(zoneId: 0x078BC1FF1A657844).Write()),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            SwShGameTextFile.Write(
            [
                new SwShGameTextLine("", Flags: 0),
                new SwShGameTextLine("Bulbasaur", Flags: 0),
                new SwShGameTextLine("Ivysaur", Flags: 0),
                new SwShGameTextLine("Venusaur", Flags: 0),
                new SwShGameTextLine("Charmander", Flags: 0),
            ]));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        var table = Assert.Single(workflow.Tables);
        Assert.Equal("Route 1", table.Location);
        Assert.Contains(":078BC1FF1A657844:", table.TableId, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFiltersEncounterArchivesToSelectedGame()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile("encount_symbol_k.bin", SwShEncounterTestFixtures.CreateArchive().Write()),
                new SwShGfPackNamedFile("encount_k.bin", SwShEncounterTestFixtures.CreateArchive(speciesOffset: 2).Write()),
                new SwShGfPackNamedFile("encount_symbol_t.bin", SwShEncounterTestFixtures.CreateArchive(speciesOffset: 4).Write()),
                new SwShGfPackNamedFile("encount_t.bin", SwShEncounterTestFixtures.CreateArchive(speciesOffset: 6).Write()),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            SwShGameTextFile.Write(
            [
                new SwShGameTextLine("", Flags: 0),
                new SwShGameTextLine("Bulbasaur", Flags: 0),
                new SwShGameTextLine("Ivysaur", Flags: 0),
                new SwShGameTextLine("Venusaur", Flags: 0),
                new SwShGameTextLine("Charmander", Flags: 0),
                new SwShGameTextLine("Charmeleon", Flags: 0),
                new SwShGameTextLine("Charizard", Flags: 0),
                new SwShGameTextLine("Squirtle", Flags: 0),
                new SwShGameTextLine("Wartortle", Flags: 0),
                new SwShGameTextLine("Blastoise", Flags: 0),
            ]));
        temp.WriteBaseExeFsFile("main", "base-main");
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Shield);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Shield };
        var project = new ProjectWorkspaceService().Open(paths);

        var workflow = new SwShEncountersWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.Available, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Tables.Count);
        Assert.All(workflow.Tables, table => Assert.Equal("Shield", table.GameVersion));
        Assert.Contains(workflow.Tables, table =>
            table.ArchiveMember == "encount_symbol_t.bin" && table.Area == "Symbol");
        Assert.Contains(workflow.Tables, table =>
            table.ArchiveMember == "encount_t.bin" && table.Area == "Hidden");
        Assert.DoesNotContain(workflow.Tables, table => table.ArchiveMember.EndsWith("_k.bin", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ProjectGame.Sword, "encount_symbol_k.bin")]
    [InlineData(ProjectGame.Shield, "encount_symbol_t.bin")]
    public void LoadSkipsVanillaEmptyEncounterSubTables(ProjectGame game, string archiveMember)
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    archiveMember,
                    SwShEncounterTestFixtures.CreateArchive(subTables: CreateNormalFishingAndTreeSubTables()).Write()),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            SwShGameTextFile.Write(
            [
                new SwShGameTextLine("", Flags: 0),
                new SwShGameTextLine("Bulbasaur", Flags: 0),
                new SwShGameTextLine("Ivysaur", Flags: 0),
                new SwShGameTextLine("Venusaur", Flags: 0),
                new SwShGameTextLine("Charmander", Flags: 0),
            ]));
        temp.WriteBaseExeFsFile("main", "base-main");
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, game);
        var project = new ProjectWorkspaceService().Open(temp.Paths with { SelectedGame = game });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        Assert.Contains(workflow.Tables, table => table.EncounterType == "Normal");
        Assert.DoesNotContain(workflow.Tables, table => table.EncounterType == "Fishing");
        Assert.DoesNotContain(workflow.Tables, table => table.EncounterType == "Shaking Trees");
    }

    [Fact]
    public void LoadUsesBaseVanillaSubTablesWhenLayeredArchiveAddsImpossibleEncounters()
    {
        using var temp = TemporarySwShProject.Create();
        var baseArchive = SwShEncounterTestFixtures.CreateArchive(
            subTables: CreateNormalFishingAndTreeSubTables());
        var layeredArchive = SwShEncounterTestFixtures.CreateArchive(
            subTables: CreateNormalFishingAndTreeSubTables(
                fishing: CreateSubTable(5, 12, speciesOffset: 4),
                shakingTrees: CreateSubTable(5, 12, speciesOffset: 8)));
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile("encount_symbol_k.bin", baseArchive.Write()),
            ]).Write());
        temp.WriteOutputFile(
            "romfs/bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile("encount_symbol_k.bin", layeredArchive.Write()),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            SwShGameTextFile.Write(
            [
                new SwShGameTextLine("", Flags: 0),
                new SwShGameTextLine("Bulbasaur", Flags: 0),
                new SwShGameTextLine("Ivysaur", Flags: 0),
                new SwShGameTextLine("Venusaur", Flags: 0),
                new SwShGameTextLine("Charmander", Flags: 0),
            ]));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShEncountersWorkflowService().Load(project);

        var normal = Assert.Single(workflow.Tables);
        Assert.Equal("Normal", normal.EncounterType);
        Assert.Equal(ProjectFileLayer.Layered, normal.Provenance.SourceLayer);
        Assert.DoesNotContain(workflow.Tables, table => table.EncounterType == "Fishing");
        Assert.DoesNotContain(workflow.Tables, table => table.EncounterType == "Shaking Trees");
    }

    [Fact]
    public void LoadExposesSurfingAndFlyingSymbolZonesAsEditableTables()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    "encount_symbol_t.bin",
                    SwShEncounterTestFixtures.CreateArchiveForZones(
                        SwShEncounterTestFixtures.BallimereLakeSurfingZoneId,
                        SwShEncounterTestFixtures.BridgeFieldFlyingZoneId).Write()),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            SwShGameTextFile.Write(
            [
                new SwShGameTextLine("", Flags: 0),
                new SwShGameTextLine("Bulbasaur", Flags: 0),
                new SwShGameTextLine("Ivysaur", Flags: 0),
                new SwShGameTextLine("Venusaur", Flags: 0),
                new SwShGameTextLine("Charmander", Flags: 0),
                new SwShGameTextLine("Charmeleon", Flags: 0),
                new SwShGameTextLine("Charizard", Flags: 0),
            ]));
        temp.WriteBaseExeFsFile("main", "base-main");
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Shield);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Shield };
        var project = new ProjectWorkspaceService().Open(paths);

        var workflow = new SwShEncountersWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.Available, workflow.Summary.Availability);
        var surfingTable = Assert.Single(workflow.Tables, table => table.Location == "Ballimere Lake (Surfing)");
        var flyingTable = Assert.Single(workflow.Tables, table => table.Location == "Bridge Field (Flying)");
        Assert.Equal("Symbol", surfingTable.Area);
        Assert.Equal("Symbol", flyingTable.Area);
        Assert.Equal("encount_symbol_t.bin", surfingTable.ArchiveMember);
        Assert.NotEmpty(surfingTable.Slots);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShEncountersWorkflowService.SpeciesIdField);

        var update = new SwShEncountersEditSessionService().UpdateSlotField(
            paths,
            session: null,
            surfingTable.TableId,
            slot: 1,
            field: SwShEncountersWorkflowService.SpeciesIdField,
            value: "6");

        Assert.DoesNotContain(update.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(
            6,
            update.Workflow.Tables.Single(table => table.TableId == surfingTable.TableId).Slots[0].SpeciesId);
    }

    [Fact]
    public void LoadFormatsSpeciesZeroAsEmpty()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    "encount_symbol_k.bin",
                    SwShEncounterTestFixtures.CreateArchive(
                        firstSlotSpecies: 0,
                        firstSlotProbability: 0,
                        secondSlotProbability: 100).Write()),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            SwShGameTextFile.Write(
            [
                new SwShGameTextLine("Egg", Flags: 0),
                new SwShGameTextLine("Bulbasaur", Flags: 0),
                new SwShGameTextLine("Ivysaur", Flags: 0),
                new SwShGameTextLine("Venusaur", Flags: 0),
                new SwShGameTextLine("Charmander", Flags: 0),
            ]));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        var table = Assert.Single(workflow.Tables);
        Assert.Equal(0, table.Slots[0].SpeciesId);
        Assert.Equal("Empty", table.Slots[0].Species);
        var speciesField = workflow.EditableFields.Single(field =>
            field.Field == SwShEncountersWorkflowService.SpeciesIdField);
        Assert.Contains(speciesField.Options, option => option.Value == 0 && option.Label == "000 Empty");
        Assert.DoesNotContain(
            speciesField.Options,
            option => option.Value == 0 && option.Label.Contains("Egg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenWildDataPackIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/encounters.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.encounters");
    }

    [Fact]
    public void LoadReportsUnsupportedWildDataPack()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("bin/archive/field/resident/data_table.gfpak", "not-a-pack");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.encounters");
    }

    private static IReadOnlyList<SwShWildEncounterSubTable> CreateNormalFishingAndTreeSubTables(
        SwShWildEncounterSubTable? fishing = null,
        SwShWildEncounterSubTable? shakingTrees = null)
    {
        return Enumerable.Range(0, 11)
            .Select(index => index switch
            {
                0 => CreateSubTable(3, 8),
                9 => shakingTrees ?? CreateEmptySubTable(),
                10 => fishing ?? CreateEmptySubTable(),
                _ => CreateEmptySubTable(),
            })
            .ToArray();
    }

    private static SwShWildEncounterSubTable CreateSubTable(
        byte levelMin,
        byte levelMax,
        int speciesOffset = 0)
    {
        return new SwShWildEncounterSubTable(
            levelMin,
            levelMax,
            [
                new SwShWildEncounterSlot(35, 1 + speciesOffset, 0),
                new SwShWildEncounterSlot(65, 4 + speciesOffset, 1),
            ]);
    }

    private static SwShWildEncounterSubTable CreateEmptySubTable()
    {
        return new SwShWildEncounterSubTable(
            0,
            0,
            Enumerable.Range(0, 10)
                .Select(_ => new SwShWildEncounterSlot(0, 0, 0))
                .ToArray());
    }

    private static byte[] CreateSpeciesNameTable(int highestIndex, params (int Index, string Name)[] replacements)
    {
        var names = Enumerable.Range(0, highestIndex + 1)
            .Select(_ => new SwShGameTextLine("", Flags: 0))
            .ToArray();

        foreach (var (index, name) in replacements)
        {
            names[index] = new SwShGameTextLine(name, Flags: 0);
        }

        return SwShGameTextFile.Write(names);
    }
}
