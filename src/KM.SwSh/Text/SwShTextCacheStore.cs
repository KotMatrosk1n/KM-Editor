// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;

namespace KM.SwSh.Text;

internal sealed record SwShTextBaseSource(
    string VirtualPath,
    string Context,
    string DataPath,
    string? KeyPath);

internal sealed record SwShTextCachedLine(
    string Value,
    string? MessageKey);

internal sealed record SwShTextCachedSource(
    string VirtualPath,
    string Context,
    IReadOnlyList<SwShTextCachedLine> Lines);

internal sealed record SwShTextCategoryCacheData(
    IReadOnlyList<SwShTextCachedSource> Sources,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed class SwShTextCacheStore
{
    public const string ParserVersion = "swsh-text-cache-v1";

    private const int RuntimeCategoryCapacity = 16;
    private const string TextDomain = "workflow.text";

    private readonly SwShCacheManager? cacheManager;
    private readonly object syncRoot = new();
    private readonly Dictionary<string, SwShCacheSourceIdentity> sourceIdentities =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeCategory> runtimeCategories =
        new(StringComparer.Ordinal);
    private readonly LinkedList<string> runtimeLru = [];

    public SwShTextCacheStore(SwShCacheManager? cacheManager)
    {
        this.cacheManager = cacheManager;
    }

    public SwShTextCategoryCacheData LoadBaseCategory(
        ProjectGame selectedGame,
        string language,
        string categoryId,
        IReadOnlyList<SwShTextBaseSource> sources)
    {
        return LoadBaseCategoryCore(
            selectedGame,
            language,
            categoryId,
            sources,
            verifyPersistence: false).Data;
    }

    public bool WarmBaseCategory(
        ProjectGame selectedGame,
        string language,
        string categoryId,
        IReadOnlyList<SwShTextBaseSource> sources)
    {
        return LoadBaseCategoryCore(
            selectedGame,
            language,
            categoryId,
            sources,
            verifyPersistence: true).IsPersisted;
    }

    private CacheLoadResult LoadBaseCategoryCore(
        ProjectGame selectedGame,
        string language,
        string categoryId,
        IReadOnlyList<SwShTextBaseSource> sources,
        bool verifyPersistence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            return new CacheLoadResult(
                new SwShTextCategoryCacheData([], []),
                IsPersisted: false);
        }

        var groupKey = CreateGroupKey(selectedGame, language, categoryId, sources);
        lock (syncRoot)
        {
            var hasRetained = TryGetRuntimeCategory(groupKey, out var retained);
            if (hasRetained && !verifyPersistence)
            {
                return new CacheLoadResult(retained, IsPersisted: false);
            }

            SwShTextCategoryCacheData data;
            SwShCacheSourceIdentity? identity = null;
            if (cacheManager is null)
            {
                data = hasRetained ? retained : ParseSources(sources);
            }
            else
            {
                try
                {
                    data = LoadManagedCategory(
                        selectedGame,
                        language,
                        categoryId,
                        sources,
                        groupKey,
                        out identity);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
                {
                    data = hasRetained ? retained : ParseSources(sources);
                }
            }

            AddRuntimeCategory(groupKey, data);
            var isPersisted = false;
            if (verifyPersistence && cacheManager is not null && identity is not null)
            {
                var artifact = CreateArtifact(language, categoryId);
                isPersisted = cacheManager.IsArtifactPersisted<SwShTextCategoryCacheData>(
                    identity,
                    artifact);
                if (!isPersisted)
                {
                    cacheManager.SetArtifact(identity, artifact, data);
                    isPersisted = cacheManager.IsArtifactPersisted<SwShTextCategoryCacheData>(
                        identity,
                        artifact);
                }
            }

            return new CacheLoadResult(data, isPersisted);
        }
    }

    public void ClearMemoryCache()
    {
        lock (syncRoot)
        {
            sourceIdentities.Clear();
            runtimeCategories.Clear();
            runtimeLru.Clear();
        }
    }

    private SwShTextCategoryCacheData LoadManagedCategory(
        ProjectGame selectedGame,
        string language,
        string categoryId,
        IReadOnlyList<SwShTextBaseSource> sources,
        string groupKey,
        out SwShCacheSourceIdentity identity)
    {
        if (!sourceIdentities.TryGetValue(groupKey, out var retainedIdentity))
        {
            var cacheSources = sources
                .SelectMany(source => EnumerateCacheSources(source))
                .ToArray();
            retainedIdentity = cacheManager!.CaptureSourceIdentity(
                selectedGame,
                cacheSources,
                $"{ParserVersion};language={language};category={categoryId}");
            sourceIdentities.Add(groupKey, retainedIdentity);
        }

        identity = retainedIdentity;

        var artifact = CreateArtifact(language, categoryId);
        return cacheManager!.GetOrCreateArtifact(
            identity,
            artifact,
            () => ParseSources(sources));
    }

    private static SwShCacheArtifactDescriptor CreateArtifact(
        string language,
        string categoryId)
    {
        return new SwShCacheArtifactDescriptor(
            "text.category",
            $"{language}/{categoryId}",
            SwShCacheArtifactPolicy.Balanced);
    }

    private static IEnumerable<SwShCacheSourceFile> EnumerateCacheSources(
        SwShTextBaseSource source)
    {
        yield return new SwShCacheSourceFile(source.DataPath, ProjectFileLayer.Base);
        if (!string.IsNullOrWhiteSpace(source.KeyPath))
        {
            yield return new SwShCacheSourceFile(source.KeyPath, ProjectFileLayer.Base);
        }
    }

    private static SwShTextCategoryCacheData ParseSources(
        IReadOnlyList<SwShTextBaseSource> sources)
    {
        var parsedSources = new List<SwShTextCachedSource>(sources.Count);
        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var source in sources)
        {
            try
            {
                var textFile = SwShGameTextFile.Parse(File.ReadAllBytes(source.DataPath));
                var keys = LoadMessageKeys(source, textFile.Lines.Count, diagnostics);
                var lines = new SwShTextCachedLine[textFile.Lines.Count];
                for (var lineIndex = 0; lineIndex < textFile.Lines.Count; lineIndex++)
                {
                    lines[lineIndex] = new SwShTextCachedLine(
                        textFile.Lines[lineIndex].Text,
                        lineIndex < keys.Count && !string.IsNullOrWhiteSpace(keys[lineIndex])
                            ? keys[lineIndex]
                            : null);
                }

                parsedSources.Add(new SwShTextCachedSource(
                    source.VirtualPath,
                    source.Context,
                    lines));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Message table '{source.VirtualPath}' could not be decoded: {exception.Message}",
                    source.VirtualPath,
                    "Sword/Shield encrypted text table"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table '{source.VirtualPath}' could not be read: {exception.Message}",
                    source.VirtualPath,
                    "Readable Sword/Shield message table"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table '{source.VirtualPath}' could not be read: {exception.Message}",
                    source.VirtualPath,
                    "Readable Sword/Shield message table"));
            }
        }

        return new SwShTextCategoryCacheData(parsedSources, diagnostics);
    }

    private static IReadOnlyList<string> LoadMessageKeys(
        SwShTextBaseSource source,
        int lineCount,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(source.KeyPath))
        {
            return [];
        }

        try
        {
            var keys = SwShAhtbFile.Parse(File.ReadAllBytes(source.KeyPath))
                .Entries
                .Select(entry => entry.Name)
                .ToArray();
            var hasExpectedSentinel = keys.Length == lineCount + 1
                && keys[^1].EndsWith("_max", StringComparison.OrdinalIgnoreCase);
            if (keys.Length != lineCount && !hasExpectedSentinel)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Message key table '{Path.ChangeExtension(source.VirtualPath, ".tbl")}' has {keys.Length} keys for {lineCount} editable lines. Available keys were used by line index.",
                    Path.ChangeExtension(source.VirtualPath, ".tbl"),
                    $"{lineCount} keys, optionally followed by one *_max sentinel"));
            }

            return keys.Length <= lineCount ? keys : keys[..lineCount];
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Message key table '{Path.ChangeExtension(source.VirtualPath, ".tbl")}' could not be decoded: {exception.Message}",
                Path.ChangeExtension(source.VirtualPath, ".tbl"),
                "Sword/Shield AHTB message-key table"));
            return [];
        }
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string file,
        string expected)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: TextDomain,
            Expected: expected);
    }

    private bool TryGetRuntimeCategory(
        string groupKey,
        out SwShTextCategoryCacheData data)
    {
        if (!runtimeCategories.TryGetValue(groupKey, out var runtime))
        {
            data = default!;
            return false;
        }

        runtimeLru.Remove(runtime.LruNode);
        runtimeLru.AddLast(runtime.LruNode);
        data = runtime.Data;
        return true;
    }

    private void AddRuntimeCategory(string groupKey, SwShTextCategoryCacheData data)
    {
        if (runtimeCategories.Remove(groupKey, out var existing))
        {
            runtimeLru.Remove(existing.LruNode);
        }

        var node = runtimeLru.AddLast(groupKey);
        runtimeCategories.Add(groupKey, new RuntimeCategory(data, node));
        while (runtimeCategories.Count > RuntimeCategoryCapacity && runtimeLru.First is { } first)
        {
            runtimeCategories.Remove(first.Value);
            runtimeLru.RemoveFirst();
        }
    }

    private static string CreateGroupKey(
        ProjectGame selectedGame,
        string language,
        string categoryId,
        IReadOnlyList<SwShTextBaseSource> sources)
    {
        var firstPath = Path.GetFullPath(sources[0].DataPath);
        return $"{(int)selectedGame}:{firstPath}:{language}:{categoryId}";
    }

    private sealed record RuntimeCategory(
        SwShTextCategoryCacheData Data,
        LinkedListNode<string> LruNode);

    private sealed record CacheLoadResult(
        SwShTextCategoryCacheData Data,
        bool IsPersisted);
}
