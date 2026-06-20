// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Placement;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Placement;

public sealed class SwShPlacementEditSessionServiceTests
{
    [Fact]
    public void UpdateObjectFieldAddsPendingEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPlacementEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShPlacementWorkflowService().Load(project);
        var fieldItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");

        var result = service.UpdateObjectField(
            temp.Paths,
            EditSession.Start(),
            fieldItem.ObjectId,
            SwShPlacementWorkflowService.QuantityField,
            "3");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.placement", edit.Domain);
        Assert.Equal(SwShPlacementWorkflowService.QuantityField, edit.Field);
        var updatedObject = result.Workflow.Objects.Single(placedObject => placedObject.ObjectId == fieldItem.ObjectId);
        Assert.Equal(3, updatedObject.Quantity);
    }

    [Fact]
    public void UpdateObjectFieldRequiresHashTableForHashBackedItemIds()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShPlacementWorkflowService().Load(project);
        var fieldItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");
        var service = new SwShPlacementEditSessionService();

        var result = service.UpdateObjectField(
            temp.Paths,
            EditSession.Start(),
            fieldItem.ObjectId,
            SwShPlacementWorkflowService.ItemIdField,
            "2");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesUpdatedPlacementPackToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPlacementEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShPlacementWorkflowService().Load(project);
        var hiddenItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "HiddenItem");
        var update = service.UpdateObjectField(
            temp.Paths,
            EditSession.Start(),
            hiddenItem.ObjectId,
            SwShPlacementWorkflowService.ItemIdField,
            "2");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(SwShPlacementWorkflowService.PlacementDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "placement.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputArchive = SwShPlacementZoneArchive.Parse(
            outputPack.GetFileByName(SwShPlacementTestFixtures.AreaMember),
            new SwShItemHashTable(
            [
                new SwShItemHashEntry(1, SwShPlacementTestFixtures.PotionHash),
                new SwShItemHashEntry(2, SwShPlacementTestFixtures.GreatBallHash),
            ]).ToItemIdByHash());
        Assert.Equal(2, outputArchive.Zones[0].HiddenItems[0].Chances[0].ItemId);
        Assert.Equal(SwShPlacementTestFixtures.GreatBallHash, outputArchive.Zones[0].HiddenItems[0].Chances[0].ItemHash);
    }

    [Fact]
    public void ApplyChangePlanWritesEditableRawPlacementField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPlacementEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShPlacementWorkflowService().Load(project);
        var fieldItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");
        var scaleXField = fieldItem.Fields!.First(field =>
            field.Field.StartsWith("raw.", StringComparison.Ordinal)
            && field.Field.EndsWith("ScaleX", StringComparison.Ordinal));

        Assert.False(scaleXField.IsReadOnly);
        var update = service.UpdateObjectField(
            temp.Paths,
            EditSession.Start(),
            fieldItem.ObjectId,
            scaleXField.Field,
            "2");
        var edit = Assert.Single(update.Session.PendingEdits);
        Assert.Equal(scaleXField.Field, edit.Field);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "placement.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var outputArchive = SwShPlacementZoneArchive.Parse(
            outputPack.GetFileByName(SwShPlacementTestFixtures.AreaMember),
            new SwShItemHashTable(
            [
                new SwShItemHashEntry(1, SwShPlacementTestFixtures.PotionHash),
                new SwShItemHashEntry(2, SwShPlacementTestFixtures.GreatBallHash),
            ]).ToItemIdByHash());
        var outputRawObject = outputArchive.Zones[0].RawObjects.Single(rawObject =>
            rawObject.ObjectType == "FieldItem"
            && rawObject.ObjectIndex == fieldItem.ObjectIndex);
        Assert.Equal("2", outputRawObject.Fields.Single(field => field.Field == scaleXField.Field).Value);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }
}
