// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.SV.TmMachine;

internal enum SvTmMaterialVisibilityKind
{
    DiscoveryGated,
    AlwaysVisible,
    Unsupported,
}

internal sealed record SvTmMaterialVisibilityAnalysis(
    SvTmMaterialVisibilityKind Kind,
    string Message,
    string Sha256,
    int? InstructionOffset);

internal static class SvTmMaterialVisibilityPatcher
{
    private const string SupportedBaseSha256 =
        "C1A4F4E2625912CF0B739A642A79BA3357DF0557A544B92DA22A6CB139DE2815";
    private const string AlwaysVisibleSha256 =
        "7CA910D8781AFDB8168160020449C1627CFF29598406C6B06B1FA517E00D7F7A";
    private const uint DiscoveryGateInstruction = 0x000085C2;
    private const uint AlwaysVisibleInstruction = 0x000B0580;
    private const uint VisibilityJumpInstruction = 0x800011B8;
    private const int GateInstructionIndex = 168;
    private const int VisibleRenderingIndex = 206;

    public static SvTmMaterialVisibilityAnalysis Analyze(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var expectedInstruction = string.Equals(sha256, SupportedBaseSha256, StringComparison.Ordinal)
            ? DiscoveryGateInstruction
            : string.Equals(sha256, AlwaysVisibleSha256, StringComparison.Ordinal)
                ? AlwaysVisibleInstruction
                : (uint?)null;
        if (expectedInstruction is null)
        {
            return new SvTmMaterialVisibilityAnalysis(
                SvTmMaterialVisibilityKind.Unsupported,
                "TM material visibility supports only the exact Scarlet/Violet 4.0.0 script input or KM's verified output.",
                sha256,
                InstructionOffset: null);
        }

        try
        {
            var prototype = Lua54ChunkInspector.FindMaterialTracker(bytes);
            ValidatePatchWindow(prototype, expectedInstruction.Value);
            return expectedInstruction == DiscoveryGateInstruction
                ? new SvTmMaterialVisibilityAnalysis(
                    SvTmMaterialVisibilityKind.DiscoveryGated,
                    "TM material names use the standard discovery checks in the tracking window.",
                    sha256,
                    prototype.CodeOffset + (GateInstructionIndex * sizeof(uint)))
                : new SvTmMaterialVisibilityAnalysis(
                    SvTmMaterialVisibilityKind.AlwaysVisible,
                    "TM material names are always shown in the tracking window.",
                    sha256,
                    prototype.CodeOffset + (GateInstructionIndex * sizeof(uint)));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or OverflowException
            or ArgumentOutOfRangeException)
        {
            return new SvTmMaterialVisibilityAnalysis(
                SvTmMaterialVisibilityKind.Unsupported,
                $"The TM tracking script failed structural validation: {exception.Message}",
                sha256,
                InstructionOffset: null);
        }
    }

    public static byte[] Apply(byte[] currentBytes, bool alwaysVisible)
    {
        ArgumentNullException.ThrowIfNull(currentBytes);

        var analysis = Analyze(currentBytes);
        if (analysis.Kind == SvTmMaterialVisibilityKind.Unsupported
            || analysis.InstructionOffset is not { } instructionOffset)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var desiredKind = alwaysVisible
            ? SvTmMaterialVisibilityKind.AlwaysVisible
            : SvTmMaterialVisibilityKind.DiscoveryGated;
        if (analysis.Kind == desiredKind)
        {
            return currentBytes.ToArray();
        }

        var output = currentBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            output.AsSpan(instructionOffset, sizeof(uint)),
            alwaysVisible ? AlwaysVisibleInstruction : DiscoveryGateInstruction);

        var expectedHash = alwaysVisible ? AlwaysVisibleSha256 : SupportedBaseSha256;
        var actualHash = Convert.ToHexString(SHA256.HashData(output));
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The TM material visibility transform changed bytes outside its owned instruction.");
        }

        var reparsed = Analyze(output);
        if (reparsed.Kind != desiredKind || reparsed.InstructionOffset != instructionOffset)
        {
            throw new InvalidDataException("The TM material visibility transform did not survive structural reparse validation.");
        }

        return output;
    }

    private static void ValidatePatchWindow(ParsedPrototype prototype, uint expectedInstruction)
    {
        if (prototype.MaxStackSize != 20
            || prototype.Code.Length != 412
            || prototype.ConstantCount != 46
            || prototype.ChildCount != 1)
        {
            throw new InvalidDataException("The TM tracking prototype shape does not match the supported build.");
        }

        if (prototype.Code[GateInstructionIndex] != expectedInstruction)
        {
            throw new InvalidDataException("The TM tracking discovery instruction does not match its supported preimage.");
        }

        if (prototype.Code[GateInstructionIndex + 1] != VisibilityJumpInstruction)
        {
            throw new InvalidDataException("The TM tracking visible-render jump does not match its supported preimage.");
        }

        var jumpTarget = GateInstructionIndex + 2 + GetSignedJump(prototype.Code[GateInstructionIndex + 1]);
        if (jumpTarget != VisibleRenderingIndex)
        {
            throw new InvalidDataException("The TM tracking visible-render jump target is not supported.");
        }

        for (var index = 0; index < prototype.Code.Length; index++)
        {
            var instruction = prototype.Code[index];
            if ((instruction & 0x7F) != 56)
            {
                continue;
            }

            var target = index + 1 + GetSignedJump(instruction);
            if (target is GateInstructionIndex or GateInstructionIndex + 1)
            {
                throw new InvalidDataException("The TM tracking patch window has an unsupported incoming control-flow edge.");
            }
        }
    }

    private static int GetSignedJump(uint instruction) =>
        checked((int)((instruction >> 7) & 0x1FFFFFF) - 0xFFFFFF);

    private sealed record ParsedPrototype(
        int CodeOffset,
        uint[] Code,
        int ConstantCount,
        int ChildCount,
        byte MaxStackSize,
        bool HasNeedItemArray,
        bool HasItemNamePane,
        bool HasUnknownMaterialLabel)
    {
        public bool IsMaterialTracker =>
            HasNeedItemArray && HasItemNamePane && HasUnknownMaterialLabel;
    }

    private sealed class Lua54ChunkInspector
    {
        private static ReadOnlySpan<byte> LuaSignature => [0x1B, 0x4C, 0x75, 0x61];
        private static ReadOnlySpan<byte> LuaCheckData => [0x19, 0x93, 0x0D, 0x0A, 0x1A, 0x0A];
        private static ReadOnlySpan<byte> NeedItemArray => "m_NeedItemArray"u8;
        private static ReadOnlySpan<byte> ItemNamePane => "T_itamename_0"u8;
        private static ReadOnlySpan<byte> UnknownMaterialLabel => "hud_infoarea_17"u8;
        private const int MaximumPrototypeCount = 100_000;
        private const int MaximumPrototypeDepth = 256;

        private readonly byte[] bytes;
        private readonly List<ParsedPrototype> prototypes = [];
        private int offset;

        private Lua54ChunkInspector(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public static ParsedPrototype FindMaterialTracker(byte[] bytes)
        {
            var inspector = new Lua54ChunkInspector(bytes);
            inspector.ReadChunk();
            var matches = inspector.prototypes.Where(prototype => prototype.IsMaterialTracker).ToArray();
            return matches.Length == 1
                ? matches[0]
                : throw new InvalidDataException(
                    $"Expected one TM tracking prototype, but found {matches.Length}.");
        }

        private void ReadChunk()
        {
            RequireBytes(LuaSignature);
            if (ReadByte() != 0x54 || ReadByte() != 0)
            {
                throw new InvalidDataException("The script is not a supported Lua 5.4 binary chunk.");
            }

            RequireBytes(LuaCheckData);
            if (ReadByte() != 4 || ReadByte() != 8 || ReadByte() != 8)
            {
                throw new InvalidDataException("The Lua primitive-size header is not supported.");
            }

            if (ReadInt64() != 0x5678
                || ReadUInt64() != 0x4077280000000000UL)
            {
                throw new InvalidDataException("The Lua endian and number-format header is not supported.");
            }

            _ = ReadByte();
            ReadPrototype(depth: 0);
            if (offset != bytes.Length)
            {
                throw new InvalidDataException("The Lua chunk contains trailing or unparsed bytes.");
            }
        }

        private void ReadPrototype(int depth)
        {
            if (depth > MaximumPrototypeDepth || prototypes.Count >= MaximumPrototypeCount)
            {
                throw new InvalidDataException("The Lua prototype tree exceeds its safety limit.");
            }

            SkipString();
            _ = ReadBoundedCount();
            _ = ReadBoundedCount();
            _ = ReadByte();
            _ = ReadByte();
            var maxStackSize = ReadByte();

            var codeCount = ReadBoundedCount();
            var codeOffset = offset;
            uint[] code;
            if (codeCount == 412)
            {
                code = new uint[codeCount];
                for (var index = 0; index < code.Length; index++)
                {
                    code[index] = ReadUInt32();
                }
            }
            else
            {
                Skip(checked(codeCount * sizeof(uint)));
                code = [];
            }

            var constantCount = ReadBoundedCount();
            var hasNeedItemArray = false;
            var hasItemNamePane = false;
            var hasUnknownMaterialLabel = false;
            for (var index = 0; index < constantCount; index++)
            {
                var tag = ReadByte();
                switch (tag)
                {
                    case 0:
                    case 1:
                    case 17:
                        break;
                    case 3:
                    case 19:
                        Skip(sizeof(long));
                        break;
                    case 4:
                    case 20:
                        {
                            var value = ReadString(required: true);
                            hasNeedItemArray |= value.SequenceEqual(NeedItemArray);
                            hasItemNamePane |= value.SequenceEqual(ItemNamePane);
                            hasUnknownMaterialLabel |= value.SequenceEqual(UnknownMaterialLabel);
                            break;
                        }
                    default:
                        throw new InvalidDataException($"The Lua chunk contains unsupported constant tag {tag}.");
                }
            }

            var upvalueCount = ReadBoundedCount();
            Skip(checked(upvalueCount * 3));

            var childCount = ReadBoundedCount();
            var prototype = new ParsedPrototype(
                codeOffset,
                code,
                constantCount,
                childCount,
                maxStackSize,
                hasNeedItemArray,
                hasItemNamePane,
                hasUnknownMaterialLabel);
            prototypes.Add(prototype);
            for (var index = 0; index < childCount; index++)
            {
                ReadPrototype(depth + 1);
            }

            Skip(ReadBoundedCount());
            var absoluteLineCount = ReadBoundedCount();
            for (var index = 0; index < absoluteLineCount; index++)
            {
                _ = ReadBoundedCount();
                _ = ReadBoundedCount();
            }

            var localCount = ReadBoundedCount();
            for (var index = 0; index < localCount; index++)
            {
                SkipString();
                _ = ReadBoundedCount();
                _ = ReadBoundedCount();
            }

            var hasUpvalueNames = ReadBoundedCount();
            if (hasUpvalueNames != 0)
            {
                for (var index = 0; index < upvalueCount; index++)
                {
                    SkipString();
                }
            }
        }

        private int ReadBoundedCount()
        {
            var value = ReadUnsigned((ulong)int.MaxValue);
            return checked((int)value);
        }

        private ulong ReadUnsigned(ulong limit)
        {
            ulong value = 0;
            for (var index = 0; index < 10; index++)
            {
                var next = ReadByte();
                if (value > (limit >> 7))
                {
                    throw new InvalidDataException("A Lua variable-length integer exceeds its safety limit.");
                }

                value = (value << 7) | (uint)(next & 0x7F);
                if (value > limit)
                {
                    throw new InvalidDataException("A Lua variable-length integer exceeds its safety limit.");
                }

                if ((next & 0x80) != 0)
                {
                    return value;
                }
            }

            throw new InvalidDataException("A Lua variable-length integer is unterminated.");
        }

        private ReadOnlySpan<byte> ReadString(bool required)
        {
            var encodedLength = ReadUnsigned((ulong)int.MaxValue);
            if (encodedLength == 0)
            {
                if (required)
                {
                    throw new InvalidDataException("A Lua string constant is null.");
                }

                return ReadOnlySpan<byte>.Empty;
            }

            var length = checked((int)encodedLength - 1);
            return ReadSpan(length);
        }

        private void SkipString() => _ = ReadString(required: false);

        private byte ReadByte() => ReadSpan(1)[0];

        private uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadSpan(sizeof(uint)));

        private ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadSpan(sizeof(ulong)));

        private long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(ReadSpan(sizeof(long)));

        private void RequireBytes(ReadOnlySpan<byte> expected)
        {
            if (!ReadSpan(expected.Length).SequenceEqual(expected))
            {
                throw new InvalidDataException("The Lua chunk header is not supported.");
            }
        }

        private void Skip(int count) => _ = ReadSpan(count);

        private ReadOnlySpan<byte> ReadSpan(int count)
        {
            if (count < 0 || offset < 0 || offset > bytes.Length - count)
            {
                throw new InvalidDataException("The Lua chunk is truncated.");
            }

            var value = bytes.AsSpan(offset, count);
            offset += count;
            return value;
        }
    }
}
