// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.DynamaxAdventures;

internal static class SwShDynamaxAdventuresBossTargetPatcher
{
    internal const int TextEndPaddedOffset = 0x01901000;
    internal const int CallSiteAOffset = 0x015D615C;
    internal const int CallSiteBOffset = 0x015D68AC;
    internal const uint CallSiteAVanillaInstruction = 0x2A1503E1; // mov w1, w21
    internal const uint CallSiteBVanillaInstruction = 0x2A1403E1; // mov w1, w20
    internal const int CallSiteASourceRegister = 21;
    internal const int CallSiteBSourceRegister = 20;
    internal const int StubSize = 0x18;

    public static bool TryReadConditionalTargetSpeciesRemap(
        byte[] mainBytes,
        out BossTargetSpeciesRemap remap)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var text = SwShNsoFile.Parse(mainBytes).Text.DecompressedData;
        var hasA = TryReadOwnedCallSite(
            text,
            CallSiteAOffset,
            CallSiteASourceRegister,
            "boss target species call A",
            out var remapA);
        var hasB = TryReadOwnedCallSite(
            text,
            CallSiteBOffset,
            CallSiteBSourceRegister,
            "boss target species call B",
            out var remapB);
        if (!hasA && !hasB)
        {
            remap = default;
            return false;
        }

        if (!hasA || !hasB || remapA != remapB)
        {
            throw new InvalidDataException("Dynamax Adventures boss target remap requires matching owned stubs at both final-boss target species call sites.");
        }

        remap = remapA;
        return true;
    }

    public static byte[] ApplyConditionalTargetSpeciesRemap(
        byte[] mainBytes,
        SwShDynamaxAdventureArchive archive,
        int fromSpecies,
        int toSpecies)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ArgumentNullException.ThrowIfNull(archive);

        var sourceBoss = ValidateBossSpecies(archive, fromSpecies, "source");
        var replacementBoss = ValidateBossSpecies(archive, toSpecies, "replacement");
        ValidateBossRemapBucket(sourceBoss, replacementBoss);

        var nso = SwShNsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.ToArray();
        var stubAOffset = text.Length;
        var stubBOffset = stubAOffset + StubSize;
        var newTextLength = stubBOffset + StubSize;
        if (newTextLength > TextEndPaddedOffset)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventures boss target remap stubs need text length 0x{newTextLength:X}, beyond padded text end 0x{TextEndPaddedOffset:X}."));
        }

        Array.Resize(ref text, newTextLength);
        WriteBranchToStub(text, CallSiteAOffset, stubAOffset, CallSiteAVanillaInstruction, "boss target species call A");
        WriteBranchToStub(text, CallSiteBOffset, stubBOffset, CallSiteBVanillaInstruction, "boss target species call B");
        WriteStub(text, stubAOffset, CallSiteAOffset, CallSiteASourceRegister, fromSpecies, toSpecies);
        WriteStub(text, stubBOffset, CallSiteBOffset, CallSiteBSourceRegister, fromSpecies, toSpecies);

        return nso.Write(textDecompressedData: text);
    }

    internal static byte[] RestoreTextFromBase(byte[] currentText, ReadOnlySpan<byte> baseText)
    {
        ArgumentNullException.ThrowIfNull(currentText);

        if (currentText.Length < baseText.Length)
        {
            throw new InvalidDataException("Dynamax Adventures boss target restore requires current main.text to be at least as large as base main.text.");
        }

        var text = currentText.ToArray();
        var ownedStubRanges = new List<(int Start, int End)>();
        RestoreOwnedCallSiteIfNeeded(
            text,
            baseText,
            CallSiteAOffset,
            CallSiteASourceRegister,
            "boss target species call A",
            ownedStubRanges);
        RestoreOwnedCallSiteIfNeeded(
            text,
            baseText,
            CallSiteBOffset,
            CallSiteBSourceRegister,
            "boss target species call B",
            ownedStubRanges);

        var trimLength = FindOwnedStubTrimLength(text.Length, ownedStubRanges);
        if (trimLength < text.Length)
        {
            Array.Resize(ref text, trimLength);
        }

        return text;
    }

    private static SwShDynamaxAdventureRecord ValidateBossSpecies(SwShDynamaxAdventureArchive archive, int species, string label)
    {
        if (species < 0 || species > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(species), species, "Dynamax Adventures boss target remap supports 16-bit species IDs.");
        }

        var matches = archive.Entries.Where(row =>
            SwShDynamaxAdventureSafetyRules.IsBossEntryIndex(row.EntryIndex)
            && row.Species == species)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventures boss target {label} species {species} must appear exactly once in the boss table, found {matches.Length}."));
        }

        return matches[0];
    }

    private static void ValidateBossRemapBucket(
        SwShDynamaxAdventureRecord sourceBoss,
        SwShDynamaxAdventureRecord replacementBoss)
    {
        if (sourceBoss.Version != replacementBoss.Version
            || sourceBoss.IsStoryProgressGated != replacementBoss.IsStoryProgressGated)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventures boss target remap requires source and replacement bosses in the same version/story bucket until cross-bucket final boss swaps are live-tested. Source row {sourceBoss.EntryIndex} uses version {sourceBoss.Version}, story {sourceBoss.IsStoryProgressGated}; replacement row {replacementBoss.EntryIndex} uses version {replacementBoss.Version}, story {replacementBoss.IsStoryProgressGated}."));
        }
    }

    private static void WriteBranchToStub(
        byte[] text,
        int callSiteOffset,
        int stubOffset,
        uint vanillaInstruction,
        string label)
    {
        var actual = ReadInstruction(text, callSiteOffset, label);
        if (actual != vanillaInstruction)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventures expected vanilla {label} instruction at main.text+0x{callSiteOffset:X}, but found 0x{actual:X8}."));
        }

        WriteInstruction(text, callSiteOffset, EncodeBranch(callSiteOffset, stubOffset), label);
    }

    private static void WriteStub(
        byte[] text,
        int stubOffset,
        int callSiteOffset,
        int sourceRegister,
        int fromSpecies,
        int toSpecies)
    {
        var instructions = CreateStub(stubOffset, callSiteOffset, sourceRegister, fromSpecies, toSpecies);
        for (var index = 0; index < instructions.Length; index++)
        {
            WriteInstruction(text, stubOffset + (index * sizeof(uint)), instructions[index], $"boss target remap stub {index}");
        }
    }

    internal static uint[] CreateStub(
        int stubOffset,
        int callSiteOffset,
        int sourceRegister,
        int fromSpecies,
        int toSpecies)
    {
        return
        [
            EncodeCmpImmediate(sourceRegister, fromSpecies),
            EncodeConditionalBranch(stubOffset + 0x04, stubOffset + 0x10, Arm64Condition.Ne),
            EncodeMovImmediate(1, toSpecies),
            EncodeBranch(stubOffset + 0x0C, callSiteOffset + sizeof(uint)),
            EncodeMovRegister(1, sourceRegister),
            EncodeBranch(stubOffset + 0x14, callSiteOffset + sizeof(uint)),
        ];
    }

    private static void RestoreOwnedCallSiteIfNeeded(
        byte[] text,
        ReadOnlySpan<byte> baseText,
        int callSiteOffset,
        int sourceRegister,
        string label,
        ICollection<(int Start, int End)> ownedStubRanges)
    {
        if (callSiteOffset < 0
            || callSiteOffset + sizeof(uint) > text.Length
            || callSiteOffset + sizeof(uint) > baseText.Length)
        {
            return;
        }

        var baseInstruction = ReadInstruction(baseText, callSiteOffset, $"Base {label}");
        var actual = ReadInstruction(text, callSiteOffset, label);
        if (actual == baseInstruction)
        {
            return;
        }

        var stubOffset = DecodeUnconditionalBranchTarget(callSiteOffset, actual, label);
        _ = ValidateOwnedStub(text, stubOffset, callSiteOffset, sourceRegister, label);
        WriteInstruction(text, callSiteOffset, baseInstruction, label);
        ownedStubRanges.Add((stubOffset, stubOffset + StubSize));
    }

    private static bool TryReadOwnedCallSite(
        ReadOnlySpan<byte> text,
        int callSiteOffset,
        int sourceRegister,
        string label,
        out BossTargetSpeciesRemap remap)
    {
        remap = default;
        if (callSiteOffset < 0 || callSiteOffset + sizeof(uint) > text.Length)
        {
            return false;
        }

        var actual = ReadInstruction(text, callSiteOffset, label);
        if ((actual & 0xFC000000u) != 0x14000000u)
        {
            return false;
        }

        var stubOffset = DecodeUnconditionalBranchTarget(callSiteOffset, actual, label);
        remap = ValidateOwnedStub(text, stubOffset, callSiteOffset, sourceRegister, label);
        return true;
    }

    private static int FindOwnedStubTrimLength(
        int textLength,
        IReadOnlyCollection<(int Start, int End)> ownedStubRanges)
    {
        if (ownedStubRanges.Count == 0)
        {
            return textLength;
        }

        var orderedRanges = ownedStubRanges
            .OrderBy(range => range.Start)
            .ToArray();
        var cursor = orderedRanges[0].Start;
        foreach (var range in orderedRanges)
        {
            if (range.Start != cursor)
            {
                return textLength;
            }

            cursor = range.End;
        }

        return cursor == textLength
            ? orderedRanges[0].Start
            : textLength;
    }

    private static int DecodeUnconditionalBranchTarget(int sourceOffset, uint instruction, string label)
    {
        if ((instruction & 0xFC000000u) != 0x14000000u)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventures restore expected owned {label} branch at main.text+0x{sourceOffset:X}, but found 0x{instruction:X8}."));
        }

        var imm26 = (int)(instruction & 0x03FFFFFFu);
        if ((imm26 & 0x02000000) != 0)
        {
            imm26 |= unchecked((int)0xFC000000);
        }

        return sourceOffset + (imm26 * sizeof(uint));
    }

    private static BossTargetSpeciesRemap ValidateOwnedStub(
        ReadOnlySpan<byte> text,
        int stubOffset,
        int callSiteOffset,
        int sourceRegister,
        string label)
    {
        EnsureRange(text, stubOffset, StubSize, $"{label} owned stub");

        var cmp = ReadInstruction(text, stubOffset, $"{label} stub compare");
        if ((cmp & 0xFFC0001Fu) != 0x7100001Fu
            || (int)((cmp >> 5) & 0x1Fu) != sourceRegister)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventures restore did not find an owned {label} compare stub at main.text+0x{stubOffset:X}."));
        }

        var fromSpecies = (int)((cmp >> 10) & 0xFFFu);
        var conditionalBranch = ReadInstruction(text, stubOffset + 0x04, $"{label} stub condition");
        if (conditionalBranch != EncodeConditionalBranch(stubOffset + 0x04, stubOffset + 0x10, Arm64Condition.Ne))
        {
            throw new InvalidDataException($"Dynamax Adventures restore did not find an owned {label} condition branch.");
        }

        var movReplacement = ReadInstruction(text, stubOffset + 0x08, $"{label} stub replacement");
        if ((movReplacement & 0xFFE0001Fu) != 0x52800001u)
        {
            throw new InvalidDataException($"Dynamax Adventures restore did not find an owned {label} replacement move.");
        }

        var toSpecies = (int)((movReplacement >> 5) & 0xFFFFu);
        var expected = CreateStub(stubOffset, callSiteOffset, sourceRegister, fromSpecies, toSpecies);
        for (var index = 0; index < expected.Length; index++)
        {
            var actual = ReadInstruction(text, stubOffset + (index * sizeof(uint)), $"{label} stub {index}");
            if (actual != expected[index])
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Dynamax Adventures restore found a non-owned {label} stub instruction at main.text+0x{stubOffset + (index * sizeof(uint)):X}."));
            }
        }

        return new BossTargetSpeciesRemap(fromSpecies, toSpecies);
    }

    internal static uint EncodeBranch(int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 0x3) != 0)
        {
            throw new InvalidDataException("Branch target must be 4-byte aligned.");
        }

        var imm26 = delta / 4;
        if (imm26 is < -0x2000000 or > 0x1FFFFFF)
        {
            throw new InvalidDataException("Branch target is outside ARM64 range.");
        }

        return 0x14000000u | ((uint)imm26 & 0x03FFFFFFu);
    }

    internal static uint EncodeConditionalBranch(int sourceOffset, int targetOffset, Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 0x3) != 0)
        {
            throw new InvalidDataException("Conditional branch target must be 4-byte aligned.");
        }

        var imm19 = delta / 4;
        if (imm19 is < -0x40000 or > 0x3FFFF)
        {
            throw new InvalidDataException("Conditional branch target is outside ARM64 range.");
        }

        return 0x54000000u | (((uint)imm19 & 0x7FFFFu) << 5) | (uint)condition;
    }

    internal static uint EncodeMovImmediate(int destinationRegister, int value)
    {
        ValidateRegister(destinationRegister);
        if ((uint)value > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "MOV immediate supports only 16-bit values.");
        }

        return 0x52800000u | ((uint)value << 5) | (uint)destinationRegister;
    }

    internal static uint EncodeMovRegister(int destinationRegister, int sourceRegister)
    {
        ValidateRegister(destinationRegister);
        ValidateRegister(sourceRegister);
        return 0x2A0003E0u | ((uint)sourceRegister << 16) | (uint)destinationRegister;
    }

    internal static uint EncodeCmpImmediate(int register, int value)
    {
        ValidateRegister(register);
        if ((uint)value > 0xFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "CMP immediate supports only 12-bit values.");
        }

        return 0x7100001Fu | ((uint)value << 10) | ((uint)register << 5);
    }

    internal static uint ReadInstruction(ReadOnlySpan<byte> text, int offset, string label)
    {
        EnsureRange(text, offset, sizeof(uint), $"{label} instruction");
        return BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction, string label)
    {
        EnsureRange(text, offset, sizeof(uint), $"{label} instruction");
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static void ValidateRegister(int register)
    {
        if (register is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(register), register, "ARM64 register index must be 0..31.");
        }
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed NSO segment.");
        }
    }

    internal enum Arm64Condition : uint
    {
        Eq = 0,
        Ne = 1,
    }

    public readonly record struct BossTargetSpeciesRemap(int FromSpecies, int ToSpecies);
}
