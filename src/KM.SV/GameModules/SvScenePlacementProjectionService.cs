// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.FlatBuffers;
using KM.Core.Projects;
using KM.Formats.SV.Placement;
using KM.SV.Data;
using KM.SV.Placement;
using KM.SV.Workflows;

namespace KM.SV.GameModules;

public sealed class SvScenePlacementProjectionService
{
    private const int MaximumSourceBytesPerFile = 64 * 1024 * 1024;
    private const int MaximumSourceFiles = 8;
    private const long MaximumSourceBytes = 256L * 1024L * 1024L;
    private const int MaximumRecords = 10_000;
    private const int MaximumOwnedFields = 300_000;

    private static readonly IReadOnlyList<string> HiddenItemSourceIdentities =
    [
        SvDataPaths.HiddenItemDataTableArray,
        SvDataPaths.HiddenItemDataTableSu1Array,
        SvDataPaths.HiddenItemDataTableSu2Array,
        SvDataPaths.HiddenItemDataTableLcArray,
    ];

    private static readonly IReadOnlyList<string> ScarletVisibleItemSourceIdentities =
    [
        SvDataPaths.VisibleItemScenePaldeaScarlet,
        SvDataPaths.VisibleItemSceneKitakamiScarlet,
        SvDataPaths.VisibleItemSceneBlueberryScarlet,
    ];

    private static readonly IReadOnlyList<string> VioletVisibleItemSourceIdentities =
    [
        SvDataPaths.VisibleItemScenePaldeaViolet,
        SvDataPaths.VisibleItemSceneKitakamiViolet,
        SvDataPaths.VisibleItemSceneBlueberryViolet,
    ];

    private readonly SvCacheManager cacheManager;

    public SvScenePlacementProjectionService(SvCacheManager? cacheManager = null)
    {
        this.cacheManager = cacheManager ?? new SvCacheManager();
    }

    public SvScenePlacementProjection LoadFreshBounded(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame))
        {
            throw new InvalidDataException(
                "Scene placement inspection requires a Scarlet or Violet project.");
        }

        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidDataException(
                "Scene placement inspection requires a configured base RomFS.");
        }

        try
        {
            lock (SvWorkflowFileSource.OutputWriteSyncRoot)
            {
                var initial = CaptureObservation(paths);
                var final = CaptureObservation(paths);
                if (!ObservationsMatch(initial, final))
                {
                    throw new SvScenePlacementObservationChangedException();
                }

                return initial.Projection;
            }
        }
        catch (Exception exception) when (exception is
            IndexOutOfRangeException or
            ArgumentOutOfRangeException or
            OverflowException)
        {
            throw new InvalidDataException(
                "A scene placement source does not match the supported data layout.",
                exception);
        }
    }

    private CaptureResult CaptureObservation(ProjectPaths paths)
    {
        var source = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSourceBytesPerFile,
            MaximumSourceFiles,
            MaximumSourceBytes);
        var project = new ProjectWorkspaceService().Open(paths, DateTimeOffset.UtcNow);
        using var sourceFingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sources = new List<SvScenePlacementSource>(MaximumSourceFiles);
        var entries = new List<SvScenePlacementEntry>();
        var ownedFieldCount = 0;

        var visibleSourceIdentities = paths.SelectedGame == ProjectGame.Violet
            ? VioletVisibleItemSourceIdentities
            : ScarletVisibleItemSourceIdentities;
        foreach (var sourceIdentity in visibleSourceIdentities)
        {
            var file = ReadExactSource(source, project, sourceIdentity, sourceFingerprint);
            var points = SvVisibleItemSceneReader.Read(file.Bytes, file.VirtualPath);
            if (points.Count > MaximumRecords)
            {
                throw new InvalidDataException(
                    "A visible-item scene exceeds the bounded placement-record limit.");
            }

            var sourceRecordCount = 0;
            for (var occurrence = 0; occurrence < points.Count; occurrence++)
            {
                var point = points[occurrence];
                AddEntry(
                    entries,
                    ref ownedFieldCount,
                    SvScenePlacementDomain.VisibleItem,
                    file,
                    occurrence,
                    [
                        new SvScenePlacementOwnedField(
                            SvPlacementWorkflowService.VisibleItemIdField,
                            point.ItemFieldName is null ? null : point.ItemId),
                        new SvScenePlacementOwnedField(
                            SvPlacementWorkflowService.VisibleQuantityField,
                            point.QuantityFieldName is null ? null : point.Quantity),
                    ]);
                sourceRecordCount = checked(sourceRecordCount + 1);
            }

            sources.Add(ToSource(
                file,
                SvScenePlacementDomain.VisibleItem,
                sourceRecordCount));
        }

        foreach (var sourceIdentity in HiddenItemSourceIdentities)
        {
            var file = ReadExactSource(source, project, sourceIdentity, sourceFingerprint);
            var table = HiddenItemDataTableArray.GetRootAsHiddenItemDataTableArray(
                new ByteBuffer(file.Bytes));
            source.EnsureBoundedTableCount(
                table.ValuesLength,
                "The S/V hidden-item pool table");
            var sourceRecordCount = 0;
            for (var occurrence = 0; occurrence < table.ValuesLength; occurrence++)
            {
                if (table.Values(occurrence) is not { } row)
                {
                    continue;
                }

                var fields = new List<SvScenePlacementOwnedField>(30);
                for (var slot = 0; slot < 10; slot++)
                {
                    var item = row.Item(slot);
                    fields.Add(new SvScenePlacementOwnedField(
                        SvPlacementWorkflowService.HiddenItemField(
                            slot,
                            SvPlacementWorkflowService.HiddenItemSlotField.ItemId),
                        item?.ItemId));
                    fields.Add(new SvScenePlacementOwnedField(
                        SvPlacementWorkflowService.HiddenItemField(
                            slot,
                            SvPlacementWorkflowService.HiddenItemSlotField.Chance),
                        item?.EmergePercent));
                    fields.Add(new SvScenePlacementOwnedField(
                        SvPlacementWorkflowService.HiddenItemField(
                            slot,
                            SvPlacementWorkflowService.HiddenItemSlotField.Count),
                        item?.DropCount));
                }

                AddEntry(
                    entries,
                    ref ownedFieldCount,
                    SvScenePlacementDomain.HiddenItemPool,
                    file,
                    occurrence,
                    fields);
                sourceRecordCount = checked(sourceRecordCount + 1);
            }

            sources.Add(ToSource(
                file,
                SvScenePlacementDomain.HiddenItemPool,
                sourceRecordCount));
        }

        {
            var file = ReadExactSource(
                source,
                project,
                SvDataPaths.RummagingItemDataTableArray,
                sourceFingerprint);
            var table = RummagingItemDataTableArray.GetRootAsRummagingItemDataTableArray(
                new ByteBuffer(file.Bytes));
            source.EnsureBoundedTableCount(
                table.ValuesLength,
                "The S/V rummaging-item pool table");
            var sourceRecordCount = 0;
            for (var occurrence = 0; occurrence < table.ValuesLength; occurrence++)
            {
                if (table.Values(occurrence) is not { } row)
                {
                    continue;
                }

                var fields = new List<SvScenePlacementOwnedField>(7)
                {
                    new(
                        SvPlacementWorkflowService.RummagingCategoryField,
                        (int)row.Category),
                    new(
                        SvPlacementWorkflowService.RummagingPatternField,
                        (int)row.Pattern),
                };
                for (var slot = 0; slot < 5; slot++)
                {
                    fields.Add(new SvScenePlacementOwnedField(
                        SvPlacementWorkflowService.RummagingItemField(slot),
                        row.Item(slot)));
                }

                AddEntry(
                    entries,
                    ref ownedFieldCount,
                    SvScenePlacementDomain.RummagingItemPool,
                    file,
                    occurrence,
                    fields);
                sourceRecordCount = checked(sourceRecordCount + 1);
            }

            sources.Add(ToSource(
                file,
                SvScenePlacementDomain.RummagingItemPool,
                sourceRecordCount));
        }

        if (sources.Count != MaximumSourceFiles)
        {
            throw new InvalidDataException(
                "Scene placement inspection did not observe the exact supported source set.");
        }

        var orderedSources = sources
            .OrderBy(item => item.Domain)
            .ThenBy(item => item.SourceIdentity, StringComparer.Ordinal)
            .ToArray();
        var orderedEntries = entries
            .OrderBy(item => item.Domain)
            .ThenBy(item => item.SourceIdentity, StringComparer.Ordinal)
            .ThenBy(item => item.Occurrence)
            .ToArray();
        return new CaptureResult(
            new SvScenePlacementProjection(orderedSources, orderedEntries),
            Convert.ToHexStringLower(sourceFingerprint.GetHashAndReset()));
    }

    private static SvWorkflowFile ReadExactSource(
        SvWorkflowFileSource source,
        OpenedProject project,
        string expectedVirtualPath,
        IncrementalHash sourceFingerprint)
    {
        var file = source.Read(project, expectedVirtualPath);
        if (!string.Equals(file.VirtualPath, expectedVirtualPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Scene placement inspection resolved an unexpected virtual source identity.");
        }

        AppendSourceFingerprint(sourceFingerprint, file);
        return file;
    }

    private static void AddEntry(
        ICollection<SvScenePlacementEntry> entries,
        ref int ownedFieldCount,
        SvScenePlacementDomain domain,
        SvWorkflowFile source,
        int occurrence,
        IReadOnlyList<SvScenePlacementOwnedField> fields)
    {
        if (entries.Count >= MaximumRecords)
        {
            throw new InvalidDataException(
                "Scene placement inspection exceeds the bounded placement-record limit.");
        }

        if (occurrence < 0
            || fields.Count == 0
            || ownedFieldCount > MaximumOwnedFields - fields.Count)
        {
            throw new InvalidDataException(
                "Scene placement inspection exceeds the bounded owned-field limit.");
        }

        ownedFieldCount = checked(ownedFieldCount + fields.Count);
        var category = CategoryId(domain);
        entries.Add(new SvScenePlacementEntry(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{category}:{source.VirtualPath}:{occurrence}"),
            domain,
            source.VirtualPath,
            source.SourceLayer,
            source.FileState,
            occurrence,
            fields));
    }

    private static SvScenePlacementSource ToSource(
        SvWorkflowFile source,
        SvScenePlacementDomain domain,
        int recordCount)
    {
        return new SvScenePlacementSource(
            source.VirtualPath,
            domain,
            source.SourceLayer,
            source.FileState,
            recordCount);
    }

    private static string CategoryId(SvScenePlacementDomain domain)
    {
        return domain switch
        {
            SvScenePlacementDomain.VisibleItem =>
                SvPlacementWorkflowService.VisibleItemsCategory,
            SvScenePlacementDomain.HiddenItemPool =>
                SvPlacementWorkflowService.HiddenItemsCategory,
            SvScenePlacementDomain.RummagingItemPool =>
                SvPlacementWorkflowService.RummagingPointsCategory,
            _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, null),
        };
    }

    private static void AppendSourceFingerprint(
        IncrementalHash fingerprint,
        SvWorkflowFile source)
    {
        AppendFingerprintText(fingerprint, source.VirtualPath);
        AppendFingerprintText(
            fingerprint,
            ((int)source.SourceLayer).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintText(
            fingerprint,
            ((int)source.FileState).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintText(
            fingerprint,
            source.Bytes.Length.ToString(CultureInfo.InvariantCulture));
        fingerprint.AppendData(source.Bytes);
    }

    private static void AppendFingerprintText(
        IncrementalHash fingerprint,
        string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        fingerprint.AppendData(length);
        fingerprint.AppendData(bytes);
    }

    private static bool ObservationsMatch(CaptureResult left, CaptureResult right)
    {
        if (!string.Equals(
                left.SourceFingerprint,
                right.SourceFingerprint,
                StringComparison.Ordinal)
            || left.Projection.Sources.Count != right.Projection.Sources.Count
            || left.Projection.Entries.Count != right.Projection.Entries.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Projection.Sources.Count; index++)
        {
            if (left.Projection.Sources[index] != right.Projection.Sources[index])
            {
                return false;
            }
        }

        for (var index = 0; index < left.Projection.Entries.Count; index++)
        {
            var leftEntry = left.Projection.Entries[index];
            var rightEntry = right.Projection.Entries[index];
            if (!string.Equals(
                    leftEntry.StableIdentity,
                    rightEntry.StableIdentity,
                    StringComparison.Ordinal)
                || leftEntry.Domain != rightEntry.Domain
                || !string.Equals(
                    leftEntry.SourceIdentity,
                    rightEntry.SourceIdentity,
                    StringComparison.Ordinal)
                || leftEntry.SourceLayer != rightEntry.SourceLayer
                || leftEntry.FileState != rightEntry.FileState
                || leftEntry.Occurrence != rightEntry.Occurrence
                || !leftEntry.Fields.SequenceEqual(rightEntry.Fields))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record CaptureResult(
        SvScenePlacementProjection Projection,
        string SourceFingerprint);
}
