// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KM.SwSh.Randomizer;

internal static class SwShRandomizerSeedCodec
{
    public const int MaximumUserSeedLength = 20;
    public const string Prefix = "KM1";

    private const string LegacyPrefix = "KMR1";
    private const int ChecksumByteLength = 10;
    private const int ChecksumTextLength = 16;
    private const int SeedGroupLength = 5;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Export(SwShRandomizerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var normalized = Normalize(config);
        var payload = new CompactSeedPayload(
            1,
            normalized.UserSeed,
            normalized.RollSeed,
            normalized.OutputHash,
            EncodeOptions(normalized.Options));
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var encodedPayload = Base32Encode(payloadJson);
        var checksum = ComputeChecksum(Prefix, encodedPayload);

        return FormatSeed(encodedPayload, checksum);
    }

    public static SwShRandomizerImportResult Import(string seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            return Invalid("Randomizer seed cannot be empty.");
        }

        var trimmedSeed = seed.Trim();
        if (trimmedSeed.StartsWith($"{LegacyPrefix}.", StringComparison.Ordinal))
        {
            return ImportLegacy(trimmedSeed);
        }

        var normalizedSeed = RemoveSeedWhitespace(trimmedSeed);
        if (!normalizedSeed.StartsWith($"{Prefix}-", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("Randomizer seed must use the KM1 format.");
        }

        var token = normalizedSeed[(Prefix.Length + 1)..].Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (token.Length <= ChecksumTextLength)
        {
            return Invalid("Randomizer seed payload could not be read.");
        }

        var encodedPayload = token[..^ChecksumTextLength];
        var checksum = token[^ChecksumTextLength..];
        var expectedChecksum = ComputeChecksum(Prefix, encodedPayload);
        if (!FixedTimeEqualsBase32(expectedChecksum, checksum))
        {
            return Invalid("Randomizer seed checksum does not match.");
        }

        CompactSeedPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CompactSeedPayload>(Base32Decode(encodedPayload), JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return Invalid("Randomizer seed payload could not be read.");
        }

        if (payload is null || payload.Version != 1)
        {
            return Invalid("Randomizer seed version is not supported.");
        }

        if (payload.UserSeed.Length > MaximumUserSeedLength)
        {
            return Invalid($"Base seed must be {MaximumUserSeedLength} characters or fewer.");
        }

        SwShRandomizerOptions options;
        try
        {
            options = DecodeOptions(payload.Options);
        }
        catch (FormatException)
        {
            return Invalid("Randomizer seed payload could not be read.");
        }

        var config = Normalize(new SwShRandomizerConfig(
            payload.UserSeed,
            options,
            payload.RollSeed,
            payload.OutputHash));
        var canonicalSeed = Export(config);

        return new SwShRandomizerImportResult(config, canonicalSeed, Array.Empty<ValidationDiagnostic>());
    }

    public static string CreateGenerationKey(SwShRandomizerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var normalized = Normalize(config);
        var payload = new GenerationPayload(
            1,
            normalized.UserSeed,
            normalized.RollSeed ?? string.Empty,
            normalized.Options);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return Base64UrlEncode(SHA256.HashData(payloadJson));
    }

    public static SwShRandomizerConfig Normalize(SwShRandomizerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var userSeed = config.UserSeed ?? string.Empty;
        if (userSeed.Length > MaximumUserSeedLength)
        {
            userSeed = userSeed[..MaximumUserSeedLength];
        }

        return config with
        {
            UserSeed = userSeed,
            Options = NormalizeOptions(config.Options ?? SwShRandomizerOptions.Empty),
            RollSeed = string.IsNullOrWhiteSpace(config.RollSeed) ? null : config.RollSeed,
            OutputHash = string.IsNullOrWhiteSpace(config.OutputHash) ? null : config.OutputHash,
        };
    }

    private static SwShRandomizerOptions NormalizeOptions(SwShRandomizerOptions options)
    {
        return options.TypeChartNoImmunities && options.TypeChartOneImmunityPerType
            ? options with { TypeChartOneImmunityPerType = false }
            : options;
    }

    private static SwShRandomizerImportResult Invalid(string message)
    {
        return new SwShRandomizerImportResult(
            Config: null,
            Seed: null,
            Diagnostics:
            [
                new ValidationDiagnostic(
                    DiagnosticSeverity.Error,
                    message,
                    Domain: "workflow.randomizer",
                    Field: "seed",
                    Expected: "Valid KM1 randomizer seed"),
            ]);
    }

    private static SwShRandomizerImportResult ImportLegacy(string seed)
    {
        var parts = seed.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], LegacyPrefix, StringComparison.Ordinal))
        {
            return Invalid("Randomizer seed must use the KM1 format.");
        }

        var expectedChecksum = ComputeChecksumLegacy(parts[1]);
        if (!FixedTimeEqualsBase64Url(expectedChecksum, parts[2]))
        {
            return Invalid("Randomizer seed checksum does not match.");
        }

        LegacySeedPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LegacySeedPayload>(Base64UrlDecode(parts[1]), JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return Invalid("Randomizer seed payload could not be read.");
        }

        if (payload is null || payload.Version != 1)
        {
            return Invalid("Randomizer seed version is not supported.");
        }

        if (payload.UserSeed.Length > MaximumUserSeedLength)
        {
            return Invalid($"Base seed must be {MaximumUserSeedLength} characters or fewer.");
        }

        var config = Normalize(new SwShRandomizerConfig(
            payload.UserSeed,
            payload.Options ?? SwShRandomizerOptions.Empty,
            payload.RollSeed,
            payload.OutputHash));
        var canonicalSeed = Export(config);

        return new SwShRandomizerImportResult(config, canonicalSeed, Array.Empty<ValidationDiagnostic>());
    }

    private static string ComputeChecksum(string prefix, string encodedPayload)
    {
        var input = Encoding.UTF8.GetBytes($"{prefix}.{encodedPayload}");
        var hash = SHA256.HashData(input);
        return Base32Encode(hash.AsSpan(0, ChecksumByteLength));
    }

    private static string ComputeChecksumLegacy(string encodedPayload)
    {
        var input = Encoding.UTF8.GetBytes($"{LegacyPrefix}.{encodedPayload}");
        var hash = SHA256.HashData(input);
        return Base64UrlEncode(hash.AsSpan(0, ChecksumByteLength));
    }

    private static bool FixedTimeEqualsBase32(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Base32Decode(left), Base32Decode(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedTimeEqualsBase64Url(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Base64UrlDecode(left), Base64UrlDecode(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string FormatSeed(string encodedPayload, string checksum)
    {
        var token = encodedPayload + checksum;
        var groups = new List<string>();
        for (var index = 0; index < token.Length; index += SeedGroupLength)
        {
            groups.Add(token.Substring(index, Math.Min(SeedGroupLength, token.Length - index)));
        }

        return $"{Prefix}-{string.Join('-', groups)}";
    }

    private static string RemoveSeedWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string Base32Encode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return builder.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var bytes = new List<byte>(value.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in value)
        {
            var base32Value = GetBase32Value(character);
            buffer = (buffer << 5) | base32Value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
                buffer &= (1 << bitsLeft) - 1;
            }
        }

        return bytes.ToArray();
    }

    private static int GetBase32Value(char character)
    {
        var normalized = char.ToUpperInvariant(character);
        if (normalized is >= 'A' and <= 'Z')
        {
            return normalized - 'A';
        }

        if (normalized is >= '2' and <= '7')
        {
            return normalized - '2' + 26;
        }

        throw new FormatException("Randomizer seed contains a character outside the KM1 alphabet.");
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static string EncodeOptions(SwShRandomizerOptions options)
    {
        var flags = new[]
        {
            options.RandomizePokemonStats,
            options.ShufflePokemonStats,
            options.StatHp,
            options.StatAttack,
            options.StatDefense,
            options.StatSpecialAttack,
            options.StatSpecialDefense,
            options.StatSpeed,
            options.RandomizePokemonTypes,
            options.TypePrimary,
            options.TypeSecondary,
            options.AllowSameType,
            options.RandomizePokemonAbilities,
            options.Ability1,
            options.Ability2,
            options.HiddenAbility,
            options.RandomizePokemonHeldItems,
            options.RandomizePokemonCatchRates,
            options.RandomizePokemonLearnsets,
            options.LearnsetStabFirst,
            options.LearnsetExpandTo25,
            options.LearnsetBanFixedDamageMoves,
            options.LearnsetRequireDamagingMove,
            options.RandomizePokemonCompatibility,
            options.CompatibilityMachines,
            options.CompatibilityRecords,
            options.CompatibilityTutors,
            options.RandomizePokemonEvolutions,
            options.RandomizeWildEncounters,
            options.RandomizeStaticEncounters,
            options.RandomizeGiftEncounters,
            options.RandomizeRaidRewards,
            options.RandomizeRaidBonusRewards,
            options.RandomizeTypeChart,
            options.TypeChartNoImmunities,
            options.TypeChartOneImmunityPerType,
        };
        Span<byte> bytes = stackalloc byte[(flags.Length + 7) / 8];
        for (var index = 0; index < flags.Length; index++)
        {
            if (flags[index])
            {
                bytes[index / 8] |= (byte)(1 << (index % 8));
            }
        }

        return Base64UrlEncode(bytes);
    }

    private static SwShRandomizerOptions DecodeOptions(string? encodedOptions)
    {
        if (string.IsNullOrWhiteSpace(encodedOptions))
        {
            return SwShRandomizerOptions.Empty;
        }

        var bytes = Base64UrlDecode(encodedOptions);
        bool Flag(int index)
        {
            var byteIndex = index / 8;
            return byteIndex < bytes.Length
                && (bytes[byteIndex] & (1 << (index % 8))) != 0;
        }

        return new SwShRandomizerOptions(
            Flag(0),
            Flag(1),
            Flag(2),
            Flag(3),
            Flag(4),
            Flag(5),
            Flag(6),
            Flag(7),
            Flag(8),
            Flag(9),
            Flag(10),
            Flag(11),
            Flag(12),
            Flag(13),
            Flag(14),
            Flag(15),
            Flag(16),
            Flag(17),
            Flag(18),
            Flag(19),
            Flag(20),
            Flag(21),
            Flag(22),
            Flag(23),
            Flag(24),
            Flag(25),
            Flag(26),
            Flag(27),
            Flag(28),
            Flag(29),
            Flag(30),
            Flag(31),
            Flag(32),
            Flag(33),
            Flag(34),
            Flag(35));
    }

    private sealed record CompactSeedPayload(
        int Version,
        string UserSeed,
        string? RollSeed,
        string? OutputHash,
        string Options);

    private sealed record LegacySeedPayload(
        int Version,
        string UserSeed,
        string? RollSeed,
        string? OutputHash,
        SwShRandomizerOptions? Options);

    private sealed record GenerationPayload(
        int Version,
        string UserSeed,
        string RollSeed,
        SwShRandomizerOptions? Options);
}
