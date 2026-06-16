// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using PKHeX.Core;
using System.Globalization;

namespace KM.SwSh.DynamaxAdventures;

public sealed class SwShDynamaxAdventureSaveSeedService
{
    public const uint MaxLairRentalChoiceSeedKey = 0x0D74AA40;

    public SwShDynamaxAdventureSaveSeedResult SetSeed(ProjectPaths paths, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var diagnostics = new List<ValidationDiagnostic>();
        if (!TryResolveSavePath(paths, diagnostics, out var savePath))
        {
            return SwShDynamaxAdventureSaveSeedResult.NotWritten(
                string.IsNullOrWhiteSpace(savePath) ? null : savePath,
                seed,
                diagnostics);
        }

        try
        {
            var save = ReadSwordShieldSave(savePath);
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
            var tempPath = savePath + ".km-editor.tmp";
            File.WriteAllBytes(tempPath, save.Write().ToArray());

            try
            {
                var tempSave = ReadSwordShieldSave(tempPath);
                var tempSeed = (ulong)tempSave.Accessor.GetBlockSafe(MaxLairRentalChoiceSeedKey).GetValue();
                if (!tempSave.ChecksumsValid || tempSeed != seed)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Rewritten save verification failed. The original save was not changed.",
                        file: savePath,
                        expected: "Verified checksum and requested Max Lair seed"));
                    return SwShDynamaxAdventureSaveSeedResult.NotWritten(savePath, seed, diagnostics);
                }

                var backupPath = CreateBackupPath(savePath);
                File.Replace(tempPath, savePath, backupPath, ignoreMetadataErrors: true);

                var finalSave = ReadSwordShieldSave(savePath);
                var finalSeed = (ulong)finalSave.Accessor.GetBlockSafe(MaxLairRentalChoiceSeedKey).GetValue();
                var verified = finalSave.ChecksumsValid && finalSeed == seed;
                if (!verified)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Save write completed but final verification failed. Restore the backup before testing.",
                        file: savePath,
                        expected: "Verified checksum and requested Max Lair seed"));
                }

                return new SwShDynamaxAdventureSaveSeedResult(
                    savePath,
                    backupPath,
                    FormatSeed(oldSeed),
                    FormatSeed(seed),
                    WasChanged: true,
                    ChecksumsValid: verified,
                    diagnostics);
            }
            finally
            {
                DeleteTempFile(tempPath);
            }
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
    }

    public SwShDynamaxAdventureSaveSeedInspectResult Inspect(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var diagnostics = new List<ValidationDiagnostic>();
        if (!TryResolveSavePath(paths, diagnostics, out var savePath))
        {
            return new SwShDynamaxAdventureSaveSeedInspectResult(null, null, false, diagnostics);
        }

        try
        {
            var save = ReadSwordShieldSave(savePath);
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

    private static SAV8SWSH ReadSwordShieldSave(string path)
    {
        var save = SaveUtil.GetSaveFile(path);
        return save as SAV8SWSH
            ?? throw new InvalidDataException($"Not a Sword/Shield save: {save?.GetType().FullName ?? "<unknown>"}.");
    }

    private static string CreateBackupPath(string savePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(savePath)) ?? string.Empty;
        var name = Path.GetFileName(savePath);
        for (var index = 0; index < 1000; index++)
        {
            var suffix = index == 0 ? string.Empty : $".{index}";
            var candidate = Path.Combine(directory, $"{name}.km-editor.bak{suffix}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Could not allocate a save backup file name.");
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
