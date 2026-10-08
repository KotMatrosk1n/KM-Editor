// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Blueberry;

public sealed record BlueberryRowDto(string Id, string Label, int Group, int Difficulty, int Goal, int Species,
    IReadOnlyDictionary<string, int> Values, IReadOnlyDictionary<string, int> VanillaValues);
public sealed record BlueberryWorkflowDto(WorkflowSummaryDto Summary, string SourceRevision,
    IReadOnlyList<BlueberryRowDto> Rows, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record BlueberryUpdateDto(string RowId, string Field, int Value);
public sealed record LoadBlueberryRequest(ProjectPathsDto Paths, EditSessionDto? Session, string Editor);
public sealed record LoadBlueberryResponse(BlueberryWorkflowDto Workflow);
public sealed record StageBlueberryRequest(ProjectPathsDto Paths, EditSessionDto? Session, string Editor,
    string SourceRevision, IReadOnlyList<BlueberryUpdateDto> Updates);
public sealed record StageBlueberryResponse(BlueberryWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
