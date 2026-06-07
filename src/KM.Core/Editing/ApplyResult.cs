// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;

namespace KM.Core.Editing;

public sealed record ApplyResult(
    string ApplyId,
    DateTimeOffset AppliedAt,
    IReadOnlyList<ProjectFileReference> WrittenFiles,
    WriteManifest Manifest,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record WriteManifest(
    string ApplyId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PlannedFileWrite> Writes);
