// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.Restoration;

public sealed record RestorationEntryDto(int SpeciesId, string Name, string? DefinitionId, string Support,
    string Installation, IReadOnlyList<int> Family, string? PreparedHash, bool Selected, bool Staged)
{
    public int Form { get; init; }
    public string? FormLabel { get; init; }
    public int? PlannedPersonalId { get; init; }
    public string? Donor { get; init; }
    public string? AssetsRemoved { get; init; }
    public bool RemovalStaged { get; init; }
    public bool UpdateAvailable { get; init; }
    public bool AvailableForAssignment { get; init; }
}
public sealed record RestorationWorkflowDto(bool CanEdit, string Revision, IReadOnlyList<RestorationEntryDto> Entries,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record RestorationPreparationDto(string Id, string DefinitionId, string Status, string Phase, int CompletedSpecies,
    int TotalSpecies, string? PreparedHash, IReadOnlyDictionary<int, int[]> UnavailableSourceMoves, IReadOnlyList<ApiDiagnostic> Diagnostics)
{
    public string Activity { get; init; } = "";
    public int? Species { get; init; }
    public int CompletedUnits { get; init; }
    public int? TotalUnits { get; init; }
    public string? CurrentResource { get; init; }
    public IReadOnlyList<string> RecentResources { get; init; } = [];
    public long ElapsedMilliseconds { get; init; }
    public bool CancellationRequested { get; init; }
    public string? CacheMode { get; init; }
    public string? RemovalId { get; init; }
}
public sealed record InspectRestorationDonorRequest(ProjectPathsDto Paths, string Donor, string DonorRomFsPath, string? CompressionSupportFolderPath);
public sealed record RestorationDonorResponse(string Mode);
public sealed record LoadRestorationRequest(ProjectPathsDto Paths, EditSessionDto? Session = null);
public sealed record LoadRestorationResponse(RestorationWorkflowDto Workflow);
public sealed record PrepareRestorationRequest(ProjectPathsDto Paths, string DefinitionId, string DonorRomFsPath, string? CompressionSupportFolderPath);
public sealed record RestorationPreparationRequest(ProjectPathsDto Paths, string PreparationId);
public sealed record RestorationPreparationResponse(RestorationPreparationDto Preparation);
public sealed record StageRestorationRequest(ProjectPathsDto Paths, string DefinitionId, string PreparedHash, bool Enabled, EditSessionDto? Session, bool RemoveAssets = false, bool UpdateAssets = false, bool AdoptExternal = false);
public sealed record StageRestorationResponse(RestorationWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
