// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
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
        Assert.Equal("available", unlimited.Status);
        Assert.Equal("unlimited", unlimited.Mode);
        Assert.Equal(1128, unlimited.ItemId);
        Assert.Equal(50, unlimited.TemplateItemId);
        Assert.Equal(ProjectFileLayer.Base, unlimited.Provenance.SourceLayer);
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":item-data", StringComparison.Ordinal) && check.Status == "Pass");
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
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.royalCandy");
    }

    [Fact]
    public void LoadDetectsKnownLayeredOutputsForUninstallReview()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteOutputFile("exefs/main", CreateCompatibleNso());
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var uninstall = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-uninstall");
        Assert.Equal("warning", uninstall.Status);
        Assert.Contains(
            workflow.Checks,
            check => check.WorkflowId == uninstall.WorkflowId
                && check.Status == "Warning"
                && check.Message.Contains("known Royal Candy output", StringComparison.Ordinal));
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
            new byte[(1128 + 1) * 0x30]);
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
            [0x08]);
        temp.WriteBaseRomFsFile("bin/message/English/common/iteminfo.dat", [0x09]);
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", [0x0A]);
        temp.WriteBaseExeFsFile("main", CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
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
        WriteInstruction(text, 0x00747D44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BA24, EncodeCmpImmediate(26, 50));
        WriteInstruction(text, 0x0074BDA8, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074DFE4, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074DFF8, EncodeCmpImmediate(28, 50));
        WriteInstruction(text, 0x0075CEFC, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x007BB204, EncodeCmpImmediate(20, 50));
        WriteInstruction(text, 0x007BB3C0, EncodeCmpImmediate(19, 50));
        WriteInstruction(text, 0x007BC1F8, EncodeCmpImmediate(8, 50));
        WriteInstruction(text, 0x00747DE0, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BE44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0075CCE8, EncodeCmpImmediate(27, 50));
        WriteInstruction(text, 0x0075D08C, EncodeCmpImmediate(10, 50));
        WriteInstruction(text, 0x007BBFD4, EncodeCmpImmediate(23, 50));
        WriteInstruction(text, 0x007BC1BC, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007BC1C4, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007B1F20, 0x2A0003E2);
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
}
