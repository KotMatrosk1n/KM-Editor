// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Formats.SV.Habitat;
using KM.SV.Workflows;

namespace KM.SV.HabitatCoordinates;

public static class SvHabitatCoordinatesDiagnosticCodes
{
    public const string ProjectUnsupported = "KM-SV-HABITAT-PROJECT-UNSUPPORTED";
    public const string BuildUnsupported = "KM-SV-HABITAT-BUILD-UNSUPPORTED";
    public const string RegionSourceUnavailable = "KM-SV-HABITAT-REGION-SOURCE-UNAVAILABLE";
    public const string RegionSourceUnsupported = "KM-SV-HABITAT-REGION-SOURCE-UNSUPPORTED";
    public const string QueryInvalid = "KM-SV-HABITAT-QUERY-INVALID";
    public const string EditSessionInvalid = "KM-SV-HABITAT-EDIT-SESSION-INVALID";
    public const string RowBindingStale = "KM-SV-HABITAT-ROW-BINDING-STALE";
    public const string CoordinateUnobserved = "KM-SV-HABITAT-COORDINATE-UNOBSERVED";
    public const string ReviewedPlanStale = "KM-SV-HABITAT-REVIEWED-PLAN-STALE";
    public const string TargetResolutionFailed = "KM-SV-HABITAT-TARGET-RESOLUTION-FAILED";
    public const string OutputPreparationFailed = "KM-SV-HABITAT-OUTPUT-PREPARATION-FAILED";
    public const string OutputPreimageCaptureFailed = "KM-SV-HABITAT-OUTPUT-PREIMAGE-CAPTURE-FAILED";
    public const string OutputCommitFailed = "KM-SV-HABITAT-OUTPUT-COMMIT-FAILED";
    public const string OutputVerificationFailed = "KM-SV-HABITAT-OUTPUT-VERIFICATION-FAILED";
    public const string OutputRollbackRestored = "KM-SV-HABITAT-OUTPUT-ROLLBACK-RESTORED";
    public const string OutputRollbackFailed = "KM-SV-HABITAT-OUTPUT-ROLLBACK-FAILED";
}

public sealed record SvHabitatCoordinatesQuery(
    string Region,
    string Search,
    int Offset,
    int Limit);

public sealed record SvHabitatCoordinateChoice(int X, int Y);

public sealed record SvHabitatRowBinding(
    string SourceFile,
    string SourceRevision,
    int OuterGroupOccurrence,
    int RowOccurrence,
    string RowPreimageSha256,
    int DevNo,
    int FormNo,
    bool VersionA,
    bool VersionB,
    int CurrentX,
    int CurrentY);

public sealed record SvHabitatCoordinateRecord(
    SvHabitatRowBinding Binding,
    string SpeciesName,
    string? FormName,
    int X,
    int Y,
    bool IsStaged,
    SvHabitatCoordinateChoice? StagedCoordinate);

public sealed record SvHabitatRegionState(
    string Region,
    string Label,
    string SourceFile,
    ProjectFileLayer? SourceLayer,
    ProjectFileGraphEntryState? FileState,
    string SourceRevision,
    bool CanStage,
    int OuterGroupCount,
    int RowCount,
    int SemanticIdentityCount,
    IReadOnlyList<SvHabitatCoordinateChoice> CoordinateChoices);

public sealed record SvHabitatCoordinatePage(
    string Region,
    string Search,
    int Offset,
    int Limit,
    int TotalMatches,
    IReadOnlyList<SvHabitatCoordinateRecord> Records);

public sealed record SvHabitatCoordinatesStats(
    int RegionCount,
    int ReadyRegionCount,
    int TotalRowCount,
    int TotalSemanticIdentityCount);

public sealed record SvHabitatCoordinatesWorkflow(
    SvWorkflowSummary Summary,
    string SupportedBuild,
    string DetectedBuildId,
    IReadOnlyList<SvHabitatRegionState> Regions,
    SvHabitatCoordinatePage Page,
    SvHabitatCoordinatesStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvHabitatCoordinatesEditResult(
    SvHabitatCoordinatesWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed record SvHabitatLoadedRegion(
    SvHabitatRegionProfile Profile,
    SvHabitatDistributionDocument BaseDocument,
    SvHabitatDistributionDocument CurrentDocument,
    ProjectFileReference CurrentSource,
    ProjectFileGraphEntryState FileState);

internal sealed record SvHabitatBuildGateResult(
    bool IsSupported,
    string DetectedBuildId,
    string Message);
