// SPDX-License-Identifier: GPL-3.0-only

using System.Security;
using KM.Core.Files;

namespace KM.Core.Projects;

public enum ProjectRelocationDisposition
{
    Accepted,
    RejectedInvalidRequest,
    RejectedSelectedGameMismatch,
    RejectedInvalidCandidatePaths,
    RejectedDiscoveryLimit,
}

public sealed class ProjectRelocationResult
{
    private ProjectRelocationResult(
        ProjectRelocationDisposition disposition,
        ProjectHealth? candidateHealth,
        ProjectId? candidateProjectId,
        bool? stableSourceIdentityChanged,
        PortableProjectDescriptor? shareableDescriptor,
        ProjectFileGraphDiscoveryLimit? discoveryLimit)
    {
        Disposition = disposition;
        CandidateHealth = candidateHealth;
        CandidateProjectId = candidateProjectId;
        StableSourceIdentityChanged = stableSourceIdentityChanged;
        ShareableDescriptor = shareableDescriptor;
        DiscoveryLimit = discoveryLimit;
    }

    public ProjectRelocationDisposition Disposition { get; }

    public bool IsAccepted => Disposition == ProjectRelocationDisposition.Accepted;

    /// <summary>
    /// Contains private path diagnostics for local presentation and must not be exported.
    /// </summary>
    public ProjectHealth? CandidateHealth { get; }

    public ProjectId? CandidateProjectId { get; }

    public bool? StableSourceIdentityChanged { get; }

    public PortableProjectDescriptor? ShareableDescriptor { get; }

    public ProjectFileGraphDiscoveryLimit? DiscoveryLimit { get; }

    internal static ProjectRelocationResult Accepted(
        ProjectHealth candidateHealth,
        ProjectId candidateProjectId,
        bool stableSourceIdentityChanged,
        PortableProjectDescriptor shareableDescriptor)
    {
        return new ProjectRelocationResult(
            ProjectRelocationDisposition.Accepted,
            candidateHealth,
            candidateProjectId,
            stableSourceIdentityChanged,
            shareableDescriptor,
            discoveryLimit: null);
    }

    internal static ProjectRelocationResult Rejected(
        ProjectRelocationDisposition disposition,
        ProjectHealth? candidateHealth = null,
        ProjectFileGraphDiscoveryLimit? discoveryLimit = null)
    {
        if (disposition == ProjectRelocationDisposition.Accepted)
        {
            throw new ArgumentException("An accepted relocation requires an accepted result.", nameof(disposition));
        }

        return new ProjectRelocationResult(
            disposition,
            candidateHealth,
            candidateProjectId: null,
            stableSourceIdentityChanged: null,
            shareableDescriptor: null,
            discoveryLimit);
    }
}

/// <summary>
/// Evaluates an explicit relocation request without changing an active project or persisting paths.
/// </summary>
public sealed class ProjectRelocationService
{
    public const int MaximumProjectIdLength = 128;
    public const int MaximumCandidatePathLength = 32_767;
    public const int MaximumGameTextLanguageLength = 64;

    private readonly ProjectValidator validator;
    private readonly Func<ProjectPaths, ProjectId> projectIdentityFactory;

    public ProjectRelocationService(
        ProjectValidator? validator = null,
        Func<ProjectPaths, ProjectId>? projectIdentityFactory = null)
    {
        this.validator = validator ?? new ProjectValidator();
        this.projectIdentityFactory = projectIdentityFactory ?? ProjectIdentity.FromPaths;
    }

    public ProjectRelocationResult Evaluate(
        ProjectId existingProjectId,
        ProjectGame selectedGame,
        ProjectPaths candidatePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidatePaths);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsValidProjectId(existingProjectId)
            || !Enum.IsDefined(selectedGame)
            || !HasValidCandidateStrings(candidatePaths, selectedGame))
        {
            return ProjectRelocationResult.Rejected(
                ProjectRelocationDisposition.RejectedInvalidRequest);
        }

        if (candidatePaths.SelectedGame != selectedGame)
        {
            return ProjectRelocationResult.Rejected(
                ProjectRelocationDisposition.RejectedSelectedGameMismatch);
        }

        ProjectHealth candidateHealth;
        try
        {
            candidateHealth = validator.Validate(candidatePaths, cancellationToken);
        }
        catch (ProjectFileGraphDiscoveryException exception)
        {
            return ProjectRelocationResult.Rejected(
                ProjectRelocationDisposition.RejectedDiscoveryLimit,
                discoveryLimit: exception.LimitKind);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAcceptedHealth(candidateHealth)
            || HasLinkedCandidateRoot(candidatePaths, selectedGame))
        {
            return ProjectRelocationResult.Rejected(
                ProjectRelocationDisposition.RejectedInvalidCandidatePaths,
                candidateHealth);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var candidateProjectId = projectIdentityFactory(candidatePaths);
        if (!IsValidProjectId(candidateProjectId))
        {
            return ProjectRelocationResult.Rejected(
                ProjectRelocationDisposition.RejectedInvalidRequest,
                candidateHealth);
        }

        var descriptor = new PortableProjectDescriptor(
            selectedGame,
            GetConfiguredPathRoles(candidatePaths, selectedGame));
        cancellationToken.ThrowIfCancellationRequested();
        return ProjectRelocationResult.Accepted(
            candidateHealth,
            candidateProjectId,
            candidateProjectId != existingProjectId,
            descriptor);
    }

    private static bool IsAcceptedHealth(ProjectHealth health)
    {
        return health.CanOpenReadOnlyWorkflows
            && health.Paths.All(path =>
                path.Status == ProjectPathStatus.Valid
                || (!path.IsRequired && path.Status == ProjectPathStatus.NotSet));
    }

    private static bool IsValidProjectId(ProjectId projectId)
    {
        var value = projectId.Value;
        return value is not null
            && value.Length <= MaximumProjectIdLength
            && !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && !value.Any(char.IsControl);
    }

    private static bool HasValidCandidateStrings(ProjectPaths paths, ProjectGame selectedGame)
    {
        if (!IsBoundedOptionalString(
                paths.ScarletVioletSupportFolderPath,
                MaximumCandidatePathLength)
            || !IsBoundedOptionalString(
                paths.PokemonLegendsZASupportFolderPath,
                MaximumCandidatePathLength))
        {
            return false;
        }

        var pathValues = new List<string?>
        {
            paths.BaseRomFsPath,
            paths.BaseExeFsPath,
            paths.OutputRootPath,
            paths.SaveFilePath,
        };
        if (selectedGame is ProjectGame.Scarlet or ProjectGame.Violet)
        {
            pathValues.Add(paths.ScarletVioletSupportFolderPath);
        }
        else if (selectedGame is ProjectGame.ZA)
        {
            pathValues.Add(paths.PokemonLegendsZASupportFolderPath);
        }

        if (pathValues.Any(path => !IsValidCandidatePath(path)))
        {
            return false;
        }

        var language = paths.GameTextLanguage;
        return IsBoundedOptionalString(language, MaximumGameTextLanguageLength)
            && (string.IsNullOrWhiteSpace(language)
                || string.Equals(language, language!.Trim(), StringComparison.Ordinal));
    }

    private static bool IsValidCandidatePath(string? path)
    {
        if (path is null)
        {
            return true;
        }

        if (path.Length > MaximumCandidatePathLength
            || path.Any(char.IsControl))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            _ = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            NotSupportedException or
            IOException or
            SecurityException)
        {
            return false;
        }
    }

    private static bool IsBoundedOptionalString(string? value, int maximumLength)
    {
        return value is null
            || (value.Length <= maximumLength && !value.Any(char.IsControl));
    }

    private static bool HasLinkedCandidateRoot(ProjectPaths paths, ProjectGame selectedGame)
    {
        var directoryPaths = new List<string?>
        {
            paths.BaseRomFsPath,
            paths.BaseExeFsPath,
            paths.OutputRootPath,
        };
        if (selectedGame is ProjectGame.Scarlet or ProjectGame.Violet)
        {
            directoryPaths.Add(paths.ScarletVioletSupportFolderPath);
        }
        else if (selectedGame is ProjectGame.ZA)
        {
            directoryPaths.Add(paths.PokemonLegendsZASupportFolderPath);
        }

        return directoryPaths.Any(path => HasLinkTarget(path, isDirectory: true))
            || HasLinkTarget(paths.SaveFilePath, isDirectory: false);
    }

    private static bool HasLinkTarget(string? path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (isDirectory ? !Directory.Exists(path) : !File.Exists(path)))
        {
            return false;
        }

        return !FileSystemPathBoundary.HasSafeExistingChain(path, isDirectory);
    }

    private static IEnumerable<ProjectPathRole> GetConfiguredPathRoles(
        ProjectPaths paths,
        ProjectGame selectedGame)
    {
        yield return ProjectPathRole.BaseRomFs;
        yield return ProjectPathRole.BaseExeFs;

        if (!string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            yield return ProjectPathRole.OutputRoot;
        }

        if (!string.IsNullOrWhiteSpace(paths.SaveFilePath))
        {
            yield return ProjectPathRole.SaveFile;
        }

        if ((selectedGame is ProjectGame.Scarlet or ProjectGame.Violet)
            && !string.IsNullOrWhiteSpace(paths.ScarletVioletSupportFolderPath))
        {
            yield return ProjectPathRole.ScarletVioletSupportFolder;
        }

        if (selectedGame is ProjectGame.ZA
            && !string.IsNullOrWhiteSpace(paths.PokemonLegendsZASupportFolderPath))
        {
            yield return ProjectPathRole.PokemonLegendsZASupportFolder;
        }
    }
}
