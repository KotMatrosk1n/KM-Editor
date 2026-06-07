// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.RoyalCandy;

public sealed class SwShRoyalCandyWorkflowServiceTests
{
    [Fact]
    public void LoadReadsWorkflowRecipesFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/royal-candy.workflows.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "workflows": [
                {
                  "workflowId": "candy_reward_setup",
                  "name": "Candy Reward Setup",
                  "category": "Items",
                  "target": "items",
                  "status": "available",
                  "description": "Prepare a safe candy reward workflow fixture.",
                  "steps": [
                    {
                      "step": 2,
                      "label": "Preview output",
                      "description": "Inspect planned output files."
                    },
                    {
                      "step": 1,
                      "label": "Review target",
                      "description": "Review target item and output preview."
                    }
                  ]
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var recipe = Assert.Single(workflow.Workflows);
        Assert.Equal("candy_reward_setup", recipe.WorkflowId);
        Assert.Equal("Candy Reward Setup", recipe.Name);
        Assert.Equal("Items", recipe.Category);
        Assert.Equal("items", recipe.Target);
        Assert.Equal("available", recipe.Status);
        Assert.Equal(2, recipe.Steps.Count);
        Assert.Equal("Review target", recipe.Steps[0].Label);
        Assert.Equal("Preview output", recipe.Steps[1].Label);
        Assert.Equal(ProjectFileLayer.Base, recipe.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, recipe.Provenance.FileState);
        Assert.Equal(1, workflow.Stats.TotalWorkflowCount);
        Assert.Equal(2, workflow.Stats.TotalStepCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        Assert.Empty(workflow.Workflows);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.royalCandy");
    }

    [Fact]
    public void LoadWarnsWhenWorkflowIdsAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/royal-candy.workflows.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "workflows": [
                {
                  "workflowId": "candy_reward_setup",
                  "name": "Candy Reward Setup",
                  "category": "Items",
                  "target": "items",
                  "status": "available",
                  "description": "Prepare a safe candy reward workflow fixture.",
                  "steps": []
                },
                {
                  "workflowId": "candy_reward_setup",
                  "name": "Duplicate Candy Workflow",
                  "category": "Items",
                  "target": "items",
                  "status": "available",
                  "description": "Duplicate workflow fixture.",
                  "steps": []
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        Assert.Equal(2, workflow.Workflows.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.royalCandy");
    }
}
