// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Files;

public sealed record ProjectFileReference(
    ProjectFileLayer Layer,
    string RelativePath);
