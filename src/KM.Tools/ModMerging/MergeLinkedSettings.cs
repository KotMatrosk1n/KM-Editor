// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Buffers.Binary;
using KM.Api.ModMerger;
using KM.Formats.SwSh;
using KM.Core.ModMerging;
using KM.SwSh.ExeFs;

namespace KM.Tools.ModMerging;

internal static class MergeLinkedSettings
{
    private static readonly string[] HyperPaths = ["exefs/main", SwShExecutableMergeSupport.HyperScript, SwShExecutableMergeSupport.HyperDialogue];

    internal static MergeDocument? WithPartyCount(string path, MergeDocument? document, IReadOnlyList<MergeInput> inputs)
    {
        if (document is null || !path.StartsWith("romfs/bin/trainer/trainer_data/", StringComparison.OrdinalIgnoreCase)) return document;
        var party = path.Replace("/trainer_data/", "/trainer_poke/", StringComparison.OrdinalIgnoreCase).Replace("/trainer_data_", "/trainer_poke_", StringComparison.OrdinalIgnoreCase);
        if (!inputs.Any(input => input.Files.ContainsKey(party))) return document;
        var content = document.Content.DeepClone();
        var count = content["00000"]!["PokemonCount"]!.DeepClone();
        content["00000"]!.AsObject().Remove("PokemonCount");
        return document with { Content = content, Write = node =>
        {
            var restored = node.DeepClone(); restored["00000"]!["PokemonCount"] = count.DeepClone();
            return document.Write(restored);
        } };
    }

    internal static void PrepareHyperTraining(List<MergeInput> inputs, MergeVanilla vanilla, List<MergeIssueDto> issues,
        Func<string, string, IReadOnlyList<MergeValueDto>, string?> resolve)
    {
        var changed = new List<(MergeInput Input, int Level)>();
        int? ReadLevel(string path, byte[] bytes)
        {
            if (path == "exefs/main")
            {
                if (!bytes.AsSpan().StartsWith("NSO0"u8)) return null;
                try { MergeExecutableEdits.CheckImageSize(bytes); }
                catch (InvalidDataException)
                {
                    issues.Add(new(MergeWorkspaceErrorCodes.PatchInvalid, "error", "The executable exceeds the supported decoded size or has an invalid header.", path));
                    return null;
                }
            }
            return SwShExecutableMergeSupport.HyperLevel(path, bytes);
        }
        foreach (var input in inputs)
        {
            var levels = HyperPaths.Where(input.Files.ContainsKey).Select(path => (Path: path,
                Level: ReadLevel(path, input.Files[path].Bytes),
                Base: vanilla.Read(path) is { } original ? ReadLevel(path, original) : 100)).ToArray();
            var edits = levels.Where(value => value.Level is not null && value.Level != value.Base).Select(value => value.Level!.Value).Distinct().ToArray();
            if (edits.Length == 0) continue;
            if (edits.Length > 1 || levels.Any(value => value.Level is null))
            {
                issues.Add(new(MergeWorkspaceErrorCodes.SemanticInvalid, "error", "A source has inconsistent Hyper Training settings. Repair its executable, script and dialogue together.", "exefs/main"));
                return;
            }
            changed.Add((input, edits[0]));
        }
        if (changed.Count == 0) return;
        var selected = changed[0].Level;
        if (changed.Select(value => value.Level).Distinct().Count() > 1)
        {
            var choice = resolve("exefs/main", "Hyper Training minimum level", changed.Select(value => new MergeValueDto(value.Input.Info.Id, value.Input.Info.Name, value.Level.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());
            selected = changed.FirstOrDefault(value => value.Input.Info.Id == choice, changed[0]).Level;
        }
        foreach (var path in HyperPaths)
        {
            var supplied = inputs.Any(input => input.Files.ContainsKey(path));
            var original = vanilla.Read(path);
            if (!supplied && original is null)
            {
                if (path == SwShExecutableMergeSupport.HyperDialogue) continue;
                issues.Add(new(MergeWorkspaceErrorCodes.ExecutableBaseRequired, "error", "Hyper Training needs its original executable and script to keep the selected level consistent.", path));
                continue;
            }
            for (var index = 0; index < inputs.Count; index++)
            {
                var input = inputs[index];
                var bytes = input.Files.GetValueOrDefault(path)?.Bytes ?? (!supplied && index == 0 ? original : null);
                if (bytes is null) continue;
                try
                {
                    var patched = SwShExecutableMergeSupport.SetHyperLevel(path, bytes, selected);
                    var files = new Dictionary<string, MergeInputFile>(input.Files, StringComparer.OrdinalIgnoreCase) { [path] = new(path, patched, Convert.ToHexString(SHA256.HashData(patched))) };
                    inputs[index] = input with { Files = files };
                }
                catch (Exception e) when (e is InvalidDataException or ArgumentException or InvalidOperationException)
                {
                    issues.Add(new(MergeWorkspaceErrorCodes.SemanticInvalid, "error", "Hyper Training could not preserve the selected level in every dependent file.", path));
                }
            }
        }
    }

    internal static void CompleteTrainerParties(IReadOnlyList<MergeInput> inputs, MergeVanilla vanilla, IDictionary<string, byte[]> output,
        List<MergeFileDto> files, List<MergeIssueDto> issues)
    {
        const string root = "romfs/bin/trainer/trainer_poke/";
        foreach (var header in output.Where(p => p.Key.StartsWith("romfs/bin/trainer/trainer_data/", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var partyPath = header.Key.Replace("/trainer_data/", "/trainer_poke/", StringComparison.OrdinalIgnoreCase)
                .Replace("/trainer_data_", "/trainer_poke_", StringComparison.OrdinalIgnoreCase);
            if (output.ContainsKey(partyPath)) continue;
            var originalHeader = vanilla.Read(header.Key);
            if (originalHeader is { Length: 20 } && header.Value is { Length: 20 } && header.Value[3] == originalHeader[3]) continue;
            var originalParty = vanilla.Read(partyPath);
            if (header.Value.Length != 20 || header.Value[3] > 6 || originalParty is not null && header.Value[3] != originalParty.Length / 32) Invalid(header.Key);
        }
        SwShPersonalTable? personal = null;
        if (output.Keys.Any(path => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = output.TryGetValue(SwShPersonalTable.PersonalDataRelativePath, out var editedPersonal) ? editedPersonal : vanilla.Read(SwShPersonalTable.PersonalDataRelativePath);
            if (bytes is not null)
            {
                try { personal = SwShPersonalTable.Parse(bytes); }
                catch (InvalidDataException) { Invalid(SwShPersonalTable.PersonalDataRelativePath); }
            }
        }
        foreach (var pair in output.Where(p => p.Key.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var headerPath = pair.Key.Replace("/trainer_poke/", "/trainer_data/", StringComparison.OrdinalIgnoreCase)
                .Replace("/trainer_poke_", "/trainer_data_", StringComparison.OrdinalIgnoreCase);
            var count = pair.Value.Length / 32;
            if (pair.Value.Length % 32 != 0 || count > 6) { Invalid(pair.Key); continue; }
            if (personal is not null)
                for (var slot = 0; slot < count; slot++)
                {
                    var species = BinaryPrimitives.ReadUInt16LittleEndian(pair.Value.AsSpan(slot * 32 + 12));
                    var form = BinaryPrimitives.ReadUInt16LittleEndian(pair.Value.AsSpan(slot * 32 + 14));
                    if (species >= personal.Records.Count || form >= Math.Max(1, personal.Records[species].FormCount))
                        issues.Add(new(MergeWorkspaceErrorCodes.SemanticInvalid, "error", "The selected trainer species and form do not exist in the effective Pokemon data.", pair.Key));
                }
            foreach (var input in inputs)
            {
                if (input.Files.TryGetValue(headerPath, out var header))
                {
                    var party = input.Files.GetValueOrDefault(pair.Key)?.Bytes ?? vanilla.Read(pair.Key);
                    if (header.Bytes.Length != 20 || party is not null && header.Bytes[3] != party.Length / 32) Invalid(headerPath);
                }
            }
            var selectedHeader = output.TryGetValue(headerPath, out var mergedHeader) ? mergedHeader : vanilla.Read(headerPath);
            if (selectedHeader is null)
            {
                var lengths = inputs.Where(i => i.Files.ContainsKey(pair.Key)).Select(i => i.Files[pair.Key].Bytes.Length).Distinct().Count();
                if (lengths > 1) Invalid(headerPath);
                continue;
            }
            if (selectedHeader.Length != 20) { Invalid(headerPath); continue; }
            if (selectedHeader[3] == count) continue;
            var corrected = selectedHeader.ToArray(); corrected[3] = (byte)count; output[headerPath] = corrected;
            var existing = files.FindIndex(f => f.Path == headerPath);
            if (existing < 0) files.Add(new(headerPath, "trainers", "combined", 0, corrected.Length));
            else files[existing] = files[existing] with { Size = corrected.Length, Status = files[existing].Status == "unchanged" ? "combined" : files[existing].Status };
        }
        void Invalid(string path) => issues.Add(new(MergeWorkspaceErrorCodes.SemanticInvalid, "error", "The trainer party and Pokemon count must agree. Supply matching party and header files, or their originals.", path));
    }
}
