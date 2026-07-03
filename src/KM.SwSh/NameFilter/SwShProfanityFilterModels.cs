// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;

namespace KM.SwSh.NameFilter;

public sealed record SwShProfanityFilterStatus(
    string Status,
    string Message,
    string? BuildId,
    ProjectGame? DetectedGame,
    string PatchOffsetHex,
    string PatchShape,
    string SourceLayer,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShProfanityFilterApplyResult(
    SwShProfanityFilterStatus Status,
    ApplyResult ApplyResult);
