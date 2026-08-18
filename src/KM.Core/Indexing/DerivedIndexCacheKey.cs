// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using KM.Core.Semantics;

namespace KM.Core.Indexing;

/// <summary>
/// Identifies a derived index for one exact project source revision.
/// </summary>
public readonly record struct DerivedIndexCacheKey
{
    private const int MaximumCallerKeyBytes = 1024;

    public DerivedIndexCacheKey(ProjectSourceRevision revision, string callerKey)
    {
        ArgumentNullException.ThrowIfNull(revision);

        Revision = revision;
        CallerKey = NormalizeCallerKey(callerKey);
    }

    public ProjectSourceRevision Revision { get; }

    public string CallerKey { get; }

    internal static string NormalizeCallerKey(string callerKey)
    {
        if (string.IsNullOrWhiteSpace(callerKey))
        {
            throw new ArgumentException("A derived-index caller key cannot be empty.", nameof(callerKey));
        }

        var normalized = callerKey.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A derived-index caller key cannot contain control characters.",
                nameof(callerKey));
        }

        if (Encoding.UTF8.GetByteCount(normalized) > MaximumCallerKeyBytes)
        {
            throw new ArgumentException(
                $"A derived-index caller key cannot exceed {MaximumCallerKeyBytes} UTF-8 bytes.",
                nameof(callerKey));
        }

        return normalized;
    }
}
