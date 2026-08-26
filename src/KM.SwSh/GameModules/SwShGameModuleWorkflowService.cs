// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.ExeFs;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.NpcItemGift;
using KM.SwSh.Placement;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Workflows;

namespace KM.SwSh.GameModules;

public sealed class SwShGameModuleWorkflowService
{
    private const int MaximumSourceFiles = 20_000;
    private const long MaximumSourceBytesPerFile = 512L * 1024L * 1024L;
    private const long MaximumAggregateSourceBytes = 2L * 1024L * 1024L * 1024L;

    public SwShGameModuleWorkflowBatch LoadFreshBounded(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            throw new InvalidDataException(
                "Sword and Shield game modules require a matching selected project game.");
        }

        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        var sourceReader = new BoundedSourceReader(paths);
        PreflightKnownSources(project, sourceReader);

        var items = new SwShItemsWorkflowService(sourceReader.ReadAllBytes);
        var itemWorkflow = items.Load(project);
        var bagHook = new SwShBagHookWorkflowService(items, sourceReader.ReadAllBytes);
        var raidRewards = new SwShRaidRewardsWorkflowService(sourceReader.ReadAllBytes);
        var shops = new SwShShopsWorkflowService(items, sourceReader.ReadAllBytes);
        var parsedDataCache = new SwShParsedDataCache();
        var exeFs = new SwShExeFsPatchWorkflowService(parsedDataCache);
        var royalCandy = new SwShRoyalCandyWorkflowService(exeFs, bagHook);

        var rewardSources = new SwShRewardEcosystemWorkflowSources(
            new SwShNpcItemGiftWorkflowService(bagHook, items).Load(project),
            raidRewards.Load(project),
            raidRewards.LoadBonus(project),
            shops.Load(project),
            new SwShPlacementWorkflowService().Load(project));

        return new SwShGameModuleWorkflowBatch(
            rewardSources,
            exeFs.Load(project),
            new SwShDynamaxAdventuresWorkflowService().Load(project),
            new SwShRentalPokemonWorkflowService(sourceReader.ReadAllBytes).Load(project),
            royalCandy.Load(project),
            SwShBattleCafeRewardSourceReader.Load(
                project,
                sourceReader.ReadAllBytes,
                itemWorkflow.Items.ToDictionary(item => item.ItemId, item => item.Name)),
            SwShTrainerTypeEventAssignmentSourceReader.Load(
                project,
                sourceReader.ReadAllBytes));
    }

    private static void PreflightKnownSources(
        OpenedProject project,
        BoundedSourceReader sourceReader)
    {
        var sourceEntries = project.FileGraph.Entries
            .Where(entry => IsKnownSourcePath(entry.RelativePath))
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (sourceEntries.Length > MaximumSourceFiles)
        {
            throw new InvalidDataException("Sword and Shield game-module sources exceed the bounded file limit.");
        }

        foreach (var entry in sourceEntries)
        {
            if (entry.BaseFile is not null)
            {
                sourceReader.Observe(ResolveBasePath(project.Paths, entry.RelativePath));
            }

            if (entry.LayeredFile is not null)
            {
                sourceReader.Observe(ResolveLayeredPath(project.Paths, entry.RelativePath)!);
            }
        }

        var ownershipManifest = ResolveLayeredPath(
            project.Paths,
            SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath,
            required: false);
        if (ownershipManifest is not null && File.Exists(ownershipManifest))
        {
            sourceReader.Observe(ownershipManifest);
        }
    }

    internal static bool IsKnownSourcePath(string relativePath)
    {
        if (string.Equals(relativePath, SwShExeFsPatchWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRentalPokemonWorkflowService.RentalPokemonDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShPersonalTable.PersonalDataRelativePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShPokemonLearnsetTable.LearnsetDataRelativePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShItemsWorkflowService.ItemDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemHashPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.ShopDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.LegacyShopDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.NestDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.PlacementPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShBattleCafeRewardSourceReader.SourceRelativePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShStaticEncountersWorkflowService.StaticEncounterDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, "romfs/bin/trainer/trainer_id_hash_table.tbl", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (relativePath.StartsWith(
                SwShMoveDataFile.MoveDataRelativeDirectory.TrimEnd('/') + '/',
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(
                SwShTrainerTypeEventAssignmentSourceReader.SourceRootRelativePath.TrimEnd('/') + '/',
                StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("romfs/bin/flagwork/", StringComparison.OrdinalIgnoreCase)
                && relativePath.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (SwShNpcItemGiftWorkflowService.Gifts.Any(gift =>
                string.Equals(gift.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!relativePath.StartsWith("romfs/bin/message/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(relativePath.Replace('/', Path.DirectorySeparatorChar));
        return string.Equals(fileName, "iteminfo.dat", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("itemname", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "wazaname.dat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "monsname.dat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "tokusei.dat", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveBasePath(ProjectPaths paths, string relativePath)
    {
        if (relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveContainedPath(
                paths.BaseRomFsPath,
                relativePath["romfs/".Length..],
                required: true)!;
        }

        if (relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveContainedPath(
                paths.BaseExeFsPath,
                relativePath["exefs/".Length..],
                required: true)!;
        }

        throw new InvalidDataException("A Sword and Shield game-module source is outside a configured base root.");
    }

    private static string? ResolveLayeredPath(
        ProjectPaths paths,
        string relativePath,
        bool required = true)
    {
        return ResolveContainedPath(paths.OutputRootPath, relativePath, required);
    }

    private static string? ResolveContainedPath(
        string? rootPath,
        string relativePath,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            if (required)
            {
                throw new InvalidDataException("A Sword and Shield game-module source root is unavailable.");
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("A Sword and Shield game-module source path is invalid.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var fullPath = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(root, fullPath)))
        {
            throw new InvalidDataException("A Sword and Shield game-module source path escapes its configured root.");
        }

        return fullPath;
    }

    private sealed class BoundedSourceReader
    {
        private readonly string[] roots;
        private readonly HashSet<string> observedPaths = new(StringComparer.OrdinalIgnoreCase);
        private long observedBytes;

        public BoundedSourceReader(ProjectPaths paths)
        {
            roots = new[] { paths.BaseRomFsPath, paths.BaseExeFsPath, paths.OutputRootPath }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path!)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (roots.Length < 2)
            {
                throw new InvalidDataException("Sword and Shield game modules require configured base source roots.");
            }
        }

        public byte[] ReadAllBytes(string path)
        {
            var fullPath = ValidatePath(path);
            using var stream = OpenBounded(fullPath);
            var length = stream.Length;
            ObserveLength(fullPath, length);
            var result = new byte[checked((int)length)];
            stream.ReadExactly(result);
            if (stream.ReadByte() != -1 || stream.Length != length)
            {
                throw new InvalidDataException(
                    "A Sword and Shield game-module source changed while it was read.");
            }

            return result;
        }

        public void Observe(string path)
        {
            var fullPath = ValidatePath(path);
            using var stream = OpenBounded(fullPath);
            ObserveLength(fullPath, stream.Length);
        }

        private string ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                throw new InvalidDataException("A Sword and Shield game-module source path is invalid.");
            }

            var fullPath = Path.GetFullPath(path);
            var root = roots.FirstOrDefault(candidate =>
                !PathContainment.IsOutsideRoot(Path.GetRelativePath(candidate, fullPath)));
            if (root is null || TraversesReparsePoint(root, fullPath))
            {
                throw new InvalidDataException(
                    "A Sword and Shield game-module source is outside its configured root or traverses a linked path.");
            }

            return fullPath;
        }

        private static FileStream OpenBounded(string fullPath)
        {
            var file = new FileInfo(fullPath);
            file.Refresh();
            if (!file.Exists
                || file.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || !string.IsNullOrEmpty(file.LinkTarget)
                || file.Length < 0
                || file.Length > MaximumSourceBytesPerFile)
            {
                throw new InvalidDataException(
                    "A Sword and Shield game-module source is missing, linked, or exceeds its bounded size.");
            }

            return new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.SequentialScan);
        }

        private void ObserveLength(string fullPath, long length)
        {
            if (length < 0 || length > MaximumSourceBytesPerFile)
            {
                throw new InvalidDataException(
                    "A Sword and Shield game-module source exceeds its bounded size.");
            }

            if (!observedPaths.Add(fullPath))
            {
                return;
            }

            if (observedPaths.Count > MaximumSourceFiles
                || length > MaximumAggregateSourceBytes - observedBytes)
            {
                throw new InvalidDataException(
                    "Sword and Shield game-module sources exceed the bounded aggregate budget.");
            }

            observedBytes = checked(observedBytes + length);
        }

        private static bool TraversesReparsePoint(string root, string fullPath)
        {
            var relative = Path.GetRelativePath(root, fullPath);
            var current = root;
            foreach (var segment in relative.Split(
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
    }
}
