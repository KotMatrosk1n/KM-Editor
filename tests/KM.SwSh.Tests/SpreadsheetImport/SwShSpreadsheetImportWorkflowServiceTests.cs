// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.SpreadsheetImport;

public sealed class SwShSpreadsheetImportWorkflowServiceTests
{
    [Fact]
    public void LoadReadsProfilesFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/spreadsheet-import.profiles.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "profiles": [
                {
                  "profileId": "items_price_sheet",
                  "name": "Items Price Sheet",
                  "sourceKind": "xlsx",
                  "targetWorkflow": "items",
                  "status": "available",
                  "description": "Import item price columns from a workbook fixture.",
                  "columns": [
                    {
                      "column": 2,
                      "header": "BuyPrice",
                      "valueKind": "integer",
                      "isRequired": true,
                      "description": "Updated buy price."
                    },
                    {
                      "column": 1,
                      "header": "ItemId",
                      "valueKind": "integer",
                      "isRequired": true,
                      "description": "Item identifier."
                    }
                  ]
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShSpreadsheetImportWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var profile = Assert.Single(workflow.Profiles);
        Assert.Equal("items_price_sheet", profile.ProfileId);
        Assert.Equal("Items Price Sheet", profile.Name);
        Assert.Equal("xlsx", profile.SourceKind);
        Assert.Equal("items", profile.TargetWorkflow);
        Assert.Equal("available", profile.Status);
        Assert.Equal(2, profile.Columns.Count);
        Assert.Equal("ItemId", profile.Columns[0].Header);
        Assert.Equal("BuyPrice", profile.Columns[1].Header);
        Assert.True(profile.Columns[0].IsRequired);
        Assert.Equal(ProjectFileLayer.Base, profile.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, profile.Provenance.FileState);
        Assert.Equal(1, workflow.Stats.TotalProfileCount);
        Assert.Equal(2, workflow.Stats.TotalColumnCount);
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

        var workflow = new SwShSpreadsheetImportWorkflowService().Load(project);

        Assert.Empty(workflow.Profiles);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.spreadsheetImport");
    }

    [Fact]
    public void LoadWarnsWhenProfileIdsAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/spreadsheet-import.profiles.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "profiles": [
                {
                  "profileId": "items_price_sheet",
                  "name": "Items Price Sheet",
                  "sourceKind": "xlsx",
                  "targetWorkflow": "items",
                  "status": "available",
                  "description": "Import item price columns from a workbook fixture.",
                  "columns": []
                },
                {
                  "profileId": "items_price_sheet",
                  "name": "Duplicate Items Sheet",
                  "sourceKind": "csv",
                  "targetWorkflow": "items",
                  "status": "available",
                  "description": "Duplicate profile fixture.",
                  "columns": []
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShSpreadsheetImportWorkflowService().Load(project);

        Assert.Equal(2, workflow.Profiles.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.spreadsheetImport");
    }
}
