// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Core.Editing;
using KM.SwSh.Encounters;
using KM.SwSh.Items;
using KM.SwSh.Pokemon;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;
using KM.SV.Encounters;
using KM.SV.Items;
using KM.SV.ModMerger;
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
    private readonly SvModMergerWorkflowService modMergerWorkflowService;
    private readonly SvItemsEditSessionService itemsEditSessionService;
    private readonly SvPokemonEditSessionService pokemonEditSessionService;
    private readonly SvTrainersEditSessionService trainersEditSessionService;
    private readonly SvEncountersEditSessionService encountersEditSessionService;

    public SvWorkflowService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        var fileSource = new SvWorkflowFileSource();
        itemsWorkflowService = new SvItemsWorkflowService(fileSource);
        pokemonWorkflowService = new SvPokemonWorkflowService(fileSource);
        trainersWorkflowService = new SvTrainersWorkflowService(fileSource);
        encountersWorkflowService = new SvEncountersWorkflowService(fileSource);
        modMergerWorkflowService = new SvModMergerWorkflowService(this.projectWorkspaceService);
        itemsEditSessionService = new SvItemsEditSessionService(this.projectWorkspaceService, fileSource, itemsWorkflowService);
        pokemonEditSessionService = new SvPokemonEditSessionService(this.projectWorkspaceService, fileSource, pokemonWorkflowService);
        trainersEditSessionService = new SvTrainersEditSessionService(this.projectWorkspaceService, fileSource, trainersWorkflowService);
        encountersEditSessionService = new SvEncountersEditSessionService(this.projectWorkspaceService, fileSource, encountersWorkflowService);
    }

    public SwShWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame))
        {
            return new SwShWorkflowList([]);
        }

        var project = projectWorkspaceService.Open(paths);
        return new SwShWorkflowList([modMergerWorkflowService.CreateSummary(project)]);
    }

    public SwShItemsWorkflow LoadItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return itemsWorkflowService.Load(project);
    }

    public SwShPokemonWorkflow LoadPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return pokemonWorkflowService.Load(project);
    }

    public SwShTrainersWorkflow LoadTrainers(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return trainersWorkflowService.Load(project);
    }

    public SwShEncountersWorkflow LoadEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return encountersWorkflowService.Load(project);
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

    public SwShItemsEditResult UpdateItemField(
        ProjectPaths paths,
        EditSession? session,
        int itemId,
        string field,
        string value)
    {
        return itemsEditSessionService.UpdateField(paths, session, itemId, field, value);
    }

    public SwShPokemonEditResult UpdatePokemonField(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string field,
        string value)
    {
        return pokemonEditSessionService.UpdateField(paths, session, personalId, field, value);
    }

    public SwShPokemonEditResult UpdatePokemonLearnset(
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

    public SwShPokemonEditResult UpdatePokemonEvolution(
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

    public SwShTrainersEditResult UpdateTrainerField(
        ProjectPaths paths,
        EditSession? session,
        int trainerId,
        int? slot,
        string field,
        string value)
    {
        return trainersEditSessionService.UpdateField(paths, session, trainerId, slot, field, value);
    }

    public SwShEncountersEditResult UpdateEncounterSlotField(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot,
        string field,
        string value)
    {
        return encountersEditSessionService.UpdateSlotField(paths, session, tableId, slot, field, value);
    }

    public SwShEditSessionValidation ValidateEditSession(ProjectPaths paths, EditSession session)
    {
        return GetDomain(session) switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Trainers => trainersEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Encounters => encountersEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedValidation(session),
            _ => itemsEditSessionService.Validate(paths, session),
        };
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        return GetDomain(session) switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.CreateChangePlan(paths, session),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.CreateChangePlan(paths, session),
            SvEditSessionDomain.Trainers => trainersEditSessionService.CreateChangePlan(paths, session),
            SvEditSessionDomain.Encounters => encountersEditSessionService.CreateChangePlan(paths, session),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedChangePlan(session),
            _ => itemsEditSessionService.CreateChangePlan(paths, session),
        };
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan changePlan)
    {
        return GetDomain(session) switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.ApplyChangePlan(paths, session, changePlan),
            SvEditSessionDomain.Trainers => trainersEditSessionService.ApplyChangePlan(paths, session, changePlan),
            SvEditSessionDomain.Encounters => encountersEditSessionService.ApplyChangePlan(paths, session, changePlan),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedApplyResult(session),
            _ => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan),
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
            _ => SvEditSessionDomain.Mixed,
        };
    }

    private static SwShEditSessionValidation CreateUnsupportedMixedValidation(EditSession session)
    {
        return new SwShEditSessionValidation(session, IsValid: false, [CreateMixedDiagnostic()]);
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
        Mixed,
    }
}
