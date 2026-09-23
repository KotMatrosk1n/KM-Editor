// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Workflows;

namespace KM.Api.Behavior;

public sealed record ZaBehaviorResourceDto(string EntryId, int SpeciesId, string SpeciesName, int Form,
    int Gender, string SourceFile, string SourceLayer, string Profile, bool IsInitialized,
    IReadOnlyList<string> Tags, IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> VanillaTags, IReadOnlyDictionary<string, string> VanillaFields, string VanillaProfile);
public sealed record ZaBehaviorFieldDto(string Field, string Label, double Minimum, double Maximum,
    double StockMinimum, double StockMaximum);
public sealed record ZaBehaviorProfileDto(string Value, string Label);
public sealed record ZaBehaviorWorkflowDto(string Game, WorkflowSummaryDto Summary,
    IReadOnlyList<ZaBehaviorResourceDto> Resources, IReadOnlyList<ZaBehaviorFieldDto> Fields,
    IReadOnlyList<ZaBehaviorProfileDto> Profiles, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadZaBehaviorWorkflowResponse(ZaBehaviorWorkflowDto Workflow);
public sealed record UpdateZaBehaviorResponse(ZaBehaviorWorkflowDto Workflow, EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
