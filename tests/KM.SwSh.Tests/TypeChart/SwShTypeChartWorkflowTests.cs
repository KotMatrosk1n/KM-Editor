// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using KM.SwSh.Tests.Items;
using KM.SwSh.TypeChart;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.TypeChart;

public sealed class SwShTypeChartWorkflowTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    [Fact]
    public void AnalyzeFindsVanillaSwordTypeChart()
    {
        var main = CreateSyntheticTypeChartMain();

        var analysis = SwShTypeChartMainPatcher.Analyze(main, ProjectGame.Sword);

        Assert.Equal(SwShTypeChartMainKind.Vanilla, analysis.Kind);
        Assert.Equal(ProjectGame.Sword, analysis.DetectedGame);
        Assert.Equal("main.ro+0x00743600", analysis.ChartOffsetHex);
        Assert.Equal(SwShTypeChartMainPatcher.VanillaChartValues, analysis.EffectivenessValues);
    }

    [Fact]
    public void AnalyzeRejectsSelectedGameMismatch()
    {
        var main = CreateSyntheticTypeChartMain(ProjectGame.Sword);

        var analysis = SwShTypeChartMainPatcher.Analyze(main, ProjectGame.Shield);

        Assert.Equal(SwShTypeChartMainKind.GameMismatch, analysis.Kind);
        Assert.Equal(ProjectGame.Sword, analysis.DetectedGame);
        Assert.Contains("will not patch a different game's executable", analysis.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyChartPatchesOnlyReservedRoChartBytes()
    {
        var main = CreateSyntheticTypeChartMain();
        var before = SwShNsoFile.Parse(main);
        var values = SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
        values[0] = 0;
        values[(1 * SwShTypeChartMainPatcher.TypeCount) + 4] = 2;

        var patched = SwShTypeChartMainPatcher.ApplyChart(main, values, ProjectGame.Sword);
        var after = SwShNsoFile.Parse(patched);
        var analysis = SwShTypeChartMainPatcher.Analyze(patched, ProjectGame.Sword);

        Assert.Equal(SwShTypeChartMainKind.Modified, analysis.Kind);
        Assert.Equal(values, analysis.EffectivenessValues);
        Assert.Equal(before.Text.DecompressedData, after.Text.DecompressedData);
        Assert.Equal(before.Data.DecompressedData, after.Data.DecompressedData);
        Assert.All(
            ChangedOffsets(before.Ro.DecompressedData, after.Ro.DecompressedData),
            changedOffset => Assert.Contains(
                SwShTypeChartMainPatcher.ReservedMainRoRegions(),
                region => SwShExeFsReservedRegionLedger.Overlaps(region, changedOffset, 1)));
    }

    [Fact]
    public void ApplyVanillaChartRestoresOnlyTypeChartBytesAndPreservesOtherExeFsEdits()
    {
        var mainWithOtherEdits = CreateSyntheticTypeChartMainWithOtherExeFsEdits();
        var customValues = SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
        customValues[0] = 0;
        customValues[(1 * SwShTypeChartMainPatcher.TypeCount) + 4] = 2;

        var installed = SwShTypeChartMainPatcher.ApplyChart(mainWithOtherEdits, customValues, ProjectGame.Sword);
        var restored = SwShTypeChartMainPatcher.ApplyChart(
            installed,
            SwShTypeChartMainPatcher.VanillaChartValues,
            ProjectGame.Sword);
        var restoredAnalysis = SwShTypeChartMainPatcher.Analyze(restored, ProjectGame.Sword);

        Assert.Equal(SwShTypeChartMainKind.Vanilla, restoredAnalysis.Kind);
        AssertOnlyReservedRoBytesChanged(mainWithOtherEdits, installed);
        AssertOnlyReservedRoBytesChanged(installed, restored);
        AssertOtherExeFsEditsStillPresent(restored);
    }

    [Fact]
    public void ApplyChartRejectsIllegalEffectivenessValues()
    {
        var main = CreateSyntheticTypeChartMain();
        var values = SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
        values[0] = 3;

        Assert.Throws<InvalidDataException>(() =>
            SwShTypeChartMainPatcher.ApplyChart(main, values, ProjectGame.Sword));
    }

    [Fact]
    public void StageAndApplyTypeChartWritesExeFsMainOutput()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseExeFsFile("main", CreateSyntheticTypeChartMain());
        var values = SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
        values[0] = 0;
        values[(14 * SwShTypeChartMainPatcher.TypeCount) + 17] = 2;
        var service = new SwShTypeChartEditSessionService();

        var staged = service.StageChart(temp.Paths, values, session: null);
        var plan = service.CreateChangePlan(temp.Paths, staged.Session);
        var apply = service.ApplyChangePlan(temp.Paths, staged.Session, plan);
        var outputMain = File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main"));
        var analysis = SwShTypeChartMainPatcher.Analyze(outputMain, ProjectGame.Sword);

        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Single(plan.Writes);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShTypeChartWorkflowService.ExeFsMainPath);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShTypeChartWorkflowService.ExeFsMainPath);
        Assert.Equal(SwShTypeChartMainKind.Modified, analysis.Kind);
        Assert.Equal(values, analysis.EffectivenessValues);
    }

    private static byte[] CreateSyntheticTypeChartMain(ProjectGame game = ProjectGame.Sword)
    {
        var text = Enumerable.Range(0, 0x40).Select(index => (byte)(0x80 + index)).ToArray();
        var ro = new byte[SwShTypeChartMainPatcher.SwordRoChartOffset + SwShTypeChartMainPatcher.ChartLength + 0x40];
        var data = Enumerable.Range(0, 0x20).Select(index => (byte)(0x20 + index)).ToArray();
        Array.Fill(ro, (byte)0xCC);
        SwShTypeChartMainPatcher.VanillaChartValues
            .Select(value => checked((byte)value))
            .ToArray()
            .CopyTo(ro.AsSpan(SwShTypeChartMainPatcher.SwordRoChartOffset));

        return CreateNso(text, ro, data, BuildIdForGame(game));
    }

    private static byte[] CreateSyntheticTypeChartMainWithOtherExeFsEdits()
    {
        var nso = SwShNsoFile.Parse(CreateSyntheticTypeChartMain());
        var text = nso.Text.DecompressedData.ToArray();
        var ro = nso.Ro.DecompressedData.ToArray();
        var data = nso.Data.DecompressedData.ToArray();
        text[0x10] = 0x42;
        ro[0x100] = 0x24;
        data[0x08] = 0x66;
        return nso.Write(textDecompressedData: text, roDecompressedData: ro, dataDecompressedData: data);
    }

    private static void AssertOnlyReservedRoBytesChanged(byte[] beforeMain, byte[] afterMain)
    {
        var before = SwShNsoFile.Parse(beforeMain);
        var after = SwShNsoFile.Parse(afterMain);

        Assert.Equal(before.Text.DecompressedData, after.Text.DecompressedData);
        Assert.Equal(before.Data.DecompressedData, after.Data.DecompressedData);
        Assert.All(
            ChangedOffsets(before.Ro.DecompressedData, after.Ro.DecompressedData),
            changedOffset => Assert.Contains(
                SwShTypeChartMainPatcher.ReservedMainRoRegions(),
                region => SwShExeFsReservedRegionLedger.Overlaps(region, changedOffset, 1)));
    }

    private static void AssertOtherExeFsEditsStillPresent(byte[] main)
    {
        var nso = SwShNsoFile.Parse(main);
        Assert.Equal(0x42, nso.Text.DecompressedData[0x10]);
        Assert.Equal(0x24, nso.Ro.DecompressedData[0x100]);
        Assert.Equal(0x66, nso.Data.DecompressedData[0x08]);
    }

    private static int[] ChangedOffsets(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        return Enumerable.Range(0, before.Length)
            .Where(index => before[index] != after[index])
            .ToArray();
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[] buildId)
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
        buildId.CopyTo(output.AsSpan(0x40, 0x20));
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

    private static void WriteSegmentHeader(byte[] output, int offset, int fileOffset, int memoryOffset, int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static byte[] BuildIdForGame(ProjectGame game)
    {
        return Convert.FromHexString(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
