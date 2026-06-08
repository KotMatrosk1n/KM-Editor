// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using Xunit;

namespace KM.SwSh.Tests.Pokemon;

public sealed class SwShPokemonEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldOverlaysPendingPersonalEdit()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Equal(99, pokemon.BaseStats.HP);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal(SwShPokemonWorkflowService.HPField, edit.Field);
        Assert.Equal("99", edit.NewValue);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateFieldOverlaysBooleanPersonalFlag()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.CanNotDynamaxField,
            "true");

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.True(pokemon.Personal.CanNotDynamax);
        Assert.Equal("1", Assert.Single(result.Session.PendingEdits).NewValue);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateFieldReplacesPendingEditForSamePokemonAndField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "90");

        var second = service.UpdateField(
            temp.Paths,
            first.Session,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "91");

        var edit = Assert.Single(second.Session.PendingEdits);
        Assert.Equal("91", edit.NewValue);
        Assert.Equal(91, second.Workflow.Pokemon.Single(record => record.PersonalId == 1).BaseStats.HP);
    }

    [Fact]
    public void CreateChangePlanTargetsPersonalTable()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.Type1Field,
            "9");

        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.True(plan.CanApply);
        var write = Assert.Single(plan.Writes);
        Assert.Equal(SwShPokemonWorkflowService.PersonalDataPath, write.TargetRelativePath);
        Assert.Contains(write.Sources, source => source.Layer == ProjectFileLayer.Base);
    }

    [Fact]
    public void ApplyChangePlanWritesOutputPersonalTableAndLeavesBaseUntouched()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var hpUpdate = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "100");
        var flagUpdate = service.UpdateField(
            temp.Paths,
            hpUpdate.Session,
            personalId: 1,
            SwShPokemonWorkflowService.CanNotDynamaxField,
            "true");
        var plan = service.CreateChangePlan(temp.Paths, flagUpdate.Session);

        var apply = service.ApplyChangePlan(temp.Paths, flagUpdate.Session, plan);

        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShPokemonWorkflowService.PersonalDataPath);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar)));
        var outputRecord = SwShPersonalTable.Parse(outputBytes).Records[1];
        Assert.Equal(100, outputRecord.HP);
        Assert.True(outputRecord.CanNotDynamax);
        var baseBytes = File.ReadAllBytes(Path.Combine(
            temp.BaseRomFsPath,
            "bin/pml/personal/personal_total.bin"));
        Assert.Equal(45, SwShPersonalTable.Parse(baseBytes).Records[1].HP);
    }

    [Fact]
    public void UpdateFieldRejectsOutOfRangeValue()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.Type1Field,
            "18");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldRequiresEditableProjectPaths()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths with { OutputRootPath = null },
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static TemporaryPokemonProject CreateEditableProject()
    {
        var temp = TemporaryPokemonProject.Create();
        SwShPokemonWorkflowServiceTests.WriteBasePokemonData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        return temp;
    }
}
