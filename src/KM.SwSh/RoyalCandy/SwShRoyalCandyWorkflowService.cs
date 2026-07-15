// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.ExeFs;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.RoyalCandy;

public sealed class SwShRoyalCandyWorkflowService
{
    public const string ItemPath = "romfs/bin/pml/item/item.dat";
    public const string ItemHashPath = "romfs/bin/pml/item/item_hash_to_index.dat";
    public const string ShopDataPath = "romfs/bin/appli/shop/bin/shop_data.bin";
    public const string LegacyShopDataPath = "romfs/bin/app/shop/shop_data.bin";
    public const string NestDataPath = "romfs/bin/archive/field/resident/data_table.gfpak";
    public const string PlacementPath = "romfs/bin/archive/field/resident/placement.gfpak";
    public const string BagEventScriptPath = "romfs/bin/script/amx/main_event_0020.amx";
    public const string ExeFsMainPath = "exefs/main";
    public const string ExeFsNpdmPath = "exefs/main.npdm";

    private const string PreflightWorkflowId = "royal-candy-preflight";
    private const string UnlimitedWorkflowId = "royal-candy-unlimited";
    private const string StoryLimitsWorkflowId = "royal-candy-story-limits";
    private const string UninstallWorkflowId = "royal-candy-uninstall";
    private const int RareCandyItemId = 50;
    private const int RoyalCandyItemId = 1128;
    private const string UnlimitedRoyalCandyName = "Unlimited Royal Candy";
    private const string StoryLimitsRoyalCandyName = "Royal Candy with Story Limits";
    private const string AppliedRoyalCandyName = "Royal Candy";
    private const string AppliedRoyalCandyPluralName = "Royal Candies";
    private const string RemoveRoyalCandyName = "Remove Royal Candy";
    private const string UnlimitedRoyalCandyDescription = "A candy packed with strange energy. It can be used repeatedly by compatible Pokemon.";
    private const string StoryLimitsRoyalCandyDescription = "A candy packed with strange energy. Its full power follows the current story limit.";
    private const int ItemRawRowSize = 0x30;
    private const ulong SwordTitleId = 0x0100ABF008968000;
    private const ulong ShieldTitleId = 0x01008DB008C2C000;
    private const int MinimumStoryLevelCap = 1;
    private const int MaximumStoryLevelCap = 100;
    private const ulong SceneMainMasterWorkHash = 0x00188D41BB7B57FB;
    private const ulong HopFirstBattleFlagHash = 0xA9C039F0598B8A31;
    private const ulong HopEndorsementFlagHash = 0x005A329212277F11;

    private static readonly SwShRoyalCandyProvenance GeneratedProvenance = new(
        "project",
        ProjectFileLayer.Generated,
        ProjectFileGraphEntryState.BaseOnly);

    private static readonly FileRequirement[] RequiredInputs =
    [
        new("item-data", "RomFS", "Item data", [ItemPath], "RomFS data", "Appends the Rare Candy template as a unique Royal Candy key-item row and points item 1128 at it."),
        new("item-hash", "RomFS", "Item hash table", [ItemHashPath], "RomFS data", "Preserves the existing item hash lookup while verifying item 1128 is present."),
        new("shop-data", "RomFS", "Shop data", [ShopDataPath, LegacyShopDataPath], "RomFS data", "Removes the vanilla Exp. Candy XL shop listing that would become purchasable Royal Candy."),
        new("bag-event-script", "RomFS", "Bag event script", [BagEventScriptPath], "RomFS script", "Validates the bag event script source used by the grant workflow."),
        new("exefs-main", "ExeFS", "ExeFS main", [ExeFsMainPath], "ExeFS NSO", "Validates the NSO source used by Royal Candy UI and usage patches."),
    ];

    private static readonly LevelCapMilestoneDefinition[] DefaultLevelCapMilestones =
    [
        new(10, HopFirstBattleFlagHash, "Hop 004/005/006", "Hop 004/005/006"),
        new(16, HopEndorsementFlagHash, "Hop 007/008/009", "Hop 007/008/009"),
        new(20, SceneMainMasterWorkHash, "Hop 191/192/193", "Hop 191/192/193", "workAtLeast", 530),
        new(23, SceneMainMasterWorkHash, "Bede 195", "Bede 195", "workAtLeast", 550),
        new(25, 0xB02911749203329A, "Milo 032", "Milo 032"),
        new(28, SceneMainMasterWorkHash, "Hop 121/122/123", "Hop 121/122/123", "workAtLeast", 640),
        new(30, 0x8B4F4365890D1CF9, "Nessa 036", "Nessa 036"),
        new(32, SceneMainMasterWorkHash, "Bede 240", "Bede 240", "workAtLeast", 720),
        new(36, SceneMainMasterWorkHash, "Marnie 196", "Marnie 196", "workAtLeast", 760),
        new(38, 0xABFC3E0B626D6B24, "Kabu 037", "Kabu 037"),
        new(40, SceneMainMasterWorkHash, "Hop 124/125/126", "Hop 124/125/126", "workAtLeast", 950),
        new(42, 0xC07B67FC3148B754, "Bea 077", "Allister 078"),
        new(44, SceneMainMasterWorkHash, "Bede 133", "Bede 133", "workAtLeast", 1090),
        new(47, 0xDF7AC7105B946783, "Opal 108", "Opal 108"),
        new(50, SceneMainMasterWorkHash, "Hop 127/128/129", "Hop 127/128/129", "workAtLeast", 1200),
        new(52, 0x7042D310DF3DB17F, "Gordie 135", "Melony 136"),
        new(54, SceneMainMasterWorkHash, "Hop 202/203/204", "Hop 202/203/204", "workAtLeast", 1300),
        new(55, SceneMainMasterWorkHash, "Marnie 138", "Marnie 138", "workAtLeast", 1330),
        new(60, 0xA52A7561C28A76F1, "Piers 107", "Piers 107"),
        new(65, 0xE336BF34143E0946, "Raihan 144", "Raihan 144"),
        new(70, SceneMainMasterWorkHash, "Hop 130/131/132", "Hop 130/131/132", "workAtLeast", 1550),
        new(75, SceneMainMasterWorkHash, "Oleana 143", "Oleana 143", "workAtLeast", 1660),
        new(80, SceneMainMasterWorkHash, "Raihan 213", "Raihan 213", "workAtLeast", 1780),
        new(85, SceneMainMasterWorkHash, "Rose 175", "Rose 175", "workAtLeast", 1910),
        new(90, SceneMainMasterWorkHash, "Leon 149/189/190", "Leon 149/189/190", "workAtLeast", 3000),
    ];

    private readonly SwShExeFsPatchWorkflowService exeFsPatchWorkflowService;
    private readonly SwShBagHookWorkflowService bagHookWorkflowService;

    public SwShRoyalCandyWorkflowService(
        SwShExeFsPatchWorkflowService? exeFsPatchWorkflowService = null,
        SwShBagHookWorkflowService? bagHookWorkflowService = null)
    {
        this.exeFsPatchWorkflowService = exeFsPatchWorkflowService ?? new SwShExeFsPatchWorkflowService();
        this.bagHookWorkflowService = bagHookWorkflowService ?? new SwShBagHookWorkflowService();
    }

    public void ClearMemoryCache()
    {
        exeFsPatchWorkflowService.ClearMemoryCache();
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy Workflows requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShRoyalCandyWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<SwShRoyalCandyWorkflowRecord>(),
                Array.Empty<SwShRoyalCandyWorkflowCheckRecord>(),
                Array.Empty<SwShRoyalCandyOutputRecord>(),
                sourceFileCount: 0,
                diagnostics);
        }

        var checks = new List<SwShRoyalCandyWorkflowCheckRecord>();
        var sourceEntries = new List<ProjectFileGraphEntry>();
        var sourceMap = new Dictionary<string, ProjectFileGraphEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in RequiredInputs)
        {
            var entry = FindFirstEntry(project, requirement.RelativePaths);
            if (entry is null)
            {
                AddCheck(
                    checks,
                    $"{PreflightWorkflowId}:{requirement.CheckId}",
                    PreflightWorkflowId,
                    "Fail",
                    requirement.Area,
                    requirement.RelativePaths[0],
                    $"{requirement.Name} is missing.",
                    CreateMissingProvenance(requirement.RelativePaths[0]));
                continue;
            }

            sourceEntries.Add(entry);
            sourceMap[requirement.RelativePaths[0]] = entry;
            foreach (var relativePath in requirement.RelativePaths)
            {
                sourceMap.TryAdd(relativePath, entry);
            }

            AddRequiredInputCheck(project, checks, requirement, entry);
        }

        AddItemDataShapeChecks(project, checks, sourceMap, sourceEntries);

        var textSets = DiscoverMessageTextSets(project.FileGraph.Entries).ToArray();
        foreach (var textSet in textSets)
        {
            sourceEntries.Add(textSet.ItemInfo);
            sourceEntries.AddRange(textSet.ItemNames);
        }

        var selectedTextSets = SelectSupportedTextOutputSets(project.Paths, textSets);
        AddMessageTextSetCheck(checks, project.Paths, textSets, selectedTextSets);
        AddConcreteInputShapeChecks(project, checks, sourceMap, selectedTextSets);

        var npdmEntry = FindFirstEntry(project, [ExeFsNpdmPath]);
        if (npdmEntry is not null)
        {
            sourceMap[ExeFsNpdmPath] = npdmEntry;
        }

        var npdmFlavor = AddNpdmFlavorCheck(project, checks, sourceMap, sourceEntries);
        var gameFlavor = AddGameRoutingCheck(project, checks, sourceMap, npdmFlavor);
        var installationState = DetectRoyalCandyInstallation(project, selectedTextSets);
        var installedStoryLevelCaps = ReadInstalledStoryLevelCapOverrides(project, sourceMap, installationState, checks);
        var exeFsWorkflow = exeFsPatchWorkflowService.Load(project);
        AddExeFsCompatibilityChecks(checks, exeFsWorkflow, installationState);
        AddRoyalCandyReservedAnchorChecks(project, checks, sourceMap);
        var bagHookWorkflow = bagHookWorkflowService.Load(project);
        AddBagHookRequirementChecks(checks, bagHookWorkflow);

        var outputRootReady = project.Health.CanOpenEditableWorkflows
            && !string.IsNullOrWhiteSpace(project.Paths.OutputRootPath);
        AddOutputRootCheck(checks, outputRootReady);

        var preflightChecks = checks.Where(check => check.WorkflowId == PreflightWorkflowId).ToArray();
        var installStatus = DetermineInstallStatus(preflightChecks, outputRootReady, installationState);
        var workflows = CreateWorkflows(
            installStatus,
            gameFlavor,
            SelectPrimaryProvenance(sourceEntries),
            installationState,
            installedStoryLevelCaps).ToList();
        var outputs = new List<SwShRoyalCandyOutputRecord>();

        outputs.AddRange(CreateInstallOutputs(
            UnlimitedWorkflowId,
            workflows.Single(workflow => workflow.WorkflowId == UnlimitedWorkflowId).Status,
            sourceMap,
            selectedTextSets));
        outputs.AddRange(CreateInstallOutputs(
            StoryLimitsWorkflowId,
            workflows.Single(workflow => workflow.WorkflowId == StoryLimitsWorkflowId).Status,
            sourceMap,
            selectedTextSets));
        AddUninstallWorkflow(project, installationState, workflows, checks, outputs);

        AddAggregateDiagnostics(diagnostics, preflightChecks, installationState);

        return CreateWorkflow(
            summary,
            workflows,
            checks,
            outputs,
            sourceEntries
                .Select(entry => entry.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            diagnostics);
    }

    private static SwShRoyalCandyWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShRoyalCandyWorkflowRecord> workflows,
        IReadOnlyList<SwShRoyalCandyWorkflowCheckRecord> checks,
        IReadOnlyList<SwShRoyalCandyOutputRecord> outputs,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShRoyalCandyWorkflow(
            summary,
            workflows,
            checks,
            outputs,
            new SwShRoyalCandyWorkflowStats(
                workflows.Count,
                workflows.Sum(workflow => workflow.Steps.Count),
                checks.Count,
                checks.Count(check => check.Status == "Pass"),
                checks.Count(check => check.Status == "Warning"),
                checks.Count(check => check.Status == "Fail"),
                outputs.Count,
                sourceFileCount),
            diagnostics);
    }

    private static void AddRequiredInputCheck(
        OpenedProject project,
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        FileRequirement requirement,
        ProjectFileGraphEntry entry)
    {
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:{requirement.CheckId}",
                PreflightWorkflowId,
                "Fail",
                requirement.Area,
                entry.RelativePath,
                $"{requirement.Name} could not be resolved from the project graph.",
                CreateProvenance(entry));
            return;
        }

        try
        {
            var size = new FileInfo(sourcePath).Length;
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:{requirement.CheckId}",
                PreflightWorkflowId,
                size > 0 ? "Pass" : "Warning",
                requirement.Area,
                entry.RelativePath,
                size > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{requirement.Name} found ({size:N0} bytes).")
                    : $"{requirement.Name} exists but is empty.",
                CreateProvenance(entry));
        }
        catch (IOException exception)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:{requirement.CheckId}",
                PreflightWorkflowId,
                "Fail",
                requirement.Area,
                entry.RelativePath,
                $"{requirement.Name} could not be inspected: {exception.Message}",
                CreateProvenance(entry));
        }
    }

    private static void AddItemDataShapeChecks(
        OpenedProject project,
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        IReadOnlyDictionary<string, ProjectFileGraphEntry> sourceMap,
        ICollection<ProjectFileGraphEntry> sourceEntries)
    {
        if (!sourceMap.TryGetValue(ItemPath, out var entry))
        {
            return;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            var table = SwShItemTable.Parse(File.ReadAllBytes(sourcePath));
            var provenance = CreateProvenance(entry);
            sourceEntries.Add(entry);
            var hasRareCandy = table.Records.Any(record => record.ItemId == RareCandyItemId);
            var hasRoyalCandy = table.Records.Any(record => record.ItemId == RoyalCandyItemId);

            AddCheck(
                checks,
                $"{PreflightWorkflowId}:item-data-stride",
                PreflightWorkflowId,
                "Pass",
                "RomFS",
                entry.RelativePath,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Item table decoded with {table.Records.Count:N0} item id(s); 0x{ItemRawRowSize:X}-byte rows are validated from the table header."),
                provenance);

            AddCheck(
                checks,
                $"{PreflightWorkflowId}:item-template-row",
                PreflightWorkflowId,
                hasRareCandy ? "Pass" : "Fail",
                "RomFS",
                entry.RelativePath,
                hasRareCandy
                    ? "Rare Candy template item id is present."
                    : "Rare Candy template item id is outside the item table.",
                provenance);

            AddCheck(
                checks,
                $"{PreflightWorkflowId}:royal-candy-row",
                PreflightWorkflowId,
                hasRoyalCandy ? "Pass" : "Fail",
                "RomFS",
                entry.RelativePath,
                hasRoyalCandy
                    ? "Royal Candy target item id is present."
                    : "Royal Candy target item id is outside the item table.",
                provenance);
        }
        catch (InvalidDataException exception)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:item-data-shape",
                PreflightWorkflowId,
                "Fail",
                "RomFS",
                entry.RelativePath,
                $"Item table could not be decoded: {exception.Message}",
                CreateProvenance(entry));
        }
        catch (IOException exception)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:item-data-shape",
                PreflightWorkflowId,
                "Fail",
                "RomFS",
                entry.RelativePath,
                $"Item table could not be inspected: {exception.Message}",
                CreateProvenance(entry));
        }
    }

    private static void AddMessageTextSetCheck(
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        ProjectPaths paths,
        IReadOnlyList<MessageTextSet> textSets,
        IReadOnlyList<MessageTextSet> selectedTextSets)
    {
        if (textSets.Count == 0)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:message-text-sets",
                PreflightWorkflowId,
                "Fail",
                "RomFS",
                "romfs/bin/message",
                "No language has both common/iteminfo.dat and itemname*.dat files.",
                CreateMissingProvenance("romfs/bin/message"));
            return;
        }

        var languages = string.Join(", ", textSets.Select(set => set.Language).Order(StringComparer.Ordinal));
        AddCheck(
            checks,
            $"{PreflightWorkflowId}:message-text-sets",
            PreflightWorkflowId,
            "Pass",
            "RomFS",
            "romfs/bin/message",
            string.Create(CultureInfo.InvariantCulture, $"Found {textSets.Count:N0} Royal Candy item text language set(s): {languages}."),
            CreateProvenance(textSets[0].ItemInfo));

        var preferredLanguage = SwShGameTextLanguage.Resolve(paths);
        var selectedLanguage = selectedTextSets.FirstOrDefault()?.Language;
        var usesPreferredLanguage = string.Equals(
            selectedLanguage,
            preferredLanguage,
            StringComparison.OrdinalIgnoreCase);
        AddCheck(
            checks,
            $"{PreflightWorkflowId}:selected-message-language",
            PreflightWorkflowId,
            usesPreferredLanguage ? "Pass" : "Warning",
            "RomFS",
            selectedTextSets.FirstOrDefault()?.ItemInfo.RelativePath ?? "romfs/bin/message",
            usesPreferredLanguage
                ? $"Royal Candy item text will use the selected {preferredLanguage} game-text set."
                : $"The selected {preferredLanguage} game-text set is unavailable; Royal Candy will use {selectedLanguage ?? "no language"} as a deterministic fallback.",
            selectedTextSets.Count > 0
                ? CreateProvenance(selectedTextSets[0].ItemInfo)
                : CreateMissingProvenance("romfs/bin/message"));
    }

    private static void AddConcreteInputShapeChecks(
        OpenedProject project,
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        IReadOnlyDictionary<string, ProjectFileGraphEntry> sourceMap,
        IReadOnlyList<MessageTextSet> selectedTextSets)
    {
        AddDecodedInputCheck(
            project,
            checks,
            sourceMap.GetValueOrDefault(ItemHashPath),
            "item-hash-shape",
            "Item hash table",
            bytes =>
            {
                var table = SwShItemHashTable.Parse(bytes);
                if (table.Entries.All(entry => entry.ItemId != RoyalCandyItemId))
                {
                    throw new InvalidDataException($"Item hash table does not contain item {RoyalCandyItemId}.");
                }
            });

        AddDecodedInputCheck(
            project,
            checks,
            FindSource(sourceMap, ShopDataPath, LegacyShopDataPath),
            "shop-data-shape",
            "Shop data",
            bytes => _ = SwShShopDataFile.Parse(bytes));
        AddDecodedInputCheck(
            project,
            checks,
            sourceMap.GetValueOrDefault(BagEventScriptPath),
            "bag-event-script-shape",
            "Bag event script",
            bytes => _ = SwShBagHookAmxPatcher.Analyze(bytes));

        foreach (var textSet in selectedTextSets)
        {
            foreach (var entry in textSet.ItemNames.Prepend(textSet.ItemInfo))
            {
                AddDecodedInputCheck(
                    project,
                    checks,
                    entry,
                    $"item-text-shape:{entry.RelativePath}",
                    "Item text",
                    bytes =>
                    {
                        var text = SwShGameTextFile.Parse(bytes);
                        if (text.Lines.Count <= RoyalCandyItemId)
                        {
                            throw new InvalidDataException($"Text table does not contain item {RoyalCandyItemId}.");
                        }
                    });
            }
        }
    }

    private static void AddDecodedInputCheck(
        OpenedProject project,
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        ProjectFileGraphEntry? entry,
        string checkId,
        string name,
        Action<byte[]> decode)
    {
        if (entry is null)
        {
            return;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            decode(File.ReadAllBytes(sourcePath));
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:{checkId}",
                PreflightWorkflowId,
                "Pass",
                "RomFS",
                entry.RelativePath,
                $"{name} decoded successfully for Royal Candy output generation.",
                CreateProvenance(entry));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:{checkId}",
                PreflightWorkflowId,
                "Fail",
                "RomFS",
                entry.RelativePath,
                $"{name} could not be decoded safely: {exception.Message}",
                CreateProvenance(entry));
        }
    }

    private static string AddGameRoutingCheck(
        OpenedProject project,
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        IReadOnlyDictionary<string, ProjectFileGraphEntry> sourceMap,
        string npdmFlavor)
    {
        ProjectGame? executableGame = null;
        ProjectFileGraphEntry? mainEntry = null;
        if (sourceMap.TryGetValue(ExeFsMainPath, out mainEntry))
        {
            var mainPath = ResolveSourcePath(project.Paths, mainEntry);
            if (mainPath is not null && File.Exists(mainPath))
            {
                try
                {
                    executableGame = SwShExeFsRoyalCandyMainPatcher.DetectSupportedGame(
                        NsoFile.Parse(File.ReadAllBytes(mainPath)).BuildId);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException)
                {
                    AddCheck(
                        checks,
                        $"{PreflightWorkflowId}:game-routing",
                        PreflightWorkflowId,
                        "Fail",
                        "ExeFS",
                        ExeFsMainPath,
                        $"Royal Candy could not determine the game route from exefs/main: {exception.Message}",
                        CreateProvenance(mainEntry));
                    return "unknown";
                }
            }
        }

        var npdmGame = npdmFlavor switch
        {
            "Sword" => ProjectGame.Sword,
            "Shield" => ProjectGame.Shield,
            _ => (ProjectGame?)null,
        };
        var declaredGames = new[] { executableGame, project.Paths.SelectedGame, npdmGame }
            .Where(game => game is not null)
            .Select(game => game!.Value)
            .Distinct()
            .ToArray();
        var authoritativeGame = executableGame ?? project.Paths.SelectedGame ?? npdmGame;
        var agrees = executableGame is not null && declaredGames.Length == 1;
        AddCheck(
            checks,
            $"{PreflightWorkflowId}:game-routing",
            PreflightWorkflowId,
            agrees ? "Pass" : "Fail",
            "ExeFS",
            ExeFsMainPath,
            agrees
                ? $"Royal Candy will use the supported Pokemon {authoritativeGame} executable route; selected game and main.npdm agree when present."
                : executableGame is null
                    ? "Royal Candy requires a supported Sword or Shield exefs/main build to select game-specific hooks and labels."
                    : "The supported exefs/main build, selected game, and main.npdm do not agree on the Royal Candy game route.",
            mainEntry is null ? CreateMissingProvenance(ExeFsMainPath) : CreateProvenance(mainEntry));
        return agrees ? authoritativeGame!.Value.ToString() : "unknown";
    }

    private static string AddNpdmFlavorCheck(
        OpenedProject project,
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        IReadOnlyDictionary<string, ProjectFileGraphEntry> sourceMap,
        ICollection<ProjectFileGraphEntry> sourceEntries)
    {
        if (!sourceMap.TryGetValue(ExeFsNpdmPath, out var entry))
        {
            return "unknown";
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return "unknown";
        }

        try
        {
            var data = File.ReadAllBytes(sourcePath);
            if (data.Length < 0x298)
            {
                AddCheck(
                    checks,
                    $"{PreflightWorkflowId}:game-flavor",
                    PreflightWorkflowId,
                    "Warning",
                    "ExeFS",
                    entry.RelativePath,
                    "main.npdm is too small to contain a Sword/Shield title id.",
                    CreateProvenance(entry));
                return "unknown";
            }

            sourceEntries.Add(entry);
            var titleId = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(0x290, 8));
            var flavor = titleId switch
            {
                SwordTitleId => "Sword",
                ShieldTitleId => "Shield",
                _ => "unknown",
            };
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:game-flavor",
                PreflightWorkflowId,
                flavor == "unknown" ? "Warning" : "Pass",
                "ExeFS",
                entry.RelativePath,
                flavor == "unknown"
                    ? string.Create(CultureInfo.InvariantCulture, $"Title id 0x{titleId:X16} is not recognized as Sword or Shield.")
                    : string.Create(CultureInfo.InvariantCulture, $"Detected Pokemon {flavor} title id 0x{titleId:X16}."),
                CreateProvenance(entry));
            return flavor;
        }
        catch (IOException exception)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:game-flavor",
                PreflightWorkflowId,
                "Warning",
                "ExeFS",
                entry.RelativePath,
                $"main.npdm could not be inspected: {exception.Message}",
                CreateProvenance(entry));
            return "unknown";
        }
    }

    private static void AddExeFsCompatibilityChecks(
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        SwShExeFsPatchWorkflow exeFsWorkflow,
        RoyalCandyInstallationState installationState)
    {
        if (exeFsWorkflow.Checks.Count == 0)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:exefs-compatibility",
                PreflightWorkflowId,
                exeFsWorkflow.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    ? "Fail"
                    : "Warning",
                "ExeFS",
                ExeFsMainPath,
                "ExeFS compatibility checks could not be completed.",
                CreateMissingProvenance(ExeFsMainPath));
            return;
        }

        foreach (var check in exeFsWorkflow.Checks)
        {
            var target = string.IsNullOrWhiteSpace(check.Offset)
                ? check.Area
                : string.Create(CultureInfo.InvariantCulture, $"{check.Area} {check.Offset}");
            var status = check.Status;
            var notes = check.Notes;
            if (installationState.InstalledWorkflowId is not null
                && check.Status == "Fail"
                && IsExpectedInstalledExeFsCheckFailure(check.CheckId))
            {
                status = "Info";
                notes = "Signature differs because a recognized Royal Candy installation is already present.";
            }

            AddCheck(
                checks,
                $"{PreflightWorkflowId}:exefs:{check.CheckId}",
                PreflightWorkflowId,
                status,
                "ExeFS",
                target,
                $"{check.Name}: expected {check.Expected}, actual {check.Actual}. {notes}",
                new SwShRoyalCandyProvenance(
                    check.Provenance.SourceFile,
                    check.Provenance.SourceLayer,
                    check.Provenance.FileState));
        }
    }

    private static bool IsExpectedInstalledExeFsCheckFailure(string checkId)
    {
        var suffix = checkId[(checkId.LastIndexOf(':') + 1)..];
        return suffix is "patch-code-cave"
            or "exact-patch-preflight"
            or "ui-check-a"
            or "ui-check-b"
            or "ui-check-c"
            or "ui-check-d"
            or "ui-check-e"
            or "ui-check-f"
            or "ui-check-g"
            or "ui-check-h"
            or "ui-check-i"
            or "ui-check-j"
            or "equal-branch-a"
            or "equal-branch-b"
            or "equal-branch-c"
            or "equal-branch-d"
            or "equal-branch-e"
            or "exp-candy-upper-bound-a"
            or "exp-candy-upper-bound-b"
            or "consume-quantity-move"
            or "allowed-consumable-upper-bound";
    }

    private static void AddRoyalCandyReservedAnchorChecks(
        OpenedProject project,
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        IReadOnlyDictionary<string, ProjectFileGraphEntry> sourceMap)
    {
        if (!sourceMap.TryGetValue(ExeFsMainPath, out var entry))
        {
            return;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            var signature = SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(
                File.ReadAllBytes(sourcePath),
                project.Paths.SelectedGame);
            var status = signature.Kind is SwShRoyalCandyExeFsSignatureKind.ForeignPatch
                or SwShRoyalCandyExeFsSignatureKind.GameMismatch
                ? "Fail"
                : "Pass";
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:royal-candy-reserved-anchors",
                PreflightWorkflowId,
                status,
                "ExeFS",
                "Royal Candy reserved anchors",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{signature.Message} Recognized {signature.RecognizedAnchorCount:N0} of {signature.ReservedAnchorCount:N0} reserved identity anchors; payload data lives elsewhere and other hooks must not overwrite these areas."),
                CreateProvenance(entry));
        }
        catch (InvalidDataException exception)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:royal-candy-reserved-anchors",
                PreflightWorkflowId,
                "Fail",
                "ExeFS",
                "Royal Candy reserved anchors",
                $"Royal Candy reserved-anchor signature scan failed: {exception.Message}",
                CreateProvenance(entry));
        }
        catch (IOException exception)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:royal-candy-reserved-anchors",
                PreflightWorkflowId,
                "Warning",
                "ExeFS",
                "Royal Candy reserved anchors",
                $"Royal Candy reserved-anchor signature scan could not read ExeFS main: {exception.Message}",
                CreateProvenance(entry));
        }
    }

    private static void AddBagHookRequirementChecks(
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        SwShBagHookWorkflow bagHookWorkflow)
    {
        var provenance = bagHookWorkflow.Slots.FirstOrDefault()?.Provenance;
        var royalCandyProvenance = provenance is null
            ? CreateMissingProvenance(BagEventScriptPath)
            : new SwShRoyalCandyProvenance(
                provenance.SourceFile,
                provenance.SourceLayer,
                provenance.FileState);

        if (!IsBagHookInstalledForSlotWrites(bagHookWorkflow.InstallStatus))
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:bag-hook-installed",
                PreflightWorkflowId,
                "Fail",
                "Bag Hook",
                BagEventScriptPath,
                "Bag Hook V2 must be installed before Royal Candy can claim slot 1.",
                royalCandyProvenance);
            return;
        }

        AddCheck(
            checks,
            $"{PreflightWorkflowId}:bag-hook-installed",
            PreflightWorkflowId,
            "Pass",
            "Bag Hook",
            BagEventScriptPath,
            "Bag Hook V2 is installed and available for Royal Candy slot assignment.",
            royalCandyProvenance);

        var startingItemRoyalCandySlots = bagHookWorkflow.Slots
            .Where(slot => slot.Slot is >= SwShBagHookAmxPatcher.FirstStartingItemSlot and <= SwShBagHookAmxPatcher.LastStartingItemSlot
                && slot.ItemId == RoyalCandyItemId)
            .Select(slot => slot.Slot)
            .ToArray();
        AddCheck(
            checks,
            $"{PreflightWorkflowId}:bag-hook-starting-items-item-1128",
            PreflightWorkflowId,
            startingItemRoyalCandySlots.Length == 0 ? "Pass" : "Fail",
            "Bag Hook",
            BagEventScriptPath,
            startingItemRoyalCandySlots.Length == 0
                ? "Starting Items slots 2-20 do not contain item 1128."
                : $"Clear item 1128 from Starting Items slot(s) {string.Join(", ", startingItemRoyalCandySlots)} before installing or refreshing Royal Candy; KM will not delete those grants automatically.",
            royalCandyProvenance);

        var slot = bagHookWorkflow.Slots.FirstOrDefault(slot => slot.Slot == SwShBagHookAmxPatcher.RoyalCandySlot);
        if (slot is null)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:bag-hook-slot-1",
                PreflightWorkflowId,
                "Fail",
                "Bag Hook",
                "Slot 1",
                "Bag Hook slot 1 could not be inspected.",
                royalCandyProvenance);
            return;
        }

        var status = slot.Status == "empty"
            || (slot.Status == "occupied" && slot.ItemId == RoyalCandyItemId)
                ? "Pass"
                : "Fail";
        AddCheck(
            checks,
            $"{PreflightWorkflowId}:bag-hook-slot-1",
            PreflightWorkflowId,
            status,
            "Bag Hook",
            "Slot 1",
            status == "Pass"
                ? "Bag Hook slot 1 is reserved for Royal Candy and can hold item 1128 x1."
                : $"Bag Hook slot 1 is occupied by {slot.ItemName}; only Royal Candy item 1128 can use slot 1.",
            royalCandyProvenance);
    }

    private static bool IsBagHookInstalledForSlotWrites(string installStatus)
    {
        return installStatus is SwShBagHookWorkflowService.InstalledStatus
            or SwShBagHookWorkflowService.RepairableStatus;
    }

    private static void AddOutputRootCheck(
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        bool outputRootReady)
    {
        AddCheck(
            checks,
            $"{PreflightWorkflowId}:layeredfs-output-root",
            PreflightWorkflowId,
            outputRootReady ? "Pass" : "Warning",
            "Output",
            "LayeredFS output root",
            outputRootReady
                ? "LayeredFS output root is configured; generated writes can be previewed before apply."
                : "LayeredFS output root is not configured; Royal Candy workflows can be inspected read-only.",
            GeneratedProvenance);
    }

    private static IReadOnlyList<SwShRoyalCandyWorkflowRecord> CreateWorkflows(
        string installStatus,
        string gameFlavor,
        SwShRoyalCandyProvenance provenance,
        RoyalCandyInstallationState installationState,
        IReadOnlyDictionary<LevelCapMilestoneKey, int> installedStoryLevelCaps)
    {
        return
        [
            new(
                UnlimitedWorkflowId,
                UnlimitedRoyalCandyName,
                "Build",
                "RomFS + ExeFS LayeredFS",
                "unlimited",
                RoyalCandyItemId,
                RareCandyItemId,
                GetInstallWorkflowStatus(UnlimitedWorkflowId, installStatus, installationState),
                $"Prepares Royal Candy item {RoyalCandyItemId} from Rare Candy item {RareCandyItemId}, claims Bag Hook slot 1, and installs the reserved ExeFS item-route/decrement signature for unlimited-use behavior in Pokemon {FormatGameFlavor(gameFlavor)} projects.",
                Array.Empty<SwShRoyalCandyLevelCapRecord>(),
                CreateInstallSteps(includeStoryLimits: false),
                provenance),
            new(
                StoryLimitsWorkflowId,
                StoryLimitsRoyalCandyName,
                "Build",
                "RomFS + ExeFS LayeredFS",
                "storyLimits",
                RoyalCandyItemId,
                RareCandyItemId,
                GetInstallWorkflowStatus(StoryLimitsWorkflowId, installStatus, installationState),
                $"Prepares Royal Candy item {RoyalCandyItemId}, claims Bag Hook slot 1, and installs the reserved ExeFS Royal Candy story-limit signature so level gains follow story progress in Pokemon {FormatGameFlavor(gameFlavor)} projects.",
                CreateLevelCaps(gameFlavor, installedStoryLevelCaps),
                CreateInstallSteps(includeStoryLimits: true),
                provenance),
        ];
    }

    private static string GetInstallWorkflowStatus(
        string workflowId,
        string installStatus,
        RoyalCandyInstallationState installationState)
    {
        if (string.Equals(installStatus, "blocked", StringComparison.Ordinal))
        {
            return "blocked";
        }

        if (string.Equals(installationState.InstalledWorkflowId, workflowId, StringComparison.Ordinal))
        {
            return "installed";
        }

        if (installationState.BlocksInstallation)
        {
            return "blocked";
        }

        return installStatus;
    }

    public static IReadOnlyList<SwShRoyalCandyLevelCapRecord> CreateLevelCaps(string gameFlavor)
    {
        return CreateLevelCaps(gameFlavor, new Dictionary<LevelCapMilestoneKey, int>());
    }

    private static IReadOnlyList<SwShRoyalCandyLevelCapRecord> CreateLevelCaps(
        string gameFlavor,
        IReadOnlyDictionary<LevelCapMilestoneKey, int> installedStoryLevelCaps)
    {
        var useShieldNames = string.Equals(gameFlavor, "Shield", StringComparison.OrdinalIgnoreCase);
        return DefaultLevelCapMilestones
            .Select((definition, index) =>
            {
                var key = new LevelCapMilestoneKey(
                    definition.ProgressHash,
                    definition.ProgressKind,
                    definition.ProgressKind == "workAtLeast" ? definition.WorkMinimum : 0);
                var levelCap = installedStoryLevelCaps.TryGetValue(key, out var installedLevelCap)
                    ? installedLevelCap
                    : definition.DefaultCap;

                return new SwShRoyalCandyLevelCapRecord(
                    Slot: index,
                    MilestoneId: string.Create(
                        CultureInfo.InvariantCulture,
                        $"{index}:{definition.ProgressHash:X16}:{definition.WorkMinimum}"),
                    Label: useShieldNames ? definition.ShieldName : definition.SwordName,
                    LevelCap: levelCap,
                    MinimumLevelCap: MinimumStoryLevelCap,
                    MaximumLevelCap: MaximumStoryLevelCap,
                    ProgressKind: definition.ProgressKind,
                    ProgressHash: string.Create(CultureInfo.InvariantCulture, $"0x{definition.ProgressHash:X16}"),
                    WorkMinimum: definition.ProgressKind == "workAtLeast" ? definition.WorkMinimum : null);
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShRoyalCandyWorkflowStepRecord> CreateInstallSteps(bool includeStoryLimits)
    {
        var steps = new List<SwShRoyalCandyWorkflowStepRecord>
        {
            new(1, "Validate sources", "Resolve required RomFS files, a supported ExeFS main, optional main.npdm, and the selected item text language set from the project graph."),
            new(2, "Prepare item records", $"Append a unique Royal Candy item row from item {RareCandyItemId} into item {RoyalCandyItemId} and preserve item hash lookup data."),
            new(3, "Patch item text", "Patch Royal Candy names and descriptions in every applicable itemname table for the selected game-text language."),
            new(4, "Plan acquisition edits", "Patch the verified shop and Bag Hook targets while keeping raid reward and placement integrations explicitly deferred."),
            new(5, "Validate ExeFS anchors", "Verify the Royal Candy reserved item-route/decrement anchors are vanilla, already KM-owned, or blocked as a foreign signature before writing."),
        };

        if (includeStoryLimits)
        {
            steps.Add(new(6, "Apply story limits", "Use the reserved story-limit gate, quantity, and inventory-clamp anchors with the selected story milestone caps."));
        }

        steps.Add(new(steps.Count + 1, "Review LayeredFS output", "Review generated output targets before any future apply operation writes to LayeredFS."));
        return steps;
    }

    private static IEnumerable<SwShRoyalCandyOutputRecord> CreateInstallOutputs(
        string workflowId,
        string installStatus,
        IReadOnlyDictionary<string, ProjectFileGraphEntry> sourceMap,
        IReadOnlyList<MessageTextSet> textSets)
    {
        var outputStatus = installStatus switch
        {
            "available" => "ready",
            "warning" => "review",
            "installed" => "review",
            "readOnly" => "readOnly",
            _ => "blocked",
        };

        yield return CreateOutput(workflowId, ItemPath, FindSource(sourceMap, ItemPath), "RomFS data", outputStatus, "Royal Candy item row patch.");
        yield return CreateOutput(workflowId, ItemHashPath, FindSource(sourceMap, ItemHashPath), "RomFS data", outputStatus, "Royal Candy item hash lookup patch.");
        yield return CreateOutput(workflowId, ResolveShopOutputPath(sourceMap), FindSource(sourceMap, ShopDataPath, LegacyShopDataPath), "RomFS data", outputStatus, "Royal Candy shop inventory cleanup.");
        yield return CreateOutput(workflowId, NestDataPath, FindSource(sourceMap, NestDataPath), "RomFS archive", "deferred", "Reserved future Royal Candy raid reward integration; this workflow does not write the archive.");
        yield return CreateOutput(workflowId, PlacementPath, FindSource(sourceMap, PlacementPath), "RomFS archive", "deferred", "Reserved future Royal Candy placement integration; this workflow does not write the archive.");
        yield return CreateOutput(workflowId, BagEventScriptPath, FindSource(sourceMap, BagEventScriptPath), "Bag Hook slot", outputStatus, "Royal Candy Bag Hook slot 1 grant.");
        yield return CreateOutput(workflowId, ExeFsMainPath, FindSource(sourceMap, ExeFsMainPath), "ExeFS NSO", outputStatus, "Royal Candy ExeFS UI and usage patch.");

        foreach (var textSet in textSets)
        {
            yield return CreateOutput(
                workflowId,
                textSet.ItemInfo.RelativePath,
                textSet.ItemInfo,
                "RomFS text",
                outputStatus,
                $"Royal Candy description text patch for {textSet.Language}.");

            foreach (var itemName in textSet.ItemNames)
            {
                yield return CreateOutput(
                    workflowId,
                    itemName.RelativePath,
                    itemName,
                    "RomFS text",
                    outputStatus,
                    $"Royal Candy name text patch for {textSet.Language}.");
            }
        }
    }

    private static IReadOnlyList<MessageTextSet> SelectSupportedTextOutputSets(
        ProjectPaths paths,
        IReadOnlyList<MessageTextSet> textSets)
    {
        var preferredLanguage = SwShGameTextLanguage.Resolve(paths);
        var preferred = textSets
            .Where(set => string.Equals(set.Language, preferredLanguage, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (preferred.Length > 0)
        {
            return preferred;
        }

        var english = textSets
            .Where(set => string.Equals(set.Language, SwShGameTextLanguage.English, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return english.Length > 0 ? english : textSets.Take(1).ToArray();
    }

    private static void AddUninstallWorkflow(
        OpenedProject project,
        RoyalCandyInstallationState installationState,
        ICollection<SwShRoyalCandyWorkflowRecord> workflows,
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        ICollection<SwShRoyalCandyOutputRecord> outputs)
    {
        var bagHookEntry = FindRoyalCandyBagHookOutput(project);
        var hasIdentifyingOutput = installationState.LayeredEntries.Count > 0 || bagHookEntry is not null;
        var cleanupBlockers = hasIdentifyingOutput
            ? SwShRoyalCandyCleanup.FindBlockingCleanupTargets(project)
            : Array.Empty<SwShRoyalCandyCleanupBlocker>();
        var shopEntries = hasIdentifyingOutput
            ? FindRoyalCandyShopOutputs(project)
            : Array.Empty<ProjectFileGraphEntry>();
        var itemHashEntry = hasIdentifyingOutput ? FindBaseIdenticalItemHashOutput(project) : null;
        var cleanupOutputCount = installationState.LayeredEntries.Count
            + shopEntries.Count
            + (itemHashEntry is null ? 0 : 1)
            + (bagHookEntry is null ? 0 : 1)
            + cleanupBlockers.Count;
        var hasOutputRoot = project.Health.CanOpenEditableWorkflows;
        var status = !hasOutputRoot
            ? "readOnly"
            : cleanupBlockers.Count > 0
                ? "blocked"
                : cleanupOutputCount > 0
                    ? "warning"
                    : "blocked";
        var cleanupMessage = !hasOutputRoot
            ? "LayeredFS output root is not configured; uninstall can only be inspected read-only."
            : cleanupBlockers.Count > 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Royal Candy cleanup is blocked because {cleanupBlockers.Count:N0} required layered target(s) cannot be verified for safe atomic cleanup.")
                : cleanupOutputCount > 0
                    ? installationState.LayeredEntries.Count > 0
                        ? installationState.Message
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"Detected {cleanupOutputCount:N0} Royal Candy cleanup target(s) in the configured output.")
                    : "No known Royal Candy output target was found in LayeredFS output.";
        var provenance = installationState.LayeredEntries.Count > 0
            ? CreateProvenance(installationState.LayeredEntries[0])
            : shopEntries.Count > 0
                ? CreateProvenance(shopEntries[0])
                : itemHashEntry is not null
                    ? CreateProvenance(itemHashEntry)
                    : bagHookEntry is not null
                        ? CreateProvenance(bagHookEntry)
                        : cleanupBlockers.Count > 0
                            ? CreateProvenance(cleanupBlockers[0].Entry)
                            : GeneratedProvenance;

        workflows.Add(new SwShRoyalCandyWorkflowRecord(
            UninstallWorkflowId,
            RemoveRoyalCandyName,
            "Cleanup",
            "LayeredFS output",
            "uninstall",
            RoyalCandyItemId,
            RareCandyItemId,
            status,
            "Safely removes reviewed Royal Candy output, restores the verified item 1128 mapping, clears Bag Hook slot 1, restores Royal Candy-owned shop entries, and restores only Royal Candy-owned ExeFS bytes.",
            Array.Empty<SwShRoyalCandyLevelCapRecord>(),
            [
                new(1, "Inspect output root", "Find known Royal Candy LayeredFS files without reading or changing base RomFS/ExeFS."),
                new(2, "Review leftovers", "Review detected Royal Candy output files and ExeFS state before cleanup."),
                new(3, "Clean LayeredFS output", "Remove reviewed Royal Candy output while preserving Bag Hook, Starting Items, and Catch Cap when present."),
            ],
            provenance));

        AddCheck(
            checks,
            $"{UninstallWorkflowId}:known-output",
            UninstallWorkflowId,
            cleanupBlockers.Count > 0 ? "Fail" : "Warning",
            "Output",
            "LayeredFS output root",
            cleanupMessage,
            provenance);

        foreach (var entry in installationState.LayeredEntries)
        {
            outputs.Add(CreateOutput(
                UninstallWorkflowId,
                entry.RelativePath,
                entry,
                entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase) ? "ExeFS NSO" : "LayeredFS output",
                "review",
                "Detected known Royal Candy output target for cleanup review."));
        }

        foreach (var shopEntry in shopEntries)
        {
            outputs.Add(CreateOutput(
                UninstallWorkflowId,
                shopEntry.RelativePath,
                shopEntry,
                "Shop data",
                "review",
                "Detected Royal Candy shop inventory patch; cleanup will restore only base Exp. Candy XL item 1128 shop entries."));
        }

        if (itemHashEntry is not null)
        {
            outputs.Add(CreateOutput(
                UninstallWorkflowId,
                itemHashEntry.RelativePath,
                itemHashEntry,
                "Item hash data",
                "review",
                "Detected a base-identical Royal Candy item hash override; cleanup will remove only this redundant layered file."));
        }

        if (bagHookEntry is not null)
        {
            outputs.Add(CreateOutput(
                UninstallWorkflowId,
                bagHookEntry.RelativePath,
                bagHookEntry,
                "Bag Hook slot",
                "review",
                "Detected Royal Candy in Bag Hook slot 1; cleanup will clear only that slot."));
        }

        foreach (var blocker in cleanupBlockers)
        {
            outputs.Add(CreateOutput(
                UninstallWorkflowId,
                blocker.Entry.RelativePath,
                blocker.Entry,
                "Blocked cleanup target",
                "blocked",
                blocker.Message));
        }
    }

    private static SwShRoyalCandyOutputRecord CreateOutput(
        string workflowId,
        string relativePath,
        ProjectFileGraphEntry? sourceEntry,
        string outputKind,
        string status,
        string description)
    {
        var provenance = sourceEntry is null ? CreateMissingProvenance(relativePath) : CreateProvenance(sourceEntry);
        return new SwShRoyalCandyOutputRecord(
            $"{workflowId}:{relativePath}",
            workflowId,
            relativePath,
            sourceEntry?.RelativePath ?? relativePath,
            outputKind,
            status,
            description,
            provenance);
    }

    private static void AddAggregateDiagnostics(
        ICollection<ValidationDiagnostic> diagnostics,
        IReadOnlyList<SwShRoyalCandyWorkflowCheckRecord> preflightChecks,
        RoyalCandyInstallationState installationState)
    {
        if (installationState.Kind == RoyalCandyInstallKind.UnknownConflict)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                installationState.Message,
                expected: "Remove or review conflicting Royal Candy target files before installing"));
        }
        else if (installationState.InstalledWorkflowId is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                installationState.Message,
                expected: "Use Remove Royal Candy before switching variants"));
        }
        else if (installationState.LayeredEntries.Count > 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                installationState.Message,
                expected: "Review known Royal Candy output targets before cleanup"));
        }

        if (preflightChecks.Any(check => check.Status == "Fail"))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Royal Candy preflight is blocked by missing or incompatible project inputs.",
                expected: "Required RomFS/ExeFS files and compatible patch anchors"));
            return;
        }

        if (preflightChecks.Any(check => check.Status == "Warning"))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Royal Candy preflight loaded with warnings that need review before output can be generated.",
                expected: "Known Sword/Shield project flavor, output root, and clean compatibility checks"));
        }
    }

    private static string DetermineInstallStatus(
        IReadOnlyList<SwShRoyalCandyWorkflowCheckRecord> preflightChecks,
        bool outputRootReady,
        RoyalCandyInstallationState installationState)
    {
        if (installationState.Kind == RoyalCandyInstallKind.UnknownConflict)
        {
            return "blocked";
        }

        if (preflightChecks.Any(check => check.Status == "Fail"))
        {
            return "blocked";
        }

        if (!outputRootReady)
        {
            return "readOnly";
        }

        return preflightChecks.Any(check => check.Status == "Warning")
            ? "warning"
            : "available";
    }

    private static RoyalCandyInstallationState DetectRoyalCandyInstallation(
        OpenedProject project,
        IReadOnlyList<MessageTextSet> selectedTextSets)
    {
        var layeredEntries = GetKnownRoyalCandyLayeredEntries(project)
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var identifyingEntries = layeredEntries
            .Where(entry => !string.Equals(entry.RelativePath, ItemHashPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (identifyingEntries.Length == 0)
        {
            return new RoyalCandyInstallationState(
                RoyalCandyInstallKind.None,
                null,
                layeredEntries,
                "No Royal Candy LayeredFS output was detected.");
        }

        var textDetection = DetectRoyalCandyTextInstallKind(project, identifyingEntries, selectedTextSets);
        if (textDetection.HasConflict)
        {
            return new RoyalCandyInstallationState(
                RoyalCandyInstallKind.UnknownConflict,
                null,
                layeredEntries,
                "LayeredFS output contains conflicting Royal Candy item-text variants across language sets. Remove the stale Royal Candy text output and reinstall one variant for the selected game-text language.");
        }

        var textInstallKind = textDetection.Kind;
        var exeFsInstallKind = DetectRoyalCandyExeFsInstallKind(project, identifyingEntries);
        if (textInstallKind is null || exeFsInstallKind is null)
        {
            var selectedLanguage = textDetection.SelectedLanguage ?? "selected";
            var detectedPart = (textInstallKind, exeFsInstallKind, textDetection.HasRecognizedText) switch
            {
                (not null, null, _) => "selected-language Royal Candy item text without a matching complete exefs/main signature",
                (null, not null, true) => $"a complete Royal Candy exefs/main signature with Royal Candy text only outside the selected {selectedLanguage} language set",
                (null, not null, false) => $"a complete Royal Candy exefs/main signature without matching Royal Candy item text in the selected {selectedLanguage} language set",
                (null, null, true) => $"Royal Candy item text outside the selected {selectedLanguage} language set without a matching complete exefs/main signature",
                _ => "known Royal Candy output that does not contain a complete selected-language text and exefs/main signature pair",
            };
            return new RoyalCandyInstallationState(
                RoyalCandyInstallKind.UnknownConflict,
                null,
                layeredEntries,
                $"LayeredFS output contains a partial Royal Candy installation: {detectedPart}. Review and remove the partial output before reinstalling.");
        }

        if (textInstallKind.Value != exeFsInstallKind.Value)
        {
            return new RoyalCandyInstallationState(
                RoyalCandyInstallKind.UnknownConflict,
                null,
                layeredEntries,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"LayeredFS output contains mixed Royal Candy targets: item text identifies {FormatRoyalCandyInstallKind(textInstallKind.Value)}, but exefs/main identifies {FormatRoyalCandyInstallKind(exeFsInstallKind.Value)}. Remove stale Royal Candy output and reinstall one variant."));
        }

        var installKind = exeFsInstallKind.Value;
        var workflowId = installKind == RoyalCandyInstallKind.StoryLimits
            ? StoryLimitsWorkflowId
            : UnlimitedWorkflowId;
        var name = installKind == RoyalCandyInstallKind.StoryLimits
            ? StoryLimitsRoyalCandyName
            : UnlimitedRoyalCandyName;
        return new RoyalCandyInstallationState(
            installKind,
            workflowId,
            layeredEntries,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{name} is installed in the configured LayeredFS output ({layeredEntries.Length:N0} known Royal Candy target file(s))."));
    }

    private static string FormatRoyalCandyInstallKind(RoyalCandyInstallKind installKind)
    {
        return installKind switch
        {
            RoyalCandyInstallKind.StoryLimits => StoryLimitsRoyalCandyName,
            RoyalCandyInstallKind.Unlimited => UnlimitedRoyalCandyName,
            _ => "unknown Royal Candy output",
        };
    }

    private static RoyalCandyTextInstallationDetection DetectRoyalCandyTextInstallKind(
        OpenedProject project,
        IReadOnlyList<ProjectFileGraphEntry> layeredEntries,
        IReadOnlyList<MessageTextSet> selectedTextSets)
    {
        var selectedItemInfoPaths = selectedTextSets
            .Select(textSet => textSet.ItemInfo.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recognized = new List<(string RelativePath, RoyalCandyInstallKind Kind)>();
        foreach (var entry in layeredEntries.Where(entry =>
            entry.RelativePath.EndsWith("/iteminfo.dat", StringComparison.OrdinalIgnoreCase)))
        {
            var sourcePath = ResolveSourcePath(project.Paths, entry);
            if (sourcePath is null || !File.Exists(sourcePath))
            {
                continue;
            }

            try
            {
                var text = SwShGameTextFile.Parse(File.ReadAllBytes(sourcePath));
                if (text.Lines.Count <= RoyalCandyItemId)
                {
                    continue;
                }

                var description = text.Lines[RoyalCandyItemId].Text;
                if (string.Equals(description, StoryLimitsRoyalCandyDescription, StringComparison.Ordinal))
                {
                    recognized.Add((entry.RelativePath, RoyalCandyInstallKind.StoryLimits));
                }
                else if (string.Equals(description, UnlimitedRoyalCandyDescription, StringComparison.Ordinal))
                {
                    recognized.Add((entry.RelativePath, RoyalCandyInstallKind.Unlimited));
                }
            }
            catch (InvalidDataException)
            {
            }
            catch (IOException)
            {
            }
        }

        var recognizedKinds = recognized
            .Select(candidate => candidate.Kind)
            .Distinct()
            .ToArray();
        var selectedKinds = recognized
            .Where(candidate => selectedItemInfoPaths.Contains(candidate.RelativePath))
            .Select(candidate => candidate.Kind)
            .Distinct()
            .ToArray();
        return new RoyalCandyTextInstallationDetection(
            selectedKinds.Length == 1 ? selectedKinds[0] : null,
            HasRecognizedText: recognized.Count > 0,
            HasConflict: recognizedKinds.Length > 1 || selectedKinds.Length > 1,
            SelectedLanguage: selectedTextSets.FirstOrDefault()?.Language);
    }

    private static RoyalCandyInstallKind? DetectRoyalCandyExeFsInstallKind(
        OpenedProject project,
        IReadOnlyList<ProjectFileGraphEntry> layeredEntries)
    {
        var entry = layeredEntries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            var signature = SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(
                File.ReadAllBytes(sourcePath),
                project.Paths.SelectedGame);
            return signature.Kind switch
            {
                SwShRoyalCandyExeFsSignatureKind.StoryLimits => RoyalCandyInstallKind.StoryLimits,
                SwShRoyalCandyExeFsSignatureKind.Unlimited => RoyalCandyInstallKind.Unlimited,
                _ => null,
            };
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static IReadOnlyDictionary<LevelCapMilestoneKey, int> ReadInstalledStoryLevelCapOverrides(
        OpenedProject project,
        IReadOnlyDictionary<string, ProjectFileGraphEntry> sourceMap,
        RoyalCandyInstallationState installationState,
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks)
    {
        if (installationState.Kind != RoyalCandyInstallKind.StoryLimits
            || !sourceMap.TryGetValue(ExeFsMainPath, out var entry))
        {
            return new Dictionary<LevelCapMilestoneKey, int>();
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return new Dictionary<LevelCapMilestoneKey, int>();
        }

        try
        {
            var installedCaps = SwShExeFsRoyalCandyMainPatcher.ReadInstalledStoryLevelCaps(
                File.ReadAllBytes(sourcePath),
                project.Paths.SelectedGame);
            var capMap = new Dictionary<LevelCapMilestoneKey, int>();
            foreach (var cap in installedCaps)
            {
                var key = new LevelCapMilestoneKey(
                    cap.ProgressHash,
                    FormatProgressKind(cap.ProgressKind),
                    cap.ProgressKind == SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast
                        ? cap.WorkMinimum
                        : 0);
                if (!capMap.TryAdd(key, cap.LevelCap))
                {
                    throw new InvalidDataException("The installed Royal Candy story ladder contains a duplicate milestone.");
                }
            }

            if (capMap.Count != DefaultLevelCapMilestones.Length)
            {
                throw new InvalidDataException(
                    $"The installed Royal Candy story ladder contains {capMap.Count} unique milestones; expected {DefaultLevelCapMilestones.Length}.");
            }

            var expectedKeys = DefaultLevelCapMilestones
                .Select(milestone => new LevelCapMilestoneKey(
                    milestone.ProgressHash,
                    milestone.ProgressKind,
                    milestone.ProgressKind == "workAtLeast" ? milestone.WorkMinimum : 0))
                .ToHashSet();
            if (!expectedKeys.SetEquals(capMap.Keys))
            {
                throw new InvalidDataException(
                    "The installed Royal Candy story ladder does not contain the exact supported story milestones.");
            }

            AddCheck(
                checks,
                $"{PreflightWorkflowId}:installed-story-ladder",
                PreflightWorkflowId,
                "Pass",
                "ExeFS",
                ExeFsMainPath,
                $"Read back all {capMap.Count} unique Royal Candy story-limit milestones from the installed executable.",
                CreateProvenance(entry));
            return capMap;
        }
        catch (InvalidDataException exception)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:installed-story-ladder",
                PreflightWorkflowId,
                "Fail",
                "ExeFS",
                ExeFsMainPath,
                $"Installed Royal Candy story limits could not be read safely: {exception.Message}",
                CreateProvenance(entry));
        }
        catch (IOException exception)
        {
            AddCheck(
                checks,
                $"{PreflightWorkflowId}:installed-story-ladder",
                PreflightWorkflowId,
                "Fail",
                "ExeFS",
                ExeFsMainPath,
                $"Installed Royal Candy story limits could not be read: {exception.Message}",
                CreateProvenance(entry));
        }

        return new Dictionary<LevelCapMilestoneKey, int>();
    }

    private static string FormatProgressKind(SwShRoyalCandyStoryLevelCapProgressKind progressKind)
    {
        return progressKind switch
        {
            SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast => "workAtLeast",
            _ => "flag",
        };
    }

    private static IEnumerable<ProjectFileGraphEntry> GetKnownRoyalCandyLayeredEntries(OpenedProject project)
    {
        foreach (var entry in project.FileGraph.Entries.Where(entry => entry.LayeredFile is not null))
        {
            if (string.Equals(entry.RelativePath, ItemPath, StringComparison.OrdinalIgnoreCase))
            {
                if (SwShRoyalCandyCleanup.HasRoyalCandyItemData(project, entry))
                {
                    yield return entry;
                }

                continue;
            }

            if (string.Equals(entry.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase))
            {
                if (HasRoyalCandyExeFsSignature(project, entry))
                {
                    yield return entry;
                }

                continue;
            }

            if (HasRoyalCandyItemText(project, entry))
            {
                yield return entry;
            }
        }
    }

    private static ProjectFileGraphEntry? FindBaseIdenticalItemHashOutput(OpenedProject project)
    {
        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            candidate.LayeredFile is not null
            && string.Equals(candidate.RelativePath, ItemHashPath, StringComparison.OrdinalIgnoreCase));
        return entry is not null && SwShRoyalCandyCleanup.IsOwnedItemHashOutput(project, entry)
            ? entry
            : null;
    }

    private static bool HasRoyalCandyItemText(OpenedProject project, ProjectFileGraphEntry entry)
    {
        if (!IsItemMessageOutputPath(entry.RelativePath)
            || !TryParseMessageCommonFile(entry.RelativePath, out _, out var fileName))
        {
            return false;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return false;
        }

        try
        {
            var text = SwShGameTextFile.Parse(File.ReadAllBytes(sourcePath));
            if (text.Lines.Count <= RoyalCandyItemId)
            {
                return false;
            }

            var value = text.Lines[RoyalCandyItemId].Text;
            if (fileName.StartsWith("itemname", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(value, AppliedRoyalCandyName, StringComparison.Ordinal)
                    || string.Equals(value, AppliedRoyalCandyPluralName, StringComparison.Ordinal)
                    || string.Equals(value, UnlimitedRoyalCandyName, StringComparison.Ordinal)
                    || string.Equals(value, StoryLimitsRoyalCandyName, StringComparison.Ordinal);
            }

            return string.Equals(value, UnlimitedRoyalCandyDescription, StringComparison.Ordinal)
                || string.Equals(value, StoryLimitsRoyalCandyDescription, StringComparison.Ordinal);
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    private static bool HasRoyalCandyExeFsSignature(OpenedProject project, ProjectFileGraphEntry entry)
    {
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return false;
        }

        try
        {
            var signature = SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(
                File.ReadAllBytes(sourcePath),
                project.Paths.SelectedGame);
            return signature.Kind is SwShRoyalCandyExeFsSignatureKind.Unlimited
                or SwShRoyalCandyExeFsSignatureKind.StoryLimits;
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    private static IReadOnlyList<MessageTextSet> DiscoverMessageTextSets(
        IEnumerable<ProjectFileGraphEntry> entries)
    {
        var builders = new Dictionary<string, MessageTextSetBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!TryParseMessageCommonFile(entry.RelativePath, out var language, out var fileName))
            {
                continue;
            }

            if (!builders.TryGetValue(language, out var builder))
            {
                builder = new MessageTextSetBuilder(language);
                builders.Add(language, builder);
            }

            if (string.Equals(fileName, "iteminfo.dat", StringComparison.OrdinalIgnoreCase))
            {
                builder.ItemInfo = entry;
            }
            else if (fileName.StartsWith("itemname", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                builder.ItemNames.Add(entry);
            }
        }

        return builders.Values
            .Where(builder => builder.ItemInfo is not null && builder.ItemNames.Count > 0)
            .OrderBy(builder => builder.Language, StringComparer.Ordinal)
            .Select(builder => new MessageTextSet(
                builder.Language,
                builder.ItemInfo!,
                builder.ItemNames
                    .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static bool TryParseMessageCommonFile(string relativePath, out string language, out string fileName)
    {
        language = string.Empty;
        fileName = string.Empty;

        var parts = relativePath.Split('/');
        if (parts.Length != 6
            || !string.Equals(parts[0], "romfs", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[1], "bin", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], "message", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[4], "common", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        language = parts[3];
        fileName = parts[5];
        return true;
    }

    private static ProjectFileGraphEntry? FindFirstEntry(OpenedProject project, IReadOnlyList<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                return entry;
            }
        }

        return null;
    }

    private static ProjectFileGraphEntry? FindSource(
        IReadOnlyDictionary<string, ProjectFileGraphEntry> sourceMap,
        params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            if (sourceMap.TryGetValue(relativePath, out var entry))
            {
                return entry;
            }
        }

        return null;
    }

    private static string ResolveShopOutputPath(IReadOnlyDictionary<string, ProjectFileGraphEntry> sourceMap)
    {
        return FindSource(sourceMap, ShopDataPath, LegacyShopDataPath)?.RelativePath ?? ShopDataPath;
    }

    private static ProjectFileGraphEntry? FindRoyalCandyBagHookOutput(OpenedProject project)
    {
        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            candidate.LayeredFile is not null
            && string.Equals(candidate.RelativePath, BagEventScriptPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            var analysis = SwShBagHookAmxPatcher.Analyze(File.ReadAllBytes(sourcePath));
            var slot = analysis.Slots.FirstOrDefault(slot => slot.Slot == SwShBagHookAmxPatcher.RoyalCandySlot);
            return slot?.ItemId == RoyalCandyItemId ? entry : null;
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static IReadOnlyList<ProjectFileGraphEntry> FindRoyalCandyShopOutputs(OpenedProject project)
    {
        return project.FileGraph.Entries
            .Where(entry => entry.LayeredFile is not null
                && (string.Equals(entry.RelativePath, ShopDataPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.RelativePath, LegacyShopDataPath, StringComparison.OrdinalIgnoreCase)))
            .Where(entry => HasRoyalCandyShopPatch(project, entry))
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasRoyalCandyShopPatch(OpenedProject project, ProjectFileGraphEntry entry)
    {
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        var basePath = ResolveBaseSourcePath(project.Paths, entry.RelativePath);
        if (sourcePath is null || basePath is null || !File.Exists(sourcePath) || !File.Exists(basePath))
        {
            return false;
        }

        try
        {
            var targetData = SwShShopDataFile.Parse(File.ReadAllBytes(sourcePath));
            var baseData = SwShShopDataFile.Parse(File.ReadAllBytes(basePath));
            return HasMissingBaseRoyalCandyShopEntry(targetData, baseData);
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    private static bool HasMissingBaseRoyalCandyShopEntry(SwShShopDataFile targetData, SwShShopDataFile baseData)
    {
        return SwShRoyalCandyShopPatchMapper.Analyze(targetData, baseData).MissingOccurrences > 0;
    }

    private static bool IsItemMessageOutputPath(string relativePath)
    {
        return TryParseMessageCommonFile(relativePath, out _, out var fileName)
            && (string.Equals(fileName, "iteminfo.dat", StringComparison.OrdinalIgnoreCase)
                || (fileName.StartsWith("itemname", StringComparison.OrdinalIgnoreCase)
                    && fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)));
    }

    private static SwShRoyalCandyProvenance SelectPrimaryProvenance(
        IReadOnlyList<ProjectFileGraphEntry> sourceEntries)
    {
        var entry = sourceEntries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, ItemPath, StringComparison.OrdinalIgnoreCase))
            ?? sourceEntries.FirstOrDefault();
        return entry is null ? GeneratedProvenance : CreateProvenance(entry);
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, entry.RelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string relativePath)
    {
        if (relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, relativePath["romfs/".Length..]);
        }

        if (relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, relativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShRoyalCandyProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShRoyalCandyProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShRoyalCandyProvenance CreateMissingProvenance(string relativePath)
    {
        return new SwShRoyalCandyProvenance(
            relativePath,
            ProjectFileLayer.Generated,
            ProjectFileGraphEntryState.BaseOnly);
    }

    private static void AddCheck(
        ICollection<SwShRoyalCandyWorkflowCheckRecord> checks,
        string checkId,
        string workflowId,
        string status,
        string area,
        string target,
        string message,
        SwShRoyalCandyProvenance provenance)
    {
        checks.Add(new SwShRoyalCandyWorkflowCheckRecord(
            checkId,
            workflowId,
            status,
            area,
            target,
            message,
            provenance));
    }

    private static string FormatGameFlavor(string gameFlavor)
    {
        return gameFlavor == "unknown" ? "Sword/Shield" : gameFlavor;
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.RoyalCandy,
            "Royal Candy Workflows",
            "Requires Bag Hook, uses Bag Hook slot 1, and patches reserved Royal Candy ExeFS regions. Use Remove Royal Candy to uninstall safely.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: "workflow.royalCandy",
            Expected: expected);
    }

    private sealed record FileRequirement(
        string CheckId,
        string Area,
        string Name,
        IReadOnlyList<string> RelativePaths,
        string OutputKind,
        string Description);

    private sealed record LevelCapMilestoneDefinition(
        int DefaultCap,
        ulong ProgressHash,
        string SwordName,
        string ShieldName,
        string ProgressKind = "flag",
        int WorkMinimum = 0);

    private readonly record struct LevelCapMilestoneKey(
        ulong ProgressHash,
        string ProgressKind,
        int WorkMinimum);

    private enum RoyalCandyInstallKind
    {
        None,
        Unlimited,
        StoryLimits,
        UnknownConflict,
    }

    private sealed record RoyalCandyInstallationState(
        RoyalCandyInstallKind Kind,
        string? InstalledWorkflowId,
        IReadOnlyList<ProjectFileGraphEntry> LayeredEntries,
        string Message)
    {
        public bool BlocksInstallation => Kind is RoyalCandyInstallKind.Unlimited
            or RoyalCandyInstallKind.StoryLimits
            or RoyalCandyInstallKind.UnknownConflict;
    }

    private sealed record RoyalCandyTextInstallationDetection(
        RoyalCandyInstallKind? Kind,
        bool HasRecognizedText,
        bool HasConflict,
        string? SelectedLanguage);

    private sealed record MessageTextSet(
        string Language,
        ProjectFileGraphEntry ItemInfo,
        IReadOnlyList<ProjectFileGraphEntry> ItemNames);

    private sealed class MessageTextSetBuilder(string language)
    {
        public string Language { get; } = language;

        public ProjectFileGraphEntry? ItemInfo { get; set; }

        public List<ProjectFileGraphEntry> ItemNames { get; } = [];
    }
}
