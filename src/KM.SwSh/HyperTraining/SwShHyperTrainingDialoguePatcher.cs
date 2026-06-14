// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.HyperTraining;

internal static class SwShHyperTrainingDialoguePatcher
{
    public const int IntroLineIndex = 0;
    public const int LevelFailureLineIndex = 3;

    private const ushort BaseKey = 0x7C89;
    private const ushort KeyAdvance = 0x2983;
    private const ushort Terminator = 0x0000;
    private const ushort VariableMarker = 0x0010;
    private const int HeaderSize = 0x10;
    private const int LineEntrySize = 0x08;

    public static byte[] ApplyMinimumLevel(byte[] data, int minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateLevel(minimumLevel);

        var textFile = ReadTextFile(data);
        if (textFile.Lines.Count <= LevelFailureLineIndex)
        {
            throw new InvalidDataException("Hyper Training dialogue table does not contain the expected level failure line.");
        }

        var lines = textFile.Lines.ToArray();
        lines[IntroLineIndex] = lines[IntroLineIndex] with
        {
            EncryptedBytes = EncodeEncryptedLine(CreateIntroText(minimumLevel), IntroLineIndex),
        };
        lines[LevelFailureLineIndex] = lines[LevelFailureLineIndex] with
        {
            EncryptedBytes = EncodeEncryptedLine(CreateLevelFailureText(minimumLevel), LevelFailureLineIndex),
        };

        return WriteTextFile(lines);
    }

    private static string CreateIntroText(int minimumLevel)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Oho... I see you've become the Champion.\\nThen I can now have your Lv. {minimumLevel} Pok\u00E9mon[VAR BE00]\\nundergo Hyper Training.");
    }

    private static string CreateLevelFailureText(int minimumLevel)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"If it isn't Lv. {minimumLevel}, it's not hype enough to\\nundergo Hyper Training.");
    }

    private static TextFile ReadTextFile(ReadOnlySpan<byte> data)
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

        var lines = new TextLine[lineCount];
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

            lines[i] = new TextLine(data.Slice(textStart, byteLength).ToArray(), flags);
        }

        return new TextFile(lines);
    }

    private static byte[] WriteTextFile(IReadOnlyList<TextLine> lines)
    {
        if (lines.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(lines), "Sword/Shield text files cannot contain more than 65535 lines.");
        }

        var sectionLength = sizeof(uint) + (lines.Count * LineEntrySize);
        var offsets = new int[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            offsets[i] = sectionLength;
            sectionLength += lines[i].EncryptedBytes.Length;
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
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.EncryptedBytes.Length % sizeof(ushort) != 0)
            {
                throw new InvalidDataException($"Text line {i} has an odd byte length.");
            }

            var entryOffset = lineTableStart + (i * LineEntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entryOffset), offsets[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset + 0x04), (ushort)(line.EncryptedBytes.Length / sizeof(ushort)));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset + 0x06), line.Flags);
            line.EncryptedBytes.CopyTo(data.AsSpan(HeaderSize + offsets[i]));
        }

        return data;
    }

    private static byte[] EncodeEncryptedLine(string text, int lineIndex)
    {
        var data = EncodeLine(text);
        CryptLineData(data, GetLineKey(lineIndex));
        return data;
    }

    private static byte[] EncodeLine(string text)
    {
        var values = new List<ushort>(text.Length + 1);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                var escaped = text[++i];
                values.Add(escaped switch
                {
                    'n' => '\n',
                    '\\' => '\\',
                    '[' => '[',
                    '{' => '{',
                    _ => escaped,
                });
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
        values.Add(checked((ushort)(arguments.Count + 1)));
        values.Add(variable);
        foreach (var argument in arguments)
        {
            values.Add(argument);
        }

        consumed = end - start + 1;
        return true;
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

    private static void ValidateLevel(int minimumLevel)
    {
        if (minimumLevel is < SwShHyperTrainingAmxPatcher.MinimumAllowedLevel or > SwShHyperTrainingAmxPatcher.MaximumAllowedLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumLevel),
                minimumLevel,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training minimum level must be between {SwShHyperTrainingAmxPatcher.MinimumAllowedLevel} and {SwShHyperTrainingAmxPatcher.MaximumAllowedLevel}."));
        }
    }

    private sealed record TextFile(IReadOnlyList<TextLine> Lines);

    private sealed record TextLine(byte[] EncryptedBytes, ushort Flags);
}
