// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.Formats.SwSh;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.Text;

internal sealed record SvTextBaseSource(
    string VirtualPath,
    string Context);

internal sealed record SvTextCachedLine(
    string Value,
    string? MessageKey);

internal sealed record SvTextCachedSource(
    string VirtualPath,
    string Context,
    IReadOnlyList<SvTextCachedLine> Lines);

internal sealed record SvTextCategoryCacheData(
    IReadOnlyList<SvTextCachedSource> Sources,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed class SvTextCacheStore
{
    public const string ParserVersion = "sv-text-cache-v1";

    private const long RuntimeCacheCapacityBytes = 64L * 1024 * 1024;
    private const string TextDomain = "workflow.text";

    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SvWorkflowFileSource fileSource;
    private readonly SvCacheManager? cacheManager;
    private readonly object syncRoot = new();
    private readonly Dictionary<string, RuntimeCategory> runtimeCategories = new(StringComparer.Ordinal);
    private readonly LinkedList<string> runtimeLru = [];
    private long runtimeSizeBytes;

    public SvTextCacheStore(
        SvWorkflowFileSource fileSource,
        SvCacheManager? cacheManager)
    {
        this.fileSource = fileSource ?? throw new ArgumentNullException(nameof(fileSource));
        this.cacheManager = cacheManager;
    }

    public SvTextCategoryCacheData LoadBaseCategory(
        OpenedProject project,
        string language,
        string categoryId,
        IReadOnlyList<SvTextBaseSource> sources)
    {
        return LoadBaseCategoryCore(project, language, categoryId, sources);
    }

    public void ClearMemoryCache()
    {
        lock (syncRoot)
        {
            runtimeCategories.Clear();
            runtimeLru.Clear();
            runtimeSizeBytes = 0;
        }
    }

    private SvTextCategoryCacheData LoadBaseCategoryCore(
        OpenedProject project,
        string language,
        string categoryId,
        IReadOnlyList<SvTextBaseSource> sources)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            return new SvTextCategoryCacheData([], []);
        }

        var normalizedSources = sources.Select(NormalizeSource).ToArray();
        var groupKey = CreateRuntimeGroupKey(
            project,
            language,
            categoryId,
            normalizedSources);
        lock (syncRoot)
        {
            if (TryGetRuntimeCategory(groupKey, out var retained))
            {
                return retained;
            }
        }

        var canUsePersistentArtifacts = CanUsePersistentArtifacts();
        var cachedSources = new List<SvTextCachedSource>(normalizedSources.Length);
        var diagnostics = new List<ValidationDiagnostic>();
        var isCategoryCacheable = true;
        foreach (var source in normalizedSources)
        {
            var sourceResult = LoadBaseSource(
                project,
                source,
                canUsePersistentArtifacts);
            var sourceData = sourceResult.Data;
            if (sourceData.Source is not null)
            {
                cachedSources.Add(sourceData.Source);
            }

            diagnostics.AddRange(sourceData.Diagnostics);
            isCategoryCacheable &= sourceResult.IsCacheable;
        }

        var data = new SvTextCategoryCacheData(cachedSources, diagnostics);
        if (isCategoryCacheable)
        {
            lock (syncRoot)
            {
                AddRuntimeCategory(groupKey, data);
            }
        }

        return data;
    }

    private SourceLoadResult LoadBaseSource(
        OpenedProject project,
        SvTextBaseSource source,
        bool canUsePersistentArtifacts)
    {
        var tablePath = Path.ChangeExtension(source.VirtualPath, ".tbl")
            .Replace('\\', '/');
        var basePaths = new[] { source.VirtualPath, tablePath };
        var artifactKey = CreateArtifactKey(source);

        if (canUsePersistentArtifacts
            && cacheManager!.TryReadTextArtifact(
                project.Paths,
                artifactKey,
                ParserVersion,
                basePaths,
                out var payload))
        {
            if (TryDeserializeSource(payload, source, out var persisted))
            {
                return new SourceLoadResult(persisted, IsCacheable: true);
            }

            cacheManager.InvalidateTextArtifact(
                project.Paths,
                artifactKey,
                ParserVersion,
                basePaths);
        }

        var parsed = ParseSource(project, source, tablePath);
        if (canUsePersistentArtifacts && parsed.IsCacheable && parsed.Data.Source is not null)
        {
            try
            {
                var serializedPayload = JsonSerializer.SerializeToUtf8Bytes(
                    parsed.Data,
                    ArtifactJsonOptions);
                cacheManager!.WriteTextArtifact(
                    project.Paths,
                    artifactKey,
                    ParserVersion,
                    basePaths,
                    serializedPayload);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                // Parsed base data remains usable when optional cache serialization fails.
            }
        }

        return parsed;
    }

    private bool CanUsePersistentArtifacts()
    {
        if (cacheManager is null)
        {
            return false;
        }

        try
        {
            return cacheManager.GetSettings().Mode == SvCacheMode.Performance;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private SourceLoadResult ParseSource(
        OpenedProject project,
        SvTextBaseSource source,
        string tablePath)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        try
        {
            var dataFile = fileSource.ReadBase(project, source.VirtualPath);
            var textFile = SwShGameTextFile.Parse(dataFile.Bytes);
            var keyResult = LoadMessageKeys(
                project,
                tablePath,
                textFile.Lines.Count,
                diagnostics);
            var lines = new SvTextCachedLine[textFile.Lines.Count];
            for (var lineIndex = 0; lineIndex < textFile.Lines.Count; lineIndex++)
            {
                lines[lineIndex] = new SvTextCachedLine(
                    textFile.Lines[lineIndex].Text,
                    lineIndex < keyResult.Keys.Count
                        && !string.IsNullOrWhiteSpace(keyResult.Keys[lineIndex])
                        ? keyResult.Keys[lineIndex]
                        : null);
            }

            return new SourceLoadResult(
                new SvTextSourceCacheData(
                    new SvTextCachedSource(source.VirtualPath, source.Context, lines),
                    diagnostics),
                keyResult.IsCacheable);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Message table 'romfs/{source.VirtualPath}' could not be decoded: {exception.Message}",
                source.VirtualPath,
                "Scarlet/Violet encrypted text table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Message table 'romfs/{source.VirtualPath}' could not be read: {exception.Message}",
                source.VirtualPath,
                "Readable Scarlet/Violet message table"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Message table 'romfs/{source.VirtualPath}' could not be read: {exception.Message}",
                source.VirtualPath,
                "Readable Scarlet/Violet message table"));
        }

        return new SourceLoadResult(
            new SvTextSourceCacheData(Source: null, diagnostics),
            IsCacheable: false);
    }

    private MessageKeyLoadResult LoadMessageKeys(
        OpenedProject project,
        string tablePath,
        int lineCount,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var keys = SwShAhtbFile.Parse(fileSource.ReadBase(project, tablePath).Bytes)
                .Entries
                .Select(entry => entry.Name)
                .ToArray();
            var hasExpectedSentinel = keys.Length == lineCount + 1
                && keys[^1].EndsWith("_max", StringComparison.OrdinalIgnoreCase);
            if (keys.Length != lineCount && !hasExpectedSentinel)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Message key table 'romfs/{tablePath}' has {keys.Length} keys for {lineCount} editable lines. Available keys were used by line index.",
                    tablePath,
                    $"{lineCount} keys, optionally followed by one *_max sentinel"));
            }

            return new MessageKeyLoadResult(
                keys.Length <= lineCount ? keys : keys[..lineCount],
                IsCacheable: true);
        }
        catch (Exception exception) when (IsMissingBaseFile(exception))
        {
            return new MessageKeyLoadResult([], IsCacheable: true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Message key table 'romfs/{tablePath}' could not be decoded: {exception.Message}",
                tablePath,
                "Scarlet/Violet AHTB message-key table"));
            return new MessageKeyLoadResult([], IsCacheable: false);
        }
    }

    private static bool TryDeserializeSource(
        byte[] payload,
        SvTextBaseSource expectedSource,
        out SvTextSourceCacheData data)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<SvTextSourceCacheData>(payload, ArtifactJsonOptions);
            if (parsed?.Source is null
                || parsed.Diagnostics is null
                || parsed.Source.Lines is null
                || !string.Equals(
                    parsed.Source.VirtualPath,
                    expectedSource.VirtualPath,
                    StringComparison.Ordinal)
                || !string.Equals(parsed.Source.Context, expectedSource.Context, StringComparison.Ordinal)
                || parsed.Source.Lines.Any(line => line is null || line.Value is null)
                || parsed.Diagnostics.Any(diagnostic => diagnostic is null
                    || diagnostic.Message is null
                    || !Enum.IsDefined(diagnostic.Severity)))
            {
                data = default!;
                return false;
            }

            data = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            data = default!;
            return false;
        }
    }

    private static SvTextBaseSource NormalizeSource(SvTextBaseSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.VirtualPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Context);

        var virtualPath = source.VirtualPath.Trim().Replace('\\', '/').TrimStart('/');
        if (virtualPath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            virtualPath = virtualPath["romfs/".Length..];
        }

        var segments = virtualPath.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
            || !virtualPath.StartsWith(
                $"{SvMessagePathResolver.MessageRootPath}/",
                StringComparison.OrdinalIgnoreCase)
            || !virtualPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "S/V Text base sources must be safe message DAT virtual paths.",
                nameof(source));
        }

        return new SvTextBaseSource(virtualPath, source.Context.Trim());
    }

    private static bool IsMissingBaseFile(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }
        }

        return false;
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string virtualPath,
        string expected)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: $"romfs/{virtualPath}",
            Domain: TextDomain,
            Expected: expected);
    }

    private static string CreateArtifactKey(SvTextBaseSource source)
    {
        return $"{source.VirtualPath}/{source.Context}";
    }

    private static string CreateRuntimeGroupKey(
        OpenedProject project,
        string language,
        string categoryId,
        IReadOnlyList<SvTextBaseSource> sources)
    {
        var identity = new StringBuilder();
        identity.Append((int?)project.Paths.SelectedGame)
            .Append('\n')
            .Append(language.Trim())
            .Append('\n')
            .Append(categoryId.Trim())
            .Append('\n');

        AppendBaseSourceStamps(identity, project.Paths, sources);
        foreach (var source in sources)
        {
            identity.Append(source.VirtualPath)
                .Append('\n')
                .Append(source.Context)
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendBaseSourceStamps(
        StringBuilder identity,
        ProjectPaths paths,
        IReadOnlyList<SvTextBaseSource> sources)
    {
        var basePath = string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            ? string.Empty
            : Path.GetFullPath(paths.BaseRomFsPath);
        identity.Append(basePath).Append('\n');

        var archiveRoot = File.Exists(Path.Combine(basePath, "arc", "data.trpfd"))
            ? basePath
            : Path.Combine(basePath, "romfs");
        AppendFileStamp(identity, Path.Combine(archiveRoot, "arc", "data.trpfd"));
        AppendFileStamp(identity, Path.Combine(archiveRoot, "arc", "data.trpfs"));
        identity.Append(paths.ScarletVioletSupportFolderPath).Append('\n');
        if (SvCompressionRuntime.TryResolveRequiredFilePath(
            paths.ScarletVioletSupportFolderPath,
            out var runtimePath))
        {
            AppendFileStamp(identity, runtimePath);
        }

        foreach (var source in sources)
        {
            AppendFileStamp(identity, Path.Combine(
                basePath,
                source.VirtualPath.Replace('/', Path.DirectorySeparatorChar)));
            AppendFileStamp(identity, Path.Combine(
                basePath,
                Path.ChangeExtension(source.VirtualPath, ".tbl")
                    .Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    private static void AppendFileStamp(StringBuilder identity, string path)
    {
        var file = new FileInfo(path);
        identity.Append(file.FullName).Append('|');
        if (file.Exists)
        {
            identity.Append(file.Length)
                .Append('|')
                .Append(file.LastWriteTimeUtc.Ticks);
        }

        identity.Append('\n');
    }

    private bool TryGetRuntimeCategory(string key, out SvTextCategoryCacheData data)
    {
        if (!runtimeCategories.TryGetValue(key, out var runtime))
        {
            data = default!;
            return false;
        }

        runtimeLru.Remove(runtime.LruNode);
        runtimeLru.AddLast(runtime.LruNode);
        data = runtime.Data;
        return true;
    }

    private void AddRuntimeCategory(string key, SvTextCategoryCacheData data)
    {
        if (runtimeCategories.Remove(key, out var existing))
        {
            runtimeLru.Remove(existing.LruNode);
            runtimeSizeBytes = Math.Max(0, runtimeSizeBytes - existing.EstimatedSizeBytes);
        }

        var estimatedSize = EstimateSize(data);
        if (estimatedSize > RuntimeCacheCapacityBytes)
        {
            return;
        }

        while (runtimeSizeBytes + estimatedSize > RuntimeCacheCapacityBytes
            && runtimeLru.First is { } first)
        {
            if (runtimeCategories.Remove(first.Value, out var removed))
            {
                runtimeSizeBytes = Math.Max(0, runtimeSizeBytes - removed.EstimatedSizeBytes);
            }

            runtimeLru.RemoveFirst();
        }

        var node = runtimeLru.AddLast(key);
        runtimeCategories.Add(key, new RuntimeCategory(data, estimatedSize, node));
        runtimeSizeBytes += estimatedSize;
    }

    private static long EstimateSize(SvTextCategoryCacheData data)
    {
        const int recordOverhead = 64;
        long size = recordOverhead;
        foreach (var source in data.Sources)
        {
            size += recordOverhead + EstimateString(source.VirtualPath) + EstimateString(source.Context);
            foreach (var line in source.Lines)
            {
                size += recordOverhead + EstimateString(line.Value) + EstimateString(line.MessageKey);
            }
        }

        foreach (var diagnostic in data.Diagnostics)
        {
            size += recordOverhead
                + EstimateString(diagnostic.Message)
                + EstimateString(diagnostic.File)
                + EstimateString(diagnostic.Domain)
                + EstimateString(diagnostic.Field)
                + EstimateString(diagnostic.Expected)
                + EstimateString(diagnostic.Code);
        }

        return size;
    }

    private static long EstimateString(string? value)
    {
        return value is null ? 0 : 24L + (value.Length * sizeof(char));
    }

    private sealed record SvTextSourceCacheData(
        SvTextCachedSource? Source,
        IReadOnlyList<ValidationDiagnostic> Diagnostics);

    private sealed record MessageKeyLoadResult(
        IReadOnlyList<string> Keys,
        bool IsCacheable);

    private sealed record RuntimeCategory(
        SvTextCategoryCacheData Data,
        long EstimatedSizeBytes,
        LinkedListNode<string> LruNode);

    private sealed record SourceLoadResult(
        SvTextSourceCacheData Data,
        bool IsCacheable);

}
