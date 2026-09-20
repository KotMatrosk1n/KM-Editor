// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.ZA.Pokemon;

internal sealed class ZaPokemonSizeDocument
{
    private readonly byte[] bytes;
    private readonly int root;
    private readonly int vtable;
    private readonly int objectSize;
    private readonly int[] offsets;

    public float Minimum { get; }
    public float Maximum { get; }
    public double Midpoint => ((double)Minimum + Maximum) / 2;

    public ZaPokemonSizeDocument(byte[] bytes)
    {
        this.bytes = bytes;
        if (bytes.Length < 16 || bytes.Length > 1024 * 1024)
            throw new InvalidDataException("Pokemon size configuration has an unsupported length.");
        root = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (root < 4 || root > bytes.Length - 4 || root % 4 != 0)
            throw new InvalidDataException("Pokemon size configuration has an invalid root.");
        var displacement = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(root));
        if (displacement <= 0 || displacement > root - 4)
            throw new InvalidDataException("Pokemon size configuration has an unsupported table layout.");
        vtable = root - displacement;
        var vtableSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtable));
        objectSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtable + 2));
        if (vtableSize < 10 || vtableSize % 2 != 0 || vtableSize > root - vtable
            || objectSize < 4 || objectSize > bytes.Length - root)
            throw new InvalidDataException("Pokemon size configuration has invalid table bounds.");
        offsets = [ReadOffset(1), ReadOffset(2)];
        Minimum = ReadValue(offsets[0], 0.8f);
        Maximum = ReadValue(offsets[1], 1.2f);
        if (!Valid(Minimum) || !Valid(Maximum) || Minimum > Maximum)
            throw new InvalidDataException("Pokemon size endpoints must be positive finite numbers in ascending order.");
        if (offsets.Any(offset => offset == 0) && (root + objectSize != bytes.Length || objectSize % 4 != 0))
            throw new InvalidDataException("Pokemon size defaults cannot be materialized in this table layout.");
        if (offsets[0] != 0 && offsets[0] == offsets[1])
            throw new InvalidDataException("Pokemon size endpoint fields overlap.");
        for (var field = 0; field < (vtableSize - 4) / 2; field++)
        {
            if (field is 1 or 2) continue;
            var offset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtable + 4 + field * 2));
            if (offset != 0 && (offset < 4 || offset >= objectSize
                || offsets.Any(sizeOffset => sizeOffset != 0 && offset >= sizeOffset && offset < sizeOffset + 4)))
                throw new InvalidDataException("Pokemon size fields overlap unrelated configuration data.");
        }
    }

    public byte[] WriteFixed(float value)
    {
        if (!Valid(value))
            throw new InvalidDataException("Alpha size must be a positive finite number.");
        var result = new byte[bytes.Length + offsets.Count(offset => offset == 0) * 4];
        bytes.CopyTo(result, 0);
        var size = objectSize;
        for (var index = 0; index < 2; index++)
        {
            var offset = offsets[index];
            if (offset == 0)
            {
                offset = size;
                size = checked(size + 4);
                if (size > ushort.MaxValue)
                    throw new InvalidDataException("Pokemon size table exceeds its supported object size.");
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(vtable + 6 + index * 2), (ushort)offset);
            }
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(root + offset), value);
        }
        if (size != objectSize)
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(vtable + 2), (ushort)size);
        var verified = new ZaPokemonSizeDocument(result);
        if (verified.Minimum != value || verified.Maximum != value)
            throw new InvalidDataException("Alpha size output could not be verified.");
        return result;
    }

    private int ReadOffset(int field)
    {
        var offset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtable + 4 + field * 2));
        if (offset != 0 && (offset < 4 || offset % 4 != 0 || offset > objectSize - 4))
            throw new InvalidDataException("Pokemon size field is outside its object.");
        return offset;
    }

    private float ReadValue(int offset, float fallback) => offset == 0
        ? fallback : BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(root + offset));

    internal static bool Valid(float value) => float.IsFinite(value) && value > 0;
}
