// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresMainPatcherTests
{
    [Fact]
    public void AnalyzeRejectsStableHeaderMetadataMismatch()
    {
        var archive = SwShDynamaxAdventureTestFixtures.CreateArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateCompatibleMain();
        var changedMain = baseMain.ToArray();
        changedMain[0x08] ^= 0x01;

        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            changedMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);

        Assert.Equal(SwShDynamaxAdventuresMainKind.Conflict, analysis.Kind);
        Assert.Contains("stable NSO", analysis.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeAllowsAppendOnlyForeignTextWithStableHeaderMetadata()
    {
        var archive = SwShDynamaxAdventureTestFixtures.CreateArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateCompatibleMain();
        var nso = NsoFile.Parse(baseMain);
        var appendedText = nso.Text.DecompressedData.Concat(new byte[0x20]).ToArray();
        var changedMain = nso.Write(textDecompressedData: appendedText);

        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            changedMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);

        Assert.Equal(SwShDynamaxAdventuresMainKind.Vanilla, analysis.Kind);
    }

    [Fact]
    public void AnalyzeRejectsTruncatedEffectiveText()
    {
        var archive = SwShDynamaxAdventureTestFixtures.CreateArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateCompatibleMain();
        var nso = NsoFile.Parse(baseMain);
        var truncatedText = nso.Text.DecompressedData[..^sizeof(uint)].ToArray();
        var changedMain = nso.Write(textDecompressedData: truncatedText);

        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            changedMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);

        Assert.Equal(SwShDynamaxAdventuresMainKind.Conflict, analysis.Kind);
        Assert.Contains("complete base text prefix", analysis.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconcilePreservesForeignLegacyCallSiteInstruction()
    {
        var archive = CreateBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive);
        const uint ForeignInstruction = 0xD503201F;
        var currentMain = RewriteText(baseMain, text => BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset),
            ForeignInstruction));

        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            currentMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);
        var output = SwShDynamaxAdventuresMainPatcher.Reconcile(
            currentMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);

        Assert.Equal(SwShDynamaxAdventuresMainKind.Vanilla, analysis.Kind);
        Assert.False(analysis.HasLegacyBossTargetPatch);
        Assert.Equal(
            ForeignInstruction,
            ReadInstruction(NsoFile.Parse(output).Text.DecompressedData, SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset));
    }

    [Fact]
    public void ReconcilePreservesForeignBranchAtLegacyCallSite()
    {
        var archive = CreateBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive);
        var foreignInstruction = SwShDynamaxAdventuresBossTargetPatcher.EncodeBranch(
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset + 0x40);
        var currentMain = RewriteText(baseMain, text => BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset),
            foreignInstruction));

        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            currentMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);
        var output = SwShDynamaxAdventuresMainPatcher.Reconcile(
            currentMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);

        Assert.Equal(SwShDynamaxAdventuresMainKind.Vanilla, analysis.Kind);
        Assert.False(analysis.HasLegacyBossTargetPatch);
        Assert.Equal(
            foreignInstruction,
            ReadInstruction(
                NsoFile.Parse(output).Text.DecompressedData,
                SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset));
    }

    [Fact]
    public void ReconcileCleansExactLegacyBossPatch()
    {
        var archive = CreateBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive);
        var legacyMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);

        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            legacyMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);
        var output = SwShDynamaxAdventuresMainPatcher.Reconcile(
            legacyMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);

        Assert.True(analysis.HasLegacyBossTargetPatch);
        Assert.Equal(
            NsoFile.Parse(baseMain).Text.DecompressedData,
            NsoFile.Parse(output).Text.DecompressedData);
    }

    [Fact]
    public void ReconcileClearsExactLegacyStubsWhenLaterOwnerPayloadPreventsTrim()
    {
        var archive = CreateBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive);
        var baseTextLength = NsoFile.Parse(baseMain).Text.DecompressedData.Length;
        var legacyMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);
        var marker = Enumerable.Range(1, 24).Select(value => checked((byte)value)).ToArray();
        var legacyNso = NsoFile.Parse(legacyMain);
        var legacyWithMarker = legacyNso.Write(
            textDecompressedData: legacyNso.Text.DecompressedData.Concat(marker).ToArray());

        var output = SwShDynamaxAdventuresMainPatcher.Reconcile(
            legacyWithMarker,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);
        var outputText = NsoFile.Parse(output).Text.DecompressedData;

        Assert.True(outputText.AsSpan()[^marker.Length..].SequenceEqual(marker));
        Assert.All(
            outputText.AsSpan(baseTextLength, SwShDynamaxAdventuresBossTargetPatcher.StubSize * 2).ToArray(),
            value => Assert.Equal(0, value));
    }

    [Fact]
    public void AnalyzeRejectsDamagedRecognizableLegacyBossPatch()
    {
        var archive = CreateBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateProductCompatibleMain(archive);
        var baseTextLength = NsoFile.Parse(baseMain).Text.DecompressedData.Length;
        var legacyMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);
        var damagedLegacyMain = RewriteText(legacyMain, text =>
            BinaryPrimitives.WriteUInt32LittleEndian(
                text.AsSpan(baseTextLength + 0x08, sizeof(uint)),
                0xD503201F));

        var analysis = SwShDynamaxAdventuresMainPatcher.Analyze(
            damagedLegacyMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);

        Assert.Equal(SwShDynamaxAdventuresMainKind.Conflict, analysis.Kind);
        Assert.Contains("partial or damaged historical KM boss-target remap", analysis.Message, StringComparison.Ordinal);
        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShDynamaxAdventuresMainPatcher.Reconcile(
                damagedLegacyMain,
                baseMain,
                archive,
                archive,
                ProjectGame.Sword));
        Assert.Contains("partial or damaged historical KM boss-target remap", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconcilePreservesAppendOnlyOtherOwnerPayload()
    {
        var archive = SwShDynamaxAdventureTestFixtures.CreateArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateCompatibleMain();
        var nso = NsoFile.Parse(baseMain);
        var marker = Enumerable.Range(1, 24).Select(value => checked((byte)value)).ToArray();
        var text = nso.Text.DecompressedData.Concat(marker).ToArray();
        var currentMain = nso.Write(textDecompressedData: text);

        var output = SwShDynamaxAdventuresMainPatcher.Reconcile(
            currentMain,
            baseMain,
            archive,
            archive,
            ProjectGame.Sword);

        Assert.True(NsoFile.Parse(output).Text.DecompressedData.AsSpan()[^marker.Length..].SequenceEqual(marker));
    }

    [Fact]
    public void ReconcilePreservesSummaryStridePaddingByte()
    {
        var baseArchive = SwShDynamaxAdventureTestFixtures.CreateArchive();
        var effectiveArchive = WithEntry(baseArchive, 1, entry => entry with { Species = 26 });
        var baseMain = RewriteRo(
            SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(),
            ro => ro[SwShDynamaxAdventuresMainPatcher.SummaryOffset + 1] = 0xA5);

        var output = SwShDynamaxAdventuresMainPatcher.Reconcile(
            baseMain,
            baseMain,
            effectiveArchive,
            baseArchive,
            ProjectGame.Sword);
        var ro = NsoFile.Parse(output).Ro.DecompressedData;

        Assert.Equal(0xA5, ro[SwShDynamaxAdventuresMainPatcher.SummaryOffset + 1]);
    }

    [Fact]
    public void AnalyzeRejectsForeignValidatorAndSummaryStates()
    {
        var baseArchive = SwShDynamaxAdventureTestFixtures.CreateArchive();
        var effectiveArchive = WithEntry(baseArchive, 1, entry => entry with { Species = 26 });
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateCompatibleMain();
        var foreignValidator = RewriteText(baseMain, text => BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(SwShDynamaxAdventuresMainPatcher.LocalSpeciesPresentMismatchBranchOffset),
            0xAAAAAAAA));
        var foreignSummary = RewriteRo(baseMain, ro => BinaryPrimitives.WriteInt16LittleEndian(
            ro.AsSpan(SwShDynamaxAdventuresMainPatcher.SummaryOffset + 2),
            777));

        Assert.Equal(
            SwShDynamaxAdventuresMainKind.Conflict,
            SwShDynamaxAdventuresMainPatcher.Analyze(
                foreignValidator,
                baseMain,
                effectiveArchive,
                baseArchive,
                ProjectGame.Sword).Kind);
        Assert.Equal(
            SwShDynamaxAdventuresMainKind.Conflict,
            SwShDynamaxAdventuresMainPatcher.Analyze(
                foreignSummary,
                baseMain,
                effectiveArchive,
                baseArchive,
                ProjectGame.Sword).Kind);
    }

    [Fact]
    public void ReconcileUsesShieldValidatorOffsetsAndRejectsSwordSelection()
    {
        var baseArchive = SwShDynamaxAdventureTestFixtures.CreateArchive();
        var effectiveArchive = WithEntry(baseArchive, 1, entry => entry with { Species = 26 });
        var shieldMain = SwShDynamaxAdventureTestFixtures.CreateCompatibleMain(
            SwShDynamaxAdventuresMainPatcher.ShieldCommandValidatorOffsetDelta,
            SwShDynamaxAdventuresMainPatcher.ShieldBuildId);

        var mismatch = SwShDynamaxAdventuresMainPatcher.Analyze(
            shieldMain,
            shieldMain,
            effectiveArchive,
            baseArchive,
            ProjectGame.Sword);
        var output = SwShDynamaxAdventuresMainPatcher.Reconcile(
            shieldMain,
            shieldMain,
            effectiveArchive,
            baseArchive,
            ProjectGame.Shield);
        var text = NsoFile.Parse(output).Text.DecompressedData;

        Assert.Equal(SwShDynamaxAdventuresMainKind.GameMismatch, mismatch.Kind);
        Assert.Equal(
            0xD503201Fu,
            ReadInstruction(
                text,
                SwShDynamaxAdventuresMainPatcher.LocalSpeciesPresentMismatchBranchOffset
                    + SwShDynamaxAdventuresMainPatcher.ShieldCommandValidatorOffsetDelta));
    }

    private static SwShDynamaxAdventureArchive CreateBossArchive()
    {
        return new SwShDynamaxAdventureArchive(
            Enumerable.Range(0, 228)
                .Select(index => new SwShDynamaxAdventureRecord(
                    index,
                    IsSingleCapture: index >= 226,
                    SingleCaptureFlagBlock: (ulong)(index + 1),
                    Field02: 0,
                    Form: 0,
                    GigantamaxState: 1,
                    BallItemId: 4,
                    AdventureIndex: index,
                    Level: index >= 226 ? 70 : 65,
                    Species: index switch { 226 => 144, 227 => 150, _ => 25 },
                    UiMessageId: (ulong)(index + 1),
                    OtGender: 0,
                    Version: 0,
                    ShinyRoll: 1,
                    new SwShDynamaxAdventureIvs(-2, -1, -1, -1, -1, -1),
                    Ability: 0,
                    IsStoryProgressGated: false,
                    Moves: [1, 2, 3, 4]))
                .ToArray());
    }

    private static SwShDynamaxAdventureArchive WithEntry(
        SwShDynamaxAdventureArchive archive,
        int entryIndex,
        Func<SwShDynamaxAdventureRecord, SwShDynamaxAdventureRecord> update)
    {
        return new SwShDynamaxAdventureArchive(
            archive.Entries.Select(entry => entry.EntryIndex == entryIndex ? update(entry) : entry).ToArray());
    }

    private static byte[] RewriteText(byte[] main, Action<byte[]> rewrite)
    {
        var nso = NsoFile.Parse(main);
        var text = nso.Text.DecompressedData.ToArray();
        rewrite(text);
        return nso.Write(textDecompressedData: text);
    }

    private static byte[] RewriteRo(byte[] main, Action<byte[]> rewrite)
    {
        var nso = NsoFile.Parse(main);
        var ro = nso.Ro.DecompressedData.ToArray();
        rewrite(ro);
        return nso.Write(roDecompressedData: ro);
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text.Slice(offset, sizeof(uint)));
    }
}
