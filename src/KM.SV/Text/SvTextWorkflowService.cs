// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.Formats.SwSh;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.Text;

public sealed class SvTextWorkflowService
{
    public const string MessageRootPath = SvMessagePathResolver.MessageRootPath;
    public const string WorkflowLabel = "Text and Dialogue Map";
    public const string WorkflowDescription = "Text entries, dialogue references, and source provenance.";
    public const string TextValueField = "value";
    public const int MaximumTextLength = 4096;
    public const int DefaultQueryLimit = 500;
    public const int MaximumQueryLimit = 1000;

    private const string AllCategoryId = "all";
    private const string MainStoryCategoryId = "sv-main-story";
    private const string SideEventsSchoolCategoryId = "sv-side-events-school";
    private const string TealMaskCategoryId = "sv-teal-mask";
    private const string IndigoDiskCategoryId = "sv-indigo-disk";
    private const string FieldWorldCategoryId = "sv-field-world";
    private const string BattlesCategoryId = "sv-battles";
    private const string ItemsCategoryId = "sv-items";
    private const string PokemonPokedexCategoryId = "sv-pokemon-pokedex";
    private const string MovesAbilitiesCategoryId = "sv-moves-abilities";
    private const string TrainersCharactersCategoryId = "sv-trainers-characters";
    private const string LocationsCategoryId = "sv-locations";
    private const string FacilitiesActivitiesCategoryId = "sv-facilities-activities";
    private const string UiOnlineSharedCategoryId = "sv-ui-online-shared";
    private const string OtherScriptsCategoryId = "sv-other-scripts";

    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };

    private static readonly IReadOnlyList<TextCategoryDefinition> CategoryDefinitions =
    [
        new(MainStoryCategoryId, "Main Story", "Base-game story, Gym, Titan, Team Star, Champion, and legendary story text."),
        new(SideEventsSchoolCategoryId, "Side Events and School", "Side events, classes, friendship stories, and school dialogue."),
        new(TealMaskCategoryId, "The Teal Mask", "Kitakami story, events, activities, and related DLC text."),
        new(IndigoDiskCategoryId, "The Indigo Disk", "Blueberry Academy story, coaches, clubs, missions, and related DLC text."),
        new(FieldWorldCategoryId, "Field and World", "Area, town, route, field-event, fishing, lighthouse, and ambient world text."),
        new(BattlesCategoryId, "Battles", "Battle messages, raids, competitions, regulations, teams, and online battle text."),
        new(ItemsCategoryId, "Items", "Item names and descriptions, bag pockets, field items, and related text."),
        new(PokemonPokedexCategoryId, "Pokemon and Pokedex", "Pokemon names, Pokedex and habitat text, storage, status, evolution, and memories."),
        new(MovesAbilitiesCategoryId, "Moves and Abilities", "Move and Ability names, descriptions, learning, remembering, and forgetting text."),
        new(TrainersCharactersCategoryId, "Trainers and Characters", "Trainer names, classes, dialogue labels, staff, and character names."),
        new(LocationsCategoryId, "Locations", "Place, area, school map, mission, and world map location labels."),
        new(FacilitiesActivitiesCategoryId, "Facilities and Activities", "Picnics, shops, trades, customization, photos, restaurants, and other services."),
        new(UiOnlineSharedCategoryId, "UI, Online, and Shared", "Shared menus, prompts, networking, system messages, map UI, and interface text."),
        new(OtherScriptsCategoryId, "Other Scripts", "Internal or newly discovered script tables without a narrower proven owner."),
    ];

    private static readonly HashSet<string> MainStoryExactSources = CreateSourceSet(
        "script/3poke_walk.dat",
        "script/dan.dat",
        "script/futatsuna.dat",
        "script/stopper.dat");

    private static readonly HashSet<string> SideEventsExactSources = CreateSourceSet(
        "script/sch_entrance01.dat");

    private static readonly HashSet<string> TealMaskSources = CreateSourceSet(
        "script/kitakami_center.dat",
        "common/oniballoon_ingame.dat",
        "common/oniballoon_matching.dat");

    private static readonly HashSet<string> IndigoDiskSources = CreateSourceSet(
        "script/bbmission_event.dat",
        "script/bbschool.dat",
        "script/clubroom_pc_event.dat",
        "script/dome.dat",
        "script/pair_talk.dat",
        "script/synchromachine.dat",
        "common/club_bbmission.dat",
        "common/club_itemmachine.dat",
        "common/clubroom_bgm.dat",
        "common/clubroom_pc.dat");

    private static readonly HashSet<string> FieldWorldSources = CreateSourceSet(
        "script/c01.dat",
        "script/c02.dat",
        "script/c03.dat",
        "script/t01.dat",
        "script/t02.dat",
        "script/t03.dat",
        "script/t04.dat",
        "script/t05.dat",
        "script/t06.dat",
        "script/t07.dat",
        "script/t08.dat",
        "script/t09.dat",
        "script/t10.dat",
        "script/bg_event.dat",
        "script/field_event.dat",
        "script/fishing.dat",
        "script/light_house.dat",
        "script/other_area.dat",
        "script/road_01.dat");

    private static readonly HashSet<string> BattleSources = CreateSourceSet(
        "common/btl_app.dat",
        "common/btl_attack.dat",
        "common/btl_bgm_select.dat",
        "common/btl_dan.dat",
        "common/btl_pokelist.dat",
        "common/btl_pokeselect.dat",
        "common/btl_set.dat",
        "common/btl_state.dat",
        "common/btl_std.dat",
        "common/btl_team.dat",
        "common/btlspot.dat",
        "common/competition_organize.dat",
        "common/lastbattle.dat",
        "common/net_btl.dat",
        "common/raid_list.dat",
        "common/raid_matching.dat",
        "common/regulation.dat",
        "common/rental_team.dat",
        "common/result.dat",
        "common/vs_demo.dat",
        "script/btl_talk.dat",
        "script/rankmatch.dat");

    private static readonly HashSet<string> ItemSources = CreateSourceSet(
        "common/bag.dat",
        "common/bag_pocket.dat",
        "common/dressup_item_name.dat",
        "common/hud_itemget.dat",
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
        "common/bunpu_comment_A.dat",
        "common/bunpu_comment_B.dat",
        "common/bunpu_comment_dlc1_A.dat",
        "common/bunpu_comment_dlc1_B.dat",
        "common/bunpu_comment_dlc2_A.dat",
        "common/bunpu_comment_dlc2_B.dat",
        "common/event_pokename.dat",
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
        "common/zukan_comment_A.dat",
        "common/zukan_comment_B.dat",
        "script/change_deox_scr.dat",
        "script/change_rotom_scr.dat",
        "script/change_torimian_scr.dat",
        "script/fossil_scr.dat",
        "script/poke_memory_feeling.dat",
        "script/poke_memory_place.dat",
        "script/poke_memory_rank.dat");

    private static readonly HashSet<string> MoveAbilitySources = CreateSourceSet(
        "common/gwazainfo.dat",
        "common/gwazamessage.dat",
        "common/gwazaname.dat",
        "common/tokusei.dat",
        "common/tokuseiinfo.dat",
        "common/waza_remember.dat",
        "common/waza_wasure.dat",
        "common/wazainfo.dat",
        "common/wazaname.dat",
        "script/hidden_power.dat",
        "script/poke_waza.dat");

    private static readonly HashSet<string> TrainerCharacterSources = CreateSourceSet(
        "common/another_name.dat",
        "common/namelist.dat",
        "common/staff_list.dat",
        "common/trmsg.dat",
        "common/trname.dat",
        "common/trtype.dat");

    private static readonly HashSet<string> LocationSources = CreateSourceSet(
        "common/hud_areaname.dat",
        "common/place_name.dat",
        "common/place_name_indirect.dat",
        "common/place_name_out.dat",
        "common/place_name_per.dat",
        "common/place_name_spe.dat",
        "common/schoolmap.dat",
        "common/ymap_mission_place_name.dat",
        "common/ymap_place_name.dat",
        "common/ymap_point_name.dat",
        "common/ymap_town_facility.dat");

    private static readonly HashSet<string> FacilityActivitySources = CreateSourceSet(
        "common/dressup.dat",
        "common/emote.dat",
        "common/emotename.dat",
        "common/food_power.dat",
        "common/gymtest.dat",
        "common/leaguecard.dat",
        "common/mystery.dat",
        "common/mystery_card.dat",
        "common/photomode.dat",
        "common/pokepicnic_cookinfo.dat",
        "common/pokepicnic_cooking.dat",
        "common/pokepicnic_cookname.dat",
        "common/pokepicnic_main.dat",
        "common/pokepicnic_trainer.dat",
        "common/pokepicnic_wash.dat",
        "common/restaurant_menu.dat",
        "common/shop_restaurant.dat",
        "script/elevator.dat",
        "script/field_trade.dat",
        "script/hairsalon.dat",
        "script/id_present_scr.dat",
        "script/my_room.dat",
        "script/pokemoncenter.dat",
        "script/shop.dat",
        "script/shop_waza.dat",
        "script/vending_machine.dat");

    private static readonly HashSet<string> UiOnlineSharedSources = CreateSourceSet(
        "common/app_common.dat",
        "common/appli_header.dat",
        "common/common_text.dat",
        "common/dlc.dat",
        "common/dlc_group.dat",
        "common/event_skip.dat",
        "common/gameover.dat",
        "common/hud.dat",
        "common/hud_announce.dat",
        "common/hud_buttonguide.dat",
        "common/hud_info.dat",
        "common/hud_minimap.dat",
        "common/hud_net.dat",
        "common/hud_notice.dat",
        "common/illegalname.dat",
        "common/initial.dat",
        "common/language_select.dat",
        "common/message_error.dat",
        "common/money_window.dat",
        "common/net_save.dat",
        "common/net_topmenu.dat",
        "common/netconnect.dat",
        "common/network_common.dat",
        "common/option.dat",
        "common/original_book.dat",
        "common/player_select.dat",
        "common/strinput.dat",
        "common/team_circle.dat",
        "common/tips.dat",
        "common/title_backup.dat",
        "common/title_menu.dat",
        "common/tokkun.dat",
        "common/xmenu.dat",
        "common/ymap_main.dat",
        "common/ymap_mission_add.dat",
        "common/ymap_mission_character.dat",
        "common/ymap_mission_character_title.dat",
        "common/ymap_mission_guide.dat",
        "common/ymap_mission_reward.dat",
        "common/ymap_mission_title.dat",
        "common/ymap_title.dat",
        "common/ymap_topmenu.dat",
        "script/common_scr.dat",
        "script/popup.dat");

    private static readonly IReadOnlyList<SvTextEditableField> EditableFields =
    [
        new SvTextEditableField(TextValueField, "Text value", "multilineText", 0, MaximumTextLength),
    ];

    private static readonly IReadOnlyList<SvTextLanguageRecord> Languages =
        SvGameTextLanguage.SupportedMessageLanguages
            .Select(language => new SvTextLanguageRecord(language, GetLanguageLabel(language)))
            .ToArray();

    private readonly SvWorkflowFileSource fileSource;
    private readonly SvTextCacheStore cacheStore;
    private readonly object sourceInventorySyncRoot = new();
    private ProjectId? retainedSourceInventoryProjectId;
    private TextSourceInventory? retainedSourceInventory;

    internal SvTextWorkflowService(
        SvWorkflowFileSource? fileSource = null,
        SvCacheManager? cacheManager = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource(cacheManager);
        cacheStore = new SvTextCacheStore(this.fileSource, cacheManager);
    }

    public static IReadOnlyList<SvTextLanguageRecord> SupportedLanguages => Languages;

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Text,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvTextWorkflow Load(OpenedProject project, SvTextWorkflowQuery? query = null)
    {
        return LoadCore(project, query, unpagedLanguage: null);
    }

    internal SvTextWorkflow LoadUnpaged(OpenedProject project, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return LoadCore(project, query: null, unpagedLanguage: language);
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

    private SvTextWorkflow LoadCore(
        OpenedProject project,
        SvTextWorkflowQuery? query,
        string? unpagedLanguage)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var normalizedQuery = NormalizeQuery(query);
        var requestedLanguage = SvGameTextLanguage.Resolve(
            unpagedLanguage ?? normalizedQuery?.Language ?? project.Paths.GameTextLanguage);
        if (summary.Availability == SvWorkflowAvailability.Disabled)
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
                "Text and Dialogue Map did not find any Scarlet/Violet message tables.",
                expected: $"{MessageRootPath}/{SvGameTextLanguage.English}/{{common,script}}/*.dat"));
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
        var loadedCategories = new Dictionary<string, IReadOnlyDictionary<string, LoadedTextSource>>(
            StringComparer.Ordinal);
        var entries = new List<SvTextEntryRecord>();
        var dialogueReferences = new List<SvDialogueReferenceRecord>();
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
                    inventory,
                    diagnostics);
                loadedCategories.Add(source.CategoryId, loadedSources);
            }

            if (!loadedSources.TryGetValue(source.VirtualPath, out var parsedSource))
            {
                continue;
            }

            for (var lineIndex = 0; lineIndex < parsedSource.Source.Lines.Count; lineIndex++)
            {
                var line = parsedSource.Source.Lines[lineIndex];
                var textId = scannedTextEntryCount++;
                var label = CreateTextLabel(parsedSource.Source.Context, lineIndex, line.MessageKey);
                if (normalizedQuery is not null)
                {
                    if (!MatchesQuery(
                        normalizedQuery.SearchText,
                        textId,
                        line.MessageKey,
                        selectedLanguage,
                        CreateRelativePath(source.VirtualPath),
                        parsedSource.Source.Context,
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
                    parsedSource.Source.Context,
                    lineIndex,
                    line.Value,
                    label,
                    line.MessageKey,
                    parsedSource.Provenance);
            }

            if (stopScanning)
            {
                break;
            }
        }

        var page = normalizedQuery is null
            ? null
            : new SvTextResultPage(
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

    private IReadOnlyDictionary<string, LoadedTextSource> LoadEffectiveCategory(
        OpenedProject project,
        string language,
        string categoryId,
        IReadOnlyList<TextFileSource> sources,
        TextSourceInventory inventory,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, LoadedTextSource>(StringComparer.OrdinalIgnoreCase);
        var baseSources = sources
            .Where(source => source.HasBaseSource)
            .Select(source => new SvTextBaseSource(source.VirtualPath, source.Context))
            .ToArray();
        if (baseSources.Length > 0)
        {
            var cached = cacheStore.LoadBaseCategory(project, language, categoryId, baseSources);
            foreach (var diagnostic in cached.Diagnostics)
            {
                diagnostics.Add(diagnostic);
            }

            foreach (var source in cached.Sources)
            {
                result[source.VirtualPath] = new LoadedTextSource(
                    source,
                    CreateBaseProvenance(source.VirtualPath));
            }
        }

        foreach (var source in sources)
        {
            var keyPath = ChangeExtension(source.VirtualPath, ".tbl");
            if (!inventory.LayeredOverrides.Contains(source.VirtualPath)
                && !inventory.LayeredOverrides.Contains(keyPath)
                && !inventory.PackedOutputOverrides.Contains(source.VirtualPath)
                && !inventory.PackedOutputOverrides.Contains(keyPath)
                && result.ContainsKey(source.VirtualPath))
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

    private LoadedTextSource? TryParseEffectiveSource(
        OpenedProject project,
        TextFileSource source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var dataFile = fileSource.Read(project, source.VirtualPath);
            var textFile = SwShGameTextFile.Parse(dataFile.Bytes);
            var keys = TryLoadEffectiveMessageKeys(project, source.VirtualPath, textFile.Lines.Count, diagnostics);
            var lines = new SvTextCachedLine[textFile.Lines.Count];
            for (var lineIndex = 0; lineIndex < textFile.Lines.Count; lineIndex++)
            {
                lines[lineIndex] = new SvTextCachedLine(
                    textFile.Lines[lineIndex].Text,
                    lineIndex < keys.Count && !string.IsNullOrWhiteSpace(keys[lineIndex])
                        ? keys[lineIndex]
                        : null);
            }

            return new LoadedTextSource(
                new SvTextCachedSource(source.VirtualPath, source.Context, lines),
                CreateProvenance(dataFile));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Message table 'romfs/{source.VirtualPath}' could not be decoded: {exception.Message}",
                file: $"romfs/{source.VirtualPath}",
                expected: "Scarlet/Violet encrypted text table"));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Message table 'romfs/{source.VirtualPath}' could not be read: {exception.Message}",
                file: $"romfs/{source.VirtualPath}",
                expected: "Readable Scarlet/Violet message table"));
        }

        return null;
    }

    private IReadOnlyList<string> TryLoadEffectiveMessageKeys(
        OpenedProject project,
        string dataVirtualPath,
        int lineCount,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var keyVirtualPath = ChangeExtension(dataVirtualPath, ".tbl");
        try
        {
            var keys = SwShAhtbFile.Parse(fileSource.Read(project, keyVirtualPath).Bytes)
                .Entries
                .Select(entry => entry.Name)
                .ToArray();
            var hasExpectedSentinel = keys.Length == lineCount + 1
                && keys[^1].EndsWith("_max", StringComparison.OrdinalIgnoreCase);
            if (keys.Length != lineCount && !hasExpectedSentinel)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Message key table 'romfs/{keyVirtualPath}' has {keys.Length} keys for {lineCount} editable lines. Available keys were used by line index.",
                    file: $"romfs/{keyVirtualPath}",
                    expected: $"{lineCount} keys, optionally followed by one *_max sentinel"));
            }

            return keys.Length <= lineCount ? keys : keys[..lineCount];
        }
        catch (Exception exception) when (IsMissingFile(exception))
        {
            return [];
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Message key table 'romfs/{keyVirtualPath}' could not be decoded: {exception.Message}",
                file: $"romfs/{keyVirtualPath}",
                expected: "Scarlet/Violet AHTB message-key table"));
            return [];
        }
    }

    internal bool TryLoadEntry(
        OpenedProject project,
        string? textKey,
        ICollection<ValidationDiagnostic> diagnostics,
        out SvTextEntryRecord? entry)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);
        entry = null;
        if (!TryGetVirtualPathFromTextKey(textKey, out var virtualPath, out var lineIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending S/V text edit does not include a safe supported source path.",
                field: "textKey",
                expected: "message/dat/{language}/{common|script}/file.dat#line"));
            return false;
        }

        try
        {
            var dataFile = fileSource.Read(project, virtualPath);
            var textFile = SwShGameTextFile.Parse(dataFile.Bytes);
            if (lineIndex >= textFile.Lines.Count)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending S/V text edit targets a line outside the source table.",
                    field: "textKey",
                    expected: "Existing text line",
                    file: dataFile.RelativePath));
                return false;
            }

            var language = GetLanguage(virtualPath)!;
            var context = GetLanguageRelativePath(virtualPath, language);
            var keys = TryLoadEffectiveMessageKeys(project, virtualPath, textFile.Lines.Count, diagnostics);
            var messageKey = lineIndex < keys.Count && !string.IsNullOrWhiteSpace(keys[lineIndex])
                ? keys[lineIndex]
                : null;
            var label = CreateTextLabel(context, lineIndex, messageKey);
            entry = new SvTextEntryRecord(
                lineIndex,
                CreateTextKey(dataFile.RelativePath, lineIndex),
                label,
                messageKey,
                language,
                dataFile.RelativePath,
                lineIndex,
                textFile.Lines[lineIndex].Text,
                CanEdit: true,
                EditBlockedReason: null,
                CreateProvenance(dataFile));
            return true;
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"S/V text source file could not be decoded: {exception.Message}",
                file: $"romfs/{virtualPath}",
                expected: "Scarlet/Violet encrypted text table"));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"S/V text source file could not be read: {exception.Message}",
                file: $"romfs/{virtualPath}",
                expected: "Readable Scarlet/Violet message table"));
        }

        return false;
    }

    private static SvTextWorkflowQuery? NormalizeQuery(SvTextWorkflowQuery? query)
    {
        if (query is null)
        {
            return null;
        }

        return new SvTextWorkflowQuery(
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
        ICollection<SvTextEntryRecord> entries,
        ICollection<SvDialogueReferenceRecord> dialogueReferences,
        int textId,
        TextFileSource source,
        string context,
        int lineIndex,
        string value,
        string label,
        string? messageKey,
        SvTextProvenance provenance)
    {
        var relativePath = CreateRelativePath(source.VirtualPath);
        entries.Add(new SvTextEntryRecord(
            textId,
            CreateTextKey(relativePath, lineIndex),
            label,
            messageKey,
            source.Language,
            relativePath,
            lineIndex,
            value,
            CanEdit: true,
            EditBlockedReason: null,
            provenance));
        dialogueReferences.Add(new SvDialogueReferenceRecord(
            CreateDialogueId(context, lineIndex),
            label,
            textId,
            context,
            CreatePreview(value),
            provenance));
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
        if (normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["romfs/".Length..];
        }

        var segments = normalized.Split('/');
        if (segments.Length != 5
            || !string.Equals(segments[0], "message", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "dat", StringComparison.OrdinalIgnoreCase)
            || !SvGameTextLanguage.SupportedMessageLanguages.Contains(
                segments[2],
                StringComparer.OrdinalIgnoreCase)
            || !(string.Equals(segments[3], "common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[3], "script", StringComparison.OrdinalIgnoreCase))
            || segments[4].Length <= ".dat".Length
            || !segments[4].EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
            || segments[4].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || segments[4].Any(char.IsControl)
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                || string.Equals(segment, ".", StringComparison.Ordinal)
                || string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            return false;
        }

        var canonicalLanguage = SvGameTextLanguage.SupportedMessageLanguages.First(language =>
            string.Equals(language, segments[2], StringComparison.OrdinalIgnoreCase));
        virtualPath = $"{MessageRootPath}/{canonicalLanguage}/{segments[3]}/{segments[4]}";
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

    private static SvTextWorkflow CreateWorkflow(
        SvWorkflowSummary summary,
        IReadOnlyList<SvTextEntryRecord> entries,
        IReadOnlyList<SvDialogueReferenceRecord> dialogueReferences,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        IReadOnlyList<SvTextCategoryRecord> categories,
        string selectedCategoryId,
        string selectedLanguage,
        SvTextResultPage? page,
        int sourceFileCount)
    {
        return new SvTextWorkflow(
            summary,
            entries,
            dialogueReferences,
            EditableFields,
            categories,
            selectedCategoryId,
            Languages,
            selectedLanguage,
            page,
            new SvTextWorkflowStats(entries.Count, dialogueReferences.Count, sourceFileCount),
            diagnostics);
    }

    private static IReadOnlyList<TextFileSource> ResolveMessageSources(
        TextSourceInventory inventory,
        string preferredLanguage,
        ICollection<ValidationDiagnostic> diagnostics,
        out string selectedLanguage)
    {
        var preferredSources = ResolveMessageSources(inventory, preferredLanguage);
        if (preferredSources.Count > 0)
        {
            selectedLanguage = preferredLanguage;
            return preferredSources;
        }

        var fallbackLanguages = SvGameTextLanguage.SupportedMessageLanguages
            .Where(language => !string.Equals(language, preferredLanguage, StringComparison.OrdinalIgnoreCase))
            .OrderBy(language => string.Equals(language, SvGameTextLanguage.English, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(language => language, StringComparer.OrdinalIgnoreCase);
        foreach (var fallbackLanguage in fallbackLanguages)
        {
            var fallbackSources = ResolveMessageSources(inventory, fallbackLanguage);
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
        string language)
    {
        var canonicalLanguage = SvGameTextLanguage.SupportedMessageLanguages.FirstOrDefault(candidate =>
            string.Equals(candidate, language, StringComparison.OrdinalIgnoreCase));
        return canonicalLanguage is not null
            && inventory.SourcesByLanguage.TryGetValue(canonicalLanguage, out var sources)
                ? sources
                : [];
    }

    private TextSourceInventory GetSourceInventory(OpenedProject project)
    {
        lock (sourceInventorySyncRoot)
        {
            if (retainedSourceInventoryProjectId == project.Id && retainedSourceInventory is not null)
            {
                return retainedSourceInventory;
            }

            var inventory = CreateSourceInventory(project);
            retainedSourceInventoryProjectId = project.Id;
            retainedSourceInventory = inventory;
            return inventory;
        }
    }

    private TextSourceInventory CreateSourceInventory(OpenedProject project)
    {
        var basePackNames = fileSource.ListBasePackNames(project);
        var outputArchive = fileSource.GetOutputArchiveInventory(project);
        var outputPackNames = outputArchive?.PackNames ?? [];
        var sourcesByLanguage = new Dictionary<string, IReadOnlyList<TextFileSource>>(
            StringComparer.OrdinalIgnoreCase);
        var layeredOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packedOutputOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var language in SvGameTextLanguage.SupportedMessageLanguages)
        {
            var baseCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddPackMessageCandidates(baseCandidates, basePackNames, language);
            AddLooseMessageCandidates(baseCandidates, null, project.Paths.BaseRomFsPath, language);

            var effectiveCandidates = new HashSet<string>(baseCandidates, StringComparer.OrdinalIgnoreCase);
            AddPackMessageCandidates(effectiveCandidates, outputPackNames, language);
            if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
            {
                AddLooseMessageCandidates(
                    effectiveCandidates,
                    layeredOverrides,
                    project.Paths.OutputRootPath,
                    language);
                AddLooseMessageCandidates(
                    effectiveCandidates,
                    layeredOverrides,
                    Path.Combine(project.Paths.OutputRootPath, "romfs"),
                    language);
            }

            if (outputArchive is not null)
            {
                foreach (var path in effectiveCandidates)
                {
                    if (outputArchive.FileHashes.Contains(SvTrinityPathHasher.HashPath(path)))
                    {
                        packedOutputOverrides.Add(path);
                    }

                    var keyPath = ChangeExtension(path, ".tbl");
                    if (outputArchive.FileHashes.Contains(SvTrinityPathHasher.HashPath(keyPath)))
                    {
                        packedOutputOverrides.Add(keyPath);
                    }
                }
            }

            sourcesByLanguage.Add(
                language,
                effectiveCandidates
                    .Select(path => new TextFileSource(
                        path,
                        language,
                        GetLanguageRelativePath(path, language),
                        ClassifySource(GetLanguageRelativePath(path, language)),
                        baseCandidates.Contains(path)))
                    .Where(source => IsSupportedMessageContext(source.Context))
                    .OrderBy(source => source.VirtualPath, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        return new TextSourceInventory(
            sourcesByLanguage,
            layeredOverrides,
            packedOutputOverrides);
    }

    private static void AddPackMessageCandidates(
        ISet<string> candidates,
        IReadOnlyList<string> packNames,
        string language)
    {
        foreach (var packName in packNames)
        {
            var virtualPath = SvMessagePathResolver.TryCreateMessageDatPathFromPackName(packName, language);
            if (!string.IsNullOrWhiteSpace(virtualPath))
            {
                candidates.Add(virtualPath);
            }
        }
    }

    private static void AddLooseMessageCandidates(
        ISet<string> candidates,
        ISet<string>? overrides,
        string? romFsRootPath,
        string language)
    {
        if (string.IsNullOrWhiteSpace(romFsRootPath))
        {
            return;
        }

        var messageRoot = Path.Combine(
            romFsRootPath,
            MessageRootPath.Replace('/', Path.DirectorySeparatorChar),
            language);
        if (!Directory.Exists(messageRoot))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(messageRoot, "*.*", RecursiveEnumeration))
        {
            var extension = Path.GetExtension(filePath);
            if (!string.Equals(extension, ".dat", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".tbl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativeToRoot = Path.GetRelativePath(romFsRootPath, filePath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            if (!relativeToRoot.StartsWith($"{MessageRootPath}/{language}/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            overrides?.Add(relativeToRoot);
            candidates.Add(ChangeExtension(relativeToRoot, ".dat"));
        }
    }

    private static IReadOnlyList<SvTextCategoryRecord> CreateCategories(
        IReadOnlyList<TextFileSource> sources)
    {
        var categories = new List<SvTextCategoryRecord>(CategoryDefinitions.Count + 1)
        {
            new(
                AllCategoryId,
                "All",
                "Every editable message table in the selected game-text language.",
                sources.Count),
        };
        categories.AddRange(CategoryDefinitions.Select(definition => new SvTextCategoryRecord(
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
        IReadOnlyList<SvTextCategoryRecord> categories)
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

    internal static string ClassifySource(string context)
    {
        var normalized = context.Replace('\\', '/').TrimStart('/');
        if (string.Equals(normalized, "script/school_debug.dat", StringComparison.OrdinalIgnoreCase))
        {
            return OtherScriptsCategoryId;
        }

        if (StartsWithNumberedFileFamily(normalized, "script/common_")
            || StartsWithFileFamily(normalized, "script/champ_")
            || StartsWithFileFamily(normalized, "script/gym_")
            || StartsWithFileFamily(normalized, "script/nushi_")
            || StartsWithFileFamily(normalized, "script/legend_")
            || StartsWithFileFamily(normalized, "script/team_")
            || StartsWithFileFamily(normalized, "script/ajito_")
            || StartsWithFileFamily(normalized, "script/atlantis_")
            || MainStoryExactSources.Contains(normalized))
        {
            return MainStoryCategoryId;
        }

        if (StartsWithFileFamily(normalized, "script/sub_")
            || StartsWithFileFamily(normalized, "script/class_")
            || StartsWithFileFamily(normalized, "script/kizuna_")
            || StartsWithFileFamily(normalized, "script/school_")
            || SideEventsExactSources.Contains(normalized))
        {
            return SideEventsSchoolCategoryId;
        }

        if (StartsWithFileFamily(normalized, "script/sdc01_")
            || StartsWithFileFamily(normalized, "script/s1_")
            || TealMaskSources.Contains(normalized))
        {
            return TealMaskCategoryId;
        }

        if (StartsWithFileFamily(normalized, "script/sdc02_")
            || StartsWithFileFamily(normalized, "script/s2_")
            || StartsWithFileFamily(normalized, "script/coach_")
            || IndigoDiskSources.Contains(normalized))
        {
            return IndigoDiskCategoryId;
        }

        if (StartsWithFileFamily(normalized, "script/a_") || FieldWorldSources.Contains(normalized))
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

        if (UiOnlineSharedSources.Contains(normalized))
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

    private static bool StartsWithNumberedFileFamily(string context, string prefix)
    {
        return StartsWithFileFamily(context, prefix)
            && context.Length > prefix.Length
            && char.IsAsciiDigit(context[prefix.Length]);
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

    private static string GetLanguageRelativePath(string virtualPath, string language)
    {
        var prefix = $"{MessageRootPath}/{language}/";
        return virtualPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? virtualPath[prefix.Length..]
            : virtualPath;
    }

    private static string? GetLanguage(string virtualPath)
    {
        if (!virtualPath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var languageStart = MessageRootPath.Length + 1;
        var nextSeparator = virtualPath.IndexOf('/', languageStart);
        var discovered = nextSeparator < 0 ? null : virtualPath[languageStart..nextSeparator];
        return SvGameTextLanguage.SupportedMessageLanguages.FirstOrDefault(language =>
            string.Equals(language, discovered, StringComparison.OrdinalIgnoreCase));
    }

    private static string ChangeExtension(string virtualPath, string extension)
    {
        return Path.ChangeExtension(virtualPath, extension)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string CreateRelativePath(string virtualPath)
    {
        return $"romfs/{virtualPath.TrimStart('/')}";
    }

    private static SvTextProvenance CreateBaseProvenance(string virtualPath)
    {
        return new SvTextProvenance(
            CreateRelativePath(virtualPath),
            ProjectFileLayer.Base,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static SvTextProvenance CreateProvenance(SvWorkflowFile file)
    {
        return new SvTextProvenance(file.RelativePath, file.SourceLayer, file.FileState);
    }

    private static string CreateDialogueId(string context, int lineIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.ChangeExtension(context, null)?.Replace('\\', '/')}:{lineIndex}");
    }

    private static bool IsMissingFile(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }
        }

        return false;
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
            Domain: SvEditSessionSupport.TextDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record TextCategoryDefinition(
        string CategoryId,
        string Label,
        string Description);

    private sealed record TextFileSource(
        string VirtualPath,
        string Language,
        string Context,
        string CategoryId,
        bool HasBaseSource);

    private sealed record TextSourceInventory(
        IReadOnlyDictionary<string, IReadOnlyList<TextFileSource>> SourcesByLanguage,
        IReadOnlySet<string> LayeredOverrides,
        IReadOnlySet<string> PackedOutputOverrides);

    private sealed record LoadedTextSource(
        SvTextCachedSource Source,
        SvTextProvenance Provenance);
}
