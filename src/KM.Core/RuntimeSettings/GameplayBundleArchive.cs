// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;

namespace KM.Core.RuntimeSettings;

public sealed record GameplayBundleArchiveResult(
    byte[] Bytes,
    string Sha256,
    IReadOnlyList<string> Entries);

public sealed class GameplayBundleArchiveReadResult
{
    internal GameplayBundleArchiveReadResult(
        GameplayBundleManifest manifest,
        ImmutableDictionary<string, ImmutableArray<byte>> immutableComponents,
        ImmutableDictionary<string, ImmutableArray<byte>> runtimeMutableComponents,
        ImmutableArray<byte> settingsJournal,
        ImmutableArray<byte> manifestBytes,
        string sha256,
        ImmutableArray<string> entries)
    {
        Manifest = manifest;
        ImmutableComponents = immutableComponents;
        RuntimeMutableComponents = runtimeMutableComponents;
        SettingsJournal = settingsJournal;
        ManifestBytes = manifestBytes;
        Sha256 = sha256;
        Entries = entries;
    }

    public GameplayBundleManifest Manifest { get; }

    public ImmutableDictionary<string, ImmutableArray<byte>> ImmutableComponents { get; }

    public ImmutableDictionary<string, ImmutableArray<byte>> RuntimeMutableComponents { get; }

    public ImmutableArray<byte> SettingsJournal { get; }

    public ImmutableArray<byte> ManifestBytes { get; }

    public string Sha256 { get; }

    public ImmutableArray<string> Entries { get; }
}

public static class GameplayBundleArchive
{
    public const int MaximumEntryCount = GameplayBundleIdentity.MaximumComponentCount + 2;
    public const int MaximumEntryBytes = 128 * 1024 * 1024;
    public const long MaximumArchivePayloadBytes = 512L * 1024L * 1024L;
    private static readonly DateTimeOffset CanonicalTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static GameplayBundleArchiveResult Build(
        GameplayBundleManifest manifest,
        IReadOnlyDictionary<string, byte[]> immutableComponents,
        GameplaySettingsFamily family,
        byte[] settingsJournal)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(immutableComponents);
        ArgumentNullException.ThrowIfNull(settingsJournal);
        var canonicalManifest = GameplayBundleIdentity.SerializeManifest(manifest);
        var reparsedManifest = GameplayBundleIdentity.ParseManifest(canonicalManifest);
        GameplayBundleIdentity.VerifyComponents(reparsedManifest, immutableComponents);
        ValidateTitleLayerPaths(reparsedManifest);
        ValidateBootstrapJournal(reparsedManifest, family, settingsJournal);

        var settingsPath = GetSettingsPath(manifest.TitleId);
        var manifestPath = GetManifestPath(manifest.TitleId);
        var entries = reparsedManifest.Components
            .Select(component => (component.Path, Bytes: immutableComponents[component.Path]))
            .Append((Path: settingsPath, Bytes: settingsJournal))
            .Append((Path: manifestPath, Bytes: canonicalManifest))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
        ValidatePayloadBounds(entries);
        var bytes = WriteCanonicalArchive(entries);
        var verifiedEntries = Verify(bytes, family);
        if (!entries.Select(entry => entry.Path).SequenceEqual(verifiedEntries, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The rebuilt gameplay bundle archive changed its canonical entry inventory.");
        }

        return new GameplayBundleArchiveResult(
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)),
            verifiedEntries);
    }

    public static IReadOnlyList<string> Verify(
        ReadOnlyMemory<byte> archiveBytes,
        GameplaySettingsFamily expectedFamily)
    {
        return Read(archiveBytes, expectedFamily).Entries;
    }

    public static GameplayBundleArchiveReadResult Read(
        ReadOnlyMemory<byte> archiveBytes,
        GameplaySettingsFamily expectedFamily)
    {
        if (!Enum.IsDefined(expectedFamily))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedFamily));
        }

        if (archiveBytes.Length < 1 || (long)archiveBytes.Length > MaximumArchivePayloadBytes)
        {
            throw new InvalidDataException("The gameplay bundle archive is empty or exceeds its bounded size.");
        }

        using var input = new MemoryStream(archiveBytes.ToArray(), writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count is < 3 or > MaximumEntryCount)
        {
            throw new InvalidDataException("The gameplay bundle archive entry count is invalid or out of bounds.");
        }

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var foldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            GameplayBundleIdentity.ValidateNormalizedPath(entry.FullName);
            if (!foldedPaths.Add(entry.FullName))
            {
                throw new InvalidDataException("The gameplay bundle archive contains a duplicate or case-colliding path.");
            }

            if (entry.LastWriteTime.DateTime != CanonicalTimestamp.DateTime)
            {
                throw new InvalidDataException("A gameplay bundle archive entry does not use the canonical timestamp.");
            }

            if (entry.ExternalAttributes != 0x81A4 << 16)
            {
                throw new InvalidDataException("A gameplay bundle archive entry does not use the canonical file attributes.");
            }

            if (entry.Length < 0 || entry.Length > MaximumEntryBytes)
            {
                throw new InvalidDataException("A gameplay bundle archive entry exceeds its bounded size.");
            }

            if (entry.CompressedLength != entry.Length)
            {
                throw new InvalidDataException("A gameplay bundle archive entry is unexpectedly compressed.");
            }

            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaximumArchivePayloadBytes)
            {
                throw new InvalidDataException("The gameplay bundle archive payload exceeds its bounded size.");
            }

            using var stream = entry.Open();
            using var bytes = new MemoryStream(checked((int)entry.Length));
            stream.CopyTo(bytes);
            if (bytes.Length != entry.Length || !entries.TryAdd(entry.FullName, bytes.ToArray()))
            {
                throw new InvalidDataException("A gameplay bundle archive entry is truncated or duplicated.");
            }
        }

        var manifestCandidates = entries
            .Where(entry => entry.Key.EndsWith("/bundle.manifest", StringComparison.Ordinal))
            .ToArray();
        if (manifestCandidates.Length != 1)
        {
            throw new InvalidDataException("The gameplay bundle archive must contain exactly one manifest.");
        }

        var manifest = GameplayBundleIdentity.ParseManifest(manifestCandidates[0].Value);
        var expectedManifestPath = GetManifestPath(manifest.TitleId);
        var expectedSettingsPath = GetSettingsPath(manifest.TitleId);
        if (!string.Equals(manifestCandidates[0].Key, expectedManifestPath, StringComparison.Ordinal)
            || !entries.TryGetValue(expectedSettingsPath, out var settingsJournal))
        {
            throw new InvalidDataException("The gameplay bundle archive control files are misplaced or incomplete.");
        }

        ValidateTitleLayerPaths(manifest);
        ValidateBootstrapJournal(manifest, expectedFamily, settingsJournal);
        var components = entries
            .Where(entry => !string.Equals(entry.Key, expectedManifestPath, StringComparison.Ordinal)
                && !string.Equals(entry.Key, expectedSettingsPath, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        GameplayBundleIdentity.VerifyComponents(manifest, components);
        var expectedTogglesPath = GetTogglesPath(manifest.TitleId);
        var runtimeMutable = components
            .Where(entry => string.Equals(entry.Key, expectedTogglesPath, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        foreach (var component in runtimeMutable.Values)
        {
            _ = AtmosphereCheatToggleDocument.Parse(component);
        }

        var immutable = components
            .Where(entry => !string.Equals(entry.Key, expectedTogglesPath, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        if (!GameplayBundleIdentity.SerializeManifest(manifest).AsSpan().SequenceEqual(manifestCandidates[0].Value))
        {
            throw new InvalidDataException("The gameplay bundle manifest changed during archive verification.");
        }

        var canonicalEntries = entries
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => (Path: entry.Key, Bytes: entry.Value))
            .ToArray();
        var canonicalBytes = WriteCanonicalArchive(canonicalEntries);
        if (!archiveBytes.Span.SequenceEqual(canonicalBytes))
        {
            throw new InvalidDataException("The gameplay bundle archive does not use the canonical byte representation.");
        }

        return new GameplayBundleArchiveReadResult(
            manifest,
            immutable.ToImmutableDictionary(
                entry => entry.Key,
                entry => ImmutableArray.CreateRange(entry.Value),
                StringComparer.Ordinal),
            runtimeMutable.ToImmutableDictionary(
                entry => entry.Key,
                entry => ImmutableArray.CreateRange(entry.Value),
                StringComparer.Ordinal),
            ImmutableArray.CreateRange(settingsJournal),
            ImmutableArray.CreateRange(manifestCandidates[0].Value),
            Convert.ToHexString(SHA256.HashData(archiveBytes.Span)),
            canonicalEntries.Select(entry => entry.Path).ToImmutableArray());
    }

    private static void ValidateBootstrapJournal(
        GameplayBundleManifest manifest,
        GameplaySettingsFamily family,
        byte[] settingsJournal)
    {
        if (manifest.PackageVersion.Major > ushort.MaxValue
            || manifest.PackageVersion.Minor > ushort.MaxValue
            || manifest.PackageVersion.Patch > ushort.MaxValue)
        {
            throw new InvalidDataException("The package version cannot be represented by the settings journal.");
        }

        var inspection = GameplaySettingsJournal.Inspect(settingsJournal, family, manifest.TitleId);
        if (inspection.Disposition != GameplaySettingsJournalDisposition.Ready
            || inspection.ActiveSlotIndex != 0
            || inspection.ActiveSnapshot is null
            || inspection.ActiveSnapshot.Generation != 1
            || inspection.SlotB.Classification != GameplaySettingsSlotClassification.Empty)
        {
            throw new InvalidDataException("The archive settings journal is not the canonical one-slot bootstrap image.");
        }

        var expectedVersion = new GameplaySettingsWriterVersion(
            (ushort)manifest.PackageVersion.Major,
            (ushort)manifest.PackageVersion.Minor,
            (ushort)manifest.PackageVersion.Patch);
        if (inspection.ActiveSnapshot.WriterVersion != expectedVersion)
        {
            throw new InvalidDataException("The archive settings journal package version does not match the manifest.");
        }

        var canonical = GameplaySettingsJournal.CreateBootstrap(
            family,
            manifest.TitleId,
            expectedVersion,
            inspection.ActiveSnapshot.Presence,
            inspection.ActiveSnapshot.Values);
        if (!canonical.AsSpan().SequenceEqual(settingsJournal))
        {
            throw new InvalidDataException("The archive settings journal is not byte-canonical.");
        }
    }

    private static void ValidateTitleLayerPaths(GameplayBundleManifest manifest)
    {
        if (manifest.Components.Count == 0)
        {
            throw new InvalidDataException(
                "The gameplay bundle must contain at least one immutable title-layer component.");
        }

        var titleRoot = GetSupportedTitleRoots(manifest.TitleId)
            .FirstOrDefault(candidate => manifest.Components.All(component =>
                component.Path.StartsWith(candidate, StringComparison.Ordinal)
                && component.Path.Length > candidate.Length));
        if (titleRoot is null)
        {
            throw new InvalidDataException(
                "Every immutable gameplay bundle component must belong to one exact supported title layer.");
        }
    }

    private static IReadOnlyList<string> GetSupportedTitleRoots(ulong titleId) =>
    [
        $"atmosphere/contents/{titleId:X16}/",
        $"mods/contents/{titleId:X16}/KM-Gameplay-Settings/",
        $"load/{titleId:X16}/KM-Gameplay-Settings/",
    ];

    private static void ValidatePayloadBounds(IReadOnlyList<(string Path, byte[] Bytes)> entries)
    {
        if (entries.Count is < 3 or > MaximumEntryCount)
        {
            throw new InvalidDataException("The gameplay bundle payload entry count is invalid or out of bounds.");
        }

        long total = 0;
        foreach (var entry in entries)
        {
            GameplayBundleIdentity.ValidateNormalizedPath(entry.Path);
            ArgumentNullException.ThrowIfNull(entry.Bytes);
            if (entry.Bytes.LongLength > MaximumEntryBytes)
            {
                throw new InvalidDataException("A gameplay bundle payload entry exceeds its bounded size.");
            }

            total = checked(total + entry.Bytes.LongLength);
            if (total > MaximumArchivePayloadBytes)
            {
                throw new InvalidDataException("The gameplay bundle payload exceeds its bounded size.");
            }
        }
    }

    private static byte[] WriteCanonicalArchive(IReadOnlyList<(string Path, byte[] Bytes)> entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Path, CompressionLevel.NoCompression);
                zipEntry.LastWriteTime = CanonicalTimestamp;
                zipEntry.ExternalAttributes = 0x81A4 << 16;
                using var stream = zipEntry.Open();
                stream.Write(entry.Bytes);
            }
        }

        return output.ToArray();
    }

    private static string GetSettingsPath(ulong titleId) =>
        $"config/km-editor/gameplay-settings/{titleId:X16}/settings.bin";

    private static string GetManifestPath(ulong titleId) =>
        $"config/km-editor/gameplay-settings/{titleId:X16}/bundle.manifest";

    private static string GetTogglesPath(ulong titleId) =>
        $"atmosphere/contents/{titleId:X16}/cheats/toggles.txt";
}
