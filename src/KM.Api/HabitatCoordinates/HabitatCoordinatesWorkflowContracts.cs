// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.HabitatCoordinates;

public sealed record HabitatCoordinatesQueryDto(
    string Region,
    string Search,
    int Offset,
    int Limit);

public sealed record HabitatCoordinateChoiceDto(int X, int Y);

public sealed record HabitatRowBindingDto(
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

public sealed record HabitatCoordinateRecordDto(
    HabitatRowBindingDto Binding,
    string SpeciesName,
    string? FormName,
    int X,
    int Y,
    bool IsStaged,
    HabitatCoordinateChoiceDto? StagedCoordinate);

public sealed record HabitatRegionStateDto(
    string Region,
    string Label,
    string SourceFile,
    ProjectFileLayerDto? SourceLayer,
    ProjectFileGraphEntryStateDto? FileState,
    string SourceRevision,
    bool CanStage,
    int OuterGroupCount,
    int RowCount,
    int SemanticIdentityCount,
    IReadOnlyList<HabitatCoordinateChoiceDto> CoordinateChoices);

public sealed record HabitatCoordinatePageDto(
    string Region,
    string Search,
    int Offset,
    int Limit,
    int TotalMatches,
    IReadOnlyList<HabitatCoordinateRecordDto> Records);

public sealed record HabitatCoordinatesStatsDto(
    int RegionCount,
    int ReadyRegionCount,
    int TotalRowCount,
    int TotalSemanticIdentityCount);

public sealed record HabitatCoordinatesWorkflowDto(
    WorkflowSummaryDto Summary,
    string SupportedBuild,
    string DetectedBuildId,
    IReadOnlyList<HabitatRegionStateDto> Regions,
    HabitatCoordinatePageDto Page,
    HabitatCoordinatesStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadHabitatCoordinatesRequest(
    ProjectPathsDto Paths,
    HabitatCoordinatesQueryDto? Query,
    EditSessionDto? Session);

public sealed record LoadHabitatCoordinatesResponse(HabitatCoordinatesWorkflowDto Workflow);

public sealed record StageHabitatCoordinateRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    HabitatCoordinatesQueryDto? Query,
    string Region,
    HabitatRowBindingDto Binding,
    HabitatCoordinateChoiceDto Coordinate);

public sealed record StageHabitatCoordinateResponse(
    HabitatCoordinatesWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
