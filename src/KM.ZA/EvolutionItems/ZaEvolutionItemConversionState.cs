// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Pokemon;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.EvolutionItems;

internal sealed class ZaEvolutionItemConversionState
{
    private static readonly AllocationCandidate[] AllocationCandidates =
    [
        // This is an allowlist. Parameters 11-14, 50, 119, and every omitted row are never allocated.
        // Tier 1: no known Z-A consumer or historical assignment.
        new(17),
        new(18),
        .. Enumerable.Range(42, 7).Select(parameterId => new AllocationCandidate(parameterId)),
        new(90),
        new(91),
        .. Enumerable.Range(103, 3).Select(parameterId => new AllocationCandidate(parameterId)),

        // Tier 2: historical S/V DLC assignments without an active Z-A consumer.
        new(94),
        new(95),
        new(96),

        // Tier 3: dormant form parameters whose affected species are unavailable in stock Z-A.
        .. Enumerable.Range(53, 17).Select(parameterId => new AllocationCandidate(parameterId)),
        .. Enumerable.Range(72, 7).Select(parameterId => new AllocationCandidate(parameterId)),
        .. Enumerable.Range(97, 5).Select(parameterId => new AllocationCandidate(parameterId)),
        new(106),

        // Tier 4: reclaim only the exact stock mapping when its source item remains absent.
        new(9, 110),
        new(10, 1779),
        new(15, 229),
        new(19, 280),
        .. Enumerable.Range(0, 16).Select(index => new AllocationCandidate(26 + index, 298 + index)),
        new(49, 326),
        new(51, 644),
        new(70, 1103),
        new(71, 1104),
        new(79, 1116),
        new(80, 1117),
        new(81, 1253),
        new(82, 1254),
        new(87, 2345),
        new(88, 1857),
        new(89, 1858),
    ];

    private readonly List<EvolutionItemConversion> rows;
    private readonly Dictionary<int, int> itemToParameter;
    private readonly Dictionary<int, int> parameterToItem;
    private readonly HashSet<int> parameterIds;
    private readonly HashSet<int> knownItemIds;
    private readonly HashSet<int> activePersonalParameters;
    private readonly bool canReclaimMappedRows;
    private readonly bool canAllocateNewMappings;

    private ZaEvolutionItemConversionState(
        ZaWorkflowFile source,
        List<EvolutionItemConversion> rows,
        Dictionary<int, int> itemToParameter,
        Dictionary<int, int> parameterToItem,
        HashSet<int> parameterIds,
        HashSet<int> knownItemIds,
        bool canReclaimMappedRows,
        HashSet<int> activePersonalParameters,
        bool canAllocateNewMappings)
    {
        Source = source;
        this.rows = rows;
        this.itemToParameter = itemToParameter;
        this.parameterToItem = parameterToItem;
        this.parameterIds = parameterIds;
        this.knownItemIds = knownItemIds;
        this.canReclaimMappedRows = canReclaimMappedRows;
        this.activePersonalParameters = activePersonalParameters;
        this.canAllocateNewMappings = canAllocateNewMappings;
    }

    public ZaWorkflowFile Source { get; }

    public bool Modified { get; private set; }

    public static ZaEvolutionItemConversionState Load(OpenedProject project, ZaWorkflowFileSource fileSource)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fileSource);

        var source = fileSource.Read(project, ZaDataPaths.EvolutionItemConversionArray);
        var rows = EvolutionItemConversionTable.Read(source.Bytes).ToList();
        var itemToParameter = new Dictionary<int, int>();
        var parameterToItem = new Dictionary<int, int>();
        var parameterIds = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row.ParameterId <= 0 || row.ParameterId > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"Evolution item conversion parameter {row.ParameterId} is outside the supported range.");
            }

            parameterIds.Add(row.ParameterId);
            if (row.ItemId <= 0)
            {
                continue;
            }

            if (!itemToParameter.TryAdd(row.ItemId, row.ParameterId))
            {
                throw new InvalidDataException($"Evolution item conversion item {row.ItemId} is duplicated.");
            }

            if (!parameterToItem.TryAdd(row.ParameterId, row.ItemId))
            {
                throw new InvalidDataException(
                    $"Evolution item conversion parameter {row.ParameterId} has conflicting item assignments.");
            }
        }

        var knownItemIds = TryReadKnownItemIds(project, fileSource, out var canReclaimMappedRows);
        var activePersonalParameters = TryReadActivePersonalParameters(
            project,
            fileSource,
            out var canAllocateNewMappings);
        return new ZaEvolutionItemConversionState(
            source,
            rows,
            itemToParameter,
            parameterToItem,
            parameterIds,
            knownItemIds,
            canReclaimMappedRows,
            activePersonalParameters,
            canAllocateNewMappings);
    }

    public bool TryDecode(int parameterId, out int itemId)
    {
        return parameterToItem.TryGetValue(parameterId, out itemId);
    }

    public int Encode(int itemId)
    {
        if (itemId <= 0 || itemId > ushort.MaxValue)
        {
            throw new InvalidDataException($"Evolution item id {itemId} is outside the supported range.");
        }

        if (itemToParameter.TryGetValue(itemId, out var existing))
        {
            return existing;
        }

        if (!canAllocateNewMappings)
        {
            throw new InvalidDataException(
                "Evolution item conversion allocation requires readable active Pokemon personal data.");
        }

        foreach (var candidate in AllocationCandidates)
        {
            var rowIndex = FindAvailableRow(candidate);
            if (rowIndex < 0)
            {
                continue;
            }

            var previous = rows[rowIndex];
            if (previous.ItemId > 0)
            {
                itemToParameter.Remove(previous.ItemId);
                parameterToItem.Remove(previous.ParameterId);
            }

            var assigned = previous with { ItemId = itemId };
            rows[rowIndex] = assigned;
            itemToParameter.Add(itemId, assigned.ParameterId);
            parameterToItem.Add(assigned.ParameterId, itemId);
            Modified = true;
            return assigned.ParameterId;
        }

        throw new InvalidDataException(
            $"No approved evolution item conversion slot is available for item {itemId}.");
    }

    public bool TryMigrateLegacyArgument(int storedArgument, out int encodedArgument)
    {
        encodedArgument = storedArgument;
        if (parameterToItem.ContainsKey(storedArgument))
        {
            return false;
        }

        var isKnownLegacyItem = knownItemIds.Contains(storedArgument);
        var isOutsideParameterTable = storedArgument > 0 && !parameterIds.Contains(storedArgument);
        if (!isKnownLegacyItem || !isOutsideParameterTable)
        {
            return false;
        }

        encodedArgument = Encode(storedArgument);
        return encodedArgument != storedArgument;
    }

    public byte[] Write()
    {
        return EvolutionItemConversionTable.Write(rows);
    }

    public ProjectFileReference SourceReference()
    {
        return new ProjectFileReference(Source.SourceLayer, Source.RelativePath);
    }

    private int FindAvailableRow(AllocationCandidate candidate)
    {
        if (rows.Count(row => row.ParameterId == candidate.ParameterId) != 1)
        {
            return -1;
        }

        if (activePersonalParameters.Contains(candidate.ParameterId))
        {
            return -1;
        }

        if (candidate.ReplacedItemId is { } replacedItemId)
        {
            if (!canReclaimMappedRows
                || knownItemIds.Contains(replacedItemId)
                || !parameterToItem.TryGetValue(candidate.ParameterId, out var mappedItemId)
                || mappedItemId != replacedItemId)
            {
                return -1;
            }

            return rows.FindIndex(row =>
                row.ParameterId == candidate.ParameterId && row.ItemId == replacedItemId);
        }

        if (parameterToItem.ContainsKey(candidate.ParameterId))
        {
            return -1;
        }

        return rows.FindIndex(row => row.ParameterId == candidate.ParameterId && row.ItemId == 0);
    }

    private static HashSet<int> TryReadKnownItemIds(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        out bool succeeded)
    {
        var knownItemIds = new HashSet<int>();
        succeeded = false;
        try
        {
            var itemSource = fileSource.Read(project, ZaDataPaths.ItemDataArray);
            var itemTable = ZaItemDataArray.GetRootAsZaItemDataArray(new ByteBuffer(itemSource.Bytes));
            for (var index = 0; index < itemTable.ValuesLength; index++)
            {
                if (itemTable.Values(index) is { } item && item.Id > 0)
                {
                    knownItemIds.Add(item.Id);
                }
            }

            succeeded = true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            // Active item data protects reclaimed mappings when a mod restores their source items.
        }

        return knownItemIds;
    }

    private static HashSet<int> TryReadActivePersonalParameters(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        out bool succeeded)
    {
        var parameters = new HashSet<int>();
        succeeded = false;
        try
        {
            var personalSource = fileSource.Read(project, ZaDataPaths.PersonalArray);
            var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(personalSource.Bytes));
            for (var entryIndex = 0; entryIndex < table.EntryLength; entryIndex++)
            {
                if (table.Entry(entryIndex) is not { } entry || !entry.IsPresent)
                {
                    continue;
                }

                for (var evolutionIndex = 0; evolutionIndex < entry.EvolutionsLength; evolutionIndex++)
                {
                    if (entry.Evolutions(evolutionIndex) is { } evolution
                        && IsConversionBackedMethod(evolution.Condition)
                        && evolution.Parameter > 0)
                    {
                        parameters.Add(evolution.Parameter);
                    }
                }
            }

            succeeded = true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            // New allocations fail closed when active personal consumers cannot be inspected.
        }

        return parameters;
    }

    private static bool IsConversionBackedMethod(int method)
    {
        return method is 8 or 17 or 18 or 19 or 20 or 42;
    }

    private sealed record AllocationCandidate(int ParameterId, int? ReplacedItemId = null);
}
