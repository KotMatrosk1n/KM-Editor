// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Projects;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SV.TypeChart;
using System.Buffers.Binary;

namespace KM.Integration.Tests.Tools;

internal static class SvTypeChartBridgeFixtures
{
    private const string ScarletBuildId = "421C5411B487EB4D049DD065FEC9547773E8E598";
    private const string VioletBuildId = "709BFD66115298640155FCC4979DBA151C7CC79A";

    public static byte[] CreateCompatibleMain(ProjectGameDto game)
    {
        return game switch
        {
            ProjectGameDto.Scarlet => CreateCompatibleMain(ScarletBuildId),
            ProjectGameDto.Violet => CreateCompatibleMain(VioletBuildId),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Type Chart fixtures are Scarlet/Violet only."),
        };
    }

    public static byte[] CreateCompatibleMain(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Scarlet => CreateCompatibleMain(ScarletBuildId),
            ProjectGame.Violet => CreateCompatibleMain(VioletBuildId),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Type Chart fixtures are Scarlet/Violet only."),
        };
    }

    public static int[] ReadChartValues(byte[] mainBytes)
    {
        var nso = SwShNsoFile.Parse(mainBytes);
        return nso.Ro.DecompressedData
            .AsSpan(SvTypeChartMainPatcher.RoChartOffset, SvTypeChartMainPatcher.ChartLength)
            .ToArray()
            .Select(value => (int)value)
            .ToArray();
    }

    private static byte[] CreateCompatibleMain(string buildId)
    {
        var text = new byte[SvHyperspaceBypassBridgeFixtures.PatchOffset + sizeof(uint)];
        for (var index = 0; index < text.Length; index++)
        {
            text[index] = (byte)(0x40 + (index & 0x3F));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(SvHyperspaceBypassBridgeFixtures.PatchOffset, sizeof(uint)),
            SvHyperspaceBypassBridgeFixtures.VanillaSpeciesCompare);

        var ro = new byte[SvTypeChartMainPatcher.RoChartOffset + SvTypeChartMainPatcher.ChartLength + 0x40];
        Array.Fill(ro, (byte)0xCC);
        SvTypeChartMainPatcher.VanillaChartValues
            .Select(value => checked((byte)value))
            .ToArray()
            .CopyTo(ro.AsSpan(SvTypeChartMainPatcher.RoChartOffset));

        var data = Enumerable.Range(0, 0x20).Select(index => (byte)(0x20 + index)).ToArray();
        return CreateNso(text, ro, data, Convert.FromHexString(buildId));
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[] buildId)
    {
        var textOffset = SwShNsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), SwShNsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Fill(0xAB);
        buildId.CopyTo(output.AsSpan(0x40, Math.Min(buildId.Length, 0x20)));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        SwShNsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        SwShNsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        SwShNsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
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
