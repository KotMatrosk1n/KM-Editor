// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.ExeFs;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using System.Globalization;
using Xunit;

namespace KM.SwSh.Tests.RoyalCandy;

public sealed class SwShRoyalCandyWorkflowServiceTests
{
    [Fact]
    public void LoadBuildsRealPreflightFromProjectFiles()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.Available, workflow.Summary.Availability);
        Assert.Equal(3, workflow.Workflows.Count);
        var unlimited = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited");
        Assert.Equal("Unlimited Royal Candy", unlimited.Name);
        Assert.Equal("available", unlimited.Status);
        Assert.Equal("unlimited", unlimited.Mode);
        Assert.Equal(1128, unlimited.ItemId);
        Assert.Equal(50, unlimited.TemplateItemId);
        Assert.Empty(unlimited.LevelCaps);
        Assert.Equal(ProjectFileLayer.Base, unlimited.Provenance.SourceLayer);
        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Equal("Royal Candy with Story Limits", storyLimits.Name);
        Assert.Equal("storyLimits", storyLimits.Mode);
        Assert.Equal(25, storyLimits.LevelCaps.Count);
        Assert.Equal("Hop 004/005/006", storyLimits.LevelCaps[0].Label);
        Assert.Equal(10, storyLimits.LevelCaps[0].LevelCap);
        Assert.Equal("Gordie 135", storyLimits.LevelCaps.Single(cap => cap.LevelCap == 52).Label);
        Assert.Equal("workAtLeast", storyLimits.LevelCaps.Single(cap => cap.LevelCap == 20).ProgressKind);
        Assert.Equal(530, storyLimits.LevelCaps.Single(cap => cap.LevelCap == 20).WorkMinimum);
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":item-data", StringComparison.Ordinal) && check.Status == "Pass");
        Assert.Contains(
            workflow.Checks,
            check => check.CheckId.EndsWith(":item-data-stride", StringComparison.Ordinal)
                && check.Status == "Pass"
                && check.Message.Contains("1,129 item id", StringComparison.Ordinal));
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":royal-candy-row", StringComparison.Ordinal) && check.Status == "Pass");
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":message-text-sets", StringComparison.Ordinal) && check.Status == "Pass");
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":game-flavor", StringComparison.Ordinal) && check.Message.Contains("Pokemon Sword", StringComparison.Ordinal));
        Assert.Contains(workflow.Checks, check => check.CheckId.Contains("patch-code-cave", StringComparison.Ordinal) && check.Status == "Pass");
        Assert.Contains(workflow.Outputs, output => output.WorkflowId == unlimited.WorkflowId && output.RelativePath == SwShRoyalCandyWorkflowService.ItemPath);
        Assert.Contains(workflow.Outputs, output => output.WorkflowId == unlimited.WorkflowId && output.RelativePath == "romfs/bin/message/English/common/itemname.dat");
        Assert.Contains(workflow.Outputs, output => output.WorkflowId == unlimited.WorkflowId && output.RelativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath);
        Assert.Equal(3, workflow.Stats.TotalWorkflowCount);
        Assert.True(workflow.Stats.TotalCheckCount >= 40);
        Assert.Equal(0, workflow.Stats.FailCount);
        Assert.True(workflow.Stats.SourceFileCount >= 10);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void CreateLevelCapsUsesVersionSpecificGymFourLabels()
    {
        var swordCaps = SwShRoyalCandyWorkflowService.CreateLevelCaps("Sword");
        var shieldCaps = SwShRoyalCandyWorkflowService.CreateLevelCaps("Shield");

        var swordGymFour = swordCaps.Single(cap => cap.LevelCap == 42);
        var shieldGymFour = shieldCaps.Single(cap => cap.LevelCap == 42);

        Assert.Equal("Bea 077", swordGymFour.Label);
        Assert.Equal("Allister 078", shieldGymFour.Label);
        Assert.Equal("0xC07B67FC3148B754", swordGymFour.ProgressHash);
        Assert.Equal(swordGymFour.ProgressHash, shieldGymFour.ProgressHash);
    }

    [Fact]
    public void LoadUsesShieldStoryCapLabelsForShieldProjects()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x01008DB008C2C000));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Contains(storyLimits.LevelCaps, cap => cap.Label == "Allister 078");
        Assert.DoesNotContain(storyLimits.LevelCaps, cap => cap.Label == "Bea 077");
    }

    [Fact]
    public void LoadReflectsInstalledCustomStoryLevelCaps()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var defaultCaps = SwShRoyalCandyWorkflowService.CreateLevelCaps("Sword");
        var customCaps = defaultCaps
            .Select(cap => new SwShRoyalCandyStoryLevelCap(
                LevelCap: cap.LevelCap + 1,
                ProgressHash: ulong.Parse(cap.ProgressHash[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                Label: cap.Label,
                ProgressKind: cap.ProgressKind == "workAtLeast"
                    ? SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast
                    : SwShRoyalCandyStoryLevelCapProgressKind.Flag,
                WorkMinimum: cap.WorkMinimum ?? 0))
            .ToArray();
        temp.WriteOutputFile(
            "exefs/main",
            SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(CreateCompatibleNso(), customCaps));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Equal("installed", storyLimits.Status);
        Assert.Equal(11, storyLimits.LevelCaps.Single(cap => cap.Slot == 0).LevelCap);
        Assert.Equal(43, storyLimits.LevelCaps.Single(cap => cap.Label == "Bea 077").LevelCap);
        Assert.Equal(91, storyLimits.LevelCaps.Single(cap => cap.Label == "Leon 149/189/190").LevelCap);
    }

    [Fact]
    public void LoadBlocksInstallWhenRequiredInputsAreMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/placeholder.bin", [0x01]);
        temp.WriteBaseExeFsFile("placeholder.bin", [0x02]);
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var unlimited = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited");
        Assert.Equal("blocked", unlimited.Status);
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":item-data", StringComparison.Ordinal) && check.Status == "Fail");
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":message-text-sets", StringComparison.Ordinal) && check.Status == "Fail");
        Assert.True(workflow.Stats.FailCount >= 8);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.royalCandy");
    }

    [Fact]
    public void LoadMarksMatchingRoyalCandyVariantInstalled()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteOutputFile(
            "romfs/bin/message/English/common/iteminfo.dat",
            CreateTextTable(
                1128,
                (1128, "A candy packed with strange energy. Its full power follows the current story limit.")));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var unlimited = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited");
        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Equal("blocked", unlimited.Status);
        Assert.Equal("installed", storyLimits.Status);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("Royal Candy with Story Limits is installed", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadFlagsMixedRoyalCandyTextAndExeFsVariants()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteOutputFile(
            "romfs/bin/message/English/common/iteminfo.dat",
            CreateTextTable(
                1128,
                (1128, "A candy packed with strange energy. Its full power follows the current story limit.")));
        temp.WriteOutputFile("exefs/main", SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(CreateCompatibleNso()));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var unlimited = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited");
        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Equal("blocked", unlimited.Status);
        Assert.Equal("blocked", storyLimits.Status);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("mixed Royal Candy targets", StringComparison.Ordinal)
                && diagnostic.Message.Contains("item text identifies Royal Candy with Story Limits", StringComparison.Ordinal)
                && diagnostic.Message.Contains("exefs/main identifies Unlimited Royal Candy", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadDetectsKnownLayeredOutputsForUninstallReview()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteOutputFile("exefs/main", SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(CreateCompatibleNso()));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var uninstall = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-uninstall");
        Assert.Equal("warning", uninstall.Status);
        Assert.Contains(
            workflow.Checks,
            check => check.WorkflowId == uninstall.WorkflowId
                && check.Status == "Warning"
                && check.Message.Contains("installed", StringComparison.Ordinal));
        Assert.Contains(
            workflow.Outputs,
            output => output.WorkflowId == uninstall.WorkflowId
                && output.RelativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath
                && output.Provenance.SourceLayer == ProjectFileLayer.Layered);
    }

    private static void WriteRoyalCandyBaseInputs(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemPath["romfs/".Length..],
            CreateCompactRoyalCandyItemTable());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemHashPath["romfs/".Length..],
            [0x01, 0x02, 0x03, 0x04]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            [0x05]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.NestDataPath["romfs/".Length..],
            [0x06]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.PlacementPath["romfs/".Length..],
            [0x07]);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.BagEventScriptPath["romfs/".Length..],
            CreateRoyalCandyBagEventScript());
        temp.WriteBaseRomFsFile("bin/message/English/common/iteminfo.dat", [0x09]);
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", [0x0A]);
        temp.WriteBaseExeFsFile("main", CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
        InstallEmptyBagHook(temp);
    }

    private static void InstallEmptyBagHook(TemporarySwShProject temp)
    {
        var service = new SwShBagHookEditSessionService();
        var stage = service.StageInstall(temp.Paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(temp.Paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static byte[] CreateCompactRoyalCandyItemTable()
    {
        var records = Enumerable.Range(0, 1129)
            .Select(itemId => new ItemFixtureRecord(
                itemId,
                itemId == 50 || itemId == 1128 ? 50 : 0,
                0,
                0,
                0,
                SwShItemPouch.Medicine))
            .ToArray();

        return SwShItemTestFixtures.CreateItemTable(records);
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(index => new SwShGameTextLine(
                entries.FirstOrDefault(entry => entry.index == index).value ?? string.Empty,
                Flags: 0))
            .ToArray();

        return SwShGameTextFile.Write(lines);
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x290, 8), titleId);
        return data;
    }

    private static byte[] CreateCompatibleNso()
    {
        return CreateNso(CreateCompatibleText(), [0x10], [0x20]);
    }

    private static byte[] CreateCompatibleText()
    {
        var text = new byte[0x007DDA90];
        WriteInstruction(text, 0x00747988, EncodeCmpImmediate(28, 50));
        WriteInstruction(text, 0x0074798C, EncodeConditionalBranch(0x0074798C, 0x00747A80, Arm64Condition.NE));
        WriteInstruction(text, 0x00747D44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x00747D48, EncodeConditionalBranch(0x00747D48, 0x007477E8, Arm64Condition.NE));
        WriteInstruction(text, 0x0074BA24, EncodeCmpImmediate(26, 50));
        WriteInstruction(text, 0x0074BA28, EncodeConditionalBranch(0x0074BA28, 0x0074BAD4, Arm64Condition.NE));
        WriteInstruction(text, 0x0074BDA8, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BDAC, EncodeConditionalBranch(0x0074BDAC, 0x0074B788, Arm64Condition.NE));
        WriteInstruction(text, 0x0074DFE4, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074DFE8, EncodeConditionalBranch(0x0074DFE8, 0x0074DE78, Arm64Condition.NE));
        WriteInstruction(text, 0x0074DFF8, EncodeCmpImmediate(28, 50));
        WriteInstruction(text, 0x0074DFFC, EncodeConditionalBranch(0x0074DFFC, 0x0074E16C, Arm64Condition.NE));
        WriteInstruction(text, 0x0075CEFC, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0075CF00, EncodeConditionalBranch(0x0075CF00, 0x0075CC18, Arm64Condition.NE));
        WriteInstruction(text, 0x007BB204, EncodeCmpImmediate(20, 50));
        WriteInstruction(text, 0x007BB208, EncodeConditionalBranch(0x007BB208, 0x007BB26C, Arm64Condition.NE));
        WriteInstruction(text, 0x007BB3C0, EncodeCmpImmediate(19, 50));
        WriteInstruction(text, 0x007BB3C4, EncodeConditionalBranch(0x007BB3C4, 0x007BB3EC, Arm64Condition.NE));
        WriteInstruction(text, 0x007BC1F8, EncodeCmpImmediate(8, 50));
        WriteInstruction(text, 0x007BC1FC, EncodeConditionalBranch(0x007BC1FC, 0x007BC2B4, Arm64Condition.NE));
        WriteInstruction(text, 0x00747DE0, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x00747DE4, EncodeConditionalBranch(0x00747DE4, 0x00747D4C, Arm64Condition.EQ));
        WriteInstruction(text, 0x0074BE44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BE48, EncodeConditionalBranch(0x0074BE48, 0x0074BDB0, Arm64Condition.EQ));
        WriteInstruction(text, 0x0075CCE8, EncodeCmpImmediate(27, 50));
        WriteInstruction(text, 0x0075CCEC, EncodeConditionalBranch(0x0075CCEC, 0x0075D064, Arm64Condition.EQ));
        WriteInstruction(text, 0x0075D08C, EncodeCmpImmediate(10, 50));
        WriteInstruction(text, 0x0075D090, EncodeConditionalBranch(0x0075D090, 0x0075D05C, Arm64Condition.EQ));
        WriteInstruction(text, 0x007BBFD4, EncodeCmpImmediate(23, 50));
        WriteInstruction(text, 0x007BBFD8, EncodeConditionalBranch(0x007BBFD8, 0x007BC054, Arm64Condition.EQ));
        WriteInstruction(text, 0x007BC1BC, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007BC1C4, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007B1F20, 0x2A0003E2);
        WriteInstruction(text, 0x007BAF38, 0x6B36231F);
        WriteInstruction(text, 0x007BAF3C, 0x1A963316);
        WriteInstruction(text, 0x007DDA8C, EncodeCmpImmediate(8, 0x32));
        return text;
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, 4), instruction);
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static uint EncodeConditionalBranch(int sourceOffset, int targetOffset, Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        var imm19 = delta >> 2;
        return (uint)(0x54000000 | ((imm19 & 0x7FFFF) << 5) | ((int)condition & 0xF));
    }

    private static byte[] CreateRoyalCandyBagEventScript()
    {
        const ushort pawnMagic64 = 0xF1E1;
        const short pawnFlagCompact = 0x0004;
        const short defSize = 12;
        const int cellSize = 8;
        const int nativeCount = 77;
        const int natives = 0x38;
        const int libraries = natives + nativeCount * defSize;
        const int cod = libraries;
        const int codeCellCount = 5022;
        const uint duplicatedNativeHash = 0x0473BE4E;

        var prefix = new byte[cod];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(natives + 70 * defSize + 8), duplicatedNativeHash);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(natives + 76 * defSize + 8), duplicatedNativeHash);

        var cells = new ulong[codeCellCount];
        cells[3686] = 135;
        cells[3687] = 70;
        cells[3688] = 8;
        cells[4991] = 46;
        cells[4992] = 89;
        cells[4993] = 48;
        cells[5020] = 49;
        cells[5021] = unchecked((ulong)((4991 - 5020) * cellSize));

        var compactCode = CompactAmxCells(cells);
        var data = new byte[cod + compactCode.Length];
        Array.Copy(prefix, data, prefix.Length);
        Array.Copy(compactCode, 0, data, cod, compactCode.Length);

        var dat = cod + codeCellCount * cellSize;
        WriteAmxHeaderFields(
            data,
            size: data.Length,
            magic: pawnMagic64,
            flags: pawnFlagCompact,
            defSize: defSize,
            cod: cod,
            dat: dat,
            hea: dat,
            stp: dat,
            publics: natives,
            natives: natives,
            libraries: libraries,
            nameTable: libraries);
        return data;
    }

    private static byte[] CompactAmxCells(IEnumerable<ulong> cells)
    {
        var compact = new List<byte>();
        foreach (var cell in cells)
        {
            var value = unchecked((long)cell);
            var chunks = new List<byte>();
            while (true)
            {
                var payload = (byte)(value & 0x7F);
                chunks.Add(payload);
                value >>= 7;
                var signBitSet = (payload & 0x40) != 0;
                if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                {
                    break;
                }
            }

            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                var current = chunks[i];
                if (i != 0)
                {
                    current |= 0x80;
                }

                compact.Add(current);
            }
        }

        return compact.ToArray();
    }

    private static void WriteAmxHeaderFields(
        byte[] data,
        int size,
        ushort magic,
        short flags,
        short defSize,
        int cod,
        int dat,
        int hea,
        int stp,
        int publics,
        int natives,
        int libraries,
        int nameTable)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x00), size);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), magic);
        data[0x06] = 11;
        data[0x07] = 11;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x08), flags);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x0A), defSize);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), hea);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), stp);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x1C), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x20), publics);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x24), natives);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x28), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x2C), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x34), nameTable);
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data)
    {
        var textOffset = SwShNsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), SwShNsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Fill(0xAB);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        SwShNsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        SwShNsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        SwShNsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        return output;
    }

    private static void WriteSegmentHeader(
        byte[] output,
        int offset,
        int fileOffset,
        int memoryOffset,
        int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

    private enum Arm64Condition
    {
        EQ = 0,
        NE = 1,
    }
}
