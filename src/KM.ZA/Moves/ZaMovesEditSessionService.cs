// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.BattleMoves;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.ZA.Moves;

internal sealed class ZaMovesEditSessionService
{
    private const string BattleVanillaRestoreField = "runtime.restore.battle";
    private const string TimingVanillaRestoreField = "runtime.restore.timing";
    private const string PlayerDamageVanillaRestoreField = "runtime.restore.playerDamage";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaMovesWorkflowService movesWorkflowService;

    public ZaMovesEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaMovesWorkflowService? movesWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.movesWorkflowService = movesWorkflowService ?? new ZaMovesWorkflowService(this.fileSource);
    }

    public ZaMovesEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int moveId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = movesWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.MovesDomain,
                diagnostics))
        {
            return new ZaMovesEditResult(workflow, currentSession, diagnostics);
        }

        var move = workflow.Moves.FirstOrDefault(candidate => candidate.MoveId == moveId);
        if (move is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} is not present in the loaded Moves workflow.",
                ZaEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Existing Z-A move record"));
            return new ZaMovesEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, move, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new ZaMovesEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ZaEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new ZaMovesEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaMovesEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaMoveFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = movesWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                ZaEditSessionSupport.MovesDomain,
                diagnostics))
        {
            return new ZaMovesEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var effectiveWorkflow = workflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Move batch update is missing a field or value.",
                    ZaEditSessionSupport.MovesDomain,
                    field: "updates",
                    expected: "Complete move field update"));
                continue;
            }

            var move = effectiveWorkflow.Moves.FirstOrDefault(candidate => candidate.MoveId == update.MoveId);
            if (move is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {update.MoveId} is not present in the loaded Moves workflow.",
                    ZaEditSessionSupport.MovesDomain,
                    field: "moveId",
                    expected: "Existing Z-A move record"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(effectiveWorkflow, move, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, pendingEdit);
        }

        ValidatePendingPairs(loadedWorkflow, updatedSession.PendingEdits, diagnostics);

        return new ZaMovesEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaMovesEditResult StageMoveVanilla(
        ProjectPaths paths,
        EditSession? session,
        int moveId)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = movesWorkflowService.Load(project);
        var currentWorkflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!ZaEditSessionSupport.CanEdit(
                project,
                loadedWorkflow.Summary,
                loadedWorkflow.Diagnostics,
                ZaEditSessionSupport.MovesDomain,
                diagnostics))
        {
            return new ZaMovesEditResult(currentWorkflow, currentSession, diagnostics);
        }

        var targetMove = loadedWorkflow.Moves.FirstOrDefault(candidate => candidate.MoveId == moveId);
        if (targetMove is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} is not present in the loaded Moves workflow.",
                ZaEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Existing Z-A move record"));
            return new ZaMovesEditResult(currentWorkflow, currentSession, diagnostics);
        }

        if (!targetMove.CanRevertToVanilla)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                targetMove.RevertToVanillaBlockedReason
                    ?? "This move cannot be matched safely to verified vanilla runtime data.",
                ZaEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Matching runtime variants and timing rows in the active and verified vanilla files"));
            return new ZaMovesEditResult(currentWorkflow, currentSession, diagnostics);
        }

        var recordId = moveId.ToString(CultureInfo.InvariantCulture);
        var retainedEdits = currentSession.PendingEdits
            .Where(edit =>
                !string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
                || !string.Equals(edit.RecordId, recordId, StringComparison.Ordinal))
            .ToArray();
        var removedEditCount = currentSession.PendingEdits.Count - retainedEdits.Length;
        var updatedSession = currentSession with { PendingEdits = retainedEdits };
        var stagedTableCount = 0;

        if (targetMove.WazaFlinchDiffersFromVanilla)
        {
            if (targetMove.VanillaFlinch is not { } vanillaFlinch)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Verified vanilla conventional flinch data is unavailable for the selected move.",
                    ZaEditSessionSupport.MovesDomain,
                    field: ZaMovesWorkflowService.FlinchField,
                    expected: "One exact verified vanilla Waza row"));
                return new ZaMovesEditResult(currentWorkflow, currentSession, diagnostics);
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(
                updatedSession,
                new PendingEdit(
                    ZaEditSessionSupport.MovesDomain,
                    $"Restore {targetMove.Name} conventional flinch chance to verified vanilla.",
                    [
                        new ProjectFileReference(
                            targetMove.Provenance.SourceLayer,
                            targetMove.Provenance.SourceFile),
                        new ProjectFileReference(
                            ProjectFileLayer.Base,
                            ZaDataPaths.MoveDataArray),
                    ],
                    recordId,
                    ZaMovesWorkflowService.FlinchField,
                    vanillaFlinch.ToString(CultureInfo.InvariantCulture)));
            stagedTableCount++;
        }

        if (targetMove.RuntimeBattleDiffersFromVanilla)
        {
            if (targetMove.RuntimeBattleVanillaFingerprint is not { } fingerprint)
            {
                diagnostics.Add(CreateRestoreShapeDiagnostic(
                    moveId,
                    BattleVanillaRestoreField,
                    "Verified vanilla battle rows are unavailable for the selected move."));
                return new ZaMovesEditResult(currentWorkflow, currentSession, diagnostics);
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(
                updatedSession,
                CreateRuntimeRestoreEdit(
                    targetMove,
                    BattleVanillaRestoreField,
                    ZaDataPaths.BattleMoveParameterArray,
                    targetMove.RuntimeBattleSourceLayer,
                    fingerprint,
                    "battle parameter rows"));
            stagedTableCount++;
        }

        if (targetMove.RuntimeTimingDiffersFromVanilla)
        {
            if (targetMove.RuntimeTimingVanillaFingerprint is not { } fingerprint)
            {
                diagnostics.Add(CreateRestoreShapeDiagnostic(
                    moveId,
                    TimingVanillaRestoreField,
                    "Verified vanilla timing rows are unavailable for the selected move."));
                return new ZaMovesEditResult(currentWorkflow, currentSession, diagnostics);
            }

            if (!ValidateTimingRestoreProjectileCatalog(
                    loadedWorkflow,
                    targetMove.MoveId,
                    targetMove.VanillaTimingRows,
                    diagnostics))
            {
                return new ZaMovesEditResult(currentWorkflow, currentSession, diagnostics);
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(
                updatedSession,
                CreateRuntimeRestoreEdit(
                    targetMove,
                    TimingVanillaRestoreField,
                    ZaDataPaths.MoveTimingParameterArray,
                    targetMove.RuntimeTimingSourceLayer,
                    fingerprint,
                    "timing rows",
                    loadedWorkflow.ProjectileCatalogSources));
            stagedTableCount++;
        }

        if (targetMove.RuntimePlayerDamageDiffersFromVanilla)
        {
            if (targetMove.RuntimePlayerDamageVanillaFingerprint is not { } fingerprint)
            {
                diagnostics.Add(CreateRestoreShapeDiagnostic(
                    moveId,
                    PlayerDamageVanillaRestoreField,
                    "Verified vanilla player-damage rows are unavailable for the selected move."));
                return new ZaMovesEditResult(currentWorkflow, currentSession, diagnostics);
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(
                updatedSession,
                CreateRuntimeRestoreEdit(
                    targetMove,
                    PlayerDamageVanillaRestoreField,
                    ZaDataPaths.AiAttackParamArray,
                    targetMove.RuntimePlayerDamageSourceLayer,
                    fingerprint,
                    "player-damage rows",
                    loadedWorkflow.ProjectileCatalogSources));
            stagedTableCount++;
        }

        ValidatePendingPairs(loadedWorkflow, updatedSession.PendingEdits, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaMovesEditResult(currentWorkflow, currentSession, diagnostics);
        }

        var message = stagedTableCount > 0
            ? $"Staged verified vanilla move data from {stagedTableCount.ToString(CultureInfo.InvariantCulture)} source table{(stagedTableCount == 1 ? string.Empty : "s")} for the selected move."
            : removedEditCount > 0
                ? "The selected move already matches verified vanilla values. Its pending edits were cleared."
                : "The selected move already matches verified vanilla values.";
        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            message,
            ZaEditSessionSupport.MovesDomain));
        return new ZaMovesEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = movesWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.MovesDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        ValidatePendingPairs(workflow, session.PendingEdits, diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Moves change is valid.",
                ZaEditSessionSupport.MovesDomain));
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
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Moves edit before reviewing a change plan.",
                ZaEditSessionSupport.MovesDomain,
                expected: "Pending Moves edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var workflow = movesWorkflowService.Load(project);
            var writes = new List<PlannedFileWrite>();
            AddWazaWrite(writes);
            AddRuntimeWrite(ZaDataPaths.BattleMoveParameterArray, ZaRuntimeMoveData.BattlePrefix, "battle parameters", writes);
            AddRuntimeWrite(ZaDataPaths.MoveTimingParameterArray, ZaRuntimeMoveData.TimingPrefix, "timing parameters", writes);
            AddRuntimeWrite(ZaDataPaths.AiAttackParamArray, ZaMovePlayerDamageDataDocument.FieldPrefix, "player-damage", writes);
            if (outputMode == ZaOutputMode.Standalone)
            {
                var descriptor = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptor.TargetRelativePath,
                    descriptor.Sources,
                    descriptor.ReplacesExistingOutput,
                    "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides."));
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Change plan preview contains {writes.Count} target files.",
                ZaEditSessionSupport.MovesDomain));
            return new ChangePlan(session.Id, writes, diagnostics);

            void AddWazaWrite(ICollection<PlannedFileWrite> target)
            {
                var edits = session.PendingEdits
                    .Where(IsWazaEdit)
                    .ToArray();
                if (edits.Length == 0)
                {
                    return;
                }

                var source = fileSource.Read(project, ZaDataPaths.MoveDataArray);
                var baseSource = edits.Any(edit => HasBaseSource(edit, ZaDataPaths.MoveDataArray))
                    ? fileSource.ReadBase(project, ZaDataPaths.MoveDataArray)
                    : null;
                var sourceReferences = new[]
                    {
                        new ProjectFileReference(source.SourceLayer, source.RelativePath),
                    }
                    .Concat(edits.SelectMany(edit => edit.Sources)
                        .Where(reference =>
                            reference.Layer == ProjectFileLayer.Base
                            && HasMatchingPath(reference.RelativePath, ZaDataPaths.MoveDataArray)))
                    .Distinct()
                    .ToArray();
                var info = ZaWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    ZaDataPaths.MoveDataArray,
                    sourceReferences,
                    outputMode);
                target.Add(new PlannedFileWrite(
                    info.TargetRelativePath,
                    info.Sources,
                    info.ReplacesExistingOutput,
                    $"Apply {edits.Length} pending move conventional flinch edits.",
                    CreateRuntimePlanFingerprint(
                        paths,
                        ZaDataPaths.MoveDataArray,
                        source,
                        baseSource,
                        [],
                        edits,
                        outputMode)));
            }

            void AddRuntimeWrite(
                string path,
                string prefix,
                string label,
                ICollection<PlannedFileWrite> target)
            {
                var edits = session.PendingEdits
                    .Where(edit => IsRuntimeEditForPath(edit, path, prefix))
                    .ToArray();
                if (edits.Length == 0)
                {
                    return;
                }

                var source = fileSource.Read(project, path);
                var baseSource = edits.Any(edit => IsRuntimeRestoreEditForPath(edit, path))
                        ? fileSource.ReadBase(project, path)
                        : null;
                var dependencySources = edits.Any(RequiresProjectileCatalog)
                    ? new[]
                    {
                        fileSource.Read(project, ZaDataPaths.AiBulletParamArray),
                        fileSource.ReadBase(project, ZaDataPaths.AiBulletParamArray),
                    }
                    : [];
                var sourceReferences = new[]
                    {
                        new ProjectFileReference(source.SourceLayer, source.RelativePath),
                    }
                    .Concat(edits.SelectMany(edit => edit.Sources)
                        .Where(reference =>
                            reference.Layer == ProjectFileLayer.Base
                            && HasMatchingPath(reference.RelativePath, path)))
                    .Concat(dependencySources.Select(dependency =>
                        new ProjectFileReference(dependency.SourceLayer, dependency.RelativePath)))
                    .Distinct()
                    .ToArray();
                var info = ZaWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    path,
                    sourceReferences,
                    outputMode);
                target.Add(new PlannedFileWrite(
                    info.TargetRelativePath,
                    info.Sources,
                    info.ReplacesExistingOutput,
                    $"Apply {edits.Length} pending move {label} edits.",
                    CreateRuntimePlanFingerprint(
                        paths,
                        path,
                        source,
                        baseSource,
                        dependencySources,
                        edits,
                        outputMode)));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Moves change plan could not resolve the output target: {exception.Message}",
                ZaEditSessionSupport.MovesDomain,
                expected: "Writable output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }
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
        IDisposable outputLock;
        try
        {
            outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
        }
        catch (Exception exception)
        {
            var lockDiagnostics = new[]
            {
                ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Moves output is busy or unavailable: {exception.Message}",
                    ZaEditSessionSupport.MovesDomain,
                    expected: "Exclusive access to the selected output root"),
            };
            return ZaEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                reviewedPlan,
                Array.Empty<ProjectFileReference>(),
                lockDiagnostics);
        }

        using var acquiredOutputLock = outputLock;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                ZaEditSessionSupport.MovesDomain,
                expected: "Current reviewed Moves change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var workflow = movesWorkflowService.Load(project);
            var wazaEdits = session.PendingEdits
                .Where(IsWazaEdit)
                .ToArray();
            var wazaSource = wazaEdits.Length > 0
                ? fileSource.Read(project, ZaDataPaths.MoveDataArray)
                : null;
            var wazaBaseSource = wazaEdits.Any(edit => HasBaseSource(edit, ZaDataPaths.MoveDataArray))
                ? fileSource.ReadBase(project, ZaDataPaths.MoveDataArray)
                : null;
            var battleSource = fileSource.Read(project, ZaDataPaths.BattleMoveParameterArray);
            var timingSource = fileSource.Read(project, ZaDataPaths.MoveTimingParameterArray);
            var battleEdits = session.PendingEdits
                .Where(edit => IsRuntimeEditForPath(
                    edit,
                    ZaDataPaths.BattleMoveParameterArray,
                    ZaRuntimeMoveData.BattlePrefix))
                .ToArray();
            var timingEdits = session.PendingEdits
                .Where(edit => IsRuntimeEditForPath(
                    edit,
                    ZaDataPaths.MoveTimingParameterArray,
                    ZaRuntimeMoveData.TimingPrefix))
                .ToArray();
            var playerDamageEdits = session.PendingEdits
                .Where(edit => IsRuntimeEditForPath(
                    edit,
                    ZaDataPaths.AiAttackParamArray,
                    ZaMovePlayerDamageDataDocument.FieldPrefix))
                .ToArray();
            var playerDamageSource = playerDamageEdits.Length > 0
                ? fileSource.Read(project, ZaDataPaths.AiAttackParamArray)
                : null;
            var battleBaseSource = battleEdits.Any(edit => IsBattleVanillaRestoreEdit(edit))
                    ? fileSource.ReadBase(project, ZaDataPaths.BattleMoveParameterArray)
                    : null;
            var timingBaseSource = timingEdits.Any(edit => IsTimingVanillaRestoreEdit(edit))
                    ? fileSource.ReadBase(project, ZaDataPaths.MoveTimingParameterArray)
                    : null;
            var playerDamageBaseSource = playerDamageEdits.Any(edit => IsPlayerDamageVanillaRestoreEdit(edit))
                    ? fileSource.ReadBase(project, ZaDataPaths.AiAttackParamArray)
                    : null;
            var timingDependencySources = timingEdits.Any(RequiresProjectileCatalog)
                ? new[]
                {
                    fileSource.Read(project, ZaDataPaths.AiBulletParamArray),
                    fileSource.ReadBase(project, ZaDataPaths.AiBulletParamArray),
                }
                : [];
            var playerDamageDependencySources = playerDamageEdits.Any(RequiresProjectileCatalog)
                ? new[]
                {
                    fileSource.Read(project, ZaDataPaths.AiBulletParamArray),
                    fileSource.ReadBase(project, ZaDataPaths.AiBulletParamArray),
                }
                : [];
            if ((wazaEdits.Length > 0
                    && !RuntimeSourceMatchesPlan(
                        paths,
                        currentPlan,
                        ZaDataPaths.MoveDataArray,
                        wazaSource!,
                        wazaBaseSource,
                        [],
                        wazaEdits,
                        outputMode))
                || (battleEdits.Length > 0
                    && !RuntimeSourceMatchesPlan(
                        paths,
                        currentPlan,
                        ZaDataPaths.BattleMoveParameterArray,
                        battleSource,
                        battleBaseSource,
                        [],
                        battleEdits,
                        outputMode))
                || (timingEdits.Length > 0
                    && !RuntimeSourceMatchesPlan(
                        paths,
                        currentPlan,
                        ZaDataPaths.MoveTimingParameterArray,
                        timingSource,
                        timingBaseSource,
                        timingDependencySources,
                        timingEdits,
                        outputMode))
                || (playerDamageEdits.Length > 0
                    && !RuntimeSourceMatchesPlan(
                        paths,
                        currentPlan,
                        ZaDataPaths.AiAttackParamArray,
                        playerDamageSource!,
                        playerDamageBaseSource,
                        playerDamageDependencySources,
                        playerDamageEdits,
                        outputMode)))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Move source or destination changed after review. Review the change plan again before applying.",
                    ZaEditSessionSupport.MovesDomain,
                    expected: "The exact reviewed move source and output target"));
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            var originalWazaRows = wazaSource is null ? null : ReadRows(wazaSource.Bytes);
            var wazaRows = wazaSource is null ? null : ReadRows(wazaSource.Bytes);
            var battleTable = ZaRuntimeMoveData.ReadBattle(battleSource.Bytes);
            var timingTable = ZaRuntimeMoveData.ReadTiming(timingSource.Bytes);
            var baseBattleTable = battleBaseSource is null
                ? null
                : ZaRuntimeMoveData.ReadBattle(battleBaseSource.Bytes);
            var baseTimingTable = timingBaseSource is null
                ? null
                : ZaRuntimeMoveData.ReadTiming(timingBaseSource.Bytes);
            var playerDamageDocument = playerDamageSource is null
                ? null
                : ZaMovePlayerDamageDataDocument.Parse(playerDamageSource.Bytes);
            var basePlayerDamageDocument = playerDamageBaseSource is null
                ? null
                : ZaMovePlayerDamageDataDocument.Parse(playerDamageBaseSource.Bytes);
            var playerDamageValues = playerDamageDocument?.Values.ToList();
            foreach (var edit in session.PendingEdits.OrderBy(edit => IsRuntimeRestoreEdit(edit) ? 0 : 1))
            {
                if (IsWazaEdit(edit))
                {
                    ApplyEdit(wazaRows!, edit, diagnostics);
                }
                else if (IsPlayerDamageVanillaRestoreEdit(edit)
                    || ZaMovePlayerDamageDataDocument.TryParseField(edit.Field, out _))
                {
                    ApplyPlayerDamageEdit(
                        workflow,
                        playerDamageDocument,
                        basePlayerDamageDocument,
                        playerDamageValues,
                        edit,
                        diagnostics);
                }
                else
                {
                    ApplyRuntimeEdit(
                        workflow,
                        battleTable,
                        timingTable,
                        baseBattleTable,
                        baseTimingTable,
                        edit,
                        diagnostics);
                }
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var writes = new List<ZaWorkflowFileWrite>();
            var expectedBattleRestores = CreateExpectedRestoreFingerprints(
                battleTable,
                null,
                battleEdits);
            var expectedTimingRestores = CreateExpectedRestoreFingerprints(
                null,
                timingTable,
                timingEdits);
            var expectedPlayerDamageRestores = CreateExpectedPlayerDamageRestoreFingerprints(
                playerDamageValues,
                playerDamageEdits);
            if (wazaEdits.Length > 0)
            {
                var wazaBytes = WriteRows(wazaRows!);
                ValidateWazaOutput(
                    originalWazaRows!,
                    wazaBytes,
                    wazaEdits,
                    diagnostics);
                writes.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.MoveDataArray,
                    wazaBytes));
            }

            if (battleEdits.Length > 0)
            {
                var battleBytes = battleTable.SerializeToBinary();
                ValidateRuntimeOutput(
                    workflow,
                    battleBytes,
                    null,
                    battleEdits,
                    expectedBattleRestores,
                    diagnostics);
                writes.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.BattleMoveParameterArray,
                    battleBytes));
            }

            if (timingEdits.Length > 0)
            {
                var timingBytes = timingTable.SerializeToBinary();
                ValidateRuntimeOutput(
                    workflow,
                    null,
                    timingBytes,
                    timingEdits,
                    expectedTimingRestores,
                    diagnostics);
                writes.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.MoveTimingParameterArray,
                    timingBytes));
            }

            if (playerDamageEdits.Length > 0
                && playerDamageDocument is not null
                && playerDamageValues is not null)
            {
                var playerDamageBytes = playerDamageDocument.Write(playerDamageValues);
                ValidatePlayerDamageOutput(
                    playerDamageBytes,
                    playerDamageEdits,
                    expectedPlayerDamageRestores,
                    diagnostics);
                writes.Add(new ZaWorkflowFileWrite(
                    ZaDataPaths.AiAttackParamArray,
                    playerDamageBytes));
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, [], diagnostics);
            }

            ZaWorkflowFileSource.WriteBatch(
                paths,
                writes,
                outputMode,
                revalidateReviewedState: () =>
                    ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                        reviewedPlan,
                        CreateChangePlan(paths, session, outputMode)));
            foreach (var write in writes)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(write.VirtualPath, outputMode));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Moves", outputMode),
                ZaEditSessionSupport.MovesDomain));
        }
        catch (Exception exception) when (!ZaEditSessionSupport.IsOutputSafetyException(exception))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Moves output could not be written: {exception.Message}",
                ZaEditSessionSupport.MovesDomain,
                file: $"romfs/{ZaDataPaths.BattleMoveParameterArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        ZaMovesWorkflow workflow,
        ZaMoveRecord move,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var normalizedValue = TryNormalizeEditableValue(workflow, normalizedField, value, diagnostics);
        if (normalizedValue is null)
        {
            return null;
        }

        if (!IsFieldPresent(move, normalizedField, diagnostics))
        {
            return null;
        }

        var editableField = ZaMovesWorkflowService.GetEditableField(workflow, normalizedField)!;
        var isWazaField = ZaMovesWorkflowService.IsWazaEditableField(normalizedField);
        var isBattleField = normalizedField.StartsWith(
            ZaRuntimeMoveData.BattlePrefix,
            StringComparison.Ordinal);
        var isPlayerDamageField = normalizedField.StartsWith(
            ZaMovePlayerDamageDataDocument.FieldPrefix,
            StringComparison.Ordinal);
        if (isPlayerDamageField
            && !ValidatePlayerDamageEditability(workflow, move, normalizedField, diagnostics))
        {
            return null;
        }

        var targetSource = new ProjectFileReference(
                isWazaField
                    ? move.Provenance.SourceLayer
                    : isBattleField
                    ? move.RuntimeBattleSourceLayer
                    : isPlayerDamageField
                        ? move.RuntimePlayerDamageSourceLayer
                        : move.RuntimeTimingSourceLayer,
                isWazaField
                    ? ZaDataPaths.MoveDataArray
                    : isBattleField
                    ? ZaDataPaths.BattleMoveParameterArray
                    : isPlayerDamageField
                        ? ZaDataPaths.AiAttackParamArray
                        : ZaDataPaths.MoveTimingParameterArray);
        var sources = ZaMovesWorkflowService.IsProjectileField(normalizedField)
            || isPlayerDamageField
            ? new[] { targetSource }.Concat(workflow.ProjectileCatalogSources).Distinct().ToArray()
            : [targetSource];
        return new PendingEdit(
            ZaEditSessionSupport.MovesDomain,
            $"Set {move.Name} {editableField.Label.ToLowerInvariant()} to {normalizedValue}.",
            sources,
            move.MoveId.ToString(CultureInfo.InvariantCulture),
            normalizedField,
            normalizedValue);
    }

    private static void ValidatePendingEdit(
        ZaMovesWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Moves.",
                ZaEditSessionSupport.MovesDomain,
                expected: ZaEditSessionSupport.MovesDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || workflow.Moves.FirstOrDefault(move => move.MoveId == moveId) is not { } move)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending move edit targets a record that is not loaded.",
                ZaEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Existing Z-A move record"));
            return;
        }

        if (IsRuntimeRestoreEdit(edit))
        {
            ValidateRuntimeRestoreEdit(workflow, move, edit, diagnostics);
            return;
        }

        _ = TryNormalizeEditableValue(workflow, edit.Field, edit.NewValue, diagnostics);
        if (edit.Field is not null)
        {
            _ = IsFieldPresent(move, edit.Field, diagnostics);
            var isPlayerDamageField = ZaMovePlayerDamageDataDocument.TryParseField(edit.Field, out _);
            if (isPlayerDamageField
                && !ValidatePlayerDamageEditability(workflow, move, edit.Field, diagnostics))
            {
                return;
            }

            if (ZaMovesWorkflowService.IsProjectileField(edit.Field)
                || isPlayerDamageField)
            {
                ValidateProjectileCatalogProvenance(workflow, edit, diagnostics);
            }
            ValidateVanillaRestoreValue(move, edit, diagnostics);
        }
    }

    private static bool ValidatePlayerDamageEditability(
        ZaMovesWorkflow workflow,
        ZaMoveRecord move,
        string field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (workflow.ProjectileCatalogSources.Count == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Boss player damage cannot be edited because the active and verified-base bullet catalogs are unavailable.",
                ZaEditSessionSupport.MovesDomain,
                field: field,
                expected: "Verified active and base BulletParam provenance"));
            return false;
        }

        if (!ZaMovePlayerDamageDataDocument.TryParseField(field, out var attackId)
            || move.PlayerDamageRows.FirstOrDefault(row => row.AttackId == attackId) is not { } damageRow
            || damageRow.Invocations.Count == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Boss player damage cannot be edited because no active BulletParam row invokes this Attack ID.",
                ZaEditSessionSupport.MovesDomain,
                field: field,
                expected: "At least one active damage-bearing BulletParam consumer"));
            return false;
        }

        return true;
    }

    private static void ValidatePendingPairs(
        ZaMovesWorkflow workflow,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editedMoveIds = edits
            .Where(edit => string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal))
            .Select(edit => int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
                ? moveId
                : (int?)null)
            .Where(moveId => moveId is not null)
            .Select(moveId => moveId!.Value)
            .Distinct()
            .ToHashSet();
        var editedFieldsByMove = edits
            .Where(edit => string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
                           && int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                           && edit.Field is not null)
            .GroupBy(edit => int.Parse(edit.RecordId!, NumberStyles.None, CultureInfo.InvariantCulture))
            .ToDictionary(
                group => group.Key,
                group => group.Select(edit => edit.Field!).ToHashSet(StringComparer.Ordinal));

        if (editedMoveIds.Count == 0)
        {
            return;
        }

        var overlaidWorkflow = OverlayPendingEdits(workflow, edits);
        foreach (var move in overlaidWorkflow.Moves.Where(move => editedMoveIds.Contains(move.MoveId)))
        {
            var editedFields = editedFieldsByMove.GetValueOrDefault(move.MoveId) ?? [];
            if (!move.HasRuntimeData && move.HitMin > move.HitMax)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {move.MoveId} has a minimum hit count greater than its maximum hit count.",
                    ZaEditSessionSupport.MovesDomain,
                    field: "hit",
                    expected: "Minimum hits less than or equal to maximum hits"));
            }

            if (!move.HasRuntimeData && move.TurnMin > move.TurnMax)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {move.MoveId} has a minimum inflict turn count greater than its maximum inflict turn count.",
                    ZaEditSessionSupport.MovesDomain,
                    field: "turn",
                    expected: "Minimum turns less than or equal to maximum turns"));
            }
            foreach (var variant in move.RuntimeVariants)
            {
                if (variant.ConditionTurnMin > variant.ConditionTurnMax)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{FormatRuntimeVariantLabel(variant.Variant)} data for move {move.MoveId} has a minimum condition turn count greater than its maximum.",
                        ZaEditSessionSupport.MovesDomain,
                        field: ZaRuntimeMoveData.BattleField(variant.Variant, "conditionTurnMin"),
                        expected: "Minimum condition turns less than or equal to maximum condition turns"));
                }


                if ((editedFields.Contains(ZaRuntimeMoveData.BattleField(variant.Variant, "damageRecoverRatio"))
                     || editedFields.Contains(ZaRuntimeMoveData.BattleField(variant.Variant, "damageDrainRatio")))
                    && variant.DamageRecoverRatio != 0
                    && variant.DamageDrainRatio != 0)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{FormatRuntimeVariantLabel(variant.Variant)} data for move {move.MoveId} cannot use recovery/recoil and damage drain together.",
                        ZaEditSessionSupport.MovesDomain,
                        field: ZaRuntimeMoveData.BattleField(variant.Variant, "damageRecoverRatio"),
                        expected: "At most one nonzero damage recovery or drain ratio"));
                }

                if ((editedFields.Contains(ZaRuntimeMoveData.BattleField(variant.Variant, "hpRecoverRatio"))
                     || editedFields.Contains(ZaRuntimeMoveData.BattleField(variant.Variant, "restoresHp")))
                    && variant.HpRecoverRatio != 0
                    && !variant.RestoresHp)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"{FormatRuntimeVariantLabel(variant.Variant)} data for move {move.MoveId} has HP recovery but its Restores HP flag is disabled.",
                        ZaEditSessionSupport.MovesDomain,
                        field: ZaRuntimeMoveData.BattleField(variant.Variant, "restoresHp"),
                        expected: "Restores HP enabled when HP recovery is nonzero"));
                }

                var hasEditedStatChange = variant.StatChanges.Any(stat =>
                {
                    var statPrefix = $"stat{stat.Slot}";
                    return editedFields.Contains(ZaRuntimeMoveData.BattleField(variant.Variant, statPrefix))
                        || editedFields.Contains(ZaRuntimeMoveData.BattleField(variant.Variant, statPrefix + "Stage"))
                        || editedFields.Contains(ZaRuntimeMoveData.BattleField(variant.Variant, statPrefix + "Percent"));
                });
                if (hasEditedStatChange)
                {
                    var encounteredUnused = false;
                    var occupiedStats = new List<int>();
                    foreach (var stat in variant.StatChanges.OrderBy(stat => stat.Slot))
                    {
                        var validUnused = stat.Stat == 0 && stat.Stage == 0 && stat.Percent == 0;
                        var validOccupied = stat.Stat != 0 && stat.Stage != 0;
                        if (!validUnused && !validOccupied)
                        {
                            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                                DiagnosticSeverity.Error,
                                $"{FormatRuntimeVariantLabel(variant.Variant)} data for move {move.MoveId} has an incomplete stat change in slot {stat.Slot}.",
                                ZaEditSessionSupport.MovesDomain,
                                field: ZaRuntimeMoveData.BattleField(variant.Variant, $"stat{stat.Slot}"),
                                expected: "Unused stat with zero stage/chance, or occupied stat with a nonzero stage"));
                            continue;
                        }

                        if (validUnused)
                        {
                            encounteredUnused = true;
                            continue;
                        }

                        occupiedStats.Add(stat.Stat);
                        if (encounteredUnused)
                        {
                            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                                DiagnosticSeverity.Error,
                                $"{FormatRuntimeVariantLabel(variant.Variant)} data for move {move.MoveId} has a stat change after an empty slot.",
                                ZaEditSessionSupport.MovesDomain,
                                field: ZaRuntimeMoveData.BattleField(variant.Variant, $"stat{stat.Slot}"),
                                expected: "Stat changes packed contiguously from slot 1"));
                        }
                    }

                    if (occupiedStats.Count != occupiedStats.Distinct().Count())
                    {
                        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            $"{FormatRuntimeVariantLabel(variant.Variant)} data for move {move.MoveId} repeats the same stat in more than one slot.",
                            ZaEditSessionSupport.MovesDomain,
                            field: ZaRuntimeMoveData.BattleField(variant.Variant, "stat1"),
                            expected: "Each changed stat selected at most once"));
                    }

                    if (occupiedStats.Contains(9) && occupiedStats.Count > 1)
                    {
                        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            $"{FormatRuntimeVariantLabel(variant.Variant)} data for move {move.MoveId} combines All Stats with another stat change.",
                            ZaEditSessionSupport.MovesDomain,
                            field: ZaRuntimeMoveData.BattleField(variant.Variant, "stat1"),
                            expected: "All Stats used by itself, or individual stats without All Stats"));
                    }
                }
            }

            foreach (var timing in move.TimingRows)
            {
                if ((IsTimingMemberEdited(editedFields, move.MoveId, timing, "rangeMin")
                     || IsTimingMemberEdited(editedFields, move.MoveId, timing, "rangeMax"))
                    && timing.RangeMin > timing.RangeMax)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Move {move.MoveId} {FormatTimingProfileLabel(timing)} has a minimum range greater than its maximum.",
                        ZaEditSessionSupport.MovesDomain,
                        field: ZaRuntimeMoveData.TimingField(
                            timing.TimingMoveId,
                            timing.Occurrence,
                            "rangeMin"),
                        expected: "Minimum range less than or equal to maximum range"));
                }

                if ((IsTimingMemberEdited(editedFields, move.MoveId, timing, "projectileCountMin")
                     || IsTimingMemberEdited(editedFields, move.MoveId, timing, "projectileCountMax"))
                    && timing.ProjectileCountMin > timing.ProjectileCountMax)
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Move {move.MoveId} {FormatTimingProfileLabel(timing)} has a minimum projectile count greater than its maximum.",
                        ZaEditSessionSupport.MovesDomain,
                        field: ZaRuntimeMoveData.TimingField(
                            timing.TimingMoveId,
                            timing.Occurrence,
                            "projectileCountMin"),
                        expected: "Minimum projectile count less than or equal to maximum projectile count"));
                }

                if (editedFields.Any(field =>
                        TimingFieldTargetsRecord(move.MoveId, timing, field, out var member)
                        && ZaRuntimeMoveData.IsProjectileMember(member)))
                {
                    ValidateProjectilePairs(move.MoveId, timing, diagnostics);
                }
            }
        }
    }

    private static void ValidateProjectilePairs(
        int moveId,
        ZaMoveTimingRecord timing,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var pairs = new[]
        {
            (timing.OverwriteProjectile1, timing.ReplacementProjectile1),
            (timing.OverwriteProjectile2, timing.ReplacementProjectile2),
            (timing.OverwriteProjectile3, timing.ReplacementProjectile3),
            (timing.OverwriteProjectile4, timing.ReplacementProjectile4),
            (timing.OverwriteProjectile5, timing.ReplacementProjectile5),
        };
        var encounteredEmpty = false;
        for (var index = 0; index < pairs.Length; index++)
        {
            var (overwrite, replacement) = pairs[index];
            var overwriteEmpty = overwrite == 0;
            var replacementEmpty = replacement == 0;
            var field = ZaRuntimeMoveData.TimingField(
                timing.TimingMoveId,
                timing.Occurrence,
                $"overwriteProjectile{index + 1}");
            if (overwriteEmpty != replacementEmpty)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {moveId} {FormatTimingProfileLabel(timing)} projectile pair {index + 1} must set both IDs or clear both.",
                    ZaEditSessionSupport.MovesDomain,
                    field: field,
                    expected: "Both projectile IDs zero or both nonzero"));
            }

            if (overwriteEmpty && replacementEmpty)
            {
                encounteredEmpty = true;
            }
            else if (encounteredEmpty)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {moveId} {FormatTimingProfileLabel(timing)} projectile pairs must be populated contiguously from slot 1.",
                    ZaEditSessionSupport.MovesDomain,
                    field: field,
                    expected: "No populated projectile pair after an empty slot"));
            }
        }
    }

    private static bool ValidateTimingRestoreProjectileCatalog(
        ZaMovesWorkflow workflow,
        int moveId,
        IReadOnlyList<ZaMoveTimingRecord> timingRows,
        ICollection<ValidationDiagnostic> diagnostics) =>
        ValidateTimingRestoreProjectileCatalog(
            workflow,
            moveId,
            timingRows.SelectMany(GetProjectileIds),
            diagnostics);

    private static bool ValidateTimingRestoreProjectileCatalog(
        ZaMovesWorkflow workflow,
        int moveId,
        IReadOnlyList<ZaMoveTimingParameterT> timingRows,
        ICollection<ValidationDiagnostic> diagnostics) =>
        ValidateTimingRestoreProjectileCatalog(
            workflow,
            moveId,
            timingRows.SelectMany(GetProjectileIds),
            diagnostics);

    private static bool ValidateTimingRestoreProjectileCatalog(
        ZaMovesWorkflow workflow,
        int moveId,
        IEnumerable<int> projectileIds,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var activeProjectileIds = workflow.ProjectileOptions
            .Where(option => option.Value >= 0
                             && option.Value <= int.MaxValue
                             && option.Value == Math.Truncate(option.Value))
            .Select(option => checked((int)option.Value))
            .ToHashSet();
        if (!activeProjectileIds.Contains(0)
            || workflow.ProjectileCatalogSources.Count == 0)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} timing rows cannot be restored because the active bullet catalog could not be verified.",
                ZaEditSessionSupport.MovesDomain,
                field: TimingVanillaRestoreField,
                expected: "A structurally valid active and verified-base bullet parameter catalog"));
            return false;
        }

        var missingIds = projectileIds
            .Where(projectileId => projectileId != 0 && !activeProjectileIds.Contains(projectileId))
            .Distinct()
            .Order()
            .ToArray();
        if (missingIds.Length == 0)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Move {moveId} timing rows cannot be restored because verified vanilla projectile IDs "
            + $"{string.Join(", ", missingIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))} "
            + "are absent from the active bullet catalog.",
            ZaEditSessionSupport.MovesDomain,
            field: TimingVanillaRestoreField,
            expected: "Every nonzero restored projectile ID present in the active bullet catalog"));
        return false;
    }

    private static bool ValidateProjectileCatalogProvenance(
        ZaMovesWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var hasCompleteProvenance = workflow.ProjectileCatalogSources.Count > 0
            && workflow.ProjectileCatalogSources.All(required =>
                edit.Sources.Any(actual =>
                    actual.Layer == required.Layer
                    && HasMatchingPath(actual.RelativePath, required.RelativePath)));
        if (hasCompleteProvenance)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "A projectile-dependent move edit is missing its reviewed active/base bullet-catalog provenance.",
            ZaEditSessionSupport.MovesDomain,
            field: edit.Field,
            expected: "Current active and verified-base bullet parameter sources"));
        return false;
    }

    private static IEnumerable<int> GetProjectileIds(ZaMoveTimingRecord timing)
    {
        yield return timing.OverwriteProjectile1;
        yield return timing.ReplacementProjectile1;
        yield return timing.OverwriteProjectile2;
        yield return timing.ReplacementProjectile2;
        yield return timing.OverwriteProjectile3;
        yield return timing.ReplacementProjectile3;
        yield return timing.OverwriteProjectile4;
        yield return timing.ReplacementProjectile4;
        yield return timing.OverwriteProjectile5;
        yield return timing.ReplacementProjectile5;
    }

    private static IEnumerable<int> GetProjectileIds(ZaMoveTimingParameterT timing)
    {
        yield return timing.OverwriteProjectile1;
        yield return timing.ReplacementProjectile1;
        yield return timing.OverwriteProjectile2;
        yield return timing.ReplacementProjectile2;
        yield return timing.OverwriteProjectile3;
        yield return timing.ReplacementProjectile3;
        yield return timing.OverwriteProjectile4;
        yield return timing.ReplacementProjectile4;
        yield return timing.OverwriteProjectile5;
        yield return timing.ReplacementProjectile5;
    }

    private static bool ValidateImmediatePairs(
        ZaMoveRecord selectedMove,
        string field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var hitMin = field == ZaMovesWorkflowService.HitMinField ? value : selectedMove.HitMin;
        var hitMax = field == ZaMovesWorkflowService.HitMaxField ? value : selectedMove.HitMax;
        var turnMin = field == ZaMovesWorkflowService.TurnMinField ? value : selectedMove.TurnMin;
        var turnMax = field == ZaMovesWorkflowService.TurnMaxField ? value : selectedMove.TurnMax;

        if (hitMin > hitMax)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Move minimum hits cannot be greater than the current maximum hits.",
                ZaEditSessionSupport.MovesDomain,
                field: ZaMovesWorkflowService.HitMinField,
                expected: "Minimum hits less than or equal to maximum hits"));
            return false;
        }

        if (turnMin > turnMax)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Move minimum inflict turns cannot be greater than the current maximum inflict turns.",
                ZaEditSessionSupport.MovesDomain,
                field: ZaMovesWorkflowService.TurnMinField,
                expected: "Minimum turns less than or equal to maximum turns"));
            return false;
        }

        return true;
    }

    private static int? TryParseEditableValue(
        ZaMovesWorkflow? workflow,
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = workflow is null
            ? ZaMovesWorkflowService.GetEditableField(field)
            : ZaMovesWorkflowService.GetEditableField(workflow, field);
        if (editableField is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move field '{field ?? "(missing)"}' is not supported by Pokemon Legends Z-A Moves yet.",
                ZaEditSessionSupport.MovesDomain,
                field: "field",
                expected: "Supported Z-A move field"));
            return null;
        }

        var parsedValue = editableField.ValueKind == "boolean"
            ? TryParseBooleanValue(value, out var booleanValue) ? booleanValue : (int?)null
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue)
                ? integerValue
                : (int?)null;

        if (parsedValue is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be a valid {editableField.ValueKind} value.",
                ZaEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: $"Safe move {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (parsedValue.Value < (editableField.MinimumValue ?? int.MinValue)
            || parsedValue.Value > (editableField.MaximumValue ?? int.MaxValue))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                ZaEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: $"Safe move {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (editableField.Options.Count > 0
            && editableField.Options.All(option => option.Value != parsedValue.Value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must use one of the supported game values.",
                ZaEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: string.Join(
                    ", ",
                    editableField.Options.Select(option => option.Value.ToString(CultureInfo.InvariantCulture)))));
            return null;
        }

        if (workflow is not null
            && ZaMovesWorkflowService.IsProjectileField(field)
            && workflow.ProjectileOptions.All(option => option.Value != parsedValue.Value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must reference None or a BulletId in the active bullet catalog.",
                ZaEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: "0 or an active bullet parameter BulletId"));
            return null;
        }

        return parsedValue.Value;
    }

    private static string CreateRuntimePlanFingerprint(
        ProjectPaths paths,
        string virtualPath,
        ZaWorkflowFile source,
        ZaWorkflowFile? baseSource,
        IReadOnlyList<ZaWorkflowFile> dependencySources,
        IReadOnlyList<PendingEdit> edits,
        ZaOutputMode outputMode)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(source.RelativePath);
        Append(source.SourceLayer.ToString());
        hash.AppendData(source.Bytes);
        if (baseSource is not null)
        {
            Append("VerifiedBase");
            Append(baseSource.RelativePath);
            Append(baseSource.SourceLayer.ToString());
            hash.AppendData(baseSource.Bytes);
        }
        foreach (var dependency in dependencySources
                     .OrderBy(dependency => dependency.SourceLayer)
                     .ThenBy(dependency => dependency.RelativePath, StringComparer.Ordinal))
        {
            Append("ReadOnlyDependency");
            Append(dependency.RelativePath);
            Append(dependency.SourceLayer.ToString());
            hash.AppendData(dependency.Bytes);
        }
        foreach (var edit in edits.OrderBy(edit => edit.RecordId, StringComparer.Ordinal).ThenBy(edit => edit.Field, StringComparer.Ordinal))
        {
            Append(edit.RecordId);
            Append(edit.Field);
            Append(edit.NewValue);
            foreach (var editSource in edit.Sources
                .OrderBy(editSource => editSource.Layer)
                .ThenBy(editSource => editSource.RelativePath, StringComparer.Ordinal))
            {
                Append(editSource.Layer.ToString());
                Append(editSource.RelativePath);
            }
        }

        var targetPath = ZaWorkflowFileSource.ResolveOutputPath(paths, virtualPath, outputMode);
        Append(targetPath);
        if (File.Exists(targetPath))
        {
            hash.AppendData(File.ReadAllBytes(targetPath));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void Append(string? value)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value ?? "<null>"));
            hash.AppendData([0]);
        }
    }

    private static bool RuntimeSourceMatchesPlan(
        ProjectPaths paths,
        ChangePlan plan,
        string virtualPath,
        ZaWorkflowFile source,
        ZaWorkflowFile? baseSource,
        IReadOnlyList<ZaWorkflowFile> dependencySources,
        IReadOnlyList<PendingEdit> edits,
        ZaOutputMode outputMode)
    {
        var targetRelativePath = ZaWorkflowFileSource.CreatePlannedWrite(
            paths,
            virtualPath,
            Array.Empty<ProjectFileReference>(),
            outputMode).TargetRelativePath;
        var write = plan.Writes.FirstOrDefault(candidate => string.Equals(
            candidate.TargetRelativePath,
            targetRelativePath,
            StringComparison.Ordinal));
        return write is not null
            && string.Equals(
                write.SourceFingerprint,
                CreateRuntimePlanFingerprint(
                    paths,
                    virtualPath,
                    source,
                    baseSource,
                    dependencySources,
                    edits,
                    outputMode),
                StringComparison.Ordinal);
    }

    private static string? TryNormalizeEditableValue(
        ZaMovesWorkflow? workflow,
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = workflow is null
            ? ZaMovesWorkflowService.GetEditableField(field)
            : ZaMovesWorkflowService.GetEditableField(workflow, field);
        if (editableField?.ValueKind != "decimal")
        {
            var integer = TryParseEditableValue(workflow, field, value, diagnostics);
            return integer?.ToString(CultureInfo.InvariantCulture);
        }

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !float.IsFinite(parsed))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be a valid decimal value.",
                ZaEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: $"Safe move {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        // The table stores these values as float32. Compare against the same
        // representation so decimal bounds such as -0.1 remain inclusive
        // after parsing instead of being rejected by float-to-double drift.
        var minimumValue = editableField.MinimumValue is { } minimum
            ? checked((float)minimum)
            : float.MinValue;
        var maximumValue = editableField.MaximumValue is { } maximum
            ? checked((float)maximum)
            : float.MaxValue;
        if (parsed < minimumValue || parsed > maximumValue)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                ZaEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: $"Safe move {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (editableField.Options.Count > 0
            && editableField.Options.All(option => Math.Abs(option.Value - parsed) > 0.00001))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must use one of the supported game values.",
                ZaEditSessionSupport.MovesDomain,
                field: editableField.Field,
                expected: string.Join(
                    ", ",
                    editableField.Options.Select(option => option.Value.ToString(CultureInfo.InvariantCulture)))));
            return null;
        }

        return parsed.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatRuntimeVariantLabel(int variant) => variant switch
    {
        0 => "Normal Move",
        1 => "Plus Move",
        2 => "Boss Move",
        _ => $"runtime variant {variant}"
    };

    private static string FormatTimingProfileLabel(ZaMoveTimingRecord timing) =>
        $"{FormatRuntimeVariantLabel(timing.Variant)} timing profile "
        + $"{timing.TimingMoveId}, occurrence {timing.Occurrence}";

    private static int ResolveTimingMoveId(int moveId, int? timingMoveId) =>
        timingMoveId ?? moveId;

    private static bool TimingFieldTargetsRecord(
        int moveId,
        ZaMoveTimingRecord timing,
        string field,
        out string member)
    {
        member = string.Empty;
        return ZaRuntimeMoveData.TryParseTimingField(
                field,
                out var timingMoveId,
                out var occurrence,
                out member)
            && ResolveTimingMoveId(moveId, timingMoveId) == timing.TimingMoveId
            && (occurrence is null || occurrence.Value == timing.Occurrence);
    }

    private static bool IsTimingMemberEdited(
        IReadOnlySet<string> editedFields,
        int moveId,
        ZaMoveTimingRecord timing,
        string member) =>
        editedFields.Any(field =>
            TimingFieldTargetsRecord(moveId, timing, field, out var parsedMember)
            && string.Equals(parsedMember, member, StringComparison.Ordinal));

    private static string? GetEditableValue(ZaMoveRecord move, string field)
    {
        if (string.Equals(field, ZaMovesWorkflowService.FlinchField, StringComparison.Ordinal))
        {
            return move.Flinch.ToString(CultureInfo.InvariantCulture);
        }

        if (ZaMovePlayerDamageDataDocument.TryParseField(field, out var attackId))
        {
            var playerDamage = move.PlayerDamageRows
                .FirstOrDefault(row => row.AttackId == attackId);
            return playerDamage?.PlayerDamage.ToString(CultureInfo.InvariantCulture);
        }

        if (ZaRuntimeMoveData.TryParseBattleField(field, out var variant, out var battleMember))
        {
            var runtimeVariant = move.RuntimeVariants.FirstOrDefault(candidate => candidate.Variant == variant);
            return runtimeVariant is null
                ? null
                : ZaRuntimeMoveData.GetValue(runtimeVariant, battleMember);
        }

        if (ZaRuntimeMoveData.TryParseTimingField(
                field,
                out var timingMoveId,
                out var occurrence,
                out var timingMember))
        {
            var resolvedTimingMoveId = ResolveTimingMoveId(move.MoveId, timingMoveId);
            var timing = move.TimingRows.FirstOrDefault(candidate =>
                candidate.TimingMoveId == resolvedTimingMoveId
                && (occurrence is null || candidate.Occurrence == occurrence.Value));
            return timing is null ? null : ZaRuntimeMoveData.GetValue(timing, timingMember);
        }

        return null;
    }

    private static bool NumericValuesEqual(string left, string right)
    {
        return double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftValue)
            && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightValue)
            && Math.Abs(leftValue - rightValue) <= 0.00001;
    }

    private static void ValidateVanillaRestoreValue(
        ZaMoveRecord move,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (edit.Field is null
            || !HasVanillaRestoreSourceMarker(edit, edit.Field))
        {
            return;
        }

        var activeSourceLayer = edit.Field.StartsWith(
            ZaMovesWorkflowService.FlinchField,
            StringComparison.Ordinal)
                ? move.Provenance.SourceLayer
                : edit.Field.StartsWith(
                    ZaRuntimeMoveData.BattlePrefix,
                    StringComparison.Ordinal)
                ? move.RuntimeBattleSourceLayer
                : edit.Field.StartsWith(
                    ZaMovePlayerDamageDataDocument.FieldPrefix,
                    StringComparison.Ordinal)
                    ? move.RuntimePlayerDamageSourceLayer
                    : move.RuntimeTimingSourceLayer;
        if (activeSourceLayer == ProjectFileLayer.Base)
        {
            return;
        }

        var vanillaValue = move.VanillaValues.FirstOrDefault(value =>
            string.Equals(value.Field, edit.Field, StringComparison.Ordinal));
        if (vanillaValue is not null
            && edit.NewValue is not null
            && NumericValuesEqual(vanillaValue.Value, edit.NewValue))
        {
            return;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "A staged move restoration no longer matches the verified vanilla value.",
            ZaEditSessionSupport.MovesDomain,
            field: edit.Field,
            expected: vanillaValue?.Value ?? "Matching verified vanilla runtime field"));
    }

    private static bool HasVanillaRestoreSourceMarker(PendingEdit edit, string field)
    {
        var virtualPath = string.Equals(field, ZaMovesWorkflowService.FlinchField, StringComparison.Ordinal)
            ? ZaDataPaths.MoveDataArray
            : field.StartsWith(ZaRuntimeMoveData.BattlePrefix, StringComparison.Ordinal)
            ? ZaDataPaths.BattleMoveParameterArray
            : field.StartsWith(ZaMovePlayerDamageDataDocument.FieldPrefix, StringComparison.Ordinal)
                ? ZaDataPaths.AiAttackParamArray
                : ZaDataPaths.MoveTimingParameterArray;
        return HasBaseSource(edit, virtualPath);
    }

    private static bool HasBaseSource(PendingEdit edit, string virtualPath) =>
        edit.Sources.Any(source =>
            source.Layer == ProjectFileLayer.Base
            && HasMatchingPath(source.RelativePath, virtualPath));

    private static bool HasMatchingPath(string relativePath, string virtualPath) =>
        string.Equals(relativePath, virtualPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(relativePath, $"romfs/{virtualPath}", StringComparison.OrdinalIgnoreCase);

    private static bool IsFieldPresent(
        ZaMoveRecord move,
        string field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (ZaMovesWorkflowService.IsWazaEditableField(field))
        {
            return true;
        }

        var isPlayerDamage = ZaMovePlayerDamageDataDocument.TryParseField(field, out var attackId);
        if (isPlayerDamage)
        {
            var playerDamageRow = move.PlayerDamageRows.FirstOrDefault(row => row.AttackId == attackId);
            if (playerDamageRow is not null
                && ZaMovePlayerDamageDataDocument.IsForBaseMove(playerDamageRow.RuntimeMoveId, move.MoveId))
            {
                return true;
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{move.Name} does not contain player-damage AttackId {attackId} for its Boss Move.",
                ZaEditSessionSupport.MovesDomain,
                field: field,
                expected: "An exact player-damage AttackId associated with the selected Boss Move",
                code: ZaMovesDiagnosticCodes.RuntimeFieldMissing));
            return false;
        }

        var isBattle = ZaRuntimeMoveData.TryParseBattleField(field, out var variant, out _);
        if (isBattle && move.AmbiguousRuntimeVariantIds.Contains(variant))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{move.Name} contains divergent duplicate rows for {FormatRuntimeVariantLabel(variant)}; editing is disabled for that ambiguous variant.",
                ZaEditSessionSupport.MovesDomain,
                field: field,
                expected: "Byte-identical duplicate rows for a shared runtime variant",
                code: ZaMovesDiagnosticCodes.RuntimeVariantAmbiguous));
            return false;
        }

        var isTiming = ZaRuntimeMoveData.TryParseTimingField(
            field,
            out var timingMoveId,
            out var occurrence,
            out _);
        var present = isBattle
            ? move.RuntimeVariants.Any(candidate => candidate.Variant == variant)
            : isTiming && move.TimingRows.Any(candidate =>
                  candidate.TimingMoveId == ResolveTimingMoveId(move.MoveId, timingMoveId)
                  && (occurrence is null || candidate.Occurrence == occurrence.Value));
        if (present)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"{move.Name} does not contain the selected runtime move field.",
            ZaEditSessionSupport.MovesDomain,
            field: field,
            expected: "A runtime variant or timing row present in the source data",
            code: isTiming
                ? ZaMovesDiagnosticCodes.TimingProfileMissing
                : ZaMovesDiagnosticCodes.RuntimeFieldMissing));
        return false;
    }

    private static bool TryParseBooleanValue(string? value, out int parsedValue)
    {
        parsedValue = 0;
        if (string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = 1;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.Ordinal)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static ZaMovesWorkflow OverlayPendingEdits(
        ZaMovesWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits.OrderBy(edit => IsRuntimeRestoreEdit(edit) ? 0 : 1))
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static ZaMovesWorkflow OverlayPendingEdit(ZaMovesWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId))
        {
            return workflow;
        }

        if (IsRuntimeRestoreEdit(edit))
        {
            return workflow with
            {
                Moves = workflow.Moves
                    .Select(move => move.MoveId == moveId ? OverlayRuntimeRestore(move, edit) : move)
                    .ToArray(),
            };
        }

        if (TryNormalizeEditableValue(workflow, edit.Field, edit.NewValue, new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        return workflow with
        {
            Moves = workflow.Moves
                .Select(move => move.MoveId == moveId
                    ? ZaMovesWorkflowService.IsWazaEditableField(edit.Field)
                        ? OverlayMoveField(
                            move,
                            edit.Field!,
                            int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture))
                        : OverlayRuntimeMoveField(workflow, move, edit.Field!, value)
                    : move)
                .ToArray(),
        };
    }

    private static ZaMoveRecord OverlayRuntimeRestore(ZaMoveRecord move, PendingEdit edit)
    {
        if (IsBattleVanillaRestoreEdit(edit))
        {
            var restored = move with { RuntimeVariants = move.VanillaRuntimeVariants };
            var primary = restored.RuntimeVariants.FirstOrDefault(row => row.Variant == 0)
                ?? restored.RuntimeVariants.FirstOrDefault();
            return primary is null
                ? restored
                : restored with
                {
                    Type = primary.Type,
                    TypeName = primary.TypeName,
                    Category = primary.DamageType,
                    CategoryName = primary.DamageTypeName,
                    Power = primary.Power,
                    CritStage = primary.CriticalRank,
                    TurnMin = primary.ConditionTurnMin,
                    TurnMax = primary.ConditionTurnMax,
                    Inflict = primary.ConditionId,
                    InflictName = ZaMovesWorkflowService.FormatInflict(primary.ConditionId),
                    InflictPercent = primary.ConditionPercent,
                    RawInflictCount = primary.ConditionCount,
                    Recoil = primary.DamageDrainRatio,
                    RawHealing = primary.HpRecoverRatio,
                    StatChanges = primary.StatChanges,
                };
        }

        if (IsTimingVanillaRestoreEdit(edit))
        {
            var timing = move.VanillaTimingRows.FirstOrDefault(row => row.TimingMoveId == move.MoveId)
                ?? move.VanillaTimingRows.FirstOrDefault();
            return move with
            {
                Timing = timing,
                TimingRows = move.VanillaTimingRows,
                Accuracy = timing?.HitPercent ?? move.Accuracy,
                HitMin = timing?.ProjectileCountMin ?? move.HitMin,
                HitMax = timing?.ProjectileCountMax ?? move.HitMax,
            };
        }

        if (IsPlayerDamageVanillaRestoreEdit(edit))
        {
            var vanillaDamageByAttackId = move.VanillaPlayerDamageRows
                .ToDictionary(row => row.AttackId, row => row.PlayerDamage);
            return move with
            {
                PlayerDamageRows = move.PlayerDamageRows
                    .Select(row => vanillaDamageByAttackId.TryGetValue(
                            row.AttackId,
                            out var vanillaPlayerDamage)
                        ? row with { PlayerDamage = vanillaPlayerDamage }
                        : row)
                    .ToArray(),
            };
        }

        return move;
    }

    private static ZaMoveRecord OverlayRuntimeMoveField(
        ZaMovesWorkflow workflow,
        ZaMoveRecord move,
        string field,
        string value)
    {
        if (ZaMovePlayerDamageDataDocument.TryParseField(field, out var attackId)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerDamage))
        {
            return move with
            {
                PlayerDamageRows = move.PlayerDamageRows
                    .Select(row => row.AttackId == attackId
                        ? row with { PlayerDamage = playerDamage }
                        : row)
                    .ToArray(),
            };
        }

        if (ZaRuntimeMoveData.TryParseBattleField(field, out var variant, out var member)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            var updatedVariants = move.RuntimeVariants
                .Select(row => row.Variant == variant ? OverlayRuntimeVariant(row, member, integer) : row)
                .ToArray();
            var result = move with { RuntimeVariants = updatedVariants };
            var primary = updatedVariants.FirstOrDefault(row => row.Variant == 0) ?? updatedVariants.FirstOrDefault();
            return primary is null ? result : result with
            {
                Type = primary.Type,
                TypeName = primary.TypeName,
                Category = primary.DamageType,
                CategoryName = primary.DamageTypeName,
                Power = primary.Power,
                CritStage = primary.CriticalRank,
                TurnMin = primary.ConditionTurnMin,
                TurnMax = primary.ConditionTurnMax,
                Inflict = primary.ConditionId,
                InflictName = ZaMovesWorkflowService.FormatInflict(primary.ConditionId),
                InflictPercent = primary.ConditionPercent,
                RawInflictCount = primary.ConditionCount,
                Recoil = primary.DamageDrainRatio,
                RawHealing = primary.HpRecoverRatio,
                StatChanges = primary.StatChanges,
            };
        }

        if (ZaRuntimeMoveData.TryParseTimingField(
                field,
                out var timingMoveId,
                out var occurrence,
                out member))
        {
            var resolvedTimingMoveId = ResolveTimingMoveId(move.MoveId, timingMoveId);
            var updatedRows = move.TimingRows
                .Select(row => row.TimingMoveId == resolvedTimingMoveId
                               && (occurrence is null || row.Occurrence == occurrence.Value)
                    ? OverlayTimingRecord(row, member, value, workflow.SpawnLocators)
                    : row)
                .ToArray();
            var updatedTiming = updatedRows.FirstOrDefault(row => row.TimingMoveId == move.MoveId)
                ?? updatedRows.FirstOrDefault();
            return move with
            {
                Timing = updatedTiming,
                TimingRows = updatedRows,
                Accuracy = updatedTiming?.HitPercent ?? move.Accuracy,
                HitMin = updatedTiming?.ProjectileCountMin ?? move.HitMin,
                HitMax = updatedTiming?.ProjectileCountMax ?? move.HitMax,
            };
        }

        return move;
    }

    private static ZaMoveTimingRecord OverlayTimingRecord(
        ZaMoveTimingRecord row,
        string member,
        string value,
        IReadOnlyList<string> spawnLocators)
    {
        var tableRow = ZaRuntimeMoveData.ToTableRow(row);
        return ZaRuntimeMoveData.Apply(tableRow, member, value, spawnLocators)
            ? ZaRuntimeMoveData.ToRecord(tableRow, row.Occurrence, spawnLocators)
            : row;
    }

    private static ZaMoveRuntimeVariantRecord OverlayRuntimeVariant(
        ZaMoveRuntimeVariantRecord row,
        string member,
        int value)
    {
        return member switch
        {
            "effectCategory" => row with { EffectCategory = value },
            "type" => row with { Type = value, TypeName = ZaMovesWorkflowService.FormatType(value) },
            "damageType" => row with { DamageType = value, DamageTypeName = ZaMovesWorkflowService.FormatCategory(value) },
            "power" => row with { Power = value },
            "criticalRank" => row with { CriticalRank = value },
            "hpRecoverRatio" => row with { HpRecoverRatio = value },
            "shrinkPercent" => row with { ShrinkPercent = value },
            "conditionId" => row with { ConditionId = value },
            "conditionPercent" => row with { ConditionPercent = value },
            "conditionCount" => row with { ConditionCount = value },
            "conditionTurnMin" => row with { ConditionTurnMin = value },
            "conditionTurnMax" => row with { ConditionTurnMax = value },
            "stat1" => row with { StatChanges = OverlayStatChange(row.StatChanges, 1, stat => stat with { Stat = value, StatName = ZaMovesWorkflowService.FormatStat(value) }) },
            "stat1Stage" => row with { StatChanges = OverlayStatChange(row.StatChanges, 1, stat => stat with { Stage = value }) },
            "stat1Percent" => row with { StatChanges = OverlayStatChange(row.StatChanges, 1, stat => stat with { Percent = value }) },
            "stat2" => row with { StatChanges = OverlayStatChange(row.StatChanges, 2, stat => stat with { Stat = value, StatName = ZaMovesWorkflowService.FormatStat(value) }) },
            "stat2Stage" => row with { StatChanges = OverlayStatChange(row.StatChanges, 2, stat => stat with { Stage = value }) },
            "stat2Percent" => row with { StatChanges = OverlayStatChange(row.StatChanges, 2, stat => stat with { Percent = value }) },
            "stat3" => row with { StatChanges = OverlayStatChange(row.StatChanges, 3, stat => stat with { Stat = value, StatName = ZaMovesWorkflowService.FormatStat(value) }) },
            "stat3Stage" => row with { StatChanges = OverlayStatChange(row.StatChanges, 3, stat => stat with { Stage = value }) },
            "stat3Percent" => row with { StatChanges = OverlayStatChange(row.StatChanges, 3, stat => stat with { Percent = value }) },
            "damageRecoverRatio" => row with { DamageRecoverRatio = value },
            "damageDrainRatio" => row with { DamageDrainRatio = value },
            "isGuard" => row with { IsGuard = value != 0 },
            "isAvoidedByFloating" => row with { IsAvoidedByFloating = value != 0 },
            "makesContact" => row with { MakesContact = value != 0 },
            "isSlicing" => row with { IsSlicing = value != 0 },
            "isWind" => row with { IsWind = value != 0 },
            "bypassesSubstitute" => row with { BypassesSubstitute = value != 0 },
            "thawsUser" => row with { ThawsUser = value != 0 },
            "restoresHp" => row with { RestoresHp = value != 0 },
            "allowedWhileHealBlocked" => row with { AllowedWhileHealBlocked = value != 0 },
            "callableByMetronome" => row with { CallableByMetronome = value != 0 },
            "appliesCondition" => row with { AppliesCondition = value != 0 },
            "blockedByProtect" => row with { BlockedByProtect = value != 0 },
            "cannotKnockOut" => row with { CannotKnockOut = value != 0 },
            "valueEffectRatio" => row with { ValueEffectRatio = value },
            _ => row,
        };
    }

    private static ZaMoveRecord OverlayMoveField(ZaMoveRecord move, string field, int value)
    {
        return field switch
        {
            ZaMovesWorkflowService.CanUseMoveField => move with { CanUseMove = value != 0 },
            ZaMovesWorkflowService.TypeField => move with { Type = value, TypeName = ZaMovesWorkflowService.FormatType(value) },
            ZaMovesWorkflowService.QualityField => move with { Quality = value },
            ZaMovesWorkflowService.CategoryField => move with { Category = value, CategoryName = ZaMovesWorkflowService.FormatCategory(value) },
            ZaMovesWorkflowService.PowerField => move with { Power = value },
            ZaMovesWorkflowService.AccuracyField => move with { Accuracy = value },
            ZaMovesWorkflowService.PpField => move with { PP = value },
            ZaMovesWorkflowService.PriorityField => move with { Priority = value },
            ZaMovesWorkflowService.CritStageField => move with { CritStage = value },
            ZaMovesWorkflowService.TargetField => move with { Target = value, TargetName = ZaMovesWorkflowService.FormatTarget(value) },
            ZaMovesWorkflowService.HitMinField => move with { HitMin = value },
            ZaMovesWorkflowService.HitMaxField => move with { HitMax = value },
            ZaMovesWorkflowService.TurnMinField => move with { TurnMin = value },
            ZaMovesWorkflowService.TurnMaxField => move with { TurnMax = value },
            ZaMovesWorkflowService.InflictField => move with { Inflict = value, InflictName = ZaMovesWorkflowService.FormatInflict(value) },
            ZaMovesWorkflowService.InflictPercentField => move with { InflictPercent = value },
            ZaMovesWorkflowService.RawInflictCountField => move with { RawInflictCount = value },
            ZaMovesWorkflowService.FlinchField => move with { Flinch = value },
            ZaMovesWorkflowService.EffectSequenceField => move with { EffectSequence = value },
            ZaMovesWorkflowService.RecoilField => move with { Recoil = value },
            ZaMovesWorkflowService.RawHealingField => move with { RawHealing = value },
            ZaMovesWorkflowService.Stat1Field => move with { StatChanges = OverlayStatChange(move.StatChanges, 1, stat => stat with { Stat = value, StatName = ZaMovesWorkflowService.FormatStat(value) }) },
            ZaMovesWorkflowService.Stat1StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, 1, stat => stat with { Stage = value }) },
            ZaMovesWorkflowService.Stat1PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, 1, stat => stat with { Percent = value }) },
            ZaMovesWorkflowService.Stat2Field => move with { StatChanges = OverlayStatChange(move.StatChanges, 2, stat => stat with { Stat = value, StatName = ZaMovesWorkflowService.FormatStat(value) }) },
            ZaMovesWorkflowService.Stat2StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, 2, stat => stat with { Stage = value }) },
            ZaMovesWorkflowService.Stat2PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, 2, stat => stat with { Percent = value }) },
            ZaMovesWorkflowService.Stat3Field => move with { StatChanges = OverlayStatChange(move.StatChanges, 3, stat => stat with { Stat = value, StatName = ZaMovesWorkflowService.FormatStat(value) }) },
            ZaMovesWorkflowService.Stat3StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, 3, stat => stat with { Stage = value }) },
            ZaMovesWorkflowService.Stat3PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, 3, stat => stat with { Percent = value }) },
            _ when ZaMovesWorkflowService.IsEditableFlagField(field) => move with { Flags = OverlayFlag(move.Flags, field, value != 0) },
            _ => move,
        };
    }

    private static IReadOnlyList<ZaMoveStatChangeRecord> OverlayStatChange(
        IReadOnlyList<ZaMoveStatChangeRecord> statChanges,
        int slot,
        Func<ZaMoveStatChangeRecord, ZaMoveStatChangeRecord> update)
    {
        var updated = statChanges.ToList();
        var index = updated.FindIndex(stat => stat.Slot == slot);
        if (index < 0)
        {
            updated.Add(update(new ZaMoveStatChangeRecord(slot, Stat: 0, "None", Stage: 0, Percent: 0)));
        }
        else
        {
            updated[index] = update(updated[index]);
        }

        return updated.OrderBy(stat => stat.Slot).ToArray();
    }

    private static IReadOnlyList<ZaMoveFlagRecord> OverlayFlag(
        IReadOnlyList<ZaMoveFlagRecord> flags,
        string field,
        bool enabled)
    {
        return flags
            .Select(flag => string.Equals(flag.Field, field, StringComparison.Ordinal)
                ? flag with { Enabled = enabled }
                : flag)
            .ToArray();
    }

    private static PendingEdit CreateRuntimeRestoreEdit(
        ZaMoveRecord move,
        string field,
        string virtualPath,
        ProjectFileLayer activeSourceLayer,
        string fingerprint,
        string rowLabel,
        IReadOnlyList<ProjectFileReference>? dependencySources = null)
    {
        return new PendingEdit(
            ZaEditSessionSupport.MovesDomain,
            $"Revert {move.Name} complete runtime {rowLabel} to verified vanilla data.",
            new[]
            {
                new ProjectFileReference(activeSourceLayer, virtualPath),
                new ProjectFileReference(ProjectFileLayer.Base, virtualPath),
            }
                .Concat(dependencySources ?? [])
                .Distinct()
                .ToArray(),
            move.MoveId.ToString(CultureInfo.InvariantCulture),
            field,
            fingerprint);
    }

    private static ValidationDiagnostic CreateRestoreShapeDiagnostic(
        int moveId,
        string field,
        string message)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.MovesDomain,
            field: field,
            expected: $"Exact verified vanilla runtime row shape for move {moveId}");
    }

    private static void ValidateRuntimeRestoreEdit(
        ZaMovesWorkflow workflow,
        ZaMoveRecord move,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var isBattleRestore = IsBattleVanillaRestoreEdit(edit);
        var isTimingRestore = IsTimingVanillaRestoreEdit(edit);
        var expectedFingerprint = isBattleRestore
            ? move.RuntimeBattleVanillaFingerprint
            : isTimingRestore
                ? move.RuntimeTimingVanillaFingerprint
                : move.RuntimePlayerDamageVanillaFingerprint;
        var virtualPath = isBattleRestore
            ? ZaDataPaths.BattleMoveParameterArray
            : isTimingRestore
                ? ZaDataPaths.MoveTimingParameterArray
                : ZaDataPaths.AiAttackParamArray;

        if (!move.CanRevertToVanilla)
        {
            diagnostics.Add(CreateRestoreShapeDiagnostic(
                move.MoveId,
                edit.Field!,
                move.RevertToVanillaBlockedReason
                    ?? "The selected move no longer has an exact restorable runtime row shape."));
            return;
        }

        if (expectedFingerprint is null
            || !string.Equals(edit.NewValue, expectedFingerprint, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "A staged complete move restoration no longer matches the verified vanilla rows.",
                ZaEditSessionSupport.MovesDomain,
                field: edit.Field,
                expected: expectedFingerprint ?? "Current verified vanilla runtime row fingerprint"));
        }

        if (!HasBaseSource(edit, virtualPath))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "A staged complete move restoration is missing verified base provenance.",
                ZaEditSessionSupport.MovesDomain,
                field: edit.Field,
                expected: $"Base source for {virtualPath}"));
        }

        if (isTimingRestore)
        {
            ValidateTimingRestoreProjectileCatalog(
                workflow,
                move.MoveId,
                move.VanillaTimingRows,
                diagnostics);
        }

        if (isTimingRestore || IsPlayerDamageVanillaRestoreEdit(edit))
        {
            ValidateProjectileCatalogProvenance(workflow, edit, diagnostics);
        }
    }

    private static void ApplyBattleVanillaRestore(
        ZaBattleMoveParameterArrayT activeTable,
        ZaBattleMoveParameterArrayT? baseTable,
        PendingEdit edit,
        int moveId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (baseTable is null)
        {
            diagnostics.Add(CreateRestoreShapeDiagnostic(
                moveId,
                BattleVanillaRestoreField,
                "Verified vanilla battle rows were not loaded for the selected move restoration."));
            return;
        }

        var activeRows = ZaRuntimeMoveData.BattleRows(activeTable)
            .Where(row => row.MoveId == checked((uint)moveId))
            .OrderBy(row => row.VariantType)
            .ToArray();
        var baseRows = ZaRuntimeMoveData.BattleRows(baseTable)
            .Where(row => row.MoveId == checked((uint)moveId))
            .OrderBy(row => row.VariantType)
            .ToArray();
        var activeVariantIds = activeRows.Select(row => row.VariantType).ToArray();
        var baseVariantIds = baseRows.Select(row => row.VariantType).ToArray();
        var hasExactShape = activeRows.Length > 0
            && HasOnlyIdenticalDuplicateVariants(activeRows)
            && HasOnlyIdenticalDuplicateVariants(baseRows)
            && activeVariantIds.SequenceEqual(baseVariantIds);
        var baseFingerprint = baseRows.Length == 0
            ? null
            : ZaRuntimeMoveData.CreateBattleRowsFingerprint(baseRows);
        if (!hasExactShape
            || baseFingerprint is null
            || !string.Equals(edit.NewValue, baseFingerprint, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateRestoreShapeDiagnostic(
                moveId,
                BattleVanillaRestoreField,
                "The active and verified vanilla battle rows no longer have the exact reviewed identity and shape."));
            return;
        }

        var replacements = baseRows
            .GroupBy(row => row.VariantType)
            .ToDictionary(
                group => group.Key,
                group => new Queue<ZaBattleMoveParameterT>(group.Select(ZaRuntimeMoveData.Clone)));
        var replacementCount = 0;
        foreach (var group in activeTable.Values ?? [])
        {
            if (group?.Root is not { } rows)
            {
                continue;
            }

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.MoveId == checked((uint)moveId))
                {
                    if (!replacements.TryGetValue(row.VariantType, out var queue) || queue.Count == 0)
                    {
                        diagnostics.Add(CreateRestoreShapeDiagnostic(
                            moveId,
                            BattleVanillaRestoreField,
                            "The selected move battle occurrences changed while the verified vanilla rows were being restored."));
                        return;
                    }

                    rows[index] = queue.Dequeue();
                    replacementCount++;
                }
            }
        }

        if (replacementCount != baseRows.Length || replacements.Values.Any(queue => queue.Count != 0))
        {
            diagnostics.Add(CreateRestoreShapeDiagnostic(
                moveId,
                BattleVanillaRestoreField,
                "The selected move battle occurrences changed while the verified vanilla rows were being restored."));
        }
    }

    private static bool HasOnlyIdenticalDuplicateVariants(
        IReadOnlyList<ZaBattleMoveParameterT> rows)
    {
        return rows
            .GroupBy(row => row.VariantType)
            .All(group => group
                .Select(row => ZaRuntimeMoveData.CreateBattleRowsFingerprint([row]))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count() <= 1);
    }

    private static void ApplyTimingVanillaRestore(
        ZaMovesWorkflow workflow,
        ZaMoveTimingParameterArrayT activeTable,
        ZaMoveTimingParameterArrayT? baseTable,
        PendingEdit edit,
        int moveId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (baseTable is null)
        {
            diagnostics.Add(CreateRestoreShapeDiagnostic(
                moveId,
                TimingVanillaRestoreField,
                "Verified vanilla timing rows were not loaded for the selected move restoration."));
            return;
        }

        var activeRows = ZaRuntimeMoveData.TimingRows(activeTable)
            .Where(row => ZaRuntimeMoveData.IsTimingForMove(row.MoveId, moveId))
            .ToArray();
        var baseRows = ZaRuntimeMoveData.TimingRows(baseTable)
            .Where(row => ZaRuntimeMoveData.IsTimingForMove(row.MoveId, moveId))
            .ToArray();
        if (!ValidateTimingRestoreProjectileCatalog(
                workflow,
                moveId,
                baseRows,
                diagnostics)
            || !ValidateProjectileCatalogProvenance(workflow, edit, diagnostics))
        {
            return;
        }

        var baseFingerprint = baseRows.Length == 0
            ? null
            : ZaRuntimeMoveData.CreateTimingRowsFingerprint(baseRows);
        var hasExactOccurrenceShape = activeRows.Length > 0
            && activeRows
                .Select(row => row.MoveId)
                .SequenceEqual(baseRows.Select(row => row.MoveId));
        if (!hasExactOccurrenceShape
            || baseFingerprint is null
            || !string.Equals(edit.NewValue, baseFingerprint, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateRestoreShapeDiagnostic(
                moveId,
                TimingVanillaRestoreField,
                "The active and verified vanilla timing rows no longer have the exact reviewed occurrence shape."));
            return;
        }

        var replacementIndex = 0;
        foreach (var group in activeTable.Values ?? [])
        {
            if (group?.Root is not { } rows)
            {
                continue;
            }

            for (var index = 0; index < rows.Count; index++)
            {
                if (ZaRuntimeMoveData.IsTimingForMove(rows[index].MoveId, moveId))
                {
                    rows[index] = ZaRuntimeMoveData.Clone(baseRows[replacementIndex]);
                    replacementIndex++;
                }
            }
        }

        if (replacementIndex != baseRows.Length)
        {
            diagnostics.Add(CreateRestoreShapeDiagnostic(
                moveId,
                TimingVanillaRestoreField,
                "The selected move timing occurrences changed while the verified vanilla rows were being restored."));
        }
    }

    private static IReadOnlyDictionary<int, string> CreateExpectedRestoreFingerprints(
        ZaBattleMoveParameterArrayT? battleTable,
        ZaMoveTimingParameterArrayT? timingTable,
        IReadOnlyList<PendingEdit> edits)
    {
        var fingerprints = new Dictionary<int, string>();
        foreach (var edit in edits.Where(IsRuntimeRestoreEdit))
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId))
            {
                continue;
            }

            if (IsBattleVanillaRestoreEdit(edit) && battleTable is not null)
            {
                fingerprints[moveId] = ZaRuntimeMoveData.CreateBattleRowsFingerprint(
                    ZaRuntimeMoveData.BattleRows(battleTable)
                        .Where(row => row.MoveId == checked((uint)moveId))
                        .OrderBy(row => row.VariantType));
            }
            else if (IsTimingVanillaRestoreEdit(edit) && timingTable is not null)
            {
                fingerprints[moveId] = ZaRuntimeMoveData.CreateTimingRowsFingerprint(
                    ZaRuntimeMoveData.TimingRows(timingTable).Where(row =>
                        ZaRuntimeMoveData.IsTimingForMove(row.MoveId, moveId)));
            }
        }

        return fingerprints;
    }

    private static bool IsRuntimeEditForPath(PendingEdit edit, string virtualPath, string editablePrefix)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
            && (edit.Field?.StartsWith(editablePrefix, StringComparison.Ordinal) == true
                || IsRuntimeRestoreEditForPath(edit, virtualPath));
    }

    private static bool IsWazaEdit(PendingEdit edit) =>
        string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
        && ZaMovesWorkflowService.IsWazaEditableField(edit.Field);

    private static bool IsRuntimeRestoreEditForPath(PendingEdit edit, string virtualPath)
    {
        return string.Equals(virtualPath, ZaDataPaths.BattleMoveParameterArray, StringComparison.Ordinal)
            ? IsBattleVanillaRestoreEdit(edit)
            : string.Equals(virtualPath, ZaDataPaths.MoveTimingParameterArray, StringComparison.Ordinal)
                ? IsTimingVanillaRestoreEdit(edit)
                : string.Equals(virtualPath, ZaDataPaths.AiAttackParamArray, StringComparison.Ordinal)
                    && IsPlayerDamageVanillaRestoreEdit(edit);
    }

    private static bool IsRuntimeRestoreEdit(PendingEdit edit) =>
        IsBattleVanillaRestoreEdit(edit)
        || IsTimingVanillaRestoreEdit(edit)
        || IsPlayerDamageVanillaRestoreEdit(edit);

    private static bool RequiresProjectileCatalog(PendingEdit edit) =>
        ZaMovesWorkflowService.IsProjectileField(edit.Field)
        || ZaMovePlayerDamageDataDocument.TryParseField(edit.Field, out _)
        || IsTimingVanillaRestoreEdit(edit)
        || IsPlayerDamageVanillaRestoreEdit(edit);

    private static bool IsBattleVanillaRestoreEdit(PendingEdit edit) =>
        string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
        && string.Equals(edit.Field, BattleVanillaRestoreField, StringComparison.Ordinal);

    private static bool IsTimingVanillaRestoreEdit(PendingEdit edit) =>
        string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
        && string.Equals(edit.Field, TimingVanillaRestoreField, StringComparison.Ordinal);

    private static bool IsPlayerDamageVanillaRestoreEdit(PendingEdit edit) =>
        string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
        && string.Equals(edit.Field, PlayerDamageVanillaRestoreField, StringComparison.Ordinal);

    private static void ApplyPlayerDamageEdit(
        ZaMovesWorkflow workflow,
        ZaMovePlayerDamageDataDocument? activeDocument,
        ZaMovePlayerDamageDataDocument? baseDocument,
        IList<ZaMovePlayerDamageValues>? values,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || moveId is < 0 or >= 1000
            || activeDocument is null
            || values is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Boss Move player-damage edit is not valid for apply.",
                ZaEditSessionSupport.MovesDomain,
                field: edit.Field,
                expected: "A verified Boss Move player-damage row"));
            return;
        }

        var runtimeMoveId = checked(2000 + moveId);
        if (IsPlayerDamageVanillaRestoreEdit(edit))
        {
            if (baseDocument is null)
            {
                diagnostics.Add(CreateRestoreShapeDiagnostic(
                    moveId,
                    PlayerDamageVanillaRestoreField,
                    "Verified vanilla player-damage rows were not loaded for the selected move restoration."));
                return;
            }

            var activeRows = activeDocument.GetValuesForRuntimeMove(runtimeMoveId);
            var baseRows = baseDocument.GetValuesForRuntimeMove(runtimeMoveId);
            var baseFingerprint = baseRows.Count == 0
                ? null
                : baseDocument.GetCanonicalFingerprint(runtimeMoveId);
            if (activeRows.Count == 0
                || baseRows.Count == 0
                || !activeDocument.HasSameCanonicalShape(baseDocument, runtimeMoveId)
                || baseFingerprint is null
                || !string.Equals(edit.NewValue, baseFingerprint, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateRestoreShapeDiagnostic(
                    moveId,
                    PlayerDamageVanillaRestoreField,
                    "The active and verified vanilla player-damage rows no longer have the exact reviewed AttackId shape."));
                return;
            }

            var replacements = baseRows.ToDictionary(row => row.AttackId);
            var replacementCount = 0;
            for (var index = 0; index < values.Count; index++)
            {
                var current = values[index];
                if (current.RuntimeMoveId != runtimeMoveId)
                {
                    continue;
                }

                if (!replacements.TryGetValue(current.AttackId, out var replacement)
                    || current.RuntimeMoveId != replacement.RuntimeMoveId
                    || current.DefaultDamage != replacement.DefaultDamage
                    || current.HitInterval != replacement.HitInterval)
                {
                    diagnostics.Add(CreateRestoreShapeDiagnostic(
                        moveId,
                        PlayerDamageVanillaRestoreField,
                        "The selected move player-damage AttackId shape changed while the verified vanilla rows were being restored."));
                    return;
                }

                values[index] = current with { PlayerDamage = replacement.PlayerDamage };
                replacementCount++;
            }

            if (replacementCount != baseRows.Count)
            {
                diagnostics.Add(CreateRestoreShapeDiagnostic(
                    moveId,
                    PlayerDamageVanillaRestoreField,
                    "The selected move player-damage AttackId shape changed while the verified vanilla rows were being restored."));
            }

            return;
        }

        if (!ZaMovePlayerDamageDataDocument.TryParseField(edit.Field, out var attackId)
            || TryNormalizeEditableValue(workflow, edit.Field, edit.NewValue, diagnostics) is not { } normalizedValue
            || !int.TryParse(normalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerDamage)
            || playerDamage is < ZaMovePlayerDamageDataDocument.MinimumPlayerDamage
                or > ZaMovePlayerDamageDataDocument.MaximumPlayerDamage)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Boss Move player-damage edit is not valid for apply.",
                ZaEditSessionSupport.MovesDomain,
                field: edit.Field,
                expected: $"An integer from {ZaMovePlayerDamageDataDocument.MinimumPlayerDamage} to {ZaMovePlayerDamageDataDocument.MaximumPlayerDamage}"));
            return;
        }

        var targetIndexes = values
            .Select((row, index) => new { Row = row, Index = index })
            .Where(candidate => candidate.Row.AttackId == attackId)
            .ToArray();
        if (targetIndexes.Length != 1
            || targetIndexes[0].Row.RuntimeMoveId != runtimeMoveId)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Boss Move {moveId} could not apply player damage for AttackId {attackId}.",
                ZaEditSessionSupport.MovesDomain,
                field: edit.Field,
                expected: "One exact AttackId associated with the selected Boss Move"));
            return;
        }

        var target = targetIndexes[0];
        values[target.Index] = target.Row with { PlayerDamage = playerDamage };
    }

    private static IReadOnlyDictionary<int, string> CreateExpectedPlayerDamageRestoreFingerprints(
        IReadOnlyList<ZaMovePlayerDamageValues>? values,
        IReadOnlyList<PendingEdit> edits)
    {
        var fingerprints = new Dictionary<int, string>();
        if (values is null)
        {
            return fingerprints;
        }

        foreach (var edit in edits.Where(IsPlayerDamageVanillaRestoreEdit))
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
                || moveId is < 0 or >= 1000)
            {
                continue;
            }

            var rows = values
                .Where(row => row.RuntimeMoveId == checked(2000 + moveId))
                .ToArray();
            if (rows.Length > 0)
            {
                fingerprints[moveId] = CreatePlayerDamageFingerprint(rows);
            }
        }

        return fingerprints;
    }

    private static string CreatePlayerDamageFingerprint(
        IEnumerable<ZaMovePlayerDamageValues> values)
    {
        var canonical = string.Join(
            "\n",
            values
                .OrderBy(value => value.AttackId)
                .Select(value => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.AttackId}:{value.RuntimeMoveId}:{value.DefaultDamage}:"
                    + $"{value.PlayerDamage}:{BitConverter.SingleToInt32Bits(value.HitInterval):X8}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static void ValidatePlayerDamageOutput(
        byte[] bytes,
        IReadOnlyList<PendingEdit> edits,
        IReadOnlyDictionary<int, string> expectedRestoreFingerprints,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var document = ZaMovePlayerDamageDataDocument.Parse(bytes);
        foreach (var edit in edits)
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
                || moveId is < 0 or >= 1000)
            {
                continue;
            }

            var runtimeMoveId = checked(2000 + moveId);
            if (IsPlayerDamageVanillaRestoreEdit(edit))
            {
                var rows = document.GetValuesForRuntimeMove(runtimeMoveId);
                _ = expectedRestoreFingerprints.TryGetValue(moveId, out var expectedFingerprint);
                var actualFingerprint = rows.Count == 0
                    ? null
                    : document.GetCanonicalFingerprint(runtimeMoveId);
                if (actualFingerprint is null
                    || expectedFingerprint is null
                    || !string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal))
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Serialized Boss Move output did not retain the complete vanilla player-damage restoration for move {moveId}.",
                        ZaEditSessionSupport.MovesDomain,
                        field: edit.Field,
                        expected: expectedFingerprint ?? "Complete verified vanilla player-damage rows",
                        code: ZaMovesDiagnosticCodes.RuntimeRestoreVerificationFailed));
                }

                continue;
            }

            if (!ZaMovePlayerDamageDataDocument.TryParseField(edit.Field, out var attackId)
                || !int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedDamage))
            {
                continue;
            }

            var matchingRows = document.Values
                .Where(row => row.AttackId == attackId && row.RuntimeMoveId == runtimeMoveId)
                .ToArray();
            if (matchingRows.Length != 1 || matchingRows[0].PlayerDamage != expectedDamage)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Serialized Boss Move output did not retain player damage for AttackId {attackId} on move {moveId}.",
                    ZaEditSessionSupport.MovesDomain,
                    field: edit.Field,
                    expected: edit.NewValue,
                    code: ZaMovesDiagnosticCodes.RuntimeVariantVerificationFailed));
            }
        }
    }

    private static void ApplyRuntimeEdit(
        ZaMovesWorkflow workflow,
        ZaBattleMoveParameterArrayT battleTable,
        ZaMoveTimingParameterArrayT timingTable,
        ZaBattleMoveParameterArrayT? baseBattleTable,
        ZaMoveTimingParameterArrayT? baseTimingTable,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending runtime move edit is not valid for apply.",
                ZaEditSessionSupport.MovesDomain,
                expected: "Valid Z-A runtime move edit"));
            return;
        }

        if (IsBattleVanillaRestoreEdit(edit))
        {
            ApplyBattleVanillaRestore(battleTable, baseBattleTable, edit, moveId, diagnostics);
            return;
        }

        if (IsTimingVanillaRestoreEdit(edit))
        {
            ApplyTimingVanillaRestore(
                workflow,
                timingTable,
                baseTimingTable,
                edit,
                moveId,
                diagnostics);
            return;
        }

        if (TryNormalizeEditableValue(workflow, edit.Field, edit.NewValue, diagnostics) is not { } value)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending runtime move edit is not valid for apply.",
                ZaEditSessionSupport.MovesDomain,
                expected: "Valid Z-A runtime move edit"));
            return;
        }

        if (ZaRuntimeMoveData.TryParseBattleField(edit.Field, out var variant, out var member))
        {
            var rows = ZaRuntimeMoveData.BattleRows(battleTable)
                .Where(candidate =>
                    candidate.MoveId == checked((uint)moveId) && candidate.VariantType == variant)
                .ToArray();
            var rowsAreIdentical = rows
                .Select(row => ZaRuntimeMoveData.CreateBattleRowsFingerprint([row]))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .Count() <= 1;
            if (rows.Length == 0
                || !rowsAreIdentical
                || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                || rows.Any(row => !ZaRuntimeMoveData.Apply(row, member, integer)))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{FormatRuntimeVariantLabel(variant)} data for move {moveId} could not apply field '{member}'.",
                    ZaEditSessionSupport.MovesDomain,
                    field: edit.Field,
                    expected: "Existing writable battle parameter"));
            }

            return;
        }

        if (ZaRuntimeMoveData.TryParseTimingField(
                edit.Field,
                out var timingMoveId,
                out var occurrence,
                out member))
        {
            var resolvedTimingMoveId = ResolveTimingMoveId(moveId, timingMoveId);
            var rows = ZaRuntimeMoveData.TimingRows(timingTable)
                .Where(candidate =>
                    ZaRuntimeMoveData.IsTimingForMove(resolvedTimingMoveId, moveId)
                    && candidate.MoveId == resolvedTimingMoveId)
                .ToArray();
            var targets = occurrence is null
                ? rows
                : rows.ElementAtOrDefault(occurrence.Value) is { } target
                    ? [target]
                    : [];
            if (targets.Length == 0
                || (occurrence is null && member is not ("hitPercent" or "cooldown"))
                || targets.Any(row => !ZaRuntimeMoveData.Apply(row, member, value, workflow.SpawnLocators)))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {moveId} timing profile {resolvedTimingMoveId} could not apply field '{member}'.",
                    ZaEditSessionSupport.MovesDomain,
                    field: edit.Field,
                    expected: "Existing writable timing parameter",
                    code: ZaMovesDiagnosticCodes.TimingProfileApplyFailed));
            }

            return;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Move field '{edit.Field}' is not a runtime battle or timing field.",
            ZaEditSessionSupport.MovesDomain,
            field: edit.Field,
            expected: "Supported runtime move field"));
    }

    private static void ValidateWazaOutput(
        IReadOnlyList<MoveRow> originalRows,
        byte[] outputBytes,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var outputRows = ReadRows(outputBytes);
        if (originalRows.Count != outputRows.Count
            || !originalRows.Select(row => row.MoveId).SequenceEqual(outputRows.Select(row => row.MoveId)))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Serialized Waza output changed the move row identity or occurrence shape.",
                ZaEditSessionSupport.MovesDomain,
                field: ZaMovesWorkflowService.FlinchField,
                expected: "The exact original Waza row order and move IDs",
                code: ZaMovesDiagnosticCodes.WazaPreservationFailed));
            return;
        }

        foreach (var edit in edits)
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
                || !byte.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedFlinch))
            {
                continue;
            }

            var matches = outputRows.Where(row => row.MoveId == moveId).ToArray();
            if (matches.Length != 1 || matches[0].Flinch != expectedFlinch)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Serialized Waza output did not retain the conventional flinch chance for move {moveId}.",
                    ZaEditSessionSupport.MovesDomain,
                    field: ZaMovesWorkflowService.FlinchField,
                    expected: edit.NewValue,
                    code: ZaMovesDiagnosticCodes.WazaVerificationFailed));
            }
        }

        // Canonicalize both sides through the same exact-schema writer after
        // restoring the edited Flinch byte in the output model. Equality then
        // proves that every other Waza scalar, struct byte, flag, and row was
        // preserved semantically by this narrowly scoped edit.
        foreach (var edit in edits)
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId))
            {
                continue;
            }

            var originalMatches = originalRows.Where(row => row.MoveId == moveId).ToArray();
            var outputMatches = outputRows.Where(row => row.MoveId == moveId).ToArray();
            if (originalMatches.Length == 1 && outputMatches.Length == 1)
            {
                outputMatches[0].Flinch = originalMatches[0].Flinch;
            }
        }

        if (!WriteRows(originalRows).AsSpan().SequenceEqual(WriteRows(outputRows)))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Serialized Waza output changed data outside the selected conventional flinch fields.",
                ZaEditSessionSupport.MovesDomain,
                field: ZaMovesWorkflowService.FlinchField,
                expected: "All non-Flinch Waza data preserved exactly",
                code: ZaMovesDiagnosticCodes.WazaPreservationFailed));
        }
    }

    private static void ValidateRuntimeOutput(
        ZaMovesWorkflow workflow,
        byte[]? battleBytes,
        byte[]? timingBytes,
        IReadOnlyList<PendingEdit> edits,
        IReadOnlyDictionary<int, string> expectedRestoreFingerprints,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var battleRows = battleBytes is null
            ? []
            : ZaRuntimeMoveData.BattleRows(ZaRuntimeMoveData.ReadBattle(battleBytes)).ToArray();
        var timingRows = timingBytes is null
            ? []
            : ZaRuntimeMoveData.TimingRows(ZaRuntimeMoveData.ReadTiming(timingBytes)).ToArray();
        foreach (var edit in edits)
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId))
            {
                continue;
            }

            if (IsRuntimeRestoreEdit(edit))
            {
                _ = expectedRestoreFingerprints.TryGetValue(moveId, out var expectedFingerprint);
                var actualFingerprint = IsBattleVanillaRestoreEdit(edit) && battleBytes is not null
                    ? ZaRuntimeMoveData.CreateBattleRowsFingerprint(
                        battleRows
                            .Where(row => row.MoveId == checked((uint)moveId))
                            .OrderBy(row => row.VariantType))
                    : IsTimingVanillaRestoreEdit(edit) && timingBytes is not null
                        ? ZaRuntimeMoveData.CreateTimingRowsFingerprint(
                            timingRows.Where(row =>
                                ZaRuntimeMoveData.IsTimingForMove(row.MoveId, moveId)))
                        : null;
                if (actualFingerprint is null
                    || expectedFingerprint is null
                    || !string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal))
                {
                    diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Serialized runtime move output did not retain the complete vanilla row restoration for move {moveId}.",
                        ZaEditSessionSupport.MovesDomain,
                        field: edit.Field,
                        expected: expectedFingerprint ?? "Complete verified vanilla runtime rows",
                        code: ZaMovesDiagnosticCodes.RuntimeRestoreVerificationFailed));
                }

                continue;
            }

            var isBattleEdit = ZaRuntimeMoveData.TryParseBattleField(
                edit.Field,
                out var variant,
                out var battleMember);
            var isTimingEdit = ZaRuntimeMoveData.TryParseTimingField(
                edit.Field,
                out var timingMoveId,
                out var timingOccurrence,
                out var timingMember);
            if ((isBattleEdit && battleBytes is null)
                || (isTimingEdit && timingBytes is null)
                || (!isBattleEdit && !isTimingEdit))
            {
                continue;
            }

            IReadOnlyList<string?> actualValues = [];
            if (battleBytes is not null
                && isBattleEdit)
            {
                actualValues = battleRows
                    .Where(row => row.MoveId == checked((uint)moveId) && row.VariantType == variant)
                    .Select(row => ZaRuntimeMoveData.GetValue(ZaRuntimeMoveData.ToRecord(row), battleMember))
                    .ToArray();
            }
            else if (timingBytes is not null
                     && isTimingEdit)
            {
                var resolvedTimingMoveId = ResolveTimingMoveId(moveId, timingMoveId);
                var moveTimingRows = timingRows
                    .Where(row =>
                        ZaRuntimeMoveData.IsTimingForMove(resolvedTimingMoveId, moveId)
                        && row.MoveId == resolvedTimingMoveId)
                    .ToArray();
                actualValues = timingOccurrence is null
                    ? moveTimingRows
                        .Select((row, index) => ZaRuntimeMoveData.GetValue(
                            ZaRuntimeMoveData.ToRecord(row, index, workflow.SpawnLocators),
                            timingMember))
                        .ToArray()
                    : moveTimingRows.ElementAtOrDefault(timingOccurrence.Value) is { } row
                        ? [ZaRuntimeMoveData.GetValue(
                            ZaRuntimeMoveData.ToRecord(row, timingOccurrence.Value, workflow.SpawnLocators),
                            timingMember)]
                        : [];
            }

            if (!double.TryParse(edit.NewValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber)
                || actualValues.Count == 0
                || actualValues.Any(actual =>
                    actual is null
                    || !double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualNumber)
                    || Math.Abs(actualNumber - expectedNumber) > 0.00001))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Serialized runtime move output did not retain field '{edit.Field}' for move {moveId}.",
                    ZaEditSessionSupport.MovesDomain,
                    field: edit.Field,
                    expected: edit.NewValue,
                    code: isTimingEdit
                        ? ZaMovesDiagnosticCodes.TimingProfileVerificationFailed
                        : ZaMovesDiagnosticCodes.RuntimeVariantVerificationFailed));
            }
        }
    }

    private static void ApplyEdit(
        IReadOnlyList<MoveRow> rows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.MovesDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || TryParseEditableValue(null, edit.Field, edit.NewValue, diagnostics) is not { } value)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending move edit is not valid for apply.",
                ZaEditSessionSupport.MovesDomain,
                expected: "Valid Z-A move edit"));
            return;
        }

        var row = rows.FirstOrDefault(candidate => candidate.MoveId == moveId);
        if (row is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} is not present in the source move array.",
                ZaEditSessionSupport.MovesDomain,
                field: "moveId",
                expected: "Existing move source row"));
            return;
        }

        ApplyField(row, edit.Field, value);
    }

    private static void ApplyField(MoveRow row, string? field, int value)
    {
        switch (field)
        {
            case ZaMovesWorkflowService.CanUseMoveField:
                row.CanUseMove = value != 0;
                break;
            case ZaMovesWorkflowService.TypeField:
                row.Type = checked((byte)value);
                break;
            case ZaMovesWorkflowService.QualityField:
                row.Quality = checked((byte)value);
                break;
            case ZaMovesWorkflowService.CategoryField:
                row.Category = checked((byte)value);
                break;
            case ZaMovesWorkflowService.PowerField:
                row.Power = checked((byte)value);
                break;
            case ZaMovesWorkflowService.AccuracyField:
                row.Accuracy = checked((byte)value);
                break;
            case ZaMovesWorkflowService.PpField:
                row.PP = checked((byte)value);
                break;
            case ZaMovesWorkflowService.PriorityField:
                row.Priority = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.CritStageField:
                row.CritStage = checked((byte)value);
                break;
            case ZaMovesWorkflowService.TargetField:
                row.RawTarget = checked((byte)value);
                break;
            case ZaMovesWorkflowService.HitMinField:
                row.HitMin = checked((byte)value);
                break;
            case ZaMovesWorkflowService.HitMaxField:
                row.HitMax = checked((byte)value);
                break;
            case ZaMovesWorkflowService.TurnMinField:
                row.Inflict.TurnMin = checked((byte)value);
                break;
            case ZaMovesWorkflowService.TurnMaxField:
                row.Inflict.TurnMax = checked((byte)value);
                break;
            case ZaMovesWorkflowService.InflictField:
                row.Inflict.Condition = checked((ushort)value);
                break;
            case ZaMovesWorkflowService.InflictPercentField:
                row.Inflict.Chance = checked((byte)value);
                break;
            case ZaMovesWorkflowService.RawInflictCountField:
                row.Inflict.TurnMode = checked((byte)value);
                break;
            case ZaMovesWorkflowService.FlinchField:
                row.Flinch = checked((byte)value);
                break;
            case ZaMovesWorkflowService.EffectSequenceField:
                row.EffectSequence = checked((ushort)value);
                break;
            case ZaMovesWorkflowService.RecoilField:
                row.Recoil = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.RawHealingField:
                row.SelfHeal = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat1Field:
                row.StatChanges.Stat1 = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat1StageField:
                row.StatChanges.Stat1Stage = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat1PercentField:
                row.StatChanges.Stat1Chance = checked((byte)value);
                break;
            case ZaMovesWorkflowService.Stat2Field:
                row.StatChanges.Stat2 = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat2StageField:
                row.StatChanges.Stat2Stage = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat2PercentField:
                row.StatChanges.Stat2Chance = checked((byte)value);
                break;
            case ZaMovesWorkflowService.Stat3Field:
                row.StatChanges.Stat3 = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat3StageField:
                row.StatChanges.Stat3Stage = checked((sbyte)value);
                break;
            case ZaMovesWorkflowService.Stat3PercentField:
                row.StatChanges.Stat3Chance = checked((byte)value);
                break;
            default:
                ApplyFlag(row, field, value != 0);
                break;
        }
    }

    private static void ApplyFlag(MoveRow row, string? field, bool value)
    {
        switch (field)
        {
            case ZaMovesWorkflowService.MakesContactField:
                row.FlagMakesContact = value;
                break;
            case ZaMovesWorkflowService.ChargeField:
                row.FlagCharge = value;
                break;
            case ZaMovesWorkflowService.RechargeField:
                row.FlagRecharge = value;
                break;
            case ZaMovesWorkflowService.ProtectField:
                row.FlagProtect = value;
                break;
            case ZaMovesWorkflowService.ReflectableField:
                row.FlagReflectable = value;
                break;
            case ZaMovesWorkflowService.SnatchField:
                row.FlagSnatch = value;
                break;
            case ZaMovesWorkflowService.MirrorField:
                row.FlagMirror = value;
                break;
            case ZaMovesWorkflowService.PunchField:
                row.FlagPunch = value;
                break;
            case ZaMovesWorkflowService.SoundField:
                row.FlagSound = value;
                break;
            case ZaMovesWorkflowService.DanceField:
                row.FlagDance = value;
                break;
            case ZaMovesWorkflowService.GravityField:
                row.FlagGravity = value;
                break;
            case ZaMovesWorkflowService.DefrostField:
                row.FlagDefrost = value;
                break;
            case ZaMovesWorkflowService.DistanceTripleField:
                row.FlagDistanceTriple = value;
                break;
            case ZaMovesWorkflowService.HealField:
                row.FlagHeal = value;
                break;
            case ZaMovesWorkflowService.IgnoreSubstituteField:
                row.FlagIgnoreSubstitute = value;
                break;
            case ZaMovesWorkflowService.FailSkyBattleField:
                row.FlagFailSkyBattle = value;
                break;
            case ZaMovesWorkflowService.AnimateAllyField:
                row.FlagAnimateAlly = value;
                break;
            case ZaMovesWorkflowService.MetronomeField:
                row.FlagMetronome = value;
                break;
            case ZaMovesWorkflowService.FailEncoreField:
                row.FlagFailEncore = value;
                break;
            case ZaMovesWorkflowService.FailMeFirstField:
                row.FlagFailMeFirst = value;
                break;
            case ZaMovesWorkflowService.FutureAttackField:
                row.FlagFutureAttack = value;
                break;
            case ZaMovesWorkflowService.PressureField:
                row.FlagPressure = value;
                break;
            case ZaMovesWorkflowService.ComboField:
                row.FlagCombo = value;
                break;
            case ZaMovesWorkflowService.NoSleepTalkField:
                row.FlagNoSleepTalk = value;
                break;
            case ZaMovesWorkflowService.NoAssistField:
                row.FlagNoAssist = value;
                break;
            case ZaMovesWorkflowService.FailCopycatField:
                row.FlagFailCopycat = value;
                break;
            case ZaMovesWorkflowService.FailMimicField:
                row.FlagFailMimic = value;
                break;
            case ZaMovesWorkflowService.FailInstructField:
                row.FlagFailInstruct = value;
                break;
            case ZaMovesWorkflowService.PowderField:
                row.FlagPowder = value;
                break;
            case ZaMovesWorkflowService.BiteField:
                row.FlagBite = value;
                break;
            case ZaMovesWorkflowService.BulletField:
                row.FlagBullet = value;
                break;
            case ZaMovesWorkflowService.NoMultiHitField:
                row.FlagNoMultiHit = value;
                break;
            case ZaMovesWorkflowService.NoEffectivenessField:
                row.FlagNoEffectiveness = value;
                break;
            case ZaMovesWorkflowService.SheerForceField:
                row.FlagSheerForce = value;
                break;
            case ZaMovesWorkflowService.SlicingField:
                row.FlagSlicing = value;
                break;
            case ZaMovesWorkflowService.WindField:
                row.FlagWind = value;
                break;
            case ZaMovesWorkflowService.CantUseTwiceField:
                row.FlagCantUseTwice = value;
                break;
        }
    }

    private static IReadOnlyList<MoveRow> ReadRows(byte[] bytes)
    {
        var table = ZaMoveDataArray.GetRootAsZaMoveDataArray(new ByteBuffer(bytes));
        var rows = new List<MoveRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null)
            {
                rows.Add(MoveRow.From(row.Value));
            }
        }

        return rows;
    }

    private static byte[] WriteRows(IReadOnlyList<MoveRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = ZaMoveDataArray.CreateValuesVector(builder, offsets);
        var root = ZaMoveDataArray.CreateZaMoveDataArray(builder, vector);
        ZaMoveDataArray.FinishZaMoveDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private sealed class MoveRow
    {
        public ushort MoveId { get; init; }
        public bool CanUseMove { get; set; }
        public byte Type { get; set; }
        public byte Quality { get; set; }
        public byte Category { get; set; }
        public byte Power { get; set; }
        public byte Accuracy { get; set; }
        public byte PP { get; set; }
        public sbyte Priority { get; set; }
        public byte HitMax { get; set; }
        public byte HitMin { get; set; }
        public InflictRow Inflict { get; } = new();
        public byte CritStage { get; set; }
        public byte Flinch { get; set; }
        public ushort EffectSequence { get; set; }
        public sbyte Recoil { get; set; }
        public sbyte SelfHeal { get; set; }
        public byte DamageHeal { get; init; }
        public byte RawTarget { get; set; }
        public StatChangesRow StatChanges { get; } = new();
        public sbyte Affinity { get; init; }
        public bool FlagMakesContact { get; set; }
        public bool FlagCharge { get; set; }
        public bool FlagRecharge { get; set; }
        public bool FlagProtect { get; set; }
        public bool FlagReflectable { get; set; }
        public bool FlagSnatch { get; set; }
        public bool FlagMirror { get; set; }
        public bool FlagPunch { get; set; }
        public bool FlagSound { get; set; }
        public bool FlagDance { get; set; }
        public bool FlagGravity { get; set; }
        public bool FlagDefrost { get; set; }
        public bool FlagDistanceTriple { get; set; }
        public bool FlagHeal { get; set; }
        public bool FlagIgnoreSubstitute { get; set; }
        public bool FlagFailSkyBattle { get; set; }
        public bool FlagAnimateAlly { get; set; }
        public bool FlagMetronome { get; set; }
        public bool FlagFailEncore { get; set; }
        public bool FlagFailMeFirst { get; set; }
        public bool FlagFutureAttack { get; set; }
        public bool FlagPressure { get; set; }
        public bool FlagCombo { get; set; }
        public bool FlagNoSleepTalk { get; set; }
        public bool FlagNoAssist { get; set; }
        public bool FlagFailCopycat { get; set; }
        public bool FlagFailMimic { get; set; }
        public bool FlagFailInstruct { get; set; }
        public bool FlagPowder { get; set; }
        public bool FlagBite { get; set; }
        public bool FlagBullet { get; set; }
        public bool FlagNoMultiHit { get; set; }
        public bool FlagNoEffectiveness { get; set; }
        public bool FlagSheerForce { get; set; }
        public bool FlagSlicing { get; set; }
        public bool FlagWind { get; set; }
        public bool Unknown57 { get; init; }
        public bool Unknown58 { get; init; }
        public bool Unknown59 { get; init; }
        public bool Unknown60 { get; init; }
        public bool Unknown61 { get; init; }
        public bool Unused62 { get; init; }
        public bool Unused63 { get; init; }
        public bool Unused64 { get; init; }
        public bool Unused65 { get; init; }
        public bool Unused66 { get; init; }
        public bool Unused67 { get; init; }
        public bool Unused68 { get; init; }
        public bool Unused69 { get; init; }
        public bool Unused70 { get; init; }
        public bool Unused71 { get; init; }
        public bool FlagCantUseTwice { get; set; }

        public static MoveRow From(ZaMoveData row)
        {
            var result = new MoveRow
            {
                MoveId = row.MoveId,
                CanUseMove = row.CanUseMove,
                Type = row.Type,
                Quality = row.Quality,
                Category = row.Category,
                Power = row.Power,
                Accuracy = row.Accuracy,
                PP = row.Pp,
                Priority = row.Priority,
                HitMax = row.HitMax,
                HitMin = row.HitMin,
                CritStage = row.CritStage,
                Flinch = row.Flinch,
                EffectSequence = row.EffectSequence,
                Recoil = row.Recoil,
                SelfHeal = row.SelfHeal,
                DamageHeal = row.DamageHeal,
                RawTarget = row.RawTarget,
                Affinity = row.Affinity,
                FlagMakesContact = row.FlagMakesContact,
                FlagCharge = row.FlagCharge,
                FlagRecharge = row.FlagRecharge,
                FlagProtect = row.FlagProtect,
                FlagReflectable = row.FlagReflectable,
                FlagSnatch = row.FlagSnatch,
                FlagMirror = row.FlagMirror,
                FlagPunch = row.FlagPunch,
                FlagSound = row.FlagSound,
                FlagDance = row.FlagDance,
                FlagGravity = row.FlagGravity,
                FlagDefrost = row.FlagDefrost,
                FlagDistanceTriple = row.FlagDistanceTriple,
                FlagHeal = row.FlagHeal,
                FlagIgnoreSubstitute = row.FlagIgnoreSubstitute,
                FlagFailSkyBattle = row.FlagFailSkyBattle,
                FlagAnimateAlly = row.FlagAnimateAlly,
                FlagMetronome = row.FlagMetronome,
                FlagFailEncore = row.FlagFailEncore,
                FlagFailMeFirst = row.FlagFailMeFirst,
                FlagFutureAttack = row.FlagFutureAttack,
                FlagPressure = row.FlagPressure,
                FlagCombo = row.FlagCombo,
                FlagNoSleepTalk = row.FlagNoSleepTalk,
                FlagNoAssist = row.FlagNoAssist,
                FlagFailCopycat = row.FlagFailCopycat,
                FlagFailMimic = row.FlagFailMimic,
                FlagFailInstruct = row.FlagFailInstruct,
                FlagPowder = row.FlagPowder,
                FlagBite = row.FlagBite,
                FlagBullet = row.FlagBullet,
                FlagNoMultiHit = row.FlagNoMultiHit,
                FlagNoEffectiveness = row.FlagNoEffectiveness,
                FlagSheerForce = row.FlagSheerForce,
                FlagSlicing = row.FlagSlicing,
                FlagWind = row.FlagWind,
                Unknown57 = row.Unknown57,
                Unknown58 = row.Unknown58,
                Unknown59 = row.Unknown59,
                Unknown60 = row.Unknown60,
                Unknown61 = row.Unknown61,
                Unused62 = row.Unused62,
                Unused63 = row.Unused63,
                Unused64 = row.Unused64,
                Unused65 = row.Unused65,
                Unused66 = row.Unused66,
                Unused67 = row.Unused67,
                Unused68 = row.Unused68,
                Unused69 = row.Unused69,
                Unused70 = row.Unused70,
                Unused71 = row.Unused71,
                FlagCantUseTwice = row.FlagCantUseTwice,
            };

            if (row.Inflict is { } inflict)
            {
                result.Inflict.CopyFrom(inflict);
            }

            if (row.StatAmps is { } statChanges)
            {
                result.StatChanges.CopyFrom(statChanges);
            }

            return result;
        }

        public Offset<ZaMoveData> Write(FlatBufferBuilder builder)
        {
            ZaMoveData.StartZaMoveData(builder);
            ZaMoveData.AddFlagCantUseTwice(builder, FlagCantUseTwice);
            ZaMoveData.AddUnused71(builder, Unused71);
            ZaMoveData.AddUnused70(builder, Unused70);
            ZaMoveData.AddUnused69(builder, Unused69);
            ZaMoveData.AddUnused68(builder, Unused68);
            ZaMoveData.AddUnused67(builder, Unused67);
            ZaMoveData.AddUnused66(builder, Unused66);
            ZaMoveData.AddUnused65(builder, Unused65);
            ZaMoveData.AddUnused64(builder, Unused64);
            ZaMoveData.AddUnused63(builder, Unused63);
            ZaMoveData.AddUnused62(builder, Unused62);
            ZaMoveData.AddUnknown61(builder, Unknown61);
            ZaMoveData.AddUnknown60(builder, Unknown60);
            ZaMoveData.AddUnknown59(builder, Unknown59);
            ZaMoveData.AddUnknown58(builder, Unknown58);
            ZaMoveData.AddUnknown57(builder, Unknown57);
            ZaMoveData.AddFlagWind(builder, FlagWind);
            ZaMoveData.AddFlagSlicing(builder, FlagSlicing);
            ZaMoveData.AddFlagSheerForce(builder, FlagSheerForce);
            ZaMoveData.AddFlagNoEffectiveness(builder, FlagNoEffectiveness);
            ZaMoveData.AddFlagNoMultiHit(builder, FlagNoMultiHit);
            ZaMoveData.AddFlagBullet(builder, FlagBullet);
            ZaMoveData.AddFlagBite(builder, FlagBite);
            ZaMoveData.AddFlagPowder(builder, FlagPowder);
            ZaMoveData.AddFlagFailInstruct(builder, FlagFailInstruct);
            ZaMoveData.AddFlagFailMimic(builder, FlagFailMimic);
            ZaMoveData.AddFlagFailCopycat(builder, FlagFailCopycat);
            ZaMoveData.AddFlagNoAssist(builder, FlagNoAssist);
            ZaMoveData.AddFlagNoSleepTalk(builder, FlagNoSleepTalk);
            ZaMoveData.AddFlagCombo(builder, FlagCombo);
            ZaMoveData.AddFlagPressure(builder, FlagPressure);
            ZaMoveData.AddFlagFutureAttack(builder, FlagFutureAttack);
            ZaMoveData.AddFlagFailMeFirst(builder, FlagFailMeFirst);
            ZaMoveData.AddFlagFailEncore(builder, FlagFailEncore);
            ZaMoveData.AddFlagMetronome(builder, FlagMetronome);
            ZaMoveData.AddFlagAnimateAlly(builder, FlagAnimateAlly);
            ZaMoveData.AddFlagFailSkyBattle(builder, FlagFailSkyBattle);
            ZaMoveData.AddFlagIgnoreSubstitute(builder, FlagIgnoreSubstitute);
            ZaMoveData.AddFlagHeal(builder, FlagHeal);
            ZaMoveData.AddFlagDistanceTriple(builder, FlagDistanceTriple);
            ZaMoveData.AddFlagDefrost(builder, FlagDefrost);
            ZaMoveData.AddFlagGravity(builder, FlagGravity);
            ZaMoveData.AddFlagDance(builder, FlagDance);
            ZaMoveData.AddFlagSound(builder, FlagSound);
            ZaMoveData.AddFlagPunch(builder, FlagPunch);
            ZaMoveData.AddFlagMirror(builder, FlagMirror);
            ZaMoveData.AddFlagSnatch(builder, FlagSnatch);
            ZaMoveData.AddFlagReflectable(builder, FlagReflectable);
            ZaMoveData.AddFlagProtect(builder, FlagProtect);
            ZaMoveData.AddFlagRecharge(builder, FlagRecharge);
            ZaMoveData.AddFlagCharge(builder, FlagCharge);
            ZaMoveData.AddFlagMakesContact(builder, FlagMakesContact);
            ZaMoveData.AddAffinity(builder, Affinity);
            ZaMoveData.AddStatAmps(builder, StatChanges.Write(builder));
            ZaMoveData.AddRawTarget(builder, RawTarget);
            ZaMoveData.AddDamageHeal(builder, DamageHeal);
            ZaMoveData.AddSelfHeal(builder, SelfHeal);
            ZaMoveData.AddRecoil(builder, Recoil);
            ZaMoveData.AddEffectSequence(builder, EffectSequence);
            ZaMoveData.AddFlinch(builder, Flinch);
            ZaMoveData.AddCritStage(builder, CritStage);
            ZaMoveData.AddInflict(builder, Inflict.Write(builder));
            ZaMoveData.AddHitMin(builder, HitMin);
            ZaMoveData.AddHitMax(builder, HitMax);
            ZaMoveData.AddPriority(builder, Priority);
            ZaMoveData.AddPp(builder, PP);
            ZaMoveData.AddAccuracy(builder, Accuracy);
            ZaMoveData.AddPower(builder, Power);
            ZaMoveData.AddCategory(builder, Category);
            ZaMoveData.AddQuality(builder, Quality);
            ZaMoveData.AddType(builder, Type);
            ZaMoveData.AddCanUseMove(builder, CanUseMove);
            ZaMoveData.AddMoveId(builder, MoveId);
            return ZaMoveData.EndZaMoveData(builder);
        }
    }

    private sealed class InflictRow
    {
        public ushort Condition { get; set; }
        public byte Chance { get; set; }
        public byte TurnMode { get; set; }
        public byte TurnMin { get; set; }
        public byte TurnMax { get; set; }

        public void CopyFrom(ZaMoveInflict row)
        {
            Condition = row.Condition;
            Chance = row.Chance;
            TurnMode = row.TurnMode;
            TurnMin = row.TurnMin;
            TurnMax = row.TurnMax;
        }

        public Offset<ZaMoveInflict> Write(FlatBufferBuilder builder) =>
            ZaMoveInflict.CreateZaMoveInflict(builder, Condition, Chance, TurnMode, TurnMin, TurnMax);
    }

    private sealed class StatChangesRow
    {
        public sbyte Stat1 { get; set; }
        public sbyte Stat2 { get; set; }
        public sbyte Stat3 { get; set; }
        public sbyte Stat1Stage { get; set; }
        public sbyte Stat2Stage { get; set; }
        public sbyte Stat3Stage { get; set; }
        public byte Stat1Chance { get; set; }
        public byte Stat2Chance { get; set; }
        public byte Stat3Chance { get; set; }

        public void CopyFrom(ZaMoveStatChanges row)
        {
            Stat1 = row.Stat1;
            Stat2 = row.Stat2;
            Stat3 = row.Stat3;
            Stat1Stage = row.Stat1Stage;
            Stat2Stage = row.Stat2Stage;
            Stat3Stage = row.Stat3Stage;
            Stat1Chance = row.Stat1Chance;
            Stat2Chance = row.Stat2Chance;
            Stat3Chance = row.Stat3Chance;
        }

        public Offset<ZaMoveStatChanges> Write(FlatBufferBuilder builder) =>
            ZaMoveStatChanges.CreateZaMoveStatChanges(
                builder,
                Stat1,
                Stat1Stage,
                Stat1Chance,
                Stat2,
                Stat2Stage,
                Stat2Chance,
                Stat3,
                Stat3Stage,
                Stat3Chance);
    }
}
