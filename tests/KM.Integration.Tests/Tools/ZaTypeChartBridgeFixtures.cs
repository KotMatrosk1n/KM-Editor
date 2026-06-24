// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.Executable;
using KM.ZA.TypeChart;

namespace KM.Integration.Tests.Tools;

internal static class ZaTypeChartBridgeFixtures
{
    private const string ZABuildId = "B1F12FD919EAE86AB8A978317677E64BCE443D1F";

    public static byte[] CreateCompatibleMain()
    {
        var text = Enumerable.Range(0, 0x40).Select(index => (byte)(0x40 + index)).ToArray();

        var ro = new byte[ZaTypeChartMainPatcher.RoChartOffset + ZaTypeChartMainPatcher.ChartLength + 0x40];
        Array.Fill(ro, (byte)0xCC);
        ZaTypeChartMainPatcher.VanillaChartValues
            .Select(value => checked((byte)value))
            .ToArray()
            .CopyTo(ro.AsSpan(ZaTypeChartMainPatcher.RoChartOffset));

        var data = Enumerable.Range(0, 0x20).Select(index => (byte)(0x20 + index)).ToArray();
        return CreateNso(text, ro, data, Convert.FromHexString(ZABuildId));
    }

    public static int[] ReadChartValues(byte[] mainBytes)
    {
        var nso = NsoFile.Parse(mainBytes);
        return nso.Ro.DecompressedData
            .AsSpan(ZaTypeChartMainPatcher.RoChartOffset, ZaTypeChartMainPatcher.ChartLength)
            .ToArray()
            .Select(value => (int)value)
            .ToArray();
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[] buildId)
    {
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Fill(0xAB);
        buildId.CopyTo(output.AsSpan(0x40, Math.Min(buildId.Length, 0x20)));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        return output;
    }

    private static void WriteSegmentHeader(
        byte[] output,
        int offset,
        int fileOffset,
        int memoryOffset,
        int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
