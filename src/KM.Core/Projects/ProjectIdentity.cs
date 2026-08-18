// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Globalization;

namespace KM.Core.Projects;

/// <summary>
/// Derives the private, restart-stable identity of a configured project.
/// Output profiles and auxiliary save files deliberately do not participate.
/// Source-content freshness belongs to <c>ProjectSourceRevision</c>, not this id.
/// </summary>
public static class ProjectIdentity
{
    private const int IdentitySchemaVersion = 1;

    public static ProjectId FromPaths(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var identityBuilder = new StringBuilder();
        AppendIdentityField(
            identityBuilder,
            "schema",
            IdentitySchemaVersion.ToString(CultureInfo.InvariantCulture));
        AppendIdentityField(identityBuilder, "game", paths.SelectedGame?.ToString() ?? "unset");
        AppendIdentityField(identityBuilder, "romfs", NormalizePrivatePath(paths.BaseRomFsPath));
        AppendIdentityField(identityBuilder, "exefs", NormalizePrivatePath(paths.BaseExeFsPath));
        AppendIdentityField(
            identityBuilder,
            "svSupport",
            paths.SelectedGame is ProjectGame.Scarlet or ProjectGame.Violet
                ? NormalizePrivatePath(paths.ScarletVioletSupportFolderPath)
                : "not-applicable");
        AppendIdentityField(
            identityBuilder,
            "zaSupport",
            paths.SelectedGame is ProjectGame.ZA
                ? NormalizePrivatePath(paths.PokemonLegendsZASupportFolderPath)
                : "not-applicable");

        return ProjectId.FromStableIdentity(identityBuilder.ToString());
    }

    private static void AppendIdentityField(StringBuilder builder, string key, string value)
    {
        // Length framing keeps identities unambiguous even on platforms that permit
        // separators or control characters inside file names.
        builder
            .Append(key.Length)
            .Append(':')
            .Append(key)
            .Append('=')
            .Append(value.Length)
            .Append(':')
            .Append(value)
            .Append(';');
    }

    private static string NormalizePrivatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "unset";
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(path.Trim());
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            // Invalid paths are still given a deterministic private identity so project
            // validation can report the actual path problem through its normal contract.
            normalized = path.Trim();
        }

        normalized = Path.TrimEndingDirectorySeparator(normalized);
        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }
}
