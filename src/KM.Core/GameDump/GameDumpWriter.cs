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
    private static readonly JsonSerializerOptions IndentedJsonOptions = CreateJsonOptions(writeIndented: true);
    private static readonly JsonSerializerOptions CompactJsonOptions = CreateJsonOptions(writeIndented: false);

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
            loadRows);
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
            loadRows);
    }

    public static IReadOnlyList<GameDumpWrittenFile> WriteRows<T>(
        string destinationFolder,
        string categoryId,
        string categoryLabel,
        IReadOnlyList<T> rows,
        GameDumpFormat format)
    {
        var categoryFolderName = SanitizePathComponent(categoryLabel);
        var categoryFolder = Path.Combine(destinationFolder, categoryFolderName);
        if (Directory.Exists(categoryFolder))
        {
            Directory.Delete(categoryFolder, recursive: true);
        }

        Directory.CreateDirectory(categoryFolder);

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
            files.Add(WriteTextFile(categoryId, categoryFolder, Path.Combine(categoryFolderName, $"{baseFileName}.txt"), rows));
        }

        return files;
    }

    public static GameDumpWrittenFile WriteManifest(
        string destinationFolder,
        object manifest)
    {
        Directory.CreateDirectory(destinationFolder);
        var fullPath = Path.Combine(destinationFolder, "manifest.json");
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(manifest, IndentedJsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new GameDumpWrittenFile("manifest", "manifest.json", new FileInfo(fullPath).Length);
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
        IReadOnlyList<T> rows)
    {
        var fullPath = Path.Combine(categoryFolder, Path.GetFileName(relativePath));
        File.WriteAllLines(
            fullPath,
            rows.Select(FormatTextRow),
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

    private static string FormatTextRow<T>(T row)
    {
        if (row is null)
        {
            return string.Empty;
        }

        var type = typeof(T);
        var labelProperty = type.GetProperty("Label") ?? type.GetProperty("Name") ?? type.GetProperty("TextKey");
        var valueProperty = type.GetProperty("Value") ?? type.GetProperty("Description");
        if (labelProperty is not null && valueProperty is not null)
        {
            return $"{FormatCellValue(labelProperty.GetValue(row))}\t{FormatCellValue(valueProperty.GetValue(row))}";
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
}

public sealed record GameDumpCategoryData<T>(
    IReadOnlyList<T> Rows,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public interface IGameDumpCategoryDefinition
{
    string Id { get; }
    string Label { get; }
    string Description { get; }
    GameDumpCategoryKind Kind { get; }
    IReadOnlyList<GameDumpFormat> Formats { get; }
    GameDumpFormat DefaultFormat { get; }

    GameDumpCategory ToCategory(bool isAvailable, IReadOnlyList<ValidationDiagnostic> diagnostics);
    GameDumpWriteCategoryResult Write(ProjectPaths paths, string destinationFolder, GameDumpFormat format);
}

public sealed record GameDumpWriteCategoryResult(
    IReadOnlyList<GameDumpWrittenFile> WrittenFiles,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class GameDumpCategoryDefinition<T> : IGameDumpCategoryDefinition
{
    private readonly Func<ProjectPaths, GameDumpCategoryData<T>> loadRows;

    internal GameDumpCategoryDefinition(
        string id,
        string label,
        string description,
        GameDumpCategoryKind kind,
        IReadOnlyList<GameDumpFormat> formats,
        GameDumpFormat defaultFormat,
        Func<ProjectPaths, GameDumpCategoryData<T>> loadRows)
    {
        Id = id;
        Label = label;
        Description = description;
        Kind = kind;
        Formats = formats;
        DefaultFormat = defaultFormat;
        this.loadRows = loadRows;
    }

    public string Id { get; }
    public string Label { get; }
    public string Description { get; }
    public GameDumpCategoryKind Kind { get; }
    public IReadOnlyList<GameDumpFormat> Formats { get; }
    public GameDumpFormat DefaultFormat { get; }

    public GameDumpCategory ToCategory(bool isAvailable, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new GameDumpCategory(Id, Label, Description, Kind, Formats, DefaultFormat, isAvailable, diagnostics);
    }

    public GameDumpWriteCategoryResult Write(ProjectPaths paths, string destinationFolder, GameDumpFormat format)
    {
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
                ]);
        }

        var data = loadRows(paths);
        if (data.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new GameDumpWriteCategoryResult([], data.Diagnostics);
        }

        var writtenFiles = GameDumpWriter.WriteRows(destinationFolder, Id, Label, data.Rows, format);
        return new GameDumpWriteCategoryResult(writtenFiles, data.Diagnostics);
    }
}
