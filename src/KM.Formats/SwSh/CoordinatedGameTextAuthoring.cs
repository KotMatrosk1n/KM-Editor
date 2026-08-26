// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record CoordinatedGameTextSource(
    string Language,
    byte[] Data,
    byte[] Keys);

public sealed record CoordinatedGameTextOutput(
    string Language,
    int LineIndex,
    byte[] Data,
    byte[] Keys);

public static class CoordinatedGameTextAuthoring
{
    private const int MaximumLanguageCount = 64;
    private const int MaximumLanguageLength = 64;
    private const int MaximumKeyLength = 256;
    private const int MaximumSourceBytes = 64 * 1024 * 1024;

    public static IReadOnlyList<CoordinatedGameTextOutput> Insert(
        IReadOnlyList<CoordinatedGameTextSource> sources,
        IReadOnlyList<string> requiredLanguages,
        string key,
        IReadOnlyDictionary<string, string> textByLanguage,
        GameTextNullLineEncoding nullLineEncoding,
        ushort flags = 0)
    {
        ArgumentNullException.ThrowIfNull(textByLanguage);
        var parsed = ParseCoordinatedSources(sources, requiredLanguages, nullLineEncoding);
        ValidateKey(key);
        ValidateLanguageValues(parsed, textByLanguage);

        var hash = SwShGfPackFile.HashFnv1a64(key);
        EnsureKeyIsAvailable(parsed[0].AllKeyEntries, key, hash);

        var outputs = new List<CoordinatedGameTextOutput>(parsed.Count);
        foreach (var source in parsed)
        {
            var newLines = source.Data.Lines
                .Append(new SwShGameTextLine(textByLanguage[source.Language], flags))
                .ToArray();
            var newKeys = source.Keys.Entries
                .Take(source.Data.Lines.Count)
                .Append(new SwShAhtbEntry(hash, key))
                .Concat(source.Keys.Entries.Skip(source.Data.Lines.Count))
                .ToArray();
            var dataBytes = source.Data.WriteStructural(newLines, nullLineEncoding);
            var keyBytes = WriteKeyTable(newKeys, source.UncountedTerminalBytes);
            ValidateOutput(source, dataBytes, keyBytes, key, textByLanguage[source.Language], flags, isInsertion: true);
            outputs.Add(new CoordinatedGameTextOutput(
                source.Language,
                source.Data.Lines.Count,
                dataBytes,
                keyBytes));
        }

        return outputs.AsReadOnly();
    }

    public static IReadOnlyList<CoordinatedGameTextOutput> Delete(
        IReadOnlyList<CoordinatedGameTextSource> sources,
        IReadOnlyList<string> requiredLanguages,
        string key,
        GameTextNullLineEncoding nullLineEncoding)
    {
        var parsed = ParseCoordinatedSources(sources, requiredLanguages, nullLineEncoding);
        ValidateKey(key);

        var matchingIndices = parsed[0].Keys.Entries
            .Take(parsed[0].Data.Lines.Count)
            .Select((entry, index) => (entry, index))
            .Where(candidate => string.Equals(candidate.entry.Name, key, StringComparison.Ordinal))
            .Select(candidate => candidate.index)
            .ToArray();
        if (matchingIndices.Length != 1)
        {
            throw new InvalidDataException("The message key must identify exactly one editable row before it can be deleted.");
        }

        var lineIndex = matchingIndices[0];
        var outputs = new List<CoordinatedGameTextOutput>(parsed.Count);
        foreach (var source in parsed)
        {
            var newLines = source.Data.Lines
                .Where((_, index) => index != lineIndex)
                .ToArray();
            var newKeys = source.Keys.Entries
                .Where((_, index) => index != lineIndex)
                .ToArray();
            var dataBytes = source.Data.WriteStructural(newLines, nullLineEncoding);
            var keyBytes = WriteKeyTable(newKeys, source.UncountedTerminalBytes);
            ValidateOutput(source, dataBytes, keyBytes, key, expectedText: null, flags: 0, isInsertion: false);
            outputs.Add(new CoordinatedGameTextOutput(
                source.Language,
                lineIndex,
                dataBytes,
                keyBytes));
        }

        return outputs.AsReadOnly();
    }

    private static IReadOnlyList<ParsedSource> ParseCoordinatedSources(
        IReadOnlyList<CoordinatedGameTextSource> sources,
        IReadOnlyList<string> requiredLanguages,
        GameTextNullLineEncoding nullLineEncoding)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(requiredLanguages);
        ValidateNullLineEncoding(nullLineEncoding);
        if (requiredLanguages.Count is < 1 or > MaximumLanguageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredLanguages), "A coordinated message edit requires between 1 and 64 languages.");
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in requiredLanguages)
        {
            ValidateLanguage(language);
            if (!required.Add(language))
            {
                throw new ArgumentException("Required message languages must be unique.", nameof(requiredLanguages));
            }
        }

        if (sources.Count != required.Count)
        {
            throw new InvalidDataException("Every required message language must have exactly one source pair.");
        }

        var byLanguage = new Dictionary<string, CoordinatedGameTextSource>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            ValidateLanguage(source.Language);
            ArgumentNullException.ThrowIfNull(source.Data);
            ArgumentNullException.ThrowIfNull(source.Keys);
            if (!required.Contains(source.Language) || !byLanguage.TryAdd(source.Language, source))
            {
                throw new InvalidDataException("Message source languages must match the required language set exactly.");
            }

            if (source.Data.Length > MaximumSourceBytes || source.Keys.Length > MaximumSourceBytes)
            {
                throw new InvalidDataException("A coordinated message source exceeds the bounded input size.");
            }
        }

        var parsed = new List<ParsedSource>(requiredLanguages.Count);
        foreach (var language in requiredLanguages)
        {
            var source = byLanguage[language];
            var data = SwShGameTextFile.Parse(source.Data, ushort.MaxValue - 1);
            var parsedKeys = ParseKeyTable(source.Keys, checked(data.Lines.Count + 1));
            ValidatePair(data, parsedKeys.Keys, parsedKeys.UncountedTerminal);
            parsed.Add(new ParsedSource(
                language,
                data,
                parsedKeys.Keys,
                parsedKeys.UncountedTerminal,
                parsedKeys.UncountedTerminalBytes));
        }

        var referenceKeys = parsed[0].Keys.Entries;
        foreach (var source in parsed.Skip(1))
        {
            if (!referenceKeys.SequenceEqual(source.Keys.Entries)
                || parsed[0].UncountedTerminal != source.UncountedTerminal)
            {
                throw new InvalidDataException("Message key tables must have the same ordered identities in every required language.");
            }
        }

        return parsed.AsReadOnly();
    }

    private static ParsedKeyTable ParseKeyTable(byte[] source, int maximumEntryCount)
    {
        var keys = SwShAhtbFile.Parse(source, maximumEntryCount);
        var canonicalPrefix = keys.Write();
        if (source.Length < canonicalPrefix.Length
            || !source.AsSpan(0, canonicalPrefix.Length).SequenceEqual(canonicalPrefix))
        {
            throw new InvalidDataException("Message key tables must use the exact supported canonical encoding before structural edits are enabled.");
        }

        var suffix = source.AsSpan(canonicalPrefix.Length).ToArray();
        if (suffix.Length == 0)
        {
            return new ParsedKeyTable(keys, null, suffix);
        }

        var wrapped = new byte[checked(8 + suffix.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(wrapped, SwShAhtbFile.Magic);
        BinaryPrimitives.WriteInt32LittleEndian(wrapped.AsSpan(4), 1);
        suffix.CopyTo(wrapped.AsSpan(8));
        var trailingTable = SwShAhtbFile.Parse(wrapped, 1);
        if (!trailingTable.Write().AsSpan(8).SequenceEqual(suffix))
        {
            throw new InvalidDataException("Message key tables contain unsupported trailing data.");
        }

        var terminal = trailingTable.Entries[0];
        if (!terminal.Name.EndsWith("_max", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only one canonical uncounted terminal key may follow a message key table.");
        }

        return new ParsedKeyTable(keys, terminal, suffix);
    }

    private static byte[] WriteKeyTable(
        IReadOnlyList<SwShAhtbEntry> entries,
        byte[] uncountedTerminalBytes)
    {
        var prefix = new SwShAhtbFile(entries).Write();
        if (uncountedTerminalBytes.Length == 0)
        {
            return prefix;
        }

        var output = new byte[checked(prefix.Length + uncountedTerminalBytes.Length)];
        prefix.CopyTo(output, 0);
        uncountedTerminalBytes.CopyTo(output, prefix.Length);
        return output;
    }

    private static void ValidatePair(
        SwShGameTextFile data,
        SwShAhtbFile keys,
        SwShAhtbEntry? uncountedTerminal)
    {
        var hasNoTerminalKey = keys.Entries.Count == data.Lines.Count
            && (uncountedTerminal is null
                || uncountedTerminal.Name.EndsWith("_max", StringComparison.OrdinalIgnoreCase));
        var hasTerminalKey = keys.Entries.Count == data.Lines.Count + 1
            && keys.Entries[^1].Name.EndsWith("_max", StringComparison.OrdinalIgnoreCase)
            && uncountedTerminal is null;
        if (!hasNoTerminalKey && !hasTerminalKey)
        {
            throw new InvalidDataException("A coordinated message pair requires one key per row and may include one terminal key.");
        }

        foreach (var entry in keys.Entries.Append(uncountedTerminal).OfType<SwShAhtbEntry>())
        {
            if (entry.Name.Length == 0
                || entry.Name.Any(character => character > 0x7F)
                || SwShGfPackFile.HashFnv1a64(entry.Name) != entry.Hash)
            {
                throw new InvalidDataException("Message keys must use verified ASCII names with matching stored hashes.");
            }
        }
    }

    private static void ValidateLanguageValues(
        IReadOnlyList<ParsedSource> sources,
        IReadOnlyDictionary<string, string> textByLanguage)
    {
        if (textByLanguage.Count != sources.Count)
        {
            throw new InvalidDataException("Every required message language must provide exactly one text value.");
        }

        foreach (var source in sources)
        {
            if (!textByLanguage.TryGetValue(source.Language, out var text) || text is null)
            {
                throw new InvalidDataException("Every required message language must provide exactly one text value.");
            }

            SwShGameTextFile.ValidateText(text);
        }

        if (textByLanguage.Keys.Any(language => sources.All(source => !string.Equals(source.Language, language, StringComparison.Ordinal))))
        {
            throw new InvalidDataException("Localized message values must match the required language set exactly.");
        }
    }

    private static void EnsureKeyIsAvailable(
        IReadOnlyList<SwShAhtbEntry> entries,
        string key,
        ulong hash)
    {
        if (entries.Any(entry => string.Equals(entry.Name, key, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The requested message key already exists.");
        }

        if (entries.Any(entry => entry.Hash == hash))
        {
            throw new InvalidDataException("The requested message key collides with an existing key hash.");
        }
    }

    private static void ValidateOutput(
        ParsedSource source,
        byte[] dataBytes,
        byte[] keyBytes,
        string key,
        string? expectedText,
        ushort flags,
        bool isInsertion)
    {
        var data = SwShGameTextFile.Parse(dataBytes, ushort.MaxValue);
        var parsedKeys = ParseKeyTable(keyBytes, ushort.MaxValue + 1);
        var keys = parsedKeys.Keys;
        ValidatePair(data, keys, parsedKeys.UncountedTerminal);
        if (!parsedKeys.UncountedTerminalBytes.AsSpan().SequenceEqual(source.UncountedTerminalBytes))
        {
            throw new InvalidDataException("The rebuilt message table changed uncounted terminal data.");
        }

        if (isInsertion)
        {
            var lineIndex = source.Data.Lines.Count;
            if (data.Lines.Count != source.Data.Lines.Count + 1
                || keys.Entries.Count != source.Keys.Entries.Count + 1
                || !string.Equals(data.Lines[lineIndex].Text, expectedText, StringComparison.Ordinal)
                || data.Lines[lineIndex].Flags != flags
                || !string.Equals(keys.Entries[lineIndex].Name, key, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The rebuilt message insertion did not pass semantic readback validation.");
            }

            for (var index = 0; index < source.Data.Lines.Count; index++)
            {
                if (data.Lines[index] != source.Data.Lines[index]
                    || keys.Entries[index] != source.Keys.Entries[index])
                {
                    throw new InvalidDataException("The rebuilt message insertion changed an existing row.");
                }
            }

            if (source.Keys.Entries.Count > source.Data.Lines.Count
                && keys.Entries[^1] != source.Keys.Entries[^1])
            {
                throw new InvalidDataException("The rebuilt message insertion changed the terminal key.");
            }
        }
        else
        {
            if (data.Lines.Count != source.Data.Lines.Count - 1
                || keys.Entries.Count != source.Keys.Entries.Count - 1
                || keys.Entries.Any(entry => string.Equals(entry.Name, key, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("The rebuilt message deletion did not pass semantic readback validation.");
            }


            var deletedIndex = source.Keys.Entries
                .Take(source.Data.Lines.Count)
                .Select((entry, index) => (entry, index))
                .Single(candidate => string.Equals(candidate.entry.Name, key, StringComparison.Ordinal))
                .index;
            for (var outputIndex = 0; outputIndex < data.Lines.Count; outputIndex++)
            {
                var sourceIndex = outputIndex < deletedIndex ? outputIndex : outputIndex + 1;
                if (data.Lines[outputIndex] != source.Data.Lines[sourceIndex]
                    || keys.Entries[outputIndex] != source.Keys.Entries[sourceIndex])
                {
                    throw new InvalidDataException("The rebuilt message deletion changed a retained row.");
                }
            }

            if (source.Keys.Entries.Count > source.Data.Lines.Count
                && keys.Entries[^1] != source.Keys.Entries[^1])
            {
                throw new InvalidDataException("The rebuilt message deletion changed the terminal key.");
            }
        }
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > MaximumKeyLength
            || key.EndsWith("_max", StringComparison.OrdinalIgnoreCase)
            || !IsIdentifierStart(key[0])
            || key.Any(character => !IsIdentifierPart(character)))
        {
            throw new ArgumentException(
                "Message keys must be ASCII identifiers of at most 256 characters and cannot use the terminal suffix.",
                nameof(key));
        }
    }

    private static void ValidateLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (language.Length > MaximumLanguageLength
            || language.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new ArgumentException("Message language identifiers must use a bounded safe identifier.", nameof(language));
        }
    }

    private static bool IsIdentifierStart(char character) =>
        char.IsAsciiLetter(character) || character == '_';

    private static bool IsIdentifierPart(char character) =>
        IsIdentifierStart(character) || char.IsAsciiDigit(character);

    private static void ValidateNullLineEncoding(GameTextNullLineEncoding nullLineEncoding)
    {
        if (nullLineEncoding is not GameTextNullLineEncoding.LegacyCountOne
            and not GameTextNullLineEncoding.PayloadCountTwo)
        {
            throw new ArgumentOutOfRangeException(nameof(nullLineEncoding));
        }
    }

    private sealed record ParsedSource(
        string Language,
        SwShGameTextFile Data,
        SwShAhtbFile Keys,
        SwShAhtbEntry? UncountedTerminal,
        byte[] UncountedTerminalBytes)
    {
        public IReadOnlyList<SwShAhtbEntry> AllKeyEntries => UncountedTerminal is null
            ? Keys.Entries
            : Keys.Entries.Append(UncountedTerminal).ToArray();
    }

    private sealed record ParsedKeyTable(
        SwShAhtbFile Keys,
        SwShAhtbEntry? UncountedTerminal,
        byte[] UncountedTerminalBytes);
}
