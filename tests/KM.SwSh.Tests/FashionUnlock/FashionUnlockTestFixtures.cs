// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.FashionUnlock;
using KM.SwSh.Tests.Items;

namespace KM.SwSh.Tests.FashionUnlock;

internal static class FashionUnlockTestFixtures
{
    private const ulong SwordTitleId = 0x0100ABF008968000;
    private const ulong ShieldTitleId = 0x01008DB008C2C000;
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private static readonly byte[] DirectGetterVanilla =
    [
        0xE8, 0x03, 0x00, 0xAA,
        0xE0, 0x03, 0x1F, 0x2A,
    ];

    private static readonly byte[] MappedGetterVanilla =
    [
        0xFF, 0x03, 0x06, 0xD1,
        0xFC, 0x5F, 0x14, 0xA9,
    ];

    public static TemporarySwShProject CreateProject(ProjectGame game, byte[]? baseMain = null)
    {
        var project = TemporarySwShProject.Create();
        project.WriteBaseExeFsFile("main", baseMain ?? CreateMain(game));
        project.WriteBaseExeFsFile("main.npdm", CreateNpdm(game));
        return project;
    }

    public static byte[] CreateMain(ProjectGame game)
    {
        var text = new byte[
            SwShFashionUnlockMainPatcher.ShieldMappedGetterOffset
            + SwShFashionUnlockMainPatcher.PatchLength
            + 0x20];
        var ro = Enumerable.Range(0, 0x20).Select(index => (byte)(0x40 + index)).ToArray();
        var data = Enumerable.Range(0, 0x20).Select(index => (byte)(0x80 + index)).ToArray();
        DirectGetterVanilla.CopyTo(text.AsSpan(DirectOffset(game)));
        MappedGetterVanilla.CopyTo(text.AsSpan(MappedOffset(game)));
        return CreateNso(text, ro, data, BuildIdForGame(game));
    }

    public static byte[] CreateSemanticallyStableNoncanonicalBase(ProjectGame game)
    {
        var output = CreateMain(game);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x0C), 0);
        output[0xA0] ^= 0x5A;
        output[0xC0] ^= 0x5A;
        output[0xE0] ^= 0x5A;
        return output;
    }

    public static byte[] MutateUnownedSemanticBytes(byte[] mainBytes)
    {
        var nso = NsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.ToArray();
        var ro = nso.Ro.DecompressedData.ToArray();
        var data = nso.Data.DecompressedData.ToArray();
        text[0x100] ^= 0x5A;
        ro[0] ^= 0x5A;
        data[0] ^= 0x5A;
        return nso.Write(
            textDecompressedData: text,
            roDecompressedData: ro,
            dataDecompressedData: data);
    }

    public static string OutputMainPath(ProjectPaths paths)
    {
        return Path.Combine(paths.OutputRootPath!, "exefs", "main");
    }

    private static byte[] CreateNpdm(ProjectGame game)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(
            data.AsSpan(0x290, sizeof(ulong)),
            game == ProjectGame.Shield ? ShieldTitleId : SwordTitleId);
        return data;
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[] buildId)
    {
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];
        var flags = NsoFlags.CheckHashText | NsoFlags.CheckHashRo | NsoFlags.CheckHashData;

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x0C), (uint)flags);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        buildId.CopyTo(output.AsSpan(0x40, 0x20));
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

    private static byte[] BuildIdForGame(ProjectGame game)
    {
        return Convert.FromHexString(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId);
    }

    private static int DirectOffset(ProjectGame game)
    {
        return game == ProjectGame.Shield
            ? SwShFashionUnlockMainPatcher.ShieldDirectGetterOffset
            : SwShFashionUnlockMainPatcher.SwordDirectGetterOffset;
    }

    private static int MappedOffset(ProjectGame game)
    {
        return game == ProjectGame.Shield
            ? SwShFashionUnlockMainPatcher.ShieldMappedGetterOffset
            : SwShFashionUnlockMainPatcher.SwordMappedGetterOffset;
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
