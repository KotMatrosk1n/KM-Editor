// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Encounters;
using KM.SV.Gifts;
using KM.SV.HyperspaceBypass;
using KM.SV.Items;
using KM.SV.ModMerger;
using KM.SV.Moves;
using KM.SV.Placement;
using KM.SV.Pokemon;
using KM.SV.Trainers;
using KM.SV.Trades;
using KM.SV.TypeChart;

namespace KM.SV.Workflows;

public sealed class SvWorkflowService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvItemsWorkflowService itemsWorkflowService;
    private readonly SvMovesWorkflowService movesWorkflowService;
    private readonly SvPokemonWorkflowService pokemonWorkflowService;
    private readonly SvTrainersWorkflowService trainersWorkflowService;
    private readonly SvEncountersWorkflowService encountersWorkflowService;
    private readonly SvGiftPokemonWorkflowService giftPokemonWorkflowService;
    private readonly SvTradePokemonWorkflowService tradePokemonWorkflowService;
    private readonly SvPlacementWorkflowService placementWorkflowService;
    private readonly SvTypeChartWorkflowService typeChartWorkflowService;
    private readonly SvHyperspaceBypassWorkflowService hyperspaceBypassWorkflowService;
    private readonly SvModMergerWorkflowService modMergerWorkflowService;
    private readonly SvItemsEditSessionService itemsEditSessionService;
    private readonly SvMovesEditSessionService movesEditSessionService;
    private readonly SvPokemonEditSessionService pokemonEditSessionService;
    private readonly SvTrainersEditSessionService trainersEditSessionService;
    private readonly SvEncountersEditSessionService encountersEditSessionService;
    private readonly SvGiftPokemonEditSessionService giftPokemonEditSessionService;
    private readonly SvTradePokemonEditSessionService tradePokemonEditSessionService;
    private readonly SvPlacementEditSessionService placementEditSessionService;
    private readonly SvTypeChartEditSessionService typeChartEditSessionService;
    private readonly SvHyperspaceBypassEditSessionService hyperspaceBypassEditSessionService;

    public SvWorkflowService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        var fileSource = new SvWorkflowFileSource();
        itemsWorkflowService = new SvItemsWorkflowService(fileSource);
        movesWorkflowService = new SvMovesWorkflowService(fileSource);
        pokemonWorkflowService = new SvPokemonWorkflowService(fileSource);
        trainersWorkflowService = new SvTrainersWorkflowService(fileSource);
        encountersWorkflowService = new SvEncountersWorkflowService(fileSource);
        giftPokemonWorkflowService = new SvGiftPokemonWorkflowService(fileSource);
        tradePokemonWorkflowService = new SvTradePokemonWorkflowService(fileSource);
        placementWorkflowService = new SvPlacementWorkflowService(fileSource);
        typeChartWorkflowService = new SvTypeChartWorkflowService();
        hyperspaceBypassWorkflowService = new SvHyperspaceBypassWorkflowService();
        modMergerWorkflowService = new SvModMergerWorkflowService(this.projectWorkspaceService);
        itemsEditSessionService = new SvItemsEditSessionService(this.projectWorkspaceService, fileSource, itemsWorkflowService);
        movesEditSessionService = new SvMovesEditSessionService(this.projectWorkspaceService, fileSource, movesWorkflowService);
        pokemonEditSessionService = new SvPokemonEditSessionService(this.projectWorkspaceService, fileSource, pokemonWorkflowService);
        trainersEditSessionService = new SvTrainersEditSessionService(this.projectWorkspaceService, fileSource, trainersWorkflowService);
        encountersEditSessionService = new SvEncountersEditSessionService(this.projectWorkspaceService, fileSource, encountersWorkflowService);
        giftPokemonEditSessionService = new SvGiftPokemonEditSessionService(this.projectWorkspaceService, fileSource, giftPokemonWorkflowService);
        tradePokemonEditSessionService = new SvTradePokemonEditSessionService(this.projectWorkspaceService, fileSource, tradePokemonWorkflowService);
        placementEditSessionService = new SvPlacementEditSessionService(this.projectWorkspaceService, fileSource, placementWorkflowService);
        typeChartEditSessionService = new SvTypeChartEditSessionService(
            this.projectWorkspaceService,
            typeChartWorkflowService);
        hyperspaceBypassEditSessionService = new SvHyperspaceBypassEditSessionService(
            this.projectWorkspaceService,
            hyperspaceBypassWorkflowService);
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
            movesWorkflowService.CreateSummary(project),
            pokemonWorkflowService.CreateSummary(project),
            trainersWorkflowService.CreateSummary(project),
            encountersWorkflowService.CreateSummary(project),
            giftPokemonWorkflowService.CreateSummary(project),
            tradePokemonWorkflowService.CreateSummary(project),
            placementWorkflowService.CreateSummary(project),
            typeChartWorkflowService.CreateSummary(project),
            hyperspaceBypassWorkflowService.CreateSummary(project),
            modMergerWorkflowService.CreateSummary(project),
        ]);
    }

    public SvItemsWorkflow LoadItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return itemsWorkflowService.Load(project);
    }

    public SvMovesWorkflow LoadMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return movesWorkflowService.Load(project);
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

    public SvGiftPokemonWorkflow LoadGiftPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return giftPokemonWorkflowService.Load(project);
    }

    public SvTradePokemonWorkflow LoadTradePokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return tradePokemonWorkflowService.Load(project);
    }

    public SvPlacementWorkflow LoadPlacement(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return placementWorkflowService.Load(project);
    }

    public SvHyperspaceBypassWorkflow LoadHyperspaceBypass(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return hyperspaceBypassWorkflowService.Load(project);
    }

    public SvTypeChartWorkflow LoadTypeChart(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return typeChartWorkflowService.Load(project);
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

    public SvItemsEditResult UpdateItemFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvItemFieldUpdate> updates)
    {
        return itemsEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvMovesEditResult UpdateMoveField(
        ProjectPaths paths,
        EditSession? session,
        int moveId,
        string field,
        string value)
    {
        return movesEditSessionService.UpdateField(paths, session, moveId, field, value);
    }

    public SvMovesEditResult UpdateMoveFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvMoveFieldUpdate> updates)
    {
        return movesEditSessionService.UpdateFields(paths, session, updates);
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

    public SvPokemonEditResult UpdatePokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvPokemonFieldUpdate> updates)
    {
        return pokemonEditSessionService.UpdateFields(paths, session, updates);
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

    public SvTrainersEditResult UpdateTrainerFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvTrainerFieldUpdate> updates)
    {
        return trainersEditSessionService.UpdateFields(paths, session, updates);
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

    public SvEncountersEditResult UpdateEncounterSlotFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvEncounterSlotFieldUpdate> updates)
    {
        return encountersEditSessionService.UpdateSlotFields(paths, session, updates);
    }

    public SvGiftPokemonEditResult UpdateGiftPokemonField(
        ProjectPaths paths,
        EditSession? session,
        int giftIndex,
        string field,
        string value)
    {
        return giftPokemonEditSessionService.UpdateField(paths, session, giftIndex, field, value);
    }

    public SvGiftPokemonEditResult UpdateGiftPokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvGiftPokemonFieldUpdate> updates)
    {
        return giftPokemonEditSessionService.UpdateFields(paths, session, updates);
    }

    public SvTradePokemonEditResult UpdateTradePokemonField(
        ProjectPaths paths,
        EditSession? session,
        int tradeIndex,
        string field,
        string value)
    {
        return tradePokemonEditSessionService.UpdateField(paths, session, tradeIndex, field, value);
    }

    public SvTradePokemonEditResult UpdateTradePokemonFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvTradePokemonFieldUpdate> updates)
    {
        return tradePokemonEditSessionService.UpdateFields(paths, session, updates);
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

    public SvPlacementEditResult UpdatePlacementObjectFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvPlacementObjectFieldUpdate> updates)
    {
        return placementEditSessionService.UpdateObjectFields(paths, session, updates);
    }

    public SvHyperspaceBypassEditResult StageHyperspaceBypassInstall(
        ProjectPaths paths,
        EditSession? session)
    {
        return hyperspaceBypassEditSessionService.StageInstall(paths, session);
    }

    public SvTypeChartEditResult StageTypeChart(
        ProjectPaths paths,
        IReadOnlyList<int> values,
        EditSession? session)
    {
        return typeChartEditSessionService.StageChart(paths, values, session);
    }

    public SvTypeChartEditResult StageTypeChartUninstall(
        ProjectPaths paths,
        EditSession? session)
    {
        return typeChartEditSessionService.StageUninstall(paths, session);
    }

    public SvHyperspaceBypassEditResult StageHyperspaceBypassUninstall(
        ProjectPaths paths,
        EditSession? session)
    {
        return hyperspaceBypassEditSessionService.StageUninstall(paths, session);
    }

    public SvEditSessionValidation ValidateEditSession(ProjectPaths paths, EditSession session)
    {
        var domain = GetDomain(session);
        return domain == SvEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? ValidateNormalDomains(paths, session, domains)
            : ValidateSingleDomain(paths, session, domain);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        var domain = GetDomain(session);
        return domain == SvEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? CreateNormalDomainChangePlan(paths, session, domains, outputMode)
            : CreateSingleDomainChangePlan(paths, session, domain, outputMode);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan changePlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        var domain = GetDomain(session);
        return domain == SvEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? ApplyNormalDomainChangePlan(paths, session, changePlan, domains, outputMode)
            : ApplySingleDomainChangePlan(paths, session, changePlan, domain, outputMode);
    }

    private SvEditSessionValidation ValidateSingleDomain(
        ProjectPaths paths,
        EditSession session,
        SvEditSessionDomain domain)
    {
        return domain switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Moves => movesEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Trainers => trainersEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Encounters => encountersEditSessionService.Validate(paths, session),
            SvEditSessionDomain.GiftPokemon => giftPokemonEditSessionService.Validate(paths, session),
            SvEditSessionDomain.TradePokemon => tradePokemonEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Placement => placementEditSessionService.Validate(paths, session),
            SvEditSessionDomain.TypeChart => typeChartEditSessionService.Validate(paths, session),
            SvEditSessionDomain.HyperspaceBypass => hyperspaceBypassEditSessionService.Validate(paths, session),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedValidation(session),
            _ => itemsEditSessionService.Validate(paths, session),
        };
    }

    private ChangePlan CreateSingleDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvEditSessionDomain domain,
        SvOutputMode outputMode)
    {
        return domain switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Moves => movesEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Trainers => trainersEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Encounters => encountersEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.GiftPokemon => giftPokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.TradePokemon => tradePokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Placement => placementEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.TypeChart => typeChartEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.HyperspaceBypass => hyperspaceBypassEditSessionService.CreateChangePlan(paths, session, outputMode),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedChangePlan(session),
            _ => itemsEditSessionService.CreateChangePlan(paths, session, outputMode),
        };
    }

    private ApplyResult ApplySingleDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan changePlan,
        SvEditSessionDomain domain,
        SvOutputMode outputMode)
    {
        return domain switch
        {
            SvEditSessionDomain.Items => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Moves => movesEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Pokemon => pokemonEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Trainers => trainersEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Encounters => encountersEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.GiftPokemon => giftPokemonEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.TradePokemon => tradePokemonEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Placement => placementEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.TypeChart => typeChartEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.HyperspaceBypass => hyperspaceBypassEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
            SvEditSessionDomain.Mixed => CreateUnsupportedMixedApplyResult(session),
            _ => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan, outputMode),
        };
    }

    private SvEditSessionValidation ValidateNormalDomains(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<SvEditSessionDomain> domains)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var domain in domains)
        {
            var validation = ValidateSingleDomain(paths, SliceSession(session, domain), domain);
            diagnostics.AddRange(validation.Diagnostics);
        }

        return new SvEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    private ChangePlan CreateNormalDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<SvEditSessionDomain> domains,
        SvOutputMode outputMode)
    {
        var validation = ValidateNormalDomains(paths, session, domains);
        var diagnostics = validation.Diagnostics.ToList();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        foreach (var domain in domains)
        {
            var domainPlan = CreateSingleDomainChangePlan(paths, SliceSession(session, domain), domain, outputMode);
            diagnostics.AddRange(domainPlan.Diagnostics);
            writes.AddRange(domainPlan.Writes);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        return new ChangePlan(session.Id, CombinePlannedWrites(writes), diagnostics);
    }

    private ApplyResult ApplyNormalDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        IReadOnlyList<SvEditSessionDomain> domains,
        SvOutputMode outputMode)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateNormalDomainChangePlan(paths, session, domains, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                "sv.editor",
                expected: "Current reviewed Scarlet/Violet change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        foreach (var domain in domains)
        {
            var domainSession = SliceSession(session, domain);
            var domainPlan = CreateSingleDomainChangePlan(paths, domainSession, domain, outputMode);
            var result = ApplySingleDomainChangePlan(paths, domainSession, domainPlan, domain, outputMode);
            diagnostics.AddRange(result.Diagnostics);
            writtenFiles.AddRange(result.WrittenFiles);

            if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                break;
            }
        }

        return SvEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
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
            [SvEditSessionSupport.MovesDomain] => SvEditSessionDomain.Moves,
            [SvEditSessionSupport.PokemonDomain] => SvEditSessionDomain.Pokemon,
            [SvEditSessionSupport.TrainersDomain] => SvEditSessionDomain.Trainers,
            [SvEditSessionSupport.EncountersDomain] => SvEditSessionDomain.Encounters,
            [SvEditSessionSupport.GiftPokemonDomain] => SvEditSessionDomain.GiftPokemon,
            [SvEditSessionSupport.TradePokemonDomain] => SvEditSessionDomain.TradePokemon,
            [SvEditSessionSupport.PlacementDomain] => SvEditSessionDomain.Placement,
            [SvTypeChartEditSessionService.TypeChartEditDomain] => SvEditSessionDomain.TypeChart,
            [SvHyperspaceBypassEditSessionService.HyperspaceBypassEditDomain] => SvEditSessionDomain.HyperspaceBypass,
            _ => SvEditSessionDomain.Mixed,
        };
    }

    private static bool TryGetNormalDomains(
        EditSession session,
        out IReadOnlyList<SvEditSessionDomain> domains)
    {
        var orderedDomains = session.PendingEdits
            .Select(edit => GetDomain(edit.Domain))
            .Where(domain => domain != SvEditSessionDomain.None)
            .Distinct()
            .ToArray();

        domains = orderedDomains;
        return orderedDomains.Length > 1 && orderedDomains.All(IsNormalDomain);
    }

    private static SvEditSessionDomain GetDomain(string? domain)
    {
        return domain switch
        {
            SvEditSessionSupport.ItemsDomain => SvEditSessionDomain.Items,
            SvEditSessionSupport.MovesDomain => SvEditSessionDomain.Moves,
            SvEditSessionSupport.PokemonDomain => SvEditSessionDomain.Pokemon,
            SvEditSessionSupport.TrainersDomain => SvEditSessionDomain.Trainers,
            SvEditSessionSupport.EncountersDomain => SvEditSessionDomain.Encounters,
            SvEditSessionSupport.GiftPokemonDomain => SvEditSessionDomain.GiftPokemon,
            SvEditSessionSupport.TradePokemonDomain => SvEditSessionDomain.TradePokemon,
            SvEditSessionSupport.PlacementDomain => SvEditSessionDomain.Placement,
            SvTypeChartEditSessionService.TypeChartEditDomain => SvEditSessionDomain.TypeChart,
            SvHyperspaceBypassEditSessionService.HyperspaceBypassEditDomain => SvEditSessionDomain.HyperspaceBypass,
            null or "" => SvEditSessionDomain.None,
            _ => SvEditSessionDomain.Mixed,
        };
    }

    private static bool IsNormalDomain(SvEditSessionDomain domain)
    {
        return domain is
            SvEditSessionDomain.Items or
            SvEditSessionDomain.Moves or
            SvEditSessionDomain.Pokemon or
            SvEditSessionDomain.Trainers or
            SvEditSessionDomain.Encounters or
            SvEditSessionDomain.GiftPokemon or
            SvEditSessionDomain.TradePokemon or
            SvEditSessionDomain.Placement;
    }

    private static EditSession SliceSession(EditSession session, SvEditSessionDomain domain)
    {
        var domainName = GetDomainName(domain);
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => string.Equals(edit.Domain, domainName, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static string GetDomainName(SvEditSessionDomain domain)
    {
        return domain switch
        {
            SvEditSessionDomain.Items => SvEditSessionSupport.ItemsDomain,
            SvEditSessionDomain.Moves => SvEditSessionSupport.MovesDomain,
            SvEditSessionDomain.Pokemon => SvEditSessionSupport.PokemonDomain,
            SvEditSessionDomain.Trainers => SvEditSessionSupport.TrainersDomain,
            SvEditSessionDomain.Encounters => SvEditSessionSupport.EncountersDomain,
            SvEditSessionDomain.GiftPokemon => SvEditSessionSupport.GiftPokemonDomain,
            SvEditSessionDomain.TradePokemon => SvEditSessionSupport.TradePokemonDomain,
            SvEditSessionDomain.Placement => SvEditSessionSupport.PlacementDomain,
            SvEditSessionDomain.TypeChart => SvTypeChartEditSessionService.TypeChartEditDomain,
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<PlannedFileWrite> CombinePlannedWrites(IEnumerable<PlannedFileWrite> writes)
    {
        return writes
            .GroupBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .Select(group =>
            {
                var groupedWrites = group.ToArray();
                if (groupedWrites.Length == 1)
                {
                    return groupedWrites[0];
                }

                return new PlannedFileWrite(
                    group.Key,
                    groupedWrites
                        .SelectMany(write => write.Sources)
                        .Distinct()
                        .ToArray(),
                    groupedWrites.Any(write => write.ReplacesExistingOutput),
                    string.Join(
                        " ",
                        groupedWrites
                            .Select(write => write.Reason)
                            .Where(reason => !string.IsNullOrWhiteSpace(reason))
                            .Distinct(StringComparer.Ordinal)));
            })
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();
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
        Moves,
        Pokemon,
        Trainers,
        Encounters,
        GiftPokemon,
        TradePokemon,
        Placement,
        TypeChart,
        HyperspaceBypass,
        Mixed,
    }
}
