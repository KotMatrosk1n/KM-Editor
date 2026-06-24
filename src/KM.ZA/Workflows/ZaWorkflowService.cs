// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Moves;
using KM.ZA.Pokemon;

namespace KM.ZA.Workflows;

public sealed class ZaWorkflowService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaCacheManager cacheManager;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaPokemonWorkflowService pokemonWorkflowService;
    private readonly ZaMovesWorkflowService movesWorkflowService;
    private readonly ZaPokemonEditSessionService pokemonEditSessionService;
    private readonly ZaMovesEditSessionService movesEditSessionService;

    public ZaWorkflowService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaCacheManager? cacheManager = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.cacheManager = cacheManager ?? new ZaCacheManager();
        fileSource = new ZaWorkflowFileSource(this.cacheManager);
        pokemonWorkflowService = new ZaPokemonWorkflowService(fileSource);
        movesWorkflowService = new ZaMovesWorkflowService(fileSource);
        pokemonEditSessionService = new ZaPokemonEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            pokemonWorkflowService);
        movesEditSessionService = new ZaMovesEditSessionService(
            this.projectWorkspaceService,
            fileSource,
            movesWorkflowService);
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
            movesWorkflowService.CreateSummary(project),
        ]);
    }

    public ZaPokemonWorkflow LoadPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return pokemonWorkflowService.Load(project);
    }

    public ZaMovesWorkflow LoadMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        return movesWorkflowService.Load(project);
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

    public ZaMovesEditResult UpdateMoveField(
        ProjectPaths paths,
        EditSession? session,
        int moveId,
        string field,
        string value)
    {
        return movesEditSessionService.UpdateField(paths, session, moveId, field, value);
    }

    public ZaMovesEditResult UpdateMoveFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaMoveFieldUpdate> updates)
    {
        return movesEditSessionService.UpdateFields(paths, session, updates);
    }

    public ZaEditSessionValidation ValidateEditSession(ProjectPaths paths, EditSession session)
    {
        var domain = GetDomain(session);
        return domain == ZaEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? ValidateNormalDomains(paths, session, domains)
            : ValidateSingleDomain(paths, session, domain);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, ZaOutputMode outputMode)
    {
        var domain = GetDomain(session);
        return domain == ZaEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? CreateNormalDomainChangePlan(paths, session, domains, outputMode)
            : CreateSingleDomainChangePlan(paths, session, domain, outputMode);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan, ZaOutputMode outputMode)
    {
        var domain = GetDomain(session);
        return domain == ZaEditSessionDomain.Mixed && TryGetNormalDomains(session, out var domains)
            ? ApplyNormalDomainChangePlan(paths, session, reviewedPlan, domains, outputMode)
            : ApplySingleDomainChangePlan(paths, session, reviewedPlan, domain, outputMode);
    }

    private ZaEditSessionValidation ValidateSingleDomain(
        ProjectPaths paths,
        EditSession session,
        ZaEditSessionDomain domain)
    {
        return domain switch
        {
            ZaEditSessionDomain.Moves => movesEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Pokemon => pokemonEditSessionService.Validate(paths, session),
            ZaEditSessionDomain.Mixed => CreateUnsupportedMixedValidation(session),
            _ => pokemonEditSessionService.Validate(paths, session),
        };
    }

    private ChangePlan CreateSingleDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaEditSessionDomain domain,
        ZaOutputMode outputMode)
    {
        return domain switch
        {
            ZaEditSessionDomain.Moves => movesEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Pokemon => pokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
            ZaEditSessionDomain.Mixed => CreateUnsupportedMixedChangePlan(session),
            _ => pokemonEditSessionService.CreateChangePlan(paths, session, outputMode),
        };
    }

    private ApplyResult ApplySingleDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        ZaEditSessionDomain domain,
        ZaOutputMode outputMode)
    {
        return domain switch
        {
            ZaEditSessionDomain.Moves => movesEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Pokemon => pokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
            ZaEditSessionDomain.Mixed => CreateUnsupportedMixedApplyResult(session),
            _ => pokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan, outputMode),
        };
    }

    private ZaEditSessionValidation ValidateNormalDomains(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<ZaEditSessionDomain> domains)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var domain in domains)
        {
            var validation = ValidateSingleDomain(paths, SliceSession(session, domain), domain);
            diagnostics.AddRange(validation.Diagnostics);
        }

        return new ZaEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    private ChangePlan CreateNormalDomainChangePlan(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<ZaEditSessionDomain> domains,
        ZaOutputMode outputMode)
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
        IReadOnlyList<ZaEditSessionDomain> domains,
        ZaOutputMode outputMode)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateNormalDomainChangePlan(paths, session, domains, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                "za.editor",
                expected: "Current reviewed Pokemon Legends Z-A change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
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

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
    }

    private static ZaEditSessionDomain GetDomain(EditSession session)
    {
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return domains switch
        {
            [] => ZaEditSessionDomain.None,
            [ZaEditSessionSupport.PokemonDomain] => ZaEditSessionDomain.Pokemon,
            [ZaEditSessionSupport.MovesDomain] => ZaEditSessionDomain.Moves,
            _ => ZaEditSessionDomain.Mixed,
        };
    }

    private static bool TryGetNormalDomains(
        EditSession session,
        out IReadOnlyList<ZaEditSessionDomain> domains)
    {
        var orderedDomains = session.PendingEdits
            .Select(edit => GetDomain(edit.Domain))
            .Where(domain => domain != ZaEditSessionDomain.None)
            .Distinct()
            .ToArray();

        domains = orderedDomains;
        return orderedDomains.Length > 1 && orderedDomains.All(IsNormalDomain);
    }

    private static ZaEditSessionDomain GetDomain(string? domain)
    {
        return domain switch
        {
            ZaEditSessionSupport.PokemonDomain => ZaEditSessionDomain.Pokemon,
            ZaEditSessionSupport.MovesDomain => ZaEditSessionDomain.Moves,
            null or "" => ZaEditSessionDomain.None,
            _ => ZaEditSessionDomain.Mixed,
        };
    }

    private static bool IsNormalDomain(ZaEditSessionDomain domain)
    {
        return domain is ZaEditSessionDomain.Pokemon or ZaEditSessionDomain.Moves;
    }

    private static EditSession SliceSession(EditSession session, ZaEditSessionDomain domain)
    {
        var domainName = GetDomainName(domain);
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => string.Equals(edit.Domain, domainName, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static string GetDomainName(ZaEditSessionDomain domain)
    {
        return domain switch
        {
            ZaEditSessionDomain.Pokemon => ZaEditSessionSupport.PokemonDomain,
            ZaEditSessionDomain.Moves => ZaEditSessionSupport.MovesDomain,
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

    private static ZaEditSessionValidation CreateUnsupportedMixedValidation(EditSession session)
    {
        return new ZaEditSessionValidation(session, IsValid: false, [CreateMixedDiagnostic()]);
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
            Array.Empty<ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, plan.Writes),
            plan.Diagnostics);
    }

    private static ValidationDiagnostic CreateMixedDiagnostic()
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pokemon Legends Z-A edit sessions cannot mix unsupported workflow domains in one change plan yet.",
            "za.editor",
            expected: "Pending edits from supported Z-A editor domains");
    }

    private enum ZaEditSessionDomain
    {
        None,
        Pokemon,
        Moves,
        Mixed,
    }
}
