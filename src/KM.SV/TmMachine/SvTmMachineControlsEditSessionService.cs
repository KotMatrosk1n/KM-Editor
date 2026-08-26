// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SV.Workflows;
using System.Globalization;
using System.Security;

namespace KM.SV.TmMachine;

public sealed class SvTmMachineControlsEditSessionService
{
    public const string EditDomain = "workflow.tmMachineControls";

    private const string RecipeRecordId = "tm-recipe-availability-v1";
    private const string MaterialRecordId = "tm-material-visibility-v1";
    private const string RecipeField = "allAvailable";
    private const string MaterialField = "alwaysVisible";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvTmMachineControlsWorkflowService workflowService;

    public SvTmMachineControlsEditSessionService()
        : this(
            new ProjectWorkspaceService(),
            new SvWorkflowFileSource(bypassReusableBaseCache: true))
    {
    }

    internal SvTmMachineControlsEditSessionService(
        ProjectWorkspaceService projectWorkspaceService,
        SvWorkflowFileSource fileSource)
        : this(
            projectWorkspaceService,
            fileSource,
            new SvTmMachineControlsWorkflowService(fileSource))
    {
    }

    internal SvTmMachineControlsEditSessionService(
        ProjectWorkspaceService projectWorkspaceService,
        SvWorkflowFileSource fileSource,
        SvTmMachineControlsWorkflowService workflowService)
    {
        this.projectWorkspaceService = projectWorkspaceService;
        this.fileSource = fileSource;
        this.workflowService = workflowService;
    }

    public SvTmMachineControlsEditResult StageRecipeAvailability(
        ProjectPaths paths,
        EditSession? session,
        bool allAvailable)
    {
        return StageControl(
            paths,
            session,
            RecipeRecordId,
            RecipeField,
            allAvailable,
            "recipe availability");
    }

    public SvTmMachineControlsEditResult StageMaterialVisibility(
        ProjectPaths paths,
        EditSession? session,
        bool alwaysVisible)
    {
        return StageControl(
            paths,
            session,
            MaterialRecordId,
            MaterialField,
            alwaysVisible,
            "tracking-window material visibility");
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var workflow = LoadFresh(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        var effectiveEdits = new List<PendingEdit>();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Stage a TM Machine control before validating.",
                expected: "Pending TM recipe or material visibility policy",
                code: SvTmMachineControlsDiagnosticCodes.EditSessionInvalid));
            return new SvEditSessionValidation(session, IsValid: false, diagnostics);
        }

        var seenRecords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edit in session.PendingEdits)
        {
            if (!string.Equals(edit.Domain, EditDomain, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by TM Machine Controls.",
                    expected: EditDomain,
                    code: SvTmMachineControlsDiagnosticCodes.EditSessionInvalid));
                continue;
            }

            if (edit.RecordId is null || !seenRecords.Add(edit.RecordId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "A TM Machine control may be staged only once per edit session.",
                    expected: "One pending edit per TM Machine control",
                    code: SvTmMachineControlsDiagnosticCodes.EditSessionInvalid));
                continue;
            }

            if (!TryReadPolicy(edit, out var enabled, out var state, workflow))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending TM Machine edit '{edit.RecordId}' is not supported.",
                    expected: "TM recipe availability or material visibility policy",
                    code: SvTmMachineControlsDiagnosticCodes.EditSessionInvalid));
                continue;
            }

            if (!state.CanStage)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    state.Message,
                    field: edit.Field,
                    expected: "Supported Scarlet/Violet 4.0.0 source input",
                    code: SourceUnsupportedCode(edit.RecordId)));
                continue;
            }

            if (PolicyMatches(state, edit.RecordId, enabled))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    $"The staged {FormatControlName(edit.RecordId)} policy already matches the current output and was removed."));
                continue;
            }

            effectiveEdits.Add(edit);
        }

        var effectiveSession = session with { PendingEdits = effectiveEdits };
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                effectiveEdits.Count == 0
                    ? "No TM Machine control changes remain after source refresh."
                    : "Pending TM Machine control changes are valid for review."));
        }

        return new SvEditSessionValidation(
            effectiveSession,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        var effectiveSession = validation.Session;
        if (effectiveSession.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending TM Machine control change before reviewing a change plan.",
                expected: "Pending TM Machine control change",
                code: SvTmMachineControlsDiagnosticCodes.EditSessionInvalid));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics)
            {
                EffectivePendingEdits = null,
            };
        }

        var writes = new List<PlannedFileWrite>();
        try
        {
            foreach (var group in effectiveSession.PendingEdits
                .GroupBy(GetVirtualPath, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var writeInfo = SvWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    group.Key,
                    group.SelectMany(edit => edit.Sources).Distinct().ToArray(),
                    outputMode);
                writes.Add(new PlannedFileWrite(
                    writeInfo.TargetRelativePath,
                    writeInfo.Sources,
                    writeInfo.ReplacesExistingOutput,
                    group.Key == SvTmMachineControlsWorkflowService.RecipeDataPath
                        ? "Apply the reviewed TM recipe availability policy."
                        : "Apply the reviewed TM tracking-window material visibility policy."));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                var descriptor = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptor.TargetRelativePath,
                    descriptor.Sources,
                    descriptor.ReplacesExistingOutput,
                    "Patch the Scarlet/Violet Trinity descriptor for the reviewed TM Machine controls."));
            }
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"TM Machine control targets could not be resolved: {exception.Message}",
                expected: "Writable output root",
                code: SvTmMachineControlsDiagnosticCodes.TargetResolutionFailed));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics)
            {
                EffectivePendingEdits = null,
            };
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(
                CultureInfo.InvariantCulture,
                $"TM Machine control review contains {writes.Count:N0} target file(s).")));
        return SvChangePlanSourceGuard.Capture(
            paths,
            effectiveSession,
            new ChangePlan(session.Id, writes, diagnostics),
            outputMode);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
        return ApplyChangePlanLocked(paths, session, reviewedPlan, outputMode);
    }

    private ApplyResult ApplyChangePlanLocked(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvOutputMode outputMode)
    {

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The reviewed TM Machine control plan is stale. Review it again before applying.",
                expected: "Current reviewed TM Machine control plan",
                code: SvTmMachineControlsDiagnosticCodes.ReviewedPlanStale));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || currentPlan.EffectivePendingEdits is not { Count: > 0 } effectiveEdits)
        {
            return SvEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                diagnostics);
        }

        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[]? expectedDescriptor = null;
        try
        {
            foreach (var edit in effectiveEdits)
            {
                if (!bool.TryParse(edit.NewValue, out var enabled))
                {
                    throw new InvalidDataException("A reviewed TM Machine policy value is invalid.");
                }

                if (string.Equals(edit.RecordId, RecipeRecordId, StringComparison.Ordinal))
                {
                    var baseBytes = fileSource.ReadBaseBytesFresh(
                        paths,
                        SvTmMachineControlsWorkflowService.RecipeDataPath);
                    var currentBytes = fileSource.ReadCurrentBytesFresh(
                        paths,
                        SvTmMachineControlsWorkflowService.RecipeDataPath);
                    outputs[SvTmMachineControlsWorkflowService.RecipeDataPath] =
                        SvTmRecipeAvailabilityPatcher.Apply(baseBytes, currentBytes, enabled);
                    continue;
                }

                if (string.Equals(edit.RecordId, MaterialRecordId, StringComparison.Ordinal))
                {
                    var baseBytes = fileSource.ReadBaseBytesFresh(
                        paths,
                        SvTmMachineControlsWorkflowService.MaterialTrackingScriptPath);
                    var baseAnalysis = SvTmMaterialVisibilityPatcher.Analyze(baseBytes);
                    if (baseAnalysis.Kind != SvTmMaterialVisibilityKind.DiscoveryGated)
                    {
                        throw new InvalidDataException(
                            "The base TM tracking script is not the supported Scarlet/Violet 4.0.0 input.");
                    }

                    var currentBytes = fileSource.ReadCurrentBytesFresh(
                        paths,
                        SvTmMachineControlsWorkflowService.MaterialTrackingScriptPath);
                    outputs[SvTmMachineControlsWorkflowService.MaterialTrackingScriptPath] =
                        SvTmMaterialVisibilityPatcher.Apply(currentBytes, enabled);
                    continue;
                }

                throw new InvalidDataException($"Reviewed TM Machine edit '{edit.RecordId}' is not supported.");
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                expectedDescriptor = SvWorkflowFileSource.CreateStandaloneDescriptorPreview(
                    paths,
                    outputs.Keys);
            }
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or UnauthorizedAccessException
            or SecurityException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"TM Machine control output could not be prepared: {exception.Message}",
                expected: "Fresh supported source inputs",
                code: SvTmMachineControlsDiagnosticCodes.OutputPreparationFailed));
            return SvEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                diagnostics);
        }

        try
        {
            var context = new SvOutputApplyContext(
                OutputReviewFingerprint.FromChangePlan(currentPlan),
                new OwnershipOwnerId("workflow.sv.tm-machine-controls"),
                [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, EditDomain)]);
            SvWorkflowFileSource.WriteBatch(
                paths,
                outputs
                    .OrderBy(output => output.Key, StringComparer.Ordinal)
                    .Select(output => new SvWorkflowFileWrite(output.Key, output.Value, context))
                    .ToArray(),
                outputMode,
                context,
                () => ChangePlanReview.Matches(
                    reviewedPlan,
                    CreateChangePlan(paths, session, outputMode)));
            foreach (var output in outputs.OrderBy(output => output.Key, StringComparer.Ordinal))
            {
                if (!SvWorkflowFileSource.ReadOutputBytesForVerification(
                        paths,
                        output.Key,
                        outputMode)
                    .SequenceEqual(output.Value))
                {
                    throw new InvalidDataException(
                        $"TM Machine output '{output.Key}' failed write/readback verification.");
                }

                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(output.Key, outputMode));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                if (!SvWorkflowFileSource.IsDeferredOutputBatchActive(paths, outputMode))
                {
                    var descriptorPath = SvWorkflowFileSource.ResolveOutputPath(
                        paths,
                        SvWorkflowFileSource.DescriptorVirtualPath,
                        outputMode);
                    if (expectedDescriptor is null
                        || !File.ReadAllBytes(descriptorPath).SequenceEqual(expectedDescriptor))
                    {
                        throw new InvalidDataException(
                            "The TM Machine descriptor failed write/readback verification.");
                    }
                }

                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                SvEditSessionSupport.CreateApplyOutputMessage("TM Machine Controls", outputMode)));
        }
        catch (Exception exception) when (exception is OutputCoordinatorException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or UnauthorizedAccessException
            or SecurityException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"TM Machine control output could not be committed: {exception.Message}",
                expected: "Writable output root with verified readback",
                code: SvTmMachineControlsDiagnosticCodes.OutputCommitFailed));
            writtenFiles.Clear();
        }

        return SvEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
    }

    private SvTmMachineControlsEditResult StageControl(
        ProjectPaths paths,
        EditSession? session,
        string recordId,
        string field,
        bool enabled,
        string controlName)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var workflow = LoadFresh(paths);
        var state = string.Equals(recordId, RecipeRecordId, StringComparison.Ordinal)
            ? workflow.RecipeAvailability
            : workflow.MaterialVisibility;
        var diagnostics = new List<ValidationDiagnostic>();
        if (!state.CanStage)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                state.Message,
                field: field,
                expected: "Editable Scarlet/Violet 4.0.0 source input",
                code: SourceUnsupportedCode(recordId)));
            return new SvTmMachineControlsEditResult(
                OverlayStagedPolicies(workflow, currentSession),
                currentSession,
                diagnostics);
        }

        var remaining = currentSession.PendingEdits
            .Where(edit => !string.Equals(edit.Domain, EditDomain, StringComparison.Ordinal)
                || !string.Equals(edit.RecordId, recordId, StringComparison.Ordinal))
            .ToList();
        if (PolicyMatches(state, recordId, enabled))
        {
            var updated = currentSession with { PendingEdits = remaining };
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"TM {controlName} already uses the requested policy; no change was staged."));
            return new SvTmMachineControlsEditResult(
                OverlayStagedPolicies(workflow, updated),
                updated,
                diagnostics);
        }

        remaining.Add(new PendingEdit(
            EditDomain,
            enabled
                ? $"Enable TM {controlName}."
                : $"Restore standard TM {controlName}.",
            CreateSources(workflow, recordId),
            recordId,
            field,
            enabled.ToString(CultureInfo.InvariantCulture)));
        var stagedSession = currentSession with { PendingEdits = remaining };
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"TM {controlName} policy is staged for change-plan review."));
        return new SvTmMachineControlsEditResult(
            OverlayStagedPolicies(workflow, stagedSession),
            stagedSession,
            diagnostics);
    }

    private SvTmMachineControlsWorkflow LoadFresh(ProjectPaths paths)
    {
        projectWorkspaceService.ClearMemoryCache();
        return workflowService.Load(projectWorkspaceService.Open(paths, DateTimeOffset.UtcNow));
    }

    private static IReadOnlyList<ProjectFileReference> CreateSources(
        SvTmMachineControlsWorkflow workflow,
        string recordId)
    {
        var control = string.Equals(recordId, RecipeRecordId, StringComparison.Ordinal)
            ? "recipeAvailability"
            : "materialVisibility";
        var path = string.Equals(recordId, RecipeRecordId, StringComparison.Ordinal)
            ? SvTmMachineControlsWorkflowService.RecipeDataPath
            : SvTmMachineControlsWorkflowService.MaterialTrackingScriptPath;
        var current = workflow.Provenance.First(source =>
            string.Equals(source.Control, control, StringComparison.Ordinal));
        return new[]
        {
            new ProjectFileReference(current.SourceLayer, current.SourceFile),
            new ProjectFileReference(ProjectFileLayer.Base, $"romfs/{path}"),
        }
        .Distinct()
        .ToArray();
    }

    private static bool TryReadPolicy(
        PendingEdit edit,
        out bool enabled,
        out SvTmMachineControlState state,
        SvTmMachineControlsWorkflow workflow)
    {
        enabled = false;
        state = workflow.RecipeAvailability;
        if (!bool.TryParse(edit.NewValue, out enabled))
        {
            return false;
        }

        if (string.Equals(edit.RecordId, RecipeRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, RecipeField, StringComparison.Ordinal))
        {
            state = workflow.RecipeAvailability;
            return true;
        }

        if (string.Equals(edit.RecordId, MaterialRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, MaterialField, StringComparison.Ordinal))
        {
            state = workflow.MaterialVisibility;
            return true;
        }

        return false;
    }

    private static bool PolicyMatches(
        SvTmMachineControlState state,
        string recordId,
        bool enabled)
    {
        var expected = string.Equals(recordId, RecipeRecordId, StringComparison.Ordinal)
            ? enabled ? "allAvailable" : "progressionGated"
            : enabled ? "alwaysVisible" : "discoveryGated";
        return string.Equals(state.Policy, expected, StringComparison.Ordinal);
    }

    private static SvTmMachineControlsWorkflow OverlayStagedPolicies(
        SvTmMachineControlsWorkflow workflow,
        EditSession session)
    {
        var recipe = session.PendingEdits.FirstOrDefault(edit =>
            string.Equals(edit.Domain, EditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, RecipeRecordId, StringComparison.Ordinal));
        var material = session.PendingEdits.FirstOrDefault(edit =>
            string.Equals(edit.Domain, EditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, MaterialRecordId, StringComparison.Ordinal));
        return workflow with
        {
            RecipeAvailability = workflow.RecipeAvailability with
            {
                StagedPolicy = recipe is not null && bool.TryParse(recipe.NewValue, out var allAvailable)
                    ? allAvailable ? "allAvailable" : "progressionGated"
                    : null,
            },
            MaterialVisibility = workflow.MaterialVisibility with
            {
                StagedPolicy = material is not null && bool.TryParse(material.NewValue, out var alwaysVisible)
                    ? alwaysVisible ? "alwaysVisible" : "discoveryGated"
                    : null,
            },
        };
    }

    private static string GetVirtualPath(PendingEdit edit) =>
        string.Equals(edit.RecordId, RecipeRecordId, StringComparison.Ordinal)
            ? SvTmMachineControlsWorkflowService.RecipeDataPath
            : SvTmMachineControlsWorkflowService.MaterialTrackingScriptPath;

    private static string FormatControlName(string? recordId) =>
        string.Equals(recordId, RecipeRecordId, StringComparison.Ordinal)
            ? "recipe availability"
            : "material visibility";

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null,
        string? code = null)
    {
        return SvTmMachineControlsWorkflowService.CreateDiagnostic(
            severity,
            message,
            file,
            field,
            expected,
            code);
    }

    private static string SourceUnsupportedCode(string? recordId) =>
        string.Equals(recordId, RecipeRecordId, StringComparison.Ordinal)
            ? SvTmMachineControlsDiagnosticCodes.RecipeSourceUnsupported
            : SvTmMachineControlsDiagnosticCodes.MaterialSourceUnsupported;

}
