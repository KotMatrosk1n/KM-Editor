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

    private static ushort[] ReadDecryptedLineValues(byte[] data, int lineIndex)
    {
        var sectionStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x0C));
        var lineEntryOffset = sectionStart + sizeof(uint) + (lineIndex * 0x08);
        var textOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(lineEntryOffset));
        var textLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(lineEntryOffset + 0x04));
        var lineData = data.AsSpan(sectionStart + textOffset, textLength * sizeof(ushort)).ToArray();
        CryptLineData(lineData, GetLineKey(lineIndex));

        var values = new ushort[textLength];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16LittleEndian(lineData.AsSpan(i * sizeof(ushort)));
        }

        return values;
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
