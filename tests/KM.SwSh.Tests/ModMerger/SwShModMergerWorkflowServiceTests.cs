// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.ModMerger;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.ModMerger;

public sealed class SwShModMergerWorkflowServiceTests
{
    private const string TestPath = "romfs/bin/test.bin";

    [Fact]
    public void LoadScansRomFsFilesAndIgnoresExeFsFiles()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        WriteFile(modDirectory1, "romfs/bin/shop_data.bin", [1, 2, 3]);
        WriteFile(modDirectory1, "exefs/main", [4, 5, 6]);
        WriteFile(modDirectory2, "romfs/bin/shop_data.bin", [1, 2, 4]);
        temp.WriteBaseRomFsFile("bin/shop_data.bin", [1, 2, 3]);

        var workflow = new SwShModMergerWorkflowService().Load(
            temp.Paths,
            modDirectory1,
            modDirectory2);

        var file = Assert.Single(workflow.Directory1Files);
        Assert.Equal("romfs/bin/shop_data.bin", file.RelativePath);
        Assert.DoesNotContain(workflow.Directory1Files, candidate => candidate.RelativePath.StartsWith("exefs/", StringComparison.Ordinal));
        Assert.Equal(1, workflow.Stats.MatchingFileCount);
    }

    [Theory]
    [InlineData("romfs/bin/battle/waza/sequence/ew052.bseq")]
    [InlineData("romfs/bin/battle/waza/sequence/ee411.bseq")]
    [InlineData("romfs/bin/battle/waza/sequence/d230.bseq")]
    [InlineData("romfs/bin/battle/waza/camera/wait/ba_wait01_cam.gfbcama")]
    [InlineData("romfs/bin/appli/battle/bin/battle_commandSelect_00.arc")]
    public void StageWarnsWhenSelectedFileIsManagedByFpsPatch(string relativePath)
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        temp.WriteBaseRomFsFile(relativePath["romfs/".Length..], [0, 0, 0]);
        WriteFile(modDirectory1, relativePath, [0, 1, 0]);
        WriteFile(modDirectory2, "romfs/bin/unselected.bin", [0]);

        var stage = new SwShModMergerWorkflowService().Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [relativePath],
            [],
            []);

        var file = Assert.Single(stage.Preview.Files);
        Assert.Equal("60FPS Patch ROMFS diff", file.SupportKind);
        var warning = Assert.Single(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("60FPS Patch", StringComparison.Ordinal));
        Assert.Equal("romfs", warning.File);
    }

    [Fact]
    public void StageWarnsWhenSelectedArchiveIsManagedByFpsPatch()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        const string relativePath = "romfs/bin/archive/demo/share/anime/a_pl0110.gfpak";
        temp.WriteBaseRomFsFile("bin/archive/demo/share/anime/a_pl0110.gfpak", [0, 0, 0]);
        WriteFile(modDirectory1, relativePath, [0, 1, 0]);
        WriteFile(modDirectory2, "romfs/bin/unselected.bin", [0]);

        var stage = new SwShModMergerWorkflowService().Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [relativePath],
            [],
            []);

        var file = Assert.Single(stage.Preview.Files);
        Assert.Equal("60FPS Patch ROMFS diff", file.SupportKind);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("60FPS Patch", StringComparison.Ordinal)
                && diagnostic.File == "romfs");
    }

    [Fact]
    public void ApplyWritesMergedNonOverlappingByteChangesAndLeavesUnrelatedOutput()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        temp.WriteBaseRomFsFile("bin/test.bin", [0, 0, 0, 0]);
        temp.WriteOutputFile("romfs/bin/other.bin", [7, 7, 7]);
        WriteFile(modDirectory1, TestPath, [1, 0, 0, 0]);
        WriteFile(modDirectory2, TestPath, [0, 0, 2, 0]);

        var result = new SwShModMergerWorkflowService().Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [TestPath],
            [TestPath],
            []);

        Assert.True(result.Preview.CanApply);
        Assert.Empty(result.Preview.Conflicts);
        var previewFile = Assert.Single(result.Preview.Files);
        Assert.Equal("safeMerge", previewFile.MergeKind);
        Assert.Equal([TestPath], result.WrittenFiles);
        Assert.Equal([1, 0, 2, 0], ReadOutputFile(temp, TestPath));
        Assert.Equal([7, 7, 7], ReadOutputFile(temp, "romfs/bin/other.bin"));
        AssertNoModMergerTempFiles(temp);
    }

    [Theory]
    [InlineData(SwShModMergerMergeModes.PreferMod1, new byte[] { 1, 0, 0 })]
    [InlineData(SwShModMergerMergeModes.PreferMod2, new byte[] { 0, 2, 0 })]
    public void ApplyPriorityMergeModeKeepsPriorityModFile(string mergeMode, byte[] expectedOutput)
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        temp.WriteBaseRomFsFile("bin/test.bin", [0, 0, 0]);
        WriteFile(modDirectory1, TestPath, [1, 0, 0]);
        WriteFile(modDirectory2, TestPath, [0, 2, 0]);

        var result = new SwShModMergerWorkflowService().Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [TestPath],
            [TestPath],
            [],
            mergeMode);

        Assert.True(result.Preview.CanApply);
        var previewFile = Assert.Single(result.Preview.Files);
        Assert.Equal("replacement", previewFile.MergeKind);
        Assert.Equal(mergeMode, result.Preview.MergeMode);
        Assert.Equal(expectedOutput, ReadOutputFile(temp, TestPath));
    }

    [Fact]
    public void SyntheticTwoFileCheckCoversEveryMergeMode()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        const string mergeablePath = "romfs/bin/mergeable.bin";
        const string overlapPath = "romfs/bin/overlap.bin";
        string[] selectedFiles = [mergeablePath, overlapPath];
        temp.WriteBaseRomFsFile("bin/mergeable.bin", [0, 0, 0, 0]);
        temp.WriteBaseRomFsFile("bin/overlap.bin", [0, 0, 0, 0]);
        WriteFile(modDirectory1, mergeablePath, [1, 0, 0, 0]);
        WriteFile(modDirectory2, mergeablePath, [0, 0, 2, 0]);
        WriteFile(modDirectory1, overlapPath, [0, 3, 0, 0]);
        WriteFile(modDirectory2, overlapPath, [0, 4, 0, 0]);
        var service = new SwShModMergerWorkflowService();

        var smartStage = service.Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            selectedFiles,
            selectedFiles,
            [],
            SwShModMergerMergeModes.Smart);

        Assert.False(smartStage.Preview.CanApply);
        Assert.Equal(SwShModMergerMergeModes.Smart, smartStage.Preview.MergeMode);
        Assert.Equal(2, smartStage.Preview.Files.Count);
        Assert.Equal("safeMerge", Assert.Single(smartStage.Preview.Files, file => file.RelativePath == mergeablePath).MergeKind);
        Assert.Equal("needsChoice", Assert.Single(smartStage.Preview.Files, file => file.RelativePath == overlapPath).MergeKind);
        var smartConflict = Assert.Single(smartStage.Preview.Conflicts);

        var smartApply = service.Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            selectedFiles,
            selectedFiles,
            [new SwShModMergerConflictResolution(smartConflict.ConflictId, "mod2")],
            SwShModMergerMergeModes.Smart);

        Assert.True(smartApply.Preview.CanApply);
        Assert.Equal(SwShModMergerMergeModes.Smart, smartApply.Preview.MergeMode);
        Assert.Equal([mergeablePath, overlapPath], smartApply.WrittenFiles);
        Assert.Equal([1, 0, 2, 0], ReadOutputFile(temp, mergeablePath));
        Assert.Equal([0, 4, 0, 0], ReadOutputFile(temp, overlapPath));

        var mod1Apply = service.Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            selectedFiles,
            selectedFiles,
            [],
            SwShModMergerMergeModes.PreferMod1);

        Assert.True(mod1Apply.Preview.CanApply);
        Assert.Equal(SwShModMergerMergeModes.PreferMod1, mod1Apply.Preview.MergeMode);
        Assert.All(mod1Apply.Preview.Files, file => Assert.Equal("replacement", file.MergeKind));
        Assert.Equal([mergeablePath, overlapPath], mod1Apply.WrittenFiles);
        Assert.Equal([1, 0, 0, 0], ReadOutputFile(temp, mergeablePath));
        Assert.Equal([0, 3, 0, 0], ReadOutputFile(temp, overlapPath));

        var mod2Apply = service.Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            selectedFiles,
            selectedFiles,
            [],
            SwShModMergerMergeModes.PreferMod2);

        Assert.True(mod2Apply.Preview.CanApply);
        Assert.Equal(SwShModMergerMergeModes.PreferMod2, mod2Apply.Preview.MergeMode);
        Assert.All(mod2Apply.Preview.Files, file => Assert.Equal("replacement", file.MergeKind));
        Assert.Equal([mergeablePath, overlapPath], mod2Apply.WrittenFiles);
        Assert.Equal([0, 0, 2, 0], ReadOutputFile(temp, mergeablePath));
        Assert.Equal([0, 4, 0, 0], ReadOutputFile(temp, overlapPath));
    }

    [Fact]
    public void StageExplainsEverySmartMergeOutcomeInPlainText()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        const string identicalPath = "romfs/bin/identical.bin";
        const string singleSourcePath = "romfs/bin/single-source.bin";
        const string safeMergePath = "romfs/bin/safe-merge.bin";
        const string trainerPath = "romfs/bin/trainer/trainer_poke/000.bin";
        const string overlapPath = "romfs/bin/overlap.bin";
        const string lengthPath = "romfs/bin/length.bin";
        const string missingBasePath = "romfs/bin/missing-base.bin";
        string[] selectedFiles =
        [
            identicalPath,
            singleSourcePath,
            safeMergePath,
            trainerPath,
            overlapPath,
            lengthPath,
            missingBasePath,
        ];
        temp.WriteBaseRomFsFile("bin/identical.bin", [0]);
        temp.WriteBaseRomFsFile("bin/single-source.bin", [0]);
        temp.WriteBaseRomFsFile("bin/safe-merge.bin", [0, 0, 0]);
        temp.WriteBaseRomFsFile("bin/trainer/trainer_poke/000.bin", new byte[0x20]);
        temp.WriteBaseRomFsFile("bin/overlap.bin", [0, 0]);
        temp.WriteBaseRomFsFile("bin/length.bin", [0]);
        WriteFile(modDirectory1, identicalPath, [9]);
        WriteFile(modDirectory2, identicalPath, [9]);
        WriteFile(modDirectory1, singleSourcePath, [0]);
        WriteFile(modDirectory2, singleSourcePath, [2]);
        WriteFile(modDirectory1, safeMergePath, [1, 0, 0]);
        WriteFile(modDirectory2, safeMergePath, [0, 2, 0]);
        var trainerMod1Bytes = new byte[0x20];
        var trainerMod2Bytes = new byte[0x20];
        trainerMod1Bytes[0x0C] = 25;
        trainerMod2Bytes[0x10] = 10;
        WriteFile(modDirectory1, trainerPath, trainerMod1Bytes);
        WriteFile(modDirectory2, trainerPath, trainerMod2Bytes);
        WriteFile(modDirectory1, overlapPath, [1, 0]);
        WriteFile(modDirectory2, overlapPath, [2, 0]);
        WriteFile(modDirectory1, lengthPath, [1, 2]);
        WriteFile(modDirectory2, lengthPath, [3]);
        WriteFile(modDirectory1, missingBasePath, [1]);
        WriteFile(modDirectory2, missingBasePath, [2]);

        var stage = new SwShModMergerWorkflowService().Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            selectedFiles,
            selectedFiles,
            [],
            SwShModMergerMergeModes.Smart);

        Assert.False(stage.Preview.CanApply);
        AssertPlainSummary(FindFile(stage.Preview, identicalPath), "unchanged", "same file contents");
        AssertPlainSummary(FindFile(stage.Preview, singleSourcePath), "singleSource", "Only Mod Directory 2 changed");
        AssertPlainSummary(FindFile(stage.Preview, safeMergePath), "safeMerge", "Safe merge", "cannot inspect this file type");
        AssertPlainSummary(FindFile(stage.Preview, trainerPath), "smartMerge", "Smart Merge", "species", "held item");
        AssertPlainSummary(FindFile(stage.Preview, overlapPath), "needsChoice", "cannot inspect this file type", "Open Overlaps");
        AssertPlainSummary(FindFile(stage.Preview, lengthPath), "needsChoice", "file length", "Choose which whole file");
        AssertPlainSummary(FindFile(stage.Preview, missingBasePath), "error", "vanilla RomFS file is missing");
        var overlapConflict = Assert.Single(stage.Preview.Conflicts, conflict => conflict.RelativePath == overlapPath);
        Assert.Equal("one byte", overlapConflict.Label);
        Assert.Contains("cannot inspect this file type", overlapConflict.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x", overlapConflict.Description, StringComparison.OrdinalIgnoreCase);
        var lengthConflict = Assert.Single(stage.Preview.Conflicts, conflict => conflict.RelativePath == lengthPath);
        Assert.Contains("file length", lengthConflict.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x", lengthConflict.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyExplainsResolvedChoicesAndReplacementModesInPlainText()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        const string safeMergePath = "romfs/bin/safe-merge.bin";
        const string overlapPath = "romfs/bin/overlap.bin";
        const string lengthPath = "romfs/bin/length.bin";
        string[] selectedFiles = [safeMergePath, overlapPath, lengthPath];
        temp.WriteBaseRomFsFile("bin/safe-merge.bin", [0, 0, 0]);
        temp.WriteBaseRomFsFile("bin/overlap.bin", [0, 0]);
        temp.WriteBaseRomFsFile("bin/length.bin", [0]);
        WriteFile(modDirectory1, safeMergePath, [1, 0, 0]);
        WriteFile(modDirectory2, safeMergePath, [0, 2, 0]);
        WriteFile(modDirectory1, overlapPath, [1, 0]);
        WriteFile(modDirectory2, overlapPath, [2, 0]);
        WriteFile(modDirectory1, lengthPath, [1, 2]);
        WriteFile(modDirectory2, lengthPath, [3]);
        var service = new SwShModMergerWorkflowService();
        var stage = service.Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            selectedFiles,
            selectedFiles,
            [],
            SwShModMergerMergeModes.Smart);
        var resolutions = stage.Preview.Conflicts
            .Select(conflict => new SwShModMergerConflictResolution(conflict.ConflictId, "mod2"))
            .ToArray();

        var smartApply = service.Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            selectedFiles,
            selectedFiles,
            resolutions,
            SwShModMergerMergeModes.Smart);

        Assert.True(smartApply.Preview.CanApply);
        AssertPlainSummary(FindFile(smartApply.Preview, safeMergePath), "safeMerge", "Safe merge", "cannot inspect this file type");
        AssertPlainSummary(FindFile(smartApply.Preview, overlapPath), "manualChoice", "Choice applied", "byte overlap");
        AssertPlainSummary(FindFile(smartApply.Preview, lengthPath), "manualChoice", "whole-file conflict", "Mod Directory 2");

        var priorityApply = service.Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            selectedFiles,
            selectedFiles,
            [],
            SwShModMergerMergeModes.PreferMod1);

        Assert.True(priorityApply.Preview.CanApply);
        AssertPlainSummary(FindFile(priorityApply.Preview, safeMergePath), "replacement", "Replacement mode", "Mod Directory 1");
        AssertPlainSummary(FindFile(priorityApply.Preview, overlapPath), "replacement", "Replacement mode", "Mod Directory 1");
        AssertPlainSummary(FindFile(priorityApply.Preview, lengthPath), "replacement", "Replacement mode", "Mod Directory 1");
    }

    [Fact]
    public void StageBlocksOverlappingByteChangesUntilResolved()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        temp.WriteBaseRomFsFile("bin/test.bin", [0, 0, 0]);
        WriteFile(modDirectory1, TestPath, [0, 1, 0]);
        WriteFile(modDirectory2, TestPath, [0, 2, 0]);
        var service = new SwShModMergerWorkflowService();

        var stage = service.Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [TestPath],
            [TestPath],
            []);

        Assert.False(stage.Preview.CanApply);
        Assert.Equal("needsResolution", stage.Preview.Status);
        var conflict = Assert.Single(stage.Preview.Conflicts);
        Assert.Null(conflict.Resolution);

        var apply = service.Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [TestPath],
            [TestPath],
            [new SwShModMergerConflictResolution(conflict.ConflictId, "mod2")]);

        Assert.True(apply.Preview.CanApply);
        Assert.Equal([0, 2, 0], ReadOutputFile(temp, TestPath));
    }

    [Fact]
    public void ApplyPreservesNonOverlappingChangesWhenConflictIsResolved()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        temp.WriteBaseRomFsFile("bin/test.bin", [0, 0, 0, 0, 0]);
        WriteFile(modDirectory1, TestPath, [0, 1, 0, 3, 0]);
        WriteFile(modDirectory2, TestPath, [0, 2, 4, 0, 0]);
        var service = new SwShModMergerWorkflowService();

        var stage = service.Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [TestPath],
            [TestPath],
            []);
        var conflict = Assert.Single(stage.Preview.Conflicts);

        var apply = service.Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [TestPath],
            [TestPath],
            [new SwShModMergerConflictResolution(conflict.ConflictId, "mod1")]);

        Assert.True(apply.Preview.CanApply);
        Assert.Equal([0, 1, 4, 3, 0], ReadOutputFile(temp, TestPath));
    }

    [Fact]
    public void StageIncludesOneSidedSelectionsAsCopyReadyFiles()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        WriteFile(modDirectory1, "romfs/bin/a.bin", [1]);
        WriteFile(modDirectory2, "romfs/bin/b.bin", [2]);

        var stage = new SwShModMergerWorkflowService().Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            ["romfs/bin/a.bin"],
            ["romfs/bin/b.bin"],
            []);

        Assert.True(stage.Preview.CanApply);
        Assert.Equal("ready", stage.Preview.Status);
        Assert.Equal(2, stage.Preview.Files.Count);
        AssertPlainSummary(FindFile(stage.Preview, "romfs/bin/a.bin"), "singleSource", "Only Mod Directory 1 contains this file", "copy");
        AssertPlainSummary(FindFile(stage.Preview, "romfs/bin/b.bin"), "singleSource", "Only Mod Directory 2 contains this file", "copy");
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ApplyCopiesOneSidedSelectionsFromBothDirectoriesAndWritesMatchedSelections()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        temp.WriteBaseRomFsFile("bin/test.bin", [0, 0, 0]);
        WriteFile(modDirectory1, TestPath, [1, 0, 0]);
        WriteFile(modDirectory2, TestPath, [0, 0, 2]);
        WriteFile(modDirectory1, "romfs/bin/dir1-only.bin", [9]);
        WriteFile(modDirectory2, "romfs/bin/dir2-only.bin", [8, 8]);

        var result = new SwShModMergerWorkflowService().Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [TestPath, "romfs/bin/dir1-only.bin"],
            [TestPath, "romfs/bin/dir2-only.bin"],
            []);

        Assert.True(result.Preview.CanApply);
        Assert.Equal("ready", result.Preview.Status);
        Assert.Equal(
            ["romfs/bin/dir1-only.bin", "romfs/bin/dir2-only.bin", TestPath],
            result.WrittenFiles);
        Assert.Equal([1, 0, 2], ReadOutputFile(temp, TestPath));
        Assert.Equal([9], ReadOutputFile(temp, "romfs/bin/dir1-only.bin"));
        Assert.Equal([8, 8], ReadOutputFile(temp, "romfs/bin/dir2-only.bin"));
        AssertPlainSummary(FindFile(result.Preview, "romfs/bin/dir1-only.bin"), "singleSource", "Only Mod Directory 1 contains this file", "copy");
        AssertPlainSummary(FindFile(result.Preview, "romfs/bin/dir2-only.bin"), "singleSource", "Only Mod Directory 2 contains this file", "copy");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ApplyCanReplaceFilesWhenOutputRootIsAlsoAModDirectory(int outputRootDirectory)
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = outputRootDirectory == 1
            ? temp.OutputRootPath
            : CreateModDirectory(temp, "mod-1");
        var modDirectory2 = outputRootDirectory == 2
            ? temp.OutputRootPath
            : CreateModDirectory(temp, "mod-2");
        temp.WriteBaseRomFsFile("bin/test.bin", [0, 0, 0, 0]);
        WriteFile(modDirectory1, TestPath, [1, 0, 0, 0]);
        WriteFile(modDirectory2, TestPath, [0, 0, 2, 0]);
        WriteFile(temp.OutputRootPath, "romfs/bin/unrelated.bin", [7, 7, 7]);

        var result = new SwShModMergerWorkflowService().Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [TestPath],
            [TestPath],
            []);

        Assert.True(result.Preview.CanApply);
        Assert.Equal([TestPath], result.WrittenFiles);
        Assert.Equal([1, 0, 2, 0], ReadOutputFile(temp, TestPath));
        Assert.Equal([7, 7, 7], ReadOutputFile(temp, "romfs/bin/unrelated.bin"));
        AssertNoModMergerTempFiles(temp);
    }

    [Fact]
    public void ApplyWritesVerifiedBytesAndLeavesNoTemporaryFiles()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        const string nestedPath = "romfs/bin/nested/verified.bin";
        temp.WriteBaseRomFsFile("bin/nested/verified.bin", [0, 0, 0, 0, 0]);
        WriteFile(modDirectory1, nestedPath, [1, 0, 0, 3, 0]);
        WriteFile(modDirectory2, nestedPath, [0, 0, 2, 0, 4]);

        var result = new SwShModMergerWorkflowService().Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [nestedPath],
            [nestedPath],
            []);

        Assert.True(result.Preview.CanApply);
        Assert.Equal([nestedPath], result.WrittenFiles);
        Assert.Equal([1, 0, 2, 3, 4], ReadOutputFile(temp, nestedPath));
        AssertNoModMergerTempFiles(temp);
    }

    [Fact]
    public void RepeatedStageDoesNotRetainMergedFileBuffers()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        const string largePath = "romfs/bin/large.bin";
        var baseBytes = CreateSequentialBytes(512 * 1024);
        var mod1Bytes = baseBytes.ToArray();
        var mod2Bytes = baseBytes.ToArray();
        mod1Bytes[17] = 0x7D;
        mod2Bytes[^18] = 0x7E;
        temp.WriteBaseRomFsFile("bin/large.bin", baseBytes);
        WriteFile(modDirectory1, largePath, mod1Bytes);
        WriteFile(modDirectory2, largePath, mod2Bytes);
        var service = new SwShModMergerWorkflowService();
        var warmup = service.Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [largePath],
            [largePath],
            []);
        Assert.True(warmup.Preview.CanApply);

        var stageReferences = new List<WeakReference<SwShModMergerStageResult>>();
        for (var iteration = 0; iteration < 60; iteration++)
        {
            stageReferences.Add(StageAndCaptureStageResult(service, temp, modDirectory1, modDirectory2, largePath));
        }

        ForceFullCollection();
        Assert.All(
            stageReferences,
            reference => Assert.False(
                reference.TryGetTarget(out _),
                "Repeated Mod Merger staging retained a stage result."));
    }

    private static WeakReference<SwShModMergerStageResult> StageAndCaptureStageResult(
        SwShModMergerWorkflowService service,
        TemporarySwShProject temp,
        string modDirectory1,
        string modDirectory2,
        string largePath)
    {
        var stage = service.Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [largePath],
            [largePath],
            []);
        Assert.True(stage.Preview.CanApply);
        Assert.Equal("safeMerge", Assert.Single(stage.Preview.Files).MergeKind);

        return new WeakReference<SwShModMergerStageResult>(stage);
    }

    private static string CreateModDirectory(TemporarySwShProject temp, string name)
    {
        return Directory.CreateDirectory(Path.Combine(temp.RootPath, name)).FullName;
    }

    private static void WriteFile(string rootPath, string relativePath, byte[] contents)
    {
        var path = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
    }

    private static byte[] ReadOutputFile(TemporarySwShProject temp, string relativePath)
    {
        var path = Path.Combine(
            temp.OutputRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllBytes(path);
    }

    private static byte[] CreateSequentialBytes(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        return bytes;
    }

    private static void AssertNoModMergerTempFiles(TemporarySwShProject temp)
    {
        Assert.Empty(Directory.EnumerateFiles(temp.OutputRootPath, "*.tmp", SearchOption.AllDirectories));
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static SwShModMergerFilePreviewRecord FindFile(
        SwShModMergerPreview preview,
        string relativePath)
    {
        return Assert.Single(preview.Files, file => file.RelativePath == relativePath);
    }

    private static void AssertPlainSummary(
        SwShModMergerFilePreviewRecord file,
        string expectedMergeKind,
        params string[] expectedPhrases)
    {
        Assert.Equal(expectedMergeKind, file.MergeKind);
        Assert.False(string.IsNullOrWhiteSpace(file.Summary));
        Assert.DoesNotContain("0x", file.Summary, StringComparison.OrdinalIgnoreCase);
        foreach (var expectedPhrase in expectedPhrases)
        {
            Assert.Contains(expectedPhrase, file.Summary, StringComparison.OrdinalIgnoreCase);
        }
    }
}
