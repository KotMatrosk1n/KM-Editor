// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA;
using KM.Formats.ZA.Trinity;
using KM.Integration.Tests.Tools;
using KM.ZA.ModMerger;
using Xunit;

namespace KM.Integration.Tests.ZA;

public sealed class ZaModMergerWorkflowServiceTests
{
    private const ulong PokemonLegendsZATitleId = 0x0100F43008C44000;
    private const int ZaNpdmTitleIdOffset = 0x480;
    private const string DataVirtualPath = "bin/mock/data.bin";
    private const string DataOutputPath = "romfs/bin/mock/data.bin";
    private const string DescriptorOutputPath = "romfs/arc/data.trpfd";

    [Fact]
    public void StageReadsDirectRomFsRootAndSkipsBundledDescriptorAndSwitchWrapper()
    {
        using var temp = CreatePokemonLegendsZAProject([0, 0, 0]);
        var modRoot = Path.Combine(temp.RootPath, "direct-root-mod");
        WriteModSourceFile(modRoot, DataVirtualPath, [7, 0, 0]);
        WriteModSourceFile(modRoot, ZaTrinityDescriptorPatcher.DescriptorVirtualPath, [0xFD]);
        WriteModSourceFile(modRoot, "switch/readme.txt", [0x01]);
        var service = new ZaModMergerWorkflowService();

        var stage = service.Stage(
            ToCorePaths(temp),
            [new ZaModMergerSourceRequest(modRoot)]);

        AssertNoErrors(stage.Diagnostics);
        var source = Assert.Single(stage.Workflow.Sources);
        Assert.Equal("ready", source.Status);
        Assert.Equal(2, source.FileCount);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.File == DescriptorOutputPath
                && diagnostic.Message.Contains("source descriptor was skipped", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.File is not null
                && diagnostic.File.EndsWith("switch/readme.txt", StringComparison.OrdinalIgnoreCase));
        var stagedFile = Assert.Single(stage.Preview.Files);
        Assert.Equal(DataOutputPath, stagedFile.RelativePath);
        Assert.Equal("singleSource", stagedFile.MergeKind);
    }

    private static TemporaryBridgeProject CreatePokemonLegendsZAProject(byte[] baseBytes)
    {
        var temp = TemporaryBridgeProject.Create();
        temp.EnsurePokemonLegendsZASupportFolder();
        temp.WriteBaseRomFsFile("arc/data.trpfd", CreateTrinityDescriptor([DataVirtualPath]));
        temp.WriteBaseRomFsFile("arc/data.trpfs", []);
        temp.WriteBaseRomFsFile(DataVirtualPath, baseBytes);
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(PokemonLegendsZATitleId));
        return temp;
    }

    private static ProjectPaths ToCorePaths(TemporaryBridgeProject temp)
    {
        return new ProjectPaths(
            temp.BaseRomFsPath,
            temp.BaseExeFsPath,
            temp.OutputRootPath,
            SaveFilePath: null,
            ScarletVioletSupportFolderPath: null,
            SelectedGame: ProjectGame.ZA)
        {
            PokemonLegendsZASupportFolderPath = temp.PokemonLegendsZASupportFolderPath,
        };
    }

    private static void WriteModSourceFile(string root, string relativePath, byte[] bytes)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var npdm = new byte[ZaNpdmTitleIdOffset + sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(npdm.AsSpan(ZaNpdmTitleIdOffset), titleId);
        return npdm;
    }

    private static byte[] CreateTrinityDescriptor(IReadOnlyList<string> virtualPaths)
    {
        var builder = new FlatBufferBuilder(1024);
        var packName = builder.CreateString("pack/test.trpak");
        var packNames = FileDescriptor.CreatePackNamesVector(builder, [packName]);
        var fileHashes = FileDescriptor.CreateFileHashesVector(
            builder,
            virtualPaths.Select(ZaTrinityPathHasher.HashPath).ToArray());
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
