// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;

namespace KM.Tools.Application;

/// <summary>
/// Identifies one exact gameplay runtime bundle that completed its title-specific proof process.
/// The manifest fingerprint binds the authorization to the complete immutable component inventory.
/// </summary>
public sealed record GameplaySettingsBundleAuthorityKey
{
    public GameplaySettingsBundleAuthorityKey(
        GameFamily gameFamily,
        ulong titleId,
        GameplayBundleVersion packageVersion,
        string bundleId,
        string manifestSha256)
    {
        if (!Enum.IsDefined(gameFamily))
        {
            throw new ArgumentOutOfRangeException(nameof(gameFamily));
        }

        GameFamily = gameFamily;
        var game = ProjectGameMetadata.DetectByTitleId(titleId);
        if (game is null || !gameFamily.Contains(game.Value))
        {
            throw new ArgumentException(
                "The gameplay bundle title does not belong to the selected game family.",
                nameof(titleId));
        }

        if (packageVersion.Major > ushort.MaxValue
            || packageVersion.Minor > ushort.MaxValue
            || packageVersion.Patch > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(packageVersion),
                "The gameplay bundle package version is out of bounds.");
        }

        ValidateUpperHex(bundleId, 32, nameof(bundleId));
        ValidateUpperHex(manifestSha256, 64, nameof(manifestSha256));
        TitleId = titleId;
        PackageVersion = packageVersion;
        BundleId = bundleId;
        ManifestSha256 = manifestSha256;
    }

    public GameFamily GameFamily { get; }

    public ulong TitleId { get; }

    public GameplayBundleVersion PackageVersion { get; }

    public string BundleId { get; }

    public string ManifestSha256 { get; }

    public static GameplaySettingsBundleAuthorityKey FromManifest(
        GameFamily gameFamily,
        GameplayBundleManifest manifest,
        ReadOnlySpan<byte> canonicalManifestBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var game = ProjectGameMetadata.DetectByTitleId(manifest.TitleId);
        if (game is null || !gameFamily.Contains(game.Value))
        {
            throw new ArgumentException(
                "The gameplay bundle title does not belong to the selected game family.",
                nameof(manifest));
        }

        var canonical = GameplayBundleIdentity.SerializeManifest(manifest);
        if (!canonicalManifestBytes.SequenceEqual(canonical))
        {
            throw new ArgumentException(
                "The gameplay bundle authority requires the canonical manifest bytes.",
                nameof(canonicalManifestBytes));
        }

        return new GameplaySettingsBundleAuthorityKey(
            gameFamily,
            manifest.TitleId,
            manifest.PackageVersion,
            manifest.BundleId,
            Convert.ToHexString(SHA256.HashData(canonicalManifestBytes)));
    }

    private static void ValidateUpperHex(string value, int exactLength, string parameterName)
    {
        if (value is null
            || value.Length != exactLength
            || value.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException(
                "A gameplay bundle authority identity is not canonical uppercase hexadecimal.",
                parameterName);
        }
    }
}

/// <summary>
/// Immutable allowlist for exact gameplay runtime bundles. The production default is deny-all.
/// </summary>
public sealed class GameplaySettingsBundleAuthority
{
    public const int MaximumAuthorizedBundles = 32;

    private readonly ImmutableHashSet<GameplaySettingsBundleAuthorityKey> authorizedBundles;

    private GameplaySettingsBundleAuthority(
        ImmutableHashSet<GameplaySettingsBundleAuthorityKey> authorizedBundles)
    {
        this.authorizedBundles = authorizedBundles;
    }

    public static GameplaySettingsBundleAuthority DenyAll { get; } = new(
        ImmutableHashSet<GameplaySettingsBundleAuthorityKey>.Empty);

    public static GameplaySettingsBundleAuthority AllowOnly(
        IEnumerable<GameplaySettingsBundleAuthorityKey> authorizedBundles)
    {
        ArgumentNullException.ThrowIfNull(authorizedBundles);
        var builder = ImmutableHashSet.CreateBuilder<GameplaySettingsBundleAuthorityKey>();
        foreach (var bundle in authorizedBundles)
        {
            if (bundle is null)
            {
                throw new ArgumentException(
                    "A gameplay bundle authority cannot contain a null identity.",
                    nameof(authorizedBundles));
            }

            if (builder.Count == MaximumAuthorizedBundles)
            {
                throw new ArgumentException(
                    "The gameplay bundle authority inventory is out of bounds.",
                    nameof(authorizedBundles));
            }

            if (!builder.Add(bundle))
            {
                throw new ArgumentException(
                    "A gameplay bundle authority cannot contain duplicate identities.",
                    nameof(authorizedBundles));
            }
        }

        return new GameplaySettingsBundleAuthority(builder.ToImmutable());
    }

    public bool IsAuthorized(GameplaySettingsBundleAuthorityKey bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return authorizedBundles.Contains(bundle);
    }
}
