// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.GameModules;

public enum ZaNamedCatalogKind
{
    Flag,
    WorkVariable,
}

public sealed record ZaNamedFlagEntry(
    string SourcePath,
    int PhysicalIndex,
    ZaNamedCatalogKind CatalogKind,
    string Name);

public sealed record ZaNamedFlagFileCatalog(
    ZaReadOnlyProjectionSource Source,
    ZaNamedCatalogKind CatalogKind,
    IReadOnlyList<ZaNamedFlagEntry> Entries);

public sealed record ZaNamedFlagCatalog(
    IReadOnlyList<ZaNamedFlagFileCatalog> Sources,
    int TotalEntryCount);

public sealed class ZaNamedFlagCatalogService
{
    private static readonly SourceDefinition[] Sources =
    [
        new(ZaDataPaths.EventFlagCatalog, ZaNamedCatalogKind.Flag),
        new(ZaDataPaths.SystemFlagCatalog, ZaNamedCatalogKind.Flag),
        new(ZaDataPaths.TemporaryFlagCatalog, ZaNamedCatalogKind.Flag),
        new(ZaDataPaths.QuestWorkCatalog, ZaNamedCatalogKind.WorkVariable),
        new(ZaDataPaths.SystemWorkCatalog, ZaNamedCatalogKind.WorkVariable),
        new(ZaDataPaths.TemporaryWorkCatalog, ZaNamedCatalogKind.WorkVariable),
        new(ZaDataPaths.MomijiWorkCatalog, ZaNamedCatalogKind.WorkVariable),
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaNamedFlagCatalogService()
        : this(new ZaWorkflowFileSource())
    {
    }

    internal ZaNamedFlagCatalogService(ZaWorkflowFileSource fileSource)
    {
        this.fileSource = fileSource ?? throw new ArgumentNullException(nameof(fileSource));
    }

    public ZaNamedFlagCatalog Load(OpenedProject project)
    {
        ZaReadOnlyProjectionSupport.ValidateProject(project, "Named Flag and Work Catalog");

        var catalogs = new ZaNamedFlagFileCatalog[Sources.Length];
        var totalEntryCount = 0;
        for (var sourceIndex = 0; sourceIndex < Sources.Length; sourceIndex++)
        {
            var definition = Sources[sourceIndex];
            var source = fileSource.Read(project, definition.Path);
            var entries = ZaNamedFlagCatalogParser.Read(
                source.Bytes,
                source.VirtualPath,
                definition.CatalogKind);
            totalEntryCount = checked(totalEntryCount + entries.Count);
            catalogs[sourceIndex] = new ZaNamedFlagFileCatalog(
                ZaReadOnlyProjectionSupport.ToSource(source),
                definition.CatalogKind,
                entries);
        }

        return new ZaNamedFlagCatalog(catalogs, totalEntryCount);
    }

    private sealed record SourceDefinition(
        string Path,
        ZaNamedCatalogKind CatalogKind);
}

internal static class ZaNamedFlagCatalogParser
{
    private const int RootFieldCount = 1;
    private const int FlagFieldCount = 1;
    private const int ItemsField = 0;
    private const int NameField = 0;
    private const int MaximumPayloadBytes = 8 * 1024 * 1024;
    private const int MaximumFlagCount = 65_536;
    private const int MaximumStringByteLength = 4_096;
    private const long MaximumAggregateStringBytes = 16L * 1024L * 1024L;

    public static IReadOnlyList<ZaNamedFlagEntry> Read(
        byte[] data,
        string sourcePath,
        ZaNamedCatalogKind catalogKind)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!Enum.IsDefined(catalogKind))
        {
            throw new ArgumentOutOfRangeException(nameof(catalogKind));
        }

        try
        {
            var reader = new ZaReadOnlyFlatBufferReader(
                data,
                "Named flag catalog",
                MaximumPayloadBytes,
                MaximumStringByteLength,
                MaximumAggregateStringBytes);
            var root = reader.ReadRootTable(RootFieldCount, "named flag root");
            var tables = reader.ReadRequiredTableVectorField(
                root,
                RootFieldCount,
                ItemsField,
                MaximumFlagCount,
                "named flag entries");

            var entries = new ZaNamedFlagEntry[tables.Count];
            for (var index = 0; index < tables.Count; index++)
            {
                entries[index] = new ZaNamedFlagEntry(
                    sourcePath,
                    index,
                    catalogKind,
                    reader.ReadRequiredStringField(
                        tables[index],
                        FlagFieldCount,
                        NameField,
                        $"named flag entry {index} name"));
            }

            return entries;
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Named flag catalog contains an overflowing FlatBuffer offset or count.",
                exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Named flag catalog contains invalid UTF-8 text.",
                exception);
        }
    }
}
