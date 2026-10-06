// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;

namespace KM.SwSh.Randomizer;

public sealed partial class SwShRandomizerService
{
    private RandomizerDomainPlan? CreateValueRestorePlan(ProjectPaths paths, string domain,
        IReadOnlyList<RandomizerValueOwnership> ownership, ICollection<ValidationDiagnostic> diagnostics)
    {
        var current = ReadRandomizerValues(paths, [domain], diagnostics);
        var edits = new List<PendingEdit>();
        var chart = domain == "Type Chart" ? current.Values.Select(value => value.Values[0]).ToArray() : [];
        var chartChanged = false;
        foreach (var owned in ownership)
        {
            var path = ResolveOutputPath(paths, owned.Path)
                ?? throw new InvalidDataException("Randomizer value path is not a physical output path.");
            // Do not resurrect a later user deletion, including Gift's primary source.
            if (!File.Exists(path)) continue;
            if (!current.TryGetValue(ValueKey(domain, owned.Key), out var value)
                || value.Path != owned.Path || !value.Values.SequenceEqual(owned.After)) continue;
            if (!owned.Key.EndsWith("/learnset", StringComparison.Ordinal) && owned.Before.Length != value.Values.Length)
                throw new InvalidDataException("Randomizer value shape does not match the current record.");
            if (domain == "Type Chart")
            {
                var index = current.Values.ToList().FindIndex(cell => cell.Key == owned.Key);
                chart[index] = owned.Before[0];
                chartChanged = true;
            }
            else edits.AddRange(value.MakeEdits(owned.Before));
        }
        var session = CreateSession(edits);
        if (domain == "Type Chart" && chartChanged)
        {
            var staged = typeChartEditSessionService.StageChart(paths, chart.Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray(), null);
            foreach (var diagnostic in staged.Diagnostics) diagnostics.Add(diagnostic);
            session = staged.Session;
        }
        if (session.PendingEdits.Count == 0) return null;
        return domain switch
        {
            "Pokemon" => new(domain, session, pokemonEditSessionService.CreateChangePlan, pokemonEditSessionService.ApplyChangePlan),
            "Wild Encounters" => new(domain, session, encountersEditSessionService.CreateChangePlan, encountersEditSessionService.ApplyChangePlan),
            "Static Encounters" => new(domain, session, staticEncountersEditSessionService.CreateChangePlan, staticEncountersEditSessionService.ApplyChangePlan),
            "Gift Encounters" => new(domain, session, giftPokemonEditSessionService.CreateChangePlan, giftPokemonEditSessionService.ApplyChangePlan),
            "Raid Rewards" or "Raid Bonus Rewards" => new(domain, session, raidRewardsEditSessionService.CreateChangePlan, raidRewardsEditSessionService.ApplyChangePlan),
            "Type Chart" => new(domain, session, typeChartEditSessionService.CreateChangePlan, typeChartEditSessionService.ApplyChangePlan),
            _ => throw new InvalidDataException("Unsupported Randomizer restore domain."),
        };
    }

    private ApplyResult RestoreRandomizerValues(ProjectPaths paths, RandomizerRestoreManifest manifest, RestoreFilePreimage manifestPreimage)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        string? stagingRoot = null;
        try
        {
            var ownership = ValidateRandomizerValues(manifest);
            var entries = CreateRestoreEntries(manifest);
            if (entries.Count > SwShOutputTransactionWriter.MaximumFilesPerTransaction
                || entries.Any(entry => !IsLayeredFsRelativePath(entry.RelativePath)))
                throw new InvalidDataException("Randomizer restore targets are invalid or exceed the transaction bound.");
            var targets = entries.Select(entry => entry.RelativePath).ToHashSet(StringComparer.Ordinal);
            if (ownership.Any(value => !targets.Contains(value.Path)))
                throw new InvalidDataException("Randomizer value ownership has no matching tracked file.");

            var groups = ownership.GroupBy(value => value.Domain, StringComparer.Ordinal).ToArray();
            var reviewed = new List<(RandomizerDomainPlan Domain, ChangePlan Plan)>();
            foreach (var group in groups)
            {
                var domain = CreateValueRestorePlan(paths, group.Key, group.ToArray(), diagnostics);
                if (domain is null) continue;
                var plan = domain.CreateChangePlan(paths, domain.Session);
                diagnostics.AddRange(plan.Diagnostics);
                reviewed.Add((domain, plan));
            }
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return CreateApplyResult(diagnostics);

            // Reuse the ordinary editors in a private overlay, then publish all resulting
            // files and ownership cleanup in one transaction against the original preimages.
            var dependencies = reviewed.SelectMany(item => item.Plan.Writes)
                .SelectMany(write => write.Sources.Select(source => source.RelativePath).Append(write.TargetRelativePath))
                .Concat(targets).Where(IsLayeredFsRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var outputPreimages = new Dictionary<string, RestoreFilePreimage>(StringComparer.OrdinalIgnoreCase);
            var inputPreimages = new Dictionary<string, RestoreFilePreimage>(StringComparer.OrdinalIgnoreCase);
            stagingRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KMEditor-randomizer-restore-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(stagingRoot);
            foreach (var relative in dependencies)
            {
                var source = ResolveOutputPath(paths, relative) ?? throw new IOException("Randomizer source is not physically contained by Output Root.");
                var preimage = CaptureRestoreFilePreimage(source);
                if (preimage.Kind == RestoreFileKind.Directory) throw new IOException("A Randomizer file target is occupied by a directory.");
                outputPreimages[relative] = preimage;
                if (preimage.Kind == RestoreFileKind.File)
                {
                    var destination = Path.Combine(stagingRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination);
                    if (CaptureRestoreFilePreimage(destination) != preimage) throw new IOException("Randomizer source changed while preparing restore.");
                }
                // Base may also participate in validation when a layered source exists.
                {
                    var baseRoot = relative.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase) ? paths.BaseRomFsPath : paths.BaseExeFsPath;
                    if (!string.IsNullOrWhiteSpace(baseRoot))
                    {
                        var baseFile = Path.Combine(baseRoot, relative[6..]);
                        inputPreimages[baseFile] = CaptureRestoreFilePreimage(baseFile);
                    }
                }
            }

            var stagedPaths = paths with { OutputRootPath = stagingRoot };
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                // Earlier domain writes may have changed a shared archive's revision.
                var domain = CreateValueRestorePlan(stagedPaths, group.Key, group.ToArray(), diagnostics);
                if (domain is null) continue;
                var plan = domain.CreateChangePlan(stagedPaths, domain.Session);
                diagnostics.AddRange(plan.Diagnostics);
                if (!plan.CanApply || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return CreateApplyResult(diagnostics);
                if (plan.Writes.Any(write => !outputPreimages.ContainsKey(write.TargetRelativePath)))
                    throw new IOException("Randomizer restore targets changed during preparation.");
                var applied = domain.ApplyChangePlan(stagedPaths, domain.Session, plan);
                diagnostics.AddRange(applied.Diagnostics);
                if (applied.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return CreateApplyResult(diagnostics);
                foreach (var file in applied.WrittenFiles) written.Add(file.RelativePath);
            }

            var mutations = new List<SwShOutputFileMutation>();
            foreach (var relative in written.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                RestoreMutationHook?.Invoke(SwShRandomizerRestoreMutationStage.BeforeTargetMutation, relative);
                var output = File.ReadAllBytes(Path.Combine(stagingRoot, relative));
                var entry = entries.FirstOrDefault(entry => entry.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase));
                var baseRoot = relative.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase) ? paths.BaseRomFsPath : paths.BaseExeFsPath;
                var basePath = string.IsNullOrWhiteSpace(baseRoot) ? null : Path.Combine(baseRoot, relative[6..]);
                var isBase = entry?.OriginalExisted == false && basePath is not null && File.Exists(basePath)
                    && output.AsSpan().SequenceEqual(File.ReadAllBytes(basePath));
                mutations.Add(isBase
                    ? SwShOutputFileMutation.DeleteLegacyAdoption(relative, ToOutputFileState(outputPreimages[relative]))
                    : relative.Equals("exefs/main", StringComparison.OrdinalIgnoreCase)
                        ? SwShOutputFileMutation.WriteComposed(relative, output, ToOutputFileState(outputPreimages[relative]))
                        : SwShOutputFileMutation.Write(relative, output, ToOutputFileState(outputPreimages[relative])));
            }

            RestoreMutationHook?.Invoke(SwShRandomizerRestoreMutationStage.BeforeManifestMutation, RandomizerManifestRelativePath);
            var manifestPath = ResolveOutputPath(paths, RandomizerManifestRelativePath) ?? throw new IOException("Randomizer manifest path changed.");
            EnsureRestoreMutationTarget(paths, RandomizerManifestRelativePath, manifestPath, manifestPreimage);
            mutations.Add(SwShOutputFileMutation.DeleteLegacyAdoption(RandomizerManifestRelativePath, ToOutputFileState(manifestPreimage)));
            foreach (var entry in entries.Where(entry => entry.OriginalExisted == true))
            {
                var backup = PrepareRequiredRandomizerBackup(paths, entry, entry.RelativePath, diagnostics);
                if (backup is null) return CreateApplyResult(diagnostics);
                RestoreMutationHook?.Invoke(SwShRandomizerRestoreMutationStage.BeforeBackupDeletion, backup.RelativePath);
                EnsureRestoreMutationTarget(paths, backup.RelativePath, backup.Path, backup.Preimage);
                mutations.Add(SwShOutputFileMutation.DeleteLegacyAdoption(backup.RelativePath, ToOutputFileState(backup.Preimage)));
            }
            foreach (var (relative, preimage) in outputPreimages)
                EnsureRestoreMutationTarget(paths, relative, ResolveOutputPath(paths, relative)!, preimage);
            foreach (var (file, preimage) in inputPreimages)
                if (CaptureRestoreFilePreimage(file) != preimage) throw new IOException("Base data changed during Randomizer restore.");
            if (!SwShOutputTransactionWriter.TryApply(paths, mutations, "workflow.sword-shield.randomizer-restore-values",
                    out var transaction, out var failure))
            {
                diagnostics.Add(CreateOutputTransactionDiagnostic("Randomizer selective restore failed", failure));
                return CreateApplyResult(diagnostics);
            }
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Info,
                "Restored values still owned by Randomizer and preserved later edits and deletions."));
            var id = Guid.NewGuid().ToString("N");
            var now = DateTimeOffset.UtcNow;
            return new ApplyResult(id, now, written.Select(relative => new ProjectFileReference(ProjectFileLayer.Generated, relative)).ToArray(),
                new WriteManifest(id, now, []), CollapseDiagnostics(diagnostics), transaction);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or FormatException or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, "Randomizer selective restore could not be prepared: " + exception.Message));
            return CreateApplyResult(diagnostics);
        }
        finally
        {
            if (stagingRoot is not null && Path.GetDirectoryName(stagingRoot) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                && Path.GetFileName(stagingRoot).StartsWith("KMEditor-randomizer-restore-", StringComparison.Ordinal))
            {
                try { Directory.Delete(stagingRoot, recursive: true); }
                catch (IOException) { /* A locked temporary overlay can be cleaned up later. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
