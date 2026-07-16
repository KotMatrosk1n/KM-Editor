// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.FashionUnlock;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.FashionUnlock;

public sealed class SwShFashionUnlockMainPatcherSafetyTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private const int UnownedTextOffset = 0x100;

    private static readonly byte[] DirectGetterVanilla =
    [
        0xE8, 0x03, 0x00, 0xAA,
        0xE0, 0x03, 0x1F, 0x2A,
    ];

    private static readonly byte[] MappedGetterVanilla =
    [
        0xFF, 0x03, 0x06, 0xD1,
        0xFC, 0x5F, 0x14, 0xA9,
    ];

    private static readonly byte[] ReturnTrueStub =
    [
        0x20, 0x00, 0x80, 0x52,
        0xC0, 0x03, 0x5F, 0xD6,
    ];

    private static readonly Lazy<byte[]> SwordMain = new(() => CreateSyntheticMainCore(ProjectGame.Sword));
    private static readonly Lazy<byte[]> ShieldMain = new(() => CreateSyntheticMainCore(ProjectGame.Shield));

    [Theory]
    [InlineData(ProjectGame.Sword, SwShFashionUnlockMainPatcher.SwordDirectGetterOffset, SwShFashionUnlockMainPatcher.SwordMappedGetterOffset)]
    [InlineData(ProjectGame.Shield, SwShFashionUnlockMainPatcher.ShieldDirectGetterOffset, SwShFashionUnlockMainPatcher.ShieldMappedGetterOffset)]
    public void AnalyzeUsesTheVerifiedSelectedGameMapping(
        ProjectGame game,
        int expectedDirectOffset,
        int expectedMappedOffset)
    {
        var analysis = SwShFashionUnlockMainPatcher.Analyze(CreateSyntheticMain(game), game);

        Assert.Equal(SwShFashionUnlockInstallKind.NotInstalled, analysis.Kind);
        Assert.Equal(game, analysis.DetectedGame);
        Assert.Equal($"main.text+0x{expectedDirectOffset:X8}", analysis.DirectGetterOffsetHex);
        Assert.Equal($"main.text+0x{expectedMappedOffset:X8}", analysis.MappedGetterOffsetHex);
    }

    [Fact]
    public void AnalyzeRejectsNonCanonicalBuildIdSuffix()
    {
        var main = CreateSyntheticMain(ProjectGame.Sword);
        main[0x54] = 0x7F;

        var analysis = SwShFashionUnlockMainPatcher.Analyze(main, ProjectGame.Sword);

        Assert.Equal(SwShFashionUnlockInstallKind.UnsupportedBuild, analysis.Kind);
    }

    [Theory]
    [InlineData(0xA0, ".text")]
    [InlineData(0xC0, ".ro")]
    [InlineData(0xE0, ".data")]
    public void AnalyzeRejectsMismatchedRequiredSegmentHash(int hashOffset, string segmentName)
    {
        var main = CreateSyntheticMain(ProjectGame.Sword);
        main[hashOffset] ^= 0xFF;

        var analysis = SwShFashionUnlockMainPatcher.Analyze(main, ProjectGame.Sword);

        Assert.Equal(SwShFashionUnlockInstallKind.Conflict, analysis.Kind);
        Assert.Contains(segmentName, analysis.Message, StringComparison.Ordinal);
        Assert.Contains("required NSO header hash", analysis.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void ApplyAndRestorePreserveEveryUnownedSemanticByte(ProjectGame game)
    {
        var baseMain = CreateSyntheticMain(game);
        var baseNso = NsoFile.Parse(baseMain);
        var effectiveText = baseNso.Text.DecompressedData.ToArray();
        var effectiveRo = baseNso.Ro.DecompressedData.ToArray();
        var effectiveData = baseNso.Data.DecompressedData.ToArray();
        effectiveText[UnownedTextOffset] ^= 0x5A;
        effectiveRo[0] ^= 0x5A;
        effectiveData[0] ^= 0x5A;
        var effectiveMain = baseNso.Write(
            textDecompressedData: effectiveText,
            roDecompressedData: effectiveRo,
            dataDecompressedData: effectiveData);
        var effectiveNso = NsoFile.Parse(effectiveMain);

        var installed = SwShFashionUnlockMainPatcher.Apply(effectiveMain, game);
        var installedNso = NsoFile.Parse(installed);
        var expectedInstalledText = effectiveText.ToArray();
        ReturnTrueStub.CopyTo(expectedInstalledText.AsSpan(DirectOffset(game)));
        ReturnTrueStub.CopyTo(expectedInstalledText.AsSpan(MappedOffset(game)));

        Assert.Equal(expectedInstalledText, installedNso.Text.DecompressedData);
        Assert.Equal(baseNso.BuildId, installedNso.BuildId);
        AssertPreservedSegment(effectiveNso.Ro, installedNso.Ro);
        AssertPreservedSegment(effectiveNso.Data, installedNso.Data);
        Assert.Equal(SwShFashionUnlockInstallKind.Installed, SwShFashionUnlockMainPatcher.Analyze(installed, game).Kind);

        var restored = SwShFashionUnlockMainPatcher.RestoreFromBase(installed, baseMain, game);
        var restoredNso = NsoFile.Parse(restored);

        Assert.Equal(effectiveText, restoredNso.Text.DecompressedData);
        AssertPreservedSegment(installedNso.Ro, restoredNso.Ro);
        AssertPreservedSegment(installedNso.Data, restoredNso.Data);
        Assert.Equal(SwShFashionUnlockInstallKind.NotInstalled, SwShFashionUnlockMainPatcher.Analyze(restored, game).Kind);
    }

    [Fact]
    public void ApplyAndRestoreRequireAnExplicitSupportedGame()
    {
        var baseMain = CreateSyntheticMain(ProjectGame.Sword);

        Assert.Throws<InvalidDataException>(() => SwShFashionUnlockMainPatcher.Apply(baseMain, expectedGame: null));
        Assert.Throws<InvalidDataException>(() => SwShFashionUnlockMainPatcher.Apply(baseMain, ProjectGame.Scarlet));

        var installed = SwShFashionUnlockMainPatcher.Apply(baseMain, ProjectGame.Sword);
        Assert.Throws<InvalidDataException>(() =>
            SwShFashionUnlockMainPatcher.RestoreFromBase(installed, baseMain, expectedGame: null));
    }

    [Fact]
    public void CompatibleIdentityAcceptsUnownedEffectiveEdits()
    {
        var baseMain = CreateSyntheticMain(ProjectGame.Sword);
        var baseNso = NsoFile.Parse(baseMain);
        var text = baseNso.Text.DecompressedData.ToArray();
        var ro = baseNso.Ro.DecompressedData.ToArray();
        var data = baseNso.Data.DecompressedData.ToArray();
        text[UnownedTextOffset] ^= 0x33;
        ro[0] ^= 0x33;
        data[0] ^= 0x33;
        var effectiveMain = baseNso.Write(
            textDecompressedData: text,
            roDecompressedData: ro,
            dataDecompressedData: data);

        SwShFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseMain, effectiveMain);
    }

    [Fact]
    public void CompatibleIdentityRejectsStableHeaderMismatch()
    {
        var baseMain = CreateSyntheticMain(ProjectGame.Sword);
        var effectiveMain = baseMain.ToArray();
        effectiveMain[0x08] ^= 0x01;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseMain, effectiveMain));

        Assert.Contains("stable header metadata", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibleIdentityRejectsSegmentLayoutMismatch()
    {
        var baseMain = CreateSyntheticMain(ProjectGame.Sword);
        var effectiveMain = baseMain.ToArray();
        var roMemoryOffset = BinaryPrimitives.ReadInt32LittleEndian(effectiveMain.AsSpan(0x24));
        var dataMemoryOffset = BinaryPrimitives.ReadInt32LittleEndian(effectiveMain.AsSpan(0x34));
        BinaryPrimitives.WriteInt32LittleEndian(effectiveMain.AsSpan(0x24), roMemoryOffset + 0x10);
        BinaryPrimitives.WriteInt32LittleEndian(effectiveMain.AsSpan(0x34), dataMemoryOffset + 0x10);

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShFashionUnlockMainPatcher.EnsureCompatibleExecutableIdentity(baseMain, effectiveMain));

        Assert.Contains("matching NSO", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword, "sword", SwShFashionUnlockMainPatcher.SwordDirectGetterOffset, SwShFashionUnlockMainPatcher.SwordMappedGetterOffset)]
    [InlineData(ProjectGame.Shield, "shield", SwShFashionUnlockMainPatcher.ShieldDirectGetterOffset, SwShFashionUnlockMainPatcher.ShieldMappedGetterOffset)]
    public void ActiveReservationsContainOnlyTheSelectedGamesOwnedBytes(
        ProjectGame game,
        string gameToken,
        int expectedDirectOffset,
        int expectedMappedOffset)
    {
        var regions = SwShFashionUnlockMainPatcher.ReservedMainTextRegions(game);

        Assert.Equal(2, regions.Count);
        Assert.All(regions, region => Assert.Contains($"-{gameToken}-", region.FeatureId, StringComparison.Ordinal));
        Assert.Equal(SwShFashionUnlockMainPatcher.PatchLength * 2, regions.Sum(region => region.Length ?? 0));
        Assert.Equal(
            [expectedDirectOffset, expectedMappedOffset],
            regions.Select(region => region.StartOffset!.Value).Order().ToArray());
    }

    [Fact]
    public void ActiveReservationsRejectUnsupportedGamesWithoutChangingTheGlobalLedgerView()
    {
        Assert.Empty(SwShFashionUnlockMainPatcher.ReservedMainTextRegions(ProjectGame.Scarlet));
        Assert.Equal(4, SwShFashionUnlockMainPatcher.ReservedMainTextRegions().Count);
    }

    private static byte[] CreateSyntheticMain(ProjectGame game)
    {
        return (game == ProjectGame.Shield ? ShieldMain : SwordMain).Value.ToArray();
    }

    private static byte[] CreateSyntheticMainCore(ProjectGame game)
    {
        var text = new byte[SwShFashionUnlockMainPatcher.ShieldMappedGetterOffset + SwShFashionUnlockMainPatcher.PatchLength + 0x20];
        var ro = Enumerable.Range(0, 0x20).Select(index => (byte)(0x40 + index)).ToArray();
        var data = Enumerable.Range(0, 0x20).Select(index => (byte)(0x80 + index)).ToArray();
        DirectGetterVanilla.CopyTo(text.AsSpan(DirectOffset(game)));
        MappedGetterVanilla.CopyTo(text.AsSpan(MappedOffset(game)));
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

    private static int DirectOffset(ProjectGame game)
    {
        return game == ProjectGame.Shield
            ? SwShFashionUnlockMainPatcher.ShieldDirectGetterOffset
            : SwShFashionUnlockMainPatcher.SwordDirectGetterOffset;
    }

    private static int MappedOffset(ProjectGame game)
    {
        return game == ProjectGame.Shield
            ? SwShFashionUnlockMainPatcher.ShieldMappedGetterOffset
            : SwShFashionUnlockMainPatcher.SwordMappedGetterOffset;
    }

    private static void AssertPreservedSegment(NsoSegment expected, NsoSegment actual)
    {
        Assert.Equal(expected.Header.MemoryOffset, actual.Header.MemoryOffset);
        Assert.Equal(expected.Header.DecompressedSize, actual.Header.DecompressedSize);
        Assert.Equal(expected.CompressedSize, actual.CompressedSize);
        Assert.Equal(expected.Hash, actual.Hash);
        Assert.Equal(expected.CompressedData, actual.CompressedData);
        Assert.Equal(expected.DecompressedData, actual.DecompressedData);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
