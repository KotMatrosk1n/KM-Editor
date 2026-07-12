// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
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
                fieldItemRawItems: [1, 2]));
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
        Assert.Equal(
            2,
            fieldItem.Fields!.Count(field => field.Field.Contains(".Items[", StringComparison.Ordinal)));
        Assert.All(
            fieldItem.Fields!.Where(field => field.Field.Contains(".Items[", StringComparison.Ordinal)),
            field => Assert.True(field.IsReadOnly));
    }

    [Fact]
    public void LoadUsesDirectItemIdWhenHashVectorIsEmpty()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack(
                fieldItemRawItems: [2, 1],
                includeFieldItemHash: false));
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item_hash_to_index.dat",
            new SwShItemHashTable(
            [
                new SwShItemHashEntry(1, SwShPlacementTestFixtures.PotionHash),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        var fieldItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");
        Assert.Equal(2u, fieldItem.ItemId);
        Assert.Equal("Great Ball", fieldItem.ItemName);
        Assert.Contains(
            fieldItem.Fields!.Single(field => field.Field == SwShPlacementWorkflowService.ItemIdField).Options!,
            option => option.Value == 2 && option.Label.EndsWith("Great Ball", StringComparison.Ordinal));
        Assert.DoesNotContain(
            workflow.EditableFields.Single(field => field.Field == SwShPlacementWorkflowService.ItemIdField).Options,
            option => option.Value == 2);
        Assert.DoesNotContain(
            fieldItem.Fields!,
            field => field.Field.EndsWith(".Items[0]", StringComparison.Ordinal));
        Assert.Contains(
            fieldItem.Fields!,
            field => field.Field.EndsWith(".Items[1]", StringComparison.Ordinal)
                && field.IsReadOnly);
    }

    [Fact]
    public void LoadKeepsUnsupportedDirectItemIdsCorrectableWithoutOverflowing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack(
                fieldItemRawItems: [uint.MaxValue],
                includeFieldItemHash: false));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        var fieldItem = workflow.Objects.Single(placedObject => placedObject.ObjectType == "FieldItem");
        Assert.Null(fieldItem.ItemId);
        Assert.False(fieldItem.Fields!.Single(field =>
            field.Field == SwShPlacementWorkflowService.ItemIdField).IsReadOnly);
    }

    [Fact]
    public void LoadOffersOnlyItemsWithNonZeroEncodableHashes()
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
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        var itemField = workflow.EditableFields.Single(field =>
            field.Field == SwShPlacementWorkflowService.ItemIdField);
        Assert.Collection(
            itemField.Options,
            option => Assert.Equal(1, option.Value));
    }

    [Fact]
    public void LoadMarksOmittedCanonicalStorageReadOnlyAndUsesTypeSpecificQuantityLimits()
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
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        var fieldItem = workflow.Objects.Single(record => record.ObjectType == "FieldItem");
        var fieldItemFields = Assert.IsAssignableFrom<IReadOnlyList<SwShPlacementFieldValue>>(fieldItem.Fields);
        var fieldItemX = fieldItemFields.Single(field =>
            field.Field == SwShPlacementWorkflowService.LocationXField);
        var fieldItemY = fieldItemFields.Single(field =>
            field.Field == SwShPlacementWorkflowService.LocationYField);
        var fieldItemQuantity = fieldItemFields.Single(field =>
            field.Field == SwShPlacementWorkflowService.QuantityField);
        Assert.False(fieldItemX.IsReadOnly);
        Assert.True(fieldItemY.IsReadOnly);
        Assert.Contains("omitted", fieldItemY.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(fieldItemQuantity.IsReadOnly);
        Assert.Equal(byte.MaxValue, fieldItemQuantity.MaximumValue);

        var hiddenItem = workflow.Objects.Single(record => record.ObjectType == "HiddenItem");
        var hiddenQuantity = hiddenItem.Fields!.Single(field =>
            field.Field == SwShPlacementWorkflowService.QuantityField);
        Assert.Equal(SwShPlacementWorkflowService.MaximumQuantity, hiddenQuantity.MaximumValue);
    }

    [Fact]
    public void LoadPreservesFloat32PrecisionInCanonicalFields()
    {
        const float storedX = 10.1234567f;
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShPlacementTestFixtures.CreatePlacementPack(fieldItemX: storedX));
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item_hash_to_index.dat",
            SwShPlacementTestFixtures.CreateItemHashTable());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Great Ball"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        var fieldItem = workflow.Objects.Single(record => record.ObjectType == "FieldItem");
        var x = fieldItem.Fields!.Single(field =>
            field.Field == SwShPlacementWorkflowService.LocationXField);
        Assert.Equal(storedX, float.Parse(x.Value, CultureInfo.InvariantCulture));
        Assert.Equal(storedX.ToString("G9", CultureInfo.InvariantCulture), x.Value);
    }

    [Fact]
    public void LoadMarksWr02HoeruoScaleAndRotationAsRuntimeOwned()
    {
        using var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(
            temp,
            includeRuntimeOwnedWailord: true);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        var staticObject = workflow.Objects.Single(record => record.ObjectType == "StaticObject");
        var staticFields = Assert.IsAssignableFrom<IReadOnlyList<SwShPlacementFieldValue>>(staticObject.Fields);
        var locationX = staticFields.Single(field =>
            field.Group == "Transform" && field.Label == "X");
        var rotationY = staticFields.Single(field =>
            field.Group == "Transform" && field.Label == "Rotation Y");
        var scaleX = staticFields.Single(field =>
            field.Group == "Transform" && field.Label == "Scale X");
        var spawnId = staticFields.Single(field =>
            field.Field.EndsWith(".SpawnID", StringComparison.Ordinal));
        Assert.False(locationX.IsReadOnly);
        Assert.True(rotationY.IsReadOnly);
        Assert.True(scaleX.IsReadOnly);
        Assert.Contains("wr02_hoeruo", rotationY.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(spawnId.IsReadOnly);
    }

    [Fact]
    public void LoadSummarizesAllDistinctStaticObjectSpawnIds()
    {
        using var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(
            temp,
            includeStaticObject: true,
            staticSpawnIds:
            [
                SwShPlacementTestFixtures.StaticEncounterHash,
                SwShPlacementTestFixtures.StaticEncounterHash,
                SwShPlacementTestFixtures.SecondStaticEncounterHash,
            ]);
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        var staticObject = workflow.Objects.Single(record => record.ObjectType == "StaticObject");
        Assert.Contains("0x0102030405060708", staticObject.Label, StringComparison.Ordinal);
        Assert.Contains("0x1112131415161718", staticObject.Label, StringComparison.Ordinal);
        Assert.Contains("2 spawn IDs", staticObject.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadPropagatesStaticEncounterLabelDiagnostics()
    {
        using var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(temp);
        temp.WriteBaseRomFsFile("bin/script_event_data/event_encount_data.bin", "not-an-encounter-archive");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShPlacementWorkflowService().Load(project);

        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.StartsWith("Static Encounter labels:", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadSeedsAndReusesCompactEditingSnapshotUntilCleared()
    {
        using var temp = TemporarySwShProject.Create();
        SwShPlacementTestFixtures.WriteBasePlacement(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });
        var service = new SwShPlacementWorkflowService();

        var full = service.Load(project);
        var first = service.LoadForEditing(project);
        var second = service.LoadForEditing(project);
        service.ClearMemoryCache();
        var third = service.LoadForEditing(project);

        Assert.NotSame(full, first);
        Assert.Same(first, second);
        Assert.NotSame(first, third);
        Assert.Equal(full.Objects.Count, first.Objects.Count);
        Assert.Empty(first.Categories);
        Assert.True(
            first.Objects.Sum(record => record.Fields?.Count ?? 0)
            < full.Objects.Sum(record => record.Fields?.Count ?? 0));
        Assert.All(
            first.Objects.SelectMany(record => record.Fields ?? []),
            field => Assert.True(
                !field.IsReadOnly
                || field.Field is SwShPlacementWorkflowService.LocationXField
                    or SwShPlacementWorkflowService.LocationYField
                    or SwShPlacementWorkflowService.LocationZField
                    or SwShPlacementWorkflowService.RotationYField
                    or SwShPlacementWorkflowService.ItemIdField
                    or SwShPlacementWorkflowService.QuantityField
                    or SwShPlacementWorkflowService.ChanceField
                    or "fieldItem.hash"
                    or "hiddenItem.hash"
                || field.Field.StartsWith("raw.", StringComparison.Ordinal)
                    && field.Group == "Transform"
                    && field.Label is "X" or "Y" or "Z" or "Rotation Y"));
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
