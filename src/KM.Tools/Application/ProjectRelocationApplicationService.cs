// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Output;
using KM.Api.Projects;
using KM.Api.Workspace;
using KM.Core.Projects;
using KM.Tools.Bridge;

namespace KM.Tools.Application;

/// <summary>
/// Reviews and applies an explicit project relocation while retaining the source workspace.
/// Only allowlisted private workspace documents are considered for migration.
/// </summary>
public sealed class ProjectRelocationApplicationService
{
    private const string DraftDocumentId = "drafts";
    private const string PersonalStateDocumentId = "personal-state";
    private const string ChangeSetDocumentId = "change-sets";
    private static readonly EnumerationOptions MetadataEntryEnumeration = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        MatchCasing = MatchCasing.CaseInsensitive,
        MatchType = MatchType.Simple,
        MaxRecursionDepth = 0,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };
    private static readonly JsonSerializerOptions FingerprintSerializerOptions =
        new(BridgeJson.SerializerOptions);

    private readonly ProjectRelocationService relocationService;
    private readonly WorkspaceDraftApplicationService workspaceDraftService;
    private readonly WorkspacePersonalStateApplicationService workspacePersonalStateService;
    private readonly ChangeSetApplicationService changeSetService;
    private readonly OutputSafetyApplicationService outputSafetyService;

    public ProjectRelocationApplicationService(
        ProjectRelocationService? relocationService = null,
        WorkspaceDraftApplicationService? workspaceDraftService = null,
        WorkspacePersonalStateApplicationService? workspacePersonalStateService = null,
        ChangeSetApplicationService? changeSetService = null,
        OutputSafetyApplicationService? outputSafetyService = null)
    {
        this.relocationService = relocationService ?? new ProjectRelocationService();
        this.workspaceDraftService = workspaceDraftService ?? new WorkspaceDraftApplicationService();
        this.workspacePersonalStateService = workspacePersonalStateService
            ?? new WorkspacePersonalStateApplicationService();
        this.changeSetService = changeSetService ?? new ChangeSetApplicationService();
        this.outputSafetyService = outputSafetyService ?? new OutputSafetyApplicationService();
    }

    public async Task<PreviewProjectRelocationResponse> PreviewAsync(
        PreviewProjectRelocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        ValidateProjectId(request.Source.ProjectId);
        ArgumentNullException.ThrowIfNull(request.Source.Paths);
        ArgumentNullException.ThrowIfNull(request.CandidatePaths);

        var sourcePaths = ProjectBridgeMapper.ToCore(request.Source.Paths);
        if (!HasBoundedProjectPathStrings(sourcePaths)
            || sourcePaths.SelectedGame is not { } selectedGame
            || ProjectIdentity.FromPaths(sourcePaths).Value != request.Source.ProjectId)
        {
            return RejectedPreview(
                request.Source.ProjectId,
                "The relocation source no longer matches the active project.",
                "KM-PROJECT-RELOCATION-MISMATCH");
        }

        await outputSafetyService
            .EnsureRecoveryReadyAsync(request.Source, cancellationToken)
            .ConfigureAwait(false);

        var candidatePaths = ProjectBridgeMapper.ToCore(request.CandidatePaths);
        if (candidatePaths.SelectedGame is null)
        {
            return RejectedPreview(
                request.Source.ProjectId,
                "Select a game before reviewing project relocation.",
                "KM-PROJECT-RELOCATION-MISMATCH");
        }

        var result = relocationService.Evaluate(
            new ProjectId(request.Source.ProjectId),
            selectedGame,
            candidatePaths,
            cancellationToken);
        if (!result.IsAccepted
            || result.CandidateHealth is null
            || result.CandidateProjectId is not { } candidateProjectId)
        {
            return RejectedPreview(
                request.Source.ProjectId,
                GetRejectedMessage(result.Disposition),
                result.Disposition == ProjectRelocationDisposition.RejectedDiscoveryLimit
                    ? "KM-OUTPUT-LIMIT-EXCEEDED"
                    : "KM-PROJECT-RELOCATION-MISMATCH",
                result.CandidateHealth);
        }

        using var changeSetLeases = await changeSetService.AcquireProjectLeasesAsync(
                [request.Source.ProjectId, candidateProjectId.Value],
                cancellationToken)
            .ConfigureAwait(false);
        var sourceDrafts = await workspaceDraftService
            .ReadAsync(request.Source.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var destinationDrafts = candidateProjectId.Value == request.Source.ProjectId
            ? sourceDrafts
            : await workspaceDraftService
                .ReadAsync(candidateProjectId.Value, cancellationToken)
                .ConfigureAwait(false);
        var documentStatus = GetDraftDocumentStatus(sourceDrafts, destinationDrafts);
        var sourcePersonalState = await workspacePersonalStateService
            .ReadProjectAsync(request.Source.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var destinationPersonalState = candidateProjectId.Value == request.Source.ProjectId
            ? sourcePersonalState
            : await workspacePersonalStateService
                .ReadProjectAsync(candidateProjectId.Value, cancellationToken)
                .ConfigureAwait(false);
        var relocatedPersonalState = sourcePersonalState.Document is null
            ? null
            : workspacePersonalStateService.PrepareForRelocation(
                sourcePersonalState.Document,
                candidatePaths.OutputRootPath);
        var personalStateStatus = GetPersonalStateDocumentStatus(
            sourcePersonalState,
            destinationPersonalState,
            relocatedPersonalState,
            sameProject: candidateProjectId.Value == request.Source.ProjectId);
        var sourceChangeSets = await changeSetService
            .ReadStoredForRelocationAsync(request.Source.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var destinationChangeSets = candidateProjectId.Value == request.Source.ProjectId
            ? sourceChangeSets
            : await changeSetService
                .ReadStoredForRelocationAsync(candidateProjectId.Value, cancellationToken)
                .ConfigureAwait(false);
        var changeSetStatus = GetChangeSetDocumentStatus(
            sourceChangeSets,
            destinationChangeSets,
            sameProject: candidateProjectId.Value == request.Source.ProjectId);
        var outputStoreState = InspectCandidateOutputStore(candidatePaths.OutputRootPath);
        var hasOutputContinuityConflict = result.StableSourceIdentityChanged is true
            && outputStoreState == RelocationOutputStoreState.OccupiedOrUnverifiable;
        var canApply = documentStatus != ProjectRelocationDocumentStatusDto.Conflict
            && personalStateStatus != ProjectRelocationDocumentStatusDto.Conflict
            && changeSetStatus != ProjectRelocationDocumentStatusDto.Conflict
            && !hasOutputContinuityConflict;
        var diagnostics = result.CandidateHealth.Diagnostics
            .Select(ProjectBridgeMapper.ToDto)
            .ToList();
        if (documentStatus == ProjectRelocationDocumentStatusDto.Conflict
            || personalStateStatus == ProjectRelocationDocumentStatusDto.Conflict
            || changeSetStatus == ProjectRelocationDocumentStatusDto.Conflict)
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "The relocated project already has different private workspace state. No workspace data was copied.",
                "KM-PROJECT-RELOCATION-CONFLICT"));
        }
        else if (hasOutputContinuityConflict)
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "Choose a clean output root before relocating a project whose source identity changed.",
                "KM-PROJECT-RELOCATION-CONFLICT"));
        }
        else if (result.StableSourceIdentityChanged is true)
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Info,
                "The source identity changed. Relocation can copy the allowlisted private workspace while retaining the original.",
                "KM-PROJECT-RELOCATION-REVIEWED"));
        }

        var reviewToken = CreateReviewToken(
            request.Source.ProjectId,
            candidateProjectId.Value,
            CreatePathsFingerprint(candidatePaths),
            sourceDrafts.ETag,
            destinationDrafts.ETag,
            documentStatus,
            sourcePersonalState.ETag,
            destinationPersonalState.ETag,
            personalStateStatus,
            FingerprintDocument(relocatedPersonalState),
            sourceChangeSets.ETag,
            destinationChangeSets.ETag,
            changeSetStatus,
            outputStoreState);
        return new PreviewProjectRelocationResponse(
            reviewToken,
            request.Source.ProjectId,
            candidateProjectId.Value,
            canApply,
            result.CandidateHealth.Paths
                .Select(path => new ProjectRelocationRoleDto(ToDto(path.Role), ToDto(path.Status)))
                .ToArray(),
            [
                new ProjectRelocationDocumentDto(DraftDocumentId, documentStatus),
                new ProjectRelocationDocumentDto(PersonalStateDocumentId, personalStateStatus),
                new ProjectRelocationDocumentDto(ChangeSetDocumentId, changeSetStatus),
            ],
            diagnostics);
    }

    public async Task<ApplyProjectRelocationResponse> ApplyAsync(
        ApplyProjectRelocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateReviewToken(request.ReviewToken);

        var preview = await PreviewAsync(
                new PreviewProjectRelocationRequest(
                    request.Source,
                    request.CandidatePaths),
                cancellationToken)
            .ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(preview.ReviewToken),
                Encoding.ASCII.GetBytes(request.ReviewToken)))
        {
            throw new ProjectRelocationReviewMismatchException();
        }

        if (!preview.CanApply || preview.DestinationProjectId is null)
        {
            throw new ProjectRelocationConflictException();
        }

        var candidatePaths = ProjectBridgeMapper.ToCore(request.CandidatePaths);
        var sourcePaths = ProjectBridgeMapper.ToCore(request.Source.Paths);
        var selectedGame = sourcePaths.SelectedGame
            ?? throw new ProjectRelocationReviewMismatchException();
        using var changeSetLeases = await changeSetService.AcquireProjectLeasesAsync(
                [request.Source.ProjectId, preview.DestinationProjectId],
                cancellationToken)
            .ConfigureAwait(false);
        return await outputSafetyService.ExecuteExclusiveOutputOperationAsync(
                request.Source,
                operationCancellationToken => ApplyUnderOutputLockAsync(
                    request,
                    preview,
                    candidatePaths,
                    selectedGame,
                    operationCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ApplyProjectRelocationResponse> ApplyUnderOutputLockAsync(
        ApplyProjectRelocationRequest request,
        PreviewProjectRelocationResponse preview,
        ProjectPaths candidatePaths,
        ProjectGame selectedGame,
        CancellationToken cancellationToken)
    {
        var destinationProjectId = preview.DestinationProjectId
            ?? throw new ProjectRelocationReviewMismatchException();
        var finalResult = relocationService.Evaluate(
            new ProjectId(request.Source.ProjectId),
            selectedGame,
            candidatePaths,
            cancellationToken);
        if (!finalResult.IsAccepted
            || finalResult.CandidateHealth is not { } candidateHealth
            || finalResult.CandidateProjectId?.Value != destinationProjectId)
        {
            throw new ProjectRelocationReviewMismatchException();
        }

        var migratedDocuments = new List<string>();
        var draftStatus = preview.WorkspaceDocuments.Single(document => document.DocumentId == DraftDocumentId).Status;
        var personalStateStatus = preview.WorkspaceDocuments
            .Single(document => document.DocumentId == PersonalStateDocumentId)
            .Status;
        var changeSetStatus = preview.WorkspaceDocuments
            .Single(document => document.DocumentId == ChangeSetDocumentId)
            .Status;
        var sourceDrafts = await workspaceDraftService
            .ReadAsync(request.Source.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var destinationDrafts = destinationProjectId == request.Source.ProjectId
            ? sourceDrafts
            : await workspaceDraftService
                .ReadAsync(destinationProjectId, cancellationToken)
                .ConfigureAwait(false);
        var currentDraftStatus = GetDraftDocumentStatus(sourceDrafts, destinationDrafts);
        var sourcePersonalState = await workspacePersonalStateService
            .ReadProjectAsync(request.Source.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var destinationPersonalState = destinationProjectId == request.Source.ProjectId
            ? sourcePersonalState
            : await workspacePersonalStateService
                .ReadProjectAsync(destinationProjectId, cancellationToken)
                .ConfigureAwait(false);
        var relocatedPersonalState = sourcePersonalState.Document is null
            ? null
            : workspacePersonalStateService.PrepareForRelocation(
                sourcePersonalState.Document,
                candidatePaths.OutputRootPath);
        var currentPersonalStateStatus = GetPersonalStateDocumentStatus(
            sourcePersonalState,
            destinationPersonalState,
            relocatedPersonalState,
            sameProject: destinationProjectId == request.Source.ProjectId);
        var sourceChangeSets = await changeSetService
            .ReadStoredForRelocationAsync(request.Source.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var destinationChangeSets = destinationProjectId == request.Source.ProjectId
            ? sourceChangeSets
            : await changeSetService
                .ReadStoredForRelocationAsync(destinationProjectId, cancellationToken)
                .ConfigureAwait(false);
        var currentChangeSetStatus = GetChangeSetDocumentStatus(
            sourceChangeSets,
            destinationChangeSets,
            sameProject: destinationProjectId == request.Source.ProjectId);
        var currentOutputStoreState = InspectCandidateOutputStore(candidatePaths.OutputRootPath);
        var currentReviewToken = CreateReviewToken(
            request.Source.ProjectId,
            destinationProjectId,
            CreatePathsFingerprint(candidatePaths),
            sourceDrafts.ETag,
            destinationDrafts.ETag,
            currentDraftStatus,
            sourcePersonalState.ETag,
            destinationPersonalState.ETag,
            currentPersonalStateStatus,
            FingerprintDocument(relocatedPersonalState),
            sourceChangeSets.ETag,
            destinationChangeSets.ETag,
            currentChangeSetStatus,
            currentOutputStoreState);
        if (currentDraftStatus != draftStatus
            || currentPersonalStateStatus != personalStateStatus
            || currentChangeSetStatus != changeSetStatus
            || (destinationProjectId != request.Source.ProjectId
                && currentOutputStoreState == RelocationOutputStoreState.OccupiedOrUnverifiable)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(currentReviewToken),
                Encoding.ASCII.GetBytes(request.ReviewToken)))
        {
            throw new ProjectRelocationReviewMismatchException();
        }

        string? copiedDraftETag = null;
        string? copiedPersonalStateETag = null;
        string? copiedChangeSetETag = null;
        try
        {
            if (draftStatus == ProjectRelocationDocumentStatusDto.Copy)
            {
                if (!sourceDrafts.Exists || sourceDrafts.Document is null)
                {
                    throw new ProjectRelocationReviewMismatchException();
                }

                var writeResult = await workspaceDraftService
                    .WriteForRelocationAsync(
                        destinationProjectId,
                        sourceDrafts.Document,
                        expectedETag: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                copiedDraftETag = writeResult.ETag;
                var sourceAfterCopy = await workspaceDraftService
                    .ReadAsync(request.Source.ProjectId, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(
                        sourceAfterCopy.ETag,
                        sourceDrafts.ETag,
                        StringComparison.Ordinal))
                {
                    throw new ProjectRelocationReviewMismatchException();
                }

                migratedDocuments.Add(DraftDocumentId);
            }

            if (personalStateStatus == ProjectRelocationDocumentStatusDto.Copy)
            {
                if (!sourcePersonalState.Exists || relocatedPersonalState is null)
                {
                    throw new ProjectRelocationReviewMismatchException();
                }

                var writeResult = await workspacePersonalStateService
                    .WriteProjectForRelocationAsync(
                        destinationProjectId,
                        relocatedPersonalState,
                        expectedETag: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                copiedPersonalStateETag = writeResult.ETag;
                var sourceAfterCopy = await workspacePersonalStateService
                    .ReadProjectAsync(request.Source.ProjectId, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(
                        sourceAfterCopy.ETag,
                        sourcePersonalState.ETag,
                        StringComparison.Ordinal))
                {
                    throw new ProjectRelocationReviewMismatchException();
                }

                migratedDocuments.Add(PersonalStateDocumentId);
            }

            if (changeSetStatus == ProjectRelocationDocumentStatusDto.Copy)
            {
                if (!sourceChangeSets.Exists || sourceChangeSets.Document is null)
                {
                    throw new ProjectRelocationReviewMismatchException();
                }

                copiedChangeSetETag = await changeSetService
                    .WriteStoredForRelocationAsync(
                        destinationProjectId,
                        sourceChangeSets.Document,
                        expectedETag: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                var sourceAfterCopy = await changeSetService
                    .ReadStoredForRelocationAsync(request.Source.ProjectId, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(
                        sourceAfterCopy.ETag,
                        sourceChangeSets.ETag,
                        StringComparison.Ordinal))
                {
                    throw new ProjectRelocationReviewMismatchException();
                }

                migratedDocuments.Add(ChangeSetDocumentId);
            }

            await EnsureWorkspaceBindingsUnchangedAsync(
                    request.Source.ProjectId,
                    destinationProjectId,
                    sourceDrafts.ETag,
                    copiedDraftETag ?? destinationDrafts.ETag,
                    sourcePersonalState.ETag,
                    copiedPersonalStateETag ?? destinationPersonalState.ETag,
                    sourceChangeSets.ETag,
                    copiedChangeSetETag ?? destinationChangeSets.ETag,
                    cancellationToken)
                .ConfigureAwait(false);

            if (InspectCandidateOutputStore(candidatePaths.OutputRootPath) != currentOutputStoreState)
            {
                throw new ProjectRelocationReviewMismatchException();
            }

            return new ApplyProjectRelocationResponse(
                destinationProjectId,
                ProjectBridgeMapper.ToDto(candidateHealth),
                migratedDocuments,
                preview.Diagnostics);
        }
        catch (KM.Core.Workspace.WorkspaceDocumentConflictException exception)
        {
            await RollBackCopiedWorkspaceDocumentsAsync(
                    destinationProjectId,
                    copiedDraftETag,
                    copiedPersonalStateETag,
                    copiedChangeSetETag)
                .ConfigureAwait(false);
            throw new ProjectRelocationConflictException(exception);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            await RollBackCopiedWorkspaceDocumentsAsync(
                    destinationProjectId,
                    copiedDraftETag,
                    copiedPersonalStateETag,
                    copiedChangeSetETag)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task EnsureWorkspaceBindingsUnchangedAsync(
        string sourceProjectId,
        string destinationProjectId,
        string? expectedSourceDraftETag,
        string? expectedDestinationDraftETag,
        string? expectedSourcePersonalStateETag,
        string? expectedDestinationPersonalStateETag,
        string? expectedSourceChangeSetETag,
        string? expectedDestinationChangeSetETag,
        CancellationToken cancellationToken)
    {
        var sourceDrafts = await workspaceDraftService
            .ReadAsync(sourceProjectId, cancellationToken)
            .ConfigureAwait(false);
        var destinationDrafts = destinationProjectId == sourceProjectId
            ? sourceDrafts
            : await workspaceDraftService
                .ReadAsync(destinationProjectId, cancellationToken)
                .ConfigureAwait(false);
        var sourcePersonalState = await workspacePersonalStateService
            .ReadProjectAsync(sourceProjectId, cancellationToken)
            .ConfigureAwait(false);
        var destinationPersonalState = destinationProjectId == sourceProjectId
            ? sourcePersonalState
            : await workspacePersonalStateService
                .ReadProjectAsync(destinationProjectId, cancellationToken)
                .ConfigureAwait(false);
        var sourceChangeSets = await changeSetService
            .ReadStoredForRelocationAsync(sourceProjectId, cancellationToken)
            .ConfigureAwait(false);
        var destinationChangeSets = destinationProjectId == sourceProjectId
            ? sourceChangeSets
            : await changeSetService
                .ReadStoredForRelocationAsync(destinationProjectId, cancellationToken)
                .ConfigureAwait(false);

        if (!string.Equals(sourceDrafts.ETag, expectedSourceDraftETag, StringComparison.Ordinal)
            || !string.Equals(
                destinationDrafts.ETag,
                expectedDestinationDraftETag,
                StringComparison.Ordinal)
            || !string.Equals(
                sourcePersonalState.ETag,
                expectedSourcePersonalStateETag,
                StringComparison.Ordinal)
            || !string.Equals(
                destinationPersonalState.ETag,
                expectedDestinationPersonalStateETag,
                StringComparison.Ordinal)
            || !string.Equals(
                sourceChangeSets.ETag,
                expectedSourceChangeSetETag,
                StringComparison.Ordinal)
            || !string.Equals(
                destinationChangeSets.ETag,
                expectedDestinationChangeSetETag,
                StringComparison.Ordinal))
        {
            throw new ProjectRelocationReviewMismatchException();
        }
    }

    private async Task RollBackCopiedWorkspaceDocumentsAsync(
        string destinationProjectId,
        string? copiedDraftETag,
        string? copiedPersonalStateETag,
        string? copiedChangeSetETag)
    {
        List<Exception>? rollbackFailures = null;
        if (copiedChangeSetETag is not null)
        {
            try
            {
                var deleted = await changeSetService
                    .DeleteStoredForRelocationAsync(
                        destinationProjectId,
                        copiedChangeSetETag,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!deleted)
                {
                    (rollbackFailures ??= []).Add(
                        new InvalidOperationException(
                            "The copied project change sets could not be removed."));
                }
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                (rollbackFailures ??= []).Add(exception);
            }
        }

        if (copiedPersonalStateETag is not null)
        {
            try
            {
                var result = await workspacePersonalStateService
                    .DeleteProjectForRelocationAsync(
                        destinationProjectId,
                        copiedPersonalStateETag,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!result.Deleted)
                {
                    (rollbackFailures ??= []).Add(
                        new InvalidOperationException(
                            "The copied project personal state could not be removed."));
                }
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                (rollbackFailures ??= []).Add(exception);
            }
        }

        if (copiedDraftETag is not null)
        {
            try
            {
                var result = await workspaceDraftService
                    .DeleteForRelocationAsync(
                        destinationProjectId,
                        copiedDraftETag,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!result.Deleted)
                {
                    (rollbackFailures ??= []).Add(
                        new InvalidOperationException(
                            "The copied project drafts could not be removed."));
                }
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                (rollbackFailures ??= []).Add(exception);
            }
        }

        if (rollbackFailures is { Count: > 0 })
        {
            var rollbackFailure = rollbackFailures.Count == 1
                ? rollbackFailures[0]
                : new AggregateException(rollbackFailures);
            throw new ProjectRelocationConflictException(rollbackFailure);
        }
    }

    private static bool IsFatal(Exception exception)
    {
        if (exception is
            OutOfMemoryException or
            StackOverflowException or
            AccessViolationException or
            AppDomainUnloadedException)
        {
            return true;
        }

        if (exception is AggregateException aggregateException
            && aggregateException.InnerExceptions.Any(IsFatal))
        {
            return true;
        }

        return exception.InnerException is not null && IsFatal(exception.InnerException);
    }

    private static ProjectRelocationDocumentStatusDto GetDraftDocumentStatus(
        Api.Workspace.ReadWorkspaceDraftsResponse source,
        Api.Workspace.ReadWorkspaceDraftsResponse destination)
    {
        if (!source.Exists || source.Document is null)
        {
            return ProjectRelocationDocumentStatusDto.Skip;
        }

        if (!destination.Exists || destination.Document is null)
        {
            return ProjectRelocationDocumentStatusDto.Copy;
        }

        var sourceBytes = JsonSerializer.SerializeToUtf8Bytes(source.Document, FingerprintSerializerOptions);
        var destinationBytes = JsonSerializer.SerializeToUtf8Bytes(destination.Document, FingerprintSerializerOptions);
        return sourceBytes.AsSpan().SequenceEqual(destinationBytes)
            ? ProjectRelocationDocumentStatusDto.Skip
            : ProjectRelocationDocumentStatusDto.Conflict;
    }

    private static ProjectRelocationDocumentStatusDto GetPersonalStateDocumentStatus(
        ReadWorkspaceProjectStateResponse source,
        ReadWorkspaceProjectStateResponse destination,
        WorkspaceProjectPersonalStateDocumentDto? relocatedSource,
        bool sameProject)
    {
        if (!source.Exists || source.Document is null || relocatedSource is null)
        {
            return ProjectRelocationDocumentStatusDto.Skip;
        }

        if (sameProject)
        {
            return ProjectRelocationDocumentStatusDto.Skip;
        }

        if (!destination.Exists || destination.Document is null)
        {
            return ProjectRelocationDocumentStatusDto.Copy;
        }

        var sourceBytes = JsonSerializer.SerializeToUtf8Bytes(
            relocatedSource,
            FingerprintSerializerOptions);
        var destinationBytes = JsonSerializer.SerializeToUtf8Bytes(
            destination.Document,
            FingerprintSerializerOptions);
        return sourceBytes.AsSpan().SequenceEqual(destinationBytes)
            ? ProjectRelocationDocumentStatusDto.Skip
            : ProjectRelocationDocumentStatusDto.Conflict;
    }

    private static ProjectRelocationDocumentStatusDto GetChangeSetDocumentStatus(
        StoredChangeSetReadResult source,
        StoredChangeSetReadResult destination,
        bool sameProject)
    {
        if (!source.Exists || source.Document is null || sameProject)
        {
            return ProjectRelocationDocumentStatusDto.Skip;
        }

        if (!destination.Exists || destination.Document is null)
        {
            return ProjectRelocationDocumentStatusDto.Copy;
        }

        var sourceBytes = JsonSerializer.SerializeToUtf8Bytes(
            source.Document,
            FingerprintSerializerOptions);
        var destinationBytes = JsonSerializer.SerializeToUtf8Bytes(
            destination.Document,
            FingerprintSerializerOptions);
        return sourceBytes.AsSpan().SequenceEqual(destinationBytes)
            ? ProjectRelocationDocumentStatusDto.Skip
            : ProjectRelocationDocumentStatusDto.Conflict;
    }

    private static string FingerprintDocument<TDocument>(TDocument? document)
    {
        return document is null
            ? "missing"
            : Convert.ToHexStringLower(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(document, FingerprintSerializerOptions)));
    }

    private static string CreateReviewToken(
        string sourceProjectId,
        string destinationProjectId,
        string candidatePathsFingerprint,
        string? sourceETag,
        string? destinationETag,
        ProjectRelocationDocumentStatusDto documentStatus,
        string? sourcePersonalStateETag,
        string? destinationPersonalStateETag,
        ProjectRelocationDocumentStatusDto personalStateStatus,
        string personalStateFingerprint,
        string? sourceChangeSetETag,
        string? destinationChangeSetETag,
        ProjectRelocationDocumentStatusDto changeSetStatus,
        RelocationOutputStoreState outputStoreState)
    {
        var framed = string.Create(
            provider: null,
            $"project-relocation-v5\n{sourceProjectId.Length}:{sourceProjectId}\n{destinationProjectId.Length}:{destinationProjectId}\n{candidatePathsFingerprint}\n{sourceETag ?? "missing"}\n{destinationETag ?? "missing"}\n{documentStatus}\n{sourcePersonalStateETag ?? "missing"}\n{destinationPersonalStateETag ?? "missing"}\n{personalStateStatus}\n{personalStateFingerprint}\n{sourceChangeSetETag ?? "missing"}\n{destinationChangeSetETag ?? "missing"}\n{changeSetStatus}\n{outputStoreState}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(framed)));
    }

    private static RelocationOutputStoreState InspectCandidateOutputStore(string? outputRootPath)
    {
        if (string.IsNullOrWhiteSpace(outputRootPath))
        {
            return RelocationOutputStoreState.NotConfigured;
        }

        try
        {
            var outputRoot = new DirectoryInfo(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRootPath)));
            outputRoot.Refresh();
            if (!outputRoot.Exists)
            {
                return RelocationOutputStoreState.OccupiedOrUnverifiable;
            }

            using var candidates = outputRoot
                .EnumerateFileSystemInfos(".km", MetadataEntryEnumeration)
                .Take(2)
                .GetEnumerator();
            if (!candidates.MoveNext())
            {
                return RelocationOutputStoreState.Absent;
            }

            var metadataEntry = candidates.Current;
            if (candidates.MoveNext()
                || !string.Equals(metadataEntry.Name, ".km", StringComparison.Ordinal)
                || metadataEntry is not DirectoryInfo metadataDirectory)
            {
                return RelocationOutputStoreState.OccupiedOrUnverifiable;
            }

            metadataDirectory.Refresh();
            if (!metadataDirectory.Exists
                || metadataDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    && !string.IsNullOrEmpty(metadataDirectory.LinkTarget))
            {
                return RelocationOutputStoreState.OccupiedOrUnverifiable;
            }

            using var contents = metadataDirectory
                .EnumerateFileSystemInfos("*", MetadataEntryEnumeration)
                .Take(1)
                .GetEnumerator();
            return contents.MoveNext()
                ? RelocationOutputStoreState.OccupiedOrUnverifiable
                : RelocationOutputStoreState.Empty;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException)
        {
            return RelocationOutputStoreState.OccupiedOrUnverifiable;
        }
    }

    private static string CreatePathsFingerprint(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintField(hash, "project-relocation-paths-v1");
        AppendFingerprintField(hash, paths.BaseRomFsPath);
        AppendFingerprintField(hash, paths.BaseExeFsPath);
        AppendFingerprintField(hash, paths.OutputRootPath);
        AppendFingerprintField(hash, paths.SaveFilePath);
        AppendFingerprintField(
            hash,
            paths.SelectedGame is ProjectGame.Scarlet or ProjectGame.Violet
                ? paths.ScarletVioletSupportFolderPath
                : null);
        AppendFingerprintField(
            hash,
            paths.SelectedGame is ProjectGame.ZA
                ? paths.PokemonLegendsZASupportFolderPath
                : null);
        AppendFingerprintField(hash, paths.SelectedGame?.ToString());
        AppendFingerprintField(hash, paths.GameTextLanguage);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendFingerprintField(IncrementalHash hash, string? value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, -1);
            hash.AppendData(length);
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        BinaryPrimitives.WriteInt32LittleEndian(length, byteCount);
        hash.AppendData(length);
        if (byteCount > 0)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value));
        }
    }

    private static PreviewProjectRelocationResponse RejectedPreview(
        string sourceProjectId,
        string message,
        string code,
        ProjectHealth? candidateHealth = null)
    {
        var diagnostics = candidateHealth?.Diagnostics
            .Select(ProjectBridgeMapper.ToDto)
            .ToList() ?? [];
        diagnostics.Add(CreateDiagnostic(ApiDiagnosticSeverity.Error, message, code));
        return new PreviewProjectRelocationResponse(
            CreateReviewToken(
                sourceProjectId,
                "unavailable",
                Convert.ToHexStringLower(SHA256.HashData("unavailable"u8)),
                sourceETag: null,
                destinationETag: null,
                ProjectRelocationDocumentStatusDto.Skip,
                sourcePersonalStateETag: null,
                destinationPersonalStateETag: null,
                ProjectRelocationDocumentStatusDto.Skip,
                personalStateFingerprint: "missing",
                sourceChangeSetETag: null,
                destinationChangeSetETag: null,
                ProjectRelocationDocumentStatusDto.Skip,
                RelocationOutputStoreState.Unavailable),
            sourceProjectId,
            DestinationProjectId: null,
            CanApply: false,
            candidateHealth?.Paths
                .Select(path => new ProjectRelocationRoleDto(ToDto(path.Role), ToDto(path.Status)))
                .ToArray() ?? [],
            [
                new ProjectRelocationDocumentDto(DraftDocumentId, ProjectRelocationDocumentStatusDto.Skip),
                new ProjectRelocationDocumentDto(PersonalStateDocumentId, ProjectRelocationDocumentStatusDto.Skip),
                new ProjectRelocationDocumentDto(ChangeSetDocumentId, ProjectRelocationDocumentStatusDto.Skip),
            ],
            diagnostics);
    }

    private static string GetRejectedMessage(ProjectRelocationDisposition disposition)
    {
        return disposition switch
        {
            ProjectRelocationDisposition.RejectedSelectedGameMismatch =>
                "The relocation candidate does not match the selected game.",
            ProjectRelocationDisposition.RejectedInvalidCandidatePaths =>
                "The relocation candidate paths are not valid for this project.",
            ProjectRelocationDisposition.RejectedDiscoveryLimit =>
                "The relocation candidate exceeded a bounded project discovery limit.",
            _ => "The relocation request is invalid.",
        };
    }

    private static ApiDiagnostic CreateDiagnostic(
        ApiDiagnosticSeverity severity,
        string message,
        string code)
    {
        return new ApiDiagnostic(severity, message, Domain: "project.relocation")
        {
            Code = code,
        };
    }

    private static void ValidateProjectId(string projectId)
    {
        if (projectId is null
            || projectId.Length > ProjectRelocationService.MaximumProjectIdLength
            || string.IsNullOrWhiteSpace(projectId)
            || projectId != projectId.Trim()
            || projectId.Any(char.IsControl))
        {
            throw new ArgumentException("The source project id is invalid.", nameof(projectId));
        }
    }

    private static bool HasBoundedProjectPathStrings(ProjectPaths paths)
    {
        return IsBoundedOptionalString(paths.BaseRomFsPath, ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(paths.BaseExeFsPath, ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(paths.OutputRootPath, ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(paths.SaveFilePath, ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(
                paths.ScarletVioletSupportFolderPath,
                ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(
                paths.PokemonLegendsZASupportFolderPath,
                ProjectRelocationService.MaximumCandidatePathLength)
            && IsBoundedOptionalString(
                paths.GameTextLanguage,
                ProjectRelocationService.MaximumGameTextLanguageLength);
    }

    private static bool IsBoundedOptionalString(string? value, int maximumLength)
    {
        return value is null
            || (value.Length <= maximumLength && !value.Any(char.IsControl));
    }

    private static void ValidateReviewToken(string reviewToken)
    {
        if (reviewToken is null
            || reviewToken.Length != 64
            || reviewToken.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ProjectRelocationReviewMismatchException();
        }
    }

    private static ProjectPathRoleDto ToDto(ProjectPathRole role)
    {
        return role switch
        {
            ProjectPathRole.BaseRomFs => ProjectPathRoleDto.BaseRomFs,
            ProjectPathRole.BaseExeFs => ProjectPathRoleDto.BaseExeFs,
            ProjectPathRole.OutputRoot => ProjectPathRoleDto.OutputRoot,
            ProjectPathRole.SaveFile => ProjectPathRoleDto.SaveFile,
            ProjectPathRole.ScarletVioletSupportFolder => ProjectPathRoleDto.ScarletVioletSupportFolder,
            ProjectPathRole.PokemonLegendsZASupportFolder => ProjectPathRoleDto.PokemonLegendsZASupportFolder,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private static ProjectPathStatusDto ToDto(ProjectPathStatus status)
    {
        return status switch
        {
            ProjectPathStatus.NotSet => ProjectPathStatusDto.NotSet,
            ProjectPathStatus.Missing => ProjectPathStatusDto.Missing,
            ProjectPathStatus.WrongKind => ProjectPathStatusDto.WrongKind,
            ProjectPathStatus.Valid => ProjectPathStatusDto.Valid,
            ProjectPathStatus.Unsafe => ProjectPathStatusDto.Unsafe,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }

    private enum RelocationOutputStoreState
    {
        Unavailable,
        NotConfigured,
        Absent,
        Empty,
        OccupiedOrUnverifiable,
    }
}

public sealed class ProjectRelocationReviewMismatchException : Exception
{
    public ProjectRelocationReviewMismatchException()
        : base("The project relocation changed after review. Review it again before applying.")
    {
    }
}

public sealed class ProjectRelocationConflictException : Exception
{
    public ProjectRelocationConflictException()
        : base("The relocated project contains conflicting private workspace state.")
    {
    }

    public ProjectRelocationConflictException(Exception innerException)
        : base("The relocated project contains conflicting private workspace state.", innerException)
    {
    }
}
