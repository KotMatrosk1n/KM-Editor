// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using System.Text.Json.Serialization;

namespace KM.Api.Trainers;

public sealed record AiFlagsRepairRowDto(int TrainerId, string Name, string SourceFile, int CurrentFlags,
    int? ProposedFlags, string Fingerprint, bool Candidate, bool PreviouslyFixed, bool ChangedSinceFix,
    DateTimeOffset? FixedAtUtc, int? BaseFlags);
public sealed record AiFlagsRepairSelectionDto([property: JsonRequired] int TrainerId,
    [property: JsonRequired] string Fingerprint, [property: JsonRequired] bool AcknowledgePreviousFix);
public sealed record AiFlagsRepairWorkflowDto(bool CanEdit, IReadOnlyList<AiFlagsRepairRowDto> Trainers,
    ProjectGameDto? DetectedGame, bool CustomAiScripts, string ContextFingerprint, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadFixAiFlagsRequest(ProjectPathsDto Paths);
public sealed record LoadFixAiFlagsResponse(AiFlagsRepairWorkflowDto Workflow);
public sealed record StageFixAiFlagsRequest(ProjectPathsDto Paths, EditSessionDto? Session,
    [property: JsonRequired] IReadOnlyList<AiFlagsRepairSelectionDto> Selections,
    [property: JsonRequired] string ContextFingerprint, [property: JsonRequired] bool AcknowledgeCustomScripts);
public sealed record StageFixAiFlagsResponse(AiFlagsRepairWorkflowDto Workflow, EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
