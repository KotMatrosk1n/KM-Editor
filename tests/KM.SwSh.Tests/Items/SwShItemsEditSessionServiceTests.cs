// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.SwSh.Items;
using Xunit;

namespace KM.SwSh.Tests.Items;

public sealed class SwShItemsEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldAddsPendingEditAndPreviewsWorkflowValue()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.items", edit.Domain);
        Assert.Equal(SwShItemsEditSessionService.BuyPriceField, edit.Field);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("450", edit.NewValue);
        Assert.Equal(ProjectFileLayer.Base, Assert.Single(edit.Sources).Layer);
        Assert.Equal(450, Assert.Single(result.Workflow.Items).BuyPrice);
    }

    [Fact]
    public void UpdateFieldReplacesExistingPendingEditForSameItemField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var firstResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var secondResult = service.UpdateField(
            temp.Paths,
            firstResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "600");

        var edit = Assert.Single(secondResult.Session.PendingEdits);
        Assert.Equal("600", edit.NewValue);
        Assert.Equal(600, Assert.Single(secondResult.Workflow.Items).BuyPrice);
    }

    [Fact]
    public void UpdateFieldRejectsUnsupportedItemField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: "sellPrice",
            value: "250");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ValidateAcceptsPendingBuyPriceForLoadedItem()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var validation = service.Validate(temp.Paths, editResult.Session);

        Assert.True(validation.IsValid);
        Assert.DoesNotContain(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void CreateChangePlanListsItemsTargetFileForPendingBuyPrice()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var changePlan = service.CreateChangePlan(temp.Paths, editResult.Session);

        Assert.True(changePlan.CanApply);
        var write = Assert.Single(changePlan.Writes);
        Assert.Equal(SwShItemsWorkflowService.ItemsReadModelPath, write.TargetRelativePath);
        Assert.False(write.ReplacesExistingOutput);
        Assert.Contains("Potion", write.Reason);
        Assert.Equal(ProjectFileLayer.Base, Assert.Single(write.Sources).Layer);
        Assert.Contains(changePlan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void CreateChangePlanMarksExistingOutputFileReplacement()
    {
        using var temp = CreateEditableProject();
        temp.WriteOutputFile(
            SwShItemsWorkflowService.ItemsReadModelPath,
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
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var write = Assert.Single(service.CreateChangePlan(temp.Paths, editResult.Session).Writes);

        Assert.True(write.ReplacesExistingOutput);
    }

    [Fact]
    public void ApplyChangePlanWritesItemsReadModelToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");
        var changePlan = service.CreateChangePlan(temp.Paths, editResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, editResult.Session, changePlan);

        var writtenFile = Assert.Single(applyResult.WrittenFiles);
        Assert.Equal(ProjectFileLayer.Generated, writtenFile.Layer);
        Assert.Equal(SwShItemsWorkflowService.ItemsReadModelPath, writtenFile.RelativePath);
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "kmeditor", "items.readmodel.json");
        Assert.True(File.Exists(outputPath));
        Assert.Contains("\"buyPrice\": 450", File.ReadAllText(outputPath));
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanRejectsStaleReviewedTargets()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");
        var changePlan = service.CreateChangePlan(temp.Paths, editResult.Session);
        var staleWrite = Assert.Single(changePlan.Writes) with { TargetRelativePath = "romfs/kmeditor/stale.json" };
        var stalePlan = new ChangePlan(changePlan.SessionId, [staleWrite], changePlan.Diagnostics);

        var applyResult = service.ApplyChangePlan(temp.Paths, editResult.Session, stalePlan);

        Assert.Empty(applyResult.WrittenFiles);
        Assert.Contains(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "kmeditor", "items.readmodel.json")));
    }

    [Fact]
    public void UpdateFieldRequiresEditableProjectPaths()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths with { OutputRootPath = null },
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
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

        return temp;
    }
}
