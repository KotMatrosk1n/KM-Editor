// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Placement;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.StaticEncounters;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Placement;

public sealed class SwShPlacementWorkflowServiceTests
{
    [Fact]
    public void LoadReadsPlacedObjectsFromRealPlacementPack()
    {
        using var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(temp, includeStaticObject: true);
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(3, workflow.Objects.Count);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShPlacementWorkflowService.ItemIdField);
        var fieldItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");
        Assert.Equal($"{SwShPlacementTestFixtures.AreaMember}|0|fieldItem|0|-", fieldItem.ObjectId);
        Assert.Equal("Field item: Potion", fieldItem.Label);
        Assert.Equal("Route 1", fieldItem.Map);
        Assert.Equal(SwShPlacementTestFixtures.AreaMember, fieldItem.ArchiveMember);
        Assert.Equal(1u, fieldItem.ItemId);
        Assert.Equal("Potion", fieldItem.ItemName);
        Assert.Equal("0xAABBCCDD00112233", fieldItem.ItemHash);
        Assert.Equal(1, fieldItem.Quantity);
        Assert.Null(fieldItem.Chance);
        Assert.Equal(10.5, fieldItem.X);
        Assert.Equal(0, fieldItem.Y);
        Assert.Equal(-4.25, fieldItem.Z);
        Assert.Equal(90, fieldItem.RotationY);
        Assert.Equal("visible_potion", fieldItem.ScriptId);
        Assert.Equal(ProjectFileLayer.Base, fieldItem.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, fieldItem.Provenance.FileState);
        Assert.Equal(SwShPlacementWorkflowService.PlacementDataPath, fieldItem.Provenance.SourceFile);

        var hiddenItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "HiddenItem");
        Assert.Equal(0, hiddenItem.ChanceIndex);
        Assert.Equal(2, hiddenItem.Quantity);
        Assert.Equal(50, hiddenItem.Chance);
        Assert.Equal("hidden_item", hiddenItem.ScriptId);
        var staticObject = workflow.Objects.Single(placedObject => placedObject.ObjectType == "StaticObject");
        Assert.Contains("Static 000", staticObject.Label, StringComparison.Ordinal);
        Assert.Contains("Grookey", staticObject.Label, StringComparison.Ordinal);
        Assert.Contains("0x0102030405060708", staticObject.Label, StringComparison.Ordinal);
        Assert.Equal("Test Cave", staticObject.Map);
        Assert.Contains(
            staticObject.Fields!,
            field => field.Label == "Static Encounter"
                && field.DisplayValue.Contains("Grookey", StringComparison.Ordinal)
                && field.DisplayValue.Contains("0x0102030405060708", StringComparison.Ordinal));
        Assert.Equal(3, workflow.Stats.TotalObjectCount);
        Assert.Equal(2, workflow.Stats.TotalAreaCount);
        Assert.Equal(3, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadResolvesFieldItemsByHashBeforeRawItemsVector()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack(
                fieldItemHash: SwShPlacementTestFixtures.GreatBallHash,
                fieldItemRawItems: [1]));
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item_hash_to_index.dat",
            SwShPlacementTestFixtures.CreateItemHashTable());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames(
                "",
                "Potion",
                "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        var fieldItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");
        Assert.Equal(2u, fieldItem.ItemId);
        Assert.Equal("Great Ball", fieldItem.ItemName);
        Assert.Equal("Field item: Great Ball", fieldItem.Label);
        Assert.Equal("0xAABBCCDD00112244", fieldItem.ItemHash);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenPlacementPackIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/placement.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        Assert.Empty(workflow.Objects);
        Assert.Equal(0, workflow.Stats.SourceFileCount);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.placement");
    }

    [Fact]
    public void LoadWarnsWhenItemHashTableIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        Assert.Equal(2, workflow.Objects.Count);
        Assert.All(workflow.Objects, placedObject => Assert.Null(placedObject.ItemId));
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.placement"
                && diagnostic.Expected == SwShPlacementWorkflowService.ItemHashPath);
    }
}
