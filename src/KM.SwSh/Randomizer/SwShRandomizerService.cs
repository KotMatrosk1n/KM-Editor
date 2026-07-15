// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.Encounters;
using KM.SwSh.Gifts;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.RoyalCandy;
using KM.SwSh.StaticEncounters;
using KM.SwSh.TypeChart;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KM.SwSh.Randomizer;

public sealed class SwShRandomizerService
{
    private const int ExpandedLearnsetMoveCount = 25;
    private const int ExpandedLearnsetMaxLevel = 75;
    private const string RandomizerDomain = "workflow.randomizer";
    private const string PokemonEditDomain = "workflow.pokemon";
    private const string EncountersEditDomain = "workflow.encounters";
    private const string RandomizerManifestRelativePath = ".km-editor/randomizer-manifest.json";
    private const string RandomizerBackupDirectoryRelativePath = ".km-editor/randomizer-backups";
    private const int RoyalCandyItemId = 1128;
    private const int PokemonTypeShapeChangeChancePercent = 30;
    private const int ZacianSpeciesId = 888;
    private const int ZamazentaSpeciesId = 889;
    private const int EternatusSpeciesId = 890;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly string[] FirstMoveIds =
    [
        "1",
        "40",
        "52",
        "55",
        "64",
        "71",
        "84",
        "98",
        "122",
        "141",
    ];

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShPokemonWorkflowService pokemonWorkflowService;
    private readonly SwShPokemonEditSessionService pokemonEditSessionService;
    private readonly SwShMovesWorkflowService movesWorkflowService;
    private readonly SwShItemsWorkflowService itemsWorkflowService;
    private readonly SwShEncountersWorkflowService encountersWorkflowService;
    private readonly SwShEncountersEditSessionService encountersEditSessionService;
    private readonly SwShStaticEncountersWorkflowService staticEncountersWorkflowService;
    private readonly SwShStaticEncountersEditSessionService staticEncountersEditSessionService;
    private readonly SwShGiftPokemonWorkflowService giftPokemonWorkflowService;
    private readonly SwShGiftPokemonEditSessionService giftPokemonEditSessionService;
    private readonly SwShRaidRewardsWorkflowService raidRewardsWorkflowService;
    private readonly SwShRaidRewardsEditSessionService raidRewardsEditSessionService;
    private readonly SwShRoyalCandyWorkflowService royalCandyWorkflowService;
    private readonly SwShTypeChartWorkflowService typeChartWorkflowService;
    private readonly SwShTypeChartEditSessionService typeChartEditSessionService;

    internal Action<SwShRandomizerRestoreMutationStage, string>? RestoreMutationHook { get; init; }

    public SwShRandomizerService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShPokemonWorkflowService? pokemonWorkflowService = null,
        SwShPokemonEditSessionService? pokemonEditSessionService = null,
        SwShMovesWorkflowService? movesWorkflowService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null,
        SwShEncountersWorkflowService? encountersWorkflowService = null,
        SwShEncountersEditSessionService? encountersEditSessionService = null,
        SwShStaticEncountersWorkflowService? staticEncountersWorkflowService = null,
        SwShStaticEncountersEditSessionService? staticEncountersEditSessionService = null,
        SwShGiftPokemonWorkflowService? giftPokemonWorkflowService = null,
        SwShGiftPokemonEditSessionService? giftPokemonEditSessionService = null,
        SwShRaidRewardsWorkflowService? raidRewardsWorkflowService = null,
        SwShRaidRewardsEditSessionService? raidRewardsEditSessionService = null,
        SwShRoyalCandyWorkflowService? royalCandyWorkflowService = null,
        SwShTypeChartWorkflowService? typeChartWorkflowService = null,
        SwShTypeChartEditSessionService? typeChartEditSessionService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.pokemonWorkflowService = pokemonWorkflowService ?? new SwShPokemonWorkflowService();
        this.pokemonEditSessionService = pokemonEditSessionService ?? new SwShPokemonEditSessionService(this.projectWorkspaceService, this.pokemonWorkflowService);
        this.movesWorkflowService = movesWorkflowService ?? new SwShMovesWorkflowService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
        this.encountersWorkflowService = encountersWorkflowService ?? new SwShEncountersWorkflowService();
        this.encountersEditSessionService = encountersEditSessionService ?? new SwShEncountersEditSessionService(this.projectWorkspaceService, this.encountersWorkflowService);
        this.staticEncountersWorkflowService = staticEncountersWorkflowService ?? new SwShStaticEncountersWorkflowService();
        this.staticEncountersEditSessionService = staticEncountersEditSessionService ?? new SwShStaticEncountersEditSessionService(this.projectWorkspaceService, this.staticEncountersWorkflowService);
        this.giftPokemonWorkflowService = giftPokemonWorkflowService ?? new SwShGiftPokemonWorkflowService();
        this.giftPokemonEditSessionService = giftPokemonEditSessionService ?? new SwShGiftPokemonEditSessionService(this.projectWorkspaceService, this.giftPokemonWorkflowService);
        this.raidRewardsWorkflowService = raidRewardsWorkflowService ?? new SwShRaidRewardsWorkflowService();
        this.raidRewardsEditSessionService = raidRewardsEditSessionService ?? new SwShRaidRewardsEditSessionService(this.projectWorkspaceService, this.raidRewardsWorkflowService);
        this.royalCandyWorkflowService = royalCandyWorkflowService ?? new SwShRoyalCandyWorkflowService();
        this.typeChartWorkflowService = typeChartWorkflowService ?? new SwShTypeChartWorkflowService();
        this.typeChartEditSessionService = typeChartEditSessionService ?? new SwShTypeChartEditSessionService(this.projectWorkspaceService, this.typeChartWorkflowService);
    }

    public SwShRandomizerImportResult ImportSeed(string seed)
    {
        return SwShRandomizerSeedCodec.Import(seed);
    }

    internal SwShRandomizerPreviewResult Preview(ProjectPaths paths, SwShRandomizerConfig config)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(config);

        var build = BuildDomainPlans(paths, config);
        return new SwShRandomizerPreviewResult(
            build.Config,
            build.Seed,
            CollapseDiagnostics(build.Diagnostics),
            build.DomainPlans
                .Select(plan => new SwShRandomizerPreviewDomain(
                    plan.Label,
                    plan.Session.PendingEdits
                        .Select(edit => new SwShRandomizerPreviewEdit(
                            edit.Domain ?? string.Empty,
                            edit.RecordId ?? string.Empty,
                            edit.Field ?? string.Empty,
                            edit.NewValue ?? string.Empty,
                            edit.Summary))
                        .ToArray()))
                .ToArray());
    }

    public SwShRandomizerApplyResult Apply(ProjectPaths paths, SwShRandomizerConfig config)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(config);

        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(
            paths,
            out var stablePaths,
            out var stableRootFailure))
        {
            var failedConfig = PrepareConfig(config);
            return new SwShRandomizerApplyResult(
                failedConfig,
                SwShRandomizerSeedCodec.Export(failedConfig),
                CreateApplyResult(
                [
                    CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        stableRootFailure ?? "Randomizer could not resolve Output Root safely.",
                        expected: "Stable physical Output Root"),
                ]));
        }

        paths = stablePaths;

        var build = BuildDomainPlans(paths, config);
        var normalizedConfig = build.Config;
        var exportedSeed = build.Seed;
        var diagnostics = build.Diagnostics.ToList();
        var domainPlans = build.DomainPlans;

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShRandomizerApplyResult(normalizedConfig, exportedSeed, CreateApplyResult(diagnostics));
        }

        var reviewedDomainPlans = new List<(RandomizerDomainPlan Domain, ChangePlan Plan)>();
        foreach (var domainPlan in domainPlans)
        {
            var previewPlan = domainPlan.CreateChangePlan(paths, domainPlan.Session);
            reviewedDomainPlans.Add((domainPlan, previewPlan));
            diagnostics.AddRange(previewPlan.Diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShRandomizerApplyResult(normalizedConfig, exportedSeed, CreateApplyResult(diagnostics));
        }

        var rollbackTargets = reviewedDomainPlans
            .SelectMany(item => item.Plan.Writes)
            .Select(write => write.TargetRelativePath)
            .Append(RandomizerManifestRelativePath);
        if (!SwShOutputRollbackScope.TryCapture(
            paths,
            rollbackTargets,
            out var rollbackScope,
            out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Randomizer could not snapshot output before apply: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable existing outputs and writable temporary storage"));
            return new SwShRandomizerApplyResult(normalizedConfig, exportedSeed, CreateApplyResult(diagnostics));
        }

        var outputRollback = rollbackScope!;
        using (outputRollback)
        {
            var restoreCapture = CaptureRandomizerRestoreState(
                paths,
                reviewedDomainPlans.SelectMany(item => item.Plan.Writes),
                diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                CleanupCapturedRandomizerBackups(paths, restoreCapture, diagnostics);
                outputRollback.Commit();
                return new SwShRandomizerApplyResult(normalizedConfig, exportedSeed, CreateApplyResult(diagnostics));
            }

            var appliedWrites = new List<ProjectFileReference>();
            var manifestWrites = new List<PlannedFileWrite>();
            foreach (var (domainPlan, _) in reviewedDomainPlans)
            {
                try
                {
                    var currentPlan = domainPlan.CreateChangePlan(paths, domainPlan.Session);
                    diagnostics.AddRange(currentPlan.Diagnostics);
                    if (!currentPlan.CanApply)
                    {
                        break;
                    }

                    var applyResult = domainPlan.ApplyChangePlan(paths, domainPlan.Session, currentPlan);
                    appliedWrites.AddRange(applyResult.WrittenFiles);
                    manifestWrites.AddRange(applyResult.Manifest.Writes);
                    diagnostics.AddRange(applyResult.Diagnostics);
                    if (applyResult.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    {
                        break;
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Randomizer domain apply failed: {exception.Message}",
                        expected: "Every selected Randomizer domain applied successfully"));
                    break;
                }
            }

            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                var writtenFilesBeforeManifest = appliedWrites
                    .DistinctBy(file => $"{file.Layer}:{file.RelativePath}", StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                RecordRandomizerManifest(paths, writtenFilesBeforeManifest, restoreCapture, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                CleanupCapturedRandomizerBackups(paths, restoreCapture, diagnostics);
                RollbackFailedApply(outputRollback, appliedWrites, manifestWrites, diagnostics);
            }
            else
            {
                outputRollback.Commit();
            }

            var uniqueWrittenFiles = appliedWrites
                .DistinctBy(file => $"{file.Layer}:{file.RelativePath}", StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var aggregateApplyId = Guid.NewGuid().ToString("N");
            var appliedAt = DateTimeOffset.UtcNow;
            var aggregateResult = new ApplyResult(
                aggregateApplyId,
                appliedAt,
                uniqueWrittenFiles,
                new WriteManifest(
                    aggregateApplyId,
                    appliedAt,
                    manifestWrites
                        .DistinctBy(write => write.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
                        .ToArray()),
                CollapseDiagnostics(diagnostics));

            return new SwShRandomizerApplyResult(normalizedConfig, exportedSeed, aggregateResult);
        }
    }

    private static void RollbackFailedApply(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> appliedWrites,
        ICollection<PlannedFileWrite> manifestWrites,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var rollbackFailures = rollbackScope.Rollback();
        appliedWrites.Clear();
        manifestWrites.Clear();
        if (rollbackFailures.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Randomizer apply failed and all output changes were rolled back."));
            return;
        }

        foreach (var failure in rollbackFailures)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Randomizer rollback failed: {failure.Message}",
                file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                expected: "Output restored to its exact pre-apply state"));
            if (!string.IsNullOrWhiteSpace(failure.RelativePath))
            {
                appliedWrites.Add(new ProjectFileReference(ProjectFileLayer.Generated, failure.RelativePath));
            }
        }
    }

    private RandomizerBuildResult BuildDomainPlans(ProjectPaths paths, SwShRandomizerConfig config)
    {
        var normalizedConfig = PrepareConfig(config);
        var generationKey = SwShRandomizerSeedCodec.CreateGenerationKey(normalizedConfig);
        var exportedSeed = SwShRandomizerSeedCodec.Export(normalizedConfig);
        var diagnostics = new List<ValidationDiagnostic>();
        var options = normalizedConfig.Options;

        if (!options.HasAnySelection)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "At least one randomizer option must be selected.",
                field: "options",
                expected: "One or more enabled randomizer categories"));
        }

        var project = projectWorkspaceService.Open(paths);
        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Randomizer requires valid base data and a configured LayeredFS output root.",
                expected: "Editable project paths"));
        }

        var pokemonWorkflow = ShouldLoadPokemon(options)
            ? pokemonWorkflowService.Load(project)
            : null;
        if (pokemonWorkflow is not null)
        {
            AddWorkflowErrors(pokemonWorkflow.Diagnostics, diagnostics);
        }

        var pokemonTargets = pokemonWorkflow is null
            ? Array.Empty<PokemonCandidate>()
            : CreatePokemonTargets(pokemonWorkflow).ToArray();
        if (pokemonWorkflow is not null && pokemonTargets.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Randomizer could not find any present, sprite-backed Pokemon forms.",
                field: "pokemon",
                expected: "Loaded Pokemon personal data with legal SwSh forms"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new RandomizerBuildResult(normalizedConfig, exportedSeed, diagnostics, Array.Empty<RandomizerDomainPlan>());
        }

        var movesWorkflow = ShouldLoadMoves(options)
            ? movesWorkflowService.Load(project)
            : null;
        var itemsWorkflow = ShouldLoadItems(options)
            ? itemsWorkflowService.Load(project)
            : null;

        if (movesWorkflow is not null)
        {
            AddWorkflowErrors(movesWorkflow.Diagnostics, diagnostics);
        }

        if (itemsWorkflow is not null)
        {
            AddWorkflowErrors(itemsWorkflow.Diagnostics, diagnostics);
        }

        var movePool = movesWorkflow is null
            ? Array.Empty<MoveCandidate>()
            : CreateMovePool(movesWorkflow, options.LearnsetBanFixedDamageMoves).ToArray();
        if (options.RandomizePokemonLearnsets && movePool.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon learnset randomization requires at least one usable move.",
                field: "learnsets",
                expected: "Loaded move data with usable, non-banned moves"));
        }

        var royalCandyInstalled = IsRoyalCandyInstalled(project);
        var itemPool = itemsWorkflow is null
            ? Array.Empty<ItemCandidate>()
            : CreateItemPool(itemsWorkflow.Items, royalCandyInstalled).ToArray();
        if ((options.RandomizePokemonHeldItems || options.RandomizeRaidRewards || options.RandomizeRaidBonusRewards) && itemPool.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Item randomization requires at least one safe item candidate.",
                field: "items",
                expected: "Loaded item data with non-key, reward-safe item IDs"));
        }

        var domainPlans = new List<RandomizerDomainPlan>();
        AddWildEncounterPlan(project, pokemonTargets, generationKey, options, domainPlans, diagnostics);
        AddStaticPlan(project, pokemonTargets, generationKey, options, domainPlans, diagnostics);
        AddGiftPlan(project, pokemonTargets, generationKey, options, domainPlans, diagnostics);
        AddRaidRewardPlan(project, itemPool, generationKey, options, domainPlans, diagnostics);
        if (pokemonWorkflow is not null)
        {
            AddPokemonPlan(project, pokemonWorkflow, pokemonTargets, movePool, itemPool, generationKey, options, domainPlans, diagnostics);
        }
        AddTypeChartPlan(project, generationKey, options, domainPlans, diagnostics);

        if (domainPlans.Count == 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Selected randomizer options did not produce any editable changes.",
                field: "options",
                expected: "Randomizer options with matching project data"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new RandomizerBuildResult(normalizedConfig, exportedSeed, diagnostics, domainPlans);
        }

        var outputHash = ComputeOutputHash(domainPlans);
        if (!string.IsNullOrWhiteSpace(normalizedConfig.OutputHash)
            && !string.Equals(normalizedConfig.OutputHash, outputHash, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Imported randomizer seed does not match the output that would be generated for the currently loaded project.",
                field: "seed",
                expected: "Project data matching the imported randomizer output"));
        }

        normalizedConfig = string.IsNullOrWhiteSpace(normalizedConfig.OutputHash)
            ? normalizedConfig with { OutputHash = outputHash }
            : normalizedConfig;
        exportedSeed = SwShRandomizerSeedCodec.Export(normalizedConfig);

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new RandomizerBuildResult(normalizedConfig, exportedSeed, diagnostics, domainPlans);
        }

        return new RandomizerBuildResult(normalizedConfig, exportedSeed, diagnostics, domainPlans);
    }

    public ApplyResult Restore(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var diagnostics = new List<ValidationDiagnostic>();
        if (!SwShOutputRollbackScope.TryResolveStableOutputPaths(
            paths,
            out var stablePaths,
            out var stableRootFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                stableRootFailure ?? "Randomizer could not resolve Output Root safely.",
                expected: "Stable physical Output Root"));
            return CreateApplyResult(diagnostics);
        }

        paths = stablePaths;
        var project = projectWorkspaceService.Open(paths);
        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Restore Vanilla Values requires valid base data and a configured LayeredFS output root.",
                expected: "Editable project paths"));
        }

        var outputRoot = project.Paths.OutputRootPath;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Restore Vanilla Values requires a configured LayeredFS output root.",
                expected: "Writable LayeredFS output root"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(diagnostics);
        }

        var manifestPath = ResolveOutputPath(project.Paths, RandomizerManifestRelativePath);
        if (manifestPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Randomizer restore manifest path is not a physical path inside Output Root.",
                file: RandomizerManifestRelativePath,
                expected: "Physical manifest path inside Output Root"));
            return CreateApplyResult(diagnostics);
        }

        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "No Randomizer restore manifest was found. There are no tracked randomizer files to restore.",
                expected: "Previously applied Randomizer output"));
            return CreateApplyResult(diagnostics);
        }

        RandomizerRestoreManifest? manifest;
        RestoreFilePreimage manifestPreimage;
        try
        {
            var manifestBytes = File.ReadAllBytes(manifestPath);
            manifest = JsonSerializer.Deserialize<RandomizerRestoreManifest>(manifestBytes, JsonOptions);
            manifestPreimage = RestoreFilePreimage.ForFile(manifestBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Randomizer restore manifest could not be read: {ex.Message}",
                file: RandomizerManifestRelativePath,
                expected: "Readable Randomizer restore manifest"));
            return CreateApplyResult(diagnostics);
        }

        if (manifest is null || manifest.Version is not (1 or 2))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Randomizer restore manifest version is not supported.",
                file: RandomizerManifestRelativePath,
                expected: "KM Editor Randomizer restore manifest version 1 or 2"));
            return CreateApplyResult(diagnostics);
        }

        var preparedEntries = new List<PreparedRandomizerRestoreEntry>();
        var consumedBackups = new Dictionary<string, PreparedRandomizerBackup>(StringComparer.OrdinalIgnoreCase);
        var remainingEntries = new List<RandomizerRestoreEntry>();
        foreach (var manifestEntry in CreateRestoreEntries(manifest)
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = NormalizeRelativePath(manifestEntry.RelativePath);
            var entry = manifestEntry with { RelativePath = relativePath };
            if (!IsLayeredFsRelativePath(relativePath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "Skipped a tracked Randomizer path because it is outside romfs/exefs.",
                    file: relativePath,
                    expected: "Randomizer-owned romfs or exefs path"));
                remainingEntries.Add(entry);
                continue;
            }

            var targetPath = ResolveOutputPath(project.Paths, relativePath);
            if (targetPath is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Skipped a tracked Randomizer path because it resolves outside Output Root.",
                    file: relativePath,
                    expected: "Path contained by Output Root"));
                remainingEntries.Add(entry);
                continue;
            }

            try
            {
                var targetPreimage = CaptureRestoreFilePreimage(targetPath);
                if (targetPreimage.Kind == RestoreFileKind.Missing)
                {
                    preparedEntries.Add(new PreparedRandomizerRestoreEntry(
                        relativePath,
                        targetPath,
                        targetPreimage,
                        RandomizerRestoreMutationKind.AcknowledgeMissing,
                        Backup: null));
                    TryPrepareConsumedBackup(project.Paths, entry, consumedBackups);
                    if (entry.OriginalExisted == true)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Warning,
                            "Preserved a later file deletion instead of recreating the pre-Randomizer output.",
                            file: relativePath,
                            expected: "Later user changes remain untouched"));
                    }

                    continue;
                }

                if (targetPreimage.Kind != RestoreFileKind.File)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Randomizer output could not be restored because the tracked target is not a file.",
                        file: relativePath,
                        expected: "Tracked Randomizer output file"));
                    remainingEntries.Add(entry);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.RandomizedSha256))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        "Preserved a legacy tracked Randomizer file because its ownership cannot be verified safely.",
                        file: relativePath,
                        expected: "Version 2 manifest with a Randomizer output hash"));
                    remainingEntries.Add(entry);
                    continue;
                }

                if (!string.Equals(targetPreimage.Sha256, entry.RandomizedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        "Preserved a tracked Randomizer file because it changed after Randomizer apply.",
                        file: relativePath,
                        expected: "Current file matching the recorded Randomizer output hash"));
                    remainingEntries.Add(entry);
                    continue;
                }

                if (entry.OriginalExisted is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        "Preserved a tracked Randomizer file because its pre-Randomizer ownership is unknown.",
                        file: relativePath,
                        expected: "Version 2 manifest with pre-Randomizer ownership"));
                    remainingEntries.Add(entry);
                    continue;
                }

                PreparedRandomizerBackup? preparedBackup = null;
                if (entry.OriginalExisted == true)
                {
                    preparedBackup = PrepareRequiredRandomizerBackup(project.Paths, entry, relativePath, diagnostics);
                    if (preparedBackup is null)
                    {
                        remainingEntries.Add(entry);
                        continue;
                    }

                    consumedBackups[preparedBackup.RelativePath] = preparedBackup;
                }
                else
                {
                    TryPrepareConsumedBackup(project.Paths, entry, consumedBackups);
                }

                preparedEntries.Add(new PreparedRandomizerRestoreEntry(
                    relativePath,
                    targetPath,
                    targetPreimage,
                    entry.OriginalExisted == true
                        ? RandomizerRestoreMutationKind.RestoreBackup
                        : RandomizerRestoreMutationKind.DeleteCreatedOutput,
                    preparedBackup));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Randomizer output file could not be prepared for restore: {ex.Message}",
                    file: relativePath,
                    expected: "Readable generated output and backup files"));
                remainingEntries.Add(entry);
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(diagnostics);
        }

        var rollbackPaths = preparedEntries
            .Select(entry => entry.RelativePath)
            .Concat(consumedBackups.Keys)
            .Append(RandomizerManifestRelativePath);
        if (!SwShOutputRollbackScope.TryCapture(
            project.Paths,
            rollbackPaths,
            out var rollbackScope,
            out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Randomizer restore could not snapshot output before changes: {captureFailure?.Message ?? "Unknown snapshot error."}",
                file: captureFailure?.RelativePath,
                expected: "Readable tracked outputs, backups, and restore manifest"));
            return CreateApplyResult(diagnostics);
        }

        var restoredFiles = new List<ProjectFileReference>();
        var outputRollback = rollbackScope!;
        using (outputRollback)
        {
            foreach (var preparedEntry in preparedEntries)
            {
                try
                {
                    RestoreMutationHook?.Invoke(
                        SwShRandomizerRestoreMutationStage.BeforeTargetMutation,
                        preparedEntry.RelativePath);
                    EnsureRestoreMutationTarget(
                        project.Paths,
                        preparedEntry.RelativePath,
                        preparedEntry.TargetPath,
                        preparedEntry.TargetPreimage);
                    if (preparedEntry.Mutation == RandomizerRestoreMutationKind.AcknowledgeMissing)
                    {
                        continue;
                    }

                    if (preparedEntry.Mutation == RandomizerRestoreMutationKind.DeleteCreatedOutput)
                    {
                        File.Delete(preparedEntry.TargetPath);
                        DeleteEmptyParentDirectories(project.Paths.OutputRootPath!, preparedEntry.TargetPath);
                    }
                    else
                    {
                        var backup = preparedEntry.Backup!;
                        EnsureRestoreMutationTarget(
                            project.Paths,
                            backup.RelativePath,
                            backup.Path,
                            backup.Preimage);
                        File.Copy(backup.Path, preparedEntry.TargetPath, overwrite: true);
                    }

                    restoredFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, preparedEntry.RelativePath));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Randomizer output file could not be restored: {ex.Message}",
                        file: preparedEntry.RelativePath,
                        expected: "Unchanged writable generated output file"));
                    return RollbackFailedRestore(outputRollback, restoredFiles, diagnostics);
                }
            }

            try
            {
                RestoreMutationHook?.Invoke(
                    SwShRandomizerRestoreMutationStage.BeforeManifestMutation,
                    RandomizerManifestRelativePath);
                EnsureRestoreMutationTarget(
                    project.Paths,
                    RandomizerManifestRelativePath,
                    manifestPath,
                    manifestPreimage);
                if (remainingEntries.Count == 0)
                {
                    File.Delete(manifestPath);
                    DeleteEmptyParentDirectories(project.Paths.OutputRootPath!, manifestPath);
                }
                else
                {
                    WriteRandomizerManifest(manifestPath, remainingEntries);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Randomizer restore manifest could not be updated: {ex.Message}",
                    file: RandomizerManifestRelativePath,
                    expected: "Unchanged writable Randomizer restore manifest"));
                return RollbackFailedRestore(outputRollback, restoredFiles, diagnostics);
            }

            foreach (var backup in consumedBackups.Values.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    RestoreMutationHook?.Invoke(
                        SwShRandomizerRestoreMutationStage.BeforeBackupDeletion,
                        backup.RelativePath);
                    EnsureRestoreMutationTarget(
                        project.Paths,
                        backup.RelativePath,
                        backup.Path,
                        backup.Preimage);
                    File.Delete(backup.Path);
                    DeleteEmptyParentDirectories(project.Paths.OutputRootPath!, backup.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Randomizer backup could not be consumed: {ex.Message}",
                        file: backup.RelativePath,
                        expected: "Unchanged deletable Randomizer backup"));
                    return RollbackFailedRestore(outputRollback, restoredFiles, diagnostics);
                }
            }

            outputRollback.Commit();
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            restoredFiles.Count == 0
                ? "Restore Vanilla Values completed without replacing or deleting any tracked output files."
                : $"Restore Vanilla Values safely restored {restoredFiles.Count.ToString(CultureInfo.InvariantCulture)} tracked Randomizer output file(s).",
            expected: remainingEntries.Count == 0
                ? "Randomizer outputs restored to their pre-apply state"
                : "Later changes retained; unresolved entries remain tracked"));

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        return new ApplyResult(
            applyId,
            appliedAt,
            restoredFiles,
            new WriteManifest(applyId, appliedAt, Array.Empty<PlannedFileWrite>()),
            diagnostics);
    }

    private static PreparedRandomizerBackup? PrepareRequiredRandomizerBackup(
        ProjectPaths paths,
        RandomizerRestoreEntry entry,
        string ownerRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var backupRelativePath = NormalizeRelativePath(entry.OriginalBackupRelativePath);
        var backupPath = IsRandomizerBackupRelativePath(backupRelativePath)
            ? ResolveOutputPath(paths, backupRelativePath)
            : null;
        if (backupPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Randomizer could not restore the pre-existing output because its backup path is invalid.",
                file: ownerRelativePath,
                expected: "Randomizer backup contained by Output Root"));
            return null;
        }

        var backupPreimage = CaptureRestoreFilePreimage(backupPath);
        if (backupPreimage.Kind != RestoreFileKind.File)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Randomizer could not restore the pre-existing output because its backup is missing.",
                file: ownerRelativePath,
                expected: "Readable Randomizer backup under Output Root"));
            return null;
        }

        if (string.IsNullOrWhiteSpace(entry.OriginalSha256)
            || !string.Equals(backupPreimage.Sha256, entry.OriginalSha256, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Randomizer could not restore the pre-existing output because its backup failed verification.",
                file: ownerRelativePath,
                expected: "Backup matching the recorded pre-Randomizer hash"));
            return null;
        }

        return new PreparedRandomizerBackup(backupRelativePath, backupPath, backupPreimage);
    }

    private static void TryPrepareConsumedBackup(
        ProjectPaths paths,
        RandomizerRestoreEntry entry,
        IDictionary<string, PreparedRandomizerBackup> consumedBackups)
    {
        var backupRelativePath = NormalizeRelativePath(entry.OriginalBackupRelativePath);
        if (!IsRandomizerBackupRelativePath(backupRelativePath))
        {
            return;
        }

        var backupPath = ResolveOutputPath(paths, backupRelativePath);
        if (backupPath is null)
        {
            return;
        }

        var backupPreimage = CaptureRestoreFilePreimage(backupPath);
        if (backupPreimage.Kind == RestoreFileKind.File)
        {
            consumedBackups[backupRelativePath] = new PreparedRandomizerBackup(
                backupRelativePath,
                backupPath,
                backupPreimage);
        }
    }

    private static RestoreFilePreimage CaptureRestoreFilePreimage(string path)
    {
        if (File.Exists(path))
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return new RestoreFilePreimage(
                RestoreFileKind.File,
                stream.Length,
                Convert.ToHexString(SHA256.HashData(stream)));
        }

        return Directory.Exists(path)
            ? new RestoreFilePreimage(RestoreFileKind.Directory, 0, null)
            : new RestoreFilePreimage(RestoreFileKind.Missing, 0, null);
    }

    private static void EnsureRestoreMutationTarget(
        ProjectPaths paths,
        string relativePath,
        string preparedPath,
        RestoreFilePreimage expected)
    {
        var resolvedPath = ResolveOutputPath(paths, relativePath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (resolvedPath is null
            || !string.Equals(
                Path.GetFullPath(resolvedPath),
                Path.GetFullPath(preparedPath),
                pathComparison))
        {
            throw new IOException("The tracked path changed after Randomizer restore validation.");
        }

        var actual = CaptureRestoreFilePreimage(resolvedPath);
        if (actual != expected)
        {
            throw new IOException("The tracked file changed after Randomizer restore validation.");
        }
    }

    private static ApplyResult RollbackFailedRestore(
        SwShOutputRollbackScope rollbackScope,
        ICollection<ProjectFileReference> restoredFiles,
        List<ValidationDiagnostic> diagnostics)
    {
        var rollbackFailures = rollbackScope.Rollback();
        restoredFiles.Clear();
        if (rollbackFailures.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Randomizer restore failed and all output, backup, and manifest changes were rolled back."));
        }
        else
        {
            foreach (var failure in rollbackFailures)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Randomizer restore rollback failed: {failure.Message}",
                    file: string.IsNullOrWhiteSpace(failure.RelativePath) ? null : failure.RelativePath,
                    expected: "Tracked outputs, backups, and restore manifest restored to their exact pre-restore state"));
            }
        }

        return CreateApplyResult(diagnostics);
    }

    internal static IReadOnlyList<int> CreateRaidRewardItemPool(IEnumerable<SwShItemRecord> items, bool royalCandyInstalled)
    {
        return CreateItemPool(items, royalCandyInstalled)
            .Select(item => item.ItemId)
            .ToArray();
    }

    private void AddPokemonPlan(
        OpenedProject project,
        SwShPokemonWorkflow workflow,
        IReadOnlyList<PokemonCandidate> pokemonTargets,
        IReadOnlyList<MoveCandidate> movePool,
        IReadOnlyList<ItemCandidate> itemPool,
        string generationKey,
        SwShRandomizerOptions options,
        ICollection<RandomizerDomainPlan> plans,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!ShouldRandomizePokemon(options))
        {
            return;
        }

        var edits = new List<PendingEdit>();
        var pokemon = workflow.Pokemon
            .Where(IsEditablePokemon)
            .OrderBy(record => record.PersonalId)
            .ToArray();

        if (options.RandomizePokemonStats)
        {
            AddStatEdits(pokemon, options, generationKey, edits, diagnostics);
        }

        if (options.RandomizePokemonTypes)
        {
            AddTypeEdits(pokemon, options, generationKey, edits, diagnostics);
        }

        if (options.RandomizePokemonAbilities)
        {
            AddAbilityEdits(pokemon, generationKey, options, edits, diagnostics);
        }

        if (options.RandomizePokemonHeldItems)
        {
            AddHeldItemEdits(pokemon, itemPool, generationKey, edits);
        }

        if (options.RandomizePokemonCatchRates)
        {
            AddCatchRateEdits(pokemon, generationKey, edits);
        }

        if (options.RandomizePokemonLearnsets)
        {
            if (SwShPokemonWorkflowService.ResolveLearnsetDataSource(project) is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pokemon learnset randomization requires learnset data.",
                    file: SwShPokemonWorkflowService.LearnsetDataPath,
                    field: "learnsets",
                    expected: "Readable learnset data"));
            }
            else
            {
                AddLearnsetEdits(pokemon, movePool, generationKey, options, edits, diagnostics);
            }
        }

        if (options.RandomizePokemonCompatibility)
        {
            AddCompatibilityEdits(pokemon, generationKey, options, edits);
        }

        if (options.RandomizePokemonEvolutions)
        {
            AddEvolutionEdits(project, pokemon, pokemonTargets, generationKey, edits);
        }

        if (edits.Count == 0)
        {
            return;
        }

        plans.Add(new RandomizerDomainPlan(
            "Pokemon",
            CreateSession(edits),
            pokemonEditSessionService.CreateChangePlan,
            pokemonEditSessionService.ApplyChangePlan));
    }

    private void AddWildEncounterPlan(
        OpenedProject project,
        IReadOnlyList<PokemonCandidate> pokemonTargets,
        string generationKey,
        SwShRandomizerOptions options,
        ICollection<RandomizerDomainPlan> plans,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!options.RandomizeWildEncounters)
        {
            return;
        }

        var workflow = encountersWorkflowService.Load(project);
        AddWorkflowErrors(workflow.Diagnostics, diagnostics);
        var rng = DeterministicRandom.Create(generationKey, "wildEncounters");
        var edits = new List<PendingEdit>();

        foreach (var group in workflow.Tables
            .Where(table => table.Slots.Count > 0)
            .GroupBy(CreateWildEncounterMirrorKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var tables = group
                .OrderBy(table => table.GameVersion, StringComparer.Ordinal)
                .ThenBy(table => table.Location, StringComparer.Ordinal)
                .ThenBy(table => table.Area, StringComparer.Ordinal)
                .ThenBy(table => table.TableId, StringComparer.Ordinal)
                .ToArray();
            var randomizedSlotCount = Math.Min(10, tables.Max(table => table.Slots.Count));
            if (randomizedSlotCount == 0)
            {
                continue;
            }

            var targets = Enumerable.Range(0, randomizedSlotCount)
                .Select(_ => rng.Pick(pokemonTargets))
                .ToArray();
            var weightsByCount = new Dictionary<int, IReadOnlyList<int>>();

            foreach (var table in tables)
            {
                var slots = table.Slots
                    .OrderBy(slot => slot.Slot)
                    .ToArray();
                var activeSlots = slots.Take(10).ToArray();
                if (activeSlots.Length == 0)
                {
                    continue;
                }

                if (!weightsByCount.TryGetValue(activeSlots.Length, out var weights))
                {
                    weights = CreateStrictDescendingSlotWeights(activeSlots.Length, rng);
                    weightsByCount[activeSlots.Length] = weights;
                }

                for (var slotIndex = 0; slotIndex < activeSlots.Length; slotIndex++)
                {
                    var slot = activeSlots[slotIndex];
                    if (IsProtectedBoxLegendarySpecies(slot.SpeciesId))
                    {
                        continue;
                    }

                    var target = targets[slotIndex];
                    var recordId = SwShEncountersWorkflowService.CreateSlotRecordId(table.TableId, slot.Slot);
                    var source = Source(table.Provenance);

                    edits.Add(CreateEdit(
                        EncountersEditDomain,
                        $"Randomize wild encounter {table.Location} {table.Area} {table.EncounterType} slot {slot.Slot} species",
                        recordId,
                        SwShEncountersWorkflowService.SpeciesIdField,
                        target.SpeciesId,
                        source));
                    edits.Add(CreateEdit(
                        EncountersEditDomain,
                        $"Randomize wild encounter {table.Location} {table.Area} {table.EncounterType} slot {slot.Slot} form",
                        recordId,
                        SwShEncountersWorkflowService.FormField,
                        target.Form,
                        source));
                    edits.Add(CreateEdit(
                        EncountersEditDomain,
                        $"Randomize wild encounter {table.Location} {table.Area} {table.EncounterType} slot {slot.Slot} probability",
                        recordId,
                        SwShEncountersWorkflowService.ProbabilityField,
                        weights[slotIndex],
                        source));
                }

                foreach (var extraSlot in slots.Skip(10).Where(slot => slot.Weight != 0 && !IsProtectedBoxLegendarySpecies(slot.SpeciesId)))
                {
                    edits.Add(CreateEdit(
                        EncountersEditDomain,
                        $"Disable extra wild encounter {table.Location} {table.Area} {table.EncounterType} slot {extraSlot.Slot} probability",
                        SwShEncountersWorkflowService.CreateSlotRecordId(table.TableId, extraSlot.Slot),
                        SwShEncountersWorkflowService.ProbabilityField,
                        0,
                        Source(table.Provenance)));
                }
            }
        }

        if (edits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Wild encounter randomization did not find any editable encounter tables.",
                field: "wildEncounters"));
            return;
        }

        plans.Add(new RandomizerDomainPlan(
            "Wild Encounters",
            CreateSession(edits),
            encountersEditSessionService.CreateChangePlan,
            encountersEditSessionService.ApplyChangePlan));
    }

    private void AddStaticPlan(
        OpenedProject project,
        IReadOnlyList<PokemonCandidate> pokemonTargets,
        string generationKey,
        SwShRandomizerOptions options,
        ICollection<RandomizerDomainPlan> plans,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!options.RandomizeStaticEncounters)
        {
            return;
        }

        var workflow = staticEncountersWorkflowService.Load(project);
        AddWorkflowErrors(workflow.Diagnostics, diagnostics);
        var rng = DeterministicRandom.Create(generationKey, "staticEncounters");
        var edits = new List<PendingEdit>();
        var personalSource = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);

        foreach (var encounter in workflow.Encounters.OrderBy(encounter => encounter.EncounterIndex))
        {
            if (encounter.SpeciesId <= 0 || IsProtectedBoxLegendarySpecies(encounter.SpeciesId))
            {
                continue;
            }

            var target = rng.Pick(pokemonTargets);
            var recordId = SwShStaticEncountersWorkflowService.CreateEncounterRecordId(
                encounter.EncounterIndex,
                encounter.EncounterKey);
            var source = Source(encounter.Provenance);
            IReadOnlyList<ProjectFileReference> sources = personalSource is null
                ? [source]
                :
                [
                    source,
                    new ProjectFileReference(
                        personalSource.GraphEntry.LayeredFile is not null
                            ? ProjectFileLayer.Layered
                            : ProjectFileLayer.Base,
                        personalSource.GraphEntry.RelativePath),
                ];
            edits.Add(CreateEdit(
                SwShStaticEncountersWorkflowService.StaticEncountersEditDomain,
                $"Randomize static encounter {encounter.Label} species",
                recordId,
                SwShStaticEncountersWorkflowService.SpeciesField,
                target.SpeciesId,
                sources));
            edits.Add(CreateEdit(
                SwShStaticEncountersWorkflowService.StaticEncountersEditDomain,
                $"Randomize static encounter {encounter.Label} form",
                recordId,
                SwShStaticEncountersWorkflowService.FormField,
                target.Form,
                sources));
            if (encounter.CanGigantamax)
            {
                edits.Add(CreateEdit(
                    SwShStaticEncountersWorkflowService.StaticEncountersEditDomain,
                    $"Clear static encounter {encounter.Label} Gigantamax flag",
                    recordId,
                    SwShStaticEncountersWorkflowService.CanGigantamaxField,
                    0,
                    sources));
            }
        }

        if (edits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Static encounter randomization did not find any populated encounters.",
                field: "staticEncounters"));
            return;
        }

        plans.Add(new RandomizerDomainPlan(
            "Static Encounters",
            CreateSession(edits),
            staticEncountersEditSessionService.CreateChangePlan,
            staticEncountersEditSessionService.ApplyChangePlan));
    }

    private void AddGiftPlan(
        OpenedProject project,
        IReadOnlyList<PokemonCandidate> pokemonTargets,
        string generationKey,
        SwShRandomizerOptions options,
        ICollection<RandomizerDomainPlan> plans,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!options.RandomizeGiftEncounters)
        {
            return;
        }

        var workflow = giftPokemonWorkflowService.Load(project);
        AddWorkflowErrors(workflow.Diagnostics, diagnostics);
        var rng = DeterministicRandom.Create(generationKey, "giftEncounters");
        var edits = new List<PendingEdit>();

        foreach (var gift in workflow.Gifts.OrderBy(gift => gift.GiftIndex))
        {
            if (gift.SpeciesId <= 0 || IsProtectedBoxLegendarySpecies(gift.SpeciesId))
            {
                continue;
            }

            var target = rng.Pick(pokemonTargets);
            var recordId = SwShGiftPokemonWorkflowService.CreateGiftRecordId(gift.GiftIndex);
            var source = Source(gift.Provenance);
            edits.Add(CreateEdit(
                SwShGiftPokemonWorkflowService.GiftPokemonEditDomain,
                $"Randomize gift Pokemon {gift.Label} species",
                recordId,
                SwShGiftPokemonWorkflowService.SpeciesField,
                target.SpeciesId,
                source));
            edits.Add(CreateEdit(
                SwShGiftPokemonWorkflowService.GiftPokemonEditDomain,
                $"Randomize gift Pokemon {gift.Label} form",
                recordId,
                SwShGiftPokemonWorkflowService.FormField,
                target.Form,
                source));
            if (gift.CanGigantamax)
            {
                edits.Add(CreateEdit(
                    SwShGiftPokemonWorkflowService.GiftPokemonEditDomain,
                    $"Clear gift Pokemon {gift.Label} Gigantamax flag",
                    recordId,
                    SwShGiftPokemonWorkflowService.CanGigantamaxField,
                    0,
                    source));
            }
        }

        if (edits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Gift encounter randomization did not find any populated gifts.",
                field: "giftEncounters"));
            return;
        }

        plans.Add(new RandomizerDomainPlan(
            "Gift Encounters",
            CreateSession(edits),
            giftPokemonEditSessionService.CreateChangePlan,
            giftPokemonEditSessionService.ApplyChangePlan));
    }

    private void AddRaidRewardPlan(
        OpenedProject project,
        IReadOnlyList<ItemCandidate> itemPool,
        string generationKey,
        SwShRandomizerOptions options,
        ICollection<RandomizerDomainPlan> plans,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (options.RandomizeRaidRewards)
        {
            AddRaidRewardPlan(
                project,
                itemPool,
                generationKey,
                SwShRaidRewardWorkflowKind.Drop,
                SwShRaidRewardsEditSessionService.RaidRewardsEditDomain,
                "raidRewards",
                "Raid Rewards",
                plans,
                diagnostics);
        }

        if (options.RandomizeRaidBonusRewards)
        {
            AddRaidRewardPlan(
                project,
                itemPool,
                generationKey,
                SwShRaidRewardWorkflowKind.Bonus,
                SwShRaidRewardsEditSessionService.RaidBonusRewardsEditDomain,
                "raidBonusRewards",
                "Raid Bonus Rewards",
                plans,
                diagnostics);
        }
    }

    private void AddRaidRewardPlan(
        OpenedProject project,
        IReadOnlyList<ItemCandidate> itemPool,
        string generationKey,
        SwShRaidRewardWorkflowKind kind,
        string editDomain,
        string module,
        string label,
        ICollection<RandomizerDomainPlan> plans,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var workflow = raidRewardsWorkflowService.Load(project, kind);
        AddWorkflowErrors(workflow.Diagnostics, diagnostics);
        var rng = DeterministicRandom.Create(generationKey, module);
        var edits = new List<PendingEdit>();
        var itemNamesSource = SwShRaidRewardsWorkflowService.ResolveItemNamesSourceForValidation(project);
        var itemValidationSource = itemNamesSource is null
            ? null
            : new ProjectFileReference(
                itemNamesSource.GraphEntry.LayeredFile is not null
                    ? ProjectFileLayer.Layered
                    : ProjectFileLayer.Base,
                itemNamesSource.GraphEntry.RelativePath);

        foreach (var table in workflow.Tables.OrderBy(table => table.TableId, StringComparer.Ordinal))
        {
            IReadOnlyList<ProjectFileReference> sources = itemValidationSource is null
                ? [Source(table.Provenance)]
                : [Source(table.Provenance), itemValidationSource];
            foreach (var reward in table.Rewards.OrderBy(reward => reward.Slot))
            {
                if (reward.ItemId <= 0)
                {
                    continue;
                }

                var item = rng.Pick(itemPool);
                edits.Add(CreateEdit(
                    editDomain,
                    $"Randomize {label} {table.DisplayName} slot {reward.Slot}",
                    SwShRaidRewardsWorkflowService.CreateRewardRecordId(table.TableId, reward.Slot),
                    SwShRaidRewardsWorkflowService.ItemIdField,
                    item.ItemId,
                    sources));
            }
        }

        if (edits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{label} randomization did not find any populated reward entries.",
                field: module));
            return;
        }

        plans.Add(new RandomizerDomainPlan(
            label,
            CreateSession(edits),
            raidRewardsEditSessionService.CreateChangePlan,
            raidRewardsEditSessionService.ApplyChangePlan));
    }

    private void AddTypeChartPlan(
        OpenedProject project,
        string generationKey,
        SwShRandomizerOptions options,
        ICollection<RandomizerDomainPlan> plans,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!options.RandomizeTypeChart)
        {
            return;
        }

        var workflow = typeChartWorkflowService.Load(project);
        var workflowErrors = workflow.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        foreach (var diagnostic in workflowErrors)
        {
            diagnostics.Add(diagnostic);
        }

        if (workflowErrors.Length > 0)
        {
            return;
        }

        var currentValues = workflow.Cells
            .OrderBy(cell => cell.AttackTypeIndex)
            .ThenBy(cell => cell.DefenseTypeIndex)
            .Select(cell => cell.Effectiveness)
            .ToArray();
        var randomizedValues = CreateRandomizedTypeChartValues(currentValues, generationKey, options);
        var staged = typeChartEditSessionService.StageChart(project.Paths, randomizedValues, session: null);
        foreach (var diagnostic in staged.Diagnostics.Where(diagnostic => diagnostic.Severity != DiagnosticSeverity.Info))
        {
            diagnostics.Add(diagnostic);
        }

        if (staged.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        plans.Add(new RandomizerDomainPlan(
            "Type Chart",
            staged.Session,
            typeChartEditSessionService.CreateChangePlan,
            typeChartEditSessionService.ApplyChangePlan));
    }

    internal static int[] CreateRandomizedTypeChartValues(
        IReadOnlyList<int> currentValues,
        string generationKey,
        SwShRandomizerOptions options)
    {
        SwShTypeChartMainPatcher.ValidateValues(currentValues);

        var rng = DeterministicRandom.Create(generationKey, "typeChart");
        var vanillaDistribution = SwShTypeChartWorkflowService
            .ToDisplayOrder(SwShTypeChartMainPatcher.VanillaChartValues)
            .Where(value => !options.TypeChartNoImmunities || value != 0)
            .ToArray();
        var nonImmuneDistribution = vanillaDistribution
            .Where(value => value != 0)
            .ToArray();
        var values = new int[SwShTypeChartMainPatcher.ChartLength];
        var immunityCountsByAttackType = new int[SwShTypeChartMainPatcher.TypeCount];

        for (var attackType = 0; attackType < SwShTypeChartMainPatcher.TypeCount; attackType++)
        {
            for (var defenseType = 0; defenseType < SwShTypeChartMainPatcher.TypeCount; defenseType++)
            {
                var value = rng.Pick(vanillaDistribution);
                if (value == 0
                    && options.TypeChartOneImmunityPerType
                    && immunityCountsByAttackType[attackType] > 0)
                {
                    value = rng.Pick(nonImmuneDistribution);
                }

                if (value == 0)
                {
                    immunityCountsByAttackType[attackType]++;
                }

                values[(attackType * SwShTypeChartMainPatcher.TypeCount) + defenseType] = value;
            }
        }

        if (values.SequenceEqual(currentValues))
        {
            values[0] = values[0] == 4 ? 2 : 4;
        }

        return values;
    }

    private static void AddStatEdits(
        IReadOnlyList<SwShPokemonRecord> pokemon,
        SwShRandomizerOptions options,
        string exportedSeed,
        ICollection<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var selectedFields = CreateSelectedStatFields(options).ToArray();
        if (selectedFields.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Pokemon stat randomization is enabled but no stat fields are selected.",
                field: "stats"));
            return;
        }

        var rng = DeterministicRandom.Create(exportedSeed, "pokemon.stats");
        foreach (var record in pokemon)
        {
            var values = selectedFields
                .Select(field => GetStatValue(record, field))
                .ToArray();
            var randomizedValues = options.ShufflePokemonStats
                ? values.ToArray()
                : DistributeStatTotal(values.Sum(), values.Length, rng);
            if (options.ShufflePokemonStats)
            {
                rng.Shuffle(randomizedValues);
            }

            for (var i = 0; i < selectedFields.Length; i++)
            {
                edits.Add(CreatePokemonEdit(
                    record,
                    selectedFields[i],
                    randomizedValues[i],
                    $"Randomize {record.Name} stat {selectedFields[i]}"));
            }
        }
    }

    private static void AddTypeEdits(
        IReadOnlyList<SwShPokemonRecord> pokemon,
        SwShRandomizerOptions options,
        string exportedSeed,
        ICollection<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!options.TypePrimary && !options.TypeSecondary)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Pokemon type randomization is enabled but no type fields are selected.",
                field: "types"));
            return;
        }

        var typeOptions = SwShPokemonWorkflowService.GetEditableField(SwShPokemonWorkflowService.Type1Field)?.Options
            .Select(option => option.Value)
            .Where(value => value >= 0)
            .Distinct()
            .Order()
            .ToArray() ?? Array.Empty<int>();
        if (typeOptions.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon type randomization could not resolve valid type IDs.",
                field: "types",
                expected: "Pokemon type editable field options"));
            return;
        }

        var rng = DeterministicRandom.Create(exportedSeed, "pokemon.types");
        foreach (var record in pokemon)
        {
            int type1;
            int type2;
            if (options.TypePrimary && options.TypeSecondary)
            {
                (type1, type2) = CreateRandomizedTypePair(
                    record.Personal.Type1,
                    record.Personal.Type2,
                    typeOptions,
                    rng);
            }
            else
            {
                type1 = options.TypePrimary ? rng.Pick(typeOptions) : record.Personal.Type1;
                type2 = options.TypeSecondary ? rng.Pick(typeOptions) : record.Personal.Type2;
                if (!options.AllowSameType && typeOptions.Length > 1 && type2 == type1)
                {
                    type2 = typeOptions.First(value => value != type1);
                }
            }

            if (options.TypePrimary)
            {
                edits.Add(CreatePokemonEdit(
                    record,
                    SwShPokemonWorkflowService.Type1Field,
                    type1,
                    $"Randomize {record.Name} primary type"));
            }

            if (options.TypeSecondary)
            {
                edits.Add(CreatePokemonEdit(
                    record,
                    SwShPokemonWorkflowService.Type2Field,
                    type2,
                    $"Randomize {record.Name} secondary type"));
            }
        }
    }

    internal static (int Type1, int Type2) CreateRandomizedTypePair(
        int originalType1,
        int originalType2,
        IReadOnlyList<int> typeOptions,
        DeterministicRandom rng)
    {
        ArgumentNullException.ThrowIfNull(typeOptions);
        ArgumentNullException.ThrowIfNull(rng);
        if (typeOptions.Count == 0)
        {
            throw new ArgumentException("Type options cannot be empty.", nameof(typeOptions));
        }

        var wasSingleType = originalType1 == originalType2;
        var useSingleType = wasSingleType;
        if (typeOptions.Count > 1 && rng.NextInt(100) < PokemonTypeShapeChangeChancePercent)
        {
            useSingleType = !wasSingleType;
        }

        var type1 = rng.Pick(typeOptions);
        if (useSingleType || typeOptions.Count == 1)
        {
            return (type1, type1);
        }

        return (type1, PickDifferentType(typeOptions, type1, rng));
    }

    private static void AddAbilityEdits(
        IReadOnlyList<SwShPokemonRecord> pokemon,
        string exportedSeed,
        SwShRandomizerOptions options,
        ICollection<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!options.Ability1 && !options.Ability2 && !options.HiddenAbility)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Pokemon ability randomization is enabled but no ability slots are selected.",
                field: "abilities"));
            return;
        }

        var abilityPool = pokemon
            .SelectMany(record => new[]
            {
                record.Abilities.Ability1,
                record.Abilities.Ability2,
                record.Abilities.HiddenAbility,
            })
            .Where(value => value > 0)
            .Distinct()
            .Order()
            .ToArray();
        if (abilityPool.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon ability randomization could not resolve valid ability IDs.",
                field: "abilities",
                expected: "Pokemon personal data with ability IDs"));
            return;
        }

        var rng = DeterministicRandom.Create(exportedSeed, "pokemon.abilities");
        foreach (var record in pokemon)
        {
            var available = abilityPool.ToList();
            if (options.Ability1)
            {
                var ability = PickAndCycle(rng, available, abilityPool);
                edits.Add(CreatePokemonEdit(record, SwShPokemonWorkflowService.Ability1Field, ability, $"Randomize {record.Name} ability 1"));
            }

            if (options.Ability2)
            {
                var ability = PickAndCycle(rng, available, abilityPool);
                edits.Add(CreatePokemonEdit(record, SwShPokemonWorkflowService.Ability2Field, ability, $"Randomize {record.Name} ability 2"));
            }

            if (options.HiddenAbility)
            {
                var ability = PickAndCycle(rng, available, abilityPool);
                edits.Add(CreatePokemonEdit(record, SwShPokemonWorkflowService.HiddenAbilityField, ability, $"Randomize {record.Name} hidden ability"));
            }
        }
    }

    private static void AddHeldItemEdits(
        IReadOnlyList<SwShPokemonRecord> pokemon,
        IReadOnlyList<ItemCandidate> itemPool,
        string exportedSeed,
        ICollection<PendingEdit> edits)
    {
        var rng = DeterministicRandom.Create(exportedSeed, "pokemon.heldItems");
        foreach (var record in pokemon)
        {
            AddHeldItemEdit(record, SwShPokemonWorkflowService.HeldItem1Field, itemPool, rng, edits);
            AddHeldItemEdit(record, SwShPokemonWorkflowService.HeldItem2Field, itemPool, rng, edits);
            AddHeldItemEdit(record, SwShPokemonWorkflowService.HeldItem3Field, itemPool, rng, edits);
        }
    }

    private static void AddCatchRateEdits(
        IReadOnlyList<SwShPokemonRecord> pokemon,
        string exportedSeed,
        ICollection<PendingEdit> edits)
    {
        var rng = DeterministicRandom.Create(exportedSeed, "pokemon.catchRates");
        foreach (var record in pokemon)
        {
            edits.Add(CreatePokemonEdit(
                record,
                SwShPokemonWorkflowService.CatchRateField,
                rng.NextInt(byte.MaxValue) + 1,
                $"Randomize {record.Name} catch rate"));
        }
    }

    private static void AddLearnsetEdits(
        IReadOnlyList<SwShPokemonRecord> pokemon,
        IReadOnlyList<MoveCandidate> movePool,
        string exportedSeed,
        SwShRandomizerOptions options,
        ICollection<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (movePool.Count == 0)
        {
            return;
        }

        var rng = DeterministicRandom.Create(exportedSeed, "pokemon.learnsets");
        foreach (var record in pokemon.Where(record => record.Learnset.Count > 0))
        {
            var levels = CreateLearnsetLevels(record.Learnset, options.LearnsetExpandTo25);
            var moves = GenerateLearnset(record, movePool, levels.Count, options, rng);
            if (moves.Count != levels.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pokemon learnset randomization could not create {levels.Count} distinct moves for {record.Name}.",
                    field: "learnsets",
                    expected: "Enough legal move candidates"));
                continue;
            }

            for (var slot = 0; slot < moves.Count; slot++)
            {
                edits.Add(new PendingEdit(
                    PokemonEditDomain,
                    $"Randomize {record.Name} learnset slot {slot}",
                    [Source(record.Provenance)],
                    record.PersonalId.ToString(CultureInfo.InvariantCulture),
                    string.Create(CultureInfo.InvariantCulture, $"learnset:upsert:{slot}"),
                    string.Create(CultureInfo.InvariantCulture, $"{moves[slot].MoveId}:{levels[slot]}")));
            }
        }
    }

    internal static IReadOnlyList<int> CreateExpandedLearnsetLevels(int count)
    {
        if (count <= 0)
        {
            return Array.Empty<int>();
        }

        if (count == 1)
        {
            return [1];
        }

        var levels = new int[count];
        for (var index = 0; index < levels.Length; index++)
        {
            levels[index] = 1 + (int)Math.Round(
                index * (ExpandedLearnsetMaxLevel - 1) / (double)(levels.Length - 1),
                MidpointRounding.AwayFromZero);
        }

        return levels;
    }

    private static IReadOnlyList<int> CreateLearnsetLevels(
        IReadOnlyList<SwShPokemonLearnsetMove> learnset,
        bool expandTo25)
    {
        var existingLevels = learnset
            .OrderBy(move => move.Slot)
            .Select(move => move.Level)
            .ToArray();
        if (!expandTo25 || existingLevels.Length >= ExpandedLearnsetMoveCount)
        {
            return existingLevels;
        }

        return CreateExpandedLearnsetLevels(ExpandedLearnsetMoveCount);
    }

    private static void AddCompatibilityEdits(
        IReadOnlyList<SwShPokemonRecord> pokemon,
        string exportedSeed,
        SwShRandomizerOptions options,
        ICollection<PendingEdit> edits)
    {
        var selectedGroupIds = new HashSet<string>(StringComparer.Ordinal)
        {
            { options.CompatibilityMachines ? SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId : string.Empty },
            { options.CompatibilityRecords ? SwShPokemonWorkflowService.TechnicalRecordCompatibilityGroupId : string.Empty },
        };

        if (options.CompatibilityTutors)
        {
            selectedGroupIds.Add(SwShPokemonWorkflowService.TypeTutorCompatibilityGroupId);
            selectedGroupIds.Add(SwShPokemonWorkflowService.ArmorTutorCompatibilityGroupId);
        }

        selectedGroupIds.Remove(string.Empty);
        var rng = DeterministicRandom.Create(exportedSeed, "pokemon.compatibility");
        foreach (var record in pokemon)
        {
            foreach (var group in record.Compatibility.Where(group => selectedGroupIds.Contains(group.GroupId)))
            {
                foreach (var entry in group.Entries)
                {
                    edits.Add(new PendingEdit(
                        PokemonEditDomain,
                        $"Randomize {record.Name} {group.Label} compatibility {entry.Label}",
                        [Source(record.Provenance)],
                        record.PersonalId.ToString(CultureInfo.InvariantCulture),
                        SwShPokemonWorkflowService.CreateCompatibilityFieldId(group.GroupId, entry.Slot),
                        rng.NextInt(100) < 45 ? "1" : "0"));
                }
            }
        }
    }

    private static void AddEvolutionEdits(
        OpenedProject project,
        IReadOnlyList<SwShPokemonRecord> pokemon,
        IReadOnlyList<PokemonCandidate> pokemonTargets,
        string exportedSeed,
        ICollection<PendingEdit> edits)
    {
        var rng = DeterministicRandom.Create(exportedSeed, "pokemon.evolutions");
        foreach (var record in pokemon.Where(record => record.Evolutions.Count > 0))
        {
            if (SwShPokemonWorkflowService.ResolveEvolutionDataSource(project, record.PersonalId) is null)
            {
                continue;
            }

            var forwardTargets = pokemonTargets
                .Where(candidate => candidate.EvolutionStage > record.EvolutionStage)
                .ToArray();
            var candidatePool = forwardTargets.Length > 0
                ? forwardTargets
                : pokemonTargets.Where(candidate => candidate.PersonalId != record.PersonalId).ToArray();
            if (candidatePool.Length == 0)
            {
                continue;
            }

            foreach (var evolution in record.Evolutions.OrderBy(evolution => evolution.Slot))
            {
                if (evolution.Method <= 0 || IsProtectedBoxLegendarySpecies(evolution.Species))
                {
                    continue;
                }

                var target = rng.Pick(candidatePool);
                edits.Add(new PendingEdit(
                    PokemonEditDomain,
                    $"Randomize {record.Name} evolution slot {evolution.Slot}",
                    [Source(record.Provenance)],
                    record.PersonalId.ToString(CultureInfo.InvariantCulture),
                    string.Create(CultureInfo.InvariantCulture, $"evolution:upsert:{evolution.Slot}"),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{evolution.Method}:{evolution.Argument}:{target.SpeciesId}:{target.Form}:{evolution.Level}")));
            }
        }
    }

    private static IReadOnlyList<MoveCandidate> GenerateLearnset(
        SwShPokemonRecord pokemon,
        IReadOnlyList<MoveCandidate> movePool,
        int count,
        SwShRandomizerOptions options,
        DeterministicRandom rng)
    {
        if (count == 0)
        {
            return Array.Empty<MoveCandidate>();
        }

        var selected = new List<MoveCandidate>(count);
        var available = movePool.ToList();
        if (options.LearnsetStabFirst)
        {
            var stabMoves = available
                .Where(move => move.Type == pokemon.Personal.Type1 || move.Type == pokemon.Personal.Type2)
                .ToArray();
            var firstMove = stabMoves.Length > 0
                ? rng.Pick(stabMoves)
                : PickPreferredFirstMove(available, rng);
            selected.Add(firstMove);
            available.RemoveAll(move => move.MoveId == firstMove.MoveId);
        }

        while (selected.Count < count && available.Count > 0)
        {
            var move = rng.Pick(available);
            selected.Add(move);
            available.RemoveAll(candidate => candidate.MoveId == move.MoveId);
        }

        if (selected.Count == count
            && options.LearnsetRequireDamagingMove
            && selected.All(move => !move.IsDamaging))
        {
            var damaging = movePool
                .Where(move => move.IsDamaging && selected.All(selectedMove => selectedMove.MoveId != move.MoveId))
                .ToArray();
            if (damaging.Length > 0)
            {
                selected[^1] = rng.Pick(damaging);
            }
        }

        return selected;
    }

    private static MoveCandidate PickPreferredFirstMove(IReadOnlyList<MoveCandidate> available, DeterministicRandom rng)
    {
        var preferred = available
            .Where(move => FirstMoveIds.Contains(move.MoveId.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal))
            .ToArray();

        return preferred.Length > 0
            ? rng.Pick(preferred)
            : rng.Pick(available);
    }

    private static IReadOnlyList<PokemonCandidate> CreatePokemonTargets(SwShPokemonWorkflow workflow)
    {
        var strictTargets = workflow.Pokemon
            .Where(IsLegalPokemonTarget)
            .ToArray();
        var targets = strictTargets.Length > 0
            ? strictTargets
            : workflow.Pokemon.Where(IsPresentPokemonTarget);

        return targets
            .Select(record => new PokemonCandidate(
                record.PersonalId,
                record.SpeciesId,
                Math.Clamp(record.Form, byte.MinValue, byte.MaxValue),
                record.Personal.Type1,
                record.Personal.Type2,
                record.EvolutionStage,
                Source(record.Provenance)))
            .GroupBy(candidate => $"{candidate.SpeciesId}:{candidate.Form}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.SpeciesId)
            .ThenBy(candidate => candidate.Form)
            .ToArray();
    }

    private static IEnumerable<MoveCandidate> CreateMovePool(SwShMovesWorkflow workflow, bool banFixedDamageMoves)
    {
        var fixedDamageMoves = banFixedDamageMoves
            ? new HashSet<int>([49, 82])
            : new HashSet<int>();

        return workflow.Moves
            .Where(move => move.MoveId > 0
                && move.CanUseMove
                && move.PP > 0
                && !fixedDamageMoves.Contains(move.MoveId)
                && !IsMaxMove(move))
            .Select(move => new MoveCandidate(
                move.MoveId,
                move.Type,
                move.Category != 0,
                Source(move.Provenance)))
            .OrderBy(move => move.MoveId);
    }

    private static IEnumerable<ItemCandidate> CreateItemPool(IEnumerable<SwShItemRecord> items, bool royalCandyInstalled)
    {
        var materializedItems = items as IReadOnlyCollection<SwShItemRecord> ?? items.ToArray();
        var strictPool = materializedItems
            .Where(item => IsSafeItemCandidate(item, royalCandyInstalled, strict: true))
            .Select(item => new ItemCandidate(item.ItemId, Source(item.Provenance)))
            .OrderBy(item => item.ItemId)
            .ToArray();
        if (strictPool.Length > 0)
        {
            return strictPool;
        }

        return materializedItems
            .Where(item => IsSafeItemCandidate(item, royalCandyInstalled, strict: false))
            .Select(item => new ItemCandidate(item.ItemId, Source(item.Provenance)))
            .OrderBy(item => item.ItemId)
            .ToArray();
    }

    private static bool IsSafeItemCandidate(SwShItemRecord item, bool royalCandyInstalled, bool strict)
    {
        if (item.ItemId <= 0
            || item.ItemId > SwShRaidRewardsWorkflowService.MaximumItemId
            || (royalCandyInstalled && item.ItemId == RoyalCandyItemId)
            || string.IsNullOrWhiteSpace(item.Name))
        {
            return false;
        }

        if (!strict)
        {
            return true;
        }

        return !ContainsIgnoreCase(item.Category, "key")
            && !ContainsIgnoreCase(item.Category, "system")
            && item.Metadata.Pouch is not 7 and not 8;
    }

    private bool IsRoyalCandyInstalled(OpenedProject project)
    {
        var workflow = royalCandyWorkflowService.Load(project);
        return workflow.Workflows.Any(workflow =>
            workflow.ItemId == RoyalCandyItemId
            && string.Equals(workflow.Status, "installed", StringComparison.Ordinal));
    }

    private static SwShRandomizerConfig PrepareConfig(SwShRandomizerConfig config)
    {
        var normalized = SwShRandomizerSeedCodec.Normalize(config);
        return normalized with
        {
            UserSeed = string.IsNullOrEmpty(normalized.UserSeed)
                ? GenerateToken(length: 12)
                : normalized.UserSeed,
            RollSeed = string.IsNullOrWhiteSpace(normalized.RollSeed)
                ? GenerateToken(length: 16)
                : normalized.RollSeed,
        };
    }

    private static string GenerateToken(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return new string(chars);
    }

    private static EditSession CreateSession(IEnumerable<PendingEdit> edits)
    {
        return EditSession.Start() with
        {
            PendingEdits = edits.ToArray(),
        };
    }

    private static PendingEdit CreatePokemonEdit(
        SwShPokemonRecord pokemon,
        string field,
        int value,
        string summary)
    {
        return CreateEdit(
            PokemonEditDomain,
            summary,
            pokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            field,
            value,
            Source(pokemon.Provenance));
    }

    private static PendingEdit CreateEdit(
        string domain,
        string summary,
        string recordId,
        string field,
        int value,
        ProjectFileReference source)
    {
        return CreateEdit(domain, summary, recordId, field, value, [source]);
    }

    private static PendingEdit CreateEdit(
        string domain,
        string summary,
        string recordId,
        string field,
        int value,
        IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            domain,
            summary,
            sources,
            recordId,
            field,
            value.ToString(CultureInfo.InvariantCulture));
    }

    private static ProjectFileReference Source(SwShPokemonProvenance provenance)
    {
        return new ProjectFileReference(provenance.SourceLayer, provenance.SourceFile);
    }

    private static ProjectFileReference Source(SwShMoveProvenance provenance)
    {
        return new ProjectFileReference(provenance.SourceLayer, provenance.SourceFile);
    }

    private static ProjectFileReference Source(SwShItemProvenance provenance)
    {
        return new ProjectFileReference(provenance.SourceLayer, provenance.SourceFile);
    }

    private static ProjectFileReference Source(SwShStaticEncounterProvenance provenance)
    {
        return new ProjectFileReference(provenance.SourceLayer, provenance.SourceFile);
    }

    private static ProjectFileReference Source(SwShGiftPokemonProvenance provenance)
    {
        return new ProjectFileReference(provenance.SourceLayer, provenance.SourceFile);
    }

    private static ProjectFileReference Source(SwShEncounterProvenance provenance)
    {
        return new ProjectFileReference(provenance.SourceLayer, provenance.SourceFile);
    }

    private static ProjectFileReference Source(SwShRaidRewardProvenance provenance)
    {
        return new ProjectFileReference(provenance.SourceLayer, provenance.SourceFile);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            file,
            RandomizerDomain,
            field,
            expected);
    }

    private static ApplyResult CreateApplyResult(IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        return new ApplyResult(
            applyId,
            appliedAt,
            Array.Empty<ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, Array.Empty<PlannedFileWrite>()),
            CollapseDiagnostics(diagnostics));
    }

    private static IReadOnlyList<ValidationDiagnostic> CollapseDiagnostics(
        IEnumerable<ValidationDiagnostic> diagnostics)
    {
        var collapsed = new List<CollapsedDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            var existing = collapsed.FirstOrDefault(item =>
                item.Diagnostic.Severity == diagnostic.Severity
                && string.Equals(item.Diagnostic.Message, diagnostic.Message, StringComparison.Ordinal)
                && string.Equals(item.Diagnostic.File, diagnostic.File, StringComparison.Ordinal)
                && string.Equals(item.Diagnostic.Domain, diagnostic.Domain, StringComparison.Ordinal)
                && string.Equals(item.Diagnostic.Field, diagnostic.Field, StringComparison.Ordinal)
                && string.Equals(item.Diagnostic.Expected, diagnostic.Expected, StringComparison.Ordinal));
            if (existing is null)
            {
                collapsed.Add(new CollapsedDiagnostic(diagnostic, count: 1));
                continue;
            }

            existing.Count++;
        }

        return collapsed
            .Select(item => item.Count == 1
                ? item.Diagnostic
                : item.Diagnostic with
                {
                    Message = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{item.Diagnostic.Message} ({item.Count} times)"),
                })
            .ToArray();
    }

    private static RandomizerRestoreCapture CaptureRandomizerRestoreState(
        ProjectPaths paths,
        IEnumerable<PlannedFileWrite> plannedWrites,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var entries = new Dictionary<string, RandomizerRestoreEntry>(StringComparer.OrdinalIgnoreCase);
        var newRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var createdBackupRelativePaths = new List<string>();
        var manifestPath = ResolveOutputPath(paths, RandomizerManifestRelativePath);
        if (manifestPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Randomizer restore state could not be captured because Output Root is not writable.",
                file: RandomizerManifestRelativePath,
                expected: "Writable path under Output Root"));
            return new RandomizerRestoreCapture(entries, newRelativePaths, createdBackupRelativePaths);
        }

        if (File.Exists(manifestPath))
        {
            try
            {
                var existingManifest = JsonSerializer.Deserialize<RandomizerRestoreManifest>(
                    File.ReadAllText(manifestPath),
                    JsonOptions);
                if (existingManifest is null || existingManifest.Version is not (1 or 2))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Randomizer cannot apply while the existing restore manifest version is unsupported.",
                        file: RandomizerManifestRelativePath,
                        expected: "KM Editor Randomizer restore manifest version 1 or 2"));
                    return new RandomizerRestoreCapture(entries, newRelativePaths, createdBackupRelativePaths);
                }

                foreach (var entry in CreateRestoreEntries(existingManifest))
                {
                    var relativePath = NormalizeRelativePath(entry.RelativePath);
                    if (IsLayeredFsRelativePath(relativePath))
                    {
                        entries[relativePath] = entry with { RelativePath = relativePath };
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Randomizer cannot safely apply because the existing restore manifest could not be read: {ex.Message}",
                    file: RandomizerManifestRelativePath,
                    expected: "Readable Randomizer restore manifest"));
                return new RandomizerRestoreCapture(entries, newRelativePaths, createdBackupRelativePaths);
            }
        }

        var plannedPaths = plannedWrites
            .Select(write => NormalizeRelativePath(write.TargetRelativePath))
            .Where(IsLayeredFsRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var relativePath in plannedPaths)
        {
            if (entries.TryGetValue(relativePath, out var existingEntry))
            {
                try
                {
                    var currentPath = ResolveOutputPath(paths, relativePath);
                    var stillMatchesRecordedRandomizerOutput = currentPath is not null
                        && File.Exists(currentPath)
                        && !string.IsNullOrWhiteSpace(existingEntry.RandomizedSha256)
                        && string.Equals(
                            ComputeFileSha256(currentPath),
                            existingEntry.RandomizedSha256,
                            StringComparison.OrdinalIgnoreCase);
                    if (!stillMatchesRecordedRandomizerOutput)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "Randomizer apply is blocked because a tracked output changed after an earlier apply. Restore or move the later-edited file before applying Randomizer again.",
                            file: relativePath,
                            expected: "Current file matching the recorded Randomizer output hash"));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Randomizer could not verify existing restore ownership: {ex.Message}",
                        file: relativePath,
                        expected: "Readable tracked Randomizer output"));
                }

                continue;
            }

            var targetPath = ResolveOutputPath(paths, relativePath);
            if (targetPath is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Randomizer restore state could not resolve a planned output under Output Root.",
                    file: relativePath,
                    expected: "Path contained by Output Root"));
                continue;
            }

            try
            {
                RandomizerRestoreEntry entry;
                if (File.Exists(targetPath))
                {
                    var backupRelativePath = CreateRandomizerBackupRelativePath(relativePath);
                    var backupPath = ResolveOutputPath(paths, backupRelativePath);
                    if (backupPath is null)
                    {
                        throw new IOException("The Randomizer backup path is not a physical path inside Output Root.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(targetPath, backupPath, overwrite: true);

                    var originalSha256 = ComputeFileSha256(targetPath);
                    if (!string.Equals(originalSha256, ComputeFileSha256(backupPath), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("The pre-Randomizer backup did not match its source file.");
                    }

                    entry = new RandomizerRestoreEntry(
                        relativePath,
                        OriginalExisted: true,
                        OriginalSha256: originalSha256,
                        OriginalBackupRelativePath: backupRelativePath,
                        RandomizedSha256: null);
                    createdBackupRelativePaths.Add(backupRelativePath);
                }
                else
                {
                    entry = new RandomizerRestoreEntry(
                        relativePath,
                        OriginalExisted: false,
                        OriginalSha256: null,
                        OriginalBackupRelativePath: null,
                        RandomizedSha256: null);
                }

                entries[relativePath] = entry;
                newRelativePaths.Add(relativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Randomizer could not capture the pre-apply output for safe restoration: {ex.Message}",
                    file: relativePath,
                    expected: "Readable output and writable Randomizer backup directory"));
            }
        }

        return new RandomizerRestoreCapture(entries, newRelativePaths, createdBackupRelativePaths);
    }

    private static void RecordRandomizerManifest(
        ProjectPaths paths,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        RandomizerRestoreCapture restoreCapture,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var trackedPaths = writtenFiles
            .Where(file => file.Layer == ProjectFileLayer.Generated)
            .Select(file => NormalizeRelativePath(file.RelativePath))
            .Where(IsLayeredFsRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var trackedPathSet = trackedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = restoreCapture.Entries
            .Where(item => !restoreCapture.NewRelativePaths.Contains(item.Key) || trackedPathSet.Contains(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var backupRelativePath in restoreCapture.CreatedBackupRelativePaths)
        {
            var owner = restoreCapture.Entries.FirstOrDefault(item =>
                string.Equals(item.Value.OriginalBackupRelativePath, backupRelativePath, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(owner.Key) && trackedPathSet.Contains(owner.Key))
            {
                continue;
            }

            TryDeleteRandomizerBackup(
                paths,
                new RandomizerRestoreEntry(string.Empty, null, null, backupRelativePath, null),
                diagnostics);
        }

        if (trackedPaths.Length == 0)
        {
            return;
        }

        var manifestPath = ResolveOutputPath(paths, RandomizerManifestRelativePath);
        if (manifestPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Randomizer restore manifest could not be written because Output Root is not writable.",
                file: RandomizerManifestRelativePath,
                expected: "Writable path under Output Root"));
            return;
        }

        foreach (var relativePath in trackedPaths)
        {
            try
            {
                var targetPath = ResolveOutputPath(paths, relativePath);
                if (targetPath is null || !File.Exists(targetPath))
                {
                    throw new IOException("The reported Randomizer output file does not exist.");
                }

                entries.TryGetValue(relativePath, out var entry);
                entry ??= new RandomizerRestoreEntry(relativePath, null, null, null, null);
                entries[relativePath] = entry with
                {
                    RelativePath = relativePath,
                    RandomizedSha256 = ComputeFileSha256(targetPath),
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Randomizer output ownership could not be recorded: {ex.Message}",
                    file: relativePath,
                    expected: "Readable generated Randomizer output"));
            }
        }

        try
        {
            WriteRandomizerManifest(manifestPath, entries.Values);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Randomizer restore manifest could not be written: {ex.Message}",
                file: RandomizerManifestRelativePath,
                expected: "Writable Randomizer restore manifest"));
        }
    }

    private static void CleanupCapturedRandomizerBackups(
        ProjectPaths paths,
        RandomizerRestoreCapture restoreCapture,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var backupRelativePath in restoreCapture.CreatedBackupRelativePaths)
        {
            TryDeleteRandomizerBackup(
                paths,
                new RandomizerRestoreEntry(string.Empty, null, null, backupRelativePath, null),
                diagnostics);
        }
    }

    private static IReadOnlyList<RandomizerRestoreEntry> CreateRestoreEntries(RandomizerRestoreManifest manifest)
    {
        if (manifest.Version == 2)
        {
            return (manifest.Entries ?? Array.Empty<RandomizerRestoreEntry>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry.RelativePath))
                .DistinctBy(entry => NormalizeRelativePath(entry.RelativePath), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return (manifest.WrittenRelativePaths ?? Array.Empty<string>())
            .Select(NormalizeRelativePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new RandomizerRestoreEntry(path, null, null, null, null))
            .ToArray();
    }

    private static void WriteRandomizerManifest(
        string manifestPath,
        IEnumerable<RandomizerRestoreEntry> entries)
    {
        var normalizedEntries = entries
            .Select(entry => entry with { RelativePath = NormalizeRelativePath(entry.RelativePath) })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.RelativePath))
            .DistinctBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var manifest = new RandomizerRestoreManifest(
            Version: 2,
            UpdatedAt: DateTimeOffset.UtcNow,
            WrittenRelativePaths: normalizedEntries.Select(entry => entry.RelativePath).ToArray(),
            Entries: normalizedEntries);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static string CreateRandomizerBackupRelativePath(string relativePath)
    {
        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath.ToUpperInvariant())));
        return $"{RandomizerBackupDirectoryRelativePath}/{pathHash}.bin";
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsRandomizerBackupRelativePath(string relativePath)
    {
        return relativePath.StartsWith($"{RandomizerBackupDirectoryRelativePath}/", StringComparison.OrdinalIgnoreCase)
            && !Path.IsPathRooted(relativePath)
            && !relativePath.Split('/').Any(part => string.Equals(part, "..", StringComparison.Ordinal));
    }

    private static void TryDeleteRandomizerBackup(
        ProjectPaths paths,
        RandomizerRestoreEntry entry,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var backupRelativePath = NormalizeRelativePath(entry.OriginalBackupRelativePath);
        if (!IsRandomizerBackupRelativePath(backupRelativePath))
        {
            return;
        }

        var backupPath = ResolveOutputPath(paths, backupRelativePath);
        if (backupPath is null)
        {
            return;
        }

        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
                DeleteEmptyParentDirectories(paths.OutputRootPath!, backupPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Randomizer backup could not be deleted: {ex.Message}",
                file: backupRelativePath,
                expected: "Deletable Randomizer backup"));
        }
    }

    private static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        return SwShOutputRollbackScope.ResolvePhysicalContainedPath(
            paths.OutputRootPath,
            targetRelativePath);
    }

    private static bool IsPathInsideRoot(string outputRoot, string targetPath)
    {
        var relativePath = Path.GetRelativePath(outputRoot, targetPath);
        return PathContainment.IsWithinRoot(relativePath);
    }

    private static string NormalizeRelativePath(string? relativePath)
    {
        return (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
    }

    private static bool IsLayeredFsRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Split('/').Any(part => string.Equals(part, "..", StringComparison.Ordinal)))
        {
            return false;
        }

        return relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteEmptyParentDirectories(string outputRootPath, string filePath)
    {
        var outputRoot = Path.GetFullPath(outputRootPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrWhiteSpace(directory)
            && IsPathInsideRoot(outputRoot, directory)
            && !string.Equals(
                Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(outputRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(outputRoot, directory));
            var currentDirectory = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
                outputRoot,
                relativePath);
            if (currentDirectory is null
                || !string.Equals(currentDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                break;
            }

            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static void AddWorkflowErrors(
        IEnumerable<ValidationDiagnostic> sourceDiagnostics,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var diagnostic in sourceDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }
    }

    private static bool ShouldRandomizePokemon(SwShRandomizerOptions options)
    {
        return options.RandomizePokemonStats
            || options.RandomizePokemonTypes
            || options.RandomizePokemonAbilities
            || options.RandomizePokemonHeldItems
            || options.RandomizePokemonCatchRates
            || options.RandomizePokemonLearnsets
            || options.RandomizePokemonCompatibility
            || options.RandomizePokemonEvolutions;
    }

    private static bool ShouldLoadPokemon(SwShRandomizerOptions options)
    {
        return ShouldRandomizePokemon(options)
            || options.RandomizeWildEncounters
            || options.RandomizeStaticEncounters
            || options.RandomizeGiftEncounters;
    }

    private static bool ShouldLoadMoves(SwShRandomizerOptions options)
    {
        return options.RandomizePokemonLearnsets;
    }

    private static bool ShouldLoadItems(SwShRandomizerOptions options)
    {
        return options.RandomizePokemonHeldItems
            || options.RandomizeRaidRewards
            || options.RandomizeRaidBonusRewards;
    }

    private static bool IsEditablePokemon(SwShPokemonRecord record)
    {
        return record.PersonalId > 0
            && record.SpeciesId > 0
            && !IsProtectedBoxLegendarySpecies(record.SpeciesId);
    }

    private static bool IsLegalPokemonTarget(SwShPokemonRecord record)
    {
        return record.PersonalId > 0
            && record.SpeciesId > 0
            && !IsProtectedBoxLegendarySpecies(record.SpeciesId)
            && record.Personal.IsPresentInGame
            && record.Personal.HasSpriteForm
            && record.DexPresence.IsPresentInGame;
    }

    private static bool IsPresentPokemonTarget(SwShPokemonRecord record)
    {
        return record.PersonalId > 0
            && record.SpeciesId > 0
            && !IsProtectedBoxLegendarySpecies(record.SpeciesId)
            && record.Personal.IsPresentInGame
            && record.DexPresence.IsPresentInGame
            && record.BaseStats.Total > 0;
    }

    private static bool IsProtectedBoxLegendarySpecies(int speciesId)
    {
        return speciesId is ZacianSpeciesId or ZamazentaSpeciesId or EternatusSpeciesId;
    }

    private static bool IsMaxMove(SwShMoveRecord move)
    {
        return move.Name.StartsWith("Max ", StringComparison.OrdinalIgnoreCase)
            || move.Name.StartsWith("G-Max ", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CreateSelectedStatFields(SwShRandomizerOptions options)
    {
        if (options.StatHp)
        {
            yield return SwShPokemonWorkflowService.HPField;
        }

        if (options.StatAttack)
        {
            yield return SwShPokemonWorkflowService.AttackField;
        }

        if (options.StatDefense)
        {
            yield return SwShPokemonWorkflowService.DefenseField;
        }

        if (options.StatSpecialAttack)
        {
            yield return SwShPokemonWorkflowService.SpecialAttackField;
        }

        if (options.StatSpecialDefense)
        {
            yield return SwShPokemonWorkflowService.SpecialDefenseField;
        }

        if (options.StatSpeed)
        {
            yield return SwShPokemonWorkflowService.SpeedField;
        }
    }

    private static int GetStatValue(SwShPokemonRecord record, string field)
    {
        return field switch
        {
            SwShPokemonWorkflowService.HPField => record.BaseStats.HP,
            SwShPokemonWorkflowService.AttackField => record.BaseStats.Attack,
            SwShPokemonWorkflowService.DefenseField => record.BaseStats.Defense,
            SwShPokemonWorkflowService.SpecialAttackField => record.BaseStats.SpecialAttack,
            SwShPokemonWorkflowService.SpecialDefenseField => record.BaseStats.SpecialDefense,
            SwShPokemonWorkflowService.SpeedField => record.BaseStats.Speed,
            _ => 1,
        };
    }

    private static int[] DistributeStatTotal(int total, int count, DeterministicRandom rng)
    {
        var values = Enumerable.Repeat(1, count).ToArray();
        var remaining = Math.Clamp(total - count, 0, count * 254);
        while (remaining > 0)
        {
            var index = rng.NextInt(count);
            var room = 255 - values[index];
            if (room <= 0)
            {
                continue;
            }

            var add = Math.Min(room, Math.Min(remaining, rng.NextInt(Math.Min(room, remaining)) + 1));
            values[index] += add;
            remaining -= add;
        }

        return values;
    }

    private static int PickDifferentType(IReadOnlyList<int> typeOptions, int type1, DeterministicRandom rng)
    {
        var alternatives = typeOptions
            .Where(value => value != type1)
            .ToArray();

        return alternatives.Length > 0 ? rng.Pick(alternatives) : type1;
    }

    private static int PickAndCycle(DeterministicRandom rng, List<int> available, IReadOnlyList<int> fallback)
    {
        if (available.Count == 0)
        {
            foreach (var value in fallback)
            {
                available.Add(value);
            }
        }

        var picked = rng.Pick(available);
        available.Remove(picked);
        return picked;
    }

    private static void AddHeldItemEdit(
        SwShPokemonRecord record,
        string field,
        IReadOnlyList<ItemCandidate> itemPool,
        DeterministicRandom rng,
        ICollection<PendingEdit> edits)
    {
        var value = rng.NextInt(100) < 35
            ? rng.Pick(itemPool).ItemId
            : 0;
        edits.Add(CreatePokemonEdit(record, field, value, $"Randomize {record.Name} held item {field}"));
    }

    private static string CreateWildEncounterMirrorKey(SwShEncounterTableRecord table)
    {
        if (SwShEncountersWorkflowService.TryParseTableId(
            table.TableId,
            out var member,
            out var tableIndex,
            out var zoneId,
            out var subTableIndex))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{member.GameKey}:{tableIndex}:{zoneId:X16}:{subTableIndex}");
        }

        return $"{table.GameVersion}:{table.Location}:{table.EncounterType}";
    }

    internal static IReadOnlyList<int> CreateStrictDescendingSlotWeights(int count, DeterministicRandom rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        if (count <= 0)
        {
            return Array.Empty<int>();
        }

        if (count == 1)
        {
            return [100];
        }

        var weights = Enumerable.Range(0, count)
            .Select(index => count - index)
            .ToArray();
        var minimumTotal = count * (count + 1) / 2;
        if (minimumTotal > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Strictly descending encounter weights cannot sum to 100 for this many slots.");
        }

        var remaining = 100 - minimumTotal;
        while (remaining > 0)
        {
            var prefixLength = rng.NextInt(Math.Min(count, remaining)) + 1;
            for (var index = 0; index < prefixLength; index++)
            {
                weights[index]++;
            }

            remaining -= prefixLength;
        }

        return weights;
    }

    private static string ComputeOutputHash(IEnumerable<RandomizerDomainPlan> domainPlans)
    {
        var edits = domainPlans
            .SelectMany(plan => plan.Session.PendingEdits.Select(edit => new RandomizerOutputEdit(
                plan.Label,
                edit.Domain ?? string.Empty,
                edit.RecordId ?? string.Empty,
                edit.Field ?? string.Empty,
                edit.NewValue ?? string.Empty)))
            .OrderBy(edit => edit.Plan, StringComparer.Ordinal)
            .ThenBy(edit => edit.Domain, StringComparer.Ordinal)
            .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
            .ThenBy(edit => edit.Field, StringComparer.Ordinal)
            .ThenBy(edit => edit.NewValue, StringComparer.Ordinal)
            .ToArray();
        var payload = JsonSerializer.SerializeToUtf8Bytes(edits);
        var hash = SHA256.HashData(payload);

        return SwShRandomizerSeedCodec.Base64UrlEncode(hash.AsSpan(0, 16));
    }

    private static bool ContainsIgnoreCase(string value, string text)
    {
        return value.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RandomizerOutputEdit(
        string Plan,
        string Domain,
        string RecordId,
        string Field,
        string NewValue);

    private sealed class CollapsedDiagnostic(ValidationDiagnostic diagnostic, int count)
    {
        public ValidationDiagnostic Diagnostic { get; } = diagnostic;

        public int Count { get; set; } = count;
    }

    private sealed record RandomizerDomainPlan(
        string Label,
        EditSession Session,
        Func<ProjectPaths, EditSession, ChangePlan> CreateChangePlan,
        Func<ProjectPaths, EditSession, ChangePlan, ApplyResult> ApplyChangePlan);

    private sealed record RandomizerBuildResult(
        SwShRandomizerConfig Config,
        string Seed,
        IReadOnlyList<ValidationDiagnostic> Diagnostics,
        IReadOnlyList<RandomizerDomainPlan> DomainPlans);

    private sealed record PokemonCandidate(
        int PersonalId,
        int SpeciesId,
        int Form,
        int Type1,
        int Type2,
        int EvolutionStage,
        ProjectFileReference Source);

    private sealed record MoveCandidate(
        int MoveId,
        int Type,
        bool IsDamaging,
        ProjectFileReference Source);

    private sealed record ItemCandidate(
        int ItemId,
        ProjectFileReference Source);

    private sealed record RandomizerRestoreManifest(
        int Version,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<string>? WrittenRelativePaths = null,
        IReadOnlyList<RandomizerRestoreEntry>? Entries = null);

    private sealed record RandomizerRestoreEntry(
        string RelativePath,
        bool? OriginalExisted,
        string? OriginalSha256,
        string? OriginalBackupRelativePath,
        string? RandomizedSha256);

    private sealed record RandomizerRestoreCapture(
        IReadOnlyDictionary<string, RandomizerRestoreEntry> Entries,
        IReadOnlySet<string> NewRelativePaths,
        IReadOnlyList<string> CreatedBackupRelativePaths);

    private sealed record PreparedRandomizerRestoreEntry(
        string RelativePath,
        string TargetPath,
        RestoreFilePreimage TargetPreimage,
        RandomizerRestoreMutationKind Mutation,
        PreparedRandomizerBackup? Backup);

    private sealed record PreparedRandomizerBackup(
        string RelativePath,
        string Path,
        RestoreFilePreimage Preimage);

    private readonly record struct RestoreFilePreimage(
        RestoreFileKind Kind,
        long Length,
        string? Sha256)
    {
        public static RestoreFilePreimage ForFile(byte[] bytes)
        {
            return new RestoreFilePreimage(
                RestoreFileKind.File,
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)));
        }
    }

    private enum RandomizerRestoreMutationKind
    {
        AcknowledgeMissing,
        DeleteCreatedOutput,
        RestoreBackup,
    }

    private enum RestoreFileKind
    {
        Missing,
        File,
        Directory,
    }
}

internal enum SwShRandomizerRestoreMutationStage
{
    BeforeTargetMutation,
    BeforeManifestMutation,
    BeforeBackupDeletion,
}
