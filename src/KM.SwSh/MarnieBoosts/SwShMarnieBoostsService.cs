// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.FairyGymBoosts;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.Workflows;

namespace KM.SwSh.MarnieBoosts;

public sealed record SwShMarnieBoostSource(string Path, string Status, string Layer);
public sealed record SwShMarnieBoostsWorkflow(bool CanEdit, ProjectGame? DetectedGame,
    IReadOnlyList<SwShFairyGymBoostSelection> Selections, IReadOnlyList<SwShMarnieBoostSource> Sources,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShMarnieBoostsResult(SwShMarnieBoostsWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class SwShMarnieBoostsService(ProjectWorkspaceService? workspace = null)
{
    public const string Domain = "workflow.marnieBoosts";
    public const string SourceCode = "KM-SWSH-MARNIE-SOURCE-INVALID";
    public const string SelectionCode = "KM-SWSH-MARNIE-SELECTION-INVALID";
    public const string SessionCode = "KM-SWSH-MARNIE-SESSION-INVALID";
    public const string StaleCode = "KM-SWSH-MARNIE-PLAN-STALE";
    public const string ApplyCode = "KM-SWSH-MARNIE-APPLY-FAILED";
    private const string Record = "marnie-wyndon-boosts";
    private const string Field = "boostSelections";
    private readonly ProjectWorkspaceService workspace = workspace ?? new ProjectWorkspaceService();

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var supported = ProjectGameMetadata.IsSwordShield(project.Paths.SelectedGame);
        var available = supported && project.Health.CanOpenReadOnlyWorkflows;
        return new(
            SwShWorkflowIds.MarnieBoosts,
            "Marnie Wyndon Boosts",
            "Edit Marnie cheering outcomes in the three Wyndon battles.",
            !available ? SwShWorkflowAvailability.Disabled
                : project.Health.CanOpenEditableWorkflows ? SwShWorkflowAvailability.Available
                : SwShWorkflowAvailability.ReadOnly,
            available ? [] : [Error(SourceCode, supported
                ? "Check the configured Sword or Shield sources."
                : "Marnie Wyndon Boosts requires Sword or Shield.")]);
    }

    public SwShMarnieBoostsWorkflow Load(ProjectPaths paths)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var states = Read(paths, diagnostics);
        return new(states.Count == 3 && states.All(state => state.CanEdit) && !HasErrors(diagnostics), paths.SelectedGame,
            states.SelectMany(state => SwShMarnieBoostsPatcher.Read(state.Source, state.Battle)).ToArray(),
            SwShMarnieBoostsPatcher.Paths.Select(path => states.FirstOrDefault(state => state.Path == path) is { } state
                ? new SwShMarnieBoostSource(path, "available", state.Layer.ToString().ToLowerInvariant())
                : new SwShMarnieBoostSource(path, "blocked", "missing")).ToArray(), diagnostics);
    }

    public SwShMarnieBoostsResult Stage(ProjectPaths paths, IReadOnlyList<SwShFairyGymBoostSelection> selections, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        var states = Read(paths, diagnostics);
        ValidateSelections(selections, diagnostics);
        RequireEditable(states, diagnostics);
        if (!HasErrors(diagnostics))
        {
            var retained = current.PendingEdits.Where(edit => edit.Domain != Domain);
            var same = states.SelectMany(state => SwShMarnieBoostsPatcher.Read(state.Source, state.Battle)).SequenceEqual(selections);
            current = current with { PendingEdits = (same ? retained : retained.Append(CreateEdit(selections))).ToArray() };
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
        var states = Read(paths, diagnostics);
        RequireEditable(states, diagnostics);
        var selections = Decode(session, diagnostics);
        if (HasErrors(diagnostics)) return new(session.Id, [], diagnostics);
        var sources = states.Select(state => new ProjectFileReference(ProjectFileLayer.Base, state.Path))
            .Concat(states.Where(state => state.Layer == ProjectFileLayer.Layered).Select(state => new ProjectFileReference(ProjectFileLayer.Layered, state.Path)))
            .Concat(CreateEdit(selections).Sources).ToArray();
        var writes = states.Where(state => !SwShMarnieBoostsPatcher.Read(state.Source, state.Battle).SequenceEqual(selections.Skip((state.Battle - 1) * 2).Take(2)))
            .Select(state => new PlannedFileWrite(state.Path, sources, File.Exists(state.Target),
                "Update Marnie Wyndon cheering outcomes while preserving other sequence data.")).ToArray();
        return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        try
        {
            var plan = CreateChangePlan(paths, session);
            var diagnostics = plan.Diagnostics.ToList();
            if (!ChangePlanReview.Matches(reviewedPlan, plan)) diagnostics.Add(Error(StaleCode, "The cheering plan changed. Review it again."));
            diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
            if (HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out var failures)) return Result(plan, [], failures);
            using var verified = scope!;
            if (!verified.TryPrepareSnapshotPlan(CreateChangePlan(verified.ApplyPaths, session), out var prepared))
                return Result(plan, [], [.. prepared.Diagnostics, Error(StaleCode, "Cheering sources changed. Review again.")]);
            var snapshotDiagnostics = prepared.Diagnostics.ToList();
            var states = Read(verified.ApplyPaths, snapshotDiagnostics);
            var selections = Decode(session, snapshotDiagnostics);
            if (HasErrors(snapshotDiagnostics)) return Result(plan, [], snapshotDiagnostics);
            var outputs = states.Where(state => prepared.Writes.Any(write => write.TargetRelativePath == state.Path))
                .Select(state => (State: state, Bytes: SwShMarnieBoostsPatcher.Apply(state.Vanilla, state.Source, state.Battle,
                    selections.Skip((state.Battle - 1) * 2).Take(2).ToArray()))).ToArray();
            foreach (var (state, bytes) in outputs)
            {
                if (bytes.AsSpan().SequenceEqual(state.Vanilla)) File.Delete(state.Target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(state.Target)!);
                    File.WriteAllBytes(state.Target, bytes);
                    if (!File.ReadAllBytes(state.Target).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Cheering output did not round trip.");
                }
            }
            return verified.Commit(Result(prepared, outputs.Select(output => new ProjectFileReference(ProjectFileLayer.Generated, output.State.Path)).ToArray(), snapshotDiagnostics));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        { return Result(reviewedPlan, [], [Error(ApplyCode, "Cheering output could not be applied. Check the sources and output, then review again.")]); }
        finally { workspace.ClearMemoryCache(); }
    }

    private List<State> Read(ProjectPaths paths, List<ValidationDiagnostic> diagnostics)
    {
        var states = new List<State>();
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        { diagnostics.Add(Error(SourceCode, "Marnie Wyndon Boosts requires Sword or Shield.")); return states; }
        try
        {
            var project = workspace.Open(paths);
            if (!project.Health.CanOpenReadOnlyWorkflows)
            { diagnostics.Add(Error(SourceCode, "Check the configured Sword or Shield sources.")); return states; }
            for (var battle = 1; battle <= 3; battle++)
            {
                var path = SwShMarnieBoostsPatcher.Paths[battle - 1];
                try
                {
                    var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, path);
                    var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, path);
                    if (basePath is null || target is not null && Directory.Exists(target)) throw new InvalidDataException();
                    var vanilla = File.ReadAllBytes(basePath);
                    SwShMarnieBoostsPatcher.ValidateBase(vanilla, battle);
                    var exists = target is not null && File.Exists(target);
                    var source = exists ? File.ReadAllBytes(target!) : vanilla;
                    _ = SwShMarnieBoostsPatcher.Read(source, battle);
                    states.Add(new(battle, path, vanilla, source, target ?? "", exists ? ProjectFileLayer.Layered : ProjectFileLayer.Base,
                        target is not null && project.Health.CanOpenEditableWorkflows));
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
                { diagnostics.Add(Error(SourceCode, "A cheering sequence is missing, unreadable or incompatible. Check its original and output files.", path)); }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        { diagnostics.Add(Error(SourceCode, "The cheering project sources could not be opened.")); }
        return states;
    }

    private static void ValidateSelections(IReadOnlyList<SwShFairyGymBoostSelection>? selections, List<ValidationDiagnostic> diagnostics)
    {
        if (selections is null || selections.Count != 6 || selections.Where((value, index) => value is null
            || value.BoostId != SwShMarnieBoostsPatcher.Ids[index]
            || !SwShFairyGymBoostsWorkflowService.IsSupportedSelection(value.EffectId, value.ResultKind)).Any())
            diagnostics.Add(Error(SelectionCode, "Choose one supported outcome for each of the six cheering answers."));
    }

    private static void RequireEditable(IReadOnlyList<State> states, List<ValidationDiagnostic> diagnostics)
    {
        if (states.Count != 3 || states.Any(state => !state.CanEdit))
            diagnostics.Add(Error(SourceCode, "All three original cheering sequences and an editable output are required."));
    }

    private static PendingEdit CreateEdit(IReadOnlyList<SwShFairyGymBoostSelection> selections)
    {
        var payload = string.Join(';', selections.Select(value => $"{value.BoostId}:{value.EffectId.ToString(CultureInfo.InvariantCulture)}:{value.ResultKind}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new(Domain, "Stage Marnie Wyndon cheering outcomes.",
            [new(ProjectFileLayer.Pending, $"pending/marnie-boosts/selections/{hash}")], Record, Field, payload);
    }

    private static SwShFairyGymBoostSelection[] Decode(EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits.Count != 1) { diagnostics.Add(Error(SessionCode, "Stage cheering outcomes before review.")); return []; }
        var edit = session.PendingEdits[0];
        var entries = (edit.NewValue ?? "").Split(';');
        var selections = entries.Select(entry => entry.Split(':')).Select(parts => parts.Length == 3
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var effect)
                ? new SwShFairyGymBoostSelection(parts[0], effect, parts[2]) : new("", -1, "")).ToArray();
        ValidateSelections(selections, diagnostics);
        if (HasErrors(diagnostics)) return [];
        var expected = CreateEdit(selections);
        if (edit.Domain != Domain || edit.RecordId != Record || edit.Field != Field || edit.NewValue != expected.NewValue
            || edit.Summary != expected.Summary || !edit.Sources.SequenceEqual(expected.Sources))
            diagnostics.Add(Error(SessionCode, "The staged cheering selection is invalid. Stage it again."));
        return selections;
    }

    private static ValidationDiagnostic Error(string code, string message, string? path = null) =>
        new(DiagnosticSeverity.Error, message, File: path, Domain: Domain, Field: Field) { Code = code };
    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) => diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        return new(id, now, written, new WriteManifest(id, now, plan.Writes), diagnostics);
    }
    private sealed record State(int Battle, string Path, byte[] Vanilla, byte[] Source, string Target, ProjectFileLayer Layer, bool CanEdit);
}
