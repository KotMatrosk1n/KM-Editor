// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Api.ModMerger;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace KM.Tools.ModMerging;

/// <summary>Discovers package boundaries before installation paths are flattened.</summary>
public sealed class MergePackageService
{
    public MergePackageCatalogDto Scan(MergePackageScanRequest request)
    {
        if (request?.Source is not { } source || string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.Path))
            throw new MergeInputException(MergeWorkspaceErrorCodes.RequestInvalid, "Choose a mod archive or folder.");
        var remaining = MergeInputs.MaximumBytes;
        return MergePackages.Discover(source, ref remaining).Catalog;
    }
}

internal static class MergePackages
{
    private const int MaximumPackages = 64;
    private const string ManifestName = "km-mod.json";
    private sealed record Manifest(int Version, PackageDeclaration[] Packages, GroupDeclaration[]? Groups = null);
    private sealed record PackageDeclaration(string Id, string Path, string? Name = null, string? Description = null,
        string? Game = null, string? Layout = null, bool Required = false, bool Recommended = false,
        string[]? Requires = null, string? Group = null);
    private sealed record GroupDeclaration(string Id, string? Name = null, bool Required = false);
    private sealed record Declaration(string Root, string Scope, PackageDeclaration Value);
    internal sealed record Discovery(MergePackageCatalogDto Catalog, IReadOnlyDictionary<string, MergeInput> Inputs, int FileCount);

    internal static IReadOnlyList<MergeInput> ReadSelected(MergeSourceDto source, ref long remaining, out int fileCount)
    {
        var discovery = Discover(source, ref remaining);
        var catalog = discovery.Catalog;
        fileCount = discovery.FileCount;
        if (source.PackageIds is null)
        {
            if (catalog.RequiresSelection)
                throw new MergeInputException(MergeWorkspaceErrorCodes.PackagesRequired, "Choose the packages to include from this source.");
            return [discovery.Inputs[catalog.Packages[0].Id]];
        }
        if (source.PackageScanToken != catalog.ScanToken)
            throw new MergeInputException(MergeWorkspaceErrorCodes.PackagesStale, "The source changed. Scan it again and confirm its packages.");
        ValidateSelection(catalog, source.PackageIds);
        return source.PackageIds.Select(id => discovery.Inputs[id]).ToArray();
    }

    private static void ValidateSelection(MergePackageCatalogDto catalog, IReadOnlyList<string> selected)
    {
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0 || ids.Count > MaximumPackages || ids.Count != selected.Count
            || ids.Any(id => !catalog.Packages.Any(package => package.Id == id))
            || catalog.Packages.Any(package => package.Required && !ids.Contains(package.Id))
            || catalog.Packages.Where(package => ids.Contains(package.Id)).Any(package => package.Requires.Any(id => !ids.Contains(id)))
            || catalog.Groups.Any(group => catalog.Packages.Count(package => package.Group == group.Id && ids.Contains(package.Id)) is var count
                && (count > 1 || group.Required && count != 1)))
            throw new MergeInputException(MergeWorkspaceErrorCodes.PackageSelectionInvalid, "Select the required packages, dependencies and one option per alternative group.");
    }

    internal static Discovery Discover(MergeSourceDto source, ref long remaining)
    {
        var raw = MergeInputs.ReadRaw(source.Path, ref remaining);
        var selectedFolder = Directory.Exists(source.Path) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(source.Path)).ToLowerInvariant() : null;
        var installationFolder = selectedFolder is "romfs" or "exefs" or "trinity-mod-manager-romfs";
        if (!installationFolder) ExpandArchives(raw, ref remaining);
        if (raw.Count == 0) throw new MergeInputException(MergeWorkspaceErrorCodes.ArchiveUnreadable, "The source has no files.");
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source.Path));
        var declarations = new List<Declaration>();
        var groups = new List<MergePackageGroupDto>();
        var manifests = raw.Values.Where(file => !installationFolder && FindBoundary(file.Path) is null && Path.GetFileName(file.Path).Equals(ManifestName, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var file in manifests)
        {
            try
            {
                if (file.Bytes.Length > 256 * 1024) throw new JsonException();
                using (var document = JsonDocument.Parse(file.Bytes, new JsonDocumentOptions { MaxDepth = 16 }))
                    RejectDuplicateProperties(document.RootElement);
                var manifest = JsonSerializer.Deserialize<Manifest>(file.Bytes, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    MaxDepth = 16,
                });
                if (manifest is null || manifest.Version != 1 || manifest.Packages is not { Length: > 0 and <= MaximumPackages }
                    || manifest.Packages.Any(package => package is null) || manifest.Groups?.Any(group => group is null) == true)
                    throw new JsonException();
                var scope = Parent(file.Path);
                var declaredGroups = manifest.Groups ?? [];
                if (declaredGroups.Length > MaximumPackages || declaredGroups.Select(group => group.Id).Distinct().Count() != declaredGroups.Length)
                    throw new JsonException();
                foreach (var group in declaredGroups)
                {
                    ValidateId(group.Id); ValidateText(group.Name, 160);
                    groups.Add(new(Identity(scope + "/group/" + group.Id), group.Name ?? group.Id, group.Required));
                }
                if (manifest.Packages.Select(package => package.Id).Distinct().Count() != manifest.Packages.Length) throw new JsonException();
                foreach (var package in manifest.Packages)
                {
                    ValidateId(package.Id); ValidateText(package.Name, 160); ValidateText(package.Description, 4000);
                    if (package.Path is null || package.Game is not (null or "sword" or "shield" or "scarlet" or "violet" or "za")
                        || package.Layout is not (null or "standalone" or "trinity" or "bypass" or "independent")
                        || package.Requires is { Length: > MaximumPackages }
                        || package.Requires is { } requires && requires.Distinct(StringComparer.Ordinal).Count() != requires.Length
                        || package.Requires?.Any(id => id == package.Id || !manifest.Packages.Any(other => other.Id == id)) == true
                        || package.Group is not null && !declaredGroups.Any(group => group.Id == package.Group)) throw new JsonException();
                    var relative = package.Path == "." ? "" : MergeInputs.SafePath(package.Path.TrimEnd('/'));
                    declarations.Add(new(Join(scope, relative), scope, package));
                }
            }
            catch (Exception exception) when (exception is JsonException or MergeInputException or ArgumentException)
            { throw new MergeInputException(MergeWorkspaceErrorCodes.PackageManifestInvalid, "The package manifest is invalid or uses unsupported declarations."); }
        }
        if (declarations.Count > MaximumPackages || groups.Count > MaximumPackages
            || declarations.Select(item => item.Root).Distinct(StringComparer.OrdinalIgnoreCase).Count() != declarations.Count)
            throw new MergeInputException(MergeWorkspaceErrorCodes.PackageManifestInvalid, "Package declarations must have distinct paths within the supported size.");

        var buckets = new Dictionary<string, Dictionary<string, MergeInputFile>>(StringComparer.OrdinalIgnoreCase);
        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var documents = new List<MergePackageDocumentDto>();
        var documentCharacters = 0;
        foreach (var file in raw.Values.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            if (manifests.Contains(file)) continue;
            var boundary = installationFolder ? (Root: "", Evidence: "installationRoot") : FindBoundary(file.Path);
            var declared = declarations.Where(item => IsWithin(file.Path, item.Root)).OrderByDescending(item => item.Root.Length).FirstOrDefault();
            var explicitLoosePayload = (source.Layout ?? declared?.Value.Layout) is "trinity" or "independent";
            if (boundary is null && !explicitLoosePayload && IsDocumentation(file.Path))
            {
                if (documents.Count < 32 && documentCharacters < 64 * 1024)
                {
                    var readable = Path.GetExtension(file.Path).ToLowerInvariant() is ".txt" or ".md" or ".rst";
                    var length = readable ? Math.Min(file.Bytes.Length, Math.Min(8192, 64 * 1024 - documentCharacters)) : 0;
                    var text = Encoding.UTF8.GetString(file.Bytes.AsSpan(0, length));
                    documents.Add(new(file.Path, text, length < file.Bytes.Length)); documentCharacters += text.Length;
                }
                continue;
            }
            var root = declared?.Root ?? boundary?.Root ?? (explicitLoosePayload ? "" : Parent(file.Path));
            if (!buckets.TryGetValue(root, out var bucket))
            {
                if (buckets.Count == MaximumPackages) throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "The source contains too many packages.");
                buckets[root] = bucket = new(StringComparer.OrdinalIgnoreCase);
                evidence[root] = declared is not null ? "manifest" : boundary?.Evidence ?? (explicitLoosePayload ? "directLayout" : "unclassified");
            }
            bucket.Add(file.Path, file);
        }
        if (buckets.Count == 0) throw new MergeInputException(MergeWorkspaceErrorCodes.ArchiveUnreadable, "No mod payload was found in this source.");
        if (declarations.Any(item => !buckets.ContainsKey(item.Root)))
            throw new MergeInputException(MergeWorkspaceErrorCodes.PackageManifestInvalid, "A declared package has no files.");

        var inputs = new Dictionary<string, MergeInput>(StringComparer.Ordinal);
        var packages = new List<MergePackageDto>();
        var previewRemaining = 1024;
        foreach (var (root, bucket) in buckets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var declared = declarations.SingleOrDefault(item => item.Root.Equals(root, StringComparison.OrdinalIgnoreCase));
            var id = Identity(root.ToLowerInvariant());
            var titles = root.Split('/').Select(MergeInputs.DetectTitle).OfType<string>().Distinct().ToArray();
            if (titles.Length > 1) throw new MergeInputException(MergeWorkspaceErrorCodes.MixedGames, "A package contains conflicting game title folders.");
            var detectedGame = titles.FirstOrDefault();
            var game = declared?.Value.Game ?? source.Game ?? detectedGame;
            var layout = source.Layout ?? declared?.Value.Layout ?? (selectedFolder == "trinity-mod-manager-romfs" ? "trinity" : null);
            if (source.Game is not null && declared?.Value.Game is { } declaredGame && !MergeInputs.Compatible(source.Game, declaredGame))
                throw new MergeInputException(MergeWorkspaceErrorCodes.GameMismatch, "The selected game disagrees with the package manifest.");
            if (detectedGame is not null && game is not null && !MergeInputs.Compatible(game, detectedGame))
                throw new MergeInputException(MergeWorkspaceErrorCodes.GameMismatch, "The selected game disagrees with the package title folder.");
            var normalizedSource = source with { Id = buckets.Count == 1 ? source.Id : Identity(source.Id + "/" + id), Game = game, Layout = layout };
            var normalizedFiles = bucket.Values.ToDictionary(file => Relative(file.Path, root),
                file => file with { Path = Relative(file.Path, root) }, StringComparer.OrdinalIgnoreCase);
            var input = MergeInputs.Normalize(normalizedSource, normalizedFiles);
            var displayName = declared?.Value.Name ?? (root.Length == 0 ? name : root);
            var details = new List<string> { evidence[root] };
            if (root.Contains("!/", StringComparison.Ordinal) || root.EndsWith('!')) details.Add("nestedArchive");
            details.AddRange(input.Info.Evidence);
            var requires = declared?.Value.Requires?.Select(required => Identity(declarations.Single(item => item.Scope == declared.Scope && item.Value.Id == required).Root.ToLowerInvariant())).ToArray() ?? [];
            var group = declared?.Value.Group is { } groupId ? Identity(declared.Scope + "/group/" + groupId) : null;
            var files = input.Files.Keys.Order(StringComparer.Ordinal).Take(Math.Min(100, previewRemaining)).ToArray(); previewRemaining -= files.Length;
            packages.Add(new(id, displayName, root, input.Info.Game, input.Info.Layout, input.Files.Count,
                input.Files.Values.Sum(file => (long)file.Bytes.Length), declared?.Value.Required ?? false, declared?.Value.Recommended ?? false,
                group, requires, declared?.Value.Description, details, files, 0));
            inputs[id] = input with { Info = input.Info with { Name = buckets.Count == 1 ? name : name + " / " + displayName, ParentId = source.Id } };
        }
        foreach (var group in groups)
            if (!packages.Any(package => package.Group == group.Id))
                throw new MergeInputException(MergeWorkspaceErrorCodes.PackageManifestInvalid, "An alternative group has no packages.");
        ValidateDependencies(packages, groups);
        var sharedPaths = inputs.Values.SelectMany(input => input.Files.Keys).GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        packages = packages.Select(package => package with { OverlapCount = inputs[package.Id].Files.Keys.Count(sharedPaths.Contains) }).ToList();
        var token = Identity(string.Join('\n', raw.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + pair.Value.Hash))
            + "\n" + JsonSerializer.Serialize(packages) + "\n" + JsonSerializer.Serialize(groups));
        foreach (var id in inputs.Keys.ToArray()) inputs[id] = inputs[id] with { SourceFingerprint = token + ":" + inputs[id].SourceFingerprint };
        return new(new(source.Id, name, token, packages.Count != 1 || groups.Count > 0 || packages.Any(package => package.Evidence.Contains("unclassified")), packages, groups, documents), inputs, raw.Count);
    }

    private static void ValidateDependencies(IReadOnlyList<MergePackageDto> packages, IReadOnlyList<MergePackageGroupDto> groups)
    {
        var closures = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> Closure(string id, HashSet<string> visiting)
        {
            if (closures.TryGetValue(id, out var cached)) return cached;
            if (!visiting.Add(id)) throw new MergeInputException(MergeWorkspaceErrorCodes.PackageManifestInvalid, "Package dependencies contain a cycle.");
            var result = new HashSet<string>(StringComparer.Ordinal) { id };
            foreach (var dependency in packages.Single(package => package.Id == id).Requires) result.UnionWith(Closure(dependency, visiting));
            visiting.Remove(id); closures[id] = result; return result;
        }
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            var closure = Closure(package.Id, []);
            if (groups.Any(group => packages.Count(item => item.Group == group.Id && closure.Contains(item.Id)) > 1))
                throw new MergeInputException(MergeWorkspaceErrorCodes.PackageManifestInvalid, "A package requires mutually exclusive alternatives.");
            if (package.Required) required.UnionWith(closure);
        }
        if (groups.Any(group => packages.Count(package => package.Group == group.Id && required.Contains(package.Id)) > 1))
            throw new MergeInputException(MergeWorkspaceErrorCodes.PackageManifestInvalid, "Required packages select conflicting alternatives.");
    }

    private static (string Root, string Evidence)? FindBoundary(string path)
    {
        var parts = path.Split('/');
        var index = Array.FindIndex(parts, part => part.ToLowerInvariant() is "romfs" or "exefs" or "trinity-mod-manager-romfs");
        if (index >= 0) return (string.Join('/', parts.Take(index)), "installationRoot");
        index = Array.FindIndex(parts, MergeInputs.IsRoot);
        if (index >= 0) return (string.Join('/', parts.Take(index)), "virtualRoot");
        if (Path.GetExtension(path).ToLowerInvariant() is ".ips" or ".ips32" or ".pchtxt") return (Parent(path), "patchFiles");
        return null;
    }

    private static void ExpandArchives(Dictionary<string, MergeInputFile> files, ref long remaining)
    {
        for (var depth = 0; ; depth++)
        {
            var archives = files.Values.Where(file => Path.GetExtension(file.Path).ToLowerInvariant() is ".zip" or ".rar" or ".7z")
                .Where(file => FindBoundary(file.Path) is null).ToArray();
            if (archives.Length == 0) return;
            if (depth == 3) throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "Nested archives exceed the supported depth.");
            foreach (var archiveFile in archives)
            {
                files.Remove(archiveFile.Path);
                using var stream = new MemoryStream(archiveFile.Bytes, writable: false);
                using var archive = archiveFile.Bytes.AsSpan().StartsWith(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c })
                    ? ArchiveFactory.OpenArchive(stream, new ReaderOptions()) : null;
                using var reader = archive is not null ? archive.ExtractAllEntries() : ReaderFactory.OpenReader(stream, new ReaderOptions());
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory) continue;
                    if (reader.Entry.IsEncrypted || !string.IsNullOrEmpty(reader.Entry.LinkTarget))
                        throw new MergeInputException(MergeWorkspaceErrorCodes.ArchiveUnreadable, "Encrypted entries and archive links cannot be merged.");
                    var entryPath = MergeInputs.SafePath(reader.Entry.Key ?? "");
                    using var entry = reader.OpenEntryStream();
                    MergeInputs.Add(archiveFile.Path + "!/" + entryPath, entry, reader.Entry.Size, files, ref remaining);
                }
            }
        }
    }

    private static bool IsDocumentation(string path) => Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md" or ".rst" or ".pdf" or ".html"
        || Path.GetFileName(path).ToLowerInvariant() is "readme" or "license" or "changelog";
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
    private static bool IsWithin(string path, string root) => root.Length == 0 || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    private static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
    private static string Join(string first, string second) => first.Length == 0 ? second : second.Length == 0 ? first : first + "/" + second;
    private static string Relative(string path, string root) => root.Length == 0 ? path : path[(root.Length + 1)..];
    private static string Identity(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) throw new JsonException();
    }
    private static void ValidateText(string? text, int maximum)
    { if (text?.Length > maximum || text?.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t') == true) throw new JsonException(); }
}
