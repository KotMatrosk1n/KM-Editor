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

        if (string.Equals(targetRelativePath, SwShRoyalCandyWorkflowService.ItemPath, StringComparison.OrdinalIgnoreCase))
        {
            return TryRestoreRoyalCandyItemData(paths, targetPath, targetRelativePath, diagnosticDomain, diagnostics);
        }

        if (string.Equals(targetRelativePath, SwShRoyalCandyWorkflowService.ItemHashPath, StringComparison.OrdinalIgnoreCase))
        {
            return TryRemoveBaseIdenticalItemHash(paths, targetPath, targetRelativePath, diagnosticDomain, diagnostics);
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
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SwShRoyalCandyWorkflowService.ItemHashPath, StringComparison.OrdinalIgnoreCase)
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

        if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.ItemPath, StringComparison.OrdinalIgnoreCase))
        {
            return HasRoyalCandyItemData(project, entry);
        }

        if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.ItemHashPath, StringComparison.OrdinalIgnoreCase))
        {
            return HasInstalledOwnershipMarker(project) && IsOwnedItemHashOutput(project, entry);
        }

        if (IsItemTextOutput(entry.RelativePath))
        {
            return HasRoyalCandyItemText(project, entry);
        }

        return IsShopDataOutput(entry.RelativePath)
            && HasInstalledOwnershipMarker(project)
            && HasRoyalCandyShopPatch(project, entry);
    }

    public static IReadOnlyList<SwShRoyalCandyCleanupBlocker> FindBlockingCleanupTargets(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!HasInstalledOwnershipMarker(project))
        {
            return Array.Empty<SwShRoyalCandyCleanupBlocker>();
        }

        var blockers = new List<SwShRoyalCandyCleanupBlocker>();
        foreach (var entry in project.FileGraph.Entries.Where(entry => entry.LayeredFile is not null))
        {
            if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.ExeFsMainPath, StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetExeFsCleanupBlocker(project, entry, out var message))
                {
                    blockers.Add(new SwShRoyalCandyCleanupBlocker(entry, message));
                }

                continue;
            }

            if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.ItemPath, StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetItemCleanupBlocker(project, entry, out var message))
                {
                    blockers.Add(new SwShRoyalCandyCleanupBlocker(entry, message));
                }

                continue;
            }

            if (IsShopDataOutput(entry.RelativePath))
            {
                if (TryGetShopCleanupBlocker(project, entry, out var message))
                {
                    blockers.Add(new SwShRoyalCandyCleanupBlocker(entry, message));
                }

                continue;
            }

            if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.BagEventScriptPath, StringComparison.OrdinalIgnoreCase)
                && TryGetBagHookCleanupBlocker(project, entry, out var bagHookMessage))
            {
                blockers.Add(new SwShRoyalCandyCleanupBlocker(entry, bagHookMessage));
            }
        }

        return blockers
            .OrderBy(blocker => blocker.Entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryGetExeFsCleanupBlocker(
        OpenedProject project,
        ProjectFileGraphEntry entry,
        out string message)
    {
        message = string.Empty;
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            message = "Royal Candy ExeFS cleanup cannot resolve the layered exefs/main.";
            return true;
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(sourcePath);
            var signature = SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(
                sourceBytes,
                project.Paths.SelectedGame);
            if (signature.Kind == SwShRoyalCandyExeFsSignatureKind.NotInstalled)
            {
                return false;
            }

            if (signature.Kind is SwShRoyalCandyExeFsSignatureKind.Unlimited
                or SwShRoyalCandyExeFsSignatureKind.StoryLimits)
            {
                var basePath = ResolveBaseSourcePath(project.Paths, entry.RelativePath);
                if (basePath is not null && File.Exists(basePath))
                {
                    _ = SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(
                        sourceBytes,
                        File.ReadAllBytes(basePath),
                        project.Paths.SelectedGame);
                    return false;
                }

                message = "Royal Candy ExeFS cleanup cannot resolve base exefs/main for exact owned-byte restoration.";
                return true;
            }

            message = $"Royal Candy ExeFS cleanup cannot prove an owned signature: {signature.Message}";
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            message = $"Royal Candy ExeFS cleanup cannot verify and restore layered exefs/main safely: {exception.Message}";
            return true;
        }
    }

    private static bool TryGetItemCleanupBlocker(
        OpenedProject project,
        ProjectFileGraphEntry entry,
        out string message)
    {
        message = string.Empty;
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        var basePath = ResolveBaseSourcePath(project.Paths, entry.RelativePath);
        if (sourcePath is null || basePath is null || !File.Exists(sourcePath) || !File.Exists(basePath))
        {
            message = "Royal Candy item cleanup cannot resolve both layered and base item.dat for ownership verification.";
            return true;
        }

        SwShItemTable baseTable;
        try
        {
            baseTable = SwShItemTable.Parse(File.ReadAllBytes(basePath));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            message = $"Royal Candy item cleanup cannot decode base item data safely: {exception.Message}";
            return true;
        }

        SwShItemTable sourceTable;
        try
        {
            sourceTable = SwShItemTable.Parse(File.ReadAllBytes(sourcePath));
        }
        catch (InvalidDataException)
        {
            // An undecodable replacement has no positive Royal Candy ownership proof.
            // Preserve it as an unowned candidate instead of claiming or blocking it.
            return false;
        }
        catch (IOException exception)
        {
            message = $"Royal Candy item cleanup cannot read layered item data safely: {exception.Message}";
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            message = $"Royal Candy item cleanup cannot access layered item data safely: {exception.Message}";
            return true;
        }

        try
        {
            var sourceRecord = sourceTable.Records.FirstOrDefault(record => record.ItemId == RoyalCandyItemId);
            var baseRecord = baseTable.Records.FirstOrDefault(record => record.ItemId == RoyalCandyItemId);
            if (sourceRecord is null || baseRecord is null)
            {
                message = "Royal Candy item cleanup cannot resolve item 1128 in both layered and base item.dat.";
                return true;
            }

            try
            {
                _ = sourceTable.RestoreRoyalCandyRowFromBase(
                    baseTable,
                    templateItemId: 50,
                    targetItemId: RoyalCandyItemId);
                return false;
            }
            catch (InvalidDataException) when (sourceRecord.RawRowIndex == baseRecord.RawRowIndex)
            {
                return false;
            }
            catch (InvalidDataException exception)
            {
                message = $"Royal Candy item cleanup cannot restore the non-base item 1128 mapping safely: {exception.Message}";
                return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            message = $"Royal Candy item cleanup cannot read its layered or base item data safely: {exception.Message}";
            return true;
        }
    }

    private static bool TryGetShopCleanupBlocker(
        OpenedProject project,
        ProjectFileGraphEntry entry,
        out string message)
    {
        message = string.Empty;
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        var basePath = ResolveBaseSourcePath(project.Paths, entry.RelativePath);
        if (sourcePath is null || basePath is null || !File.Exists(sourcePath) || !File.Exists(basePath))
        {
            message = "Royal Candy shop cleanup cannot resolve both layered and base shop data for ownership verification.";
            return true;
        }

        SwShShopDataFile baseData;
        try
        {
            baseData = SwShShopDataFile.Parse(File.ReadAllBytes(basePath));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            message = $"Royal Candy shop cleanup cannot decode base shop data safely: {exception.Message}";
            return true;
        }

        SwShShopDataFile sourceData;
        try
        {
            sourceData = SwShShopDataFile.Parse(File.ReadAllBytes(sourcePath));
        }
        catch (InvalidDataException)
        {
            // Match item-data cleanup policy: undecodable replacement bytes are unowned
            // candidates and must be preserved without being claimed by Royal Candy.
            return false;
        }
        catch (IOException exception)
        {
            message = $"Royal Candy shop cleanup cannot read layered shop data safely: {exception.Message}";
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            message = $"Royal Candy shop cleanup cannot access layered shop data safely: {exception.Message}";
            return true;
        }

        try
        {
            var mapping = SwShRoyalCandyShopPatchMapper.Analyze(sourceData, baseData);
            if (mapping.BaseOccurrences == 0)
            {
                message = "Royal Candy shop cleanup cannot find any base item 1128 occurrence to verify.";
                return true;
            }

            if (mapping.MissingOccurrences > 0)
            {
                var restoredBytes = sourceData.WriteEdits(mapping.RestoreEdits);
                var restoredMapping = SwShRoyalCandyShopPatchMapper.Analyze(
                    SwShShopDataFile.Parse(restoredBytes),
                    baseData);
                if (restoredMapping.MissingOccurrences != 0)
                {
                    message = "Royal Candy shop cleanup cannot restore every uniquely mapped owned item 1128 occurrence.";
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            message = $"Royal Candy shop cleanup cannot map the layered shop data safely: {exception.Message}";
            return true;
        }
    }

    private static bool TryGetBagHookCleanupBlocker(
        OpenedProject project,
        ProjectFileGraphEntry entry,
        out string message)
    {
        message = string.Empty;
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            message = "Royal Candy Bag Hook cleanup cannot resolve the layered Bag-event script.";
            return true;
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(sourcePath);
            var analysis = SwShBagHookAmxPatcher.Analyze(sourceBytes);
            var royalSlot = analysis.Slots.FirstOrDefault(slot => slot.Slot == SwShBagHookAmxPatcher.RoyalCandySlot);
            if (royalSlot?.ItemId == RoyalCandyItemId)
            {
                _ = SwShBagHookAmxPatcher.ApplySlotPatches(
                    sourceBytes,
                    [new SwShBagHookSlotPatch(SwShBagHookAmxPatcher.RoyalCandySlot, null, null)]);
            }

            return false;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            message = $"Royal Candy Bag Hook cleanup cannot decode the layered Bag-event script safely: {exception.Message}";
            return true;
        }
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
        if (SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(restored, baseBytes))
        {
            File.Delete(targetPath);
        }
        else
        {
            File.WriteAllBytes(targetPath, restored);
        }

        return true;
    }

    private static bool TryRestoreRoyalCandyItemData(
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
                "Royal Candy item cleanup could not resolve base item.dat for ownership verification.",
                file: targetRelativePath,
                expected: "Readable base item.dat"));
            return false;
        }

        var baseBytes = File.ReadAllBytes(basePath);
        var restored = SwShItemTable.Parse(File.ReadAllBytes(targetPath)).RestoreRoyalCandyRowFromBase(
            SwShItemTable.Parse(baseBytes),
            templateItemId: 50,
            targetItemId: RoyalCandyItemId);
        if (restored.SequenceEqual(baseBytes))
        {
            File.Delete(targetPath);
        }
        else
        {
            File.WriteAllBytes(targetPath, restored);
        }

        return true;
    }

    private static bool TryRemoveBaseIdenticalItemHash(
        ProjectPaths paths,
        string targetPath,
        string targetRelativePath,
        string diagnosticDomain,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var basePath = ResolveBaseSourcePath(paths, targetRelativePath);
        if (basePath is null
            || !File.Exists(basePath)
            || !IsOwnedItemHashOutput(File.ReadAllBytes(targetPath), File.ReadAllBytes(basePath)))
        {
            diagnostics.Add(CreateDiagnostic(
                diagnosticDomain,
                DiagnosticSeverity.Warning,
                "Royal Candy item-hash cleanup preserved the layered file because it is neither base-identical nor the exact legacy KM normalization.",
                file: targetRelativePath,
                expected: "Base-identical item hash override or exact legacy KM normalization"));
            return false;
        }

        File.Delete(targetPath);
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

        var exactOwnedLines = baseText.Lines.ToArray();
        exactOwnedLines[RoyalCandyItemId] = exactOwnedLines[RoyalCandyItemId] with
        {
            Text = targetText.Lines[RoyalCandyItemId].Text,
        };
        if (targetBytes.SequenceEqual(baseText.WritePreserving(exactOwnedLines)))
        {
            File.Delete(targetPath);
            return true;
        }

        var restoredLines = targetText.Lines.ToArray();
        restoredLines[RoyalCandyItemId] = baseText.Lines[RoyalCandyItemId];
        var restoredBytes = targetText.WritePreserving(restoredLines);
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

        var targetBytes = File.ReadAllBytes(targetPath);
        var baseBytes = File.ReadAllBytes(basePath);
        if (IsExactRoyalCandyShopOutput(targetBytes, baseBytes))
        {
            File.Delete(targetPath);
            return true;
        }

        var targetData = SwShShopDataFile.Parse(targetBytes);
        var baseData = SwShShopDataFile.Parse(baseBytes);
        var mapping = SwShRoyalCandyShopPatchMapper.Analyze(targetData, baseData);
        if (mapping.MissingOccurrences == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                diagnosticDomain,
                DiagnosticSeverity.Warning,
                $"Preserved Royal Candy shop cleanup target '{targetRelativePath}' because no uniquely mapped Royal Candy-owned shop removal was detected.",
                file: targetRelativePath,
                expected: "A uniquely mapped missing base item 1128 occurrence"));
            return false;
        }

        var restoredBytes = targetData.WriteEdits(mapping.RestoreEdits);
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

    internal static bool HasInstalledOwnershipMarker(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

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

            if (string.Equals(entry.RelativePath, SwShRoyalCandyWorkflowService.ItemPath, StringComparison.OrdinalIgnoreCase)
                && HasRoyalCandyItemData(project, entry))
            {
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<ProjectFileReference> GetOwnershipMarkerSources(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SwShRoyalCandyWorkflowService.BagEventScriptPath,
            SwShRoyalCandyWorkflowService.ExeFsMainPath,
            SwShRoyalCandyWorkflowService.ItemPath,
        };
        foreach (var entry in project.FileGraph.Entries.Where(entry => IsItemTextOutput(entry.RelativePath)))
        {
            relativePaths.Add(entry.RelativePath);
        }

        var sources = relativePaths
            .OrderBy(relativePath => relativePath, StringComparer.Ordinal)
            .Select(relativePath => new ProjectFileReference(ProjectFileLayer.Generated, relativePath))
            .ToList();
        var itemEntry = project.FileGraph.Entries.FirstOrDefault(entry => string.Equals(
            entry.RelativePath,
            SwShRoyalCandyWorkflowService.ItemPath,
            StringComparison.OrdinalIgnoreCase));
        if (itemEntry?.BaseFile is not null)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, itemEntry.RelativePath));
        }

        return sources;
    }

    internal static bool HasRoyalCandyItemData(OpenedProject project, ProjectFileGraphEntry entry)
    {
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        var basePath = ResolveBaseSourcePath(project.Paths, entry.RelativePath);
        if (sourcePath is null || basePath is null || !File.Exists(sourcePath) || !File.Exists(basePath))
        {
            return false;
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(sourcePath);
            var restored = SwShItemTable.Parse(sourceBytes).RestoreRoyalCandyRowFromBase(
                SwShItemTable.Parse(File.ReadAllBytes(basePath)),
                templateItemId: 50,
                targetItemId: RoyalCandyItemId);
            return !restored.SequenceEqual(sourceBytes);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return false;
        }
    }

    internal static bool IsLayeredOutputIdenticalToBase(OpenedProject project, ProjectFileGraphEntry entry)
    {
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        var basePath = ResolveBaseSourcePath(project.Paths, entry.RelativePath);
        if (sourcePath is null || basePath is null || !File.Exists(sourcePath) || !File.Exists(basePath))
        {
            return false;
        }

        try
        {
            return File.ReadAllBytes(sourcePath).SequenceEqual(File.ReadAllBytes(basePath));
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal static bool IsOwnedItemHashOutput(OpenedProject project, ProjectFileGraphEntry entry)
    {
        var sourcePath = ResolveSourcePath(project.Paths, entry);
        var basePath = ResolveBaseSourcePath(project.Paths, entry.RelativePath);
        if (sourcePath is null || basePath is null || !File.Exists(sourcePath) || !File.Exists(basePath))
        {
            return false;
        }

        try
        {
            return IsOwnedItemHashOutput(
                File.ReadAllBytes(sourcePath),
                File.ReadAllBytes(basePath));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return false;
        }
    }

    private static bool IsOwnedItemHashOutput(byte[] targetBytes, byte[] baseBytes)
    {
        if (targetBytes.SequenceEqual(baseBytes))
        {
            return true;
        }

        var baseTable = SwShItemHashTable.Parse(baseBytes);
        if (baseTable.Entries.All(entry => entry.ItemId != RoyalCandyItemId))
        {
            return false;
        }

        var exactLegacyOutput = baseTable.Write();
        return !exactLegacyOutput.SequenceEqual(baseBytes)
            && targetBytes.SequenceEqual(exactLegacyOutput);
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
                project.Paths.SelectedGame).Kind is
                SwShRoyalCandyExeFsSignatureKind.Unlimited or
                SwShRoyalCandyExeFsSignatureKind.StoryLimits;
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
            return SwShRoyalCandyShopPatchMapper.Analyze(targetData, baseData).MissingOccurrences > 0;
        }
        catch (InvalidDataException)
        {
        }
        catch (IOException)
        {
        }

        return false;
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

    private static bool IsExactRoyalCandyShopOutput(byte[] targetBytes, byte[] baseBytes)
    {
        var baseData = SwShShopDataFile.Parse(baseBytes);
        var mapping = SwShRoyalCandyShopPatchMapper.Analyze(baseData, baseData);
        return mapping.RemovalEdits.Count > 0
            && targetBytes.SequenceEqual(baseData.WriteEdits(mapping.RemovalEdits));
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

internal sealed record SwShRoyalCandyCleanupBlocker(
    ProjectFileGraphEntry Entry,
    string Message);
