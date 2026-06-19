// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Projects;
using KM.Formats.SwSh;
using System.Buffers.Binary;

namespace KM.Integration.Tests.Tools;

internal static class SvHyperspaceBypassBridgeFixtures
{
    public const int PatchOffset = 0x02873A50;
    public const uint VanillaSpeciesCompare = 0x710B411F;
    public const uint BypassBranch = 0x1400001A;

    private const string ScarletBuildId = "421C5411B487EB4D049DD065FEC9547773E8E598";
    private const string VioletBuildId = "709BFD66115298640155FCC4979DBA151C7CC79A";

    public static byte[] CreateCompatibleMain(ProjectGameDto game)
    {
        return game switch
        {
            ProjectGameDto.Scarlet => CreateCompatibleMain(ScarletBuildId),
            ProjectGameDto.Violet => CreateCompatibleMain(VioletBuildId),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Hyperspace Bypass fixtures are Scarlet/Violet only."),
        };
    }

    public static uint ReadPatchInstruction(byte[] mainBytes)
    {
        var nso = SwShNsoFile.Parse(mainBytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(nso.Text.DecompressedData.AsSpan(PatchOffset, sizeof(uint)));
    }

    private static byte[] CreateCompatibleMain(string buildId)
    {
        var text = new byte[PatchOffset + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(PatchOffset, sizeof(uint)), VanillaSpeciesCompare);
        return CreateNso(text, [0x10], [0x20], Convert.FromHexString(buildId));
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
