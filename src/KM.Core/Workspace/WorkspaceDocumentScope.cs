// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Workspace;

public enum WorkspaceDocumentScopeKind
{
    Application,
    Project,
}

/// <summary>
/// Identifies whether a private workspace document belongs to the application or one project.
/// </summary>
public readonly record struct WorkspaceDocumentScope
{
    private WorkspaceDocumentScope(
        WorkspaceDocumentScopeKind kind,
        WorkspaceProjectIdentity projectIdentity)
    {
        Kind = kind;
        ProjectIdentity = projectIdentity;
        IsInitialized = true;
    }

    public static WorkspaceDocumentScope Application { get; } =
        new(WorkspaceDocumentScopeKind.Application, default);

    public WorkspaceDocumentScopeKind Kind { get; }

    internal WorkspaceProjectIdentity ProjectIdentity { get; }

    internal bool IsInitialized { get; }

    public static WorkspaceDocumentScope ForProject(WorkspaceProjectIdentity projectIdentity)
    {
        if (string.IsNullOrWhiteSpace(projectIdentity.Value))
        {
            throw new ArgumentException(
                "A workspace project identity must be initialized.",
                nameof(projectIdentity));
        }

        return new WorkspaceDocumentScope(WorkspaceDocumentScopeKind.Project, projectIdentity);
    }
}
