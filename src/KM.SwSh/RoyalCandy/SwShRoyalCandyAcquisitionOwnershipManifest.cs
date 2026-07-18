// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text.Json;

namespace KM.SwSh.RoyalCandy;

internal sealed record SwShRoyalCandyAcquisitionOwnershipManifestRecord(
    int Version,
    string ShopRelativePath,
    string BaseShopSha256,
    string BaseNestSha256,
    string BasePlacementSha256,
    string BaseItemHashSha256);

internal static class SwShRoyalCandyAcquisitionOwnershipManifest
{
    public const string RelativePath = ".km-editor/royal-candy-acquisition-manifest.json";
    public const int CurrentVersion = 1;

    private const int MaximumManifestByteCount = 4096;
    private const string ModernShopRelativePath = "romfs/bin/appli/shop/bin/shop_data.bin";
    private const string LegacyShopRelativePath = "romfs/bin/app/shop/shop_data.bin";

    private static readonly string[] PropertyNames =
    [
        "version",
        "shopRelativePath",
        "baseShopSha256",
        "baseNestSha256",
        "basePlacementSha256",
        "baseItemHashSha256",
    ];

    public static SwShRoyalCandyAcquisitionOwnershipManifestRecord Create(
        string shopRelativePath,
        byte[] baseShopBytes,
        byte[] baseNestBytes,
        byte[] basePlacementBytes,
        byte[] baseItemHashBytes)
    {
        ArgumentNullException.ThrowIfNull(shopRelativePath);
        ArgumentNullException.ThrowIfNull(baseShopBytes);
        ArgumentNullException.ThrowIfNull(baseNestBytes);
        ArgumentNullException.ThrowIfNull(basePlacementBytes);
        ArgumentNullException.ThrowIfNull(baseItemHashBytes);

        RequireSupportedShopRelativePath(shopRelativePath);
        return new SwShRoyalCandyAcquisitionOwnershipManifestRecord(
            CurrentVersion,
            shopRelativePath,
            ComputeSha256(baseShopBytes),
            ComputeSha256(baseNestBytes),
            ComputeSha256(basePlacementBytes),
            ComputeSha256(baseItemHashBytes));
    }

    public static byte[] Write(
        SwShRoyalCandyAcquisitionOwnershipManifestRecord manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateManifestFields(manifest);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = false,
            }))
        {
            writer.WriteStartObject();
            writer.WriteNumber(PropertyNames[0], manifest.Version);
            writer.WriteString(PropertyNames[1], manifest.ShopRelativePath);
            writer.WriteString(PropertyNames[2], manifest.BaseShopSha256);
            writer.WriteString(PropertyNames[3], manifest.BaseNestSha256);
            writer.WriteString(PropertyNames[4], manifest.BasePlacementSha256);
            writer.WriteString(PropertyNames[5], manifest.BaseItemHashSha256);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static SwShRoyalCandyAcquisitionOwnershipManifestRecord Parse(
        byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.Length > MaximumManifestByteCount)
        {
            throw new InvalidDataException(
                $"Royal Candy acquisition ownership manifest must contain between 1 and {MaximumManifestByteCount} bytes.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 4,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "Royal Candy acquisition ownership manifest root must be an object.");
            }

            int? version = null;
            string? shopRelativePath = null;
            string? baseShopSha256 = null;
            string? baseNestSha256 = null;
            string? basePlacementSha256 = null;
            string? baseItemHashSha256 = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    throw new InvalidDataException(
                        $"Royal Candy acquisition ownership manifest property '{property.Name}' occurs more than once.");
                }

                switch (property.Name)
                {
                    case "version":
                        if (property.Value.ValueKind != JsonValueKind.Number
                            || !property.Value.TryGetInt32(out var parsedVersion))
                        {
                            throw new InvalidDataException(
                                "Royal Candy acquisition ownership manifest version must be an integer.");
                        }

                        version = parsedVersion;
                        break;
                    case "shopRelativePath":
                        shopRelativePath = ReadRequiredString(property);
                        break;
                    case "baseShopSha256":
                        baseShopSha256 = ReadRequiredString(property);
                        break;
                    case "baseNestSha256":
                        baseNestSha256 = ReadRequiredString(property);
                        break;
                    case "basePlacementSha256":
                        basePlacementSha256 = ReadRequiredString(property);
                        break;
                    case "baseItemHashSha256":
                        baseItemHashSha256 = ReadRequiredString(property);
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Royal Candy acquisition ownership manifest contains unknown property '{property.Name}'.");
                }
            }

            if (seen.Count != PropertyNames.Length
                || PropertyNames.Any(propertyName => !seen.Contains(propertyName)))
            {
                throw new InvalidDataException(
                    "Royal Candy acquisition ownership manifest does not contain exactly the required v1 properties.");
            }

            var manifest = new SwShRoyalCandyAcquisitionOwnershipManifestRecord(
                version!.Value,
                shopRelativePath!,
                baseShopSha256!,
                baseNestSha256!,
                basePlacementSha256!,
                baseItemHashSha256!);
            ValidateManifestFields(manifest);
            if (!bytes.AsSpan().SequenceEqual(Write(manifest)))
            {
                throw new InvalidDataException(
                    "Royal Candy acquisition ownership manifest is not in the canonical v1 encoding.");
            }

            return manifest;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Royal Candy acquisition ownership manifest is not valid JSON.",
                exception);
        }
    }

    public static void Validate(
        SwShRoyalCandyAcquisitionOwnershipManifestRecord manifest,
        string shopRelativePath,
        byte[] baseShopBytes,
        byte[] baseNestBytes,
        byte[] basePlacementBytes,
        byte[] baseItemHashBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var expected = Create(
            shopRelativePath,
            baseShopBytes,
            baseNestBytes,
            basePlacementBytes,
            baseItemHashBytes);
        ValidateManifestFields(manifest);

        if (!string.Equals(
            manifest.ShopRelativePath,
            expected.ShopRelativePath,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Royal Candy acquisition ownership manifest does not match the selected shop source.");
        }

        RequireMatchingHash(
            manifest.BaseShopSha256,
            expected.BaseShopSha256,
            "base shop");
        RequireMatchingHash(
            manifest.BaseNestSha256,
            expected.BaseNestSha256,
            "base raid reward");
        RequireMatchingHash(
            manifest.BasePlacementSha256,
            expected.BasePlacementSha256,
            "base placement");
        RequireMatchingHash(
            manifest.BaseItemHashSha256,
            expected.BaseItemHashSha256,
            "base item-hash");
    }

    public static SwShRoyalCandyAcquisitionOwnershipManifestRecord ParseAndValidate(
        byte[] manifestBytes,
        string shopRelativePath,
        byte[] baseShopBytes,
        byte[] baseNestBytes,
        byte[] basePlacementBytes,
        byte[] baseItemHashBytes)
    {
        var manifest = Parse(manifestBytes);
        Validate(
            manifest,
            shopRelativePath,
            baseShopBytes,
            baseNestBytes,
            basePlacementBytes,
            baseItemHashBytes);
        return manifest;
    }

    private static string ReadRequiredString(JsonProperty property)
    {
        if (property.Value.ValueKind != JsonValueKind.String
            || property.Value.GetString() is not { } value
            || value.Length == 0)
        {
            throw new InvalidDataException(
                $"Royal Candy acquisition ownership manifest property '{property.Name}' must be a nonempty string.");
        }

        return value;
    }

    private static void ValidateManifestFields(
        SwShRoyalCandyAcquisitionOwnershipManifestRecord manifest)
    {
        if (manifest.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Royal Candy acquisition ownership manifest version {manifest.Version} is not supported.");
        }

        RequireSupportedShopRelativePath(manifest.ShopRelativePath);
        RequireCanonicalSha256(manifest.BaseShopSha256, "baseShopSha256");
        RequireCanonicalSha256(manifest.BaseNestSha256, "baseNestSha256");
        RequireCanonicalSha256(manifest.BasePlacementSha256, "basePlacementSha256");
        RequireCanonicalSha256(manifest.BaseItemHashSha256, "baseItemHashSha256");
    }

    private static void RequireSupportedShopRelativePath(string relativePath)
    {
        if (!string.Equals(relativePath, ModernShopRelativePath, StringComparison.Ordinal)
            && !string.Equals(relativePath, LegacyShopRelativePath, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Royal Candy acquisition ownership manifest shop path is not a supported Sword/Shield shop relative path.");
        }
    }

    private static void RequireCanonicalSha256(string hash, string propertyName)
    {
        if (string.IsNullOrEmpty(hash)
            || hash.Length != SHA256.HashSizeInBytes * 2
            || hash.Any(character => !IsUpperHexCharacter(character)))
        {
            throw new InvalidDataException(
                $"Royal Candy acquisition ownership manifest property '{propertyName}' must be a 64-character uppercase SHA-256 hash.");
        }
    }

    private static bool IsUpperHexCharacter(char character)
    {
        return character is >= '0' and <= '9'
            or >= 'A' and <= 'F';
    }

    private static void RequireMatchingHash(
        string actual,
        string expected,
        string label)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Royal Candy acquisition ownership manifest does not match the authoritative {label} source.");
        }
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
