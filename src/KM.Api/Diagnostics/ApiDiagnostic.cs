// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;

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
    string? Expected = null)
{
    /// <summary>
    /// A stable KM-prefixed semantic identifier; the human message may evolve independently.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }
}
