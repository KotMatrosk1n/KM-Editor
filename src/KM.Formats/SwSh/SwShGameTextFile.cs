// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.Formats.SwSh;

public sealed record SwShGameTextLine(string Text, ushort Flags);

public sealed class SwShGameTextFile
{
    private const ushort BaseKey = 0x7C89;
    private const ushort KeyAdvance = 0x2983;
    private const ushort Terminator = 0x0000;
    private const ushort VariableMarker = 0x0010;
    private const ushort TextReturn = 0xBE00;
    private const ushort TextClear = 0xBE01;
    private const ushort IndexedNameVariable = 0xBDFF;
    private const int HeaderSize = 0x10;
    private const int LineEntrySize = 0x08;

    private SwShGameTextFile(IReadOnlyList<SwShGameTextLine> lines)
    {
        Lines = lines;
    }

    public IReadOnlyList<SwShGameTextLine> Lines { get; }

    public static SwShGameTextFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize + sizeof(uint))
        {
            throw new InvalidDataException("Text file is too small to contain a Sword/Shield text header.");
        }

        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var lineCount = BinaryPrimitives.ReadUInt16LittleEndian(data[0x02..]);
        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(data[0x04..]);
        var initialKey = BinaryPrimitives.ReadUInt32LittleEndian(data[0x08..]);
        var sectionOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[0x0C..]);

        if (sectionCount != 1 || initialKey != 0 || sectionOffset > int.MaxValue)
        {
            throw new InvalidDataException("Text file header is not a supported Sword/Shield text table.");
        }

        if (sectionOffset + totalLength != data.Length)
        {
            throw new InvalidDataException("Text file section length does not match the file length.");
        }

        var sectionStart = (int)sectionOffset;
        var sectionLength = BinaryPrimitives.ReadUInt32LittleEndian(data[sectionStart..]);
        if (sectionLength != totalLength)
        {
            throw new InvalidDataException("Text file section length does not match the total length.");
        }

        var lineTableStart = sectionStart + sizeof(uint);
        var lineTableLength = checked(lineCount * LineEntrySize);
        if (lineTableStart + lineTableLength > data.Length)
        {
            throw new InvalidDataException("Text file line table extends past the end of the file.");
        }

        var lines = new SwShGameTextLine[lineCount];
        for (var i = 0; i < lines.Length; i++)
        {
            var entryOffset = lineTableStart + (i * LineEntrySize);
            var textOffset = BinaryPrimitives.ReadInt32LittleEndian(data[entryOffset..]);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data[(entryOffset + 0x04)..]);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(data[(entryOffset + 0x06)..]);
            var byteLength = checked(length * sizeof(ushort));
            var textStart = sectionStart + textOffset;

            if (textOffset < 0 || textStart < sectionStart || textStart + byteLength > data.Length)
            {
                throw new InvalidDataException($"Text line {i} points outside the file.");
            }

            var encrypted = data.Slice(textStart, byteLength).ToArray();
            CryptLineData(encrypted, GetLineKey(i));
            lines[i] = new SwShGameTextLine(DecodeLine(encrypted), flags);
        }

        return new SwShGameTextFile(lines);
    }

    public static byte[] Write(IReadOnlyList<SwShGameTextLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(lines), "Sword/Shield text files cannot contain more than 65535 lines.");
        }

        var encryptedLines = new byte[lines.Count][];
        for (var i = 0; i < lines.Count; i++)
        {
            encryptedLines[i] = EncodeLine(lines[i].Text);
            CryptLineData(encryptedLines[i], GetLineKey(i));
        }

        var sectionLength = sizeof(uint) + (lines.Count * LineEntrySize);
        var offsets = new int[lines.Count];
        for (var i = 0; i < encryptedLines.Length; i++)
        {
            offsets[i] = sectionLength;
            sectionLength += encryptedLines[i].Length;
            if (sectionLength % sizeof(uint) == sizeof(ushort))
            {
                sectionLength += sizeof(ushort);
            }
        }

        var data = new byte[HeaderSize + sectionLength];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x02), (ushort)lines.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x04), (uint)sectionLength);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x0C), HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(HeaderSize), (uint)sectionLength);

        var lineTableStart = HeaderSize + sizeof(uint);
        for (var i = 0; i < encryptedLines.Length; i++)
        {
            var entryOffset = lineTableStart + (i * LineEntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entryOffset), offsets[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset + 0x04), (ushort)(encryptedLines[i].Length / sizeof(ushort)));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset + 0x06), lines[i].Flags);
            encryptedLines[i].CopyTo(data.AsSpan(HeaderSize + offsets[i]));
        }

        return data;
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

    private static string DecodeLine(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder();

        for (var offset = 0; offset + sizeof(ushort) <= data.Length;)
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            offset += sizeof(ushort);

            if (value == Terminator)
            {
                break;
            }

            if (value == VariableMarker)
            {
                if (!TryAppendVariablePlaceholder(data, builder, ref offset))
                {
                    builder.Append("[VAR]");
                    break;
                }

                continue;
            }

            builder.Append(value switch
            {
                (ushort)'\n' => "\\n",
                (ushort)'\\' => "\\\\",
                (ushort)'[' => "\\[",
                (ushort)'{' => "\\{",
                _ => ((char)value).ToString(),
            });
        }

        return builder.ToString();
    }

    private static bool TryAppendVariablePlaceholder(ReadOnlySpan<byte> data, StringBuilder builder, ref int offset)
    {
        if (offset + (sizeof(ushort) * 2) > data.Length)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += sizeof(ushort);
        var variable = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += sizeof(ushort);

        if (count == 1 && variable == TextReturn)
        {
            builder.Append("\\r");
            return true;
        }

        if (count == 1 && variable == TextClear)
        {
            builder.Append("\\c");
            return true;
        }

        if (count == 1
            && variable == IndexedNameVariable
            && offset + sizeof(ushort) <= data.Length)
        {
            var argument = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            if (argument != Terminator)
            {
                offset += sizeof(ushort);
                builder.Append("[VAR ")
                    .Append(variable.ToString("X4", CultureInfo.InvariantCulture))
                    .Append('(')
                    .Append(argument.ToString("X4", CultureInfo.InvariantCulture))
                    .Append(")]");
                return true;
            }
        }

        var argumentCount = Math.Max(0, count - 1);
        var arguments = new List<string>(argumentCount);

        for (var i = 0; i < argumentCount && offset + sizeof(ushort) <= data.Length; i++)
        {
            arguments.Add(BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]).ToString("X4", CultureInfo.InvariantCulture));
            offset += sizeof(ushort);
        }

        builder.Append("[VAR ").Append(variable.ToString("X4", CultureInfo.InvariantCulture));
        if (arguments.Count > 0)
        {
            builder.Append('(').Append(string.Join(',', arguments)).Append(')');
        }

        builder.Append(']');
        return true;
    }

    private static byte[] EncodeLine(string text)
    {
        var values = new List<ushort>(text.Length + 1);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                var escaped = text[++i];
                switch (escaped)
                {
                    case 'n':
                        values.Add('\n');
                        break;
                    case '\\':
                        values.Add('\\');
                        break;
                    case '[':
                        values.Add('[');
                        break;
                    case '{':
                        values.Add('{');
                        break;
                    case 'r':
                        values.Add(VariableMarker);
                        values.Add(1);
                        values.Add(TextReturn);
                        break;
                    case 'c':
                        values.Add(VariableMarker);
                        values.Add(1);
                        values.Add(TextClear);
                        break;
                    default:
                        values.Add('\\');
                        values.Add(escaped);
                        break;
                }
                continue;
            }

            if (text[i] == '[' && TryAppendVariable(text, i, values, out var consumed))
            {
                i += consumed - 1;
                continue;
            }

            values.Add(text[i]);
        }

        values.Add(Terminator);

        var data = new byte[values.Count * sizeof(ushort)];
        for (var i = 0; i < values.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * sizeof(ushort)), values[i]);
        }

        return data;
    }

    private static bool TryAppendVariable(string text, int start, ICollection<ushort> values, out int consumed)
    {
        consumed = 0;
        const string prefix = "[VAR ";
        if (!text.AsSpan(start).StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var end = text.IndexOf(']', start + prefix.Length);
        if (end < 0)
        {
            return false;
        }

        var body = text[(start + prefix.Length)..end];
        var argumentStart = body.IndexOf('(', StringComparison.Ordinal);
        var variableText = argumentStart < 0 ? body : body[..argumentStart];
        if (!ushort.TryParse(variableText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var variable))
        {
            return false;
        }

        var arguments = new List<ushort>();
        if (argumentStart >= 0)
        {
            if (!body.EndsWith(")", StringComparison.Ordinal))
            {
                return false;
            }

            var argumentText = body[(argumentStart + 1)..^1];
            foreach (var argumentPart in argumentText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!ushort.TryParse(argumentPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argument))
                {
                    return false;
                }

                arguments.Add(argument);
            }
        }

        values.Add(VariableMarker);
        values.Add(CreateVariableCount(variable, arguments.Count));
        values.Add(variable);
        foreach (var argument in arguments)
        {
            values.Add(argument);
        }

        consumed = end - start + 1;
        return true;
    }

    private static ushort CreateVariableCount(ushort variable, int argumentCount)
    {
        if (variable == IndexedNameVariable && argumentCount == 1)
        {
            return 1;
        }

        return checked((ushort)(argumentCount + 1));
    }
}
