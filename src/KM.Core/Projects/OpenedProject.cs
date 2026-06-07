// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.Core.Projects;

public sealed record OpenedProject(
    ProjectId Id,
    ProjectPaths Paths,
    ProjectHealth Health,
    ProjectFileGraph FileGraph,
    DateTimeOffset OpenedAt);

