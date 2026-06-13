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
        Assert.Equal([TestPath], result.WrittenFiles);
        Assert.Equal([1, 0, 2, 0], ReadOutputFile(temp, TestPath));
        Assert.Equal([7, 7, 7], ReadOutputFile(temp, "romfs/bin/other.bin"));
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
    public void StageIgnoresSelectedFilesMissingFromOneSide()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        temp.WriteBaseRomFsFile("bin/a.bin", [0]);
        temp.WriteBaseRomFsFile("bin/b.bin", [0]);
        WriteFile(modDirectory1, "romfs/bin/a.bin", [1]);
        WriteFile(modDirectory2, "romfs/bin/b.bin", [2]);

        var stage = new SwShModMergerWorkflowService().Stage(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            ["romfs/bin/a.bin"],
            ["romfs/bin/b.bin"],
            []);

        Assert.False(stage.Preview.CanApply);
        Assert.Equal("empty", stage.Preview.Status);
        Assert.Empty(stage.Preview.Files);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("Files missing from one side were ignored", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyIgnoresOneSidedSelectionsAndWritesMatchedSelections()
    {
        using var temp = TemporarySwShProject.Create();
        var modDirectory1 = CreateModDirectory(temp, "mod-1");
        var modDirectory2 = CreateModDirectory(temp, "mod-2");
        temp.WriteBaseRomFsFile("bin/test.bin", [0, 0, 0]);
        temp.WriteBaseRomFsFile("bin/dir1-only.bin", [0]);
        WriteFile(modDirectory1, TestPath, [1, 0, 0]);
        WriteFile(modDirectory2, TestPath, [0, 0, 2]);
        WriteFile(modDirectory1, "romfs/bin/dir1-only.bin", [9]);

        var result = new SwShModMergerWorkflowService().Apply(
            temp.Paths,
            modDirectory1,
            modDirectory2,
            [TestPath, "romfs/bin/dir1-only.bin"],
            [TestPath],
            []);

        Assert.True(result.Preview.CanApply);
        Assert.Equal("ready", result.Preview.Status);
        Assert.Equal([TestPath], result.WrittenFiles);
        Assert.Equal([1, 0, 2], ReadOutputFile(temp, TestPath));
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "dir1-only.bin")));
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("Files missing from one side were ignored", StringComparison.Ordinal));
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
}
