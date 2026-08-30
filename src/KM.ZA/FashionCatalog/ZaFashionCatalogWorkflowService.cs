// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.FashionCatalog;

internal sealed class ZaFashionCatalogWorkflowService
{
    internal const string WorkflowId = "fashionCatalog";
    internal const string Domain = "workflow.fashionCatalog";
    internal const string DressUpGroupCatalogPath =
        "world/exl/dress_up_data/dress_up_group_data/dress_up_group_data.bin";

    private const string WorkflowLabel = "Fashion Catalog";
    private const string WorkflowDescription =
        "Edit Pokemon Legends Z-A dress-up, hair and makeup catalogs and their proven shop lineups.";
    private const string DressUpItemNamesTable = "common/dressup_item_name";
    private const string DressUpInterfaceTable = "common/dressup";

    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaFashionCatalogService catalogService;

    public ZaFashionCatalogWorkflowService(
        ZaWorkflowFileSource? fileSource = null,
        ZaFashionCatalogService? catalogService = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.catalogService = catalogService ?? new ZaFashionCatalogService();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return ZaWorkflowSupport.CreateSummary(
            project,
            WorkflowId,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaFashionCatalogWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return TryLoadState(project, out var state, out var blockedWorkflow)
            ? state!.Workflow
            : blockedWorkflow!;
    }

    public bool TryLoadState(
        OpenedProject project,
        out ZaFashionCatalogLoadedState? state,
        out ZaFashionCatalogWorkflow? blockedWorkflow)
    {
        ArgumentNullException.ThrowIfNull(project);
        state = null;
        blockedWorkflow = null;
        if (project.Paths.SelectedGame is not ProjectGame.ZA)
        {
            var diagnostics = new[]
            {
                Error(
                    "Fashion Catalog requires a Pokemon Legends Z-A project.",
                    expected: "Pokemon Legends Z-A project"),
            };
            blockedWorkflow = CreateWorkflow(project, EmptySnapshot(), diagnostics);
            return false;
        }

        try
        {
            var dressUpItems = fileSource.Read(project, ZaDataPaths.DressUpDataArray);
            var dressUpGroups = fileSource.Read(project, DressUpGroupCatalogPath);
            var hairAndMakeup = fileSource.Read(project, ZaDataPaths.HairMakeDataArray);
            var fashionShops = fileSource.Read(project, ZaDataPaths.ShopDressUpArray);
            var dressUpLineups = fileSource.Read(project, ZaDataPaths.ShopDressUpLineupArray);
            var hairAndMakeupLineups = fileSource.Read(project, ZaDataPaths.ShopHairMakeLineupArray);
            var sources = new ZaFashionCatalogSourceSet(
                dressUpItems.Bytes,
                dressUpGroups.Bytes,
                hairAndMakeup.Bytes,
                fashionShops.Bytes,
                dressUpLineups.Bytes,
                hairAndMakeupLineups.Bytes);
            var snapshot = catalogService.CreateSnapshot(sources);
            var references = new[]
            {
                Reference(dressUpItems),
                Reference(dressUpGroups),
                Reference(hairAndMakeup),
                Reference(fashionShops),
                Reference(dressUpLineups),
                Reference(hairAndMakeupLineups),
            };
            var diagnostics = new List<ValidationDiagnostic>();
            var textLabels = LoadTextLabels(project, diagnostics);
            var workflow = CreateWorkflow(project, snapshot, diagnostics, textLabels);
            state = new ZaFashionCatalogLoadedState(
                workflow,
                sources,
                references);
            blockedWorkflow = null;
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            UnauthorizedAccessException or
            OverflowException)
        {
            var diagnostics = new[]
            {
                Error(
                    $"Fashion Catalog could not be loaded safely: {exception.Message}",
                    expected: "Complete supported Fashion Catalog and shop-lineup source set"),
            };
            state = null;
            blockedWorkflow = CreateWorkflow(project, EmptySnapshot(), diagnostics);
            return false;
        }
    }

    public ZaFashionCatalogPreparedUpdate PrepareDressUpItemUpdate(
        OpenedProject project,
        ZaFashionCatalogRowBinding binding,
        ZaDressUpItemPatch patch)
    {
        var loaded = LoadRequiredState(project);
        var result = catalogService.UpdateDressUpItem(loaded.Sources, binding, patch);
        return Prepare(ZaDataPaths.DressUpDataArray, loaded, result);
    }

    public ZaFashionCatalogPreparedUpdate PrepareDressUpGroupUpdate(
        OpenedProject project,
        ZaFashionCatalogRowBinding binding,
        ZaDressUpGroupPatch patch)
    {
        var loaded = LoadRequiredState(project);
        var result = catalogService.UpdateDressUpGroup(loaded.Sources, binding, patch);
        return Prepare(DressUpGroupCatalogPath, loaded, result);
    }

    public ZaFashionCatalogPreparedUpdate PrepareHairAndMakeupUpdate(
        OpenedProject project,
        ZaFashionCatalogRowBinding binding,
        ZaHairAndMakeupPatch patch)
    {
        var loaded = LoadRequiredState(project);
        var result = catalogService.UpdateHairAndMakeup(loaded.Sources, binding, patch);
        return Prepare(ZaDataPaths.HairMakeDataArray, loaded, result);
    }

    public ZaFashionCatalogPreparedUpdate PrepareLineupEntryUpdate(
        OpenedProject project,
        ZaFashionCatalogFile catalogFile,
        ZaFashionCatalogRowBinding binding,
        ZaFashionLineupEntryPatch patch)
    {
        var loaded = LoadRequiredState(project);
        var result = catalogService.UpdateLineupEntry(
            loaded.Sources,
            catalogFile,
            binding,
            patch);
        var path = catalogFile switch
        {
            ZaFashionCatalogFile.DressUpLineups => ZaDataPaths.ShopDressUpLineupArray,
            ZaFashionCatalogFile.HairAndMakeupLineups => ZaDataPaths.ShopHairMakeLineupArray,
            _ => throw new InvalidDataException("The selected Fashion Catalog source is not a lineup."),
        };
        return Prepare(path, loaded, result);
    }

    private static ZaFashionCatalogPreparedUpdate Prepare(
        string virtualPath,
        ZaFashionCatalogLoadedState loaded,
        ZaFashionCatalogEditResult result)
    {
        var bytes = result.ChangedFile switch
        {
            ZaFashionCatalogFile.DressUpItems => result.Sources.DressUpItems,
            ZaFashionCatalogFile.DressUpGroups => result.Sources.DressUpGroups,
            ZaFashionCatalogFile.HairAndMakeup => result.Sources.HairAndMakeup,
            ZaFashionCatalogFile.DressUpLineups => result.Sources.DressUpLineups,
            ZaFashionCatalogFile.HairAndMakeupLineups => result.Sources.HairAndMakeupLineups,
            _ => throw new InvalidDataException("The Fashion Catalog update selected an unknown source file."),
        };
        return new ZaFashionCatalogPreparedUpdate(
            virtualPath,
            bytes,
            loaded.Workflow.Snapshot.SourceRevision,
            result.Snapshot,
            loaded.SourcesReferences);
    }

    private ZaFashionCatalogLoadedState LoadRequiredState(OpenedProject project)
    {
        if (TryLoadState(project, out var state, out var blockedWorkflow))
        {
            return state!;
        }

        throw new InvalidDataException(
            blockedWorkflow?.Diagnostics.FirstOrDefault()?.Message
                ?? "Fashion Catalog sources could not be loaded safely.");
    }

    private ZaFashionCatalogWorkflow CreateWorkflow(
        OpenedProject project,
        ZaFashionCatalogSnapshot snapshot,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        IReadOnlyList<ZaFashionCatalogTextLabel>? textLabels = null)
    {
        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            WorkflowId,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics);
        var allDiagnostics = summary.Diagnostics
            .Concat(diagnostics)
            .Distinct()
            .ToArray();
        return new ZaFashionCatalogWorkflow(
            summary,
            snapshot,
            textLabels ?? Array.Empty<ZaFashionCatalogTextLabel>(),
            new ZaFashionCatalogWorkflowStats(
                snapshot.DressUpItems.Count,
                snapshot.DressUpGroups.Count,
                snapshot.HairAndMakeup.Count,
                snapshot.DressUpLineups.Count,
                snapshot.HairAndMakeupLineups.Count),
            allDiagnostics,
            summary.Availability == ZaWorkflowAvailability.Available
                && allDiagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error));
    }

    private IReadOnlyList<ZaFashionCatalogTextLabel> LoadTextLabels(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var language = ZaGameTextLanguage.Resolve(project.Paths);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in new[] { DressUpItemNamesTable, DressUpInterfaceTable })
        {
            var loaded = TryLoadTextLabelTable(project, language, table, diagnostics);
            if (loaded is null
                && !string.Equals(language, ZaGameTextLanguage.English, StringComparison.OrdinalIgnoreCase))
            {
                loaded = TryLoadTextLabelTable(
                    project,
                    ZaGameTextLanguage.English,
                    table,
                    diagnostics);
            }

            if (loaded is null)
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Fashion Catalog display names from '{table}' are unavailable; stored keys and IDs remain visible.",
                    expected: "Readable localized Fashion Catalog message data and key tables"));
                continue;
            }

            foreach (var label in loaded)
            {
                labels.TryAdd(label.Key, label.Label);
            }
        }

        return labels
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ZaFashionCatalogTextLabel(pair.Key, pair.Value))
            .ToArray();
    }

    private IReadOnlyList<ZaFashionCatalogTextLabel>? TryLoadTextLabelTable(
        OpenedProject project,
        string language,
        string table,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var dataPath = $"ik_message/dat/{language}/{table}.dat";
        var keyPath = $"ik_message/dat/{language}/{table}.tbl";
        try
        {
            var values = SwShGameTextFile.Parse(
                    fileSource.Read(project, dataPath).Bytes,
                    fileSource.BoundedTableRecordLimit)
                .Lines
                .Select(line => line.Text)
                .ToArray();
            var keys = SwShAhtbFile.Parse(
                    fileSource.Read(project, keyPath).Bytes,
                    fileSource.BoundedTableRecordLimit)
                .Entries
                .Select(entry => entry.Name)
                .ToArray();
            var count = Math.Min(values.Length, keys.Length);
            if (count != values.Length)
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Fashion Catalog message table 'romfs/{dataPath}' has {values.Length} values but only {keys.Length} usable keys. Matched entries were retained by exact index.",
                    $"romfs/{keyPath}",
                    expected: $"At least {values.Length} message keys"));
            }

            return Enumerable.Range(0, count)
                .Where(index =>
                    !string.IsNullOrWhiteSpace(keys[index])
                    && !string.IsNullOrWhiteSpace(values[index]))
                .Select(index => new ZaFashionCatalogTextLabel(keys[index], values[index]))
                .GroupBy(label => label.Key, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (
            (exception is IOException or InvalidDataException or ArgumentException)
            && !fileSource.IsBoundedSemanticLimit(exception))
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Fashion Catalog message table 'romfs/{dataPath}' could not be decoded: {exception.Message}",
                $"romfs/{dataPath}",
                expected: "Pokemon Legends Z-A encrypted text and AHTB key tables"));
            return null;
        }
    }

    private static ZaFashionCatalogSnapshot EmptySnapshot() =>
        new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<ZaDressUpItemRecord>(),
            Array.Empty<ZaDressUpGroupRecord>(),
            Array.Empty<ZaHairAndMakeupRecord>(),
            Array.Empty<ZaFashionLineupEntryRecord>(),
            Array.Empty<ZaFashionLineupEntryRecord>());

    private static ValidationDiagnostic Error(
        string message,
        string? file = null,
        string? field = null,
        string? expected = null) =>
        ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            Domain,
            file,
            field,
            expected,
            code: ZaFashionCatalogDiagnosticCodes.Safety);

    private static ProjectFileReference Reference(ZaWorkflowFile source) =>
        new(source.SourceLayer, source.RelativePath);
}

internal sealed record ZaFashionCatalogLoadedState(
    ZaFashionCatalogWorkflow Workflow,
    ZaFashionCatalogSourceSet Sources,
    IReadOnlyList<ProjectFileReference> SourcesReferences);

internal sealed record ZaFashionCatalogPreparedUpdate(
    string VirtualPath,
    byte[] Bytes,
    string ExpectedSourceRevision,
    ZaFashionCatalogSnapshot Snapshot,
    IReadOnlyList<ProjectFileReference> Sources);
