// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.GameDump;
using KM.Core.Projects;
using KM.SV.Workflows;

namespace KM.SV.GameDump;

public sealed class SvGameDumpService
{
    private readonly SvWorkflowService workflowService;

    public SvGameDumpService(SvWorkflowService? workflowService = null)
    {
        this.workflowService = workflowService ?? new SvWorkflowService();
    }

    public GameDumpWorkflow Load(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!IsScarletViolet(paths.SelectedGame))
        {
            return new GameDumpWorkflow([], [CreateGameMismatchDiagnostic()]);
        }

        var summaries = workflowService.List(paths).Workflows.ToDictionary(summary => summary.Id, StringComparer.Ordinal);
        var categories = CreateCategories()
            .Select(definition =>
            {
                var summary = summaries.GetValueOrDefault(definition.Id);
                var isAvailable = summary?.Availability is SvWorkflowAvailability.ReadOnly or SvWorkflowAvailability.Available;
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

        if (!IsScarletViolet(paths.SelectedGame))
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
                    $"{definition.Label} is not available for the current Scarlet/Violet project.",
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
                gameFamily = "Scarlet/Violet",
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
                SvWorkflowIds.Items,
                "Items",
                "Item records, prices, TM data, categories, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadItems(paths);
                    return new GameDumpCategoryData<KM.SV.Items.SvItemRecord>(workflow.Items, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.Pokemon,
                "Pokemon",
                "Pokemon personal data, evolutions, learnsets, compatibility, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadPokemon(paths);
                    return new GameDumpCategoryData<KM.SV.Pokemon.SvPokemonRecord>(workflow.Pokemon, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.Moves,
                "Moves",
                "Move stats, flags, secondary effects, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadMoves(paths);
                    return new GameDumpCategoryData<KM.SV.Moves.SvMoveRecord>(workflow.Moves, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTextCategory(
                SvWorkflowIds.Text,
                "Text",
                "Message text entries, dialogue contexts, languages, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadText(paths);
                    return new GameDumpCategoryData<KM.SV.Text.SvTextEntryRecord>(workflow.Entries, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.Trainers,
                "Trainers",
                "Trainer records, parties, Terastallization data, battle metadata, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadTrainers(paths);
                    return new GameDumpCategoryData<KM.SV.Trainers.SvTrainerRecord>(workflow.Trainers, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.Encounters,
                "Wild Encounters",
                "Wild encounter tables, condition rows, slots, levels, weights, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadEncounters(paths);
                    return new GameDumpCategoryData<KM.SV.Encounters.SvEncounterTableRecord>(workflow.Tables, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.TeraRaids,
                "Tera Raids",
                "Tera Raid boss rows, reward table previews, battle settings, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadTeraRaids(paths);
                    return new GameDumpCategoryData<KM.SV.Raids.SvTeraRaidEntry>(workflow.Raids, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.StaticEncounters,
                "Static Encounters",
                "Placed scripted Pokemon encounters, IVs, moves, shiny locks, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadStaticEncounters(paths);
                    return new GameDumpCategoryData<KM.SV.StaticEncounters.SvStaticEncounterEntry>(workflow.Encounters, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.GiftPokemon,
                "Gift Pokemon",
                "Scripted gift Pokemon records, Tera types, moves, IV modes, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadGiftPokemon(paths);
                    return new GameDumpCategoryData<KM.SV.Gifts.SvGiftPokemonEntry>(workflow.Gifts, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.TradePokemon,
                "Trade Pokemon",
                "In-game trade records, requested Pokemon, Tera types, moves, IV modes, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadTradePokemon(paths);
                    return new GameDumpCategoryData<KM.SV.Trades.SvTradePokemonEntry>(workflow.Trades, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.Placement,
                "Placement",
                "Placed objects, categories, coordinates, script links, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadPlacement(paths);
                    return new GameDumpCategoryData<KM.SV.Placement.SvPlacedObjectRecord>(workflow.Objects, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.Shops,
                "Shops",
                "Shop inventories, item metadata, prices, unlock conditions, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadShops(paths);
                    return new GameDumpCategoryData<KM.SV.Shops.SvShopRecord>(workflow.Shops, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                SvWorkflowIds.TypeChart,
                "Type Chart",
                "Type-effectiveness cells, vanilla values, build metadata, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadTypeChart(paths);
                    return new GameDumpCategoryData<KM.SV.TypeChart.SvTypeChartCell>(workflow.Cells, workflow.Diagnostics);
                }),
        ];
    }

    private static bool IsScarletViolet(ProjectGame? game)
    {
        return game is ProjectGame.Scarlet or ProjectGame.Violet;
    }

    private static ValidationDiagnostic CreateGameMismatchDiagnostic()
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Scarlet/Violet game dumps are only available for Scarlet/Violet projects.",
            expected: "Pokemon Scarlet or Pokemon Violet");
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
