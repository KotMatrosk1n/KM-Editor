// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Output;
using KM.Core.Semantics;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.GameOptions;

public sealed record SwShGameOptionsWorkflow(bool CanEdit, ProjectGame? DetectedGame,
    IReadOnlyList<int> Selections, string SourceLayer, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShGameOptionsResult(SwShGameOptionsWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class SwShGameOptionsService(ProjectWorkspaceService? workspace = null)
{
    public const string Domain = "workflow.gameOptions";
    public const string SourceCode = "KM-SWSH-GAME-OPTIONS-SOURCE-INVALID";
    public const string SelectionsCode = "KM-SWSH-GAME-OPTIONS-SELECTIONS-INVALID";
    public const string SessionCode = "KM-SWSH-GAME-OPTIONS-SESSION-INVALID";
    public const string StaleCode = "KM-SWSH-GAME-OPTIONS-PLAN-STALE";
    public const string ApplyCode = "KM-SWSH-GAME-OPTIONS-APPLY-FAILED";
    private const string MainPath = "exefs/main";
    private readonly ProjectWorkspaceService workspace = workspace ?? new ProjectWorkspaceService();

    public SwShWorkflowSummary CreateSummary(OpenedProject project) => new(SwShWorkflowIds.GameOptions,
        "Game Options", "Choose new game option values and hide individual menu selections.",
        !ProjectGameMetadata.IsSwordShield(project.Paths.SelectedGame) || !project.Health.CanOpenReadOnlyWorkflows
            ? SwShWorkflowAvailability.Disabled : project.Health.CanOpenEditableWorkflows
                ? SwShWorkflowAvailability.Available : SwShWorkflowAvailability.ReadOnly, []);

    public SwShGameOptionsWorkflow Load(ProjectPaths paths)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        return new(state?.CanEdit == true, paths.SelectedGame, state?.Selections ?? [],
            state?.Layer.ToString().ToLowerInvariant() ?? "missing", diagnostics);
    }

    public SwShGameOptionsResult Stage(ProjectPaths paths, IReadOnlyList<int>? selections, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics);
        try { selections = SwShGameOptionsPolicy.Normalize(selections); }
        catch (InvalidDataException exception) { diagnostics.Add(Error(SelectionsCode, exception.Message, "selections")); }
        if (state is not null && !HasErrors(diagnostics))
        {
            var retained = current.PendingEdits.Where(edit => edit.Domain != Domain);
            current = current with { PendingEdits = (state.Selections.SequenceEqual(selections!)
                ? retained : retained.Append(CreateEdit(selections!))).ToArray() };
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
        var selections = Decode(session, diagnostics);
        if (state is null || HasErrors(diagnostics)) return new(session.Id, [], diagnostics);
        var sources = new ProjectFileReference[] { new(ProjectFileLayer.Base, MainPath) }
            .Concat(state.Layer == ProjectFileLayer.Layered ? [new ProjectFileReference(ProjectFileLayer.Layered, MainPath)] : [])
            .Concat(CreateEdit(selections).Sources).ToArray();
        PlannedFileWrite[] writes = state.Selections.SequenceEqual(selections) ? [] :
            [new(MainPath, sources, File.Exists(state.Target), "Update starting options and hidden choices while preserving other executable edits.")];
        return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        try
        {
            var plan = CreateChangePlan(paths, session);
            var diagnostics = plan.Diagnostics.ToList();
            if (!ChangePlanReview.Matches(reviewedPlan, plan)) diagnostics.Add(Error(StaleCode, "The game options plan changed. Review it again."));
            diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
            if (HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out var failures)) return Result(plan, [], failures);
            using var verified = scope!;
            if (!verified.TryPrepareSnapshotPlan(CreateChangePlan(verified.ApplyPaths, session), out var prepared))
                return Result(plan, [], [.. prepared.Diagnostics, Error(StaleCode, "Game Options sources changed. Review again.")]);
            var state = Read(verified.ApplyPaths, diagnostics);
            var selections = Decode(session, diagnostics);
            if (state is null || HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (prepared.Writes.Count == 0) return verified.Commit(Result(prepared, [], diagnostics));
            var bytes = SwShGameOptionsPatcher.Apply(state.Vanilla, state.Source, paths.SelectedGame, selections);
            if (SwShExeFsMainComparison.IsSemanticallyEquivalentToBase(bytes, state.Vanilla)
                && CanDeleteRestoredOutput(paths, state.Source)) File.Delete(state.Target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(state.Target)!);
                File.WriteAllBytes(state.Target, bytes);
                if (!File.ReadAllBytes(state.Target).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException();
            }
            return verified.Commit(Result(prepared, [new(ProjectFileLayer.Generated, MainPath)], diagnostics));
        }
        catch (Exception exception) when (IsSourceError(exception))
        { return Result(reviewedPlan, [], [Error(ApplyCode, "Game Options output could not be applied. Check sources and output, then review again.")]); }
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
            var baseline = SwShGameOptionsPatcher.Read(vanilla, paths.SelectedGame);
            var effective = SwShGameOptionsPatcher.Read(source, paths.SelectedGame);
            if (!baseline.Selections.SequenceEqual(SwShGameOptionsPolicy.Defaults)) throw new InvalidDataException();
            SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline.Main, effective.Main, "Game Options");
            return new(vanilla, source, target ?? "", exists ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
                effective.Selections, target is not null && project.Health.CanOpenEditableWorkflows);
        }
        catch (Exception exception) when (IsSourceError(exception))
        {
            diagnostics.Add(Error(SourceCode, "Game Options requires readable selected game 1.3.2 sources, original Base options and compatible output hooks and descriptors."));
            return null;
        }
    }

    private static PendingEdit CreateEdit(IReadOnlyList<int> selections)
    {
        var payload = string.Join(',', selections.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new(Domain, "Stage global game option selections.", [new(ProjectFileLayer.Pending, $"pending/game-options/selections/{hash}")],
            "game-options", "selections", payload);
    }

    internal static int[] Decode(EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits.Count != 1) { diagnostics.Add(Error(SessionCode, "Stage game option selections before review.")); return []; }
        var edit = session.PendingEdits[0];
        var selections = (edit.NewValue ?? "").Split(',').Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rate) ? rate : -1).ToArray();
        try
        {
            if (!SwShGameOptionsPolicy.Normalize(selections).SequenceEqual(selections)) throw new InvalidDataException();
        }
        catch (InvalidDataException) { diagnostics.Add(Error(SessionCode, "The staged game option selections are invalid. Stage them again.")); return []; }
        var expected = CreateEdit(selections);
        if (edit.Domain != Domain || edit.RecordId != expected.RecordId || edit.Field != expected.Field
            || edit.NewValue != expected.NewValue || edit.Summary != expected.Summary || !edit.Sources.SequenceEqual(expected.Sources))
            diagnostics.Add(Error(SessionCode, "The staged game option selections are inconsistent. Stage them again."));
        return selections;
    }

    private static void RequireEditable(State? state, List<ValidationDiagnostic> diagnostics)
    { if (state is not null && !state.CanEdit) diagnostics.Add(Error(SourceCode, "Configure an editable output before staging game option selections.")); }
    private static bool CanDeleteRestoredOutput(ProjectPaths paths, byte[] source)
    {
        var coordinator = OutputTransactionCoordinator.ForProject(paths);
        var inventory = coordinator.GetOwnershipInventorySnapshotAsync().GetAwaiter().GetResult();
        var record = inventory.Inventory.Files.SingleOrDefault(value => value.Path.CanonicalKey == new RelativeOutputPath(MainPath).CanonicalKey);
        return record is not null && record.FileDeleteEligible
            && record.CurrentState == OutputFileState.Existing(Convert.ToHexStringLower(SHA256.HashData(source)), source.LongLength)
            && coordinator.OwnershipScopeMatches(record, ProjectIdentity.FromPaths(paths), GameFamily.SwordShield)
            && record.OutputMode == "sword-shield-layered-output"
            && record.Claims.Any() && record.Claims.All(claim => claim.OwnerId == new OwnershipOwnerId("sword-shield-verified-editor"));
    }
    private static bool IsSourceError(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException or OutputCoordinatorException;
    private static ValidationDiagnostic Error(string code, string message, string field = "selections") =>
        new(DiagnosticSeverity.Error, message, File: MainPath, Domain: Domain, Field: field) { Code = code };
    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) => diagnostics.Any(value => value.Severity == DiagnosticSeverity.Error);
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        return new(id, now, written, new WriteManifest(id, now, plan.Writes), diagnostics);
    }
    private sealed record State(byte[] Vanilla, byte[] Source, string Target, ProjectFileLayer Layer, int[] Selections, bool CanEdit);
}
