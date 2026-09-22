// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Api.ModMerger;
using KM.Formats.SV;
using KM.Formats.ZA;

namespace KM.Tools.ModMerging;

internal static class MergePackedSources
{
    // Resolve overlaps with loose paths before descriptor removal can hide a packed edit.
    // Unnamed packed members remain in the reviewed complete archive and descriptor pair.
    public static void ExpandKnownPaths(List<MergeInput> sources, string game, string? supportFolder, ref long remaining)
    {
        const string packedPath = "romfs/arc/data.trpfs";
        const string descriptorPath = "romfs/arc/data.trpfd";
        var paths = sources.SelectMany(source => source.Files.Keys)
            .Where(path => path.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase) && path != packedPath && path != descriptorPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            if (!source.Files.TryGetValue(packedPath, out var packed) || !source.Files.TryGetValue(descriptorPath, out var descriptor)) continue;
            var temporaryRoot = Path.Combine(Path.GetTempPath(), "KMEditor-merge-" + Guid.NewGuid().ToString("N"));
            MergeInputs.RejectLinks(temporaryRoot);
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "arc"));
            try
            {
                File.WriteAllBytes(Path.Combine(temporaryRoot, "arc", "data.trpfs"), packed.Bytes);
                File.WriteAllBytes(Path.Combine(temporaryRoot, "arc", "data.trpfd"), descriptor.Bytes);
                using var sv = MergeInputs.Family(game) == "sv" ? SvTrinityArchive.Open(temporaryRoot, supportFolder,
                    maximumIndexBytes: 64 * 1024 * 1024, maximumPackBytes: 256 * 1024 * 1024) : null;
                using var za = MergeInputs.Family(game) == "za" ? ZaTrinityArchive.Open(temporaryRoot, supportFolder,
                    maximumIndexBytes: 64 * 1024 * 1024, maximumPackBytes: 256 * 1024 * 1024) : null;
                var files = new Dictionary<string, MergeInputFile>(source.Files, StringComparer.OrdinalIgnoreCase);
                foreach (var path in paths)
                {
                    byte[]? bytes = null;
                    if (sv is not null && sv.TryReadFile(path[6..], 256 * 1024 * 1024, out var svBytes)) bytes = svBytes;
                    if (za is not null && za.TryReadFile(path[6..], 256 * 1024 * 1024, out var zaBytes)) bytes = zaBytes;
                    if (bytes is null) continue;
                    if (files.TryGetValue(path, out var loose))
                    {
                        if (!loose.Bytes.AsSpan().SequenceEqual(bytes))
                            throw new MergeInputException(MergeWorkspaceErrorCodes.PackedOverlap, "A source descriptor still loads packed data that disagrees with its own loose replacement. Repair that source package before merging.");
                        continue;
                    }
                    if (bytes.Length > remaining || files.Count == MergeInputs.MaximumFiles)
                        throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "Expanded packed files exceed the merge limit.");
                    remaining -= bytes.Length;
                    files[path] = new(path, bytes, Convert.ToHexString(SHA256.HashData(bytes)));
                }
                sources[index] = source with { Files = files };
            }
            catch (FileNotFoundException exception) when (exception.FileName?.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == true)
            { throw new MergeInputException(MergeWorkspaceErrorCodes.SupportRequired, "A compression support folder is required for packed input files."); }
            catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            { throw new MergeInputException(MergeWorkspaceErrorCodes.SupportRequired, "The compression support library could not be loaded."); }
            finally
            {
                // This exact private directory was created above and contains only our two staging files.
                MergeInputs.RejectLinks(temporaryRoot);
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }
}
