// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace KM.Formats.SwSh;

public sealed class SwShAmxAssembler
{
    private const int CellSize = sizeof(long);

    private readonly SwShAmxDocument source;
    private readonly List<SwShAmxInstruction> instructions;
    private readonly ReadOnlyCollection<SwShAmxInstruction> readOnlyInstructions;
    private readonly List<uint> nativeHashes;
    private readonly ReadOnlyCollection<uint> readOnlyNativeHashes;

    internal SwShAmxAssembler(SwShAmxDocument source)
    {
        this.source = source;
        instructions = source.Instructions
            .Select(instruction => instruction.CloneUnresolved())
            .ToList();
        var instructionsByCell = instructions.ToDictionary(
            instruction => instruction.OriginalCell
                ?? throw new InvalidDataException("Source AMX instruction is missing its original cell."));
        foreach (var instruction in instructions)
        {
            instruction.ResolveTargets(instructionsByCell);
        }

        nativeHashes = source.NativeHashes.ToList();
        readOnlyInstructions = instructions.AsReadOnly();
        readOnlyNativeHashes = nativeHashes.AsReadOnly();
    }

    public IReadOnlyList<SwShAmxInstruction> Instructions => readOnlyInstructions;

    public IReadOnlyList<uint> NativeHashes => readOnlyNativeHashes;

    public SwShAmxInstruction GetInstructionAtOriginalCell(int cell)
    {
        return instructions.SingleOrDefault(instruction => instruction.OriginalCell == cell)
            ?? throw new KeyNotFoundException($"No Sword/Shield AMX instruction originated at cell {cell}.");
    }

    public int GetOrAddNative(uint nameHash)
    {
        var existing = nativeHashes.IndexOf(nameHash);
        if (existing >= 0)
        {
            return existing;
        }

        nativeHashes.Add(nameHash);
        return nativeHashes.Count - 1;
    }

    public int GetOrAddNative(ReadOnlySpan<char> name)
    {
        return GetOrAddNative(SwShAmxNativeNameHash.Compute(name));
    }

    public void InsertBefore(
        SwShAmxInstruction anchor,
        params SwShAmxInstruction[] newInstructions)
    {
        Insert(anchor, newInstructions, after: false);
    }

    public void InsertAfter(
        SwShAmxInstruction anchor,
        params SwShAmxInstruction[] newInstructions)
    {
        Insert(anchor, newInstructions, after: true);
    }

    public SwShAmxInstruction ReplaceLiteralOperand(
        SwShAmxInstruction instruction,
        int operandIndex,
        long expectedValue,
        long replacementValue)
    {
        RequireOwnedInstruction(instruction, nameof(instruction));
        var replacement = instruction.CloneReplacingLiteral(operandIndex, expectedValue, replacementValue);
        ReplaceInstruction(instruction, replacement);
        return replacement;
    }

    public SwShAmxInstruction AddSwitchCase(
        SwShAmxInstruction switchTable,
        long caseValue,
        SwShAmxInstruction target)
    {
        RequireOwnedInstruction(switchTable, nameof(switchTable));
        RequireOwnedInstruction(target, nameof(target));
        var replacement = switchTable.CloneAddingSwitchCase(caseValue, target);
        ReplaceInstruction(switchTable, replacement);
        return replacement;
    }

    public byte[] Assemble()
    {
        if (source.TrailingBytes.Length != 0)
        {
            throw new InvalidDataException(
                "Sword/Shield AMX images with trailing metadata are parsed read-only; structural assembly is disabled because metadata relocation is not proven.");
        }

        if ((source.Header.Flags & SwShAmxHeader.DebugFlag) != 0)
        {
            throw new InvalidDataException(
                "Sword/Shield AMX debug tables are not relocated by the deterministic assembler.");
        }

        var instructionSet = new HashSet<SwShAmxInstruction>(instructions, ReferenceEqualityComparer.Instance);
        if (instructionSet.Count != instructions.Count)
        {
            throw new InvalidDataException("Sword/Shield AMX assembly contains the same instruction object more than once.");
        }

        ValidateTargets(instructionSet);
        var cellsByInstruction = BuildLayout();
        var codeCells = EncodeInstructions(cellsByInstruction);
        var prefix = BuildPrefix(out var definitionOffsetDelta);

        var codeOffset = prefix.Length;
        var codeByteLength = checked(codeCells.Count * CellSize);
        var dataOffset = checked(codeOffset + codeByteLength);
        var dataByteLength = checked(source.RawDataCells.Length * CellSize);
        var heapOffset = checked(dataOffset + dataByteLength);
        var stackSize = checked(source.Header.StackTop - source.Header.HeapOffset);
        var stackTop = checked(heapOffset + stackSize);
        var entryPoint = source.EntryPointCell is { } entryCell
            ? checked(cellsByInstruction[GetInstructionAtOriginalCell(entryCell)] * CellSize)
            : -1;

        RelocatePublics(prefix, cellsByInstruction);
        WriteHeader(
            prefix,
            size: 0,
            codeOffset,
            dataOffset,
            heapOffset,
            stackTop,
            entryPoint,
            checked(source.Header.LibrariesOffset + definitionOffsetDelta),
            checked(source.Header.PublicVariablesOffset + definitionOffsetDelta),
            checked(source.Header.TagsOffset + definitionOffsetDelta),
            checked(source.Header.NameTableOffset + definitionOffsetDelta));

        var bodyCells = new List<ulong>(checked(codeCells.Count + source.RawDataCells.Length));
        bodyCells.AddRange(codeCells);
        bodyCells.AddRange(source.RawDataCells);
        var body = source.Header.IsCompact ? Compact(bodyCells) : Expand(bodyCells);
        var result = new byte[checked(prefix.Length + body.Length)];
        prefix.CopyTo(result, 0);
        body.CopyTo(result, prefix.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x00, sizeof(int)), result.Length);

        var verification = SwShAmxDocument.Parse(result);
        if (!verification.NativeHashes.SequenceEqual(nativeHashes)
            || !verification.DataCells.SequenceEqual(source.DataCells))
        {
            throw new InvalidDataException("Sword/Shield AMX assembly failed its semantic round-trip check.");
        }

        return result;
    }

    private void Insert(
        SwShAmxInstruction anchor,
        IReadOnlyList<SwShAmxInstruction> newInstructions,
        bool after)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(newInstructions);
        var anchorIndex = instructions.IndexOf(anchor);
        if (anchorIndex < 0)
        {
            throw new ArgumentException("The AMX insertion anchor does not belong to this assembler.", nameof(anchor));
        }

        if (newInstructions.Count == 0)
        {
            return;
        }

        var distinct = new HashSet<SwShAmxInstruction>(newInstructions, ReferenceEqualityComparer.Instance);
        if (distinct.Count != newInstructions.Count
            || newInstructions.Any(instruction => instruction is null || instructions.Contains(instruction)))
        {
            throw new ArgumentException(
                "Inserted AMX instructions must be non-null, distinct objects that are not already in the program.",
                nameof(newInstructions));
        }

        instructions.InsertRange(after ? anchorIndex + 1 : anchorIndex, newInstructions);
    }

    private void ReplaceInstruction(
        SwShAmxInstruction oldInstruction,
        SwShAmxInstruction replacement)
    {
        var index = instructions.IndexOf(oldInstruction);
        if (index < 0)
        {
            throw new ArgumentException("The AMX instruction does not belong to this assembler.", nameof(oldInstruction));
        }

        replacement.Retarget(oldInstruction, replacement);
        foreach (var instruction in instructions)
        {
            instruction.Retarget(oldInstruction, replacement);
        }

        instructions[index] = replacement;
    }

    private Dictionary<SwShAmxInstruction, int> BuildLayout()
    {
        var layout = new Dictionary<SwShAmxInstruction, int>(ReferenceEqualityComparer.Instance);
        var cell = 0;
        foreach (var instruction in instructions)
        {
            layout.Add(instruction, cell);
            cell = checked(cell + instruction.EncodedCellCount);
        }

        return layout;
    }

    private List<ulong> EncodeInstructions(
        IReadOnlyDictionary<SwShAmxInstruction, int> cellsByInstruction)
    {
        var cells = new List<ulong>(instructions.Sum(instruction => instruction.EncodedCellCount));
        foreach (var instruction in instructions)
        {
            var instructionCell = cellsByInstruction[instruction];
            if (instruction.IsSwitchTable)
            {
                cells.Add(unchecked((uint)instruction.Opcode));
                cells.Add(unchecked((ulong)instruction.SwitchCases.Count));
                cells.Add(EncodeDestination(
                    instruction.DefaultDestination!,
                    instruction.IsIndirectSwitchTable,
                    instructionCell + 1,
                    cellsByInstruction));
                for (var index = 0; index < instruction.SwitchCases.Count; index++)
                {
                    var @case = instruction.SwitchCases[index];
                    cells.Add(unchecked((ulong)@case.Value));
                    cells.Add(EncodeDestination(
                        @case.Destination,
                        instruction.IsIndirectSwitchTable,
                        instructionCell + 3 + (index * 2),
                        cellsByInstruction));
                }

                continue;
            }

            if (instruction.Encoding == SwShAmxOpcodeEncoding.Packed)
            {
                var value = instruction.Operands[0].LiteralValue;
                if (value < int.MinValue || value > uint.MaxValue)
                {
                    throw new InvalidDataException(
                        $"Sword/Shield AMX packed operand {value} for {instruction.Mnemonic} does not fit 32 bits.");
                }

                cells.Add(((ulong)unchecked((uint)value) << 32) | unchecked((uint)instruction.Opcode));
                continue;
            }

            cells.Add(unchecked((uint)instruction.Opcode));
            foreach (var operand in instruction.Operands)
            {
                cells.Add(operand.Kind == SwShAmxOperandKind.Literal
                    ? unchecked((ulong)operand.LiteralValue)
                    : EncodeRelativeTarget(instructionCell, operand.Target, cellsByInstruction));
            }
        }

        return cells;
    }

    private byte[] BuildPrefix(out int definitionOffsetDelta)
    {
        var addedNativeCount = nativeHashes.Count - source.NativeHashes.Count;
        if (addedNativeCount < 0)
        {
            throw new InvalidOperationException("Sword/Shield AMX native definitions cannot be removed.");
        }

        if (addedNativeCount == 0)
        {
            definitionOffsetDelta = 0;
            return source.Prefix.ToArray();
        }

        var insertedLength = checked(addedNativeCount * source.Header.DefinitionSize);
        var withoutAlignment = new byte[checked(source.Prefix.Length + insertedLength)];
        source.Prefix.AsSpan(0, source.Header.LibrariesOffset).CopyTo(withoutAlignment);
        source.Prefix.AsSpan(source.Header.LibrariesOffset).CopyTo(
            withoutAlignment.AsSpan(source.Header.LibrariesOffset + insertedLength));

        for (var index = 0; index < addedNativeCount; index++)
        {
            var recordOffset = checked(
                source.Header.LibrariesOffset + (index * source.Header.DefinitionSize));
            var hash = nativeHashes[source.NativeHashes.Count + index];
            BinaryPrimitives.WriteUInt32LittleEndian(
                withoutAlignment.AsSpan(recordOffset + sizeof(ulong), sizeof(uint)),
                hash);
        }

        var alignedLength = Align(withoutAlignment.Length, CellSize);
        var prefix = new byte[alignedLength];
        withoutAlignment.CopyTo(prefix, 0);
        definitionOffsetDelta = insertedLength;
        return prefix;
    }

    private void RelocatePublics(
        byte[] prefix,
        IReadOnlyDictionary<SwShAmxInstruction, int> cellsByInstruction)
    {
        foreach (var publicTarget in source.PublicTargets)
        {
            var target = GetInstructionAtOriginalCell(publicTarget.OriginalCell);
            var byteOffset = checked((ulong)cellsByInstruction[target] * CellSize);
            BinaryPrimitives.WriteUInt64LittleEndian(
                prefix.AsSpan(publicTarget.DefinitionOffset, sizeof(ulong)),
                byteOffset);
        }
    }

    private void WriteHeader(
        byte[] prefix,
        int size,
        int codeOffset,
        int dataOffset,
        int heapOffset,
        int stackTop,
        int entryPoint,
        int librariesOffset,
        int publicVariablesOffset,
        int tagsOffset,
        int nameTableOffset)
    {
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x00), size);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x0C), codeOffset);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x10), dataOffset);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x14), heapOffset);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x18), stackTop);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x1C), entryPoint);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x28), librariesOffset);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x2C), publicVariablesOffset);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x30), tagsOffset);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(0x34), nameTableOffset);
    }

    private static ulong EncodeDestination(
        SwShAmxOperand destination,
        bool isIndirect,
        int baseCell,
        IReadOnlyDictionary<SwShAmxInstruction, int> cellsByInstruction)
    {
        if (isIndirect)
        {
            return unchecked((ulong)destination.LiteralValue);
        }

        return EncodeRelativeTarget(baseCell, destination.Target, cellsByInstruction);
    }

    private static ulong EncodeRelativeTarget(
        int baseCell,
        SwShAmxInstruction target,
        IReadOnlyDictionary<SwShAmxInstruction, int> cellsByInstruction)
    {
        var relativeCells = checked(cellsByInstruction[target] - baseCell);
        var relativeBytes = checked((long)relativeCells * CellSize);
        return unchecked((ulong)relativeBytes);
    }

    private void ValidateTargets(IReadOnlySet<SwShAmxInstruction> instructionSet)
    {
        foreach (var instruction in instructions)
        {
            foreach (var operand in instruction.Operands)
            {
                ValidateTarget(operand, instructionSet, instruction.Mnemonic);
            }

            if (instruction.DefaultDestination is not null)
            {
                ValidateTarget(instruction.DefaultDestination, instructionSet, instruction.Mnemonic);
            }

            foreach (var @case in instruction.SwitchCases)
            {
                ValidateTarget(@case.Destination, instructionSet, instruction.Mnemonic);
            }
        }
    }

    private static void ValidateTarget(
        SwShAmxOperand operand,
        IReadOnlySet<SwShAmxInstruction> instructionSet,
        string mnemonic)
    {
        if (operand.Kind == SwShAmxOperandKind.CodeTarget && !instructionSet.Contains(operand.Target))
        {
            throw new InvalidDataException(
                $"Sword/Shield AMX {mnemonic} targets an instruction that is not in the assembled program.");
        }
    }

    private void RequireOwnedInstruction(SwShAmxInstruction instruction, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (!instructions.Contains(instruction))
        {
            throw new ArgumentException("The AMX instruction does not belong to this assembler.", parameterName);
        }
    }

    private static byte[] Expand(IReadOnlyList<ulong> cells)
    {
        var bytes = new byte[checked(cells.Count * CellSize)];
        for (var index = 0; index < cells.Count; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(index * CellSize, CellSize),
                cells[index]);
        }

        return bytes;
    }

    private static byte[] Compact(IReadOnlyList<ulong> cells)
    {
        var bytes = new List<byte>(cells.Count * 2);
        foreach (var cell in cells)
        {
            CompactCell(cell, bytes);
        }

        return bytes.ToArray();
    }

    private static void CompactCell(ulong cell, ICollection<byte> destination)
    {
        var chunks = new List<byte>(10);
        var value = unchecked((long)cell);
        while (true)
        {
            var payload = (byte)(value & 0x7F);
            chunks.Add(payload);
            value >>= 7;
            var signBitSet = (payload & 0x40) != 0;
            if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
            {
                break;
            }
        }

        for (var index = chunks.Count - 1; index >= 0; index--)
        {
            var current = chunks[index];
            if (index != 0)
            {
                current |= 0x80;
            }

            destination.Add(current);
        }
    }

    private static int Align(int value, int alignment)
    {
        return checked((value + alignment - 1) & -alignment);
    }
}
