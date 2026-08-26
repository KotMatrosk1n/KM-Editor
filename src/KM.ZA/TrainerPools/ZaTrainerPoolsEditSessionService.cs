// SPDX-License-Identifier: GPL-3.0-only

using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.TrainerPools;

internal sealed class ZaTrainerPoolsEditSessionService
{
    private const string SwapField = "fixedCountSwap";
    private const string RecordPrefix = "trainer-pool-swap:";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaTrainerPoolsWorkflowService workflowService;

    public ZaTrainerPoolsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaTrainerPoolsWorkflowService? workflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.workflowService = workflowService ?? new ZaTrainerPoolsWorkflowService();
    }

    public ZaTrainerPoolsEditResult StageFixedCountSwap(
        ProjectPaths paths,
        EditSession? session,
        ZaTrainerPoolFixedCountSwap operation)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(operation);
        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        if (currentSession.PendingEdits.Any(edit => !string.Equals(
                edit.Domain,
                ZaEditSessionSupport.TrainerPoolsDomain,
                StringComparison.Ordinal)))
        {
            diagnostics.Add(Error(
                "Trainer Pools fixed-count swaps need their own edit session.",
                expected: "A Trainer Pools-only edit session",
                code: ZaTrainerPoolsDiagnosticCodes.SessionConflict));
            return new ZaTrainerPoolsEditResult(
                workflowService.Load(project),
                currentSession,
                diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit => string.Equals(
                edit.Domain,
                ZaEditSessionSupport.TrainerPoolsDomain,
                StringComparison.Ordinal)))
        {
            diagnostics.Add(Error(
                "A Trainer Pools swap is already staged. Review and apply it, or discard it, before staging another swap.",
                expected: "No existing staged Trainer Pools swap",
                code: ZaTrainerPoolsDiagnosticCodes.SwapAlreadyStaged));
            return new ZaTrainerPoolsEditResult(
                workflowService.Load(project),
                currentSession,
                diagnostics);
        }

        var operationShapeIsValid = ValidateOperationShape(operation, diagnostics);
        var stateWasLoaded = workflowService.TryLoadState(
            project,
            out var state,
            out var blockedWorkflow);
        if (!operationShapeIsValid || !stateWasLoaded)
        {
            return new ZaTrainerPoolsEditResult(
                state?.Workflow ?? blockedWorkflow!,
                currentSession,
                diagnostics.Concat(blockedWorkflow?.Diagnostics ?? []).ToArray());
        }

        diagnostics.AddRange(state!.Workflow.Diagnostics);
        if (!state.Workflow.CanStage
            || !ZaTrainerPoolsWorkflowService.TryApplyFixedCountSwap(
                state,
                operation,
                diagnostics,
                out var editedDocument,
                out var changedReferenceCount))
        {
            return new ZaTrainerPoolsEditResult(state.Workflow, currentSession, diagnostics);
        }

        diagnostics.AddRange(ZaTrainerPoolsWorkflowService.ValidateEditedState(state, editedDocument!));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaTrainerPoolsEditResult(state.Workflow, currentSession, diagnostics);
        }

        var pendingEdit = new PendingEdit(
            ZaEditSessionSupport.TrainerPoolsDomain,
            $"Swap trainer '{DisplayName(state, operation.SourceRawTrainerId)}' in "
                + $"'{DisplayPool(state, operation.SourceLogicalPoolId)}' with "
                + $"'{DisplayName(state, operation.DestinationRawTrainerId)}' in "
                + $"'{DisplayPool(state, operation.DestinationLogicalPoolId)}' "
                + $"across {changedReferenceCount} physical references.",
            state.Sources,
            CreateRecordId(operation),
            SwapField,
            Encode(operation));
        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits
                .Append(pendingEdit)
                .ToArray(),
        };
        diagnostics.Add(Info(
            $"Staged one fixed-count Trainer Pools swap across {changedReferenceCount} exact physical references."));
        return new ZaTrainerPoolsEditResult(
            OverlayWorkflow(state.Workflow, state.Identities, operation),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        var diagnostics = new List<ValidationDiagnostic>();
        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(Error(
                "Trainer Pools expects exactly one staged fixed-count swap.",
                expected: "One pending fixed-count swap",
                code: ZaTrainerPoolsDiagnosticCodes.EditSafety));
        }

        var project = projectWorkspaceService.Open(paths);
        if (!workflowService.TryLoadState(project, out var state, out var blockedWorkflow))
        {
            diagnostics.AddRange(blockedWorkflow!.Diagnostics);
            return new ZaEditSessionValidation(session, IsValid: false, diagnostics);
        }

        diagnostics.AddRange(state!.Workflow.Diagnostics);
        foreach (var edit in session.PendingEdits)
        {
            if (!TryDecodePendingEdit(edit, diagnostics, out var operation))
            {
                continue;
            }

            if (!SourcesMatch(edit.Sources, state.Sources))
            {
                diagnostics.Add(Error(
                    "The staged Trainer Pools source set does not match the current authoritative workflow sources.",
                    expected: "Exact trainer-table, identity, roster, and spawner sources",
                    code: ZaTrainerPoolsDiagnosticCodes.SourceChanged));
                continue;
            }

            if (!ZaTrainerPoolsWorkflowService.TryApplyFixedCountSwap(
                    state,
                    operation!,
                    diagnostics,
                    out var editedDocument,
                    out _))
            {
                continue;
            }

            diagnostics.AddRange(ZaTrainerPoolsWorkflowService.ValidateEditedState(state, editedDocument!));
        }

        if (session.PendingEdits.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(Info(
                "The pending fixed-count Trainer Pools swap is valid for reviewed change planning."));
        }

        return new ZaEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        return ZaChangePlanSourceGuard.Capture(
            paths,
            session,
            () => ZaEditSessionSupport.CreateSingleFileChangePlan(
                paths,
                session,
                ZaEditSessionSupport.TrainerPoolsDomain,
                ZaDataPaths.TrainerPoolTableDataArray,
                "Trainer Pools",
                Validate(paths, session).Diagnostics,
                outputMode),
            outputMode);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(Error(
                "The reviewed Trainer Pools plan is stale. Review the exact current sources and swap again before applying.",
                expected: "Current reviewed Trainer Pools change plan",
                code: ZaTrainerPoolsDiagnosticCodes.PlanStale));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                diagnostics);
        }

        OutputApplyResult? outputTransaction = null;
        try
        {
            var project = projectWorkspaceService.Open(paths);
            if (!workflowService.TryLoadState(project, out var state, out var blockedWorkflow))
            {
                diagnostics.AddRange(blockedWorkflow!.Diagnostics);
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            if (!TryDecodePendingEdit(session.PendingEdits.Single(), diagnostics, out var operation)
                || !ZaTrainerPoolsWorkflowService.TryApplyFixedCountSwap(
                    state!,
                    operation!,
                    diagnostics,
                    out var editedDocument,
                    out var changedReferenceCount))
            {
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            diagnostics.AddRange(ZaTrainerPoolsWorkflowService.ValidateEditedState(state!, editedDocument!));
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            var outputBytes = editedDocument!.Write();
            var finalDocument = ZaTrainerPoolDataDocument.Parse(outputBytes);
            if (!string.Equals(
                    editedDocument.CreateSemanticFingerprint(),
                    finalDocument.CreateSemanticFingerprint(),
                    StringComparison.Ordinal))
            {
                diagnostics.Add(Error(
                    "The final Trainer Pools output failed complete rebuild and reparse verification.",
                    expected: "Exact reviewed semantic output",
                    code: ZaTrainerPoolsDiagnosticCodes.VerificationFailed));
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            outputTransaction = ZaWorkflowFileSource.WriteBatch(
                paths,
                [new ZaWorkflowFileWrite(ZaDataPaths.TrainerPoolTableDataArray, outputBytes)],
                outputMode,
                revalidateReviewedState: () =>
                    ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                        reviewedPlan,
                        CreateChangePlan(paths, session, outputMode)));
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                ZaDataPaths.TrainerPoolTableDataArray,
                outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(Info(
                $"Applied the fixed-count Trainer Pools swap across {changedReferenceCount} physical references. "
                + ZaEditSessionSupport.CreateApplyOutputMessage("Trainer Pools", outputMode)));
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            if (exception is ZaOutputApplyNotCommittedException notCommitted)
            {
                outputTransaction = notCommitted.Result;
            }

            diagnostics.Add(Error(
                $"Trainer Pools output could not be applied atomically: {exception.Message}",
                expected: "Fresh readable sources and a writable transactional output target",
                code: ZaTrainerPoolsDiagnosticCodes.ApplyFailed));
            writtenFiles.Clear();
        }

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles,
            diagnostics,
            outputTransaction);
    }

    private static bool TryDecodePendingEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics,
        out ZaTrainerPoolFixedCountSwap? operation)
    {
        operation = null;
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.TrainerPoolsDomain, StringComparison.Ordinal)
            || !string.Equals(edit.Field, SwapField, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(edit.NewValue))
        {
            diagnostics.Add(Error(
                "The pending edit is not a supported fixed-count Trainer Pools swap.",
                expected: "Trainer Pools fixed-count swap payload",
                code: ZaTrainerPoolsDiagnosticCodes.EditSafety));
            return false;
        }

        try
        {
            operation = JsonSerializer.Deserialize<ZaTrainerPoolFixedCountSwap>(
                edit.NewValue,
                PayloadOptions);
        }
        catch (JsonException)
        {
        }

        if (operation is null
            || !ValidateOperationShape(operation, diagnostics)
            || !string.Equals(edit.RecordId, CreateRecordId(operation), StringComparison.Ordinal))
        {
            if (operation is not null
                && !string.Equals(edit.RecordId, CreateRecordId(operation), StringComparison.Ordinal))
            {
                diagnostics.Add(Error(
                    "The pending Trainer Pools operation key does not match its exact raw identities.",
                    expected: "Stable logical-pool and raw-trainer operation key",
                    code: ZaTrainerPoolsDiagnosticCodes.EditSafety));
            }

            return false;
        }

        return true;
    }

    private static bool ValidateOperationShape(
        ZaTrainerPoolFixedCountSwap operation,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(operation.SourceLogicalPoolId)
            && !string.IsNullOrWhiteSpace(operation.SourceRawTrainerId)
            && !string.IsNullOrWhiteSpace(operation.DestinationLogicalPoolId)
            && !string.IsNullOrWhiteSpace(operation.DestinationRawTrainerId))
        {
            return true;
        }

        diagnostics.Add(Error(
            "A fixed-count Trainer Pools swap requires exact source and destination logical-pool and raw-trainer identities.",
            field: "rawTrainerId",
            expected: "Non-empty exact identity values",
            code: ZaTrainerPoolsDiagnosticCodes.SelectionInvalid));
        return false;
    }

    private static string Encode(ZaTrainerPoolFixedCountSwap operation)
    {
        return JsonSerializer.Serialize(operation, PayloadOptions);
    }

    private static string CreateRecordId(ZaTrainerPoolFixedCountSwap operation)
    {
        var payload = string.Join(
            "\u001f",
            operation.SourceLogicalPoolId,
            operation.SourceRawTrainerId,
            operation.DestinationLogicalPoolId,
            operation.DestinationRawTrainerId);
        return RecordPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16];
    }

    private static string DisplayName(ZaTrainerPoolsLoadedState state, string rawTrainerId)
    {
        return state.Identities.TryGetValue(rawTrainerId, out var identity)
            ? identity.DisplayName
            : "Unknown trainer";
    }

    private static string DisplayPool(ZaTrainerPoolsLoadedState state, string logicalPoolId)
    {
        return state.Workflow.Pools.FirstOrDefault(pool => string.Equals(
            pool.LogicalPoolId,
            logicalPoolId,
            StringComparison.Ordinal))?.DisplayLabel ?? "Unknown pool";
    }

    private static bool SourcesMatch(
        IReadOnlyList<ProjectFileReference> left,
        IReadOnlyList<ProjectFileReference> right)
    {
        return left
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .SequenceEqual(right
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal));
    }

    private static ZaTrainerPoolsWorkflow OverlayWorkflow(
        ZaTrainerPoolsWorkflow workflow,
        IReadOnlyDictionary<string, ZaTrainerPoolIdentityRecord> identities,
        ZaTrainerPoolFixedCountSwap operation)
    {
        var pools = workflow.Pools.Select(pool =>
        {
            var isSource = string.Equals(
                pool.LogicalPoolId,
                operation.SourceLogicalPoolId,
                StringComparison.Ordinal);
            var isDestination = string.Equals(
                pool.LogicalPoolId,
                operation.DestinationLogicalPoolId,
                StringComparison.Ordinal);
            if (!isSource && !isDestination)
            {
                return pool;
            }

            var members = pool.Members.Select(member =>
            {
                var replacement = isSource && string.Equals(
                        member.RawTrainerId,
                        operation.SourceRawTrainerId,
                        StringComparison.Ordinal)
                    ? operation.DestinationRawTrainerId
                    : isDestination && string.Equals(
                        member.RawTrainerId,
                        operation.DestinationRawTrainerId,
                        StringComparison.Ordinal)
                        ? operation.SourceRawTrainerId
                        : member.RawTrainerId;
                if (string.Equals(replacement, member.RawTrainerId, StringComparison.Ordinal)
                    || !identities.TryGetValue(replacement, out var identity))
                {
                    return member;
                }

                return new ZaTrainerPoolMember(
                    identity.RawTrainerId,
                    identity.AppearanceAssetId,
                    identity.RawRosterId,
                    identity.RosterIndex,
                    identity.DisplayName,
                    identity.StoredRank,
                    identity.TeamSize,
                    member.Weight);
            }).ToArray();
            return pool with { Members = members };
        }).ToArray();
        return workflow with { Pools = pools };
    }

    private static ValidationDiagnostic Error(
        string message,
        string? field = null,
        string? expected = null,
        string code = ZaTrainerPoolsDiagnosticCodes.EditSafety)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.TrainerPoolsDomain,
            field: field,
            expected: expected,
            code: code);
    }

    private static ValidationDiagnostic Info(string message)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            message,
            ZaEditSessionSupport.TrainerPoolsDomain,
            code: ZaTrainerPoolsDiagnosticCodes.ReviewedState);
    }
}
