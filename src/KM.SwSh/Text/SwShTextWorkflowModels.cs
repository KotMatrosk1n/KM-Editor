// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Diagnostics;
using KM.SwSh.Workflows;

namespace KM.SwSh.Text;

public sealed record SwShTextProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShTextEntryRecord(
    int TextId,
    string Label,
    string Language,
    string Value,
    SwShTextProvenance Provenance);

public sealed record SwShDialogueReferenceRecord(
    string DialogueId,
    string Label,
    int TextId,
    string Context,
    string Preview,
    SwShTextProvenance Provenance);

public sealed record SwShTextWorkflowStats(
    int TotalTextEntryCount,
    int DialogueReferenceCount,
    int SourceFileCount);

public sealed record SwShTextWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShTextEntryRecord> Entries,
    IReadOnlyList<SwShDialogueReferenceRecord> DialogueReferences,
    SwShTextWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
