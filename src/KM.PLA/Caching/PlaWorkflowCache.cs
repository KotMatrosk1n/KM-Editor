// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.Json;
using KM.Core.Files;
using KM.Core.Projects;

namespace KM.PLA.Caching;

internal sealed record PlaCacheInput(string Path, string BaseHash, string? CurrentHash, bool HasOutput);
internal sealed record PlaCacheDependencies(PlaCacheInput[] Inputs, Dictionary<string, string[]> Catalogs, string? Compression);
internal sealed record PlaCachedListing(PlaCacheDependencies Dependencies, PlaPage Page);

/// <summary>Reusable listings contain no session edits or authority to write output.</summary>
internal sealed class PlaWorkflowCache(PlaCacheManager manager)
{
    internal static readonly string[] CoreKinds = ["personal", "moves", "text", "items", "trainers"];
    internal static readonly string[] ArchiveKinds = ["encounters", "outbreaks", "weather"];

    internal PlaPage? LoadListing(PlaSourceScope scope, string kind, string? textFile)
    {
        // Prefer the current output snapshot after an edit. Checking a known
        // superseded Base listing first would reread its dependencies twice.
        foreach (var layered in new[] { true, false })
        {
            if (layered && string.IsNullOrWhiteSpace(scope.Paths.OutputRootPath)) continue;
            try
            {
                if (manager.TryGetArtifact<PlaCachedListing>(Source(scope, layered), Descriptor(scope, kind, textFile, layered), out var cached)
                    && cached?.Page is { } page && page.Kind == kind && cached.Dependencies is not null
                    && scope.Matches(cached.Dependencies)) return page;
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or JsonException)
            {
                // A cache hint may refer to a resource removed from a new catalog.
                // Rebuild from the live source, which supplies authoritative errors.
            }
            scope.ResetCacheObservations();
        }
        return null;
    }

    internal void StoreListing(PlaSourceScope scope, string kind, string? textFile, PlaPage page)
    {
        var dependencies = scope.CacheDependencies();
        var layered = dependencies.Inputs.Any(input => input.HasOutput);
        manager.SetArtifact(Source(scope, layered), Descriptor(scope, kind, textFile, layered), new PlaCachedListing(dependencies, page));
    }

    internal bool IsPrepared(PlaSourceScope scope, string kind)
    {
        var descriptor = Descriptor(scope, kind, null, false);
        try
        {
            // Completion belongs to a verified durable Base artifact, never to a
            // successful memory lookup or a previous warmup step number.
            return manager.IsArtifactPersisted<PlaCachedListing>(scope.CacheSource, descriptor)
                && manager.TryGetArtifact<PlaCachedListing>(scope.CacheSource, descriptor, out var cached)
                && cached?.Page?.Kind == kind && cached.Dependencies?.Inputs is not null
                && !cached.Dependencies.Inputs.Any(input => input.HasOutput)
                && scope.Matches(cached.Dependencies);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return false; }
    }

    private static PlaCacheSourceIdentity Source(PlaSourceScope scope, bool layered) => !layered ? scope.CacheSource
        : scope.CacheSource with { Sources = scope.CacheSource.Sources.Select(stamp => stamp with { SourceLayer = ProjectFileLayer.Layered }).ToArray() };

    private static PlaCacheArtifactDescriptor Descriptor(PlaSourceScope scope, string kind, string? textFile, bool layered)
    {
        static string Root(string? path) => string.IsNullOrWhiteSpace(path) ? "" : OperatingSystem.IsWindows()
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant() : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var key = JsonSerializer.Serialize(new[] { Root(scope.Paths.BaseRomFsPath), Root(scope.Paths.BaseExeFsPath),
            Root(scope.Paths.PokemonLegendsArceusSupportFolderPath), scope.Language, kind switch
            {
                "moves" => "moves-v2",
                "items" => "items-with-provenance",
                _ => kind,
            }, textFile ?? "",
            layered ? Root(scope.Paths.OutputRootPath) : "" });
        return new("editor-listing", PlaSourceScope.Hash(Encoding.UTF8.GetBytes(key)),
            PlaWildCatalog.Supports(kind) || PlaEventCatalog.Supports(kind) || kind == "weather" || PlaSettingsCatalog.Supports(kind)
                ? PlaCacheArtifactPolicy.Performance : PlaCacheArtifactPolicy.Balanced);
    }
}
