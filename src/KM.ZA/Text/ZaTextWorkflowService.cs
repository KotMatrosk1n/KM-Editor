// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KM.ZA.Text;

public sealed class ZaTextWorkflowService
{
    public const string MessageRootPath = ZaMessagePathResolver.MessageRootPath;
    public const string WorkflowLabel = "Text and Dialogue Map";
    public const string WorkflowDescription = "Text entries, dialogue references, and source provenance.";
    public const string TextValueField = "value";
    public const int MaximumTextLength = 4096;
    public const int DefaultQueryLimit = 500;
    public const int MaximumQueryLimit = 1000;

    private const string AllCategoryId = "all";
    private const string MainStoryCategoryId = "main-story";
    private const string SideMissionsCategoryId = "side-missions";
    private const string BattleRoyaleCategoryId = "battle-royale";
    private const string MegaDimensionCategoryId = "mega-dimension";
    private const string ItemsCategoryId = "items";
    private const string PokemonPokedexCategoryId = "pokemon-pokedex";
    private const string MovesAbilitiesCategoryId = "moves-abilities";
    private const string TrainersCategoryId = "trainers-characters";
    private const string LocationsCategoryId = "locations";
    private const string ShopsServicesCategoryId = "facilities-services";
    private const string UiSharedCategoryId = "ui-shared";
    private const string OtherScriptsCategoryId = "other-scripts";

    private static readonly Regex MainStoryFileName = new(
        "^main_[0-9]{2}_[0-9]{2}\\.dat$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SideMissionFileName = new(
        "^sub_[0-9]{3}\\.dat$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> MainStorySources = new(StringComparer.OrdinalIgnoreCase)
    {
        "common/questlist_main.dat",
        "script/demo_01.dat",
        "script/demo_03.dat",
        "script/demo_04.dat",
        "script/demo_30.dat",
        "script/demo_31.dat",
        "script/demo_35.dat",
        "script/demo_37.dat",
        "script/main_hud.dat",
    };

    private static readonly HashSet<string> BattleRoyaleSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "common/btl_app.dat",
        "common/btl_attack.dat",
        "common/btl_bgm_select.dat",
        "common/btl_bousou.dat",
        "common/btl_net.dat",
        "common/btl_pokelist.dat",
        "common/btl_pokeselect.dat",
        "common/btl_set.dat",
        "common/btl_state.dat",
        "common/btl_std.dat",
        "common/btlspot.dat",
        "common/competition_organize.dat",
        "common/gameover.dat",
        "common/hud_result.dat",
        "common/hud_ryl.dat",
        "common/net_btl.dat",
        "common/regulation.dat",
        "common/ryl_bonus.dat",
        "common/ryl.dat",
        "common/vs_demo.dat",
        "script/btl_talk.dat",
        "script/field_event.dat",
        "script/megarematch.dat",
        "script/reward.dat",
        "script/royale_event.dat",
        "script/t2.dat",
        "script/t3.dat",
        "script/yukari_infinity.dat",
    };

    private static readonly HashSet<string> ItemSources = new(StringComparer.OrdinalIgnoreCase)
    {
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
        "script/fld_item.dat",
    };

    private static readonly HashSet<string> PokemonPokedexSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "common/box.dat",
        "common/boxname.dat",
        "common/event_pokename.dat",
        "common/level_up.dat",
        "common/monsname.dat",
        "common/pokedex.dat",
        "common/pokelist.dat",
        "common/questlist_mj.dat",
        "common/ribbon.dat",
        "common/seikaku.dat",
        "common/shinka_demo.dat",
        "common/status.dat",
        "common/statusname.dat",
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
        "script/lucario_get.dat",
    };

    private static readonly HashSet<string> MoveAbilitySources = new(StringComparer.OrdinalIgnoreCase)
    {
        "common/tokusei.dat",
        "common/tokuseiinfo.dat",
        "common/waza_remember.dat",
        "common/waza_wasure.dat",
        "common/wazainfo.dat",
        "common/wazaname.dat",
    };

    private static readonly HashSet<string> TrainerSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "common/another_name.dat",
        "common/namelist.dat",
        "common/trmsg.dat",
        "common/trname.dat",
        "common/trtype.dat",
    };

    private static readonly HashSet<string> LocationSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "common/information.dat",
        "common/map.dat",
        "common/place_info.dat",
        "common/place_name.dat",
        "common/place_name_indirect.dat",
        "common/place_name_out.dat",
        "common/place_name_per.dat",
        "common/place_name_spe.dat",
    };

    private static readonly HashSet<string> ShopServiceSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "common/dressup.dat",
        "common/money_window.dat",
        "script/affection.dat",
        "script/bed.dat",
        "script/bench.dat",
        "script/cafe.dat",
        "script/change_deox_scr.dat",
        "script/change_rotom_scr.dat",
        "script/change_torimian_scr.dat",
        "script/coordination.dat",
        "script/elevator.dat",
        "script/field_trade.dat",
        "script/fossil.dat",
        "script/hairsalon.dat",
        "script/lostball.dat",
        "script/megashards.dat",
        "script/moveplus.dat",
        "script/pokemoncenter.dat",
        "script/reset.dat",
        "script/restaurant.dat",
        "script/shop.dat",
        "script/staffrollreplay.dat",
        "script/stand.dat",
        "script/taxi.dat",
        "script/training.dat",
        "script/vending_machine.dat",
    };

    private static readonly IReadOnlyList<TextCategoryDefinition> CategoryDefinitions =
    [
        new TextCategoryDefinition(
            MainStoryCategoryId,
            "Main Story",
            "Main mission dialogue and the main quest list."),
        new TextCategoryDefinition(
            SideMissionsCategoryId,
            "Side Missions",
            "Base-game numbered side mission dialogue and the side mission list."),
        new TextCategoryDefinition(
            MegaDimensionCategoryId,
            "Mega Dimension",
            "Mega Dimension story, missions, activities, and interface text."),
        new TextCategoryDefinition(
            BattleRoyaleCategoryId,
            "Battles and Z-A Royale",
            "Battle interface text, battle dialogue, Z-A Royale tiers, rewards, and Mega rematches."),
        new TextCategoryDefinition(
            ItemsCategoryId,
            "Items",
            "Item names and descriptions, bag text, pockets, nuts, and field-item messages."),
        new TextCategoryDefinition(
            PokemonPokedexCategoryId,
            "Pokemon and Pokedex",
            "Pokemon names, Pokedex entries, storage, status, forms, measurements, and related messages."),
        new TextCategoryDefinition(
            MovesAbilitiesCategoryId,
            "Moves and Abilities",
            "Move and ability names, descriptions, remembering, and forgetting text."),
        new TextCategoryDefinition(
            TrainersCategoryId,
            "Trainers and Characters",
            "Trainer names, classes, dialogue labels, character names, and appellations."),
        new TextCategoryDefinition(
            LocationsCategoryId,
            "Locations",
            "Map, place-name, and place-information text."),
        new TextCategoryDefinition(
            ShopsServicesCategoryId,
            "Facilities and Services",
            "Shops, cafes, restaurants, Pokemon Centers, salons, taxis, trades, activities, and upgrade services."),
        new TextCategoryDefinition(
            UiSharedCategoryId,
            "UI and Shared Text",
            "Shared menus, prompts, system messages, activities, and interface text."),
        new TextCategoryDefinition(
            OtherScriptsCategoryId,
            "Other Scripts",
            "Mixed event and script text that cannot be categorized more narrowly."),
    ];

    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };

    private static readonly IReadOnlyList<ZaTextEditableField> EditableFields =
    [
        new ZaTextEditableField(TextValueField, "Text value", "multilineText", 0, MaximumTextLength),
    ];

    private static readonly IReadOnlyList<ZaTextLanguageRecord> Languages =
        ZaGameTextLanguage.SupportedMessageLanguages
            .Select(language => new ZaTextLanguageRecord(language, GetLanguageLabel(language)))
            .ToArray();

    private readonly ZaWorkflowFileSource fileSource;

    public static IReadOnlyList<ZaTextLanguageRecord> SupportedLanguages => Languages;

    internal ZaTextWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Text,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaTextWorkflow Load(OpenedProject project, ZaTextWorkflowQuery? query = null)
    {
        return LoadCore(project, query, unpagedLanguage: null);
    }

    public ZaTextWorkflow LoadUnpaged(OpenedProject project, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return LoadCore(project, query: null, unpagedLanguage: language);
    }

    private ZaTextWorkflow LoadCore(
        OpenedProject project,
        ZaTextWorkflowQuery? query,
        string? unpagedLanguage)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var normalizedQuery = NormalizeQuery(query);
        var requestedLanguage = ZaGameTextLanguage.Resolve(
            unpagedLanguage ?? normalizedQuery?.Language ?? project.Paths.GameTextLanguage);
        if (summary.Availability == ZaWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<ZaTextEntryRecord>(),
                Array.Empty<ZaDialogueReferenceRecord>(),
                diagnostics,
                Array.Empty<ZaTextCategoryRecord>(),
                AllCategoryId,
                Languages,
                requestedLanguage,
                page: null,
                sourceFileCount: 0);
        }

        var textSources = ResolveMessageSources(
            project,
            requestedLanguage,
            diagnostics,
            out var selectedLanguage);
        if (textSources.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Text and Dialogue Map did not find any Pokemon Legends Z-A message tables.",
                expected: $"{MessageRootPath}/{ZaGameTextLanguage.English}/**/*.dat"));
            return CreateWorkflow(
                summary,
                Array.Empty<ZaTextEntryRecord>(),
                Array.Empty<ZaDialogueReferenceRecord>(),
                diagnostics,
                Array.Empty<ZaTextCategoryRecord>(),
                AllCategoryId,
                Languages,
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
        var entries = new List<ZaTextEntryRecord>();
        var dialogueReferences = new List<ZaDialogueReferenceRecord>();
        var scannedTextEntryCount = 0;
        var matchedTextEntryCount = 0;
        var hasNextPage = false;
        var stopScanning = false;

        foreach (var source in selectedSources)
        {
            try
            {
                var sourceFile = fileSource.Read(project, source.VirtualPath);
                var textFile = SwShGameTextFile.Parse(sourceFile.Bytes);
                var provenance = CreateProvenance(sourceFile);
                var context = GetLanguageRelativePath(sourceFile.VirtualPath, source.Language);
                var messageKeys = TryLoadMessageKeys(
                    project,
                    source.VirtualPath,
                    textFile.Lines.Count,
                    diagnostics);

                for (var lineIndex = 0; lineIndex < textFile.Lines.Count; lineIndex++)
                {
                    var line = textFile.Lines[lineIndex];
                    var textId = scannedTextEntryCount++;
                    var messageKey = lineIndex < messageKeys.Count
                        && !string.IsNullOrWhiteSpace(messageKeys[lineIndex])
                            ? messageKeys[lineIndex]
                            : null;
                    var label = CreateTextLabel(context, lineIndex, messageKey);
                    if (normalizedQuery is not null)
                    {
                        if (!MatchesQuery(
                            normalizedQuery.SearchText,
                            textId,
                            messageKey,
                            source.Language,
                            sourceFile.RelativePath,
                            context,
                            label,
                            lineIndex,
                            line.Text))
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
                        sourceFile,
                        context,
                        lineIndex,
                        line.Text,
                        label,
                        messageKey,
                        provenance);
                }

                if (stopScanning)
                {
                    break;
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Message table 'romfs/{source.VirtualPath}' could not be decoded: {exception.Message}",
                    file: $"romfs/{source.VirtualPath}",
                    expected: "Pokemon Legends Z-A encrypted text table"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table 'romfs/{source.VirtualPath}' could not be read: {exception.Message}",
                    file: $"romfs/{source.VirtualPath}",
                    expected: "Readable Pokemon Legends Z-A message table"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table 'romfs/{source.VirtualPath}' could not be read: {exception.Message}",
                    file: $"romfs/{source.VirtualPath}",
                    expected: "Readable Pokemon Legends Z-A message table"));
            }
        }

        var page = normalizedQuery is null
            ? null
            : new ZaTextResultPage(
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
            Languages,
            selectedLanguage,
            page,
            textSources.Count);
    }

    private static ZaTextWorkflowQuery? NormalizeQuery(ZaTextWorkflowQuery? query)
    {
        if (query is null)
        {
            return null;
        }

        var searchText = string.IsNullOrWhiteSpace(query.SearchText)
            ? null
            : query.SearchText.Trim();
        return new ZaTextWorkflowQuery(
            searchText,
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
        ICollection<ZaTextEntryRecord> entries,
        ICollection<ZaDialogueReferenceRecord> dialogueReferences,
        int textId,
        TextFileSource source,
        ZaWorkflowFile sourceFile,
        string context,
        int lineIndex,
        string value,
        string label,
        string? messageKey,
        ZaTextProvenance provenance)
    {
        var entry = new ZaTextEntryRecord(
            textId,
            CreateTextKey(sourceFile.RelativePath, lineIndex),
            label,
            messageKey,
            source.Language,
            sourceFile.RelativePath,
            lineIndex,
            value,
            CanEdit: true,
            EditBlockedReason: null,
            provenance);

        entries.Add(entry);
        dialogueReferences.Add(new ZaDialogueReferenceRecord(
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

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sourceFile}#{lineIndex}");
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

    internal static bool TryGetVirtualPathFromTextKey(string? textKey, out string virtualPath, out int lineIndex)
    {
        virtualPath = string.Empty;
        if (!TryParseTextKey(textKey, out var sourceFile, out lineIndex))
        {
            return false;
        }

        var normalizedSource = sourceFile.Replace('\\', '/').TrimStart('/');
        if (normalizedSource.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSource = normalizedSource["romfs/".Length..];
        }

        if (!normalizedSource.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase)
            || !normalizedSource.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = normalizedSource.Split('/');
        var hasSupportedFolder = segments.Length >= 4
            && (string.Equals(segments[3], "common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[3], "script", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[3], "sk", StringComparison.OrdinalIgnoreCase));
        if (segments.Length < 5
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                || string.Equals(segment, ".", StringComparison.Ordinal)
                || string.Equals(segment, "..", StringComparison.Ordinal))
            || !string.Equals(segments[0], "ik_message", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "dat", StringComparison.OrdinalIgnoreCase)
            || !ZaGameTextLanguage.SupportedMessageLanguages.Contains(
                segments[2],
                StringComparer.OrdinalIgnoreCase)
            || !hasSupportedFolder)
        {
            return false;
        }

        virtualPath = normalizedSource;
        return true;
    }

    internal static string CreatePreview(string value)
    {
        const int maxPreviewLength = 72;
        var singleLine = value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return singleLine.Length <= maxPreviewLength ? singleLine : $"{singleLine[..maxPreviewLength]}...";
    }

    private ZaTextWorkflow CreateWorkflow(
        ZaWorkflowSummary summary,
        IReadOnlyList<ZaTextEntryRecord> entries,
        IReadOnlyList<ZaDialogueReferenceRecord> dialogueReferences,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        IReadOnlyList<ZaTextCategoryRecord> categories,
        string selectedCategoryId,
        IReadOnlyList<ZaTextLanguageRecord> languages,
        string selectedLanguage,
        ZaTextResultPage? page,
        int sourceFileCount)
    {
        return new ZaTextWorkflow(
            summary,
            entries,
            dialogueReferences,
            EditableFields,
            categories,
            selectedCategoryId,
            languages,
            selectedLanguage,
            page,
            new ZaTextWorkflowStats(
                entries.Count,
                dialogueReferences.Count,
                sourceFileCount),
            diagnostics);
    }

    private IReadOnlyList<TextFileSource> ResolveMessageSources(
        OpenedProject project,
        string preferredLanguage,
        ICollection<ValidationDiagnostic> diagnostics,
        out string selectedLanguage)
    {
        var preferredSources = ResolveMessageSources(project, preferredLanguage);
        if (preferredSources.Count > 0 || string.Equals(preferredLanguage, ZaGameTextLanguage.English, StringComparison.OrdinalIgnoreCase))
        {
            selectedLanguage = preferredLanguage;
            return preferredSources;
        }

        var englishSources = ResolveMessageSources(project, ZaGameTextLanguage.English);
        if (englishSources.Count > 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"'{preferredLanguage}' message tables were not found; loaded English message tables instead.",
                expected: $"{MessageRootPath}/{preferredLanguage}/**/*.dat"));
        }

        selectedLanguage = englishSources.Count > 0
            ? ZaGameTextLanguage.English
            : preferredLanguage;
        return englishSources;
    }

    private IReadOnlyList<TextFileSource> ResolveMessageSources(OpenedProject project, string language)
    {
        return CreateMessageVirtualPathCandidates(project, language)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new TextFileSource(
                path,
                language,
                ClassifySource(GetLanguageRelativePath(path, language))))
            .ToArray();
    }

    private IReadOnlyList<string> CreateMessageVirtualPathCandidates(OpenedProject project, string language)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packName in fileSource.ListBasePackNames(project))
        {
            var virtualPath = ZaMessagePathResolver.TryCreateMessageDatPathFromPackName(packName, language);
            if (!string.IsNullOrWhiteSpace(virtualPath)
                && virtualPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(virtualPath);
            }
        }

        AddLooseMessageCandidates(candidates, project.Paths.BaseRomFsPath, language, hasRomFsPrefix: false);

        if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            AddLooseMessageCandidates(candidates, project.Paths.OutputRootPath, language, hasRomFsPrefix: false);
            AddLooseMessageCandidates(candidates, Path.Combine(project.Paths.OutputRootPath, "romfs"), language, hasRomFsPrefix: false);
            AddLooseMessageCandidates(
                candidates,
                Path.Combine(
                    project.Paths.OutputRootPath,
                    ZaWorkflowFileSource.TrinityModManagerRomFsDirectory),
                language,
                hasRomFsPrefix: false);
        }

        return candidates.ToArray();
    }

    private static void AddLooseMessageCandidates(
        ISet<string> candidates,
        string? romFsRootPath,
        string language,
        bool hasRomFsPrefix)
    {
        if (string.IsNullOrWhiteSpace(romFsRootPath))
        {
            return;
        }

        var messageRoot = Path.Combine(romFsRootPath, MessageRootPath.Replace('/', Path.DirectorySeparatorChar), language);
        if (!Directory.Exists(messageRoot))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(messageRoot, "*.dat", RecursiveEnumeration))
        {
            var relativeToRoot = Path.GetRelativePath(romFsRootPath, filePath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var normalized = hasRomFsPrefix && relativeToRoot.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
                ? relativeToRoot["romfs/".Length..]
                : relativeToRoot;

            if (normalized.StartsWith($"{MessageRootPath}/{language}/", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
            }
        }
    }

    private static ZaTextProvenance CreateProvenance(ZaWorkflowFile file)
    {
        return new ZaTextProvenance(file.RelativePath, file.SourceLayer, file.FileState);
    }

    private static string GetLanguageRelativePath(string virtualPath, string language)
    {
        var prefix = $"{MessageRootPath}/{language}/";
        return virtualPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? virtualPath[prefix.Length..]
            : virtualPath;
    }

    private IReadOnlyList<string> TryLoadMessageKeys(
        OpenedProject project,
        string messageDataPath,
        int lineCount,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var keyPath = Path.ChangeExtension(messageDataPath, ".tbl")
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        try
        {
            var keys = SwShAhtbFile.Parse(fileSource.Read(project, keyPath).Bytes)
                .Entries
                .Select(entry => entry.Name)
                .ToArray();
            var hasExpectedSentinel = keys.Length == lineCount + 1
                && keys[^1].EndsWith("_max", StringComparison.OrdinalIgnoreCase);
            if (keys.Length != lineCount && !hasExpectedSentinel)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Message key table 'romfs/{keyPath}' has {keys.Length} keys for {lineCount} editable lines. Available keys were used by line index.",
                    file: $"romfs/{keyPath}",
                    expected: $"{lineCount} keys, optionally followed by one *_max sentinel"));
            }

            return keys.Length <= lineCount
                ? keys
                : keys[..lineCount];
        }
        catch (FileNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Message key table 'romfs/{keyPath}' could not be decoded: {exception.Message}",
                file: $"romfs/{keyPath}",
                expected: "Pokemon Legends Z-A AHTB message-key table"));
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<ZaTextCategoryRecord> CreateCategories(
        IReadOnlyList<TextFileSource> sources)
    {
        var categories = new List<ZaTextCategoryRecord>(CategoryDefinitions.Count + 1)
        {
            new ZaTextCategoryRecord(
                AllCategoryId,
                "All",
                "Every editable message table in the selected game-text language.",
                sources.Count),
        };

        foreach (var definition in CategoryDefinitions)
        {
            var sourceCount = sources.Count(source => string.Equals(
                source.CategoryId,
                definition.CategoryId,
                StringComparison.Ordinal));
            if (sourceCount > 0)
            {
                categories.Add(new ZaTextCategoryRecord(
                    definition.CategoryId,
                    definition.Label,
                    definition.Description,
                    sourceCount));
            }
        }

        return categories;
    }

    private static string ResolveSelectedCategoryId(
        string? requestedCategoryId,
        IReadOnlyList<ZaTextCategoryRecord> categories)
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
            "LATAM" => "Español (Latinoamérica)",
            "Simp_Chinese" => "简体中文",
            "Trad_Chinese" => "繁體中文",
            _ => language,
        };
    }

    private static string ClassifySource(string context)
    {
        var normalized = context.Replace('\\', '/').TrimStart('/');
        var fileName = Path.GetFileName(normalized);
        if (normalized.StartsWith("sk/", StringComparison.OrdinalIgnoreCase))
        {
            return MegaDimensionCategoryId;
        }

        if (MainStorySources.Contains(normalized)
            || (normalized.StartsWith("script/", StringComparison.OrdinalIgnoreCase)
                && MainStoryFileName.IsMatch(fileName)))
        {
            return MainStoryCategoryId;
        }

        if (string.Equals(normalized, "common/questlist_sub.dat", StringComparison.OrdinalIgnoreCase)
            || (normalized.StartsWith("script/", StringComparison.OrdinalIgnoreCase)
                && SideMissionFileName.IsMatch(fileName)))
        {
            return SideMissionsCategoryId;
        }

        if (BattleRoyaleSources.Contains(normalized))
        {
            return BattleRoyaleCategoryId;
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

        if (TrainerSources.Contains(normalized))
        {
            return TrainersCategoryId;
        }

        if (LocationSources.Contains(normalized))
        {
            return LocationsCategoryId;
        }

        if (ShopServiceSources.Contains(normalized))
        {
            return ShopsServicesCategoryId;
        }

        if (normalized.StartsWith("script/t1", StringComparison.OrdinalIgnoreCase))
        {
            return BattleRoyaleCategoryId;
        }

        if (normalized.StartsWith("common/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "script/common_scr.dat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "script/popup.dat", StringComparison.OrdinalIgnoreCase))
        {
            return UiSharedCategoryId;
        }

        return OtherScriptsCategoryId;
    }

    private static string CreateTextLabel(string context, int lineIndex, string? messageKey)
    {
        if (!string.IsNullOrWhiteSpace(messageKey) && !IsOpaqueMessageKey(messageKey))
        {
            return messageKey;
        }

        return $"{Path.GetFileNameWithoutExtension(context)} #{lineIndex}";
    }

    private static bool IsOpaqueMessageKey(string messageKey)
    {
        const string prefix = "msg_";
        if (!messageKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || messageKey.Length != prefix.Length + 32)
        {
            return false;
        }

        return messageKey.AsSpan(prefix.Length).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
    }

    private static string CreateDialogueId(string context, int lineIndex)
    {
        return $"{Path.ChangeExtension(context, null)?.Replace('\\', '/') ?? context}:{lineIndex}";
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
            Domain: ZaEditSessionSupport.TextDomain,
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
        string CategoryId);
}


