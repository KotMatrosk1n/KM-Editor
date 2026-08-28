// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KM.Formats.Lua;

namespace KM.SV.RuntimeSettings;

/// <summary>
/// Adds three calls to KM-owned helpers at the stable construction, load, and
/// apply boundaries of the retail Scarlet/Violet 4.0.0 Options menu. The helper
/// implementations are registered by the matching runtime module; this class
/// only performs the deterministic ROMFS bytecode transform.
/// </summary>
public static class SvGameplayOptionsLuaTransformer
{
    public const string SourceRelativePath =
        "script/lua/bin/release/main/main.blua";

    public const string VanillaSourceSha256 =
        "C1A4F4E2625912CF0B739A642A79BA3357DF0557A544B92DA22A6CB139DE2815";
    public const string DerivedSourceSha256 =
        "59566A4F2E9E686D89A8655A5604CAE99C28FA34BBF10852F358807A7C9006F2";

    private const int ExpectedRootChildCount = 30_829;

    private static readonly byte[] VanillaSourceHash =
        Convert.FromHexString(VanillaSourceSha256);
    private static readonly byte[] DerivedSourceHash =
        Convert.FromHexString(DerivedSourceSha256);

    private static readonly TargetPrototype[] Targets =
    [
        new(
            Name: "Options constructor",
            RootChildIndex: 25_796,
            RawOffset: 0xA3B2B1,
            RawLength: 3_785,
            RawSha256: "CBE450A8F7992BA73825A3DE7E1B7EA0A5A27B084E80FE375C87E327B53B514B",
            LineDefined: 395_551,
            LastLineDefined: 395_565,
            NumParams: 2,
            MaxStackSize: 25,
            CodeCount: 599,
            ConstantCount: 77,
            UpvalueCount: 2,
            ChildCount: 0,
            CodeSha256: "C9FFB7230514A0FFA75F46720C235A4D0CB5931B1BA00C35D358DF16660478C8",
            InsertionProgramCounter: 587,
            ExpectedPreviousInstruction: 0x020C0011,
            ExpectedNextInstruction: 0x010B8011,
            HelperGlobal: "KMGameplayOptionsConstruct",
            InsertedInstructions: [0x4D000B8B, 0x00000C00, 0x01020BC4],
            AllowsJumpToInsertion: false),
        new(
            Name: "Options load",
            RootChildIndex: 25_800,
            RawOffset: 0xA3C375,
            RawLength: 2_113,
            RawSha256: "05B8C5AE26FADDD5681A2262DE1222CC872D03B70DA292B69FE36B5EB496A277",
            LineDefined: 395_587,
            LastLineDefined: 395_726,
            NumParams: 2,
            MaxStackSize: 11,
            CodeCount: 174,
            ConstantCount: 26,
            UpvalueCount: 4,
            ChildCount: 12,
            CodeSha256: "71F46D36810651ECA114BA1B1DC9C5D5A2FAF3E65177804F366142489CC0352E",
            InsertionProgramCounter: 173,
            ExpectedPreviousInstruction: 0x010203C4,
            ExpectedNextInstruction: 0x000103C7,
            HelperGlobal: "KMGameplayOptionsLoad",
            InsertedInstructions: [0x1A00048B, 0x00000500, 0x010204C4],
            AllowsJumpToInsertion: false),
        new(
            Name: "Options apply",
            RootChildIndex: 25_802,
            RawOffset: 0xA3D08A,
            RawLength: 1_041,
            RawSha256: "F04A52112FD94F22A6992D4A9C128CBAECCE34DAA4DCEE5D59C1AD77A4590A61",
            LineDefined: 395_776,
            LastLineDefined: 395_801,
            NumParams: 1,
            MaxStackSize: 5,
            CodeCount: 176,
            ConstantCount: 27,
            UpvalueCount: 4,
            ChildCount: 0,
            CodeSha256: "9CF79F8C6F3DE4BBAFF7C7E436297C18CB0DE8B888CE55D2651F2579835EED45",
            InsertionProgramCounter: 175,
            ExpectedPreviousInstruction: 0x01020144,
            ExpectedNextInstruction: 0x00010147,
            HelperGlobal: "KMGameplayOptionsApply",
            InsertedInstructions: [0x1B00018B, 0x00000200, 0x010201C4],
            AllowsJumpToInsertion: true),
    ];

    public static bool IsSupportedVanillaSource(ReadOnlySpan<byte> sourceBytes) =>
        SourceHashMatches(sourceBytes);

    public static byte[] TransformVanillaSource(ReadOnlySpan<byte> sourceBytes)
    {
        if (!SourceHashMatches(sourceBytes))
        {
            throw new InvalidDataException(
                "The S/V Options script is not the exact supported retail 4.0.0 source.");
        }

        ValidateRawPrototypePreimages(sourceBytes);

        var source = sourceBytes.ToArray();
        var chunk = Lua54BinaryChunk.Parse(source);
        if (!chunk.Serialize().AsSpan().SequenceEqual(source))
        {
            throw new InvalidDataException(
                "The supported S/V Options script did not round-trip losslessly before transformation.");
        }

        if (chunk.Root.Children.Count != ExpectedRootChildCount)
        {
            throw new InvalidDataException(
                "The supported S/V Options script has an unexpected root prototype inventory.");
        }

        var children = chunk.Root.Children.ToArray();
        foreach (var target in Targets)
        {
            var original = children[target.RootChildIndex];
            ValidateBasePrototype(original, target);
            children[target.RootChildIndex] = PatchPrototype(original, target);
        }

        var outputChunk = chunk.WithRoot(chunk.Root.WithChildren(children));
        var output = outputChunk.Serialize();
        VerifyOutputAndInverse(output, source);
        if (!HashMatches(output, DerivedSourceHash))
        {
            throw new InvalidDataException(
                "The S/V Options transform did not produce its exact reviewed derived identity.");
        }
        return output;
    }

    private static Lua54Prototype PatchPrototype(
        Lua54Prototype prototype,
        TargetPrototype target)
    {
        ValidateBranchRelocationPolicy(prototype, target);

        var code = new uint[prototype.Code.Count + target.InsertedInstructions.Length];
        CopyInstructions(
            prototype.Code,
            sourceIndex: 0,
            code,
            destinationIndex: 0,
            count: target.InsertionProgramCounter);
        target.InsertedInstructions.CopyTo(
            code,
            target.InsertionProgramCounter);
        CopyInstructions(
            prototype.Code,
            sourceIndex: target.InsertionProgramCounter,
            code,
            destinationIndex: target.InsertionProgramCounter
                + target.InsertedInstructions.Length,
            count: prototype.Code.Count - target.InsertionProgramCounter);

        var constants = prototype.Constants.ToList();
        constants.Add(Lua54Constant.FromUtf8String(target.HelperGlobal));
        return prototype.WithCodeAndConstants(code, constants);
    }

    private static void ValidateBasePrototype(
        Lua54Prototype prototype,
        TargetPrototype target)
    {
        if (prototype.LineDefined != target.LineDefined
            || prototype.LastLineDefined != target.LastLineDefined
            || prototype.NumParams != target.NumParams
            || prototype.IsVarArg != 0
            || prototype.MaxStackSize != target.MaxStackSize
            || prototype.Code.Count != target.CodeCount
            || prototype.Constants.Count != target.ConstantCount
            || prototype.Upvalues.Count != target.UpvalueCount
            || prototype.Children.Count != target.ChildCount
            || prototype.LineInfoBytes.Count != 0
            || prototype.AbsoluteLines.Count != 0
            || prototype.LocalVariables.Count != 0
            || prototype.UpvalueNameBytes.Count != 0)
        {
            throw new InvalidDataException(
                $"The {target.Name} prototype does not match its structural preimage.");
        }

        if (!CodeHashMatches(prototype.Code, target.CodeHash))
        {
            throw new InvalidDataException(
                $"The {target.Name} prototype does not match its instruction preimage.");
        }

        if (target.InsertionProgramCounter <= 0
            || target.InsertionProgramCounter >= prototype.Code.Count
            || prototype.Code[target.InsertionProgramCounter - 1]
                != target.ExpectedPreviousInstruction
            || prototype.Code[target.InsertionProgramCounter]
                != target.ExpectedNextInstruction)
        {
            throw new InvalidDataException(
                $"The {target.Name} insertion boundary is not the verified instruction boundary.");
        }

        if (prototype.Constants.Any(constant =>
                ConstantStringEquals(constant, target.HelperGlobal)))
        {
            throw new InvalidDataException(
                $"The {target.Name} prototype already contains the KM helper global.");
        }
    }

    private static void ValidatePatchedPrototype(
        Lua54Prototype patched,
        Lua54Prototype original,
        TargetPrototype target)
    {
        if (patched.Code.Count
                != original.Code.Count + target.InsertedInstructions.Length
            || patched.Constants.Count != original.Constants.Count + 1
            || !ConstantStringEquals(patched.Constants[^1], target.HelperGlobal)
            || !PrototypeMetadataMatches(patched, original))
        {
            throw new InvalidDataException(
                $"The transformed {target.Name} prototype has an invalid shape.");
        }

        for (var index = 0; index < target.InsertionProgramCounter; index++)
        {
            if (patched.Code[index] != original.Code[index])
            {
                throw new InvalidDataException(
                    $"The transformed {target.Name} changed instructions before its insertion boundary.");
            }
        }

        for (var index = 0; index < target.InsertedInstructions.Length; index++)
        {
            if (patched.Code[target.InsertionProgramCounter + index]
                != target.InsertedInstructions[index])
            {
                throw new InvalidDataException(
                    $"The transformed {target.Name} does not contain the expected helper call.");
            }
        }

        for (var sourceIndex = target.InsertionProgramCounter;
             sourceIndex < original.Code.Count;
             sourceIndex++)
        {
            if (patched.Code[sourceIndex + target.InsertedInstructions.Length]
                != original.Code[sourceIndex])
            {
                throw new InvalidDataException(
                    $"The transformed {target.Name} changed instructions after its insertion boundary.");
            }
        }

        for (var index = 0; index < original.Constants.Count; index++)
        {
            if (!ConstantsEqual(patched.Constants[index], original.Constants[index]))
            {
                throw new InvalidDataException(
                    $"The transformed {target.Name} changed an existing constant.");
            }
        }
    }

    private static void VerifyOutputAndInverse(byte[] output, byte[] source)
    {
        var parsedOutput = Lua54BinaryChunk.Parse(output);
        if (!parsedOutput.Serialize().AsSpan().SequenceEqual(output))
        {
            throw new InvalidDataException(
                "The transformed S/V Options script did not round-trip losslessly.");
        }

        var sourceChunk = Lua54BinaryChunk.Parse(source);
        var restoredChildren = parsedOutput.Root.Children.ToArray();
        foreach (var target in Targets)
        {
            var original = sourceChunk.Root.Children[target.RootChildIndex];
            var patched = restoredChildren[target.RootChildIndex];
            ValidatePatchedPrototype(patched, original, target);

            var restoredCode = new uint[original.Code.Count];
            CopyInstructions(
                patched.Code,
                sourceIndex: 0,
                restoredCode,
                destinationIndex: 0,
                count: target.InsertionProgramCounter);
            CopyInstructions(
                patched.Code,
                sourceIndex: target.InsertionProgramCounter
                    + target.InsertedInstructions.Length,
                restoredCode,
                destinationIndex: target.InsertionProgramCounter,
                count: original.Code.Count - target.InsertionProgramCounter);
            restoredChildren[target.RootChildIndex] = patched.WithCodeAndConstants(
                restoredCode,
                patched.Constants.Take(patched.Constants.Count - 1).ToArray());
        }

        var restored = parsedOutput
            .WithRoot(parsedOutput.Root.WithChildren(restoredChildren))
            .Serialize();
        if (!restored.AsSpan().SequenceEqual(source))
        {
            throw new InvalidDataException(
                "The S/V Options transform modified data outside its three owned insertions.");
        }
    }

    private static void ValidateRawPrototypePreimages(ReadOnlySpan<byte> source)
    {
        foreach (var target in Targets)
        {
            if (target.RawOffset < 0
                || target.RawLength < 0
                || target.RawOffset > source.Length - target.RawLength
                || !HashMatches(
                    source.Slice(target.RawOffset, target.RawLength),
                    target.RawHash))
            {
                throw new InvalidDataException(
                    $"The {target.Name} raw prototype does not match its verified preimage.");
            }
        }
    }

    private static void ValidateBranchRelocationPolicy(
        Lua54Prototype prototype,
        TargetPrototype target)
    {
        for (var programCounter = 0;
             programCounter < prototype.Code.Count;
             programCounter++)
        {
            var instruction = prototype.Code[programCounter];
            if ((instruction & 0x7F) != 56)
            {
                continue;
            }

            var ax = (int)(instruction >> 7);
            var targetProgramCounter = programCounter + 1 + ax - 16_777_215;
            var crossesInsertion = programCounter < target.InsertionProgramCounter
                && targetProgramCounter >= target.InsertionProgramCounter;
            if (!crossesInsertion)
            {
                continue;
            }

            if (!target.AllowsJumpToInsertion
                || targetProgramCounter != target.InsertionProgramCounter)
            {
                throw new InvalidDataException(
                    $"The {target.Name} contains an unsupported jump across its insertion boundary.");
            }
        }
    }

    private static bool PrototypeMetadataMatches(
        Lua54Prototype left,
        Lua54Prototype right)
    {
        return ByteListsEqual(left.DeclaredSourceBytes, right.DeclaredSourceBytes)
            && left.LineDefined == right.LineDefined
            && left.LastLineDefined == right.LastLineDefined
            && left.NumParams == right.NumParams
            && left.IsVarArg == right.IsVarArg
            && left.MaxStackSize == right.MaxStackSize
            && left.Upvalues.SequenceEqual(right.Upvalues)
            && PrototypeSequencesEqual(left.Children, right.Children)
            && left.LineInfoBytes.SequenceEqual(right.LineInfoBytes)
            && left.AbsoluteLines.SequenceEqual(right.AbsoluteLines)
            && LocalVariablesEqual(left.LocalVariables, right.LocalVariables)
            && NullableByteListSequenceEqual(
                left.UpvalueNameBytes,
                right.UpvalueNameBytes);
    }

    private static bool SourceHashMatches(ReadOnlySpan<byte> sourceBytes) =>
        sourceBytes.Length <= Lua54BinaryChunk.MaximumInputBytes
        && HashMatches(sourceBytes, VanillaSourceHash);

    private static bool CodeHashMatches(
        IReadOnlyList<uint> code,
        ReadOnlySpan<byte> expectedHash)
    {
        var bytes = new byte[checked(code.Count * sizeof(uint))];
        for (var index = 0; index < code.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)),
                code[index]);
        }

        return HashMatches(bytes, expectedHash);
    }

    private static bool HashMatches(
        ReadOnlySpan<byte> bytes,
        ReadOnlySpan<byte> expectedHash)
    {
        Span<byte> actualHash = stackalloc byte[SHA256.HashSizeInBytes];
        return SHA256.TryHashData(bytes, actualHash, out var bytesWritten)
            && bytesWritten == SHA256.HashSizeInBytes
            && CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static bool ConstantStringEquals(
        Lua54Constant constant,
        string expected)
    {
        return constant.Tag is Lua54Constant.ShortStringTag
            or Lua54Constant.LongStringTag
            && constant.PayloadBytes.SequenceEqual(Encoding.UTF8.GetBytes(expected));
    }

    private static bool ConstantsEqual(
        Lua54Constant left,
        Lua54Constant right) =>
        left.Tag == right.Tag
        && left.PayloadBytes.SequenceEqual(right.PayloadBytes);

    private static bool ByteListsEqual(
        IReadOnlyList<byte>? left,
        IReadOnlyList<byte>? right) =>
        left is null
            ? right is null
            : right is not null && left.SequenceEqual(right);

    private static bool NullableByteListSequenceEqual(
        IReadOnlyList<byte[]?> left,
        IReadOnlyList<byte[]?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!ByteListsEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LocalVariablesEqual(
        IReadOnlyList<Lua54LocalVariable> left,
        IReadOnlyList<Lua54LocalVariable> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!ByteListsEqual(left[index].NameBytes, right[index].NameBytes)
                || left[index].StartProgramCounter
                    != right[index].StartProgramCounter
                || left[index].EndProgramCounter
                    != right[index].EndProgramCounter)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PrototypeSequencesEqual(
        IReadOnlyList<Lua54Prototype> left,
        IReadOnlyList<Lua54Prototype> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!PrototypesEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PrototypesEqual(
        Lua54Prototype left,
        Lua54Prototype right)
    {
        if (!PrototypeMetadataMatches(left, right)
            || !left.Code.SequenceEqual(right.Code)
            || left.Constants.Count != right.Constants.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Constants.Count; index++)
        {
            if (!ConstantsEqual(left.Constants[index], right.Constants[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void CopyInstructions(
        IReadOnlyList<uint> source,
        int sourceIndex,
        uint[] destination,
        int destinationIndex,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            destination[destinationIndex + index] = source[sourceIndex + index];
        }
    }

    private sealed record TargetPrototype(
        string Name,
        int RootChildIndex,
        int RawOffset,
        int RawLength,
        string RawSha256,
        int LineDefined,
        int LastLineDefined,
        byte NumParams,
        byte MaxStackSize,
        int CodeCount,
        int ConstantCount,
        int UpvalueCount,
        int ChildCount,
        string CodeSha256,
        int InsertionProgramCounter,
        uint ExpectedPreviousInstruction,
        uint ExpectedNextInstruction,
        string HelperGlobal,
        uint[] InsertedInstructions,
        bool AllowsJumpToInsertion)
    {
        public byte[] RawHash { get; } = Convert.FromHexString(RawSha256);

        public byte[] CodeHash { get; } = Convert.FromHexString(CodeSha256);
    }
}
