// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.HyperTraining;

namespace KM.SwSh.CustomGifts;

internal sealed class SwShCustomGiftPresentation
{
    internal const string MetadataPath = "romfs/bin/km-editor/gifts/presentation.json";
    internal const string InvalidCode = "KM-SWSH-CUSTOM-GIFTS-PRESENTATION-INVALID";
    internal sealed record Output(string Path, string Target, byte[]? Before, byte[]? After);
    private sealed record Resource(string Path, string Target, Lazy<byte[]?> BaseBytes, Lazy<byte[]?> OutputBytes)
    {
        internal byte[]? Base => BaseBytes.Value;
        internal byte[]? Output => OutputBytes.Value;
        internal byte[]? Source => Output ?? Base;
    }
    internal sealed record TextBackup(string Path, string Key, string Before, string After);
    internal sealed record IconBackup(string Path, byte[] Original);
    internal sealed record Manifest(int Version, bool Active, IReadOnlyList<TextBackup> Text, IReadOnlyList<IconBackup> Icons);
    private readonly Dictionary<string, Resource> resources;
    private static readonly string[] TitlePaths = new[] { "", "_eng", "_fre", "_ger", "_spa", "_ita", "_kor", "_sch", "_tch" }
        .Select(suffix => $"romfs/bin/appli/mystery/bin/mystery_title_00{suffix}.arc").ToArray();
    private static readonly string[] MenuPaths = new[] { "", "_eng", "_fre", "_ger", "_spa", "_ita", "_kor", "_sch", "_tch" }
        .Select(suffix => $"romfs/bin/appli/xmenu/bin/xmenu_top_00{suffix}.arc").ToArray();
    internal static IReadOnlyList<string> Paths { get; } = SwShGameTextLanguage.SupportedMessageLanguages
        .SelectMany(language => new[] { "xmenu.dat", "mystery.dat", "btlspot.dat", "xmenu.tbl", "mystery.tbl", "btlspot.tbl" }
            .Select(file => SwShGameTextLanguage.CommonMessagePath(language, file)))
        .Concat(TitlePaths).Concat(MenuPaths).Append(MetadataPath).ToArray();

    internal SwShCustomGiftPresentation(ProjectPaths paths)
    {
        resources = Paths.ToDictionary(path => path, path =>
        {
            var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, path) ?? "";
            byte[]? Read(string? file)
            {
                if (file is null || file.Length == 0) return null;
                if (Directory.Exists(file)) throw new InvalidDataException("A gift presentation file is a directory.");
                if (!File.Exists(file)) return null;
                if (new FileInfo(file).Length > (path == MetadataPath ? 2 * 1024 * 1024 : 8 * 1024 * 1024)) throw new InvalidDataException();
                return File.ReadAllBytes(file);
            }
            return new Resource(path, target, new(() => Read(SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, path))), new(() => Read(target)));
        }, StringComparer.Ordinal);
    }

    internal IReadOnlyList<Output> Prepare(string mode)
    {
        var metadata = resources[MetadataPath];
        Manifest? owned = null;
        if (metadata.Source is not null)
        {
            owned = JsonSerializer.Deserialize<Manifest>(metadata.Source) ?? throw new InvalidDataException();
            if (owned.Version != 1 || owned.Text is null || owned.Icons is null || owned.Text.Count > 256
                || owned.Text.Any(item => item is null || !Paths.Contains(item.Path) || !item.Path.EndsWith(".dat", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 512 || item.Before is null || item.After is null || item.Before.Length > 8192 || item.After.Length > 8192)
                || owned.Text.Select(item => (item.Path, item.Key)).Distinct().Count() != owned.Text.Count
                || owned.Icons.Count > TitlePaths.Length + MenuPaths.Length
                || owned.Icons.Any(item => item is null || !TitlePaths.Concat(MenuPaths).Contains(item.Path) || item.Original is null
                    || item.Original.Length != (MenuPaths.Contains(item.Path) ? 24200 : 90880))
                || owned.Icons.Select(item => item.Path).Distinct().Count() != owned.Icons.Count) throw new InvalidDataException("Unsupported Royal Gift presentation metadata.");
        }
        if (mode != "royal" && owned is null && !HasRoyalPresentation()) return [];
        var texts = new List<TextBackup>(); var icons = new List<IconBackup>(); var outputs = new List<Output>();
        foreach (var language in SwShGameTextLanguage.SupportedMessageLanguages)
        {
            foreach (var file in new[] { "xmenu.dat", "mystery.dat", "btlspot.dat" })
            {
                var resource = resources[SwShGameTextLanguage.CommonMessagePath(language, file)];
                if (resource.Source is null) continue;
                var source = SwShGameTextFile.Parse(resource.Source); var lines = source.Lines.ToArray();
                var original = resource.Base is null ? null : SwShGameTextFile.Parse(resource.Base);
                var keyResource = resources[resource.Path[..^4] + ".tbl"];
                var keys = ReadKeys(keyResource.Source, lines.Length);
                var originalKeys = original is null ? null : ReadKeys(keyResource.Base, original.Lines.Count);
                var indexes = keys.Entries.Select((entry, index) => (entry.Name, index)).ToDictionary(item => item.Name, item => item.index);
                var originalIndexes = originalKeys?.Entries.Select((entry, index) => (entry.Name, index)).ToDictionary(item => item.Name, item => item.index);
                var backups = owned?.Text.Where(item => item.Path == resource.Path).ToDictionary(item => item.Key) ?? [];
                var replacements = (original is null ? [] : Replacements(language, file, original.Lines, originalKeys!))
                    .Concat(Replacements(language, file, source.Lines, keys)).GroupBy(item => item.Key)
                    .ToDictionary(group => group.Key, group => group.Last().Text);
                foreach (var backup in backups.Values) replacements.TryAdd(backup.Key, backup.After);
                foreach (var (key, desired) in replacements.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    if (!indexes.TryGetValue(key, out var index)) throw new InvalidDataException("A Royal Gift message key is missing.");
                    backups.TryGetValue(key, out var backup);
                    var current = lines[index].Text;
                    var baseline = originalIndexes is not null && originalIndexes.TryGetValue(key, out var originalIndex) ? original!.Lines[originalIndex].Text : null;
                    // Recover a deleted metadata file using an unbranded Base when possible.
                    var before = backup is not null && current == backup.After ? backup.Before
                        : current == desired && baseline is not null && baseline != desired ? baseline : current;
                    if (mode == "royal")
                    {
                        if (owned?.Active == true && backup is not null && current != backup.After && resource.Output is not null)
                        { texts.Add(backup); continue; }
                        lines[index] = lines[index] with { Text = desired };
                        texts.Add(new(resource.Path, key, before, desired));
                    }
                    else if (backup is not null && current == backup.After || backup is null && baseline is not null && current == desired)
                        lines[index] = lines[index] with { Text = before };
                }
                // Recorded references can survive later edits to the other text in a file.
                if (mode != "royal")
                    foreach (var backup in backups.Values)
                    {
                        if (!indexes.TryGetValue(backup.Key, out var index)) throw new InvalidDataException();
                        if (lines[index].Text == backup.After) lines[index] = lines[index] with { Text = backup.Before };
                    }
                var bytes = source.WritePreserving(lines);
                outputs.Add(Result(resource, bytes));
            }
        }
        foreach (var path in TitlePaths)
        {
            var resource = resources[path]; if (resource.Source is null) continue;
            outputs.Add(Result(resource, Icon(resource, resource.Source, isMenu: false)));
        }
        foreach (var path in MenuPaths)
        {
            var resource = resources[path]; if (resource.Source is not null) outputs.Add(Result(resource, Icon(resource, resource.Source, isMenu: true)));
        }
        if (mode == "royal" && (texts.All(text => !text.Path.EndsWith("/xmenu.dat", StringComparison.Ordinal)) || icons.All(icon => !TitlePaths.Contains(icon.Path))))
            throw new InvalidDataException("Royal Gift requires native menu text and title resources.");
        outputs.Add(Result(metadata, mode == "royal" ? JsonSerializer.SerializeToUtf8Bytes(new Manifest(1, true, texts, icons))
            : metadata.Base is null ? null : JsonSerializer.SerializeToUtf8Bytes(new Manifest(1, false, owned!.Text, owned.Icons))));
        return outputs;

        byte[] Icon(Resource resource, byte[] bytes, bool isMenu)
        {
            var size = isMenu ? 220 : 1420; var name = isMenu ? SwShRoyalGiftIcon.MenuTexture : SwShRoyalGiftIcon.TitleTexture;
            var archive = new SwShGiftArchive(bytes); var texture = archive.Get(SwShRoyalGiftIcon.TextureMember);
            var region = SwShRoyalGiftIcon.Read(texture, name, size); var artwork = SwShRoyalGiftIcon.Artwork(size);
            var backup = owned?.Icons.SingleOrDefault(item => item.Path == resource.Path);
            var currentIsRoyal = region.Bytes.AsSpan().SequenceEqual(artwork);
            var before = currentIsRoyal && backup is not null ? backup.Original : currentIsRoyal && resource.Base is not null
                ? SwShRoyalGiftIcon.Read(new SwShGiftArchive(resource.Base).Get(SwShRoyalGiftIcon.TextureMember), name, size).Bytes : region.Bytes;
            if (currentIsRoyal && before.AsSpan().SequenceEqual(artwork) && backup is null)
                throw new InvalidDataException("Royal Gift artwork restoration metadata is missing.");
            if (mode == "royal")
            {
                if (owned?.Active == true && backup is not null && !currentIsRoyal && resource.Output is not null) { icons.Add(backup); return bytes; }
                icons.Add(new(resource.Path, before));
                return archive.Replace(SwShRoyalGiftIcon.TextureMember, SwShRoyalGiftIcon.Write(texture, region, artwork));
            }
            return currentIsRoyal ? archive.Replace(SwShRoyalGiftIcon.TextureMember, SwShRoyalGiftIcon.Write(texture, region, before)) : bytes;
        }
    }

    private bool HasRoyalPresentation()
    {
        foreach (var language in SwShGameTextLanguage.SupportedMessageLanguages)
        foreach (var file in new[] { "xmenu.dat", "mystery.dat" })
        {
            var path = SwShGameTextLanguage.CommonMessagePath(language, file);
            try
            {
                if (resources[path].Source is not { } bytes) continue;
                var text = SwShGameTextFile.Parse(bytes); var keys = ReadKeys(resources[path[..^4] + ".tbl"].Source, text.Lines.Count);
                foreach (var (key, value) in Replacements(language, file, text.Lines, keys))
                {
                    var index = keys.Entries.Select((entry, index) => (entry.Name, index)).Where(entry => entry.Name == key)
                        .Select(entry => entry.index).DefaultIfEmpty(-1).Single();
                    if (index >= 0 && text.Lines[index].Text == value) return true;
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) { }
        }
        foreach (var path in TitlePaths.Concat(MenuPaths))
        {
            try
            {
                if (resources[path].Source is not { } bytes) continue;
                var menu = MenuPaths.Contains(path); var size = menu ? 220 : 1420;
                var region = SwShRoyalGiftIcon.Read(new SwShGiftArchive(bytes).Get(SwShRoyalGiftIcon.TextureMember),
                    menu ? SwShRoyalGiftIcon.MenuTexture : SwShRoyalGiftIcon.TitleTexture, size);
                if (region.Bytes.AsSpan().SequenceEqual(SwShRoyalGiftIcon.Artwork(size))) return true;
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException) { }
        }
        return false;
    }
    private static Output Result(Resource resource, byte[]? bytes)
    {
        if (bytes is not null && resource.Base is not null)
        {
            var same = resource.Path.EndsWith(".arc", StringComparison.Ordinal)
                ? new SwShGiftArchive(bytes).SameMembers(new(resource.Base)) : bytes.AsSpan().SequenceEqual(resource.Base);
            if (same) bytes = null;
        }
        return new(resource.Path, resource.Target, resource.Output, bytes);
    }

    private static SwShAhtbFile ReadKeys(byte[]? bytes, int count)
    {
        if (bytes is null) throw new InvalidDataException("The gift message key table is missing.");
        var keys = SwShAhtbFile.Parse(bytes, 65536);
        if (keys.Entries.Count == count + 1 && keys.Entries[^1].Name.EndsWith("_max", StringComparison.OrdinalIgnoreCase))
            keys = new(keys.Entries.Take(count).ToArray());
        if (keys.Entries.Count != count || keys.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != count)
            throw new InvalidDataException("Gift text and message keys do not match.");
        return keys;
    }

    private static IEnumerable<(string Key, string Text)> Replacements(string language, string file, IReadOnlyList<SwShGameTextLine> lines, SwShAhtbFile keys)
    {
        if (file == "xmenu.dat") { yield return ("xmenu_ui_gift_s", "ROYAL GIFT"); yield break; }
        if (file == "mystery.dat")
        {
            var labels = language switch
            {
                "French" => ("Vérifier Royal Gift", "Recevoir un Royal Gift"),
                "German" => ("Royal Gift überprüfen", "Royal Gift erhalten"),
                "Italian" => ("Controlla Royal Gift", "Ricevi un Royal Gift"),
                "Spanish" => ("Ver Royal Gifts", "Recibir un Royal Gift"),
                "JPN" => ("Royal Giftを　かくにんする", "Royal Giftを　うけとる"),
                "JPN_KANJI" => ("Royal Giftを　確認する", "Royal Giftを　受け取る"),
                "Korean" => ("Royal Gift를 확인한다", "Royal Gift를 받는다"),
                "Simp_Chinese" => ("确认Royal Gift", "接收Royal Gift"),
                "Trad_Chinese" => ("確認Royal Gift", "接收Royal Gift"),
                _ => ("Check Royal Gifts", "Get a Royal Gift"),
            };
            yield return ("msg_o_mystery_sel_05", labels.Item1); yield return ("msg_o_mystery_sel_12", labels.Item2); yield break;
        }
        string[] terms = language switch
        {
            "French" => ["Cadeaux Mystère", "Cadeau Mystère"],
            "German" => ["Geheimgeschenken", "Geheimgeschenke", "Geheimgeschenk"],
            "Italian" => ["Doni Segreti", "Dono Segreto"], "Spanish" => ["regalos misteriosos", "regalo misterioso"],
            "JPN" or "JPN_KANJI" => ["ふしぎなおくりもの"], "Korean" => ["이상한 소포"],
            "Simp_Chinese" => ["神秘礼物"], "Trad_Chinese" => ["神秘禮物"], _ => ["Mystery Gift"],
        };
        for (var i = 0; i < lines.Count; i++)
        {
            var changed = terms.Aggregate(lines[i].Text, (text, term) => text.Replace(term, "Royal Gift", StringComparison.OrdinalIgnoreCase));
            if (changed != lines[i].Text) yield return (keys.Entries[i].Name, changed);
        }
    }
}
