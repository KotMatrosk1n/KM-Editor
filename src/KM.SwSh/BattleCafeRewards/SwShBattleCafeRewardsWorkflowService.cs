// SPDX-License-Identifier: GPL-3.0-only

using System.Security;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.GameModules;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.BattleCafeRewards;

public sealed class SwShBattleCafeRewardsWorkflowService
{
    public const string EditDomain = "workflow.battleCafeRewards";

    private const int MaximumSourceBytes = 4 * 1024 * 1024;
    private const int MaximumItemOptions = 10_000;
    private readonly SwShItemsWorkflowService itemsWorkflowService;

    public SwShBattleCafeRewardsWorkflowService(
        SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Battle Cafe Rewards requires a Pokemon Sword or Pokemon Shield project.",
                    SwShBattleCafeRewardsDiagnosticCodes.ProjectUnsupported,
                    expected: "Pokemon Sword or Pokemon Shield project"));
        }

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Battle Cafe Rewards requires readable base project sources.",
                    SwShBattleCafeRewardsDiagnosticCodes.SourceUnavailable,
                    expected: "Readable base RomFS and ExeFS sources"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShBattleCafeRewardsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return Empty(summary, diagnostics);
        }

        try
        {
            var loaded = LoadVerified(project);
            return new SwShBattleCafeRewardsWorkflow(
                summary,
                loaded.Rewards,
                loaded.ItemOptions,
                Totals(loaded.Rewards),
                loaded.Provenance,
                diagnostics);
        }
        catch (SwShBattleCafeItemCatalogException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Battle Cafe reward item choices could not be loaded from the current project.",
                SwShBattleCafeRewardsDiagnosticCodes.ItemCatalogUnavailable,
                file: SwShItemsWorkflowService.ItemDataPath,
                expected: "Readable bounded Sword and Shield item catalog"));
        }
        catch (FileNotFoundException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The verified Battle Cafe reward source is unavailable.",
                SwShBattleCafeRewardsDiagnosticCodes.SourceUnavailable,
                file: SwShBattleCafeRewardSourceReader.SourceRelativePath,
                expected: "Verified Battle Cafe AMX source"));
        }
        catch (Exception exception) when (IsSourceException(exception))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The Battle Cafe reward source does not match the verified layout.",
                SwShBattleCafeRewardsDiagnosticCodes.SourceUnsupported,
                file: SwShBattleCafeRewardSourceReader.SourceRelativePath,
                expected: "Exact bounded 23 row Battle Cafe reward source"));
        }

        return Empty(summary with { Availability = SwShWorkflowAvailability.ReadOnly }, diagnostics);
    }

    internal SwShBattleCafeRewardsLoadedSource LoadVerified(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            throw new InvalidDataException("Battle Cafe Rewards requires a Sword or Shield project.");
        }

        var itemWorkflow = itemsWorkflowService.Load(project);
        if (itemWorkflow.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || itemWorkflow.Items.Count is 0 or > MaximumItemOptions)
        {
            throw new SwShBattleCafeItemCatalogException();
        }

        var itemOptions = itemWorkflow.Items
            .Where(item => item.ItemId is > 0 and <= ushort.MaxValue)
            .Where(item => IsSafeItemName(item.Name))
            .Select(item => new SwShBattleCafeRewardsItemOption(
                item.ItemId,
                item.Name.Trim(),
                NormalizeCategory(item.Category)))
            .DistinctBy(item => item.ItemId)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ItemId)
            .ToArray();
        if (itemOptions.Length is 0 or > MaximumItemOptions)
        {
            throw new SwShBattleCafeItemCatalogException();
        }

        var itemNames = itemOptions.ToDictionary(item => item.ItemId, item => item.Name);
        var entry = project.FileGraph.Entries.SingleOrDefault(candidate =>
            string.Equals(
                candidate.RelativePath,
                SwShBattleCafeRewardSourceReader.SourceRelativePath,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("The Battle Cafe source is unavailable.");
        var sourcePath = ResolveSourcePath(project.Paths, entry)
            ?? throw new FileNotFoundException("The Battle Cafe source is unavailable.");
        var bytes = ReadBounded(sourcePath);
        var rewards = SwShBattleCafeRewardSourceReader.Parse(bytes, itemNames).Rewards;
        if (rewards.Count != 23)
        {
            throw new InvalidDataException("The Battle Cafe reward row count is unsupported.");
        }

        var sourceLayer = entry.LayeredFile is null
            ? ProjectFileLayer.Base
            : ProjectFileLayer.Layered;
        return new SwShBattleCafeRewardsLoadedSource(
            bytes,
            itemNames,
            rewards,
            itemOptions,
            new SwShBattleCafeRewardsProvenance(
                entry.RelativePath,
                sourceLayer,
                entry.State),
            new ProjectFileReference(sourceLayer, entry.RelativePath));
    }

    internal IReadOnlyList<ProjectFileReference> GetPlanSources(
        OpenedProject project,
        SwShBattleCafeRewardsLoadedSource loaded)
    {
        var sources = new List<ProjectFileReference> { loaded.EffectiveSource };
        AddEffectiveSource(project, SwShItemsWorkflowService.ItemDataPath, sources);

        var language = SwShGameTextLanguage.Resolve(project.Paths);
        var localizedNames = SwShGameTextLanguage.CommonMessagePath(language, "itemname.dat");
        if (!AddEffectiveSource(project, localizedNames, sources)
            && !string.Equals(language, SwShGameTextLanguage.English, StringComparison.OrdinalIgnoreCase))
        {
            AddEffectiveSource(
                project,
                SwShGameTextLanguage.CommonMessagePath(
                    SwShGameTextLanguage.English,
                    "itemname.dat"),
                sources);
        }

        return sources
            .DistinctBy(source => (source.Layer, source.RelativePath.ToUpperInvariant()))
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string? ResolveOutputPath(ProjectPaths paths)
    {
        return ResolveContainedPath(
            paths.OutputRootPath,
            SwShBattleCafeRewardSourceReader.SourceRelativePath);
    }

    private static bool AddEffectiveSource(
        OpenedProject project,
        string relativePath,
        ICollection<ProjectFileReference> sources)
    {
        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null || ResolveSourcePath(project.Paths, entry) is null)
        {
            return false;
        }

        sources.Add(new ProjectFileReference(
            entry.LayeredFile is null ? ProjectFileLayer.Base : ProjectFileLayer.Layered,
            entry.RelativePath));
        return true;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null)
        {
            return ResolveContainedPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is null
            || !entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ResolveContainedPath(
            paths.BaseRomFsPath,
            entry.RelativePath["romfs/".Length..]);
    }

    private static string? ResolveContainedPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)
            || string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var candidate = Path.GetFullPath(Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (PathContainment.IsOutsideRoot(Path.GetRelativePath(root, candidate))
                || TraversesReparsePoint(root, candidate))
            {
                return null;
            }

            return candidate;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or SecurityException or
            ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static byte[] ReadBounded(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists
            || file.Attributes.HasFlag(FileAttributes.ReparsePoint)
            || !string.IsNullOrEmpty(file.LinkTarget)
            || file.Length is < 1 or > MaximumSourceBytes)
        {
            throw new InvalidDataException("The Battle Cafe source is missing, linked, or oversized.");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        var length = stream.Length;
        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1 || stream.Length != length)
        {
            throw new InvalidDataException("The Battle Cafe source changed during bounded load.");
        }

        return bytes;
    }

    private static bool TraversesReparsePoint(string root, string path)
    {
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, path).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            entry.Refresh();
            if (entry.Exists
                && (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    || !string.IsNullOrEmpty(entry.LinkTarget)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSafeItemName(string name)
    {
        var value = name.Trim();
        return value.Length is > 0 and <= 128
            && !value.Contains("???", StringComparison.Ordinal)
            && !value.Contains('\r')
            && !value.Contains('\n')
            && !value.Contains('\0');
    }

    private static string NormalizeCategory(string category)
    {
        var value = category.Trim();
        return value.Length is > 0 and <= 128
            && !value.Contains('\r')
            && !value.Contains('\n')
            && !value.Contains('\0')
                ? value
                : "Other";
    }

    private static bool IsSupportedGame(ProjectGame? game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
    }

    private static bool IsSourceException(Exception exception)
    {
        return exception is
            InvalidDataException or OverflowException or IOException or
            UnauthorizedAccessException or SecurityException;
    }

    private static SwShBattleCafeRewardsTotals Totals(
        IReadOnlyList<SwShBattleCafeRewardEntry> rewards)
    {
        return new SwShBattleCafeRewardsTotals(
            rewards.Sum(reward => reward.DwightPercent),
            rewards.Sum(reward => reward.BernardPercent),
            rewards.Sum(reward => reward.RichardPercent));
    }

    private static SwShBattleCafeRewardsWorkflow Empty(
        SwShWorkflowSummary summary,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShBattleCafeRewardsWorkflow(
            summary,
            [],
            [],
            new SwShBattleCafeRewardsTotals(0, 0, 0),
            null,
            diagnostics);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.BattleCafeRewards,
            "Battle Cafe Rewards",
            "Edit the verified 23 row reward table and preserve its exact owner branches.",
            availability,
            diagnostics);
    }

    internal static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string code,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: EditDomain,
            Field: field,
            Expected: expected)
        {
            Code = code,
        };
    }

}

internal sealed class SwShBattleCafeItemCatalogException : Exception;
