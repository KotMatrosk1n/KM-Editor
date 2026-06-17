// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.CatchCap;
using KM.SwSh.ExeFs;
using KM.SwSh.IvScreen;

namespace KM.SwSh.RoyalCandy;

internal static class SwShRoyalCandyCleanup
{
    private const int RoyalCandyItemId = 1128;
    private const string AppliedRoyalCandyName = "Royal Candy";
    private const string AppliedRoyalCandyPluralName = "Royal Candies";
    private const string UnlimitedRoyalCandyName = "Unlimited Royal Candy";
    private const string StoryLimitsRoyalCandyName = "Royal Candy with Story Limits";
    private const string UnlimitedDescription = "A candy packed with strange energy. It can be used repeatedly by compatible Pokemon.";
    private const string StoryLimitsDescription = "A candy packed with strange energy. Its full power follows the current story limit.";

    public static bool TryApplyCleanupTarget(
        ProjectPaths paths,
        string targetPath,
        string targetRelativePath,
        string diagnosticDomain,
        ICollection<ValidationDiagnostic> diagnostics,
        bool clearBagHookSlot)
    {
        if (string.Equals(targetRelativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase))
        {
            return RestoreOrDeleteExeFsMain(paths, targetPath, targetRelativePath, diagnosticDomain, diagnostics);
        }

        if (string.Equals(targetRelativePath, SwShRoyalCandyWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase)
            && clearBagHookSlot)
        {
            var restored = SwShBagHookAmxPatcher.ApplySlotPatches(
                File.ReadAllBytes(targetPath),
                [
                    new SwShBagHookSlotPatch(
                        SwShBagHookAmxPatcher.RoyalCandySlot,
                        null,
                        null),
                ]);
            File.WriteAllBytes(targetPath, restored);
            return true;
        }

        if (IsItemTextOutput(targetRelativePath))
        {
            return TryRestoreRoyalCandyItemText(paths, targetPath, targetRelativePath, diagnosticDomain, diagnostics);
        }

        if (IsShopDataOutput(targetRelativePath))
        {
            return TryRestoreRoyalCandyShopEntries(paths, targetPath, targetRelativePath, diagnosticDomain, diagnostics);
        }

        diagnostics.Add(CreateDiagnostic(
            diagnosticDomain,
            DiagnosticSeverity.Warning,
            $"Skipped Royal Candy cleanup target '{targetRelativePath}' because KM cannot safely isolate Royal Candy-owned data in that file.",
            file: targetRelativePath,
            expected: "A verified Royal Candy-owned text row, Bag Hook slot, shop entry, or ExeFS signature"));
        return false;
    }

    public static bool IsCleanupOutputPath(string relativePath)
    {
        return string.Equals(relativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
            || IsItemTextOutput(relativePath)
            || IsShopDataOutput(relativePath);
    }

    public static bool IsBagHookDependentCleanupTarget(OpenedProject project, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is null)
        {
            return false;
        }

        if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase))
        {
            return HasRoyalCandyExeFsSignature(project, entry);
        }

        if (IsItemTextOutput(entry.RelativePath))
        {
            return HasRoyalCandyItemText(project, entry);
        }

        return IsShopDataOutput(entry.RelativePath)
            && IsRoyalCandyInstalled(project)
            && HasRoyalCandyShopPatch(project, entry);
    }

    private static bool RestoreOrDeleteExeFsMain(
        ProjectPaths paths,
        string targetPath,
        string targetRelativePath,
        string diagnosticDomain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var basePath = ResolveBaseSourcePath(paths, targetRelativePath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                diagnosticDomain,
                DiagnosticSeverity.Error,
                "Royal Candy cleanup could not resolve base exefs/main for restoration.",
                file: targetRelativePath,
                expected: "Readable base ExeFS main"));
            return false;
        }

        var baseBytes = File.ReadAllBytes(basePath);
        var restored = SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(
            File.ReadAllBytes(targetPath),
            baseBytes,
            paths.SelectedGame);
        if (restored.SequenceEqual(baseBytes) || !ContainsIndependentExeFsHook(restored))
        {
            File.Delete(targetPath);
        }
        else
        {
            File.WriteAllBytes(targetPath, restored);
        }

        return true;
    }

    private static bool TryRestoreRoyalCandyItemText(
        ProjectPaths paths,
        string targetPath,
        string targetRelativePath,
        string diagnosticDomain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetBytes = File.ReadAllBytes(targetPath);
        var targetText = SwShGameTextFile.Parse(targetBytes);
        if (targetText.Lines.Count <= RoyalCandyItemId)
        {
            diagnostics.Add(CreateDiagnostic(
                diagnosticDomain,
                DiagnosticSeverity.Warning,
                $"Skipped Royal Candy text cleanup for '{targetRelativePath}' because item {RoyalCandyItemId} is not present.",
                file: targetRelativePath,
                expected: $"Text table containing item {RoyalCandyItemId}"));
            return false;
        }

        if (!IsRoyalCandyTextValue(targetRelativePath, targetText.Lines[RoyalCandyItemId].Text))
        {
            diagnostics.Add(CreateDiagnostic(
                diagnosticDomain,
                DiagnosticSeverity.Warning,
                $"Skipped Royal Candy text cleanup for '{targetRelativePath}' because item {RoyalCandyItemId} does not contain Royal Candy-owned text.",
                file: targetRelativePath,
                expected: "Royal Candy item name or description text"));
            return false;
        }

        var basePath = ResolveBaseSourcePath(paths, targetRelativePath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                diagnosticDomain,
                DiagnosticSeverity.Warning,
                $"Skipped Royal Candy text cleanup for '{targetRelativePath}' because the base text file could not be resolved for row restoration.",
                file: targetRelativePath,
                expected: "Readable base RomFS text file"));
            return false;
        }

        var baseBytes = File.ReadAllBytes(basePath);
        var baseText = SwShGameTextFile.Parse(baseBytes);
        if (baseText.Lines.Count <= RoyalCandyItemId)
        {
            diagnostics.Add(CreateDiagnostic(
                diagnosticDomain,
                DiagnosticSeverity.Warning,
                $"Skipped Royal Candy text cleanup for '{targetRelativePath}' because the base text file does not contain item {RoyalCandyItemId}.",
                file: targetRelativePath,
                expected: $"Base text table containing item {RoyalCandyItemId}"));
            return false;
        }

        var restoredLines = targetText.Lines.ToArray();
        restoredLines[RoyalCandyItemId] = baseText.Lines[RoyalCandyItemId];
        var restoredBytes = SwShGameTextFile.Write(restoredLines);
        if (restoredBytes.SequenceEqual(baseBytes))
        {
            File.Delete(targetPath);
        }
        else
        {
            File.WriteAllBytes(targetPath, restoredBytes);
        }

        return true;
    }

    private static bool TryRestoreRoyalCandyShopEntries(
        ProjectPaths paths,
        string targetPath,
        string targetRelativePath,
        string diagnosticDomain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var basePath = ResolveBaseSourcePath(paths, targetRelativePath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                diagnosticDomain,
                DiagnosticSeverity.Warning,
                $"Skipped Royal Candy shop cleanup for '{targetRelativePath}' because the base shop data could not be resolved.",
                file: targetRelativePath,
                expected: "Readable base shop_data.bin"));
            return false;
        }

        var targetData = SwShShopDataFile.Parse(File.ReadAllBytes(targetPath));
        var baseData = SwShShopDataFile.Parse(File.ReadAllBytes(basePath));
        var edits = CreateRoyalCandyShopRestoreEdits(targetData, baseData);
        if (edits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                diagnosticDomain,
                DiagnosticSeverity.Warning,
                $"Skipped Royal Candy shop cleanup for '{targetRelativePath}' because no Royal Candy-owned shop edit was detected.",
                file: targetRelativePath,
                expected: "Shop data missing base item 1128 entries"));
            return false;
        }

        var restoredBytes = targetData.WriteEdits(edits);
        var restoredData = SwShShopDataFile.Parse(restoredBytes);
        if (ShopDataSemanticallyEquals(restoredData, baseData))
        {
            File.Delete(targetPath);
        }
        else
        {
            File.WriteAllBytes(targetPath, restoredBytes);
        }

        return true;
    }

    private static bool IsRoyalCandyTextValue(string relativePath, string value)
    {
        if (TryParseMessageCommonFile(relativePath, out _, out var fileName)
            && fileName.StartsWith("itemname", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(value, AppliedRoyalCandyName, StringComparison.Ordinal)
                || string.Equals(value, AppliedRoyalCandyPluralName, StringComparison.Ordinal)
                || string.Equals(value, UnlimitedRoyalCandyName, StringComparison.Ordinal)
                || string.Equals(value, StoryLimitsRoyalCandyName, StringComparison.Ordinal);
        }

        return string.Equals(value, UnlimitedDescription, StringComparison.Ordinal)
            || string.Equals(value, StoryLimitsDescription, StringComparison.Ordinal);
    }

    private static bool IsRoyalCandyInstalled(OpenedProject project)
    {
        foreach (var entry in project.FileGraph.Entries.Where(entry => entry.LayeredFile is not null))
        {
            if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase)
                && HasRoyalCandyBagHookSlot(project, entry))
            {
                return true;
            }

            if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && HasRoyalCandyExeFsSignature(project, entry))
            {
                return true;
            }

            if (IsItemTextOutput(entry.RelativePath) && HasRoyalCandyItemText(project, entry))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRoyalCandyBagHookSlot(OpenedProject project, ProjectFileGraphEntry entry)
    {
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return false;
        }

        try
        {
            var analysis = SwShBagHookAmxPatcher.Analyze(File.ReadAllBytes(sourcePath));
            var slot = analysis.Slots.FirstOrDefault(slot => slot.Slot == SwShBagHookAmxPatcher.RoyalCandySlot);
            return slot?.ItemId == RoyalCandyItemId;
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    private static bool HasRoyalCandyItemText(OpenedProject project, ProjectFileGraphEntry entry)
    {
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return false;
        }

        try
        {
            var text = SwShGameTextFile.Parse(File.ReadAllBytes(sourcePath));
            return text.Lines.Count > RoyalCandyItemId
                && IsRoyalCandyTextValue(entry.RelativePath, text.Lines[RoyalCandyItemId].Text);
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
            return SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(
                File.ReadAllBytes(sourcePath),
                project.Paths.SelectedGame).Kind
                != SwShRoyalCandyExeFsSignatureKind.NotInstalled;
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return false;
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
            return CreateRoyalCandyShopRestoreEdits(targetData, baseData).Count > 0;
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    private static bool ContainsIndependentExeFsHook(byte[] mainBytes)
    {
        return SwShIndependentExeFsHookDetector.ContainsAny(mainBytes);
    }

    private static bool IsShopDataOutput(string relativePath)
    {
        return string.Equals(relativePath, SwShRoyalCandyWorkflowService.ShopDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.LegacyShopDataPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsItemTextOutput(string relativePath)
    {
        return TryParseMessageCommonFile(relativePath, out _, out var fileName)
            && (string.Equals(fileName, "iteminfo.dat", StringComparison.OrdinalIgnoreCase)
                || (fileName.StartsWith("itemname", StringComparison.OrdinalIgnoreCase)
                    && fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<SwShShopInventoryEdit> CreateRoyalCandyShopRestoreEdits(
        SwShShopDataFile targetData,
        SwShShopDataFile baseData)
    {
        var edits = new List<SwShShopInventoryEdit>();

        foreach (var baseShop in baseData.SingleShops)
        {
            var targetShop = targetData.SingleShops.FirstOrDefault(shop => shop.Hash == baseShop.Hash);
            if (targetShop is null)
            {
                continue;
            }

            AddRoyalCandyRestoreEdit(
                edits,
                SwShShopKind.Single,
                baseShop.Hash,
                inventoryIndex: 0,
                targetShop.Inventory.Items,
                baseShop.Inventory.Items);
        }

        foreach (var baseShop in baseData.MultiShops)
        {
            var targetShop = targetData.MultiShops.FirstOrDefault(shop => shop.Hash == baseShop.Hash);
            if (targetShop is null)
            {
                continue;
            }

            var inventoryCount = Math.Min(baseShop.Inventories.Count, targetShop.Inventories.Count);
            for (var inventoryIndex = 0; inventoryIndex < inventoryCount; inventoryIndex++)
            {
                AddRoyalCandyRestoreEdit(
                    edits,
                    SwShShopKind.Multi,
                    baseShop.Hash,
                    inventoryIndex,
                    targetShop.Inventories[inventoryIndex].Items,
                    baseShop.Inventories[inventoryIndex].Items);
            }
        }

        return edits;
    }

    private static void AddRoyalCandyRestoreEdit(
        ICollection<SwShShopInventoryEdit> edits,
        SwShShopKind kind,
        ulong hash,
        int inventoryIndex,
        IReadOnlyList<int> targetItems,
        IReadOnlyList<int> baseItems)
    {
        var insertSlots = baseItems
            .Select((itemId, slot) => (itemId, slot))
            .Where(entry => entry.itemId == RoyalCandyItemId)
            .Select(entry => entry.slot)
            .Where(slot => (uint)slot >= (uint)targetItems.Count || targetItems[slot] != RoyalCandyItemId)
            .ToArray();
        if (insertSlots.Length == 0)
        {
            return;
        }

        var restoredItems = targetItems.ToList();
        foreach (var slot in insertSlots)
        {
            restoredItems.Insert(Math.Min(slot, restoredItems.Count), RoyalCandyItemId);
        }

        edits.Add(new SwShShopInventoryEdit(
            kind,
            hash,
            inventoryIndex,
            Slot: 0,
            ItemId: 0,
            SwShShopInventoryEditAction.Set,
            restoredItems));
    }

    private static bool ShopDataSemanticallyEquals(SwShShopDataFile left, SwShShopDataFile right)
    {
        return left.SingleShops.Count == right.SingleShops.Count
            && left.MultiShops.Count == right.MultiShops.Count
            && left.SingleShops.Zip(right.SingleShops).All(pair =>
                pair.First.Hash == pair.Second.Hash
                && pair.First.Inventory.Items.SequenceEqual(pair.Second.Inventory.Items))
            && left.MultiShops.Zip(right.MultiShops).All(pair =>
                pair.First.Hash == pair.Second.Hash
                && pair.First.Inventories.Count == pair.Second.Inventories.Count
                && pair.First.Inventories.Zip(pair.Second.Inventories).All(inventoryPair =>
                    inventoryPair.First.Items.SequenceEqual(inventoryPair.Second.Items)));
    }

    private static string? ResolveBaseSourcePath(ProjectPaths paths, string targetRelativePath)
    {
        if (targetRelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, targetRelativePath["romfs/".Length..]);
        }

        if (targetRelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, targetRelativePath["exefs/".Length..]);
        }

        return null;
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

    private static ValidationDiagnostic CreateDiagnostic(
        string domain,
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: domain,
            Expected: expected);
    }
}
