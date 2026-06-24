// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.GameDump;
using KM.Core.Projects;
using KM.ZA.Workflows;

namespace KM.ZA.GameDump;

public sealed class ZaGameDumpService
{
    private readonly ZaWorkflowService workflowService;

    public ZaGameDumpService(ZaWorkflowService? workflowService = null)
    {
        this.workflowService = workflowService ?? new ZaWorkflowService();
    }

    public GameDumpWorkflow Load(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.SelectedGame is not ProjectGame.ZA)
        {
            return new GameDumpWorkflow([], [CreateGameMismatchDiagnostic()]);
        }

        var summaries = workflowService.List(paths).Workflows.ToDictionary(summary => summary.Id, StringComparer.Ordinal);
        var categories = CreateCategories()
            .Select(definition =>
            {
                var summary = summaries.GetValueOrDefault(definition.Id);
                var isAvailable = summary?.Availability is ZaWorkflowAvailability.ReadOnly or ZaWorkflowAvailability.Available;
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

        if (paths.SelectedGame is not ProjectGame.ZA)
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
                    $"{definition.Label} is not available for the current Pokemon Legends Z-A project.",
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
                gameFamily = "Pokemon Legends Z-A",
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
                ZaWorkflowIds.Pokemon,
                "Pokemon",
                "Pokemon personal data, evolutions, learnsets, compatibility, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadPokemon(paths);
                    return new GameDumpCategoryData<KM.ZA.Pokemon.ZaPokemonRecord>(workflow.Pokemon, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.Trainers,
                "Trainers",
                "Trainer records, ranks, AI flags, party Pokemon, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadTrainers(paths);
                    return new GameDumpCategoryData<KM.ZA.Trainers.ZaTrainerRecord>(workflow.Trainers, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.StaticEncounters,
                "Static Encounters",
                "Scripted static encounter Pokemon rows, moves, IVs, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadStaticEncounters(paths);
                    return new GameDumpCategoryData<KM.ZA.StaticEncounters.ZaStaticEncounterEntry>(workflow.Encounters, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.GiftPokemon,
                "Gift Pokemon",
                "Scripted local gift Pokemon rows, moves, IVs, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadGiftPokemon(paths);
                    return new GameDumpCategoryData<KM.ZA.Gifts.ZaGiftPokemonEntry>(workflow.Gifts, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.TradePokemon,
                "Trade Pokemon",
                "Scripted local trade Pokemon rows, moves, IVs, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadTradePokemon(paths);
                    return new GameDumpCategoryData<KM.ZA.Trades.ZaTradePokemonEntry>(workflow.Trades, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.Moves,
                "Moves",
                "Move stats, flags, secondary effects, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadMoves(paths);
                    return new GameDumpCategoryData<KM.ZA.Moves.ZaMoveRecord>(workflow.Moves, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.Items,
                "Items",
                "Item records, prices, TM data, categories, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadItems(paths);
                    return new GameDumpCategoryData<KM.ZA.Items.ZaItemRecord>(workflow.Items, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.Placement,
                "Placement",
                "Spawner transform placement rows and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadPlacement(paths);
                    return new GameDumpCategoryData<KM.ZA.Placement.ZaPlacedObjectRecord>(workflow.Objects, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.Shops,
                "Shops",
                "Shop inventories, prices, currencies, unlock conditions, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadShops(paths);
                    return new GameDumpCategoryData<KM.ZA.Shops.ZaShopRecord>(workflow.Shops, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.TypeChart,
                "Type Chart",
                "Type-effectiveness table cells from exefs/main.",
                paths =>
                {
                    var workflow = workflowService.LoadTypeChart(paths);
                    return new GameDumpCategoryData<KM.ZA.TypeChart.ZaTypeChartCell>(workflow.Cells, workflow.Diagnostics);
                }),
        ];
    }

    private static ValidationDiagnostic CreateGameMismatchDiagnostic()
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Pokemon Legends Z-A game dumps are only available for Pokemon Legends Z-A projects.",
            expected: "Pokemon Legends Z-A project");
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
