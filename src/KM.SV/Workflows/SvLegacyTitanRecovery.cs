// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Output;
using KM.Core.Semantics;
using System.Security.Cryptography;
using KM.SV.Data;
using KM.SV.TitanSwapper;
using KM.SV.Trainers;

namespace KM.SV.Workflows;

internal sealed record SvLegacyTitanRecovery(
    ChangePlan Plan, IReadOnlyList<SvWorkflowFileWrite> Writes, bool MigratesTitans)
{
    internal const string Domain = "workflow.sv.titan-recovery";
    internal const string PlannedCode = "KM-SV-TITAN-RECOVERY-PLANNED";
    internal const string CurrentWriter = "workflow.sv.titan-recovery-generation-1";
    private const string PreservationKey = "sv.titan-recovery-excluded";
    internal const string SourceCode = "KM-SV-TITAN-RECOVERY-SOURCE-INVALID";
    internal const string UnsupportedCode = "KM-SV-TITAN-RECOVERY-UNSUPPORTED";
    internal const string StaleCode = "KM-SV-TITAN-RECOVERY-STALE";
    internal const string ApplyCode = "KM-SV-TITAN-RECOVERY-APPLY-FAILED";

    internal static SvLegacyTitanRecovery Prepare(ProjectPaths paths, EditSession session, SvOutputMode mode,
        ProjectWorkspaceService projects, SvTrainersEditSessionService trainers)
    {
        var writes = new List<SvWorkflowFileWrite>();
        var sources = new List<ProjectFileReference>();
        var diagnostics = new List<ValidationDiagnostic>();
        var migratesTitans = false;
        string? currentFile = null;
        try
        {
            if (SvWorkflowFileSource.GetCurrentLooseOutputRelativePath(paths, SvDataPaths.TrainerDataArray) is null
                && SvWorkflowFileSource.GetCurrentLooseOutputRelativePath(paths, SvDataPaths.EventBattlePokemonArray) is null)
                return new(new(session.Id, [], []), [], false);
            var project = projects.Open(paths);
            var files = new SvWorkflowFileSource(bypassReusableBaseCache: true, maximumReadBytes: 32 * 1024 * 1024);
            // Older receipts have no producer version. Only unchanged, owned data
            // without the new writer marker is eligible for this one-time migration.
            var coordinator = OutputTransactionCoordinator.ForProject(paths);
            var inventory = coordinator.GetOwnershipInventoryAsync().GetAwaiter().GetResult();
            HashSet<string>? excluded = null;
            bool HasNewerWriter(string path)
            {
                excluded ??= coordinator.GetHistorySnapshotAsync().GetAwaiter().GetResult().Receipts
                    .Where(receipt => receipt.GameFamily == GameFamily.ScarletViolet
                        && receipt.Outcome == OutputApplyOutcome.Committed
                        && receipt.Origins.Any(origin => origin.Id == CurrentWriter))
                    .SelectMany(receipt => receipt.Targets)
                    .Select(target => Normalize(target.Path.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return excluded.Contains(Normalize(path));
            }
            bool CanRecover(string path)
            {
                var target = SvWorkflowFileSource.GetCurrentLooseOutputRelativePath(paths, path);
                if (target is null)
                    return !HasNewerWriter(path) && files.Read(project, path).SourceLayer == ProjectFileLayer.Base;
                var owned = inventory.Files.SingleOrDefault(record => record.Path.Value.Equals(target, StringComparison.OrdinalIgnoreCase));
                // The marker is sufficient to skip newer files without reading their payloads.
                if (owned is null || !coordinator.OwnershipScopeMatches(owned, ProjectIdentity.FromPaths(paths), GameFamily.ScarletViolet)
                    || owned.Claims.Any(IsExclusion)
                    || !owned.Claims.Any(claim => claim.OwnerId.Value.StartsWith("workflow.sv.", StringComparison.Ordinal))
                    || HasNewerWriter(path)) return false;
                var source = files.Read(project, path);
                return source.SourceLayer == ProjectFileLayer.Layered && owned.CurrentState.LengthBytes == source.Bytes.LongLength
                    && owned.CurrentState.Sha256 == Convert.ToHexStringLower(SHA256.HashData(source.Bytes));
            }
            currentFile = SvDataPaths.TrainerDataArray;
            if (SvWorkflowFileSource.GetCurrentLooseOutputRelativePath(paths, currentFile) is not null && CanRecover(currentFile))
                trainers.PrepareLegacyPartnerRecovery(project, files, writes, sources, CanRecover);
            currentFile = SvDataPaths.EventBattlePokemonArray;
            var events = SvWorkflowFileSource.GetCurrentLooseOutputRelativePath(paths, currentFile) is null || !CanRecover(currentFile)
                ? null : ReadLayered(files, project, currentFile);
            if (events is not null)
            {
                var baseline = files.ReadBase(project, currentFile);
                var document = new SvTitanSwapperDocument(events.Bytes);
                var bytes = document.RecoverLegacy(new(baseline.Bytes),
                    paths.SelectedGame == ProjectGame.Scarlet ? "scarlet" : "violet", out migratesTitans);
                if (migratesTitans)
                {
                    sources.Add(SvWorkflowFileSource.CreateReference(events));
                    sources.Add(SvWorkflowFileSource.CreateReference(baseline));
                    currentFile = SvTitanSwapperScript.VirtualPath;
                    var script = files.Read(project, currentFile);
                    sources.Add(SvWorkflowFileSource.CreateReference(script));
                    var migrated = new SvTitanSwapperDocument(bytes);
                    writes.Add(new(SvDataPaths.EventBattlePokemonArray, bytes));
                    writes.Add(new(currentFile, SvTitanSwapperScript.Apply(script.Bytes,
                        migrated.Rows.Where(row => row.Values["enabled"] == 1).Select(row => row.Id))));
                }
            }
        }
        catch (NotSupportedException)
        {
            diagnostics.Add(Error(UnsupportedCode,
                "Older Titan or Arven output contains an unsupported Pokemon or form. Restore that identity or select a supported base form before reviewing output again.", currentFile));
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            diagnostics.Add(Error(SourceCode,
                "Older Titan or Arven output could not be recovered safely. Check the original data and the affected output file, then review again.", currentFile));
        }

        var planned = new List<PlannedFileWrite>();
        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error) && writes.Count > 0)
        {
            foreach (var write in writes)
            {
                var info = SvWorkflowFileSource.CreatePlannedWrite(paths, write.VirtualPath, sources.Distinct().ToArray(), mode);
                planned.Add(new(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput,
                    "Recover earlier Titan and Arven edits while preserving unrelated changes."));
            }
            if (mode == SvOutputMode.Standalone)
            {
                var descriptor = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                planned.Add(new(descriptor.TargetRelativePath, descriptor.Sources, descriptor.ReplacesExistingOutput,
                    "Register recovered Titan and Arven output."));
            }
            diagnostics.Add(new(DiagnosticSeverity.Info,
                "This output will recover unchanged KM Editor output treated as legacy from 2.6.4 or earlier, retain supported Titan and Arven replacements and disable Terastallization for the five Titan partner teams.",
                Domain: Domain) { Code = PlannedCode });
        }
        var plan = SvChangePlanSourceGuard.Capture(paths, session, new(session.Id, planned, diagnostics), mode);
        return new(plan, writes, migratesTitans);
    }

    internal void Stage(ProjectPaths paths, SvOutputMode mode)
    {
        foreach (var write in Writes) SvWorkflowFileSource.Write(paths, write.VirtualPath, write.Bytes, mode);
    }

    private static string Normalize(string path) => path.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
        ? path[6..] : path;

    internal static bool IsExclusion(OwnedTarget claim) => claim.PreservationRule.Key == PreservationKey;

    internal static bool NeedsExclusion(string path)
    {
        path = Normalize(path);
        return path == SvDataPaths.TrainerDataArray || path == SvDataPaths.EventBattlePokemonArray
            || path == SvTitanSwapperScript.VirtualPath
            || path.StartsWith("world/scene/parts/field/field_contents/nushi/", StringComparison.Ordinal)
            || path.StartsWith("world/scene/parts/event/event_scenario/main_scenario/nushi_dragon_020_/", StringComparison.Ordinal);
    }

    internal static OwnedTarget CreateExclusion(OwnedTarget claim) => new(claim.GameFamily, claim.Address,
        claim.OwnerId, new PreservationRuleDescriptor(PreservationKey, 1, preservesUnownedData: true, requiresPreimage: true));

    internal static SvWorkflowFile? ReadLayered(SvWorkflowFileSource files, OpenedProject project, string path)
    {
        try
        {
            var source = files.Read(project, path);
            return source.SourceLayer == ProjectFileLayer.Layered ? source : null;
        }
        catch (Exception exception) when (IsMissing(exception)) { return null; }
    }

    private static bool IsMissing(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is FileNotFoundException or DirectoryNotFoundException) return true;
        return false;
    }

    internal static bool IsSourceFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException
        or IndexOutOfRangeException or KeyNotFoundException;

    internal static ValidationDiagnostic Error(string code, string message, string? file = null) => new(
        DiagnosticSeverity.Error, message, file is null ? null : $"romfs/{file}", Domain) { Code = code };
}
