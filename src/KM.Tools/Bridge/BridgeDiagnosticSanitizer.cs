// SPDX-License-Identifier: GPL-3.0-only

using System.Text.RegularExpressions;
using KM.Api.Diagnostics;
using KM.Core.Diagnostics;

namespace KM.Tools.Bridge;

/// <summary>
/// Removes machine-local details before diagnostic text crosses the bridge boundary.
/// </summary>
public static class BridgeDiagnosticSanitizer
{
    private const string LocalPathReplacement = "[local path]";
    private const string RedactedDiagnosticReplacement = "Diagnostic details were redacted.";

    private static readonly Regex QuotedLocalPath = new(
        "(?<quote>[\\\"'])(?:file:(?:/{1,3}|[A-Za-z]:[\\\\/]|\\\\\\\\)[^\\\"'\\r\\n]+|[A-Za-z]:[\\\\/][^\\\"'\\r\\n]*|\\\\\\\\[^\\\"'\\r\\n]+|//[^\\\"'\\r\\n]+|~(?:[A-Za-z0-9._-]+)?[\\\\/][^\\\"'\\r\\n]*|/(?!/)[^\\\"'\\r\\n]+)\\k<quote>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex FileUrl = new(
        "(?<![A-Za-z0-9])file:(?:/{1,3}|[A-Za-z]:[\\\\/]|\\\\\\\\)[^\\r\\n\\\"'<>]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DriveRootedPath = new(
        "(?<![A-Za-z0-9])[A-Za-z]:[\\\\/][^\\r\\n\\\"'<>|]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BackslashUncPath = new(
        "\\\\\\\\[^\\\\/\\r\\n\\\"'<>|]+[\\\\/][^\\r\\n\\\"'<>|]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ForwardSlashUncPath = new(
        "(?<!:)//[^/\\r\\n\\\"'<>|]+/[^\\r\\n\\\"'<>|]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TildeHomePath = new(
        "(?<![A-Za-z0-9._-])~(?:[A-Za-z0-9._-]+)?[\\\\/][^\\r\\n\\\"'<>|]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EnvironmentHomePath = new(
        "(?<![A-Za-z0-9._-])(?:%(?:USERPROFILE|HOME)%|\\$\\{(?:USERPROFILE|HOME)\\}|\\$(?:USERPROFILE|HOME)|\\$env:(?:USERPROFILE|HOME))[\\\\/][^\\r\\n\\\"'<>|]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PosixAbsolutePath = new(
        "(?<![:/A-Za-z0-9._-])/(?!/)[^\\r\\n\\\"'<>|]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExceptionTypeMarker = new(
        "(?<prefix>^|:\\s)(?:--->\\s*)?(?:[A-Za-z_][A-Za-z0-9_`]*(?:\\.[A-Za-z_][A-Za-z0-9_`]*)*Exception):\\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex NativeRuntimeDetail = new(
        "(?:Unable to load (?:shared library|DLL)|Unable to find an entry point|Bad IL format)[^\\r\\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex StackTraceStart = new(
        "(?:\\s+--->\\s+(?:[A-Za-z_][A-Za-z0-9_`]*\\.)*[A-Za-z_][A-Za-z0-9_`]*Exception:|\\r?\\n\\s*(?:at\\s|--- End of .*stack trace))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SemanticCode = new(
        "^KM(?:-[A-Z0-9]+)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sanitized = SanitizeText(value);
        return sanitized.Length == 0
            ? RedactedDiagnosticReplacement
            : sanitized;
    }

    public static ApiDiagnostic Sanitize(ApiDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return new ApiDiagnostic(
            diagnostic.Severity,
            Sanitize(diagnostic.Message),
            SanitizeOptional(diagnostic.File),
            SanitizeOptional(diagnostic.Domain),
            SanitizeOptional(diagnostic.Field),
            SanitizeOptional(diagnostic.Expected))
        {
            Code = SanitizeCode(diagnostic.Code),
        };
    }

    public static ValidationDiagnostic Sanitize(ValidationDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return new ValidationDiagnostic(
            diagnostic.Severity,
            Sanitize(diagnostic.Message),
            SanitizeOptional(diagnostic.File),
            SanitizeOptional(diagnostic.Domain),
            SanitizeOptional(diagnostic.Field),
            SanitizeOptional(diagnostic.Expected))
        {
            Code = SanitizeCode(diagnostic.Code),
        };
    }

    public static string? SanitizeOptional(string? value)
    {
        return value is null ? null : SanitizeText(value);
    }

    public static string? SanitizeCode(string? code)
    {
        if (code is null)
        {
            return null;
        }

        var sanitized = SanitizeText(code);
        return SemanticCode.IsMatch(sanitized) ? sanitized : null;
    }

    private static string SanitizeText(string value)
    {
        var withoutExceptionDetails = RemoveRawExceptionDetails(value);
        return RedactLocalPaths(withoutExceptionDetails).Trim();
    }

    private static string RemoveRawExceptionDetails(string value)
    {
        var stackTraceStart = StackTraceStart.Match(value);
        var summary = stackTraceStart.Success
            ? value[..stackTraceStart.Index]
            : value;

        var withoutExceptionTypes = ExceptionTypeMarker.Replace(
            summary.Trim(),
            "${prefix}");
        return NativeRuntimeDetail.Replace(
            withoutExceptionTypes,
            "A native runtime component could not be loaded.");
    }

    private static string RedactLocalPaths(string value)
    {
        var sanitized = QuotedLocalPath.Replace(
            value,
            match => $"{match.Groups["quote"].Value}{LocalPathReplacement}{match.Groups["quote"].Value}");
        sanitized = FileUrl.Replace(sanitized, LocalPathReplacement);
        sanitized = DriveRootedPath.Replace(sanitized, LocalPathReplacement);
        sanitized = BackslashUncPath.Replace(sanitized, LocalPathReplacement);
        sanitized = ForwardSlashUncPath.Replace(sanitized, LocalPathReplacement);
        sanitized = EnvironmentHomePath.Replace(sanitized, LocalPathReplacement);
        sanitized = TildeHomePath.Replace(sanitized, LocalPathReplacement);
        return PosixAbsolutePath.Replace(sanitized, LocalPathReplacement);
    }
}
