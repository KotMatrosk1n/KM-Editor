// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.Trainers;

namespace KM.SwSh.TrainerWhiteout;

public sealed record SwShTrainerWhiteoutRecord(int TrainerId, string Name, bool Enabled,
    bool VanillaEnabled, bool? Override, bool Mixed);
public sealed record SwShTrainerWhiteoutChange(int TrainerId, bool? Enabled);
public sealed record SwShTrainerWhiteoutWorkflow(bool CanEdit, IReadOnlyList<SwShTrainerWhiteoutRecord> Trainers,
    ProjectGame? DetectedGame, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShTrainerWhiteoutEditResult(SwShTrainerWhiteoutWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class SwShTrainerWhiteoutService(ProjectWorkspaceService? workspace = null)
{
    public const string Domain = "workflow.trainerWhiteout";
    public const string Field = "enabled";
    public const string InvalidCode = "KM-SWSH-TRAINER-WHITEOUT-INVALID";
    private const string MainPath = "exefs/main";
    private const string ScriptRoot = "romfs/bin/script/amx/";
    private readonly ProjectWorkspaceService workspace = workspace ?? new ProjectWorkspaceService();

    public SwShTrainerWhiteoutWorkflow Load(ProjectPaths paths)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        if (state is null) return new(false, [], paths.SelectedGame, diagnostics);
        var roster = new SwShTrainersWorkflowService().Load(workspace.Open(paths));
        diagnostics.AddRange(roster.Diagnostics);
        var supported = roster.Trainers.Where(trainer => trainer.TrainerId is >= 1 and < SwShTrainerWhiteoutAmxPatcher.TrainerCount).ToArray();
        if (supported.Length != roster.Trainers.Count)
            diagnostics.Add(new(DiagnosticSeverity.Warning,
                "Additional trainer IDs outside the original roster are not included in Trainer Whiteout. Original trainer settings remain editable.",
                Domain: Domain, Field: Field) { Code = InvalidCode });
        var rows = supported.Select(trainer =>
        {
            var id = trainer.TrainerId;
            var vanilla = SwShTrainerWhiteoutCatalog.VanillaEnabled(id);
            var values = state.Scripts.Where(script => AppliesTo(script, id)).Select(script => script.Settings[id]).Distinct().ToArray();
            var value = values[0];
            return new SwShTrainerWhiteoutRecord(id, trainer.Name, value == 0 ? vanilla : value == 2,
                vanilla, value == 0 ? null : value == 2, values.Length > 1);
        }).ToArray();
        return new(state.CanEdit && !HasErrors(diagnostics), rows, paths.SelectedGame, diagnostics);
    }

    public SwShTrainerWhiteoutEditResult Stage(ProjectPaths paths, IReadOnlyList<SwShTrainerWhiteoutChange> changes, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics);
        if (changes.Count is < 1 or > 436 || changes.Select(change => change.TrainerId).Distinct().Count() != changes.Count
            || changes.Any(change => change.TrainerId is < 1 or >= SwShTrainerWhiteoutAmxPatcher.TrainerCount))
            diagnostics.Add(Error("Stage one valid whiteout setting per trainer."));
        if (state is not null && !HasErrors(diagnostics))
        {
            var retained = current.PendingEdits.Where(edit => edit.Domain != Domain
                || !changes.Any(change => edit.RecordId == change.TrainerId.ToString(CultureInfo.InvariantCulture))).ToList();
            foreach (var change in changes)
                if (state.Scripts.Any(script => script.Settings[change.TrainerId] != Encode(AppliesTo(script, change.TrainerId) ? change.Enabled : null)))
                    retained.Add(CreateEdit(change.TrainerId, change.Enabled));
            current = current with { PendingEdits = retained.ToArray() };
        }
        return new(Load(paths), current, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics);
        _ = ReadChanges(session, diagnostics);
        return new(session, !HasErrors(diagnostics), diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics);
        var changes = ReadChanges(session, diagnostics);
        if (state is null || HasErrors(diagnostics)) return new(session.Id, [], diagnostics);
        var pendingSource = CreatePendingSource(changes);
        var writes = SelectWrites(state, changes).Select(file => new PlannedFileWrite(file.RelativePath,
            new ProjectFileReference[] { new(ProjectFileLayer.Base, file.RelativePath) }
                .Concat(file.Layer == ProjectFileLayer.Layered ? [new ProjectFileReference(ProjectFileLayer.Layered, file.RelativePath)] : [])
                .Append(pendingSource).Distinct().ToArray(),
            File.Exists(file.Target), "Apply pending trainer whiteout settings while preserving other edits.")).ToArray();
        // Every table participates in deciding whether facility callbacks are required.
        var allSources = state.Scripts.SelectMany(script => new[] { new ProjectFileReference(ProjectFileLayer.Base, script.File.RelativePath) }
            .Concat(script.File.Layer == ProjectFileLayer.Layered ? [new ProjectFileReference(ProjectFileLayer.Layered, script.File.RelativePath)] : []))
            .Append(new(ProjectFileLayer.Base, MainPath))
            .Concat(state.Main.Layer == ProjectFileLayer.Layered ? [new ProjectFileReference(ProjectFileLayer.Layered, MainPath)] : []).ToArray();
        if (writes.Length > 0) writes[0] = writes[0] with { Sources = writes[0].Sources.Concat(allSources).Distinct().ToArray() };
        return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        try
        {
            var plan = CreateChangePlan(paths, session);
            var diagnostics = plan.Diagnostics.ToList();
            if (!ChangePlanReview.Matches(reviewedPlan, plan)) diagnostics.Add(Error("The Trainer Whiteout output plan changed. Review it again."));
            diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
            if (HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out var failures)) return Result(plan, [], failures);
            using var verified = scope!;
            var snapshot = CreateChangePlan(verified.ApplyPaths, session);
            if (!verified.TryPrepareSnapshotPlan(snapshot, out var prepared))
                return Result(plan, [], [.. prepared.Diagnostics, Error("Trainer Whiteout sources changed while preparing output. Review again.")]);
            var state = Read(verified.ApplyPaths, diagnostics);
            var changes = ReadChanges(session, diagnostics);
            if (state is null || HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            var outputs = new List<(SourceFile File, byte[] Bytes)>();
            foreach (var file in SelectWrites(state, changes))
            {
                var bytes = file.RelativePath == MainPath
                    ? SwShTrainerWhiteoutMainPatcher.Apply(file.Vanilla, file.Source, NeedsBridge(state, changes), paths.SelectedGame!.Value)
                    : SwShTrainerWhiteoutAmxPatcher.ApplySettings(file.Vanilla, file.Source, Path.GetFileName(file.RelativePath),
                        ScriptChanges(state.Scripts.Single(script => script.File.RelativePath == file.RelativePath), changes));
                outputs.Add((file, bytes));
            }
            foreach (var (file, bytes) in outputs)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                File.WriteAllBytes(file.Target, bytes);
                if (!File.ReadAllBytes(file.Target).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Trainer Whiteout output failed verification.");
            }
            return verified.Commit(Result(prepared, outputs.Select(output => new ProjectFileReference(ProjectFileLayer.Generated, output.File.RelativePath)).ToArray(), diagnostics));
        }
        catch (Exception exception) when (IsSourceError(exception))
        {
            return Result(reviewedPlan, [], [Error($"Trainer Whiteout output could not be applied: {exception.Message}")]);
        }
        finally { workspace.ClearMemoryCache(); }
    }

    private State? Read(ProjectPaths paths, List<ValidationDiagnostic> diagnostics)
    {
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            diagnostics.Add(Error("Trainer Whiteout supports Pokemon Sword and Pokemon Shield."));
            return null;
        }
        try
        {
            var project = workspace.Open(paths);
            if (!project.Health.CanOpenReadOnlyWorkflows) throw new InvalidDataException("Valid Base RomFS and Base ExeFS paths are required.");
            var scripts = SwShTrainerWhiteoutCatalog.ScriptHashes.Keys.Select(name =>
            {
                var file = ReadFile(paths, ScriptRoot + name);
                var configuration = SwShTrainerWhiteoutAmxPatcher.ReadConfiguration(file.Vanilla, file.Source, name);
                return new Script(file, configuration.Settings, configuration.VanillaRouting);
            }).ToArray();
            var main = ReadFile(paths, MainPath);
            if (SwShTrainerWhiteoutMainPatcher.Inspect(main.Vanilla, paths.SelectedGame).HasAny)
                throw new InvalidDataException("Base ExeFS must contain vanilla facility callbacks.");
            var mainState = SwShTrainerWhiteoutMainPatcher.Inspect(main.Source, paths.SelectedGame);
            return new(scripts, main, mainState, project.Health.CanOpenEditableWorkflows && main.Target.Length != 0);
        }
        catch (Exception exception) when (IsSourceError(exception))
        {
            diagnostics.Add(Error($"Trainer Whiteout could not read compatible sources: {exception.Message}"));
            return null;
        }
    }

    private static SourceFile ReadFile(ProjectPaths paths, string relativePath)
    {
        var basePath = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, relativePath)
            ?? throw new InvalidDataException($"The vanilla source is missing: {relativePath}.");
        var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, relativePath);
        if (target is not null && Directory.Exists(target)) throw new InvalidDataException($"The output path must be a readable file or absent: {relativePath}.");
        var vanilla = File.ReadAllBytes(basePath);
        var exists = target is not null && File.Exists(target);
        return new(relativePath, vanilla, exists ? File.ReadAllBytes(target!) : vanilla, target ?? string.Empty,
            exists ? ProjectFileLayer.Layered : ProjectFileLayer.Base);
    }

    private static IEnumerable<SourceFile> SelectWrites(State state, IReadOnlyDictionary<int, bool?> changes)
    {
        foreach (var script in state.Scripts)
            if (ScriptChanges(script, changes).Any(change => script.Settings[change.Key] != Encode(change.Value))) yield return script.File;
        var bridge = NeedsBridge(state, changes);
        if (bridge ? !state.MainState.Installed : state.MainState.HasAny) yield return state.Main;
    }

    private static bool NeedsBridge(State state, IReadOnlyDictionary<int, bool?> changes) => state.Scripts
        .Where(script => Path.GetFileName(script.File.RelativePath) is "tournament.amx" or "shibari_dojo.amx")
        .Any(script => script.Settings.Select((value, id) => AppliesTo(script, id)
            ? changes.TryGetValue(id, out var change) ? Encode(change) : value : (byte)0).Any(value => value != 0));

    private static bool AppliesTo(Script script, int trainerId) =>
        !script.VanillaRouting || SwShTrainerWhiteoutRoutes.Contains(Path.GetFileName(script.File.RelativePath), trainerId);

    // Clear obsolete copies from older output only for trainers in this edit.
    private static IReadOnlyDictionary<int, bool?> ScriptChanges(Script script, IReadOnlyDictionary<int, bool?> changes) =>
        changes.Where(change => AppliesTo(script, change.Key) || script.Settings[change.Key] != 0)
            .ToDictionary(change => change.Key, change => AppliesTo(script, change.Key) ? change.Value : null);

    private static ProjectFileReference CreatePendingSource(IReadOnlyDictionary<int, bool?> changes)
    {
        // Bind the complete edit without repeating hundreds of source paths per output file.
        var settings = new byte[SwShTrainerWhiteoutAmxPatcher.TrainerCount];
        Array.Fill(settings, byte.MaxValue);
        foreach (var change in changes) settings[change.Key] = Encode(change.Value);
        return new(ProjectFileLayer.Pending, $"pending/trainer-whiteout/changes/{Convert.ToHexString(SHA256.HashData(settings))}");
    }

    private static Dictionary<int, bool?> ReadChanges(EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        var changes = new Dictionary<int, bool?>();
        if (session.PendingEdits.Count is < 1 or > 436) diagnostics.Add(Error("Trainer Whiteout requires valid staged trainer settings."));
        foreach (var edit in session.PendingEdits)
        {
            if (edit.Domain != Domain || edit.Field != Field || !int.TryParse(edit.RecordId, out var id) || id is < 1 or >= 437
                || edit.NewValue is not ("true" or "false" or "vanilla"))
            {
                diagnostics.Add(Error("A staged Trainer Whiteout setting is invalid. Stage it again."));
                continue;
            }
            bool? enabled = edit.NewValue == "vanilla" ? null : edit.NewValue == "true";
            var expected = CreateEdit(id, enabled);
            if (edit.RecordId != expected.RecordId || edit.Summary != expected.Summary || !edit.Sources.SequenceEqual(expected.Sources) || !changes.TryAdd(id, enabled))
                diagnostics.Add(Error("A staged Trainer Whiteout setting is inconsistent. Stage it again."));
        }
        return changes;
    }

    private static PendingEdit CreateEdit(int id, bool? enabled)
    {
        var value = enabled is null ? "vanilla" : enabled.Value ? "true" : "false";
        var record = id.ToString(CultureInfo.InvariantCulture);
        return new(Domain, $"Set trainer {record} whiteout to {value}.",
            [new(ProjectFileLayer.Pending, $"pending/trainer-whiteout/{record}/{value}")], record, Field, value);
    }
    private static byte Encode(bool? value) => value is null ? (byte)0 : value.Value ? (byte)2 : (byte)1;
    private static void RequireEditable(State? state, List<ValidationDiagnostic> diagnostics)
    {
        if (state is not null && !state.CanEdit) diagnostics.Add(Error("Trainer Whiteout requires an editable project with a configured Output Root."));
    }
    private static bool IsSourceError(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException;
    private static ValidationDiagnostic Error(string message) => new(DiagnosticSeverity.Error, message, Domain: Domain, Field: Field) { Code = InvalidCode };
    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) => diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        return new(id, now, written, new WriteManifest(id, now, plan.Writes), diagnostics);
    }
    private sealed record SourceFile(string RelativePath, byte[] Vanilla, byte[] Source, string Target, ProjectFileLayer Layer);
    private sealed record Script(SourceFile File, IReadOnlyList<byte> Settings, bool VanillaRouting);
    private sealed record State(IReadOnlyList<Script> Scripts, SourceFile Main, SwShTrainerWhiteoutMainState MainState, bool CanEdit);
}
