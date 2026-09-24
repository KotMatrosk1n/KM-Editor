// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KM.Api.ModMerger;
using KM.Core.ModMerging;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.SV;
using KM.Formats.ZA;

namespace KM.Tools.ModMerging;

public sealed class MergeWorkspaceService
{
    private const string PackedData = "romfs/arc/data.trpfs";
    private const string Descriptor = "romfs/arc/data.trpfd";
    private static readonly OwnershipOwnerId Owner = new("workflow.mod-merger");
    private sealed record Prepared(MergeWorkspaceResult Result, ProjectPaths? Paths, IReadOnlyList<OutputMutation> Mutations, OutputStateRevision? InventoryRevision, OutputDirectoryMembershipSnapshot? RomFsMembership);

    public MergeWorkspaceResult Analyze(MergeWorkspaceRequest request) => Prepare(request).Result;

    public MergeWorkspaceResult Export(MergeWorkspaceRequest request)
    {
        var prepared = Prepare(request);
        if (!prepared.Result.CanExport) return prepared.Result;
        if (string.IsNullOrEmpty(request.ReviewToken) || request.ReviewToken != prepared.Result.ReviewToken)
            return Error(prepared.Result, MergeWorkspaceErrorCodes.ReviewStale, "The sources, choices or output changed. Review the merge again.");
        if (prepared.Mutations.Count == 0) return prepared.Result with { CanExport = false, WrittenFiles = [] };
        var paths = prepared.Paths!;
        var coordinator = OutputTransactionCoordinator.ForProject(paths, new OutputTransactionCoordinatorOptions
        {
            MaximumMutationsPerApply = OutputLimits.MaximumMutationsPerApply,
            MaximumWriteBytesPerMutation = OutputLimits.MaximumWriteBytesPerMutation,
            MaximumWriteBytesPerApply = OutputLimits.MaximumWriteBytesPerApply,
            MaximumBackupBytesPerApply = OutputLimits.MaximumBackupBytesPerApply,
        });
        var plan = new OutputApplyPlan(ProjectIdentity.FromPaths(paths), paths.SelectedGame!.Value.ToGameFamily(),
            "mod-merger." + request.OutputMode, request.ReviewToken,
            [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, Owner.Value)], prepared.Mutations, directoryMembershipDependencies: prepared.RomFsMembership is { } membership ? [new(membership.Directory, membership.Revision)] : [], ownershipInventoryRevision: prepared.InventoryRevision);
        var applied = coordinator.ApplyAsync(plan).GetAwaiter().GetResult();
        if (applied.Outcome != OutputApplyOutcome.Committed)
            return Error(prepared.Result, MergeWorkspaceErrorCodes.ExportFailed, "The merge could not be committed. Check output recovery before exporting again.");
        return prepared.Result with { CanExport = false, WrittenFiles = prepared.Mutations.Select(mutation => mutation.Path.Value).ToArray() };
    }

    private static Prepared Prepare(MergeWorkspaceRequest request)
    {
        Validate(request);
        var issues = new List<MergeIssueDto>();
        var conflicts = new List<MergeConflictDto>();
        long conflictCharacters = 0;
        var files = new List<MergeFileDto>();
        var inputs = new List<MergeInput>();
        var remaining = MergeInputs.MaximumBytes;
        var inputFiles = 0;
        foreach (var source in request.Sources)
        {
            inputs.AddRange(MergePackages.ReadSelected(source, ref remaining, out var sourceFiles));
            inputFiles += sourceFiles;
            if (inputFiles > MergeInputs.MaximumFiles)
                throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "The combined sources contain too many files.");
        }
        if (inputs.Count > 64)
            throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "Select at most 64 packages across the mod sources.");
        if (inputs.Sum(input => input.Files.Count) > MergeInputs.MaximumFiles)
            throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "The combined sources contain too many files.");
        MergeExecutableInputs.NormalizeBuildNames(inputs);
        if (inputs.SelectMany(input => input.Files.Keys).GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Distinct(StringComparer.Ordinal).Count() > 1))
            throw new MergeInputException(MergeWorkspaceErrorCodes.DuplicatePath, "Source paths differ only by letter case. Resolve their intended game path before merging.");
        var game = request.Game;
        foreach (var input in inputs)
        {
            if (input.Info.Game is { } detected)
            {
                if (game is not null && !MergeInputs.Compatible(game, detected))
                    issues.Add(new(MergeWorkspaceErrorCodes.GameMismatch, "error", "These sources target different games."));
                else if (game is null || game is "swsh" or "sv") game = detected;
            }
        }
        if (game is null or "swsh" or "sv")
            issues.Add(new(MergeWorkspaceErrorCodes.GameUnknown, "error", "Choose the target game. These files do not identify an exact edition."));
        if (request.Mode == "advanced" && !Directory.Exists(request.BaseRomFs))
            issues.Add(new(MergeWorkspaceErrorCodes.VanillaRequired, "error", "Advanced mode requires the original RomFS folder."));
        if (MergeInputs.Family(game ?? "") == "swsh" && request.OutputMode != "standalone")
            issues.Add(new(MergeWorkspaceErrorCodes.OutputUnavailable, "error", "Sword and Shield use Standalone output."));

        if (game is not null && MergeInputs.Family(game) is "sv" or "za")
            MergePackedSources.ExpandKnownPaths(inputs, game, request.SupportFolder, ref remaining);
        var paths = game is not null && Enum.TryParse<ProjectGame>(game, true, out var selected)
            ? new ProjectPaths(null, null, Path.GetFullPath(request.OutputRoot), null, selected) : null;
        var inventory = paths is not null && new OutputWorkspaceStorage(paths).HasMaterial
            ? OutputTransactionCoordinator.ForProject(paths).GetOwnershipInventorySnapshotAsync().GetAwaiter().GetResult() : null;
        var ownedFiles = inventory?.Inventory.Files.ToArray() ?? [];
        var romFsMembership = paths is not null && request.OutputMode == "standalone" && MergeInputs.Family(game!) is "sv" or "za"
            ? ReadOnlyOutputDirectoryMembership.Capture(paths.OutputRootPath!, new RelativeOutputPath("romfs")) : null;
        var output = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var resolutions = request.Choices.ToDictionary(choice => choice.ConflictId, choice => choice.SourceId, StringComparer.Ordinal);
        using var vanilla = new MergeVanilla(request, game ?? "unknown");
        MergeExecutableInputs.ExpandImagePatches(inputs, vanilla, issues);
        if (request.Mode == "advanced" && vanilla.Read("exefs/main.npdm") is { } npdm
            && MergeInputs.DetectNpdm(npdm) is { } baseGame && game is not null && !MergeInputs.Compatible(game, baseGame))
            issues.Add(new(MergeWorkspaceErrorCodes.GameMismatch, "error", "The original game dump belongs to a different game."));
        var packedInputs = inputs.Where(input => input.Files.ContainsKey(PackedData)).ToArray();
        byte[]? packedDescriptor = null;
        if (packedInputs.Length > 0)
        {
            if (request.OutputMode != "standalone")
                issues.Add(new(MergeWorkspaceErrorCodes.PackedLayout, "error", "Packed Trinity archives require Standalone output. Export loose files from the source tool to convert this package."));
            if (packedInputs.Any(input => !input.Files.ContainsKey(Descriptor)))
                issues.Add(new(MergeWorkspaceErrorCodes.DescriptorRequired, "error", "Each packed archive must include its matching descriptor."));
            else
            {
                var values = packedInputs.Select(input => new MergeValueDto(input.Info.Id, input.Info.Name,
                    input.Files[PackedData].Hash + " / " + input.Files[Descriptor].Hash)).ToArray();
                var choice = values.Select(value => value.Value).Distinct().Count() == 1 ? values[0].SourceId
                    : AddConflict(PackedData, "Packed archive and descriptor", "package", null, values);
                var selectedPack = packedInputs.FirstOrDefault(input => input.Info.Id == choice) ?? packedInputs[0];
                output[PackedData] = selectedPack.Files[PackedData].Bytes;
                packedDescriptor = selectedPack.Files[Descriptor].Bytes;
                files.Add(new(PackedData, "package", choice is null ? "conflict" : "combined", values.Length > 1 ? 1 : 0, output[PackedData].Length));
            }

        }
        foreach (var path in inputs.SelectMany(input => input.Files.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            if (path.Equals(PackedData, StringComparison.OrdinalIgnoreCase) && packedInputs.Length > 0) continue;
            if (path.Equals(Descriptor, StringComparison.OrdinalIgnoreCase) && MergeInputs.Family(game ?? "") is "sv" or "za") continue;
            var candidates = inputs.Where(input => input.Files.ContainsKey(path)).Select(input => (Input: input, File: input.Files[path])).ToArray();
            var original = vanilla.Read(path);
            if (request.Mode == "advanced" && original is null && candidates.Length > 1 && !MergeExecutableEdits.IsPatch(path))
                issues.Add(new(MergeWorkspaceErrorCodes.BaseMissing, "warning", "This file was not found in the original dump. Its differences require Basic mode review.", path));
            var active = original is null ? candidates : candidates.Where(candidate => !candidate.File.Bytes.AsSpan().SequenceEqual(original)).ToArray();
            if (active.Length == 0)
            {
                files.Add(new(path, "unchanged", "unchanged", 0, candidates[0].File.Bytes.Length));
                continue;
            }
            var before = conflicts.Count;
            var kind = "file";
            byte[]? merged = null;
            if (MergeExecutableEdits.IsPatch(path))
            {
                try
                {
                    kind = "executable";
                    merged = MergeExecutableEdits.CombinePatches(active.Select(candidate => new MergeExecutableEdits.Source(candidate.Input.Info.Id,
                        candidate.Input.Info.Name, candidate.File.Bytes)).ToArray(), (region, values) => AddConflict(path, region, kind, null, values));
                }
                catch (InvalidDataException)
                {
                    conflicts.RemoveRange(before, conflicts.Count - before);
                    issues.Add(new(MergeWorkspaceErrorCodes.PatchInvalid, "error", "An executable patch is malformed, exceeds the supported limits or changes the target layout.", path));
                    files.Add(new(path, "executable", "conflict", 0, 0));
                    continue;
                }
            }
            else if (active.Select(candidate => candidate.File.Hash).Distinct().Count() == 1) merged = active[0].File.Bytes;
            else
            {
                try
                {
                    if (path.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase) && active.All(candidate => candidate.File.Bytes.AsSpan().StartsWith("NSO0"u8)))
                    {
                        if (original is null)
                            issues.Add(new(MergeWorkspaceErrorCodes.ExecutableBaseRequired, "warning", "Complete executable images need their original ExeFS image to identify independent region edits.", path));
                        else
                        {
                            kind = "executable";
                            merged = MergeExecutableEdits.CombineImages(original, active.Select(candidate => new MergeExecutableEdits.Source(candidate.Input.Info.Id,
                                candidate.Input.Info.Name, candidate.File.Bytes)).ToArray(), (region, values) => AddConflict(path, region, kind, null, values));
                        }
                    }
                    if (merged is null)
                    {
                        var documents = active.Select(candidate => MergeFormats.Read(game ?? "unknown", path, candidate.File.Bytes)).ToArray();
                        var baseline = original is null ? null : MergeFormats.Read(game ?? "unknown", path, original);
                        if (documents.All(document => document is not null) && (original is null || baseline is not null))
                        {
                            if (documents.Any(document => document!.LayoutIdentity != documents[0]!.LayoutIdentity)
                                || baseline is not null && baseline.LayoutIdentity != documents[0]!.LayoutIdentity)
                                throw new InvalidDataException("Record identities or their order differ between sources.");
                            kind = documents[0]!.Kind;
                            var node = StructuralMerge.Combine(baseline?.Content, documents.Select((document, index) => new MergeCandidate(active[index].Input.Info.Id, document!.Content)).ToArray(),
                                original is not null, difference =>
                                {
                                    var values = difference.Candidates.Select(candidate => new MergeValueDto(candidate.SourceId,
                                        inputs.Single(input => input.Info.Id == candidate.SourceId).Info.Name,
                                        candidate.Exists ? DisplayValue(candidate.Value, kind, difference.Key) : "Removed")).ToArray();
                                    return AddConflict(path, difference.Key, kind, difference.OriginalExists ? DisplayValue(difference.Original, kind, difference.Key) : null, values, JsonSerializer.Serialize(difference));
                                });
                            merged = (baseline ?? documents[0]!).Write(node!);
                            var verified = MergeFormats.Read(game ?? "unknown", path, merged);
                            if (verified is null || !JsonNode.DeepEquals(verified.Content, node))
                                throw new InvalidDataException("The reconstructed file differs from the selected records.");
                        }
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException or JsonException or NotSupportedException or InvalidOperationException or System.Reflection.TargetInvocationException)
                {
                    conflicts.RemoveRange(before, conflicts.Count - before);
                    issues.Add(new(MergeWorkspaceErrorCodes.FormatOpaque, "warning", "This file needs a complete file choice because its structure cannot be preserved safely.", path));
                    merged = null;
                    kind = "file";
                }
                if (merged is null)
                {
                    if (!issues.Any(issue => issue.Code == MergeWorkspaceErrorCodes.FormatOpaque && issue.File == path))
                        issues.Add(new(MergeWorkspaceErrorCodes.FormatOpaque, "warning", "Field merging is unavailable for this file's structure. Review the complete file choice.", path));
                    var choice = AddConflict(path, "", "file", original is null ? null : Fingerprint(original),
                        active.Select(candidate => new MergeValueDto(candidate.Input.Info.Id, candidate.Input.Info.Name,
                            candidate.File.Bytes.Length + " bytes · " + candidate.File.Hash[..12])).ToArray());
                    merged = active.FirstOrDefault(candidate => candidate.Input.Info.Id == choice).File?.Bytes ?? active[0].File.Bytes;
                }
            }
            var outputPath = request.OutputMode == "trinity" && path.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase) ? path[6..] : path;
            if (!output.TryAdd(outputPath, merged))
                throw new MergeInputException(MergeWorkspaceErrorCodes.DuplicatePath, "Output layout conversion creates a duplicate file path.");
            var unresolved = conflicts.Skip(before).Any(conflict => conflict.Resolution is null);
            files.Add(new(path, kind, unresolved ? "conflict" : conflicts.Count > before ? "resolved" : "combined", conflicts.Count - before, merged.Length));
        }
        if (game is not null && MergeInputs.Family(game) is "sv" or "za")
            PrepareDescriptor(request, game, inputs, output, issues, vanilla, packedDescriptor,
                romFsMembership?.Entries.Where(entry => !entry.IsDirectory && !ownedFiles.Any(owned => owned.Path == entry.Path
                    && owned.ProjectId == ProjectIdentity.FromPaths(paths!) && owned.Claims.All(claim => claim.OwnerId == Owner)))
                    .Select(entry => entry.Path.Value).ToArray() ?? [],
                values => AddConflict(Descriptor, "Complete file", "descriptor", null, values));
        if (output.TryGetValue(Descriptor, out var descriptorBytes))
        {
            var descriptorConflicts = conflicts.Where(conflict => conflict.File == Descriptor).ToArray();
            files.Add(new(Descriptor, "descriptor", descriptorConflicts.Any(conflict => conflict.Resolution is null) ? "conflict"
                : descriptorConflicts.Length > 0 ? "resolved" : "combined", descriptorConflicts.Length, descriptorBytes.Length));
        }
        else if (inputs.Any(input => input.Files.ContainsKey(Descriptor)))
            files.Add(new(Descriptor, "descriptor", "converted", 0, 0));
        var mutations = new List<OutputMutation>();
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Hash(string value) => fingerprint.AppendData(Encoding.UTF8.GetBytes(value + "\n"));
        Hash(JsonSerializer.Serialize(request with { ReviewToken = null }));
        foreach (var input in inputs)
        {
            Hash(input.Info.Id + ":source:" + input.SourceFingerprint);
            foreach (var file in input.Files.Values.OrderBy(file => file.Path, StringComparer.Ordinal)) Hash(input.Info.Id + ":" + file.Path + ":" + file.Hash);
        }
        foreach (var original in vanilla.Observed.OrderBy(pair => pair.Key, StringComparer.Ordinal)) Hash("base:" + original.Key + ":" + Fingerprint(original.Value));
        var inventoryRevision = inventory?.Revision;
        Hash("inventory:" + (inventory?.Revision.Value ?? "none"));
        if (romFsMembership is not null) Hash("romfs-membership:" + romFsMembership.Revision.Value);
        if (paths is not null)
        {
            foreach (var owned in ownedFiles.Where(record => record.Claims.All(claim => claim.OwnerId == Owner)
                && record.ProjectId == ProjectIdentity.FromPaths(paths)))
            {
                if (output.ContainsKey(owned.Path.Value)) continue;
                var stalePath = Path.Combine(paths.OutputRootPath!, owned.Path.Value.Replace('/', Path.DirectorySeparatorChar));
                MergeInputs.RejectLinks(stalePath);
                var current = File.Exists(stalePath) ? Capture(stalePath) : OutputFileState.Missing;
                Hash("previous:" + owned.Path.Value + ":" + JsonSerializer.Serialize(current));
                if (!current.Exists) continue;
                if (current != owned.CurrentState || !owned.FileDeleteEligible)
                    issues.Add(new(MergeWorkspaceErrorCodes.OutputConflict, "error", "A previous output was modified outside this merge. Use a separate output folder or restore that file.", owned.Path.Value));
                else
                {
                    mutations.Add(OutputMutation.Delete(owned.Path, current, owned.Claims));
                    files.Add(new(owned.Path.Value, "file", "removed", 0, current.LengthBytes));
                }
            }
            foreach (var pair in output)
            {
                var target = Path.Combine(paths.OutputRootPath!, MergeInputs.SafePath(pair.Key).Replace('/', Path.DirectorySeparatorChar));
                MergeInputs.RejectLinks(target);
                if (Directory.Exists(target)) throw new MergeInputException(MergeWorkspaceErrorCodes.OutputUnavailable, "An output file path is occupied by a folder.");
                var preimage = File.Exists(target) ? Capture(target) : OutputFileState.Missing;
                Hash(pair.Key + ":" + JsonSerializer.Serialize(preimage) + ":" + Fingerprint(pair.Value));
                if (preimage.Exists && preimage.Sha256 == Fingerprint(pair.Value)) continue;
                if (preimage.Exists && !ownedFiles.Any(record => record.Path.Value.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)
                    && record.ProjectId == ProjectIdentity.FromPaths(paths) && record.Claims.All(claim => claim.OwnerId == Owner) && record.CurrentState == preimage))
                    issues.Add(new(MergeWorkspaceErrorCodes.OutputConflict, "error", "An existing output file is not an unchanged result of this merger. Choose a separate output folder.", pair.Key));
                var relative = new RelativeOutputPath(pair.Key);
                var claim = new OwnedTarget(paths.SelectedGame!.Value.ToGameFamily(), new OwnedTargetAddress(relative), Owner,
                    new PreservationRuleDescriptor("structural-mod-merge", 1, preservesUnownedData: true, requiresPreimage: true));
                mutations.Add(OutputMutation.Write(relative, pair.Value, preimage, [claim]));
            }
        }
        if (mutations.Count > OutputLimits.MaximumMutationsPerApply || output.Values.Sum(value => (long)value.Length) > OutputLimits.MaximumWriteBytesPerApply)
            issues.Add(new(MergeWorkspaceErrorCodes.LimitExceeded, "error", "This output exceeds the supported transaction size. Split the package into smaller groups."));
        var token = Convert.ToHexStringLower(fingerprint.GetHashAndReset());
        var ready = paths is not null && (output.Count != 0 || mutations.Count != 0) && !issues.Any(issue => issue.Severity == "error") && conflicts.All(conflict => conflict.Resolution is not null);
        return new(new(game, request.Mode, request.OutputMode, token, ready, inputs.Select(input => input.Info).ToArray(), files, conflicts, issues, [], paths is null ? null : ProjectIdentity.FromPaths(paths).Value), paths, mutations, inventoryRevision, romFsMembership);

        string? AddConflict(string path, string key, string kind, string? original, IReadOnlyList<MergeValueDto> values, string? identity = null)
        {
            conflictCharacters += (original?.Length ?? 0) + values.Sum(value => (long)value.Value.Length + value.SourceName.Length) + path.Length + key.Length;
            if (conflicts.Count >= 100_000 || conflictCharacters > 12 * 1024 * 1024)
                throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "The conflict review exceeds the supported size. Merge fewer sources at once.");
            var id = Fingerprint(Encoding.UTF8.GetBytes(path + "\n" + key + "\n" + (identity ?? JsonSerializer.Serialize(values))));
            resolutions.TryGetValue(id, out var choice);
            if (!values.Any(value => value.SourceId == choice)) choice = null;
            conflicts.Add(new(id, path, key.Length == 0 ? "Complete file" : key.TrimStart('/').Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal).Replace("/", " → ", StringComparison.Ordinal),
                kind, original, values, choice));
            return choice;
        }
    }

    private static void PrepareDescriptor(MergeWorkspaceRequest request, string game, IReadOnlyList<MergeInput> inputs,
        IDictionary<string, byte[]> output, List<MergeIssueDto> issues, MergeVanilla vanilla, byte[]? packedDescriptor, IReadOnlyList<string> retainedOutputFiles,
        Func<IReadOnlyList<MergeValueDto>, string?> resolveDescriptor)
    {
        var descriptors = inputs.Where(input => input.Files.ContainsKey(Descriptor)).Select(input => input.Files[Descriptor].Bytes).ToList();
        var loose = output.Keys.Concat(retainedOutputFiles).Where(path => path.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            && !path.Equals(Descriptor, StringComparison.OrdinalIgnoreCase) && !path.Equals(PackedData, StringComparison.OrdinalIgnoreCase)).Select(path => path[6..]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (request.OutputMode != "standalone")
        {
            if (descriptors.Count > 0 && !output.Keys.Any(path => request.OutputMode == "trinity" ? !path.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase) : path.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)))
                issues.Add(new(MergeWorkspaceErrorCodes.DescriptorRequired, "error", "A descriptor without its replacement files cannot be converted to loose output."));
            return;
        }
        if (loose.Length == 0 || packedDescriptor is null && loose.All(path => IsPhysicalRomFsPath("romfs/" + path)))
        {
            if (packedDescriptor is not null) output[Descriptor] = packedDescriptor;
            else if (descriptors.Count > 0)
            {
                if (descriptors.Any(bytes => !bytes.AsSpan().SequenceEqual(descriptors[0])))
                {
                    var candidates = inputs.Where(input => input.Files.ContainsKey(Descriptor)).ToArray();
                    var choice = resolveDescriptor(candidates.Select(input => new MergeValueDto(input.Info.Id, input.Info.Name, input.Files[Descriptor].Hash)).ToArray());
                    output[Descriptor] = (candidates.FirstOrDefault(input => input.Info.Id == choice) ?? candidates[0]).Files[Descriptor].Bytes;
                }
                else output[Descriptor] = descriptors[0];
            }
            return;
        }
        var original = packedDescriptor is null ? vanilla.Read(Descriptor) : null;
        if (packedDescriptor is not null) descriptors = [packedDescriptor];
        else if (original is not null) descriptors.Add(original);
        if (descriptors.Count == 0)
        {
            issues.Add(new(MergeWorkspaceErrorCodes.DescriptorRequired, "error", "Standalone output needs a compatible descriptor from the sources or an original game dump. Choose Advanced mode or another output format."));
            return;
        }
        var hashes = loose.Select(path => MergeInputs.Family(game) == "za" ? ZaTrinityPathHasher.HashPath(path) : SvTrinityPathHasher.HashPath(path)).ToHashSet();
        byte[][] patched;
        try
        {
            // Canonical known routing data is compared only. It is never used as the output payload.
            patched = descriptors.Select(bytes => MergeInputs.Family(game) == "za"
                ? ZaTrinityDescriptorPatcher.RemoveFileHashes(bytes, hashes) : SvTrinityDescriptorPatcher.RemoveFileHashes(bytes, hashes)).ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException or System.Reflection.TargetInvocationException)
        {
            issues.Add(new(MergeWorkspaceErrorCodes.DescriptorMismatch, "error", "The descriptor contains unsupported data and cannot be safely reconstructed."));
            return;
        }
        if (patched.Any(bytes => !bytes.AsSpan().SequenceEqual(patched[0])))
        {
            issues.Add(new(MergeWorkspaceErrorCodes.DescriptorMismatch, "error", "The source descriptors do not agree. Use Advanced mode with the matching original game version."));
            return;
        }
        var selected = descriptors[0];
        var sourceDescriptors = inputs.Where(input => input.Files.ContainsKey(Descriptor)).ToArray();
        if (packedDescriptor is null && sourceDescriptors.Length > 1)
        {
            var preserving = sourceDescriptors.Select(input => KM.Formats.TrinityMergeDescriptor.RemoveFileHashes(input.Files[Descriptor].Bytes, hashes)).ToArray();
            if (preserving.Any(bytes => !bytes.AsSpan().SequenceEqual(preserving[0]))
                && !preserving.All(KM.Formats.TrinityMergeDescriptor.HasOnlyKnownFields))
            {
                var choice = resolveDescriptor(sourceDescriptors.Select((input, index) =>
                    new MergeValueDto(input.Info.Id, input.Info.Name, Fingerprint(preserving[index]))).ToArray());
                selected = sourceDescriptors.FirstOrDefault(input => input.Info.Id == choice)?.Files[Descriptor].Bytes ?? selected;
            }
        }
        output[Descriptor] = KM.Formats.TrinityMergeDescriptor.RemoveFileHashes(selected, hashes);
        var verified = MergeInputs.Family(game) == "za" ? ZaTrinityDescriptorPatcher.RemoveFileHashes(output[Descriptor], new HashSet<ulong>())
            : SvTrinityDescriptorPatcher.RemoveFileHashes(output[Descriptor], new HashSet<ulong>());
        if (!verified.AsSpan().SequenceEqual(patched[0]))
            issues.Add(new(MergeWorkspaceErrorCodes.DescriptorMismatch, "error", "The preserved descriptor does not match the reviewed routing entries."));
    }

    private static void Validate(MergeWorkspaceRequest request)
    {
        if (request.Sources is null || request.Choices is null || request.Mode is not ("basic" or "advanced") || request.OutputMode is not ("standalone" or "trinity" or "bypass")
            || request.Sources.Count is < 1 or > 64 || request.Choices.Count > 100_000 || string.IsNullOrWhiteSpace(request.OutputRoot)
            || request.Sources.Any(source => source is null || string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.Path))
            || request.Sources.Select(source => source.Id).Distinct().Count() != request.Sources.Count
            || request.Choices.Any(choice => choice is null || string.IsNullOrWhiteSpace(choice.ConflictId) || string.IsNullOrWhiteSpace(choice.SourceId))
            || request.Choices.Select(choice => choice.ConflictId).Distinct().Count() != request.Choices.Count
            || request.Game is not (null or "sword" or "shield" or "scarlet" or "violet" or "za"))
            throw new MergeInputException(MergeWorkspaceErrorCodes.RequestInvalid, "Choose valid merger inputs, mode, game and output.");
        var target = Path.GetFullPath(request.OutputRoot);
        MergeInputs.RejectLinks(target);
        foreach (var source in request.Sources.Select(source => source.Path).Concat(new[] { request.BaseRomFs, MergeVanilla.ResolveExeFsRoot(request) }.OfType<string>()).Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var input = Path.GetFullPath(source);
            if (Overlaps(input, target)) throw new MergeInputException(MergeWorkspaceErrorCodes.PathUnsafe, "The output must be separate from every mod source and original game folder.");
        }
    }

    private static bool IsPhysicalRomFsPath(string path) => path.StartsWith("romfs/audio/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("romfs/system/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("romfs/demo/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("romfs/event/", StringComparison.OrdinalIgnoreCase);

    private static bool Overlaps(string first, string second) => first.Equals(second, StringComparison.OrdinalIgnoreCase)
        || first.StartsWith(second.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || second.StartsWith(first.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static OutputFileState Capture(string path) { using var stream = File.OpenRead(path); return OutputFileState.Existing(Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length); }
    private static string Fingerprint(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string DisplayValue(JsonNode? value, string kind, string key)
    {
        if (value is not null && (kind == "archive-members" || kind == "archive-fields" && key.Split('/').Length == 3 || key.EndsWith("/UnrecognizedFields", StringComparison.Ordinal)))
        {
            var text = value.ToJsonString();
            return "SHA-256 " + Fingerprint(Encoding.UTF8.GetBytes(text));
        }
        return Display(value);
    }
    private static string Display(JsonNode? node) => node?.ToJsonString() ?? "null";
    private static MergeWorkspaceResult Error(MergeWorkspaceResult result, string code, string message) => result with
    { CanExport = false, Issues = [.. result.Issues, new(code, "error", message)] };
}
