// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Workspace;
using KM.Core.Projects;
using KM.Core.Workspace;
using System.Text;
using System.Text.Json;

namespace KM.Tools.Application;

/// <summary>
/// Owns the private, project-scoped persistence boundary for editor drafts.
/// Draft payloads remain inert authored state; this service never stages or applies them.
/// </summary>
public sealed class WorkspaceDraftApplicationService
{
    private const int MaximumDraftCount = 256;
    private const int MaximumDraftPayloadBytes = 512 * 1024;
    private const int MaximumEntityIdLength = 4_096;
    private const int MaximumIdentifierLength = 256;
    private const int MaximumStableIdLength = 1_024;
    private static readonly JsonSerializerOptions DocumentSizeSerializerOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly WorkspaceDocumentDefinition<WorkspaceDraftDocumentDto> DocumentDefinition =
        new(
            new WorkspaceDocumentId("drafts"),
            "workspace-drafts",
            WorkspaceDraftContract.SchemaVersion);
    private static readonly WorkspaceDocumentId AuthoringOperationLeaseId =
        new("change-sets-operation");

    private readonly VersionedWorkspaceDocumentStore store;

    public WorkspaceDraftApplicationService(VersionedWorkspaceDocumentStore? store = null)
    {
        this.store = store ?? new VersionedWorkspaceDocumentStore(GetDefaultAppDataRoot());
    }

    public async Task<ReadWorkspaceDraftsResponse> ReadAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var identity = GetProjectIdentity(projectId);
        var result = await store.ReadAsync(identity, DocumentDefinition, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return new ReadWorkspaceDraftsResponse(
                Exists: false,
                Document: null,
                ETag: null);
        }

        ValidateDocument(result.Document);
        return new ReadWorkspaceDraftsResponse(
            Exists: true,
            result.Document,
            result.ETag);
    }

    public async Task<WriteWorkspaceDraftsResponse> WriteAsync(
        string projectId,
        WorkspaceDraftDocumentDto document,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        var identity = GetProjectIdentity(projectId);
        using var authoringLease = await store.AcquireProjectOperationLeaseAsync(
                identity,
                AuthoringOperationLeaseId,
                cancellationToken)
            .ConfigureAwait(false);
        return await WriteCoreAsync(identity, document, expectedETag, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<WriteWorkspaceDraftsResponse> WriteForRelocationAsync(
        string projectId,
        WorkspaceDraftDocumentDto document,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        return await WriteCoreAsync(
                GetProjectIdentity(projectId),
                document,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<WriteWorkspaceDraftsResponse> WriteCoreAsync(
        WorkspaceProjectIdentity identity,
        WorkspaceDraftDocumentDto document,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);
        ValidateExpectedETag(expectedETag);

        var result = await store.WriteConditionalAsync(
                identity,
                DocumentDefinition,
                document,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
        return new WriteWorkspaceDraftsResponse(result.WrittenAtUtc, result.ETag);
    }

    public async Task<DeleteWorkspaceDraftsResponse> DeleteAsync(
        string projectId,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        var identity = GetProjectIdentity(projectId);
        using var authoringLease = await store.AcquireProjectOperationLeaseAsync(
                identity,
                AuthoringOperationLeaseId,
                cancellationToken)
            .ConfigureAwait(false);
        return await DeleteCoreAsync(identity, expectedETag, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<DeleteWorkspaceDraftsResponse> DeleteForRelocationAsync(
        string projectId,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        return await DeleteCoreAsync(
                GetProjectIdentity(projectId),
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DeleteWorkspaceDraftsResponse> DeleteCoreAsync(
        WorkspaceProjectIdentity identity,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        ValidateExpectedETag(expectedETag);
        var result = await store.DeleteConditionalAsync(
                identity,
                DocumentDefinition.DocumentId,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
        return new DeleteWorkspaceDraftsResponse(result.Deleted);
    }

    private static WorkspaceProjectIdentity GetProjectIdentity(string projectId)
    {
        ValidateIdentifier(projectId, nameof(projectId), MaximumIdentifierLength);
        return WorkspaceProjectIdentity.FromProjectId(new ProjectId(projectId));
    }

    private static void ValidateDocument(WorkspaceDraftDocumentDto document)
    {
        if (document.SchemaVersion != WorkspaceDraftContract.SchemaVersion)
        {
            throw new WorkspaceDraftValidationException(
                $"Workspace drafts must use schema version {WorkspaceDraftContract.SchemaVersion}.");
        }

        if (document.Drafts is null)
        {
            throw new WorkspaceDraftValidationException("Workspace drafts are missing their entries.");
        }

        if (document.UpdatedAtUtc == default)
        {
            throw new WorkspaceDraftValidationException(
                "Workspace drafts require a valid update timestamp.");
        }

        if (document.Drafts.Count > MaximumDraftCount)
        {
            throw new WorkspaceDraftValidationException(
                $"A project workspace can retain at most {MaximumDraftCount} drafts.");
        }

        var uniqueKeys = new HashSet<WorkspaceDraftKeyDto>();
        long aggregatePayloadBytes = 0;
        foreach (var draft in document.Drafts)
        {
            if (draft is null || draft.Key is null)
            {
                throw new WorkspaceDraftValidationException("A workspace draft entry is invalid.");
            }

            if (!Enum.IsDefined(draft.Key.Game))
            {
                throw new WorkspaceDraftValidationException("A workspace draft has an invalid game.");
            }

            ValidateIdentifier(draft.Key.ChangeSetId, "change set id", MaximumStableIdLength);
            ValidateIdentifier(draft.Key.Domain, "draft domain", MaximumIdentifierLength);
            ValidateIdentifier(draft.Key.Section, "draft section", MaximumIdentifierLength);
            ValidateIdentifier(draft.Key.EntityId, "draft entity id", MaximumEntityIdLength);
            ValidateIdentifier(draft.AdapterId, "draft adapter id", MaximumIdentifierLength);

            if (draft.UpdatedAtUtc == default)
            {
                throw new WorkspaceDraftValidationException(
                    "A workspace draft requires a valid update timestamp.");
            }

            if (draft.AdapterSchemaVersion <= 0)
            {
                throw new WorkspaceDraftValidationException(
                    "A workspace draft adapter schema version must be positive.");
            }

            if (draft.Payload.ValueKind is System.Text.Json.JsonValueKind.Undefined)
            {
                throw new WorkspaceDraftValidationException("A workspace draft payload is missing.");
            }

            var payloadBytes = Encoding.UTF8.GetByteCount(draft.Payload.GetRawText());
            if (payloadBytes > MaximumDraftPayloadBytes)
            {
                throw new WorkspaceDraftValidationException(
                    $"A workspace draft payload cannot exceed {MaximumDraftPayloadBytes} bytes.");
            }

            aggregatePayloadBytes = checked(aggregatePayloadBytes + payloadBytes);
            if (aggregatePayloadBytes > WorkspaceDraftContract.MaximumSerializedDocumentBytes)
            {
                throw new WorkspaceDraftValidationException(
                    $"A workspace draft document cannot exceed {WorkspaceDraftContract.MaximumSerializedDocumentBytes} bytes.");
            }

            if (draft.ProjectSourceRevisionFingerprint is { } fingerprint
                && !IsSha256Fingerprint(fingerprint))
            {
                throw new WorkspaceDraftValidationException(
                    "A workspace draft source revision fingerprint is invalid.");
            }

            if (!uniqueKeys.Add(draft.Key))
            {
                throw new WorkspaceDraftValidationException(
                    "A workspace draft document contains duplicate draft keys.");
            }
        }


        var serializedDocumentBytes = JsonSerializer.SerializeToUtf8Bytes(
            document,
            DocumentSizeSerializerOptions);
        if (serializedDocumentBytes.Length > WorkspaceDraftContract.MaximumSerializedDocumentBytes)
        {
            throw new WorkspaceDraftValidationException(
                $"A workspace draft document cannot exceed {WorkspaceDraftContract.MaximumSerializedDocumentBytes} bytes.");
        }
    }

    private static void ValidateIdentifier(string value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new WorkspaceDraftValidationException(
                $"The {name} must be a non-empty bounded identifier.");
        }
    }

    private static bool IsSha256Fingerprint(string value)
    {
        return value.Length == 64
            && value.All(character =>
                char.IsAsciiDigit(character)
                || character is >= 'a' and <= 'f'
                || character is >= 'A' and <= 'F');
    }

    private static void ValidateExpectedETag(string? expectedETag)
    {
        if (expectedETag is not null && !IsSha256Fingerprint(expectedETag))
        {
            throw new WorkspaceDraftValidationException(
                "The expected workspace document ETag is invalid.");
        }
    }

    private static string GetDefaultAppDataRoot()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData)
            || !Path.IsPathFullyQualified(localApplicationData))
        {
            throw new InvalidOperationException(
                "A private local application-data location is unavailable.");
        }

        return Path.Combine(localApplicationData, "KM Editor");
    }
}

public sealed class WorkspaceDraftValidationException : Exception
{
    public WorkspaceDraftValidationException(string message)
        : base(message)
    {
    }
}
