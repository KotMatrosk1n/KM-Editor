// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Api.Projects;

namespace KM.Api.Workspace;

/// <summary>
/// Version 1 of the private project draft document. This is authored workspace
/// state, not a game file, edit session, change plan, or shareable recipe.
/// </summary>
public static class WorkspaceDraftContract
{
    public const int SchemaVersion = 1;

    // Leaves one MiB of the generic four-MiB workspace envelope budget for
    // envelope fields and serializer overhead.
    public const int MaximumSerializedDocumentBytes = 3 * 1024 * 1024;
}

public sealed record WorkspaceDraftKeyDto(
    string ChangeSetId,
    ProjectGameDto Game,
    string Domain,
    string Section,
    string EntityId);

public sealed record WorkspaceDraftEntryDto(
    WorkspaceDraftKeyDto Key,
    string AdapterId,
    int AdapterSchemaVersion,
    JsonElement Payload,
    DateTimeOffset UpdatedAtUtc,
    string? ProjectSourceRevisionFingerprint = null);

public sealed record WorkspaceDraftDocumentDto(
    int SchemaVersion,
    IReadOnlyList<WorkspaceDraftEntryDto> Drafts,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReadWorkspaceDraftsRequest(string ProjectId);

public sealed record ReadWorkspaceDraftsResponse(
    bool Exists,
    WorkspaceDraftDocumentDto? Document,
    [property: JsonPropertyName("etag")]
    string? ETag);

public sealed record WriteWorkspaceDraftsRequest(
    string ProjectId,
    WorkspaceDraftDocumentDto Document,
    string? ExpectedETag);

public sealed record WriteWorkspaceDraftsResponse(
    DateTimeOffset WrittenAtUtc,
    [property: JsonPropertyName("etag")]
    string ETag);

public sealed record DeleteWorkspaceDraftsRequest(
    string ProjectId,
    string? ExpectedETag);

public sealed record DeleteWorkspaceDraftsResponse(bool Deleted);
