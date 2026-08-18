// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Workspace;

/// <summary>
/// A normalized, path-safe identifier for one private workspace document.
/// </summary>
public readonly record struct WorkspaceDocumentId
{
    public WorkspaceDocumentId(string value)
    {
        var normalized = WorkspaceIdentifier.Normalize(value, nameof(value), maximumLength: 64);
        if (IsWindowsReservedDeviceAlias(normalized))
        {
            throw new ArgumentException(
                "A workspace document id cannot use a reserved Windows device name.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }

    private static bool IsWindowsReservedDeviceAlias(string value)
    {
        var extensionSeparator = value.IndexOf('.');
        var alias = extensionSeparator < 0 ? value : value[..extensionSeparator];
        if (alias is "con" or "prn" or "aux" or "nul")
        {
            return true;
        }

        return alias.Length == 4
            && (alias.StartsWith("com", StringComparison.Ordinal)
                || alias.StartsWith("lpt", StringComparison.Ordinal))
            && (alias[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3');
    }
}

internal static class WorkspaceIdentifier
{
    public static string Normalize(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A workspace identifier cannot be empty.", parameterName);
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A workspace identifier cannot exceed {maximumLength} characters.",
                parameterName);
        }

        if (!char.IsAsciiLetterOrDigit(normalized[0])
            || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '-' and not '_'))
        {
            throw new ArgumentException(
                "A workspace identifier must start with an ASCII letter or digit and contain only ASCII letters, digits, '.', '-', or '_'.",
                parameterName);
        }

        return normalized;
    }
}
