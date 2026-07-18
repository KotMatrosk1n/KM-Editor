// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;

namespace KM.SwSh.RoyalCandy;

internal enum SwShRoyalCandyAcquisitionOwnershipState
{
    Absent,
    Valid,
    Invalid,
}

internal sealed record SwShRoyalCandyAcquisitionOwnershipInspection(
    SwShRoyalCandyAcquisitionOwnershipState State,
    ProjectFileGraphEntry? Entry,
    string Message)
{
    public bool IsValid => State == SwShRoyalCandyAcquisitionOwnershipState.Valid;
}

internal sealed record SwShRoyalCandyAcquisitionOwnershipInputs(
    string ShopRelativePath,
    byte[] BaseShopBytes,
    byte[] BaseNestBytes,
    byte[] BasePlacementBytes,
    byte[] BaseItemHashBytes);

internal static class SwShRoyalCandyAcquisitionOwnershipService
{
    public static SwShRoyalCandyAcquisitionOwnershipInspection Inspect(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            candidate.LayeredFile is not null
            && string.Equals(
                candidate.RelativePath,
                SwShRoyalCandyAcquisitionOwnershipManifest.RelativePath,
                StringComparison.OrdinalIgnoreCase));
        var markerPath = ResolveOutputPath(
            project.Paths,
            SwShRoyalCandyAcquisitionOwnershipManifest.RelativePath);
        if (entry is null && (markerPath is null || (!File.Exists(markerPath) && !Directory.Exists(markerPath))))
        {
            return new SwShRoyalCandyAcquisitionOwnershipInspection(
                SwShRoyalCandyAcquisitionOwnershipState.Absent,
                null,
                "No Royal Candy acquisition ownership manifest was found.");
        }

        if (markerPath is null || !File.Exists(markerPath))
        {
            return new SwShRoyalCandyAcquisitionOwnershipInspection(
                SwShRoyalCandyAcquisitionOwnershipState.Invalid,
                entry ?? CreateSyntheticMarkerEntry(),
                "Royal Candy acquisition ownership manifest is not a readable file.");
        }

        try
        {
            var inputs = ReadAuthoritativeInputs(project.Paths);
            _ = SwShRoyalCandyAcquisitionOwnershipManifest.ParseAndValidate(
                File.ReadAllBytes(markerPath),
                inputs.ShopRelativePath,
                inputs.BaseShopBytes,
                inputs.BaseNestBytes,
                inputs.BasePlacementBytes,
                inputs.BaseItemHashBytes);
            return new SwShRoyalCandyAcquisitionOwnershipInspection(
                SwShRoyalCandyAcquisitionOwnershipState.Valid,
                entry ?? CreateSyntheticMarkerEntry(),
                "Royal Candy acquisition ownership manifest matches the authoritative base inputs.");
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OverflowException)
        {
            return new SwShRoyalCandyAcquisitionOwnershipInspection(
                SwShRoyalCandyAcquisitionOwnershipState.Invalid,
                entry ?? CreateSyntheticMarkerEntry(),
                $"Royal Candy acquisition ownership manifest is invalid: {exception.Message}");
        }
    }

    private static ProjectFileGraphEntry CreateSyntheticMarkerEntry()
    {
        return new ProjectFileGraphEntry(
            SwShRoyalCandyAcquisitionOwnershipManifest.RelativePath,
            BaseFile: null,
            new ProjectFileReference(
                ProjectFileLayer.Layered,
                SwShRoyalCandyAcquisitionOwnershipManifest.RelativePath),
            ProjectFileGraphEntryState.LayeredOnly);
    }

    public static byte[] CreateManifestBytes(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var inputs = ReadAuthoritativeInputs(paths);
        return SwShRoyalCandyAcquisitionOwnershipManifest.Write(
            SwShRoyalCandyAcquisitionOwnershipManifest.Create(
                inputs.ShopRelativePath,
                inputs.BaseShopBytes,
                inputs.BaseNestBytes,
                inputs.BasePlacementBytes,
                inputs.BaseItemHashBytes));
    }

    public static SwShRoyalCandyAcquisitionOwnershipInputs ReadAuthoritativeInputs(
        ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var shopRelativePath = ResolveShopRelativePath(paths);
        return new SwShRoyalCandyAcquisitionOwnershipInputs(
            shopRelativePath,
            ReadBaseBytes(paths, shopRelativePath),
            ReadBaseBytes(paths, SwShRoyalCandyWorkflowService.NestDataPath),
            ReadBaseBytes(paths, SwShRoyalCandyWorkflowService.PlacementPath),
            ReadBaseBytes(paths, SwShRoyalCandyWorkflowService.ItemHashPath));
    }

    private static string ResolveShopRelativePath(ProjectPaths paths)
    {
        foreach (var relativePath in new[]
        {
            SwShRoyalCandyWorkflowService.ShopDataPath,
            SwShRoyalCandyWorkflowService.LegacyShopDataPath,
        })
        {
            var basePath = ResolveBasePath(paths, relativePath);
            if (basePath is not null && File.Exists(basePath))
            {
                return relativePath;
            }
        }

        throw new InvalidDataException(
            "Royal Candy acquisition ownership requires a readable base shop data file.");
    }

    private static byte[] ReadBaseBytes(ProjectPaths paths, string relativePath)
    {
        var basePath = ResolveBasePath(paths, relativePath);
        if (basePath is null || !File.Exists(basePath))
        {
            throw new InvalidDataException(
                $"Royal Candy acquisition ownership requires readable base '{relativePath}'.");
        }

        return File.ReadAllBytes(basePath);
    }

    private static string? ResolveBasePath(ProjectPaths paths, string relativePath)
    {
        if (relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineRootPath(paths.BaseRomFsPath, relativePath["romfs/".Length..]);
        }

        if (relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineRootPath(paths.BaseExeFsPath, relativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? ResolveOutputPath(ProjectPaths paths, string relativePath)
    {
        return SwShOutputRollbackScope.ResolvePhysicalContainedPath(
            paths.OutputRootPath,
            relativePath);
    }

    private static string? CombineRootPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }
}
