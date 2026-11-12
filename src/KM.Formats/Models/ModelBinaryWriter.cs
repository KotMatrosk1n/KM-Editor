// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;

namespace KM.Formats.Models;

internal static class ModelBinaryWriter
{
    internal static void Write(BinaryWriter writer, ReadOnlySpan<float> values)
    {
        if (BitConverter.IsLittleEndian) writer.Write(MemoryMarshal.AsBytes(values));
        else foreach (var value in values) writer.Write(value);
    }

    internal static void Write(BinaryWriter writer, ReadOnlySpan<uint> values)
    {
        if (BitConverter.IsLittleEndian) writer.Write(MemoryMarshal.AsBytes(values));
        else foreach (var value in values) writer.Write(value);
    }
}
