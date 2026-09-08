// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Audio;
using KM.Core.Files;
using KM.Core.Concurrency;
using KM.Core.Projects;
using KM.Formats.Audio;
using KM.Formats.SwSh;
using KM.SwSh.Audio;
using System.Text.RegularExpressions;

namespace KM.Tools.Bridge;

internal static class SoundStudioBridge
{
    private sealed record Asset(SoundBankItem Item, string Path, long FileSize, long Stamp);
    private sealed class Catalog(string token, ProjectPaths paths)
    {
        public string Token { get; } = token;
        public ProjectPaths Paths { get; } = paths;
        public CancellationTokenSource Cancel { get; } = new();
        public Asset[] Assets = [];
        public string[] SpeciesNames = [];
        public volatile int Completed;
        public volatile int Total;
        public volatile bool Done;
        public volatile string? Error;
        public DateTime LastAccess = DateTime.UtcNow;
        public Task Worker = Task.CompletedTask;
    }
    private static readonly object Gate = new();
    private static Catalog? current;
    private static readonly BoundedConcurrencyPolicy CatalogPolicy = new("audio.catalog", BoundedWorkloadKind.Read, 192L * 1024 * 1024, maximumDegreeOfParallelism: 1);
    private static readonly Timer Expiration = new(_ => { lock (Gate) { if (current is { } c && DateTime.UtcNow - c.LastAccess > TimeSpan.FromMinutes(2)) Close(); } }, null, 60000, 60000);

    internal static object Dispatch(SoundStudioRequest request)
    {
        if (request.Token is not { Length: 32 } || !request.Token.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Audio catalog token is invalid.");
        var paths = ProjectBridgeMapper.ToCore(request.Paths);
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield or ProjectGame.Scarlet or ProjectGame.Violet or ProjectGame.ZA))
            throw new InvalidDataException("Select a supported game.");
        lock (Gate)
        {
            if (request.Action == "begin")
            {
                Close(); var catalog = new Catalog(request.Token, paths); current = catalog;
                catalog.Worker = BuildAsync(catalog); return new { Ready = true };
            }
            if (request.Action == "close") { if (current?.Token == request.Token) Close(); return new { Ready = true }; }
            var state = current;
            if (state is null || state.Token != request.Token || !Equals(state.Paths, paths)) throw new InvalidDataException("Audio catalog expired. Reload Sound Studio.");
            state.LastAccess = DateTime.UtcNow;
            if (request.Action == "status") return new { state.Done, state.Completed, state.Total, Count = state.Assets.Length, state.Error };
            if (!state.Done || state.Error is not null) throw new InvalidDataException("Audio catalog is not ready.");
            if (request.Action == "page")
            {
                if (request.Index < 0 || request.Index > state.Assets.Length) throw new InvalidDataException("Audio catalog page is invalid.");
                return state.Assets.Skip(request.Index).Take(512).Select((asset, i) => new {
                    Id = request.Index + i, asset.Item.Identifier, asset.Item.Kind, asset.Item.Bank,
                    asset.Item.Codec, asset.Item.Channels, asset.Item.SampleRate, asset.Item.Status,
                    Size = asset.Item.Length, Category = Category(asset.Item.Bank), Species = Species(asset.Item.Bank),
                    Name = DisplayName(asset.Item, state.SpeciesNames)
                }).ToArray();
            }
            if (request.Action != "media" || request.Index < 0 || request.Index >= state.Assets.Length) throw new InvalidDataException("Audio selection is invalid.");
            var selected = state.Assets[request.Index]; var item = selected.Item;
            if (item.Status != "sample" || item.Length > 64L * 1024 * 1024 || request.Offset < 0 || request.Offset >= item.Length)
                throw new InvalidDataException("This audio entry is not playable.");
            VerifySource(selected);
            using var stream = new FileStream(selected.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != selected.FileSize) throw new InvalidDataException("Audio source changed.");
            var bytes = new byte[(int)Math.Min(512 * 1024, item.Length - request.Offset)];
            stream.Position = checked(item.Offset + request.Offset); stream.ReadExactly(bytes); VerifySource(selected);
            return new { Data = Convert.ToBase64String(bytes) };
        }
    }
    private static void Close()
    {
        var state = current; current = null;
        if (state is null) return;
        state.Cancel.Cancel(); _ = ReleaseAsync(state);
    }
    private static async Task ReleaseAsync(Catalog state)
    {
        try { await state.Worker.ConfigureAwait(false); }
        finally { state.Cancel.Dispose(); }
    }
    private static async Task BuildAsync(Catalog state)
    {
        try { await BoundedParallel.RunAsync(CatalogPolicy, _ => Build(state), state.Cancel.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is BoundedConcurrencyResourceException or BoundedWorkItemException)
        { state.Error = "KM-AUDIO-SOURCE-UNAVAILABLE"; state.Done = true; }
    }
    private static void VerifySource(Asset asset)
    {
        var info = new FileInfo(asset.Path);
        if (info.Length != asset.FileSize || info.LastWriteTimeUtc.Ticks != asset.Stamp || !SafeChain(info))
            throw new InvalidDataException("Audio source changed. Reload Sound Studio.");
    }
    private static bool SafeChain(FileSystemInfo info)
    {
        for (FileSystemInfo? part = info; part is not null; part = part is FileInfo file ? file.Directory : ((DirectoryInfo)part).Parent)
            if (!part.Exists || part.LinkTarget is not null) return false;
        return true;
    }
    private static void Build(Catalog state)
    {
        try
        {
            var token = state.Cancel.Token;
            if (state.Paths.BaseRomFsPath is not { } root || !Directory.Exists(root)) throw new InvalidDataException("Audio source is missing.");
            // Use the same source graph and overlay precedence as the other asset browsers.
            var graph = new ProjectFileGraphBuilder().Build(state.Paths with { BaseExeFsPath = null }, token);
            if (state.Paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield)
            {
                var namesPath = Path.Combine(root, SwShSoundMetadata.SpeciesNamePath(state.Paths));
                var info = new FileInfo(namesPath);
                if (info.Exists && info.Length < 1024 * 1024 && SafeChain(info))
                    try { state.SpeciesNames = SwShGameTextFile.Parse(File.ReadAllBytes(namesPath), 2048).Lines.Select(line => line.Text).ToArray(); }
                    catch (InvalidDataException) { }
            }
            var files = graph.Entries.Where(entry => entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(entry.RelativePath).ToLowerInvariant() is ".bnk" or ".pck" or ".wem" or ".wav" or ".ogg" or ".opus" or ".bfsar" or ".bfstm").ToArray();
            state.Total = files.Length; var assets = new List<Asset>();
            foreach (var entry in files)
            {
                token.ThrowIfCancellationRequested();
                var path = entry.LayeredFile is not null ? Path.Combine(state.Paths.OutputRootPath!, entry.RelativePath)
                    : Path.Combine(root, entry.RelativePath[6..]);
                var info = new FileInfo(path); if (!SafeChain(info)) throw new InvalidDataException("Audio source is unavailable.");
                var size = info.Length; var stamp = info.LastWriteTimeUtc.Ticks;
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, FileOptions.RandomAccess);
                    foreach (var item in new SoundBankReader(stream, token).Read(entry.RelativePath[6..])) assets.Add(new(item, path, size, stamp));
                }
                catch (Exception e) when (e is InvalidDataException or EndOfStreamException or OverflowException)
                { assets.Add(new(new(Path.GetFileName(entry.RelativePath), "bank", 0, 0, entry.RelativePath[6..], Status: "damaged"), path, size, stamp)); }
                VerifySource(new(new("", "", 0, 0, ""), path, size, stamp));
                if (assets.Count > 200_000) throw new InvalidDataException("Audio catalog exceeds its budget.");
                state.Completed++;
            }
            // Prefetch headers and external references must resolve to a complete, unambiguous recording.
            var complete = assets.Where(a => a.Item.Kind == "sample" && a.Item.Status == "sample")
                .GroupBy(a => a.Item.Identifier).ToDictionary(g => g.Key, g => g.ToArray());
            for (var i = 0; i < assets.Count; i++)
            {
                token.ThrowIfCancellationRequested(); var asset = assets[i];
                if (asset.Item.Status is not ("missing" or "prefetch") || !complete.TryGetValue(asset.Item.Identifier, out var candidates)) continue;
                var related = candidates.Where(c => c.Item.Bank.Split('/')[0] == asset.Item.Bank.Split('/')[0]).ToArray();
                if (related.Length == 1) assets[i] = related[0] with { Item = related[0].Item with { Bank = asset.Item.Bank } };
            }
            state.Assets = assets.ToArray(); state.Done = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { state.Error = "KM-AUDIO-SOURCE-UNAVAILABLE"; state.Done = true; }
    }
    private static string Category(string bank)
    {
        var name = Path.GetFileNameWithoutExtension(bank).ToUpperInvariant();
        if (Species(bank) > 0) return "cries";
        if (name is "BGM" || name.StartsWith("BGM_", StringComparison.Ordinal)) return "music";
        if (name is "PM_VOICE" || name.StartsWith("POKEVOICE", StringComparison.Ordinal)) return "cries";
        if (name.StartsWith("BATTLE", StringComparison.Ordinal) || name == "PM_SKILLS") return "battle";
        if (name is "ENV" || name.StartsWith("AMBI", StringComparison.Ordinal)) return "ambience";
        if (name is "UI" or "UI_ADD" or "COMMON_UI" || name.StartsWith("UI_", StringComparison.Ordinal)) return "interface";
        if (name is "NPC" or "PL") return "characters";
        if (name is "ME" or "DEMO" or "DIALOG") return "events";
        return "other";
    }
    private static int Species(string path)
    {
        var match = Regex.Match(Path.GetFileName(path), @"^pv(\d{4})_\d{3}_(Common|Happy)\.wav$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var species) ? species : 0;
    }
    private static string DisplayName(SoundBankItem item, string[] names)
    {
        var species = Species(item.Bank);
        if (species > 0 && species < names.Length) return $"{names[species]} #{species} / {item.Identifier}";
        return Path.GetFileName(item.Bank) == item.Identifier ? item.Identifier : $"{Path.GetFileNameWithoutExtension(item.Bank)} / {item.Identifier}";
    }
}
