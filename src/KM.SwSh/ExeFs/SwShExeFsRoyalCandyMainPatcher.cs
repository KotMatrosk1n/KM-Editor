// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;

namespace KM.SwSh.ExeFs;

internal static class SwShExeFsRoyalCandyMainPatcher
{
    private const int RareCandyItemId = 50;
    private const int RoyalCandyItemId = 1128;
    private const int RareCandyUiHookCodeCaveSearchStart = 0x007BC338;

    public static byte[] ApplyBasePatch(byte[] mainBytes)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var nso = SwShNsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.ToArray();

        PatchExpCandyFixedAmountBypass(text);
        PatchInfiniteRoyalCandyUse(text);
        PatchRoyalCandyUiRoute(text);

        return nso.Write(textDecompressedData: text);
    }

    private static void PatchRoyalCandyUiRoute(byte[] text)
    {
        foreach (var check in UiRouteChecks)
        {
            var caveOffset = FindCodeCave(text, 0x0C, $"Royal Candy UI route check at text+0x{check.CompareOffset:X}");
            ExpectInstruction(
                text,
                check.CompareOffset,
                EncodeCmpImmediate(check.ItemRegister, RareCandyItemId),
                $"Rare Candy UI compare at text+0x{check.CompareOffset:X}");

            var branchOffset = check.CompareOffset + 4;
            ExpectInstruction(
                text,
                branchOffset,
                EncodeConditionalBranch(branchOffset, check.FailOffset, Arm64Condition.NE),
                $"Rare Candy UI branch at text+0x{branchOffset:X}");

            WriteInstruction(text, branchOffset, EncodeConditionalBranch(branchOffset, caveOffset, Arm64Condition.NE));
            WriteInstruction(text, caveOffset, EncodeCmpImmediate(check.ItemRegister, RoyalCandyItemId));
            WriteInstruction(text, caveOffset + 4, EncodeConditionalBranch(caveOffset + 4, check.PassOffset, Arm64Condition.EQ));
            WriteInstruction(text, caveOffset + 8, EncodeBranch(caveOffset + 8, check.FailOffset));
        }

        foreach (var check in EqualBranchChecks)
        {
            ExpectInstruction(
                text,
                check.CompareOffset,
                EncodeCmpImmediate(check.ItemRegister, RareCandyItemId),
                $"Rare Candy equal-branch compare at text+0x{check.CompareOffset:X}");

            var branchOffset = check.CompareOffset + 4;
            ExpectInstruction(
                text,
                branchOffset,
                EncodeConditionalBranch(branchOffset, check.TargetOffset, Arm64Condition.EQ),
                $"Rare Candy equal branch at text+0x{branchOffset:X}");

            var firstCaveOffset = FindCodeCave(text, 0x0C, $"Royal Candy equal-branch first stub at text+0x{check.CompareOffset:X}");
            var secondCaveOffset = FindZeroRun(text, 0x0C, firstCaveOffset + 0x0C);
            if (secondCaveOffset < 0)
            {
                secondCaveOffset = FindZeroRun(text, 0x0C, 0);
            }

            if (secondCaveOffset < 0 || secondCaveOffset == firstCaveOffset)
            {
                throw NoCodeCave(text, 0x0C, $"Royal Candy equal-branch second stub at text+0x{check.CompareOffset:X}");
            }

            WriteInstruction(text, check.CompareOffset, EncodeBranch(check.CompareOffset, firstCaveOffset));
            WriteInstruction(text, branchOffset, EncodeNop());

            WriteInstruction(text, firstCaveOffset, EncodeCmpImmediate(check.ItemRegister, RareCandyItemId));
            WriteInstruction(text, firstCaveOffset + 4, EncodeConditionalBranch(firstCaveOffset + 4, check.TargetOffset, Arm64Condition.EQ));
            WriteInstruction(text, firstCaveOffset + 8, EncodeBranch(firstCaveOffset + 8, secondCaveOffset));

            WriteInstruction(text, secondCaveOffset, EncodeCmpImmediate(check.ItemRegister, RoyalCandyItemId));
            WriteInstruction(text, secondCaveOffset + 4, EncodeConditionalBranch(secondCaveOffset + 4, check.TargetOffset, Arm64Condition.EQ));
            WriteInstruction(text, secondCaveOffset + 8, EncodeBranch(secondCaveOffset + 8, check.FallthroughOffset));
        }
    }

    private static void PatchExpCandyFixedAmountBypass(byte[] text)
    {
        foreach (var offset in new[] { 0x007BC1BC, 0x007BC1C4 })
        {
            ExpectInstruction(
                text,
                offset,
                EncodeCmpImmediate(register: 9, immediate: 4),
                $"Exp Candy fixed amount compare at text+0x{offset:X}");
            WriteInstruction(text, offset, EncodeCmpImmediate(register: 9, immediate: 3));
        }
    }

    private static void PatchInfiniteRoyalCandyUse(byte[] text)
    {
        const int quantityMoveOffset = 0x007B1F20;
        const int resumeOffset = quantityMoveOffset + 4;
        const int itemRegister = 22;
        const uint expectedQuantityMove = 0x2A0003E2; // MOV w2, w0

        ExpectInstruction(text, quantityMoveOffset, expectedQuantityMove, "consume quantity move");

        var caveOffset = FindCodeCave(text, 0x0C, "Royal Candy infinite-use stub");
        WriteInstruction(text, quantityMoveOffset, EncodeBranch(quantityMoveOffset, caveOffset));
        WriteInstruction(text, caveOffset, EncodeCmpImmediate(itemRegister, RoyalCandyItemId));
        WriteInstruction(text, caveOffset + 4, EncodeConditionalSelect32(2, 31, 0, Arm64Condition.EQ));
        WriteInstruction(text, caveOffset + 8, EncodeBranch(caveOffset + 8, resumeOffset));
    }

    private static int FindCodeCave(byte[] text, int requiredBytes, string label)
    {
        var caveOffset = FindZeroRun(text, requiredBytes, RareCandyUiHookCodeCaveSearchStart);
        if (caveOffset >= 0)
        {
            return caveOffset;
        }

        caveOffset = FindZeroRun(text, requiredBytes, 0);
        if (caveOffset >= 0)
        {
            return caveOffset;
        }

        throw NoCodeCave(text, requiredBytes, label);
    }

    private static InvalidDataException NoCodeCave(byte[] text, int requiredBytes, string label)
    {
        var largest = FindLargestZeroRun(text);
        return new InvalidDataException(
            $"Could not find a {requiredBytes}-byte zero-filled code cave for {label}. Largest run: text+0x{largest.Offset:X} length 0x{largest.Length:X}.");
    }

    private static int FindZeroRun(byte[] data, int requiredBytes, int startOffset)
    {
        var runStart = -1;
        for (var offset = Math.Max(0, startOffset); offset < data.Length; offset++)
        {
            if (data[offset] == 0)
            {
                if (runStart < 0)
                {
                    runStart = offset;
                }

                var alignedStart = (runStart + 3) & ~3;
                if (offset - alignedStart + 1 >= requiredBytes)
                {
                    return alignedStart;
                }

                continue;
            }

            runStart = -1;
        }

        return -1;
    }

    private static ZeroRun FindLargestZeroRun(byte[] data)
    {
        var best = new ZeroRun(-1, 0);
        var runStart = -1;
        for (var offset = 0; offset < data.Length; offset++)
        {
            if (data[offset] == 0)
            {
                if (runStart < 0)
                {
                    runStart = offset;
                }

                var length = offset - runStart + 1;
                if (length > best.Length)
                {
                    best = new ZeroRun(runStart, length);
                }

                continue;
            }

            runStart = -1;
        }

        return best;
    }

    private static void ExpectInstruction(byte[] text, int offset, uint expectedInstruction, string label)
    {
        if (offset < 0 || offset + 4 > text.Length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed .text segment.");
        }

        var actualInstruction = BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset, 4));
        if (actualInstruction != expectedInstruction)
        {
            throw new InvalidDataException(
                $"Unexpected {label}: 0x{actualInstruction:X8}; expected 0x{expectedInstruction:X8}.");
        }
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        if (offset < 0 || offset + 4 > text.Length)
        {
            throw new InvalidDataException($"Patch instruction target text+0x{offset:X} is outside the decompressed .text segment.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, 4), instruction);
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static uint EncodeConditionalBranch(int sourceOffset, int targetOffset, Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("Conditional branch target must be 4-byte aligned.");
        }

        var imm19 = delta >> 2;
        if (imm19 < -(1 << 18) || imm19 >= (1 << 18))
        {
            throw new InvalidDataException("Conditional branch target is outside ARM64 range.");
        }

        return (uint)(0x54000000 | ((imm19 & 0x7FFFF) << 5) | ((int)condition & 0xF));
    }

    private static uint EncodeBranch(int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("Branch target must be 4-byte aligned.");
        }

        var imm26 = delta >> 2;
        if (imm26 < -(1 << 25) || imm26 >= (1 << 25))
        {
            throw new InvalidDataException("Branch target is outside ARM64 range.");
        }

        return (uint)(0x14000000 | (imm26 & 0x03FFFFFF));
    }

    private static uint EncodeConditionalSelect32(
        int destinationRegister,
        int trueRegister,
        int falseRegister,
        Arm64Condition condition)
    {
        return (uint)(0x1A800000
            | (((int)condition & 0xF) << 12)
            | ((falseRegister & 0x1F) << 16)
            | ((trueRegister & 0x1F) << 5)
            | (destinationRegister & 0x1F));
    }

    private static uint EncodeNop()
    {
        return 0xD503201F;
    }

    private static readonly RareCandyUiCheck[] UiRouteChecks =
    [
        new(0x00747988, 28, 0x00747990, 0x00747A80),
        new(0x00747D44, 9, 0x00747D4C, 0x007477E8),
        new(0x0074BA24, 26, 0x0074BA2C, 0x0074BAD4),
        new(0x0074BDA8, 9, 0x0074BDB0, 0x0074B788),
        new(0x0074DFE4, 9, 0x0074DFEC, 0x0074DE78),
        new(0x0074DFF8, 28, 0x0074E000, 0x0074E16C),
        new(0x0075CEFC, 9, 0x0075CF04, 0x0075CC18),
        new(0x007BB204, 20, 0x007BB20C, 0x007BB26C),
        new(0x007BB3C0, 19, 0x007BB3C8, 0x007BB3EC),
        new(0x007BC1F8, 8, 0x007BC200, 0x007BC2B4),
    ];

    private static readonly RareCandyEqualBranchCheck[] EqualBranchChecks =
    [
        new(0x00747DE0, 9, 0x00747D4C, 0x00747DE8),
        new(0x0074BE44, 9, 0x0074BDB0, 0x0074BE4C),
        new(0x0075CCE8, 27, 0x0075D064, 0x0075CCF0),
        new(0x0075D08C, 10, 0x0075D05C, 0x0075D094),
        new(0x007BBFD4, 23, 0x007BC054, 0x007BBFDC),
    ];

    private enum Arm64Condition
    {
        EQ = 0,
        NE = 1,
    }

    private sealed record RareCandyUiCheck(
        int CompareOffset,
        int ItemRegister,
        int PassOffset,
        int FailOffset);

    private sealed record RareCandyEqualBranchCheck(
        int CompareOffset,
        int ItemRegister,
        int TargetOffset,
        int FallthroughOffset);

    private sealed record ZeroRun(int Offset, int Length);
}
