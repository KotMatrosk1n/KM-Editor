// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.ExeFs;

public sealed class SwShExeFsPatchWorkflowServiceTests
{
    [Fact]
    public void LoadReadsExeFsMainAndReportsCompatibilityChecks()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", CreateCompatibleNso());
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var patch = Assert.Single(workflow.Patches);
        Assert.Equal("exefs-main-compatibility", patch.PatchId);
        Assert.Equal("ExeFS main compatibility", patch.Name);
        Assert.Equal("exefs/main", patch.TargetFile);
        Assert.Equal("NSO signature scan", patch.PatchKind);
        Assert.Equal("available", patch.Status);
        Assert.Contains(patch.Details, detail => detail.StartsWith("Build ID:", StringComparison.Ordinal));
        Assert.Equal(ProjectFileLayer.Base, patch.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, patch.Provenance.FileState);
        Assert.Equal("exefs/main", patch.Provenance.SourceFile);
        Assert.Equal(3, workflow.Segments.Count);
        Assert.All(workflow.Segments, segment => Assert.Equal("Pass", segment.HashStatus));
        Assert.Contains(workflow.Checks, check => check.Name == "Patch code cave" && check.Status == "Pass");
        Assert.Contains(workflow.Checks, check => check.Name == "Allowed consumable upper bound" && check.Status == "Pass");
        Assert.Contains(workflow.Checks, check => check.Name == "Royal Candy immediate scan" && check.Status == "Info");
        Assert.Equal(1, workflow.Stats.TotalPatchCount);
        Assert.Equal(26, workflow.Stats.TotalCheckCount);
        Assert.Equal(24, workflow.Stats.PassCount);
        Assert.Equal(0, workflow.Stats.WarningCount);
        Assert.Equal(0, workflow.Stats.FailCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenExeFsMainIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        Assert.Empty(workflow.Patches);
        Assert.Empty(workflow.Segments);
        Assert.Empty(workflow.Checks);
        Assert.Equal(0, workflow.Stats.SourceFileCount);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.exefsPatches"
                && diagnostic.Expected == "exefs/main");
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenExeFsMainIsNotNso()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "not-an-nso");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        Assert.Empty(workflow.Patches);
        Assert.Empty(workflow.Segments);
        Assert.Empty(workflow.Checks);
        Assert.Equal(0, workflow.Stats.SourceFileCount);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.exefsPatches"
                && diagnostic.File == "exefs/main");
    }

    [Fact]
    public void LoadBlocksPatchWhenKnownAnchorDoesNotMatch()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        var text = CreateCompatibleText();
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(0x00747988, 4), 0xD503201F);
        temp.WriteBaseExeFsFile("main", CreateNso(text, [0x10], [0x20]));
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        var patch = Assert.Single(workflow.Patches);
        Assert.Equal("blocked", patch.Status);
        Assert.Equal(1, workflow.Stats.FailCount);
        Assert.Contains(
            workflow.Checks,
            check => check.Name == "UI check A"
                && check.Status == "Fail"
                && check.Actual == "0xD503201F");
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.exefsPatches");
    }

    private static byte[] CreateCompatibleNso()
    {
        return CreateNso(CreateCompatibleText(), [0x10], [0x20]);
    }

    private static byte[] CreateCompatibleText()
    {
        var text = new byte[0x01421094];
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
        WriteInstruction(text, 0x007DDA90, 0x54000348);
        WriteInstruction(text, 0x01420EF0, 0xF81D0FF5);
        WriteInstruction(text, 0x01421090, 0xA9BE4FF4);
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
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Fill(0xAB);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
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
