// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.GymUniformRemoval;
using Xunit;

namespace KM.SwSh.Tests.GymUniformRemoval;

public sealed class SwShGymUniformRemovalMainPatcherSafetyTests
{
    [Fact]
    public void AnalyzeRejectsNonCanonicalBuildIdSuffix()
    {
        var main = GymUniformRemovalTestFixtures.CreateMain(ProjectGame.Sword);
        main[0x54] = 0x7F;

        var analysis = SwShGymUniformRemovalMainPatcher.Analyze(main, ProjectGame.Sword);

        Assert.Equal(SwShGymUniformRemovalInstallKind.UnsupportedBuild, analysis.Kind);
    }

    [Theory]
    [InlineData(0xA0, ".text")]
    [InlineData(0xC0, ".ro")]
    [InlineData(0xE0, ".data")]
    public void AnalyzeRejectsMismatchedRequiredSegmentHash(int hashOffset, string segmentName)
    {
        var main = GymUniformRemovalTestFixtures.CreateMain(ProjectGame.Sword);
        main[hashOffset] ^= 0xFF;

        var analysis = SwShGymUniformRemovalMainPatcher.Analyze(main, ProjectGame.Sword);

        Assert.Equal(SwShGymUniformRemovalInstallKind.Conflict, analysis.Kind);
        Assert.Contains(segmentName, analysis.Message, StringComparison.Ordinal);
        Assert.Contains("required NSO header hash", analysis.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibleIdentityRejectsStableHeaderAndSegmentLayoutMismatches()
    {
        var baseMain = GymUniformRemovalTestFixtures.CreateMain(ProjectGame.Sword);
        var headerMismatch = baseMain.ToArray();
        headerMismatch[0x08] ^= 0x01;
        var headerException = Assert.Throws<InvalidDataException>(() =>
            SwShGymUniformRemovalMainPatcher.EnsureCompatibleExecutableIdentity(
                baseMain,
                headerMismatch));
        Assert.Contains("stable header metadata", headerException.Message, StringComparison.Ordinal);

        var layoutMismatch = baseMain.ToArray();
        var roMemoryOffset = BinaryPrimitives.ReadInt32LittleEndian(layoutMismatch.AsSpan(0x24));
        var dataMemoryOffset = BinaryPrimitives.ReadInt32LittleEndian(layoutMismatch.AsSpan(0x34));
        BinaryPrimitives.WriteInt32LittleEndian(layoutMismatch.AsSpan(0x24), roMemoryOffset + 0x10);
        BinaryPrimitives.WriteInt32LittleEndian(layoutMismatch.AsSpan(0x34), dataMemoryOffset + 0x10);
        var layoutException = Assert.Throws<InvalidDataException>(() =>
            SwShGymUniformRemovalMainPatcher.EnsureCompatibleExecutableIdentity(
                baseMain,
                layoutMismatch));
        Assert.Contains("matching", layoutException.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void DirectApplyAndRestorePreserveEveryUnownedSemanticByte(ProjectGame game)
    {
        var baseMain = GymUniformRemovalTestFixtures.CreateMain(game);
        var effectiveMain = GymUniformRemovalTestFixtures.MutateUnownedSemanticBytes(baseMain);
        var effective = NsoFile.Parse(effectiveMain);

        var installed = SwShGymUniformRemovalMainPatcher.Apply(effectiveMain, game);
        var restored = SwShGymUniformRemovalMainPatcher.RestoreFromBase(installed, baseMain, game);
        var actual = NsoFile.Parse(restored);

        Assert.Equal(effective.Text.DecompressedData, actual.Text.DecompressedData);
        Assert.Equal(effective.Ro.DecompressedData, actual.Ro.DecompressedData);
        Assert.Equal(effective.Data.DecompressedData, actual.Data.DecompressedData);
        Assert.Equal(
            SwShGymUniformRemovalInstallKind.NotInstalled,
            SwShGymUniformRemovalMainPatcher.Analyze(restored, game).Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword, ProjectGame.Shield)]
    [InlineData(ProjectGame.Shield, ProjectGame.Sword)]
    public void AnalyzeAndIpsCreationRejectASelectedGameMismatch(
        ProjectGame selectedGame,
        ProjectGame mainGame)
    {
        var main = GymUniformRemovalTestFixtures.CreateMain(mainGame);

        var analysis = SwShGymUniformRemovalMainPatcher.Analyze(main, selectedGame);

        Assert.Equal(SwShGymUniformRemovalInstallKind.GameMismatch, analysis.Kind);
        Assert.Equal(mainGame, analysis.DetectedGame);
        Assert.Throws<InvalidDataException>(() =>
            SwShGymUniformRemovalMainPatcher.CreateIpsPatch(main, selectedGame));
    }

    [Theory]
    [InlineData(ProjectGame.Sword, SwShGymUniformRemovalMainPatcher.SwordPatchOffset)]
    [InlineData(ProjectGame.Shield, SwShGymUniformRemovalMainPatcher.ShieldPatchOffset)]
    public void CreateIpsPatchWritesTheExactCanonicalSingleRecord(
        ProjectGame game,
        int patchOffset)
    {
        var main = GymUniformRemovalTestFixtures.CreateMain(game);

        var ips = SwShGymUniformRemovalMainPatcher.CreateIpsPatch(main, game);

        var expected = new byte[23];
        Encoding.ASCII.GetBytes("IPS32").CopyTo(expected, 0);
        BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(5), unchecked((uint)patchOffset));
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(9), SwShGymUniformRemovalMainPatcher.PatchLength);
        new byte[]
        {
            0xE0, 0x03, 0x00, 0x32,
            0xC0, 0x03, 0x5F, 0xD6,
        }.CopyTo(expected, 11);
        Encoding.ASCII.GetBytes("EEOF").CopyTo(expected, 19);

        Assert.Equal(expected, ips);
        Assert.Equal(23, ips.Length);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void AnalyzeIpsRecognizesCanonicalLegacyAndForeignArtifacts(ProjectGame game)
    {
        var main = GymUniformRemovalTestFixtures.CreateMain(game);
        var canonical = SwShGymUniformRemovalMainPatcher.CreateIpsPatch(main, game);
        var legacy = canonical[..^4]
            .Concat(Encoding.ASCII.GetBytes("EOF"))
            .ToArray();
        var foreign = canonical.ToArray();
        foreign[11] ^= 0x01;

        Assert.Equal(
            SwShGymUniformRemovalInstallKind.InstalledV1,
            SwShGymUniformRemovalMainPatcher.AnalyzeIpsPatch(canonical, main, game).Kind);
        Assert.Equal(
            SwShGymUniformRemovalInstallKind.InstalledCompatible,
            SwShGymUniformRemovalMainPatcher.AnalyzeIpsPatch(legacy, main, game).Kind);
        Assert.Equal(
            SwShGymUniformRemovalInstallKind.ForeignPatch,
            SwShGymUniformRemovalMainPatcher.AnalyzeIpsPatch(foreign, main, game).Kind);
    }

    [Theory]
    [InlineData(
        ProjectGame.Sword,
        "-sword-",
        SwShGymUniformRemovalMainPatcher.SwordPatchOffset)]
    [InlineData(
        ProjectGame.Shield,
        "-shield-",
        SwShGymUniformRemovalMainPatcher.ShieldPatchOffset)]
    public void ActiveReservationsContainOnlyTheSelectedGamesOwnedRange(
        ProjectGame game,
        string gameToken,
        int expectedPatchOffset)
    {
        var region = Assert.Single(SwShGymUniformRemovalMainPatcher.ReservedMainTextRegions(game));

        Assert.Contains(gameToken, region.FeatureId, StringComparison.Ordinal);
        Assert.Equal(expectedPatchOffset, region.StartOffset);
        Assert.Equal(SwShGymUniformRemovalMainPatcher.PatchLength, region.Length);
    }

    [Fact]
    public void ActiveReservationsRejectUnsupportedGamesWithoutChangingTheGlobalLedgerView()
    {
        Assert.Empty(SwShGymUniformRemovalMainPatcher.ReservedMainTextRegions(ProjectGame.Scarlet));
        Assert.Equal(2, SwShGymUniformRemovalMainPatcher.ReservedMainTextRegions().Count);
    }
}
