// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Diagnostics;

public sealed record ValidationDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    string? File = null,
    string? Domain = null,
    string? Field = null,
    string? Expected = null);
