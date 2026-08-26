// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.Core.RuntimeSettings;

public readonly record struct GameplayBundleVersion(uint Major, uint Minor, uint Patch)
{
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Major}.{Minor}.{Patch}");
}

public sealed record GameplayBundleSemanticComponent(string Path, string InputSha256);

public sealed record GameplayBundleOutputComponent(string Path, ulong Length, string Sha256);

public sealed record GameplayBundleHandshake(
    uint BundleAbi,
    uint Word0,
    uint Word1,
    uint Word2,
    uint Word3);

public sealed record GameplayBundleManifest(
    ulong TitleId,
    GameplayBundleVersion UpdateVersion,
    string BuildId,
    uint BundleAbi,
    string BundleId,
    ushort SettingsSchema,
    GameplayBundleVersion PackageVersion,
    IReadOnlyList<GameplayBundleOutputComponent> Components);

public static class GameplayBundleIdentity
{
    public const uint BundleAbi = 1;
    public const ushort SettingsSchema = 1;
    public const int MaximumComponentCount = 4096;
    public const int MaximumNormalizedPathLength = 1024;
    public const int MaximumSerializationBytes = 4 * 1024 * 1024;

    private const string IdentityTag = "KM-BUNDLE-IDENTITY-1";
    private const string ManifestTag = "KM-BUNDLE-MANIFEST-1";
    private static readonly byte[] IdentityLinePrefix = Encoding.ASCII.GetBytes(IdentityTag + "\n");
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] SerializeIdentityPreimage(
        ulong titleId,
        GameplayBundleVersion updateVersion,
        string buildId,
        GameplayBundleVersion packageVersion,
        string sourceRevision,
        string profileSha256,
        IReadOnlyList<GameplayBundleSemanticComponent> components)
    {
        ValidateTitleId(titleId);
        ValidateUpperHex(buildId, 64, nameof(buildId));
        ValidateUpperHex(sourceRevision, 40, nameof(sourceRevision));
        ValidateUpperHex(profileSha256, 64, nameof(profileSha256));
        var normalized = NormalizeSemanticComponents(components);
        var builder = new StringBuilder();
        builder.Append(IdentityTag).Append('\n');
        builder.Append("titleId=").Append(titleId.ToString("X16", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("update=").Append(updateVersion).Append('\n');
        builder.Append("buildId=").Append(buildId).Append('\n');
        builder.Append("bundleAbi=").Append(BundleAbi).Append('\n');
        builder.Append("settingsSchema=").Append(SettingsSchema).Append('\n');
        builder.Append("kmVersion=").Append(packageVersion).Append('\n');
        builder.Append("sourceRevision=").Append(sourceRevision).Append('\n');
        builder.Append("profileSha256=").Append(profileSha256).Append('\n');
        foreach (var component in normalized)
        {
            builder.Append("component=")
                .Append(component.Path)
                .Append('\t')
                .Append(component.InputSha256)
                .Append('\n');
        }

        return EncodeBounded(builder.ToString());
    }

    public static string CreateBundleId(ReadOnlySpan<byte> identityPreimage)
    {
        if (identityPreimage.Length is < 1 or > MaximumSerializationBytes
            || identityPreimage[^1] != (byte)'\n'
            || identityPreimage.IndexOf((byte)'\r') >= 0
            || identityPreimage.Length < IdentityLinePrefix.Length
            || !identityPreimage[..IdentityLinePrefix.Length].SequenceEqual(IdentityLinePrefix))
        {
            throw new ArgumentException("The bundle identity preimage is invalid or out of bounds.", nameof(identityPreimage));
        }

        var prefix = Encoding.ASCII.GetBytes(IdentityTag + "\0");
        var hashInput = new byte[checked(prefix.Length + identityPreimage.Length)];
        prefix.CopyTo(hashInput, 0);
        identityPreimage.CopyTo(hashInput.AsSpan(prefix.Length));
        return Convert.ToHexString(SHA256.HashData(hashInput)[..16]);
    }

    public static GameplayBundleHandshake CreateHandshake(string bundleId)
    {
        ValidateUpperHex(bundleId, 32, nameof(bundleId));
        var bytes = Convert.FromHexString(bundleId);
        return new GameplayBundleHandshake(
            BundleAbi,
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12, 4)));
    }

    public static bool MatchesHandshake(
        string bundleId,
        uint bundleAbi,
        ulong word0,
        ulong word1,
        ulong word2,
        ulong word3)
    {
        var expected = CreateHandshake(bundleId);
        var extraBits = (word0 | word1 | word2 | word3) >> 32;
        var difference = (ulong)(bundleAbi ^ expected.BundleAbi)
            | extraBits
            | ((uint)word0 ^ expected.Word0)
            | ((uint)word1 ^ expected.Word1)
            | ((uint)word2 ^ expected.Word2)
            | ((uint)word3 ^ expected.Word3);
        return difference == 0;
    }

    public static byte[] SerializeManifest(GameplayBundleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateTitleId(manifest.TitleId);
        ValidateUpperHex(manifest.BuildId, 64, nameof(manifest.BuildId));
        if (manifest.BundleAbi != BundleAbi || manifest.SettingsSchema != SettingsSchema)
        {
            throw new ArgumentException("The bundle manifest uses an unsupported ABI or settings schema.", nameof(manifest));
        }

        ValidateUpperHex(manifest.BundleId, 32, nameof(manifest.BundleId));
        var components = NormalizeOutputComponents(
            manifest.Components,
            manifest.TitleId,
            rejectReservedPaths: true);
        var builder = new StringBuilder();
        builder.Append(ManifestTag).Append('\n');
        builder.Append("titleId=").Append(manifest.TitleId.ToString("X16", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("update=").Append(manifest.UpdateVersion).Append('\n');
        builder.Append("buildId=").Append(manifest.BuildId).Append('\n');
        builder.Append("bundleAbi=").Append(BundleAbi).Append('\n');
        builder.Append("bundleId=").Append(manifest.BundleId).Append('\n');
        builder.Append("settingsSchema=").Append(SettingsSchema).Append('\n');
        builder.Append("kmVersion=").Append(manifest.PackageVersion).Append('\n');
        builder.Append("componentCount=")
            .Append(components.Count.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        builder.Append("--\n");
        foreach (var component in components)
        {
            builder.Append(component.Path)
                .Append('\t')
                .Append(component.Length.ToString(CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(component.Sha256)
                .Append('\n');
        }

        return EncodeBounded(builder.ToString());
    }

    public static GameplayBundleManifest ParseManifest(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaximumSerializationBytes
            || bytes[^1] != (byte)'\n'
            || bytes.Length >= 2 && bytes[^2] == (byte)'\n'
            || bytes.IndexOf((byte)'\r') >= 0
            || bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF)
        {
            throw new InvalidDataException("The bundle manifest text envelope is not canonical.");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The bundle manifest is not valid UTF-8.", exception);
        }

        var lines = text[..^1].Split('\n', StringSplitOptions.None);
        if (lines.Length < 10
            || lines[0] != ManifestTag
            || lines[9] != "--")
        {
            throw new InvalidDataException("The bundle manifest header is incomplete or out of order.");
        }

        var titleId = ParseUpperHexUlong(ReadHeader(lines[1], "titleId"), 16, "titleId");
        var update = ParseVersion(ReadHeader(lines[2], "update"));
        var buildId = ReadHeader(lines[3], "buildId");
        ValidateUpperHex(buildId, 64, "buildId");
        var bundleAbi = ParseCanonicalUInt(ReadHeader(lines[4], "bundleAbi"), "bundleAbi");
        var bundleId = ReadHeader(lines[5], "bundleId");
        ValidateUpperHex(bundleId, 32, "bundleId");
        var settingsSchema = ParseCanonicalUshort(ReadHeader(lines[6], "settingsSchema"), "settingsSchema");
        var packageVersion = ParseVersion(ReadHeader(lines[7], "kmVersion"));
        var componentCount = ParseCanonicalUInt(ReadHeader(lines[8], "componentCount"), "componentCount");
        if (bundleAbi != BundleAbi
            || settingsSchema != SettingsSchema
            || componentCount > MaximumComponentCount
            || lines.Length != checked(10 + componentCount))
        {
            throw new InvalidDataException("The bundle manifest ABI, schema, or component count is invalid.");
        }

        var components = new List<GameplayBundleOutputComponent>((int)componentCount);
        for (var index = 0; index < componentCount; index++)
        {
            var parts = lines[checked(10 + (int)index)].Split('\t', StringSplitOptions.None);
            if (parts.Length != 3)
            {
                throw new InvalidDataException("A bundle manifest component line is malformed.");
            }

            ValidateNormalizedPath(parts[0]);
            var length = ParseCanonicalUlong(parts[1], "component length");
            ValidateUpperHex(parts[2], 64, "component hash");
            components.Add(new GameplayBundleOutputComponent(parts[0], length, parts[2]));
        }

        var normalized = NormalizeOutputComponents(components, titleId, rejectReservedPaths: true);
        if (!components.SequenceEqual(normalized))
        {
            throw new InvalidDataException("Bundle manifest components are not in canonical order.");
        }

        var manifest = new GameplayBundleManifest(
            titleId,
            update,
            buildId,
            bundleAbi,
            bundleId,
            settingsSchema,
            packageVersion,
            normalized);
        if (!SerializeManifest(manifest).AsSpan().SequenceEqual(bytes))
        {
            throw new InvalidDataException("The bundle manifest did not pass canonical round-trip validation.");
        }

        return manifest;
    }

    public static void VerifyComponents(
        GameplayBundleManifest manifest,
        IReadOnlyDictionary<string, byte[]> componentBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(componentBytes);
        if (componentBytes.Count != manifest.Components.Count)
        {
            throw new InvalidDataException("The supplied bundle component set is incomplete or contains extras.");
        }

        foreach (var expected in manifest.Components)
        {
            if (!componentBytes.TryGetValue(expected.Path, out var bytes) || bytes is null)
            {
                throw new InvalidDataException("A required bundle component is missing.");
            }

            if ((ulong)bytes.LongLength != expected.Length
                || !string.Equals(
                    Convert.ToHexString(SHA256.HashData(bytes)),
                    expected.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("A bundle component does not match its manifest identity.");
            }
        }

        foreach (var suppliedPath in componentBytes.Keys)
        {
            ValidateNormalizedPath(suppliedPath);
            if (!manifest.Components.Any(component => string.Equals(component.Path, suppliedPath, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("The supplied bundle component set contains an unexpected path.");
            }
        }
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static IReadOnlyList<GameplayBundleSemanticComponent> NormalizeSemanticComponents(
        IReadOnlyList<GameplayBundleSemanticComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Count is < 1 or > MaximumComponentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(components));
        }

        var normalized = components.Select(component =>
        {
            ArgumentNullException.ThrowIfNull(component);
            ValidateNormalizedPath(component.Path);
            ValidateUpperHex(component.InputSha256, 64, nameof(component.InputSha256));
            return component;
        }).OrderBy(component => component.Path, OrdinalUtf8Comparer.Instance).ToArray();
        ValidateUniquePaths(normalized.Select(component => component.Path));
        return normalized;
    }

    private static IReadOnlyList<GameplayBundleOutputComponent> NormalizeOutputComponents(
        IReadOnlyList<GameplayBundleOutputComponent> components,
        ulong titleId,
        bool rejectReservedPaths)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Count is < 1 or > MaximumComponentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(components));
        }

        var settingsRoot = $"config/km-editor/gameplay-settings/{titleId:X16}/";
        var normalized = components.Select(component =>
        {
            ArgumentNullException.ThrowIfNull(component);
            ValidateNormalizedPath(component.Path);
            ValidateUpperHex(component.Sha256, 64, nameof(component.Sha256));
            if (rejectReservedPaths
                && (string.Equals(component.Path, settingsRoot + "settings.bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(component.Path, settingsRoot + "bundle.manifest", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Runtime-mutable settings and the manifest itself cannot be immutable manifest components.", nameof(components));
            }

            return component;
        }).OrderBy(component => component.Path, OrdinalUtf8Comparer.Instance).ToArray();
        ValidateUniquePaths(normalized.Select(component => component.Path));
        return normalized;
    }

    private static void ValidateUniquePaths(IEnumerable<string> paths)
    {
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!exact.Add(path) || !folded.Add(path))
            {
                throw new ArgumentException("Bundle component paths must be unique without case collisions.", nameof(paths));
            }
        }
    }

    internal static void ValidateNormalizedPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Length > MaximumNormalizedPathLength
            || !path.IsNormalized(NormalizationForm.FormC)
            || path[0] == '/'
            || path[^1] == '/'
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains(':', StringComparison.Ordinal)
            || path.Any(character => character != '/' && !IsPortablePathCharacter(character)))
        {
            throw new ArgumentException("A bundle component path is not normalized and portable.", nameof(path));
        }

        foreach (var segment in path.Split('/', StringSplitOptions.None))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new ArgumentException("A bundle component path contains an unsafe segment.", nameof(path));
            }
        }
    }

    private static bool IsPortablePathCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.';
    }

    private static void ValidateUpperHex(string value, int length, string parameterName)
    {
        if (value is null
            || value.Length != length
            || value.Any(character =>
                !char.IsAsciiHexDigit(character)
                || character is >= 'a' and <= 'f'))
        {
            throw new ArgumentException($"{parameterName} must be {length} uppercase hexadecimal characters.", parameterName);
        }
    }

    private static string ReadHeader(string line, string key)
    {
        var prefix = key + "=";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || line.Length == prefix.Length)
        {
            throw new InvalidDataException("A bundle manifest header field is missing or malformed.");
        }

        return line[prefix.Length..];
    }

    private static GameplayBundleVersion ParseVersion(string value)
    {
        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 3)
        {
            throw new InvalidDataException("A bundle version must have three canonical decimal components.");
        }

        return new GameplayBundleVersion(
            ParseCanonicalUInt(parts[0], "version"),
            ParseCanonicalUInt(parts[1], "version"),
            ParseCanonicalUInt(parts[2], "version"));
    }

    private static uint ParseCanonicalUInt(string value, string field)
    {
        var parsed = ParseCanonicalUlong(value, field);
        if (parsed > uint.MaxValue)
        {
            throw new InvalidDataException($"The {field} value exceeds its supported range.");
        }

        return (uint)parsed;
    }

    private static ushort ParseCanonicalUshort(string value, string field)
    {
        var parsed = ParseCanonicalUlong(value, field);
        if (parsed > ushort.MaxValue)
        {
            throw new InvalidDataException($"The {field} value exceeds its supported range.");
        }

        return (ushort)parsed;
    }

    private static ulong ParseCanonicalUlong(string value, string field)
    {
        if (value.Length == 0
            || value.Length > 1 && value[0] == '0'
            || !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"The {field} value is not canonical unsigned decimal.");
        }

        return parsed;
    }

    private static ulong ParseUpperHexUlong(string value, int length, string field)
    {
        ValidateUpperHex(value, length, field);
        if (!ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"The {field} value is outside the supported hexadecimal range.");
        }

        return parsed;
    }

    private static byte[] EncodeBounded(string text)
    {
        var bytes = StrictUtf8.GetBytes(text);
        if (bytes.Length > MaximumSerializationBytes)
        {
            throw new InvalidDataException("The bundle identity serialization exceeds its bounded size.");
        }

        return bytes;
    }

    private static void ValidateTitleId(ulong titleId)
    {
        if (titleId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(titleId));
        }
    }

    private sealed class OrdinalUtf8Comparer : IComparer<string>
    {
        public static OrdinalUtf8Comparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            return StrictUtf8.GetBytes(left).AsSpan().SequenceCompareTo(StrictUtf8.GetBytes(right));
        }
    }
}
