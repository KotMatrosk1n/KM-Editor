// SPDX-License-Identifier: GPL-3.0-only

using KM.Tools.Bridge;

namespace KM.Tools.Application;

internal static class AnalysisDiagnosticPresentation
{
    public static string Text(string value) => Optional(value) ?? string.Empty;

    public static string? Optional(string? value)
    {
        if (value is null) return null;
        var safe = BridgeDiagnosticSanitizer.Sanitize(value);
        safe = string.Concat(safe.Select(character => char.IsControl(character) ? ' ' : character));
        return safe.Length > 1024 ? safe[..1021] + "..." : safe;
    }
}
