// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Text;

public sealed class SwShTextWorkflowService
{
    public const string MessageRootPath = "romfs/bin/message";
    public const string PreferredLanguage = SwShGameTextLanguage.English;
    public const string TextValueField = "value";
    public const int MaximumTextLength = 4096;
    public const int DefaultQueryLimit = 500;
    public const int MaximumQueryLimit = 1000;

    private const string AllCategoryId = "all";
    private const string MainStoryCategoryId = "swsh-main-story";
    private const string SideEventsCategoryId = "swsh-side-events";
    private const string IsleOfArmorCategoryId = "swsh-isle-of-armor";
    private const string CrownTundraCategoryId = "swsh-crown-tundra";
    private const string FieldWorldCategoryId = "swsh-field-world";
    private const string BattlesCategoryId = "swsh-battles";
    private const string ItemsCategoryId = "swsh-items";
    private const string PokemonPokedexCategoryId = "swsh-pokemon-pokedex";
    private const string MovesAbilitiesCategoryId = "swsh-moves-abilities";
    private const string TrainersCharactersCategoryId = "swsh-trainers-characters";
    private const string LocationsCategoryId = "swsh-locations";
    private const string FacilitiesActivitiesCategoryId = "swsh-facilities-activities";
    private const string UiOnlineSharedCategoryId = "swsh-ui-online-shared";
    private const string OtherScriptsCategoryId = "swsh-other-scripts";

    private static readonly IReadOnlyList<TextCategoryDefinition> CategoryDefinitions =
    [
        new(MainStoryCategoryId, "Main Story", "Base-game story dialogue and cutscene text."),
        new(SideEventsCategoryId, "Side Events", "Base-game numbered side-event dialogue."),
        new(IsleOfArmorCategoryId, "Isle of Armor", "Isle of Armor story, side events, stations, and activities."),
        new(CrownTundraCategoryId, "Crown Tundra", "Crown Tundra story, side events, expeditions, and tournaments."),
        new(FieldWorldCategoryId, "Field and World", "Route, town, Wild Area, field-event, sign, and ambient world text."),
        new(BattlesCategoryId, "Battles", "Battle interface, teams, videos, tournaments, Battle Tower, and online battle text."),
        new(ItemsCategoryId, "Items", "Item names and descriptions, bag pockets, field items, and related text."),
        new(PokemonPokedexCategoryId, "Pokemon and Pokedex", "Pokemon names, Pokedex entries, storage, status, evolution, and related text."),
        new(MovesAbilitiesCategoryId, "Moves and Abilities", "Move and Ability names, descriptions, learning, remembering, and forgetting text."),
        new(TrainersCharactersCategoryId, "Trainers and Characters", "Trainer names, classes, dialogue labels, and character names."),
        new(LocationsCategoryId, "Locations", "Place names, Town Map targets, and facility-location labels."),
        new(FacilitiesActivitiesCategoryId, "Facilities and Activities", "Camps, jobs, customization, League Cards, shops, trades, and other services."),
        new(UiOnlineSharedCategoryId, "UI, Online, and Shared", "Shared menus, prompts, networking, system messages, and interface text."),
        new(OtherScriptsCategoryId, "Other Scripts", "Small internal script tables without a narrower proven owner."),
    ];

    private static readonly HashSet<string> IsleOfArmorSources = CreateSourceSet(
        "common/kumite.dat",
        "common/kumite_msg.dat");

    private static readonly HashSet<string> CrownTundraSources = CreateSourceSet(
        "common/chika.dat",
        "common/gst.dat",
        "common/gst_tournament.dat",
        "common/peonymemo.dat");

    private static readonly HashSet<string> FieldWorldSources = CreateSourceSet(
        "script/berry.dat",
        "script/book_event.dat",
        "script/circuit.dat",
        "script/field_event.dat",
        "script/field_pokemon_fureai.dat",
        "script/fishing.dat",
        "script/kanban.dat",
        "script/pokecamp.dat",
        "script/traffic_npc.dat",
        "script/tv_event.dat",
        "script/wide_road.dat");

    private static readonly HashSet<string> BattleSources = CreateSourceSet(
        "common/battle_bgm_select.dat",
        "common/battle_watching.dat",
        "common/battleteam_select.dat",
        "common/battletrainer_select.dat",
        "common/battlevideo_player.dat",
        "common/battlevideo_rec.dat",
        "common/btl_app.dat",
        "common/btl_attack.dat",
        "common/btl_bgm_select.dat",
        "common/btl_get.dat",
        "common/btl_pokelist.dat",
        "common/btl_pokeselect.dat",
        "common/btl_raidget.dat",
        "common/btl_set.dat",
        "common/btl_state.dat",
        "common/btl_std.dat",
        "common/btl_talk.dat",
        "common/btl_team.dat",
        "common/btlspot.dat",
        "common/btltower.dat",
        "common/competition_organize.dat",
        "common/dendou_demo.dat",
        "common/live_tournament.dat",
        "common/net_battle_reception.dat",
        "common/net_btl.dat",
        "common/regulation.dat",
        "common/rental_team.dat",
        "common/tournament.dat",
        "common/tower_msg.dat",
        "common/tower_trainer.dat",
        "common/tower_trname.dat",
        "common/vs_demo.dat",
        "script/battle_fes.dat",
        "script/rankmatch.dat",
        "script/tournament.dat");

    private static readonly HashSet<string> ItemSources = CreateSourceSet(
        "common/bag.dat",
        "common/bag_pocket.dat",
        "common/dressup_item_name.dat",
        "common/iteminfo.dat",
        "common/itemname.dat",
        "common/itemname_acc.dat",
        "common/itemname_acc_classified.dat",
        "common/itemname_classified.dat",
        "common/itemname_plural.dat",
        "common/itemname_plural_classified.dat",
        "common/nuts_name.dat",
        "script/fld_item.dat");

    private static readonly HashSet<string> PokemonPokedexSources = CreateSourceSet(
        "common/box.dat",
        "common/boxname.dat",
        "common/capturedemo.dat",
        "common/level_up.dat",
        "common/monsname.dat",
        "common/pokedex.dat",
        "common/pokelist.dat",
        "common/ribbon.dat",
        "common/seikaku.dat",
        "common/shinka_demo.dat",
        "common/status.dat",
        "common/syoujou.dat",
        "common/tamago_demo.dat",
        "common/trade_demo.dat",
        "common/trainermemo.dat",
        "common/typename.dat",
        "common/zkn_form.dat",
        "common/zkn_height.dat",
        "common/zkn_type.dat",
        "common/zkn_weight.dat",
        "common/zukan.dat",
        "common/zukan_comment_A.dat",
        "common/zukan_comment_B.dat",
        "common/zukan_hyouka.dat",
        "script/change_deox_scr.dat",
        "script/change_rotom_scr.dat",
        "script/change_torimian_scr.dat",
        "script/zukan_praise.dat");

    private static readonly HashSet<string> MoveAbilitySources = CreateSourceSet(
        "common/gwazainfo.dat",
        "common/gwazamessage.dat",
        "common/gwazaname.dat",
        "common/msg_ui_waza_.dat",
        "common/tokusei.dat",
        "common/tokuseiinfo.dat",
        "common/waza_con.dat",
        "common/waza_omoidashi.dat",
        "common/waza_remember.dat",
        "common/waza_wasure.dat",
        "common/wazainfo.dat",
        "common/wazaname.dat",
        "script/hidden_power.dat",
        "script/poke_waza.dat",
        "script/poke_waza_coalescence.dat",
        "script/poke_waza_dragon.dat",
        "script/poke_waza_garyoutensei.dat",
        "script/poke_waza_maboroshi.dat",
        "script/poke_waza_powerful.dat");

    private static readonly HashSet<string> TrainerCharacterSources = CreateSourceSet(
        "common/another_name.dat",
        "common/namelist.dat",
        "common/trmsg.dat",
        "common/trname.dat",
        "common/trtype.dat");

    private static readonly HashSet<string> LocationSources = CreateSourceSet(
        "common/place_name.dat",
        "common/place_name_indirect.dat",
        "common/place_name_out.dat",
        "common/place_name_per.dat",
        "common/place_name_spe.dat",
        "common/townmap.dat",
        "common/townmap_facility.dat",
        "common/townmap_target.dat",
        "script/place_name.dat");

    private static readonly HashSet<string> FacilityActivitySources = CreateSourceSet(
        "common/dressup.dat",
        "common/field_camp.dat",
        "common/fs_album.dat",
        "common/fs_deco.dat",
        "common/fs_photo.dat",
        "common/fs_sd.dat",
        "common/hairsalon.dat",
        "common/id_photo.dat",
        "common/kisekae.dat",
        "common/kisekae_color.dat",
        "common/kisekae_item_name.dat",
        "common/pokejob.dat",
        "common/pokecamp_cookinfo.dat",
        "common/pokecamp_cooking.dat",
        "common/pokecamp_cooknamet.dat",
        "common/pokecamp_kinomiinfo.dat",
        "common/pokecamp_main.dat",
        "common/pokecamp_npccamp.dat",
        "common/pokecamp_talk.dat",
        "common/pw.dat",
        "common/pw_worklist.dat",
        "common/trainer_license.dat",
        "common/trainer_license_character.dat",
        "common/trainer_license_comon.dat",
        "common/trainer_license_maker.dat",
        "common/trainer_pass.dat",
        "common/vendingmachine.dat",
        "script/elevator.dat",
        "script/field_trade.dat",
        "script/fitting_room.dat",
        "script/fossil_scr.dat",
        "script/hairsalon.dat",
        "script/id_present_scr.dat",
        "script/kaifuku_kisekae_room.dat",
        "script/msg_ui_shop.dat",
        "script/namechange.dat",
        "script/poke_memory_feeling.dat",
        "script/poke_memory_place.dat",
        "script/poke_memory_rank.dat",
        "script/pokemoncenter.dat",
        "script/railway.dat",
        "script/roto_housing.dat",
        "script/shop.dat",
        "script/sodateya_scr.dat");

    private static readonly IReadOnlyList<SwShTextEditableField> EditableFields =
    [
        new SwShTextEditableField(TextValueField, "Text value", "multilineText", 0, MaximumTextLength),
    ];

    private static readonly IReadOnlyList<SwShTextLanguageRecord> Languages =
        SwShGameTextLanguage.SupportedMessageLanguages
            .Select(language => new SwShTextLanguageRecord(language, GetLanguageLabel(language)))
            .ToArray();

    private readonly SwShTextCacheStore cacheStore;
    private readonly object sourceInventorySyncRoot = new();
    private ProjectId? retainedSourceInventoryProjectId;
    private TextSourceInventory? retainedSourceInventory;

    public SwShTextWorkflowService(SwShCacheManager? cacheManager = null)
    {
        cacheStore = new SwShTextCacheStore(cacheManager);
    }

    public static IReadOnlyList<SwShTextLanguageRecord> SupportedLanguages => Languages;

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Text and Dialogue Map requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShTextWorkflow Load(OpenedProject project, SwShTextWorkflowQuery? query = null)
    {
        return LoadCore(project, query, unpagedLanguage: null);
    }

    public SwShTextWorkflow LoadUnpaged(OpenedProject project, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return LoadCore(project, query: null, unpagedLanguage: language);
    }

    internal IReadOnlyList<SwShTextCacheWarmupTarget> CreateCacheWarmupTargets(
        OpenedProject project,
        SwShCacheMode mode)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (mode == SwShCacheMode.Minimal)
        {
            return [];
        }

        var inventory = GetSourceInventory(project);
        var availableLanguages = SwShGameTextLanguage.SupportedMessageLanguages
            .Where(language => ResolveMessageSources(inventory, language, requireBaseSource: true).Count > 0)
            .ToArray();
        if (availableLanguages.Length == 0)
        {
            return [];
        }

        IReadOnlyList<string> selectedLanguages;
        if (mode == SwShCacheMode.Performance)
        {
            selectedLanguages = availableLanguages;
        }
        else
        {
            var preferred = SwShGameTextLanguage.Resolve(project.Paths);
            selectedLanguages =
            [
                availableLanguages.Contains(preferred, StringComparer.OrdinalIgnoreCase)
                    ? preferred
                    : availableLanguages.Contains(PreferredLanguage, StringComparer.OrdinalIgnoreCase)
                        ? PreferredLanguage
                        : availableLanguages[0],
            ];
        }

        var targets = new List<SwShTextCacheWarmupTarget>();
        foreach (var language in selectedLanguages)
        {
            var sources = ResolveMessageSources(inventory, language, requireBaseSource: true);
            foreach (var category in CategoryDefinitions)
            {
                if (sources.Any(source => string.Equals(
                    source.CategoryId,
                    category.CategoryId,
                    StringComparison.Ordinal)))
                {
                    targets.Add(new SwShTextCacheWarmupTarget(language, category.CategoryId));
                }
            }
        }

        return targets;
    }

    internal bool WarmupCache(OpenedProject project, SwShTextCacheWarmupTarget target)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(target);

        var inventory = GetSourceInventory(project);
        var sources = ResolveMessageSources(inventory, target.Language, requireBaseSource: true)
            .Where(source => string.Equals(source.CategoryId, target.CategoryId, StringComparison.Ordinal))
            .ToArray();
        var baseSources = CreateBaseSources(project, sources);
        if (baseSources.Count > 0 && project.Paths.SelectedGame is { } selectedGame)
        {
            return cacheStore.WarmBaseCategory(
                selectedGame,
                target.Language,
                target.CategoryId,
                baseSources);
        }

        return false;
    }

    public void ClearMemoryCache()
    {
        cacheStore.ClearMemoryCache();
        lock (sourceInventorySyncRoot)
        {
            retainedSourceInventoryProjectId = null;
            retainedSourceInventory = null;
        }
    }

    private SwShTextWorkflow LoadCore(
        OpenedProject project,
        SwShTextWorkflowQuery? query,
        string? unpagedLanguage)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var normalizedQuery = NormalizeQuery(query);
        var requestedLanguage = SwShGameTextLanguage.Resolve(
            unpagedLanguage ?? normalizedQuery?.Language ?? project.Paths.GameTextLanguage);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                [],
                [],
                diagnostics,
                [],
                AllCategoryId,
                requestedLanguage,
                page: null,
                sourceFileCount: 0);
        }

        var inventory = GetSourceInventory(project);
        var textSources = ResolveMessageSources(
            inventory,
            requestedLanguage,
            diagnostics,
            out var selectedLanguage);
        if (textSources.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Text and Dialogue Map did not find any Sword/Shield message tables.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/{{common,script}}/*.dat"));
            return CreateWorkflow(
                summary,
                [],
                [],
                diagnostics,
                [],
                AllCategoryId,
                selectedLanguage,
                page: null,
                sourceFileCount: 0);
        }

        var categories = CreateCategories(textSources);
        var selectedCategoryId = ResolveSelectedCategoryId(normalizedQuery?.CategoryId, categories);
        var selectedSources = string.Equals(selectedCategoryId, AllCategoryId, StringComparison.Ordinal)
            ? textSources
            : textSources
                .Where(source => string.Equals(source.CategoryId, selectedCategoryId, StringComparison.Ordinal))
                .ToArray();
        var sourcesByCategory = textSources
            .GroupBy(source => source.CategoryId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var loadedCategories = new Dictionary<string, IReadOnlyDictionary<string, SwShTextCachedSource>>(
            StringComparer.Ordinal);
        var entries = new List<SwShTextEntryRecord>();
        var dialogueReferences = new List<SwShDialogueReferenceRecord>();
        var scannedTextEntryCount = 0;
        var matchedTextEntryCount = 0;
        var hasNextPage = false;
        var stopScanning = false;

        foreach (var source in selectedSources)
        {
            if (!loadedCategories.TryGetValue(source.CategoryId, out var loadedSources))
            {
                loadedSources = LoadEffectiveCategory(
                    project,
                    selectedLanguage,
                    source.CategoryId,
                    sourcesByCategory[source.CategoryId],
                    inventory.LayeredKeyOverrides,
                    diagnostics);
                loadedCategories.Add(source.CategoryId, loadedSources);
            }

            if (!loadedSources.TryGetValue(source.VirtualPath, out var parsedSource))
            {
                continue;
            }

            var provenance = CreateProvenance(source.Entry);
            for (var lineIndex = 0; lineIndex < parsedSource.Lines.Count; lineIndex++)
            {
                var line = parsedSource.Lines[lineIndex];
                var textId = scannedTextEntryCount++;
                var label = CreateTextLabel(parsedSource.Context, lineIndex, line.MessageKey);
                if (normalizedQuery is not null)
                {
                    if (!MatchesQuery(
                        normalizedQuery.SearchText,
                        textId,
                        line.MessageKey,
                        selectedLanguage,
                        source.VirtualPath,
                        parsedSource.Context,
                        label,
                        lineIndex,
                        line.Value))
                    {
                        continue;
                    }

                    if (matchedTextEntryCount++ < normalizedQuery.Offset)
                    {
                        continue;
                    }

                    if (entries.Count >= normalizedQuery.Limit)
                    {
                        hasNextPage = true;
                        stopScanning = true;
                        break;
                    }
                }

                AddTextRecord(
                    entries,
                    dialogueReferences,
                    textId,
                    source,
                    parsedSource.Context,
                    lineIndex,
                    line.Value,
                    label,
                    line.MessageKey,
                    provenance);
            }

            if (stopScanning)
            {
                break;
            }
        }

        var page = normalizedQuery is null
            ? null
            : new SwShTextResultPage(
                normalizedQuery.Offset,
                normalizedQuery.Limit,
                entries.Count,
                normalizedQuery.Offset > 0,
                hasNextPage);
        return CreateWorkflow(
            summary,
            entries,
            dialogueReferences,
            diagnostics,
            categories,
            selectedCategoryId,
            selectedLanguage,
            page,
            textSources.Count);
    }

    private IReadOnlyDictionary<string, SwShTextCachedSource> LoadEffectiveCategory(
        OpenedProject project,
        string language,
        string categoryId,
        IReadOnlyList<TextFileSource> sources,
        IReadOnlySet<string> layeredKeyOverrides,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, SwShTextCachedSource>(StringComparer.OrdinalIgnoreCase);
        var baseSources = CreateBaseSources(project, sources);
        if (baseSources.Count > 0 && project.Paths.SelectedGame is { } selectedGame)
        {
            var cached = cacheStore.LoadBaseCategory(selectedGame, language, categoryId, baseSources);
            foreach (var diagnostic in cached.Diagnostics)
            {
                diagnostics.Add(diagnostic);
            }

            foreach (var source in cached.Sources)
            {
                result[source.VirtualPath] = source;
            }
        }

        foreach (var source in sources)
        {
            if (source.Entry.LayeredFile is null
                && !layeredKeyOverrides.Contains(ChangeExtension(source.VirtualPath, ".tbl")))
            {
                continue;
            }

            var parsed = TryParseEffectiveSource(project, source, diagnostics);
            if (parsed is null)
            {
                result.Remove(source.VirtualPath);
            }
            else
            {
                result[source.VirtualPath] = parsed;
            }
        }

        return result;
    }

    private static IReadOnlyList<SwShTextBaseSource> CreateBaseSources(
        OpenedProject project,
        IReadOnlyList<TextFileSource> sources)
    {
        var result = new List<SwShTextBaseSource>();
        foreach (var source in sources)
        {
            if (source.Entry.BaseFile is null)
            {
                continue;
            }

            var dataPath = ResolveBasePath(project.Paths, source.VirtualPath);
            if (dataPath is null || !File.Exists(dataPath))
            {
                continue;
            }

            var keyVirtualPath = ChangeExtension(source.VirtualPath, ".tbl");
            var keyPath = ResolveBasePath(project.Paths, keyVirtualPath);
            result.Add(new SwShTextBaseSource(
                source.VirtualPath,
                source.Context,
                dataPath,
                keyPath is not null && File.Exists(keyPath) ? keyPath : null));
        }

        return result;
    }

    private static SwShTextCachedSource? TryParseEffectiveSource(
        OpenedProject project,
        TextFileSource source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var dataSource = ResolveWorkflowFile(project, source.VirtualPath);
            if (dataSource is null)
            {
                return null;
            }

            var textFile = SwShGameTextFile.Parse(File.ReadAllBytes(dataSource.AbsolutePath));
            var keys = TryLoadEffectiveMessageKeys(project, source.VirtualPath, textFile.Lines.Count, diagnostics);
            var lines = new SwShTextCachedLine[textFile.Lines.Count];
            for (var lineIndex = 0; lineIndex < textFile.Lines.Count; lineIndex++)
            {
                lines[lineIndex] = new SwShTextCachedLine(
                    textFile.Lines[lineIndex].Text,
                    lineIndex < keys.Count && !string.IsNullOrWhiteSpace(keys[lineIndex])
                        ? keys[lineIndex]
                        : null);
            }

            return new SwShTextCachedSource(source.VirtualPath, source.Context, lines);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Message table '{source.VirtualPath}' could not be decoded: {exception.Message}",
                file: source.VirtualPath,
                expected: "Sword/Shield encrypted text table"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Message table '{source.VirtualPath}' could not be read: {exception.Message}",
                file: source.VirtualPath,
                expected: "Readable Sword/Shield message table"));
        }

        return null;
    }

    private static IReadOnlyList<string> TryLoadEffectiveMessageKeys(
        OpenedProject project,
        string dataVirtualPath,
        int lineCount,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var keyVirtualPath = ChangeExtension(dataVirtualPath, ".tbl");
        var keySource = ResolveWorkflowFile(project, keyVirtualPath);
        if (keySource is null)
        {
            return [];
        }

        try
        {
            var keys = SwShAhtbFile.Parse(File.ReadAllBytes(keySource.AbsolutePath))
                .Entries
                .Select(entry => entry.Name)
                .ToArray();
            var hasExpectedSentinel = keys.Length == lineCount + 1
                && keys[^1].EndsWith("_max", StringComparison.OrdinalIgnoreCase);
            if (keys.Length != lineCount && !hasExpectedSentinel)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Message key table '{keyVirtualPath}' has {keys.Length} keys for {lineCount} editable lines. Available keys were used by line index.",
                    file: keyVirtualPath,
                    expected: $"{lineCount} keys, optionally followed by one *_max sentinel"));
            }

            return keys.Length <= lineCount ? keys : keys[..lineCount];
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Message key table '{keyVirtualPath}' could not be decoded: {exception.Message}",
                file: keyVirtualPath,
                expected: "Sword/Shield AHTB message-key table"));
            return [];
        }
    }

    private static SwShTextWorkflowQuery? NormalizeQuery(SwShTextWorkflowQuery? query)
    {
        if (query is null)
        {
            return null;
        }

        return new SwShTextWorkflowQuery(
            string.IsNullOrWhiteSpace(query.SearchText) ? null : query.SearchText.Trim(),
            Math.Max(0, query.Offset),
            Math.Clamp(query.Limit <= 0 ? DefaultQueryLimit : query.Limit, 1, MaximumQueryLimit),
            string.IsNullOrWhiteSpace(query.CategoryId) ? null : query.CategoryId.Trim(),
            string.IsNullOrWhiteSpace(query.Language) ? null : query.Language.Trim());
    }

    private static bool MatchesQuery(
        string? searchText,
        int textId,
        string? messageKey,
        string language,
        string sourceFile,
        string context,
        string label,
        int lineIndex,
        string value)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return sourceFile.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || textId.ToString(CultureInfo.InvariantCulture).Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || (messageKey?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false)
            || language.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || context.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || label.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || value.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || lineIndex.ToString(CultureInfo.InvariantCulture).Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddTextRecord(
        ICollection<SwShTextEntryRecord> entries,
        ICollection<SwShDialogueReferenceRecord> dialogueReferences,
        int textId,
        TextFileSource source,
        string context,
        int lineIndex,
        string value,
        string label,
        string? messageKey,
        SwShTextProvenance provenance)
    {
        entries.Add(new SwShTextEntryRecord(
            textId,
            CreateTextKey(source.VirtualPath, lineIndex),
            label,
            messageKey,
            source.Language,
            source.VirtualPath,
            lineIndex,
            value,
            CanEdit: true,
            EditBlockedReason: null,
            provenance));
        dialogueReferences.Add(new SwShDialogueReferenceRecord(
            CreateDialogueId(context, lineIndex),
            label,
            textId,
            context,
            CreatePreview(value),
            provenance));
    }

    internal static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        return sourcePath is null || !File.Exists(sourcePath)
            ? null
            : new WorkflowFileSource(entry, sourcePath, GetLanguage(entry.RelativePath) ?? "unknown");
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var normalizedRelativePath = targetRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, normalizedRelativePath));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);

        return PathContainment.IsWithinRoot(pathFromOutputRoot)
            ? SwShOutputRollbackScope.ResolvePhysicalContainedPath(outputRoot, targetRelativePath)
            : null;
    }

    internal static string CreateTextKey(string sourceFile, int lineIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        return string.Create(CultureInfo.InvariantCulture, $"{sourceFile}#{lineIndex}");
    }

    internal static bool TryParseTextKey(string? textKey, out string sourceFile, out int lineIndex)
    {
        sourceFile = string.Empty;
        lineIndex = -1;
        if (string.IsNullOrWhiteSpace(textKey))
        {
            return false;
        }

        var separatorIndex = textKey.LastIndexOf('#');
        if (separatorIndex <= 0 || separatorIndex == textKey.Length - 1)
        {
            return false;
        }

        sourceFile = textKey[..separatorIndex];
        return int.TryParse(
            textKey[(separatorIndex + 1)..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out lineIndex)
            && lineIndex >= 0;
    }

    internal static bool TryGetVirtualPathFromTextKey(
        string? textKey,
        out string virtualPath,
        out int lineIndex)
    {
        virtualPath = string.Empty;
        if (!TryParseTextKey(textKey, out var sourceFile, out lineIndex))
        {
            return false;
        }

        var normalized = sourceFile.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/');
        if (segments.Length != 6
            || !string.Equals(segments[0], "romfs", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "bin", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], "message", StringComparison.OrdinalIgnoreCase)
            || !SwShGameTextLanguage.SupportedMessageLanguages.Contains(
                segments[3],
                StringComparer.OrdinalIgnoreCase)
            || !(string.Equals(segments[4], "common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[4], "script", StringComparison.OrdinalIgnoreCase))
            || !segments[5].EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                || string.Equals(segment, ".", StringComparison.Ordinal)
                || string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            return false;
        }

        virtualPath = normalized;
        return true;
    }

    internal static string CreatePreview(string value)
    {
        const int maxPreviewLength = 96;
        var singleLine = value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return singleLine.Length <= maxPreviewLength ? singleLine : $"{singleLine[..maxPreviewLength]}...";
    }

    private static SwShTextWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShTextEntryRecord> entries,
        IReadOnlyList<SwShDialogueReferenceRecord> dialogueReferences,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        IReadOnlyList<SwShTextCategoryRecord> categories,
        string selectedCategoryId,
        string selectedLanguage,
        SwShTextResultPage? page,
        int sourceFileCount)
    {
        return new SwShTextWorkflow(
            summary,
            entries,
            dialogueReferences,
            EditableFields,
            categories,
            selectedCategoryId,
            Languages,
            selectedLanguage,
            page,
            new SwShTextWorkflowStats(entries.Count, dialogueReferences.Count, sourceFileCount),
            diagnostics);
    }

    private static IReadOnlyList<TextFileSource> ResolveMessageSources(
        TextSourceInventory inventory,
        string preferredLanguage,
        ICollection<ValidationDiagnostic> diagnostics,
        out string selectedLanguage)
    {
        var preferredSources = ResolveMessageSources(inventory, preferredLanguage, requireBaseSource: false);
        if (preferredSources.Count > 0)
        {
            selectedLanguage = preferredLanguage;
            return preferredSources;
        }

        var fallbackLanguages = SwShGameTextLanguage.SupportedMessageLanguages
            .Where(language => !string.Equals(language, preferredLanguage, StringComparison.OrdinalIgnoreCase))
            .OrderBy(language => string.Equals(language, PreferredLanguage, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(language => language, StringComparer.OrdinalIgnoreCase);
        foreach (var fallbackLanguage in fallbackLanguages)
        {
            var fallbackSources = ResolveMessageSources(inventory, fallbackLanguage, requireBaseSource: false);
            if (fallbackSources.Count == 0)
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"'{preferredLanguage}' message tables were not found; loaded '{fallbackLanguage}' message tables instead.",
                expected: $"{MessageRootPath}/{preferredLanguage}/{{common,script}}/*.dat"));
            selectedLanguage = fallbackLanguage;
            return fallbackSources;
        }

        selectedLanguage = preferredLanguage;
        return [];
    }

    private static IReadOnlyList<TextFileSource> ResolveMessageSources(
        TextSourceInventory inventory,
        string language,
        bool requireBaseSource)
    {
        var canonicalLanguage = SwShGameTextLanguage.SupportedMessageLanguages.FirstOrDefault(
            candidate => string.Equals(candidate, language, StringComparison.OrdinalIgnoreCase));
        if (canonicalLanguage is null
            || !inventory.SourcesByLanguage.TryGetValue(canonicalLanguage, out var sources))
        {
            return [];
        }

        return requireBaseSource
            ? sources.Where(source => source.Entry.BaseFile is not null).ToArray()
            : sources;
    }

    private TextSourceInventory GetSourceInventory(OpenedProject project)
    {
        lock (sourceInventorySyncRoot)
        {
            if (retainedSourceInventoryProjectId == project.Id
                && retainedSourceInventory is not null)
            {
                return retainedSourceInventory;
            }

            var inventory = CreateSourceInventory(project);
            retainedSourceInventoryProjectId = project.Id;
            retainedSourceInventory = inventory;
            return inventory;
        }
    }

    private static TextSourceInventory CreateSourceInventory(OpenedProject project)
    {
        var sourcesByLanguage = SwShGameTextLanguage.SupportedMessageLanguages.ToDictionary(
            language => language,
            _ => new List<TextFileSource>(),
            StringComparer.OrdinalIgnoreCase);
        var layeredKeyOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in project.FileGraph.Entries)
        {
            if (entry.LayeredFile is not null
                && entry.RelativePath.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase))
            {
                layeredKeyOverrides.Add(entry.RelativePath);
            }

            if (!entry.RelativePath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var discoveredLanguage = GetLanguage(entry.RelativePath);
            var language = SwShGameTextLanguage.SupportedMessageLanguages.FirstOrDefault(
                candidate => string.Equals(
                    candidate,
                    discoveredLanguage,
                    StringComparison.OrdinalIgnoreCase));
            if (language is null)
            {
                continue;
            }

            var context = GetLanguageRelativePath(entry.RelativePath, language);
            if (!IsSupportedMessageContext(context))
            {
                continue;
            }

            sourcesByLanguage[language].Add(new TextFileSource(
                entry,
                entry.RelativePath,
                language,
                context,
                ClassifySource(context)));
        }

        return new TextSourceInventory(
            sourcesByLanguage.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<TextFileSource>)pair.Value
                    .OrderBy(source => source.VirtualPath, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase),
            layeredKeyOverrides);
    }

    private static IReadOnlyList<SwShTextCategoryRecord> CreateCategories(
        IReadOnlyList<TextFileSource> sources)
    {
        var categories = new List<SwShTextCategoryRecord>(CategoryDefinitions.Count + 1)
        {
            new(
                AllCategoryId,
                "All",
                "Every editable message table in the selected game-text language.",
                sources.Count),
        };
        categories.AddRange(CategoryDefinitions.Select(definition => new SwShTextCategoryRecord(
            definition.CategoryId,
            definition.Label,
            definition.Description,
            sources.Count(source => string.Equals(
                source.CategoryId,
                definition.CategoryId,
                StringComparison.Ordinal)))));
        return categories;
    }

    private static string ResolveSelectedCategoryId(
        string? requestedCategoryId,
        IReadOnlyList<SwShTextCategoryRecord> categories)
    {
        if (!string.IsNullOrWhiteSpace(requestedCategoryId))
        {
            var category = categories.FirstOrDefault(candidate => string.Equals(
                candidate.CategoryId,
                requestedCategoryId,
                StringComparison.OrdinalIgnoreCase));
            if (category is not null)
            {
                return category.CategoryId;
            }
        }

        return AllCategoryId;
    }

    private static string ClassifySource(string context)
    {
        var normalized = context.Replace('\\', '/').TrimStart('/');
        if (StartsWithFileFamily(normalized, "script/main_event_")
            || StartsWithFileFamily(normalized, "script/demo_"))
        {
            return MainStoryCategoryId;
        }

        if (StartsWithFileFamily(normalized, "script/sub_event_"))
        {
            return SideEventsCategoryId;
        }

        if (StartsWithFileFamily(normalized, "script/rigel1_")
            || StartsWithFileFamily(normalized, "script/rigel_other_jizen_")
            || string.Equals(normalized, "script/rigel_other_station.dat", StringComparison.OrdinalIgnoreCase)
            || IsleOfArmorSources.Contains(normalized))
        {
            return IsleOfArmorCategoryId;
        }

        if (StartsWithFileFamily(normalized, "script/rigel2_")
            || StartsWithFileFamily(normalized, "script/rigel_other_gst_")
            || string.Equals(normalized, "script/rigel_other_startournament.dat", StringComparison.OrdinalIgnoreCase)
            || CrownTundraSources.Contains(normalized))
        {
            return CrownTundraCategoryId;
        }

        if (StartsWithFileFamily(normalized, "script/z_") || FieldWorldSources.Contains(normalized))
        {
            return FieldWorldCategoryId;
        }

        if (BattleSources.Contains(normalized))
        {
            return BattlesCategoryId;
        }

        if (ItemSources.Contains(normalized))
        {
            return ItemsCategoryId;
        }

        if (PokemonPokedexSources.Contains(normalized))
        {
            return PokemonPokedexCategoryId;
        }

        if (MoveAbilitySources.Contains(normalized))
        {
            return MovesAbilitiesCategoryId;
        }

        if (TrainerCharacterSources.Contains(normalized))
        {
            return TrainersCharactersCategoryId;
        }

        if (LocationSources.Contains(normalized))
        {
            return LocationsCategoryId;
        }

        if (FacilityActivitySources.Contains(normalized))
        {
            return FacilitiesActivitiesCategoryId;
        }

        if (normalized.StartsWith("common/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "script/common_scr.dat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "script/tutorial.dat", StringComparison.OrdinalIgnoreCase))
        {
            return UiOnlineSharedCategoryId;
        }

        return OtherScriptsCategoryId;
    }

    private static bool StartsWithFileFamily(string context, string prefix)
    {
        return context.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && context.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> CreateSourceSet(params string[] sources)
    {
        return new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSupportedMessageContext(string context)
    {
        var normalized = context.Replace('\\', '/');
        var separator = normalized.IndexOf('/');
        if (separator <= 0 || normalized.IndexOf('/', separator + 1) >= 0)
        {
            return false;
        }

        var folder = normalized[..separator];
        return (string.Equals(folder, "common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(folder, "script", StringComparison.OrdinalIgnoreCase))
            && normalized.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTextLabel(string context, int lineIndex, string? messageKey)
    {
        return !string.IsNullOrWhiteSpace(messageKey) && !IsOpaqueMessageKey(messageKey)
            ? messageKey
            : $"{Path.GetFileNameWithoutExtension(context)} #{lineIndex}";
    }

    private static bool IsOpaqueMessageKey(string messageKey)
    {
        const string prefix = "msg_";
        return messageKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && messageKey.Length == prefix.Length + 32
            && messageKey.AsSpan(prefix.Length).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
    }

    private static string GetLanguageLabel(string language)
    {
        return language switch
        {
            "English" => "English",
            "Spanish" => "Español",
            "French" => "Français",
            "German" => "Deutsch",
            "Italian" => "Italiano",
            "JPN" => "日本語（かな）",
            "JPN_KANJI" => "日本語（漢字）",
            "Korean" => "한국어",
            "Simp_Chinese" => "简体中文",
            "Trad_Chinese" => "繁體中文",
            _ => language,
        };
    }

    private static string GetLanguageRelativePath(string relativePath, string language)
    {
        var prefix = $"{MessageRootPath}/{language}/";
        return relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? relativePath[prefix.Length..]
            : relativePath;
    }

    private static string? GetLanguage(string relativePath)
    {
        if (!relativePath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var languageStart = MessageRootPath.Length + 1;
        var nextSeparator = relativePath.IndexOf('/', languageStart);
        return nextSeparator < 0 ? null : relativePath[languageStart..nextSeparator];
    }

    private static string ChangeExtension(string virtualPath, string extension)
    {
        return Path.ChangeExtension(virtualPath, extension)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        return entry.BaseFile is not null ? ResolveBasePath(paths, entry.RelativePath) : null;
    }

    private static string? ResolveBasePath(ProjectPaths paths, string relativePath)
    {
        return relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            ? CombineGraphPath(paths.BaseRomFsPath, relativePath["romfs/".Length..])
            : null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        return string.IsNullOrWhiteSpace(rootPath)
            ? null
            : Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShTextProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        return new SwShTextProvenance(
            entry.RelativePath,
            entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
            entry.State);
    }

    private static string CreateDialogueId(string context, int lineIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.ChangeExtension(context, null)?.Replace('\\', '/')}:{lineIndex}");
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Text,
            "Text and Dialogue Map",
            "Text entries, dialogue references, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: "workflow.text",
            Field: field,
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry Entry,
        string AbsolutePath,
        string Language);

    internal sealed record SwShTextCacheWarmupTarget(
        string Language,
        string CategoryId);

    private sealed record TextCategoryDefinition(
        string CategoryId,
        string Label,
        string Description);

    private sealed record TextFileSource(
        ProjectFileGraphEntry Entry,
        string VirtualPath,
        string Language,
        string Context,
        string CategoryId);

    private sealed record TextSourceInventory(
        IReadOnlyDictionary<string, IReadOnlyList<TextFileSource>> SourcesByLanguage,
        IReadOnlySet<string> LayeredKeyOverrides);
}
