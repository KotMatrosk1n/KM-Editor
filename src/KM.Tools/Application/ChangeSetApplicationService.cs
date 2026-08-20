// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Api.Bridge;
using KM.Api.ChangeSets;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Core.Workspace;
using KM.Tools.Bridge;

namespace KM.Tools.Application;

/// <summary>
/// Owns durable named authoring state and composes it through the existing
/// game-specific edit-session planners. It does not interpret game values or
/// provide a generic semantic writer.
/// </summary>
public sealed class ChangeSetApplicationService
{
    private const int MaximumIdLength = 128;
    private const int MaximumNameLength = 128;
    private const int MaximumNotesLength = 32 * 1024;
    private const int MaximumTagLength = 64;
    private const int MaximumSummaryLength = 2_048;
    private const int MaximumDomainLength = 256;
    private const int MaximumRecordIdLength = 4_096;
    private const int MaximumFieldLength = 512;
    private const int MaximumValueLength = 256 * 1024;
    private const int MaximumSourceCount = 128;
    private const int MaximumOwnedTargetCount = 64;
    private const int MaximumRelativePathLength = 4_096;
    private const int MaximumPortablePackageBytes = 2 * 1024 * 1024;
    private const int MaximumPortableNumericValueLength = 20;
    private const string PortablePendingEditAdapterId = "pending-edit.v1";
    private const int PortablePendingEditAdapterSchemaVersion = 1;
    private const string DocumentId = "change-sets";
    private const string DocumentType = "workspace-change-sets";
    private static readonly IReadOnlySet<string> PortablePendingEditDomains =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "workflow.items",
            "workflow.moves",
            "workflow.pokemon",
            "workflow.trainers",
        };
    private static readonly WorkspaceDocumentId OperationLeaseId =
        new("change-sets-operation");
    private static readonly JsonSerializerOptions SerializerOptions =
        new(BridgeJson.SerializerOptions)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        };
    private static readonly WorkspaceDocumentDefinition<StoredChangeSetWorkspaceDocument> Definition =
        new(
            new WorkspaceDocumentId(DocumentId),
            DocumentType,
            ChangeSetContract.SchemaVersion);

    private readonly VersionedWorkspaceDocumentStore store;
    private readonly WorkspacePersonalStateApplicationService workspacePersonalStateService;

    public ChangeSetApplicationService(
        VersionedWorkspaceDocumentStore? store = null,
        WorkspacePersonalStateApplicationService? workspacePersonalStateService = null)
    {
        this.store = store ?? new VersionedWorkspaceDocumentStore(
            GetDefaultAppDataRoot(),
            serializerOptions: SerializerOptions);
        this.workspacePersonalStateService = workspacePersonalStateService
            ?? new WorkspacePersonalStateApplicationService();
    }

    public async Task<ChangeSetWorkspaceSnapshotDto> ReadAsync(
        ReadChangeSetWorkspaceRequest request,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = ValidateScope(request.Scope);
        using var operationLease = await AcquireProjectLeasesAsync(
                [scope.ProjectId],
                cancellationToken)
            .ConfigureAwait(false);
        var stored = await ReadStoredAsync(scope.ProjectId, cancellationToken).ConfigureAwait(false);
        var document = stored.Document ?? CreateEmptyStoredDocument(scope.Game, DateTimeOffset.UtcNow);
        ValidateStoredDocument(document, scope.Game);
        var session = request.Session is null ? null : EditSessionBridgeMapper.ToCore(request.Session);
        var effective = await MaterializeCoreAsync(
                scope,
                document,
                stored.ETag,
                session,
                buildVariantId: null,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        return ToSnapshot(document, stored.ETag, effective);
    }

    public async Task<ChangeSetWorkspaceSnapshotDto> MutateAsync(
        MutateChangeSetWorkspaceRequest request,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Mutation);
        var scope = ValidateScope(request.Scope);
        using var operationLease = await AcquireProjectLeasesAsync(
                [scope.ProjectId],
                cancellationToken)
            .ConfigureAwait(false);
        var expectedETag = NormalizeETag(request.ExpectedETag, allowNull: true);
        var stored = await ReadStoredAsync(scope.ProjectId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(stored.ETag, expectedETag, StringComparison.Ordinal))
        {
            throw new WorkspaceDocumentConflictException(expectedETag, stored.ETag);
        }

        var current = stored.Document ?? CreateEmptyStoredDocument(scope.Game, DateTimeOffset.UtcNow);
        ValidateStoredDocument(current, scope.Game);
        var session = request.Session is null ? null : EditSessionBridgeMapper.ToCore(request.Session);
        if (session is not null)
        {
            ValidatePendingEdits(session.PendingEdits);
        }

        var updated = ApplyMutation(current, request.Mutation, DateTimeOffset.UtcNow);
        updated = TrimHistoryToBudget(updated);
        ValidateStoredDocument(updated, scope.Game);
        var write = await store.WriteConditionalAsync(
                scope.Identity,
                Definition,
                updated,
                stored.ETag,
                cancellationToken)
            .ConfigureAwait(false);
        var effective = await MaterializeCoreAsync(
                scope,
                updated,
                write.ETag,
                session,
                buildVariantId: null,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        return ToSnapshot(updated, write.ETag, effective);
    }

    public async Task<CaptureChangeSetSessionResponse> CaptureSessionAsync(
        CaptureChangeSetSessionRequest request,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = ValidateScope(request.Scope);
        using var operationLease = await AcquireProjectLeasesAsync(
                [scope.ProjectId],
                cancellationToken)
            .ConfigureAwait(false);
        ValidateId(request.ChangeSetId, "change set id");
        var expectedETag = NormalizeETag(request.ExpectedETag, allowNull: false)!;
        var stored = await RequireStoredAsync(scope.ProjectId, expectedETag, cancellationToken)
            .ConfigureAwait(false);
        var document = stored.Document!;
        ValidateStoredDocument(document, scope.Game);
        var changeSetIndex = document.ChangeSets
            .Select((item, index) => (item, index))
            .Where(pair => string.Equals(pair.item.ChangeSetId, request.ChangeSetId, StringComparison.Ordinal))
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .Single();
        if (changeSetIndex < 0)
        {
            throw Invalid("The active staging change set does not exist.");
        }

        if (!string.Equals(
                document.ActiveChangeSetId,
                request.ChangeSetId,
                StringComparison.Ordinal))
        {
            throw Invalid("The capture target is not the workspace's active staging change set.");
        }

        var targetSet = document.ChangeSets[changeSetIndex];
        if (targetSet.Archived)
        {
            throw Invalid("An archived change set cannot receive staged edits.");
        }

        var staged = EditSessionBridgeMapper.ToCore(request.StagedSession);
        var previous = request.PreviousSession is null
            ? new EditSession(staged.Id, staged.CreatedAt, Array.Empty<PendingEdit>())
            : EditSessionBridgeMapper.ToCore(request.PreviousSession);
        if (previous.Id != staged.Id)
        {
            throw Invalid("A staged transition must preserve its edit-session id.");
        }

        if (previous.AuthoringBinding is { } priorBinding)
        {
            _ = await ValidateBoundSessionCoreAsync(
                    previous,
                    scope.Paths,
                    FromBindingOutputMode(priorBinding.OutputMode),
                    planner,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (previous.PendingEdits.Any(edit => edit.Association is not null))
        {
            throw Invalid("An unbound previous session cannot contain named change-set associations.");
        }

        ValidatePendingEdits(previous.PendingEdits);
        ValidatePendingEdits(staged.PendingEdits);
        var existingOperations = targetSet.Operations.ToDictionary(
            operation => operation.OperationId,
            StringComparer.Ordinal);
        var allStoredOperations = document.ChangeSets
            .SelectMany(set => set.Operations.Select(operation => (set.ChangeSetId, Operation: operation)))
            .ToDictionary(pair => pair.Operation.OperationId, StringComparer.Ordinal);
        var existingOperationsByTarget = targetSet.Operations.ToDictionary(
            operation => CreateEditTargetKey(
                EditSessionBridgeMapper.ToPendingEditCore(operation.PendingEdit)),
            StringComparer.Ordinal);
        var previousByTarget = BuildUniqueEditMap(previous.PendingEdits);
        var stagedByTarget = BuildUniqueEditMap(staged.PendingEdits);
        var touchedTargets = previousByTarget.Keys
            .Concat(stagedByTarget.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(key =>
                !previousByTarget.TryGetValue(key, out var before)
                || !stagedByTarget.TryGetValue(key, out var after)
                || !PendingEditContentEquals(before, after))
            .ToHashSet(StringComparer.Ordinal);
        var annotatedEdits = new List<PendingEdit>(staged.PendingEdits.Count);
        var changedOperationIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stagedEdit in staged.PendingEdits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetKey = CreateEditTargetKey(stagedEdit);
            previousByTarget.TryGetValue(targetKey, out var previousEdit);
            var editChanged = previousEdit is null || !PendingEditContentEquals(previousEdit, stagedEdit);
            var association = stagedEdit.Association;
            if (previousEdit is null
                && association is not null
                && allStoredOperations.TryGetValue(association.OperationId, out var storedAssociated)
                && string.Equals(
                    storedAssociated.ChangeSetId,
                    association.ChangeSetId,
                    StringComparison.Ordinal)
                && PendingEditContentEquals(
                    EditSessionBridgeMapper.ToPendingEditCore(storedAssociated.Operation.PendingEdit),
                    stagedEdit))
            {
                editChanged = false;
                touchedTargets.Remove(targetKey);
            }

            if (!editChanged && association is null)
            {
                association = previousEdit?.Association;
            }

            if (editChanged)
            {
                var previousAssociation = previousEdit?.Association;
                existingOperationsByTarget.TryGetValue(targetKey, out var existingAtTarget);
                var operationId = existingAtTarget?.OperationId
                    ?? (string.Equals(
                        previousAssociation?.ChangeSetId,
                        request.ChangeSetId,
                        StringComparison.Ordinal)
                    ? previousAssociation!.OperationId
                    : string.Equals(
                        association?.ChangeSetId,
                        request.ChangeSetId,
                        StringComparison.Ordinal)
                        ? association!.OperationId
                        : CreateId());
                association = new PendingEditAssociation(
                    PendingEditAssociation.CurrentVersion,
                    request.ChangeSetId,
                    operationId);
                changedOperationIds.Add(operationId);
            }

            annotatedEdits.Add(stagedEdit with { Association = association });
        }

        var finalActiveEdits = annotatedEdits
            .Where(edit => string.Equals(
                edit.Association?.ChangeSetId,
                request.ChangeSetId,
                StringComparison.Ordinal))
            .ToDictionary(CreateEditTargetKey, StringComparer.Ordinal);
        var annotatedSession = staged with
        {
            PendingEdits = annotatedEdits,
            AuthoringBinding = null,
        };
        if (touchedTargets.Count == 0)
        {
            var unchangedEffective = await MaterializeCoreAsync(
                    scope,
                    document,
                    stored.ETag,
                    annotatedSession,
                    buildVariantId: null,
                    planner,
                    cancellationToken)
                .ConfigureAwait(false);
            return new CaptureChangeSetSessionResponse(
                ToSnapshot(document, stored.ETag, unchangedEffective),
                EditSessionBridgeMapper.ToDto(annotatedSession),
                CapturedOperationIds: Array.Empty<string>(),
                RemovedOperationIds: Array.Empty<string>());
        }

        var now = DateTimeOffset.UtcNow;
        var operations = new List<ChangeSetOperationDto>(targetSet.Operations.Count + finalActiveEdits.Count);
        var removedOperationIds = new List<string>();
        foreach (var storedOperation in targetSet.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var storedTarget = CreateEditTargetKey(
                EditSessionBridgeMapper.ToPendingEditCore(storedOperation.PendingEdit));
            if (!touchedTargets.Contains(storedTarget))
            {
                operations.Add(storedOperation);
                finalActiveEdits.Remove(storedTarget);
                continue;
            }

            if (!finalActiveEdits.Remove(storedTarget, out var edit))
            {
                removedOperationIds.Add(storedOperation.OperationId);
                continue;
            }

            var association = edit.Association
                ?? throw Invalid("An active staged edit is missing its authoring association.");
            var binding = CreateOperationBinding(edit, planner);
            operations.Add(new ChangeSetOperationDto(
                association.OperationId,
                ChangeSetOperationStorageKindDto.LegacyPendingEdit,
                EditSessionBridgeMapper.ToPendingEditDto(edit),
                binding.Kind,
                binding.Fingerprint,
                binding.OwnedTargets,
                storedOperation.CreatedAtUtc,
                now));
        }


        foreach (var edit in finalActiveEdits.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var association = edit.Association
                ?? throw Invalid("An active staged edit is missing its authoring association.");
            if (!touchedTargets.Contains(CreateEditTargetKey(edit)))
            {
                continue;
            }

            existingOperations.TryGetValue(association.OperationId, out var existing);
            var binding = CreateOperationBinding(edit, planner);
            operations.Add(new ChangeSetOperationDto(
                association.OperationId,
                ChangeSetOperationStorageKindDto.LegacyPendingEdit,
                EditSessionBridgeMapper.ToPendingEditDto(edit),
                binding.Kind,
                binding.Fingerprint,
                binding.OwnedTargets,
                existing?.CreatedAtUtc ?? now,
                now));
        }

        var updatedSet = targetSet with
        {
            Operations = operations,
            UpdatedAtUtc = now,
        };
        var nextSets = document.ChangeSets.ToArray();
        nextSets[changeSetIndex] = updatedSet;
        var updatedState = GetState(document) with
        {
            ChangeSets = nextSets,
            ActiveChangeSetId = request.ChangeSetId,
        };
        var updated = PushUndo(document, updatedState, "Stage changes", now);
        updated = TrimHistoryToBudget(updated);
        ValidateStoredDocument(updated, scope.Game);
        var write = await store.WriteConditionalAsync(
                scope.Identity,
                Definition,
                updated,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);

        var effective = await MaterializeCoreAsync(
                scope,
                updated,
                write.ETag,
                annotatedSession,
                buildVariantId: null,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        return new CaptureChangeSetSessionResponse(
            ToSnapshot(updated, write.ETag, effective),
            EditSessionBridgeMapper.ToDto(annotatedSession),
            changedOperationIds.Order(StringComparer.Ordinal).ToArray(),
            removedOperationIds.Order(StringComparer.Ordinal).ToArray());
    }

    public async Task<ChangeSetMaterializationDto> MaterializeAsync(
        MaterializeChangeSetWorkspaceRequest request,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = ValidateScope(request.Scope);
        using var operationLease = await AcquireProjectLeasesAsync(
                [scope.ProjectId],
                cancellationToken)
            .ConfigureAwait(false);
        if (request.BuildVariantId is not null)
        {
            ValidateId(request.BuildVariantId, "build variant id");
        }

        var expectedETag = NormalizeETag(request.ExpectedETag, allowNull: false)!;
        var stored = await RequireStoredAsync(scope.ProjectId, expectedETag, cancellationToken)
            .ConfigureAwait(false);
        var session = request.Session is null ? null : EditSessionBridgeMapper.ToCore(request.Session);
        return await MaterializeCoreAsync(
                scope,
                stored.Document!,
                stored.ETag,
                session,
                request.BuildVariantId,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteWithAuthoringBindingAsync<TResult>(
        EditSession session,
        ProjectPaths paths,
        ChangePlanOutputModeDto? requestedOutputMode,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        Func<TResult> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(operation);
        if (session.AuthoringBinding is null)
        {
            EnsureUnboundSessionHasNoNamedAssociations(session);
            return operation();
        }

        var projectId = ProjectIdentity.FromPaths(paths).Value;
        using var operationLease = await AcquireProjectLeasesAsync([projectId], cancellationToken)
            .ConfigureAwait(false);
        _ = await ValidateBoundSessionCoreAsync(
                session,
                paths,
                requestedOutputMode,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        return operation();
    }

    public async Task<ApplyResult> ExecuteBoundApplyAsync(
        EditSession session,
        ProjectPaths paths,
        ChangePlanOutputModeDto? requestedOutputMode,
        ChangePlan reviewedPlan,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        Func<ApplyResult> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(operation);
        if (session.AuthoringBinding is null)
        {
            EnsureUnboundSessionHasNoNamedAssociations(session);
            return operation();
        }

        var projectId = ProjectIdentity.FromPaths(paths).Value;
        using var operationLease = await AcquireProjectLeasesAsync([projectId], cancellationToken)
            .ConfigureAwait(false);
        var context = await ValidateBoundSessionCoreAsync(
                session,
                paths,
                requestedOutputMode,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        var result = operation();
        return await AcknowledgeSuccessfulApplyAsync(
                context,
                session,
                reviewedPlan,
                result,
                planner,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static void EnsureUnboundSessionHasNoNamedAssociations(EditSession session)
    {
        if (session.PendingEdits.Any(edit => edit.Association is not null))
        {
            throw Invalid(
                "A session with named change-set operations requires an exact authoring binding.");
        }
    }

    private async Task<BoundSessionContext> ValidateBoundSessionCoreAsync(
        EditSession session,
        ProjectPaths paths,
        ChangePlanOutputModeDto? requestedOutputMode,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        CancellationToken cancellationToken)
    {
        var binding = session.AuthoringBinding
            ?? throw Invalid("A materialized edit session authoring binding is required.");
        var actualProjectId = ProjectIdentity.FromPaths(paths).Value;
        if (!string.Equals(actualProjectId, binding.ProjectId, StringComparison.Ordinal))
        {
            throw new WorkspaceDocumentConflictException(binding.WorkspaceETag, actualETag: null);
        }

        var requestedMode = ToBindingOutputMode(requestedOutputMode);
        if (!string.Equals(requestedMode, binding.OutputMode, StringComparison.Ordinal))
        {
            throw Invalid("The requested output mode does not match the materialized build variant.");
        }

        var stored = await ReadStoredAsync(actualProjectId, cancellationToken).ConfigureAwait(false);
        if (!stored.Exists
            || stored.Document is null
            || !string.Equals(stored.ETag, binding.WorkspaceETag, StringComparison.Ordinal))
        {
            throw new WorkspaceDocumentConflictException(binding.WorkspaceETag, stored.ETag);
        }

        var scope = new ValidatedScope(
            actualProjectId,
            GetIdentity(actualProjectId),
            ProjectBridgeMapper.ToDto(paths.SelectedGame
                ?? throw Invalid("A bound edit session requires a selected game.")),
            paths);
        ValidateStoredDocument(stored.Document, scope.Game);
        var localSession = session with
        {
            PendingEdits = session.PendingEdits.Where(edit => edit.Association is null).ToArray(),
            AuthoringBinding = null,
        };
        var buildVariantId = ResolveBoundVariantId(
            stored.Document,
            binding,
            requestedOutputMode,
            localSession.PendingEdits);
        var materialized = await MaterializeCoreAsync(
                scope,
                stored.Document,
                stored.ETag,
                localSession,
                buildVariantId,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!materialized.CanMaterialize || materialized.Session is null)
        {
            throw Invalid("The named change sets are stale, conflicted, or no longer materializable.");
        }

        var expectedSession = EditSessionBridgeMapper.ToCore(materialized.Session);
        if (!EditSessionsMatchExactly(session, expectedSession))
        {
            throw Invalid("The bound edit session does not exactly match the current named change sets.");
        }

        return new BoundSessionContext(scope, stored.Document, stored.ETag!, buildVariantId);
    }

    private static string? ResolveBoundVariantId(
        StoredChangeSetWorkspaceDocument document,
        EditSessionAuthoringBinding binding,
        ChangePlanOutputModeDto? outputMode,
        IReadOnlyList<PendingEdit> sessionLocalEdits)
    {
        var candidates = document.BuildVariants
            .Cast<ChangeSetBuildVariantDto?>()
            .Prepend(null);
        foreach (var candidate in candidates)
        {
            if (candidate?.OutputProfileId is { } explicitProfileId
                && !string.Equals(
                    explicitProfileId,
                    binding.OutputProfileId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var ignoredConflicts = new List<ChangeSetConflictDto>();
            var selectedIds = SelectSets(document, candidate, ignoredConflicts)
                .Select(set => set.ChangeSetId)
                .ToArray();
            if (!selectedIds.SequenceEqual(binding.SelectedChangeSetIds, StringComparer.Ordinal))
            {
                continue;
            }

            var fingerprint = CreateWorkspaceFingerprint(
                document,
                selectedIds,
                candidate,
                binding.OutputProfileId,
                outputMode,
                binding.OutputRootFingerprint,
                sessionLocalEdits);
            if (string.Equals(fingerprint, binding.WorkspaceFingerprint, StringComparison.Ordinal))
            {
                return candidate?.VariantId;
            }
        }

        throw Invalid("The edit session build-variant binding no longer matches the workspace.");
    }

    private async Task<ApplyResult> AcknowledgeSuccessfulApplyAsync(
        BoundSessionContext context,
        EditSession session,
        ChangePlan reviewedPlan,
        ApplyResult result,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        CancellationToken cancellationToken)
    {
        if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || result.OutputTransaction?.Outcome is KM.Core.Output.OutputApplyOutcome.RolledBack
                or KM.Core.Output.OutputApplyOutcome.RecoveryRequired)
        {
            return result;
        }

        var committedPlan = new ChangePlan(
            reviewedPlan.SessionId,
            result.Manifest.Writes,
            Array.Empty<ValidationDiagnostic>());
        var reviewedTargets = reviewedPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var writtenTargets = result.WrittenFiles
            .Select(file => file.RelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var outputCommitted = result.OutputTransaction is null
            || result.OutputTransaction.Outcome == KM.Core.Output.OutputApplyOutcome.Committed;
        if (!outputCommitted
            || !string.Equals(result.ApplyId, result.Manifest.ApplyId, StringComparison.Ordinal)
            || !ChangePlanReview.Matches(reviewedPlan, committedPlan)
            || !reviewedTargets.SequenceEqual(writtenTargets, StringComparer.Ordinal))
        {
            return AppendApplyRefreshWarning(
                result,
                "The output result did not prove the exact reviewed change-set plan, so source bindings were not refreshed.");
        }

        try
        {
            var editsByOperationId = session.PendingEdits
                .Where(edit => edit.Association is not null)
                .ToDictionary(edit => edit.Association!.OperationId, StringComparer.Ordinal);
            if (editsByOperationId.Count == 0
                && context.Document.UndoHistory.Count == 0
                && context.Document.RedoHistory.Count == 0)
            {
                return result;
            }

            var selectedIds = session.AuthoringBinding!.SelectedChangeSetIds
                .ToHashSet(StringComparer.Ordinal);
            var now = DateTimeOffset.UtcNow;
            var refreshedSets = context.Document.ChangeSets.Select(set =>
            {
                if (!selectedIds.Contains(set.ChangeSetId))
                {
                    return set;
                }

                var operations = set.Operations.Select(operation =>
                {
                    if (!editsByOperationId.TryGetValue(operation.OperationId, out var edit))
                    {
                        throw Invalid("The applied session is missing a selected change-set operation.");
                    }

                    var refreshed = CreateOperationBinding(
                        edit,
                        planner,
                        outputMode: null,
                        satisfiedOwnedTargets: operation.OwnedTargets);
                    if (refreshed.Kind != ChangeSetSourceBindingKindDto.ReviewedPlan
                        || refreshed.Fingerprint is null)
                    {
                        throw Invalid("An applied change-set operation could not be rebound through its game workflow.");
                    }

                    return operation with
                    {
                        SourceBindingKind = refreshed.Kind,
                        SourceFingerprint = refreshed.Fingerprint,
                        OwnedTargets = refreshed.OwnedTargets.Count == 0
                            ? operation.OwnedTargets
                            : refreshed.OwnedTargets,
                        UpdatedAtUtc = now,
                    };
                }).ToArray();
                return set with { Operations = operations, UpdatedAtUtc = now };
            }).ToArray();
            var refreshedDocument = context.Document with
            {
                ChangeSets = refreshedSets,
                UndoHistory = Array.Empty<ChangeSetHistoryEntry>(),
                RedoHistory = Array.Empty<ChangeSetHistoryEntry>(),
                UpdatedAtUtc = now,
            };
            ValidateStoredDocument(refreshedDocument, context.Scope.Game);
            _ = await store.WriteConditionalAsync(
                    context.Scope.Identity,
                    Definition,
                    refreshedDocument,
                    context.ETag,
                    cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            return AppendApplyRefreshWarning(
                result,
                "Output committed, but the named change-set source bindings could not be refreshed. Read the workspace again before another review.");
        }
    }

    private static ApplyResult AppendApplyRefreshWarning(ApplyResult result, string message)
    {
        return result with
        {
            Diagnostics = result.Diagnostics.Append(new ValidationDiagnostic(
                DiagnosticSeverity.Warning,
                message,
                Domain: "changeSets")
            {
                Code = "KM-CHANGE-SET-REFRESH-REQUIRED",
            }).ToArray(),
        };
    }

    private static bool EditSessionsMatchExactly(EditSession actual, EditSession expected)
    {
        return JsonSerializer.SerializeToUtf8Bytes(
                EditSessionBridgeMapper.ToDto(actual),
                SerializerOptions)
            .AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(
                EditSessionBridgeMapper.ToDto(expected),
                SerializerOptions));
    }

    private static string? ToBindingOutputMode(ChangePlanOutputModeDto? outputMode)
    {
        return outputMode switch
        {
            ChangePlanOutputModeDto.Standalone => "standalone",
            ChangePlanOutputModeDto.TrinityModManager => "trinityModManager",
            ChangePlanOutputModeDto.TrinityBypass => "trinityBypass",
            null => null,
            _ => throw Invalid("The requested output mode is invalid."),
        };
    }

    private static ChangePlanOutputModeDto? FromBindingOutputMode(string? outputMode)
    {
        return outputMode switch
        {
            "standalone" => ChangePlanOutputModeDto.Standalone,
            "trinityModManager" => ChangePlanOutputModeDto.TrinityModManager,
            "trinityBypass" => ChangePlanOutputModeDto.TrinityBypass,
            null => null,
            _ => throw Invalid("The edit-session authoring output mode is invalid."),
        };
    }

    public async Task<ExportChangeSetsResponse> ExportAsync(
        ExportChangeSetsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = ValidateScope(request.Scope);
        using var operationLease = await AcquireProjectLeasesAsync(
                [scope.ProjectId],
                cancellationToken)
            .ConfigureAwait(false);
        var expectedETag = NormalizeETag(request.ExpectedETag, allowNull: false)!;
        var stored = await RequireStoredAsync(scope.ProjectId, expectedETag, cancellationToken)
            .ConfigureAwait(false);
        ValidateStoredDocument(stored.Document!, scope.Game);
        if (request.ChangeSetIds is null
            || request.ChangeSetIds.Count is 0 or > ChangeSetContract.MaximumChangeSetCount
            || request.ChangeSetIds.Distinct(StringComparer.Ordinal).Count() != request.ChangeSetIds.Count)
        {
            throw Invalid("The portable export selection is invalid.");
        }

        foreach (var changeSetId in request.ChangeSetIds)
        {
            ValidateId(changeSetId, "portable export change set id");
        }

        var byId = stored.Document!.ChangeSets.ToDictionary(set => set.ChangeSetId, StringComparer.Ordinal);
        var selectedIds = request.ChangeSetIds.ToHashSet(StringComparer.Ordinal);
        var pendingDependencyIds = new Stack<string>(request.ChangeSetIds.Reverse());
        while (pendingDependencyIds.TryPop(out var selectedId))
        {
            if (!byId.TryGetValue(selectedId, out var selectedSet))
            {
                throw Invalid("A portable export change set or dependency does not exist.");
            }

            foreach (var dependencyId in selectedSet.DependencyIds)
            {
                if (selectedIds.Add(dependencyId))
                {
                    pendingDependencyIds.Push(dependencyId);
                }
            }
        }

        var selected = stored.Document.ChangeSets
            .Where(set => selectedIds.Contains(set.ChangeSetId))
            .ToArray();
        var portableSets = new List<PortableNamedChangeSetDto>(selected.Length);
        foreach (var set in selected)
        {
            var portableOperations = new List<PortableChangeSetOperationDto>(set.Operations.Count);
            foreach (var operation in set.Operations)
            {
                if (!TryCreatePortablePendingEditOperation(operation, out var portableOperation))
                {
                    return new ExportChangeSetsResponse(
                        Available: false,
                        PackageJson: null,
                        Diagnostics:
                        [
                            CreateDiagnostic(
                                ApiDiagnosticSeverity.Warning,
                                $"{set.Name}: {operation.PendingEdit.Summary} is not a reviewed, field-addressable numeric operation supported by the portable pending-edit adapter."),
                        ]);
                }

                portableOperations.Add(portableOperation);
            }

            portableSets.Add(new PortableNamedChangeSetDto(
                set.ChangeSetId,
                set.Name,
                set.Notes,
                set.Tags,
                set.DependencyIds.ToArray(),
                portableOperations));
        }

        var package = new PortableChangeSetPackageDto(
            ChangeSetContract.PortableSchemaVersion,
            scope.Game,
            portableSets);
        var packageJson = JsonSerializer.Serialize(package, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(packageJson) > MaximumPortablePackageBytes)
        {
            return new ExportChangeSetsResponse(
                Available: false,
                PackageJson: null,
                Diagnostics:
                [
                    CreateDiagnostic(
                        ApiDiagnosticSeverity.Warning,
                        "The selected portable change sets exceed the package size limit."),
                ]);
        }

        return new ExportChangeSetsResponse(
            Available: true,
            packageJson,
            Diagnostics: Array.Empty<ApiDiagnostic>());
    }

    private static bool TryCreatePortablePendingEditOperation(
        ChangeSetOperationDto operation,
        out PortableChangeSetOperationDto portableOperation)
    {
        portableOperation = null!;
        if (operation.Kind != ChangeSetOperationStorageKindDto.LegacyPendingEdit
            || operation.SourceBindingKind != ChangeSetSourceBindingKindDto.ReviewedPlan
            || !IsSha256(operation.SourceFingerprint)
            || operation.PendingEdit.Owner is not null)
        {
            return false;
        }

        PortablePendingEditOperationPayloadDto payload;
        try
        {
            payload = new PortablePendingEditOperationPayloadDto(
                operation.PendingEdit.Domain,
                operation.PendingEdit.Summary,
                operation.PendingEdit.Sources.Select(source => source with
                {
                    RelativePath = new RelativeOutputPath(source.RelativePath).Value,
                }).ToArray(),
                operation.PendingEdit.RecordId ?? string.Empty,
                operation.PendingEdit.Field ?? string.Empty,
                operation.PendingEdit.NewValue ?? string.Empty,
                NormalizeOwnedTargets(operation.OwnedTargets, requireNonEmpty: true)
                    .Select(target => new RelativeOutputPath(target))
                    .OrderBy(target => target.CanonicalKey, StringComparer.Ordinal)
                    .Select(target => target.Value)
                    .ToArray());
            ValidatePortablePendingEditPayload(payload);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            ChangeSetValidationException)
        {
            return false;
        }

        var payloadJson = JsonSerializer.Serialize(payload, SerializerOptions);
        if (Encoding.UTF8.GetByteCount(payloadJson) > MaximumPortablePackageBytes)
        {
            return false;
        }

        portableOperation = new PortableChangeSetOperationDto(
            PortablePendingEditAdapterId,
            PortablePendingEditAdapterSchemaVersion,
            operation.SourceFingerprint!.ToLowerInvariant(),
            payloadJson);
        return true;
    }

    private static ChangeSetOperationDto ImportPortablePendingEditOperation(
        PortableChangeSetOperationDto portableOperation,
        string changeSetId,
        DateTimeOffset now,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner)
    {
        if (portableOperation is null
            || !string.Equals(
                portableOperation.AdapterId,
                PortablePendingEditAdapterId,
                StringComparison.Ordinal)
            || portableOperation.AdapterSchemaVersion != PortablePendingEditAdapterSchemaVersion
            || !IsSha256(portableOperation.SourceFingerprint)
            || string.IsNullOrEmpty(portableOperation.PayloadJson)
            || Encoding.UTF8.GetByteCount(portableOperation.PayloadJson)
                > MaximumPortablePackageBytes)
        {
            throw Invalid("A portable change-set operation adapter is unsupported or invalid.");
        }

        PortablePendingEditOperationPayloadDto payload;
        try
        {
            payload = JsonSerializer.Deserialize<PortablePendingEditOperationPayloadDto>(
                    portableOperation.PayloadJson,
                    SerializerOptions)
                ?? throw Invalid("A portable pending-edit payload is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new ChangeSetValidationException(
                "A portable pending-edit payload is invalid.",
                exception);
        }

        ValidatePortablePendingEditPayload(payload);
        var operationId = CreateId();
        var pendingEdit = new PendingEditDto(
            payload.Domain,
            payload.Summary,
            payload.Sources.ToArray(),
            payload.RecordId,
            payload.Field,
            payload.NewValue,
            Owner: null,
            new PendingEditAssociationDto(
                ChangeSetContract.AssociationVersion,
                changeSetId,
                operationId));
        var edit = EditSessionBridgeMapper.ToPendingEditCore(pendingEdit);
        var currentBinding = CreateOperationBinding(
            edit,
            planner,
            outputMode: null,
            satisfiedOwnedTargets: payload.OwnedTargets);
        if (currentBinding.Kind != ChangeSetSourceBindingKindDto.ReviewedPlan
            || !OwnedTargetsMatch(currentBinding.OwnedTargets, payload.OwnedTargets))
        {
            throw Invalid(
                "A portable pending edit is not supported by the current game workflow or targets different output files.");
        }

        return new ChangeSetOperationDto(
            operationId,
            ChangeSetOperationStorageKindDto.LegacyPendingEdit,
            pendingEdit,
            ChangeSetSourceBindingKindDto.ReviewedPlan,
            portableOperation.SourceFingerprint.ToLowerInvariant(),
            currentBinding.OwnedTargets,
            now,
            now);
    }

    private static void ValidatePortablePendingEditPayload(
        PortablePendingEditOperationPayloadDto payload)
    {
        if (payload is null
            || payload.Sources is null
            || payload.OwnedTargets is null
            || payload.Sources.Count is 0 or > MaximumSourceCount
            || payload.Sources.Any(source => source is null)
            || payload.OwnedTargets.Count is 0 or > MaximumOwnedTargetCount
            || !PortablePendingEditDomains.Contains(payload.Domain))
        {
            throw Invalid("A portable pending-edit payload is outside the supported boundary.");
        }

        ValidateDisplayText(payload.Summary, "portable pending edit summary", MaximumSummaryLength);
        ValidateDisplayText(payload.RecordId, "portable pending edit record id", MaximumRecordIdLength);
        ValidateDisplayText(payload.Field, "portable pending edit field", MaximumFieldLength);
        if (!IsCanonicalPortableNumericValue(payload.NewValue))
        {
            throw Invalid("A portable pending-edit value must be a canonical bounded integer.");
        }

        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in payload.Sources)
        {
            if (!Enum.IsDefined(source.Layer)
                || source.Layer is not (FileLayerDto.Base or FileLayerDto.Layered))
            {
                throw Invalid("A portable pending-edit source layer is unsupported.");
            }

            var path = RequireCanonicalRelativeOutputPath(
                source.RelativePath,
                "portable pending edit source");
            if (!sourceKeys.Add($"{source.Layer}\0{path.CanonicalKey}"))
            {
                throw Invalid("A portable pending-edit source is duplicated.");
            }
        }

        var normalizedOwnedTargets = NormalizeOwnedTargets(
            payload.OwnedTargets,
            requireNonEmpty: true);
        if (!normalizedOwnedTargets.SequenceEqual(payload.OwnedTargets, StringComparer.Ordinal))
        {
            throw Invalid("A portable owned output target is not canonical.");
        }
    }

    private static bool IsCanonicalPortableNumericValue(string? value)
    {
        return value is { Length: > 0 and <= MaximumPortableNumericValueLength }
            && long.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed)
            && string.Equals(
                parsed.ToString(CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal);
    }

    public async Task<ImportChangeSetsResponse> ImportAsync(
        ImportChangeSetsRequest request,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = ValidateScope(request.Scope);
        using var operationLease = await AcquireProjectLeasesAsync(
                [scope.ProjectId],
                cancellationToken)
            .ConfigureAwait(false);
        var expectedETag = NormalizeETag(request.ExpectedETag, allowNull: true);
        var session = request.Session is null ? null : EditSessionBridgeMapper.ToCore(request.Session);
        if (session is not null)
        {
            ValidatePendingEdits(session.PendingEdits);
        }

        if (request.EnableImported)
        {
            throw Invalid("Imported change sets must remain disabled until they are reviewed locally.");
        }

        if (string.IsNullOrEmpty(request.PackageJson)
            || Encoding.UTF8.GetByteCount(request.PackageJson) > MaximumPortablePackageBytes)
        {
            throw Invalid("The portable change-set package is empty or too large.");
        }

        PortableChangeSetPackageDto package;
        try
        {
            package = JsonSerializer.Deserialize<PortableChangeSetPackageDto>(
                    request.PackageJson,
                    SerializerOptions)
                ?? throw Invalid("The portable change-set package is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new ChangeSetValidationException(
                "The portable change-set package is invalid.",
                exception);
        }

        if (package.SchemaVersion != ChangeSetContract.PortableSchemaVersion
            || package.Game != scope.Game
            || package.ChangeSets is null
            || package.ChangeSets.Count is 0 or > ChangeSetContract.MaximumChangeSetCount
            || package.ChangeSets.Any(set =>
                set is null
                || set.Tags is null
                || set.DependencyIds is null
                || set.Operations is null
                || set.Operations.Count > ChangeSetContract.MaximumOperationsPerChangeSet
                || set.Operations.Any(operation => operation is null))
            || package.ChangeSets.Sum(set => set.Operations.Count)
                > ChangeSetContract.MaximumOperationCount
            || package.ChangeSets.Select(set => set.PortableId)
                .Distinct(StringComparer.Ordinal).Count() != package.ChangeSets.Count)
        {
            throw Invalid(
                "The portable change-set package schema, game, or operation boundary is unsupported.");
        }

        var stored = await ReadStoredAsync(scope.ProjectId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(stored.ETag, expectedETag, StringComparison.Ordinal))
        {
            throw new WorkspaceDocumentConflictException(expectedETag, stored.ETag);
        }

        var document = stored.Document
            ?? CreateEmptyStoredDocument(scope.Game, DateTimeOffset.UtcNow);
        ValidateStoredDocument(document, scope.Game);
        var now = DateTimeOffset.UtcNow;
        foreach (var portableSet in package.ChangeSets)
        {
            ValidateId(portableSet.PortableId, "portable change set id");
            ValidateDisplayText(portableSet.Name, "change set name", MaximumNameLength);
            ValidateOptionalText(portableSet.Notes, "change set notes", MaximumNotesLength);
            ValidateTags(portableSet.Tags);
            ValidateIdList(
                portableSet.DependencyIds,
                ChangeSetContract.MaximumDependencyCount,
                "portable dependencies");
        }

        var portableIds = package.ChangeSets
            .Select(set => set.PortableId)
            .ToHashSet(StringComparer.Ordinal);
        if (package.ChangeSets.Any(set => set.DependencyIds.Any(id => !portableIds.Contains(id))))
        {
            throw Invalid("A portable change-set dependency is missing from the package.");
        }

        var importedIdMap = package.ChangeSets.ToDictionary(
            set => set.PortableId,
            _ => CreateId(),
            StringComparer.Ordinal);
        var imported = package.ChangeSets.Select(set =>
        {
            var importedSetId = importedIdMap[set.PortableId];
            var importedOperations = set.Operations
                .Select(operation => ImportPortablePendingEditOperation(
                    operation,
                    importedSetId,
                    now,
                    planner))
                .ToArray();
            return new NamedChangeSetDto(
                importedSetId,
                set.Name,
                Enabled: false,
                Archived: false,
                set.Notes,
                set.Tags,
                set.DependencyIds
                    .Where(importedIdMap.ContainsKey)
                    .Select(id => importedIdMap[id])
                    .ToArray(),
                importedOperations,
                now,
                now);
        }).ToArray();
        if (document.ChangeSets.Count + imported.Length > ChangeSetContract.MaximumChangeSetCount)
        {
            throw Invalid("The imported change sets exceed the project limit.");
        }

        var state = GetState(document) with
        {
            ChangeSets = document.ChangeSets.Concat(imported).ToArray(),
        };
        var updated = TrimHistoryToBudget(PushUndo(document, state, "Import change sets", now));
        ValidateStoredDocument(updated, scope.Game);
        var write = await store.WriteConditionalAsync(
                scope.Identity,
                Definition,
                updated,
                stored.ETag,
                cancellationToken)
            .ConfigureAwait(false);
        var effective = await MaterializeCoreAsync(
                scope,
                updated,
                write.ETag,
                session,
                buildVariantId: null,
                planner,
                cancellationToken)
            .ConfigureAwait(false);
        return new ImportChangeSetsResponse(ToSnapshot(updated, write.ETag, effective));
    }

    internal async Task<StoredChangeSetReadResult> ReadStoredForRelocationAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var stored = await ReadStoredAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (stored.Document is not null)
        {
            ValidateStoredDocument(stored.Document, stored.Document.Game);
        }

        return stored;
    }

    internal async Task<string> WriteStoredForRelocationAsync(
        string projectId,
        StoredChangeSetWorkspaceDocument document,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        var identity = GetIdentity(projectId);
        ValidateStoredDocument(document, document.Game);
        var result = await store.WriteConditionalAsync(
                identity,
                Definition,
                document,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
        return result.ETag;
    }

    internal async Task<bool> DeleteStoredForRelocationAsync(
        string projectId,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        ValidateETag(expectedETag, allowNull: false);
        var result = await store.DeleteConditionalAsync(
                GetIdentity(projectId),
                Definition.DocumentId,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Deleted;
    }

    private static StoredChangeSetWorkspaceDocument ApplyMutation(
        StoredChangeSetWorkspaceDocument document,
        ChangeSetWorkspaceMutationDto mutation,
        DateTimeOffset now)
    {
        var state = GetState(document);
        if (mutation.Kind == ChangeSetMutationKindDto.Undo)
        {
            return Undo(document, now);
        }

        if (mutation.Kind == ChangeSetMutationKindDto.Redo)
        {
            return Redo(document, now);
        }

        var nextState = mutation.Kind switch
        {
            ChangeSetMutationKindDto.CreateSet => CreateSet(state, mutation, now),
            ChangeSetMutationKindDto.UpdateSet => UpdateSet(state, mutation, now),
            ChangeSetMutationKindDto.DeleteSet => DeleteSet(state, mutation),
            ChangeSetMutationKindDto.DuplicateSet => DuplicateSet(state, mutation, now),
            ChangeSetMutationKindDto.ReorderSets => ReorderSets(state, mutation),
            ChangeSetMutationKindDto.ReorderOperations => ReorderOperations(state, mutation, now),
            ChangeSetMutationKindDto.RemoveOperation => RemoveOperation(state, mutation, now),
            ChangeSetMutationKindDto.SetActiveSet => SetActiveSet(state, mutation),
            ChangeSetMutationKindDto.CreateVariant => CreateVariant(state, mutation, now),
            ChangeSetMutationKindDto.UpdateVariant => UpdateVariant(state, mutation, now),
            ChangeSetMutationKindDto.DeleteVariant => DeleteVariant(state, mutation),
            ChangeSetMutationKindDto.SetActiveVariant => SetActiveVariant(state, mutation),
            _ => throw Invalid("The change-set mutation is unsupported."),
        };
        return PushUndo(document, nextState, GetMutationLabel(mutation.Kind), now);
    }

    private async Task<ChangeSetMaterializationDto> MaterializeCoreAsync(
        ValidatedScope scope,
        StoredChangeSetWorkspaceDocument document,
        string? etag,
        EditSession? inputSession,
        string? buildVariantId,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        CancellationToken cancellationToken)
    {
        ValidateStoredDocument(document, scope.Game);
        if (inputSession is not null)
        {
            ValidatePendingEdits(inputSession.PendingEdits);
        }

        var diagnostics = new List<ApiDiagnostic>();
        var conflicts = new List<ChangeSetConflictDto>();
        var summaries = new List<ChangeSetOperationSummaryDto>();
        var variant = ResolveVariant(document, buildVariantId);
        var outputMode = variant?.OutputMode;
        var explicitOutputProfileId = variant?.OutputProfileId;
        var outputProfileId = explicitOutputProfileId;
        string? personalStateETag = null;
        string? outputRootFingerprint = null;
        if (string.IsNullOrWhiteSpace(scope.Paths.OutputRootPath))
        {
            outputRootFingerprint = CreateUnsetOutputRootFingerprint();
        }
        else if (TryNormalizePrivatePath(scope.Paths.OutputRootPath, out var normalizedOutputRoot))
        {
            outputRootFingerprint = CreatePrivatePathFingerprint(normalizedOutputRoot);
        }
        else
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "A valid output root is required to bind a materialized change-set session."));
        }
        var personal = await workspacePersonalStateService
            .ReadProjectAsync(scope.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var resolvedOutputProfileId = explicitOutputProfileId
            ?? personal.Document?.ActiveOutputProfileId;
        if (resolvedOutputProfileId is not null)
        {
            outputProfileId = resolvedOutputProfileId;
            var profile = personal.Document?.OutputProfiles.FirstOrDefault(candidate => string.Equals(
                candidate.ProfileId,
                resolvedOutputProfileId,
                StringComparison.Ordinal));
            if (profile is null
                || personal.ETag is null
                || !string.Equals(
                    personal.Document?.ActiveOutputProfileId,
                    resolvedOutputProfileId,
                    StringComparison.Ordinal)
                || !PathsEqual(profile.OutputRootPath, scope.Paths.OutputRootPath))
            {
                diagnostics.Add(CreateDiagnostic(
                    ApiDiagnosticSeverity.Error,
                    "Change-set materialization requires its exact output profile and output root to be active."));
            }
            else if (variant?.OutputMode is not null
                && profile.OutputMode is not null
                && variant.OutputMode != profile.OutputMode)
            {
                diagnostics.Add(CreateDiagnostic(
                    ApiDiagnosticSeverity.Error,
                    "The build variant output mode does not match its selected output profile."));
            }
            else
            {
                personalStateETag = personal.ETag;
                outputRootFingerprint = CreatePrivatePathFingerprint(profile.OutputRootPath);
                outputMode ??= profile.OutputMode;
            }
        }
        if ((scope.Game == ProjectGameDto.Sword || scope.Game == ProjectGameDto.Shield)
            && (outputMode == ChangePlanOutputModeDto.TrinityModManager
                || outputMode == ChangePlanOutputModeDto.TrinityBypass))
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "Sword and Shield change sets cannot materialize in a Trinity output mode."));
        }

        var selectedSets = SelectSets(document, variant, conflicts);
        DetectDependencyProblems(selectedSets, document.ChangeSets, conflicts);
        if (conflicts.Count > ChangeSetContract.MaximumOperationCount)
        {
            conflicts.RemoveRange(
                ChangeSetContract.MaximumOperationCount,
                conflicts.Count - ChangeSetContract.MaximumOperationCount);
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "Change-set dependency conflicts exceed the reporting limit."));
        }

        var selectedIds = selectedSets.Select(set => set.ChangeSetId).ToArray();
        var selectedOperations = selectedSets
            .SelectMany(set => set.Operations.Select(operation => (Set: set, Operation: operation)))
            .ToArray();
        var operationStates = new Dictionary<string, ChangeSetOperationMaterializationStateDto>(
            StringComparer.Ordinal);
        var currentOwnedTargets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var currentBindingFingerprints = new List<string>();
        foreach (var (set, operation) in selectedOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = ChangeSetOperationMaterializationStateDto.Fresh;
            if (operation.SourceBindingKind == ChangeSetSourceBindingKindDto.LegacyUnsupported
                || operation.SourceFingerprint is null)
            {
                state = ChangeSetOperationMaterializationStateDto.LegacyUnsupported;
                diagnostics.Add(CreateDiagnostic(
                    ApiDiagnosticSeverity.Error,
                    $"{set.Name}: {operation.PendingEdit.Summary} cannot be rebuilt safely because its workflow does not expose a complete reviewed source fingerprint."));
            }
            else
            {
                var current = CreateOperationBinding(
                    EditSessionBridgeMapper.ToPendingEditCore(operation.PendingEdit),
                    planner,
                    outputMode: null,
                    satisfiedOwnedTargets: operation.OwnedTargets);
                if (current.Kind != ChangeSetSourceBindingKindDto.ReviewedPlan
                    || !string.Equals(
                        current.Fingerprint,
                        operation.SourceFingerprint,
                        StringComparison.Ordinal))
                {
                    state = ChangeSetOperationMaterializationStateDto.Stale;
                    diagnostics.Add(CreateDiagnostic(
                        ApiDiagnosticSeverity.Error,
                        $"{set.Name}: {operation.PendingEdit.Summary} changed at its source boundary and must be staged again."));
                }
                else
                {
                    currentBindingFingerprints.Add(current.Fingerprint!);
                    var selectedModeBinding = outputMode is null
                        ? current
                        : CreateOperationBinding(
                            EditSessionBridgeMapper.ToPendingEditCore(operation.PendingEdit),
                            planner,
                            outputMode,
                            operation.OwnedTargets);
                    if (selectedModeBinding.Kind != ChangeSetSourceBindingKindDto.ReviewedPlan)
                    {
                        state = ChangeSetOperationMaterializationStateDto.LegacyUnsupported;
                        diagnostics.Add(CreateDiagnostic(
                            ApiDiagnosticSeverity.Error,
                            $"{set.Name}: {operation.PendingEdit.Summary} is not supported by the selected output mode."));
                    }
                    else
                    {
                        currentOwnedTargets[operation.OperationId] = selectedModeBinding.OwnedTargets;
                    }
                }
            }

            operationStates[operation.OperationId] = state;
        }

        var sessionLocalEdits = inputSession?.PendingEdits
            .Where(edit => edit.Association is null)
            .ToArray() ?? Array.Empty<PendingEdit>();
        if (selectedOperations.Length + sessionLocalEdits.Length
            > ChangeSetContract.MaximumOperationCount)
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "Named and session-local edits together exceed the effective operation limit."));
        }

        var sessionLocalBindings = sessionLocalEdits
            .Select(edit => CreateOperationBinding(edit, planner, outputMode))
            .ToArray();
        var compositionTargets = selectedOperations.Select(pair => new ChangeSetCompositionTarget(
                pair.Set.ChangeSetId,
                pair.Operation.OperationId,
                pair.Operation.PendingEdit.Domain,
                pair.Operation.PendingEdit.RecordId,
                pair.Operation.PendingEdit.Field,
                currentOwnedTargets.GetValueOrDefault(
                    pair.Operation.OperationId,
                    pair.Operation.OwnedTargets)))
            .Concat(sessionLocalEdits.Select((edit, index) => new ChangeSetCompositionTarget(
                "session-local",
                $"session-{index}",
                edit.Domain,
                edit.RecordId,
                edit.Field,
                OwnedTargets: sessionLocalBindings[index].OwnedTargets,
                IsSessionLocal: true)))
            .ToArray();
        foreach (var conflict in ChangeSetConflictDetector.Detect(compositionTargets))
        {
            if (conflicts.Count == ChangeSetContract.MaximumOperationCount)
            {
                diagnostics.Add(CreateDiagnostic(
                    ApiDiagnosticSeverity.Error,
                    "Change-set composition conflicts exceed the reporting limit."));
                break;
            }

            var kind = conflict.Kind == ChangeSetCompositionConflictKind.SemanticTarget
                ? conflict.First.IsSessionLocal || conflict.Second.IsSessionLocal
                    ? ChangeSetConflictKindDto.SessionTarget
                    : ChangeSetConflictKindDto.SemanticTarget
                : ChangeSetConflictKindDto.OwnedOutput;
            conflicts.Add(new ChangeSetConflictDto(
                kind,
                kind == ChangeSetConflictKindDto.SessionTarget
                    ? "A local pending edit targets the same value as an enabled change set."
                    : kind == ChangeSetConflictKindDto.SemanticTarget
                        ? "Two enabled change sets target the same semantic value."
                        : "Two opaque enabled operations own the same reviewed output target.",
                new[] { conflict.First.ChangeSetId, conflict.Second.ChangeSetId }
                    .Where(id => id != "session-local")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                new[] { conflict.First.OperationId, conflict.Second.OperationId },
                conflict.Target));
            if (!conflict.First.IsSessionLocal)
            {
                operationStates[conflict.First.OperationId] = ChangeSetOperationMaterializationStateDto.Conflict;
            }

            if (!conflict.Second.IsSessionLocal)
            {
                operationStates[conflict.Second.OperationId] = ChangeSetOperationMaterializationStateDto.Conflict;
            }
        }

        foreach (var (set, operation) in selectedOperations)
        {
            summaries.Add(CreateOperationSummary(
                operation,
                set.ChangeSetId,
                set.Name,
                operationStates.GetValueOrDefault(
                    operation.OperationId,
                    ChangeSetOperationMaterializationStateDto.Fresh)));
        }

        var availableSessionSummaryCount = Math.Max(
            0,
            ChangeSetContract.MaximumOperationCount - selectedOperations.Length);
        summaries.AddRange(sessionLocalEdits.Take(availableSessionSummaryCount)
            .Select((edit, index) => new ChangeSetOperationSummaryDto(
            $"session-{index}",
            ChangeSetId: null,
            ChangeSetName: null,
            Title: SanitizeDisplay(edit.Summary, "Local pending edit"),
            Target: CreateDisplayTarget(edit),
            Description: "Current session edit not assigned to a named change set.",
            ChangeSetOperationMaterializationStateDto.SessionLocal)));

        if (conflicts.Count > 0)
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "The selected change sets cannot be composed until their conflicts are resolved."));
        }

        if (selectedOperations.Length > 0
            && sessionLocalEdits.Select((edit, index) => (edit, index)).Any(pair =>
                (pair.edit.RecordId is null || pair.edit.Field is null)
                && sessionLocalBindings[pair.index].Kind
                    == ChangeSetSourceBindingKindDto.LegacyUnsupported))
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "An opaque local pending edit has no complete reviewed ownership binding and cannot be composed with enabled change sets."));
        }
        else if (selectedOperations.Any(pair =>
                     pair.Operation.PendingEdit.RecordId is null
                     || pair.Operation.PendingEdit.Field is null)
            && sessionLocalBindings.Any(binding =>
                binding.Kind == ChangeSetSourceBindingKindDto.LegacyUnsupported))
        {
            diagnostics.Add(CreateDiagnostic(
                ApiDiagnosticSeverity.Error,
                "A local pending edit without reviewed output ownership cannot be composed with an opaque enabled operation."));
        }

        var workspaceFingerprint = CreateWorkspaceFingerprint(
            document,
            selectedIds,
            variant,
            outputProfileId,
            outputMode,
            outputRootFingerprint,
            sessionLocalEdits);
        var sourceRevisionFingerprint = CreateSourceRevisionFingerprint(
            currentBindingFingerprints,
            workspaceFingerprint);
        var hasBlockedOperation = operationStates.Values.Any(state => state is
            ChangeSetOperationMaterializationStateDto.Stale
            or ChangeSetOperationMaterializationStateDto.LegacyUnsupported
            or ChangeSetOperationMaterializationStateDto.Conflict)
            || diagnostics.Any(diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        if (etag is null || hasBlockedOperation || conflicts.Count > 0)
        {
            return new ChangeSetMaterializationDto(
                CanMaterialize: false,
                workspaceFingerprint,
                sourceRevisionFingerprint,
                selectedIds,
                outputProfileId,
                outputMode,
                Session: null,
                ChangePlan: null,
                summaries,
                conflicts,
                diagnostics);
        }

        var pendingEdits = selectedOperations
            .Select(pair => EditSessionBridgeMapper.ToPendingEditCore(pair.Operation.PendingEdit))
            .Concat(sessionLocalEdits)
            .ToArray();
        var effectiveSession = new EditSession(
            inputSession?.Id ?? EditSessionId.New(),
            inputSession?.CreatedAt ?? DateTimeOffset.UtcNow,
            pendingEdits,
            new EditSessionAuthoringBinding(
                EditSessionAuthoringBinding.CurrentVersion,
                scope.ProjectId,
                etag,
                workspaceFingerprint,
                selectedIds,
                outputProfileId,
                outputRootFingerprint!,
                personalStateETag,
                outputMode switch
                {
                    ChangePlanOutputModeDto.Standalone => "standalone",
                    ChangePlanOutputModeDto.TrinityModManager => "trinityModManager",
                    ChangePlanOutputModeDto.TrinityBypass => "trinityBypass",
                    null => null,
                    _ => throw Invalid("The selected output mode is invalid."),
                }));
        var plan = planner(effectiveSession, outputMode);
        diagnostics.AddRange(plan.Diagnostics.Select(ProjectBridgeMapper.ToDto));
        sourceRevisionFingerprint = CreatePlanSourceRevisionFingerprint(plan, sourceRevisionFingerprint);
        var canMaterialize = plan.CanApply;
        return new ChangeSetMaterializationDto(
            canMaterialize,
            workspaceFingerprint,
            sourceRevisionFingerprint,
            selectedIds,
            outputProfileId,
            outputMode,
            EditSessionBridgeMapper.ToDto(effectiveSession),
            EditSessionBridgeMapper.ToDto(plan),
            summaries,
            conflicts,
            diagnostics);
    }

    private static ChangeSetWorkspaceState ResolveStateForMutation(
        StoredChangeSetWorkspaceDocument document)
    {
        return GetState(document);
    }

    private static ChangeSetWorkspaceState CreateSet(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation,
        DateTimeOffset now)
    {
        EnsureOnly(mutation, allowName: true);
        ValidateDisplayText(mutation.Name, "change set name", MaximumNameLength);
        if (state.ChangeSets.Count >= ChangeSetContract.MaximumChangeSetCount)
        {
            throw Invalid("The project has reached the change-set limit.");
        }

        var id = CreateId();
        var created = new NamedChangeSetDto(
            id,
            mutation.Name!,
            Enabled: true,
            Archived: false,
            Notes: null,
            Tags: Array.Empty<string>(),
            DependencyIds: Array.Empty<string>(),
            Operations: Array.Empty<ChangeSetOperationDto>(),
            now,
            now);
        return state with
        {
            ChangeSets = state.ChangeSets.Append(created).ToArray(),
            ActiveChangeSetId = id,
        };
    }

    private static ChangeSetWorkspaceState UpdateSet(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation,
        DateTimeOffset now)
    {
        EnsureOnly(mutation, allowChangeSetId: true, allowMetadata: true);
        var index = FindSetIndex(state.ChangeSets, mutation.ChangeSetId);
        var metadata = mutation.Metadata ?? throw Invalid("Change-set metadata is required.");
        ValidateMetadata(metadata);
        var current = state.ChangeSets[index];
        if (metadata.DependencyIds.Contains(current.ChangeSetId, StringComparer.Ordinal))
        {
            throw Invalid("A change set cannot depend on itself.");
        }

        var knownIds = state.ChangeSets.Select(set => set.ChangeSetId).ToHashSet(StringComparer.Ordinal);
        if (metadata.DependencyIds.Any(id => !knownIds.Contains(id)))
        {
            throw Invalid("A change-set dependency does not exist.");
        }

        var sets = state.ChangeSets.ToArray();
        sets[index] = current with
        {
            Name = metadata.Name,
            Enabled = metadata.Enabled,
            Archived = metadata.Archived,
            Notes = metadata.Notes,
            Tags = metadata.Tags,
            DependencyIds = metadata.DependencyIds,
            UpdatedAtUtc = now,
        };
        return state with
        {
            ChangeSets = sets,
            ActiveChangeSetId = metadata.Archived
                && string.Equals(state.ActiveChangeSetId, current.ChangeSetId, StringComparison.Ordinal)
                    ? null
                    : state.ActiveChangeSetId,
        };
    }

    private static ChangeSetWorkspaceState DeleteSet(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation)
    {
        EnsureOnly(mutation, allowChangeSetId: true);
        var index = FindSetIndex(state.ChangeSets, mutation.ChangeSetId);
        var id = state.ChangeSets[index].ChangeSetId;
        if (state.ChangeSets.Any(set => set.DependencyIds.Contains(id, StringComparer.Ordinal))
            || state.BuildVariants.Any(variant => variant.ChangeSetIds.Contains(id, StringComparer.Ordinal)))
        {
            throw Invalid("Remove this change set from dependencies and build variants before deleting it.");
        }

        return state with
        {
            ChangeSets = state.ChangeSets.Where((_, itemIndex) => itemIndex != index).ToArray(),
            ActiveChangeSetId = string.Equals(state.ActiveChangeSetId, id, StringComparison.Ordinal)
                ? null
                : state.ActiveChangeSetId,
        };
    }

    private static ChangeSetWorkspaceState DuplicateSet(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation,
        DateTimeOffset now)
    {
        EnsureOnly(mutation, allowChangeSetId: true, allowName: true);
        var source = state.ChangeSets[FindSetIndex(state.ChangeSets, mutation.ChangeSetId)];
        ValidateDisplayText(mutation.Name, "change set name", MaximumNameLength);
        if (state.ChangeSets.Count >= ChangeSetContract.MaximumChangeSetCount)
        {
            throw Invalid("The project has reached the change-set limit.");
        }

        var id = CreateId();
        var operations = source.Operations.Select(operation =>
        {
            var operationId = CreateId();
            return operation with
            {
                OperationId = operationId,
                PendingEdit = operation.PendingEdit with
                {
                    Association = new PendingEditAssociationDto(
                        ChangeSetContract.AssociationVersion,
                        id,
                        operationId),
                },
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
        }).ToArray();
        var duplicate = source with
        {
            ChangeSetId = id,
            Name = mutation.Name!,
            Enabled = false,
            Archived = false,
            Operations = operations,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        return state with
        {
            ChangeSets = state.ChangeSets.Append(duplicate).ToArray(),
            ActiveChangeSetId = id,
        };
    }

    private static ChangeSetWorkspaceState ReorderSets(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation)
    {
        EnsureOnly(mutation, allowOrderedIds: true);
        return state with
        {
            ChangeSets = ReorderExact(
                state.ChangeSets,
                mutation.OrderedIds,
                set => set.ChangeSetId,
                "change sets"),
        };
    }

    private static ChangeSetWorkspaceState ReorderOperations(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation,
        DateTimeOffset now)
    {
        EnsureOnly(mutation, allowChangeSetId: true, allowOrderedIds: true);
        var index = FindSetIndex(state.ChangeSets, mutation.ChangeSetId);
        var sets = state.ChangeSets.ToArray();
        sets[index] = sets[index] with
        {
            Operations = ReorderExact(
                sets[index].Operations,
                mutation.OrderedIds,
                operation => operation.OperationId,
                "change-set operations"),
            UpdatedAtUtc = now,
        };
        return state with { ChangeSets = sets };
    }

    private static ChangeSetWorkspaceState RemoveOperation(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation,
        DateTimeOffset now)
    {
        EnsureOnly(
            mutation,
            allowChangeSetId: true,
            allowOperationId: true);
        var index = FindSetIndex(state.ChangeSets, mutation.ChangeSetId);
        ValidateId(mutation.OperationId, "operation id");
        var set = state.ChangeSets[index];
        if (!set.Operations.Any(operation => string.Equals(
                operation.OperationId,
                mutation.OperationId,
                StringComparison.Ordinal)))
        {
            throw Invalid("The change-set operation does not exist.");
        }

        var sets = state.ChangeSets.ToArray();
        sets[index] = set with
        {
            Operations = set.Operations.Where(operation => !string.Equals(
                operation.OperationId,
                mutation.OperationId,
                StringComparison.Ordinal)).ToArray(),
            UpdatedAtUtc = now,
        };
        return state with { ChangeSets = sets };
    }

    private static ChangeSetWorkspaceState SetActiveSet(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation)
    {
        EnsureOnly(mutation, allowChangeSetId: true);
        if (mutation.ChangeSetId is not null)
        {
            var set = state.ChangeSets[FindSetIndex(state.ChangeSets, mutation.ChangeSetId)];
            if (set.Archived)
            {
                throw Invalid("An archived change set cannot be the active staging target.");
            }
        }

        return state with { ActiveChangeSetId = mutation.ChangeSetId };
    }

    private static ChangeSetWorkspaceState CreateVariant(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation,
        DateTimeOffset now)
    {
        EnsureOnly(mutation, allowVariant: true);
        var variant = mutation.Variant ?? throw Invalid("A build variant is required.");
        ValidateVariant(variant, state.ChangeSets);
        if (state.BuildVariants.Count >= ChangeSetContract.MaximumBuildVariantCount
            || state.BuildVariants.Any(item => string.Equals(
                item.VariantId,
                variant.VariantId,
                StringComparison.Ordinal)))
        {
            throw Invalid("The build variant id is duplicated or the variant limit was reached.");
        }

        variant = variant with { CreatedAtUtc = now, UpdatedAtUtc = now };
        return state with
        {
            BuildVariants = state.BuildVariants.Append(variant).ToArray(),
            ActiveBuildVariantId = variant.VariantId,
        };
    }

    private static ChangeSetWorkspaceState UpdateVariant(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation,
        DateTimeOffset now)
    {
        EnsureOnly(mutation, allowVariant: true);
        var variant = mutation.Variant ?? throw Invalid("A build variant is required.");
        ValidateVariant(variant, state.ChangeSets);
        var index = FindVariantIndex(state.BuildVariants, variant.VariantId);
        var variants = state.BuildVariants.ToArray();
        variants[index] = variant with
        {
            CreatedAtUtc = variants[index].CreatedAtUtc,
            UpdatedAtUtc = now,
        };
        return state with { BuildVariants = variants };
    }

    private static ChangeSetWorkspaceState DeleteVariant(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation)
    {
        EnsureOnly(mutation, allowVariantId: true);
        var index = FindVariantIndex(state.BuildVariants, mutation.VariantId);
        var id = state.BuildVariants[index].VariantId;
        return state with
        {
            BuildVariants = state.BuildVariants.Where((_, itemIndex) => itemIndex != index).ToArray(),
            ActiveBuildVariantId = string.Equals(state.ActiveBuildVariantId, id, StringComparison.Ordinal)
                ? null
                : state.ActiveBuildVariantId,
        };
    }

    private static ChangeSetWorkspaceState SetActiveVariant(
        ChangeSetWorkspaceState state,
        ChangeSetWorkspaceMutationDto mutation)
    {
        EnsureOnly(mutation, allowVariantId: true);
        if (mutation.VariantId is not null)
        {
            _ = FindVariantIndex(state.BuildVariants, mutation.VariantId);
        }

        return state with { ActiveBuildVariantId = mutation.VariantId };
    }

    private static StoredChangeSetWorkspaceDocument Undo(
        StoredChangeSetWorkspaceDocument document,
        DateTimeOffset now)
    {
        if (document.UndoHistory.Count == 0)
        {
            throw Invalid("There is no change-set authoring action to undo.");
        }

        var entry = document.UndoHistory[^1];
        var redo = document.RedoHistory.Append(new ChangeSetHistoryEntry(
            CreateId(),
            entry.Label,
            GetState(document),
            now)).TakeLast(ChangeSetContract.MaximumHistoryCount).ToArray();
        return new StoredChangeSetWorkspaceDocument(
            document.SchemaVersion,
            document.Game,
            entry.State.ChangeSets,
            entry.State.ActiveChangeSetId,
            entry.State.BuildVariants,
            entry.State.ActiveBuildVariantId,
            document.UndoHistory.Take(document.UndoHistory.Count - 1).ToArray(),
            redo,
            now);
    }

    private static StoredChangeSetWorkspaceDocument Redo(
        StoredChangeSetWorkspaceDocument document,
        DateTimeOffset now)
    {
        if (document.RedoHistory.Count == 0)
        {
            throw Invalid("There is no change-set authoring action to redo.");
        }

        var entry = document.RedoHistory[^1];
        var undo = document.UndoHistory.Append(new ChangeSetHistoryEntry(
            CreateId(),
            entry.Label,
            GetState(document),
            now)).TakeLast(ChangeSetContract.MaximumHistoryCount).ToArray();
        return new StoredChangeSetWorkspaceDocument(
            document.SchemaVersion,
            document.Game,
            entry.State.ChangeSets,
            entry.State.ActiveChangeSetId,
            entry.State.BuildVariants,
            entry.State.ActiveBuildVariantId,
            undo,
            document.RedoHistory.Take(document.RedoHistory.Count - 1).ToArray(),
            now);
    }

    private static StoredChangeSetWorkspaceDocument PushUndo(
        StoredChangeSetWorkspaceDocument document,
        ChangeSetWorkspaceState state,
        string label,
        DateTimeOffset now)
    {
        var history = document.UndoHistory.Append(new ChangeSetHistoryEntry(
            CreateId(),
            label,
            GetState(document),
            now)).TakeLast(ChangeSetContract.MaximumHistoryCount).ToArray();
        return new StoredChangeSetWorkspaceDocument(
            document.SchemaVersion,
            document.Game,
            state.ChangeSets,
            state.ActiveChangeSetId,
            state.BuildVariants,
            state.ActiveBuildVariantId,
            history,
            RedoHistory: Array.Empty<ChangeSetHistoryEntry>(),
            now);
    }

    private static StoredChangeSetWorkspaceDocument TrimHistoryToBudget(
        StoredChangeSetWorkspaceDocument document)
    {
        var current = document;
        while (JsonSerializer.SerializeToUtf8Bytes(current, SerializerOptions).Length
            > ChangeSetContract.MaximumSerializedDocumentBytes)
        {
            if (current.UndoHistory.Count > 0)
            {
                current = current with { UndoHistory = current.UndoHistory.Skip(1).ToArray() };
                continue;
            }

            if (current.RedoHistory.Count > 0)
            {
                current = current with { RedoHistory = current.RedoHistory.Skip(1).ToArray() };
                continue;
            }

            throw Invalid("The change-set workspace exceeds its private storage limit.");
        }

        return current;
    }

    private static IReadOnlyList<NamedChangeSetDto> SelectSets(
        StoredChangeSetWorkspaceDocument document,
        ChangeSetBuildVariantDto? variant,
        ICollection<ChangeSetConflictDto> conflicts)
    {
        if (variant is null)
        {
            return document.ChangeSets.Where(set => set.Enabled && !set.Archived).ToArray();
        }

        var byId = document.ChangeSets.ToDictionary(set => set.ChangeSetId, StringComparer.Ordinal);
        var selected = new List<NamedChangeSetDto>();
        foreach (var id in variant.ChangeSetIds)
        {
            if (!byId.TryGetValue(id, out var set))
            {
                conflicts.Add(new ChangeSetConflictDto(
                    ChangeSetConflictKindDto.MissingDependency,
                    "The build variant references a missing change set.",
                    [id],
                    Array.Empty<string>(),
                    id));
                continue;
            }

            if (!set.Enabled || set.Archived)
            {
                conflicts.Add(new ChangeSetConflictDto(
                    ChangeSetConflictKindDto.DisabledDependency,
                    "The build variant references a disabled or archived change set.",
                    [id],
                    Array.Empty<string>(),
                    id));
                continue;
            }

            selected.Add(set);
        }

        return selected;
    }

    private static ChangeSetBuildVariantDto? ResolveVariant(
        StoredChangeSetWorkspaceDocument document,
        string? requestedId)
    {
        var id = requestedId ?? document.ActiveBuildVariantId;
        if (id is null)
        {
            return null;
        }

        return document.BuildVariants.FirstOrDefault(variant => string.Equals(
                variant.VariantId,
                id,
                StringComparison.Ordinal))
            ?? throw Invalid("The selected build variant does not exist.");
    }

    private static void DetectDependencyProblems(
        IReadOnlyList<NamedChangeSetDto> selected,
        IReadOnlyList<NamedChangeSetDto> all,
        ICollection<ChangeSetConflictDto> conflicts)
    {
        var allById = all.ToDictionary(set => set.ChangeSetId, StringComparer.Ordinal);
        var selectedIds = selected.Select(set => set.ChangeSetId).ToHashSet(StringComparer.Ordinal);
        var selectedOrder = selected
            .Select((set, index) => (set.ChangeSetId, index))
            .ToDictionary(pair => pair.ChangeSetId, pair => pair.index, StringComparer.Ordinal);
        foreach (var set in selected)
        {
            foreach (var dependencyId in set.DependencyIds)
            {
                if (!allById.TryGetValue(dependencyId, out var dependency))
                {
                    conflicts.Add(CreateDependencyConflict(
                        ChangeSetConflictKindDto.MissingDependency,
                        set.ChangeSetId,
                        dependencyId,
                        "An enabled change set has a missing dependency."));
                }
                else if (!selectedIds.Contains(dependencyId) || !dependency.Enabled || dependency.Archived)
                {
                    conflicts.Add(CreateDependencyConflict(
                        ChangeSetConflictKindDto.DisabledDependency,
                        set.ChangeSetId,
                        dependencyId,
                        "An enabled change set depends on a disabled, archived, or unselected change set."));
                }
                else if (selectedOrder[dependencyId] > selectedOrder[set.ChangeSetId])
                {
                    conflicts.Add(CreateDependencyConflict(
                        ChangeSetConflictKindDto.DependencyOrder,
                        set.ChangeSetId,
                        dependencyId,
                        "A change-set dependency must appear before the dependent set."));
                }
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in selected)
        {
            Visit(set.ChangeSetId, selectedIds, allById, visiting, visited, conflicts);
        }
    }

    private static void Visit(
        string id,
        ISet<string> selectedIds,
        IReadOnlyDictionary<string, NamedChangeSetDto> all,
        ISet<string> visiting,
        ISet<string> visited,
        ICollection<ChangeSetConflictDto> conflicts)
    {
        if (visited.Contains(id) || !selectedIds.Contains(id) || !all.TryGetValue(id, out var set))
        {
            return;
        }

        if (!visiting.Add(id))
        {
            conflicts.Add(new ChangeSetConflictDto(
                ChangeSetConflictKindDto.DependencyCycle,
                "The selected change sets contain a dependency cycle.",
                visiting.Order(StringComparer.Ordinal).ToArray(),
                Array.Empty<string>(),
                id));
            return;
        }

        foreach (var dependency in set.DependencyIds)
        {
            Visit(dependency, selectedIds, all, visiting, visited, conflicts);
        }

        visiting.Remove(id);
        visited.Add(id);
    }

    private static ChangeSetConflictDto CreateDependencyConflict(
        ChangeSetConflictKindDto kind,
        string setId,
        string dependencyId,
        string message)
    {
        return new ChangeSetConflictDto(
            kind,
            message,
            [setId, dependencyId],
            Array.Empty<string>(),
            dependencyId);
    }

    private static OperationBinding CreateOperationBinding(
        PendingEdit edit,
        Func<EditSession, ChangePlanOutputModeDto?, ChangePlan> planner,
        ChangePlanOutputModeDto? outputMode = null,
        IReadOnlyList<string>? satisfiedOwnedTargets = null)
    {
        try
        {
            var session = new EditSession(
                EditSessionId.New(),
                DateTimeOffset.UtcNow,
                [edit with { Association = null }]);
            var plan = planner(session, outputMode);
            if (!plan.CanApply)
            {
                return OperationBinding.Unsupported;
            }

            if (plan.Writes.Count == 0)
            {
                if (satisfiedOwnedTargets is null)
                {
                    return OperationBinding.Unsupported;
                }

                var normalizedSatisfiedTargets = NormalizeOwnedTargets(
                        satisfiedOwnedTargets,
                        requireNonEmpty: true)
                    .Select(target => new RelativeOutputPath(target))
                    .OrderBy(target => target.CanonicalKey, StringComparer.Ordinal)
                    .Select(target => target.Value)
                    .ToArray();
                using var satisfiedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                AppendHash(satisfiedHash, "change-set-satisfied-binding-v2");
                AppendHash(satisfiedHash, JsonSerializer.Serialize(
                    EditSessionBridgeMapper.ToPendingEditDto(edit) with { Association = null },
                    SerializerOptions));
                foreach (var target in normalizedSatisfiedTargets
                             .Select(target => new RelativeOutputPath(target))
                             .OrderBy(target => target.CanonicalKey, StringComparer.Ordinal))
                {
                    AppendHash(satisfiedHash, target.CanonicalKey);
                }

                return new OperationBinding(
                    ChangeSetSourceBindingKindDto.ReviewedPlan,
                    Convert.ToHexStringLower(satisfiedHash.GetHashAndReset()),
                    normalizedSatisfiedTargets);
            }

            if (plan.Writes.Any(write => !IsSha256(write.SourceFingerprint)))
            {
                return OperationBinding.Unsupported;
            }

            var normalizedWrites = plan.Writes.Select(write => (
                Write: write,
                Target: new RelativeOutputPath(write.TargetRelativePath),
                Sources: write.Sources.Select(source => (
                    source.Layer,
                    Path: new RelativeOutputPath(source.RelativePath))).ToArray())).ToArray();
            var ownedTargets = NormalizeOwnedTargets(
                    normalizedWrites.Select(write => write.Target.Value).ToArray(),
                    requireNonEmpty: true)
                .Select(target => new RelativeOutputPath(target))
                .OrderBy(target => target.CanonicalKey, StringComparer.Ordinal)
                .Select(target => target.Value)
                .ToArray();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendHash(hash, "change-set-source-binding-v2");
            foreach (var write in normalizedWrites
                         .OrderBy(write => write.Target.CanonicalKey, StringComparer.Ordinal)
                         .ThenBy(write => write.Write.Reason, StringComparer.Ordinal))
            {
                AppendHash(hash, write.Target.CanonicalKey);
                AppendHash(hash, write.Write.SourceFingerprint);
                AppendHash(hash, write.Write.ReplacesExistingOutput ? "replace" : "create");
                foreach (var source in write.Sources
                             .OrderBy(source => source.Layer)
                             .ThenBy(source => source.Path.CanonicalKey, StringComparer.Ordinal))
                {
                    AppendHash(hash, source.Layer.ToString());
                    AppendHash(hash, source.Path.CanonicalKey);
                }
            }

            return new OperationBinding(
                ChangeSetSourceBindingKindDto.ReviewedPlan,
                Convert.ToHexStringLower(hash.GetHashAndReset()),
                ownedTargets);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException)
        {
            return OperationBinding.Unsupported;
        }
    }

    private static string CreateWorkspaceFingerprint(
        StoredChangeSetWorkspaceDocument document,
        IReadOnlyList<string> selectedIds,
        ChangeSetBuildVariantDto? variant,
        string? outputProfileId,
        ChangePlanOutputModeDto? outputMode,
        string? outputRootFingerprint,
        IReadOnlyList<PendingEdit> sessionLocalEdits)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "change-set-effective-composition-v3");
        AppendHash(hash, document.Game.ToString());
        AppendHash(hash, variant?.VariantId);
        AppendHash(hash, outputProfileId);
        AppendHash(hash, outputMode?.ToString());
        AppendHash(hash, outputRootFingerprint);
        var setsById = document.ChangeSets.ToDictionary(set => set.ChangeSetId, StringComparer.Ordinal);
        foreach (var id in selectedIds)
        {
            AppendHash(hash, id);
            var set = setsById[id];
            AppendHash(hash, set.Enabled ? "enabled" : "disabled");
            AppendHash(hash, set.Archived ? "archived" : "current");
            foreach (var dependencyId in set.DependencyIds)
            {
                AppendHash(hash, dependencyId);
            }

            AppendHash(hash, null);
            foreach (var operation in set.Operations)
            {
                AppendHash(hash, operation.OperationId);
                AppendHash(hash, operation.Kind.ToString());
                AppendHash(hash, operation.SourceBindingKind.ToString());
                AppendHash(hash, operation.SourceFingerprint);
                AppendHash(hash, JsonSerializer.Serialize(operation.PendingEdit, SerializerOptions));
                foreach (var target in operation.OwnedTargets
                             .Select(target => new RelativeOutputPath(target))
                             .OrderBy(target => target.CanonicalKey, StringComparer.Ordinal))
                {
                    AppendHash(hash, target.CanonicalKey);
                }

                AppendHash(hash, null);
            }

            AppendHash(hash, null);
        }

        AppendHash(hash, "session-local");
        foreach (var edit in sessionLocalEdits)
        {
            AppendHash(hash, JsonSerializer.Serialize(
                EditSessionBridgeMapper.ToPendingEditDto(edit) with { Association = null },
                SerializerOptions));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string CreateSourceRevisionFingerprint(
        IEnumerable<string> currentBindingFingerprints,
        string workspaceFingerprint)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "change-set-source-revision-v1");
        AppendHash(hash, workspaceFingerprint);
        foreach (var fingerprint in currentBindingFingerprints.Order(StringComparer.Ordinal))
        {
            AppendHash(hash, fingerprint);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string CreatePlanSourceRevisionFingerprint(ChangePlan plan, string seed)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "change-set-effective-plan-v1");
        AppendHash(hash, seed);
        foreach (var write in plan.Writes
                     .Select(write => (
                         Write: write,
                         Target: new RelativeOutputPath(write.TargetRelativePath)))
                     .OrderBy(write => write.Target.CanonicalKey, StringComparer.Ordinal))
        {
            AppendHash(hash, write.Target.CanonicalKey);
            AppendHash(hash, write.Write.SourceFingerprint);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendHash(IncrementalHash hash, string? value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, -1);
            hash.AppendData(length);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    internal async Task<IDisposable> AcquireProjectLeasesAsync(
        IEnumerable<string> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        var orderedIds = projectIds
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (orderedIds.Length is 0 or > 2)
        {
            throw Invalid("A change-set operation must target one or two projects.");
        }

        var leases = new List<IDisposable>(orderedIds.Length);
        try
        {
            foreach (var projectId in orderedIds)
            {
                leases.Add(await store.AcquireProjectOperationLeaseAsync(
                        GetIdentity(projectId),
                        OperationLeaseId,
                        cancellationToken)
                    .ConfigureAwait(false));
            }

            return new ProjectLeaseGroup(leases);
        }
        catch
        {
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                leases[index].Dispose();
            }

            throw;
        }
    }

    private async Task<StoredChangeSetReadResult> ReadStoredAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var result = await store.ReadAsync(GetIdentity(projectId), Definition, cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? new StoredChangeSetReadResult(false, null, null)
            : new StoredChangeSetReadResult(true, result.Document, result.ETag);
    }

    private async Task<StoredChangeSetReadResult> RequireStoredAsync(
        string projectId,
        string expectedETag,
        CancellationToken cancellationToken)
    {
        var stored = await ReadStoredAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!stored.Exists
            || stored.Document is null
            || !string.Equals(stored.ETag, expectedETag, StringComparison.Ordinal))
        {
            throw new WorkspaceDocumentConflictException(expectedETag, stored.ETag);
        }

        return stored;
    }

    private static ChangeSetWorkspaceSnapshotDto ToSnapshot(
        StoredChangeSetWorkspaceDocument stored,
        string? etag,
        ChangeSetMaterializationDto effective)
    {
        return new ChangeSetWorkspaceSnapshotDto(
            new ChangeSetWorkspaceDocumentDto(
                stored.SchemaVersion,
                stored.Game,
                stored.ChangeSets,
                stored.ActiveChangeSetId,
                stored.BuildVariants,
                stored.ActiveBuildVariantId,
                stored.UpdatedAtUtc),
            etag,
            stored.UndoHistory.Count > 0,
            stored.RedoHistory.Count > 0,
            stored.UndoHistory.LastOrDefault()?.Label,
            stored.RedoHistory.LastOrDefault()?.Label,
            effective);
    }

    private static ValidatedScope ValidateScope(ChangeSetWorkspaceScopeDto scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(scope.Paths);
        var paths = ProjectBridgeMapper.ToCore(scope.Paths);
        if (paths.SelectedGame is not { } game)
        {
            throw Invalid("A change-set workspace requires a selected game.");
        }

        var projectId = ProjectIdentity.FromPaths(paths).Value;
        if (!string.Equals(projectId, scope.ProjectId, StringComparison.Ordinal))
        {
            throw Invalid("The change-set scope does not match the selected project paths.");
        }

        return new ValidatedScope(
            projectId,
            GetIdentity(projectId),
            ProjectBridgeMapper.ToDto(game),
            paths);
    }

    private static WorkspaceProjectIdentity GetIdentity(string projectId)
    {
        if (string.IsNullOrEmpty(projectId)
            || projectId.Length > MaximumIdLength
            || projectId != projectId.Trim()
            || projectId.Any(char.IsControl))
        {
            throw Invalid("The change-set project id is invalid.");
        }

        return WorkspaceProjectIdentity.FromProjectId(new ProjectId(projectId));
    }

    private static StoredChangeSetWorkspaceDocument CreateEmptyStoredDocument(
        ProjectGameDto game,
        DateTimeOffset now)
    {
        return new StoredChangeSetWorkspaceDocument(
            ChangeSetContract.SchemaVersion,
            game,
            ChangeSets: Array.Empty<NamedChangeSetDto>(),
            ActiveChangeSetId: null,
            BuildVariants: Array.Empty<ChangeSetBuildVariantDto>(),
            ActiveBuildVariantId: null,
            UndoHistory: Array.Empty<ChangeSetHistoryEntry>(),
            RedoHistory: Array.Empty<ChangeSetHistoryEntry>(),
            now);
    }

    private static void ValidateStoredDocument(
        StoredChangeSetWorkspaceDocument document,
        ProjectGameDto expectedGame)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != ChangeSetContract.SchemaVersion
            || !Enum.IsDefined(document.Game)
            || document.Game != expectedGame
            || document.ChangeSets is null
            || document.BuildVariants is null
            || document.UndoHistory is null
            || document.RedoHistory is null
            || document.UpdatedAtUtc == default)
        {
            throw Invalid("The private change-set document is invalid or belongs to another game.");
        }

        ValidateState(GetState(document), expectedGame);
        ValidateHistory(document.UndoHistory, expectedGame);
        ValidateHistory(document.RedoHistory, expectedGame);
        if (document.UndoHistory.Count > ChangeSetContract.MaximumHistoryCount
            || document.RedoHistory.Count > ChangeSetContract.MaximumHistoryCount
            || JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions).Length
                > ChangeSetContract.MaximumSerializedDocumentBytes)
        {
            throw Invalid("The private change-set document exceeds a supported limit.");
        }
    }

    private static void ValidateHistory(
        IReadOnlyList<ChangeSetHistoryEntry> history,
        ProjectGameDto expectedGame)
    {
        foreach (var entry in history)
        {
            if (entry is null || entry.State is null)
            {
                throw Invalid("An authoring history entry is invalid.");
            }

            ValidateId(entry.EventId, "authoring event id");
            ValidateDisplayText(entry.Label, "authoring event label", MaximumNameLength);
            if (entry.CreatedAtUtc == default)
            {
                throw Invalid("An authoring event timestamp is invalid.");
            }

            ValidateState(entry.State, expectedGame);
        }
    }

    private static void ValidateState(
        ChangeSetWorkspaceState state,
        ProjectGameDto expectedGame)
    {
        if (state.ChangeSets is null
            || state.BuildVariants is null
            || state.ChangeSets.Count > ChangeSetContract.MaximumChangeSetCount
            || state.BuildVariants.Count > ChangeSetContract.MaximumBuildVariantCount)
        {
            throw Invalid("The change-set workspace exceeds a supported item limit.");
        }

        var setIds = new HashSet<string>(StringComparer.Ordinal);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        var operationCount = 0;
        foreach (var set in state.ChangeSets)
        {
            if (set is null
                || set.Tags is null
                || set.DependencyIds is null
                || set.Operations is null)
            {
                throw Invalid("A named change set is invalid.");
            }

            ValidateId(set.ChangeSetId, "change set id");
            ValidateDisplayText(set.Name, "change set name", MaximumNameLength);
            ValidateOptionalText(set.Notes, "change set notes", MaximumNotesLength);
            ValidateTags(set.Tags);
            ValidateIdList(set.DependencyIds, ChangeSetContract.MaximumDependencyCount, "dependencies");
            if (!setIds.Add(set.ChangeSetId)
                || set.Operations.Count > ChangeSetContract.MaximumOperationsPerChangeSet
                || set.CreatedAtUtc == default
                || set.UpdatedAtUtc == default)
            {
                throw Invalid("A named change set is invalid or duplicated.");
            }

            operationCount = checked(operationCount + set.Operations.Count);
            var setTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in set.Operations)
            {
                ValidateOperation(operation, set.ChangeSetId);
                if (!operationIds.Add(operation.OperationId)
                    || !setTargets.Add(CreateEditTargetKey(
                        EditSessionBridgeMapper.ToPendingEditCore(operation.PendingEdit))))
                {
                    throw Invalid("A change-set operation id or target is duplicated.");
                }
            }
        }

        if (operationCount > ChangeSetContract.MaximumOperationCount
            || state.ChangeSets.Any(set => set.DependencyIds.Any(id => !setIds.Contains(id))))
        {
            throw Invalid("Change-set operations or dependencies exceed their supported boundary.");
        }

        if (state.ActiveChangeSetId is not null
            && (!setIds.Contains(state.ActiveChangeSetId)
                || state.ChangeSets.Single(set => set.ChangeSetId == state.ActiveChangeSetId).Archived))
        {
            throw Invalid("The active staging change set is invalid.");
        }

        var variantIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in state.BuildVariants)
        {
            if (variant is null)
            {
                throw Invalid("A build variant is invalid.");
            }

            ValidateVariant(variant, state.ChangeSets, expectedGame);
            if (!variantIds.Add(variant.VariantId))
            {
                throw Invalid("A build variant id is duplicated.");
            }
        }

        if (state.ActiveBuildVariantId is not null && !variantIds.Contains(state.ActiveBuildVariantId))
        {
            throw Invalid("The active build variant is invalid.");
        }
    }

    private static void ValidateOperation(ChangeSetOperationDto operation, string changeSetId)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateId(operation.OperationId, "operation id");
        if (!Enum.IsDefined(operation.Kind)
            || operation.Kind != ChangeSetOperationStorageKindDto.LegacyPendingEdit
            || !Enum.IsDefined(operation.SourceBindingKind)
            || operation.PendingEdit is null
            || operation.OwnedTargets is null
            || operation.OwnedTargets.Count > MaximumOwnedTargetCount
            || operation.CreatedAtUtc == default
            || operation.UpdatedAtUtc == default)
        {
            throw Invalid("A change-set operation is invalid.");
        }

        if (operation.PendingEdit.Sources is null
            || operation.PendingEdit.Sources.Any(source => source is null)
            || operation.PendingEdit.Association is null)
        {
            throw Invalid("A change-set pending edit is invalid.");
        }

        var storedAssociation = operation.PendingEdit.Association;
        ValidateId(storedAssociation.ChangeSetId, "pending edit change set id");
        ValidateId(storedAssociation.OperationId, "pending edit operation id");
        if (storedAssociation.Version != ChangeSetContract.AssociationVersion
            || !string.Equals(storedAssociation.ChangeSetId, changeSetId, StringComparison.Ordinal)
            || !string.Equals(
                storedAssociation.OperationId,
                operation.OperationId,
                StringComparison.Ordinal)
            || operation.PendingEdit.Sources.Any(source => !Enum.IsDefined(source.Layer)))
        {
            throw Invalid("A change-set operation association or source layer is invalid.");
        }

        var edit = EditSessionBridgeMapper.ToPendingEditCore(operation.PendingEdit);
        ValidatePendingEdits([edit]);
        if (edit.Association is null)
        {
            throw Invalid("A change-set operation association is invalid.");
        }

        if (operation.SourceBindingKind == ChangeSetSourceBindingKindDto.ReviewedPlan
            ? !IsSha256(operation.SourceFingerprint) || operation.OwnedTargets.Count == 0
            : operation.SourceFingerprint is not null || operation.OwnedTargets.Count != 0)
        {
            throw Invalid("A change-set operation source binding is invalid.");
        }

        var normalizedOwnedTargets = NormalizeOwnedTargets(
            operation.OwnedTargets,
            requireNonEmpty: false);
        if (!normalizedOwnedTargets.SequenceEqual(operation.OwnedTargets, StringComparer.Ordinal))
        {
            throw Invalid("A stored owned output target is not canonical.");
        }
    }

    private static void ValidatePendingEdits(IReadOnlyList<PendingEdit> edits)
    {
        if (edits is null)
        {
            throw Invalid("An edit session pending-edit list is invalid.");
        }

        if (edits.Count > ChangeSetContract.MaximumOperationCount)
        {
            throw Invalid("An edit session exceeds the change-set operation limit.");
        }

        foreach (var edit in edits)
        {
            if (edit is null
                || edit.Sources is null
                || edit.Sources.Count > MaximumSourceCount
                || edit.Sources.Any(source => source is null || !Enum.IsDefined(source.Layer)))
            {
                throw Invalid("A pending edit is invalid.");
            }

            ValidateDisplayText(edit.Domain, "pending edit domain", MaximumDomainLength);
            ValidateDisplayText(edit.Summary, "pending edit summary", MaximumSummaryLength);
            ValidateOptionalText(edit.RecordId, "pending edit record id", MaximumRecordIdLength);
            ValidateOptionalText(edit.Field, "pending edit field", MaximumFieldLength);
            ValidateOptionalText(edit.NewValue, "pending edit value", MaximumValueLength);
            ValidateOptionalText(edit.Owner, "pending edit owner", MaximumIdLength);
            foreach (var source in edit.Sources)
            {
                ValidateRelativePath(source.RelativePath, "pending edit source");
            }

            if (edit.Association is { } association)
            {
                _ = new PendingEditAssociation(
                    association.Version,
                    association.ChangeSetId,
                    association.OperationId);
            }
        }
    }

    private static void ValidateMetadata(ChangeSetMetadataDto metadata)
    {
        if (metadata is null || metadata.Tags is null || metadata.DependencyIds is null)
        {
            throw Invalid("Change-set metadata is invalid.");
        }

        ValidateDisplayText(metadata.Name, "change set name", MaximumNameLength);
        ValidateOptionalText(metadata.Notes, "change set notes", MaximumNotesLength);
        ValidateTags(metadata.Tags);
        ValidateIdList(metadata.DependencyIds, ChangeSetContract.MaximumDependencyCount, "dependencies");
    }

    private static void ValidateVariant(
        ChangeSetBuildVariantDto variant,
        IReadOnlyList<NamedChangeSetDto> sets,
        ProjectGameDto? game = null)
    {
        if (variant is null || variant.ChangeSetIds is null)
        {
            throw Invalid("A build variant is invalid.");
        }
        ValidateId(variant.VariantId, "variant id");
        ValidateDisplayText(variant.Name, "variant name", MaximumNameLength);
        ValidateIdList(variant.ChangeSetIds, ChangeSetContract.MaximumChangeSetCount, "variant change sets");
        if (variant.ChangeSetIds.Any(id => sets.All(set => !string.Equals(
                set.ChangeSetId,
                id,
                StringComparison.Ordinal))))
        {
            throw Invalid("A build variant references a missing change set.");
        }

        if (variant.OutputProfileId is not null)
        {
            ValidateId(variant.OutputProfileId, "output profile id");
        }

        if (variant.OutputMode is { } mode && !Enum.IsDefined(mode))
        {
            throw Invalid("A build variant output mode is invalid.");
        }

        if ((game == ProjectGameDto.Sword || game == ProjectGameDto.Shield)
            && (variant.OutputMode == ChangePlanOutputModeDto.TrinityModManager
                || variant.OutputMode == ChangePlanOutputModeDto.TrinityBypass))
        {
            throw Invalid("Sword and Shield build variants cannot select a Trinity output mode.");
        }

        if (variant.CreatedAtUtc == default || variant.UpdatedAtUtc == default)
        {
            throw Invalid("A build variant timestamp is invalid.");
        }
    }

    private static void ValidateTags(IReadOnlyList<string> tags)
    {
        if (tags is null
            || tags.Count > ChangeSetContract.MaximumTagCount
            || tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tags.Count)
        {
            throw Invalid("Change-set tags exceed their supported limit or are duplicated.");
        }

        foreach (var tag in tags)
        {
            ValidateDisplayText(tag, "change set tag", MaximumTagLength);
        }
    }

    private static void ValidateIdList(IReadOnlyList<string> ids, int maximum, string label)
    {
        if (ids is null
            || ids.Count > maximum
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
        {
            throw Invalid($"Change-set {label} exceed their supported limit or are duplicated.");
        }

        foreach (var id in ids)
        {
            ValidateId(id, label);
        }
    }

    private static void ValidateId(string? value, string label)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumIdLength
            || value != value.Trim()
            || !char.IsAsciiLetterOrDigit(value[0])
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '-' or '_')))
        {
            throw Invalid($"The {label} is invalid.");
        }
    }

    private static void ValidateDisplayText(string? value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || ContainsDisallowedControl(value))
        {
            throw Invalid($"The {label} is invalid.");
        }
    }

    private static void ValidateOptionalText(string? value, string label, int maximumLength)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > maximumLength || ContainsDisallowedControl(value))
        {
            throw Invalid($"The {label} is invalid.");
        }
    }

    private static bool ContainsDisallowedControl(string value)
    {
        return value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
    }

    private static IReadOnlyList<string> NormalizeOwnedTargets(
        IReadOnlyList<string> targets,
        bool requireNonEmpty)
    {
        if (targets is null
            || targets.Count > MaximumOwnedTargetCount
            || (requireNonEmpty && targets.Count == 0))
        {
            throw Invalid("Owned output targets exceed their supported boundary.");
        }

        var canonicalKeys = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(targets.Count);
        foreach (var target in targets)
        {
            RelativeOutputPath path;
            try
            {
                path = new RelativeOutputPath(target);
            }
            catch (ArgumentException exception)
            {
                throw new ChangeSetValidationException(
                    "An owned output target is not a safe relative output path.",
                    exception);
            }

            if (!canonicalKeys.Add(path.CanonicalKey))
            {
                throw Invalid("An owned output target is duplicated by canonical identity.");
            }

            normalized.Add(path.Value);
        }

        return normalized;
    }

    private static bool OwnedTargetsMatch(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        try
        {
            var leftKeys = NormalizeOwnedTargets(left, requireNonEmpty: true)
                .Select(target => new RelativeOutputPath(target).CanonicalKey)
                .Order(StringComparer.Ordinal);
            var rightKeys = NormalizeOwnedTargets(right, requireNonEmpty: true)
                .Select(target => new RelativeOutputPath(target).CanonicalKey)
                .Order(StringComparer.Ordinal);
            return leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
        }
        catch (ChangeSetValidationException)
        {
            return false;
        }
    }

    private static RelativeOutputPath RequireCanonicalRelativeOutputPath(
        string value,
        string label)
    {
        RelativeOutputPath path;
        try
        {
            path = new RelativeOutputPath(value);
        }
        catch (ArgumentException exception)
        {
            throw new ChangeSetValidationException(
                $"The {label} is not a safe relative output path.",
                exception);
        }

        if (!string.Equals(value, path.Value, StringComparison.Ordinal))
        {
            throw Invalid($"The {label} is not canonical.");
        }

        return path;
    }

    private static void ValidateRelativePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumRelativePathLength
            || value != value.Trim()
            || value.Any(char.IsControl)
            || Path.IsPathRooted(value)
            || value.Replace('\\', '/').Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw Invalid($"The {label} is not a bounded portable relative path.");
        }
    }

    private static void ValidateETag(string? etag, bool allowNull)
    {
        if (etag is null && allowNull)
        {
            return;
        }

        if (!IsSha256(etag))
        {
            throw Invalid("The expected change-set document ETag is invalid.");
        }
    }

    private static string? NormalizeETag(string? etag, bool allowNull)
    {
        ValidateETag(etag, allowNull);
        return etag?.ToLowerInvariant();
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }

    private static IReadOnlyDictionary<string, PendingEdit> BuildUniqueEditMap(
        IReadOnlyList<PendingEdit> edits)
    {
        var result = new Dictionary<string, PendingEdit>(StringComparer.Ordinal);
        foreach (var edit in edits)
        {
            if (!result.TryAdd(CreateEditTargetKey(edit), edit))
            {
                throw Invalid("An edit session contains duplicate pending targets.");
            }
        }

        return result;
    }

    private static string CreateEditTargetKey(PendingEdit edit)
    {
        var builder = new StringBuilder();
        AppendKey(builder, edit.Domain);
        AppendKey(builder, edit.RecordId);
        AppendKey(builder, edit.Field);
        if (edit.RecordId is null && edit.Field is null)
        {
            AppendKey(builder, edit.Owner);
            AppendKey(builder, edit.Summary);
        }

        return builder.ToString();
    }

    private static void AppendKey(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-1:");
            return;
        }

        builder.Append(value.Length).Append(':').Append(value);
    }

    private static bool PendingEditContentEquals(PendingEdit left, PendingEdit right)
    {
        var leftDto = EditSessionBridgeMapper.ToPendingEditDto(left) with { Association = null };
        var rightDto = EditSessionBridgeMapper.ToPendingEditDto(right) with { Association = null };
        return JsonSerializer.SerializeToUtf8Bytes(leftDto, SerializerOptions)
            .AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(rightDto, SerializerOptions));
    }

    private static ChangeSetOperationSummaryDto CreateOperationSummary(
        ChangeSetOperationDto operation,
        string changeSetId,
        string changeSetName,
        ChangeSetOperationMaterializationStateDto state)
    {
        var edit = operation.PendingEdit;
        return new ChangeSetOperationSummaryDto(
            operation.OperationId,
            changeSetId,
            changeSetName,
            SanitizeDisplay(edit.Summary, "Staged edit"),
            CreateDisplayTarget(EditSessionBridgeMapper.ToPendingEditCore(edit)),
            operation.SourceBindingKind == ChangeSetSourceBindingKindDto.ReviewedPlan
                ? "Bound to the reviewed game workflow source and output plan."
                : "Organized legacy edit without a complete portable source binding.",
            state);
    }

    private static string CreateDisplayTarget(PendingEdit edit)
    {
        var target = edit.RecordId is null
            ? edit.Domain
            : edit.Field is null
                ? $"{edit.Domain} / {edit.RecordId}"
                : $"{edit.Domain} / {edit.RecordId} / {edit.Field}";
        return SanitizeDisplay(target, edit.Domain);
    }

    private static string SanitizeDisplay(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var sanitized = new string(value
            .Where(character => !char.IsControl(character) || character == ' ')
            .Take(MaximumSummaryLength)
            .ToArray()).Trim();
        return sanitized.Length == 0 ? fallback : sanitized;
    }

    private static bool PathsEqual(string? left, string? right)
    {
        return TryNormalizePrivatePath(left, out var normalizedLeft)
            && TryNormalizePrivatePath(right, out var normalizedRight)
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static string CreatePrivatePathFingerprint(string path)
    {
        if (!TryNormalizePrivatePath(path, out var normalized))
        {
            throw Invalid("The output profile path is invalid.");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"change-set-private-output-root-v1\0{normalized}")));
    }

    private static string CreateUnsetOutputRootFingerprint()
    {
        return Convert.ToHexStringLower(SHA256.HashData(
            "change-set-private-output-root-v1\0unset"u8));
    }

    private static bool TryNormalizePrivatePath(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
            if (OperatingSystem.IsWindows())
            {
                normalized = normalized.ToUpperInvariant();
            }

            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    private static ChangeSetWorkspaceState GetState(StoredChangeSetWorkspaceDocument document)
    {
        return new ChangeSetWorkspaceState(
            document.ChangeSets,
            document.ActiveChangeSetId,
            document.BuildVariants,
            document.ActiveBuildVariantId);
    }

    private static int FindSetIndex(IReadOnlyList<NamedChangeSetDto> sets, string? id)
    {
        ValidateId(id, "change set id");
        var matches = sets.Select((set, index) => (set, index))
            .Where(pair => string.Equals(pair.set.ChangeSetId, id, StringComparison.Ordinal))
            .Select(pair => pair.index)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw Invalid("The change set does not exist.");
    }

    private static int FindVariantIndex(IReadOnlyList<ChangeSetBuildVariantDto> variants, string? id)
    {
        ValidateId(id, "variant id");
        var matches = variants.Select((variant, index) => (variant, index))
            .Where(pair => string.Equals(pair.variant.VariantId, id, StringComparison.Ordinal))
            .Select(pair => pair.index)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw Invalid("The build variant does not exist.");
    }

    private static IReadOnlyList<T> ReorderExact<T>(
        IReadOnlyList<T> items,
        IReadOnlyList<string>? orderedIds,
        Func<T, string> getId,
        string label)
    {
        if (orderedIds is null
            || orderedIds.Count != items.Count
            || orderedIds.Distinct(StringComparer.Ordinal).Count() != orderedIds.Count)
        {
            throw Invalid($"The ordered {label} must contain every id exactly once.");
        }

        var byId = items.ToDictionary(getId, StringComparer.Ordinal);
        if (orderedIds.Any(id => !byId.ContainsKey(id)))
        {
            throw Invalid($"The ordered {label} contain an unknown id.");
        }

        return orderedIds.Select(id => byId[id]).ToArray();
    }

    private static void EnsureOnly(
        ChangeSetWorkspaceMutationDto mutation,
        bool allowChangeSetId = false,
        bool allowName = false,
        bool allowMetadata = false,
        bool allowOrderedIds = false,
        bool allowOperationId = false,
        bool allowVariant = false,
        bool allowVariantId = false)
    {
        if ((!allowChangeSetId && mutation.ChangeSetId is not null)
            || (!allowName && mutation.Name is not null)
            || (!allowMetadata && mutation.Metadata is not null)
            || (!allowOrderedIds && mutation.OrderedIds is not null)
            || (!allowOperationId && mutation.OperationId is not null)
            || (!allowVariant && mutation.Variant is not null)
            || (!allowVariantId && mutation.VariantId is not null))
        {
            throw Invalid("The change-set mutation contains fields that do not apply to its kind.");
        }
    }

    private static string GetMutationLabel(ChangeSetMutationKindDto kind)
    {
        return kind switch
        {
            ChangeSetMutationKindDto.CreateSet => "Create change set",
            ChangeSetMutationKindDto.UpdateSet => "Update change set",
            ChangeSetMutationKindDto.DeleteSet => "Delete change set",
            ChangeSetMutationKindDto.DuplicateSet => "Duplicate change set",
            ChangeSetMutationKindDto.ReorderSets => "Reorder change sets",
            ChangeSetMutationKindDto.ReorderOperations => "Reorder staged edits",
            ChangeSetMutationKindDto.RemoveOperation => "Remove staged edit",
            ChangeSetMutationKindDto.SetActiveSet => "Change staging target",
            ChangeSetMutationKindDto.CreateVariant => "Create build variant",
            ChangeSetMutationKindDto.UpdateVariant => "Update build variant",
            ChangeSetMutationKindDto.DeleteVariant => "Delete build variant",
            ChangeSetMutationKindDto.SetActiveVariant => "Select build variant",
            _ => "Update change sets",
        };
    }

    private static string CreateId() => Guid.NewGuid().ToString("N");

    private static ApiDiagnostic CreateDiagnostic(ApiDiagnosticSeverity severity, string message)
    {
        return new ApiDiagnostic(severity, message, Domain: "changeSets");
    }

    private static ChangeSetValidationException Invalid(string message)
    {
        return new ChangeSetValidationException(message);
    }

    private static bool IsFatal(Exception exception)
    {
        return exception is
            OutOfMemoryException or
            StackOverflowException or
            AccessViolationException or
            AppDomainUnloadedException
            || exception is AggregateException aggregate
                && aggregate.InnerExceptions.Any(IsFatal)
            || exception.InnerException is not null
                && IsFatal(exception.InnerException);
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

    private sealed record ValidatedScope(
        string ProjectId,
        WorkspaceProjectIdentity Identity,
        ProjectGameDto Game,
        ProjectPaths Paths);

    private sealed record BoundSessionContext(
        ValidatedScope Scope,
        StoredChangeSetWorkspaceDocument Document,
        string ETag,
        string? BuildVariantId);

    private sealed record OperationBinding(
        ChangeSetSourceBindingKindDto Kind,
        string? Fingerprint,
        IReadOnlyList<string> OwnedTargets)
    {
        public static OperationBinding Unsupported { get; } = new(
            ChangeSetSourceBindingKindDto.LegacyUnsupported,
            Fingerprint: null,
            OwnedTargets: Array.Empty<string>());
    }

    private sealed class ProjectLeaseGroup : IDisposable
    {
        private IReadOnlyList<IDisposable>? leases;

        public ProjectLeaseGroup(IReadOnlyList<IDisposable> leases)
        {
            this.leases = leases;
        }

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref leases, null);
            if (current is null)
            {
                return;
            }

            for (var index = current.Count - 1; index >= 0; index--)
            {
                current[index].Dispose();
            }
        }
    }
}

internal sealed record StoredChangeSetWorkspaceDocument(
    int SchemaVersion,
    ProjectGameDto Game,
    IReadOnlyList<NamedChangeSetDto> ChangeSets,
    string? ActiveChangeSetId,
    IReadOnlyList<ChangeSetBuildVariantDto> BuildVariants,
    string? ActiveBuildVariantId,
    IReadOnlyList<ChangeSetHistoryEntry> UndoHistory,
    IReadOnlyList<ChangeSetHistoryEntry> RedoHistory,
    DateTimeOffset UpdatedAtUtc);

internal sealed record ChangeSetWorkspaceState(
    IReadOnlyList<NamedChangeSetDto> ChangeSets,
    string? ActiveChangeSetId,
    IReadOnlyList<ChangeSetBuildVariantDto> BuildVariants,
    string? ActiveBuildVariantId);

internal sealed record ChangeSetHistoryEntry(
    string EventId,
    string Label,
    ChangeSetWorkspaceState State,
    DateTimeOffset CreatedAtUtc);

internal sealed record StoredChangeSetReadResult(
    bool Exists,
    StoredChangeSetWorkspaceDocument? Document,
    string? ETag);

public sealed class ChangeSetValidationException : Exception
{
    public ChangeSetValidationException(string message)
        : base(message)
    {
    }

    public ChangeSetValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
