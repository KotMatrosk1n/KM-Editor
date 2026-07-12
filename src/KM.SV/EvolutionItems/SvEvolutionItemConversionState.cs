// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Pokemon;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.EvolutionItems;

internal sealed class SvEvolutionItemConversionState
{
    private static readonly int[] AllocationParameters =
    [
        // This is an allowlist. S/V never allocates 11-14, 50, duplicate 119, or any omitted row.
        17,
        18,
        .. Enumerable.Range(42, 7),
        90,
        91,
        .. Enumerable.Range(53, 17),
    ];

    private readonly List<EvolutionItemConversion> rows;
    private readonly Dictionary<int, int> itemToParameter;
    private readonly Dictionary<int, int> parameterToItem;
    private readonly HashSet<int> parameterIds;
    private readonly HashSet<int> knownItemIds;
    private readonly HashSet<int> activePersonalParameters;
    private readonly bool canAllocateNewMappings;

    private SvEvolutionItemConversionState(
        SvWorkflowFile source,
        List<EvolutionItemConversion> rows,
        Dictionary<int, int> itemToParameter,
        Dictionary<int, int> parameterToItem,
        HashSet<int> parameterIds,
        HashSet<int> knownItemIds,
        HashSet<int> activePersonalParameters,
        bool canAllocateNewMappings)
    {
        Source = source;
        this.rows = rows;
        this.itemToParameter = itemToParameter;
        this.parameterToItem = parameterToItem;
        this.parameterIds = parameterIds;
        this.knownItemIds = knownItemIds;
        this.activePersonalParameters = activePersonalParameters;
        this.canAllocateNewMappings = canAllocateNewMappings;
    }

    public SvWorkflowFile Source { get; }

    public bool Modified { get; private set; }

    public static SvEvolutionItemConversionState Load(OpenedProject project, SvWorkflowFileSource fileSource)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fileSource);

        var source = fileSource.Read(project, SvDataPaths.EvolutionItemConversionArray);
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

            // The stock S/V table intentionally has a blank row and one assigned row for parameter 119.
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

        var activePersonalParameters = TryReadActivePersonalParameters(
            project,
            fileSource,
            out var canAllocateNewMappings);
        return new SvEvolutionItemConversionState(
            source,
            rows,
            itemToParameter,
            parameterToItem,
            parameterIds,
            TryReadKnownItemIds(project, fileSource),
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

        foreach (var parameterId in AllocationParameters)
        {
            if (parameterToItem.ContainsKey(parameterId)
                || activePersonalParameters.Contains(parameterId)
                || rows.Count(row => row.ParameterId == parameterId) != 1)
            {
                continue;
            }

            var rowIndex = rows.FindIndex(row => row.ParameterId == parameterId && row.ItemId == 0);
            if (rowIndex < 0)
            {
                continue;
            }

            var assigned = rows[rowIndex] with { ItemId = itemId };
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

    private static HashSet<int> TryReadKnownItemIds(
        OpenedProject project,
        SvWorkflowFileSource fileSource)
    {
        var knownItemIds = new HashSet<int>();
        try
        {
            var itemSource = fileSource.Read(project, SvDataPaths.ItemDataArray);
            var itemTable = global::ItemDataArray.GetRootAsItemDataArray(new ByteBuffer(itemSource.Bytes));
            for (var index = 0; index < itemTable.ValuesLength; index++)
            {
                if (itemTable.Values(index) is { } item && item.Id > 0)
                {
                    knownItemIds.Add(item.Id);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            // Item data only improves recognition of legacy raw item arguments.
        }

        return knownItemIds;
    }

    private static HashSet<int> TryReadActivePersonalParameters(
        OpenedProject project,
        SvWorkflowFileSource fileSource,
        out bool succeeded)
    {
        var parameters = new HashSet<int>();
        succeeded = false;
        try
        {
            var personalSource = fileSource.Read(project, SvDataPaths.PersonalArray);
            var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(personalSource.Bytes));
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
}
