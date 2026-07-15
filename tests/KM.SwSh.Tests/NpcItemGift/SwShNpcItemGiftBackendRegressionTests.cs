// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.NpcItemGift;
using KM.SwSh.Scripts;
using KM.SwSh.Tests.Encounters;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.NpcItemGift;

public sealed class SwShNpcItemGiftBackendRegressionTests
{
    private const ushort PawnMagic64 = 0xF1E1;
    private const uint PackedConstantOpcode = 0x000000BC;
    private const int MaxSelectableItemId = 1300;

    [Fact]
    public void WrongGameLoadsDisabledAndCannotStage()
    {
        using var temp = CreateProject();
        var paths = temp.Paths with { SelectedGame = ProjectGame.Scarlet };
        var workspace = new ProjectWorkspaceService();
        var workflow = new SwShNpcItemGiftWorkflowService().Load(workspace.Open(paths));
        var session = EditSession.Start();

        var staged = new SwShNpcItemGiftEditSessionService(workspace).StageGifts(
            paths,
            Array.Empty<SwShNpcItemGiftSelection>(),
            session);

        Assert.Equal(SwShWorkflowAvailability.Disabled, workflow.Summary.Availability);
        Assert.Empty(workflow.Npcs);
        Assert.Equal(session, staged.Session);
        Assert.Contains(
            staged.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Sword", StringComparison.OrdinalIgnoreCase)
                && diagnostic.Message.Contains("Shield", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ItemOnlyHelperDefinitionsPatchOnlyTheirItemOperand()
    {
        string[] expectedItemOnlyGiftIds =
        [
            "circhester-gym-npc-tm27",
            "circhester-gym-npc-tm48",
            "fake-tears-npc-tm47",
            "flying-taxi-npc-tm06",
            "hammerlocke-soothe-bell",
            "screech-npc-tm16",
            "secret-beach-npc-tm45",
            "stow-on-side-gym-npc-tm42",
            "stow-on-side-gym-npc-tm77",
            "wild-area-star-piece",
        ];
        var itemOnlyDefinitions = SwShNpcItemGiftWorkflowService.Gifts
            .Where(definition => !definition.CanEditQuantity)
            .OrderBy(definition => definition.GiftId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedItemOnlyGiftIds, itemOnlyDefinitions.Select(definition => definition.GiftId));
        Assert.All(itemOnlyDefinitions, definition =>
        {
            Assert.Null(definition.QuantityCell);
            Assert.Empty(definition.CompanionQuantityCells);
            Assert.Equal(1, definition.Quantity);
            Assert.Single(definition.Items);
        });

        using var temp = CreateProject();
        var definition = Assert.Single(
            itemOnlyDefinitions,
            candidate => candidate.GiftId == "hammerlocke-soothe-bell");
        WriteBaseScripts(temp, definition);
        var baseScript = ReadBaseScript(temp, definition).ToArray();
        WriteExpandedCodeCell(baseScript, 10, PackConstant(777));
        WriteBaseScript(temp, definition.RelativePath, baseScript);
        var workflow = LoadWorkflow(temp);
        var gift = FindGift(workflow, definition.GiftId);
        Assert.False(gift.CanEditQuantity);
        Assert.Equal("available", gift.Status);

        var service = new SwShNpcItemGiftEditSessionService();
        var invalidQuantity = service.StageGifts(
            SwordPaths(temp),
            [CreateSelection(gift, quantity: 2, itemId: 1)],
            session: null);
        Assert.Contains(
            invalidQuantity.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("fixed helper quantity", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(invalidQuantity.Session.PendingEdits);

        var staged = service.StageGifts(
            SwordPaths(temp),
            [CreateSelection(gift, quantity: 1, itemId: 1)],
            session: null);
        var plan = service.CreateChangePlan(SwordPaths(temp), staged.Session);
        var applied = service.ApplyChangePlan(SwordPaths(temp), staged.Session, plan);

        AssertNoErrors(staged.Diagnostics);
        AssertNoErrors(plan.Diagnostics);
        AssertNoErrors(applied.Diagnostics);
        var output = ReadOutputScript(temp, definition);
        Assert.Equal(1, SwShAmxCellPatcher.ReadPackedCodeCellInt(output, definition.Items.Single().ItemCell));
        Assert.Equal(777, SwShAmxCellPatcher.ReadPackedCodeCellInt(output, 10));
    }

    [Fact]
    public void PackedZeroItemRemainsEditableAndIsRewrittenAsAPackedOperand()
    {
        using var temp = CreateProject();
        var definition = FindDefinition("lab-guide-wedgehurst-potion");
        WriteBaseScripts(temp, definition);
        var layered = ReadBaseScript(temp, definition).ToArray();
        WriteExpandedCodeCell(layered, definition.Items.Single().ItemCell, PackConstant(0));
        WriteExpandedCodeCell(
            layered,
            definition.Items.Single().CompanionItemCells.Single(),
            PackConstant(0));
        temp.WriteOutputFile(definition.RelativePath, layered);

        var gift = FindGift(LoadWorkflow(temp), definition.GiftId);
        Assert.Equal("available", gift.Status);
        Assert.Equal(0, gift.Items.Single().ItemId);
        var service = new SwShNpcItemGiftEditSessionService();

        var staged = service.StageGifts(
            SwordPaths(temp),
            [CreateSelection(gift, gift.Quantity, itemId: 1)],
            session: null);
        var plan = service.CreateChangePlan(SwordPaths(temp), staged.Session);
        var applied = service.ApplyChangePlan(SwordPaths(temp), staged.Session, plan);

        AssertNoErrors(staged.Diagnostics);
        AssertNoErrors(plan.Diagnostics);
        AssertNoErrors(applied.Diagnostics);
        var output = ReadOutputScript(temp, definition);
        Assert.Equal(
            1,
            SwShAmxCellPatcher.ReadPackedCodeCellInt(output, definition.Items.Single().ItemCell));
        Assert.Equal(
            1,
            SwShAmxCellPatcher.ReadPackedCodeCellInt(
                output,
                definition.Items.Single().CompanionItemCells.Single()));
    }

    [Fact]
    public void BaseLayoutDamageBlocksOnlyTheDamagedGift()
    {
        using var temp = CreateProject();
        var definition = FindDefinition("mum-postwick-poke-ball");
        var availableDefinition = FindDefinition("mum-wedgehurst-cheri-berry");
        WriteBaseScripts(temp, definition, availableDefinition);
        var damagedBase = ReadBaseScript(temp, definition).ToArray();
        WriteExpandedCodeCell(damagedBase, definition.QuantityCell!.Value, (ulong)definition.Quantity);
        WriteBaseScript(temp, definition.RelativePath, damagedBase);

        var workflow = LoadWorkflow(temp);
        var gift = FindGift(workflow, definition.GiftId);
        var availableGift = FindGift(workflow, availableDefinition.GiftId);
        Assert.Equal("damaged", gift.Status);
        Assert.Equal(definition.Quantity, gift.Quantity);
        Assert.Equal("available", availableGift.Status);
        var service = new SwShNpcItemGiftEditSessionService();

        var availableStage = service.StageGifts(
            SwordPaths(temp),
            [CreateSelection(
                availableGift,
                quantity: availableGift.Quantity + 1,
                itemId: availableGift.Items.Single().ItemId)],
            session: null);

        AssertNoErrors(availableStage.Diagnostics);
        Assert.Single(availableStage.Session.PendingEdits);

        var staged = service.StageGifts(
            SwordPaths(temp),
            [CreateSelection(gift, quantity: definition.Quantity + 1, itemId: gift.Items.Single().ItemId)],
            session: null);

        Assert.Empty(staged.Session.PendingEdits);
        Assert.Contains(
            staged.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("damaged", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NullGiftAndItemSelectionsFailClosedWithDiagnostics()
    {
        using var temp = CreateProject();
        var definition = FindDefinition("mum-postwick-poke-ball");
        WriteBaseScripts(temp, definition);
        var gift = FindGift(LoadWorkflow(temp), definition.GiftId);
        var service = new SwShNpcItemGiftEditSessionService();
        var missingGift = service.StageGifts(
            SwordPaths(temp),
            [null!],
            session: null);

        Assert.Empty(missingGift.Session.PendingEdits);
        Assert.Contains(
            missingGift.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("selection is missing", StringComparison.OrdinalIgnoreCase));

        var malformedSelection = new SwShNpcItemGiftSelection(
            gift.GiftId,
            gift.Quantity + 1,
            [null!]);

        var staged = service.StageGifts(
            SwordPaths(temp),
            [malformedSelection],
            session: null);

        Assert.Empty(staged.Session.PendingEdits);
        Assert.Contains(
            staged.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("missing item selection", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompanionMismatchCanBeReviewedAndRepairedWithoutChangingPrimaryValues()
    {
        using var temp = CreateProject();
        var definition = FindDefinition("lab-guide-wedgehurst-potion");
        WriteBaseScripts(temp, definition);
        var layered = ReadBaseScript(temp, definition).ToArray();
        WriteExpandedCodeCell(layered, definition.CompanionQuantityCells.Single(), PackConstant(9));
        WriteExpandedCodeCell(layered, definition.Items.Single().CompanionItemCells.Single(), PackConstant(218));
        temp.WriteOutputFile(definition.RelativePath, layered);

        var workflow = LoadWorkflow(temp);
        var gift = FindGift(workflow, definition.GiftId);
        Assert.Equal("repairable", gift.Status);
        Assert.Equal(definition.Quantity, gift.Quantity);
        Assert.Equal(definition.Items.Single().ItemId, gift.Items.Single().ItemId);
        var service = new SwShNpcItemGiftEditSessionService();

        var staged = service.StageGifts(
            SwordPaths(temp),
            [CreateSelection(gift, gift.Quantity, gift.Items.Single().ItemId)],
            session: null);
        var plan = service.CreateChangePlan(SwordPaths(temp), staged.Session);
        var applied = service.ApplyChangePlan(SwordPaths(temp), staged.Session, plan);

        AssertNoErrors(staged.Diagnostics);
        AssertNoErrors(plan.Diagnostics);
        AssertNoErrors(applied.Diagnostics);
        var output = ReadOutputScript(temp, definition);
        Assert.Equal(
            definition.Quantity,
            SwShAmxCellPatcher.ReadPackedCodeCellInt(output, definition.QuantityCell!.Value));
        Assert.Equal(
            definition.Quantity,
            SwShAmxCellPatcher.ReadPackedCodeCellInt(output, definition.CompanionQuantityCells.Single()));
        Assert.Equal(
            definition.Items.Single().ItemId,
            SwShAmxCellPatcher.ReadPackedCodeCellInt(output, definition.Items.Single().ItemCell));
        Assert.Equal(
            definition.Items.Single().ItemId,
            SwShAmxCellPatcher.ReadPackedCodeCellInt(output, definition.Items.Single().CompanionItemCells.Single()));
    }

    [Fact]
    public void NoncanonicalPayloadAndPostReviewPayloadMutationFailClosed()
    {
        using var temp = CreateProject();
        var definition = FindDefinition("mum-postwick-poke-ball");
        WriteBaseScripts(temp, definition);
        var workflow = LoadWorkflow(temp);
        var gift = FindGift(workflow, definition.GiftId);
        var service = new SwShNpcItemGiftEditSessionService();
        var staged = service.StageGifts(
            SwordPaths(temp),
            [CreateSelection(gift, quantity: 6, itemId: gift.Items.Single().ItemId)],
            session: null);
        AssertNoErrors(staged.Diagnostics);
        var edit = Assert.Single(staged.Session.PendingEdits);
        var canonicalPayload = Assert.IsType<string>(edit.NewValue);
        var noncanonicalSession = staged.Session with
        {
            PendingEdits = [edit with { NewValue = canonicalPayload.Replace("|6|", "|+6|", StringComparison.Ordinal) }],
        };

        var validation = service.Validate(SwordPaths(temp), noncanonicalSession);

        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && (diagnostic.Message.Contains("canonical", StringComparison.OrdinalIgnoreCase)
                    || diagnostic.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase)));

        var reviewedPlan = service.CreateChangePlan(SwordPaths(temp), staged.Session);
        AssertNoErrors(reviewedPlan.Diagnostics);
        var changedPayload = canonicalPayload.Replace("|6|", "|7|", StringComparison.Ordinal);
        Assert.NotEqual(canonicalPayload, changedPayload);
        var changedSession = staged.Session with
        {
            PendingEdits = [edit with { NewValue = changedPayload }],
        };

        var applied = service.ApplyChangePlan(SwordPaths(temp), changedSession, reviewedPlan);

        Assert.Contains(
            applied.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(applied.WrittenFiles);
        Assert.False(File.Exists(GetOutputPath(temp, definition)));
    }

    [Fact]
    public void ReviewedLayeredPlanTracksBaseLayoutAndRejectsBaseMutation()
    {
        using var temp = CreateProject();
        var definition = FindDefinition("mum-postwick-poke-ball");
        WriteBaseScripts(temp, definition);
        var layered = ReadBaseScript(temp, definition).ToArray();
        temp.WriteOutputFile(definition.RelativePath, layered);
        var gift = FindGift(LoadWorkflow(temp), definition.GiftId);
        var service = new SwShNpcItemGiftEditSessionService();
        var staged = service.StageGifts(
            SwordPaths(temp),
            [CreateSelection(gift, gift.Quantity + 1, gift.Items.Single().ItemId)],
            session: null);
        var plan = service.CreateChangePlan(SwordPaths(temp), staged.Session);

        AssertNoErrors(staged.Diagnostics);
        AssertNoErrors(plan.Diagnostics);
        var write = Assert.Single(plan.Writes);
        Assert.Contains(
            write.Sources,
            source => source.Layer == ProjectFileLayer.Base
                && source.RelativePath == definition.RelativePath);
        Assert.Contains(
            write.Sources,
            source => source.Layer == ProjectFileLayer.Layered
                && source.RelativePath == definition.RelativePath);

        var changedBase = ReadBaseScript(temp, definition).ToArray();
        WriteExpandedCodeCell(changedBase, 10, PackConstant(777));
        WriteBaseScript(temp, definition.RelativePath, changedBase);

        var applied = service.ApplyChangePlan(SwordPaths(temp), staged.Session, plan);

        Assert.Contains(applied.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(applied.WrittenFiles);
        Assert.Equal(layered, File.ReadAllBytes(GetOutputPath(temp, definition)));
    }

    [Fact]
    public void UnchangedOutOfRangeLayeredValuesSurviveAValidSiblingEdit()
    {
        using var temp = CreateProject();
        var definitions = SwShNpcItemGiftWorkflowService.Gifts
            .Where(definition => definition.RelativePath == "romfs/bin/script/amx/main_event_0350.amx")
            .ToArray();
        WriteBaseScripts(temp, definitions);
        var legacyDefinition = Assert.Single(
            definitions,
            definition => definition.GiftId == "mum-wedgehurst-camping-gear");
        var changedDefinition = Assert.Single(
            definitions,
            definition => definition.GiftId == "mum-wedgehurst-cheri-berry");
        var layered = ReadBaseScript(temp, legacyDefinition).ToArray();
        WriteExpandedCodeCell(layered, legacyDefinition.QuantityCell!.Value, PackConstant(1001));
        WriteExpandedCodeCell(layered, legacyDefinition.CompanionQuantityCells.Single(), PackConstant(1001));
        WriteExpandedCodeCell(layered, legacyDefinition.Items.Single().ItemCell, PackConstant(2000));
        WriteExpandedCodeCell(layered, legacyDefinition.Items.Single().CompanionItemCells.Single(), PackConstant(2000));
        temp.WriteOutputFile(legacyDefinition.RelativePath, layered);

        var workflow = LoadWorkflow(temp);
        var legacyGift = FindGift(workflow, legacyDefinition.GiftId);
        var changedGift = FindGift(workflow, changedDefinition.GiftId);
        Assert.Equal("available", legacyGift.Status);
        Assert.Equal(1001, legacyGift.Quantity);
        Assert.Equal(2000, legacyGift.Items.Single().ItemId);
        Assert.DoesNotContain(workflow.ItemOptions, option => option.ItemId == 2000);
        var service = new SwShNpcItemGiftEditSessionService();

        var staged = service.StageGifts(
            SwordPaths(temp),
            [
                CreateSelection(legacyGift, quantity: 1001, itemId: 2000),
                CreateSelection(changedGift, quantity: 2, itemId: changedGift.Items.Single().ItemId),
            ],
            session: null);
        var plan = service.CreateChangePlan(SwordPaths(temp), staged.Session);
        var applied = service.ApplyChangePlan(SwordPaths(temp), staged.Session, plan);

        AssertNoErrors(staged.Diagnostics);
        AssertNoErrors(plan.Diagnostics);
        AssertNoErrors(applied.Diagnostics);
        var output = ReadOutputScript(temp, legacyDefinition);
        Assert.Equal(1001, SwShAmxCellPatcher.ReadPackedCodeCellInt(output, legacyDefinition.QuantityCell.Value));
        Assert.Equal(1001, SwShAmxCellPatcher.ReadPackedCodeCellInt(output, legacyDefinition.CompanionQuantityCells.Single()));
        Assert.Equal(2000, SwShAmxCellPatcher.ReadPackedCodeCellInt(output, legacyDefinition.Items.Single().ItemCell));
        Assert.Equal(2000, SwShAmxCellPatcher.ReadPackedCodeCellInt(output, legacyDefinition.Items.Single().CompanionItemCells.Single()));
        Assert.Equal(2, SwShAmxCellPatcher.ReadPackedCodeCellInt(output, changedDefinition.QuantityCell!.Value));
    }

    [Fact]
    public void LateSecondPromotionFailureRestoresEveryRealNpcGiftOutput()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = CreateProject();
        var firstDefinition = FindDefinition("sonia-stow-on-side-revive");
        var secondDefinition = FindDefinition("sonia-slumbering-weald-max-revive");
        WriteBaseScripts(temp, firstDefinition, secondDefinition);
        var firstOriginal = ReadBaseScript(temp, firstDefinition).ToArray();
        var secondOriginal = ReadBaseScript(temp, secondDefinition).ToArray();
        temp.WriteOutputFile(firstDefinition.RelativePath, firstOriginal);
        temp.WriteOutputFile(secondDefinition.RelativePath, secondOriginal);
        var workflow = LoadWorkflow(temp);
        var firstGift = FindGift(workflow, firstDefinition.GiftId);
        var secondGift = FindGift(workflow, secondDefinition.GiftId);
        var service = new SwShNpcItemGiftEditSessionService();
        var staged = service.StageGifts(
            SwordPaths(temp),
            [
                CreateSelection(firstGift, quantity: 7, itemId: firstGift.Items.Single().ItemId),
                CreateSelection(secondGift, quantity: 9, itemId: secondGift.Items.Single().ItemId),
            ],
            session: null);
        var plan = service.CreateChangePlan(SwordPaths(temp), staged.Session);
        AssertNoErrors(staged.Diagnostics);
        AssertNoErrors(plan.Diagnostics);
        Assert.Equal(2, plan.Writes.Count);

        using var lockedSecondTarget = new FileStream(
            GetOutputPath(temp, secondDefinition),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var applied = service.ApplyChangePlan(SwordPaths(temp), staged.Session, plan);

        Assert.Contains(applied.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(applied.WrittenFiles);
        Assert.Equal(firstOriginal, File.ReadAllBytes(GetOutputPath(temp, firstDefinition)));
        Assert.Equal(secondOriginal, File.ReadAllBytes(GetOutputPath(temp, secondDefinition)));
    }

    private static TemporarySwShProject CreateProject()
    {
        var temp = TemporarySwShProject.Create();
        WriteItemOptionsFixture(temp);
        SwShEncounterTestFixtures.WriteSelectedGameNpdm(temp, ProjectGame.Sword);
        return temp;
    }

    private static void WriteItemOptionsFixture(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTable(
                Enumerable.Range(0, MaxSelectableItemId + 1)
                    .Select(itemId => new ItemFixtureRecord(
                        itemId,
                        itemId,
                        0,
                        0,
                        0,
                        SwShItemPouch.Items))
                    .ToArray()));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames(
                Enumerable.Range(0, MaxSelectableItemId + 1)
                    .Select(itemId => itemId == 0 ? "None" : $"Selectable {itemId}")
                    .ToArray()));
    }

    private static void WriteBaseScripts(
        TemporarySwShProject temp,
        params SwShNpcItemGiftDefinition[] definitions)
    {
        foreach (var group in definitions.GroupBy(definition => definition.RelativePath, StringComparer.Ordinal))
        {
            WriteBaseScript(temp, group.Key, CreateExpandedGiftScript(group.ToArray()));
        }
    }

    private static byte[] CreateExpandedGiftScript(IReadOnlyList<SwShNpcItemGiftDefinition> definitions)
    {
        var ownedCells = definitions
            .SelectMany(definition => definition.Items.Select(item => item.ItemCell)
                .Concat(definition.Items.SelectMany(item => item.CompanionItemCells))
                .Concat(definition.CompanionQuantityCells)
                .Concat(definition.QuantityCell is int quantityCell ? [quantityCell] : []))
            .ToArray();
        var cells = new ulong[Math.Max(ownedCells.Max(), 10) + 1];
        foreach (var definition in definitions)
        {
            if (definition.QuantityCell is int quantityCell)
            {
                cells[quantityCell] = PackConstant(definition.Quantity);
            }

            foreach (var companionQuantityCell in definition.CompanionQuantityCells)
            {
                cells[companionQuantityCell] = PackConstant(definition.Quantity);
            }

            foreach (var item in definition.Items)
            {
                cells[item.ItemCell] = PackConstant(item.ItemId);
                foreach (var companionItemCell in item.CompanionItemCells)
                {
                    cells[companionItemCell] = PackConstant(item.ItemId);
                }
            }
        }

        return CreateExpandedAmx(cells);
    }

    private static byte[] CreateExpandedAmx(IReadOnlyList<ulong> codeCells)
    {
        const int headerSize = 0x38;
        const int cellSize = 8;
        var dat = headerSize + codeCells.Count * cellSize;
        var amx = new byte[dat];
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x00), amx.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(amx.AsSpan(0x04), PawnMagic64);
        amx[0x06] = 12;
        amx[0x07] = 14;
        BinaryPrimitives.WriteInt16LittleEndian(amx.AsSpan(0x08), 0);
        BinaryPrimitives.WriteInt16LittleEndian(amx.AsSpan(0x0A), 8);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x0C), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x14), dat);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x18), dat);
        for (var index = 0; index < codeCells.Count; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                amx.AsSpan(headerSize + index * cellSize),
                codeCells[index]);
        }

        return amx;
    }

    private static SwShNpcItemGiftDefinition FindDefinition(string giftId)
    {
        return Assert.Single(
            SwShNpcItemGiftWorkflowService.GetDefinitionsForGame(ProjectGame.Sword),
            definition => definition.GiftId == giftId);
    }

    private static SwShNpcItemGiftWorkflow LoadWorkflow(TemporarySwShProject temp)
    {
        return new SwShNpcItemGiftWorkflowService().Load(
            new ProjectWorkspaceService().Open(SwordPaths(temp)));
    }

    private static SwShNpcItemGiftRecord FindGift(SwShNpcItemGiftWorkflow workflow, string giftId)
    {
        return Assert.Single(
            workflow.Npcs.SelectMany(npc => npc.Gifts),
            gift => gift.GiftId == giftId);
    }

    private static SwShNpcItemGiftSelection CreateSelection(
        SwShNpcItemGiftRecord gift,
        int quantity,
        int itemId)
    {
        var item = Assert.Single(gift.Items);
        return new SwShNpcItemGiftSelection(
            gift.GiftId,
            quantity,
            [new SwShNpcItemGiftItemSelection(item.SlotId, itemId)]);
    }

    private static byte[] ReadBaseScript(
        TemporarySwShProject temp,
        SwShNpcItemGiftDefinition definition)
    {
        return File.ReadAllBytes(Path.Combine(
            temp.BaseRomFsPath,
            definition.RelativePath["romfs/".Length..].Replace('/', Path.DirectorySeparatorChar)));
    }

    private static byte[] ReadOutputScript(
        TemporarySwShProject temp,
        SwShNpcItemGiftDefinition definition)
    {
        return File.ReadAllBytes(GetOutputPath(temp, definition));
    }

    private static string GetOutputPath(
        TemporarySwShProject temp,
        SwShNpcItemGiftDefinition definition)
    {
        return Path.Combine(
            temp.OutputRootPath,
            definition.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void WriteBaseScript(
        TemporarySwShProject temp,
        string relativePath,
        byte[] contents)
    {
        temp.WriteBaseRomFsFile(relativePath["romfs/".Length..], contents);
    }

    private static void WriteExpandedCodeCell(byte[] amx, int cell, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(amx.AsSpan(0x38 + cell * 8), value);
    }

    private static ulong PackConstant(int value)
    {
        return ((ulong)(uint)value << 32) | PackedConstantOpcode;
    }

    private static ProjectPaths SwordPaths(TemporarySwShProject temp)
    {
        return temp.Paths with { SelectedGame = ProjectGame.Sword };
    }

    private static void AssertNoErrors(IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
