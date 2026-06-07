// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Items;
using Xunit;

namespace KM.SwSh.Tests.Items;

public sealed class SwShItemsEditSessionServiceTests
{
    [Fact]
    public void UpdateBuyPriceAddsPendingEditAndPreviewsWorkflowValue()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateBuyPrice(temp.Paths, session: null, itemId: 1, buyPrice: 450);

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
    public void ValidateAcceptsPendingBuyPriceForLoadedItem()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateBuyPrice(temp.Paths, session: null, itemId: 1, buyPrice: 450);

        var validation = service.Validate(temp.Paths, editResult.Session);

        Assert.True(validation.IsValid);
        Assert.DoesNotContain(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void UpdateBuyPriceRequiresEditableProjectPaths()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateBuyPrice(
            temp.Paths with { OutputRootPath = null },
            session: null,
            itemId: 1,
            buyPrice: 450);

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
