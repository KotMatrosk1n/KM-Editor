// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Projects;
namespace KM.Api.Audio;

public sealed record SoundStudioRequest(ProjectPathsDto Paths, string Action, string Token, int Index = 0, long Offset = 0);
