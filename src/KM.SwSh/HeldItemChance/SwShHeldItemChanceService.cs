// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.HeldItemChance;

public sealed record SwShHeldItemChanceWorkflow(bool CanEdit, ProjectGame? DetectedGame,
    IReadOnlyList<int> Rates, string SourceLayer, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShHeldItemChanceResult(SwShHeldItemChanceWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class SwShHeldItemChanceService(ProjectWorkspaceService? workspace = null)
{
    public const string Domain = "workflow.heldItemChance";
    public const string SourceCode = "KM-SWSH-HELD-ITEM-SOURCE-INVALID";
    public const string RatesCode = "KM-SWSH-HELD-ITEM-RATES-INVALID";
    public const string SessionCode = "KM-SWSH-HELD-ITEM-SESSION-INVALID";
    public const string StaleCode = "KM-SWSH-HELD-ITEM-PLAN-STALE";
    public const string ApplyCode = "KM-SWSH-HELD-ITEM-APPLY-FAILED";
    private const string MainPath = "exefs/main";
    private readonly ProjectWorkspaceService workspace = workspace ?? new ProjectWorkspaceService();

    public SwShWorkflowSummary CreateSummary(OpenedProject project) => new(SwShWorkflowIds.HeldItemChance,
        "Held Item Chance", "Set global normal and boosted wild held item percentages.",
        !ProjectGameMetadata.IsSwordShield(project.Paths.SelectedGame) || !project.Health.CanOpenReadOnlyWorkflows
            ? SwShWorkflowAvailability.Disabled : project.Health.CanOpenEditableWorkflows
                ? SwShWorkflowAvailability.Available : SwShWorkflowAvailability.ReadOnly, []);

    public SwShHeldItemChanceWorkflow Load(ProjectPaths paths)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        return new(state?.CanEdit == true, paths.SelectedGame, state?.Rates ?? [],
            state?.Layer.ToString().ToLowerInvariant() ?? "missing", diagnostics);
    }

    public SwShHeldItemChanceResult Stage(ProjectPaths paths, IReadOnlyList<int>? rates, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics);
        if (!SwShHeldItemChancePatcher.AreValid(rates)) diagnostics.Add(Error(RatesCode,
            "Enter six whole percentages from 0 to 100. Normal and boosted totals must each be at most 100%.", "rates"));
        if (state is not null && !HasErrors(diagnostics))
        {
            var retained = current.PendingEdits.Where(edit => edit.Domain != Domain);
            current = current with { PendingEdits = (state.Rates.SequenceEqual(rates!)
                ? retained : retained.Append(CreateEdit(rates!))).ToArray() };
        }
        return new(Load(paths), current, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        RequireEditable(Read(paths, diagnostics), diagnostics);
        _ = Decode(session, diagnostics);
        return new(session, !HasErrors(diagnostics), diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics);
        var rates = Decode(session, diagnostics);
        if (state is null || HasErrors(diagnostics)) return new(session.Id, [], diagnostics);
        var sources = new ProjectFileReference[] { new(ProjectFileLayer.Base, MainPath) }
            .Concat(state.Layer == ProjectFileLayer.Layered ? [new ProjectFileReference(ProjectFileLayer.Layered, MainPath)] : [])
            .Concat(CreateEdit(rates).Sources).ToArray();
        PlannedFileWrite[] writes = state.Rates.SequenceEqual(rates) ? [] :
            [new(MainPath, sources, File.Exists(state.Target), "Update the six held item percentages while preserving other executable edits.")];
        return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        try
        {
            var plan = CreateChangePlan(paths, session);
            var diagnostics = plan.Diagnostics.ToList();
            if (!ChangePlanReview.Matches(reviewedPlan, plan)) diagnostics.Add(Error(StaleCode, "The held item chance plan changed. Review it again."));
            diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
            if (HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out var failures)) return Result(plan, [], failures);
            using var verified = scope!;
            if (!verified.TryPrepareSnapshotPlan(CreateChangePlan(verified.ApplyPaths, session), out var prepared))
                return Result(plan, [], [.. prepared.Diagnostics, Error(StaleCode, "Held item chance sources changed. Review again.")]);
            var state = Read(verified.ApplyPaths, diagnostics);
            var rates = Decode(session, diagnostics);
            if (state is null || HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (prepared.Writes.Count == 0) return verified.Commit(Result(prepared, [], diagnostics));
            var bytes = SwShHeldItemChancePatcher.Apply(state.Vanilla, state.Source, paths.SelectedGame, rates);
            if (SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(bytes, state.Vanilla)) File.Delete(state.Target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(state.Target)!);
                File.WriteAllBytes(state.Target, bytes);
                if (!File.ReadAllBytes(state.Target).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException();
            }
            return verified.Commit(Result(prepared, [new(ProjectFileLayer.Generated, MainPath)], diagnostics));
        }
        catch (Exception exception) when (IsSourceError(exception))
        { return Result(reviewedPlan, [], [Error(ApplyCode, "Held item chance output could not be applied. Check sources and output, then review again.")]); }
        finally { workspace.ClearMemoryCache(); }
    }

    private State? Read(ProjectPaths paths, List<ValidationDiagnostic> diagnostics)
    {
        try
        {
            if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)) throw new InvalidDataException();
            var project = workspace.Open(paths);
            if (!project.Health.CanOpenReadOnlyWorkflows) throw new InvalidDataException();
            var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, MainPath) ?? throw new InvalidDataException();
            var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, MainPath);
            if (target is not null && Directory.Exists(target)) throw new InvalidDataException();
            var vanilla = File.ReadAllBytes(basePath);
            var exists = target is not null && File.Exists(target);
            var source = exists ? File.ReadAllBytes(target!) : vanilla;
            var baseline = SwShHeldItemChancePatcher.Read(vanilla, paths.SelectedGame);
            var effective = SwShHeldItemChancePatcher.Read(source, paths.SelectedGame);
            if (!SwShHeldItemChancePatcher.Rates(baseline).SequenceEqual(SwShHeldItemChancePatcher.Defaults)) throw new InvalidDataException();
            SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline, effective, "Held Item Chance");
            return new(vanilla, source, target ?? "", exists ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
                SwShHeldItemChancePatcher.Rates(effective), target is not null && project.Health.CanOpenEditableWorkflows);
        }
        catch (Exception exception) when (IsSourceError(exception))
        {
            diagnostics.Add(Error(SourceCode, "Held Item Chance requires readable selected-game 1.3.2 sources, original base rates, valid output rates and an unchanged held item picker."));
            return null;
        }
    }

    private static PendingEdit CreateEdit(IReadOnlyList<int> rates)
    {
        var payload = string.Join(',', rates.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new(Domain, "Stage global held item percentages.", [new(ProjectFileLayer.Pending, $"pending/held-item-chance/rates/{hash}")],
            "global-held-items", "rates", payload);
    }

    internal static int[] Decode(EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits.Count != 1) { diagnostics.Add(Error(SessionCode, "Stage held item percentages before review.")); return []; }
        var edit = session.PendingEdits[0];
        var rates = (edit.NewValue ?? "").Split(',').Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rate) ? rate : -1).ToArray();
        if (!SwShHeldItemChancePatcher.AreValid(rates)) { diagnostics.Add(Error(SessionCode, "The staged held item percentages are invalid. Stage them again.")); return []; }
        var expected = CreateEdit(rates);
        if (edit.Domain != Domain || edit.RecordId != expected.RecordId || edit.Field != expected.Field
            || edit.NewValue != expected.NewValue || edit.Summary != expected.Summary || !edit.Sources.SequenceEqual(expected.Sources))
            diagnostics.Add(Error(SessionCode, "The staged held item percentages are inconsistent. Stage them again."));
        return rates;
    }

    private static void RequireEditable(State? state, List<ValidationDiagnostic> diagnostics)
    { if (state is not null && !state.CanEdit) diagnostics.Add(Error(SourceCode, "Configure an editable output before staging held item percentages.")); }
    private static bool IsSourceError(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException;
    private static ValidationDiagnostic Error(string code, string message, string field = "rates") =>
        new(DiagnosticSeverity.Error, message, File: MainPath, Domain: Domain, Field: field) { Code = code };
    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) => diagnostics.Any(value => value.Severity == DiagnosticSeverity.Error);
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        return new(id, now, written, new WriteManifest(id, now, plan.Writes), diagnostics);
    }
    private sealed record State(byte[] Vanilla, byte[] Source, string Target, ProjectFileLayer Layer, int[] Rates, bool CanEdit);
}
