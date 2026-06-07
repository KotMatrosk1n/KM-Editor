// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Core.Diagnostics;
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
        Assert.Equal("Species 6", updatedTable.Slots[0].Species);
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
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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
        var update = service.UpdateSlotField(
            temp.Paths,
            session: null,
            table.TableId,
            slot: 2,
            field: "probability",
            value: "40");

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
        Assert.Equal(40, outputArchive.Tables[0].SubTables[0].Slots[1].Probability);
        var hiddenArchive = SwShWildEncounterArchive.Parse(outputPack.GetFileByName("encount_k.bin"));
        Assert.Equal(15, hiddenArchive.Tables[0].SubTables[0].Slots[1].Probability);
    }
}
