// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using KM.Api.RuntimeSettings;
using KM.Core.Concurrency;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SV.RuntimeSettings;
using KM.SwSh.RuntimeSettings;
using KM.ZA.RuntimeSettings;

namespace KM.Tools.Application;

/// <summary>
/// Builds an authorized native-menu package from the selected project's exact
/// Base files and, when present, a compatible reviewed standalone executable.
/// Retail executable and menu assets are always read from the user's project;
/// the only embedded binary is KM's independently authored guest runtime.
/// </summary>
public sealed class NativeGameplayMenuBundleProvider : IInGameSettingsBundleProvider
{
    private const int MaximumMainBytes = 128 * 1024 * 1024;
    private const int MaximumNpdmBytes = 4 * 1024 * 1024;
    private const int MaximumRuntimeBytes = 8 * 1024 * 1024;
    private const long MaximumPreparationWorkerBytes = 768L * 1024L * 1024L;
    private const string RuntimeResourceName =
        "KM.Tools.Resources.km-native-settings.nso";

    private static readonly Lazy<byte[]> Runtime = new(
        LoadEmbeddedRuntime,
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly BoundedConcurrencyPolicy PreparationPolicy = new(
        "native-gameplay-menu-source-preparation",
        BoundedWorkloadKind.Decode,
        MaximumPreparationWorkerBytes,
        maximumDegreeOfParallelism: 3,
        memoryBudgetDivisor: 2,
        degreeOfParallelismWhenMemoryUnknown: 1);

    public Task<InGameSettingsBundleResolution> ResolveAsync(
        ProjectPaths paths,
        ProjectGame game,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(
            paths,
            game,
            InGameSettingsInstallationTargetDto.Atmosphere,
            cancellationToken);

    public Task<InGameSettingsBundleResolution> ResolveAsync(
        ProjectPaths paths,
        ProjectGame game,
        InGameSettingsInstallationTargetDto installationTarget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!Enum.IsDefined(installationTarget))
        {
            throw new ArgumentOutOfRangeException(
                nameof(installationTarget),
                installationTarget,
                null);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (paths.SelectedGame != game)
        {
            return Task.FromResult(Unavailable(
                "The selected project game changed while the native menu package was being prepared."));
        }

        IReadOnlyList<OutputReadDependency>? sourceDependencies = null;
        var usesComposedMain = false;
        var usesComposedMainNpdm = false;
        var requiresOwnedMainSource = false;
        var requiresOwnedMainNpdmSource = false;
        var reviewingOutputSources = false;
        try
        {
            // Fail before scanning or transforming user sources when the KM-owned
            // runtime was not embedded correctly in this application build.
            var runtime = Runtime.Value;
            if (runtime.Length is 0 or > MaximumRuntimeBytes)
            {
                throw new InvalidDataException(
                    "The embedded native gameplay runtime is invalid.");
            }

            var prepared = BoundedParallel.MapOrdered<object>(
                3,
                PreparationPolicy,
                (index, token) => index switch
                {
                    0 => ReadBaseExeFsFile(
                        paths.BaseExeFsPath,
                        "main",
                        MaximumMainBytes,
                        token),
                    1 => ReadBaseExeFsFile(
                        paths.BaseExeFsPath,
                        "main.npdm",
                        MaximumNpdmBytes,
                        token),
                    2 => BuildRomFsComponents(paths, game, token),
                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                },
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var main = (byte[])prepared[0];
            var npdm = (byte[])prepared[1];
            var romFs = (IReadOnlyDictionary<string, byte[]>)prepared[2];

            reviewingOutputSources = true;
            var reviewedSources = BoundedParallel.MapOrdered<ReviewedOutputSource>(
                3,
                PreparationPolicy,
                (index, token) => index switch
                {
                    0 => ReviewOutputExeFsFile(
                        paths.OutputRootPath,
                        "main",
                        MaximumMainBytes,
                        token),
                    1 => ReviewOutputExeFsFile(
                        paths.OutputRootPath,
                        "main.npdm",
                        MaximumNpdmBytes,
                        token),
                    2 => ReviewOutputExeFsFile(
                        paths.OutputRootPath,
                        NativeGameplayMenuBundleFactory.RuntimeComponentName,
                        MaximumRuntimeBytes,
                        token),
                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                },
                cancellationToken);
            reviewingOutputSources = false;
            var outputMain = reviewedSources[0];
            var outputNpdm = reviewedSources[1];
            var outputRuntimeSlot = reviewedSources[2];
            sourceDependencies = reviewedSources
                .Select(source => new OutputReadDependency(source.Path, source.State))
                .ToArray();
            usesComposedMain = outputMain.Exists;
            usesComposedMainNpdm = outputNpdm.Exists;
            requiresOwnedMainSource = outputMain.Exists
                && !outputMain.Bytes.AsSpan().SequenceEqual(main);
            requiresOwnedMainNpdmSource = outputNpdm.Exists
                && !outputNpdm.Bytes.AsSpan().SequenceEqual(npdm);
            if (outputRuntimeSlot.Exists)
            {
                return Task.FromResult(Unavailable(
                    "The standalone ExeFS already uses subsdk9, which is the exact runtime module slot required by the native gameplay menu. KM will not replace or shadow it.",
                    sourceDependencies,
                    usesComposedMain,
                    usesComposedMainNpdm,
                    requiresOwnedMainSource,
                    requiresOwnedMainNpdmSource));
            }

            var executableSourceMain = outputMain.Exists ? outputMain.Bytes : main;
            var executableSourceNpdm = outputNpdm.Exists ? outputNpdm.Bytes : npdm;
            var semanticallyVerifiedMainSource = requiresOwnedMainSource
                && IsSemanticallyVerifiedOutput(main, outputMain.Bytes, game);

            var entry = NativeGameplayMenuBundleFactory.CreateEntry(
                game,
                main,
                npdm,
                executableSourceMain,
                executableSourceNpdm,
                runtime,
                romFs,
                installationTarget);
            return Task.FromResult(new InGameSettingsBundleResolution(
                InGameSettingsBundleCatalog.Create([entry]),
                GameplaySettingsBundleAuthority.AllowOnly([entry.AuthorityKey]),
                UnavailableDetail: null,
                sourceDependencies,
                usesComposedMain,
                usesComposedMainNpdm,
                requiresOwnedMainSource,
                requiresOwnedMainNpdmSource,
                AttemptedSourcePath: null,
                SemanticallyVerifiedMainSource: semanticallyVerifiedMainSource));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedUnavailable(exception))
        {
            var attemptedSourcePath = reviewingOutputSources
                ? GetAttemptedOutputSourcePath(exception)
                : null;
            return Task.FromResult(Unavailable(
                usesComposedMain || usesComposedMainNpdm
                    ? "The standalone ExeFS is not compatible with the native gameplay menu's verified executable regions. KM preserved it and changed no project file."
                    : "Native in-game controls require the exact supported Base ExeFS and Base RomFS files for this game version. No project file was changed.",
                sourceDependencies,
                usesComposedMain,
                usesComposedMainNpdm,
                requiresOwnedMainSource,
                requiresOwnedMainNpdmSource,
                attemptedSourcePath));
        }
    }

    private static bool IsSemanticallyVerifiedOutput(
        ReadOnlySpan<byte> retailMain,
        ReadOnlySpan<byte> candidateMain,
        ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword or ProjectGame.Shield =>
                SwShKnownExecutableCompositionVerifier.IsCompatibleRegisteredOutput(
                    retailMain,
                    candidateMain,
                    game),
            ProjectGame.Scarlet or ProjectGame.Violet =>
                SvKnownExecutableCompositionVerifier.IsCompatibleRegisteredOutput(
                    retailMain,
                    candidateMain,
                    game),
            ProjectGame.ZA =>
                ZaKnownExecutableCompositionVerifier.IsCompatibleRegisteredOutput(
                    retailMain,
                    candidateMain,
                    game),
            _ => false,
        };
    }

    private static IReadOnlyDictionary<string, byte[]> BuildRomFsComponents(
        ProjectPaths paths,
        ProjectGame game,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return game switch
        {
            ProjectGame.Scarlet or ProjectGame.Violet =>
                SvNativeGameplayMenuRomFsMaterializer.Build(paths, cancellationToken),
            ProjectGame.Sword or ProjectGame.Shield when
                !string.IsNullOrWhiteSpace(paths.BaseRomFsPath) =>
                SwShNativeGameplayMenuRomFsMaterializer.Build(
                    game,
                    paths.BaseRomFsPath,
                    cancellationToken),
            ProjectGame.ZA =>
                ZaNativeGameplayMenuRomFsMaterializer.Build(paths, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(game)),
        };
    }

    private static byte[] ReadBaseExeFsFile(
        string? baseExeFsRoot,
        string fileName,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseExeFsRoot)
            || !Path.IsPathFullyQualified(baseExeFsRoot))
        {
            throw new DirectoryNotFoundException(
                "The selected project has no valid Base ExeFS folder.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseExeFsRoot));
        var rootInfo = new DirectoryInfo(root);
        rootInfo.Refresh();
        if (!rootInfo.Exists
            || !string.IsNullOrEmpty(rootInfo.LinkTarget)
            || !rootInfo.Attributes.HasFlag(FileAttributes.Directory))
        {
            throw new IOException("The Base ExeFS folder is not a safe regular directory.");
        }

        var path = Path.GetFullPath(Path.Combine(root, fileName));
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new IOException("A Base ExeFS source escapes its configured folder.");
        }

        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists
            || !string.IsNullOrEmpty(info.LinkTarget)
            || info.Attributes.HasFlag(FileAttributes.Directory)
            || info.Length <= 0
            || info.Length > maximumBytes)
        {
            throw new IOException("A required Base ExeFS source is missing or invalid.");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var expectedLength = stream.Length;
        if (expectedLength <= 0 || expectedLength > maximumBytes)
        {
            throw new IOException("A required Base ExeFS source has an invalid size.");
        }

        var bytes = new byte[checked((int)expectedLength)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes.AsSpan(offset));
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "A required Base ExeFS source ended before its reviewed size.");
            }

            offset += read;
        }
        info.Refresh();
        if (!info.Exists
            || !string.IsNullOrEmpty(info.LinkTarget)
            || info.Length != expectedLength)
        {
            throw new IOException("A Base ExeFS source changed while it was being read.");
        }

        return bytes;
    }

    private static ReviewedOutputSource ReviewOutputExeFsFile(
        string? outputRoot,
        string fileName,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var relativePath = new RelativeOutputPath($"exefs/{fileName}");
        if (string.IsNullOrWhiteSpace(outputRoot)
            || !Path.IsPathFullyQualified(outputRoot))
        {
            throw new DirectoryNotFoundException(
                "The selected project has no valid Output Root folder.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        var rootInfo = new DirectoryInfo(root);
        rootInfo.Refresh();
        if (!rootInfo.Exists)
        {
            if (!string.IsNullOrEmpty(rootInfo.LinkTarget)
                || File.Exists(root))
            {
                throw new IOException("The Output Root path is not a safe regular directory.");
            }

            return ReviewedOutputSource.Missing(relativePath);
        }
        if (!string.IsNullOrEmpty(rootInfo.LinkTarget)
            || !rootInfo.Attributes.HasFlag(FileAttributes.Directory))
        {
            throw new IOException("The Output Root folder is not a safe regular directory.");
        }

        var exefsPath = Path.GetFullPath(Path.Combine(root, "exefs"));
        EnsureContained(root, exefsPath);
        var exefsDirectory = new DirectoryInfo(exefsPath);
        exefsDirectory.Refresh();
        if (!exefsDirectory.Exists)
        {
            var fileOccupant = new FileInfo(exefsPath);
            fileOccupant.Refresh();
            if (!string.IsNullOrEmpty(exefsDirectory.LinkTarget)
                || !string.IsNullOrEmpty(fileOccupant.LinkTarget)
                || fileOccupant.Exists)
            {
                throw new IOException("The Output Root exefs path is occupied or linked.");
            }

            return ReviewedOutputSource.Missing(relativePath);
        }
        if (!string.IsNullOrEmpty(exefsDirectory.LinkTarget)
            || !exefsDirectory.Attributes.HasFlag(FileAttributes.Directory))
        {
            throw new IOException("The Output Root exefs folder is not a safe regular directory.");
        }

        var path = Path.GetFullPath(Path.Combine(exefsPath, fileName));
        EnsureContained(root, path);
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        file.Refresh();
        directory.Refresh();
        if (!string.IsNullOrEmpty(file.LinkTarget)
            || !string.IsNullOrEmpty(directory.LinkTarget)
            || directory.Exists)
        {
            throw new IOException("A standalone ExeFS source is occupied, linked, or ambiguous.");
        }
        if (!file.Exists)
        {
            return ReviewedOutputSource.Missing(relativePath);
        }
        if (file.Attributes.HasFlag(FileAttributes.Directory)
            || file.Length <= 0
            || file.Length > maximumBytes)
        {
            throw new IOException("A standalone ExeFS source is not a safe bounded regular file.");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var expectedLength = stream.Length;
        if (expectedLength <= 0 || expectedLength > maximumBytes)
        {
            throw new IOException("A standalone ExeFS source has an invalid size.");
        }

        var bytes = new byte[checked((int)expectedLength)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes.AsSpan(offset));
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "A standalone ExeFS source ended before its reviewed size.");
            }

            offset += read;
        }

        file.Refresh();
        directory.Refresh();
        if (!file.Exists
            || !string.IsNullOrEmpty(file.LinkTarget)
            || !string.IsNullOrEmpty(directory.LinkTarget)
            || directory.Exists
            || file.Length != expectedLength)
        {
            throw new IOException("A standalone ExeFS source changed while it was read.");
        }

        var state = OutputFileState.Existing(
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bytes.LongLength);
        return new ReviewedOutputSource(relativePath, true, bytes, state);
    }

    private static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new IOException("A standalone ExeFS source escapes the selected Output Root.");
        }
    }

    private static byte[] LoadEmbeddedRuntime()
    {
        var assembly = typeof(NativeGameplayMenuBundleProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(RuntimeResourceName)
            ?? throw new InvalidDataException(
                "The native gameplay runtime resource is missing.");
        if (!stream.CanRead || stream.Length is <= 0 or > MaximumRuntimeBytes)
        {
            throw new InvalidDataException(
                "The native gameplay runtime resource has an invalid size.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException(
                "The native gameplay runtime resource changed while it was read.");
        }
        return bytes;
    }

    private static InGameSettingsBundleResolution Unavailable(
        string detail,
        IReadOnlyList<OutputReadDependency>? sourceDependencies = null,
        bool usesComposedMain = false,
        bool usesComposedMainNpdm = false,
        bool requiresOwnedMainSource = false,
        bool requiresOwnedMainNpdmSource = false,
        RelativeOutputPath? attemptedSourcePath = null) => new(
            InGameSettingsBundleCatalog.Empty,
            GameplaySettingsBundleAuthority.DenyAll,
            detail,
            sourceDependencies,
            usesComposedMain,
            usesComposedMainNpdm,
            requiresOwnedMainSource,
            requiresOwnedMainNpdmSource,
            attemptedSourcePath);

    private static RelativeOutputPath? GetAttemptedOutputSourcePath(Exception exception)
    {
        var bounded = exception as BoundedWorkItemException;
        if (bounded is null
            && exception is AggregateException aggregate)
        {
            bounded = aggregate.Flatten().InnerExceptions
                .OfType<BoundedWorkItemException>()
                .OrderBy(failure => failure.ItemIndex)
                .FirstOrDefault();
        }

        return bounded?.ItemIndex switch
        {
            0 => new RelativeOutputPath("exefs/main"),
            1 => new RelativeOutputPath("exefs/main.npdm"),
            2 => new RelativeOutputPath(
                $"exefs/{NativeGameplayMenuBundleFactory.RuntimeComponentName}"),
            _ => null,
        };
    }

    private static bool IsExpectedUnavailable(Exception exception)
    {
        return exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            InvalidDataException or
            NotSupportedException or
            OverflowException or
            BadImageFormatException
            || exception is BoundedWorkItemException bounded
                && IsExpectedUnavailable(bounded.InnerException!)
            || exception is AggregateException aggregate
                && aggregate.InnerExceptions.All(IsExpectedUnavailable);
    }

    private sealed record ReviewedOutputSource(
        RelativeOutputPath Path,
        bool Exists,
        byte[] Bytes,
        OutputFileState State)
    {
        public static ReviewedOutputSource Missing(RelativeOutputPath path) => new(
            path,
            false,
            [],
            OutputFileState.Missing);
    }
}
