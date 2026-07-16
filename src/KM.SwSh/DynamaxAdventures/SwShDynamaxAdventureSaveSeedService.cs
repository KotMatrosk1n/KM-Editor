// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using PKHeX.Core;
using System.Globalization;
using System.Security.Cryptography;

namespace KM.SwSh.DynamaxAdventures;

public sealed class SwShDynamaxAdventureSaveSeedService
{
    public const uint MaxLairRentalChoiceSeedKey = 0x0D74AA40;
    private const int MaximumSaveByteLength = 64 * 1024 * 1024;

    private readonly Action<string, string>? afterReplacement;
    private readonly Action<string>? beforeRollbackReplacement;
    private readonly Action<string>? afterRollbackSwap;
    private readonly Action<string>? beforeExternalTargetRestoreReplacement;
    private readonly Action<string>? afterExternalTargetRestoreSwap;
    private readonly Action<string>? afterRollbackVerificationBeforeCleanup;

    public SwShDynamaxAdventureSaveSeedService()
    {
    }

    internal SwShDynamaxAdventureSaveSeedService(
        Action<string, string> afterReplacement,
        Action<string>? beforeRollbackReplacement = null,
        Action<string>? afterRollbackSwap = null,
        Action<string>? beforeExternalTargetRestoreReplacement = null,
        Action<string>? afterExternalTargetRestoreSwap = null,
        Action<string>? afterRollbackVerificationBeforeCleanup = null)
    {
        this.afterReplacement = afterReplacement ?? throw new ArgumentNullException(nameof(afterReplacement));
        this.beforeRollbackReplacement = beforeRollbackReplacement;
        this.afterRollbackSwap = afterRollbackSwap;
        this.beforeExternalTargetRestoreReplacement = beforeExternalTargetRestoreReplacement;
        this.afterExternalTargetRestoreSwap = afterExternalTargetRestoreSwap;
        this.afterRollbackVerificationBeforeCleanup = afterRollbackVerificationBeforeCleanup;
    }

    public SwShDynamaxAdventureSaveSeedResult SetSeed(ProjectPaths paths, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var diagnostics = new List<ValidationDiagnostic>();
        if (!SwShDynamaxAdventuresWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Writing a Max Lair seed requires Pokemon Sword or Pokemon Shield to be selected explicitly.",
                expected: "Selected Pokemon Sword or Pokemon Shield project"));
            return SwShDynamaxAdventureSaveSeedResult.NotWritten(seed, diagnostics);
        }

        if (!TryResolveSavePath(paths, diagnostics, out var savePath))
        {
            return SwShDynamaxAdventureSaveSeedResult.NotWritten(
                string.IsNullOrWhiteSpace(savePath) ? null : savePath,
                seed,
                diagnostics);
        }

        string? tempPath = null;
        try
        {
            var sourceBytes = ReadBoundedFile(savePath);
            var sourceSnapshot = CaptureSnapshot(sourceBytes);
            var save = ReadSwordShieldSave(sourceBytes, savePath);
            if (!SaveMatchesSelectedGame(save, paths.SelectedGame!.Value))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "The configured save belongs to the other Sword/Shield version. The Max Lair seed was not changed.",
                    file: savePath,
                    expected: $"A {paths.SelectedGame.Value} save file"));
                return SwShDynamaxAdventureSaveSeedResult.NotWritten(savePath, seed, diagnostics);
            }

            if (!save.ChecksumsValid)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Save checksums are invalid. The Max Lair seed was not changed.",
                    file: savePath,
                    expected: "A valid Sword/Shield save file"));
                return SwShDynamaxAdventureSaveSeedResult.NotWritten(savePath, seed, diagnostics);
            }

            var block = save.Accessor.GetBlockSafe(MaxLairRentalChoiceSeedKey);
            var oldSeed = (ulong)block.GetValue();
            if (oldSeed == seed)
            {
                return new SwShDynamaxAdventureSaveSeedResult(
                    savePath,
                    BackupFilePath: null,
                    FormatSeed(oldSeed),
                    FormatSeed(seed),
                    WasChanged: false,
                    ChecksumsValid: true,
                    diagnostics);
            }

            block.SetValue(seed);
            save.State.Edited = true;
            tempPath = WriteUniqueSiblingFile(savePath, "tmp", save.Write().ToArray());

            var tempBytes = ReadBoundedFile(tempPath);
            var tempSave = ReadSwordShieldSave(tempBytes, tempPath);
            var tempSeed = (ulong)tempSave.Accessor.GetBlockSafe(MaxLairRentalChoiceSeedKey).GetValue();
            if (!SaveMatchesSelectedGame(tempSave, paths.SelectedGame.Value)
                || !tempSave.ChecksumsValid
                || tempSeed != seed)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Rewritten save verification failed. The original save was not changed.",
                    file: savePath,
                    expected: "Verified checksum, selected game, and requested Max Lair seed"));
                return SwShDynamaxAdventureSaveSeedResult.NotWritten(savePath, seed, diagnostics);
            }

            if (CaptureFileSnapshot(savePath) != sourceSnapshot)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "The save changed after it was read. The Max Lair seed was not written.",
                    file: savePath,
                    expected: "Unchanged source save during verified replacement"));
                return SwShDynamaxAdventureSaveSeedResult.NotWritten(savePath, seed, diagnostics);
            }

            var replacement = ReplaceWithVerifiedRollback(
                savePath,
                tempPath,
                sourceSnapshot,
                CaptureSnapshot(tempBytes),
                () =>
                {
                    var finalBytes = ReadBoundedFile(savePath);
                    var finalSave = ReadSwordShieldSave(finalBytes, savePath);
                    var finalSeed = (ulong)finalSave.Accessor.GetBlockSafe(MaxLairRentalChoiceSeedKey).GetValue();
                    return SaveMatchesSelectedGame(finalSave, paths.SelectedGame.Value)
                        && finalSave.ChecksumsValid
                        && finalSeed == seed;
                });
            tempPath = null;

            if (!replacement.Succeeded)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    CreateReplacementFailureMessage(replacement),
                    file: savePath,
                    expected: "Atomic verified save replacement"));
                if (replacement.Exception is not null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Post-replacement verification failed: {replacement.Exception.Message}",
                        file: savePath,
                        expected: "Readable verified replacement"));
                }

                return new SwShDynamaxAdventureSaveSeedResult(
                    savePath,
                    replacement.RecoveryFilePath,
                    FormatSeed(oldSeed),
                    FormatSeed(seed),
                    WasChanged: false,
                    ChecksumsValid: replacement.RollbackVerified
                        && !replacement.SourceChanged
                        && !replacement.TargetDiverged,
                    diagnostics);
            }

            return new SwShDynamaxAdventureSaveSeedResult(
                savePath,
                replacement.BackupFilePath,
                FormatSeed(oldSeed),
                FormatSeed(seed),
                WasChanged: true,
                ChecksumsValid: true,
                diagnostics);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            || exception is InvalidOperationException
            || exception is IOException
            || exception is UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Max Lair seed could not be written: {exception.Message}",
                file: savePath,
                expected: "Writable Sword/Shield save file"));
            return SwShDynamaxAdventureSaveSeedResult.NotWritten(savePath, seed, diagnostics);
        }
        finally
        {
            if (tempPath is not null)
            {
                DeleteTempFile(tempPath);
            }
        }
    }

    public SwShDynamaxAdventureSaveSeedInspectResult Inspect(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var diagnostics = new List<ValidationDiagnostic>();
        if (!SwShDynamaxAdventuresWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reading a Max Lair seed requires Pokemon Sword or Pokemon Shield to be selected explicitly.",
                expected: "Selected Pokemon Sword or Pokemon Shield project"));
            return new SwShDynamaxAdventureSaveSeedInspectResult(null, null, false, diagnostics);
        }

        if (!TryResolveSavePath(paths, diagnostics, out var savePath))
        {
            return new SwShDynamaxAdventureSaveSeedInspectResult(null, null, false, diagnostics);
        }

        try
        {
            var save = ReadSwordShieldSave(ReadBoundedFile(savePath), savePath);
            if (!SaveMatchesSelectedGame(save, paths.SelectedGame!.Value))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "The configured save belongs to the other Sword/Shield version.",
                    file: savePath,
                    expected: $"A {paths.SelectedGame.Value} save file"));
                return new SwShDynamaxAdventureSaveSeedInspectResult(savePath, null, false, diagnostics);
            }

            var seed = (ulong)save.Accessor.GetBlockSafe(MaxLairRentalChoiceSeedKey).GetValue();
            return new SwShDynamaxAdventureSaveSeedInspectResult(
                savePath,
                FormatSeed(seed),
                save.ChecksumsValid,
                diagnostics);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            || exception is InvalidOperationException
            || exception is IOException
            || exception is UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Max Lair seed could not be read: {exception.Message}",
                file: savePath,
                expected: "Readable Sword/Shield save file"));
            return new SwShDynamaxAdventureSaveSeedInspectResult(savePath, null, false, diagnostics);
        }
    }

    private static bool TryResolveSavePath(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics,
        out string savePath)
    {
        savePath = paths.SaveFilePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "A configured Sword/Shield save file is required before writing a Max Lair seed.",
                expected: "Project Save File path"));
            return false;
        }

        if (Directory.Exists(savePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Save file path points to a directory.",
                file: savePath,
                expected: "Writable Sword/Shield save file"));
            return false;
        }

        if (!File.Exists(savePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Save file could not be found.",
                file: savePath,
                expected: "Writable Sword/Shield save file"));
            return false;
        }

        return true;
    }

    private static SAV8SWSH ReadSwordShieldSave(byte[] data, string path)
    {
        var save = SaveUtil.GetSaveFile(data, path);
        return save as SAV8SWSH
            ?? throw new InvalidDataException($"Not a Sword/Shield save: {save?.GetType().FullName ?? "<unknown>"}.");
    }

    private static bool SaveMatchesSelectedGame(SAV8SWSH save, ProjectGame selectedGame)
    {
        return selectedGame switch
        {
            ProjectGame.Sword => save.Version == GameVersion.SW,
            ProjectGame.Shield => save.Version == GameVersion.SH,
            _ => false,
        };
    }

    internal static string CreateReplacementFailureMessage(ReplacementResult replacement)
    {
        if (replacement.TargetDiverged)
        {
            return replacement.RecoveryFilePath is null
                ? "The save changed again during replacement or recovery. KM could not verify which version is currently live, and no verified recovery copy could be retained."
                : $"The save changed again during replacement or recovery. KM could not verify which version is currently live. A recovery copy was retained at '{replacement.RecoveryFilePath}'.";
        }

        if (!replacement.RollbackVerified)
        {
            return replacement.RecoveryFilePath is null
                ? "Save write verification failed. KM attempted recovery, but the restored save could not be verified. Inspect the save before continuing."
                : $"Save write verification failed. KM attempted recovery, but the restored save could not be verified. A recovery copy was retained at '{replacement.RecoveryFilePath}'.";
        }

        if (replacement.SourceChanged)
        {
            return replacement.RecoveryFilePath is null
                ? "The save changed during replacement. KM verified the version that was present immediately before replacement during recovery. Verify the live save before continuing."
                : $"The save changed during replacement. KM verified the version that was present immediately before replacement during recovery. A recovery copy was retained at '{replacement.RecoveryFilePath}'.";
        }

        return replacement.RecoveryFilePath is null
            ? "Save write verification failed. KM verified the prior save during recovery. Verify the live save before continuing."
            : $"Save write verification failed. KM verified the prior save during recovery. A recovery copy was retained at '{replacement.RecoveryFilePath}'.";
    }

    private ReplacementResult ReplaceWithVerifiedRollback(
        string savePath,
        string replacementPath,
        FileSnapshot expectedSource,
        FileSnapshot expectedReplacement,
        Func<bool> verifyReplacement)
    {
        var backupPath = CreateUniqueSiblingPath(savePath, "bak");
        var replacementCompleted = false;
        try
        {
            File.Replace(replacementPath, savePath, backupPath, ignoreMetadataErrors: true);
            replacementCompleted = true;
            afterReplacement?.Invoke(savePath, backupPath);

            var backupSnapshot = CaptureFileSnapshot(backupPath);
            if (backupSnapshot != expectedSource)
            {
                var rollback = RestoreOnlyIfTargetStillMatchesReplacement(
                    savePath,
                    backupPath,
                    backupSnapshot,
                    expectedReplacement);
                return new ReplacementResult(
                    Succeeded: false,
                    SourceChanged: true,
                    TargetDiverged: !rollback.RestoreWasSafe,
                    RollbackVerified: rollback.Verified,
                    BackupFilePath: null,
                    RecoveryFilePath: rollback.RecoveryFilePath,
                    Exception: null);
            }

            if (CaptureFileSnapshot(savePath) != expectedReplacement)
            {
                return new ReplacementResult(
                    Succeeded: false,
                    SourceChanged: false,
                    TargetDiverged: true,
                    RollbackVerified: false,
                    BackupFilePath: null,
                    RecoveryFilePath: backupPath,
                    Exception: null);
            }

            if (!verifyReplacement())
            {
                var rollback = RestoreOnlyIfTargetStillMatchesReplacement(
                    savePath,
                    backupPath,
                    backupSnapshot,
                    expectedReplacement);
                return new ReplacementResult(
                    Succeeded: false,
                    SourceChanged: false,
                    TargetDiverged: !rollback.RestoreWasSafe,
                    RollbackVerified: rollback.Verified,
                    BackupFilePath: null,
                    RecoveryFilePath: rollback.RecoveryFilePath,
                    Exception: null);
            }

            if (CaptureFileSnapshot(savePath) != expectedReplacement)
            {
                return new ReplacementResult(
                    Succeeded: false,
                    SourceChanged: false,
                    TargetDiverged: true,
                    RollbackVerified: false,
                    BackupFilePath: null,
                    RecoveryFilePath: backupPath,
                    Exception: null);
            }

            return new ReplacementResult(
                Succeeded: true,
                SourceChanged: false,
                TargetDiverged: false,
                RollbackVerified: false,
                BackupFilePath: backupPath,
                RecoveryFilePath: null,
                Exception: null);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            || exception is InvalidOperationException
            || exception is IOException
            || exception is UnauthorizedAccessException)
        {
            if (!replacementCompleted)
            {
                throw;
            }

            FileSnapshot? backupSnapshot = null;
            try
            {
                backupSnapshot = CaptureFileSnapshot(backupPath);
            }
            catch (Exception snapshotException) when (
                snapshotException is IOException || snapshotException is UnauthorizedAccessException)
            {
            }

            var sourceChanged = backupSnapshot is null || backupSnapshot.Value != expectedSource;
            var rollback = backupSnapshot is null
                ? new RollbackResult(false, false, File.Exists(backupPath) ? backupPath : null)
                : RestoreOnlyIfTargetStillMatchesReplacement(
                    savePath,
                    backupPath,
                    backupSnapshot.Value,
                    expectedReplacement);
            return new ReplacementResult(
                Succeeded: false,
                SourceChanged: sourceChanged,
                TargetDiverged: !rollback.RestoreWasSafe,
                RollbackVerified: rollback.Verified,
                BackupFilePath: null,
                RecoveryFilePath: rollback.RecoveryFilePath,
                Exception: exception);
        }
    }

    private RollbackResult RestoreOnlyIfTargetStillMatchesReplacement(
        string savePath,
        string backupPath,
        FileSnapshot backupSnapshot,
        FileSnapshot expectedReplacement)
    {
        try
        {
            if (CaptureFileSnapshot(savePath) != expectedReplacement)
            {
                return new RollbackResult(
                    RestoreWasSafe: false,
                    Verified: false,
                    RecoveryFilePath: File.Exists(backupPath) ? backupPath : null);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RollbackResult(
                RestoreWasSafe: false,
                Verified: false,
                RecoveryFilePath: File.Exists(backupPath) ? backupPath : null);
        }

        return RestoreReplacementBackup(
            savePath,
            backupPath,
            backupSnapshot,
            expectedReplacement);
    }

    private RollbackResult RestoreReplacementBackup(
        string savePath,
        string backupPath,
        FileSnapshot backupSnapshot,
        FileSnapshot expectedReplacement)
    {
        string? recoveryPath = null;
        string? failedWritePath = null;
        string? newerTargetRecoveryPath = null;
        string? displacedBackupPath = null;
        var rollbackSwapCompleted = false;
        try
        {
            recoveryPath = CopyToUniqueSiblingFile(savePath, "recovery", backupPath);
            failedWritePath = CreateUniqueSiblingPath(savePath, "failed");
            beforeRollbackReplacement?.Invoke(savePath);
            File.Replace(backupPath, savePath, failedWritePath, ignoreMetadataErrors: true);
            rollbackSwapCompleted = true;
            afterRollbackSwap?.Invoke(savePath);
            var displacedTargetSnapshot = CaptureFileSnapshot(failedWritePath);
            if (displacedTargetSnapshot != expectedReplacement)
            {
                newerTargetRecoveryPath = CopyToUniqueSiblingFile(
                    savePath,
                    "external-recovery",
                    failedWritePath);
                if (CaptureFileSnapshot(savePath) != backupSnapshot)
                {
                    return new RollbackResult(false, false, recoveryPath);
                }

                displacedBackupPath = CreateUniqueSiblingPath(savePath, "rollback-displaced");
                beforeExternalTargetRestoreReplacement?.Invoke(savePath);
                File.Replace(
                    failedWritePath,
                    savePath,
                    displacedBackupPath,
                    ignoreMetadataErrors: true);
                afterExternalTargetRestoreSwap?.Invoke(savePath);
                var displacedLiveSnapshot = CaptureFileSnapshot(displacedBackupPath);
                if (displacedLiveSnapshot != backupSnapshot)
                {
                    // Another writer landed after the pre-swap snapshot. The atomic replace
                    // retained those bytes in displacedBackupPath; keep that file and report
                    // the live target as unverified instead of deleting the newer version.
                    return new RollbackResult(false, false, displacedBackupPath);
                }

                if (CaptureFileSnapshot(savePath) != displacedTargetSnapshot)
                {
                    return new RollbackResult(false, false, newerTargetRecoveryPath);
                }

                DeleteTempFile(displacedBackupPath);
                DeleteTempFile(newerTargetRecoveryPath);
                return new RollbackResult(false, false, recoveryPath);
            }

            if (CaptureFileSnapshot(savePath) != backupSnapshot)
            {
                return new RollbackResult(true, false, recoveryPath);
            }

            afterRollbackVerificationBeforeCleanup?.Invoke(savePath);
            if (CaptureFileSnapshot(savePath) != backupSnapshot)
            {
                return new RollbackResult(true, false, recoveryPath);
            }

            DeleteTempFile(failedWritePath);
            return new RollbackResult(true, true, recoveryPath);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            || exception is IOException
            || exception is UnauthorizedAccessException)
        {
            return new RollbackResult(
                false,
                false,
                (displacedBackupPath is not null && File.Exists(displacedBackupPath)
                    ? displacedBackupPath
                    : null)
                    ?? newerTargetRecoveryPath
                    ?? (rollbackSwapCompleted && File.Exists(failedWritePath) ? failedWritePath : null)
                    ?? recoveryPath
                    ?? (File.Exists(backupPath) ? backupPath : null));
        }
    }

    internal ReplacementResult ReplaceWithVerifiedRollbackForTests(
        string savePath,
        byte[] replacementBytes,
        Func<bool> verifyReplacement,
        Action? beforeReplacement = null)
    {
        var sourceSnapshot = CaptureFileSnapshot(savePath);
        var replacementPath = WriteUniqueSiblingFile(savePath, "tmp", replacementBytes);
        var replacementSnapshot = CaptureSnapshot(replacementBytes);
        try
        {
            beforeReplacement?.Invoke();
            return ReplaceWithVerifiedRollback(
                savePath,
                replacementPath,
                sourceSnapshot,
                replacementSnapshot,
                verifyReplacement);
        }
        finally
        {
            DeleteTempFile(replacementPath);
        }
    }

    private static string CreateUniqueSiblingPath(string savePath, string purpose)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(savePath)) ?? string.Empty;
        var name = Path.GetFileName(savePath);
        for (var index = 0; index < 32; index++)
        {
            var candidate = Path.Combine(
                directory,
                $".{name}.km-editor.{purpose}.{Guid.NewGuid():N}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not allocate a unique same-directory save {purpose} path.");
    }

    private static string WriteUniqueSiblingFile(string savePath, string purpose, ReadOnlySpan<byte> data)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(savePath)) ?? string.Empty;
        var name = Path.GetFileName(savePath);
        for (var index = 0; index < 32; index++)
        {
            var candidate = Path.Combine(directory, $".{name}.km-editor.{purpose}.{Guid.NewGuid():N}");
            try
            {
                using var stream = new FileStream(
                    candidate,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.SequentialScan);
                stream.Write(data);
                stream.Flush(flushToDisk: true);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
            }
        }

        throw new IOException($"Could not create a unique same-directory save {purpose} file.");
    }

    private static string CopyToUniqueSiblingFile(string savePath, string purpose, string sourcePath)
    {
        return WriteUniqueSiblingFile(savePath, purpose, ReadBoundedFile(sourcePath));
    }

    private static byte[] ReadBoundedFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length < 1 || stream.Length > MaximumSaveByteLength)
        {
            throw new InvalidDataException(
                $"Save length {stream.Length.ToString(CultureInfo.InvariantCulture)} is outside the supported allocation bound.");
        }

        var data = new byte[checked((int)stream.Length)];
        stream.ReadExactly(data);
        return data;
    }

    private static FileSnapshot CaptureFileSnapshot(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        return new FileSnapshot(stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
    }

    private static FileSnapshot CaptureSnapshot(ReadOnlySpan<byte> data)
    {
        return new FileSnapshot(data.Length, Convert.ToHexString(SHA256.HashData(data)));
    }

    private static void DeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FormatSeed(ulong seed)
    {
        return string.Create(CultureInfo.InvariantCulture, $"0x{seed:X16}");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: "workflow.dynamaxAdventures.seed",
            Expected: expected);
    }

    private readonly record struct FileSnapshot(long Length, string Sha256);

    internal sealed record ReplacementResult(
        bool Succeeded,
        bool SourceChanged,
        bool TargetDiverged,
        bool RollbackVerified,
        string? BackupFilePath,
        string? RecoveryFilePath,
        Exception? Exception);

    private readonly record struct RollbackResult(
        bool RestoreWasSafe,
        bool Verified,
        string? RecoveryFilePath);
}

public sealed record SwShDynamaxAdventureSaveSeedInspectResult(
    string? SaveFilePath,
    string? Seed,
    bool ChecksumsValid,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShDynamaxAdventureSaveSeedResult(
    string? SaveFilePath,
    string? BackupFilePath,
    string? OldSeed,
    string NewSeed,
    bool WasChanged,
    bool ChecksumsValid,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public static SwShDynamaxAdventureSaveSeedResult NotWritten(
        ulong seed,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return NotWritten(saveFilePath: null, seed, diagnostics);
    }

    public static SwShDynamaxAdventureSaveSeedResult NotWritten(
        string? saveFilePath,
        ulong seed,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShDynamaxAdventureSaveSeedResult(
            saveFilePath,
            BackupFilePath: null,
            OldSeed: null,
            string.Create(CultureInfo.InvariantCulture, $"0x{seed:X16}"),
            WasChanged: false,
            ChecksumsValid: false,
            diagnostics);
    }
}
