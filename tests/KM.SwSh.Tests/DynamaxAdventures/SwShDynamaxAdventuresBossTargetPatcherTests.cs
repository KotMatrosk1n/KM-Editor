// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.DynamaxAdventures;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresBossTargetPatcherTests
{
    [Fact]
    public void ApplyConditionalTargetSpeciesRemapWritesOwnedBranchesAndStubs()
    {
        var archive = CreateBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();

        var patchedMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);

        var baseNso = NsoFile.Parse(baseMain);
        var patchedNso = NsoFile.Parse(patchedMain);
        Assert.Equal(baseNso.Ro.DecompressedData.ToArray(), patchedNso.Ro.DecompressedData.ToArray());
        Assert.Equal(baseNso.Data.DecompressedData.ToArray(), patchedNso.Data.DecompressedData.ToArray());

        var baseText = baseNso.Text.DecompressedData;
        var patchedText = patchedNso.Text.DecompressedData;
        var stubAOffset = baseText.Length;
        var stubBOffset = stubAOffset + SwShDynamaxAdventuresBossTargetPatcher.StubSize;
        Assert.Equal(stubBOffset + SwShDynamaxAdventuresBossTargetPatcher.StubSize, patchedText.Length);
        Assert.Equal(
            SwShDynamaxAdventuresBossTargetPatcher.EncodeBranch(
                SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset,
                stubAOffset),
            ReadInstruction(patchedText.AsSpan(), SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset));
        Assert.Equal(
            SwShDynamaxAdventuresBossTargetPatcher.EncodeBranch(
                SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset,
                stubBOffset),
            ReadInstruction(patchedText.AsSpan(), SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset));

        AssertStub(
            patchedText.AsSpan(),
            stubAOffset,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteASourceRegister);
        AssertStub(
            patchedText.AsSpan(),
            stubBOffset,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBSourceRegister);
    }

    [Fact]
    public void ApplyConditionalTargetSpeciesRemapWritesShieldOwnedBranchesAndStubs()
    {
        var archive = CreateBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain(
            SwShDynamaxAdventuresBossTargetPatcher.ShieldCallSiteOffsetDelta,
            SwShDynamaxAdventuresMainPatcher.ShieldBuildId);

        var patchedMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);

        var baseNso = NsoFile.Parse(baseMain);
        var patchedNso = NsoFile.Parse(patchedMain);
        var baseText = baseNso.Text.DecompressedData;
        var patchedText = patchedNso.Text.DecompressedData;
        var callSiteAOffset = SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset
            + SwShDynamaxAdventuresBossTargetPatcher.ShieldCallSiteOffsetDelta;
        var callSiteBOffset = SwShDynamaxAdventuresBossTargetPatcher.CallSiteBOffset
            + SwShDynamaxAdventuresBossTargetPatcher.ShieldCallSiteOffsetDelta;
        var stubAOffset = baseText.Length;
        var stubBOffset = stubAOffset + SwShDynamaxAdventuresBossTargetPatcher.StubSize;

        Assert.Equal(
            SwShDynamaxAdventuresBossTargetPatcher.EncodeBranch(callSiteAOffset, stubAOffset),
            ReadInstruction(patchedText.AsSpan(), callSiteAOffset));
        Assert.Equal(
            SwShDynamaxAdventuresBossTargetPatcher.EncodeBranch(callSiteBOffset, stubBOffset),
            ReadInstruction(patchedText.AsSpan(), callSiteBOffset));
        AssertStub(
            patchedText.AsSpan(),
            stubAOffset,
            callSiteAOffset,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteASourceRegister);
        AssertStub(
            patchedText.AsSpan(),
            stubBOffset,
            callSiteBOffset,
            SwShDynamaxAdventuresBossTargetPatcher.CallSiteBSourceRegister);

        Assert.True(SwShDynamaxAdventuresBossTargetPatcher.TryReadConditionalTargetSpeciesRemap(
            patchedMain,
            out var remap));
        Assert.Equal(144, remap.FromSpecies);
        Assert.Equal(150, remap.ToSpecies);

        var restoredText = SwShDynamaxAdventuresBossTargetPatcher.RestoreTextFromBase(
            patchedText.ToArray(),
            baseText.AsSpan(),
            SwShDynamaxAdventuresBossTargetPatcher.ShieldCallSiteOffsetDelta);
        Assert.Equal(baseText.ToArray(), restoredText);
    }

    [Fact]
    public void TryReadConditionalTargetSpeciesRemapReturnsOwnedRemap()
    {
        var archive = CreateBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();
        var patchedMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);

        var hasRemap = SwShDynamaxAdventuresBossTargetPatcher.TryReadConditionalTargetSpeciesRemap(
            patchedMain,
            out var remap);

        Assert.True(hasRemap);
        Assert.Equal(144, remap.FromSpecies);
        Assert.Equal(150, remap.ToSpecies);
    }

    [Fact]
    public void TryReadConditionalTargetSpeciesRemapReturnsFalseForVanillaMain()
    {
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();

        var hasRemap = SwShDynamaxAdventuresBossTargetPatcher.TryReadConditionalTargetSpeciesRemap(
            baseMain,
            out _);

        Assert.False(hasRemap);
    }

    [Fact]
    public void ApplyConditionalTargetSpeciesRemapRejectsMissingBossSpecies()
    {
        var archive = CreateBossArchive();
        var main = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                main,
                archive,
                fromSpecies: 484,
                toSpecies: 150));

        Assert.Contains("must appear exactly once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyConditionalTargetSpeciesRemapRejectsDuplicateBossSpecies()
    {
        var entries = CreateBossArchive().Entries
            .Append(CreateRecord(230, species: 144, isBoss: true))
            .ToArray();
        var archive = new SwShDynamaxAdventureArchive(entries);
        var main = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                main,
                archive,
                fromSpecies: 144,
                toSpecies: 150));

        Assert.Contains("must appear exactly once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyConditionalTargetSpeciesRemapRejectsVersionMismatchedBossSpecies()
    {
        var archive = CreateBossArchive(replacementVersion: 2);
        var main = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                main,
                archive,
                fromSpecies: 144,
                toSpecies: 150));

        Assert.Contains("same version/story bucket", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyConditionalTargetSpeciesRemapRejectsStoryMismatchedBossSpecies()
    {
        var archive = CreateBossArchive(replacementStoryGated: true);
        var main = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                main,
                archive,
                fromSpecies: 144,
                toSpecies: 150));

        Assert.Contains("same version/story bucket", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyConditionalTargetSpeciesRemapRejectsUnexpectedCallSite()
    {
        var archive = CreateBossArchive();
        var main = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();
        var nso = NsoFile.Parse(main);
        var text = nso.Text.DecompressedData.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset, sizeof(uint)),
            0xD503201Fu);
        main = nso.Write(textDecompressedData: text);

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                main,
                archive,
                fromSpecies: 144,
                toSpecies: 150));

        Assert.Contains("expected vanilla boss target species call A", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyConditionalTargetSpeciesRemapRejectsInsufficientMappedTextCapacity()
    {
        var archive = CreateBossArchive();
        var main = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();
        var nso = NsoFile.Parse(main);
        BinaryPrimitives.WriteInt32LittleEndian(
            main.AsSpan(0x24, sizeof(int)),
            nso.Text.Header.MemoryOffset + nso.Text.Header.DecompressedSize);

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
                main,
                archive,
                fromSpecies: 144,
                toSpecies: 150));

        Assert.Contains("does not reserve enough executable space", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreTextFromBaseRemovesOwnedBranchesAndStubs()
    {
        var archive = CreateBossArchive();
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();
        var patchedMain = SwShDynamaxAdventuresBossTargetPatcher.ApplyConditionalTargetSpeciesRemap(
            baseMain,
            archive,
            fromSpecies: 144,
            toSpecies: 150);
        var baseText = NsoFile.Parse(baseMain).Text.DecompressedData;
        var patchedText = NsoFile.Parse(patchedMain).Text.DecompressedData.ToArray();

        var restoredText = SwShDynamaxAdventuresBossTargetPatcher.RestoreTextFromBase(
            patchedText,
            baseText.AsSpan());

        Assert.Equal(baseText.ToArray(), restoredText);
    }

    [Fact]
    public void RestoreTextFromBaseRejectsNonOwnedBranchAtCallSite()
    {
        var baseMain = SwShDynamaxAdventureTestFixtures.CreateBossTargetCompatibleMain();
        var baseText = NsoFile.Parse(baseMain).Text.DecompressedData;
        var currentText = baseText.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            currentText.AsSpan(SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset, sizeof(uint)),
            SwShDynamaxAdventuresBossTargetPatcher.EncodeBranch(
                SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset,
                SwShDynamaxAdventuresBossTargetPatcher.CallSiteAOffset + 0x20));

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShDynamaxAdventuresBossTargetPatcher.RestoreTextFromBase(
                currentText,
                baseText.AsSpan()));

        Assert.Contains("owned boss target species call A", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertStub(ReadOnlySpan<byte> text, int stubOffset, int callSiteOffset, int sourceRegister)
    {
        var expected = SwShDynamaxAdventuresBossTargetPatcher.CreateStub(
            stubOffset,
            callSiteOffset,
            sourceRegister,
            fromSpecies: 144,
            toSpecies: 150);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], ReadInstruction(text, stubOffset + (index * sizeof(uint))));
        }
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]);
    }

    private static SwShDynamaxAdventureArchive CreateBossArchive(
        int replacementVersion = 0,
        bool replacementStoryGated = false)
    {
        var entries = Enumerable.Range(0, 226)
            .Select(index => CreateRecord(index, species: index + 1, isBoss: false))
            .Concat(
            [
                CreateRecord(226, species: 145, isBoss: true),
                CreateRecord(227, species: 146, isBoss: true),
                CreateRecord(228, species: 144, isBoss: true),
                CreateRecord(
                    229,
                    species: 150,
                    isBoss: true,
                    version: replacementVersion,
                    isStoryProgressGated: replacementStoryGated),
            ])
            .ToArray();

        return new SwShDynamaxAdventureArchive(entries);
    }

    private static SwShDynamaxAdventureRecord CreateRecord(
        int entryIndex,
        int species,
        bool isBoss,
        int version = 0,
        bool isStoryProgressGated = false)
    {
        return new SwShDynamaxAdventureRecord(
            entryIndex,
            IsSingleCapture: isBoss,
            SingleCaptureFlagBlock: (ulong)entryIndex,
            Field02: 0,
            Form: 0,
            GigantamaxState: 1,
            BallItemId: 4,
            AdventureIndex: entryIndex,
            Level: isBoss ? 70 : 65,
            Species: species,
            UiMessageId: 0,
            OtGender: 1,
            Version: version,
            ShinyRoll: 1,
            new SwShDynamaxAdventureIvs(-5, -1, -1, -1, -1, -1),
            Ability: 0,
            IsStoryProgressGated: isStoryProgressGated,
            Moves: [1, 2, 3, 4]);
    }
}
