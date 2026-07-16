// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Api.Projects;
using KM.Formats.Executable;

namespace KM.Integration.Tests.Tools;

internal static class SvFashionUnlockBridgeFixtures
{
    public const int PatchOffset = 0x00EAE95C;
    public const uint VanillaOwnershipCheckEntryFirst = 0xA9BB7BFD;
    public const uint VanillaOwnershipCheckEntrySecond = 0xF9000BF9;
    public const uint ReturnTrueFirst = 0x52800020;
    public const uint ReturnTrueSecond = 0xD65F03C0;

    private const string ScarletBuildId = "421C5411B487EB4D049DD065FEC9547773E8E598";
    private const string VioletBuildId = "709BFD66115298640155FCC4979DBA151C7CC79A";

    public static byte[] CreateCompatibleMain(ProjectGameDto game)
    {
        return game switch
        {
            ProjectGameDto.Scarlet => CreateCompatibleMain(ScarletBuildId),
            ProjectGameDto.Violet => CreateCompatibleMain(VioletBuildId),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Fashion Unlock fixtures are Scarlet/Violet only."),
        };
    }

    public static byte[] CreateInstalledMain(ProjectGameDto game)
    {
        var nso = NsoFile.Parse(CreateCompatibleMain(game));
        var text = nso.Text.DecompressedData.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(PatchOffset, sizeof(uint)),
            ReturnTrueFirst);
        BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(PatchOffset + sizeof(uint), sizeof(uint)),
            ReturnTrueSecond);
        return nso.Write(textDecompressedData: text);
    }

    public static (uint First, uint Second) ReadPatchInstructions(byte[] mainBytes)
    {
        var nso = NsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData;
        return (
            BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(PatchOffset, sizeof(uint))),
            BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(PatchOffset + sizeof(uint), sizeof(uint))));
    }

    private static byte[] CreateCompatibleMain(string buildId)
    {
        var text = new byte[SvHyperspaceBypassBridgeFixtures.PatchOffset + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(PatchOffset, sizeof(uint)),
            VanillaOwnershipCheckEntryFirst);
        BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(PatchOffset + sizeof(uint), sizeof(uint)),
            VanillaOwnershipCheckEntrySecond);
        BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(SvHyperspaceBypassBridgeFixtures.PatchOffset, sizeof(uint)),
            SvHyperspaceBypassBridgeFixtures.VanillaSpeciesCompare);
        return CreateNso(text, [0x10], [0x20], Convert.FromHexString(buildId));
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
