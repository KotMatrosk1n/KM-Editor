// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.SwSh.Shops;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Shops;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Workflows;

public sealed class SwShWorkflowServiceTests
{
    [Fact]
    public void ListDisablesWorkflowWhenRequiredDependencyIsLayeredOnly()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", "base-main");
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            SwShItemTestFixtures.CreateItemNames("None", "Pound"));
        temp.WriteOutputFile(
            SwShShopsWorkflowService.ShopDataPath,
            SwShShopsWorkflowServiceTests.CreateShopData([1], []));

        var list = new SwShWorkflowService().List(temp.Paths);
        var shops = list.Workflows.Single(workflow => workflow.Id == SwShWorkflowIds.Shops);

        Assert.Equal(SwShWorkflowAvailability.Disabled, shops.Availability);
        Assert.Contains(
            shops.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("misaligned", StringComparison.OrdinalIgnoreCase)
                && diagnostic.File == SwShShopsWorkflowService.ShopDataPath);
    }

    [Fact]
    public void ListKeepsWorkflowAvailableWhenBaseDependencyHasLayeredOverride()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", "base-main");
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            SwShItemTestFixtures.CreateItemNames("None", "Pound"));
        temp.WriteOutputFile(
            SwShShopsWorkflowService.ShopDataPath,
            SwShShopsWorkflowServiceTests.CreateShopData([2], []));

        var list = new SwShWorkflowService().List(temp.Paths);
        var shops = list.Workflows.Single(workflow => workflow.Id == SwShWorkflowIds.Shops);

        Assert.Equal(SwShWorkflowAvailability.Available, shops.Availability);
        Assert.DoesNotContain(shops.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ListKeepsPrefixWorkflowAvailableWhenBaseDependencyHasAdditionalLayeredFiles()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("None", "Potion"));
        temp.WriteOutputFile(
            "romfs/bin/message/English/common/custom_mod_message.dat",
            [1, 2, 3, 4]);

        var list = new SwShWorkflowService().List(temp.Paths);
        var text = list.Workflows.Single(workflow => workflow.Id == SwShWorkflowIds.Text);

        Assert.NotEqual(SwShWorkflowAvailability.Disabled, text.Availability);
        Assert.DoesNotContain(text.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
