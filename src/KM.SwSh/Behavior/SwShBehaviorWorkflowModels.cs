// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;

namespace KM.SwSh.Behavior;

public sealed record SwShBehaviorProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShBehaviorFieldOption(
    string Value,
    string Label);

public sealed record SwShBehaviorField(
    string Field,
    string Label,
    string Group,
    string ValueKind,
    double MinimumValue,
    double MaximumValue,
    bool IsReadOnly,
    string Description,
    IReadOnlyList<SwShBehaviorFieldOption> Options)
{
    public SwShBehaviorField(
        string Field,
        string Label,
        string Group,
        string ValueKind,
        double MinimumValue,
        double MaximumValue,
        bool IsReadOnly,
        string Description)
        : this(Field, Label, Group, ValueKind, MinimumValue, MaximumValue, IsReadOnly, Description, Array.Empty<SwShBehaviorFieldOption>())
    {
    }
}

public sealed record SwShBehaviorFieldValue(
    string Field,
    string Value);

public sealed record SwShBehaviorEntryRecord(
    string EntryId,
    int Index,
    string Label,
    int SpeciesId,
    string SpeciesName,
    int Form,
    string Behavior,
    string BehaviorLabel,
    string ModelPart,
    double HitboxRadius,
    double GrassShakeRadius,
    string Hash1,
    string Hash2,
    string InternalSpeciesName,
    IReadOnlyList<SwShBehaviorFieldValue> Fields,
    SwShBehaviorProvenance Provenance)
{
    public IReadOnlyList<SwShBehaviorFieldOption> FormOptions { get; init; } =
        Array.Empty<SwShBehaviorFieldOption>();
}

public sealed record SwShBehaviorWorkflowStats(
    int TotalEntryCount,
    int TotalBehaviorCount,
    int SourceFileCount);

public sealed record SwShBehaviorWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShBehaviorEntryRecord> Entries,
    IReadOnlyList<SwShBehaviorField> Fields,
    SwShBehaviorWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    internal IReadOnlyList<SwShPersonalRecord> PersonalRecords { get; init; } =
        Array.Empty<SwShPersonalRecord>();
}
