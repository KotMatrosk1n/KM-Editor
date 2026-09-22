// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Api.ModMerger;

public sealed record MergeSourceDto(string Id, string Path, string? Game = null, string? Layout = null);
public sealed record MergeChoiceDto(string ConflictId, string SourceId);
public sealed record MergeWorkspaceRequest(
    string Mode,
    string? Game,
    string OutputMode,
    string OutputRoot,
    IReadOnlyList<MergeSourceDto> Sources,
    IReadOnlyList<MergeChoiceDto> Choices,
    string? BaseRomFs = null,
    string? BaseExeFs = null,
    string? SupportFolder = null,
    string? ReviewToken = null);

public sealed record MergeIssueDto(string Code, string Severity, string Message, string? File = null);
public sealed record MergeSourceInfoDto(
    string Id, string Name, string? Game, string Layout, int FileCount,
    IReadOnlyList<string> Evidence);
public sealed record MergeValueDto(string SourceId, string SourceName, string Value);
public sealed record MergeConflictDto(
    string Id, string File, string Label, string Kind, string? Original,
    IReadOnlyList<MergeValueDto> Values, string? Resolution);
public sealed record MergeFileDto(string Path, string Kind, string Status, int ConflictCount, long Size);
public sealed record MergeWorkspaceResult(
    string? Game, string Mode, string OutputMode, string ReviewToken, bool CanExport,
    IReadOnlyList<MergeSourceInfoDto> Sources,
    IReadOnlyList<MergeFileDto> Files,
    IReadOnlyList<MergeConflictDto> Conflicts,
    IReadOnlyList<MergeIssueDto> Issues,
    IReadOnlyList<string> WrittenFiles,
    string? ProjectId = null);
