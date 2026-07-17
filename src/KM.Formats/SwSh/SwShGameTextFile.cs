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
    private const ushort TextWait = 0xBE02;
    private const ushort TextNull = 0xBDFF;
    private const ushort TextRuby = 0xFF01;
    private const int HeaderSize = 0x10;
    private const int LineEntrySize = 0x08;

    private readonly byte[]? _sourceData;
    private readonly byte[][]? _sourceEncryptedLines;
    private readonly int _sectionOffset;

    private SwShGameTextFile(
        IReadOnlyList<SwShGameTextLine> lines,
        byte[] sourceData,
        byte[][] sourceEncryptedLines,
        int sectionOffset)
    {
        Lines = Array.AsReadOnly(lines.ToArray());
        _sourceData = sourceData;
        _sourceEncryptedLines = sourceEncryptedLines;
        _sectionOffset = sectionOffset;
    }

    public IReadOnlyList<SwShGameTextLine> Lines { get; }

    public static SwShGameTextFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize + sizeof(uint))
        {
            throw new InvalidDataException("Text file is too small to contain an encrypted game text header.");
        }

        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var lineCount = BinaryPrimitives.ReadUInt16LittleEndian(data[0x02..]);
        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(data[0x04..]);
        var initialKey = BinaryPrimitives.ReadUInt32LittleEndian(data[0x08..]);
        var sectionOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[0x0C..]);

        if (sectionCount != 1
            || initialKey != 0
            || sectionOffset < HeaderSize
            || sectionOffset > int.MaxValue
            || sectionOffset > data.Length - sizeof(uint)
            || totalLength > int.MaxValue)
        {
            throw new InvalidDataException("Text file header is not a supported encrypted game text table.");
        }

        var sectionStart = (int)sectionOffset;
        var sectionSize = (int)totalLength;
        if (sectionSize != data.Length - sectionStart)
        {
            throw new InvalidDataException("Text file section length does not match the file length.");
        }

        var sectionLength = BinaryPrimitives.ReadUInt32LittleEndian(data[sectionStart..]);
        if (sectionLength != totalLength)
        {
            throw new InvalidDataException("Text file section length does not match the total length.");
        }

        var lineTableStart = sectionStart + sizeof(uint);
        var lineTableLength = checked(lineCount * LineEntrySize);
        if (lineTableLength > data.Length - lineTableStart)
        {
            throw new InvalidDataException("Text file line table extends past the end of the file.");
        }

        var lineDataOffset = checked(sizeof(uint) + lineTableLength);
        var lines = new SwShGameTextLine[lineCount];
        var encryptedLines = new byte[lineCount][];
        var ranges = new (int Start, int End)[lineCount];
        var key = BaseKey;

        for (var i = 0; i < lines.Length; i++)
        {
            var entryOffset = lineTableStart + (i * LineEntrySize);
            var textOffset = BinaryPrimitives.ReadInt32LittleEndian(data[entryOffset..]);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data[(entryOffset + 0x04)..]);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(data[(entryOffset + 0x06)..]);
            var byteLength = checked(length * sizeof(ushort));

            if (length == 0
                || textOffset < lineDataOffset
                || (textOffset & 1) != 0
                || textOffset > sectionSize
                || byteLength > sectionSize - textOffset)
            {
                throw new InvalidDataException($"Text line {i} points outside the line data area.");
            }

            var textStart = sectionStart + textOffset;
            var encrypted = data.Slice(textStart, byteLength).ToArray();
            encryptedLines[i] = encrypted;
            ranges[i] = (textOffset, checked(textOffset + byteLength));

            var decrypted = encrypted.ToArray();
            CryptLineData(decrypted, key);
            try
            {
                lines[i] = new SwShGameTextLine(DecodeLine(decrypted, encrypted), flags);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException($"Text line {i} is malformed: {exception.Message}", exception);
            }

            key = unchecked((ushort)(key + KeyAdvance));
        }

        ValidateUnownedSectionBytes(data[sectionStart..], lineDataOffset, ranges);
        return new SwShGameTextFile(lines, data.ToArray(), encryptedLines, sectionStart);
    }

    public static byte[] Write(IReadOnlyList<SwShGameTextLine> lines)
    {
        return WriteCore(lines, source: null);
    }

    public static void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _ = EncodeLine(text, lineIndex: 0);
    }

    public byte[] WritePreserving(IReadOnlyList<SwShGameTextLine> lines)
    {
        return WriteCore(lines, this);
    }

    private static byte[] WriteCore(IReadOnlyList<SwShGameTextLine> lines, SwShGameTextFile? source)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(lines), "Sword/Shield text files cannot contain more than 65535 lines.");
        }

        if (source is not null && lines.Count != source.Lines.Count)
        {
            throw new InvalidDataException("Source-preserving text writes cannot change the number of lines.");
        }

        if (source is not null && TextValuesMatch(lines, source.Lines))
        {
            var preserved = source._sourceData!.ToArray();
            var sourceLineTableStart = source._sectionOffset + sizeof(uint);
            for (var i = 0; i < lines.Count; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    preserved.AsSpan(sourceLineTableStart + (i * LineEntrySize) + 0x06),
                    lines[i].Flags);
            }

            return preserved;
        }

        if (source is not null && TryWriteWithinSourceCapacity(lines, source, out var capacityPreserved))
        {
            return capacityPreserved;
        }

        var encryptedLines = new byte[lines.Count][];
        var key = BaseKey;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i] ?? throw new ArgumentException("Text lines cannot contain null entries.", nameof(lines));
            if (line.Text is null)
            {
                throw new ArgumentException("Text line values cannot be null.", nameof(lines));
            }

            if (source is not null && string.Equals(line.Text, source.Lines[i].Text, StringComparison.Ordinal))
            {
                encryptedLines[i] = source._sourceEncryptedLines![i].ToArray();
            }
            else
            {
                encryptedLines[i] = EncodeLine(line.Text, i);
                CryptLineData(encryptedLines[i], key);
            }

            if (encryptedLines[i].Length / sizeof(ushort) > ushort.MaxValue)
            {
                throw new InvalidDataException($"Text line {i} is too long for the Sword/Shield text format.");
            }

            key = unchecked((ushort)(key + KeyAdvance));
        }

        var sectionLength = checked(sizeof(uint) + (lines.Count * LineEntrySize));
        var offsets = new int[lines.Count];
        for (var i = 0; i < encryptedLines.Length; i++)
        {
            offsets[i] = sectionLength;
            sectionLength = checked(sectionLength + encryptedLines[i].Length);
            sectionLength = AlignToFourBytes(sectionLength);
        }

        var sectionStart = source?._sectionOffset ?? HeaderSize;
        var data = new byte[checked(sectionStart + sectionLength)];
        if (source is not null)
        {
            source._sourceData!.AsSpan(0, sectionStart).CopyTo(data);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x02), (ushort)lines.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x04), (uint)sectionLength);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x08), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x0C), (uint)sectionStart);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(sectionStart), (uint)sectionLength);

        var lineTableStart = sectionStart + sizeof(uint);
        for (var i = 0; i < encryptedLines.Length; i++)
        {
            var entryOffset = lineTableStart + (i * LineEntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entryOffset), offsets[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(entryOffset + 0x04),
                checked((ushort)(encryptedLines[i].Length / sizeof(ushort))));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset + 0x06), lines[i].Flags);
            encryptedLines[i].CopyTo(data.AsSpan(sectionStart + offsets[i]));
        }

        return data;
    }

    private static bool TryWriteWithinSourceCapacity(
        IReadOnlyList<SwShGameTextLine> lines,
        SwShGameTextFile source,
        out byte[] data)
    {
        data = source._sourceData!.ToArray();
        var lineTableStart = source._sectionOffset + sizeof(uint);
        var key = BaseKey;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i] ?? throw new ArgumentException("Text lines cannot contain null entries.", nameof(lines));
            if (line.Text is null)
            {
                throw new ArgumentException("Text line values cannot be null.", nameof(lines));
            }

            var entryOffset = lineTableStart + (i * LineEntrySize);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset + 0x06), line.Flags);

            if (!string.Equals(line.Text, source.Lines[i].Text, StringComparison.Ordinal))
            {
                var encoded = EncodeLine(line.Text, i);
                var capacity = source._sourceEncryptedLines![i].Length;
                if (encoded.Length > capacity)
                {
                    data = Array.Empty<byte>();
                    return false;
                }

                var padded = new byte[capacity];
                encoded.CopyTo(padded, 0);
                CryptLineData(padded, key);

                var textOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(entryOffset));
                padded.CopyTo(data.AsSpan(source._sectionOffset + textOffset));
            }

            key = unchecked((ushort)(key + KeyAdvance));
        }

        return true;
    }

    private static bool TextValuesMatch(IReadOnlyList<SwShGameTextLine> left, IReadOnlyList<SwShGameTextLine> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] is null
                || !string.Equals(left[i].Text, right[i].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int AlignToFourBytes(int value)
    {
        return checked((value + 3) & ~3);
    }

    private static void ValidateUnownedSectionBytes(
        ReadOnlySpan<byte> section,
        int lineDataOffset,
        IReadOnlyList<(int Start, int End)> ranges)
    {
        var orderedRanges = ranges.OrderBy(range => range.Start).ToArray();
        var cursor = lineDataOffset;
        foreach (var range in orderedRanges)
        {
            if (range.Start < cursor)
            {
                throw new InvalidDataException("Text line payloads overlap.");
            }

            if (ContainsNonZeroByte(section[cursor..range.Start]))
            {
                throw new InvalidDataException("Text file contains unsupported data between line payloads.");
            }

            cursor = range.End;
        }

        if (ContainsNonZeroByte(section[cursor..]))
        {
            throw new InvalidDataException("Text file contains unsupported data after the line payloads.");
        }
    }

    private static bool ContainsNonZeroByte(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value != 0)
            {
                return true;
            }
        }

        return false;
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

    private static string DecodeLine(ReadOnlySpan<byte> data, ReadOnlySpan<byte> encryptedData)
    {
        var builder = new StringBuilder();
        var offset = 0;
        var foundTerminator = false;

        while (offset < data.Length)
        {
            var value = ReadWord(data, ref offset, "text value");
            if (value == Terminator)
            {
                foundTerminator = true;
                break;
            }

            AppendDecodedValue(data, builder, ref offset, value, escapeRubyDelimiters: false);
        }

        if (!foundTerminator)
        {
            throw new InvalidDataException("Text line has no terminator.");
        }

        if (ContainsNonZeroByte(data[offset..])
            && !IsLegacyRawZeroAlignment(encryptedData, offset))
        {
            throw new InvalidDataException("Text line contains nonzero data after its terminator.");
        }

        var text = builder.ToString();
        ValidateUtf16(text);
        return text;
    }

    private static bool IsLegacyRawZeroAlignment(ReadOnlySpan<byte> encryptedData, int trailingOffset)
    {
        var trailingData = encryptedData[trailingOffset..];
        return encryptedData.Length % 4 == 0
            && trailingData.Length == sizeof(ushort)
            && !ContainsNonZeroByte(trailingData);
    }

    private static string DecodeFragment(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder();
        var offset = 0;
        while (offset < data.Length)
        {
            var value = ReadWord(data, ref offset, "ruby text value");
            if (value == Terminator)
            {
                throw new InvalidDataException("Ruby text contains an unexpected terminator.");
            }

            AppendDecodedValue(data, builder, ref offset, value, escapeRubyDelimiters: true);
        }

        return builder.ToString();
    }

    private static void AppendDecodedValue(
        ReadOnlySpan<byte> data,
        StringBuilder builder,
        ref int offset,
        ushort value,
        bool escapeRubyDelimiters)
    {
        if (value == VariableMarker)
        {
            AppendVariablePlaceholder(data, builder, ref offset, allowRuby: !escapeRubyDelimiters);
            return;
        }

        switch (value)
        {
            case (ushort)'\n':
                builder.Append("\\n");
                break;
            case (ushort)'\\':
                builder.Append("\\\\");
                break;
            case (ushort)'[':
                builder.Append("\\[");
                break;
            case (ushort)'{':
                builder.Append("\\{");
                break;
            case (ushort)'|' when escapeRubyDelimiters:
                builder.Append("\\|");
                break;
            case (ushort)'}' when escapeRubyDelimiters:
                builder.Append("\\}");
                break;
            default:
                builder.Append((char)value);
                break;
        }
    }

    private static void AppendVariablePlaceholder(
        ReadOnlySpan<byte> data,
        StringBuilder builder,
        ref int offset,
        bool allowRuby)
    {
        var count = ReadWord(data, ref offset, "variable count");
        var variable = ReadWord(data, ref offset, "variable code");
        if (count == 0)
        {
            throw new InvalidDataException("Text variable count cannot be zero.");
        }

        switch (variable)
        {
            case TextReturn:
                RequireVariableCount(count, expected: 1, variable);
                builder.Append("\\r");
                return;
            case TextClear:
                RequireVariableCount(count, expected: 1, variable);
                builder.Append("\\c");
                return;
            case TextWait:
                if (count is not 1 and not 2)
                {
                    throw new InvalidDataException(
                        $"Text variable {variable:X4} has count {count}, but count 1 or 2 is required.");
                }

                builder.Append("[WAIT ")
                    .Append(ReadWord(data, ref offset, "WAIT duration").ToString(CultureInfo.InvariantCulture))
                    .Append(']');
                return;
            case TextNull:
                if (count is not 1 and not 2)
                {
                    throw new InvalidDataException(
                        $"Text variable {variable:X4} has count {count}, but count 1 or 2 is required.");
                }

                builder.Append("[~ ")
                    .Append(ReadWord(data, ref offset, "null-line index").ToString(CultureInfo.InvariantCulture))
                    .Append(']');
                return;
            case TextRuby:
                if (!allowRuby)
                {
                    throw new InvalidDataException("Nested ruby text controls are not supported.");
                }

                AppendRubyPlaceholder(data, builder, ref offset, count);
                return;
        }

        var argumentCount = count - 1;
        if (argumentCount > (data.Length - offset) / sizeof(ushort))
        {
            throw new InvalidDataException("Text variable arguments extend past the line payload.");
        }

        builder.Append("[VAR ").Append(variable.ToString("X4", CultureInfo.InvariantCulture));
        if (argumentCount > 0)
        {
            builder.Append('(');
            for (var i = 0; i < argumentCount; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(ReadWord(data, ref offset, "variable argument").ToString("X4", CultureInfo.InvariantCulture));
            }

            builder.Append(')');
        }

        builder.Append(']');
    }

    private static void AppendRubyPlaceholder(
        ReadOnlySpan<byte> data,
        StringBuilder builder,
        ref int offset,
        ushort count)
    {
        var baseLength = ReadWord(data, ref offset, "ruby base length");
        var rubyLength = ReadWord(data, ref offset, "ruby annotation length");
        var expectedCount = checked(3 + baseLength + rubyLength);
        if (count != expectedCount)
        {
            throw new InvalidDataException("Ruby text variable count does not match its lengths.");
        }

        var payloadWords = checked((baseLength * 2) + rubyLength);
        var payloadBytes = checked(payloadWords * sizeof(ushort));
        if (payloadBytes > data.Length - offset)
        {
            throw new InvalidDataException("Ruby text extends past the line payload.");
        }

        var baseByteLength = baseLength * sizeof(ushort);
        var rubyByteLength = rubyLength * sizeof(ushort);
        var firstBase = data.Slice(offset, baseByteLength);
        offset += baseByteLength;
        var ruby = data.Slice(offset, rubyByteLength);
        offset += rubyByteLength;
        var secondBase = data.Slice(offset, baseByteLength);
        offset += baseByteLength;

        builder.Append('{')
            .Append(DecodeFragment(firstBase))
            .Append('|')
            .Append(DecodeFragment(ruby));
        if (!firstBase.SequenceEqual(secondBase))
        {
            builder.Append('|').Append(DecodeFragment(secondBase));
        }

        builder.Append('}');
    }

    private static void RequireVariableCount(ushort actual, ushort expected, ushort variable)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Text variable {variable:X4} has count {actual}, but count {expected} is required.");
        }
    }

    private static ushort ReadWord(ReadOnlySpan<byte> data, ref int offset, string description)
    {
        if (offset > data.Length - sizeof(ushort))
        {
            throw new InvalidDataException($"Text line ends inside its {description}.");
        }

        var value = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += sizeof(ushort);
        return value;
    }

    private static byte[] EncodeLine(string text, int lineIndex)
    {
        ValidateUtf16(text);
        if (text.Length > ushort.MaxValue)
        {
            throw new InvalidDataException($"Text line {lineIndex} is too long for the Sword/Shield text format.");
        }

        var initialCapacity = Math.Min(text.Length, ushort.MaxValue - 5) + 5;
        var values = new List<ushort>(initialCapacity);
        AppendEncodedText(text, values);

        values.Add(Terminator);
        if (values.Count > ushort.MaxValue)
        {
            throw new InvalidDataException($"Text line {lineIndex} is too long for the Sword/Shield text format.");
        }

        var data = new byte[checked(values.Count * sizeof(ushort))];
        for (var i = 0; i < values.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * sizeof(ushort)), values[i]);
        }

        return data;
    }

    private static void AppendEncodedText(
        string text,
        ICollection<ushort> values,
        bool allowRubyDelimiters = false)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var value = text[i];
            if (value == (char)Terminator || value == (char)VariableMarker)
            {
                throw new InvalidDataException($"Raw U+{(ushort)value:X4} is reserved by the Sword/Shield text format.");
            }

            if (value == '\\')
            {
                if (i + 1 >= text.Length)
                {
                    throw new InvalidDataException("Text cannot end with an incomplete escape sequence.");
                }

                AppendEscape(text[++i], values, allowRubyDelimiters);
                continue;
            }

            if (value == '[')
            {
                var consumed = AppendBracketControl(text, i, values);
                if (consumed > 0)
                {
                    i += consumed - 1;
                    continue;
                }

                throw new InvalidDataException("Literal '[' characters must be escaped as '\\['.");
            }

            if (value == '{')
            {
                var consumed = AppendRuby(text, i, values);
                i += consumed - 1;
                continue;
            }

            values.Add(value);
        }
    }

    private static void AppendEscape(
        char escaped,
        ICollection<ushort> values,
        bool allowRubyDelimiters)
    {
        switch (escaped)
        {
            case 'n':
                values.Add('\n');
                break;
            case '\\':
            case '[':
            case '{':
                values.Add(escaped);
                break;
            case '}' when allowRubyDelimiters:
            case '|' when allowRubyDelimiters:
                values.Add(escaped);
                break;
            case 'r':
                AppendControlWithoutPayload(values, TextReturn);
                break;
            case 'c':
                AppendControlWithoutPayload(values, TextClear);
                break;
            default:
                throw new InvalidDataException($"Text contains unsupported escape sequence '\\{escaped}'.");
        }
    }

    private static int AppendBracketControl(string text, int start, ICollection<ushort> values)
    {
        var remaining = text.AsSpan(start);
        if (remaining.StartsWith("[WAIT", StringComparison.Ordinal))
        {
            var body = ReadBracketBody(text, start, "[WAIT ");
            if (!ushort.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out var duration))
            {
                throw new InvalidDataException("WAIT controls require one decimal duration from 0 through 65535.");
            }

            AppendWaitVariable(values, duration);
            return body.Length + "[WAIT ]".Length;
        }

        if (remaining.StartsWith("[~", StringComparison.Ordinal))
        {
            var body = ReadBracketBody(text, start, "[~ ");
            if (!ushort.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out var lineIndex))
            {
                throw new InvalidDataException("Null-line controls require one decimal line index from 0 through 65535.");
            }

            AppendSpecialVariable(values, TextNull, lineIndex);
            return body.Length + "[~ ]".Length;
        }

        if (remaining.StartsWith("[VAR", StringComparison.Ordinal))
        {
            var body = ReadBracketBody(text, start, "[VAR ");
            AppendGenericVariable(body, values);
            return body.Length + "[VAR ]".Length;
        }

        return 0;
    }

    private static string ReadBracketBody(string text, int start, string prefix)
    {
        if (!text.AsSpan(start).StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Malformed {prefix.TrimEnd()} text control.");
        }

        var end = text.IndexOf(']', start + prefix.Length);
        if (end < 0)
        {
            throw new InvalidDataException($"Unterminated {prefix.TrimEnd()} text control.");
        }

        return text[(start + prefix.Length)..end];
    }

    private static void AppendGenericVariable(string body, ICollection<ushort> values)
    {
        var argumentStart = body.IndexOf('(', StringComparison.Ordinal);
        var variableText = argumentStart < 0 ? body : body[..argumentStart];
        if (variableText.Length is < 1 or > 4
            || !ushort.TryParse(variableText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var variable))
        {
            throw new InvalidDataException("VAR controls require a hexadecimal variable code from 0000 through FFFF.");
        }

        var arguments = new List<ushort>();
        if (argumentStart >= 0)
        {
            if (!body.EndsWith(")", StringComparison.Ordinal) || argumentStart == body.Length - 2)
            {
                throw new InvalidDataException("VAR control arguments must be a nonempty comma-separated hexadecimal list.");
            }

            var argumentText = body[(argumentStart + 1)..^1];
            foreach (var part in argumentText.Split(',', StringSplitOptions.None))
            {
                var trimmedPart = part.Trim();
                if (trimmedPart.Length is < 1 or > 4
                    || !ushort.TryParse(trimmedPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argument))
                {
                    throw new InvalidDataException("VAR control arguments must be hexadecimal values from 0000 through FFFF.");
                }

                arguments.Add(argument);
            }
        }

        switch (variable)
        {
            case TextReturn:
            case TextClear:
                if (arguments.Count != 0)
                {
                    throw new InvalidDataException($"VAR {variable:X4} does not accept arguments.");
                }
                break;
            case TextWait:
            case TextNull:
                if (arguments.Count != 1)
                {
                    throw new InvalidDataException($"VAR {variable:X4} requires exactly one argument.");
                }
                break;
            case TextRuby:
                throw new InvalidDataException("Ruby text must use {base|ruby} syntax.");
        }

        values.Add(VariableMarker);
        values.Add(CreateVariableCount(variable, arguments.Count));
        values.Add(variable);
        foreach (var argument in arguments)
        {
            values.Add(argument);
        }
    }

    private static int AppendRuby(string text, int start, ICollection<ushort> values)
    {
        var end = FindUnescaped(text, start + 1, '}');
        if (end < 0)
        {
            throw new InvalidDataException("Ruby text is missing its closing brace.");
        }

        var parts = SplitUnescaped(text[(start + 1)..end], '|');
        if (parts.Count is < 2 or > 3)
        {
            throw new InvalidDataException("Ruby text must use {base|ruby} or {base|ruby|alternate-base} syntax.");
        }

        var firstBase = EncodeRubyFragment(parts[0]);
        var ruby = EncodeRubyFragment(parts[1]);
        var secondBase = parts.Count == 3 ? EncodeRubyFragment(parts[2]) : firstBase;
        if (firstBase.Count != secondBase.Count)
        {
            throw new InvalidDataException("Ruby base text copies must contain the same number of UTF-16 values.");
        }

        var count = checked(3 + firstBase.Count + ruby.Count);
        if (count > ushort.MaxValue
            || firstBase.Count > ushort.MaxValue
            || ruby.Count > ushort.MaxValue)
        {
            throw new InvalidDataException("Ruby text is too long for the Sword/Shield text format.");
        }

        values.Add(VariableMarker);
        values.Add((ushort)count);
        values.Add(TextRuby);
        values.Add((ushort)firstBase.Count);
        values.Add((ushort)ruby.Count);
        AppendValues(values, firstBase);
        AppendValues(values, ruby);
        AppendValues(values, secondBase);
        return end - start + 1;
    }

    private static List<ushort> EncodeRubyFragment(string text)
    {
        var result = new List<ushort>(text.Length);
        AppendEncodedText(text, result, allowRubyDelimiters: true);
        return result;
    }

    private static int FindUnescaped(string text, int start, char target)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                continue;
            }

            if (text[i] == target)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> SplitUnescaped(string text, char separator)
    {
        var result = new List<string>();
        var partStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                continue;
            }

            if (text[i] != separator)
            {
                continue;
            }

            result.Add(text[partStart..i]);
            partStart = i + 1;
        }

        result.Add(text[partStart..]);
        return result;
    }

    private static void AppendValues(ICollection<ushort> destination, IEnumerable<ushort> source)
    {
        foreach (var value in source)
        {
            destination.Add(value);
        }
    }

    private static void AppendControlWithoutPayload(ICollection<ushort> values, ushort variable)
    {
        values.Add(VariableMarker);
        values.Add(1);
        values.Add(variable);
    }

    private static void AppendSpecialVariable(ICollection<ushort> values, ushort variable, ushort payload)
    {
        values.Add(VariableMarker);
        values.Add(1);
        values.Add(variable);
        values.Add(payload);
    }

    private static ushort CreateVariableCount(ushort variable, int argumentCount)
    {
        if (variable == TextNull)
        {
            return 1;
        }

        return checked((ushort)(argumentCount + 1));
    }

    private static void AppendWaitVariable(ICollection<ushort> values, ushort duration)
    {
        values.Add(VariableMarker);
        values.Add(2);
        values.Add(TextWait);
        values.Add(duration);
    }

    private static void ValidateUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var value = text[i];
            if (value == (char)Terminator || value == (char)VariableMarker)
            {
                throw new InvalidDataException($"Raw U+{(ushort)value:X4} is reserved by the Sword/Shield text format.");
            }

            if (char.IsHighSurrogate(value))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    throw new InvalidDataException("Text contains an unpaired UTF-16 surrogate.");
                }

                i++;
                continue;
            }

            if (char.IsLowSurrogate(value))
            {
                throw new InvalidDataException("Text contains an unpaired UTF-16 surrogate.");
            }
        }
    }
}
