// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Models;
using KM.SwSh.Restoration;
using KM.Core.Files;

namespace KM.SwSh.Pokemon;

public readonly record struct SwShPokemonIdentity(int Species, int Form);
public enum SwShPokemonOrigin { Native, ManagedRestoration, ExternalRestoration, Missing }
[Flags]
public enum SwShPokemonCapability { None = 0, Data = 1, Assignment = 2, Model = 4, Dynamax = 8 }
public sealed record SwShEffectivePokemonEntry(SwShPokemonIdentity Identity, int PersonalId,
    SwShPokemonOrigin Origin, SwShPokemonCapability Capabilities, SwShPersonalRecord Personal);

/// <summary>Resolves gameplay identities independently of physical personal row indices.</summary>
public sealed record SwShEffectivePokemonCatalog(string Revision, IReadOnlyList<SwShEffectivePokemonEntry> Entries, string? UnavailableReason)
{
    public const int MaximumSpecies = 898;
    public const string ModelCatalogPath = "romfs/bin/pokemon/table/poke_resource_table.gfbpmcatalog";
    internal IReadOnlyList<ProjectFileReference> Sources { get; init; } = [];
    public bool Available => UnavailableReason is null;
    public IReadOnlySet<int> SpeciesFor(SwShPokemonCapability capability) => Entries
        .Where(e => (e.Capabilities & capability) == capability).Select(e => e.Identity.Species).ToHashSet();

    public static SwShEffectivePokemonCatalog Load(ProjectPaths paths) => Load(paths, speciesIds: null);

    internal static SwShEffectivePokemonCatalog Load(ProjectPaths paths, IReadOnlySet<int>? speciesIds)
        => LoadCore(paths, speciesIds, validateModels: true);

    // Browsing lists candidates. Assignment writers still use Load, which
    // verifies the selected identities and binds their complete resource sources.
    internal static SwShEffectivePokemonCatalog LoadForBrowsing(ProjectPaths paths)
        => LoadCore(paths, null, validateModels: false);

    private static SwShEffectivePokemonCatalog LoadCore(ProjectPaths paths, IReadOnlySet<int>? speciesIds, bool validateModels)
    {
        try
        {
            var sources = new HashSet<ProjectFileReference>();
            var nativeBytes = Read(paths, SwShPersonalTable.PersonalDataRelativePath, native: true, sources: sources);
            var currentBytes = Read(paths, SwShPersonalTable.PersonalDataRelativePath, sources: sources);
            var catalogBytes = Read(paths, ModelCatalogPath, sources: sources);
            var native = SwShPersonalTable.Parse(nativeBytes, 4096).Records;
            var current = SwShPersonalTable.Parse(currentBytes, 4096).Records;
            // Restored form blocks can extend the effective table. Identities validates
            // the ownership and bounds of the rows referenced by either table.
            if (native.Count <= MaximumSpecies || current.Count < native.Count)
                throw new InvalidDataException("Native and effective personal tables have incompatible identities.");
            var modelCatalog = new SwShPokemonResourceCatalog(catalogBytes);
            var models = modelCatalog.Identities.Select(i => new SwShPokemonIdentity(i.Species, i.Form)).ToHashSet();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            const long maximumCachedBytes = 384L * 1024 * 1024;
            var observed = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var fingerprints = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var cacheOrder = new Queue<string>();
            long observedBytes = 0;
            byte[] ReadResource(string path)
            {
                if (observed.TryGetValue(path, out var cached)) return cached;
                var bytes = Read(paths, path, sources: sources);
                var fingerprint = SHA256.HashData(bytes);
                if (fingerprints.TryGetValue(path, out var previous))
                {
                    if (!fingerprint.AsSpan().SequenceEqual(previous))
                        throw new InvalidDataException("Restored Pokemon resources changed while reading the catalog.");
                }
                else
                {
                    fingerprints.Add(path, fingerprint);
                    hash.AppendData(System.Text.Encoding.UTF8.GetBytes(path));
                    hash.AppendData(bytes);
                }
                // Bound retained bytes without making the total installed asset size
                // an eligibility limit. Revisited sources must retain their identity.
                while (observedBytes + bytes.LongLength > maximumCachedBytes && cacheOrder.TryDequeue(out var oldest))
                {
                    observedBytes -= observed[oldest].LongLength;
                    observed.Remove(oldest);
                }
                observed.Add(path, bytes);
                cacheOrder.Enqueue(path);
                observedBytes += bytes.LongLength;
                return bytes;
            }
            var state = SwShRestorationStateStore.Read(paths);
            var selections = state.State.Selections.Where(s => s.Enabled
                && (speciesIds is null || SwShRestorationDefinitions.Get(s.DefinitionId).Identities.Any(i => speciesIds.Contains(i.Species)))).ToArray();
            if (validateModels) SwShRestorationReadScope.Find(paths)?.Prefetch(selections.Select(s => s.PreparedHash));
            var managed = new Dictionary<SwShPokemonIdentity, bool>();
            var presence = new SwShRestorationComposer.PresenceValidator();
            foreach (var selection in selections)
            {
                if (!validateModels)
                {
                    foreach (var identity in SwShRestorationDefinitions.Get(selection.DefinitionId).Identities) managed[identity] = true;
                    continue;
                }
                var prepared = SwShRestorationStateStore.ReadPrepared(paths, selection.PreparedHash);
                var complete = true;
                try
                {
                    complete = presence.HasAllContributions(prepared.Contributions, path =>
                    {
                        try { return ReadResource(path); } catch (InvalidDataException) { return null; }
                    });
                    foreach (var file in prepared.Contributions.Where(c => c.Writer == SwShRestorationWriter.File && c.Path.EndsWith(".gfpak", StringComparison.Ordinal)))
                        complete &= SwShGfPackFile.ParseBoundedReadOnly(ReadResource(file.Path), 4096, 64L * 1024 * 1024).FileCount > 0;
                }
                catch (InvalidDataException) { complete = false; }
                foreach (var identity in SwShRestorationDefinitions.Get(selection.DefinitionId).Identities) managed[identity] = complete;
            }
            var rows = Identities(current); var nativeRows = Identities(native).ToDictionary(e => e.Identity);
            var extendedSupport = speciesIds is not null && !speciesIds.Contains(SwShRestorationSpecies.Annihilape)
                || current.Count <= SwShRestorationSpecies.AnnihilapeRow
                || !current[SwShRestorationSpecies.AnnihilapeRow].IsPresentInGame || HasSpeciesSupport();
            var entries = rows.Where(row => speciesIds is null || speciesIds.Contains(row.Identity.Species)).Select(row =>
            {
                var present = row.Personal.IsPresentInGame;
                var origin = nativeRows.TryGetValue(row.Identity, out var baseline) && baseline.Personal.IsPresentInGame
                    ? SwShPokemonOrigin.Native : managed.ContainsKey(row.Identity) ? SwShPokemonOrigin.ManagedRestoration
                        : present ? SwShPokemonOrigin.ExternalRestoration : SwShPokemonOrigin.Missing;
                var capabilities = present ? SwShPokemonCapability.Data : SwShPokemonCapability.None;
                var complete = origin == SwShPokemonOrigin.Native || present
                    && (!managed.TryGetValue(row.Identity, out var ready) || ready)
                    && (!validateModels || ModelsComplete(paths, modelCatalog, row.Identity, ReadResource));
                if (present && complete && models.Contains(row.Identity)
                    && (row.Identity.Species != SwShRestorationSpecies.Annihilape || extendedSupport))
                    capabilities |= SwShPokemonCapability.Model | SwShPokemonCapability.Assignment;
                if ((capabilities & SwShPokemonCapability.Assignment) != 0 && !row.Personal.CanNotDynamax) capabilities |= SwShPokemonCapability.Dynamax;
                return row with { Origin = origin, Capabilities = capabilities };
            }).ToArray();
            hash.AppendData(nativeBytes); hash.AppendData(currentBytes); hash.AppendData(catalogBytes);
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(state.Hash ?? ""));
            return new(Convert.ToHexStringLower(hash.GetHashAndReset()), entries, null) { Sources = sources.ToArray() };

            bool HasSpeciesSupport()
            {
                const string relative = "exefs/main";
                var file = Path.Combine(paths.OutputRootPath ?? "", "exefs", "main"); var layer = ProjectFileLayer.Layered;
                if (!File.Exists(file))
                {
                    if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath)) return false;
                    file = Path.Combine(paths.BaseExeFsPath, "main"); layer = ProjectFileLayer.Base;
                }
                if (!File.Exists(file)) return false;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length is < 1 or > 64L * 1024 * 1024) return false;
                var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
                sources.Add(new(layer, relative)); hash.AppendData(SHA256.HashData(bytes));
                return SwShRestorationSpeciesPatcher.HasInstalledPatch(bytes);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException
            or System.Text.Json.JsonException or KM.Core.Output.OutputCoordinatorException)
        {
            return new("unavailable", [], "Required Pokemon identity or model sources could not be read: " + exception.Message);
        }
    }

    private static bool ModelsComplete(ProjectPaths paths, SwShPokemonResourceCatalog catalog, SwShPokemonIdentity identity, Func<string, byte[]> read)
    {
        try
        {
            var rows = catalog.Identities.Where(i => i.Species == identity.Species && i.Form == identity.Form).Select(catalog.Read).ToArray();
            if (!rows.Any(r => !r.Identity.Shiny) || !rows.Any(r => r.Identity.Shiny)) return false;
            var packs = new Dictionary<string, SwShGfPackFile>(StringComparer.Ordinal);
            ReadOnlyMemory<byte> Resource(string relative, string? archive)
            {
                _ = new KM.Core.Semantics.RelativeOutputPath(relative);
                if (File.Exists(Path.Combine(paths.OutputRootPath ?? "", "romfs", relative))
                    || File.Exists(Path.Combine(paths.BaseRomFsPath!, relative))) return read("romfs/" + relative);
                if (archive is null) throw new InvalidDataException("Restored model archive is missing.");
                if (!packs.TryGetValue(archive, out var pack)) packs.Add(archive, pack = SwShGfPackFile.ParseBoundedReadOnly(read("romfs/" + archive), 4096, 64L * 1024 * 1024));
                return pack.TryGetFileMemoryByName(Path.GetFileName(relative), 32 * 1024 * 1024, out var bytes)
                    ? bytes : throw new InvalidDataException("Restored model dependency is missing.");
            }
            foreach (var row in rows)
            {
                if (row.Controllers.Count == 0) return false;
                var roots = row.Controllers.Values.Append(row.Model).Append(row.Configuration).Select(p => (p, (string?)row.Archive));
                ModelAssetGraph.Validate(roots, Resource);
            }
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException or OverflowException) { return false; }
    }

    public static IReadOnlyList<SwShEffectivePokemonEntry> Identities(IReadOnlyList<SwShPersonalRecord> rows)
    {
        var entries = new List<SwShEffectivePokemonEntry>(); var used = new HashSet<int>();
        foreach (var species in SwShRestorationSpecies.BaseSpecies)
        {
            var baseRow = SwShRestorationSpecies.BaseRow(species);
            if (baseRow >= rows.Count) continue;
            var main = rows[baseRow];
            Add(species, 0, baseRow);
            if (main.FormStatsIndex == 0) continue;
            if (main.FormCount < 1 || main.FormStatsIndex <= MaximumSpecies || main.FormStatsIndex > rows.Count - (main.FormCount - 1))
                throw new InvalidDataException("Pokemon form records point outside the personal table.");
            for (var form = 1; form < main.FormCount; form++) Add(species, form, main.FormStatsIndex + form - 1);
        }
        return entries;
        void Add(int species, int form, int index)
        {
            if (!used.Add(index) || rows[index].PersonalId != index) throw new InvalidDataException("Pokemon form ownership is ambiguous.");
            entries.Add(new(new(species, form), index, SwShPokemonOrigin.Native,
                rows[index].IsPresentInGame ? SwShPokemonCapability.Data : SwShPokemonCapability.None, rows[index]));
        }
    }

    internal static byte[] Read(ProjectPaths paths, string relativePath, bool native = false,
        ISet<ProjectFileReference>? sources = null)
    {
        if (!relativePath.StartsWith("romfs/", StringComparison.Ordinal) || relativePath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Pokemon source path is invalid.");
        var path = !native && !string.IsNullOrWhiteSpace(paths.OutputRootPath)
            ? Path.Combine(paths.OutputRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)) : null;
        var layer = ProjectFileLayer.Layered;
        if (path is null || !File.Exists(path))
        {
            path = Path.Combine(paths.BaseRomFsPath ?? throw new InvalidDataException("Target RomFS is not configured."), relativePath[6..].Replace('/', Path.DirectorySeparatorChar));
            layer = ProjectFileLayer.Base;
        }
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1 || info.Length > SwShRestorationSourceBudget.MaximumBytes(relativePath))
            throw new InvalidDataException("Pokemon source is missing or exceeds its read budget.");
        var bytes = File.ReadAllBytes(path);
        sources?.Add(new(layer, relativePath));
        return bytes;
    }
}
