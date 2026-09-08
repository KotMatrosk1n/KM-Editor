// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Starmobiles;

public sealed record StarmobileRowDto(string Id, int BossType, int Difficulty, string TrainerId,
    string EventId, IReadOnlyDictionary<string, int> Values)
{
    public IReadOnlyDictionary<string, int>? VanillaValues { get; init; }
}
public sealed record StarmobilesWorkflowDto(WorkflowSummaryDto Summary, string SourceRevision,
    IReadOnlyList<StarmobileRowDto> Rows, IReadOnlyList<ApiDiagnostic> Diagnostics)
{
    public IReadOnlyList<StarmobileMoveOptionDto> MoveOptions { get; init; } = [];
    public IReadOnlyList<StarmobileAbilityOptionDto> AbilityOptions { get; init; } = [];
}
public sealed record StarmobileAbilityOptionDto(int Value, string Label);
public sealed record StarmobileMoveOptionDto(int Value, string Label, bool CanSelect);
public sealed record StarmobileUpdateDto(string RowId, string Field, int Value);
public sealed record LoadStarmobilesRequest(ProjectPathsDto Paths, EditSessionDto? Session);
public sealed record LoadStarmobilesResponse(StarmobilesWorkflowDto Workflow);
public sealed record StageStarmobilesRequest(ProjectPathsDto Paths, EditSessionDto? Session,
    string SourceRevision, IReadOnlyList<StarmobileUpdateDto> Updates);
public sealed record StageStarmobilesResponse(StarmobilesWorkflowDto Workflow, EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
