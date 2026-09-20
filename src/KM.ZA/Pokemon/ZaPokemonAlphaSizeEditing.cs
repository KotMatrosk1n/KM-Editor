// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.Pokemon;

internal sealed partial class ZaPokemonEditSessionService
{
    private ChangePlan CreateSizeChangePlan(ProjectPaths paths, EditSession session, ZaOutputMode mode)
    {
        using var reads = ZaWorkflowFileSource.BeginFreshReadScope(paths);
        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (!validation.IsValid) return new ChangePlan(session.Id, [], diagnostics);
        try
        {
            var project = projectWorkspaceService.Open(paths);
            var workflow = pokemonWorkflowService.Load(project);
            var ordinary = session with { PendingEdits = session.PendingEdits.Where(edit => !ZaPokemonAlphaSizeService.IsEdit(edit)).ToArray() };
            var writes = new List<PlannedFileWrite>();
            if (ordinary.HasPendingChanges)
            {
                var plan = CreateChangePlan(paths, ordinary, mode);
                diagnostics.AddRange(plan.Diagnostics);
                if (!plan.CanApply) return new ChangePlan(session.Id, [], diagnostics);
                writes.AddRange(plan.Writes);
            }
            foreach (var edit in session.PendingEdits.Where(ZaPokemonAlphaSizeService.IsEdit))
            {
                var path = edit.Field![ZaPokemonAlphaSizeService.FieldPrefix.Length..];
                var sources = ZaPokemonAlphaSizeService.Find(workflow, edit)!.EditSources;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var source in sources.Distinct().OrderBy(source => source.RelativePath).ThenBy(source => source.Layer))
                {
                    var virtualPath = source.RelativePath.StartsWith("romfs/", StringComparison.Ordinal)
                        ? source.RelativePath[6..] : source.RelativePath;
                    var bytes = source.Layer == ProjectFileLayer.Base ? fileSource.ReadBase(project, virtualPath).Bytes
                        : fileSource.Read(project, virtualPath).Bytes;
                    AppendFingerprintBytes(hash, source.RelativePath, bytes);
                }
                AppendFingerprintBytes(hash, "current", fileSource.Read(project, path).Bytes);
                AppendFingerprintText(hash, edit.NewValue!);
                AppendFingerprintTarget(hash, paths, "output", path, mode);
                if (mode == ZaOutputMode.Standalone)
                    AppendFingerprintTarget(hash, paths, "descriptor", ZaWorkflowFileSource.DescriptorVirtualPath, mode);
                var info = ZaWorkflowFileSource.CreatePlannedWrite(paths, path, sources, mode);
                writes.Add(new PlannedFileWrite(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput,
                    "Apply Pokemon alpha size.", Convert.ToHexString(hash.GetHashAndReset())));
            }
            if (mode == ZaOutputMode.Standalone)
            {
                var info = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                if (writes.All(write => write.TargetRelativePath != info.TargetRelativePath))
                    writes.Add(new PlannedFileWrite(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput,
                        "Update the output resource descriptor."));
            }
            return new ChangePlan(session.Id, writes, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            diagnostics.Add(ZaPokemonAlphaSizeService.Error(ZaPokemonAlphaSizeService.SourceUnavailable, null));
            return new ChangePlan(session.Id, [], diagnostics);
        }
    }

    private ApplyResult ApplySizeChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed, ZaOutputMode mode)
    {
        using var outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
        var current = CreateChangePlan(paths, session, mode);
        var diagnostics = current.Diagnostics.ToList();
        var written = new List<ProjectFileReference>();
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        OutputApplyResult? transaction = null;
        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewed, current))
            diagnostics.Add(ZaPokemonAlphaSizeService.Error(ZaPokemonAlphaSizeService.PlanStale, null));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, current, written, diagnostics);
        using var batch = ZaWorkflowFileSource.HasActiveDeferredOutputBatch ? null
            : ZaWorkflowFileSource.BeginDeferredOutputBatch(paths, mode, current, new ZaOutputApplyContext(
                OutputReviewFingerprint.FromChangePlan(current), new OwnershipOwnerId("workflow.za.output"),
                [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, ZaEditSessionSupport.PokemonDomain)])
                { HistoryDetails = OutputHistoryDetails.Capture(session.PendingEdits) });
        try
        {
            var ordinary = session with { PendingEdits = session.PendingEdits.Where(edit => !ZaPokemonAlphaSizeService.IsEdit(edit)).ToArray() };
            ZaWorkflowFileSource.ApplyHybridMixedBatch(paths, mode, false, () =>
            {
                var project = projectWorkspaceService.Open(paths);
                var writes = session.PendingEdits.Where(ZaPokemonAlphaSizeService.IsEdit).Select(edit =>
                {
                    var path = edit.Field![ZaPokemonAlphaSizeService.FieldPrefix.Length..];
                    var document = new ZaPokemonSizeDocument(fileSource.Read(project, path).Bytes);
                    var value = float.Parse(edit.NewValue!, CultureInfo.InvariantCulture);
                    written.Add(ZaEditSessionSupport.GeneratedReference(path, mode));
                    return new ZaWorkflowFileWrite(path, document.WriteFixed(value));
                }).ToArray();
                return new ZaStandaloneMixedBatch(writes, [], []);
            }, revalidateReviewedState: () => ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                reviewed, CreateChangePlan(paths, session, mode)));
            if (ordinary.HasPendingChanges)
            {
                var plan = CreateChangePlan(paths, ordinary, mode);
                var result = ApplyChangePlan(paths, ordinary, plan, mode);
                diagnostics.AddRange(result.Diagnostics);
                if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, current, [], diagnostics);
                written.AddRange(result.WrittenFiles);
            }
            if (mode == ZaOutputMode.Standalone) written.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            transaction = batch?.Commit();
        }
        catch (Exception exception) when (!ZaEditSessionSupport.IsOutputSafetyException(exception))
        {
            written.Clear();
            diagnostics.Add(ZaPokemonAlphaSizeService.Error(ZaPokemonAlphaSizeService.ApplyFailed, null));
        }
        finally { pokemonWorkflowService.ClearMemoryCache(); }
        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, current, written.Distinct().ToArray(), diagnostics, transaction);
    }
}
