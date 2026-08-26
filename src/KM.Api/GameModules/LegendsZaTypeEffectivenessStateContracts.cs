// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Projects;

namespace KM.Api.GameModules;

public sealed record LegendsZaTypeEffectivenessStateSourceDto(
    string RelativePath,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record LegendsZaTypeEffectivenessStateTypeDto(
    int TypeIndex,
    string Label,
    string ShortLabel);

public sealed record LegendsZaTypeEffectivenessStateCellDto(
    int AttackTypeIndex,
    int DefenseTypeIndex,
    int CurrentValue,
    int BaseValue);

public sealed record LegendsZaTypeEffectivenessStateDto(
    string BuildId,
    string ChartOffsetHex,
    LegendsZaTypeEffectivenessStateSourceDto BaseSource,
    LegendsZaTypeEffectivenessStateSourceDto EffectiveSource,
    IReadOnlyList<LegendsZaTypeEffectivenessStateTypeDto> Types,
    IReadOnlyList<LegendsZaTypeEffectivenessStateCellDto> Cells,
    int DifferenceCount);
