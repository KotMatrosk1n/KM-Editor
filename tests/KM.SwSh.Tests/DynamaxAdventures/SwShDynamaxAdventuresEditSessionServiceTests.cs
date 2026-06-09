// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
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
    public void ApplyChangePlanWritesLayeredDynamaxAdventureTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var service = new SwShDynamaxAdventuresEditSessionService();

        var update = service.UpdateField(temp.Paths, null, 0, SwShDynamaxAdventuresWorkflowService.SpeciesField, "25");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShDynamaxAdventuresWorkflowService.Move3Field, "85");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShDynamaxAdventuresWorkflowService.GuaranteedPerfectIvsField, "6");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShDynamaxAdventuresWorkflowService.IvAttackField, "31");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField, "0");

        var validation = service.Validate(temp.Paths, update.Session);
        Assert.True(validation.IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, Assert.Single(apply.WrittenFiles).RelativePath);

        var outputPath = Path.Combine(
            temp.OutputRootPath,
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath.Replace('/', Path.DirectorySeparatorChar));
        var output = SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(outputPath));
        var entry = output.Entries[0];
        Assert.Equal(25, entry.Species);
        Assert.Equal(85, entry.Moves[3]);
        Assert.Equal(-6, entry.Ivs.Hp);
        Assert.Equal(31, entry.Ivs.Attack);
        Assert.False(entry.IsStoryProgressGated);
        Assert.Equal(0x1122334455667788UL, entry.SingleCaptureFlagBlock);
        Assert.Equal(0x8877665544332211UL, entry.UiMessageId);
    }
}
