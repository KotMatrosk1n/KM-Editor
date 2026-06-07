// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.Core.Editing;

public sealed record PendingEdit(
    string Domain,
    string Summary,
    IReadOnlyList<ProjectFileReference> Sources);
