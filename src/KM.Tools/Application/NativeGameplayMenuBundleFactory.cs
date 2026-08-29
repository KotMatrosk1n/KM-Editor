// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Api.RuntimeSettings;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;
using KM.Formats.Executable;
using KM.SV.RuntimeSettings;
using KM.SwSh.RuntimeSettings;
using KM.ZA.RuntimeSettings;

namespace KM.Tools.Application;

/// <summary>
/// Derives one exact-build, stock-menu gameplay package from the user's own
/// retail ExeFS and RomFS inputs. No executable or game asset is editor-shipped;
/// every title-owned output is transformed from the selected project's source.
/// </summary>
public static class NativeGameplayMenuBundleFactory
{
    public const string DeliveryId = "native-gameplay-menu-v1";
    public const string RuntimeComponentName = "subsdk9";
    private const int MaximumNativeMenuComponentCount = 64;

    private static readonly GameplayBundleVersion PackageVersion = new(2, 5, 0);
    private static readonly GameplayBundleVersion FirstNativeMenuPackageVersion = new(2, 5, 0);
    private static readonly GameplaySettingPresence SettingsPresence =
        GameplaySettingPresence.ExperienceShare
        | GameplaySettingPresence.ExperienceRate
        | GameplaySettingPresence.LevelCap;
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static bool IsNativeMenuManifest(
        ProjectGame game,
        GameplayBundleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var metadata = ProjectGameMetadata.Get(game);
        if (manifest.TitleId != metadata.TitleId
            || manifest.UpdateVersion != GetUpdateVersion(game)
            || CompareVersion(manifest.PackageVersion, FirstNativeMenuPackageVersion) < 0
            || CompareVersion(manifest.PackageVersion, PackageVersion) > 0
            || manifest.BundleAbi != GameplayBundleIdentity.BundleAbi
            || manifest.SettingsSchema != GameplayBundleIdentity.SettingsSchema
            || !HasBoundedArchiveInventory(manifest))
        {
            return false;
        }

        try
        {
            EnsureExpectedBuild(game, manifest.BuildId);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        return GetRecognizedTitleRoots(metadata.TitleId)
            .Any(titleRoot => HasNativeMenuInventoryAtRoot(manifest, titleRoot));
    }

    internal static bool IsNativeMenuManifestForTarget(
        ProjectGame game,
        GameplayBundleManifest manifest,
        InGameSettingsInstallationTargetDto installationTarget)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateInstallationTarget(installationTarget);
        return IsNativeMenuManifest(game, manifest)
            && HasNativeMenuInventoryAtRoot(
                manifest,
                GetTitleRoot(manifest.TitleId, installationTarget));
    }

    internal static bool IsRetiredExternalControlManifest(
        ProjectGame game,
        GameplayBundleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var metadata = ProjectGameMetadata.Get(game);
        if (manifest.TitleId != metadata.TitleId
            || manifest.UpdateVersion != GetUpdateVersion(game)
            || manifest.PackageVersion != new GameplayBundleVersion(2, 5, 0)
            || manifest.BundleAbi != GameplayBundleIdentity.BundleAbi
            || manifest.SettingsSchema != GameplayBundleIdentity.SettingsSchema
            || !HasBoundedArchiveInventory(manifest))
        {
            return false;
        }

        try
        {
            EnsureExpectedBuild(game, manifest.BuildId);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        var cheatRoot = string.Create(
            CultureInfo.InvariantCulture,
            $"atmosphere/contents/{metadata.TitleId:X16}/cheats/");
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            cheatRoot + manifest.BuildId[..16] + ".txt",
            cheatRoot + "toggles.txt",
        };
        return manifest.Components
            .Select(component => component.Path)
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(expected);
    }

    public static InGameSettingsBundleCatalogEntry CreateEntry(
        ProjectGame game,
        ReadOnlySpan<byte> retailMain,
        ReadOnlySpan<byte> retailMainNpdm,
        ReadOnlySpan<byte> runtimeNso,
        IReadOnlyDictionary<string, byte[]> transformedRomFsComponents,
        InGameSettingsInstallationTargetDto installationTarget =
            InGameSettingsInstallationTargetDto.Atmosphere)
    {
        return CreateEntry(
            game,
            retailMain,
            retailMainNpdm,
            retailMain,
            retailMainNpdm,
            runtimeNso,
            transformedRomFsComponents,
            installationTarget);
    }

    /// <summary>
    /// Builds the native menu package from an already composed executable
    /// source while retaining the exact retail files as the build and
    /// preimage authority. Compatible changes outside KM's verified runtime
    /// regions are preserved in the generated title-layer executable.
    /// </summary>
    public static InGameSettingsBundleCatalogEntry CreateEntry(
        ProjectGame game,
        ReadOnlySpan<byte> retailMain,
        ReadOnlySpan<byte> retailMainNpdm,
        ReadOnlySpan<byte> executableSourceMain,
        ReadOnlySpan<byte> executableSourceMainNpdm,
        ReadOnlySpan<byte> runtimeNso,
        IReadOnlyDictionary<string, byte[]> transformedRomFsComponents,
        InGameSettingsInstallationTargetDto installationTarget =
            InGameSettingsInstallationTargetDto.Atmosphere)
    {
        ArgumentNullException.ThrowIfNull(transformedRomFsComponents);
        ValidateInstallationTarget(installationTarget);
        var metadata = ProjectGameMetadata.Get(game);
        var family = ToFamily(game);
        var update = GetUpdateVersion(game);
        var settingsFamily = GameplayBundleDeploymentPlanner.ToSettingsFamily(family);
        var sourceMain = retailMain.ToArray();
        var parsedSource = NsoFile.Parse(sourceMain);
        var buildId = Convert.ToHexString(parsedSource.BuildId);
        EnsureExpectedBuild(game, buildId);
        EnsureExpectedNpdmTitle(game, retailMainNpdm);
        EnsureExpectedNpdm(game, retailMainNpdm);

        var composedSourceMain = executableSourceMain.ToArray();
        var composedSourceNpdm = executableSourceMainNpdm.ToArray();
        var initialSettings = ReadInitialSettings(
            game,
            sourceMain,
            composedSourceMain);
        if (!sourceMain.AsSpan().SequenceEqual(composedSourceMain)
            && !NsoRegisteredRegionCompositionVerifier.HasCompatibleLayoutEnvelope(
                sourceMain,
                composedSourceMain)
            && !initialSettings.IsLegacyStaticOutput)
        {
            throw new InvalidDataException(
                "The executable composition source does not match the supported Base layout envelope.");
        }
        EnsureExpectedNpdmTitle(game, composedSourceNpdm);

        var guestModule = runtimeNso.ToArray();
        ValidateGuestModule(guestModule);
        var derivedMain = game switch
        {
            ProjectGame.Scarlet => SvGameplaySettingsMainPatcher
                .BuildRuntimeManaged(
                    sourceMain,
                    composedSourceMain,
                    SvGameplayRuntimeEdition.Scarlet).Main,
            ProjectGame.Violet => SvGameplaySettingsMainPatcher
                .BuildRuntimeManaged(
                    sourceMain,
                    composedSourceMain,
                    SvGameplayRuntimeEdition.Violet).Main,
            ProjectGame.Sword or ProjectGame.Shield =>
                SwShStaticGameplaySettingsMainPatcher
                    .BuildRuntimeManaged(sourceMain, composedSourceMain, game).Main,
            ProjectGame.ZA => ZaStaticGameplaySettingsMainPatcher
                .BuildRuntimeManaged(sourceMain, composedSourceMain, game).Main,
            _ => throw new ArgumentOutOfRangeException(nameof(game)),
        };
        var derivedNpdm = NpdmRuntimeCapabilityPatcher
            .AddGuestRuntimeCapabilities(composedSourceNpdm);
        NpdmRuntimeCapabilityPatcher.VerifyDerived(composedSourceNpdm, derivedNpdm);

        var titleRoot = GetTitleRoot(metadata.TitleId, installationTarget);
        var components = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [titleRoot + "exefs/main"] = derivedMain,
            [titleRoot + "exefs/main.npdm"] = derivedNpdm,
            [titleRoot + "exefs/" + RuntimeComponentName] = guestModule,
        };
        foreach (var (relativePath, bytes) in transformedRomFsComponents
                     .OrderBy(component => component.Key, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(bytes);
            var normalized = NormalizeRomFsPath(relativePath);
            if (!components.TryAdd(titleRoot + normalized, bytes.ToArray()))
            {
                throw new InvalidDataException(
                    "A native gameplay menu output path is duplicated.");
            }
        }
        if (!components.Keys.Any(path =>
                path.StartsWith(titleRoot + "romfs/", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "A native gameplay menu package requires its exact transformed stock-menu assets.");
        }
        if (components.Count > MaximumNativeMenuComponentCount)
        {
            throw new InvalidDataException(
                "The native gameplay menu component inventory exceeds its bounded size.");
        }

        var componentHashes = components
            .OrderBy(component => component.Key, StringComparer.Ordinal)
            .Select(component => new GameplayBundleSemanticComponent(
                component.Key,
                Hash(component.Value)))
            .ToArray();
        var profile = CreateProfilePreimage(
            game,
            buildId,
            Hash(sourceMain),
            Hash(retailMainNpdm),
            Hash(composedSourceMain),
            Hash(composedSourceNpdm),
            Hash(guestModule),
            componentHashes);
        var profileHash = Convert.ToHexString(SHA256.HashData(profile));
        var sourceRevision = Convert.ToHexString(
            SHA1.HashData(Utf8.GetBytes(DeliveryId)));
        var identity = GameplayBundleIdentity.SerializeIdentityPreimage(
            metadata.TitleId,
            update,
            buildId,
            PackageVersion,
            sourceRevision,
            profileHash,
            componentHashes);
        var bundleId = GameplayBundleIdentity.CreateBundleId(identity);
        var manifest = new GameplayBundleManifest(
            metadata.TitleId,
            update,
            buildId,
            GameplayBundleIdentity.BundleAbi,
            bundleId,
            GameplayBundleIdentity.SettingsSchema,
            PackageVersion,
            components
                .OrderBy(component => component.Key, StringComparer.Ordinal)
                .Select(component => new GameplayBundleOutputComponent(
                    component.Key,
                    checked((ulong)component.Value.LongLength),
                    Hash(component.Value)))
                .ToArray());
        var journal = GameplaySettingsJournal.CreateBootstrap(
            settingsFamily,
            metadata.TitleId,
            new GameplaySettingsWriterVersion(
                checked((ushort)PackageVersion.Major),
                checked((ushort)PackageVersion.Minor),
                checked((ushort)PackageVersion.Patch)),
            SettingsPresence,
            initialSettings.Values);
        var archive = GameplayBundleArchive.Build(
            manifest,
            components,
            settingsFamily,
            journal);
        return new InGameSettingsBundleCatalogEntry(
            family,
            archive.Bytes,
            isCurrent: true);
    }

    private static InitialExecutableSettings ReadInitialSettings(
        ProjectGame game,
        byte[] baseMain,
        byte[] currentMain)
    {
        return game switch
        {
            ProjectGame.Scarlet => ReadScarletVioletInitialSettings(
                currentMain,
                SvGameplayRuntimeEdition.Scarlet),
            ProjectGame.Violet => ReadScarletVioletInitialSettings(
                currentMain,
                SvGameplayRuntimeEdition.Violet),
            ProjectGame.Sword or ProjectGame.Shield =>
                ReadSwordShieldInitialSettings(baseMain, currentMain, game),
            ProjectGame.ZA => ReadZaInitialSettings(baseMain, currentMain),
            _ => throw new ArgumentOutOfRangeException(nameof(game)),
        };
    }

    private static InitialExecutableSettings ReadScarletVioletInitialSettings(
        byte[] currentMain,
        SvGameplayRuntimeEdition edition)
    {
        var analysis = SvGameplaySettingsMainPatcher.Analyze(currentMain, edition);
        if (analysis.Kind is not (SvGameplaySettingsMainKind.Vanilla
            or SvGameplaySettingsMainKind.Modified))
        {
            throw new InvalidDataException(analysis.Message);
        }

        return new InitialExecutableSettings(
            analysis.Values,
            analysis.Kind == SvGameplaySettingsMainKind.Modified);
    }

    private static InitialExecutableSettings ReadSwordShieldInitialSettings(
        byte[] baseMain,
        byte[] currentMain,
        ProjectGame game)
    {
        var analysis = SwShStaticGameplaySettingsMainPatcher.Analyze(
            baseMain,
            currentMain,
            game);
        if (analysis.Kind is not (SwShStaticGameplaySettingsMainKind.Vanilla
            or SwShStaticGameplaySettingsMainKind.Configured)
            || analysis.ExperienceShareEnabled is null
            || analysis.ExperienceRateBasisPoints is null)
        {
            throw new InvalidDataException(analysis.Message);
        }

        return new InitialExecutableSettings(
            new GameplaySettingsValues(
                analysis.ExperienceShareEnabled.Value,
                analysis.ExperienceRateBasisPoints.Value,
                analysis.LevelCapEnabled,
                analysis.LevelCap),
            analysis.Kind == SwShStaticGameplaySettingsMainKind.Configured);
    }

    private static InitialExecutableSettings ReadZaInitialSettings(
        byte[] baseMain,
        byte[] currentMain)
    {
        var analysis = ZaStaticGameplaySettingsMainPatcher.Analyze(
            baseMain,
            currentMain,
            ProjectGame.ZA);
        if (analysis.Kind is not (ZaStaticGameplaySettingsMainKind.Vanilla
            or ZaStaticGameplaySettingsMainKind.Configured)
            || analysis.ExperienceShareEnabled is null
            || analysis.ExperienceRateBasisPoints is null)
        {
            throw new InvalidDataException(analysis.Message);
        }

        return new InitialExecutableSettings(
            new GameplaySettingsValues(
                analysis.ExperienceShareEnabled.Value,
                analysis.ExperienceRateBasisPoints.Value,
                analysis.LevelCapEnabled,
                analysis.LevelCap),
            analysis.Kind == ZaStaticGameplaySettingsMainKind.Configured);
    }

    private sealed record InitialExecutableSettings(
        GameplaySettingsValues Values,
        bool IsLegacyStaticOutput);

    private static byte[] CreateProfilePreimage(
        ProjectGame game,
        string buildId,
        string baseMainSha256,
        string baseNpdmSha256,
        string sourceMainSha256,
        string sourceNpdmSha256,
        string runtimeSha256,
        IReadOnlyList<GameplayBundleSemanticComponent> components)
    {
        var builder = new StringBuilder()
            .Append("KM-NATIVE-GAMEPLAY-MENU-PROFILE-1\n")
            .Append("game=").Append(game).Append('\n')
            .Append("buildId=").Append(buildId).Append('\n')
            .Append("retailMain=").Append(baseMainSha256).Append('\n')
            .Append("retailNpdm=").Append(baseNpdmSha256).Append('\n')
            .Append("sourceMain=").Append(sourceMainSha256).Append('\n')
            .Append("sourceNpdm=").Append(sourceNpdmSha256).Append('\n')
            .Append("runtime=").Append(runtimeSha256).Append('\n');
        foreach (var component in components)
        {
            builder.Append("component=")
                .Append(component.Path).Append('\t')
                .Append(component.InputSha256).Append('\n');
        }
        return Utf8.GetBytes(builder.ToString());
    }

    private static void ValidateGuestModule(byte[] runtimeNso)
    {
        if (runtimeNso.Length is < NsoFile.HeaderSize or > 8 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "The native settings guest module is empty or exceeds its bounded size.");
        }
        var parsed = NsoFile.Parse(runtimeNso);
        if (parsed.Text.DecompressedData.Length == 0
            || parsed.Ro.DecompressedData.Length == 0
            || parsed.Data.Header.MemoryOffset <= parsed.Ro.Header.MemoryOffset
            || parsed.BuildId.All(value => value == 0))
        {
            throw new InvalidDataException(
                "The native settings guest module does not have the required canonical NSO layout.");
        }
    }

    private static string NormalizeRomFsPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith("romfs/", StringComparison.Ordinal))
        {
            normalized = "romfs/" + normalized;
        }
        _ = new RelativeOutputPath(normalized);
        if (normalized == "romfs")
        {
            throw new InvalidDataException("A native menu RomFS component has no file path.");
        }
        return normalized;
    }

    internal static string GetExecutableDestinationPath(
        ulong titleId,
        InGameSettingsInstallationTargetDto installationTarget) =>
        GetTitleRoot(titleId, installationTarget) + "exefs/main";

    internal static bool IsRuntimePackageComponentPath(string path, ulong titleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return GetRecognizedTitleRoots(titleId).Any(titleRoot =>
            path.StartsWith(titleRoot + "exefs/", StringComparison.Ordinal)
            || path.StartsWith(titleRoot + "romfs/", StringComparison.Ordinal));
    }

    internal static IReadOnlyList<string> GetEquivalentRuntimePackagePaths(
        string path,
        ulong titleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var roots = GetRecognizedTitleRoots(titleId);
        var sourceRoot = roots.FirstOrDefault(root =>
            path.StartsWith(root, StringComparison.Ordinal));
        if (sourceRoot is null)
        {
            return [path];
        }

        var suffix = path[sourceRoot.Length..];
        return roots.Select(root => root + suffix).ToArray();
    }

    private static bool HasNativeMenuInventoryAtRoot(
        GameplayBundleManifest manifest,
        string titleRoot)
    {
        var requiredExeFs = new HashSet<string>(StringComparer.Ordinal)
        {
            titleRoot + "exefs/main",
            titleRoot + "exefs/main.npdm",
            titleRoot + "exefs/" + RuntimeComponentName,
        };
        var actualExeFs = manifest.Components
            .Where(component => component.Path.StartsWith(
                titleRoot + "exefs/",
                StringComparison.Ordinal))
            .Select(component => component.Path)
            .ToHashSet(StringComparer.Ordinal);
        return manifest.Components.Count <= MaximumNativeMenuComponentCount
            && actualExeFs.SetEquals(requiredExeFs)
            && manifest.Components.Any(component => component.Path.StartsWith(
                titleRoot + "romfs/",
                StringComparison.Ordinal))
            && manifest.Components.All(component =>
                component.Path.StartsWith(titleRoot + "exefs/", StringComparison.Ordinal)
                || component.Path.StartsWith(titleRoot + "romfs/", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> GetRecognizedTitleRoots(ulong titleId) =>
    [
        GetTitleRoot(titleId, InGameSettingsInstallationTargetDto.Atmosphere),
        GetTitleRoot(titleId, InGameSettingsInstallationTargetDto.Ryujinx),
        GetTitleRoot(titleId, InGameSettingsInstallationTargetDto.Eden),
    ];

    private static string GetTitleRoot(
        ulong titleId,
        InGameSettingsInstallationTargetDto installationTarget)
    {
        ValidateInstallationTarget(installationTarget);
        return installationTarget switch
        {
            InGameSettingsInstallationTargetDto.Atmosphere => string.Create(
                CultureInfo.InvariantCulture,
                $"atmosphere/contents/{titleId:X16}/"),
            InGameSettingsInstallationTargetDto.Ryujinx => string.Create(
                CultureInfo.InvariantCulture,
                $"mods/contents/{titleId:X16}/KM-Gameplay-Settings/"),
            InGameSettingsInstallationTargetDto.Eden => string.Create(
                CultureInfo.InvariantCulture,
                $"load/{titleId:X16}/KM-Gameplay-Settings/"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(installationTarget),
                installationTarget,
                null),
        };
    }

    private static void ValidateInstallationTarget(
        InGameSettingsInstallationTargetDto installationTarget)
    {
        if (!Enum.IsDefined(installationTarget))
        {
            throw new ArgumentOutOfRangeException(
                nameof(installationTarget),
                installationTarget,
                null);
        }
    }

    private static void EnsureExpectedBuild(ProjectGame game, string buildId)
    {
        var expected = game switch
        {
            ProjectGame.Sword =>
                "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000",
            ProjectGame.Shield =>
                "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000",
            ProjectGame.Scarlet =>
                "421C5411B487EB4D049DD065FEC9547773E8E598000000000000000000000000",
            ProjectGame.Violet =>
                "709BFD66115298640155FCC4979DBA151C7CC79A000000000000000000000000",
            ProjectGame.ZA =>
                "B1F12FD919EAE86AB8A978317677E64BCE443D1F000000000000000000000000",
            _ => throw new ArgumentOutOfRangeException(nameof(game)),
        };
        if (!string.Equals(buildId, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The selected base executable is not the exact supported game build.");
        }
    }

    private static void EnsureExpectedNpdmTitle(
        ProjectGame game,
        ReadOnlySpan<byte> retailMainNpdm)
    {
        const uint metaMagic = 0x4154454D;
        const uint aci0Magic = 0x30494341;
        const int aci0OffsetField = 0x70;
        const int aci0ProgramIdOffset = 0x10;
        if (retailMainNpdm.Length < 0x80
            || BinaryPrimitives.ReadUInt32LittleEndian(retailMainNpdm) != metaMagic)
        {
            throw new InvalidDataException(
                "The selected base main.npdm does not have a supported metadata envelope.");
        }

        var aci0Offset = BinaryPrimitives.ReadInt32LittleEndian(
            retailMainNpdm[aci0OffsetField..]);
        if (aci0Offset < 0
            || aci0Offset > retailMainNpdm.Length - (aci0ProgramIdOffset + sizeof(ulong))
            || BinaryPrimitives.ReadUInt32LittleEndian(retailMainNpdm[aci0Offset..])
                != aci0Magic)
        {
            throw new InvalidDataException(
                "The selected base main.npdm does not contain a supported ACI0 identity.");
        }

        var actualTitleId = BinaryPrimitives.ReadUInt64LittleEndian(
            retailMainNpdm[(aci0Offset + aci0ProgramIdOffset)..]);
        if (actualTitleId != ProjectGameMetadata.Get(game).TitleId)
        {
            throw new InvalidDataException(
                "The selected base main.npdm belongs to a different game title.");
        }
    }

    private static bool HasBoundedArchiveInventory(GameplayBundleManifest manifest)
    {
        try
        {
            if (manifest.Components.Count + 2 > GameplayBundleArchive.MaximumEntryCount)
            {
                return false;
            }

            long totalBytes = checked(
                GameplaySettingsJournal.JournalSize
                + GameplayBundleIdentity.SerializeManifest(manifest).LongLength);
            foreach (var component in manifest.Components)
            {
                if (component.Length > GameplayBundleArchive.MaximumEntryBytes)
                {
                    return false;
                }

                totalBytes = checked(totalBytes + (long)component.Length);
                if (totalBytes > GameplayBundleArchive.MaximumArchivePayloadBytes)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            OverflowException)
        {
            return false;
        }
    }

    private static GameFamily ToFamily(ProjectGame game) => game switch
    {
        ProjectGame.Sword or ProjectGame.Shield => GameFamily.SwordShield,
        ProjectGame.Scarlet or ProjectGame.Violet => GameFamily.ScarletViolet,
        ProjectGame.ZA => GameFamily.LegendsZA,
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    private static void EnsureExpectedNpdm(
        ProjectGame game,
        ReadOnlySpan<byte> retailMainNpdm)
    {
        var expected = game switch
        {
            ProjectGame.Sword =>
                "0367B2364FCA2086EDDE95F5022506C50670B79BC7BC23240C2DE6215149CD5A",
            ProjectGame.Shield =>
                "CA29FC6ADC54B4D0278147DCE67FDEBCC8A02D2CD72344343E22CC786714A270",
            ProjectGame.Scarlet =>
                "EDF2BBF506A3619CCB1702D390DDA609959B5192646791A6374DBEE232322DC5",
            ProjectGame.Violet =>
                "1C060AA14B39652D89F5F90D27E7393E0C67AD807FCCCB183864F29A5F3D3792",
            ProjectGame.ZA =>
                "9960D4579CB8F8AEDFAC6E51D8452E31C5ACDD0E39308CFC0D4A42955D956058",
            _ => throw new ArgumentOutOfRangeException(nameof(game)),
        };
        if (!string.Equals(Hash(retailMainNpdm), expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The selected main.npdm is not the exact supported game build.");
        }
    }

    private static GameplayBundleVersion GetUpdateVersion(ProjectGame game) => game switch
    {
        ProjectGame.Sword or ProjectGame.Shield => new GameplayBundleVersion(1, 3, 2),
        ProjectGame.Scarlet or ProjectGame.Violet => new GameplayBundleVersion(4, 0, 0),
        ProjectGame.ZA => new GameplayBundleVersion(2, 0, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static int CompareVersion(
        GameplayBundleVersion left,
        GameplayBundleVersion right)
    {
        var major = left.Major.CompareTo(right.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = left.Minor.CompareTo(right.Minor);
        return minor != 0 ? minor : left.Patch.CompareTo(right.Patch);
    }
}
