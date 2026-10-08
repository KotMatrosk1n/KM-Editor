// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SV.Blueberry;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.Blueberry;

public sealed record SvBlueberryEntry(SvBlueberryRow Record, string Label, IReadOnlyDictionary<string, int> VanillaValues);
public sealed record SvBlueberryWorkflow(SvWorkflowSummary Summary, string SourceRevision,
    IReadOnlyList<SvBlueberryEntry> Rows, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SvBlueberryUpdate(string RowId, string Field, int Value);
public sealed record SvBlueberryEditResult(SvBlueberryWorkflow Workflow, EditSession Session, IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed class SvBlueberryService(ProjectWorkspaceService projects, SvBlueberryKind kind)
{
    internal static string Id(SvBlueberryKind kind) => kind switch
    { SvBlueberryKind.BbqRewards => "bbqRewards", SvBlueberryKind.SupportBoard => "supportBoard", SvBlueberryKind.Snacksworth => "snacksworth", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    internal static string DomainFor(SvBlueberryKind kind) => "workflow." + Id(kind);
    private string Domain => DomainFor(kind);
    private string Path => SvBlueberryDocument.PathFor(kind);
    private readonly SvWorkflowFileSource files = new(bypassReusableBaseCache: true);
    private sealed record Intent(string Revision, int Previous, int Value);
    private sealed record Source(SvWorkflowFile File, SvBlueberryDocument Document, SvBlueberryDocument Vanilla);
    internal sealed record Output(string Path, byte[] Bytes, IReadOnlyList<ProjectFileReference> Sources);

    public SvWorkflowSummary CreateSummary(OpenedProject project) => SvWorkflowSupport.CreateSummary(project, Id(kind), kind switch
    { SvBlueberryKind.BbqRewards => "BBQ Rewards", SvBlueberryKind.SupportBoard => "Support Board", _ => "Snacksworth" }, kind switch
    { SvBlueberryKind.BbqRewards => "Edit Blueberry Quest BP rewards.", SvBlueberryKind.SupportBoard => "Edit Support Board BP prices.", _ => "Choose solo and group quest eligibility for legendary treats." });

    private Source Read(OpenedProject project)
    {
        var source = files.Read(project, Path);
        var document = new SvBlueberryDocument(source.Bytes, kind);
        var vanilla = new SvBlueberryDocument(files.ReadBaseBytesFresh(project.Paths, Path), kind);
        if (document.Rows.Any(row => !vanilla.Rows.Any(original => original.Id == row.Id)))
            throw new InvalidDataException("Blueberry record identities do not match Base RomFS.");
        return new(source, document, vanilla);
    }
    public SvBlueberryWorkflow Load(ProjectPaths paths, EditSession? session = null)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var project = projects.Open(paths); var summary = CreateSummary(project); var diagnostics = summary.Diagnostics.ToList();
        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame) || summary.Availability == SvWorkflowAvailability.Disabled) return new(summary, "", [], diagnostics);
        try
        {
            var source = Read(project);
            var values = source.Document.Rows.ToDictionary(row => row.Id, row => row.Values.ToDictionary());
            foreach (var edit in (session?.PendingEdits ?? []).Where(edit => edit.Domain == Domain).Select(edit => RebindMissing(edit, source)))
                if (Resolve(source, edit, diagnostics, out var value)) values[edit.RecordId!][edit.Field!] = value;
            var labels = SvBlueberryLabels.Load(project, files, kind, source.Document.Rows, diagnostics);
            return new(summary, source.Document.Revision, source.Document.Rows.Select(row => new SvBlueberryEntry(
                row with { Values = values[row.Id] }, labels.GetValueOrDefault(row.Id, row.LabelKey),
                source.Vanilla.Rows.Single(original => original.Id == row.Id).Values)).ToArray(), diagnostics);
        }
        catch (Exception exception) when (SourceFailure(exception))
        { diagnostics.Add(Error("Blueberry data could not be read. Check Base RomFS and reload the editor.", "source")); return new(summary with { Availability = SvWorkflowAvailability.Disabled }, "", [], diagnostics); }
    }
    public SvBlueberryEditResult Stage(ProjectPaths paths, EditSession? session, string revision, IReadOnlyList<SvBlueberryUpdate> updates)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var current = session ?? EditSession.Start(); var diagnostics = new List<ValidationDiagnostic>();
        var workflow = Load(paths, current);
        try
        {
            var project = projects.Open(paths);
            if (!SvEditSessionSupport.CanEdit(project, workflow.Summary, workflow.Diagnostics, Domain, diagnostics)) return new(workflow, current, diagnostics);
            var source = Read(project);
            if (revision != source.Document.Revision) diagnostics.Add(Error("Blueberry data changed. Reload before staging.", "sourceRevision"));
            if (updates.Count is < 1 or > 2048) diagnostics.Add(Error("Select a bounded set of Blueberry fields.", "session"));
            var edits = current.PendingEdits.Select(edit => RebindMissing(edit, source)).ToList();
            var seen = new HashSet<(string, string)>();
            foreach (var update in updates)
            {
                var row = source.Document.Rows.SingleOrDefault(row => row.Id == update.RowId);
                if (row is null || !row.Values.TryGetValue(update.Field, out var previous)
                    || !SvBlueberryDocument.ValidValue(kind, update.Field, update.Value) || !seen.Add((update.RowId, update.Field)))
                { diagnostics.Add(Error("Select an existing field and enter a value within its range.", "value")); continue; }
                edits.RemoveAll(edit => edit.Domain == Domain && edit.RecordId == update.RowId && edit.Field == update.Field);
                if (previous != update.Value || kind == SvBlueberryKind.Snacksworth) edits.Add(new(Domain, $"Set {Id(kind)} {update.RowId} {update.Field} to {update.Value}.",
                    [new(source.File.SourceLayer, source.File.RelativePath)], update.RowId, update.Field,
                    JsonSerializer.Serialize(new Intent(source.Document.Revision, previous, update.Value))));
            }
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return new(workflow, current, diagnostics);
            var staged = current with { PendingEdits = edits };
            return new(Load(paths, staged), staged, diagnostics);
        }
        catch (Exception exception) when (SourceFailure(exception))
        { diagnostics.Add(Error("Blueberry changes could not be staged. Reload and try again.", "source")); return new(workflow, current, diagnostics); }
    }
    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths); var diagnostics = new List<ValidationDiagnostic>();
        try
        {
            var project = projects.Open(paths); var summary = CreateSummary(project);
            SvEditSessionSupport.CanEdit(project, summary, summary.Diagnostics, Domain, diagnostics);
            if (session.PendingEdits.Count is < 1 or > 2048) diagnostics.Add(Error("Stage Blueberry changes before reviewing.", "session"));
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return new(session, false, diagnostics);
            var source = Read(project); var seen = new HashSet<(string?, string?)>();
            session = session with { PendingEdits = session.PendingEdits.Select(edit => RebindMissing(edit, source)).ToArray() };
            foreach (var edit in session.PendingEdits)
            {
                if (!seen.Add((edit.RecordId, edit.Field))) diagnostics.Add(Error("A Blueberry field is staged more than once.", "session"));
                Resolve(source, edit, diagnostics, out _);
            }
        }
        catch (Exception exception) when (SourceFailure(exception)) { diagnostics.Add(Error("Blueberry changes could not be validated against the current source.", "source")); }
        return new(session, diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error), diagnostics);
    }
    private IReadOnlyList<Output> Prepare(ProjectPaths paths, EditSession session, ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projects.Open(paths); var source = Read(project); var changes = new List<(string, string, int)>();
        foreach (var edit in session.PendingEdits)
            if (Resolve(source, edit, diagnostics, out var value)) changes.Add((edit.RecordId!, edit.Field!, value));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return [];
        var bytes = source.Document.Write(changes);
        var result = new List<Output> { new(Path, bytes, [new(source.File.SourceLayer, source.File.RelativePath), new(ProjectFileLayer.Base, "romfs/" + Path)]) };
        if (kind == SvBlueberryKind.Snacksworth)
        {
            var desired = new SvBlueberryDocument(bytes, kind);
            var changed = desired.Rows.Any(row => row.Values.Any(pair => pair.Value != source.Vanilla.Rows.Single(original => original.Id == row.Id).Values[pair.Key]));
            result.AddRange(SvSnacksworthDialogue.Prepare(project, files, changed, diagnostics));
        }
        return result;
    }
    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, SvOutputMode mode)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var validation = Validate(paths, session); var diagnostics = validation.Diagnostics.ToList(); var writes = new List<PlannedFileWrite>();
        if (validation.IsValid) try
        {
            foreach (var output in Prepare(paths, validation.Session, diagnostics))
            {
                var info = SvWorkflowFileSource.CreatePlannedWrite(paths, output.Path, output.Sources, mode);
                writes.Add(new(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput, "Apply Blueberry changes."));
            }
            if (mode == SvOutputMode.Standalone)
            { var info = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths); writes.Add(new(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput, "Register Blueberry output.")); }
        }
        catch (Exception exception) when (SourceFailure(exception)) { diagnostics.Add(Error("Blueberry output could not be prepared.", "output")); }
        return SvChangePlanSourceGuard.Capture(paths, validation.Session, new ChangePlan(session.Id, writes, diagnostics) { EffectivePendingEdits = validation.Session.PendingEdits }, mode);
    }
    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed, SvOutputMode mode)
    {
        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths); using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var current = CreateChangePlan(paths, session, mode); var diagnostics = current.Diagnostics.ToList(); var written = new List<ProjectFileReference>();
        if (!ChangePlanReview.Matches(reviewed, current)) diagnostics.Add(Error("The Blueberry review is stale. Review Changes again.", "review"));
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error)) try
        {
            var outputs = Prepare(paths, session with { PendingEdits = current.EffectivePendingEdits ?? session.PendingEdits }, diagnostics);
            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            {
                foreach (var output in outputs) { SvWorkflowFileSource.Write(paths, output.Path, output.Bytes, mode); written.Add(SvEditSessionSupport.GeneratedReference(output.Path, mode)); }
                if (mode == SvOutputMode.Standalone) written.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }
        }
        catch (Exception exception) when (SourceFailure(exception)) { diagnostics.Add(Error("Blueberry output could not be written. Review the output status before retrying.", "output")); }
        return SvEditSessionSupport.CreateApplyResult(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, current, written, diagnostics);
    }
    private PendingEdit RebindMissing(PendingEdit edit, Source source)
    {
        if (edit.Domain != Domain || source.File.SourceLayer != ProjectFileLayer.Base || edit.Sources.Count != 1
            || edit.Sources[0].Layer != ProjectFileLayer.Layered || edit.Sources[0].RelativePath != source.File.RelativePath) return edit;
        var intent = Parse(edit); var row = source.Document.Rows.SingleOrDefault(row => row.Id == edit.RecordId);
        if (intent is null || row is null || edit.Field is null || !row.Values.TryGetValue(edit.Field, out var previous)) return edit;
        return edit with { Sources = [new(source.File.SourceLayer, source.File.RelativePath)], NewValue = JsonSerializer.Serialize(intent with { Revision = source.Document.Revision, Previous = previous }) };
    }
    private bool Resolve(Source source, PendingEdit edit, ICollection<ValidationDiagnostic> diagnostics, out int value)
    {
        value = 0; var intent = Parse(edit); var row = source.Document.Rows.SingleOrDefault(row => row.Id == edit.RecordId);
        if (edit.Domain != Domain || intent is null || intent.Revision != source.Document.Revision || row is null || edit.Field is null
            || !row.Values.TryGetValue(edit.Field, out var previous) || previous != intent.Previous || !SvBlueberryDocument.ValidValue(kind, edit.Field, intent.Value)
            || edit.Sources.Count != 1 || edit.Sources[0] != new ProjectFileReference(source.File.SourceLayer, source.File.RelativePath))
        { diagnostics.Add(Error("A staged Blueberry field is invalid or its source changed. Reload and stage again.", "sourceRevision")); return false; }
        value = intent.Value; return true;
    }
    private static Intent? Parse(PendingEdit edit)
    { if (edit.NewValue is not { Length: <= 256 }) return null; try { return JsonSerializer.Deserialize<Intent>(edit.NewValue); } catch (JsonException) { return null; } }
    private static bool SourceFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OverflowException;
    private ValidationDiagnostic Error(string message, string field) => new(DiagnosticSeverity.Error, message, "romfs/" + Path, Domain, field)
    { Code = field switch { "sourceRevision" or "review" => "KM-SV-BLUEBERRY-SOURCE-STALE", "source" => "KM-SV-BLUEBERRY-SOURCE-UNAVAILABLE", "output" => "KM-SV-BLUEBERRY-OUTPUT-FAILED", _ => "KM-SV-BLUEBERRY-EDIT-INVALID" } };
}
