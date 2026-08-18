// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Projects;

using System.Security.Cryptography;
using System.Text;

public readonly record struct ProjectId
{
    public ProjectId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static ProjectId New()
    {
        return new ProjectId(Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Creates an opaque identifier from a private, stable project identity.
    /// The source identity is never retained in the returned value.
    /// </summary>
    public static ProjectId FromStableIdentity(string stableIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableIdentity);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(stableIdentity));
        return new ProjectId($"km1_{Convert.ToHexStringLower(digest)}");
    }

    public override string ToString()
    {
        return Value;
    }
}

