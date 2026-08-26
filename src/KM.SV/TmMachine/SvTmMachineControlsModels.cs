// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.TmMachine;

public static class SvTmMachineControlsDiagnosticCodes
{
    public const string ProjectUnsupported = "KM-SV-TM-MACHINE-PROJECT-UNSUPPORTED";
    public const string RecipeSourceUnsupported = "KM-SV-TM-MACHINE-RECIPE-SOURCE-UNSUPPORTED";
    public const string MaterialSourceUnsupported = "KM-SV-TM-MACHINE-MATERIAL-SOURCE-UNSUPPORTED";
    public const string EditSessionInvalid = "KM-SV-TM-MACHINE-EDIT-SESSION-INVALID";
    public const string TargetResolutionFailed = "KM-SV-TM-MACHINE-TARGET-RESOLUTION-FAILED";
    public const string ReviewedPlanStale = "KM-SV-TM-MACHINE-REVIEWED-PLAN-STALE";
    public const string OutputPreparationFailed = "KM-SV-TM-MACHINE-OUTPUT-PREPARATION-FAILED";
    public const string OutputPreimageCaptureFailed = "KM-SV-TM-MACHINE-OUTPUT-PREIMAGE-CAPTURE-FAILED";
    public const string OutputCommitFailed = "KM-SV-TM-MACHINE-OUTPUT-COMMIT-FAILED";
    public const string OutputRollbackRestored = "KM-SV-TM-MACHINE-OUTPUT-ROLLBACK-RESTORED";
    public const string OutputRollbackFailed = "KM-SV-TM-MACHINE-OUTPUT-ROLLBACK-FAILED";
}

public sealed record SvTmMachineControlState(
    string Policy,
    string Status,
    string Message,
    bool CanStage,
    string? StagedPolicy,
    int MatchingRecordCount,
    int TotalRecordCount);

public sealed record SvTmMachineControlProvenance(
    string Control,
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState,
    string Sha256);

public sealed record SvTmMachineControlsStats(
    int RecipeCount,
    int SourceFileCount,
    int SupportedBuildCount);

public sealed record SvTmMachineControlsWorkflow(
    SvWorkflowSummary Summary,
    string SupportedBuild,
    SvTmMachineControlState RecipeAvailability,
    SvTmMachineControlState MaterialVisibility,
    IReadOnlyList<SvTmMachineControlProvenance> Provenance,
    SvTmMachineControlsStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvTmMachineControlsEditResult(
    SvTmMachineControlsWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
