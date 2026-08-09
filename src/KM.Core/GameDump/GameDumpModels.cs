// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;

namespace KM.Core.GameDump;

public enum GameDumpCategoryKind
{
    Table,
    Text,
    Raw,
}

public enum GameDumpFormat
{
    Tsv,
    Csv,
    Json,
    TsvAndJson,
    Txt,
    TxtAndJson,
    Raw,
    RawAndJson,
}

public sealed record GameDumpCategory(
    string Id,
    string Label,
    string Description,
    GameDumpCategoryKind Kind,
    IReadOnlyList<GameDumpFormat> Formats,
    GameDumpFormat DefaultFormat,
    bool IsAvailable,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public GameDumpCategoryLanguageOptions? LanguageOptions { get; init; }
}

public sealed record GameDumpLanguageOption(
    string Code,
    string Label);

public sealed record GameDumpCategoryLanguageOptions(
    IReadOnlyList<GameDumpLanguageOption> Options,
    IReadOnlyList<string> DefaultLanguageCodes,
    bool SupportsAllLanguages);

public sealed record GameDumpWorkflow(
    IReadOnlyList<GameDumpCategory> Categories,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record GameDumpSelection(
    string CategoryId,
    GameDumpFormat Format,
    IReadOnlyList<string>? LanguageCodes = null);

public sealed record GameDumpWrittenFile(
    string CategoryId,
    string RelativePath,
    long SizeBytes);

public sealed record GameDumpResult(
    string DestinationFolder,
    IReadOnlyList<GameDumpWrittenFile> WrittenFiles,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    bool Succeeded);

public sealed record GameDumpLanguageExportMetadata(
    string RequestedLanguage,
    string ResolvedLanguage,
    bool UsedFallback,
    string? FallbackReason,
    int SourceFileCount,
    int RowCount);

public sealed record GameDumpCategoryExportMetadata(
    IReadOnlyList<GameDumpLanguageExportMetadata> Languages);

public sealed record GameDumpManifest(
    int SchemaVersion,
    string Producer,
    string? ProducerVersion,
    DateTimeOffset GeneratedAtUtc,
    string GameFamily,
    string? SelectedGame,
    bool Succeeded,
    IReadOnlyList<GameDumpManifestCategory> Categories,
    IReadOnlyList<GameDumpManifestFile> Files,
    IReadOnlyList<GameDumpManifestDiagnostic> Diagnostics);

public sealed record GameDumpManifestCategory(
    string Id,
    string Format,
    int SchemaVersion,
    int RowCount,
    GameDumpCategoryExportMetadata? Metadata);

public sealed record GameDumpManifestFile(
    string CategoryId,
    string RelativePath,
    long SizeBytes);

public sealed record GameDumpManifestDiagnostic(
    string? Code,
    string Severity,
    string Message,
    string? File,
    string? Domain,
    string? Field,
    string? Expected);
