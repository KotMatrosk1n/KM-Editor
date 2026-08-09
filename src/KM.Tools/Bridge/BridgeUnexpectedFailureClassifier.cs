// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;

namespace KM.Tools.Bridge;

/// <summary>
/// Converts an unexpected bridge exception into bounded, allowlisted diagnostic context.
/// </summary>
public static class BridgeUnexpectedFailureClassifier
{
    private const int MaximumCommandLength = 96;

    public static ApiDiagnostic Classify(
        Exception exception,
        string? command = null,
        ProjectGame? selectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var failure = ProjectFileFailureClassifier.Classify(
            exception,
            selectedGame is ProjectGame.Scarlet or ProjectGame.Violet or ProjectGame.ZA);

        return new ApiDiagnostic(
            ApiDiagnosticSeverity.Error,
            failure.Message,
            File: failure.FileContext?.VirtualPath,
            Domain: CreateDomain(selectedGame),
            Field: NormalizeCommand(command),
            Expected: failure.Expected)
        {
            Code = failure.Code,
        };
    }

    private static string CreateDomain(ProjectGame? selectedGame)
    {
        return selectedGame switch
        {
            ProjectGame.Sword => "bridge.sword",
            ProjectGame.Shield => "bridge.shield",
            ProjectGame.Scarlet => "bridge.scarlet",
            ProjectGame.Violet => "bridge.violet",
            ProjectGame.ZA => "bridge.za",
            _ => "bridge",
        };
    }

    private static string? NormalizeCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var normalizedCommand = command.Trim();
        if (normalizedCommand.Length > MaximumCommandLength
            || normalizedCommand.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '_' and not '-'))
        {
            return null;
        }

        return normalizedCommand;
    }

}
