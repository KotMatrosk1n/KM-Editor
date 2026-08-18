// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using KM.Core.Projects;

namespace KM.Core.Workspace;

/// <summary>
/// An opaque, path-safe identity used to scope private workspace state to a project.
/// </summary>
public readonly record struct WorkspaceProjectIdentity
{
    private const int MaximumStableIdentityBytes = 4096;
    private const string Prefix = "p-";

    private WorkspaceProjectIdentity(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Creates a deterministic opaque identity without exposing the supplied stable identity in a path.
    /// </summary>
    public static WorkspaceProjectIdentity FromStableIdentity(string stableIdentity)
    {
        if (string.IsNullOrWhiteSpace(stableIdentity))
        {
            throw new ArgumentException("A stable project identity cannot be empty.", nameof(stableIdentity));
        }

        if (stableIdentity != stableIdentity.Trim())
        {
            throw new ArgumentException(
                "A stable project identity cannot have surrounding whitespace.",
                nameof(stableIdentity));
        }

        if (stableIdentity.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A stable project identity cannot contain control characters.",
                nameof(stableIdentity));
        }

        var byteCount = Encoding.UTF8.GetByteCount(stableIdentity);
        if (byteCount > MaximumStableIdentityBytes)
        {
            throw new ArgumentException(
                $"A stable project identity cannot exceed {MaximumStableIdentityBytes} UTF-8 bytes.",
                nameof(stableIdentity));
        }

        var identityBytes = Encoding.UTF8.GetBytes(stableIdentity);
        var digest = SHA256.HashData(identityBytes);
        return new WorkspaceProjectIdentity(Prefix + Convert.ToHexStringLower(digest));
    }

    public static WorkspaceProjectIdentity FromProjectId(ProjectId projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId.Value))
        {
            throw new ArgumentException("A project id cannot be empty.", nameof(projectId));
        }

        return FromStableIdentity(projectId.Value);
    }

    public override string ToString()
    {
        return Value;
    }
}
