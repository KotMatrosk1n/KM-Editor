// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.GameDump;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Text;
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
                var summaryId = string.Equals(
                    definition.Id,
                    ZaWorkflowIds.ScriptedBosses,
                    StringComparison.Ordinal)
                        ? ZaWorkflowIds.Encounters
                        : definition.Id;
                var summary = summaries.GetValueOrDefault(summaryId);
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
        IReadOnlyList<GameDumpSelection> selections,
        string? producerVersion = null)
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
        var categoryResults = new Dictionary<string, GameDumpWriteCategoryResult>(StringComparer.Ordinal);

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

        GameDumpRunTransaction transaction;
        try
        {
            transaction = GameDumpWriter.BeginTransaction(destinationFolder);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException
                or NotSupportedException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Failed to prepare the game dump destination: {exception.Message}",
                field: "destinationFolder"));
            return new GameDumpResult(destinationFolder, [], diagnostics, Succeeded: false);
        }

        using var transactionScope = transaction;
        foreach (var selection in selections.DistinctBy(selection => selection.CategoryId))
        {
            var definition = definitions[selection.CategoryId];
            try
            {
                var result = definition.Write(paths, transaction.StagingFolder, selection);
                categoryResults[selection.CategoryId] = result;
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
        if (!succeeded)
        {
            return new GameDumpResult(destinationFolder, [], diagnostics, Succeeded: false);
        }

        try
        {
            writtenFiles.Add(GameDumpWriter.WriteManifest(
                transaction.StagingFolder,
                GameDumpWriter.CreateManifest(
                    "Pokemon Legends Z-A",
                    paths.SelectedGame,
                    succeeded,
                    selections,
                    categoryResults,
                    writtenFiles,
                    diagnostics,
                    destinationFolder,
                    producerVersion)));
            transaction.Promote(
                writtenFiles,
                selections.Select(selection => selection.CategoryId).ToHashSet(StringComparer.Ordinal));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException
                or NotSupportedException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Failed to publish the game dump snapshot: {exception.Message}",
                field: "destinationFolder"));
            return new GameDumpResult(destinationFolder, [], diagnostics, Succeeded: false);
        }

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
                ZaWorkflowIds.Encounters,
                "Wild Encounters",
                "Wild encounter tables, slots, level ranges, weights, and provenance.",
                paths =>
                {
                    var workflow = workflowService.LoadEncounters(paths);
                    return new GameDumpCategoryData<KM.ZA.Encounters.ZaEncounterTableRecord>(workflow.Tables, workflow.Diagnostics);
                }),
            GameDumpWriter.CreateTableCategory(
                ZaWorkflowIds.ScriptedBosses,
                "Scripted Bosses",
                "Verified profile-specific battle-stage and HP-phase schedules, controller actions, selectors, move variants, runtime availability, and editability.",
                paths =>
                {
                    var workflow = workflowService.LoadEncounters(paths);
                    return new GameDumpCategoryData<KM.ZA.ScriptedBosses.ZaScriptedBossProfileRecord>(
                        workflow.ScriptedBosses,
                        workflow.Diagnostics);
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
                "Received local trade Pokemon payload rows, moves, IVs, event keys, and provenance.",
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
            GameDumpWriter.CreateTextCategory(
                ZaWorkflowIds.Text,
                "Text",
                "Every editable message line for the selected game-text language or all available game-text languages, including semantic message keys and source details.",
                new GameDumpCategoryLanguageOptions(
                    ZaTextWorkflowService.SupportedLanguages
                        .Select(language => new GameDumpLanguageOption(language.Language, language.Label))
                        .ToArray(),
                    ZaTextWorkflowService.SupportedLanguages
                        .Select(language => language.Language)
                        .ToArray(),
                    SupportsAllLanguages: true),
                LoadTextRows),
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

    private GameDumpCategoryData<ZaTextEntryRecord> LoadTextRows(
        ProjectPaths paths,
        GameDumpSelection selection)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var requestedLanguages = ResolveRequestedTextLanguages(paths, selection, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new GameDumpCategoryData<ZaTextEntryRecord>([], diagnostics);
        }

        var entries = new List<ZaTextEntryRecord>();
        var languageMetadata = new List<GameDumpLanguageExportMetadata>(requestedLanguages.Count);
        foreach (var requestedLanguage in requestedLanguages)
        {
            var workflow = workflowService.LoadTextUnpaged(paths, requestedLanguage);
            diagnostics.AddRange(workflow.Diagnostics);
            entries.AddRange(workflow.Entries);
            var usedFallback = !string.Equals(
                requestedLanguage,
                workflow.SelectedLanguage,
                StringComparison.OrdinalIgnoreCase);
            languageMetadata.Add(new GameDumpLanguageExportMetadata(
                requestedLanguage,
                workflow.SelectedLanguage,
                usedFallback,
                usedFallback
                    ? $"{requestedLanguage} message tables were not found; {workflow.SelectedLanguage} message tables were exported instead."
                    : null,
                workflow.Stats.SourceFileCount,
                workflow.Entries.Count));
        }

        return new GameDumpCategoryData<ZaTextEntryRecord>(
            entries,
            diagnostics,
            new GameDumpCategoryExportMetadata(languageMetadata));
    }

    private static IReadOnlyList<string> ResolveRequestedTextLanguages(
        ProjectPaths paths,
        GameDumpSelection selection,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var requestedCodes = selection.LanguageCodes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (requestedCodes.Length == 0)
        {
            return [ZaGameTextLanguage.Resolve(paths)];
        }

        var supportedLanguages = ZaTextWorkflowService.SupportedLanguages;
        var resolved = new List<string>(requestedCodes.Length);
        foreach (var requestedCode in requestedCodes)
        {
            var supported = supportedLanguages.FirstOrDefault(language => string.Equals(
                language.Language,
                requestedCode,
                StringComparison.OrdinalIgnoreCase));
            if (supported is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Text dump language '{requestedCode}' is not supported by Pokemon Legends Z-A.",
                    field: "languageCodes",
                    expected: string.Join(", ", supportedLanguages.Select(language => language.Language))));
                continue;
            }

            resolved.Add(supported.Language);
        }

        return resolved;
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
