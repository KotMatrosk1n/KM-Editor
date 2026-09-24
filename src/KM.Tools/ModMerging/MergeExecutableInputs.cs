// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Api.ModMerger;
using KM.Formats.Executable;

namespace KM.Tools.ModMerging;

internal static class MergeExecutableInputs
{
    internal static void NormalizeBuildNames(List<MergeInput> inputs)
    {
        var names = inputs.SelectMany(input => input.Files.Keys).Where(path => path.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase)
            && MergeExecutableEdits.IsPatch(path)).Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(value => value.Length is >= 16 and <= 64 && value.Length % 2 == 0 && value.All(Uri.IsHexDigit)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length > 1024) throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "Too many executable patch targets.");
        if (names.Length == 0) return;
        var complete = names.Where(value => !names.Any(other => other.Length > value.Length && other.StartsWith(value, StringComparison.OrdinalIgnoreCase))).ToArray();
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var matches = complete.Where(value => value.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new MergeInputException(MergeWorkspaceErrorCodes.PatchInvalid, "A shortened patch build identifier matches several targets.");
            replacements["exefs/" + name + ".ips"] = "exefs/" + matches[0] + ".ips";
        }
        for (var index = 0; index < inputs.Count; index++)
        {
            var source = inputs[index]; var files = new Dictionary<string, MergeInputFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in source.Files.Values)
            {
                var path = replacements.GetValueOrDefault(file.Path, file.Path); var payload = file.Bytes;
                if (files.TryGetValue(path, out var previous))
                {
                    try
                    {
                        payload = MergeExecutableEdits.CombinePatches([new("first", "", previous.Bytes), new("second", "", payload)],
                            (_, _) => throw new InvalidDataException("Patches within one source disagree at the same address."));
                    }
                    catch (InvalidDataException exception) { throw new MergeInputException(MergeWorkspaceErrorCodes.PatchInvalid, exception.Message); }
                }
                files[path] = ReferenceEquals(payload, file.Bytes) ? file with { Path = path }
                    : new(path, payload, Convert.ToHexString(SHA256.HashData(payload)));
            }
            inputs[index] = source with { Files = files };
        }
    }

    // A loose patch and a replacement image for its build must be reviewed in one address space.
    internal static void ExpandImagePatches(List<MergeInput> inputs, MergeVanilla vanilla, List<MergeIssueDto> issues)
    {
        UpgradeKnownPatches(inputs, vanilla);
        var images = inputs.SelectMany(input => input.Files.Values).Where(file => file.Path.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase)
            && file.Bytes.Length >= NsoFile.HeaderSize && file.Bytes.AsSpan().StartsWith("NSO0"u8)).ToArray();
        foreach (var patchPath in inputs.SelectMany(input => input.Files.Keys).Where(MergeExecutableEdits.IsPatch).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var build = Path.GetFileNameWithoutExtension(patchPath);
            if (build.Length is < 16 or > 64 || build.Length % 2 != 0 || !build.All(Uri.IsHexDigit)) continue;
            var targets = images.Where(image => Convert.ToHexString(image.Bytes.AsSpan(0x40, 32)).StartsWith(build, StringComparison.OrdinalIgnoreCase))
                .Select(image => image.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (targets.Length == 0) continue;
            if (targets.Length != 1)
            {
                issues.Add(new(MergeWorkspaceErrorCodes.PatchInvalid, "error", "The patch build identifier matches multiple executable images.", patchPath));
                continue;
            }
            var target = targets[0];
            var original = vanilla.Read(target);
            if (original is null)
            {
                issues.Add(new(MergeWorkspaceErrorCodes.ExecutableBaseRequired, "error", "Combining a complete executable and its patches requires the original ExeFS image.", target));
                continue;
            }
            for (var index = 0; index < inputs.Count; index++)
            {
                var source = inputs[index];
                if (!source.Files.TryGetValue(patchPath, out var patch)) continue;
                try
                {
                    var candidate = source.Files.TryGetValue(target, out var ownImage) ? ownImage.Bytes : original;
                    MergeExecutableEdits.CheckImageSize(original);
                    MergeExecutableEdits.CheckImageSize(candidate);
                    if (!NsoRegisteredRegionCompositionVerifier.HasCompatibleLayoutEnvelope(original, candidate, allowTextGrowth: true)
                        || !Convert.ToHexString(original.AsSpan(0x40, 32)).StartsWith(build, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The original image does not match the patch target.");
                    var image = NsoFile.Parse(candidate); var baseline = NsoFile.Parse(original);
                    var data = image.Segments.Select(segment => segment.DecompressedData.ToArray()).ToArray();
                    foreach (var (address, value) in MergeExecutableEdits.ReadPatch(patch.Bytes))
                    {
                        var memory = (long)address - NsoFile.HeaderSize;
                        var segment = Enumerable.Range(0, 3).FirstOrDefault(slot => memory >= image.Segments[slot].Header.MemoryOffset
                            && memory < (long)image.Segments[slot].Header.MemoryOffset + data[slot].Length, -1);
                        if (segment < 0) throw new InvalidDataException("The patch writes outside the executable segments.");
                        var offset = (int)(memory - image.Segments[segment].Header.MemoryOffset);
                        var baseData = baseline.Segments[segment].DecompressedData;
                        if ((offset >= baseData.Length || data[segment][offset] != baseData[offset]) && data[segment][offset] != value)
                            throw new InvalidDataException("A source image and its own patch disagree at the same address.");
                        data[segment][offset] = value;
                    }
                    var bytes = image.Write(data[0], data[1], data[2]);
                    var files = new Dictionary<string, MergeInputFile>(source.Files, StringComparer.OrdinalIgnoreCase);
                    files.Remove(patchPath);
                    files[target] = new(target, bytes, Convert.ToHexString(SHA256.HashData(bytes)));
                    inputs[index] = source with { Files = files };
                }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
                {
                    issues.Add(new(MergeWorkspaceErrorCodes.PatchInvalid, "error", "A patch cannot be applied to its matching executable image without losing or contradicting source data.", patchPath));
                }
            }
        }
    }

    private static void UpgradeKnownPatches(List<MergeInput> inputs, MergeVanilla vanilla)
    {
        var patchPaths = inputs.SelectMany(input => input.Files.Keys).Where(IsBuildPatch).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (patchPaths.Length == 0) return;
        var baseline = vanilla.Read("exefs/main");
        if (baseline is null) return;
        try
        {
            MergeExecutableEdits.CheckImageSize(baseline);
            var build = Convert.ToHexString(NsoFile.Parse(baseline).BuildId);
            foreach (var path in patchPaths.Where(path => build.StartsWith(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)))
            {
                for (var index = 0; index < inputs.Count; index++)
                {
                    var source = inputs[index];
                    if (!source.Files.TryGetValue(path, out var patch)) continue;
                    var upgraded = KM.SwSh.ExeFs.SwShExecutableMergeSupport.UpgradeKnownPatch(baseline, patch.Bytes);
                    if (ReferenceEquals(upgraded, patch.Bytes)) continue;
                    var files = new Dictionary<string, MergeInputFile>(source.Files, StringComparer.OrdinalIgnoreCase)
                    {
                        [path] = new(path, upgraded, Convert.ToHexString(SHA256.HashData(upgraded))),
                    };
                    inputs[index] = source with { Files = files };
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            // Unverified originals cannot authorize a patch migration. Normal merge validation still applies.
        }

        static bool IsBuildPatch(string path)
        {
            var build = Path.GetFileNameWithoutExtension(path);
            return path.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase) && MergeExecutableEdits.IsPatch(path)
                && build.Length is >= 16 and <= 64 && build.Length % 2 == 0 && build.All(Uri.IsHexDigit);
        }
    }
}
