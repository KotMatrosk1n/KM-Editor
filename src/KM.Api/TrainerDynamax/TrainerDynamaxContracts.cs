// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.TrainerDynamax;

public sealed record TrainerDynamaxOverrideDto(int TrainerId, int Player, int Opponent);
public sealed record TrainerDynamaxTrainerDto(int TrainerId, string Name, bool? VanillaPlayer = null, bool? VanillaOpponent = null);
public sealed record TrainerDynamaxSettingsDto(bool DisablePlayer, bool DisableOpponents, IReadOnlyList<TrainerDynamaxOverrideDto>? Trainers = null, bool EnablePlayer = false, bool EnableOpponents = false);
public sealed record LoadTrainerDynamaxRequest(ProjectPathsDto Paths);
public sealed record ReviewTrainerDynamaxRequest(ProjectPathsDto Paths, TrainerDynamaxSettingsDto Settings);
public sealed record ApplyTrainerDynamaxRequest(ProjectPathsDto Paths, TrainerDynamaxSettingsDto Settings, string ReviewToken);
public sealed record TrainerDynamaxStatusDto(bool CanEdit, TrainerDynamaxSettingsDto Settings, bool Partial,
    string? BuildId, string SourceLayer, IReadOnlyList<ApiDiagnostic> Diagnostics, IReadOnlyList<TrainerDynamaxTrainerDto>? Trainers = null);
public sealed record TrainerDynamaxReviewDto(string? ReviewToken, TrainerDynamaxSettingsDto Settings,
    string OutputAction, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadTrainerDynamaxResponse(TrainerDynamaxStatusDto Status);
public sealed record ReviewTrainerDynamaxResponse(TrainerDynamaxReviewDto Review);
public sealed record ApplyTrainerDynamaxResponse(TrainerDynamaxStatusDto Status, ApplyResultDto ApplyResult);
