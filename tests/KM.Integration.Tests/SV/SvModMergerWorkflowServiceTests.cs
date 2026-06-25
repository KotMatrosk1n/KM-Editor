// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.IO.Compression;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.Formats.SV.Trinity;
using KM.Integration.Tests.Tools;
using KM.SV.ModMerger;
using Xunit;

namespace KM.Integration.Tests.SV;

public sealed class SvModMergerWorkflowServiceTests
{
    private const ulong ScarletTitleId = 0x0100A3D008C5C000;
    private const string DataVirtualPath = "bin/mock/data.bin";
    private const string DataOutputPath = "romfs/bin/mock/data.bin";
    private const string DescriptorOutputPath = "romfs/arc/data.trpfd";

    [Fact]
    public void StageAndApplySmartMergeNonOverlappingFolderMods()
    {
        using var temp = CreateScarletProject([0, 0, 0, 0]);
        var firstMod = CreateFolderMod(temp, "first-mod", [1, 0, 0, 0]);
        var secondMod = CreateFolderMod(temp, "second-mod", [0, 0, 2, 0]);
        var service = new SvModMergerWorkflowService();

        var stage = service.Stage(
            ToCorePaths(temp),
            [
                new SvModMergerSourceRequest(firstMod),
                new SvModMergerSourceRequest(secondMod),
            ]);

        AssertNoErrors(stage.Diagnostics);
        Assert.True(stage.Preview.CanApply);
        Assert.Equal("ready", stage.Preview.Status);
        var stagedFile = Assert.Single(stage.Preview.Files);
        Assert.Equal(DataOutputPath, stagedFile.RelativePath);
        Assert.Equal("smartMerge", stagedFile.MergeKind);

        var apply = service.Apply(
            ToCorePaths(temp),
            [
                new SvModMergerSourceRequest(firstMod),
                new SvModMergerSourceRequest(secondMod),
            ]);

        AssertNoErrors(apply.Diagnostics);
        Assert.Contains(DataOutputPath, apply.WrittenFiles);
        Assert.Contains(DescriptorOutputPath, apply.WrittenFiles);
        Assert.Equal([1, 0, 2, 0], ReadOutputBytes(temp, DataOutputPath));
        AssertDescriptorRemovedLayeredFile(temp);
    }

    [Fact]
    public void ApplyUsesLaterEnabledSourceWhenSmartMergeConflicts()
    {
        using var temp = CreateScarletProject([0, 0]);
        var firstMod = CreateFolderMod(temp, "first-mod", [1, 0]);
        var secondMod = CreateFolderMod(temp, "second-mod", [2, 0]);
        var service = new SvModMergerWorkflowService();

        var firstApply = service.Apply(
            ToCorePaths(temp),
            [
                new SvModMergerSourceRequest(firstMod),
                new SvModMergerSourceRequest(secondMod),
            ]);

        AssertNoErrors(firstApply.Diagnostics);
        Assert.Equal("priorityFallback", firstApply.Preview.Status);
        Assert.Equal("priorityFallback", Assert.Single(firstApply.Preview.Files).MergeKind);
        Assert.Contains(
            firstApply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("overlapping byte edits", StringComparison.OrdinalIgnoreCase));
        Assert.Equal([2, 0], ReadOutputBytes(temp, DataOutputPath));

        var reversedApply = service.Apply(
            ToCorePaths(temp),
            [
                new SvModMergerSourceRequest(secondMod),
                new SvModMergerSourceRequest(firstMod),
            ]);

        AssertNoErrors(reversedApply.Diagnostics);
        Assert.Equal("priorityFallback", reversedApply.Preview.Status);
        Assert.Equal([1, 0], ReadOutputBytes(temp, DataOutputPath));
    }

    [Fact]
    public void StageReadsZipModWithNestedRomFsRoot()
    {
        using var temp = CreateScarletProject([0, 0, 0]);
        var zipPath = Path.Combine(temp.RootPath, "nested-mod.zip");
        CreateZipMod(zipPath, "Release/ExampleMod/romfs/bin/mock/data.bin", [3, 0, 0]);
        var service = new SvModMergerWorkflowService();

        var stage = service.Stage(
            ToCorePaths(temp),
            [new SvModMergerSourceRequest(zipPath)]);

        AssertNoErrors(stage.Diagnostics);
        Assert.True(stage.Preview.CanApply);
        var source = Assert.Single(stage.Workflow.Sources);
        Assert.Equal("archive", source.Kind);
        Assert.Equal("ready", source.Status);
        Assert.Equal(1, source.FileCount);
        var stagedFile = Assert.Single(stage.Preview.Files);
        Assert.Equal(DataOutputPath, stagedFile.RelativePath);
        Assert.Equal("singleSource", stagedFile.MergeKind);
    }

    [Fact]
    public void StageReadsDirectRomFsRootAndSkipsBundledDescriptor()
    {
        using var temp = CreateScarletProject([0, 0, 0]);
        var modRoot = Path.Combine(temp.RootPath, "direct-root-mod");
        WriteModSourceFile(modRoot, DataVirtualPath, [7, 0, 0]);
        WriteModSourceFile(modRoot, SvTrinityDescriptorPatcher.DescriptorVirtualPath, [0xFD]);
        var service = new SvModMergerWorkflowService();

        var stage = service.Stage(
            ToCorePaths(temp),
            [new SvModMergerSourceRequest(modRoot)]);

        AssertNoErrors(stage.Diagnostics);
        var source = Assert.Single(stage.Workflow.Sources);
        Assert.Equal("ready", source.Status);
        Assert.Equal(2, source.FileCount);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.File == DescriptorOutputPath
                && diagnostic.Message.Contains("source descriptor was skipped", StringComparison.OrdinalIgnoreCase));
        var stagedFile = Assert.Single(stage.Preview.Files);
        Assert.Equal(DataOutputPath, stagedFile.RelativePath);
        Assert.Equal("singleSource", stagedFile.MergeKind);
    }

    private static TemporaryBridgeProject CreateScarletProject(byte[] baseBytes)
    {
        var temp = TemporaryBridgeProject.Create();
        temp.EnsureScarletVioletSupportFolder();
        temp.WriteBaseRomFsFile("arc/data.trpfd", CreateTrinityDescriptor([DataVirtualPath]));
        temp.WriteBaseRomFsFile("arc/data.trpfs", []);
        temp.WriteBaseRomFsFile(DataVirtualPath, baseBytes);
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(ScarletTitleId));
        return temp;
    }

    private static ProjectPaths ToCorePaths(TemporaryBridgeProject temp)
    {
        return new ProjectPaths(
            temp.BaseRomFsPath,
            temp.BaseExeFsPath,
            temp.OutputRootPath,
            SaveFilePath: null,
            ScarletVioletSupportFolderPath: temp.ScarletVioletSupportFolderPath,
            SelectedGame: ProjectGame.Scarlet);
    }

    private static string CreateFolderMod(TemporaryBridgeProject temp, string name, byte[] bytes)
    {
        var root = Path.Combine(temp.RootPath, name);
        var path = Path.Combine(root, "romfs", "bin", "mock", "data.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return root;
    }

    private static void WriteModSourceFile(string root, string relativePath, byte[] bytes)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static void CreateZipMod(string zipPath, string entryPath, byte[] bytes)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryPath);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static byte[] ReadOutputBytes(TemporaryBridgeProject temp, string relativePath)
    {
        return File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void AssertDescriptorRemovedLayeredFile(TemporaryBridgeProject temp)
    {
        var descriptorPath = Path.Combine(temp.OutputRootPath, "romfs", "arc", "data.trpfd");
        Assert.True(File.Exists(descriptorPath));

        var descriptor = FileDescriptor.GetRootAsFileDescriptor(new ByteBuffer(File.ReadAllBytes(descriptorPath)));
        var activeHashes = Enumerable
            .Range(0, descriptor.FileHashesLength)
            .Select(descriptor.FileHashes)
            .ToHashSet();

        Assert.DoesNotContain(SvTrinityPathHasher.HashPath(DataVirtualPath), activeHashes);
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x290, 8), titleId);
        return data;
    }

    private static byte[] CreateTrinityDescriptor(IReadOnlyList<string> virtualPaths)
    {
        var builder = new FlatBufferBuilder(1024);
        var packName = builder.CreateString("pack/test.trpak");
        var packNames = FileDescriptor.CreatePackNamesVector(builder, [packName]);
        var fileHashes = FileDescriptor.CreateFileHashesVector(
            builder,
            virtualPaths.Select(SvTrinityPathHasher.HashPath).ToArray());
        var fileEntries = virtualPaths
            .Select(_ => FileDescriptorEntry.CreateFileDescriptorEntry(builder, pack_index: 0))
            .ToArray();
        var files = FileDescriptor.CreateFilesVector(builder, fileEntries);
        var pack = PackDescriptorEntry.CreatePackDescriptorEntry(
            builder,
            file_size: 123,
            file_count: checked((ulong)virtualPaths.Count));
        var packs = FileDescriptor.CreatePacksVector(builder, [pack]);
        var root = FileDescriptor.CreateFileDescriptor(builder, fileHashes, packNames, files, packs);
        FileDescriptor.FinishFileDescriptorBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static void AssertNoErrors(IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
