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
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record GameDumpWorkflow(
    IReadOnlyList<GameDumpCategory> Categories,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record GameDumpSelection(
    string CategoryId,
    GameDumpFormat Format);

public sealed record GameDumpWrittenFile(
    string CategoryId,
    string RelativePath,
    long SizeBytes);

public sealed record GameDumpResult(
    string DestinationFolder,
    IReadOnlyList<GameDumpWrittenFile> WrittenFiles,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    bool Succeeded);
