// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;
using Xunit;

namespace KM.SwSh.Tests.Text;

public sealed class SwShGameTextFileTests
{
    private const ushort BaseKey = 0x7C89;
    private const ushort KeyAdvance = 0x2983;

    [Fact]
    public void ParseReportsFormatGenericErrorForTruncatedHeader()
    {
        var exception = Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Parse(new byte[0x10]));

        Assert.Equal("Text file is too small to contain an encrypted game text header.", exception.Message);
    }

    [Fact]
    public void ParseReportsFormatGenericErrorForUnsupportedHeader()
    {
        var exception = Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Parse(new byte[0x14]));

        Assert.Equal("Text file header is not a supported encrypted game text table.", exception.Message);
    }

    [Fact]
    public void WriteEncodesTextControlEscapesAsSwordShieldControlVariables()
    {
        var data = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("A\\c\\nB\\r\\nC", Flags: 0),
        });

        var values = ReadDecryptedLineValues(data, lineIndex: 0);

        Assert.Equal(
            new ushort[]
            {
                'A',
                0x0010,
                0x0001,
                0xBE01,
                '\n',
                'B',
                0x0010,
                0x0001,
                0xBE00,
                '\n',
                'C',
                0x0000,
            },
            values);

        var parsed = SwShGameTextFile.Parse(data);
        Assert.Equal("A\\c\\nB\\r\\nC", parsed.Lines[0].Text);
    }

    [Fact]
    public void WriteRoundTripsVariablePlaceholders()
    {
        var data = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("A[VAR 0102(0001,00FF)]B", Flags: 0),
        });

        var values = ReadDecryptedLineValues(data, lineIndex: 0);

        Assert.Equal(
            new ushort[]
            {
                'A',
                0x0010,
                0x0003,
                0x0102,
                0x0001,
                0x00FF,
                'B',
                0x0000,
            },
            values);

        var parsed = SwShGameTextFile.Parse(data);
        Assert.Equal("A[VAR 0102(0001,00FF)]B", parsed.Lines[0].Text);
    }

    [Fact]
    public void WriteKeepsSupportedCompactAndSpacedVariableInput()
    {
        var data = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("A[VAR 102(1, 00FF)]B", Flags: 0),
        });

        var parsed = SwShGameTextFile.Parse(data);

        Assert.Equal("A[VAR 0102(0001,00FF)]B", parsed.Lines[0].Text);
    }

    [Fact]
    public void WriteKeepsEscapedVariablePlaceholderLiterals()
    {
        var data = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("A\\[VAR 0102(0001)]B", Flags: 0),
        });

        var parsed = SwShGameTextFile.Parse(data);

        Assert.Equal("A\\[VAR 0102(0001)]B", parsed.Lines[0].Text);
    }

    [Fact]
    public void WriteMapsWaitNullAndRubyControls()
    {
        const string text = "A[WAIT 0][WAIT 42][~ 0]{漢字|かんじ}B";
        var data = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine(text, Flags: 7),
        });

        var values = ReadDecryptedLineValues(data, lineIndex: 0);

        Assert.Equal(
            new ushort[]
            {
                'A',
                0x0010, 0x0002, 0xBE02, 0x0000,
                0x0010, 0x0002, 0xBE02, 0x002A,
                0x0010, 0x0001, 0xBDFF, 0x0000,
                0x0010, 0x0008, 0xFF01, 0x0002, 0x0003,
                '漢', '字', 'か', 'ん', 'じ', '漢', '字',
                'B',
                0x0000,
            },
            values);

        var parsed = SwShGameTextFile.Parse(data);
        Assert.Equal(text, parsed.Lines[0].Text);
        Assert.Equal((ushort)7, parsed.Lines[0].Flags);
    }

    [Fact]
    public void WriteRoundTripsEscapedRubyDelimitersAndAlternateBaseText()
    {
        const string text = "{a\\|b|r\\}b|c\\|d}";

        var data = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine(text, Flags: 0),
        });

        Assert.Equal(text, SwShGameTextFile.Parse(data).Lines[0].Text);
    }

    [Fact]
    public void WriteRoundTripsControlSyntaxInsideRubyFragments()
    {
        const string text = "{A\\c|[WAIT 2]|B\\c}";

        var data = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine(text, Flags: 0),
        });

        Assert.Equal(text, SwShGameTextFile.Parse(data).Lines[0].Text);
    }

    [Fact]
    public void WriteKeepsTerminatorOnlyEmptyTextDistinctFromNullLineControls()
    {
        var data = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("First", Flags: 0),
            new SwShGameTextLine(string.Empty, Flags: 9),
        });

        Assert.Equal(
            new ushort[] { 0x0000 },
            ReadDecryptedLineValues(data, lineIndex: 1));

        var parsed = SwShGameTextFile.Parse(data);
        Assert.Equal(string.Empty, parsed.Lines[1].Text);
        Assert.Equal((ushort)9, parsed.Lines[1].Flags);
    }

    [Fact]
    public void ParseAcceptsLegacyCountOneWaitControlsWithoutRewritingThem()
    {
        var source = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("[WAIT 0]", Flags: 0),
        });
        WriteDecryptedLineValues(
            source,
            lineIndex: 0,
            new ushort[] { 0x0010, 0x0001, 0xBE02, 0x0000, 0x0000 });

        var parsed = SwShGameTextFile.Parse(source);

        Assert.Equal("[WAIT 0]", parsed.Lines[0].Text);
        Assert.Equal(source, parsed.WritePreserving(parsed.Lines));
    }

    [Fact]
    public void WritePreservingKeepsUntouchedNoncanonicalControlBytes()
    {
        var source = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("[VAR 0102(0000)]", Flags: 3),
            new SwShGameTextLine("Original", Flags: 5),
        });
        WriteDecryptedLineValues(
            source,
            lineIndex: 0,
            new ushort[] { 0x0010, 0x0002, 0xBDFF, 0x0000, 0x0000 });

        var parsed = SwShGameTextFile.Parse(source);
        Assert.Equal("[~ 0]", parsed.Lines[0].Text);
        Assert.Equal(source, parsed.WritePreserving(parsed.Lines));
        Assert.False(parsed.Lines is SwShGameTextLine[]);
        var originalEncryptedLine = ReadEncryptedLine(source, lineIndex: 0);

        var editedLines = parsed.Lines.ToArray();
        editedLines[1] = editedLines[1] with { Text = "A longer replacement" };
        var output = parsed.WritePreserving(editedLines);

        Assert.Equal(originalEncryptedLine, ReadEncryptedLine(output, lineIndex: 0));
        var reparsed = SwShGameTextFile.Parse(output);
        Assert.Equal("[~ 0]", reparsed.Lines[0].Text);
        Assert.Equal((ushort)3, reparsed.Lines[0].Flags);
        Assert.Equal("A longer replacement", reparsed.Lines[1].Text);
        Assert.Equal(output, reparsed.WritePreserving(reparsed.Lines));
    }

    [Fact]
    public void WritePreservingKeepsOriginalLineCapacityWhenReplacementFits()
    {
        var source = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("Original", Flags: 3),
            new SwShGameTextLine("Following", Flags: 5),
        });
        var originalFirstValues = ReadDecryptedLineValues(source, lineIndex: 0);
        Array.Resize(ref originalFirstValues, originalFirstValues.Length + 4);
        source = RebuildWithLineValues(source, lineIndex: 0, originalFirstValues);
        var originalSecondEntry = ReadLineEntry(source, lineIndex: 1);
        var originalLength = source.Length;

        var parsed = SwShGameTextFile.Parse(source);
        var editedLines = parsed.Lines.ToArray();
        editedLines[0] = editedLines[0] with { Text = "Short", Flags = 7 };
        var output = parsed.WritePreserving(editedLines);

        Assert.Equal(originalLength, output.Length);
        Assert.Equal(originalSecondEntry, ReadLineEntry(output, lineIndex: 1));
        Assert.Equal(originalFirstValues.Length, ReadDecryptedLineValues(output, lineIndex: 0).Length);
        Assert.Equal("Short", SwShGameTextFile.Parse(output).Lines[0].Text);
        Assert.Equal((ushort)7, SwShGameTextFile.Parse(output).Lines[0].Flags);
    }

    [Fact]
    public void ParseAllowsZeroCapacityAfterTerminatorButRejectsNonzeroData()
    {
        var source = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("A", Flags: 0),
        });
        var padded = RebuildWithLineValues(source, lineIndex: 0, new ushort[] { 'A', 0x0000, 0x0000, 0x0000 });

        Assert.Equal("A", SwShGameTextFile.Parse(padded).Lines[0].Text);

        WriteDecryptedLineValues(padded, lineIndex: 0, new ushort[] { 'A', 0x0000, 0x0001, 0x0000 });
        Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Parse(padded));
    }

    [Fact]
    public void ParseRejectsNestedRubyControlsThatCannotBeEditedSafely()
    {
        var source = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine(new string('A', 22), Flags: 0),
        });
        ushort[] nestedRuby =
        [
            0x0010, 0x0005, 0xFF01, 0x0001, 0x0001, 'A', 'b', 'A',
        ];
        ushort[] values =
        [
            0x0010, 0x000C, 0xFF01, 0x0008, 0x0001,
            .. nestedRuby,
            'r',
            .. nestedRuby,
            0x0000,
        ];
        WriteDecryptedLineValues(source, lineIndex: 0, values);

        var exception = Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Parse(source));

        Assert.Contains("Nested ruby", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMalformedLineStructuresAndVariables()
    {
        var payloadInTable = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("A", Flags: 0),
        });
        var sectionStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(payloadInTable.AsSpan(0x0C));
        BinaryPrimitives.WriteInt32LittleEndian(payloadInTable.AsSpan(sectionStart + sizeof(uint)), sizeof(uint));
        Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Parse(payloadInTable));

        var missingTerminator = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("A", Flags: 0),
        });
        WriteDecryptedLineValues(missingTerminator, lineIndex: 0, new ushort[] { 'A', 'B' });
        Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Parse(missingTerminator));

        var truncatedVariable = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("[VAR 0102]", Flags: 0),
        });
        WriteDecryptedLineValues(
            truncatedVariable,
            lineIndex: 0,
            new ushort[] { 0x0010, 0x0004, 0x0102, 0x0000 });
        Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Parse(truncatedVariable));

        var overlappingLines = SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine("A", Flags: 0),
            new SwShGameTextLine("B", Flags: 0),
        });
        sectionStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(overlappingLines.AsSpan(0x0C));
        var firstEntry = sectionStart + sizeof(uint);
        var secondEntry = firstEntry + 0x08;
        var firstOffset = BinaryPrimitives.ReadInt32LittleEndian(overlappingLines.AsSpan(firstEntry));
        BinaryPrimitives.WriteInt32LittleEndian(overlappingLines.AsSpan(secondEntry), firstOffset);
        Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Parse(overlappingLines));
    }

    [Fact]
    public void WriteRejectsReservedValuesAndMalformedControls()
    {
        var invalidValues = new[]
        {
            "A\0B",
            "A\u0010B",
            "\uD800",
            "A\\q",
            "A\\",
            "A\\}",
            "[VAR 0102(",
            "[VAR 0102()]",
            "[VAR BDFF]",
            "[VAR FF01(0001)]",
            "[WAIT value]",
            "{base}",
        };

        foreach (var invalidValue in invalidValues)
        {
            Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Write(new[]
            {
                new SwShGameTextLine(invalidValue, Flags: 0),
            }));
        }
    }

    [Fact]
    public void WriteRejectsEncodedLinesThatExceedTheFormatLength()
    {
        var oversized = new string('A', ushort.MaxValue);

        Assert.Throws<InvalidDataException>(() => SwShGameTextFile.Write(new[]
        {
            new SwShGameTextLine(oversized, Flags: 0),
        }));
    }

    private static ushort[] ReadDecryptedLineValues(byte[] data, int lineIndex)
    {
        var lineData = ReadEncryptedLine(data, lineIndex);
        CryptLineData(lineData, GetLineKey(lineIndex));

        var values = new ushort[lineData.Length / sizeof(ushort)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16LittleEndian(lineData.AsSpan(i * sizeof(ushort)));
        }

        return values;
    }

    private static byte[] ReadEncryptedLine(byte[] data, int lineIndex)
    {
        var sectionStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x0C));
        var lineEntryOffset = sectionStart + sizeof(uint) + (lineIndex * 0x08);
        var textOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(lineEntryOffset));
        var textLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(lineEntryOffset + 0x04));
        return data.AsSpan(sectionStart + textOffset, textLength * sizeof(ushort)).ToArray();
    }

    private static byte[] ReadLineEntry(byte[] data, int lineIndex)
    {
        var sectionStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x0C));
        var lineEntryOffset = sectionStart + sizeof(uint) + (lineIndex * 0x08);
        return data.AsSpan(lineEntryOffset, 0x08).ToArray();
    }

    private static byte[] RebuildWithLineValues(byte[] source, int lineIndex, IReadOnlyList<ushort> values)
    {
        var sectionStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(0x0C));
        var lineCount = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(0x02));
        var entries = Enumerable.Range(0, lineCount)
            .Select(index => ReadDecryptedLineValues(source, index))
            .ToArray();
        entries[lineIndex] = values.ToArray();

        var sectionLength = sizeof(uint) + (lineCount * 0x08);
        var offsets = new int[lineCount];
        for (var i = 0; i < entries.Length; i++)
        {
            offsets[i] = sectionLength;
            sectionLength = (sectionLength + (entries[i].Length * sizeof(ushort)) + 3) & ~3;
        }

        var data = new byte[sectionStart + sectionLength];
        source.AsSpan(0, sectionStart).CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x04), (uint)sectionLength);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionStart), (uint)sectionLength);

        for (var i = 0; i < entries.Length; i++)
        {
            var sourceEntry = ReadLineEntry(source, i);
            var entryOffset = sectionStart + sizeof(uint) + (i * 0x08);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entryOffset), offsets[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset + 0x04), checked((ushort)entries[i].Length));
            sourceEntry.AsSpan(0x06, sizeof(ushort)).CopyTo(data.AsSpan(entryOffset + 0x06));

            var encrypted = new byte[entries[i].Length * sizeof(ushort)];
            for (var valueIndex = 0; valueIndex < entries[i].Length; valueIndex++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    encrypted.AsSpan(valueIndex * sizeof(ushort)),
                    entries[i][valueIndex]);
            }

            CryptLineData(encrypted, GetLineKey(i));
            encrypted.CopyTo(data.AsSpan(sectionStart + offsets[i]));
        }

        return data;
    }

    private static void WriteDecryptedLineValues(byte[] data, int lineIndex, IReadOnlyList<ushort> values)
    {
        var encrypted = ReadEncryptedLine(data, lineIndex);
        Assert.Equal(encrypted.Length / sizeof(ushort), values.Count);

        var decrypted = new byte[encrypted.Length];
        for (var i = 0; i < values.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(decrypted.AsSpan(i * sizeof(ushort)), values[i]);
        }

        CryptLineData(decrypted, GetLineKey(lineIndex));
        var sectionStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x0C));
        var lineEntryOffset = sectionStart + sizeof(uint) + (lineIndex * 0x08);
        var textOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(lineEntryOffset));
        decrypted.CopyTo(data.AsSpan(sectionStart + textOffset));
    }

    private static ushort GetLineKey(int index)
    {
        var key = BaseKey;
        for (var i = 0; i < index; i++)
        {
            key += KeyAdvance;
        }

        return key;
    }

    private static void CryptLineData(Span<byte> data, ushort key)
    {
        for (var offset = 0; offset + sizeof(ushort) <= data.Length; offset += sizeof(ushort))
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], (ushort)(value ^ key));
            key = (ushort)((key << 3) | (key >> 13));
        }
    }
}
