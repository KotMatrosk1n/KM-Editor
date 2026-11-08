// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.SV;
using KM.Formats.SwSh;
using KM.SwSh.Restoration;
using KM.SwSh.Pokemon;

namespace KM.Tools.Application;

public sealed record SwShRestorationPreparationProgress(string Phase, int CompletedSpecies, int TotalSpecies)
{
    public string Activity { get; init; } = "";
    public int? Species { get; init; }
    public int CompletedUnits { get; init; }
    public int? TotalUnits { get; init; }
    public string? CurrentResource { get; init; }
}

/// <summary>Converts registered Pokemon using the user's donor and target dumps.</summary>
public sealed partial class SwShRestorationPreparationService
{
    public SwShPreparedRestoration Prepare(ProjectPaths target, string donorRomFs, string? compressionSupport,
        string definitionId, IProgress<SwShRestorationPreparationProgress>? progress = null, CancellationToken cancellationToken = default, SwShRestorationDonorSession? donorSession = null)
    {
        var definition = SwShRestorationDefinitions.Get(definitionId);
        if (definition.Donor == SwShRestorationDonor.LegendsArceus)
            return new PlaSwShRestorationPreparationService().Prepare(target, donorRomFs, compressionSupport, definitionId, progress, cancellationToken, donorSession);
        if (definition.Donor == SwShRestorationDonor.LegendsZA)
            return new ZaSwShRestorationPreparationService().Prepare(target, donorRomFs, compressionSupport, definitionId, progress, cancellationToken, donorSession);
        if (target.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield) || string.IsNullOrWhiteSpace(target.BaseRomFsPath)
            || definition.Identities.Any(i => !TryProfile(i, out _))) throw new InvalidDataException("This restoration is not supported for the selected target.");
        var context = new Preparation(target, donorRomFs, compressionSupport, definition, progress, cancellationToken, donorSession);
        using (context)
        {
            var contributions = new List<SwShRestorationContribution>(); var unavailableMoves = new Dictionary<int, int[]>();
            foreach (var identity in definition.Identities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.ReportPhase("models", unavailableMoves.Count, identity.Species);
                contributions.AddRange(context.Assets(identity));
                context.ReportPhase("gameplay", unavailableMoves.Count, identity.Species);
                var gameplay = context.Gameplay(identity); contributions.AddRange(gameplay.Contributions); unavailableMoves.Add(identity.Species, gameplay.ExcludedMoves);
                contributions.AddRange(context.Names(identity));
            }
            context.ReportPhase("voices", definition.Identities.Count);
            contributions.AddRange(context.Voices(definition.Identities));
            context.ReportPhase("validation", definition.Identities.Count);
            var prepared = new SwShPreparedRestoration(definition.Id, definition.Version, definition.ConverterVersion, target.SelectedGame.Value,
                context.Fingerprints.Values.ToArray(), contributions, unavailableMoves) { AssetRevision = definition.AssetRevision };
            // Validate the complete resource closure and every shared writer before
            // preparation can be offered as a stageable artifact.
            context.Verify(prepared);
            return prepared with { Sources = context.Fingerprints.Values.ToArray() };
        }
    }

    private sealed partial class Preparation : IDisposable
    {
        private readonly ProjectPaths target;
        private readonly string donorRoot;
        private readonly string definitionId;
        private readonly SwShRestorationDonorSession archive;
        private readonly bool ownsArchive;
        private readonly IProgress<SwShRestorationPreparationProgress>? progress;
        private readonly int totalSpecies;
        private int currentSpecies;
        private SwShPokemonIdentity currentIdentity;
        private SwShRestorationPreparationProgress? currentProgress;
        internal void ReportPhase(string phase, int completedSpecies, int? species = null) =>
            Publish(new(phase, completedSpecies, totalSpecies) { Species = species, Activity = phase == "models" ? "reading" : phase });

        private void Report(string activity, int completed = 0, int? total = null, string? resource = null) =>
            Publish(new("models", currentProgress?.CompletedSpecies ?? 0, totalSpecies)
            {
                Species = currentSpecies, Activity = activity, CompletedUnits = completed, TotalUnits = total,
                CurrentResource = resource ?? (activity == currentProgress?.Activity ? currentProgress?.CurrentResource : null),
            });

        private void Publish(SwShRestorationPreparationProgress value)
        {
            currentProgress = value;
            progress?.Report(value);
        }

        private void ReportResource(string path)
        {
            if (currentProgress is { } value && value.CurrentResource != path)
                Publish(value with { CurrentResource = path });
        }
        private readonly CancellationToken token;
        private readonly Dictionary<string, byte[]> native = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> donor = new(StringComparer.Ordinal);
        private readonly HashSet<string> nativeParentEvolutionPaths = new(StringComparer.Ordinal);
        private readonly ModelTextureMipCache textureEncoding = new();
        internal readonly Dictionary<string, SwShRestorationSourceFingerprint> Fingerprints = new(StringComparer.Ordinal);
        private long retainedBytes;

        internal Preparation(ProjectPaths target, string donorRoot, string? support, SwShRestorationDefinition definition,
            IProgress<SwShRestorationPreparationProgress>? progress, CancellationToken token, SwShRestorationDonorSession? donorSession)
        {
            this.target = target; this.token = token; this.progress = progress; totalSpecies = definition.Identities.Count;
            definitionId = definition.Id;
            progress?.Report(new("sources", 0, definition.Identities.Count));
            token.ThrowIfCancellationRequested();
            ownsArchive = donorSession is null;
            archive = donorSession ?? new SwShRestorationDonorSession(definition.Donor, donorRoot, support);
            this.donorRoot = archive.Root;
        }
        internal byte[] Native(string path)
        {
            path = Canonical(path);
            if (currentProgress?.Phase is "gameplay" or "voices") ReportResource(path);
            if (native.TryGetValue(path, out var bytes)) return bytes;
            bytes = ReadPhysical(Path.Combine(target.BaseRomFsPath!, path.Replace('/', Path.DirectorySeparatorChar)), 64 * 1024 * 1024);
            Retain(bytes); native.Add(path, bytes); Fingerprint("target", path, bytes); return bytes;
        }
        private byte[] Donor(string path)
        {
            path = Canonical(path);
            if (currentProgress?.Phase == "gameplay" || currentProgress?.Activity == "textures" && path.EndsWith(".bntx", StringComparison.OrdinalIgnoreCase))
                ReportResource(path);
            if (donor.TryGetValue(path, out var bytes)) return bytes;
            token.ThrowIfCancellationRequested(); bytes = archive.Read(path);
            Retain(bytes); donor.Add(path, bytes); Fingerprint("donor", path, bytes); return bytes;
        }
        private void Retain(byte[] bytes)
        {
            retainedBytes += bytes.Length;
            if (retainedBytes > 384L * 1024 * 1024) throw new InvalidDataException("Restoration inputs exceed the preparation memory budget.");
        }
        private void Fingerprint(string role, string path, byte[] bytes) => Fingerprints[role + ":" + path] = new(role, path, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength);
        private byte[] ReadPhysical(string path, int maximum)
        {
            token.ThrowIfCancellationRequested();
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.Length is < 1 || source.Length > maximum) throw new InvalidDataException("Restoration input exceeds its file budget.");
            var bytes = new byte[checked((int)source.Length)]; source.ReadExactly(bytes);
            if (source.ReadByte() != -1) throw new InvalidDataException("Restoration input changed while reading.");
            return bytes;
        }
        private static string Canonical(string path)
        {
            if (TrinityPreviewReader.Resolve("catalog", path) != path) throw new InvalidDataException("Restoration input path is invalid.");
            return path;
        }
        private static byte[] Member(SwShGfPackFile pack, string name) => pack.TryGetFileByName(name, 32 * 1024 * 1024, out var bytes)
            ? bytes : throw new InvalidDataException($"A native donor resource is missing: {name}");
        private SwShRestorationContribution File(string path, byte[] bytes)
        {
            var physical = Path.Combine(target.BaseRomFsPath!, Canonical(path).Replace('/', Path.DirectorySeparatorChar));
            return new("romfs/" + path, SwShRestorationWriter.File, "", System.IO.File.Exists(physical) ? Native(path) : null, bytes);
        }
        public void Dispose() { if (ownsArchive) archive.Dispose(); }
    }
}
