// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.Workspace;

public static class WorkspacePersonalStateContract
{
    public const int SchemaVersion = 1;
    public const int ApplicationMaximumSerializedDocumentBytes = 3 * 1024 * 1024;
    public const int ProjectMaximumSerializedDocumentBytes = 2 * 1024 * 1024;
    public const int MaximumRecentProjectCount = 24;
    public const int MaximumShortcutOverrideCount = 128;
    public const int MaximumLocalePackCount = 4;
    public const int MaximumLocalePackBytes = 512 * 1024;
    public const int MaximumLocalePackAggregateBytes = 2 * 1024 * 1024;
    public const int MaximumGameDumpDestinationCount = 5;
    public const int MaximumRecentTargetCount = 64;
    public const int MaximumBookmarkCount = 256;
    public const int MaximumNoteCount = 256;
    public const int MaximumNoteBytes = 32 * 1024;
    public const int MaximumAggregateNoteBytes = 1024 * 1024;
    public const int MaximumSavedViewCount = 128;
    public const int MaximumSavedViewPayloadBytes = 64 * 1024;
    public const int MaximumOutputProfileCount = 32;
}

public sealed record WorkspaceApplicationStateDocumentDto(
    int SchemaVersion,
    IReadOnlyList<WorkspaceRecentProjectProfileDto> RecentProjects,
    IReadOnlyList<WorkspaceShortcutOverrideDto> ShortcutOverrides,
    IReadOnlyList<WorkspaceLocalePackDto> LocalePacks,
    IReadOnlyList<WorkspaceGameDumpDestinationDto> GameDumpDestinations,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceRecentProjectProfileDto(
    string ProjectId,
    string? Name,
    ProjectGameDto Game,
    ProjectPathsDto Paths,
    DateTimeOffset LastOpenedAtUtc);

public sealed record WorkspaceShortcutOverrideDto(
    string CommandId,
    string Shortcut,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceGameDumpDestinationDto(
    ProjectGameDto Game,
    string DestinationPath,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceLocalePackDto(
    int SchemaVersion,
    string Id,
    string DisplayName,
    string LocaleTag,
    string Direction,
    string GameTextLanguage,
    IReadOnlyDictionary<string, string> Keys,
    IReadOnlyDictionary<string, string> Literals);

public sealed record WorkspaceProjectPersonalStateDocumentDto(
    int SchemaVersion,
    ProjectGameDto Game,
    IReadOnlyList<WorkspaceBookmarkDto> Bookmarks,
    IReadOnlyList<WorkspaceProjectNoteDto> Notes,
    IReadOnlyList<WorkspaceSavedViewDto> SavedViews,
    IReadOnlyList<WorkspaceRecentTargetDto> RecentTargets,
    IReadOnlyList<WorkspaceOutputProfileDto> OutputProfiles,
    string? ActiveOutputProfileId,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceScopedLocationDto(
    int Version,
    ProjectGameDto Game,
    string Section,
    WorkspaceSemanticRecordRefDto? Entity = null,
    string? ChangeSetId = null,
    string? InspectorTab = null,
    IReadOnlyDictionary<string, JsonElement>? Subcontext = null);

public sealed record WorkspaceSemanticRecordRefDto(
    string GameFamily,
    string Domain,
    WorkspaceSemanticRecordKindDto RecordKind,
    string RecordId,
    string? SubrecordId);

public sealed record WorkspaceSemanticRecordKindDto(string Key, int SchemaVersion);

public sealed record WorkspaceBookmarkDto(
    string BookmarkId,
    string Kind,
    string? Label,
    WorkspaceScopedLocationDto Location,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceProjectNoteDto(
    string NoteId,
    WorkspaceScopedLocationDto Location,
    string Body,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceSavedViewDto(
    string ViewId,
    string Name,
    WorkspaceScopedLocationDto Location,
    string AdapterId,
    int AdapterSchemaVersion,
    JsonElement Payload,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceRecentTargetDto(
    WorkspaceScopedLocationDto Location,
    DateTimeOffset VisitedAtUtc);

public sealed record WorkspaceOutputProfileDto(
    string ProfileId,
    string Name,
    string OutputRootPath,
    ChangePlanOutputModeDto? OutputMode,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReadWorkspaceApplicationStateRequest();

public sealed record ReadWorkspaceApplicationStateResponse(
    bool Exists,
    WorkspaceApplicationStateDocumentDto? Document,
    [property: JsonPropertyName("etag")]
    string? ETag);

public sealed record WriteWorkspaceApplicationStateRequest(
    WorkspaceApplicationStateDocumentDto Document,
    string? ExpectedETag);

public sealed record WriteWorkspaceApplicationStateResponse(
    DateTimeOffset WrittenAtUtc,
    [property: JsonPropertyName("etag")]
    string ETag);

public sealed record ReadWorkspaceProjectStateRequest(string ProjectId);

public sealed record ReadWorkspaceProjectStateResponse(
    bool Exists,
    WorkspaceProjectPersonalStateDocumentDto? Document,
    [property: JsonPropertyName("etag")]
    string? ETag);

public sealed record WriteWorkspaceProjectStateRequest(
    string ProjectId,
    WorkspaceProjectPersonalStateDocumentDto Document,
    string? ExpectedETag);

public sealed record WriteWorkspaceProjectStateResponse(
    DateTimeOffset WrittenAtUtc,
    [property: JsonPropertyName("etag")]
    string ETag);

public sealed record DeleteWorkspaceProjectStateRequest(
    string ProjectId,
    string? ExpectedETag);

public sealed record DeleteWorkspaceProjectStateResponse(bool Deleted);
