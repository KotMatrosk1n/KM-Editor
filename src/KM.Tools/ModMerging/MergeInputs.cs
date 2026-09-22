// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using KM.Api.ModMerger;
using KM.Core.Output;
using KM.Core.Projects;
using SharpCompress.Common;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace KM.Tools.ModMerging;

internal sealed record MergeInputFile(string Path, byte[] Bytes, string Hash);
internal sealed record MergeInput(MergeSourceInfoDto Info, IReadOnlyDictionary<string, MergeInputFile> Files, string SourceFingerprint);
internal sealed class MergeInputException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal static partial class MergeInputs
{
    internal const int MaximumFiles = 100_000;
    internal const long MaximumBytes = 1024L * 1024 * 1024;

    public static MergeInput Read(MergeSourceDto source, ref long remaining)
    {
        var raw = new Dictionary<string, MergeInputFile>(StringComparer.OrdinalIgnoreCase);
        var sourcePath = Path.GetFullPath(source.Path);
        RejectLinks(sourcePath);
        if (Directory.Exists(sourcePath))
        {
            foreach (var path in EnumerateFiles(sourcePath))
            {
                RejectLinks(path);
                using var stream = File.OpenRead(path);
                Add(Path.GetRelativePath(sourcePath, path), stream, stream.Length, raw, ref remaining);
            }
        }
        else
        {
            using var stream = File.OpenRead(sourcePath);
            var signature = new byte[6];
            var signatureLength = stream.Read(signature);
            stream.Position = 0;
            using var archive = signatureLength == 6 && signature.AsSpan().SequenceEqual(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c })
                ? ArchiveFactory.OpenArchive(stream, new ReaderOptions()) : null;
            using var reader = archive is not null ? archive.ExtractAllEntries() : ReaderFactory.OpenReader(stream, new ReaderOptions());
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory) continue;
                if (reader.Entry.IsEncrypted || !string.IsNullOrEmpty(reader.Entry.LinkTarget))
                    throw new MergeInputException(MergeWorkspaceErrorCodes.ArchiveUnreadable, "Encrypted entries and archive links cannot be merged.");
                using var entry = reader.OpenEntryStream();
                Add(reader.Entry.Key ?? "", entry, reader.Entry.Size, raw, ref remaining);
            }
        }
        var sourceFolder = Directory.Exists(sourcePath) ? new DirectoryInfo(sourcePath) : null;
        var selectedRoot = sourceFolder?.Name.ToLowerInvariant();
        var folderGame = sourceFolder is null ? null : DetectTitle(sourceFolder.Name)
            ?? (selectedRoot is "romfs" or "exefs" && sourceFolder.Parent is { } parent ? DetectTitle(parent.Name) : null);
        var games = raw.Keys.SelectMany(path => path.Split('/')).Select(DetectTitle).Append(folderGame).OfType<string>().Distinct().ToArray();
        if (games.Length > 1)
            throw new MergeInputException(MergeWorkspaceErrorCodes.MixedGames, "This source contains multiple game packages. Add each game package separately.");
        // Remove a single packaging wrapper only when every file shares it.
        while (raw.Count > 0 && raw.Keys.Any(path => path.Split('/').Skip(1).Any(IsRoot)) && raw.Keys.All(path => path.Contains('/'))
            && raw.Keys.Select(path => path.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            && !IsRoot(raw.Keys.First().Split('/')[0]))
            raw = raw.Values.ToDictionary(file => file.Path[(file.Path.IndexOf('/') + 1)..],
                file => file with { Path = file.Path[(file.Path.IndexOf('/') + 1)..] }, StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, MergeInputFile>(StringComparer.OrdinalIgnoreCase);
        var isRomFsFolder = selectedRoot == "romfs";
        foreach (var pair in raw)
        {
            var normalized = selectedRoot is "romfs" or "exefs" ? selectedRoot + "/" + pair.Key : NormalizePackagePath(pair.Key);
            var payload = pair.Value;
            if (normalized.EndsWith(".pchtxt", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var patch = MergePatchText.Read(payload.Bytes);
                    normalized = "exefs/" + patch.Build + ".ips";
                    payload = new(normalized, patch.Bytes, Convert.ToHexString(SHA256.HashData(patch.Bytes)));
                }
                catch (Exception exception) when (exception is InvalidDataException or FormatException or OverflowException or System.Text.DecoderFallbackException)
                { throw new MergeInputException(MergeWorkspaceErrorCodes.PatchInvalid, exception.Message); }
            }
            if (source.Layout == "trinity" && !MergeExecutableEdits.IsPatch(normalized) && !normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase) && !normalized.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
                normalized = "romfs/" + normalized;
            if (MergeExecutableEdits.IsPatch(normalized) && !normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
            {
                var buildId = Path.GetFileNameWithoutExtension(normalized);
                if (buildId.Length is >= 16 and <= 64 && buildId.Length % 2 == 0 && buildId.All(Uri.IsHexDigit))
                    normalized = "exefs/" + buildId.ToUpperInvariant() + ".ips";
            }
            if (files.TryGetValue(normalized, out var duplicate) && (duplicate.Hash != payload.Hash || duplicate.Path != normalized))
            {
                if (MergeExecutableEdits.IsPatch(normalized))
                {
                    try
                    {
                        var combined = MergeExecutableEdits.CombinePatches([new("first", "", duplicate.Bytes), new("second", "", payload.Bytes)],
                            (_, _) => throw new InvalidDataException("Patch files within one source disagree at the same address."));
                        files[normalized] = new(normalized, combined, Convert.ToHexString(SHA256.HashData(combined)));
                        continue;
                    }
                    catch (InvalidDataException exception) { throw new MergeInputException(MergeWorkspaceErrorCodes.PatchInvalid, exception.Message); }
                }
                throw new MergeInputException(MergeWorkspaceErrorCodes.DuplicatePath, "This source contains different installation alternatives for the same file. Add one alternative at a time.");
            }
            files[normalized] = payload with { Path = normalized };
        }
        var evidence = new List<string>();
        var game = games.FirstOrDefault();
        if (game is not null) evidence.Add("title");
        if (files.TryGetValue("exefs/main.npdm", out var npdm) && DetectNpdm(npdm.Bytes) is { } executableGame)
        {
            if (game is not null && !Compatible(game, executableGame))
                throw new MergeInputException(MergeWorkspaceErrorCodes.MixedGames, "The executable metadata and title folder target different games.");
            game = executableGame;
            evidence.Add("executable");
        }
        var family = files.Keys.Any(path => path.StartsWith("romfs/ik_pokemon/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("romfs/ik_message/", StringComparison.OrdinalIgnoreCase)) ? "za"
            : files.Keys.Any(path => path.StartsWith("romfs/bin/pml/", StringComparison.OrdinalIgnoreCase)) ? "swsh"
            : files.Keys.Any(path => path.StartsWith("romfs/world/data/encount", StringComparison.OrdinalIgnoreCase) || path.StartsWith("romfs/world/data/item/itemdata/", StringComparison.OrdinalIgnoreCase)) ? "sv"
            : null;
        if (family is not null)
        {
            if (game is not null && Family(game) != family)
                throw new MergeInputException(MergeWorkspaceErrorCodes.MixedGames, "The game title folder and file contents belong to different game families.");
            game ??= family;
            evidence.Add("paths");
        }
        if (source.Game is not null)
        {
            if (source.Game is not ("sword" or "shield" or "scarlet" or "violet" or "za"))
                throw new MergeInputException(MergeWorkspaceErrorCodes.RequestInvalid, "Choose a supported source game.");
            if (game is not null && !Compatible(game, source.Game))
                throw new MergeInputException(MergeWorkspaceErrorCodes.GameMismatch, "The selected game does not match this source.");
            game = source.Game;
            evidence.Add("userGame");
        }
        var hasRomFs = isRomFsFolder || raw.Keys.Any(path => path.Split('/').Contains("romfs", StringComparer.OrdinalIgnoreCase));
        var hasIsolated = raw.Keys.Any(path => path.Split('/').Contains("trinity-mod-manager-romfs", StringComparer.OrdinalIgnoreCase));
        var descriptor = files.ContainsKey("romfs/arc/data.trpfd");
        var layout = hasIsolated ? "trinity" : descriptor ? "standalone" : hasRomFs ? "unknown" : "trinity";
        if (!files.Keys.Any(path => path.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))) layout = "independent";
        evidence.Add(descriptor ? "descriptor" : hasRomFs ? "ambiguousLayout" : "directLayout");
        if (source.Layout is not null)
        {
            if (source.Layout is not ("standalone" or "trinity" or "bypass" or "independent"))
                throw new MergeInputException(MergeWorkspaceErrorCodes.RequestInvalid, "Choose a supported source layout.");
            layout = source.Layout;
            evidence.Add("userLayout");
        }
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in raw.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            fingerprint.AppendData(System.Text.Encoding.UTF8.GetBytes(entry.Key + "\n" + entry.Value.Hash + "\n"));
        return new MergeInput(new(source.Id, Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar)), game, layout, files.Count, evidence), files,
            Convert.ToHexStringLower(fingerprint.GetHashAndReset()));
    }

    internal static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var visited = 0;
        while (pending.TryPop(out var folder))
        {
            RejectLinks(folder.Path);
            if (folder.Depth > 128 || ++visited > MaximumFiles)
                throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "The folder tree exceeds the supported depth or entry count.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder.Path))
            {
                RejectLinks(entry);
                if (Directory.Exists(entry)) pending.Push((entry, folder.Depth + 1));
                else yield return entry;
            }
        }
    }

    private static void Add(string path, Stream stream, long size, Dictionary<string, MergeInputFile> files, ref long remaining)
    {
        path = SafePath(path);
        if (files.Count >= MaximumFiles || size < 0 || size > OutputLimits.MaximumWriteBytesPerMutation || size > remaining)
            throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "The source exceeds the supported merge size. Split the package into smaller groups.");
        using var contents = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            if (read > remaining || contents.Length + read > OutputLimits.MaximumWriteBytesPerMutation)
                throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "An expanded file exceeds the supported merge size.");
            remaining -= read;
            contents.Write(buffer, 0, read);
        }
        var bytes = contents.ToArray();
        var file = new MergeInputFile(path, bytes, Convert.ToHexString(SHA256.HashData(bytes)));
        if (files.TryGetValue(path, out var duplicate) && (duplicate.Hash != file.Hash || duplicate.Path != path))
            throw new MergeInputException(MergeWorkspaceErrorCodes.DuplicatePath, "The archive contains different files with the same path.");
        files[path] = file;
    }

    internal static string SafePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(normalized) || segments.Any(segment =>
                segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || DeviceName().IsMatch(segment)))
            throw new MergeInputException(MergeWorkspaceErrorCodes.PathUnsafe, "A source contains an unsafe or ambiguous file path.");
        return normalized;
    }

    internal static void RejectLinks(string path)
    {
        for (var cursor = Path.GetFullPath(path); !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new MergeInputException(MergeWorkspaceErrorCodes.PathUnsafe, "Merge inputs and output must use physical folders and files.");
    }

    private static string NormalizePackagePath(string path)
    {
        var segments = path.Split('/');
        var root = Array.FindIndex(segments, segment => segment.Equals("romfs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("exefs", StringComparison.OrdinalIgnoreCase) || segment.Equals("trinity-mod-manager-romfs", StringComparison.OrdinalIgnoreCase));
        if (root >= 0) return (segments[root].Equals("exefs", StringComparison.OrdinalIgnoreCase) ? "exefs/" : "romfs/") + string.Join('/', segments[(root + 1)..]);
        // Direct Trinity packages contain virtual RomFS paths. Metadata and miscellaneous attachments retain their paths.
        return segments[0].ToLowerInvariant() is "arc" or "bin" or "world" or "avalon" or "ik_pokemon" or "ik_message" or "ik_event"
            or "message" or "param_ai" or "audio" or "system" or "system_resource" or "demo" or "event" ? "romfs/" + path : path;
    }

    private static bool IsRoot(string segment) => segment.ToLowerInvariant() is "romfs" or "exefs" or "trinity-mod-manager-romfs"
        or "arc" or "bin" or "world" or "avalon" or "ik_pokemon" or "ik_message" or "ik_event" or "message" or "param_ai"
        or "audio" or "system" or "system_resource" or "demo" or "event";

    internal static string? DetectNpdm(byte[] bytes)
    {
        if (bytes.Length < 0x80 || !bytes.AsSpan(0, 4).SequenceEqual("META"u8)) return null;
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x70));
        if (offset > bytes.Length - 0x18 || !bytes.AsSpan((int)offset, 4).SequenceEqual("ACI0"u8)) return null;
        var id = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan((int)offset + 0x10));
        return ProjectGameMetadata.All.FirstOrDefault(info => info.TitleId == id)?.Game.ToString().ToLowerInvariant();
    }

    internal static string Family(string game) => game switch { "sword" or "shield" or "swsh" => "swsh", "scarlet" or "violet" or "sv" => "sv", "za" => "za", _ => "unknown" };
    internal static bool Compatible(string first, string second) => first == second || Family(first) == Family(second)
        && (first is "swsh" or "sv" || second is "swsh" or "sv");
    private static string? DetectTitle(string segment)
    {
        var info = ProjectGameMetadata.All.FirstOrDefault(game => game.TitleId.ToString("X16").Equals(segment, StringComparison.OrdinalIgnoreCase));
        return info?.Game.ToString().ToLowerInvariant();
    }
    [GeneratedRegex(@"^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceName();
}
