// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.SpreadsheetImport;

public sealed class SwShSpreadsheetImportWorkflowServiceTests
{
    [Fact]
    public void LoadCreatesItemsPriceProfileFromRealItemsWorkflow()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShSpreadsheetImportWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var profile = Assert.Single(workflow.Profiles);
        Assert.Equal(SwShSpreadsheetImportWorkflowService.ItemsPriceProfileId, profile.ProfileId);
        Assert.Equal("Items Price Dump", profile.Name);
        Assert.Equal("csv/tsv/json", profile.SourceKind);
        Assert.Equal("items", profile.TargetWorkflow);
        Assert.Equal("readOnly", profile.Status);
        Assert.Equal(5, profile.Columns.Count);
        Assert.Equal("ItemId", profile.Columns[0].Header);
        Assert.True(profile.Columns[0].IsRequired);
        Assert.Equal("BuyPrice", profile.Columns[1].Header);
        Assert.Equal("AlternatePrice", profile.Columns[4].Header);
        Assert.Equal(ProjectFileLayer.Base, profile.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, profile.Provenance.FileState);
        Assert.Equal(SwShItemsWorkflowService.ItemDataPath, profile.Provenance.SourceFile);
        Assert.Equal(1, workflow.Stats.TotalProfileCount);
        Assert.Equal(5, workflow.Stats.TotalColumnCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadBlocksProfileWhenItemDataIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShSpreadsheetImportWorkflowService().Load(project);

        var profile = Assert.Single(workflow.Profiles);
        Assert.Equal("blocked", profile.Status);
        Assert.Equal(ProjectFileLayer.Generated, profile.Provenance.SourceLayer);
        Assert.Equal("backend:spreadsheet-import-profiles", profile.Provenance.SourceFile);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.spreadsheetImport");
    }

    [Fact]
    public void PreviewItemsPriceCsvCreatesItemsEditSessionAndAppliesThroughChangePlan()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sourcePath = Path.Combine(temp.RootPath, "items.csv");
        File.WriteAllText(
            sourcePath,
            """
            ItemId,BuyPrice,WattsPrice,AlternatePrice
            1,450,20,9
            2,250,,
            """);
        var importService = new SwShSpreadsheetImportExecutionService();

        var result = importService.Preview(
            temp.Paths,
            SwShSpreadsheetImportWorkflowService.ItemsPriceProfileId,
            sourcePath,
            session: null);

        Assert.Equal(2, result.Preview.AcceptedRowCount);
        Assert.Equal(0, result.Preview.RejectedRowCount);
        Assert.Equal(0, result.Preview.SkippedRowCount);
        Assert.Equal(4, result.Session.PendingEdits.Count);
        Assert.All(result.Session.PendingEdits, edit => Assert.Equal("workflow.items", edit.Domain));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var itemsEditService = new SwShItemsEditSessionService();
        var plan = itemsEditService.CreateChangePlan(temp.Paths, result.Session);
        Assert.True(plan.CanApply);
        var apply = itemsEditService.ApplyChangePlan(temp.Paths, result.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var output = SwShItemTable.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(450u, output.Records[1].BuyPrice);
        Assert.Equal(20u, output.Records[1].WattsPrice);
        Assert.Equal(9u, output.Records[1].AlternatePrice);
        Assert.Equal(250u, output.Records[2].BuyPrice);
    }

    [Fact]
    public void PreviewItemsPriceJsonDumpCreatesItemsEditSession()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sourcePath = Path.Combine(temp.RootPath, "items.json");
        File.WriteAllText(
            sourcePath,
            """
            [
              {
                "itemId": 1,
                "name": "Potion",
                "buyPrice": 450,
                "sellPrice": 150,
                "wattsPrice": 20,
                "alternatePrice": 9
              }
            ]
            """);
        var importService = new SwShSpreadsheetImportExecutionService();

        var result = importService.Preview(
            temp.Paths,
            SwShSpreadsheetImportWorkflowService.ItemsPriceProfileId,
            sourcePath,
            session: null);

        Assert.Equal(1, result.Preview.AcceptedRowCount);
        Assert.Equal(0, result.Preview.RejectedRowCount);
        Assert.Equal(3, result.Session.PendingEdits.Count);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PreviewAcceptsFullDumpWhenOnlyOneSharedPriceColumnChanged()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sourcePath = Path.Combine(temp.RootPath, "items.tsv");
        File.WriteAllText(
            sourcePath,
            """
            ItemId	Name	BuyPrice	SellPrice
            1	Potion	450	150
            """);
        var importService = new SwShSpreadsheetImportExecutionService();

        var result = importService.Preview(
            temp.Paths,
            SwShSpreadsheetImportWorkflowService.ItemsPriceProfileId,
            sourcePath,
            session: null);

        Assert.Equal(1, result.Preview.AcceptedRowCount);
        Assert.Equal(0, result.Preview.RejectedRowCount);
        Assert.Single(result.Session.PendingEdits);
        Assert.Equal(SwShItemsWorkflowService.BuyPriceField, result.Session.PendingEdits[0].Field);
    }

    [Fact]
    public void PreviewAcceptsFullDumpWhenSharedPriceColumnsMatchTheStoredValue()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sourcePath = Path.Combine(temp.RootPath, "items.tsv");
        File.WriteAllText(
            sourcePath,
            """
            ItemId	Name	BuyPrice	SellPrice
            1	Potion	450	225
            """);
        var importService = new SwShSpreadsheetImportExecutionService();

        var result = importService.Preview(
            temp.Paths,
            SwShSpreadsheetImportWorkflowService.ItemsPriceProfileId,
            sourcePath,
            session: null);

        Assert.Equal(1, result.Preview.AcceptedRowCount);
        Assert.Equal(0, result.Preview.RejectedRowCount);
        Assert.Single(result.Session.PendingEdits);
        Assert.Equal(SwShItemsWorkflowService.BuyPriceField, result.Session.PendingEdits[0].Field);
    }

    [Fact]
    public void PreviewRejectsFullDumpWhenBothSharedPriceColumnsChanged()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sourcePath = Path.Combine(temp.RootPath, "items.tsv");
        File.WriteAllText(
            sourcePath,
            """
            ItemId	Name	BuyPrice	SellPrice
            1	Potion	450	180
            """);
        var importService = new SwShSpreadsheetImportExecutionService();

        var result = importService.Preview(
            temp.Paths,
            SwShSpreadsheetImportWorkflowService.ItemsPriceProfileId,
            sourcePath,
            session: null);

        Assert.Equal(0, result.Preview.AcceptedRowCount);
        Assert.Equal(1, result.Preview.RejectedRowCount);
        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Preview.Rows.SelectMany(row => row.Diagnostics),
            diagnostic => diagnostic.Message.Contains("Row 2: BuyPrice and SellPrice both changed", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewRejectsMalformedJsonWithReadableLocation()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sourcePath = Path.Combine(temp.RootPath, "items.json");
        File.WriteAllText(sourcePath, """[{ "itemId": 1, "buyPrice": }]""");
        var importService = new SwShSpreadsheetImportExecutionService();

        var result = importService.Preview(
            temp.Paths,
            SwShSpreadsheetImportWorkflowService.ItemsPriceProfileId,
            sourcePath,
            session: null);

        Assert.Equal(0, result.Preview.TotalRowCount);
        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Dump Importer source could not be parsed at line", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewRejectsInvalidRowsAndSkipsUnchangedRows()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sourcePath = Path.Combine(temp.RootPath, "items.csv");
        File.WriteAllText(
            sourcePath,
            """
            ItemId,BuyPrice
            999,100
            1,1000000
            bad,100
            1,300
            """);
        var importService = new SwShSpreadsheetImportExecutionService();

        var result = importService.Preview(
            temp.Paths,
            SwShSpreadsheetImportWorkflowService.ItemsPriceProfileId,
            sourcePath,
            session: null);

        Assert.Equal(0, result.Preview.AcceptedRowCount);
        Assert.Equal(3, result.Preview.RejectedRowCount);
        Assert.Equal(1, result.Preview.SkippedRowCount);
        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Preview.Rows, row => row.Status == "skipped");
        Assert.Contains(
            result.Preview.Rows.SelectMany(row => row.Diagnostics),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.StartsWith("Row ", StringComparison.Ordinal));
    }
}
