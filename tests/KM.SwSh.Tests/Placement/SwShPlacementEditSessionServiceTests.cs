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

    [Fact]
    public void UpdateObjectFieldsRollsBackEntireBatchWhenAnyFieldIsNotStored()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack(omitFieldItemCanonicalStorage: true));
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item_hash_to_index.dat",
            SwShPlacementTestFixtures.CreateItemHashTable());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var workspace = new ProjectWorkspaceService();
        var placementWorkflowService = new SwShPlacementWorkflowService();
        var service = new SwShPlacementEditSessionService(workspace, placementWorkflowService);
        var workflow = placementWorkflowService.Load(workspace.Open(temp.Paths));
        var fieldItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");

        var result = service.UpdateObjectFields(
            temp.Paths,
            EditSession.Start(),
            [
                new SwShPlacementObjectFieldUpdate(
                    fieldItem.ObjectId,
                    SwShPlacementWorkflowService.LocationXField,
                    "20"),
                new SwShPlacementObjectFieldUpdate(
                    fieldItem.ObjectId,
                    SwShPlacementWorkflowService.LocationYField,
                    "10"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Empty(result.UpdatedObjects!);
        Assert.Equal(10.5, result.Workflow.Objects.Single(record => record.ObjectId == fieldItem.ObjectId).X);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Message.Contains("omitted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateObjectFieldsReturnsResolvedItemNameInDelta()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPlacementEditSessionService();
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var hiddenItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "HiddenItem");

        var result = service.UpdateObjectFields(
            temp.Paths,
            EditSession.Start(),
            [
                new SwShPlacementObjectFieldUpdate(
                    hiddenItem.ObjectId,
                    SwShPlacementWorkflowService.ItemIdField,
                    "2"),
            ]);

        var updatedObject = Assert.Single(result.UpdatedObjects!);
        Assert.Equal("Great Ball", updatedObject.ItemName);
        Assert.Equal("0xAABBCCDD00112244", updatedObject.ItemHash);
        Assert.Equal("Hidden item: Great Ball", updatedObject.Label);
        var updatedFields = Assert.IsAssignableFrom<IReadOnlyList<SwShPlacementFieldValue>>(updatedObject.Fields);
        Assert.Equal(
            "Great Ball",
            updatedFields.Single(field => field.Field == SwShPlacementWorkflowService.ItemIdField).DisplayValue);
        var hashField = updatedFields.Single(field => field.Field == "hiddenItem.hash");
        Assert.Equal("0xAABBCCDD00112244", hashField.Value);
        Assert.Equal("Great Ball (2)", hashField.DisplayValue);
    }

    [Fact]
    public void UpdateObjectFieldsReturnsEveryHiddenSiblingForSharedTransform()
    {
        using var temp = CreateEditableProjectWithTwoHiddenChances();
        var service = new SwShPlacementEditSessionService();
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var hiddenItems = workflow.Objects.Where(record => record.ObjectType == "HiddenItem").ToArray();

        var first = service.UpdateObjectFields(
            temp.Paths,
            EditSession.Start(),
            [
                new SwShPlacementObjectFieldUpdate(
                    hiddenItems[0].ObjectId,
                    SwShPlacementWorkflowService.LocationXField,
                    "24"),
            ]);

        Assert.Equal(2, first.UpdatedObjects!.Count);
        Assert.All(first.UpdatedObjects, placedObject => Assert.Equal(24, placedObject.X));

        var second = service.UpdateObjectFields(
            temp.Paths,
            first.Session,
            [
                new SwShPlacementObjectFieldUpdate(
                    hiddenItems[1].ObjectId,
                    SwShPlacementWorkflowService.LocationXField,
                    "36"),
            ]);

        var pending = Assert.Single(second.Session.PendingEdits);
        Assert.Equal(hiddenItems[1].ObjectId, pending.RecordId);
        Assert.Equal("36", pending.NewValue);
        Assert.Equal(2, second.UpdatedObjects!.Count);
        Assert.All(second.UpdatedObjects, placedObject => Assert.Equal(36, placedObject.X));
    }

    [Fact]
    public void UpdateObjectFieldsRejectsConflictingIncomingHiddenSiblingTransforms()
    {
        using var temp = CreateEditableProjectWithTwoHiddenChances();
        var service = new SwShPlacementEditSessionService();
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var hiddenItems = workflow.Objects.Where(record => record.ObjectType == "HiddenItem").ToArray();

        var result = service.UpdateObjectFields(
            temp.Paths,
            EditSession.Start(),
            [
                new SwShPlacementObjectFieldUpdate(
                    hiddenItems[0].ObjectId,
                    SwShPlacementWorkflowService.LocationXField,
                    "24"),
                new SwShPlacementObjectFieldUpdate(
                    hiddenItems[1].ObjectId,
                    SwShPlacementWorkflowService.LocationXField,
                    "36"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Empty(result.UpdatedObjects!);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("same underlying storage", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateObjectFieldsProjectsPrimaryRawTransformIntoTopLevelDelta()
    {
        using var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(temp, includeStaticObject: true);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShPlacementEditSessionService();
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var staticObject = workflow.Objects.Single(record => record.ObjectType == "StaticObject");
        var primaryX = staticObject.Fields!.First(field =>
            field.Field.StartsWith("raw.", StringComparison.Ordinal)
            && field.Group == "Transform"
            && field.Label == "X"
            && !field.IsReadOnly);

        var result = service.UpdateObjectFields(
            temp.Paths,
            EditSession.Start(),
            [new SwShPlacementObjectFieldUpdate(staticObject.ObjectId, primaryX.Field, "30")]);

        var updatedObject = Assert.Single(result.UpdatedObjects!);
        Assert.Equal(30, updatedObject.X);
        Assert.Equal("30", updatedObject.Fields!.Single(field => field.Field == primaryX.Field).Value);
    }

    [Fact]
    public void ValidateAllowsBalancingChanceEditsToBeStagedSeparately()
    {
        using var temp = CreateEditableProjectWithTwoHiddenChances();
        var service = new SwShPlacementEditSessionService();
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var hiddenItems = workflow.Objects
            .Where(record => record.ObjectType == "HiddenItem")
            .OrderBy(record => record.ChanceIndex)
            .ToArray();

        var first = service.UpdateObjectFields(
            temp.Paths,
            EditSession.Start(),
            [
                new SwShPlacementObjectFieldUpdate(
                    hiddenItems[0].ObjectId,
                    SwShPlacementWorkflowService.ChanceField,
                    "50"),
            ]);

        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Single(first.Session.PendingEdits);
        var incompleteValidation = service.Validate(temp.Paths, first.Session);
        Assert.False(incompleteValidation.IsValid);
        Assert.Contains(incompleteValidation.Diagnostics, diagnostic => diagnostic.Message.Contains("totals 90", StringComparison.Ordinal));

        var balanced = service.UpdateObjectFields(
            temp.Paths,
            first.Session,
            [
                new SwShPlacementObjectFieldUpdate(
                    hiddenItems[1].ObjectId,
                    SwShPlacementWorkflowService.ChanceField,
                    "50"),
            ]);
        var balancedValidation = service.Validate(temp.Paths, balanced.Session);

        Assert.True(balancedValidation.IsValid);
        Assert.Equal(2, balanced.Session.PendingEdits.Count);
    }

    [Fact]
    public void ValidateDoesNotBlockUnrelatedEditForUntouchedChancePool()
    {
        using var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(
            temp,
            hiddenItemChances:
            [
                new SwShPlacementHiddenItemChance(
                    ChanceIndex: 0,
                    ItemHash: SwShPlacementTestFixtures.PotionHash,
                    ItemId: 1,
                    Chance: 90,
                    Quantity: 2,
                    ItemHashOffset: 0,
                    ChanceOffset: 0,
                    QuantityOffset: 0),
            ]);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShPlacementEditSessionService();
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var fieldItem = workflow.Objects.Single(record => record.ObjectType == "FieldItem");
        var update = service.UpdateObjectField(
            temp.Paths,
            EditSession.Start(),
            fieldItem.ObjectId,
            SwShPlacementWorkflowService.QuantityField,
            "3");

        var validation = service.Validate(temp.Paths, update.Session);

        Assert.True(validation.IsValid);
        Assert.DoesNotContain(
            validation.Diagnostics,
            diagnostic => diagnostic.Message.Contains("chance pool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRejectsConflictingHiddenSiblingStorageAliases()
    {
        using var temp = CreateEditableProjectWithTwoHiddenChances();
        var service = new SwShPlacementEditSessionService();
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var hiddenItems = workflow.Objects.Where(record => record.ObjectType == "HiddenItem").ToArray();
        var staged = service.UpdateObjectField(
            temp.Paths,
            EditSession.Start(),
            hiddenItems[0].ObjectId,
            SwShPlacementWorkflowService.LocationXField,
            "24");
        var firstEdit = Assert.Single(staged.Session.PendingEdits);
        var conflictingSession = staged.Session with
        {
            PendingEdits =
            [
                firstEdit,
                firstEdit with { RecordId = hiddenItems[1].ObjectId, NewValue = "36" },
            ],
        };

        var validation = service.Validate(temp.Paths, conflictingSession);

        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Diagnostics,
            diagnostic => diagnostic.Message.Contains("same underlying storage", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateObjectFieldRejectsZeroItemHashMappings()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack());
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item_hash_to_index.dat",
            new SwShItemHashTable(
            [
                new SwShItemHashEntry(1, SwShPlacementTestFixtures.PotionHash),
                new SwShItemHashEntry(2, 0),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var hiddenItem = workflow.Objects.Single(record => record.ObjectType == "HiddenItem");

        var result = new SwShPlacementEditSessionService().UpdateObjectField(
            temp.Paths,
            EditSession.Start(),
            hiddenItem.ObjectId,
            SwShPlacementWorkflowService.ItemIdField,
            "2");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Message.Contains("not present", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateHashBackedZeroFieldItemStillRequiresHashTable()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack(fieldItemHash: 0));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var fieldItem = workflow.Objects.Single(record => record.ObjectType == "FieldItem");
        Assert.True(fieldItem.ItemUsesHashStorage);
        Assert.False(fieldItem.ItemUsesDirectIdStorage);
        Assert.Equal(string.Empty, fieldItem.ItemHash);

        var result = new SwShPlacementEditSessionService().UpdateObjectField(
            temp.Paths,
            EditSession.Start(),
            fieldItem.ObjectId,
            SwShPlacementWorkflowService.ItemIdField,
            "2");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Message.Contains("hash table", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MixedStorageBatchDoesNotAssignHashToDirectIdFieldItem()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack(
                fieldItemRawItems: [1],
                includeFieldItemHash: false));
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item_hash_to_index.dat",
            SwShPlacementTestFixtures.CreateItemHashTable());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var workflow = new SwShPlacementWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var fieldItem = workflow.Objects.Single(record => record.ObjectType == "FieldItem");
        var hiddenItem = workflow.Objects.Single(record => record.ObjectType == "HiddenItem");
        Assert.True(fieldItem.ItemUsesDirectIdStorage);
        Assert.False(fieldItem.ItemUsesHashStorage);
        Assert.True(hiddenItem.ItemUsesHashStorage);

        var result = new SwShPlacementEditSessionService().UpdateObjectFields(
            temp.Paths,
            EditSession.Start(),
            [
                new SwShPlacementObjectFieldUpdate(hiddenItem.ObjectId, SwShPlacementWorkflowService.ItemIdField, "2"),
                new SwShPlacementObjectFieldUpdate(fieldItem.ObjectId, SwShPlacementWorkflowService.ItemIdField, "2"),
            ]);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        var updatedObjects = Assert.IsAssignableFrom<IReadOnlyList<SwShPlacedObjectRecord>>(result.UpdatedObjects);
        var updatedHiddenItem = updatedObjects.Single(record => record.ObjectId == hiddenItem.ObjectId);
        var updatedFieldItem = updatedObjects.Single(record => record.ObjectId == fieldItem.ObjectId);
        Assert.Equal("0xAABBCCDD00112244", updatedHiddenItem.ItemHash);
        Assert.Equal(string.Empty, updatedFieldItem.ItemHash);
        Assert.Equal(
            string.Empty,
            updatedFieldItem.Fields!.Single(field => field.Field == "fieldItem.hash").Value);
    }

    [Fact]
    public void ApplyDirectIdFieldItemDoesNotRequireItemHashTable()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack(
                fieldItemRawItems: [1],
                includeFieldItemHash: false));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var workspace = new ProjectWorkspaceService();
        var placementWorkflowService = new SwShPlacementWorkflowService();
        var service = new SwShPlacementEditSessionService(workspace, placementWorkflowService);
        var workflow = placementWorkflowService.Load(workspace.Open(temp.Paths));
        var fieldItem = workflow.Objects.Single(record => record.ObjectType == "FieldItem");
        var update = service.UpdateObjectField(
            temp.Paths,
            EditSession.Start(),
            fieldItem.ObjectId,
            SwShPlacementWorkflowService.ItemIdField,
            "2");
        Assert.Equal(
            "Great Ball",
            update.Workflow.Objects.Single(record => record.ObjectId == fieldItem.ObjectId).ItemName);
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
        var outputArchive = SwShPlacementZoneArchive.Parse(outputPack.GetFileByName(SwShPlacementTestFixtures.AreaMember));
        Assert.Equal(2u, Assert.Single(outputArchive.Zones[0].FieldItems[0].ItemIds));
        var reloadedWorkflow = placementWorkflowService.Load(workspace.Open(temp.Paths));
        Assert.Equal(
            2u,
            reloadedWorkflow.Objects.Single(record => record.ObjectId == fieldItem.ObjectId).ItemId);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }

    private static TemporarySwShProject CreateEditableProjectWithTwoHiddenChances()
    {
        var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(
            temp,
            hiddenItemChances:
            [
                new SwShPlacementHiddenItemChance(
                    ChanceIndex: 0,
                    ItemHash: SwShPlacementTestFixtures.PotionHash,
                    ItemId: 1,
                    Chance: 60,
                    Quantity: 2,
                    ItemHashOffset: 0,
                    ChanceOffset: 0,
                    QuantityOffset: 0),
                new SwShPlacementHiddenItemChance(
                    ChanceIndex: 1,
                    ItemHash: SwShPlacementTestFixtures.GreatBallHash,
                    ItemId: 2,
                    Chance: 40,
                    Quantity: 1,
                    ItemHashOffset: 0,
                    ChanceOffset: 0,
                    QuantityOffset: 0),
            ]);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }
}
