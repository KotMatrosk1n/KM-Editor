// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Items;

public sealed class SwShItemsWorkflowServiceTests
{
    [Fact]
    public void LoadReadsItemsFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                },
                {
                  "itemId": 2,
                  "name": "Antidote",
                  "category": "Medicine",
                  "buyPrice": 200,
                  "sellPrice": 100
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShItemsWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Items.Count);
        Assert.Equal("Antidote", workflow.Items[1].Name);
        Assert.Equal(ProjectFileLayer.Base, workflow.Items[0].Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, workflow.Items[0].Provenance.FileState);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadPrefersLayeredReadModelWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteOutputFile(
            SwShItemsWorkflowService.ItemsReadModelPath,
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion Plus",
                  "category": "Medicine",
                  "buyPrice": 500,
                  "sellPrice": 250
                }
              ]
            }
            """);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShItemsWorkflowService().Load(project);

        var item = Assert.Single(workflow.Items);
        Assert.Equal("Potion Plus", item.Name);
        Assert.Equal(ProjectFileLayer.Layered, item.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, item.Provenance.FileState);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShItemsWorkflowService().Load(project);

        Assert.Empty(workflow.Items);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.items");
    }

}
