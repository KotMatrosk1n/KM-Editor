// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Flagwork;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using System.Security.Cryptography;
using Xunit;

namespace KM.SwSh.Tests.Flagwork;

public sealed class SwShFlagworkSaveWorkflowServiceTests
{
    [Fact]
    public void LoadReadsFlagworkAndSaveInspectorRecordsFromRealTables()
    {
        using var temp = TemporarySwShProject.Create();
        WriteFlagworkTables(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShFlagworkSaveWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Flags.Count);
        var flag = workflow.Flags.Single(record => record.Name == "FE_TEST_FLAG");
        Assert.Equal("system_flags:0000", flag.FlagId);
        Assert.Equal("system_flags", flag.Table);
        Assert.Equal(0, flag.Index);
        Assert.Equal("system_flags", flag.Category);
        Assert.Equal("Flag", flag.Kind);
        Assert.Equal("boolean", flag.ValueKind);
        Assert.Equal("false", flag.DefaultValue);
        Assert.Equal("0x1122334455667788", flag.Hash);
        Assert.Equal("0x55667788", flag.Low32Key);
        Assert.Equal(ProjectFileLayer.Base, flag.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, flag.Provenance.FileState);
        Assert.Equal("romfs/bin/flagwork/system_flags.tbl", flag.Provenance.SourceFile);

        var work = workflow.Flags.Single(record => record.Name == "WK_SCENE_MAIN");
        Assert.Equal("scene_work", work.Category);
        Assert.Equal("Work", work.Kind);
        Assert.Equal("integer", work.ValueKind);
        Assert.Equal("0", work.DefaultValue);
        Assert.Equal("0x99AABBCCDDEEFF00", work.Hash);
        Assert.Equal("0xDDEEFF00", work.Low32Key);

        var saveBlock = workflow.SaveBlocks.Single(block => block.Name == "WK_SCENE_MAIN");
        Assert.Equal("scene_work:0000:0xDDEEFF00", saveBlock.BlockId);
        Assert.Equal("0xDDEEFF00", saveBlock.Key);
        Assert.Equal("0x99AABBCCDDEEFF00", saveBlock.Hash);
        Assert.Equal("Work", saveBlock.Kind);
        Assert.Equal("integer", saveBlock.ValueKind);
        Assert.Equal(2, workflow.Stats.TotalFlagCount);
        Assert.Equal(2, workflow.Stats.TotalSaveBlockCount);
        Assert.Equal(2, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReportsConfiguredSaveFileMetadata()
    {
        using var temp = TemporarySwShProject.Create();
        WriteFlagworkTables(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var saveFileBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var saveFilePath = Path.Combine(temp.RootPath, "main");
        File.WriteAllBytes(saveFilePath, saveFileBytes);
        var project = new ProjectWorkspaceService().Open(
            temp.Paths with { OutputRootPath = null, SaveFilePath = saveFilePath });

        var workflow = new SwShFlagworkSaveWorkflowService().Load(project);

        Assert.NotNull(workflow.SaveFile);
        var saveFile = workflow.SaveFile!;
        Assert.True(workflow.Stats.HasSaveFile);
        Assert.Equal("main", saveFile.FileName);
        Assert.Equal(saveFileBytes.Length, saveFile.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(saveFileBytes)), saveFile.Sha256);
        Assert.Equal("available", saveFile.Status);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenFlagworkTablesAreMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/flags.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShFlagworkSaveWorkflowService().Load(project);

        Assert.Empty(workflow.Flags);
        Assert.Empty(workflow.SaveBlocks);
        Assert.Equal(0, workflow.Stats.SourceFileCount);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.flagworkSave");
    }

    [Fact]
    public void LoadWarnsWhenFlagworkHashesAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/flagwork/system_flags.tbl",
            new SwShAhtbFile(
            [
                new SwShAhtbEntry(0x1122334455667788, "FE_TEST_FLAG"),
                new SwShAhtbEntry(0x1122334455667788, "FE_TEST_FLAG_DUPLICATE"),
            ]).Write());
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShFlagworkSaveWorkflowService().Load(project);

        Assert.Equal(2, workflow.Flags.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.flagworkSave");
    }

    private static void WriteFlagworkTables(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/flagwork/system_flags.tbl",
            new SwShAhtbFile(
            [
                new SwShAhtbEntry(0x1122334455667788, "FE_TEST_FLAG"),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/flagwork/scene_work.tbl",
            new SwShAhtbFile(
            [
                new SwShAhtbEntry(0x99AABBCCDDEEFF00, "WK_SCENE_MAIN"),
            ]).Write());
    }
}
