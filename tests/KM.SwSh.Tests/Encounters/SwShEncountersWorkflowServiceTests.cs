// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Encounters;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Encounters;

public sealed class SwShEncountersWorkflowServiceTests
{
    [Fact]
    public void LoadReadsEncounterTablesFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/encounters.wild.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "tables": [
                {
                  "tableId": "route_1_grass_sword",
                  "location": "Route 1",
                  "area": "Grass",
                  "encounterType": "Overworld",
                  "gameVersion": "Sword",
                  "slots": [
                    {
                      "slot": 2,
                      "species": "Rookidee",
                      "levelMin": 4,
                      "levelMax": 6,
                      "weight": 25,
                      "timeOfDay": "Day",
                      "weather": "Any"
                    },
                    {
                      "slot": 1,
                      "species": "Skwovet",
                      "levelMin": 3,
                      "levelMax": 5,
                      "weight": 35,
                      "timeOfDay": null,
                      "weather": "Any"
                    }
                  ]
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var table = Assert.Single(workflow.Tables);
        Assert.Equal("route_1_grass_sword", table.TableId);
        Assert.Equal("Route 1", table.Location);
        Assert.Equal("Grass", table.Area);
        Assert.Equal("Overworld", table.EncounterType);
        Assert.Equal("Sword", table.GameVersion);
        Assert.Equal(2, table.Slots.Count);
        Assert.Equal("Skwovet", table.Slots[0].Species);
        Assert.Null(table.Slots[0].TimeOfDay);
        Assert.Equal("Rookidee", table.Slots[1].Species);
        Assert.Equal("Day", table.Slots[1].TimeOfDay);
        Assert.Equal(ProjectFileLayer.Base, table.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, table.Provenance.FileState);
        Assert.Equal(1, workflow.Stats.TotalTableCount);
        Assert.Equal(2, workflow.Stats.TotalSlotCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/encounters.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.encounters");
    }

    [Fact]
    public void LoadWarnsWhenTableIdsAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/encounters.wild.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "tables": [
                {
                  "tableId": "route_1_grass_sword",
                  "location": "Route 1",
                  "area": "Grass",
                  "encounterType": "Overworld",
                  "gameVersion": "Sword",
                  "slots": []
                },
                {
                  "tableId": "route_1_grass_sword",
                  "location": "Route 1",
                  "area": "Grass",
                  "encounterType": "Random",
                  "gameVersion": "Sword",
                  "slots": []
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShEncountersWorkflowService().Load(project);

        Assert.Equal(2, workflow.Tables.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.encounters");
    }
}
