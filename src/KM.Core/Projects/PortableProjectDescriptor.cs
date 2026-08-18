// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;

namespace KM.Core.Projects;

/// <summary>
/// Describes the logical inputs needed to locate a project without exposing filesystem paths,
/// source bytes, or decoded source content.
/// </summary>
public sealed class PortableProjectDescriptor
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumConfiguredPathRoles = 6;

    public PortableProjectDescriptor(
        ProjectGame selectedGame,
        IEnumerable<ProjectPathRole> configuredPathRoles)
    {
        if (!Enum.IsDefined(selectedGame))
        {
            throw new ArgumentOutOfRangeException(nameof(selectedGame), selectedGame, null);
        }

        ArgumentNullException.ThrowIfNull(configuredPathRoles);
        var roles = ImmutableArray.CreateBuilder<ProjectPathRole>();
        var uniqueRoles = new HashSet<ProjectPathRole>();
        foreach (var role in configuredPathRoles)
        {
            if (roles.Count >= MaximumConfiguredPathRoles)
            {
                throw new ArgumentException(
                    $"A portable project descriptor cannot contain more than {MaximumConfiguredPathRoles} path roles.",
                    nameof(configuredPathRoles));
            }

            if (!Enum.IsDefined(role))
            {
                throw new ArgumentOutOfRangeException(nameof(configuredPathRoles), role, null);
            }

            if (!uniqueRoles.Add(role))
            {
                throw new ArgumentException(
                    "A portable project descriptor cannot contain duplicate path roles.",
                    nameof(configuredPathRoles));
            }

            roles.Add(role);
        }

        if (!uniqueRoles.Contains(ProjectPathRole.BaseRomFs)
            || !uniqueRoles.Contains(ProjectPathRole.BaseExeFs))
        {
            throw new ArgumentException(
                "A portable project descriptor requires the base RomFS and ExeFS path roles.",
                nameof(configuredPathRoles));
        }

        ValidateGameScopedRoles(selectedGame, uniqueRoles, nameof(configuredPathRoles));
        SchemaVersion = CurrentSchemaVersion;
        SelectedGame = selectedGame;
        ConfiguredPathRoles = roles
            .OrderBy(role => role)
            .ToImmutableArray();
    }

    public int SchemaVersion { get; }

    public ProjectGame SelectedGame { get; }

    public ImmutableArray<ProjectPathRole> ConfiguredPathRoles { get; }

    private static void ValidateGameScopedRoles(
        ProjectGame selectedGame,
        IReadOnlySet<ProjectPathRole> roles,
        string parameterName)
    {
        if (selectedGame is not ProjectGame.Scarlet and not ProjectGame.Violet
            && roles.Contains(ProjectPathRole.ScarletVioletSupportFolder))
        {
            throw new ArgumentException(
                "The Scarlet and Violet support role does not apply to the selected game.",
                parameterName);
        }

        if (selectedGame is not ProjectGame.ZA
            && roles.Contains(ProjectPathRole.PokemonLegendsZASupportFolder))
        {
            throw new ArgumentException(
                "The Legends Z-A support role does not apply to the selected game.",
                parameterName);
        }
    }
}
