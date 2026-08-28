// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using K4os.Compression.LZ4;

namespace KM.Formats.Executable;

/// <summary>
/// Converts one bounded, position-independent AArch64 ELF image into the
/// canonical three-segment NSO form used by KM guest modules.
/// </summary>
public static class Elf64NsoBuilder
{
    public const int MaximumElfBytes = 16 * 1024 * 1024;
    public const int MaximumSegmentBytes = 8 * 1024 * 1024;
    public const int MaximumImageBytes = 32 * 1024 * 1024;
    public const int MaximumNsoBytes = 32 * 1024 * 1024;
    public const int MaximumRelocationCount = 65_536;

    private const int ElfHeaderSize = 0x40;
    private const int ProgramHeaderSize = 0x38;
    private const int SectionHeaderSize = 0x40;
    private const int DynamicEntrySize = 0x10;
    private const int RelaEntrySize = 0x18;
    private const int SymbolEntrySize = 0x18;
    private const int MaximumDynamicBytes = 64 * 1024;
    private const int MaximumHashEntries = 65_536;
    private const ushort ElfTypeDynamic = 3;
    private const ushort MachineAarch64 = 183;
    private const uint ProgramHeaderLoad = 1;
    private const uint ProgramHeaderDynamic = 2;
    private const uint SectionTypeNote = 7;
    private const uint GnuBuildIdType = 3;
    private const uint RelativeRelocationType = 1_027;
    private const long DynamicNull = 0;
    private const long DynamicNeeded = 1;
    private const long DynamicPltRelocationSize = 2;
    private const long DynamicHash = 4;
    private const long DynamicStringTable = 5;
    private const long DynamicSymbolTable = 6;
    private const long DynamicRela = 7;
    private const long DynamicRelaSize = 8;
    private const long DynamicRelaEntrySize = 9;
    private const long DynamicStringTableSize = 10;
    private const long DynamicSymbolEntrySize = 11;
    private const long DynamicRel = 17;
    private const long DynamicRelSize = 18;
    private const long DynamicRelEntrySize = 19;
    private const long DynamicPltRelocationKind = 20;
    private const long DynamicTextRelocations = 22;
    private const long DynamicJumpRelocations = 23;
    private const long DynamicRelrSize = 35;
    private const long DynamicRelr = 36;
    private const long DynamicRelrEntrySize = 37;
    private const long DynamicGnuHash = 0x6FFFFEF5;
    private const long DynamicRelaCount = 0x6FFFFFF9;

    private static readonly NsoFlags RequiredNsoFlags =
        NsoFlags.CompressedText
        | NsoFlags.CompressedRo
        | NsoFlags.CompressedData
        | NsoFlags.CheckHashText
        | NsoFlags.CheckHashRo
        | NsoFlags.CheckHashData;

    public static byte[] Build(ReadOnlyMemory<byte> elfBytes)
    {
        var image = ParseElf(elfBytes.Span);
        var compressed = image.Loads.Select(Compress).ToArray();
        var outputLength = checked(NsoFile.HeaderSize + compressed.Sum(segment => segment.Length));
        var output = new byte[outputLength];
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x0C), (uint)RequiredNsoFlags);
        image.BuildId.CopyTo(output.AsSpan(0x40, 0x20));

        var fileCursor = NsoFile.HeaderSize;
        for (var index = 0; index < image.Loads.Count; index++)
        {
            var load = image.Loads[index];
            var headerOffset = 0x10 + index * 0x10;
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(headerOffset), fileCursor);
            BinaryPrimitives.WriteInt32LittleEndian(
                output.AsSpan(headerOffset + 0x04),
                load.VirtualAddress);
            BinaryPrimitives.WriteInt32LittleEndian(
                output.AsSpan(headerOffset + 0x08),
                load.FileSize);
            BinaryPrimitives.WriteInt32LittleEndian(
                output.AsSpan(0x60 + index * sizeof(uint)),
                compressed[index].Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                output.AsSpan(headerOffset + 0x0C),
                index == 2 ? checked(load.MemorySize - load.FileSize) : 1);

            SHA256.HashData(load.Bytes).CopyTo(output.AsSpan(0xA0 + index * 0x20, 0x20));
            compressed[index].CopyTo(output.AsSpan(fileCursor));
            fileCursor += compressed[index].Length;
        }

        if (fileCursor != output.Length)
        {
            throw new InvalidDataException("The generated guest module NSO length is inconsistent.");
        }

        VerifyPackedNso(output, image);
        return output;
    }

    /// <summary>
    /// Revalidates both an ELF and its packed NSO without trusting the packer's
    /// in-memory result. Build scripts use this after reading the final file.
    /// </summary>
    public static void Verify(ReadOnlyMemory<byte> elfBytes, ReadOnlyMemory<byte> nsoBytes)
    {
        var image = ParseElf(elfBytes.Span);
        VerifyPackedNso(nsoBytes, image);
    }

    private static ElfImage ParseElf(ReadOnlySpan<byte> elf)
    {
        if (elf.Length is < ElfHeaderSize or > MaximumElfBytes)
        {
            throw new InvalidDataException("The guest module ELF is empty or exceeds its bounded size.");
        }

        ValidateHeader(elf);
        var programHeaderOffset = ReadBoundedOffset(elf, 0x20, "program header table");
        var sectionHeaderOffset = ReadBoundedOffset(elf, 0x28, "section header table");
        var programHeaderEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(elf[0x36..]);
        var programHeaderCount = BinaryPrimitives.ReadUInt16LittleEndian(elf[0x38..]);
        var sectionHeaderEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(elf[0x3A..]);
        var sectionHeaderCount = BinaryPrimitives.ReadUInt16LittleEndian(elf[0x3C..]);
        if (programHeaderEntrySize != ProgramHeaderSize
            || programHeaderCount != 4
            || sectionHeaderEntrySize != SectionHeaderSize
            || sectionHeaderCount is < 1 or > 4096)
        {
            throw new InvalidDataException(
                "The guest module ELF must contain the canonical four-entry program table and a bounded section table.");
        }

        EnsureRange(
            elf.Length,
            programHeaderOffset,
            checked(programHeaderCount * ProgramHeaderSize),
            "program header table");
        EnsureRange(
            elf.Length,
            sectionHeaderOffset,
            checked(sectionHeaderCount * SectionHeaderSize),
            "section header table");

        var programs = new ElfProgramSegment[programHeaderCount];
        for (var index = 0; index < programs.Length; index++)
        {
            programs[index] = ReadProgramSegment(
                elf,
                checked(programHeaderOffset + index * ProgramHeaderSize));
        }

        if (programs[0].Type != ProgramHeaderLoad
            || programs[1].Type != ProgramHeaderLoad
            || programs[2].Type != ProgramHeaderLoad
            || programs[3].Type != ProgramHeaderDynamic)
        {
            throw new InvalidDataException(
                "The guest module ELF must contain exactly RX, R, and RW PT_LOAD entries followed by PT_DYNAMIC.");
        }

        var loads = new List<ElfLoadSegment>(3);
        for (var index = 0; index < 3; index++)
        {
            var program = programs[index];
            if (program.FileSize is < 1 or > MaximumSegmentBytes
                || program.MemorySize is < 1 or > MaximumSegmentBytes
                || program.FileSize > program.MemorySize)
            {
                throw new InvalidDataException(
                    "A guest module ELF load segment is empty, oversized, or has invalid file and memory sizes.");
            }

            ValidateLoadAlignment(program);
            EnsureRange(elf.Length, program.FileOffset, program.FileSize, "load segment");
            loads.Add(new ElfLoadSegment(
                program.Flags,
                program.FileOffset,
                program.VirtualAddress,
                program.FileSize,
                program.MemorySize,
                elf.Slice(program.FileOffset, program.FileSize).ToArray()));
        }

        ValidateLoadLayout(loads);
        ValidateDynamicProgram(programs[3], loads[2]);
        ValidateDynamicMetadata(elf, loads, programs[3]);
        var buildId = ReadBuildId(elf, sectionHeaderOffset, sectionHeaderCount);
        return new ElfImage(loads, buildId);
    }

    private static void ValidateHeader(ReadOnlySpan<byte> elf)
    {
        if (!elf[..4].SequenceEqual([(byte)0x7F, (byte)'E', (byte)'L', (byte)'F'])
            || elf[4] != 2
            || elf[5] != 1
            || elf[6] != 1
            || elf[7] != 0
            || elf[8] != 0
            || !IsAllZero(elf[9..0x10])
            || BinaryPrimitives.ReadUInt16LittleEndian(elf[0x10..]) != ElfTypeDynamic
            || BinaryPrimitives.ReadUInt16LittleEndian(elf[0x12..]) != MachineAarch64
            || BinaryPrimitives.ReadUInt32LittleEndian(elf[0x14..]) != 1
            || BinaryPrimitives.ReadUInt64LittleEndian(elf[0x18..]) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(elf[0x30..]) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(elf[0x34..]) != ElfHeaderSize)
        {
            throw new InvalidDataException(
                "The guest module must be an entry-zero, System V AArch64 ET_DYN ELF image.");
        }
    }

    private static ElfProgramSegment ReadProgramSegment(ReadOnlySpan<byte> elf, int offset)
    {
        var type = BinaryPrimitives.ReadUInt32LittleEndian(elf[offset..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(elf[(offset + 0x04)..]);
        var fileOffset = ReadBoundedOffset(elf, offset + 0x08, "program segment");
        var virtualAddress = ReadBoundedOffset(elf, offset + 0x10, "program virtual address");
        var physicalAddress = ReadBoundedOffset(elf, offset + 0x18, "program physical address");
        var fileSize = ReadBoundedLength(elf, offset + 0x20, "program segment");
        var memorySize = ReadBoundedLength(elf, offset + 0x28, "program memory");
        var alignment = ReadBoundedLength(elf, offset + 0x30, "program alignment");
        if (physicalAddress != virtualAddress)
        {
            throw new InvalidDataException(
                "A guest module ELF program segment has different physical and virtual addresses.");
        }

        return new ElfProgramSegment(
            type,
            flags,
            fileOffset,
            virtualAddress,
            fileSize,
            memorySize,
            alignment);
    }

    private static void ValidateLoadAlignment(ElfProgramSegment load)
    {
        if (load.Alignment < 0x1000
            || !IsPowerOfTwo(load.Alignment)
            || load.VirtualAddress % 0x1000 != 0
            || load.FileOffset % 0x1000 != 0
            || load.VirtualAddress % load.Alignment != load.FileOffset % load.Alignment)
        {
            throw new InvalidDataException("A guest module ELF PT_LOAD has a noncanonical alignment.");
        }
    }

    private static void ValidateLoadLayout(IReadOnlyList<ElfLoadSegment> loads)
    {
        if (loads[0].Flags != 5
            || loads[1].Flags != 4
            || loads[2].Flags != 6
            || loads[0].VirtualAddress != 0)
        {
            throw new InvalidDataException(
                "The guest module ELF must contain canonical RX, R, and RW load permissions and start at address zero.");
        }

        for (var index = 1; index < loads.Count; index++)
        {
            var previous = loads[index - 1];
            var current = loads[index];
            if (current.VirtualAddress < checked(previous.VirtualAddress + previous.MemorySize)
                || current.FileOffset < checked(previous.FileOffset + previous.FileSize))
            {
                throw new InvalidDataException(
                    "The guest module ELF PT_LOAD file or memory ranges overlap or are out of canonical order.");
            }
        }

        if (checked(loads[^1].VirtualAddress + loads[^1].MemorySize) > MaximumImageBytes)
        {
            throw new InvalidDataException("The guest module ELF memory image exceeds its bounded size.");
        }
    }

    private static void ValidateDynamicProgram(
        ElfProgramSegment dynamic,
        ElfLoadSegment writableLoad)
    {
        if (dynamic.Flags != 6
            || dynamic.FileSize is < DynamicEntrySize or > MaximumDynamicBytes
            || dynamic.FileSize != dynamic.MemorySize
            || dynamic.FileSize % DynamicEntrySize != 0
            || dynamic.Alignment < 8
            || !IsPowerOfTwo(dynamic.Alignment)
            || dynamic.VirtualAddress % dynamic.Alignment != dynamic.FileOffset % dynamic.Alignment
            || !ContainsRange(
                writableLoad.FileOffset,
                writableLoad.FileSize,
                dynamic.FileOffset,
                dynamic.FileSize)
            || !ContainsRange(
                writableLoad.VirtualAddress,
                writableLoad.FileSize,
                dynamic.VirtualAddress,
                dynamic.MemorySize))
        {
            throw new InvalidDataException(
                "The guest module ELF PT_DYNAMIC must be bounded, aligned, and fully contained in the file-backed RW load segment.");
        }
    }

    private static void ValidateDynamicMetadata(
        ReadOnlySpan<byte> elf,
        IReadOnlyList<ElfLoadSegment> loads,
        ElfProgramSegment dynamic)
    {
        var tags = ReadDynamicTags(elf, dynamic);
        if (tags.ContainsKey(DynamicNeeded))
        {
            throw new InvalidDataException("The guest module ELF must not contain DT_NEEDED dependencies.");
        }

        foreach (var forbiddenTag in new[]
                 {
                     DynamicPltRelocationSize,
                     DynamicRel,
                     DynamicRelSize,
                     DynamicRelEntrySize,
                     DynamicPltRelocationKind,
                     DynamicTextRelocations,
                     DynamicJumpRelocations,
                     DynamicRelrSize,
                     DynamicRelr,
                     DynamicRelrEntrySize,
                 })
        {
            if (tags.ContainsKey(forbiddenTag))
            {
                throw new InvalidDataException(
                    $"The guest module ELF contains unsupported dynamic relocation tag 0x{forbiddenTag:X}.");
            }
        }

        ValidateDynamicSymbols(elf, loads, tags);
        ValidateRelativeRelocations(elf, loads, tags);
    }

    private static Dictionary<long, ulong> ReadDynamicTags(
        ReadOnlySpan<byte> elf,
        ElfProgramSegment dynamic)
    {
        var tags = new Dictionary<long, ulong>();
        var terminated = false;
        for (var cursor = dynamic.FileOffset;
             cursor < dynamic.FileOffset + dynamic.FileSize;
             cursor += DynamicEntrySize)
        {
            var tag = BinaryPrimitives.ReadInt64LittleEndian(elf[cursor..]);
            var value = BinaryPrimitives.ReadUInt64LittleEndian(elf[(cursor + 8)..]);
            if (terminated)
            {
                if (tag != DynamicNull || value != 0)
                {
                    throw new InvalidDataException(
                        "The guest module ELF PT_DYNAMIC contains data after its DT_NULL terminator.");
                }

                continue;
            }

            if (tag == DynamicNull)
            {
                if (value != 0)
                {
                    throw new InvalidDataException("The guest module ELF DT_NULL value is not zero.");
                }

                terminated = true;
                continue;
            }

            if (!tags.TryAdd(tag, value))
            {
                throw new InvalidDataException(
                    $"The guest module ELF contains duplicate dynamic tag 0x{tag:X}.");
            }
        }

        if (!terminated)
        {
            throw new InvalidDataException("The guest module ELF PT_DYNAMIC is not terminated.");
        }

        return tags;
    }

    private static void ValidateDynamicSymbols(
        ReadOnlySpan<byte> elf,
        IReadOnlyList<ElfLoadSegment> loads,
        IReadOnlyDictionary<long, ulong> tags)
    {
        var hashAddress = GetRequiredDynamicValue(tags, DynamicHash, "DT_HASH");
        var hashOffset = MapVirtualFileRange(elf, loads, hashAddress, 8, "DT_HASH");
        var bucketCount = BinaryPrimitives.ReadUInt32LittleEndian(elf[hashOffset..]);
        var chainCount = BinaryPrimitives.ReadUInt32LittleEndian(elf[(hashOffset + 4)..]);
        if (bucketCount is < 1 or > MaximumHashEntries || chainCount != 1)
        {
            throw new InvalidDataException(
                "The guest module ELF dynamic hash must describe only the mandatory null symbol.");
        }

        var hashLength = checked(8 + checked((int)(bucketCount + chainCount)) * sizeof(uint));
        hashOffset = MapVirtualFileRange(elf, loads, hashAddress, hashLength, "DT_HASH");
        if (!IsAllZero(elf.Slice(hashOffset + 8, hashLength - 8)))
        {
            throw new InvalidDataException(
                "The guest module ELF dynamic hash exposes a symbol beyond the null entry.");
        }

        var symbolEntrySize = ReadBoundedDynamicLength(
            GetRequiredDynamicValue(tags, DynamicSymbolEntrySize, "DT_SYMENT"),
            SymbolEntrySize,
            "DT_SYMENT");
        if (symbolEntrySize != SymbolEntrySize)
        {
            throw new InvalidDataException("The guest module ELF DT_SYMENT is not ELF64-sized.");
        }

        var symbolAddress = GetRequiredDynamicValue(tags, DynamicSymbolTable, "DT_SYMTAB");
        var symbolOffset = MapVirtualFileRange(
            elf,
            loads,
            symbolAddress,
            SymbolEntrySize,
            "DT_SYMTAB");
        if (!IsAllZero(elf.Slice(symbolOffset, SymbolEntrySize)))
        {
            throw new InvalidDataException(
                "The guest module ELF contains an imported or exported dynamic symbol.");
        }

        var stringTableSize = ReadBoundedDynamicLength(
            GetRequiredDynamicValue(tags, DynamicStringTableSize, "DT_STRSZ"),
            MaximumDynamicBytes,
            "DT_STRSZ");
        if (stringTableSize != 1)
        {
            throw new InvalidDataException(
                "The guest module ELF dynamic string table is not the single null byte required by a symbol-free image.");
        }

        var stringAddress = GetRequiredDynamicValue(tags, DynamicStringTable, "DT_STRTAB");
        var stringOffset = MapVirtualFileRange(elf, loads, stringAddress, 1, "DT_STRTAB");
        if (elf[stringOffset] != 0)
        {
            throw new InvalidDataException("The guest module ELF dynamic string table is malformed.");
        }

        if (tags.TryGetValue(DynamicGnuHash, out var gnuHashAddress))
        {
            ValidateGnuHash(elf, loads, gnuHashAddress);
        }
    }

    private static void ValidateGnuHash(
        ReadOnlySpan<byte> elf,
        IReadOnlyList<ElfLoadSegment> loads,
        ulong address)
    {
        var offset = MapVirtualFileRange(elf, loads, address, 16, "DT_GNU_HASH");
        var bucketCount = BinaryPrimitives.ReadUInt32LittleEndian(elf[offset..]);
        var symbolOffset = BinaryPrimitives.ReadUInt32LittleEndian(elf[(offset + 4)..]);
        var bloomCount = BinaryPrimitives.ReadUInt32LittleEndian(elf[(offset + 8)..]);
        if (bucketCount is < 1 or > MaximumHashEntries
            || symbolOffset != 1
            || bloomCount is < 1 or > MaximumHashEntries
            || !IsPowerOfTwo(bloomCount))
        {
            throw new InvalidDataException(
                "The guest module ELF GNU hash is inconsistent with a symbol-free image.");
        }

        var bloomBytes = checked((int)bloomCount * sizeof(ulong));
        var bucketBytes = checked((int)bucketCount * sizeof(uint));
        var length = checked(16 + bloomBytes + bucketBytes);
        offset = MapVirtualFileRange(elf, loads, address, length, "DT_GNU_HASH");
        if (!IsAllZero(elf.Slice(offset + 16, bloomBytes + bucketBytes)))
        {
            throw new InvalidDataException(
                "The guest module ELF GNU hash exposes a symbol beyond the null entry.");
        }
    }

    private static void ValidateRelativeRelocations(
        ReadOnlySpan<byte> elf,
        IReadOnlyList<ElfLoadSegment> loads,
        IReadOnlyDictionary<long, ulong> tags)
    {
        var entrySize = ReadBoundedDynamicLength(
            GetRequiredDynamicValue(tags, DynamicRelaEntrySize, "DT_RELAENT"),
            RelaEntrySize,
            "DT_RELAENT");
        if (entrySize != RelaEntrySize)
        {
            throw new InvalidDataException("The guest module ELF DT_RELAENT is not ELF64-sized.");
        }

        var relocationBytes = ReadBoundedDynamicLength(
            GetRequiredDynamicValue(tags, DynamicRelaSize, "DT_RELASZ"),
            checked(MaximumRelocationCount * RelaEntrySize),
            "DT_RELASZ");
        if (relocationBytes % RelaEntrySize != 0)
        {
            throw new InvalidDataException("The guest module ELF DT_RELASZ is not entry-aligned.");
        }

        var relocationCount = relocationBytes / RelaEntrySize;
        if (tags.TryGetValue(DynamicRelaCount, out var encodedCount)
            && encodedCount != (ulong)relocationCount)
        {
            throw new InvalidDataException(
                "The guest module ELF DT_RELACOUNT does not match its bounded RELA table.");
        }

        var relocationAddress = GetRequiredDynamicValue(tags, DynamicRela, "DT_RELA");
        var relocationOffset = MapVirtualFileRange(
            elf,
            loads,
            relocationAddress,
            relocationBytes,
            "DT_RELA");
        var writableLoad = loads[2];
        var imageEnd = checked(loads[^1].VirtualAddress + loads[^1].MemorySize);
        var targets = new HashSet<ulong>();
        for (var index = 0; index < relocationCount; index++)
        {
            var cursor = checked(relocationOffset + index * RelaEntrySize);
            var target = BinaryPrimitives.ReadUInt64LittleEndian(elf[cursor..]);
            var info = BinaryPrimitives.ReadUInt64LittleEndian(elf[(cursor + 8)..]);
            var addend = BinaryPrimitives.ReadInt64LittleEndian(elf[(cursor + 16)..]);
            if ((uint)info != RelativeRelocationType
                || info >> 32 != 0
                || target % sizeof(ulong) != 0
                || !ContainsRange(
                    writableLoad.VirtualAddress,
                    writableLoad.MemorySize,
                    target,
                    sizeof(ulong))
                || addend < 0
                || addend >= imageEnd
                || !targets.Add(target))
            {
                throw new InvalidDataException(
                    "The guest module ELF contains a non-relative, unbounded, misaligned, or duplicate relocation.");
            }
        }
    }

    private static int MapVirtualFileRange(
        ReadOnlySpan<byte> elf,
        IReadOnlyList<ElfLoadSegment> loads,
        ulong virtualAddress,
        int length,
        string label)
    {
        foreach (var load in loads)
        {
            if (!ContainsRange(
                    load.VirtualAddress,
                    load.FileSize,
                    virtualAddress,
                    length))
            {
                continue;
            }

            var delta = checked((int)(virtualAddress - (ulong)load.VirtualAddress));
            var offset = checked(load.FileOffset + delta);
            EnsureRange(elf.Length, offset, length, label);
            return offset;
        }

        throw new InvalidDataException(
            $"The guest module ELF {label} range is not fully contained in a file-backed PT_LOAD.");
    }

    private static byte[] ReadBuildId(
        ReadOnlySpan<byte> elf,
        int sectionHeaderOffset,
        int sectionHeaderCount)
    {
        byte[]? foundBuildId = null;
        for (var index = 0; index < sectionHeaderCount; index++)
        {
            var offset = checked(sectionHeaderOffset + index * SectionHeaderSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(elf[(offset + 0x04)..]) != SectionTypeNote)
            {
                continue;
            }

            var noteOffset = ReadBoundedOffset(elf, offset + 0x18, "ELF note");
            var noteLength = ReadBoundedLength(elf, offset + 0x20, "ELF note");
            EnsureRange(elf.Length, noteOffset, noteLength, "ELF note");
            var cursor = noteOffset;
            var end = checked(noteOffset + noteLength);
            while (cursor <= end - 0x0C)
            {
                var nameLength = ReadBoundedUInt32Length(elf, cursor, "ELF note name");
                var valueLength = ReadBoundedUInt32Length(elf, cursor + 4, "ELF note value");
                var type = BinaryPrimitives.ReadUInt32LittleEndian(elf[(cursor + 8)..]);
                cursor += 0x0C;
                var alignedNameLength = Align4(nameLength);
                var alignedValueLength = Align4(valueLength);
                if (cursor > end - alignedNameLength
                    || cursor + alignedNameLength > end - alignedValueLength)
                {
                    throw new InvalidDataException("The guest module ELF build-id note is malformed.");
                }

                var name = elf.Slice(cursor, nameLength);
                cursor += alignedNameLength;
                var value = elf.Slice(cursor, valueLength);
                cursor += alignedValueLength;
                if (type != GnuBuildIdType
                    || !name.SequenceEqual([(byte)'G', (byte)'N', (byte)'U', (byte)0]))
                {
                    continue;
                }

                if (valueLength is < 1 or > 0x20 || foundBuildId is not null)
                {
                    throw new InvalidDataException(
                        "The guest module ELF must contain one bounded GNU build id.");
                }

                foundBuildId = new byte[0x20];
                value.CopyTo(foundBuildId);
            }
        }

        return foundBuildId
            ?? throw new InvalidDataException("The guest module ELF does not contain a GNU build id.");
    }

    private static void VerifyPackedNso(ReadOnlyMemory<byte> nsoBytes, ElfImage image)
    {
        if (nsoBytes.Length is < NsoFile.HeaderSize or > MaximumNsoBytes)
        {
            throw new InvalidDataException("The generated guest module NSO exceeds its bounded size.");
        }

        var nso = nsoBytes.Span;
        if (BinaryPrimitives.ReadUInt32LittleEndian(nso[0x00..]) != NsoFile.Magic
            || BinaryPrimitives.ReadUInt32LittleEndian(nso[0x04..]) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(nso[0x08..]) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(nso[0x0C..]) != (uint)RequiredNsoFlags
            || !IsAllZero(nso[0x6C..0xA0])
            || !nso[0x40..0x60].SequenceEqual(image.BuildId))
        {
            throw new InvalidDataException("The generated guest module NSO header is not canonical.");
        }

        var fileCursor = NsoFile.HeaderSize;
        for (var index = 0; index < image.Loads.Count; index++)
        {
            var load = image.Loads[index];
            var headerOffset = 0x10 + index * 0x10;
            var fileOffset = ReadNonNegativeNsoInt32(nso, headerOffset, "segment file offset");
            var memoryOffset = ReadNonNegativeNsoInt32(nso, headerOffset + 4, "segment memory offset");
            var decompressedSize = ReadNonNegativeNsoInt32(nso, headerOffset + 8, "segment size");
            var trailingValue = ReadNonNegativeNsoInt32(nso, headerOffset + 12, "segment trailing value");
            var compressedSize = ReadNonNegativeNsoInt32(
                nso,
                0x60 + index * sizeof(uint),
                "compressed segment size");
            var expectedTrailingValue = index == 2
                ? checked(load.MemorySize - load.FileSize)
                : 1;
            if (fileOffset != fileCursor
                || memoryOffset != load.VirtualAddress
                || decompressedSize != load.FileSize
                || trailingValue != expectedTrailingValue
                || compressedSize < 1
                || compressedSize > nso.Length - fileOffset)
            {
                throw new InvalidDataException(
                    "The generated guest module NSO segment header failed semantic readback.");
            }

            fileCursor = checked(fileCursor + compressedSize);
        }

        if (fileCursor != nso.Length)
        {
            throw new InvalidDataException(
                "The generated guest module NSO has gaps, overlaps, or trailing data.");
        }

        var parsed = NsoFile.Parse(nsoBytes.ToArray());
        if (parsed.Version != 0
            || parsed.Flags != RequiredNsoFlags
            || !parsed.BuildId.AsSpan().SequenceEqual(image.BuildId))
        {
            throw new InvalidDataException("The generated guest module NSO metadata failed readback.");
        }

        for (var index = 0; index < image.Loads.Count; index++)
        {
            var load = image.Loads[index];
            var segment = parsed.Segments[index];
            var expectedHash = SHA256.HashData(load.Bytes);
            if (segment.Header.MemoryOffset != load.VirtualAddress
                || segment.Header.DecompressedSize != load.FileSize
                || !segment.DecompressedData.AsSpan().SequenceEqual(load.Bytes)
                || !segment.Hash.AsSpan().SequenceEqual(expectedHash))
            {
                throw new InvalidDataException(
                    "The generated guest module NSO content or hash failed semantic readback.");
            }
        }

        if (!parsed.Write().AsSpan().SequenceEqual(nso))
        {
            throw new InvalidDataException(
                "The generated guest module NSO is not stable under semantic round-trip readback.");
        }
    }

    private static byte[] Compress(ElfLoadSegment segment)
    {
        var output = new byte[LZ4Codec.MaximumOutputSize(segment.Bytes.Length)];
        var length = LZ4Codec.Encode(
            segment.Bytes,
            0,
            segment.Bytes.Length,
            output,
            0,
            output.Length,
            LZ4Level.L00_FAST);
        if (length <= 0)
        {
            throw new InvalidDataException("A guest module ELF segment could not be compressed.");
        }

        return output[..length];
    }

    private static ulong GetRequiredDynamicValue(
        IReadOnlyDictionary<long, ulong> tags,
        long tag,
        string label)
    {
        if (!tags.TryGetValue(tag, out var value))
        {
            throw new InvalidDataException($"The guest module ELF is missing {label}.");
        }

        return value;
    }

    private static int ReadBoundedDynamicLength(ulong value, int maximum, string label)
    {
        if (value > (ulong)maximum)
        {
            throw new InvalidDataException($"The guest module ELF {label} exceeds its bounded size.");
        }

        return (int)value;
    }

    private static int ReadBoundedOffset(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"The guest module ELF {label} offset is out of bounds.");
        }

        return (int)value;
    }

    private static int ReadBoundedLength(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"The guest module ELF {label} length is out of bounds.");
        }

        return (int)value;
    }

    private static int ReadBoundedUInt32Length(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"The guest module ELF {label} length is out of bounds.");
        }

        return (int)value;
    }

    private static int ReadNonNegativeNsoInt32(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
        if (value < 0)
        {
            throw new InvalidDataException($"The generated guest module NSO {label} is negative.");
        }

        return value;
    }

    private static void EnsureRange(int sourceLength, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > sourceLength - length)
        {
            throw new InvalidDataException($"The guest module ELF {label} range is out of bounds.");
        }
    }

    private static bool ContainsRange(int outerOffset, int outerLength, int innerOffset, int innerLength)
    {
        return innerOffset >= outerOffset
            && innerLength >= 0
            && (long)innerOffset + innerLength <= (long)outerOffset + outerLength;
    }

    private static bool ContainsRange(int outerOffset, int outerLength, ulong innerOffset, int innerLength)
    {
        if (innerOffset < (ulong)outerOffset || innerLength < 0)
        {
            return false;
        }

        var relativeOffset = innerOffset - (ulong)outerOffset;
        return relativeOffset <= (ulong)outerLength
            && (ulong)innerLength <= (ulong)outerLength - relativeOffset;
    }

    private static bool IsAllZero(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static bool IsPowerOfTwo(uint value) => value > 0 && (value & (value - 1)) == 0;

    private static int Align4(int value) => checked((value + 3) & ~3);

    private sealed record ElfImage(
        IReadOnlyList<ElfLoadSegment> Loads,
        byte[] BuildId);

    private sealed record ElfProgramSegment(
        uint Type,
        uint Flags,
        int FileOffset,
        int VirtualAddress,
        int FileSize,
        int MemorySize,
        int Alignment);

    private sealed record ElfLoadSegment(
        uint Flags,
        int FileOffset,
        int VirtualAddress,
        int FileSize,
        int MemorySize,
        byte[] Bytes);
}
