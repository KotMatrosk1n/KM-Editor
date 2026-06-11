// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.CatchCap;

public sealed record SwShCatchCapProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShCatchCapRecord(
    int BadgeCount,
    string Label,
    int LevelCap,
    int MinimumLevelCap,
    int MaximumLevelCap);

public sealed record SwShCatchCapSelection(
    int BadgeCount,
    int LevelCap);

public sealed record SwShCatchCapWorkflowStats(
    int TotalCapCount,
    int SourceFileCount);

public sealed record SwShCatchCapWorkflow(
    SwShWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    string LogicExpression,
    string CapLogicSha256,
    IReadOnlyList<SwShCatchCapRecord> Caps,
    SwShCatchCapProvenance Provenance,
    SwShCatchCapWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
