// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.SV.Workflows;

public sealed partial class SvWorkflowService
{
    private readonly SvLegacyRecoveryScanCache legacyRecoveryScanCache = new();
    private static bool SupportsLegacyRecovery(EditSession session, SvEditSessionDomain domain) =>
        IsNormalDomain(domain) || domain is SvEditSessionDomain.TypeChart or SvEditSessionDomain.FashionUnlock or SvEditSessionDomain.HyperspaceBypass
        || domain == SvEditSessionDomain.Mixed && TryGetNormalDomains(session, out _);

    private sealed record LegacyRecoverySnapshot(SvLegacyTitanRecovery Recovery, ChangePlan Plan, EditSession Session,
        IReadOnlyList<SvEditSessionDomain> Domains);

    private LegacyRecoverySnapshot? CreateLegacyRecoverySnapshot(ProjectPaths paths, EditSession session,
        SvOutputMode mode)
    {
        var scanStamp = SvLegacyRecoveryScanCache.Capture(paths);
        if (legacyRecoveryScanCache.IsClean(paths, scanStamp)) return null;
        var recovery = SvLegacyTitanRecovery.Prepare(paths, session, mode, projectWorkspaceService,
            trainersEditSessionService);
        if (recovery.Writes.Count == 0 && recovery.Plan.Diagnostics.Count == 0)
        {
            legacyRecoveryScanCache.RememberClean(paths, scanStamp);
            return null;
        }
        TryGetNormalDomains(session, out var domains);
        var diagnostics = recovery.Plan.Diagnostics.ToList();
        if (!recovery.Plan.CanApply)
            return new(recovery, recovery.Plan, session, domains);

        // Authenticate signed Titan intent against the actual files before projecting
        // a repair. Only then may its source binding be rebased within this batch.
        if (domains.Contains(SvEditSessionDomain.TitanSwapper))
            diagnostics.AddRange(titanSwapperService.Validate(paths, SliceSession(session, SvEditSessionDomain.TitanSwapper)).Diagnostics);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new(recovery, new(session.Id, [], diagnostics), session, domains);

        try
        {
            using var preview = SvWorkflowFileSource.BeginDeferredOutputBatch(paths, mode, recovery.Plan,
                RecoveryContext(recovery.Plan, session, domains));
            recovery.Stage(paths, mode);
            string? titanRevision = null;
            if (recovery.MigratesTitans)
            {
                var titans = titanSwapperService.Load(paths);
                titanRevision = titans.SourceRevision;
                diagnostics.AddRange(titans.Diagnostics);
                if (titans.Rows.Any(row => row.Values["enabled"] == 1
                    && !titans.SpeciesOptions.Any(option => option.Value == row.Values["species"])))
                    diagnostics.Add(SvLegacyTitanRecovery.Error(SvLegacyTitanRecovery.UnsupportedCode,
                        "An older Titan replacement has no supported base form and model. Select a supported replacement and review again."));
            }
            var effective = session;
            if (domains.Contains(SvEditSessionDomain.TitanSwapper))
            {
                var rebound = titanSwapperService.RebindVerifiedBatch(paths, SliceSession(session, SvEditSessionDomain.TitanSwapper));
                effective = session with { PendingEdits = session.PendingEdits
                    .Where(edit => GetDomain(edit.Domain) != SvEditSessionDomain.TitanSwapper)
                    .Concat(rebound.PendingEdits).ToArray() };
            }
            var regular = domains.Count > 1
                ? CreateNormalDomainChangePlan(paths, effective, domains, mode)
                : CreateSingleDomainChangePlan(paths, effective, domains[0], mode);
            diagnostics.AddRange(regular.Diagnostics);
            var plan = new ChangePlan(session.Id,
                CombinePlannedWrites(recovery.Plan.Writes.Concat(regular.Writes),
                    CombineFingerprintValues([CreatePendingEditFingerprint(session.PendingEdits), titanRevision])!),
                diagnostics) { EffectivePendingEdits = regular.EffectivePendingEdits };
            return new(recovery, plan, effective, domains);
        }
        catch (Exception exception) when (SvLegacyTitanRecovery.IsSourceFailure(exception) || exception is NotSupportedException)
        {
            diagnostics.Add(SvLegacyTitanRecovery.Error(SvLegacyTitanRecovery.SourceCode,
                "The older output could not be combined with the pending edits. Check the affected data and review again."));
            return new(recovery, new(session.Id, [], diagnostics), session, domains);
        }
    }

    private static SvOutputApplyContext RecoveryContext(ChangePlan plan, EditSession session,
        IReadOnlyList<SvEditSessionDomain> domains) => new(
            OutputReviewFingerprint.FromChangePlan(plan), new OwnershipOwnerId("workflow.sv.output"),
            domains.Select(domain => new OutputApplyOrigin(OutputApplyOriginKind.Workflow, GetDomainName(domain)))
                .Append(new OutputApplyOrigin(OutputApplyOriginKind.Workflow, SvLegacyTitanRecovery.Domain)).ToArray())
        { HistoryDetails = OutputHistoryDetails.Capture(session.PendingEdits) };

    private ApplyResult ApplyLegacyRecovery(ProjectPaths paths, EditSession session, ChangePlan reviewed,
        SvOutputMode mode, LegacyRecoverySnapshot snapshot)
    {
        var plan = snapshot.Plan;
        var diagnostics = plan.Diagnostics.ToList();
        var written = new List<ProjectFileReference>();
        OutputApplyResult? transaction = null;
        if (!ChangePlanReview.Matches(reviewed, plan))
            diagnostics.Add(SvLegacyTitanRecovery.Error(SvLegacyTitanRecovery.StaleCode,
                "The reviewed Titan or Arven recovery has changed. Review the output again before applying."));
        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            try
            {
                using var batch = SvWorkflowFileSource.BeginDeferredOutputBatch(paths, mode, plan,
                    RecoveryContext(plan, session, snapshot.Domains));
                snapshot.Recovery.Stage(paths, mode);
                foreach (var domain in snapshot.Domains)
                {
                    var domainSession = SliceSession(snapshot.Session, domain);
                    if (domain == SvEditSessionDomain.TitanSwapper)
                        domainSession = titanSwapperService.RebindVerifiedBatch(paths, domainSession);
                    var domainPlan = CreateSingleDomainChangePlan(paths, domainSession, domain, mode);
                    var applied = ApplySingleDomainChangePlan(paths, domainSession, domainPlan, domain, mode);
                    diagnostics.AddRange(applied.Complete(domainPlan).Diagnostics);
                    if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) break;
                }
                if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
                {
                    transaction = batch.Commit(() => ChangePlanReview.Matches(snapshot.Recovery.Plan,
                        SvLegacyTitanRecovery.Prepare(paths, session, mode, projectWorkspaceService, trainersEditSessionService).Plan));
                    if (transaction is not null)
                        written.AddRange(plan.Writes.Select(write => new ProjectFileReference(ProjectFileLayer.Generated, write.TargetRelativePath)));
                }
            }
            catch (Exception exception) when (SvLegacyTitanRecovery.IsSourceFailure(exception)
                || exception is OutputCoordinatorException or NotSupportedException)
            {
                if (exception is SvOutputApplyNotCommittedException failed) transaction = failed.Result;
                diagnostics.Add(SvLegacyTitanRecovery.Error(SvLegacyTitanRecovery.ApplyCode,
                    "Titan or Arven recovery could not be committed. Check the output diagnostics and review again."));
                written.Clear();
            }
        }
        return SvEditSessionSupport.CreateApplyResult(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            plan, written, diagnostics, transaction);
    }
}
