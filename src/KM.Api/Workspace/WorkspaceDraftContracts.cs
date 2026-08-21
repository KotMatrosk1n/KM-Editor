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
    private const int ProvisionMultiplier = 4;
    private const int HardCeilingMultiplier = 2;

    public const int SchemaVersion = 1;
    public const int ExpectedSerializedDocumentBytes = 3 * 1024 * 1024;
    public const int ProvisionedSerializedDocumentBytes = checked(
        ExpectedSerializedDocumentBytes * ProvisionMultiplier);
    public const int MaximumSerializedDocumentBytes = checked(
        ProvisionedSerializedDocumentBytes * HardCeilingMultiplier);
    public const int ExpectedPayloadBytes = 512 * 1024;
    public const int ProvisionedPayloadBytes = checked(
        ExpectedPayloadBytes * ProvisionMultiplier);
    public const int MaximumPayloadBytes = checked(
        ProvisionedPayloadBytes * HardCeilingMultiplier);
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
