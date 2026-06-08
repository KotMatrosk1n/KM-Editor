// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Formats.SwSh;
using KM.SwSh.Rentals;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Rentals;

public sealed class SwShRentalPokemonEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldCreatesPendingRentalPokemonIvEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvAttackField,
            value: "12");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.rentalPokemon", edit.Domain);
        Assert.Equal(SwShRentalPokemonWorkflowService.IvAttackField, edit.Field);
        Assert.Equal("rental:0", edit.RecordId);
        Assert.Equal("12", edit.NewValue);
        Assert.Equal(12, result.Workflow.Rentals[0].Ivs.Attack);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredRentalPokemonFixedIvsAndMoves()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var update = service.UpdateField(temp.Paths, null, 0, SwShRentalPokemonWorkflowService.IvHpField, "0");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvAttackField, "1");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvDefenseField, "2");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvSpeedField, "3");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvSpecialAttackField, "4");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvSpecialDefenseField, "5");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.EvHpField, "252");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.Move2Field, "4");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShRentalPokemonWorkflowService.RentalPokemonDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShRentalPokemonWorkflowService.RentalPokemonDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Equal(new SwShRentalPokemonStats(0, 1, 2, 4, 5, 3), output.Rentals[0].Ivs);
        Assert.Equal(252, output.Rentals[0].Evs.HP);
        Assert.Equal(4, output.Rentals[0].Moves[2]);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesPerfectIvPreset()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 1,
            field: SwShRentalPokemonWorkflowService.FixedIvPresetField,
            value: "31");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        _ = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Equal(new SwShRentalPokemonStats(31, 31, 31, 31, 31, 31), output.Rentals[1].Ivs);
    }

    [Fact]
    public void ValidateRejectsUnsupportedRentalPokemonIvSentinel()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();
        var session = EditSession.Start() with
        {
            PendingEdits =
            [
                new PendingEdit(
                    "workflow.rentalPokemon",
                    "Set unsupported IV sentinel.",
                    [],
                    RecordId: "rental:0",
                    Field: SwShRentalPokemonWorkflowService.IvAttackField,
                    NewValue: "-1")
            ],
        };

        var validation = service.Validate(temp.Paths, session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Field == SwShRentalPokemonWorkflowService.IvAttackField);
    }

    private static string GetOutputRentalPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "script_event_data",
            "rental.bin");
    }
}
