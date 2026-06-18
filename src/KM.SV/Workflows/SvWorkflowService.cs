// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Core.Projects;
using KM.SV.Encounters;
using KM.SV.Items;
using KM.SV.ModMerger;
using KM.SV.Placement;
using KM.SV.Pokemon;
using KM.SV.Trainers;

namespace KM.SV.Workflows;

public sealed class SvWorkflowService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvItemsWorkflowService itemsWorkflowService;
    private readonly SvPokemonWorkflowService pokemonWorkflowService;
    private readonly SvTrainersWorkflowService trainersWorkflowService;
    private readonly SvEncountersWorkflowService encountersWorkflowService;
    private readonly SvPlacementWorkflowService placementWorkflowService;
    private readonly SvModMergerWorkflowService modMergerWorkflowService;
    private readonly SvItemsEditSessionService itemsEditSessionService;
    private readonly SvPokemonEditSessionService pokemonEditSessionService;
    private readonly SvTrainersEditSessionService trainersEditSessionService;
    private readonly SvEncountersEditSessionService encountersEditSessionService;
    private readonly SvPlacementEditSessionService placementEditSessionService;

    public SvWorkflowService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        var fileSource = new SvWorkflowFileSource();
        itemsWorkflowService = new SvItemsWorkflowService(fileSource);
        pokemonWorkflowService = new SvPokemonWorkflowService(fileSource);
        trainersWorkflowService = new SvTrainersWorkflowService(fileSource);
        encountersWorkflowService = new SvEncountersWorkflowService(fileSource);
        placementWorkflowService = new SvPlacementWorkflowService(fileSource);
        modMergerWorkflowService = new SvModMergerWorkflowService(this.projectWorkspaceService);
        itemsEditSessionService = new SvItemsEditSessionService(this.projectWorkspaceService, fileSource, itemsWorkflowService);
        pokemonEditSessionService = new SvPokemonEditSessionService(this.projectWorkspaceService, fileSource, pokemonWorkflowService);
        trainersEditSessionService = new SvTrainersEditSessionService(this.projectWorkspaceService, fileSource, trainersWorkflowService);
        encountersEditSessionService = new SvEncountersEditSessionService(this.projectWorkspaceService, fileSource, encountersWorkflowService);
        placementEditSessionService = new SvPlacementEditSessionService(this.projectWorkspaceService, fileSource, placementWorkflowService);
    }

    public SvWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame))
        {
            return new SvWorkflowList([]);
        }

        var project = projectWorkspaceService.Open(paths);
        return new SvWorkflowList(
        [
            itemsWorkflowService.CreateSummary(project),
            pokemonWorkflowService.CreateSummary(project),
            trainersWorkflowService.CreateSummary(project),
            encountersWorkflowService.CreateSummary(project),
            placementWorkflowService.CreateSummary(project),
            modMergerWorkflowService.CreateSummary(project),
        ]);
    }

    public SvItemsWorkflow LoadItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return itemsWorkflowService.Load(project);
    }

    public SvPokemonWorkflow LoadPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return pokemonWorkflowService.Load(project);
    }

    public SvTrainersWorkflow LoadTrainers(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return trainersWorkflowService.Load(project);
    }

    public SvEncountersWorkflow LoadEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return encountersWorkflowService.Load(project);
    }

    public SvPlacementWorkflow LoadPlacement(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return placementWorkflowService.Load(project);
    }

    public SvModMergerWorkflow LoadModMerger(
        ProjectPaths paths,
        IReadOnlyList<SvModMergerSourceRequest> modSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(modSources);

        return modMergerWorkflowService.Load(paths, modSources);
    }

    public SvModMergerStageResult StageModMerge(
        ProjectPaths paths,
        IReadOnlyList<SvModMergerSourceRequest> modSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(modSources);

        return modMergerWorkflowService.Stage(paths, modSources);
    }

    public SvModMergerApplyResult ApplyModMerge(
        ProjectPaths paths,
        IReadOnlyList<SvModMergerSourceRequest> modSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(modSources);

        return modMergerWorkflowService.Apply(paths, modSources);
    }

    public SvItemsEditResult UpdateItemField(
        ProjectPaths paths,
        EditSession? session,
        int itemId,
        string field,
        string value)
    {
        return itemsEditSessionService.UpdateField(paths, session, itemId, field, value);
    }

    public SvPokemonEditResult UpdatePokemonField(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string field,
        string value)
    {
        return pokemonEditSessionService.UpdateField(paths, session, personalId, field, value);
    }

    public SvPokemonEditResult UpdatePokemonLearnset(
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

    public SvPokemonEditResult UpdatePokemonEvolution(
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
        return pokemonEditSessionService.UpdateEvolution(
            paths,
            session,
            personalId,
            action,
            slot,
            method,
            argument,
            species,
            form,
            level);
    }

    public SvTrainersEditResult UpdateTrainerField(
        ProjectPaths paths,
        EditSession? session,
        int trainerId,
        int? slot,
        string field,
        string value)
    {
        return trainersEditSessionService.UpdateField(paths, session, trainerId, slot, field, value);
    }

    public SvEncountersEditResult UpdateEncounterSlotField(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot,
        string field,
        string value)
    {
        return encountersEditSessionService.UpdateSlotField(paths, session, tableId, slot, field, value);
    }

    public SvPlacementEditResult UpdatePlacementObjectField(
        ProjectPaths paths,
        EditSession? session,
        string objectId,
        string field,
        string value)
    {
        return placementEditSessionService.UpdateObjectField(paths, session, objectId, field, value);
    }

    public SvEditSessionValidation ValidateEditSession(ProjectPaths paths, EditSession session)
    {
        return GetDomain(session) switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Trainers => trainersEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Encounters => encountersEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Placement => placementEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedValidation(session),
            _ => itemsEditSessionService.Validate(paths, session),
        };
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        return GetDomain(session) switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Trainers => trainersEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Encounters => encountersEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Placement => placementEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedChangePlan(session),
            _ => itemsEditSessionService.CreateChangePlan(paths, session, outputMode),
        };
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan changePlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        return GetDomain(session) switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Trainers => trainersEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Encounters => encountersEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Placement => placementEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedApplyResult(session),
            _ => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
        };
    }

    private static SvEditSessionDomain GetDomain(EditSession session)
    {
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return domains switch
        {
            [] => SvEditSessionDomain.None,
            [SvEditSessionSupport.ItemsDomain] => SvEditSessionDomain.Items,
            [SvEditSessionSupport.PokemonDomain] => SvEditSessionDomain.Pokemon,
            [SvEditSessionSupport.TrainersDomain] => SvEditSessionDomain.Trainers,
            [SvEditSessionSupport.EncountersDomain] => SvEditSessionDomain.Encounters,
            [SvEditSessionSupport.PlacementDomain] => SvEditSessionDomain.Placement,
            _ => SvEditSessionDomain.Mixed,
        };
    }

    private static SvEditSessionValidation CreateUnsupportedMixedValidation(EditSession session)
    {
        return new SvEditSessionValidation(session, IsValid: false, [CreateMixedDiagnostic()]);
    }

    private static ChangePlan CreateUnsupportedMixedChangePlan(EditSession session)
    {
        return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), [CreateMixedDiagnostic()]);
    }

    private static ApplyResult CreateUnsupportedMixedApplyResult(EditSession session)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var plan = CreateUnsupportedMixedChangePlan(session);
        return new ApplyResult(
            applyId,
            appliedAt,
            Array.Empty<KM.Core.Files.ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, plan.Writes),
            plan.Diagnostics);
    }

    private static KM.Core.Diagnostics.ValidationDiagnostic CreateMixedDiagnostic()
    {
        return SvEditSessionSupport.CreateDiagnostic(
            KM.Core.Diagnostics.DiagnosticSeverity.Error,
            "Scarlet/Violet edit sessions cannot mix workflow domains in one change plan yet.",
            "sv.editor",
            expected: "Pending edits from one workflow domain");
    }

    private enum SvEditSessionDomain
    {
        None,
        Items,
        Pokemon,
        Trainers,
        Encounters,
        Placement,
        Mixed,
    }
}
