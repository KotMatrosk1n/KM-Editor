// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Api.ModMerger;

public sealed record MergeSourceDto(string Id, string Path, string? Game = null, string? Layout = null,
    IReadOnlyList<string>? PackageIds = null, string? PackageScanToken = null);
public sealed record MergePackageScanRequest(MergeSourceDto Source);
public sealed record MergePackageDocumentDto(string Path, string Text, bool Truncated);
public sealed record MergePackageGroupDto(string Id, string Name, bool Required);
public sealed record MergePackageDto(string Id, string Name, string Root, string? Game, string Layout,
    int FileCount, long Size, bool Required, bool Recommended, string? Group, IReadOnlyList<string> Requires,
    string? Description, IReadOnlyList<string> Evidence, IReadOnlyList<string> Files, int OverlapCount);
public sealed record MergePackageCatalogDto(string SourceId, string Name, string ScanToken, bool RequiresSelection,
    IReadOnlyList<MergePackageDto> Packages, IReadOnlyList<MergePackageGroupDto> Groups,
    IReadOnlyList<MergePackageDocumentDto> Documents);
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
    IReadOnlyList<string> Evidence, string? ParentId = null);
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
