// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldCreatesPendingDynamaxAdventureIvEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField,
            value: "6");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.dynamaxAdventures", edit.Domain);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField, edit.Field);
        Assert.Equal("dynamaxAdventure:0", edit.RecordId);
        Assert.Equal(6, result.Workflow.Encounters[0].GuaranteedPerfectIvs);
        Assert.Equal(-6, result.Workflow.Encounters[0].Ivs.Hp);
    }

    [Fact]
    public void UpdateFieldClampsDynamaxAdventureIvOverrides()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.IvAttackField,
            value: "-2");
        result = service.UpdateField(
            temp.Paths,
            result.Session,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.IvDefenseField,
            value: "80");

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.IvAttackField
            && edit.NewValue == "0");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.IvDefenseField
            && edit.NewValue == "31");
        Assert.Equal(0, result.Workflow.Encounters[0].Ivs.Attack);
        Assert.Equal(31, result.Workflow.Encounters[0].Ivs.Defense);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateFieldRejectsAmbiguousGuaranteedPerfectIvCount()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField,
            value: "1");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("not representable", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldRejectsUnsafeDynamaxAdventureFields()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField,
            value: "0");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldRejectsGigantamaxStateForNonGigantamaxSpecies()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");
        var invalidGigantamax = service.UpdateField(
            temp.Paths,
            update.Session,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.GigantamaxStateField,
            value: "2");

        Assert.DoesNotContain(invalidGigantamax.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField);
        Assert.Contains(invalidGigantamax.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && edit.NewValue == "467");
        Assert.Contains(invalidGigantamax.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("cannot use", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldResetsGigantamaxStateWhenSpeciesCannotGigantamax()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            SwShDynamaxAdventureTestFixtures.CreateArchive().WriteEdits(
            [
                new(0, SwShDynamaxAdventureField.GigantamaxState, 2),
            ]));
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");

        Assert.Contains(update.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && edit.NewValue == "467");
        Assert.Contains(update.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.GigantamaxStateField
            && edit.NewValue == "1");
        Assert.Equal(467, update.Workflow.Encounters[0].SpeciesId);
        Assert.Equal(1, update.Workflow.Encounters[0].GigantamaxState);

        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void UpdateFieldPreservesMovesWhenSpeciesChanges()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.SpeciesField,
            value: "467");

        Assert.Contains(update.Session.PendingEdits, edit =>
            edit.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField
            && edit.NewValue == "467");
        Assert.DoesNotContain(update.Session.PendingEdits, edit =>
            edit.Field is SwShDynamaxAdventuresWorkflowService.Move0Field
                or SwShDynamaxAdventuresWorkflowService.Move1Field
                or SwShDynamaxAdventuresWorkflowService.Move2Field
                or SwShDynamaxAdventuresWorkflowService.Move3Field);
        Assert.Equal([1, 2, 10, 20], update.Workflow.Encounters[0].Moves.Select(move => move.MoveId));
    }

    [Fact]
    public void UpdateFieldRejectsEmptyDynamaxAdventureMoveSlots()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            entryIndex: 0,
            field: SwShDynamaxAdventuresWorkflowService.Move0Field,
            value: "0");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("requires all four move slots", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredDynamaxAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateCompatibleMain());
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.SpeciesField, "25");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShDynamaxAdventuresWorkflowService.Move3Field, "85");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField, "6");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShDynamaxAdventuresWorkflowService.IvAttackField, "31");

        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(2, plan.Writes.Count);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "exefs/main");
        Assert.Equal(2, apply.WrittenFiles.Count);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == "exefs/main");

        var outputPath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        var output = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(outputPath));
        var entry = output.Entries[0];
        Assert.Equal(25, entry.Species);
        Assert.Equal(1, entry.Moves[0]);
        Assert.Equal(2, entry.Moves[1]);
        Assert.Equal(10, entry.Moves[2]);
        Assert.Equal(85, entry.Moves[3]);
        Assert.Equal(-6, entry.Ivs.Hp);
        Assert.Equal(31, entry.Ivs.Attack);
        Assert.True(entry.IsStoryProgressGated);
        Assert.Equal(0x1122334455667788UL, entry.SingleCaptureFlagBlock);
        Assert.Equal(0x8877665544332211UL, entry.UiMessageId);

        var mainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var main = SwShNsoFile.Parse(File.ReadAllBytes(mainPath));
        var summary = main.Ro.DecompressedData.AsSpan(
            SwShDynamaxAdventuresMainPatcher.SummaryOffset,
            SwShDynamaxAdventuresMainPatcher.SummaryEntrySize);
        Assert.Equal(1, summary[0]);
        Assert.Equal(25, BinaryPrimitives.ReadInt16LittleEndian(summary[2..4]));
        Assert.Equal(1, unchecked((sbyte)summary[4]));
        Assert.Equal(1, unchecked((sbyte)summary[5]));
        Assert.Equal(0xD503201Fu, ReadInstruction(main.Text.DecompressedData, SwShDynamaxAdventuresMainPatcher.LocalSpeciesPresentMismatchBranchOffset));
        Assert.Equal(0xD503201Fu, ReadInstruction(main.Text.DecompressedData, SwShDynamaxAdventuresMainPatcher.NestSpeciesPresentMismatchBranchOffset));
        Assert.Equal(0xD503201Fu, ReadInstruction(main.Text.DecompressedData, SwShDynamaxAdventuresMainPatcher.DaiSpeciesPresentMismatchBranchOffset));
    }

    [Fact]
    public void ApplyChangePlanRemovesLayeredDynamaxAdventureOutputsWhenRestoredToVanilla()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseExeFsFile("main", SwShDynamaxAdventureTestFixtures.CreateCompatibleMain());
        var service = new SwShDynamaxAdventuresEditSessionService();

        var install = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.SpeciesField, "25");
        var installPlan = service.CreateChangePlan(temp.Paths, install.Session);
        Assert.True(installPlan.CanApply);
        var installed = service.ApplyChangePlan(temp.Paths, install.Session, installPlan);
        Assert.DoesNotContain(installed.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        var tablePath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        var mainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        Assert.True(File.Exists(tablePath));
        Assert.True(File.Exists(mainPath));

        var restore = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.SpeciesField, "133");
        restore = service.UpdateField(temp.Paths, restore.Session, 0, SwShDynamaxAdventuresWorkflowService.Move0Field, "1");
        restore = service.UpdateField(temp.Paths, restore.Session, 0, SwShDynamaxAdventuresWorkflowService.Move1Field, "2");
        restore = service.UpdateField(temp.Paths, restore.Session, 0, SwShDynamaxAdventuresWorkflowService.Move2Field, "10");
        restore = service.UpdateField(temp.Paths, restore.Session, 0, SwShDynamaxAdventuresWorkflowService.Move3Field, "20");

        var restorePlan = service.CreateChangePlan(temp.Paths, restore.Session);
        Assert.True(restorePlan.CanApply);
        Assert.Contains(restorePlan.Writes, write => write.TargetRelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(restorePlan.Writes, write => write.TargetRelativePath == "exefs/main");
        Assert.Contains(restorePlan.Writes, write => write.Reason.Contains("removing the generated Adventure table", StringComparison.Ordinal));
        Assert.Contains(restorePlan.Writes, write => write.Reason.Contains("Restore or remove Dynamax Adventures ExeFS mirrors", StringComparison.Ordinal));

        var restored = service.ApplyChangePlan(temp.Paths, restore.Session, restorePlan);

        Assert.DoesNotContain(restored.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.False(File.Exists(tablePath));
        Assert.False(File.Exists(mainPath));
        Assert.Contains(restored.WrittenFiles, file => file.RelativePath == SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath);
        Assert.Contains(restored.WrittenFiles, file => file.RelativePath == "exefs/main");
        Assert.Contains(restored.Diagnostics, diagnostic =>
            diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Info
            && diagnostic.Message.Contains("Restored vanilla Dynamax Adventures Pokemon", StringComparison.Ordinal));
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]);
    }
}
