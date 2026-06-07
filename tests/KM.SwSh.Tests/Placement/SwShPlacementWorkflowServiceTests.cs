// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Placement;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Placement;

public sealed class SwShPlacementWorkflowServiceTests
{
    [Fact]
    public void LoadReadsPlacedObjectsFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/placement.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "objects": [
                {
                  "objectId": "route_1_hidden_potion",
                  "objectType": "HiddenItem",
                  "label": "Hidden Potion",
                  "map": "Route 1",
                  "x": 10.5,
                  "y": 0,
                  "z": -4.25,
                  "rotationY": 90,
                  "scriptId": "script_hidden_item_001"
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var placedObject = Assert.Single(workflow.Objects);
        Assert.Equal("route_1_hidden_potion", placedObject.ObjectId);
        Assert.Equal("HiddenItem", placedObject.ObjectType);
        Assert.Equal("Hidden Potion", placedObject.Label);
        Assert.Equal("Route 1", placedObject.Map);
        Assert.Equal(10.5, placedObject.X);
        Assert.Equal(0, placedObject.Y);
        Assert.Equal(-4.25, placedObject.Z);
        Assert.Equal(90, placedObject.RotationY);
        Assert.Equal("script_hidden_item_001", placedObject.ScriptId);
        Assert.Equal(ProjectFileLayer.Base, placedObject.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, placedObject.Provenance.FileState);
        Assert.Equal(1, workflow.Stats.TotalObjectCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/placement.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        Assert.Empty(workflow.Objects);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.placement");
    }

    [Fact]
    public void LoadWarnsWhenObjectIdsAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/placement.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "objects": [
                {
                  "objectId": "route_1_hidden_potion",
                  "objectType": "HiddenItem",
                  "label": "Hidden Potion",
                  "map": "Route 1",
                  "x": 10.5,
                  "y": 0,
                  "z": -4.25,
                  "rotationY": 90,
                  "scriptId": "script_hidden_item_001"
                },
                {
                  "objectId": "route_1_hidden_potion",
                  "objectType": "HiddenItem",
                  "label": "Hidden Antidote",
                  "map": "Route 1",
                  "x": 12,
                  "y": 0,
                  "z": -5,
                  "rotationY": 180,
                  "scriptId": null
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        Assert.Equal(2, workflow.Objects.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.placement");
    }
}
