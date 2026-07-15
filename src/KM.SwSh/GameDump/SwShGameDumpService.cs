// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.GameDump;
using KM.Core.Projects;
using KM.SwSh.Workflows;

namespace KM.SwSh.GameDump;

public sealed class SwShGameDumpService
{
    private readonly SwShWorkflowService workflowService;

    public SwShGameDumpService(SwShWorkflowService? workflowService = null)
    {
        this.workflowService = workflowService ?? new SwShWorkflowService();
    }

    public GameDumpWorkflow Load(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!IsSwordShield(paths.SelectedGame))
        {
            return new GameDumpWorkflow([], [CreateGameMismatchDiagnostic()]);
        }

        var summaries = workflowService.List(paths).Workflows.ToDictionary(summary => summary.Id, StringComparer.Ordinal);
        var categories = CreateCategories()
            .Select(definition =>
            {
                var summary = summaries.GetValueOrDefault(definition.Id);
                var isAvailable = summary?.Availability is SwShWorkflowAvailability.ReadOnly or SwShWorkflowAvailability.Available;
                var diagnostics = summary?.Diagnostics ?? [];
                return definition.ToCategory(isAvailable, diagnostics);
            })
            .ToArray();

        return new GameDumpWorkflow(categories, []);
    }

    public GameDumpResult Run(
        ProjectPaths paths,
        string destinationFolder,
        IReadOnlyList<GameDumpSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(selections);

        if (!IsSwordShield(paths.SelectedGame))
        {
            return new GameDumpResult(destinationFolder, [], [CreateGameMismatchDiagnostic()], Succeeded: false);
        }

        var diagnostics = GameDumpWriter.ValidateDestination(paths, destinationFolder).ToList();
        if (selections.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Select at least one dump category.",
                field: "selections"));
        }

        var workflow = Load(paths);
        diagnostics.AddRange(workflow.Diagnostics);
        var categoryStates = workflow.Categories.ToDictionary(category => category.Id, StringComparer.Ordinal);
        var definitions = CreateCategories().ToDictionary(category => category.Id, StringComparer.Ordinal);
        var writtenFiles = new List<GameDumpWrittenFile>();

        foreach (var selection in selections)
        {
            if (!definitions.TryGetValue(selection.CategoryId, out var definition))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Dump category '{selection.CategoryId}' is not recognized.",
                    field: "categoryId",
                    expected: string.Join(", ", definitions.Keys)));
                continue;
            }

            if (!categoryStates.TryGetValue(selection.CategoryId, out var category) || !category.IsAvailable)
            {
                diagnostics.AddRange(category?.Diagnostics ?? []);
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{definition.Label} is not available for the current Sword/Shield project.",
                    field: "categoryId"));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new GameDumpResult(destinationFolder, writtenFiles, diagnostics, Succeeded: false);
        }

        Directory.CreateDirectory(destinationFolder);
        foreach (var selection in selections.DistinctBy(selection => selection.CategoryId))
        {
            var definition = definitions[selection.CategoryId];
            try
            {
                var result = definition.Write(paths, destinationFolder, selection.Format);
                diagnostics.AddRange(result.Diagnostics);
                writtenFiles.AddRange(result.WrittenFiles);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Failed to dump {definition.Label}: {exception.Message}",
                    field: definition.Id));
            }
        }

        var succeeded = diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
        writtenFiles.Add(GameDumpWriter.WriteManifest(
            destinationFolder,
            new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                gameFamily = "Sword/Shield",
                selectedGame = paths.SelectedGame?.ToString(),
                succeeded,
                categories = selections.Select(selection => new
                {
                    id = selection.CategoryId,
                    format = selection.Format.ToString(),
                }),
                files = writtenFiles.Select(file => new
                {
                    categoryId = file.CategoryId,
                    relativePath = file.RelativePath,
                    sizeBytes = file.SizeBytes,
                }),
                diagnostics = diagnostics.Select(diagnostic => new
                {
                    severity = diagnostic.Severity.ToString(),
                    diagnostic.Message,
                    diagnostic.File,
                    diagnostic.Domain,
                    diagnostic.Field,
                    diagnostic.Expected,
                }),
            }));

        return new GameDumpResult(destinationFolder, writtenFiles, diagnostics, succeeded);
    }

    private IGameDumpCategoryDefinition[] CreateCategories()
    {
        return
        [
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.Items,
                "Items",
                "Item records, prices, TM data, categories, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadItems(paths);
                    return new GameDumpCategoryData<KM.SwSh.Items.SwShItemRecord>(workflow.Items, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.Pokemon,
                "Pokemon",
                "Pokemon personal data, evolutions, learnsets, compatibility, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadPokemon(paths);
                    return new GameDumpCategoryData<KM.SwSh.Pokemon.SwShPokemonRecord>(workflow.Pokemon, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.Moves,
                "Moves",
                "Move stats, flags, secondary effects, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadMoves(paths);
                    return new GameDumpCategoryData<KM.SwSh.Moves.SwShMoveRecord>(workflow.Moves, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTextCategory(
                SwShWorkflowIds.Text,
                "Text",
                "Text entries, dialogue keys, languages, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadText(paths);
                    return new GameDumpCategoryData<KM.SwSh.Text.SwShTextEntryRecord>(workflow.Entries, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.Trainers,
                "Trainers",
                "Trainer records, parties, battle metadata, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadTrainers(paths);
                    return new GameDumpCategoryData<KM.SwSh.Trainers.SwShTrainerRecord>(workflow.Trainers, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.GiftPokemon,
                "Gift Pokemon",
                "Scripted gift Pokemon records, IV modes, items, moves, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadGiftPokemon(paths);
                    return new GameDumpCategoryData<KM.SwSh.Gifts.SwShGiftPokemonEntry>(workflow.Gifts, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.TradePokemon,
                "Trade Pokemon",
                "In-game trade records, requested Pokemon, memories, IV modes, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadTradePokemon(paths);
                    return new GameDumpCategoryData<KM.SwSh.Trades.SwShTradePokemonEntry>(workflow.Trades, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.StaticEncounters,
                "Static Encounters",
                "Scripted static encounter records, IV modes, moves, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadStaticEncounters(paths);
                    return new GameDumpCategoryData<KM.SwSh.StaticEncounters.SwShStaticEncounterEntry>(workflow.Encounters, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.RentalPokemon,
                "Rental Pokemon",
                "Rental Pokemon records, moves, IVs, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadRentalPokemon(paths);
                    return new GameDumpCategoryData<KM.SwSh.Rentals.SwShRentalPokemonEntry>(workflow.Rentals, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.Encounters,
                "Wild Encounters",
                "Wild encounter tables, slots, levels, weather, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadEncounters(paths);
                    return new GameDumpCategoryData<KM.SwSh.Encounters.SwShEncounterTableRecord>(workflow.Tables, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.RaidBattles,
                "Raid Battles",
                "Raid battle tables, Pokemon slots, probabilities, rewards, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadRaidBattles(paths);
                    return new GameDumpCategoryData<KM.SwSh.Raids.SwShRaidBattleTableRecord>(workflow.Tables, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.RaidRewards,
                "Raid Rewards",
                "Raid drop reward tables, items, per-star drop chances, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadRaidRewards(paths);
                    return new GameDumpCategoryData<KM.SwSh.Raids.SwShRaidRewardTableRecord>(workflow.Tables, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.RaidBonusRewards,
                "Raid Bonus Rewards",
                "Raid bonus reward tables, items, per-star quantities, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadRaidBonusRewards(paths);
                    return new GameDumpCategoryData<KM.SwSh.Raids.SwShRaidRewardTableRecord>(workflow.Tables, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.Shops,
                "Shops",
                "Shop inventories, prices, stock limits, currencies, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadShops(paths);
                    return new GameDumpCategoryData<KM.SwSh.Shops.SwShShopRecord>(workflow.Shops, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.Placement,
                "Placement",
                "Placed objects, map coordinates, categories, script links, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadPlacement(paths);
                    return new GameDumpCategoryData<KM.SwSh.Placement.SwShPlacedObjectRecord>(workflow.Objects, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.Behavior,
                "Behavior",
                "Symbol encounter behavior profiles, model anchors, radii, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadBehavior(paths);
                    return new GameDumpCategoryData<KM.SwSh.Behavior.SwShBehaviorEntryRecord>(workflow.Entries, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SwShWorkflowIds.TypeChart,
                "Type Chart",
                "Type-effectiveness cells, vanilla values, build metadata, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadTypeChart(paths);
                    return new GameDumpCategoryData<KM.SwSh.TypeChart.SwShTypeChartCell>(workflow.Cells, workflow.Diagnostics);
                }),
        ];
    }

    private static ValidationDiagnostic CreateGameMismatchDiagnostic()
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Sword/Shield game dumps are only available for Sword/Shield projects.",
            expected: "Pokemon Sword or Pokemon Shield");
    }

    private static bool IsSwordShield(ProjectGame? game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            Domain: "gameDump",
            Field: field,
            Expected: expected);
    }
}
