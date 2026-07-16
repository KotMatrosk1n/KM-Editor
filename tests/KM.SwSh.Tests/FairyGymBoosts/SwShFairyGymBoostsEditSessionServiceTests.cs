// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.FairyGymBoosts;
using KM.SwSh.Items;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace KM.SwSh.Tests.FairyGymBoosts;

public sealed class SwShFairyGymBoostsEditSessionServiceTests
{
    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void StageCreatesOneCanonicalEditWithAllSourcesAndPayloadIdentity(ProjectGame game)
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(game);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, game);
        var workflow = LoadWorkflow(paths);
        var selections = ChangeSelection(
            CreateCurrentSelections(workflow),
            "annette-weakness-poison",
            effectId: 2,
            SwShFairyGymBoostsWorkflowService.ResultIncrease);

        var result = new SwShFairyGymBoostsEditSessionService().StageBoosts(
            paths,
            selections,
            session: null);

        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.fairyGymBoosts", edit.Domain);
        Assert.Equal("fairy-gym-boosts", edit.RecordId);
        Assert.Equal("boostSelections", edit.Field);
        Assert.Equal("Stage Fairy Gym boost outcomes.", edit.Summary);
        Assert.Equal(12, edit.NewValue!.Split(';').Length);
        Assert.Equal(
            SwShFairyGymBoostsWorkflowService.Boosts.Select(boost => boost.BoostId),
            edit.NewValue.Split(';').Select(entry => entry.Split(':')[0]));

        var expectedHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(edit.NewValue)));
        Assert.Equal(7, edit.Sources.Count);
        Assert.Equal(6, edit.Sources.Count(source => source.Layer == ProjectFileLayer.Base));
        Assert.Equal(
            $"pending/fairy-gym-boosts/selections/{expectedHash}",
            edit.Sources.Single(source => source.Layer == ProjectFileLayer.Pending).RelativePath);
        Assert.Equal(
            edit.Sources
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal),
            edit.Sources);
    }

    [Fact]
    public void StageIncludesOptionalLayeredSourcesAndRejectsNoOp()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword);
        var relativePath = SwShFairyGymBoostsWorkflowService.AnnetteSequencePath;
        temp.WriteOutputFile(relativePath, FairyGymBoostTestFixtures.CreateVanillaBseq(relativePath));
        var workflow = LoadWorkflow(paths);
        var current = CreateCurrentSelections(workflow);
        var service = new SwShFairyGymBoostsEditSessionService();

        var noOp = service.StageBoosts(paths, current, session: null);
        var changed = service.StageBoosts(
            paths,
            ChangeSelection(
                current,
                "annette-weakness-poison",
                2,
                SwShFairyGymBoostsWorkflowService.ResultIncrease),
            session: null);

        Assert.Empty(noOp.Session.PendingEdits);
        Assert.Contains(
            noOp.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("no changed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            Assert.Single(changed.Session.PendingEdits).Sources,
            source => source.Layer == ProjectFileLayer.Layered
                && source.RelativePath == relativePath);
    }

    [Fact]
    public void ValidateRejectsForgedIdentitySummaryPayloadAndSources()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword);
        var workflow = LoadWorkflow(paths);
        var service = new SwShFairyGymBoostsEditSessionService();
        var staged = service.StageBoosts(
            paths,
            ChangeSelection(
                CreateCurrentSelections(workflow),
                "annette-weakness-poison",
                2,
                SwShFairyGymBoostsWorkflowService.ResultIncrease),
            session: null);
        var edit = Assert.Single(staged.Session.PendingEdits);

        var forgedSessions = new[]
        {
            staged.Session with { PendingEdits = [edit with { Field = "other" }] },
            staged.Session with { PendingEdits = [edit with { Summary = "Other summary" }] },
            staged.Session with { PendingEdits = [edit with { NewValue = edit.NewValue + ";" }] },
            staged.Session with { PendingEdits = [edit with { Sources = edit.Sources.Skip(1).ToArray() }] },
            staged.Session with { PendingEdits = [edit, edit] },
        };

        Assert.All(forgedSessions, forged =>
        {
            var validation = service.Validate(paths, forged);
            Assert.False(validation.IsValid);
            Assert.Contains(
                validation.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        });
    }

    [Fact]
    public void PlanIsCapturedAndApplyChangesOnlyReviewedOwnedBytes()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword);
        var relativePath = SwShFairyGymBoostsWorkflowService.AnnetteSequencePath;
        var baseBytes = File.ReadAllBytes(FairyGymBoostTestFixtures.GetBasePath(temp, relativePath));
        var service = new SwShFairyGymBoostsEditSessionService();
        var staged = service.StageBoosts(
            paths,
            ChangeSelection(
                CreateCurrentSelections(LoadWorkflow(paths)),
                "annette-weakness-poison",
                2,
                SwShFairyGymBoostsWorkflowService.ResultIncrease),
            session: null);

        var plan = service.CreateChangePlan(paths, staged.Session);
        var write = Assert.Single(plan.Writes);
        Assert.True(plan.CanApply);
        Assert.Equal(relativePath, write.TargetRelativePath);
        Assert.False(string.IsNullOrWhiteSpace(write.SourceFingerprint));
        var applied = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.DoesNotContain(
            applied.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            applied.WrittenFiles,
            file => file.RelativePath == relativePath);
        var output = File.ReadAllBytes(FairyGymBoostTestFixtures.GetOutputPath(temp, relativePath));
        Assert.Equal((2, 1), FairyGymBoostTestFixtures.ReadSlot(output, answerChoice: 1));
        Assert.Equal(
            FairyGymBoostTestFixtures.ReadSlot(baseBytes, answerChoice: 2),
            FairyGymBoostTestFixtures.ReadSlot(output, answerChoice: 2));
        Assert.Equal(
            FairyGymBoostTestFixtures.ReadSlot(baseBytes, answerChoice: 3),
            FairyGymBoostTestFixtures.ReadSlot(output, answerChoice: 3));
        Assert.All(
            Enumerable.Range(0, baseBytes.Length).Where(offset =>
                offset < SwShFairyGymBoostsBseqPatcher.PayloadOffset
                || offset >= SwShFairyGymBoostsBseqPatcher.PayloadOffset
                    + SwShFairyGymBoostsBseqPatcher.SlotSize),
            offset => Assert.Equal(baseBytes[offset], output[offset]));
    }

    [Fact]
    public void RestoreDeletesGeneratedFileOnlyWhenCompleteResultEqualsBase()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword);
        var relativePath = SwShFairyGymBoostsWorkflowService.AnnetteSequencePath;
        var baseBytes = File.ReadAllBytes(FairyGymBoostTestFixtures.GetBasePath(temp, relativePath));
        var layered = SwShFairyGymBoostsBseqPatcher.ApplySelections(
            baseBytes,
            [new SwShFairyGymBoostAnswerPatch(1, 2, 1)]);
        temp.WriteOutputFile(relativePath, layered);
        var service = new SwShFairyGymBoostsEditSessionService();
        var staged = service.StageBoosts(
            paths,
            ChangeSelection(
                CreateCurrentSelections(LoadWorkflow(paths)),
                "annette-weakness-poison",
                1,
                SwShFairyGymBoostsWorkflowService.ResultIncrease),
            session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);

        var applied = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.DoesNotContain(
            applied.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(FairyGymBoostTestFixtures.GetOutputPath(temp, relativePath)));
    }

    [Fact]
    public void RestorePreservesUnrelatedSameFileEdits()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Shield);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Shield);
        var relativePath = SwShFairyGymBoostsWorkflowService.TeresaSequencePath;
        var baseBytes = File.ReadAllBytes(FairyGymBoostTestFixtures.GetBasePath(temp, relativePath));
        var layered = SwShFairyGymBoostsBseqPatcher.ApplySelections(
            baseBytes,
            [new SwShFairyGymBoostAnswerPatch(2, 6, 1)]);
        layered[0x3000] ^= 0x3C;
        temp.WriteOutputFile(relativePath, layered);
        var service = new SwShFairyGymBoostsEditSessionService();
        var staged = service.StageBoosts(
            paths,
            ChangeSelection(
                CreateCurrentSelections(LoadWorkflow(paths)),
                "teresa-previous-trainer-annette",
                5,
                SwShFairyGymBoostsWorkflowService.ResultIncrease),
            session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);

        var applied = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.DoesNotContain(
            applied.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputPath = FairyGymBoostTestFixtures.GetOutputPath(temp, relativePath);
        Assert.True(File.Exists(outputPath));
        var output = File.ReadAllBytes(outputPath);
        Assert.Equal((5, 1), FairyGymBoostTestFixtures.ReadSlot(output, answerChoice: 2));
        Assert.Equal(layered[0x3000], output[0x3000]);
        Assert.False(output.AsSpan().SequenceEqual(baseBytes));
    }

    [Fact]
    public void ApplyRejectsSourceChangedBeforeVerifiedScope()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword);
        var relativePath = SwShFairyGymBoostsWorkflowService.AnnetteSequencePath;
        var sourcePath = FairyGymBoostTestFixtures.GetBasePath(temp, relativePath);
        var service = new SwShFairyGymBoostsEditSessionService(
            projectWorkspaceService: null,
            fairyGymBoostsWorkflowService: null,
            beforeAcquireApplyScope: () =>
            {
                var bytes = File.ReadAllBytes(sourcePath);
                bytes[0x3000] ^= 0x11;
                File.WriteAllBytes(sourcePath, bytes);
            });
        var staged = service.StageBoosts(
            paths,
            ChangeSelection(
                CreateCurrentSelections(LoadWorkflow(paths)),
                "annette-weakness-poison",
                2,
                SwShFairyGymBoostsWorkflowService.ResultIncrease),
            session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);

        var applied = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.Empty(applied.WrittenFiles);
        Assert.Contains(
            applied.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(FairyGymBoostTestFixtures.GetOutputPath(temp, relativePath)));
    }

    [Fact]
    public void MultiFilePromotionCollisionRollsBackEarlierOutput()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword);
        var firstPath = SwShFairyGymBoostsWorkflowService.AnnetteSequencePath;
        var secondPath = SwShFairyGymBoostsWorkflowService.TeresaSequencePath;
        var collision = Encoding.UTF8.GetBytes("concurrent-output");
        var service = new SwShFairyGymBoostsEditSessionService(
            projectWorkspaceService: null,
            fairyGymBoostsWorkflowService: null,
            beforeVerifiedPromotion: (index, relativePath) =>
            {
                if (index == 1)
                {
                    temp.WriteOutputFile(relativePath, collision);
                }
            });
        var selections = CreateCurrentSelections(LoadWorkflow(paths));
        selections = ChangeSelection(
            selections,
            "annette-weakness-poison",
            2,
            SwShFairyGymBoostsWorkflowService.ResultIncrease);
        selections = ChangeSelection(
            selections,
            "teresa-previous-trainer-annetta",
            6,
            SwShFairyGymBoostsWorkflowService.ResultDecrease);
        var staged = service.StageBoosts(paths, selections, session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        Assert.Equal(2, plan.Writes.Count);

        var applied = service.ApplyChangePlan(paths, staged.Session, plan);

        Assert.Contains(
            applied.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("commit", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(FairyGymBoostTestFixtures.GetOutputPath(temp, firstPath)));
        Assert.Equal(
            collision,
            File.ReadAllBytes(FairyGymBoostTestFixtures.GetOutputPath(temp, secondPath)));
    }

    [Fact]
    public void ApplyRejectsReviewedPlanWithChangedWriteContract()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        var paths = FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword);
        var service = new SwShFairyGymBoostsEditSessionService();
        var staged = service.StageBoosts(
            paths,
            ChangeSelection(
                CreateCurrentSelections(LoadWorkflow(paths)),
                "annette-weakness-poison",
                2,
                SwShFairyGymBoostsWorkflowService.ResultIncrease),
            session: null);
        var plan = service.CreateChangePlan(paths, staged.Session);
        var forged = plan with
        {
            Writes = [Assert.Single(plan.Writes) with { Reason = "forged" }],
        };

        var applied = service.ApplyChangePlan(paths, staged.Session, forged);

        Assert.Empty(applied.WrittenFiles);
        Assert.Contains(
            applied.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StageRejectsUnsupportedGameAndBlockedSource()
    {
        using var temp = FairyGymBoostTestFixtures.CreateProject(ProjectGame.Sword);
        var swordPaths = FairyGymBoostTestFixtures.GetPaths(temp, ProjectGame.Sword);
        var selections = ChangeSelection(
            CreateCurrentSelections(LoadWorkflow(swordPaths)),
            "annette-weakness-poison",
            2,
            SwShFairyGymBoostsWorkflowService.ResultIncrease);
        var service = new SwShFairyGymBoostsEditSessionService();

        var wrongGame = service.StageBoosts(
            swordPaths with { SelectedGame = ProjectGame.Scarlet },
            selections,
            session: null);
        File.WriteAllBytes(
            FairyGymBoostTestFixtures.GetBasePath(
                temp,
                SwShFairyGymBoostsWorkflowService.OpalAgeSequencePath),
            new byte[SwShFairyGymBoostsBseqPatcher.FileLength]);
        var blocked = service.StageBoosts(swordPaths, selections, session: null);

        Assert.Empty(wrongGame.Session.PendingEdits);
        Assert.Contains(
            wrongGame.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(blocked.Session.PendingEdits);
        Assert.Contains(
            blocked.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.File == SwShFairyGymBoostsWorkflowService.OpalAgeSequencePath);
    }

    private static SwShFairyGymBoostsWorkflow LoadWorkflow(ProjectPaths paths)
    {
        var project = new ProjectWorkspaceService().Open(paths);
        return new SwShFairyGymBoostsWorkflowService().Load(project);
    }

    private static SwShFairyGymBoostSelection[] CreateCurrentSelections(
        SwShFairyGymBoostsWorkflow workflow)
    {
        return workflow.Trainers
            .SelectMany(trainer => trainer.Boosts)
            .OrderBy(boost => SwShFairyGymBoostsWorkflowService.Boosts
                .ToList()
                .FindIndex(definition => definition.BoostId == boost.BoostId))
            .Select(boost => new SwShFairyGymBoostSelection(
                boost.BoostId,
                boost.EffectId,
                boost.ResultKind))
            .ToArray();
    }

    private static SwShFairyGymBoostSelection[] ChangeSelection(
        IReadOnlyList<SwShFairyGymBoostSelection> selections,
        string boostId,
        int effectId,
        string resultKind)
    {
        return selections
            .Select(selection => selection.BoostId == boostId
                ? selection with { EffectId = effectId, ResultKind = resultKind }
                : selection)
            .ToArray();
    }
}
