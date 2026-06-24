// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Core.Editing;
using KM.ZA.Pokemon;

namespace KM.ZA.Workflows;

public sealed class ZaWorkflowService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaCacheManager cacheManager;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaPokemonWorkflowService pokemonWorkflowService;
    private readonly ZaPokemonEditSessionService pokemonEditSessionService;

    public ZaWorkflowService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaCacheManager? cacheManager = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.cacheManager = cacheManager ?? new ZaCacheManager();
        fileSource = new ZaWorkflowFileSource(this.cacheManager);
        pokemonWorkflowService = new ZaPokemonWorkflowService(fileSource);
        pokemonEditSessionService = new ZaPokemonEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            pokemonWorkflowService);
    }

    public ZaCacheStatus GetCacheStatus(ProjectPaths? paths = null)
    {
        return cacheManager.GetStatus(paths);
    }

    public ZaCacheStatus UpdateCacheSettings(
        ZaCacheMode mode,
        long maxCacheSizeBytes,
        ProjectPaths? activePaths = null)
    {
        cacheManager.UpdateSettings(mode, maxCacheSizeBytes, activePaths);
        return cacheManager.GetStatus(activePaths);
    }

    public ZaCacheStatus ClearCache(ProjectPaths? activePaths = null)
    {
        return cacheManager.Clear(activePaths);
    }

    public ZaCacheStatus WarmupCacheStep(ProjectPaths paths, int stepIndex)
    {
        return cacheManager.WarmupStep(paths, stepIndex);
    }

    public ZaWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.SelectedGame is not ProjectGame.ZA)
        {
            return new ZaWorkflowList([]);
        }

        var project = projectWorkspaceService.Open(paths);
        return new ZaWorkflowList(
        [
            pokemonWorkflowService.CreateSummary(project),
        ]);
    }

    public ZaPokemonWorkflow LoadPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return pokemonWorkflowService.Load(project);
    }

    public ZaPokemonEditResult UpdatePokemonField(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string field,
        string value)
    {
        return pokemonEditSessionService.UpdateField(paths, session, personalId, field, value);
    }

    public ZaPokemonEditResult UpdatePokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaPokemonFieldUpdate> updates)
    {
        return pokemonEditSessionService.UpdateFields(paths, session, updates);
    }

    public ZaPokemonEditResult UpdatePokemonLearnset(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string action,
        int? slot,
        int? moveId,
        int? level)
    {
        return pokemonEditSessionService.UpdateLearnset(paths, session, personalId, action, slot, moveId, level);
    }

    public ZaPokemonEditResult UpdatePokemonEvolution(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string action,
        int? slot,
        int? method,
        int? argument,
        int? species,
        int? form,
        int? level)
    {
        return pokemonEditSessionService.UpdateEvolution(paths, session, personalId, action, slot, method, argument, species, form, level);
    }

    public ZaEditSessionValidation ValidateEditSession(ProjectPaths paths, EditSession session)
    {
        return pokemonEditSessionService.Validate(paths, session);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, ZaOutputMode outputMode)
    {
        return pokemonEditSessionService.CreateChangePlan(paths, session, outputMode);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan, ZaOutputMode outputMode)
    {
        return pokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode);
    }
}
