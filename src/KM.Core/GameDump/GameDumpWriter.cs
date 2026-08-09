// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KM.Core.GameDump;

public static class GameDumpWriter
{
    public const int ManifestSchemaVersion = 2;
    public const int CategorySchemaVersion = 1;

    private static readonly JsonSerializerOptions IndentedJsonOptions = CreateJsonOptions(writeIndented: true);
    private static readonly JsonSerializerOptions CompactJsonOptions = CreateJsonOptions(writeIndented: false);

    public static GameDumpRunTransaction BeginTransaction(string destinationFolder)
    {
        return new GameDumpRunTransaction(destinationFolder);
    }

    public static GameDumpCategoryDefinition<T> CreateTableCategory<T>(
        string id,
        string label,
        string description,
        Func<ProjectPaths, GameDumpCategoryData<T>> loadRows)
    {
        return new GameDumpCategoryDefinition<T>(
            id,
            label,
            description,
            GameDumpCategoryKind.Table,
            [GameDumpFormat.Tsv, GameDumpFormat.Csv, GameDumpFormat.Json, GameDumpFormat.TsvAndJson],
            GameDumpFormat.TsvAndJson,
            (paths, _) => loadRows(paths),
            languageOptions: null);
    }

    public static GameDumpCategoryDefinition<T> CreateTextCategory<T>(
        string id,
        string label,
        string description,
        Func<ProjectPaths, GameDumpCategoryData<T>> loadRows)
    {
        return new GameDumpCategoryDefinition<T>(
            id,
            label,
            description,
            GameDumpCategoryKind.Text,
            [GameDumpFormat.Txt, GameDumpFormat.Json, GameDumpFormat.TxtAndJson],
            GameDumpFormat.TxtAndJson,
            (paths, _) => loadRows(paths),
            languageOptions: null);
    }

    public static GameDumpCategoryDefinition<T> CreateTextCategory<T>(
        string id,
        string label,
        string description,
        GameDumpCategoryLanguageOptions languageOptions,
        Func<ProjectPaths, GameDumpSelection, GameDumpCategoryData<T>> loadRows)
    {
        ArgumentNullException.ThrowIfNull(languageOptions);
        ArgumentNullException.ThrowIfNull(loadRows);

        return new GameDumpCategoryDefinition<T>(
            id,
            label,
            description,
            GameDumpCategoryKind.Text,
            [GameDumpFormat.Txt, GameDumpFormat.Json, GameDumpFormat.TxtAndJson],
            GameDumpFormat.TxtAndJson,
            loadRows,
            languageOptions);
    }

    public static IReadOnlyList<GameDumpWrittenFile> WriteRows<T>(
        string destinationFolder,
        string categoryId,
        string categoryLabel,
        IReadOnlyList<T> rows,
        GameDumpFormat format,
        bool includeLanguageInTextRows = false)
    {
        Directory.CreateDirectory(destinationFolder);
        var stagingFolder = Path.Combine(
            destinationFolder,
            $".km-editor-game-dump-{Guid.NewGuid():N}.tmp");
        var categoryFolderName = SanitizePathComponent(categoryLabel);
        var categoryFolder = Path.Combine(stagingFolder, categoryFolderName);
        Directory.CreateDirectory(categoryFolder);

        try
        {
            var baseFileName = SanitizePathComponent(categoryId);
            var files = new List<GameDumpWrittenFile>();

            if (format is GameDumpFormat.Json or GameDumpFormat.TsvAndJson or GameDumpFormat.TxtAndJson or GameDumpFormat.RawAndJson)
            {
                files.Add(WriteJsonFile(categoryId, categoryFolder, Path.Combine(categoryFolderName, $"{baseFileName}.json"), rows));
            }

            if (format is GameDumpFormat.Tsv or GameDumpFormat.TsvAndJson)
            {
                files.Add(WriteDelimitedFile(categoryId, categoryFolder, Path.Combine(categoryFolderName, $"{baseFileName}.tsv"), rows, DelimitedFormat.Tsv));
            }

            if (format is GameDumpFormat.Csv)
            {
                files.Add(WriteDelimitedFile(categoryId, categoryFolder, Path.Combine(categoryFolderName, $"{baseFileName}.csv"), rows, DelimitedFormat.Csv));
            }

            if (format is GameDumpFormat.Txt or GameDumpFormat.TxtAndJson)
            {
                files.Add(WriteTextFile(
                    categoryId,
                    categoryFolder,
                    Path.Combine(categoryFolderName, $"{baseFileName}.txt"),
                    rows,
                    includeLanguageInTextRows));
            }

            PromoteFilesWithRollback(stagingFolder, destinationFolder, files);
            return files
                .Select(file => file with
                {
                    SizeBytes = new FileInfo(ResolveChildPath(destinationFolder, file.RelativePath)).Length,
                })
                .ToArray();
        }
        finally
        {
            if (Directory.Exists(stagingFolder))
            {
                Directory.Delete(stagingFolder, recursive: true);
            }
        }
    }

    public static GameDumpWrittenFile WriteManifest(
        string destinationFolder,
        object manifest)
    {
        Directory.CreateDirectory(destinationFolder);
        var fullPath = Path.Combine(destinationFolder, "manifest.json");
        var temporaryPath = Path.Combine(
            destinationFolder,
            $".manifest-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(manifest, IndentedJsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new GameDumpWrittenFile("manifest", "manifest.json", new FileInfo(fullPath).Length);
    }

    public static GameDumpManifest CreateManifest(
        string gameFamily,
        ProjectGame? selectedGame,
        bool succeeded,
        IReadOnlyList<GameDumpSelection> selections,
        IReadOnlyDictionary<string, GameDumpWriteCategoryResult> categoryResults,
        IReadOnlyList<GameDumpWrittenFile> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        string? existingDumpFolder = null,
        string? producerVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFamily);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(categoryResults);
        ArgumentNullException.ThrowIfNull(writtenFiles);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var selectedCategoryIds = selections
            .Select(selection => selection.CategoryId)
            .ToHashSet(StringComparer.Ordinal);
        var currentCategories = selections
            .DistinctBy(selection => selection.CategoryId)
            .Select(selection =>
            {
                categoryResults.TryGetValue(selection.CategoryId, out var result);
                return new GameDumpManifestCategory(
                    selection.CategoryId,
                    selection.Format.ToString(),
                    CategorySchemaVersion,
                    result?.RowCount ?? 0,
                    result?.Metadata);
            })
            .ToArray();
        var currentFiles = writtenFiles.Select(file => new GameDumpManifestFile(
            file.CategoryId,
            file.RelativePath,
            file.SizeBytes)).ToArray();
        var previousManifest = string.IsNullOrWhiteSpace(existingDumpFolder)
            ? null
            : TryReadTrustedManifest(existingDumpFolder);
        var canPreservePrevious = previousManifest is not null
            && previousManifest.SchemaVersion == ManifestSchemaVersion
            && string.Equals(previousManifest.GameFamily, gameFamily, StringComparison.Ordinal)
            && string.Equals(previousManifest.SelectedGame, selectedGame?.ToString(), StringComparison.Ordinal);
        var preservedFiles = new List<GameDumpManifestFile>();
        var preservedCategories = new List<GameDumpManifestCategory>();
        if (canPreservePrevious)
        {
            foreach (var category in previousManifest!.Categories.Where(
                         category => !selectedCategoryIds.Contains(category.Id)))
            {
                var categoryFiles = previousManifest.Files
                    .Where(file => string.Equals(file.CategoryId, category.Id, StringComparison.Ordinal))
                    .ToArray();
                var refreshedFiles = categoryFiles
                    .Select(file => TryRefreshManifestFile(existingDumpFolder!, file))
                    .ToArray();
                if (categoryFiles.Length == 0 || refreshedFiles.Any(file => file is null))
                {
                    continue;
                }

                preservedCategories.Add(category);
                preservedFiles.AddRange(refreshedFiles.Select(file => file!));
            }
        }

        return new GameDumpManifest(
            ManifestSchemaVersion,
            "KM Editor",
            NormalizeProducerVersion(producerVersion) ?? ResolveProducerVersion(),
            DateTimeOffset.UtcNow,
            gameFamily,
            selectedGame?.ToString(),
            succeeded,
            currentCategories.Concat(preservedCategories).ToArray(),
            currentFiles.Concat(preservedFiles).ToArray(),
            diagnostics.Select(diagnostic => new GameDumpManifestDiagnostic(
                diagnostic.Code,
                diagnostic.Severity.ToString(),
                diagnostic.Message,
                diagnostic.File,
                diagnostic.Domain,
                diagnostic.Field,
                diagnostic.Expected)).ToArray());
    }

    public static IReadOnlyList<ValidationDiagnostic> ValidateDestination(ProjectPaths paths, string destinationFolder)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Choose a destination folder before generating dump files.",
                field: "destinationFolder"));
            return diagnostics;
        }

        string fullDestination;
        try
        {
            fullDestination = Path.GetFullPath(destinationFolder);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"The destination folder is not a valid path: {exception.Message}",
                field: "destinationFolder"));
            return diagnostics;
        }

        AddOverlapDiagnosticIfNeeded(diagnostics, fullDestination, paths.BaseRomFsPath, "Base RomFS");
        AddOverlapDiagnosticIfNeeded(diagnostics, fullDestination, paths.BaseExeFsPath, "Base ExeFS");
        AddOverlapDiagnosticIfNeeded(diagnostics, fullDestination, paths.OutputRootPath, "Output Root");

        return diagnostics;
    }

    internal static string SanitizePathComponent(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        var sanitized = builder.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "dump" : sanitized;
    }

    private static void AddOverlapDiagnosticIfNeeded(
        List<ValidationDiagnostic> diagnostics,
        string fullDestination,
        string? projectPath,
        string label)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        string fullProjectPath;
        try
        {
            fullProjectPath = Path.GetFullPath(projectPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (PathsOverlap(fullDestination, fullProjectPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Choose a dump destination outside the project {label}.",
                field: "destinationFolder",
                expected: $"A folder that does not overlap {label}"));
        }
    }

    private static bool PathsOverlap(string left, string right)
    {
        var normalizedLeft = NormalizeDirectoryPath(left);
        var normalizedRight = NormalizeDirectoryPath(right);
        return normalizedLeft.StartsWith(normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedRight.StartsWith(normalizedLeft, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath + Path.DirectorySeparatorChar;
    }

    private static GameDumpWrittenFile WriteJsonFile<T>(
        string categoryId,
        string categoryFolder,
        string relativePath,
        IReadOnlyList<T> rows)
    {
        var fullPath = Path.Combine(categoryFolder, Path.GetFileName(relativePath));
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(rows, IndentedJsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new GameDumpWrittenFile(categoryId, relativePath, new FileInfo(fullPath).Length);
    }

    private static GameDumpWrittenFile WriteDelimitedFile<T>(
        string categoryId,
        string categoryFolder,
        string relativePath,
        IReadOnlyList<T> rows,
        DelimitedFormat format)
    {
        var fullPath = Path.Combine(categoryFolder, Path.GetFileName(relativePath));
        var properties = GetReadableProperties(typeof(T));
        var delimiter = format == DelimitedFormat.Tsv ? "\t" : ",";
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(delimiter, properties.Select(property => EncodeDelimitedCell(property.Name, format))));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(
                delimiter,
                properties.Select(property => EncodeDelimitedCell(FormatCellValue(property.GetValue(row)), format))));
        }

        File.WriteAllText(fullPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new GameDumpWrittenFile(categoryId, relativePath, new FileInfo(fullPath).Length);
    }

    private static GameDumpWrittenFile WriteTextFile<T>(
        string categoryId,
        string categoryFolder,
        string relativePath,
        IReadOnlyList<T> rows,
        bool includeLanguage)
    {
        var fullPath = Path.Combine(categoryFolder, Path.GetFileName(relativePath));
        File.WriteAllLines(
            fullPath,
            rows.Select(row => FormatTextRow(row, includeLanguage)),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new GameDumpWrittenFile(categoryId, relativePath, new FileInfo(fullPath).Length);
    }

    private static PropertyInfo[] GetReadableProperties(Type type)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
            .ToArray();
    }

    private static string FormatTextRow<T>(T row, bool includeLanguage)
    {
        if (row is null)
        {
            return string.Empty;
        }

        var type = typeof(T);
        var languageProperty = type.GetProperty("Language");
        var labelProperty = type.GetProperty("Label") ?? type.GetProperty("Name") ?? type.GetProperty("TextKey");
        var valueProperty = type.GetProperty("Value") ?? type.GetProperty("Description");
        if (labelProperty is not null && valueProperty is not null)
        {
            var labelAndValue = $"{FormatCellValue(labelProperty.GetValue(row))}\t{FormatCellValue(valueProperty.GetValue(row))}";
            return !includeLanguage || languageProperty is null
                ? labelAndValue
                : $"{FormatCellValue(languageProperty.GetValue(row))}\t{labelAndValue}";
        }

        return JsonSerializer.Serialize(row, CompactJsonOptions);
    }

    private static string FormatCellValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string stringValue)
        {
            return stringValue;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            return value.ToString() ?? string.Empty;
        }

        if (value is bool boolValue)
        {
            return boolValue ? "true" : "false";
        }

        if (value is IFormattable formattable && IsScalarType(type))
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        if (value is IEnumerable && value is not string)
        {
            return JsonSerializer.Serialize(value, CompactJsonOptions);
        }

        return IsScalarType(type)
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            : JsonSerializer.Serialize(value, CompactJsonOptions);
    }

    private static bool IsScalarType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type == typeof(decimal)
            || type == typeof(string)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid);
    }

    private static string EncodeDelimitedCell(string value, DelimitedFormat format)
    {
        if (format == DelimitedFormat.Tsv)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);
        }

        if (value.Contains('"', StringComparison.Ordinal)
            || value.Contains(',', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            Domain: "gameDump",
            Field: field,
            Expected: expected);
    }

    private static void PromoteFilesWithRollback(
        string stagingFolder,
        string destinationFolder,
        IReadOnlyList<GameDumpWrittenFile> files)
    {
        var backupFolder = Path.Combine(stagingFolder, ".backup");
        var promoted = new List<PromotedFile>(files.Count);
        try
        {
            foreach (var (file, index) in files.Select((file, index) => (file, index)))
            {
                var stagedPath = ResolveChildPath(stagingFolder, file.RelativePath);
                var targetPath = ResolveChildPath(destinationFolder, file.RelativePath);
                var targetDirectory = Path.GetDirectoryName(targetPath)
                    ?? throw new InvalidOperationException("A dump output path does not have a parent directory.");
                Directory.CreateDirectory(targetDirectory);

                string? backupPath = null;
                if (File.Exists(targetPath))
                {
                    Directory.CreateDirectory(backupFolder);
                    backupPath = Path.Combine(backupFolder, index.ToString(CultureInfo.InvariantCulture));
                    File.Copy(targetPath, backupPath, overwrite: false);
                }

                File.Move(stagedPath, targetPath, overwrite: true);
                promoted.Add(new PromotedFile(targetPath, backupPath));
            }
        }
        catch
        {
            foreach (var file in promoted.AsEnumerable().Reverse())
            {
                if (file.BackupPath is not null && File.Exists(file.BackupPath))
                {
                    File.Move(file.BackupPath, file.TargetPath, overwrite: true);
                }
                else if (File.Exists(file.TargetPath))
                {
                    File.Delete(file.TargetPath);
                }
            }

            throw;
        }
    }

    internal static void PromoteSnapshot(
        string stagingFolder,
        string destinationFolder,
        IReadOnlyList<GameDumpWrittenFile> files,
        IReadOnlySet<string> selectedCategoryIds)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(selectedCategoryIds);

        var manifest = files.SingleOrDefault(file => string.Equals(
            file.RelativePath,
            "manifest.json",
            StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("A complete game dump snapshot requires manifest.json.");
        var contentFiles = files
            .Where(file => !ReferenceEquals(file, manifest))
            .ToArray();
        var newRelativePaths = files
            .Select(file => NormalizeRelativePath(file.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (newRelativePaths.Count != files.Count)
        {
            throw new InvalidOperationException("A game dump snapshot contains duplicate output paths.");
        }

        var previousManifest = TryReadTrustedManifest(destinationFolder);
        var previousOwnedFiles = previousManifest?.Files
            .Select(file => new OwnedManifestFile(
                file.CategoryId,
                NormalizeRelativePath(file.RelativePath)))
            .ToArray() ?? [];
        var stagedManifest = TryReadTrustedManifest(stagingFolder)
            ?? throw new InvalidOperationException("The staged game dump manifest could not be read.");
        var canPreservePrevious = previousManifest is not null
            && previousManifest.SchemaVersion == ManifestSchemaVersion
            && string.Equals(previousManifest.GameFamily, stagedManifest.GameFamily, StringComparison.Ordinal)
            && string.Equals(previousManifest.SelectedGame, stagedManifest.SelectedGame, StringComparison.Ordinal);
        var categoriesToReplace = canPreservePrevious
            ? selectedCategoryIds
            : previousOwnedFiles
                .Select(file => file.CategoryId)
                .ToHashSet(StringComparer.Ordinal);
        var staleRelativePaths = previousOwnedFiles
            .Where(file => categoriesToReplace.Contains(file.CategoryId))
            .Select(file => file.RelativePath)
            .Where(relativePath => !newRelativePaths.Contains(relativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var targetRelativePaths = contentFiles
            .Select(file => NormalizeRelativePath(file.RelativePath))
            .Concat(staleRelativePaths)
            .Append(NormalizeRelativePath(manifest.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var backupFolder = Path.Combine(stagingFolder, ".snapshot-backup");
        var targets = new List<SnapshotTarget>(targetRelativePaths.Length);

        try
        {
            foreach (var (relativePath, index) in targetRelativePaths.Select((path, index) => (path, index)))
            {
                var targetPath = ResolveChildPath(destinationFolder, relativePath);
                string? backupPath = null;
                if (File.Exists(targetPath))
                {
                    Directory.CreateDirectory(backupFolder);
                    backupPath = Path.Combine(backupFolder, index.ToString(CultureInfo.InvariantCulture));
                    File.Copy(targetPath, backupPath, overwrite: false);
                }

                targets.Add(new SnapshotTarget(relativePath, targetPath, backupPath));
            }

            foreach (var file in contentFiles)
            {
                PromoteSnapshotFile(stagingFolder, destinationFolder, file.RelativePath);
            }

            foreach (var relativePath in staleRelativePaths)
            {
                var stalePath = ResolveChildPath(destinationFolder, relativePath);
                if (File.Exists(stalePath))
                {
                    File.Delete(stalePath);
                }
            }

            PromoteSnapshotFile(stagingFolder, destinationFolder, manifest.RelativePath);
        }
        catch (Exception publishException)
        {
            var rollbackExceptions = new List<Exception>();
            foreach (var target in targets.AsEnumerable().Reverse())
            {
                try
                {
                    if (target.BackupPath is not null && File.Exists(target.BackupPath))
                    {
                        var targetDirectory = Path.GetDirectoryName(target.TargetPath)
                            ?? throw new InvalidOperationException("A dump output path does not have a parent directory.");
                        Directory.CreateDirectory(targetDirectory);
                        File.Copy(target.BackupPath, target.TargetPath, overwrite: true);
                    }
                    else if (File.Exists(target.TargetPath))
                    {
                        File.Delete(target.TargetPath);
                    }
                }
                catch (Exception rollbackException)
                {
                    rollbackExceptions.Add(rollbackException);
                }
            }

            if (rollbackExceptions.Count > 0)
            {
                var failures = new List<Exception>(rollbackExceptions.Count + 1)
                {
                    publishException,
                };
                failures.AddRange(rollbackExceptions);
                throw new GameDumpRollbackException(
                    "Game Dump publication failed and one or more destination files could not be restored. Recovery copies remain in the transaction staging folder.",
                    new AggregateException(failures));
            }

            throw;
        }
    }

    private static void PromoteSnapshotFile(
        string stagingFolder,
        string destinationFolder,
        string relativePath)
    {
        var stagedPath = ResolveChildPath(stagingFolder, relativePath);
        var targetPath = ResolveChildPath(destinationFolder, relativePath);
        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("A dump output path does not have a parent directory.");
        Directory.CreateDirectory(targetDirectory);
        File.Move(stagedPath, targetPath, overwrite: true);
    }

    private static GameDumpManifest? TryReadTrustedManifest(string dumpFolder)
    {
        var manifestPath = Path.Combine(dumpFolder, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<GameDumpManifest>(
                File.ReadAllBytes(manifestPath),
                IndentedJsonOptions);
            if (manifest is null
                || manifest.SchemaVersion != ManifestSchemaVersion
                || !string.Equals(manifest.Producer, "KM Editor", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(manifest.GameFamily)
                || manifest.Categories is null
                || manifest.Files is null
                || manifest.Diagnostics is null
                || !manifest.Succeeded
                || manifest.Categories.Any(category => category is null
                    || string.IsNullOrWhiteSpace(category.Id)
                    || string.IsNullOrWhiteSpace(category.Format)
                    || category.SchemaVersion != CategorySchemaVersion
                    || category.RowCount < 0)
                || manifest.Files.Any(file => file is null)
                || manifest.Diagnostics.Any(diagnostic => diagnostic is null)
                || manifest.Categories.Select(category => category.Id).Distinct(StringComparer.Ordinal).Count()
                    != manifest.Categories.Count)
            {
                return null;
            }

            var categoryIds = manifest.Categories
                .Select(category => category.Id)
                .ToHashSet(StringComparer.Ordinal);
            var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                if (string.IsNullOrWhiteSpace(file.CategoryId)
                    || !categoryIds.Contains(file.CategoryId)
                    || string.IsNullOrWhiteSpace(file.RelativePath)
                    || Path.IsPathRooted(file.RelativePath)
                    || string.Equals(file.RelativePath, "manifest.json", StringComparison.OrdinalIgnoreCase)
                    || file.SizeBytes < 0
                    || !IsGeneratedCategoryOutputPath(file.CategoryId, file.RelativePath))
                {
                    return null;
                }

                ResolveChildPath(dumpFolder, file.RelativePath);
                if (!relativePaths.Add(NormalizeRelativePath(file.RelativePath)))
                {
                    return null;
                }
            }

            foreach (var category in manifest.Categories)
            {
                var categoryFiles = manifest.Files
                    .Where(file => string.Equals(file.CategoryId, category.Id, StringComparison.Ordinal))
                    .ToArray();
                if (!ManifestFilesMatchFormat(category.Format, categoryFiles))
                {
                    return null;
                }
            }

            return manifest;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidOperationException
                or ArgumentException
                or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsGeneratedCategoryOutputPath(string categoryId, string relativePath)
    {
        var segments = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2
            || string.IsNullOrWhiteSpace(segments[0])
            || string.Equals(segments[0], ".", StringComparison.Ordinal)
            || string.Equals(segments[0], "..", StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(segments[1]);
        return string.Equals(
                Path.GetFileNameWithoutExtension(segments[1]),
                SanitizePathComponent(categoryId),
                StringComparison.OrdinalIgnoreCase)
            && (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".tsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ManifestFilesMatchFormat(
        string formatValue,
        IReadOnlyList<GameDumpManifestFile> files)
    {
        if (!Enum.TryParse<GameDumpFormat>(formatValue, ignoreCase: false, out var format))
        {
            return false;
        }

        string[] expectedExtensions = format switch
        {
            GameDumpFormat.Tsv => [".tsv"],
            GameDumpFormat.Csv => [".csv"],
            GameDumpFormat.Json => [".json"],
            GameDumpFormat.TsvAndJson => [".json", ".tsv"],
            GameDumpFormat.Txt => [".txt"],
            GameDumpFormat.TxtAndJson => [".json", ".txt"],
            GameDumpFormat.Raw => [],
            GameDumpFormat.RawAndJson => [".json"],
            _ => [],
        };
        var actualExtensions = files
            .Select(file => Path.GetExtension(file.RelativePath).ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .ToArray();
        return expectedExtensions
            .Order(StringComparer.Ordinal)
            .SequenceEqual(actualExtensions, StringComparer.Ordinal);
    }

    private static GameDumpManifestFile? TryRefreshManifestFile(
        string dumpFolder,
        GameDumpManifestFile file)
    {
        try
        {
            var fullPath = ResolveChildPath(dumpFolder, file.RelativePath);
            return File.Exists(fullPath)
                ? file with { SizeBytes = new FileInfo(fullPath).Length }
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
    }

    internal static string ResolveChildPath(string rootFolder, string relativePath)
    {
        var fullRoot = Path.GetFullPath(rootFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A dump output path escaped its destination folder.");
        }

        return fullPath;
    }

    private static string? ResolveProducerVersion()
    {
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(version)
            || string.Equals(version, "1.0.0", StringComparison.Ordinal)
            || version.StartsWith("1.0.0+", StringComparison.Ordinal)
                ? null
                : version;
    }

    private static string? NormalizeProducerVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var normalized = version.Trim();
        return normalized.Length <= 64 && normalized.All(character => !char.IsControl(character))
            ? normalized
            : null;
    }

    private static JsonSerializerOptions CreateJsonOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private enum DelimitedFormat
    {
        Tsv,
        Csv,
    }

    private sealed record PromotedFile(string TargetPath, string? BackupPath);
    private sealed record SnapshotTarget(string RelativePath, string TargetPath, string? BackupPath);
    private sealed record OwnedManifestFile(string CategoryId, string RelativePath);
}

internal sealed class GameDumpRollbackException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);

public sealed class GameDumpRunTransaction : IDisposable
{
    private readonly string destinationFolder;
    private bool completed;
    private bool disposed;
    private bool preserveStagingOnDispose;

    internal GameDumpRunTransaction(string destinationFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);
        this.destinationFolder = Path.GetFullPath(destinationFolder);
        Directory.CreateDirectory(this.destinationFolder);
        StagingFolder = Path.Combine(
            this.destinationFolder,
            $".km-editor-game-dump-run-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(StagingFolder);
    }

    public string StagingFolder { get; }

    public void Promote(
        IReadOnlyList<GameDumpWrittenFile> files,
        IReadOnlySet<string> selectedCategoryIds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed)
        {
            throw new InvalidOperationException("This game dump transaction has already been promoted.");
        }

        try
        {
            GameDumpWriter.PromoteSnapshot(
                StagingFolder,
                destinationFolder,
                files,
                selectedCategoryIds);
        }
        catch (GameDumpRollbackException)
        {
            preserveStagingOnDispose = true;
            throw;
        }

        completed = true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (!preserveStagingOnDispose && Directory.Exists(StagingFolder))
        {
            try
            {
                Directory.Delete(StagingFolder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A completed dump must not be reported as failed only because temporary cleanup was blocked.
            }
        }
    }
}

public sealed record GameDumpCategoryData<T>(
    IReadOnlyList<T> Rows,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    GameDumpCategoryExportMetadata? Metadata = null);

public interface IGameDumpCategoryDefinition
{
    string Id { get; }
    string Label { get; }
    string Description { get; }
    GameDumpCategoryKind Kind { get; }
    IReadOnlyList<GameDumpFormat> Formats { get; }
    GameDumpFormat DefaultFormat { get; }
    GameDumpCategoryLanguageOptions? LanguageOptions { get; }

    GameDumpCategory ToCategory(bool isAvailable, IReadOnlyList<ValidationDiagnostic> diagnostics);
    GameDumpWriteCategoryResult Write(ProjectPaths paths, string destinationFolder, GameDumpSelection selection);
}

public sealed record GameDumpWriteCategoryResult(
    IReadOnlyList<GameDumpWrittenFile> WrittenFiles,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    int RowCount,
    GameDumpCategoryExportMetadata? Metadata);

public sealed class GameDumpCategoryDefinition<T> : IGameDumpCategoryDefinition
{
    private readonly Func<ProjectPaths, GameDumpSelection, GameDumpCategoryData<T>> loadRows;

    internal GameDumpCategoryDefinition(
        string id,
        string label,
        string description,
        GameDumpCategoryKind kind,
        IReadOnlyList<GameDumpFormat> formats,
        GameDumpFormat defaultFormat,
        Func<ProjectPaths, GameDumpSelection, GameDumpCategoryData<T>> loadRows,
        GameDumpCategoryLanguageOptions? languageOptions)
    {
        Id = id;
        Label = label;
        Description = description;
        Kind = kind;
        Formats = formats;
        DefaultFormat = defaultFormat;
        this.loadRows = loadRows;
        LanguageOptions = languageOptions;
    }

    public string Id { get; }
    public string Label { get; }
    public string Description { get; }
    public GameDumpCategoryKind Kind { get; }
    public IReadOnlyList<GameDumpFormat> Formats { get; }
    public GameDumpFormat DefaultFormat { get; }
    public GameDumpCategoryLanguageOptions? LanguageOptions { get; }

    public GameDumpCategory ToCategory(bool isAvailable, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new GameDumpCategory(Id, Label, Description, Kind, Formats, DefaultFormat, isAvailable, diagnostics)
        {
            LanguageOptions = LanguageOptions,
        };
    }

    public GameDumpWriteCategoryResult Write(
        ProjectPaths paths,
        string destinationFolder,
        GameDumpSelection selection)
    {
        var format = selection.Format;
        if (!Formats.Contains(format))
        {
            return new GameDumpWriteCategoryResult(
                [],
                [
                    new ValidationDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Dump format '{format}' is not available for {Label}.",
                        Domain: "gameDump",
                        Field: "format",
                        Expected: string.Join(", ", Formats)),
                ],
                RowCount: 0,
                Metadata: null);
        }

        var data = loadRows(paths, selection);
        if (data.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new GameDumpWriteCategoryResult(
                [],
                data.Diagnostics,
                data.Rows.Count,
                data.Metadata);
        }

        var writtenFiles = GameDumpWriter.WriteRows(
            destinationFolder,
            Id,
            Label,
            data.Rows,
            format,
            includeLanguageInTextRows: LanguageOptions is not null);
        return new GameDumpWriteCategoryResult(
            writtenFiles,
            data.Diagnostics,
            data.Rows.Count,
            data.Metadata);
    }
}
