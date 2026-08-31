// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.GameModules;

public sealed record ZaPokemonResourceAnimation(
    int PhysicalIndex,
    short FormNumber,
    string? Path);

public sealed record ZaPokemonResourceLocator(
    int PhysicalIndex,
    short FormNumber,
    byte LocatorIndex,
    string? Path);

public sealed record ZaPokemonResourceEntry(
    int PhysicalIndex,
    ushort Species,
    ushort Form,
    byte Gender,
    string? ModelPath,
    string? DerivedBaseAnimationPath,
    string? MaterialTablePath,
    string? ConfigurationPath,
    IReadOnlyList<ZaPokemonResourceAnimation> Animations,
    IReadOnlyList<ZaPokemonResourceLocator> Locators,
    string? IconPath,
    uint RawField7,
    string? DefensePath);

public sealed record ZaPokemonResourceCatalog(
    ZaReadOnlyProjectionSource Source,
    uint Version,
    IReadOnlyList<ZaPokemonResourceEntry> Entries);

public sealed class ZaPokemonResourceCatalogService
{
    private readonly ZaWorkflowFileSource fileSource;

    public ZaPokemonResourceCatalogService()
        : this(new ZaWorkflowFileSource())
    {
    }

    internal ZaPokemonResourceCatalogService(ZaWorkflowFileSource fileSource)
    {
        this.fileSource = fileSource ?? throw new ArgumentNullException(nameof(fileSource));
    }

    public ZaPokemonResourceCatalog Load(OpenedProject project)
    {
        ZaReadOnlyProjectionSupport.ValidateProject(project, "Pokémon Resource Catalog");
        var source = fileSource.Read(project, ZaDataPaths.PokemonResourceCatalog);
        var projection = ZaPokemonResourceCatalogParser.Read(source.Bytes);
        return new ZaPokemonResourceCatalog(
            ZaReadOnlyProjectionSupport.ToSource(source),
            projection.Version,
            projection.Entries);
    }
}

internal static class ZaPokemonResourceCatalogParser
{
    private const uint SupportedCatalogVersion = 6;

    private const int CatalogFieldCount = 2;
    private const int VersionFieldCount = 1;
    private const int EntryFieldCount = 9;
    private const int SpeciesFieldCount = 3;
    private const int AnimationFieldCount = 2;
    private const int LocatorFieldCount = 3;

    private const int CatalogVersionField = 0;
    private const int CatalogEntriesField = 1;
    private const int VersionValueField = 0;
    private const int EntrySpeciesField = 0;
    private const int EntryModelPathField = 1;
    private const int EntryMaterialTablePathField = 2;
    private const int EntryConfigurationPathField = 3;
    private const int EntryAnimationsField = 4;
    private const int EntryLocatorsField = 5;
    private const int EntryIconPathField = 6;
    private const int EntryRawField7 = 7;
    private const int EntryDefensePathField = 8;
    private const int SpeciesNumberField = 0;
    private const int SpeciesFormField = 1;
    private const int SpeciesGenderField = 2;
    private const int AnimationFormNumberField = 0;
    private const int AnimationPathField = 1;
    private const int LocatorFormNumberField = 0;
    private const int LocatorIndexField = 1;
    private const int LocatorPathField = 2;

    private const int MaximumPayloadBytes = 64 * 1024 * 1024;
    private const int MaximumEntryCount = 4_096;
    private const int MaximumNestedCountPerEntry = 4_096;
    private const int MaximumAggregateNestedCount = 65_536;
    private const int MaximumStringByteLength = 65_536;
    private const long MaximumAggregateStringBytes = 32L * 1024L * 1024L;

    public static ZaPokemonResourceCatalogProjection Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            var reader = new ZaReadOnlyFlatBufferReader(
                data,
                "Pokémon resource catalog",
                MaximumPayloadBytes,
                MaximumStringByteLength,
                MaximumAggregateStringBytes);
            var root = reader.ReadRootTable(CatalogFieldCount, "resource catalog root");
            var versionTable = reader.ReadRequiredTableField(
                root,
                CatalogFieldCount,
                CatalogVersionField,
                VersionFieldCount,
                "resource catalog version table");
            var version = reader.ReadUInt32Field(
                versionTable,
                VersionFieldCount,
                VersionValueField,
                defaultValue: 0,
                "resource catalog version");
            if (version != SupportedCatalogVersion)
            {
                throw new InvalidDataException(
                    $"Pokémon resource catalog version {version} is not the established version {SupportedCatalogVersion}.");
            }

            var entryTables = reader.ReadRequiredTableVectorField(
                root,
                CatalogFieldCount,
                CatalogEntriesField,
                MaximumEntryCount,
                "resource catalog entries");
            var entries = new ZaPokemonResourceEntry[entryTables.Count];
            var identities = new HashSet<ResourceIdentity>();
            var aggregateNestedCount = 0;
            for (var index = 0; index < entryTables.Count; index++)
            {
                var entryTable = entryTables[index];
                var speciesTable = reader.ReadRequiredTableField(
                    entryTable,
                    EntryFieldCount,
                    EntrySpeciesField,
                    SpeciesFieldCount,
                    $"resource entry {index} identity");
                var species = reader.ReadUInt16Field(
                    speciesTable,
                    SpeciesFieldCount,
                    SpeciesNumberField,
                    defaultValue: 0,
                    $"resource entry {index} species");
                var form = reader.ReadUInt16Field(
                    speciesTable,
                    SpeciesFieldCount,
                    SpeciesFormField,
                    defaultValue: 0,
                    $"resource entry {index} form");
                var gender = reader.ReadByteField(
                    speciesTable,
                    SpeciesFieldCount,
                    SpeciesGenderField,
                    defaultValue: 0,
                    $"resource entry {index} gender");
                if (!identities.Add(new ResourceIdentity(species, form, gender)))
                {
                    throw new InvalidDataException(
                        $"Pokémon resource catalog repeats identity ({species}, {form}, {gender}).");
                }

                var animationTables = reader.ReadOptionalTableVectorField(
                    entryTable,
                    EntryFieldCount,
                    EntryAnimationsField,
                    MaximumNestedCountPerEntry,
                    $"resource entry {index} animations");
                var locatorTables = reader.ReadOptionalTableVectorField(
                    entryTable,
                    EntryFieldCount,
                    EntryLocatorsField,
                    MaximumNestedCountPerEntry,
                    $"resource entry {index} locators");
                aggregateNestedCount = checked(
                    aggregateNestedCount + animationTables.Count + locatorTables.Count);
                if (aggregateNestedCount > MaximumAggregateNestedCount)
                {
                    throw new InvalidDataException(
                        "Pokémon resource catalog exceeds its bounded aggregate nested-record count.");
                }

                var modelPath = reader.ReadOptionalStringField(
                    entryTable,
                    EntryFieldCount,
                    EntryModelPathField,
                    $"resource entry {index} model path");
                entries[index] = new ZaPokemonResourceEntry(
                    index,
                    species,
                    form,
                    gender,
                    modelPath,
                    DeriveBaseAnimationPath(modelPath, index),
                    reader.ReadOptionalStringField(
                        entryTable,
                        EntryFieldCount,
                        EntryMaterialTablePathField,
                        $"resource entry {index} material table path"),
                    reader.ReadOptionalStringField(
                        entryTable,
                        EntryFieldCount,
                        EntryConfigurationPathField,
                        $"resource entry {index} configuration path"),
                    ReadAnimations(reader, animationTables, index),
                    ReadLocators(reader, locatorTables, index),
                    reader.ReadOptionalStringField(
                        entryTable,
                        EntryFieldCount,
                        EntryIconPathField,
                        $"resource entry {index} icon path"),
                    reader.ReadUInt32Field(
                        entryTable,
                        EntryFieldCount,
                        EntryRawField7,
                        defaultValue: 0,
                        $"resource entry {index} raw field 7"),
                    reader.ReadOptionalStringField(
                        entryTable,
                        EntryFieldCount,
                        EntryDefensePathField,
                        $"resource entry {index} defense path"));
            }

            return new ZaPokemonResourceCatalogProjection(version, entries);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Pokémon resource catalog contains an overflowing FlatBuffer offset or count.",
                exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Pokémon resource catalog contains invalid UTF-8 text.",
                exception);
        }
    }

    private static string? DeriveBaseAnimationPath(
        string? modelPath,
        int entryIndex)
    {
        const string modelExtension = ".trmdl";
        const string pokemonDataPrefix = "ik_pokemon/data/";

        if (modelPath is null)
        {
            return null;
        }

        if (modelPath.Length == 0
            || modelPath[0] == '/'
            || modelPath[^1] == '/'
            || modelPath.Contains('\\', StringComparison.Ordinal)
            || modelPath.Contains("//", StringComparison.Ordinal)
            || modelPath.Any(char.IsControl)
            || modelPath.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"Pokémon resource entry {entryIndex} contains a malformed model path.");
        }

        if (!modelPath.EndsWith(modelExtension, StringComparison.Ordinal))
        {
            return null;
        }

        var modelStem = modelPath[..^modelExtension.Length];
        if (modelStem.Length == 0
            || modelStem.LastIndexOf('/') == modelStem.Length - 1)
        {
            return null;
        }

        return pokemonDataPrefix + modelStem + "_base.tracr";
    }

    private static IReadOnlyList<ZaPokemonResourceAnimation> ReadAnimations(
        ZaReadOnlyFlatBufferReader reader,
        IReadOnlyList<int> tables,
        int entryIndex)
    {
        var animations = new ZaPokemonResourceAnimation[tables.Count];
        for (var index = 0; index < tables.Count; index++)
        {
            animations[index] = new ZaPokemonResourceAnimation(
                index,
                reader.ReadInt16Field(
                    tables[index],
                    AnimationFieldCount,
                    AnimationFormNumberField,
                    defaultValue: 0,
                    $"resource entry {entryIndex} animation {index} form number"),
                reader.ReadOptionalStringField(
                    tables[index],
                    AnimationFieldCount,
                    AnimationPathField,
                    $"resource entry {entryIndex} animation {index} path"));
        }

        return animations;
    }

    private static IReadOnlyList<ZaPokemonResourceLocator> ReadLocators(
        ZaReadOnlyFlatBufferReader reader,
        IReadOnlyList<int> tables,
        int entryIndex)
    {
        var locators = new ZaPokemonResourceLocator[tables.Count];
        for (var index = 0; index < tables.Count; index++)
        {
            locators[index] = new ZaPokemonResourceLocator(
                index,
                reader.ReadInt16Field(
                    tables[index],
                    LocatorFieldCount,
                    LocatorFormNumberField,
                    defaultValue: 0,
                    $"resource entry {entryIndex} locator {index} form number"),
                reader.ReadByteField(
                    tables[index],
                    LocatorFieldCount,
                    LocatorIndexField,
                    defaultValue: 0,
                    $"resource entry {entryIndex} locator {index} index"),
                reader.ReadOptionalStringField(
                    tables[index],
                    LocatorFieldCount,
                    LocatorPathField,
                    $"resource entry {entryIndex} locator {index} path"));
        }

        return locators;
    }

    private readonly record struct ResourceIdentity(
        ushort Species,
        ushort Form,
        byte Gender);
}

internal sealed record ZaPokemonResourceCatalogProjection(
    uint Version,
    IReadOnlyList<ZaPokemonResourceEntry> Entries);
