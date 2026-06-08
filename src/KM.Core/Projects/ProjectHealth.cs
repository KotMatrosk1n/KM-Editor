// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;

namespace KM.Core.Projects;

public enum ProjectHealthState
{
    NeedsPaths,
    ReadOnlyReady,
    EditableReady,
    Blocked,
}

public enum ProjectPathRole
{
    BaseRomFs,
    BaseExeFs,
    OutputRoot,
    SaveFile,
}

public enum ProjectPathStatus
{
    NotSet,
    Missing,
    WrongKind,
    Valid,
    Unsafe,
}

public sealed record ProjectPathValidation(
    ProjectPathRole Role,
    string? Path,
    ProjectPathStatus Status,
    bool IsRequired,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public bool HasBlockingError => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}

public sealed record ProjectHealth(
    ProjectHealthState State,
    IReadOnlyList<ProjectPathValidation> Paths,
    ProjectFileGraphSummary FileGraph,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public bool CanOpenReadOnlyWorkflows => State is ProjectHealthState.ReadOnlyReady or ProjectHealthState.EditableReady;

    public bool CanOpenEditableWorkflows => State is ProjectHealthState.EditableReady;
}

