// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Formats.SwSh;
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
        var item = result.Workflow.Items[1];
        Assert.Equal(450, item.BuyPrice);
        Assert.Equal(225, item.SellPrice);
    }

    [Fact]
    public void UpdateFieldAddsPendingSellPriceAndPreviewsDerivedBuyPrice()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.SellPriceField,
            value: "175");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(SwShItemsEditSessionService.SellPriceField, edit.Field);
        Assert.Equal("175", edit.NewValue);
        var item = result.Workflow.Items[1];
        Assert.Equal(350, item.BuyPrice);
        Assert.Equal(175, item.SellPrice);
    }

    [Fact]
    public void UpdateFieldReplacesExistingPendingEditForSameStoredItemField()
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
            field: SwShItemsWorkflowService.SellPriceField,
            value: "300");

        var edit = Assert.Single(secondResult.Session.PendingEdits);
        Assert.Equal(SwShItemsEditSessionService.SellPriceField, edit.Field);
        Assert.Equal("300", edit.NewValue);
        var item = secondResult.Workflow.Items[1];
        Assert.Equal(600, item.BuyPrice);
        Assert.Equal(300, item.SellPrice);
    }

    [Fact]
    public void UpdateFieldKeepsSeparatePendingEditsForDifferentStoredItemFields()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var buyResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var wattsResult = service.UpdateField(
            temp.Paths,
            buyResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.WattsPriceField,
            value: "40");

        Assert.Equal(2, wattsResult.Session.PendingEdits.Count);
        Assert.Contains(
            wattsResult.Session.PendingEdits,
            edit => edit.Field == SwShItemsEditSessionService.BuyPriceField);
        Assert.Contains(
            wattsResult.Session.PendingEdits,
            edit => edit.Field == SwShItemsEditSessionService.WattsPriceField);
        var item = wattsResult.Workflow.Items[1];
        Assert.Equal(450, item.BuyPrice);
        Assert.Equal(225, item.SellPrice);
        Assert.Equal(40, item.WattsPrice);
    }

    [Fact]
    public void UpdateFieldAddsPendingMetadataEditAndPreviewsInspectorDetails()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.PouchField,
            value: "4");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(SwShItemsEditSessionService.PouchField, edit.Field);
        Assert.Equal("4", edit.NewValue);
        var item = result.Workflow.Items[1];
        Assert.Equal("Items", item.Category);
        Assert.Equal(4, item.Metadata.Pouch);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Inventory").Details,
            detail => detail.Label == "Pouch" && detail.Value == "Items (4)");
    }

    [Fact]
    public void UpdateFieldAddsPendingMachineMoveEditAndPreviewsInspectorDetails()
    {
        using var temp = CreateEditableMachineProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.MachineMoveIdField,
            value: "85");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(SwShItemsEditSessionService.MachineMoveIdField, edit.Field);
        Assert.Equal("85", edit.NewValue);
        var item = result.Workflow.Items[1];
        Assert.Equal(85, item.Metadata.MachineMoveId);
        Assert.Equal("Thunderbolt", item.Metadata.MachineMoveName);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Inventory").Details,
            detail => detail.Label == "Machine" && detail.Value == "TM10 (slot 10) -> Thunderbolt (85)");
    }

    [Fact]
    public void UpdateFieldAddsPendingNamedBehaviorEditAndPreviewsInspectorDetails()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.CureBurnField,
            value: "1");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(SwShItemsEditSessionService.CureBurnField, edit.Field);
        Assert.Equal("1", edit.NewValue);
        var item = result.Workflow.Items[1];
        Assert.Equal(0x04, item.Metadata.CureStatusFlags);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Battle").Details,
            detail => detail.Label == "Cures status" && detail.Value == "Burn");
    }

    [Fact]
    public void UpdateFieldRejectsMachineMoveForNonMachineItem()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.MachineMoveIdField,
            value: "85");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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
            field: "category",
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
    public void CreateChangePlanListsRealItemsTargetFileForPendingBuyPrice()
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
        Assert.Equal(SwShItemsWorkflowService.ItemDataPath, write.TargetRelativePath);
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
            SwShItemsWorkflowService.ItemDataPath,
            SwShItemTestFixtures.CreateItemTable(
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(1, 1, 500, 25, 7, SwShItemPouch.Medicine),
                new ItemFixtureRecord(2, 2, 200, 10, 5, SwShItemPouch.Medicine)));
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
    public void ApplyChangePlanWritesItemDataToOutputRoot()
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
        Assert.Equal(SwShItemsWorkflowService.ItemDataPath, writtenFile.RelativePath);
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        Assert.True(File.Exists(outputPath));
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Equal(450u, item.BuyPrice);
        Assert.Equal(15u, item.WattsPrice);
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesDerivedSellPriceToStoredBuyPrice()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.SellPriceField,
            value: "175");
        var changePlan = service.CreateChangePlan(temp.Paths, editResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, editResult.Session, changePlan);

        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Equal(350u, item.BuyPrice);
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesCombinedItemPriceEditsToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var buyResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");
        var wattsResult = service.UpdateField(
            temp.Paths,
            buyResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.WattsPriceField,
            value: "40");
        var changePlan = service.CreateChangePlan(temp.Paths, wattsResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, wattsResult.Session, changePlan);

        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Single(changePlan.Writes);
        Assert.Equal(450u, item.BuyPrice);
        Assert.Equal(40u, item.WattsPrice);
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesItemMetadataToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var pouchResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.PouchField,
            value: "4");
        var healResult = service.UpdateField(
            temp.Paths,
            pouchResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.HealAmountField,
            value: "254");
        var evResult = service.UpdateField(
            temp.Paths,
            healResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.EvAttackField,
            value: "-10");
        var canUseResult = service.UpdateField(
            temp.Paths,
            evResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.CanUseOnPokemonField,
            value: "0");
        var changePlan = service.CreateChangePlan(temp.Paths, canUseResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, canUseResult.Session, changePlan);

        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Single(changePlan.Writes);
        Assert.Equal(SwShItemPouch.Items, item.Pouch);
        Assert.Equal(254, item.HealAmount);
        Assert.Equal(-10, item.EvAttack);
        Assert.False(item.CanUseOnPokemon);
        Assert.Equal(300u, item.BuyPrice);
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesMachineMoveToOutputRoot()
    {
        using var temp = CreateEditableMachineProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.MachineMoveIdField,
            value: "85");
        var changePlan = service.CreateChangePlan(temp.Paths, editResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, editResult.Session, changePlan);

        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Single(changePlan.Writes);
        Assert.Equal(10, item.MachineSlot);
        Assert.Equal((ushort)85, item.MachineMoveId);
        Assert.Equal(SwShItemPouch.TMs, item.Pouch);
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesNamedBehaviorFieldsToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var cureResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.CureBurnField,
            value: "1");
        var levelUpResult = service.UpdateField(
            temp.Paths,
            cureResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.LevelUpItemField,
            value: "1");
        var boostResult = service.UpdateField(
            temp.Paths,
            levelUpResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.AttackBoostField,
            value: "6");
        var restoreResult = service.UpdateField(
            temp.Paths,
            boostResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.RestorePpFlagField,
            value: "1");
        var evFlagResult = service.UpdateField(
            temp.Paths,
            restoreResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.SpecialAttackEvFlagField,
            value: "1");
        var changePlan = service.CreateChangePlan(temp.Paths, evFlagResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, evFlagResult.Session, changePlan);

        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Single(changePlan.Writes);
        Assert.Equal(0x04, item.CureStatusFlags);
        Assert.Equal(0x64, item.Boost0);
        Assert.Equal(0x85, item.UseFlags1);
        Assert.Equal(300u, item.BuyPrice);
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
        var staleWrite = Assert.Single(changePlan.Writes) with { TargetRelativePath = "romfs/bin/pml/item/stale.dat" };
        var stalePlan = new ChangePlan(changePlan.SessionId, [staleWrite], changePlan.Diagnostics);

        var applyResult = service.ApplyChangePlan(temp.Paths, editResult.Session, stalePlan);

        Assert.Empty(applyResult.WrittenFiles);
        Assert.Contains(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat")));
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

    [Fact]
    public void UpdateFieldsRollsBackTheWholeBatchAndPreservesExistingPreviewOnError()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var existing = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var result = service.UpdateFields(
            temp.Paths,
            existing.Session,
            [
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.WattsPriceField, "40"),
                new SwShItemFieldUpdate(999, SwShItemsWorkflowService.PouchField, "4"),
            ]);

        Assert.Equal(existing.Session, result.Session);
        Assert.Single(result.Session.PendingEdits);
        Assert.Equal(450, result.Workflow.Items[1].BuyPrice);
        Assert.Equal(15, result.Workflow.Items[1].WattsPrice);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldsPreservesForeignPendingEditsEvenWhenTheirFieldNameMatchesItems()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var foreign = new PendingEdit(
            "workflow.foreign",
            "Foreign edit",
            [],
            RecordId: "not-an-item",
            Field: SwShItemsWorkflowService.BuyPriceField,
            NewValue: "999");
        var session = service.StartSession() with { PendingEdits = [foreign] };

        var result = service.UpdateFields(
            temp.Paths,
            session,
            [new SwShItemFieldUpdate(1, SwShItemsWorkflowService.WattsPriceField, "40")]);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Contains(foreign, result.Session.PendingEdits);
        Assert.Equal(300, result.Workflow.Items[1].BuyPrice);
        Assert.Equal(40, result.Workflow.Items[1].WattsPrice);
    }

    [Fact]
    public void SharedRowAliasesCanonicalizeReplacementAndPreviewTogether()
    {
        using var temp = CreateEditableAliasProject();
        var service = new SwShItemsEditSessionService();
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 2,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "451");

        var edit = Assert.Single(first.Session.PendingEdits);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal(451, first.Workflow.Items[1].BuyPrice);
        Assert.Equal(451, first.Workflow.Items[2].BuyPrice);

        var replacement = service.UpdateField(
            temp.Paths,
            first.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.SellPriceField,
            value: "225");

        Assert.Single(replacement.Session.PendingEdits);
        Assert.Equal(450, replacement.Workflow.Items[1].BuyPrice);
        Assert.Equal(450, replacement.Workflow.Items[2].BuyPrice);
        Assert.Equal(225, replacement.Workflow.Items[1].SellPrice);
        Assert.Equal(225, replacement.Workflow.Items[2].SellPrice);
    }

    [Fact]
    public void UpdateFieldsCanonicalizesACompatibleOddBuySellPair()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.BuyPriceField, "451"),
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.SellPriceField, "225"),
            ]);

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(SwShItemsWorkflowService.BuyPriceField, edit.Field);
        Assert.Equal("451", edit.NewValue);
        Assert.Equal(451, result.Workflow.Items[1].BuyPrice);
        Assert.Equal(225, result.Workflow.Items[1].SellPrice);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldsRejectsAnIncompatibleBuySellPairAtomically()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.BuyPriceField, "451"),
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.SellPriceField, "226"),
            ]);

        Assert.False(result.Session.HasPendingChanges);
        Assert.Equal(300, result.Workflow.Items[1].BuyPrice);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldsKeepsNonOverlappingPackedBitsAndMigratesTheLegacyBattlePouchField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            null,
            [
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.CureBurnField, "1"),
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.CureFreezeField, "1"),
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.FieldFlagsField, "1"),
            ]);

        Assert.Equal(3, result.Session.PendingEdits.Count);
        Assert.Contains(
            result.Session.PendingEdits,
            edit => edit.Field == SwShItemsWorkflowService.BattlePouchField);
        Assert.Equal(0x0C, result.Workflow.Items[1].Metadata.CureStatusFlags);
        Assert.Equal(1, result.Workflow.Items[1].Metadata.BattlePouch);
        Assert.Equal(1, result.Workflow.Items[1].FieldValues[SwShItemsWorkflowService.BattlePouchField]);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(SwShItemsWorkflowService.CureStatusFlagsField)]
    [InlineData(SwShItemsWorkflowService.UseFlags1Field)]
    [InlineData(SwShItemsWorkflowService.UseFlags2Field)]
    public void UpdateFieldRejectsAdvertisedRawMasksWithTheReadOnlyReason(string field)
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(temp.Paths, null, 1, field, "1");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message == SwShItemsWorkflowService.RawFlagsReadOnlyReason);
    }

    [Fact]
    public void UpdateFieldsRelinksToAnOwnedMachineSlotBeforeWritingTheMove()
    {
        using var temp = CreateRelinkableMachineProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            null,
            [
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.MachineMoveIdField, "85"),
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.GroupIndexField, "11"),
            ]);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(11, result.Workflow.Items[1].Metadata.MachineSlot);
        Assert.Equal(85, result.Workflow.Items[1].Metadata.MachineMoveId);
        var plan = service.CreateChangePlan(temp.Paths, result.Session);
        var apply = service.ApplyChangePlan(temp.Paths, result.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShItemTable.Parse(File.ReadAllBytes(GetOutputItemPath(temp))).Records[1];
        Assert.Equal(11, output.MachineSlot);
        Assert.Equal((ushort)85, output.MachineMoveId);
    }

    [Fact]
    public void UpdateFieldsReconcilesAStaleSharedRowMachineEditToTheFinalSlotOwner()
    {
        using var temp = CreateSharedRowMachineOwnerProject();
        var service = new SwShItemsEditSessionService();
        var existing = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShItemsWorkflowService.MachineMoveIdField,
            "85");
        Assert.DoesNotContain(existing.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var rejected = service.UpdateFields(
            temp.Paths,
            existing.Session,
            [
                new SwShItemFieldUpdate(2, SwShItemsWorkflowService.GroupIndexField, "11"),
                new SwShItemFieldUpdate(2, SwShItemsWorkflowService.MachineMoveIdField, "500"),
            ]);
        Assert.Equal(existing.Session, rejected.Session);
        Assert.Equal(10, rejected.Workflow.Items[1].Metadata.MachineSlot);
        Assert.Equal(85, rejected.Workflow.Items[1].Metadata.MachineMoveId);

        var result = service.UpdateFields(
            temp.Paths,
            existing.Session,
            [
                new SwShItemFieldUpdate(2, SwShItemsWorkflowService.GroupIndexField, "11"),
                new SwShItemFieldUpdate(2, SwShItemsWorkflowService.MachineMoveIdField, "345"),
            ]);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(2, result.Session.PendingEdits.Count);
        var machineEdit = result.Session.PendingEdits.Single(
            edit => edit.Field == SwShItemsWorkflowService.MachineMoveIdField);
        Assert.Equal("2", machineEdit.RecordId);
        Assert.Equal("345", machineEdit.NewValue);
        Assert.Null(result.Workflow.Items[1].Metadata.MachineSlot);
        Assert.Equal(11, result.Workflow.Items[2].Metadata.MachineSlot);
        Assert.Equal(345, result.Workflow.Items[2].Metadata.MachineMoveId);

        var plan = service.CreateChangePlan(temp.Paths, result.Session);
        var apply = service.ApplyChangePlan(temp.Paths, result.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShItemTable.Parse(File.ReadAllBytes(GetOutputItemPath(temp))).Records;
        Assert.Null(output[1].MachineSlot);
        Assert.Equal(11, output[2].MachineSlot);
        Assert.Equal((ushort)345, output[2].MachineMoveId);
    }

    [Fact]
    public void UpdateFieldsRejectsUnlinkPlusMoveAndRestoresTheBaseItemName()
    {
        using var temp = CreateEditableMachineProject();
        var service = new SwShItemsEditSessionService();

        var rejected = service.UpdateFields(
            temp.Paths,
            null,
            [
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.GroupTypeField, "0"),
                new SwShItemFieldUpdate(1, SwShItemsWorkflowService.MachineMoveIdField, "85"),
            ]);
        Assert.False(rejected.Session.HasPendingChanges);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var unlink = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShItemsWorkflowService.GroupTypeField,
            "0");
        Assert.DoesNotContain(unlink.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("TM10 Magical Leaf", unlink.Workflow.Items[1].Name);
        Assert.Null(unlink.Workflow.Items[1].Metadata.MachineSlot);
    }

    [Fact]
    public void UpdateFieldRejectsMachineSlotConvergenceAndInvalidMoveOptions()
    {
        using var temp = CreateTwoMachineProject();
        var service = new SwShItemsEditSessionService();

        var convergence = service.UpdateField(
            temp.Paths,
            null,
            2,
            SwShItemsWorkflowService.GroupIndexField,
            "10");
        Assert.False(convergence.Session.HasPendingChanges);
        Assert.Contains(convergence.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var invalidMove = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShItemsWorkflowService.MachineMoveIdField,
            "500");
        Assert.False(invalidMove.Session.HasPendingChanges);
        Assert.Contains(invalidMove.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyRejectsReasonChangesAndSourceDrift()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShItemsWorkflowService.BuyPriceField,
            "450");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var changedReason = plan with
        {
            Writes = [Assert.Single(plan.Writes) with { Reason = "Different reviewed change" }],
        };

        var reasonResult = service.ApplyChangePlan(temp.Paths, update.Session, changedReason);
        Assert.Empty(reasonResult.WrittenFiles);

        var basePath = Path.Combine(temp.BaseRomFsPath, "bin", "pml", "item", "item.dat");
        var changedSource = File.ReadAllBytes(basePath);
        changedSource[^1] ^= 0x01;
        File.WriteAllBytes(basePath, changedSource);
        var sourceResult = service.ApplyChangePlan(temp.Paths, update.Session, plan);
        Assert.Empty(sourceResult.WrittenFiles);
        Assert.Contains(sourceResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyWriteFailurePreservesTheExistingLayeredItemTable()
    {
        using var temp = CreateEditableProject();
        var existing = File.ReadAllBytes(Path.Combine(temp.BaseRomFsPath, "bin", "pml", "item", "item.dat"));
        temp.WriteOutputFile(SwShItemsWorkflowService.ItemDataPath, existing);
        var service = new SwShItemsEditSessionService((tempPath, contents) =>
        {
            File.WriteAllBytes(tempPath, contents[..Math.Min(16, contents.Length)]);
            throw new IOException("Simulated Items output failure.");
        });
        var update = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShItemsWorkflowService.BuyPriceField,
            "450");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(existing, File.ReadAllBytes(GetOutputItemPath(temp)));
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(GetOutputItemPath(temp))!,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ApplyPreservesOpaqueAndTrailingSourceBytes()
    {
        var table = SwShItemTestFixtures.CreateItemTable(
            new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
            new ItemFixtureRecord(1, 1, 300, 15, 3, SwShItemPouch.Medicine));
        var source = table.Concat(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }).ToArray();
        using var temp = CreateProject(source, "None", "Potion");
        var service = new SwShItemsEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShItemsWorkflowService.BuyPriceField,
            "450");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = File.ReadAllBytes(GetOutputItemPath(temp));
        Assert.Equal(source.Length, output.Length);
        Assert.Equal(source[^4..], output[^4..]);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }

    private static TemporarySwShProject CreateEditableMachineProject()
    {
        var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTableWithMachineMoves(
                new Dictionary<int, int> { [10] = 345 },
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(
                    1,
                    1,
                    0,
                    0,
                    0,
                    SwShItemPouch.TMs,
                    FieldUseType: 2,
                    GroupType: 4,
                    GroupIndex: 10)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("None", "TM10 Magical Leaf"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedText(346, (85, "Thunderbolt"), (345, "Magical Leaf")));
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }

    private static TemporarySwShProject CreateEditableAliasProject()
    {
        return CreateProject(
            SwShItemTestFixtures.CreateItemTable(
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(1, 1, 300, 15, 7, SwShItemPouch.Medicine),
                new ItemFixtureRecord(2, 1, 300, 15, 7, SwShItemPouch.Medicine)),
            "None",
            "Potion",
            "Potion Alias");
    }

    private static TemporarySwShProject CreateRelinkableMachineProject()
    {
        return CreateProject(
            SwShItemTestFixtures.CreateItemTableWithMachineEntries(
                new Dictionary<int, (int ItemId, int MoveId)>
                {
                    [10] = (0, 345),
                    [11] = (1, 345),
                },
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(
                    1,
                    1,
                    0,
                    0,
                    0,
                    SwShItemPouch.TMs,
                    FieldUseType: 2,
                    GroupType: 4,
                    GroupIndex: 10)),
            "None",
            "TM10 Magical Leaf");
    }

    private static TemporarySwShProject CreateTwoMachineProject()
    {
        return CreateProject(
            SwShItemTestFixtures.CreateItemTableWithMachineEntries(
                new Dictionary<int, (int ItemId, int MoveId)>
                {
                    [10] = (1, 345),
                    [11] = (2, 85),
                },
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(
                    1,
                    1,
                    0,
                    0,
                    0,
                    SwShItemPouch.TMs,
                    FieldUseType: 2,
                    GroupType: 4,
                    GroupIndex: 10),
                new ItemFixtureRecord(
                    2,
                    2,
                    0,
                    0,
                    0,
                    SwShItemPouch.TMs,
                    FieldUseType: 2,
                    GroupType: 4,
                    GroupIndex: 11)),
            "None",
            "TM10 Magical Leaf",
            "TM11 Thunderbolt");
    }

    private static TemporarySwShProject CreateSharedRowMachineOwnerProject()
    {
        return CreateProject(
            SwShItemTestFixtures.CreateItemTableWithMachineEntries(
                new Dictionary<int, (int ItemId, int MoveId)>
                {
                    [10] = (1, 345),
                    [11] = (2, 85),
                },
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(
                    1,
                    1,
                    0,
                    0,
                    0,
                    SwShItemPouch.TMs,
                    FieldUseType: 2,
                    GroupType: 4,
                    GroupIndex: 10),
                new ItemFixtureRecord(
                    2,
                    1,
                    0,
                    0,
                    0,
                    SwShItemPouch.TMs,
                    FieldUseType: 2,
                    GroupType: 4,
                    GroupIndex: 10)),
            "None",
            "TM10 Magical Leaf",
            "TM11 Thunderbolt");
    }

    private static TemporarySwShProject CreateProject(byte[] itemTable, params string[] itemNames)
    {
        var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("bin/pml/item/item.dat", itemTable);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames(itemNames));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedText(346, (85, "Thunderbolt"), (345, "Magical Leaf")));
        temp.WriteBaseExeFsFile("main", "base-main");
        return temp;
    }

    private static string GetOutputItemPath(TemporarySwShProject temp)
    {
        return Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
    }

    private static byte[] CreateIndexedText(int count, params (int Index, string Text)[] entries)
    {
        var values = Enumerable.Repeat(string.Empty, count).ToArray();
        foreach (var (index, text) in entries)
        {
            values[index] = text;
        }

        return SwShItemTestFixtures.CreateItemNames(values);
    }
}
