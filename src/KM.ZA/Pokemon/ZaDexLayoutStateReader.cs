// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.FlatBuffers;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.Pokemon;

internal sealed record ZaDexLayoutState(
    int RegularCount,
    IReadOnlyDictionary<int, int> Assignments,
    ZaWorkflowFile PersonalSource,
    ZaWorkflowFile ContentsSource,
    ZaWorkflowFile MegaContentsSource)
{
    public string Fingerprint => ZaDexLayoutStateReader.CreateFingerprint(
        RegularCount,
        Assignments);

    public IReadOnlyList<ProjectFileReference> SourceReferences =>
    [
        ZaWorkflowFileSource.CreateReference(PersonalSource),
        ZaWorkflowFileSource.CreateReference(ContentsSource),
        ZaWorkflowFileSource.CreateReference(MegaContentsSource),
    ];
}

internal static class ZaDexLayoutStateReader
{
    public static ZaDexLayoutState ReadBase(
        OpenedProject project,
        ZaWorkflowFileSource fileSource)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fileSource);

        var personalSource = fileSource.ReadBase(project, ZaDataPaths.PersonalArray);
        var contentsSource = fileSource.ReadBase(project, ZaDataPaths.PokedexContentsData);
        var megaContentsSource = fileSource.ReadBase(
            project,
            ZaDataPaths.PokedexMegaContentsData);
        var assignments = ReadAssignments(personalSource.Bytes);
        var regularCount = ReadAndValidateGroups(contentsSource.Bytes, assignments);
        ValidateMegaContents(megaContentsSource.Bytes, assignments);

        return new ZaDexLayoutState(
            regularCount,
            assignments,
            personalSource,
            contentsSource,
            megaContentsSource);
    }

    public static string CreateFingerprint(
        int regularCount,
        IReadOnlyDictionary<int, int> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        var canonical = new StringBuilder();
        canonical.Append(regularCount.ToString(CultureInfo.InvariantCulture));
        canonical.Append('|');
        foreach (var assignment in assignments.OrderBy(pair => pair.Key))
        {
            canonical.Append(assignment.Key.ToString(CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(assignment.Value.ToString(CultureInfo.InvariantCulture));
            canonical.Append(',');
        }

        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static IReadOnlyDictionary<int, int> ReadAssignments(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(bytes));
        if (table.HasLegacyByteZADexOrderLayout)
        {
            throw new InvalidDataException(
                "The base Pokemon personal table uses an unsupported legacy Pokédex layout.");
        }

        var assignments = new Dictionary<int, int>();
        for (var index = 0; index < table.EntryLength; index++)
        {
            var row = table.Entry(index);
            if (row is null || !row.Value.IsPresent)
            {
                continue;
            }

            if (row.Value.Species is not { } species || row.Value.ZADexOrder <= 0)
            {
                throw new InvalidDataException(
                    $"Active base Pokemon personal row {index} is missing its species or positive Pokédex slot.");
            }

            var speciesId = (int)species.Species;
            var internalIndex = (int)row.Value.ZADexOrder;
            if (assignments.TryGetValue(speciesId, out var existingIndex))
            {
                if (existingIndex != internalIndex)
                {
                    throw new InvalidDataException(
                        $"Base species {speciesId} does not share one Pokédex slot across its active forms.");
                }

                continue;
            }

            assignments.Add(speciesId, internalIndex);
        }

        if (assignments.Count != ZaDexLayoutMainPatcher.TotalDexSpeciesCount)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The base Pokédex must contain exactly {ZaDexLayoutMainPatcher.TotalDexSpeciesCount} active species."));
        }

        if (!assignments.Values
                .Order()
                .SequenceEqual(Enumerable.Range(1, assignments.Count)))
        {
            throw new InvalidDataException(
                "Base Pokédex slots are not one unique contiguous range.");
        }

        return assignments;
    }

    private static int ReadAndValidateGroups(
        byte[] bytes,
        IReadOnlyDictionary<int, int> assignments)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(assignments);

        var contents = ZaPokedexContentsTable.Read(bytes);
        var rows = contents.Rows.ToArray();
        if (rows.Any(row => !row.HasKnownGroup)
            || rows.Select(row => row.Species).Distinct().Count() != rows.Length)
        {
            throw new InvalidDataException(
                "The base Pokédex contents table has duplicate species or an unsupported group.");
        }

        if (!rows.Select(row => row.Species).Order().SequenceEqual(assignments.Keys.Order()))
        {
            throw new InvalidDataException(
                "The base Pokédex contents table does not exactly cover the active base species.");
        }

        var groupBySpecies = rows.ToDictionary(row => row.Species, row => row.Group);
        var regularIndices = assignments
            .Where(pair => groupBySpecies[pair.Key] == (int)ZaPokedexContentsGroup.Regular)
            .Select(pair => pair.Value)
            .Order()
            .ToArray();
        var hyperspaceIndices = assignments
            .Where(pair => groupBySpecies[pair.Key] == (int)ZaPokedexContentsGroup.Hyperspace)
            .Select(pair => pair.Value)
            .Order()
            .ToArray();
        if (regularIndices.Length == 0
            || hyperspaceIndices.Length == 0
            || !regularIndices.SequenceEqual(Enumerable.Range(1, regularIndices.Length))
            || !hyperspaceIndices.SequenceEqual(
                Enumerable.Range(regularIndices.Length + 1, hyperspaceIndices.Length)))
        {
            throw new InvalidDataException(
                "Base Regular and Hyperspace species do not occupy one contiguous range each.");
        }

        return regularIndices.Length;
    }

    private static void ValidateMegaContents(
        byte[] bytes,
        IReadOnlyDictionary<int, int> assignments)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(assignments);

        var rows = ZaPokedexMegaContentsTable.Read(bytes).Rows;
        if (rows.Any(row => !row.HasKnownGroup)
            || rows.Select(row => row.Species).Distinct().Except(assignments.Keys).Any())
        {
            throw new InvalidDataException(
                "The base Mega Pokédex contents table has an unsupported group or species.");
        }
    }
}
