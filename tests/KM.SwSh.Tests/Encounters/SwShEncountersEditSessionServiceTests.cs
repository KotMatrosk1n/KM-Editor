// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Formats.SwSh;
using KM.SwSh.Encounters;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Encounters;

public sealed class SwShEncountersEditSessionServiceTests
{
    [Fact]
    public void UpdateSlotFieldReturnsPendingEditAndOverlay()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");

        var result = new SwShEncountersEditSessionService().UpdateSlotField(
            temp.Paths,
            session: null,
            table.TableId,
            slot: 1,
            field: "speciesId",
            value: "6");

        Assert.True(result.Session.HasPendingChanges);
        Assert.Equal("workflow.encounters", Assert.Single(result.Session.PendingEdits).Domain);
        var updatedTable = result.Workflow.Tables.First(candidate => candidate.TableId == table.TableId);
        Assert.Equal(6, updatedTable.Slots[0].SpeciesId);
        Assert.Equal("Charizard", updatedTable.Slots[0].Species);
    }

    [Fact]
    public void UpdateSlotFieldFormatsRegionalSpeciesAfterFormChange()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    "encount_symbol_k.bin",
                    SwShEncounterTestFixtures.CreateArchive(firstSlotSpecies: 80).Write()),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateSpeciesNameTable(80, (80, "Slowbro")));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = Assert.Single(workflow.Tables);

        var result = new SwShEncountersEditSessionService().UpdateSlotField(
            temp.Paths,
            session: null,
            table.TableId,
            slot: 1,
            field: SwShEncountersWorkflowService.FormField,
            value: "2");

        var updatedTable = Assert.Single(result.Workflow.Tables);
        Assert.Equal(80, updatedTable.Slots[0].SpeciesId);
        Assert.Equal(2, updatedTable.Slots[0].Form);
        Assert.Equal("Slowbro (Galarian)", updatedTable.Slots[0].Species);
    }

    [Fact]
    public void ValidateRejectsLevelRangeCrossing()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        var service = new SwShEncountersEditSessionService();

        var result = service.UpdateSlotField(
            temp.Paths,
            session: null,
            table.TableId,
            slot: 1,
            field: "levelMin",
            value: "9");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Zone", StringComparison.Ordinal)
            && diagnostic.Message.Contains("Symbol Normal slot 1", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateLevelPairDiagnosticsNameEncounterTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        var source = new ProjectFileReference(ProjectFileLayer.Base, table.Provenance.SourceFile);
        var session = EditSession.Start()
            .WithPendingEdit(new PendingEdit(
                "workflow.encounters",
                "Set invalid minimum level.",
                [source],
                SwShEncountersWorkflowService.CreateSlotRecordId(table.TableId, 1),
                SwShEncountersWorkflowService.LevelMinField,
                "9"))
            .WithPendingEdit(new PendingEdit(
                "workflow.encounters",
                "Set invalid maximum level.",
                [source],
                SwShEncountersWorkflowService.CreateSlotRecordId(table.TableId, 1),
                SwShEncountersWorkflowService.LevelMaxField,
                "8"));
        var service = new SwShEncountersEditSessionService();

        var validation = service.Validate(temp.Paths, session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Zone", StringComparison.Ordinal)
            && diagnostic.Message.Contains("Symbol Normal", StringComparison.Ordinal)
            && diagnostic.Message.Contains("minimum level 9 greater than maximum level 8", StringComparison.Ordinal)
            && !diagnostic.Message.Contains(table.TableId, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateFieldValueDiagnosticsNameEncounterSlot()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        var source = new ProjectFileReference(ProjectFileLayer.Base, table.Provenance.SourceFile);
        var session = EditSession.Start()
            .WithPendingEdit(new PendingEdit(
                "workflow.encounters",
                "Set invalid species.",
                [source],
                SwShEncountersWorkflowService.CreateSlotRecordId(table.TableId, 1),
                SwShEncountersWorkflowService.SpeciesIdField,
                "99999"));
        var service = new SwShEncountersEditSessionService();

        var validation = service.Validate(temp.Paths, session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Zone", StringComparison.Ordinal)
            && diagnostic.Message.Contains("Symbol Normal slot 1", StringComparison.Ordinal)
            && diagnostic.Message.Contains("speciesId must be between", StringComparison.Ordinal)
            && !diagnostic.Message.Contains(table.TableId, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsProbabilityTotalsOutsideOneHundred()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        var service = new SwShEncountersEditSessionService();
        var result = service.UpdateSlotField(
            temp.Paths,
            session: null,
            table.TableId,
            slot: 2,
            field: "probability",
            value: "40");

        var validation = service.Validate(temp.Paths, result.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("must total 100", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsEmptySpeciesWithPositiveProbability()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        var service = new SwShEncountersEditSessionService();

        var result = service.UpdateSlotField(
            temp.Paths,
            session: null,
            table.TableId,
            slot: 1,
            field: "speciesId",
            value: "0");

        var validation = service.Validate(temp.Paths, result.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("empty but has", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateSlotFieldsStagesEmptySpeciesFormAndZeroProbabilityTogether()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        var service = new SwShEncountersEditSessionService();

        var result = service.UpdateSlotFields(
            temp.Paths,
            session: null,
            [
                new SwShEncounterSlotFieldUpdate(
                    table.TableId,
                    Slot: 2,
                    SwShEncountersWorkflowService.SpeciesIdField,
                    "0"),
                new SwShEncounterSlotFieldUpdate(
                    table.TableId,
                    Slot: 2,
                    SwShEncountersWorkflowService.FormField,
                    "0"),
                new SwShEncounterSlotFieldUpdate(
                    table.TableId,
                    Slot: 2,
                    SwShEncountersWorkflowService.ProbabilityField,
                    "0"),
            ]);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(3, result.Session.PendingEdits.Count);
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShEncountersWorkflowService.SpeciesIdField && edit.NewValue == "0");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShEncountersWorkflowService.FormField && edit.NewValue == "0");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShEncountersWorkflowService.ProbabilityField && edit.NewValue == "0");
        var updatedTable = result.Workflow.Tables.First(candidate => candidate.TableId == table.TableId);
        var updatedSlot = updatedTable.Slots.Single(slot => slot.Slot == 2);
        Assert.Equal(0, updatedSlot.SpeciesId);
        Assert.Equal("Empty", updatedSlot.Species);
        Assert.Equal(0, updatedSlot.Form);
        Assert.Equal(0, updatedSlot.Weight);

        var validation = service.Validate(temp.Paths, result.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("must total 100", StringComparison.Ordinal));
        Assert.DoesNotContain(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("empty but has", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyChangePlanWritesEditedEncounterArchiveToOutputPack()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table => table.ArchiveMember == "encount_symbol_k.bin");
        var service = new SwShEncountersEditSessionService();
        var firstUpdate = service.UpdateSlotField(
            temp.Paths,
            session: null,
            table.TableId,
            slot: 1,
            field: "probability",
            value: "40");
        var update = service.UpdateSlotField(
            temp.Paths,
            firstUpdate.Session,
            table.TableId,
            slot: 2,
            field: "probability",
            value: "60");

        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "data_table.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputArchive = SwShWildEncounterArchive.Parse(outputPack.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal(40, outputArchive.Tables[0].SubTables[0].Slots[0].Probability);
        Assert.Equal(60, outputArchive.Tables[0].SubTables[0].Slots[1].Probability);
        var hiddenArchive = SwShWildEncounterArchive.Parse(outputPack.GetFileByName("encount_k.bin"));
        Assert.Equal(65, hiddenArchive.Tables[0].SubTables[0].Slots[1].Probability);
    }

    [Fact]
    public void LevelEditsApplyToAllSubTablesInSelectedZone()
    {
        using var temp = TemporarySwShProject.Create();
        SwShEncounterTestFixtures.WriteBaseEncounters(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var symbolSubTables = new[]
        {
            CreateSubTable(3, 8),
            CreateSubTable(4, 9),
            CreateSubTable(5, 10),
            CreateSubTable(6, 11),
        };
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    "encount_symbol_k.bin",
                    SwShEncounterTestFixtures.CreateArchive(subTables: symbolSubTables).Write()),
                new SwShGfPackNamedFile("encount_k.bin", SwShEncounterTestFixtures.CreateArchive(speciesOffset: 2).Write()),
            ]).Write());
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table =>
            table.ArchiveMember == "encount_symbol_k.bin" && table.EncounterType == "Normal");
        var service = new SwShEncountersEditSessionService();

        var update = service.UpdateSlotField(
            temp.Paths,
            session: null,
            table.TableId,
            slot: 1,
            field: "levelMin",
            value: "5");

        var symbolTables = update.Workflow.Tables
            .Where(candidate => candidate.ArchiveMember == "encount_symbol_k.bin")
            .ToArray();
        Assert.Equal(4, symbolTables.Length);
        Assert.All(symbolTables, candidate =>
            Assert.All(candidate.Slots, slot => Assert.Equal(5, slot.LevelMin)));

        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "data_table.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputSymbolArchive = SwShWildEncounterArchive.Parse(outputPack.GetFileByName("encount_symbol_k.bin"));
        Assert.All(outputSymbolArchive.Tables[0].SubTables, subTable => Assert.Equal(5, subTable.LevelMin));
        var outputHiddenArchive = SwShWildEncounterArchive.Parse(outputPack.GetFileByName("encount_k.bin"));
        Assert.Equal(3, outputHiddenArchive.Tables[0].SubTables[0].LevelMin);
    }

    [Fact]
    public void LevelEditsSkipVanillaEmptySubTablesInSelectedZone()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", "base-main");
        var symbolSubTables = new[]
        {
            CreateSubTable(3, 8),
            CreateEmptySubTable(levelMin: 4, levelMax: 9),
            CreateSubTable(5, 10),
        };
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    "encount_symbol_k.bin",
                    SwShEncounterTestFixtures.CreateArchive(subTables: symbolSubTables).Write()),
                new SwShGfPackNamedFile("encount_k.bin", SwShEncounterTestFixtures.CreateArchive(speciesOffset: 2).Write()),
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
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShEncountersWorkflowService().Load(project);
        var table = workflow.Tables.First(table =>
            table.ArchiveMember == "encount_symbol_k.bin" && table.EncounterType == "Normal");
        var service = new SwShEncountersEditSessionService();

        var update = service.UpdateSlotField(
            temp.Paths,
            session: null,
            table.TableId,
            slot: 1,
            field: "levelMin",
            value: "7");

        Assert.Equal(2, update.Workflow.Tables.Count(table => table.ArchiveMember == "encount_symbol_k.bin"));
        Assert.DoesNotContain(update.Workflow.Tables, candidate =>
            candidate.ArchiveMember == "encount_symbol_k.bin" && candidate.EncounterType == "Overcast");

        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "data_table.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputSymbolArchive = SwShWildEncounterArchive.Parse(outputPack.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal(7, outputSymbolArchive.Tables[0].SubTables[0].LevelMin);
        Assert.Equal(4, outputSymbolArchive.Tables[0].SubTables[1].LevelMin);
        Assert.Equal(7, outputSymbolArchive.Tables[0].SubTables[2].LevelMin);
    }

    private static SwShWildEncounterSubTable CreateSubTable(byte levelMin, byte levelMax)
    {
        return new SwShWildEncounterSubTable(
            levelMin,
            levelMax,
            [
                new SwShWildEncounterSlot(35, 1, 0),
                new SwShWildEncounterSlot(65, 4, 1),
            ]);
    }

    private static SwShWildEncounterSubTable CreateEmptySubTable(byte levelMin, byte levelMax)
    {
        return new SwShWildEncounterSubTable(
            levelMin,
            levelMax,
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
