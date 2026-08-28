// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace KM.Formats.SwSh;

public sealed record SwShAmxHeader(
    int Size,
    ushort Magic,
    byte FileVersion,
    byte AmxVersion,
    short Flags,
    short DefinitionSize,
    int CodeOffset,
    int DataOffset,
    int HeapOffset,
    int StackTop,
    int EntryPoint,
    int PublicsOffset,
    int NativesOffset,
    int LibrariesOffset,
    int PublicVariablesOffset,
    int TagsOffset,
    int NameTableOffset)
{
    public const ushort Pawn64Magic = 0xF1E1;
    public const short CompactFlag = 0x0004;
    public const short DebugFlag = 0x0002;
    public const int ByteLength = 0x38;

    public bool IsCompact => (Flags & CompactFlag) != 0;

    internal static SwShAmxHeader Read(byte[] bytes)
    {
        if (bytes.Length < ByteLength)
        {
            throw new InvalidDataException("Sword/Shield AMX data is too small for its header.");
        }

        var header = new SwShAmxHeader(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x00)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x04)),
            bytes[0x06],
            bytes[0x07],
            BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x08)),
            BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x0A)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x0C)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x10)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x14)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x18)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x1C)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x20)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x24)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x28)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x2C)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x30)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x34)));

        header.Validate(bytes.Length);
        return header;
    }

    private void Validate(int fileLength)
    {
        if (Magic != Pawn64Magic)
        {
            throw new InvalidDataException($"Expected a 64-bit Sword/Shield AMX image, but found magic 0x{Magic:X4}.");
        }

        if (FileVersion != 10 || AmxVersion != 10)
        {
            throw new InvalidDataException(
                $"Expected Sword/Shield Pawn AMX version 10, but found file version {FileVersion} and AMX version {AmxVersion}.");
        }

        if (DefinitionSize != 12)
        {
            throw new InvalidDataException(
                $"Expected 12-byte Sword/Shield AMX definition records, but found {DefinitionSize} bytes.");
        }

        if (Size < ByteLength || Size > fileLength)
        {
            throw new InvalidDataException(
                $"Sword/Shield AMX payload size 0x{Size:X} is outside file length 0x{fileLength:X}.");
        }

        if (CodeOffset < ByteLength
            || PublicsOffset < ByteLength
            || PublicsOffset > NativesOffset
            || NativesOffset > LibrariesOffset
            || LibrariesOffset > PublicVariablesOffset
            || PublicVariablesOffset > TagsOffset
            || TagsOffset > NameTableOffset
            || NameTableOffset > CodeOffset
            || CodeOffset > Size
            || DataOffset < CodeOffset
            || HeapOffset < DataOffset
            || StackTop < HeapOffset)
        {
            throw new InvalidDataException("Sword/Shield AMX header offsets are not in a valid order.");
        }

        if ((NativesOffset - PublicsOffset) % DefinitionSize != 0
            || (LibrariesOffset - NativesOffset) % DefinitionSize != 0
            || (PublicVariablesOffset - LibrariesOffset) % DefinitionSize != 0
            || (TagsOffset - PublicVariablesOffset) % DefinitionSize != 0
            || (NameTableOffset - TagsOffset) % DefinitionSize != 0)
        {
            throw new InvalidDataException("Sword/Shield AMX definition table boundaries are not record-aligned.");
        }

        if ((DataOffset - CodeOffset) % sizeof(long) != 0
            || (HeapOffset - DataOffset) % sizeof(long) != 0)
        {
            throw new InvalidDataException("Sword/Shield AMX code or data memory is not aligned to 64-bit cells.");
        }

        if (!IsCompact && (Size != HeapOffset || HeapOffset > fileLength))
        {
            throw new InvalidDataException(
                "Uncompressed Sword/Shield AMX images are supported only when the payload ends exactly at HEA.");
        }
    }
}

public enum SwShAmxOperandKind
{
    Literal,
    CodeTarget,
}

public sealed class SwShAmxOperand
{
    private readonly long literalValue;
    private SwShAmxInstruction? target;
    private int? unresolvedTargetCell;

    private SwShAmxOperand(long literalValue)
    {
        Kind = SwShAmxOperandKind.Literal;
        this.literalValue = literalValue;
    }

    private SwShAmxOperand(SwShAmxInstruction target)
    {
        Kind = SwShAmxOperandKind.CodeTarget;
        this.target = target;
    }

    private SwShAmxOperand(int unresolvedTargetCell)
    {
        Kind = SwShAmxOperandKind.CodeTarget;
        this.unresolvedTargetCell = unresolvedTargetCell;
    }

    public SwShAmxOperandKind Kind { get; }

    public long LiteralValue => Kind == SwShAmxOperandKind.Literal
        ? literalValue
        : throw new InvalidOperationException("This AMX operand is a code target, not a literal.");

    public SwShAmxInstruction Target => Kind == SwShAmxOperandKind.CodeTarget && target is not null
        ? target
        : throw new InvalidOperationException("This AMX code target has not been resolved.");

    public static SwShAmxOperand Literal(long value) => new(value);

    public static SwShAmxOperand CodeTarget(SwShAmxInstruction target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new SwShAmxOperand(target);
    }

    internal static SwShAmxOperand UnresolvedCodeTarget(int cell) => new(cell);

    internal SwShAmxOperand CloneUnresolved()
    {
        if (Kind == SwShAmxOperandKind.Literal)
        {
            return Literal(literalValue);
        }

        var originalCell = Target.OriginalCell
            ?? throw new InvalidOperationException("Only parsed AMX targets can be cloned as source instructions.");
        return UnresolvedCodeTarget(originalCell);
    }

    internal SwShAmxOperand CloneResolved()
    {
        return Kind == SwShAmxOperandKind.Literal ? Literal(literalValue) : CodeTarget(Target);
    }

    internal void Resolve(IReadOnlyDictionary<int, SwShAmxInstruction> instructionsByCell)
    {
        if (Kind != SwShAmxOperandKind.CodeTarget || target is not null)
        {
            return;
        }

        if (unresolvedTargetCell is not { } cell || !instructionsByCell.TryGetValue(cell, out target))
        {
            throw new InvalidDataException(
                $"Sword/Shield AMX relative target cell {unresolvedTargetCell?.ToString() ?? "<missing>"} is not an instruction boundary.");
        }

        unresolvedTargetCell = null;
    }

    internal void Retarget(SwShAmxInstruction oldTarget, SwShAmxInstruction newTarget)
    {
        if (Kind == SwShAmxOperandKind.CodeTarget && ReferenceEquals(target, oldTarget))
        {
            target = newTarget;
        }
    }
}

public sealed record SwShAmxSwitchCase(long Value, SwShAmxOperand Destination);

public sealed class SwShAmxInstruction
{
    private readonly SwShAmxOpcodeDefinition definition;
    private readonly SwShAmxOperand[] operands;
    private readonly ReadOnlyCollection<SwShAmxOperand> readOnlyOperands;
    private readonly SwShAmxSwitchCase[] switchCases;
    private readonly ReadOnlyCollection<SwShAmxSwitchCase> readOnlySwitchCases;

    private SwShAmxInstruction(
        SwShAmxOpcodeDefinition definition,
        IReadOnlyList<SwShAmxOperand> operands,
        SwShAmxOperand? defaultDestination,
        IReadOnlyList<SwShAmxSwitchCase> switchCases,
        int? originalCell)
    {
        this.definition = definition;
        this.operands = operands.ToArray();
        readOnlyOperands = Array.AsReadOnly(this.operands);
        DefaultDestination = defaultDestination;
        this.switchCases = switchCases.ToArray();
        readOnlySwitchCases = Array.AsReadOnly(this.switchCases);
        OriginalCell = originalCell;
        ValidateShape();
    }

    public int Opcode => definition.Opcode;

    public string Mnemonic => definition.Mnemonic;

    public int? OriginalCell { get; }

    public IReadOnlyList<SwShAmxOperand> Operands => readOnlyOperands;

    public bool IsSwitchTable => definition.Encoding is
        SwShAmxOpcodeEncoding.SwitchTable or SwShAmxOpcodeEncoding.IndirectSwitchTable;

    public bool IsIndirectSwitchTable => definition.Encoding == SwShAmxOpcodeEncoding.IndirectSwitchTable;

    public SwShAmxOperand? DefaultDestination { get; }

    public IReadOnlyList<SwShAmxSwitchCase> SwitchCases => readOnlySwitchCases;

    public int EncodedCellCount => IsSwitchTable
        ? checked(3 + (switchCases.Length * 2))
        : definition.Encoding == SwShAmxOpcodeEncoding.Packed
            ? 1
            : checked(1 + operands.Length);

    public static SwShAmxInstruction Create(int opcode, params SwShAmxOperand[] operands)
    {
        ArgumentNullException.ThrowIfNull(operands);
        var definition = SwShAmxOpcodeTable.Get(opcode);
        if (definition.Encoding is SwShAmxOpcodeEncoding.SwitchTable or SwShAmxOpcodeEncoding.IndirectSwitchTable)
        {
            throw new ArgumentException("Use CreateSwitchTable for a Sword/Shield AMX case table.", nameof(opcode));
        }

        return new SwShAmxInstruction(definition, operands, null, [], null);
    }

    public static SwShAmxInstruction CreateLiteral(int opcode, params long[] operands)
    {
        ArgumentNullException.ThrowIfNull(operands);
        return Create(opcode, operands.Select(SwShAmxOperand.Literal).ToArray());
    }

    public static SwShAmxInstruction CreateBranch(int opcode, SwShAmxInstruction target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Create(opcode, SwShAmxOperand.CodeTarget(target));
    }

    public static SwShAmxInstruction CreateSwitchTable(
        int opcode,
        SwShAmxOperand defaultDestination,
        params SwShAmxSwitchCase[] cases)
    {
        ArgumentNullException.ThrowIfNull(defaultDestination);
        ArgumentNullException.ThrowIfNull(cases);
        return new SwShAmxInstruction(
            SwShAmxOpcodeTable.Get(opcode),
            [],
            defaultDestination,
            cases,
            null);
    }

    internal static SwShAmxInstruction Parsed(
        SwShAmxOpcodeDefinition definition,
        IReadOnlyList<SwShAmxOperand> operands,
        SwShAmxOperand? defaultDestination,
        IReadOnlyList<SwShAmxSwitchCase> cases,
        int originalCell)
    {
        return new SwShAmxInstruction(definition, operands, defaultDestination, cases, originalCell);
    }

    internal SwShAmxInstruction CloneUnresolved()
    {
        return new SwShAmxInstruction(
            definition,
            operands.Select(operand => operand.CloneUnresolved()).ToArray(),
            DefaultDestination?.CloneUnresolved(),
            switchCases
                .Select(@case => new SwShAmxSwitchCase(@case.Value, @case.Destination.CloneUnresolved()))
                .ToArray(),
            OriginalCell);
    }

    internal SwShAmxInstruction CloneReplacingLiteral(int operandIndex, long expectedValue, long replacementValue)
    {
        if ((uint)operandIndex >= (uint)operands.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(operandIndex));
        }

        if (operands[operandIndex].Kind != SwShAmxOperandKind.Literal
            || operands[operandIndex].LiteralValue != expectedValue)
        {
            throw new InvalidDataException(
                $"AMX {Mnemonic} operand {operandIndex} did not match expected literal {expectedValue}.");
        }

        var replacements = operands.Select(operand => operand.CloneResolved()).ToArray();
        replacements[operandIndex] = SwShAmxOperand.Literal(replacementValue);
        return new SwShAmxInstruction(definition, replacements, null, [], OriginalCell);
    }

    internal SwShAmxInstruction CloneAddingSwitchCase(long value, SwShAmxInstruction target)
    {
        if (!IsSwitchTable)
        {
            throw new InvalidOperationException($"AMX instruction {Mnemonic} is not a case table.");
        }

        if (switchCases.Any(@case => @case.Value == value))
        {
            throw new InvalidDataException($"AMX case table already contains value {value}.");
        }

        var destination = IsIndirectSwitchTable
            ? throw new InvalidOperationException("Indirect AMX case tables require engine-owned destinations and cannot accept code targets.")
            : SwShAmxOperand.CodeTarget(target);
        var cases = switchCases
            .Select(@case => new SwShAmxSwitchCase(@case.Value, @case.Destination.CloneResolved()))
            .Append(new SwShAmxSwitchCase(value, destination))
            .OrderBy(@case => @case.Value)
            .ToArray();
        return new SwShAmxInstruction(
            definition,
            [],
            DefaultDestination!.CloneResolved(),
            cases,
            OriginalCell);
    }

    internal void ResolveTargets(IReadOnlyDictionary<int, SwShAmxInstruction> instructionsByCell)
    {
        foreach (var operand in operands)
        {
            operand.Resolve(instructionsByCell);
        }

        DefaultDestination?.Resolve(instructionsByCell);
        foreach (var @case in switchCases)
        {
            @case.Destination.Resolve(instructionsByCell);
        }
    }

    internal void Retarget(SwShAmxInstruction oldTarget, SwShAmxInstruction newTarget)
    {
        foreach (var operand in operands)
        {
            operand.Retarget(oldTarget, newTarget);
        }

        DefaultDestination?.Retarget(oldTarget, newTarget);
        foreach (var @case in switchCases)
        {
            @case.Destination.Retarget(oldTarget, newTarget);
        }
    }

    internal SwShAmxOpcodeEncoding Encoding => definition.Encoding;

    private void ValidateShape()
    {
        if (IsSwitchTable)
        {
            if (operands.Length != 0 || DefaultDestination is null)
            {
                throw new ArgumentException("Sword/Shield AMX case tables require a default destination and no regular operands.");
            }

            var requiredKind = IsIndirectSwitchTable
                ? SwShAmxOperandKind.Literal
                : SwShAmxOperandKind.CodeTarget;
            if (DefaultDestination.Kind != requiredKind
                || switchCases.Any(@case => @case.Destination.Kind != requiredKind))
            {
                throw new ArgumentException(
                    $"Sword/Shield AMX {Mnemonic} destinations must be {requiredKind} operands.");
            }

            if (switchCases.Select(@case => @case.Value).Distinct().Count() != switchCases.Length)
            {
                throw new ArgumentException("Sword/Shield AMX case values must be unique.");
            }

            return;
        }

        if (DefaultDestination is not null || switchCases.Length != 0 || operands.Length != definition.OperandCount)
        {
            throw new ArgumentException(
                $"Sword/Shield AMX {Mnemonic} expects {definition.OperandCount} operands.");
        }

        var requiredOperandKind = definition.Encoding == SwShAmxOpcodeEncoding.Relative
            ? SwShAmxOperandKind.CodeTarget
            : SwShAmxOperandKind.Literal;
        if (operands.Any(operand => operand.Kind != requiredOperandKind))
        {
            throw new ArgumentException(
                $"Sword/Shield AMX {Mnemonic} operands must be {requiredOperandKind} values.");
        }
    }
}

public sealed class SwShAmxDocument
{
    private const int CellSize = sizeof(long);
    private const int MaxExpandedBytes = 512 * 1024 * 1024;

    private readonly SwShAmxInstruction[] instructions;
    private readonly ReadOnlyCollection<SwShAmxInstruction> readOnlyInstructions;
    private readonly uint[] nativeHashes;
    private readonly ReadOnlyCollection<uint> readOnlyNativeHashes;
    private readonly long[] dataCells;
    private readonly ReadOnlyCollection<long> readOnlyDataCells;

    private SwShAmxDocument(
        SwShAmxHeader header,
        SwShAmxInstruction[] instructions,
        uint[] nativeHashes,
        ulong[] rawDataCells,
        byte[] prefix,
        byte[] trailingBytes,
        int? entryPointCell,
        IReadOnlyList<SwShAmxPublicTarget> publicTargets)
    {
        Header = header;
        this.instructions = instructions;
        readOnlyInstructions = Array.AsReadOnly(this.instructions);
        this.nativeHashes = nativeHashes;
        readOnlyNativeHashes = Array.AsReadOnly(this.nativeHashes);
        RawDataCells = rawDataCells;
        dataCells = rawDataCells.Select(value => unchecked((long)value)).ToArray();
        readOnlyDataCells = Array.AsReadOnly(dataCells);
        Prefix = prefix;
        TrailingBytes = trailingBytes;
        EntryPointCell = entryPointCell;
        PublicTargets = publicTargets;
    }

    public SwShAmxHeader Header { get; }

    public IReadOnlyList<SwShAmxInstruction> Instructions => readOnlyInstructions;

    public IReadOnlyList<uint> NativeHashes => readOnlyNativeHashes;

    public IReadOnlyList<long> DataCells => readOnlyDataCells;

    public SwShAmxInstruction? EntryPoint => EntryPointCell is { } cell
        ? instructions.Single(instruction => instruction.OriginalCell == cell)
        : null;

    internal byte[] Prefix { get; }

    internal byte[] TrailingBytes { get; }

    internal ulong[] RawDataCells { get; }

    internal int? EntryPointCell { get; }

    internal IReadOnlyList<SwShAmxPublicTarget> PublicTargets { get; }

    public static SwShAmxDocument Parse(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        var header = SwShAmxHeader.Read(bytes);
        var expandedCells = ExpandBody(bytes, header);
        var codeCellCount = (header.DataOffset - header.CodeOffset) / CellSize;
        var dataCellCount = (header.HeapOffset - header.DataOffset) / CellSize;
        if (expandedCells.Length != checked(codeCellCount + dataCellCount))
        {
            throw new InvalidDataException("Sword/Shield AMX expanded cell count does not match COD/DAT/HEA.");
        }

        var codeCells = expandedCells.AsSpan(0, codeCellCount).ToArray();
        var parsedInstructions = ParseInstructions(codeCells);
        var instructionsByCell = parsedInstructions.ToDictionary(
            instruction => instruction.OriginalCell
                ?? throw new InvalidDataException("Parsed AMX instruction is missing its source cell."));
        foreach (var instruction in parsedInstructions)
        {
            instruction.ResolveTargets(instructionsByCell);
        }

        int? entryPointCell = null;
        if (header.EntryPoint != -1)
        {
            entryPointCell = ResolveCodeByteOffset(header.EntryPoint, "entry point");
            RequireInstructionBoundary(instructionsByCell, entryPointCell.Value, "entry point");
        }

        var publicTargets = new List<SwShAmxPublicTarget>();
        for (var offset = header.PublicsOffset; offset < header.NativesOffset; offset += header.DefinitionSize)
        {
            var address = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, sizeof(ulong)));
            if (address > int.MaxValue)
            {
                throw new InvalidDataException($"Sword/Shield AMX public address 0x{address:X} exceeds supported code memory.");
            }

            var cell = ResolveCodeByteOffset((int)address, $"public definition at 0x{offset:X}");
            RequireInstructionBoundary(instructionsByCell, cell, $"public definition at 0x{offset:X}");
            publicTargets.Add(new SwShAmxPublicTarget(offset, cell));
        }

        var nativeCount = (header.LibrariesOffset - header.NativesOffset) / header.DefinitionSize;
        var nativeHashes = new uint[nativeCount];
        for (var index = 0; index < nativeCount; index++)
        {
            var offset = checked(header.NativesOffset + (index * header.DefinitionSize) + sizeof(ulong));
            nativeHashes[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
        }

        var trailingBytes = bytes[header.Size..];
        return new SwShAmxDocument(
            header,
            parsedInstructions,
            nativeHashes,
            expandedCells.AsSpan(codeCellCount, dataCellCount).ToArray(),
            bytes[..header.CodeOffset],
            trailingBytes,
            entryPointCell,
            new ReadOnlyCollection<SwShAmxPublicTarget>(publicTargets));
    }

    public SwShAmxAssembler CreateAssembler() => new(this);

    private static SwShAmxInstruction[] ParseInstructions(ulong[] cells)
    {
        var instructions = new List<SwShAmxInstruction>();
        var cell = 0;
        while (cell < cells.Length)
        {
            var rawInstruction = cells[cell];
            var opcodeValue = (uint)rawInstruction;
            if (opcodeValue > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Sword/Shield AMX code cell {cell} contains invalid opcode 0x{opcodeValue:X8}.");
            }
            var opcode = (int)opcodeValue;
            var definition = SwShAmxOpcodeTable.Get(opcode);
            if (definition.Encoding != SwShAmxOpcodeEncoding.Packed && rawInstruction > uint.MaxValue)
            {
                throw new InvalidDataException(
                    $"Sword/Shield AMX opcode cell {cell} contains unexpected high bits 0x{rawInstruction >> 32:X8}.");
            }

            if (definition.Encoding is SwShAmxOpcodeEncoding.SwitchTable or SwShAmxOpcodeEncoding.IndirectSwitchTable)
            {
                if (cell > cells.Length - 3)
                {
                    throw new InvalidDataException($"Sword/Shield AMX {definition.Mnemonic} at cell {cell} is truncated.");
                }

                var caseCountValue = unchecked((long)cells[cell + 1]);
                if (caseCountValue < 0 || caseCountValue > (cells.Length - cell - 3) / 2)
                {
                    throw new InvalidDataException(
                        $"Sword/Shield AMX {definition.Mnemonic} at cell {cell} has invalid case count {caseCountValue}.");
                }

                var caseCount = checked((int)caseCountValue);
                var isIndirect = definition.Encoding == SwShAmxOpcodeEncoding.IndirectSwitchTable;
                var defaultRaw = unchecked((long)cells[cell + 2]);
                var defaultDestination = isIndirect
                    ? SwShAmxOperand.Literal(defaultRaw)
                    : SwShAmxOperand.UnresolvedCodeTarget(
                        ResolveRelativeTarget(cell + 1, defaultRaw, $"default case at cell {cell}"));
                var cases = new SwShAmxSwitchCase[caseCount];
                for (var index = 0; index < caseCount; index++)
                {
                    var valueCell = checked(cell + 3 + (index * 2));
                    var value = unchecked((long)cells[valueCell]);
                    var destinationRaw = unchecked((long)cells[valueCell + 1]);
                    var destination = isIndirect
                        ? SwShAmxOperand.Literal(destinationRaw)
                        : SwShAmxOperand.UnresolvedCodeTarget(
                            ResolveRelativeTarget(valueCell, destinationRaw, $"case {value} at cell {cell}"));
                    cases[index] = new SwShAmxSwitchCase(value, destination);
                }

                instructions.Add(SwShAmxInstruction.Parsed(
                    definition,
                    [],
                    defaultDestination,
                    cases,
                    cell));
                cell = checked(cell + 3 + (caseCount * 2));
                continue;
            }

            if (definition.Encoding == SwShAmxOpcodeEncoding.Packed)
            {
                var packedValue = unchecked((int)(uint)(rawInstruction >> 32));
                instructions.Add(SwShAmxInstruction.Parsed(
                    definition,
                    [SwShAmxOperand.Literal(packedValue)],
                    null,
                    [],
                    cell));
                cell++;
                continue;
            }

            if (definition.OperandCount > cells.Length - cell - 1)
            {
                throw new InvalidDataException($"Sword/Shield AMX {definition.Mnemonic} at cell {cell} is truncated.");
            }

            var operands = new SwShAmxOperand[definition.OperandCount];
            for (var index = 0; index < operands.Length; index++)
            {
                var value = unchecked((long)cells[cell + 1 + index]);
                operands[index] = definition.Encoding == SwShAmxOpcodeEncoding.Relative
                    ? SwShAmxOperand.UnresolvedCodeTarget(
                        ResolveRelativeTarget(cell, value, $"{definition.Mnemonic} at cell {cell}"))
                    : SwShAmxOperand.Literal(value);
            }

            instructions.Add(SwShAmxInstruction.Parsed(definition, operands, null, [], cell));
            cell = checked(cell + 1 + operands.Length);
        }

        return instructions.ToArray();
    }

    private static ulong[] ExpandBody(byte[] bytes, SwShAmxHeader header)
    {
        var expandedLength = header.HeapOffset - header.CodeOffset;
        if (expandedLength < 0
            || expandedLength > MaxExpandedBytes
            || expandedLength % CellSize != 0)
        {
            throw new InvalidDataException(
                $"Sword/Shield AMX expanded body length 0x{expandedLength:X} is unsafe or unaligned.");
        }

        var expandedBytes = new byte[expandedLength];
        if (!header.IsCompact)
        {
            bytes.AsSpan(header.CodeOffset, expandedLength).CopyTo(expandedBytes);
            return ReadCells(expandedBytes);
        }

        var compactLength = header.Size - header.CodeOffset;
        if (compactLength < 0 || expandedLength > (long)compactLength * CellSize)
        {
            throw new InvalidDataException("Sword/Shield AMX compact body requests an unsafe expansion ratio.");
        }

        var source = compactLength;
        var destination = expandedLength;
        while (source > 0)
        {
            ulong value = 0;
            var shift = 0;
            var signSource = 0;
            do
            {
                source--;
                signSource = header.CodeOffset + source;
                var current = bytes[signSource];
                if (shift >= 70)
                {
                    throw new InvalidDataException("Sword/Shield AMX compact cell exceeds the 64-bit encoding bound.");
                }

                value |= (ulong)(current & 0x7F) << shift;
                shift += 7;
            }
            while (source > 0 && (bytes[header.CodeOffset + source - 1] & 0x80) != 0);

            if ((bytes[signSource] & 0x40) != 0)
            {
                while (shift < 64)
                {
                    value |= 0xFFUL << shift;
                    shift += 8;
                }
            }

            destination -= CellSize;
            if (destination < 0)
            {
                throw new InvalidDataException("Sword/Shield AMX compact body expands beyond HEA.");
            }

            BinaryPrimitives.WriteUInt64LittleEndian(expandedBytes.AsSpan(destination, CellSize), value);
        }

        if (destination != 0)
        {
            throw new InvalidDataException(
                $"Sword/Shield AMX compact body ended with 0x{destination:X} expanded bytes unwritten.");
        }

        return ReadCells(expandedBytes);
    }

    private static ulong[] ReadCells(byte[] bytes)
    {
        var cells = new ulong[bytes.Length / CellSize];
        for (var index = 0; index < cells.Length; index++)
        {
            cells[index] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(index * CellSize, CellSize));
        }

        return cells;
    }

    private static int ResolveRelativeTarget(int baseCell, long relativeByteOffset, string label)
    {
        if (relativeByteOffset % CellSize != 0)
        {
            throw new InvalidDataException(
                $"Sword/Shield AMX {label} has unaligned relative byte offset {relativeByteOffset}.");
        }

        return checked(baseCell + (int)(relativeByteOffset / CellSize));
    }

    private static int ResolveCodeByteOffset(int byteOffset, string label)
    {
        if (byteOffset < 0 || byteOffset % CellSize != 0)
        {
            throw new InvalidDataException($"Sword/Shield AMX {label} byte offset {byteOffset} is invalid.");
        }

        return byteOffset / CellSize;
    }

    private static void RequireInstructionBoundary(
        IReadOnlyDictionary<int, SwShAmxInstruction> instructionsByCell,
        int cell,
        string label)
    {
        if (!instructionsByCell.ContainsKey(cell))
        {
            throw new InvalidDataException(
                $"Sword/Shield AMX {label} points to cell {cell}, which is not an instruction boundary.");
        }
    }
}

internal sealed record SwShAmxPublicTarget(int DefinitionOffset, int OriginalCell);
