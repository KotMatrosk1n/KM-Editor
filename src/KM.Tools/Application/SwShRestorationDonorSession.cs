// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.SV;
using KM.Formats.ZA;
using KM.Formats.PLA;
using KM.SV.Workflows;
using KM.ZA.Workflows;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

/// <summary>A bounded, process lifetime cache of only the donor files requested by conversion.</summary>
/// <remarks>Use a session for sequential preparations. It is not thread-safe.</remarks>
public sealed class SwShRestorationDonorSession : IDisposable
{
    private readonly Action? verifyAdditionalSource;
    private const long Budget = 128L * 1024 * 1024;
    private readonly Dictionary<string, (byte[] Bytes, string Stamp, LinkedListNode<string> Node)> files = new(StringComparer.Ordinal);
    private readonly LinkedList<string> order = new();
    private readonly Func<string, int, byte[]> read;
    private readonly Func<string, byte[]?> cached;
    private readonly IDisposable archive;
    private readonly string sourceStamp;
    private readonly string? support;
    private readonly SwShRestorationDonor game;
    private long retained;
    private bool disposed;
    public string Root { get; }
    public string Mode { get; }
    public int SessionHits { get; private set; }
    public int ExistingHits { get; private set; }
    public int DecodedFiles { get; private set; }

    public SwShRestorationDonorSession(SwShRestorationDonor game, string root, string? support)
    {
        Root = ResolveRoot(root); this.support = support; this.game = game;
        sourceStamp = SourceStamp(game, Root, support);
        if (game == SwShRestorationDonor.ScarletViolet)
        {
            var manager = new SvCacheManager();
            var paths = new ProjectPaths(Root, null, null, null, support, ProjectGame.Scarlet);
            SvTrinityArchiveIndex? index = null;
            foreach (var edition in new[] { ProjectGame.Scarlet, ProjectGame.Violet })
            {
                paths = paths with { SelectedGame = edition };
                if (manager.TryReadExistingDonorIndex(paths, out var found)) { index = found; break; }
            }
            var source = SvTrinityArchive.Open(Root, support, index: index,
                maximumIndexBytes: 32 * 1024 * 1024, maximumPackBytes: 256L * 1024 * 1024);
            archive = source; read = source.ReadFile;
            cached = path => manager.TryReadExistingDonorPayload(paths, path, out var bytes) ? bytes : null;
            Mode = index is null ? "temporary" : "existing";
        }
        else if (game == SwShRestorationDonor.LegendsZA)
        {
            var manager = new ZaCacheManager();
            var paths = new ProjectPaths(Root, null, null, null, ProjectGame.ZA) { PokemonLegendsZASupportFolderPath = support };
            var found = manager.TryReadExistingDonorIndex(paths, out var index);
            var source = ZaTrinityArchive.Open(Root, support, index: found ? index : null,
                maximumIndexBytes: 32 * 1024 * 1024, maximumPackBytes: 256L * 1024 * 1024);
            archive = source; read = source.ReadFile;
            cached = path => manager.TryReadExistingDonorPayload(paths, path, out var bytes) ? bytes : null;
            Mode = found ? "existing" : "temporary";
        }
        else if (game == SwShRestorationDonor.LegendsArceus)
        {
            var source = new PlaRestorationArchiveSource(Root, support);
            archive = source; read = source.Read; verifyAdditionalSource = source.Verify;
            cached = _ => null; Mode = "temporary";
        }
        else throw new InvalidDataException("The donor game is not supported.");
    }

    public bool Matches(SwShRestorationDonor donor, string root, string? compression) => !disposed && donor == game
        && sourceStamp == SourceStamp(donor, ResolveRoot(root), compression);

    public void VerifySource()
    {
        if (!Matches(game, Root, support)) throw new InvalidDataException("The donor changed during preparation. Select it again and retry.");
        verifyAdditionalSource?.Invoke();
    }

    internal void BeginPreparation()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (archive is not PlaRestorationArchiveSource source) return;
        VerifySource();
        source.BeginPreparation();
        // A decoded session hit must not bypass the new preparation's archive
        // observations and member accounting, or return bytes from an old pack.
        files.Clear(); order.Clear(); retained = 0;
    }

    public byte[] Read(string path, int maximumBytes = 32 * 1024 * 1024)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        VerifySource();
        if (TrinityPreviewReader.Resolve("catalog", path) != path) throw new InvalidDataException("The donor path is invalid.");
        var physical = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        var loose = File.Exists(physical);
        var stamp = loose ? Stamp(physical) : sourceStamp;
        if (files.TryGetValue(path, out var hit) && hit.Stamp == stamp)
        {
            if (hit.Bytes.Length > maximumBytes) throw new InvalidDataException("The donor file exceeds its size limit.");
            SessionHits++; return hit.Bytes.ToArray();
        }
        var bytes = loose ? ReadPhysical(physical, maximumBytes) : cached(path);
        if (!loose && bytes is not null) ExistingHits++;
        if (bytes is null) { bytes = read(path, maximumBytes); DecodedFiles++; }
        if (bytes.Length > maximumBytes) throw new InvalidDataException("The donor file exceeds its size limit.");
        if (files.Remove(path, out var previous)) { retained -= previous.Bytes.Length; order.Remove(previous.Node); }
        while (retained + bytes.Length > Budget && order.First is { } oldest)
        {
            order.RemoveFirst();
            if (files.Remove(oldest.Value, out var removed)) retained -= removed.Bytes.Length;
        }
        if (bytes.Length <= Budget) { files[path] = (bytes.ToArray(), stamp, order.AddLast(path)); retained += bytes.Length; }
        return bytes;
    }

    private static byte[] ReadPhysical(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > maximum) throw new InvalidDataException("The donor file exceeds its size limit.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    internal static string ResolveRoot(string root)
    {
        root = Path.GetFullPath(root);
        if (File.Exists(Path.Combine(root, "romfs", "bin", "pokemon", "table", "poke_resource_table.trpmcatalog")))
            return Path.Combine(root, "romfs");
        return !File.Exists(Path.Combine(root, "arc", "data.trpfd")) && File.Exists(Path.Combine(root, "romfs", "arc", "data.trpfd"))
            ? Path.Combine(root, "romfs") : root;
    }
    private static string SourceStamp(SwShRestorationDonor game, string root, string? support)
    {
        if (game == SwShRestorationDonor.LegendsArceus)
            return string.Join('|', game, Stamp(Path.Combine(root, "bin", "pokemon", "table", "poke_resource_table.trpmcatalog")),
                Stamp(Path.Combine(root, "bin", "pml", "personal", "personal_data_total.perbin")),
                Stamp(Path.Combine(support ?? "", PlaCompressionRuntime.RequiredFileName)));
        var runtime = game == SwShRestorationDonor.ScarletViolet
            ? SvCompressionRuntime.TryResolveRequiredFilePath(support, out var sv) ? sv : null
            : ZaCompressionRuntime.TryResolveRequiredFilePath(support, out var za) ? za : null;
        return string.Join('|', game, Stamp(Path.Combine(root, "arc", "data.trpfd")), Stamp(Path.Combine(root, "arc", "data.trpfs")),
            runtime is null ? support : Stamp(runtime));
    }
    private static string Stamp(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("A required donor file is missing.");
        return $"{info.FullName.ToUpperInvariant()}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }
    public void Dispose() { if (disposed) return; disposed = true; archive.Dispose(); files.Clear(); order.Clear(); retained = 0; }
}
