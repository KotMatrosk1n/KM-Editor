// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.Diagnostics;

public enum ApiDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ApiDiagnostic(
    ApiDiagnosticSeverity Severity,
    string Message,
    string? File = null,
    string? Domain = null,
    string? Field = null,
    string? Expected = null);
