// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.Formats.Lua;

/// <summary>
/// Losslessly reads and writes the standard little-endian Lua 5.4 binary-chunk
/// envelope used by the Scarlet and Violet retail scripts. Constant payloads
/// remain opaque so integer, floating-point, and binary string data round-trip
/// without a managed representation changing their bytes.
/// </summary>
public sealed class Lua54BinaryChunk
{
    private static readonly byte[] Signature = [0x1B, 0x4C, 0x75, 0x61];
    private static readonly byte[] ConversionData = [0x19, 0x93, 0x0D, 0x0A, 0x1A, 0x0A];

    public const int MaximumInputBytes = 64 * 1024 * 1024;
    public const int MaximumPrototypeDepth = 512;
    public const int MaximumPrototypeCount = 1_000_000;

    private Lua54BinaryChunk(byte mainUpvalueCount, Lua54Prototype root)
    {
        MainUpvalueCount = mainUpvalueCount;
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public byte MainUpvalueCount { get; }

    public Lua54Prototype Root { get; }

    public static Lua54BinaryChunk Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException(
                $"Lua 5.4 chunk length must be between 1 and {MaximumInputBytes} bytes.");
        }

        var reader = new Reader(bytes);
        reader.Expect(Signature);
        if (reader.ReadByte() != 0x54 || reader.ReadByte() != 0)
        {
            throw new InvalidDataException("Expected a standard Lua 5.4 binary chunk.");
        }

        reader.Expect(ConversionData);
        if (reader.ReadByte() != sizeof(uint)
            || reader.ReadByte() != sizeof(long)
            || reader.ReadByte() != sizeof(double))
        {
            throw new InvalidDataException("The Lua chunk uses unsupported scalar sizes.");
        }

        if (reader.ReadInt64() != 0x5678
            || BitConverter.Int64BitsToDouble(reader.ReadInt64()) != 370.5)
        {
            throw new InvalidDataException(
                "The Lua chunk uses an unsupported byte order or number representation.");
        }

        var mainUpvalueCount = reader.ReadByte();
        var prototypeCount = 0;
        var root = reader.ReadPrototype(depth: 0, ref prototypeCount);
        if (!reader.IsAtEnd)
        {
            throw new InvalidDataException(
                $"The Lua chunk has {reader.Remaining} unparsed trailing bytes.");
        }

        return new Lua54BinaryChunk(mainUpvalueCount, root);
    }

    public Lua54BinaryChunk WithRoot(Lua54Prototype root) =>
        new(MainUpvalueCount, root);

    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        var writer = new Writer(stream);
        writer.WriteBytes(Signature);
        writer.WriteByte(0x54);
        writer.WriteByte(0);
        writer.WriteBytes(ConversionData);
        writer.WriteByte(sizeof(uint));
        writer.WriteByte(sizeof(long));
        writer.WriteByte(sizeof(double));
        writer.WriteInt64(0x5678);
        writer.WriteInt64(BitConverter.DoubleToInt64Bits(370.5));
        writer.WriteByte(MainUpvalueCount);
        writer.WritePrototype(Root, depth: 0);
        return stream.ToArray();
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> bytes;
        private int offset;

        public Reader(ReadOnlySpan<byte> bytes)
        {
            this.bytes = bytes;
            offset = 0;
        }

        public bool IsAtEnd => offset == bytes.Length;

        public int Remaining => bytes.Length - offset;

        public byte ReadByte()
        {
            EnsureAvailable(1);
            return bytes[offset++];
        }

        public long ReadInt64()
        {
            EnsureAvailable(sizeof(long));
            var value = BinaryPrimitives.ReadInt64LittleEndian(bytes[offset..]);
            offset += sizeof(long);
            return value;
        }

        public uint ReadUInt32()
        {
            EnsureAvailable(sizeof(uint));
            var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
            offset += sizeof(uint);
            return value;
        }

        public void Expect(ReadOnlySpan<byte> expected)
        {
            EnsureAvailable(expected.Length);
            if (!bytes.Slice(offset, expected.Length).SequenceEqual(expected))
            {
                throw new InvalidDataException(
                    $"Unexpected Lua chunk bytes at file offset 0x{offset:X}.");
            }

            offset += expected.Length;
        }

        public Lua54Prototype ReadPrototype(int depth, ref int prototypeCount)
        {
            if (depth > MaximumPrototypeDepth)
            {
                throw new InvalidDataException(
                    $"The Lua chunk exceeds the supported prototype depth of {MaximumPrototypeDepth}.");
            }

            prototypeCount = checked(prototypeCount + 1);
            if (prototypeCount > MaximumPrototypeCount)
            {
                throw new InvalidDataException(
                    $"The Lua chunk exceeds the supported prototype count of {MaximumPrototypeCount}.");
            }

            var declaredSource = ReadLuaString();
            var lineDefined = ReadCount("line number");
            var lastLineDefined = ReadCount("line number");
            var numParams = ReadByte();
            var isVarArg = ReadByte();
            var maxStackSize = ReadByte();

            var codeCount = ReadCount("instruction");
            if (codeCount > Remaining / sizeof(uint))
            {
                throw new InvalidDataException("The Lua instruction array is truncated.");
            }

            var code = new uint[codeCount];
            for (var index = 0; index < code.Length; index++)
            {
                code[index] = ReadUInt32();
            }

            var constants = new Lua54Constant[ReadCount("constant")];
            for (var index = 0; index < constants.Length; index++)
            {
                constants[index] = ReadConstant();
            }

            var upvalues = new Lua54Upvalue[ReadCount("upvalue")];
            if (upvalues.Length > Remaining / 3)
            {
                throw new InvalidDataException("The Lua upvalue array is truncated.");
            }

            for (var index = 0; index < upvalues.Length; index++)
            {
                upvalues[index] = new Lua54Upvalue(ReadByte(), ReadByte(), ReadByte());
            }

            var children = new Lua54Prototype[ReadCount("child prototype")];
            for (var index = 0; index < children.Length; index++)
            {
                children[index] = ReadPrototype(depth + 1, ref prototypeCount);
            }

            var lineInfoCount = ReadCount("line-info entry");
            EnsureAvailable(lineInfoCount);
            var lineInfo = bytes.Slice(offset, lineInfoCount).ToArray();
            offset += lineInfoCount;

            var absoluteLines = new Lua54AbsoluteLineInfo[ReadCount("absolute line-info entry")];
            for (var index = 0; index < absoluteLines.Length; index++)
            {
                absoluteLines[index] = new Lua54AbsoluteLineInfo(
                    ReadCount("instruction index"),
                    ReadCount("line number"));
            }

            var localVariables = new Lua54LocalVariable[ReadCount("local variable")];
            for (var index = 0; index < localVariables.Length; index++)
            {
                localVariables[index] = new Lua54LocalVariable(
                    ReadLuaString(),
                    ReadCount("instruction index"),
                    ReadCount("instruction index"));
            }

            var upvalueNames = new byte[]?[ReadCount("upvalue name")];
            for (var index = 0; index < upvalueNames.Length; index++)
            {
                upvalueNames[index] = ReadLuaString();
            }

            return new Lua54Prototype(
                declaredSource,
                lineDefined,
                lastLineDefined,
                numParams,
                isVarArg,
                maxStackSize,
                code,
                constants,
                upvalues,
                children,
                lineInfo,
                absoluteLines,
                localVariables,
                upvalueNames);
        }

        private Lua54Constant ReadConstant()
        {
            var tag = ReadByte();
            return tag switch
            {
                Lua54Constant.NilTag
                    or Lua54Constant.FalseTag
                    or Lua54Constant.TrueTag => new Lua54Constant(tag, []),
                Lua54Constant.NumberTag
                    or Lua54Constant.IntegerTag => new Lua54Constant(tag, ReadBytes(sizeof(long))),
                Lua54Constant.ShortStringTag
                    or Lua54Constant.LongStringTag => new Lua54Constant(
                        tag,
                        ReadLuaString()
                            ?? throw new InvalidDataException(
                                "A Lua string constant cannot use the null-string encoding.")),
                _ => throw new InvalidDataException(
                    $"Unsupported Lua 5.4 constant tag 0x{tag:X2} at file offset 0x{offset - 1:X}."),
            };
        }

        private byte[]? ReadLuaString()
        {
            var encodedLength = ReadUnsigned();
            if (encodedLength == 0)
            {
                return null;
            }

            var contentLength = encodedLength - 1;
            if (contentLength > int.MaxValue)
            {
                throw new InvalidDataException("A Lua string exceeds the supported length.");
            }

            return ReadBytes((int)contentLength);
        }

        private byte[] ReadBytes(int length)
        {
            EnsureAvailable(length);
            var result = bytes.Slice(offset, length).ToArray();
            offset += length;
            return result;
        }

        private int ReadCount(string label)
        {
            var value = ReadUnsigned();
            if (value > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"The Lua {label} count exceeds the supported range.");
            }

            return (int)value;
        }

        private ulong ReadUnsigned()
        {
            ulong value = 0;
            for (var byteIndex = 0; byteIndex < 10; byteIndex++)
            {
                var next = ReadByte();
                if (value > (ulong.MaxValue >> 7))
                {
                    throw new InvalidDataException("A Lua variable-length integer overflowed.");
                }

                value = (value << 7) | (uint)(next & 0x7F);
                if ((next & 0x80) != 0)
                {
                    return value;
                }
            }

            throw new InvalidDataException("A Lua variable-length integer is unterminated.");
        }

        private void EnsureAvailable(int length)
        {
            if (length < 0 || offset > bytes.Length - length)
            {
                throw new InvalidDataException(
                    $"The Lua chunk is truncated at file offset 0x{offset:X}.");
            }
        }
    }

    private sealed class Writer(Stream stream)
    {
        public void WritePrototype(Lua54Prototype prototype, int depth)
        {
            if (depth > MaximumPrototypeDepth)
            {
                throw new InvalidDataException(
                    $"The Lua prototype tree exceeds the supported depth of {MaximumPrototypeDepth}.");
            }

            WriteLuaString(prototype.DeclaredSourceBytes);
            WriteCount(prototype.LineDefined, "line number");
            WriteCount(prototype.LastLineDefined, "line number");
            WriteByte(prototype.NumParams);
            WriteByte(prototype.IsVarArg);
            WriteByte(prototype.MaxStackSize);

            WriteCount(prototype.Code.Count, "instruction");
            Span<byte> encodedInstruction = stackalloc byte[sizeof(uint)];
            foreach (var instruction in prototype.Code)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(encodedInstruction, instruction);
                stream.Write(encodedInstruction);
            }

            WriteCount(prototype.Constants.Count, "constant");
            foreach (var constant in prototype.Constants)
            {
                WriteConstant(constant);
            }

            WriteCount(prototype.Upvalues.Count, "upvalue");
            foreach (var upvalue in prototype.Upvalues)
            {
                WriteByte(upvalue.InStack);
                WriteByte(upvalue.Index);
                WriteByte(upvalue.Kind);
            }

            WriteCount(prototype.Children.Count, "child prototype");
            foreach (var child in prototype.Children)
            {
                WritePrototype(child, depth + 1);
            }

            WriteCount(prototype.LineInfoBytes.Count, "line-info entry");
            foreach (var value in prototype.LineInfoBytes)
            {
                WriteByte(value);
            }

            WriteCount(prototype.AbsoluteLines.Count, "absolute line-info entry");
            foreach (var absoluteLine in prototype.AbsoluteLines)
            {
                WriteCount(absoluteLine.ProgramCounter, "instruction index");
                WriteCount(absoluteLine.Line, "line number");
            }

            WriteCount(prototype.LocalVariables.Count, "local variable");
            foreach (var local in prototype.LocalVariables)
            {
                WriteLuaString(local.NameBytes);
                WriteCount(local.StartProgramCounter, "instruction index");
                WriteCount(local.EndProgramCounter, "instruction index");
            }

            WriteCount(prototype.UpvalueNameBytes.Count, "upvalue name");
            foreach (var upvalueName in prototype.UpvalueNameBytes)
            {
                WriteLuaString(upvalueName);
            }
        }

        public void WriteByte(byte value) => stream.WriteByte(value);

        public void WriteBytes(ReadOnlySpan<byte> value) => stream.Write(value);

        public void WriteInt64(long value)
        {
            Span<byte> encoded = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(encoded, value);
            stream.Write(encoded);
        }

        private void WriteConstant(Lua54Constant constant)
        {
            WriteByte(constant.Tag);
            switch (constant.Tag)
            {
                case Lua54Constant.NilTag:
                case Lua54Constant.FalseTag:
                case Lua54Constant.TrueTag:
                    RequirePayloadLength(constant, 0);
                    break;
                case Lua54Constant.NumberTag:
                case Lua54Constant.IntegerTag:
                    RequirePayloadLength(constant, sizeof(long));
                    foreach (var value in constant.PayloadBytes)
                    {
                        WriteByte(value);
                    }
                    break;
                case Lua54Constant.ShortStringTag:
                case Lua54Constant.LongStringTag:
                    WriteLuaString(constant.PayloadBytes);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported Lua 5.4 constant tag 0x{constant.Tag:X2}.");
            }
        }

        private void WriteLuaString(IReadOnlyList<byte>? value)
        {
            if (value is null)
            {
                WriteUnsigned(0);
                return;
            }

            WriteUnsigned(checked((ulong)value.Count + 1));
            foreach (var item in value)
            {
                WriteByte(item);
            }
        }

        private void WriteCount(int value, string label)
        {
            if (value < 0)
            {
                throw new InvalidDataException($"The Lua {label} count cannot be negative.");
            }

            WriteUnsigned((ulong)value);
        }

        private void WriteUnsigned(ulong value)
        {
            Span<byte> groups = stackalloc byte[10];
            var count = 0;
            do
            {
                groups[count++] = (byte)(value & 0x7F);
                value >>= 7;
            }
            while (value != 0);

            for (var index = count - 1; index >= 0; index--)
            {
                var encoded = groups[index];
                if (index == 0)
                {
                    encoded |= 0x80;
                }

                WriteByte(encoded);
            }
        }

        private static void RequirePayloadLength(Lua54Constant constant, int expected)
        {
            if (constant.PayloadBytes.Count != expected)
            {
                throw new InvalidDataException(
                    $"Lua constant tag 0x{constant.Tag:X2} requires {expected} payload bytes.");
            }
        }
    }
}

public sealed class Lua54Prototype
{
    public Lua54Prototype(
        byte[]? declaredSourceBytes,
        int lineDefined,
        int lastLineDefined,
        byte numParams,
        byte isVarArg,
        byte maxStackSize,
        IReadOnlyList<uint> code,
        IReadOnlyList<Lua54Constant> constants,
        IReadOnlyList<Lua54Upvalue> upvalues,
        IReadOnlyList<Lua54Prototype> children,
        IReadOnlyList<byte> lineInfoBytes,
        IReadOnlyList<Lua54AbsoluteLineInfo> absoluteLines,
        IReadOnlyList<Lua54LocalVariable> localVariables,
        IReadOnlyList<byte[]?> upvalueNameBytes)
    {
        DeclaredSourceBytes = declaredSourceBytes?.ToArray();
        LineDefined = lineDefined;
        LastLineDefined = lastLineDefined;
        NumParams = numParams;
        IsVarArg = isVarArg;
        MaxStackSize = maxStackSize;
        Code = code.ToArray();
        Constants = constants.ToArray();
        Upvalues = upvalues.ToArray();
        Children = children.ToArray();
        LineInfoBytes = lineInfoBytes.ToArray();
        AbsoluteLines = absoluteLines.ToArray();
        LocalVariables = localVariables.ToArray();
        UpvalueNameBytes = upvalueNameBytes
            .Select(name => name?.ToArray())
            .ToArray();
    }

    public IReadOnlyList<byte>? DeclaredSourceBytes { get; }

    public int LineDefined { get; }

    public int LastLineDefined { get; }

    public byte NumParams { get; }

    public byte IsVarArg { get; }

    public byte MaxStackSize { get; }

    public IReadOnlyList<uint> Code { get; }

    public IReadOnlyList<Lua54Constant> Constants { get; }

    public IReadOnlyList<Lua54Upvalue> Upvalues { get; }

    public IReadOnlyList<Lua54Prototype> Children { get; }

    public IReadOnlyList<byte> LineInfoBytes { get; }

    public IReadOnlyList<Lua54AbsoluteLineInfo> AbsoluteLines { get; }

    public IReadOnlyList<Lua54LocalVariable> LocalVariables { get; }

    public IReadOnlyList<byte[]?> UpvalueNameBytes { get; }

    public Lua54Prototype WithCodeAndConstants(
        IReadOnlyList<uint> code,
        IReadOnlyList<Lua54Constant> constants) =>
        new(
            DeclaredSourceBytes?.ToArray(),
            LineDefined,
            LastLineDefined,
            NumParams,
            IsVarArg,
            MaxStackSize,
            code,
            constants,
            Upvalues,
            Children,
            LineInfoBytes,
            AbsoluteLines,
            LocalVariables,
            UpvalueNameBytes);

    public Lua54Prototype WithChildren(IReadOnlyList<Lua54Prototype> children) =>
        new(
            DeclaredSourceBytes?.ToArray(),
            LineDefined,
            LastLineDefined,
            NumParams,
            IsVarArg,
            MaxStackSize,
            Code,
            Constants,
            Upvalues,
            children,
            LineInfoBytes,
            AbsoluteLines,
            LocalVariables,
            UpvalueNameBytes);
}

public sealed class Lua54Constant
{
    public const byte NilTag = 0;
    public const byte FalseTag = 1;
    public const byte NumberTag = 3;
    public const byte ShortStringTag = 4;
    public const byte TrueTag = 17;
    public const byte IntegerTag = 19;
    public const byte LongStringTag = 20;

    public Lua54Constant(byte tag, IReadOnlyList<byte> payloadBytes)
    {
        Tag = tag;
        PayloadBytes = payloadBytes.ToArray();
    }

    public byte Tag { get; }

    public IReadOnlyList<byte> PayloadBytes { get; }

    public static Lua54Constant FromUtf8String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var payload = Encoding.UTF8.GetBytes(value);
        return new Lua54Constant(
            payload.Length <= 40 ? ShortStringTag : LongStringTag,
            payload);
    }

    public bool TryGetUtf8String(out string value)
    {
        if (Tag is not (ShortStringTag or LongStringTag))
        {
            value = string.Empty;
            return false;
        }

        value = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(PayloadBytes.ToArray());
        return true;
    }
}

public readonly record struct Lua54Upvalue(byte InStack, byte Index, byte Kind);

public readonly record struct Lua54AbsoluteLineInfo(int ProgramCounter, int Line);

public sealed class Lua54LocalVariable
{
    public Lua54LocalVariable(
        IReadOnlyList<byte>? nameBytes,
        int startProgramCounter,
        int endProgramCounter)
    {
        NameBytes = nameBytes?.ToArray();
        StartProgramCounter = startProgramCounter;
        EndProgramCounter = endProgramCounter;
    }

    public IReadOnlyList<byte>? NameBytes { get; }

    public int StartProgramCounter { get; }

    public int EndProgramCounter { get; }
}
