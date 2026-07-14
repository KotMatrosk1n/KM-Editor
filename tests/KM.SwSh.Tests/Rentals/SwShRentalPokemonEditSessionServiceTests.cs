// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Pokemon;
using KM.SwSh.Rentals;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using System.Buffers.Binary;
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
            value: "80");
        result = service.UpdateField(
            temp.Paths,
            result.Session,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvDefenseField,
            value: "-50");
        result = service.UpdateField(
            temp.Paths,
            result.Session,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.EvHpField,
            value: "999");
        result = service.UpdateField(
            temp.Paths,
            result.Session,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.EvAttackField,
            value: "999");

        Assert.Equal(4, result.Session.PendingEdits.Count);
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Domain == "workflow.rentalPokemon"
            && edit.Field == SwShRentalPokemonWorkflowService.IvAttackField
            && edit.RecordId == "rental:0"
            && edit.NewValue == "31");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShRentalPokemonWorkflowService.IvDefenseField
            && edit.NewValue == "0");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShRentalPokemonWorkflowService.EvHpField
            && edit.NewValue == "252");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShRentalPokemonWorkflowService.EvAttackField
            && edit.NewValue == "78");
        Assert.Equal(31, result.Workflow.Rentals[0].Ivs.Attack);
        Assert.Equal(0, result.Workflow.Rentals[0].Ivs.Defense);
        Assert.Equal(252, result.Workflow.Rentals[0].Evs.HP);
        Assert.Equal(78, result.Workflow.Rentals[0].Evs.Attack);
        Assert.Empty(result.Diagnostics);
        Assert.True(service.Validate(temp.Paths, result.Session).IsValid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void UpdateFieldEnforcesRentalPokemonLevelBoundaries(int level, bool isValid)
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.LevelField,
            value: level.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (isValid)
        {
            var edit = Assert.Single(result.Session.PendingEdits);
            Assert.Equal(level.ToString(System.Globalization.CultureInfo.InvariantCulture), edit.NewValue);
            Assert.Equal(level, result.Workflow.Rentals[0].Level);
            Assert.Empty(result.Diagnostics);
            return;
        }

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShRentalPokemonWorkflowService.LevelField
                && diagnostic.Message.Contains("between 1 and 100", StringComparison.Ordinal));
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
    public void ApplyChangePlanMaterializesVanillaStyleOmittedRentalFields()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var baseRentalPath = Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "script_event_data",
            "rental.bin");
        var source = File.ReadAllBytes(baseRentalPath);
        var rentalTableOffset = GetFirstRentalTableOffset(source);
        var rentalVtableOffset = rentalTableOffset
            - BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(rentalTableOffset));
        foreach (var fieldIndex in new[] { 3, 13, 19, 22 })
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                source.AsSpan(
                    rentalVtableOffset
                    + (sizeof(ushort) * 2)
                    + (fieldIndex * sizeof(ushort))),
                0);
        }

        File.WriteAllBytes(baseRentalPath, source);
        var service = new SwShRentalPokemonEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.TrainerIdField,
            value: "54321");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.AbilityField,
            value: "2");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.EvHpField,
            value: "100");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "31");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Equal(54321u, output.Rentals[0].TrainerId);
        Assert.Equal(2, output.Rentals[0].Ability);
        Assert.Equal(100, output.Rentals[0].Evs.HP);
        Assert.Equal(31, output.Rentals[0].Ivs.HP);
        Assert.Equal(0x1122334455667788UL, output.Rentals[0].Hash1);
        Assert.Equal(0x8877665544332211UL, output.Rentals[0].Hash2);
    }

    [Fact]
    public void ApplyWriteFailurePreservesTheExistingLayeredRentalTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var existingLayered = SwShRentalPokemonWorkflowServiceTests.CreateRentalTable(
            new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6));
        temp.WriteOutputFile(SwShRentalPokemonWorkflowService.RentalPokemonDataPath, existingLayered);
        var service = new SwShRentalPokemonEditSessionService((tempPath, contents) =>
        {
            File.WriteAllBytes(tempPath, contents[..Math.Min(16, contents.Length)]);
            throw new IOException("Simulated temporary output write failure.");
        });
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "0");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(plan.CanApply);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Simulated temporary output write failure", StringComparison.Ordinal));
        Assert.Equal(existingLayered, File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(GetOutputRentalPath(temp))!,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ValidateRejectsAStagedEditWhenItsSourceLayerChanges()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var workspace = new ProjectWorkspaceService();
        var service = new SwShRentalPokemonEditSessionService(workspace);
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "0");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var layeredSource = SwShRentalPokemonWorkflowServiceTests.CreateRentalTable(
            new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6));
        temp.WriteOutputFile(SwShRentalPokemonWorkflowService.RentalPokemonDataPath, layeredSource);
        workspace.ClearMemoryCache();

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("source layer changed", StringComparison.Ordinal));
        Assert.Equal(layeredSource, File.ReadAllBytes(GetOutputRentalPath(temp)));
    }

    [Fact]
    public void ApplyRejectsAReviewedPlanWhenSourceBytesChangeInPlace()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var workspace = new ProjectWorkspaceService();
        var service = new SwShRentalPokemonEditSessionService(workspace);
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "0");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var changedSource = SwShRentalPokemonWorkflowServiceTests.CreateRentalTable(
            new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6));
        temp.WriteBaseRomFsFile(
            SwShRentalPokemonWorkflowService.RentalPokemonDataPath["romfs/".Length..],
            changedSource);
        workspace.ClearMemoryCache();

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("source file changed", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputRentalPath(temp)));
    }

    [Fact]
    public void ApplyRejectsAReviewedPlanAfterThePendingValueChanges()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "0");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, first.Session);
        var changed = service.UpdateField(
            temp.Paths,
            first.Session,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "1");

        var apply = service.ApplyChangePlan(temp.Paths, changed.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputRentalPath(temp)));
    }

    [Fact]
    public void RepeatedSaveUsesTheLayeredSourceAndPreservesTheEarlierEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var workspace = new ProjectWorkspaceService();
        var service = new SwShRentalPokemonEditSessionService(workspace);
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "0");
        var firstPlan = service.CreateChangePlan(temp.Paths, first.Session);
        _ = service.ApplyChangePlan(temp.Paths, first.Session, firstPlan);
        workspace.ClearMemoryCache();

        var second = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.Move2Field,
            value: "4");
        var secondPlan = service.CreateChangePlan(temp.Paths, second.Session);
        var secondApply = service.ApplyChangePlan(temp.Paths, second.Session, secondPlan);

        Assert.Equal(ProjectFileLayer.Layered, Assert.Single(secondPlan.Writes).Sources.Single().Layer);
        Assert.DoesNotContain(secondApply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Equal(0, output.Rentals[0].Ivs.HP);
        Assert.Equal(4, output.Rentals[0].Moves[2]);
        var baseOutput = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "script_event_data",
            "rental.bin")));
        Assert.Equal(31, baseOutput.Rentals[0].Ivs.HP);
        Assert.Equal(3, baseOutput.Rentals[0].Moves[2]);
    }

    [Fact]
    public void SpeciesAndFormEditsMustResolveToAPresentPersonalRecord()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var personalRecords = Enumerable.Range(0, 135)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        personalRecords[25] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 25,
            formCount: 1);
        personalRecords[133] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            formStatsIndex: 134,
            formCount: 2);
        personalRecords[134] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            localFormIndex: 1,
            form: 1);
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(personalRecords));
        var service = new SwShRentalPokemonEditSessionService();

        var species = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.SpeciesField,
            value: "25");
        var invalidIntermediate = service.Validate(temp.Paths, species.Session);

        Assert.False(invalidIntermediate.IsValid);
        Assert.Contains(
            invalidIntermediate.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShRentalPokemonWorkflowService.FormField);

        var form = service.UpdateField(
            temp.Paths,
            species.Session,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.FormField,
            value: "0");
        var validation = service.Validate(temp.Paths, form.Session);
        var plan = service.CreateChangePlan(temp.Paths, form.Session);
        var apply = service.ApplyChangePlan(temp.Paths, form.Session, plan);

        Assert.True(validation.IsValid);
        Assert.Contains(
            validation.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("hash identifiers", StringComparison.Ordinal));
        Assert.Contains(
            Assert.Single(plan.Writes).Sources,
            source => source.RelativePath == SwShPokemonWorkflowService.PersonalDataPath
                && source.Layer == ProjectFileLayer.Base);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Equal(25, output.Rentals[0].Species);
        Assert.Equal(0, output.Rentals[0].Form);
        Assert.Equal(0x1122334455667788UL, output.Rentals[0].Hash1);
        Assert.Equal(0x8877665544332211UL, output.Rentals[0].Hash2);
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

    private static int GetFirstRentalTableOffset(ReadOnlySpan<byte> data)
    {
        var rootTableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data));
        var rootVtableOffset = rootTableOffset
            - BinaryPrimitives.ReadInt32LittleEndian(data[rootTableOffset..]);
        var vectorFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            data[(rootVtableOffset + (sizeof(ushort) * 2))..]);
        var vectorFieldLocation = rootTableOffset + vectorFieldOffset;
        var vectorOffset = vectorFieldLocation
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[vectorFieldLocation..]));
        var firstElementOffset = vectorOffset + sizeof(uint);
        return firstElementOffset
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[firstElementOffset..]));
    }
}
