// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.AngeFight;

public sealed record LoadAngeFightWorkflowRequest(ProjectPathsDto Paths);

public sealed record AngeFightAttackSelectionDto(
    int AttackId,
    int DamageToPokemon,
    int DamageToPlayer);

public sealed record StageAngeFightRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int BlueFlowerHp,
    int RedFlowerHp,
    IReadOnlyList<AngeFightAttackSelectionDto> Attacks);

public sealed record StageAngeFightUninstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record AngeFightProvenanceDto(
    string RelativePath,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto State);

public sealed record AngeFightSourceRecordDto(
    string Id,
    string Label,
    string RelativePath,
    string Status,
    string EffectiveSha256,
    string VanillaSha256,
    AngeFightProvenanceDto Provenance);

public sealed record AngeFightFlowerRecordDto(
    string FlowerId,
    string Label,
    int Hp,
    int VanillaHp);

public sealed record AngeFightAttackRecordDto(
    string MoveId,
    string Label,
    string Usage,
    int BulletId,
    int AttackId,
    int DamageToPokemon,
    int DamageToPlayer,
    int VanillaDamageToPokemon,
    int VanillaDamageToPlayer,
    double HitIntervalSeconds,
    bool SharedByMultipleActions,
    bool CanRepeatHit);

public sealed record AngeFightWorkflowStatsDto(
    int SourceFileCount,
    int FlowerCount,
    int AttackCount,
    int EditableValueCount);

public sealed record AngeFightWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    bool CanUninstall,
    string UninstallMessage,
    IReadOnlyList<AngeFightSourceRecordDto> Sources,
    IReadOnlyList<AngeFightFlowerRecordDto> Flowers,
    IReadOnlyList<AngeFightAttackRecordDto> Attacks,
    AngeFightWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadAngeFightWorkflowResponse(
    AngeFightWorkflowDto Workflow);

public sealed record StageAngeFightResponse(
    AngeFightWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageAngeFightUninstallResponse(
    AngeFightWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
