// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Tests.Items;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Trainers;

public sealed class SwShTrainersWorkflowServiceTests
{
    [Fact]
    public void LoadReadsTrainersFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/trainers.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "trainers": [
                {
                  "trainerId": 10,
                  "name": "Avery",
                  "trainerClass": "Pokemon Trainer",
                  "location": "Route 1",
                  "battleType": "Single",
                  "team": [
                    {
                      "slot": 1,
                      "species": "Grookey",
                      "level": 12,
                      "heldItem": null,
                      "moves": ["Scratch", "Growl"]
                    },
                    {
                      "slot": 2,
                      "species": "Rookidee",
                      "level": 11,
                      "heldItem": "Oran Berry",
                      "moves": []
                    }
                  ]
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTrainersWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var trainer = Assert.Single(workflow.Trainers);
        Assert.Equal("Avery", trainer.Name);
        Assert.Equal("Pokemon Trainer", trainer.TrainerClass);
        Assert.Equal("Single", trainer.BattleType);
        Assert.Equal(2, trainer.Team.Count);
        Assert.Equal("Grookey", trainer.Team[0].Species);
        Assert.Equal(ProjectFileLayer.Base, trainer.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, trainer.Provenance.FileState);
        Assert.Equal(1, workflow.Stats.TotalTrainerCount);
        Assert.Equal(2, workflow.Stats.TotalPokemonCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/trainers.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTrainersWorkflowService().Load(project);

        Assert.Empty(workflow.Trainers);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.trainers");
    }

    [Fact]
    public void LoadWarnsWhenTrainerIdsAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/trainers.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "trainers": [
                {
                  "trainerId": 10,
                  "name": "Avery",
                  "trainerClass": "Pokemon Trainer",
                  "location": "Route 1",
                  "battleType": "Single",
                  "team": []
                },
                {
                  "trainerId": 10,
                  "name": "Blair",
                  "trainerClass": "Pokemon Trainer",
                  "location": "Route 2",
                  "battleType": "Single",
                  "team": []
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTrainersWorkflowService().Load(project);

        Assert.Equal(2, workflow.Trainers.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.trainers");
    }
}
