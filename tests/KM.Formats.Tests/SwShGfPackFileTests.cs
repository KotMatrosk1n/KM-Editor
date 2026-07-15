// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShGfPackFileTests
{
    [Fact]
    public void WriteRoundTripsNamedFiles()
    {
        var pack = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3]),
            new SwShGfPackNamedFile("encount_k.bin", [4, 5]),
        ]);

        var parsed = SwShGfPackFile.Parse(pack.Write());

        Assert.True(parsed.ContainsFileName("encount_symbol_k.bin"));
        Assert.Equal([1, 2, 3], parsed.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal([4, 5], parsed.GetFileByName("encount_k.bin"));
    }

    [Fact]
    public void SetFileByNameRewritesTargetAndPreservesOtherFiles()
    {
        var pack = SwShGfPackFile.Parse(SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3], SwShGfPackCompressionType.Lz4),
            new SwShGfPackNamedFile("encount_k.bin", [4, 5], SwShGfPackCompressionType.Lz4),
        ]).Write());

        pack.SetFileByName("encount_symbol_k.bin", [9, 8, 7, 6]);

        var parsed = SwShGfPackFile.Parse(pack.Write());
        Assert.Equal([9, 8, 7, 6], parsed.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal([4, 5], parsed.GetFileByName("encount_k.bin"));
    }

    [Fact]
    public void ParsedNoOpAndSameValueWritesAreByteIdentical()
    {
        var source = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3], SwShGfPackCompressionType.Lz4),
            new SwShGfPackNamedFile("encount_k.bin", [4, 5], SwShGfPackCompressionType.Zlib),
        ]).Write();
        source = [.. source, 0xA1, 0xB2, 0xC3, 0xD4];

        var parsed = SwShGfPackFile.Parse(source);

        Assert.Equal(source, parsed.Write());

        parsed.SetFileByName("encount_symbol_k.bin", [1, 2, 3]);
        Assert.Equal(source, parsed.Write());
    }

    [Fact]
    public void RevertingModifiedMemberToOriginalRestoresByteIdenticalWrite()
    {
        var source = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3], SwShGfPackCompressionType.Lz4),
        ]).Write();
        var parsed = SwShGfPackFile.Parse(source);

        parsed.SetFileByName("encount_symbol_k.bin", [9, 8, 7, 6]);
        parsed.SetFileByName("encount_symbol_k.bin", [1, 2, 3]);

        Assert.Equal(source, parsed.Write());
    }

    [Fact]
    public void ModifiedMemberIsAppendedAndOnlyItsFileTableRowIsPatched()
    {
        var source = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3], SwShGfPackCompressionType.Lz4),
            new SwShGfPackNamedFile("encount_k.bin", [4, 5], SwShGfPackCompressionType.Zlib),
        ]).Write();
        source = [.. source, 0xA1, 0xB2, 0xC3, 0xD4];
        var fileTableOffset = checked((int)BinaryPrimitives.ReadInt64LittleEndian(source.AsSpan(0x18, sizeof(long))));
        var targetRowOffset = fileTableOffset;
        var unrelatedRowOffset = fileTableOffset + 0x18;
        var originalUnrelatedRow = source.AsSpan(unrelatedRowOffset, 0x18).ToArray();
        var replacement = new byte[] { 9, 8, 7, 6, 5 };
        var parsed = SwShGfPackFile.Parse(source);

        parsed.SetFileByName("encount_symbol_k.bin", replacement);
        var output = parsed.Write();

        Assert.True(output.Length > source.Length);
        Assert.Equal(originalUnrelatedRow, output.AsSpan(unrelatedRowOffset, 0x18).ToArray());
        Assert.Equal(source.AsSpan(source.Length - 4, 4).ToArray(), output.AsSpan(source.Length - 4, 4).ToArray());

        for (var offset = 0; offset < source.Length; offset++)
        {
            var isPatchedTargetField =
                offset >= targetRowOffset + 0x04 && offset < targetRowOffset + 0x0C
                || offset >= targetRowOffset + 0x10 && offset < targetRowOffset + 0x14;
            if (!isPatchedTargetField)
            {
                Assert.Equal(source[offset], output[offset]);
            }
        }

        var appendedOffset = BinaryPrimitives.ReadInt32LittleEndian(
            output.AsSpan(targetRowOffset + 0x10, sizeof(int)));
        Assert.True(appendedOffset >= source.Length);
        Assert.Equal(0, appendedOffset % 0x10);
        Assert.Equal(0, output.Length % 0x10);

        var reparsed = SwShGfPackFile.Parse(output);
        Assert.Equal(replacement, reparsed.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal([4, 5], reparsed.GetFileByName("encount_k.bin"));
    }

    [Fact]
    public void ExactPackedDataAliasIsPreservedAndIsolatedWhenOneMemberChanges()
    {
        var source = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3]),
            new SwShGfPackNamedFile("encount_k.bin", [4, 5, 6]),
        ]).Write();
        source = [.. source, 0xA1, 0xB2, 0xC3];
        var fileTableOffset = checked((int)BinaryPrimitives.ReadInt64LittleEndian(source.AsSpan(0x18, sizeof(long))));
        var firstRowOffset = fileTableOffset;
        var secondRowOffset = fileTableOffset + 0x18;
        var firstPackedOffset = BinaryPrimitives.ReadInt32LittleEndian(
            source.AsSpan(firstRowOffset + 0x10, sizeof(int)));
        BinaryPrimitives.WriteInt32LittleEndian(
            source.AsSpan(secondRowOffset + 0x10, sizeof(int)),
            firstPackedOffset);
        var parsed = SwShGfPackFile.Parse(source);

        Assert.Equal(source, parsed.Write());
        Assert.Equal([1, 2, 3], parsed.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal([1, 2, 3], parsed.GetFileByName("encount_k.bin"));

        parsed.SetFileByName("encount_symbol_k.bin", [9, 8, 7, 6]);
        var output = parsed.Write();
        var reparsed = SwShGfPackFile.Parse(output);

        Assert.Equal([9, 8, 7, 6], reparsed.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal([1, 2, 3], reparsed.GetFileByName("encount_k.bin"));
        Assert.Equal(source.AsSpan(secondRowOffset, 0x18).ToArray(), output.AsSpan(secondRowOffset, 0x18).ToArray());
        Assert.Equal(source.AsSpan(source.Length - 3, 3).ToArray(), output.AsSpan(source.Length - 3, 3).ToArray());
    }

    [Fact]
    public void ParseRejectsPartiallyOverlappingPackedMembers()
    {
        var source = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3, 4]),
            new SwShGfPackNamedFile("encount_k.bin", [5, 6, 7, 8]),
        ]).Write();
        var fileTableOffset = checked((int)BinaryPrimitives.ReadInt64LittleEndian(source.AsSpan(0x18, sizeof(long))));
        var firstPackedOffset = BinaryPrimitives.ReadInt32LittleEndian(
            source.AsSpan(fileTableOffset + 0x10, sizeof(int)));
        BinaryPrimitives.WriteInt32LittleEndian(
            source.AsSpan(fileTableOffset + 0x18 + 0x10, sizeof(int)),
            firstPackedOffset + 1);

        var error = Assert.Throws<InvalidDataException>(() => SwShGfPackFile.Parse(source));

        Assert.Contains("overlap unsafely", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsPackedMemberOverlappingFileTable()
    {
        var source = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3, 4]),
        ]).Write();
        var fileTableOffset = checked((int)BinaryPrimitives.ReadInt64LittleEndian(source.AsSpan(0x18, sizeof(long))));
        BinaryPrimitives.WriteInt32LittleEndian(
            source.AsSpan(fileTableOffset + 0x10, sizeof(int)),
            fileTableOffset);

        var error = Assert.Throws<InvalidDataException>(() => SwShGfPackFile.Parse(source));

        Assert.Contains("overlap unsafely", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseNormalizesOverflowingHeaderCountsToInvalidData()
    {
        var fileCountOverflow = new byte[0x28];
        BinaryPrimitives.WriteUInt64LittleEndian(fileCountOverflow, SwShGfPackFile.Magic);
        BinaryPrimitives.WriteInt32LittleEndian(fileCountOverflow.AsSpan(0x10), int.MaxValue);

        var folderCountOverflow = new byte[0x28];
        BinaryPrimitives.WriteUInt64LittleEndian(folderCountOverflow, SwShGfPackFile.Magic);
        BinaryPrimitives.WriteInt32LittleEndian(folderCountOverflow.AsSpan(0x14), int.MaxValue);

        Assert.Throws<InvalidDataException>(() => SwShGfPackFile.Parse(fileCountOverflow));
        Assert.Throws<InvalidDataException>(() => SwShGfPackFile.Parse(folderCountOverflow));
    }
}
