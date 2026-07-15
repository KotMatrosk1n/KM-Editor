// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Raids;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Raids;

public sealed class SwShRaidBattlesEditSessionServiceTests
{
    [Fact]
    public void UpdateSlotFieldAddsPendingIvEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShRaidBattlesWorkflowService().Load(project);
        var table = Assert.Single(workflow.Tables);

        var result = service.UpdateSlotField(
            temp.Paths,
            EditSession.Start(),
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.FlawlessIvsField,
            "6");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.raidBattles", edit.Domain);
        Assert.Equal(SwShRaidBattlesWorkflowService.FlawlessIvsField, edit.Field);
        var updatedTable = Assert.Single(result.Workflow.Tables);
        Assert.Equal(6, updatedTable.Slots[1].FlawlessIvs);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateSlotFieldRejectsProbabilityAboveOneHundred()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShRaidBattlesWorkflowService().Load(project);
        var table = Assert.Single(workflow.Tables);

        var result = service.UpdateSlotField(
            temp.Paths,
            EditSession.Start(),
            table.TableId,
            slot: 1,
            SwShRaidBattlesWorkflowService.Star1ProbabilityField,
            "101");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateSlotFieldsIsAtomicAndRemovesSourceEquivalentEdits()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var table = Assert.Single(new SwShRaidBattlesWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths)).Tables);

        var noOp = service.UpdateSlotField(
            temp.Paths,
            null,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.FlawlessIvsField,
            "0");
        Assert.Empty(noOp.Session.PendingEdits);

        var changed = service.UpdateSlotField(
            temp.Paths,
            noOp.Session,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.FlawlessIvsField,
            "6");
        Assert.Single(changed.Session.PendingEdits);

        var restored = service.UpdateSlotField(
            temp.Paths,
            changed.Session,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.FlawlessIvsField,
            "0");
        Assert.Empty(restored.Session.PendingEdits);

        var rejected = service.UpdateSlotFields(
            temp.Paths,
            restored.Session,
            [
                new(table.TableId, 2, SwShRaidBattlesWorkflowService.FlawlessIvsField, "6"),
                new(table.TableId, 2, SwShRaidBattlesWorkflowService.FormField, "300"),
            ]);
        Assert.Empty(rejected.Session.PendingEdits);
        Assert.Equal(0, rejected.Workflow.Tables[0].Slots[1].FlawlessIvs);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Field == SwShRaidBattlesWorkflowService.FormField);
    }

    [Fact]
    public void UpdateSlotFieldsRequiresCoordinatedProbabilityDistribution()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var table = Assert.Single(new SwShRaidBattlesWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths)).Tables);

        var rejected = service.UpdateSlotField(
            temp.Paths,
            null,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.Star5ProbabilityField,
            "80");
        Assert.Empty(rejected.Session.PendingEdits);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("must total 100", StringComparison.Ordinal));

        var accepted = service.UpdateSlotFields(
            temp.Paths,
            null,
            [
                new(table.TableId, 1, SwShRaidBattlesWorkflowService.Star5ProbabilityField, "20"),
                new(table.TableId, 2, SwShRaidBattlesWorkflowService.Star5ProbabilityField, "80"),
            ]);
        Assert.Equal(2, accepted.Session.PendingEdits.Count);
        Assert.DoesNotContain(accepted.Diagnostics, diagnostic =>
            diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(20, accepted.Workflow.Tables[0].Slots[0].Probabilities[4]);
        Assert.Equal(80, accepted.Workflow.Tables[0].Slots[1].Probabilities[4]);
    }

    [Theory]
    [InlineData("species", "2", null, null, "species")]
    [InlineData("form", "1", null, null, "form")]
    [InlineData("species", "1", "ability", "1", "ability")]
    [InlineData("species", "1", "gender", "2", "gender")]
    [InlineData("species", "1", "isGigantamax", "1", "isGigantamax")]
    public void UpdateSlotFieldsRejectsInvalidSpeciesDependentValues(
        string firstField,
        string firstValue,
        string? secondField,
        string? secondValue,
        string expectedField)
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var table = Assert.Single(new SwShRaidBattlesWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths)).Tables);
        var updates = new List<SwShRaidBattleFieldUpdate?>
        {
            new(table.TableId, 2, firstField, firstValue),
        };
        if (secondField is not null && secondValue is not null)
        {
            updates.Add(new(table.TableId, 2, secondField, secondValue));
        }

        var result = service.UpdateSlotFields(temp.Paths, null, updates);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Field == expectedField);
    }

    [Fact]
    public void SpeciesUpdateRefreshesContextualFormAndAbilityMappings()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var table = Assert.Single(new SwShRaidBattlesWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths)).Tables);

        var result = service.UpdateSlotField(
            temp.Paths,
            null,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.SpeciesField,
            "133");

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
        var slot = result.Workflow.Tables[0].Slots[1];
        Assert.Equal("Eevee", slot.Species);
        Assert.Contains(slot.FormOptions, option => option.Value == 1);
        Assert.Contains(slot.AbilityOptions, option =>
            option.Value == 0 && option.Label.Contains("Run Away", StringComparison.Ordinal));
        Assert.Contains("Run Away", slot.AbilityLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangePlanRejectsSourceDriftAndIncludesSourceFingerprint()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var table = Assert.Single(new SwShRaidBattlesWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths)).Tables);
        var update = service.UpdateSlotField(
            temp.Paths,
            null,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.FlawlessIvsField,
            "6");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(plan.Writes).SourceFingerprint));

        var changedArchive = SwShRaidBattleTestFixtures.CreateArchive();
        var changedEntries = changedArchive.Tables[0].Entries.ToArray();
        changedEntries[0] = changedEntries[0] with { LevelTableId = changedEntries[0].LevelTableId + 1 };
        changedArchive = changedArchive with
        {
            Tables = [changedArchive.Tables[0] with { Entries = changedEntries }],
        };
        File.WriteAllBytes(
            GetBaseNestDataPath(temp),
            SwShRaidBattleTestFixtures.CreateRaidBattlePack(changedArchive));

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputNestDataPath(temp)));
    }

    [Fact]
    public void ValidateRejectsNonCanonicalSlotAliases()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var table = Assert.Single(new SwShRaidBattlesWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths)).Tables);
        var update = service.UpdateSlotField(
            temp.Paths,
            null,
            table.TableId,
            slot: 1,
            SwShRaidBattlesWorkflowService.FlawlessIvsField,
            "6");
        var edit = Assert.Single(update.Session.PendingEdits);
        var session = update.Session with
        {
            PendingEdits =
            [
                edit,
                edit with
                {
                    RecordId = $"{table.TableId}#01",
                    NewValue = "5",
                },
            ],
        };

        var validation = service.Validate(temp.Paths, session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Field == "recordId"
            && diagnostic.Message.Contains("canonical slot identity", StringComparison.Ordinal));
    }

    [Fact]
    public void FailedTemporaryWritePreservesExistingOutput()
    {
        using var temp = CreateEditableProject();
        var originalOutput = SwShRaidBattleTestFixtures.CreateRaidBattlePack();
        temp.WriteOutputFile(SwShRaidRewardsWorkflowService.NestDataPath, originalOutput);
        var service = new SwShRaidBattlesEditSessionService((_, _) => throw new IOException("Injected failure."));
        var table = Assert.Single(new SwShRaidBattlesWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths)).Tables);
        var update = service.UpdateSlotField(
            temp.Paths,
            null,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.FlawlessIvsField,
            "6");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(originalOutput, File.ReadAllBytes(GetOutputNestDataPath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Injected failure", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyChangePlanWritesUpdatedRaidBattlePackToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShRaidBattlesWorkflowService().Load(project);
        var table = Assert.Single(workflow.Tables);

        var update = service.UpdateSlotFields(
            temp.Paths,
            null,
            [
                new(table.TableId, 2, SwShRaidBattlesWorkflowService.SpeciesField, "133"),
                new(table.TableId, 2, SwShRaidBattlesWorkflowService.IsGigantamaxField, "1"),
                new(table.TableId, 2, SwShRaidBattlesWorkflowService.FlawlessIvsField, "6"),
                new(table.TableId, 1, SwShRaidBattlesWorkflowService.Star5ProbabilityField, "20"),
                new(table.TableId, 2, SwShRaidBattlesWorkflowService.Star5ProbabilityField, "80"),
            ]);

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShRaidRewardsWorkflowService.NestDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShRaidRewardsWorkflowService.NestDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);

        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(GetOutputNestDataPath(temp)));
        var battleArchive = SwShEncounterNestArchive.Parse(outputPack.GetFileByName(SwShRaidBattlesWorkflowService.EncounterMemberName));
        var updatedSlot = battleArchive.Tables[0].Entries[1];
        Assert.Equal(133, updatedSlot.Species);
        Assert.True(updatedSlot.IsGigantamax);
        Assert.Equal(6, updatedSlot.FlawlessIvs);
        Assert.Equal(80u, updatedSlot.Probabilities[4]);
        Assert.Equal(20u, battleArchive.Tables[0].Entries[0].Probabilities[4]);

        var rewardArchive = SwShNestHoleRewardArchive.Parse(outputPack.GetFileByName("nest_hole_drop_rewards.bin"));
        Assert.Equal(3u, rewardArchive.Tables[0].Rewards[0].ItemId);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }

    private static string GetOutputNestDataPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "data_table.gfpak");
    }

    private static string GetBaseNestDataPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "archive",
            "field",
            "resident",
            "data_table.gfpak");
    }
}
