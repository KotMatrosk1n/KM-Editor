// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
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
        new("item-data", "RomFS", "Item data", [ItemPath], "RomFS data", "Clones the Rare Candy template row into the Royal Candy item slot."),
        new("item-hash", "RomFS", "Item hash table", [ItemHashPath], "RomFS data", "Updates item hash lookup data for the Royal Candy item slot."),
        new("shop-data", "RomFS", "Shop data", [ShopDataPath, LegacyShopDataPath], "RomFS data", "Adds controlled acquisition entries to shop data where the workflow supports it."),
        new("nest-data", "RomFS", "Raid reward data", [NestDataPath], "RomFS archive", "Adds controlled acquisition entries to raid reward data."),
        new("placement-data", "RomFS", "Placement data", [PlacementPath], "RomFS archive", "Adds controlled pickup placement entries."),
        new("bag-event-script", "RomFS", "Bag event script", [BagEventScriptPath], "RomFS script", "Validates the bag event script source used by the grant workflow."),
        new("exefs-main", "ExeFS", "ExeFS main", [ExeFsMainPath], "ExeFS NSO", "Validates the NSO source used by Royal Candy UI and usage patches."),
        new("exefs-npdm", "ExeFS", "ExeFS NPDM", [ExeFsNpdmPath], "ExeFS metadata", "Detects whether the project is Pokemon Sword or Pokemon Shield."),
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
        new(42, 0xC07B67FC3148B754, "Bea 077", "Bea 077"),
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

    public SwShRoyalCandyWorkflowService(SwShExeFsPatchWorkflowService? exeFsPatchWorkflowService = null)
    {
        this.exeFsPatchWorkflowService = exeFsPatchWorkflowService ?? new SwShExeFsPatchWorkflowService();
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

        AddMessageTextSetCheck(checks, textSets);

        var gameFlavor = AddNpdmFlavorCheck(project, checks, sourceMap, sourceEntries);
        var installationState = DetectRoyalCandyInstallation(project);
        var exeFsWorkflow = exeFsPatchWorkflowService.Load(project);
        AddExeFsCompatibilityChecks(checks, exeFsWorkflow, installationState);

        var outputRootReady = project.Health.CanOpenEditableWorkflows
            && !string.IsNullOrWhiteSpace(project.Paths.OutputRootPath);
        AddOutputRootCheck(checks, outputRootReady);

        var preflightChecks = checks.Where(check => check.WorkflowId == PreflightWorkflowId).ToArray();
        var installStatus = DetermineInstallStatus(preflightChecks, outputRootReady, installationState);
        var workflows = CreateWorkflows(installStatus, gameFlavor, SelectPrimaryProvenance(sourceEntries), installationState).ToList();
        var outputs = new List<SwShRoyalCandyOutputRecord>();

        outputs.AddRange(CreateInstallOutputs(UnlimitedWorkflowId, installStatus, sourceMap, textSets));
        outputs.AddRange(CreateInstallOutputs(StoryLimitsWorkflowId, installStatus, sourceMap, textSets));
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
        IReadOnlyList<MessageTextSet> textSets)
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
            if (installationState.InstalledWorkflowId is not null && check.Status == "Fail")
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
        RoyalCandyInstallationState installationState)
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
                $"Prepares Royal Candy item {RoyalCandyItemId} from Rare Candy item {RareCandyItemId} with unlimited-use behavior for Pokemon {FormatGameFlavor(gameFlavor)} projects.",
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
                $"Prepares Royal Candy item {RoyalCandyItemId} with story-cap checks for Pokemon {FormatGameFlavor(gameFlavor)} projects.",
                CreateLevelCaps(gameFlavor),
                CreateInstallSteps(includeStoryLimits: true),
                provenance),
        ];
    }

    private static string GetInstallWorkflowStatus(
        string workflowId,
        string installStatus,
        RoyalCandyInstallationState installationState)
    {
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
        var useShieldNames = string.Equals(gameFlavor, "Shield", StringComparison.OrdinalIgnoreCase);
        return DefaultLevelCapMilestones
            .Select((definition, index) => new SwShRoyalCandyLevelCapRecord(
                Slot: index,
                MilestoneId: string.Create(
                    CultureInfo.InvariantCulture,
                    $"{index}:{definition.ProgressHash:X16}:{definition.WorkMinimum}"),
                Label: useShieldNames ? definition.ShieldName : definition.SwordName,
                LevelCap: definition.DefaultCap,
                MinimumLevelCap: MinimumStoryLevelCap,
                MaximumLevelCap: MaximumStoryLevelCap,
                ProgressKind: definition.ProgressKind,
                ProgressHash: string.Create(CultureInfo.InvariantCulture, $"0x{definition.ProgressHash:X16}"),
                WorkMinimum: definition.ProgressKind == "workAtLeast" ? definition.WorkMinimum : null))
            .ToArray();
    }

    private static IReadOnlyList<SwShRoyalCandyWorkflowStepRecord> CreateInstallSteps(bool includeStoryLimits)
    {
        var steps = new List<SwShRoyalCandyWorkflowStepRecord>
        {
            new(1, "Validate sources", "Resolve required RomFS files, ExeFS main, main.npdm, and item text language sets from the project graph."),
            new(2, "Prepare item records", $"Clone item {RareCandyItemId} into item {RoyalCandyItemId} and update item hash lookup data."),
            new(3, "Patch item text", "Patch Royal Candy names and descriptions in every discovered item text language set."),
            new(4, "Plan acquisition edits", "Plan controlled shop, raid reward, placement, and bag event script output targets."),
            new(5, "Validate ExeFS anchors", "Reuse backend ExeFS compatibility checks for the Rare Candy UI route, Royal Candy support, and code-cave readiness."),
        };

        if (includeStoryLimits)
        {
            steps.Add(new(6, "Apply story limits", "Use story-cap flag milestones and a default cap before enabling higher Royal Candy levels."));
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
            "readOnly" => "readOnly",
            _ => "blocked",
        };

        yield return CreateOutput(workflowId, ItemPath, FindSource(sourceMap, ItemPath), "RomFS data", outputStatus, "Royal Candy item row patch.");
        yield return CreateOutput(workflowId, ItemHashPath, FindSource(sourceMap, ItemHashPath), "RomFS data", outputStatus, "Royal Candy item hash lookup patch.");
        yield return CreateOutput(workflowId, ResolveShopOutputPath(sourceMap), FindSource(sourceMap, ShopDataPath, LegacyShopDataPath), "RomFS data", outputStatus, "Royal Candy acquisition shop patch.");
        yield return CreateOutput(workflowId, NestDataPath, FindSource(sourceMap, NestDataPath), "RomFS archive", outputStatus, "Royal Candy raid reward patch.");
        yield return CreateOutput(workflowId, PlacementPath, FindSource(sourceMap, PlacementPath), "RomFS archive", outputStatus, "Royal Candy pickup placement patch.");
        yield return CreateOutput(workflowId, BagEventScriptPath, FindSource(sourceMap, BagEventScriptPath), "RomFS script", outputStatus, "Royal Candy bag event grant patch.");
        yield return CreateOutput(workflowId, ExeFsMainPath, FindSource(sourceMap, ExeFsMainPath), "ExeFS NSO", outputStatus, "Royal Candy ExeFS UI and usage patch.");

        foreach (var textSet in SelectSupportedTextOutputSets(textSets))
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

    private static IReadOnlyList<MessageTextSet> SelectSupportedTextOutputSets(IReadOnlyList<MessageTextSet> textSets)
    {
        var english = textSets
            .Where(set => string.Equals(set.Language, "English", StringComparison.OrdinalIgnoreCase))
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
        var hasOutputRoot = project.Health.CanOpenEditableWorkflows;
        var status = !hasOutputRoot
            ? "readOnly"
            : installationState.LayeredEntries.Count > 0
                ? "warning"
                : "blocked";
        var provenance = installationState.LayeredEntries.Count > 0
            ? CreateProvenance(installationState.LayeredEntries[0])
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
            "Inspects known Royal Candy output targets and prepares a conservative cleanup workflow once a matching output is present.",
            Array.Empty<SwShRoyalCandyLevelCapRecord>(),
            [
                new(1, "Inspect output root", "Find known Royal Candy LayeredFS files without reading or changing base RomFS/ExeFS."),
                new(2, "Review leftovers", "Review detected Royal Candy output files and ExeFS state before cleanup."),
                new(3, "Clean LayeredFS output", "Remove only reviewed LayeredFS output files through a future backend-owned cleanup plan."),
            ],
            provenance));

        AddCheck(
            checks,
            $"{UninstallWorkflowId}:known-output",
            UninstallWorkflowId,
            "Warning",
            "Output",
            "LayeredFS output root",
            !hasOutputRoot
                ? "LayeredFS output root is not configured; uninstall can only be inspected read-only."
                : installationState.LayeredEntries.Count > 0
                    ? installationState.Message
                    : "No known Royal Candy output target was found in LayeredFS output.",
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

    private static RoyalCandyInstallationState DetectRoyalCandyInstallation(OpenedProject project)
    {
        var layeredEntries = project.FileGraph.Entries
            .Where(entry => entry.LayeredFile is not null && IsKnownRoyalCandyOutputPath(entry.RelativePath))
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();

        if (layeredEntries.Length == 0)
        {
            return new RoyalCandyInstallationState(
                RoyalCandyInstallKind.None,
                null,
                layeredEntries,
                "No Royal Candy LayeredFS output was detected.");
        }

        var installKind = DetectRoyalCandyTextInstallKind(project, layeredEntries)
            ?? DetectRoyalCandyExeFsInstallKind(project, layeredEntries);
        if (installKind is not null)
        {
            var workflowId = installKind.Value == RoyalCandyInstallKind.StoryLimits
                ? StoryLimitsWorkflowId
                : UnlimitedWorkflowId;
            var name = installKind.Value == RoyalCandyInstallKind.StoryLimits
                ? StoryLimitsRoyalCandyName
                : UnlimitedRoyalCandyName;
            return new RoyalCandyInstallationState(
                installKind.Value,
                workflowId,
                layeredEntries,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{name} is installed in the configured LayeredFS output ({layeredEntries.Length:N0} known Royal Candy target file(s))."));
        }

        return new RoyalCandyInstallationState(
            RoyalCandyInstallKind.UnknownConflict,
            null,
            layeredEntries,
            string.Create(
                CultureInfo.InvariantCulture,
                $"LayeredFS output contains {layeredEntries.Length:N0} Royal Candy target file(s), but KM could not identify them as Unlimited Royal Candy or Royal Candy with Story Limits."));
    }

    private static RoyalCandyInstallKind? DetectRoyalCandyTextInstallKind(
        OpenedProject project,
        IReadOnlyList<ProjectFileGraphEntry> layeredEntries)
    {
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
                    return RoyalCandyInstallKind.StoryLimits;
                }

                if (string.Equals(description, UnlimitedRoyalCandyDescription, StringComparison.Ordinal))
                {
                    return RoyalCandyInstallKind.Unlimited;
                }
            }
            catch (InvalidDataException)
            {
            }
            catch (IOException)
            {
            }
        }

        return null;
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
            var nso = SwShNsoFile.Parse(File.ReadAllBytes(sourcePath));
            var text = nso.Text.DecompressedData;
            var royalCandyImmediateHits =
                CountAlignedInstruction(text, EncodeCmpImmediate(8, RoyalCandyItemId))
                + CountAlignedInstruction(text, EncodeCmpImmediate(9, RoyalCandyItemId))
                + CountAlignedInstruction(text, EncodeCmpImmediate(19, RoyalCandyItemId))
                + CountAlignedInstruction(text, EncodeCmpImmediate(20, RoyalCandyItemId))
                + CountAlignedInstruction(text, EncodeCmpImmediate(22, RoyalCandyItemId))
                + CountAlignedInstruction(text, EncodeCmpImmediate(23, RoyalCandyItemId))
                + CountAlignedInstruction(text, EncodeCmpImmediate(27, RoyalCandyItemId))
                + CountAlignedInstruction(text, EncodeCmpImmediate(28, RoyalCandyItemId));
            if (royalCandyImmediateHits == 0)
            {
                return null;
            }

            const int storyInventoryClampSelectOffset = 0x007BAF3C;
            if (storyInventoryClampSelectOffset + sizeof(uint) <= text.Length)
            {
                var instruction = BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(storyInventoryClampSelectOffset, sizeof(uint)));
                if ((instruction & 0x7C000000) == 0x14000000)
                {
                    return RoyalCandyInstallKind.StoryLimits;
                }
            }

            return RoyalCandyInstallKind.Unlimited;
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static int CountAlignedInstruction(ReadOnlySpan<byte> text, uint instruction)
    {
        var count = 0;
        for (var offset = 0; offset + sizeof(uint) <= text.Length; offset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]) == instruction)
            {
                count++;
            }
        }

        return count;
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
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

    private static bool IsKnownRoyalCandyOutputPath(string relativePath)
    {
        return string.Equals(relativePath, ItemPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, ItemHashPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, ShopDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, LegacyShopDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, NestDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, PlacementPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, BagEventScriptPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
            || IsItemMessageOutputPath(relativePath);
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
            "Royal Candy source readiness, ExeFS compatibility, and LayeredFS output preview.",
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
