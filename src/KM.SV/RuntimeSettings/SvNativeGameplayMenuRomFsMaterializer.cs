// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.SV.RuntimeSettings;

/// <summary>
/// Production entry point for deriving the S/V 4.0.0 native gameplay-menu
/// RomFS components from the selected project's own base game files.
/// </summary>
public static class SvNativeGameplayMenuRomFsMaterializer
{
    public const string MainScriptPath =
        SvGameplayOptionsRomFsMaterializer.MainScriptPath;

    public static IReadOnlyDictionary<string, byte[]> Build(
        ProjectPaths paths,
        CancellationToken cancellationToken = default) =>
        SvGameplayOptionsRomFsMaterializer.Build(paths, cancellationToken);

    public static IReadOnlyDictionary<string, byte[]> Build(
        string baseRomFsRoot,
        CancellationToken cancellationToken = default) =>
        SvGameplayOptionsRomFsMaterializer.Build(
            baseRomFsRoot,
            cancellationToken);
}
