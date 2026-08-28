// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.Executable;

/// <summary>
/// Derives a title-specific NPDM that differs from retail metadata only by the
/// filesystem and process-memory permissions required by a KM guest module.
/// </summary>
public static class NpdmRuntimeCapabilityPatcher
{
    public const int MaximumNpdmBytes = 1024 * 1024;
    public const ulong SdCardFilesystemPermission = 1UL << 21;
    public const uint ProcessHandleTransferSyscallDescriptor = 0x4120000F;
    public const uint ProcessMemoryAliasSyscallDescriptor = 0x8600000F;

    private const uint MetaMagic = 0x4154454D;
    private const uint AcidMagic = 0x44494341;
    private const uint Aci0Magic = 0x30494341;
    private const int MetaSize = 0x80;
    private const int AcidSignedHeaderSize = 0x100;
    private const int AcidDescriptorHeaderSize = 0x240;
    private const int Aci0DescriptorHeaderSize = 0x40;

    /// <summary>
    /// Adds the SD-card permission and the exact SVC capabilities required by
    /// the guest runtime to both the signed-policy and process-policy views.
    /// Unsupported layouts fail closed.
    /// </summary>
    public static byte[] AddGuestRuntimeCapabilities(ReadOnlySpan<byte> retailNpdm)
    {
        var layout = ParseLayout(retailNpdm);
        var acid = retailNpdm.Slice(layout.AcidOffset, layout.AcidSize).ToArray();
        var aci0 = retailNpdm.Slice(layout.Aci0Offset, layout.Aci0Size).ToArray();

        AddFilesystemPermission(acid, 0x220, AcidDescriptorHeaderSize, "ACID FAC");
        AddFilesystemPermission(aci0, 0x20, Aci0DescriptorHeaderSize, "ACI0 FAH");
        acid = AddSyscallDescriptors(acid, 0x230, AcidDescriptorHeaderSize, "ACID KAC");
        aci0 = AddSyscallDescriptors(aci0, 0x30, Aci0DescriptorHeaderSize, "ACI0 KAC");

        BinaryPrimitives.WriteUInt32LittleEndian(
            acid.AsSpan(0x204),
            checked((uint)(acid.Length - AcidSignedHeaderSize)));

        // The supported retail layouts put ACID first, use zero alignment bytes
        // between sections, and end at ACI0. Preserve that order and consume
        // alignment space before moving the second section.
        var aci0Offset = Math.Max(layout.Aci0Offset, Align16(layout.AcidOffset + acid.Length));
        var output = new byte[checked(aci0Offset + aci0.Length)];
        retailNpdm[..layout.AcidOffset].CopyTo(output);
        acid.CopyTo(output, layout.AcidOffset);
        aci0.CopyTo(output, aci0Offset);

        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x70), aci0Offset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x74), aci0.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x78), layout.AcidOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x7C), acid.Length);

        VerifyDerived(retailNpdm, output);
        return output;
    }

    /// <summary>
    /// Proves that both policy views grant only the exact KM permission delta
    /// and that all unrelated section bytes remain unchanged.
    /// </summary>
    public static void VerifyDerived(ReadOnlySpan<byte> retailNpdm, ReadOnlySpan<byte> derivedNpdm)
    {
        var retail = ParseLayout(retailNpdm);
        var derived = ParseLayout(derivedNpdm);
        if (retail.AcidOffset != derived.AcidOffset
            || derivedNpdm.Length != derived.Aci0Offset + derived.Aci0Size)
        {
            throw new InvalidDataException("The derived main.npdm section envelope is not canonical.");
        }

        var retailAcid = retailNpdm.Slice(retail.AcidOffset, retail.AcidSize);
        var retailAci0 = retailNpdm.Slice(retail.Aci0Offset, retail.Aci0Size);
        var derivedAcid = derivedNpdm.Slice(derived.AcidOffset, derived.AcidSize);
        var derivedAci0 = derivedNpdm.Slice(derived.Aci0Offset, derived.Aci0Size);
        VerifyPolicySection(
            retailAcid,
            derivedAcid,
            0x220,
            0x230,
            AcidDescriptorHeaderSize,
            acidInnerSizeField: 0x204,
            "ACID");
        VerifyPolicySection(
            retailAci0,
            derivedAci0,
            0x20,
            0x30,
            Aci0DescriptorHeaderSize,
            acidInnerSizeField: null,
            "ACI0");

        // META may change only its four section offset/size fields.
        VerifyEqualOutsideRanges(retailNpdm[..MetaSize], derivedNpdm[..MetaSize], (0x70, 0x10));
        var paddingStart = derived.AcidOffset + derived.AcidSize;
        if (paddingStart > derived.Aci0Offset
            || HasNonZeroBytes(derivedNpdm.Slice(paddingStart, derived.Aci0Offset - paddingStart)))
        {
            throw new InvalidDataException("The derived main.npdm section padding is not canonical zero padding.");
        }
    }

    private static NpdmLayout ParseLayout(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < MetaSize or > MaximumNpdmBytes
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != MetaMagic)
        {
            throw new InvalidDataException("The main.npdm envelope is invalid or out of bounds.");
        }

        var aci0Offset = ReadNonNegativeInt32(bytes, 0x70, "ACI0 offset");
        var aci0Size = ReadNonNegativeInt32(bytes, 0x74, "ACI0 size");
        var acidOffset = ReadNonNegativeInt32(bytes, 0x78, "ACID offset");
        var acidSize = ReadNonNegativeInt32(bytes, 0x7C, "ACID size");
        ValidateSection(bytes, acidOffset, acidSize, AcidDescriptorHeaderSize, AcidMagic, 0x200, "ACID");
        ValidateSection(bytes, aci0Offset, aci0Size, Aci0DescriptorHeaderSize, Aci0Magic, 0, "ACI0");
        if (acidOffset != MetaSize
            || acidOffset + acidSize > aci0Offset
            || aci0Offset + aci0Size != bytes.Length
            || HasNonZeroBytes(bytes.Slice(acidOffset + acidSize, aci0Offset - (acidOffset + acidSize))))
        {
            throw new InvalidDataException("The main.npdm section order or alignment envelope is unsupported.");
        }

        var acidInnerSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(acidOffset + 0x204));
        if (acidInnerSize != checked((uint)(acidSize - AcidSignedHeaderSize)))
        {
            throw new InvalidDataException("The main.npdm ACID inner size is inconsistent.");
        }

        return new NpdmLayout(acidOffset, acidSize, aci0Offset, aci0Size);
    }

    private static void AddFilesystemPermission(
        Span<byte> section,
        int descriptorFieldsOffset,
        int minimumOffset,
        string label)
    {
        var range = ReadSectionRange(section, descriptorFieldsOffset, minimumOffset, label);
        if (range.Size < 0x0C)
        {
            throw new InvalidDataException($"The main.npdm {label} is too small.");
        }

        var permissionsOffset = checked(range.Offset + 4);
        var permissions = BinaryPrimitives.ReadUInt64LittleEndian(section[permissionsOffset..]);
        BinaryPrimitives.WriteUInt64LittleEndian(
            section[permissionsOffset..],
            permissions | SdCardFilesystemPermission);
    }

    private static byte[] AddSyscallDescriptors(
        byte[] section,
        int fieldsOffset,
        int minimumOffset,
        string label)
    {
        foreach (var required in new[]
                 {
                     ProcessHandleTransferSyscallDescriptor,
                     ProcessMemoryAliasSyscallDescriptor,
                 })
        {
            var range = ReadSectionRange(section, fieldsOffset, minimumOffset, label);
            if ((range.Size & 3) != 0)
            {
                throw new InvalidDataException($"The main.npdm {label} is not word aligned.");
            }

            var requiredGroup = GetSyscallMaskGroup(required);
            var updated = false;
            for (var cursor = range.Offset; cursor < range.End; cursor += sizeof(uint))
            {
                var descriptor = BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(cursor));
                if (IsSyscallMaskGroup(descriptor, requiredGroup))
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        section.AsSpan(cursor),
                        descriptor | required);
                    updated = true;
                    break;
                }
            }

            if (updated)
            {
                continue;
            }
            if (range.End != section.Length)
            {
                throw new InvalidDataException(
                    $"The main.npdm {label} is not final and cannot be expanded without moving unrelated data.");
            }

            var expanded = new byte[checked(section.Length + sizeof(uint))];
            section.CopyTo(expanded, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(expanded.AsSpan(section.Length), required);
            BinaryPrimitives.WriteInt32LittleEndian(
                expanded.AsSpan(fieldsOffset + 4),
                range.Size + sizeof(uint));
            section = expanded;
        }

        return section;
    }

    private static void VerifyPolicySection(
        ReadOnlySpan<byte> retail,
        ReadOnlySpan<byte> derived,
        int filesystemFieldsOffset,
        int syscallFieldsOffset,
        int minimumOffset,
        int? acidInnerSizeField,
        string label)
    {
        if (derived.Length < retail.Length)
        {
            throw new InvalidDataException($"The derived main.npdm {label} was truncated.");
        }

        var retailFilesystem = ReadSectionRange(retail, filesystemFieldsOffset, minimumOffset, label + " filesystem");
        var derivedFilesystem = ReadSectionRange(derived, filesystemFieldsOffset, minimumOffset, label + " filesystem");
        var retailSyscalls = ReadSectionRange(retail, syscallFieldsOffset, minimumOffset, label + " syscalls");
        var derivedSyscalls = ReadSectionRange(derived, syscallFieldsOffset, minimumOffset, label + " syscalls");
        var retailWords = ReadWords(retail.Slice(retailSyscalls.Offset, retailSyscalls.Size));
        var expectedWords = AddRequiredSyscallDescriptors(retailWords);
        var derivedWords = ReadWords(derived.Slice(derivedSyscalls.Offset, derivedSyscalls.Size));
        if (retailFilesystem != derivedFilesystem
            || retailSyscalls.Offset != derivedSyscalls.Offset
            || derivedSyscalls.Size != expectedWords.Length * sizeof(uint)
            || !derivedWords.AsSpan().SequenceEqual(expectedWords))
        {
            throw new InvalidDataException($"The derived main.npdm {label} descriptor layout changed unexpectedly.");
        }

        var retailPermissions = BinaryPrimitives.ReadUInt64LittleEndian(retail[(retailFilesystem.Offset + 4)..]);
        var derivedPermissions = BinaryPrimitives.ReadUInt64LittleEndian(derived[(derivedFilesystem.Offset + 4)..]);
        if (derivedPermissions != (retailPermissions | SdCardFilesystemPermission))
        {
            throw new InvalidDataException($"The derived main.npdm {label} filesystem permission delta is invalid.");
        }

        var ignored = new List<(int Offset, int Length)>
        {
            (syscallFieldsOffset + 4, sizeof(int)),
            (retailFilesystem.Offset + 4, sizeof(ulong)),
            (retailSyscalls.Offset, retailSyscalls.Size),
        };
        if (acidInnerSizeField is { } innerSizeOffset)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(derived[innerSizeOffset..])
                != checked((uint)(derived.Length - AcidSignedHeaderSize)))
            {
                throw new InvalidDataException("The derived main.npdm ACID inner size is invalid.");
            }
            ignored.Add((innerSizeOffset, sizeof(uint)));
        }

        VerifyEqualOutsideRanges(retail, derived[..retail.Length], ignored.ToArray());
        if (derived.Length > retail.Length)
        {
            var appendedWordCount = expectedWords.Length - retailWords.Length;
            var appended = new byte[checked(appendedWordCount * sizeof(uint))];
            for (var index = 0; index < appendedWordCount; index++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    appended.AsSpan(index * sizeof(uint)),
                    expectedWords[retailWords.Length + index]);
            }
            if (!derived[retail.Length..].SequenceEqual(appended))
            {
                throw new InvalidDataException($"The derived main.npdm {label} appended bytes are invalid.");
            }
        }
    }

    private static void VerifyEqualOutsideRanges(
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual,
        params (int Offset, int Length)[] ignored)
    {
        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException("The compared main.npdm ranges have different lengths.");
        }

        for (var offset = 0; offset < expected.Length; offset++)
        {
            if (ignored.Any(range => offset >= range.Offset && offset < range.Offset + range.Length))
            {
                continue;
            }

            if (expected[offset] != actual[offset])
            {
                throw new InvalidDataException("The derived main.npdm changed an unrelated byte.");
            }
        }
    }

    private static SectionRange ReadSectionRange(
        ReadOnlySpan<byte> section,
        int fieldsOffset,
        int minimumOffset,
        string label)
    {
        if (fieldsOffset < 0 || fieldsOffset > section.Length - 8)
        {
            throw new InvalidDataException($"The main.npdm {label} range fields are out of bounds.");
        }

        var offset = ReadNonNegativeInt32(section, fieldsOffset, label + " offset");
        var size = ReadNonNegativeInt32(section, fieldsOffset + 4, label + " size");
        if (offset < minimumOffset || offset > section.Length - size)
        {
            throw new InvalidDataException($"The main.npdm {label} range is out of bounds.");
        }

        return new SectionRange(offset, size);
    }

    private static int ReadNonNegativeInt32(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        if (offset < 0 || offset > bytes.Length - sizeof(int))
        {
            throw new InvalidDataException($"The main.npdm {label} field is out of bounds.");
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
        if (value < 0)
        {
            throw new InvalidDataException($"The main.npdm {label} is negative.");
        }

        return value;
    }

    private static void ValidateSection(
        ReadOnlySpan<byte> bytes,
        int offset,
        int size,
        int minimumSize,
        uint magic,
        int magicOffset,
        string label)
    {
        if (size < minimumSize
            || offset < MetaSize
            || offset > bytes.Length - size
            || magicOffset > size - sizeof(uint)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + magicOffset)) != magic)
        {
            throw new InvalidDataException($"The main.npdm {label} section is invalid.");
        }
    }

    private static uint[] ReadWords(ReadOnlySpan<byte> bytes)
    {
        var result = new uint[bytes.Length / sizeof(uint)];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(index * sizeof(uint))..]);
        }
        return result;
    }

    private static uint[] AddRequiredSyscallDescriptors(uint[] retail)
    {
        var expected = retail.ToList();
        foreach (var required in new[]
                 {
                     ProcessHandleTransferSyscallDescriptor,
                     ProcessMemoryAliasSyscallDescriptor,
                 })
        {
            var requiredGroup = GetSyscallMaskGroup(required);
            var index = expected.FindIndex(descriptor =>
                IsSyscallMaskGroup(descriptor, requiredGroup));
            if (index >= 0)
            {
                expected[index] |= required;
            }
            else
            {
                expected.Add(required);
            }
        }

        return expected.ToArray();
    }

    private static uint GetSyscallMaskGroup(uint descriptor) =>
        (descriptor >> 29) & 0x07;

    private static bool IsSyscallMaskGroup(uint descriptor, uint group) =>
        (descriptor & 0x1F) == 0x0F
        && GetSyscallMaskGroup(descriptor) == group;

    private static bool HasNonZeroBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value != 0)
            {
                return true;
            }
        }
        return false;
    }

    private static int Align16(int value) => checked((value + 0x0F) & ~0x0F);

    private sealed record NpdmLayout(int AcidOffset, int AcidSize, int Aci0Offset, int Aci0Size);

    private readonly record struct SectionRange(int Offset, int Size)
    {
        public int End => checked(Offset + Size);
    }
}
