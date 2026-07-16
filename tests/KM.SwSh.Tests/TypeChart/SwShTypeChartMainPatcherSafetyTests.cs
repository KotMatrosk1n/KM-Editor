// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.TypeChart;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.TypeChart;

public sealed class SwShTypeChartMainPatcherSafetyTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private static readonly byte[] DependenciesBeforeChart =
    [
        0xE8, 0x4C, 0x74, 0xFE, 0x0C, 0x4D, 0x74, 0xFE,
        0x08, 0x4D, 0x74, 0xFE, 0x0C, 0x4D, 0x74, 0xFE,
        0x0C, 0x4D, 0x74, 0xFE, 0x0C, 0x4D, 0x74, 0xFE,
        0xF8, 0x4C, 0x74, 0xFE, 0xE0, 0x4D, 0x74, 0xFE,
        0xEC, 0x4D, 0x74, 0xFE, 0xF4, 0x4D, 0x74, 0xFE,
        0xEC, 0x4D, 0x74, 0xFE, 0x08, 0x4E, 0x74, 0xFE,
        0xEC, 0x4D, 0x74, 0xFE, 0xEC, 0x4D, 0x74, 0xFE,
        0xEC, 0x4D, 0x74, 0xFE, 0x00, 0x4E, 0x74, 0xFE,
    ];

    private static readonly byte[] DependenciesAfterChart =
    [
        0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00,
        0x08, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00,
        0x80, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
        0x00, 0x02, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00,
        0x00, 0x08, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00,
        0xF8, 0x5D, 0x74, 0xFE, 0x10, 0x5E, 0x74, 0xFE,
    ];

    [Theory]
    [InlineData(ProjectGame.Sword, SwShTypeChartMainPatcher.SwordRoChartOffset)]
    [InlineData(ProjectGame.Shield, SwShTypeChartMainPatcher.ShieldRoChartOffset)]
    public void AnalyzeUsesTheVerifiedSelectedGameMapping(ProjectGame game, int expectedOffset)
    {
        var analysis = SwShTypeChartMainPatcher.Analyze(CreateSyntheticMain(game), game);

        Assert.Equal(SwShTypeChartMainKind.Vanilla, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal(expectedOffset, analysis.ChartOffset);
        Assert.Equal($"main.ro+0x{expectedOffset:X8}", analysis.ChartOffsetHex);
        Assert.Equal(BuildIdForGame(game)[..0x14], Convert.FromHexString(analysis.BuildId));
        Assert.Equal(SwShTypeChartMainPatcher.VanillaChartValues, analysis.EffectivenessValues);
    }

    [Fact]
    public void AnalyzeRejectsNonCanonicalBuildIdSuffix()
    {
        var main = CreateSyntheticMain(ProjectGame.Sword);
        main[0x54] = 0x7F;

        var analysis = SwShTypeChartMainPatcher.Analyze(main, ProjectGame.Sword);

        Assert.Equal(SwShTypeChartMainKind.UnsupportedBuild, analysis.Kind);
    }

    [Fact]
    public void AnalyzeRejectsMismatchedRequiredRoHash()
    {
        var main = CreateSyntheticMain(ProjectGame.Sword);
        main[0xC0] ^= 0xFF;

        var analysis = SwShTypeChartMainPatcher.Analyze(main, ProjectGame.Sword);

        Assert.Equal(SwShTypeChartMainKind.Conflict, analysis.Kind);
        Assert.Contains("required NSO header hash", analysis.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword, true)]
    [InlineData(ProjectGame.Sword, false)]
    [InlineData(ProjectGame.Shield, true)]
    [InlineData(ProjectGame.Shield, false)]
    public void AnalyzeRejectsChangedTableDependencies(ProjectGame game, bool before)
    {
        var main = CreateSyntheticMain(
            game,
            ro => ro[
                before
                    ? SwShTypeChartMainPatcher.SwordRoChartOffset - 1
                    : SwShTypeChartMainPatcher.SwordRoChartOffset + SwShTypeChartMainPatcher.ChartLength] ^= 0x01);

        var analysis = SwShTypeChartMainPatcher.Analyze(main, game);

        Assert.Equal(SwShTypeChartMainKind.Conflict, analysis.Kind);
        Assert.Contains(before ? "before" : "after", analysis.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void AnalyzeDoesNotRelocateTheOwnedTable(ProjectGame game)
    {
        var alternateOffset = SwShTypeChartMainPatcher.SwordRoChartOffset + 0x300;
        var main = CreateSyntheticMain(
            game,
            ro =>
            {
                Array.Fill(
                    ro,
                    (byte)0xCC,
                    SwShTypeChartMainPatcher.SwordRoChartOffset,
                    SwShTypeChartMainPatcher.ChartLength);
                SwShTypeChartMainPatcher.VanillaChartValues
                    .Select(value => checked((byte)value))
                    .ToArray()
                    .CopyTo(ro.AsSpan(alternateOffset));
            });

        var analysis = SwShTypeChartMainPatcher.Analyze(main, game);

        Assert.Equal(SwShTypeChartMainKind.MissingChart, analysis.Kind);
        Assert.Equal(SwShTypeChartMainPatcher.SwordRoChartOffset, analysis.ChartOffset);
    }

    [Fact]
    public void ApplyRequiresAnExplicitSupportedGame()
    {
        var main = CreateSyntheticMain(ProjectGame.Sword);

        Assert.Throws<InvalidDataException>(() =>
            SwShTypeChartMainPatcher.ApplyChart(
                main,
                SwShTypeChartMainPatcher.VanillaChartValues,
                expectedGame: null));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RestoreFromBaseCopiesOnlyTheOwnedChart(ProjectGame game)
    {
        var baseMain = CreateSyntheticMain(game);
        var currentNso = NsoFile.Parse(baseMain);
        var text = currentNso.Text.DecompressedData.ToArray();
        var ro = currentNso.Ro.DecompressedData.ToArray();
        var data = currentNso.Data.DecompressedData.ToArray();
        text[0x10] ^= 0x5A;
        ro[0x100] ^= 0x5A;
        data[0x08] ^= 0x5A;
        var currentWithOtherEdits = currentNso.Write(
            textDecompressedData: text,
            roDecompressedData: ro,
            dataDecompressedData: data);
        var custom = SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
        custom[0] = 0;
        var installed = SwShTypeChartMainPatcher.ApplyChart(currentWithOtherEdits, custom, game);

        var restored = SwShTypeChartMainPatcher.RestoreFromBase(installed, baseMain, game);
        var restoredNso = NsoFile.Parse(restored);

        Assert.Equal(SwShTypeChartMainKind.Vanilla, SwShTypeChartMainPatcher.Analyze(restored, game).Kind);
        Assert.Equal(text, restoredNso.Text.DecompressedData);
        Assert.Equal(data, restoredNso.Data.DecompressedData);
        Assert.Equal(0x5A ^ currentNso.Ro.DecompressedData[0x100], restoredNso.Ro.DecompressedData[0x100]);
        Assert.All(
            ChangedOffsets(NsoFile.Parse(installed).Ro.DecompressedData, restoredNso.Ro.DecompressedData),
            offset => Assert.Contains(
                SwShTypeChartMainPatcher.ReservedMainRoRegions(),
                region => SwShExeFsReservedRegionLedger.Overlaps(region, offset, 1)));
    }

    [Fact]
    public void RestoreFromBaseRejectsModifiedBaseChart()
    {
        var baseMain = CreateSyntheticMain(ProjectGame.Sword);
        var custom = SwShTypeChartMainPatcher.VanillaChartValues.ToArray();
        custom[0] = 0;
        var modifiedBase = SwShTypeChartMainPatcher.ApplyChart(baseMain, custom, ProjectGame.Sword);

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShTypeChartMainPatcher.RestoreFromBase(modifiedBase, modifiedBase, ProjectGame.Sword));

        Assert.Contains("vanilla base", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibleIdentityRejectsStableHeaderMismatch()
    {
        var baseMain = CreateSyntheticMain(ProjectGame.Sword);
        var effectiveMain = baseMain.ToArray();
        effectiveMain[0x08] ^= 0x01;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShTypeChartMainPatcher.EnsureCompatibleExecutableIdentity(baseMain, effectiveMain));

        Assert.Contains("stable header metadata", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VanillaValuesUseVerifiedGameOrderAndRawEncoding()
    {
        const int normal = 0;
        const int fighting = 1;
        const int flying = 2;
        const int poison = 3;
        const int ground = 4;
        const int ghost = 7;
        const int steel = 8;
        const int fire = 9;
        const int water = 10;
        const int grass = 11;
        const int dragon = 15;
        const int fairy = 17;
        var values = SwShTypeChartMainPatcher.VanillaChartValues;

        Assert.Equal(SwShTypeChartMainPatcher.ChartLength, values.Count);
        Assert.All(values, value => Assert.Contains(value, new[] { 0, 2, 4, 8 }));
        Assert.Equal(0, At(values, normal, ghost));
        Assert.Equal(8, At(values, fighting, normal));
        Assert.Equal(8, At(values, flying, fighting));
        Assert.Equal(0, At(values, poison, steel));
        Assert.Equal(0, At(values, ground, flying));
        Assert.Equal(8, At(values, fire, grass));
        Assert.Equal(2, At(values, fire, water));
        Assert.Equal(8, At(values, fairy, dragon));
    }

    private static int At(IReadOnlyList<int> values, int attack, int defense)
    {
        return values[(attack * SwShTypeChartMainPatcher.TypeCount) + defense];
    }

    private static byte[] CreateSyntheticMain(ProjectGame game, Action<byte[]>? configureRo = null)
    {
        var text = Enumerable.Range(0, 0x40).Select(index => (byte)(0x80 + index)).ToArray();
        var ro = new byte[
            SwShTypeChartMainPatcher.SwordRoChartOffset
            + SwShTypeChartMainPatcher.ChartLength
            + SwShTypeChartMainPatcher.DependencyLength
            + 0x400];
        var data = Enumerable.Range(0, 0x20).Select(index => (byte)(0x20 + index)).ToArray();
        Array.Fill(ro, (byte)0xCC);
        DependenciesBeforeChart.CopyTo(
            ro.AsSpan(
                SwShTypeChartMainPatcher.SwordRoChartOffset - DependenciesBeforeChart.Length,
                DependenciesBeforeChart.Length));
        SwShTypeChartMainPatcher.VanillaChartValues
            .Select(value => checked((byte)value))
            .ToArray()
            .CopyTo(ro.AsSpan(SwShTypeChartMainPatcher.SwordRoChartOffset));
        DependenciesAfterChart.CopyTo(
            ro.AsSpan(
                SwShTypeChartMainPatcher.SwordRoChartOffset + SwShTypeChartMainPatcher.ChartLength,
                DependenciesAfterChart.Length));
        configureRo?.Invoke(ro);
        return CreateNso(text, ro, data, BuildIdForGame(game));
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[] buildId)
    {
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];
        var flags = NsoFlags.CheckHashText | NsoFlags.CheckHashRo | NsoFlags.CheckHashData;

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x0C), (uint)flags);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        buildId.CopyTo(output.AsSpan(0x40, 0x20));
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

    private static byte[] BuildIdForGame(ProjectGame game)
    {
        return Convert.FromHexString(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId);
    }

    private static int[] ChangedOffsets(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        return Enumerable.Range(0, before.Length)
            .Where(index => before[index] != after[index])
            .ToArray();
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
