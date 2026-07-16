// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.FairyGymBoosts;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.FairyGymBoosts;

public sealed class SwShFairyGymBoostsWorkflowServiceTests
{
    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void LoadVerifiesAllCanonicalSourcesForBothGames(ProjectGame game)
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(game);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, game);

        var workflow = Load(paths);

        Assert.Equal(SwShWorkflowAvailability.Available, workflow.Summary.Availability);
        Assert.Equal(game, workflow.DetectedGame);
        Assert.Equal(6, workflow.Sources.Count);
        Assert.All(workflow.Sources, source =>
        {
            Assert.Equal("available", source.Status);
            Assert.Equal("0x00001550", source.PayloadOffsetHex);
            Assert.Equal("0x00001550-0x0000155F", source.OwnedRangeHex);
        });
        Assert.Equal(4, workflow.Stats.TrainerCount);
        Assert.Equal(12, workflow.Stats.BoostCount);
        Assert.Equal(6, workflow.Stats.SourceFileCount);
        Assert.Equal(96, workflow.Stats.OwnedByteCount);
        Assert.All(
            workflow.Trainers.SelectMany(trainer => trainer.Boosts),
            boost => Assert.True(boost.IsAvailable));
        Assert.DoesNotContain(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void LoadMarksMissingAndMalformedSourcesHonestly()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        File.Delete(FairyGymBoostTestFixtures.GetBasePath(
            temp,
            SwShFairyGymBoostsWorkflowService.AnnetteSequencePath));
        File.WriteAllBytes(
            FairyGymBoostTestFixtures.GetBasePath(
                temp,
                SwShFairyGymBoostsWorkflowService.TeresaSequencePath),
            new byte[SwShFairyGymBoostsBseqPatcher.FileLength]);

        var workflow = Load(FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword));

        var missing = workflow.Sources.Single(source => source.SourceId == "bk143");
        Assert.Equal("missing", missing.Status);
        Assert.Equal("unknown", missing.PayloadOffsetHex);
        Assert.Equal("unknown", missing.OwnedRangeHex);
        Assert.All(
            workflow.Trainers.SelectMany(trainer => trainer.Boosts)
                .Where(boost => boost.SequenceFile == SwShFairyGymBoostsWorkflowService.AnnetteSequencePath),
            boost => Assert.False(boost.IsAvailable));

        var blocked = workflow.Sources.Single(source => source.SourceId == "bk144");
        Assert.Equal("blocked", blocked.Status);
        Assert.Equal("unknown", blocked.PayloadOffsetHex);
        Assert.Equal("unknown", blocked.OwnedRangeHex);
        Assert.All(
            workflow.Trainers.SelectMany(trainer => trainer.Boosts)
                .Where(boost => boost.SequenceFile == SwShFairyGymBoostsWorkflowService.TeresaSequencePath),
            boost => Assert.False(boost.IsAvailable));
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.File == SwShFairyGymBoostsWorkflowService.TeresaSequencePath);
    }

    [Fact]
    public void LoadAllowsCompatibleLayeredEditsAndReadsOwnedSelections()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Shield);
        var relativePath = SwShFairyGymBoostsWorkflowService.OpalColorSequencePath;
        var layered = FairyGymBoostTestFixtures.CreateVanillaBseq(relativePath);
        layered = SwShFairyGymBoostsBseqPatcher.ApplySelections(
            layered,
            [new SwShFairyGymBoostAnswerPatch(2, 6, 2)]);
        layered[0x3000] ^= 0x5A;
        temp.WriteOutputFile(relativePath, layered);

        var workflow = Load(FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Shield));

        Assert.Equal("available", workflow.Sources.Single(source => source.SourceId == "bk173").Status);
        var boost = workflow.Trainers
            .SelectMany(trainer => trainer.Boosts)
            .Single(candidate => candidate.BoostId == "opal-color-purple");
        Assert.True(boost.IsAvailable);
        Assert.Equal(6, boost.EffectId);
        Assert.Equal(SwShFairyGymBoostsWorkflowService.ResultDecrease, boost.ResultKind);
    }

    [Fact]
    public void LoadBlocksLayeredSourceWithAmbiguousQuizCommand()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        var relativePath = SwShFairyGymBoostsWorkflowService.OpalAgeSequencePath;
        temp.WriteOutputFile(
            relativePath,
            FairyGymBoostTestFixtures.CreateVanillaBseq(
                relativePath,
                addAmbiguousCommand: true));

        var workflow = Load(FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword));

        Assert.Equal("blocked", workflow.Sources.Single(source => source.SourceId == "bk174").Status);
        Assert.All(
            workflow.Trainers.SelectMany(trainer => trainer.Boosts)
                .Where(boost => boost.SequenceFile == relativePath),
            boost => Assert.False(boost.IsAvailable));
    }

    [Fact]
    public void LoadRequiresExplicitSwordOrShieldRouting()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);

        var workflow = Load(FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Scarlet));

        Assert.Equal(SwShWorkflowAvailability.Disabled, workflow.Summary.Availability);
        Assert.Null(workflow.DetectedGame);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Sword", StringComparison.Ordinal));
    }

    private static SwShFairyGymBoostsWorkflow Load(ProjectPaths paths)
    {
        var project = new ProjectWorkspaceService().Open(paths);
        return new SwShFairyGymBoostsWorkflowService().Load(project);
    }
}
