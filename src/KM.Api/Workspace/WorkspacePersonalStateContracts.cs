// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.Workspace;

public static class WorkspacePersonalStateContract
{
    private const int ProvisionMultiplier = 4;
    private const int HardCeilingMultiplier = 2;

    public const int SchemaVersion = 1;
    public const int ApplicationExpectedSerializedDocumentBytes = 3 * 1024 * 1024;
    public const int ApplicationProvisionedSerializedDocumentBytes = checked(
        ApplicationExpectedSerializedDocumentBytes * ProvisionMultiplier);
    public const int ApplicationMaximumSerializedDocumentBytes = checked(
        ApplicationProvisionedSerializedDocumentBytes * HardCeilingMultiplier);
    public const int ProjectExpectedSerializedDocumentBytes = 2 * 1024 * 1024;
    public const int ProjectProvisionedSerializedDocumentBytes = checked(
        ProjectExpectedSerializedDocumentBytes * ProvisionMultiplier);
    public const int ProjectMaximumSerializedDocumentBytes = checked(
        ProjectProvisionedSerializedDocumentBytes * HardCeilingMultiplier);
    public const int MaximumRecentProjectCount = 24;
    public const int MaximumShortcutOverrideCount = 128;
    public const int MaximumLocalePackCount = 4;
    public const int ExpectedLocalePackBytes = 512 * 1024;
    public const int ProvisionedLocalePackBytes = checked(
        ExpectedLocalePackBytes * ProvisionMultiplier);
    public const int MaximumLocalePackBytes = checked(
        ProvisionedLocalePackBytes * HardCeilingMultiplier);
    public const int ExpectedLocalePackAggregateBytes = 2 * 1024 * 1024;
    public const int ProvisionedLocalePackAggregateBytes = checked(
        ExpectedLocalePackAggregateBytes * ProvisionMultiplier);
    public const int MaximumLocalePackAggregateBytes = checked(
        ProvisionedLocalePackAggregateBytes * HardCeilingMultiplier);
    public const int MaximumGameDumpDestinationCount = 5;
    public const int MaximumRecentTargetCount = 64;
    public const int MaximumBookmarkCount = 256;
    public const int MaximumNoteCount = 256;
    public const int ExpectedNoteBytes = 32 * 1024;
    public const int ProvisionedNoteBytes = checked(ExpectedNoteBytes * ProvisionMultiplier);
    public const int MaximumNoteBytes = checked(
        ProvisionedNoteBytes * HardCeilingMultiplier);
    public const int ExpectedAggregateNoteBytes = 1024 * 1024;
    public const int ProvisionedAggregateNoteBytes = checked(
        ExpectedAggregateNoteBytes * ProvisionMultiplier);
    public const int MaximumAggregateNoteBytes = checked(
        ProvisionedAggregateNoteBytes * HardCeilingMultiplier);
    public const int MaximumSavedViewCount = 128;
    public const int ExpectedSavedViewPayloadBytes = 64 * 1024;
    public const int ProvisionedSavedViewPayloadBytes = checked(
        ExpectedSavedViewPayloadBytes * ProvisionMultiplier);
    public const int MaximumSavedViewPayloadBytes = checked(
        ProvisionedSavedViewPayloadBytes * HardCeilingMultiplier);
    public const int ExpectedSavedViewAggregatePayloadBytes = 512 * 1024;
    public const int ProvisionedSavedViewAggregatePayloadBytes = checked(
        ExpectedSavedViewAggregatePayloadBytes * ProvisionMultiplier);
    public const int MaximumSavedViewAggregatePayloadBytes = checked(
        ProvisionedSavedViewAggregatePayloadBytes * HardCeilingMultiplier);
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
