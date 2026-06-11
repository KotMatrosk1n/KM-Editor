// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.CatchCap;

internal enum SwShCatchCapInstallKind
{
    NotInstalled,
    InstalledV1,
    ForeignPatch,
    Conflict,
}

internal sealed record SwShCatchCapAnalysis(
    SwShCatchCapInstallKind Kind,
    string Message,
    IReadOnlyList<byte> Caps,
    string LogicExpression,
    string CapLogicSha256);

internal static class SwShCatchCapMainPatcher
{
    public const int CapCount = 9;
    public const int MinimumCap = 1;
    public const int MaximumCap = 100;
    public const int FinalBadgeCount = 8;
    public const int FinalBadgeCap = 100;
    public const int ExeFsHookSiteOffset = 0x013AE3AC;
    public const int ExeFsTableOffset = 0x013AE3B0;
    public const int ExeFsReturnOffset = 0x013AE3C8;

    private const int CaveClampOffset = 0x013AE0B4;
    private const int CaveLoadAddressOffset = 0x013AEBE4;
    private const int CaveLoadValueOffset = 0x013AD734;
    private const int CaveOverflowOffset = 0x013AF464;
    private const uint VanillaTailRestoreInstruction = 0xA9417BFD; // ldp x29,x30,[sp,#0x10]
    private const uint VanillaFinalRestoreInstruction = 0xA8C24FF4; // ldp x20,x19,[sp],#0x20
    private const uint VanillaRetInstruction = 0xD65F03C0;

    private static readonly byte[] DefaultCaps = [0x14, 0x19, 0x1E, 0x23, 0x28, 0x2D, 0x32, 0x37, 0x64];
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("CCHv1");

    public static SwShCatchCapAnalysis Analyze(byte[] mainBytes)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = SwShNsoFile.Parse(mainBytes);
            var text = nso.Text.DecompressedData;
            EnsureTextRange(text, ExeFsHookSiteOffset, 0x20, "Catch Cap Hook formula tail");

            if (HasMarker(text))
            {
                var rawCaps = text.AsSpan(ExeFsTableOffset, CapCount).ToArray();
                var caps = NormalizeCaps(rawCaps);
                var message = rawCaps[FinalBadgeCount] == FinalBadgeCap
                    ? "Catch Cap Editor hook is installed. Changing values edits badge counts 0-7; eight badges is fixed at Lv.100 by the game."
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Catch Cap Editor hook is installed. Badge counts 0-7 are editable; the installed table has stale Lv.{rawCaps[FinalBadgeCount]} metadata for eight badges, so stage and apply to rewrite it to Lv.100.");
                return new SwShCatchCapAnalysis(
                    SwShCatchCapInstallKind.InstalledV1,
                    message,
                    caps,
                    FormatLogicExpression(caps),
                    ComputeCapLogicSha256(caps));
            }

            var hookInstruction = ReadInstruction(text, ExeFsHookSiteOffset);
            if (IsUnconditionalBranch(hookInstruction))
            {
                return new SwShCatchCapAnalysis(
                    SwShCatchCapInstallKind.ForeignPatch,
                    "Catch-cap formula tail is already branched, but the KM Catch Cap Hook marker is not present.",
                    DefaultCaps,
                    FormatLogicExpression(DefaultCaps),
                    ComputeCapLogicSha256(DefaultCaps));
            }

            if (ReadInstruction(text, ExeFsTableOffset) != VanillaTailRestoreInstruction)
            {
                return new SwShCatchCapAnalysis(
                    SwShCatchCapInstallKind.Conflict,
                    "Catch-cap formula tail does not look vanilla and does not contain a KM Catch Cap Hook marker.",
                    DefaultCaps,
                    FormatLogicExpression(DefaultCaps),
                    ComputeCapLogicSha256(DefaultCaps));
            }

            return new SwShCatchCapAnalysis(
                SwShCatchCapInstallKind.NotInstalled,
                "Catch Cap Editor hook is not installed. Staging values installs the hook and writes the selected cap table.",
                DefaultCaps,
                FormatLogicExpression(DefaultCaps),
                ComputeCapLogicSha256(DefaultCaps));
        }
        catch (InvalidDataException exception)
        {
            return new SwShCatchCapAnalysis(
                SwShCatchCapInstallKind.Conflict,
                exception.Message,
                DefaultCaps,
                FormatLogicExpression(DefaultCaps),
                ComputeCapLogicSha256(DefaultCaps));
        }
    }

    public static byte[] Apply(byte[] mainBytes, IReadOnlyList<int> caps)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ArgumentNullException.ThrowIfNull(caps);

        if (caps.Count != CapCount)
        {
            throw new InvalidDataException("Catch Cap Editor requires exactly nine cap values; badge count 8 must be level 100.");
        }

        var capBytes = new byte[caps.Count];
        for (var index = 0; index < caps.Count; index++)
        {
            var cap = caps[index];
            if (cap is < MinimumCap or > MaximumCap)
            {
                throw new InvalidDataException($"Catch cap {cap} must be between {MinimumCap} and {MaximumCap}.");
            }

            if (index == FinalBadgeCount && cap != FinalBadgeCap)
            {
                throw new InvalidDataException(
                    $"Catch cap for badge count {FinalBadgeCount} is fixed at level {FinalBadgeCap}; the game treats eight badges as catch any level.");
            }

            if (index > 0 && cap < caps[index - 1])
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Catch cap for badge count {index} must be level {caps[index - 1]} or higher."));
            }

            capBytes[index] = checked((byte)cap);
        }

        var nso = SwShNsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.ToArray();
        EnsureTextRange(text, ExeFsHookSiteOffset, 0x20, "Catch Cap Hook formula tail");
        EnsureCavesAvailableOrOwned(text);

        var analysis = Analyze(mainBytes);
        if (analysis.Kind is SwShCatchCapInstallKind.ForeignPatch or SwShCatchCapInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        if (analysis.Kind == SwShCatchCapInstallKind.NotInstalled)
        {
            InstallHook(text);
        }

        WriteTableAndMarker(text, capBytes);
        return nso.Write(textDecompressedData: text);
    }

    public static byte[] RestoreFromBase(byte[] currentMainBytes, byte[] baseMainBytes)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var currentNso = SwShNsoFile.Parse(currentMainBytes);
        var baseNso = SwShNsoFile.Parse(baseMainBytes);
        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("Catch Cap restore requires current and base main NSO files with matching .text sizes.");
        }

        foreach (var region in SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(
            SwShExeFsReservedRegionLedger.OwnerCatchCap))
        {
            var offset = region.StartOffset!.Value;
            var length = region.Length!.Value;
            EnsureTextRange(currentText, offset, length, region.Label);
            EnsureTextRange(baseText, offset, length, $"Base {region.Label}");
            baseText.AsSpan(offset, length).CopyTo(currentText.AsSpan(offset, length));
        }

        return currentNso.Write(textDecompressedData: currentText);
    }

    public static string FormatLogicExpression(IReadOnlyList<byte> caps)
    {
        if (caps.Count != CapCount)
        {
            return "Invalid cap table";
        }

        var step = caps[1] - caps[0];
        var isLinear = true;
        for (var index = 1; index < 8; index++)
        {
            if (caps[index] - caps[index - 1] != step)
            {
                isLinear = false;
                break;
            }
        }

        if (isLinear)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"badge_count < 8 ? {caps[0]} + badge_count * {step} : {FinalBadgeCap}");
        }

        return "badge_count < 8 ? cap_table[badge_count] : 100";
    }

    public static string ComputeCapLogicSha256(IReadOnlyList<byte> caps)
    {
        var hex = string.Join(' ', caps.Select(cap => cap.ToString("X2", CultureInfo.InvariantCulture)));
        var canonical = $"SWSH-CATCH-CAP-HOOK|v1|u8|9|{hex}";
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void InstallHook(byte[] text)
    {
        if (ReadInstruction(text, ExeFsTableOffset) != VanillaTailRestoreInstruction)
        {
            throw new InvalidDataException("Catch Cap Hook install requires the vanilla x29/x30 restore at main.text+0x013AE3B0.");
        }

        if (ReadInstruction(text, ExeFsReturnOffset) != VanillaFinalRestoreInstruction
            || ReadInstruction(text, ExeFsReturnOffset + 4) != VanillaRetInstruction)
        {
            throw new InvalidDataException("Catch Cap Hook install requires the vanilla final epilogue at main.text+0x013AE3C8.");
        }

        WriteInstruction(text, ExeFsHookSiteOffset, EncodeBranch(ExeFsHookSiteOffset, CaveClampOffset));

        WriteInstruction(text, CaveClampOffset, EncodeCmpImmediate(0, 8));
        WriteInstruction(text, CaveClampOffset + 4, EncodeConditionalBranch(CaveClampOffset + 4, CaveLoadAddressOffset, Arm64Condition.LS));
        WriteInstruction(text, CaveClampOffset + 8, EncodeBranch(CaveClampOffset + 8, CaveOverflowOffset));

        WriteInstruction(text, CaveOverflowOffset, EncodeMovzImmediate32(0, 8));
        WriteInstruction(text, CaveOverflowOffset + 4, EncodeBranch(CaveOverflowOffset + 4, CaveLoadAddressOffset));
        WriteInstruction(text, CaveOverflowOffset + 8, EncodeNop());

        WriteInstruction(text, CaveLoadAddressOffset, EncodeAdr(8, CaveLoadAddressOffset, ExeFsTableOffset));
        WriteInstruction(text, CaveLoadAddressOffset + 4, EncodeBranch(CaveLoadAddressOffset + 4, CaveLoadValueOffset));
        WriteInstruction(text, CaveLoadAddressOffset + 8, EncodeNop());

        WriteInstruction(text, CaveLoadValueOffset, EncodeLdrbRegisterOffsetUxtw(0, 8, 0));
        WriteInstruction(text, CaveLoadValueOffset + 4, VanillaTailRestoreInstruction);
        WriteInstruction(text, CaveLoadValueOffset + 8, EncodeBranch(CaveLoadValueOffset + 8, ExeFsReturnOffset));
    }

    private static void WriteTableAndMarker(byte[] text, IReadOnlyList<byte> caps)
    {
        for (var index = 0; index < CapCount; index++)
        {
            text[ExeFsTableOffset + index] = caps[index];
        }

        Marker.CopyTo(text.AsSpan(ExeFsTableOffset + CapCount));
        text[ExeFsTableOffset + CapCount + Marker.Length] = CapCount;
        text[ExeFsTableOffset + CapCount + Marker.Length + 1] = 1;
        text.AsSpan(0x013AE3C0, 8).Clear();
    }

    private static byte[] NormalizeCaps(ReadOnlySpan<byte> caps)
    {
        var normalized = caps.ToArray();
        normalized[FinalBadgeCount] = FinalBadgeCap;
        return normalized;
    }

    private static bool HasMarker(ReadOnlySpan<byte> text)
    {
        return text.Length >= ExeFsTableOffset + CapCount + Marker.Length + 2
            && text.Slice(ExeFsTableOffset + CapCount, Marker.Length).SequenceEqual(Marker)
            && text[ExeFsTableOffset + CapCount + Marker.Length] == CapCount
            && text[ExeFsTableOffset + CapCount + Marker.Length + 1] == 1;
    }

    private static void EnsureCavesAvailableOrOwned(byte[] text)
    {
        foreach (var cave in new[] { CaveClampOffset, CaveLoadAddressOffset, CaveLoadValueOffset, CaveOverflowOffset })
        {
            EnsureTextRange(text, cave, 0x0C, $"Catch Cap Hook cave main.text+0x{cave:X}");
            if (!HasMarker(text) && !text.AsSpan(cave, 0x0C).SequenceEqual(new byte[0x0C]))
            {
                throw new InvalidDataException($"Catch Cap Hook cave main.text+0x{cave:X} is not empty.");
            }
        }
    }

    private static void EnsureTextRange(ReadOnlySpan<byte> text, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset + length > text.Length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed .text segment.");
        }
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        EnsureTextRange(text, offset, sizeof(uint), $"Instruction main.text+0x{offset:X}");
        return BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        EnsureTextRange(text, offset, sizeof(uint), $"Patch instruction main.text+0x{offset:X}");
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static bool IsUnconditionalBranch(uint instruction)
    {
        return (instruction & 0x7C000000) == 0x14000000;
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

    private static uint EncodeAdr(int register, int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        if (delta < -(1 << 20) || delta >= (1 << 20))
        {
            throw new InvalidDataException("ADR target is outside ARM64 range.");
        }

        var immediate = delta & 0x1FFFFF;
        var immediateLow = immediate & 0x3;
        var immediateHigh = (immediate >> 2) & 0x7FFFF;
        return 0x10000000u
            | (uint)(immediateLow << 29)
            | (uint)(immediateHigh << 5)
            | (uint)(register & 0x1F);
    }

    private static uint EncodeMovzImmediate32(int register, int immediate)
    {
        if (immediate is < 0 or > 0xFFFF)
        {
            throw new InvalidDataException("MOVZ immediate must fit in 16 bits.");
        }

        return (uint)(0x52800000 | ((immediate & 0xFFFF) << 5) | (register & 0x1F));
    }

    private static uint EncodeLdrbRegisterOffsetUxtw(int targetRegister, int baseRegister, int offsetRegister)
    {
        return 0x38604800u
            | (uint)((offsetRegister & 0x1F) << 16)
            | (uint)((baseRegister & 0x1F) << 5)
            | (uint)(targetRegister & 0x1F);
    }

    private static uint EncodeNop()
    {
        return 0xD503201F;
    }

    private enum Arm64Condition
    {
        LS = 9,
    }
}
