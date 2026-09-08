// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;

namespace KM.SwSh.Audio;

public static class SwShSoundMetadata
{
    public static string SpeciesNamePath(ProjectPaths paths) =>
        SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.Resolve(paths), "monsname.dat")[6..];
}
