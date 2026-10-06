// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using KM.Formats.Models;

namespace KM.SV.TitanSwapper;

public sealed record SvTitanSpeciesOption(int Value, string Label);
public sealed record SvTitanSwapperWorkflow(SvWorkflowSummary Summary, string SourceRevision,
    IReadOnlyList<SvTitanSwapperRow> Rows, IReadOnlyList<SvTitanSpeciesOption> SpeciesOptions,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SvTitanSwapperUpdate(string RowId, string Field, int Value);
public sealed record SvTitanSwapperEditResult(SvTitanSwapperWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed class SvTitanSwapperService(ProjectWorkspaceService projects)
{
    public const string Domain = "workflow.titan-swapper";
    private const string TablePath = SvDataPaths.EventBattlePokemonArray;
    private const string CatalogPath = "pokemon/catalog/catalog/poke_resource_table.trpmcatalog";
    private readonly SvWorkflowFileSource files = new(bypassReusableBaseCache: true, maximumReadBytes: 32 * 1024 * 1024);
    private sealed record Intent(string Revision, int Previous, int Value, IReadOnlyList<string> Fingerprints,
        IReadOnlyDictionary<string, int> Row);
    private sealed record Source(SvTitanSwapperDocument Document, byte[] Script, string Revision,
        IReadOnlyList<ProjectFileReference> References, IReadOnlyList<SvTitanSpeciesOption> Species, IReadOnlyList<string> Fingerprints);

    public SvWorkflowSummary CreateSummary(OpenedProject project) => SvWorkflowSupport.CreateSummary(
        project, "titanSwapper", "Titan Swapper", "Choose a separate combat Pokemon for each Titan battle phase.");

    private Source Read(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        var project = projects.Open(paths);
        var data = files.Read(project, TablePath);
        var script = files.Read(project, SvTitanSwapperScript.VirtualPath);
        var personal = files.Read(project, SvDataPaths.PersonalArray);
        var catalogSource = files.Read(project, CatalogPath);
        SvTitanSwapperScript.ReadLabels(script.Bytes);
        var document = new SvTitanSwapperDocument(data.Bytes);
        var labels = SvTextLabelLookup.LoadPokemonNames(project, files, diagnostics, paths);
        var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(personal.Bytes));
        files.EnsureBoundedTableCount(table.EntryLength, "The S/V personal table");
        var options = new Dictionary<int, SvTitanSpeciesOption>();
        var catalog = new ModelBuffer(catalogSource.Bytes);
        var modeledSpecies = new HashSet<int>();
        foreach (var entry in catalog.Tables(catalog.Root, 1, 8192))
        {
            var identity = catalog.Table(entry, 0);
            if (identity == 0) throw new InvalidDataException("Missing model identity.");
            var form = catalog.Field(identity, 1);
            var speciesField = catalog.Field(identity, 0);
            if (speciesField == 0 || form != 0 && catalog.U16(form) != 0 || catalog.Text(entry, 1) is not { Length: > 0 } relative) continue;
            var path = TrinityPreviewReader.Resolve("pokemon/data/catalog", relative);
            if (!path.StartsWith("pokemon/data/", StringComparison.Ordinal) || !path.EndsWith(".trmdl", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid model path.");
            if (files.Exists(project, path)) modeledSpecies.Add(catalog.U16(speciesField));
        }
        for (var i = 0; i < table.EntryLength; i++)
            if (table.Entry(i) is { IsPresent: true, Species: { Form: 0, Species: > 0 } species } && modeledSpecies.Contains(species.Species))
                options.TryAdd(species.Species, new(species.Species, labels.Pokemon(species.Species)));
        if (options.Count == 0) throw new InvalidDataException("No available species.");
        string[] fingerprints = [document.Revision, Convert.ToHexString(SHA256.HashData(script.Bytes)),
            Convert.ToHexString(SHA256.HashData(personal.Bytes)),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Convert.ToHexString(SHA256.HashData(catalogSource.Bytes))
                + ":" + string.Join(',', options.Keys.Order()))))];
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(';', fingerprints))));
        return new(document, script.Bytes, revision,
            [new(data.SourceLayer, data.RelativePath), new(script.SourceLayer, script.RelativePath), new(personal.SourceLayer, personal.RelativePath), new(catalogSource.SourceLayer, catalogSource.RelativePath)],
            options.Values.OrderBy(option => option.Value).ToArray(), fingerprints);
    }

    public SvTitanSwapperWorkflow Load(ProjectPaths paths, EditSession? session = null)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var summary = CreateSummary(projects.Open(paths));
        var diagnostics = summary.Diagnostics.ToList();
        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame) || summary.Availability == SvWorkflowAvailability.Disabled)
            return new(summary, "", [], [], diagnostics);
        try
        {
            var source = Read(paths, diagnostics);
            var values = source.Document.Rows.ToDictionary(row => row.Id, row => row.Values.ToDictionary());
            foreach (var edit in Rebind(session?.PendingEdits ?? [], source).Where(edit => edit.Domain == Domain))
                if (Resolve(source, edit, paths, diagnostics, out var value)) values[edit.RecordId!][edit.Field!] = value;
            return new(summary, source.Revision, source.Document.Rows.Where(row => Supports(row, paths))
                .Select(row => row with { Values = values[row.Id] }).ToArray(), source.Species, diagnostics);
        }
        catch (Exception exception) when (SourceFailure(exception))
        {
            diagnostics.Add(Error("Titan Swapper could not read a supported event table and shared S/V 4.0.0 script. Check the source and reload.", "source"));
            return new(summary with { Availability = SvWorkflowAvailability.Disabled }, "", [], [], diagnostics);
        }
    }

    public SvTitanSwapperEditResult Stage(ProjectPaths paths, EditSession? session, string revision,
        IReadOnlyList<SvTitanSwapperUpdate> updates)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        try
        {
            var workflow = Load(paths, current);
            if (!SvEditSessionSupport.CanEdit(projects.Open(paths), workflow.Summary, workflow.Diagnostics, Domain, diagnostics))
                return new(workflow, current, diagnostics);
            var source = Read(paths, diagnostics);
            if (revision != source.Revision) diagnostics.Add(Error("Titan sources changed. Reload before staging.", "sourceRevision"));
            if (updates.Count is < 1 or > 39) diagnostics.Add(Error("Select a bounded set of Titan changes.", "session"));
            var edits = Rebind(current.PendingEdits, source).ToList();
            var seen = new HashSet<(string, string)>();
            foreach (var update in updates)
            {
                var row = source.Document.Rows.SingleOrDefault(row => row.Id == update.RowId);
                if (row is null || !Supports(row, paths) || !row.Values.TryGetValue(update.Field, out var previous)
                    || !Valid(source, update.Field, update.Value) || !seen.Add((update.RowId, update.Field)))
                { diagnostics.Add(Error("Select an available base form, a level from 1 to 100, and a valid Titan phase.", update.Field)); continue; }
                edits.RemoveAll(edit => edit.Domain == Domain && edit.RecordId == update.RowId && edit.Field == update.Field);
                if (previous != update.Value)
                    edits.Add(new(Domain, $"Set Titan {update.RowId} {update.Field} to {update.Value}.", source.References,
                        update.RowId, update.Field, JsonSerializer.Serialize(new Intent(source.Revision, previous, update.Value, source.Fingerprints, row.Values))));
            }
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return new(workflow, current, diagnostics);
            ValidateEnabledRows(source, edits.Where(edit => edit.Domain == Domain), paths, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return new(workflow, current, diagnostics);
            var desired = source.Document.Rows.ToDictionary(row => row.Id, row => row.Values.ToDictionary());
            foreach (var edit in edits.Where(edit => edit.Domain == Domain))
                desired[edit.RecordId!][edit.Field!] = ReadIntent(edit)!.Value;
            var staged = current with { PendingEdits = edits.Select(edit => edit.Domain == Domain
                ? edit with { NewValue = JsonSerializer.Serialize(ReadIntent(edit)! with { Row = desired[edit.RecordId!] }) } : edit).ToArray() };
            return new(Load(paths, staged), staged, diagnostics);
        }
        catch (Exception exception) when (SourceFailure(exception))
        { diagnostics.Add(Error("Titan changes could not be staged. Reload the editor and try again.", "session")); }
        return new(Load(paths, current), current, diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        try
        {
            var workflow = Load(paths);
            SvEditSessionSupport.CanEdit(projects.Open(paths), workflow.Summary, workflow.Diagnostics, Domain, diagnostics);
            if (session.PendingEdits.Count is < 1 or > 39) diagnostics.Add(Error("Stage a bounded set of Titan changes before review.", "session"));
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return new(session, false, diagnostics);
            var source = Read(paths, diagnostics);
            session = session with { PendingEdits = Rebind(session.PendingEdits, source) };
            var seen = new HashSet<(string?, string?)>();
            foreach (var edit in session.PendingEdits)
            {
                if (!seen.Add((edit.RecordId, edit.Field))) diagnostics.Add(Error("A Titan field is staged more than once.", "session"));
                Resolve(source, edit, paths, diagnostics, out _);
            }
            ValidateEnabledRows(source, session.PendingEdits, paths, diagnostics);
        }
        catch (Exception exception) when (SourceFailure(exception))
        { diagnostics.Add(Error("Titan changes could not be validated against the current source.", "source")); }
        return new(session, diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error), diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, SvOutputMode mode)
    {
        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        var writes = new List<PlannedFileWrite>();
        if (validation.IsValid)
            try
            {
                foreach (var path in new[] { TablePath, SvTitanSwapperScript.VirtualPath })
                {
                    var info = SvWorkflowFileSource.CreatePlannedWrite(paths, path,
                        validation.Session.PendingEdits.SelectMany(edit => edit.Sources).Distinct().ToArray(), mode);
                    writes.Add(new(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput, "Apply Titan Swapper combat replacements."));
                }
                if (mode == SvOutputMode.Standalone)
                {
                    var info = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                    writes.Add(new(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput, "Register Titan Swapper output."));
                }
            }
            catch (Exception exception) when (SourceFailure(exception))
            { diagnostics.Add(Error("Titan output could not be prepared.", "output")); }
        return SvChangePlanSourceGuard.Capture(paths, validation.Session,
            new ChangePlan(session.Id, writes, diagnostics) { EffectivePendingEdits = validation.Session.PendingEdits }, mode);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed, SvOutputMode mode)
    {
        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var current = CreateChangePlan(paths, session, mode);
        var diagnostics = current.Diagnostics.ToList();
        var written = new List<ProjectFileReference>();
        if (!ChangePlanReview.Matches(reviewed, current)) diagnostics.Add(Error("The Titan review is stale. Review Changes again.", "review"));
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
            try
            {
                var source = Read(paths, diagnostics);
                var values = source.Document.Rows.ToDictionary(row => row.Id, row => row.Values.ToDictionary());
                foreach (var edit in current.EffectivePendingEdits ?? session.PendingEdits)
                    if (Resolve(source, edit, paths, diagnostics, out var value)) values[edit.RecordId!][edit.Field!] = value;
                if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
                {
                    var data = source.Document.Write(values.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, int>)pair.Value));
                    var script = SvTitanSwapperScript.Apply(source.Script, values.Where(pair => pair.Value["enabled"] == 1).Select(pair => pair.Key));
                    SvWorkflowFileSource.Write(paths, TablePath, data, mode);
                    SvWorkflowFileSource.Write(paths, SvTitanSwapperScript.VirtualPath, script, mode);
                    written.Add(SvEditSessionSupport.GeneratedReference(TablePath, mode));
                    written.Add(SvEditSessionSupport.GeneratedReference(SvTitanSwapperScript.VirtualPath, mode));
                    if (mode == SvOutputMode.Standalone) written.Add(SvEditSessionSupport.GeneratedDescriptorReference());
                }
            }
            catch (Exception exception) when (SourceFailure(exception))
            { diagnostics.Add(Error("Titan output could not be written. Review the output status before retrying.", "output")); }
        return SvEditSessionSupport.CreateApplyResult(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, current, written, diagnostics);
    }

    private static bool Resolve(Source source, PendingEdit edit, ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics, out int value)
    {
        value = 0;
        try
        {
            var row = source.Document.Rows.SingleOrDefault(row => row.Id == edit.RecordId);
            var intent = ReadIntent(edit);
            if (edit.Domain != Domain || row is null || !Supports(row, paths) || intent is null || intent.Revision != source.Revision
                || edit.Field is null || !row.Values.TryGetValue(edit.Field, out var previous) || previous != intent.Previous
                || !Valid(source, edit.Field, intent.Value) || !edit.Sources.SequenceEqual(source.References)
                || !intent.Fingerprints.SequenceEqual(source.Fingerprints)) throw new InvalidDataException();
            value = intent.Value;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        { diagnostics.Add(Error("A staged Titan field is invalid or its source changed. Reload and stage it again.", "sourceRevision")); return false; }
    }

    // Called only after the enclosing mixed batch has authenticated its complete source snapshot.
    internal EditSession RebindVerifiedBatch(ProjectPaths paths, EditSession session)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var source = Read(paths, []);
        return session with { PendingEdits = session.PendingEdits.Select(edit =>
        {
            var intent = ReadIntent(edit) ?? throw new InvalidDataException("Invalid reviewed Titan intent.");
            var row = source.Document.Rows.Single(row => row.Id == edit.RecordId);
            return edit with { Sources = source.References, NewValue = JsonSerializer.Serialize(intent with
                { Revision = source.Revision, Previous = row.Values[edit.Field!], Fingerprints = source.Fingerprints }) };
        }).ToArray() };
    }

    private static Intent? ReadIntent(PendingEdit edit)
    {
        if (edit.NewValue is not { Length: <= 2048 }) return null;
        try { return JsonSerializer.Deserialize<Intent>(edit.NewValue) is { Fingerprints.Count: 4, Row.Count: 3 } intent
            && new[] { "enabled", "species", "level" }.All(intent.Row.ContainsKey) ? intent : null; }
        catch (JsonException) { return null; }
    }

    private static IReadOnlyList<PendingEdit> Rebind(IReadOnlyList<PendingEdit> edits, Source source)
    {
        var rebound = new List<PendingEdit>();
        foreach (var edit in edits)
        {
            var intent = edit.Domain == Domain ? ReadIntent(edit) : null;
            var row = source.Document.Rows.SingleOrDefault(row => row.Id == edit.RecordId);
            var removed = edit.Sources.Zip(source.References).Select(pair => pair.First.Layer == ProjectFileLayer.Layered
                && pair.Second.Layer == ProjectFileLayer.Base && pair.First.RelativePath == pair.Second.RelativePath).ToArray();
            if (intent is null || row is null || edit.Sources.Count != 4 || !removed.Any(value => value) || edit.Field is null
                || !row.Values.TryGetValue(edit.Field, out var previous)
                || !Enumerable.Range(0, 4).All(i => removed[i] || edit.Sources[i] == source.References[i] && intent.Fingerprints[i] == source.Fingerprints[i]))
            { rebound.Add(edit); continue; }
            var next = intent with { Revision = source.Revision, Previous = previous, Fingerprints = source.Fingerprints };
            rebound.Add(edit with { Sources = source.References, NewValue = JsonSerializer.Serialize(next) });
            if (removed[0])
                foreach (var (field, value) in intent.Row)
                    if (row.Values[field] != value && !edits.Concat(rebound).Any(other => other.Domain == Domain && other.RecordId == edit.RecordId && other.Field == field))
                        rebound.Add(edit with { Field = field, Sources = source.References,
                            Summary = $"Restore Titan {edit.RecordId} {field} to {value}.",
                            NewValue = JsonSerializer.Serialize(next with { Previous = row.Values[field], Value = value }) });
        }
        return rebound;
    }
    private static bool Supports(SvTitanSwapperRow row, ProjectPaths paths) => row.Edition == "both"
        || row.Edition == (paths.SelectedGame == ProjectGame.Scarlet ? "scarlet" : "violet");
    private static bool Valid(Source source, string field, int value) => field switch
    { "enabled" => value is 0 or 1, "level" => value is >= 1 and <= 100, "species" => source.Species.Any(option => option.Value == value), _ => false };
    private static void ValidateEnabledRows(Source source, IEnumerable<PendingEdit> edits, ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        var values = source.Document.Rows.ToDictionary(row => row.Id, row => row.Values.ToDictionary());
        foreach (var edit in edits)
            if (Resolve(source, edit, paths, diagnostics, out var value)) values[edit.RecordId!][edit.Field!] = value;
        foreach (var row in values.Values.Where(row => row["enabled"] == 1))
            if (!Valid(source, "species", row["species"]) || !Valid(source, "level", row["level"]))
                diagnostics.Add(Error("An enabled Titan replacement requires an available base form and a level from 1 to 100.", "species"));
    }
    private static bool SourceFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException or KeyNotFoundException;
    private static ValidationDiagnostic Error(string message, string field) => new(DiagnosticSeverity.Error, message,
        $"romfs/{TablePath}", Domain, field) { Code = field switch
        { "sourceRevision" or "review" => "KM-SV-TITAN-SWAPPER-SOURCE-STALE", "source" => "KM-SV-TITAN-SWAPPER-SOURCE-UNAVAILABLE",
            "output" => "KM-SV-TITAN-SWAPPER-OUTPUT-FAILED", _ => "KM-SV-TITAN-SWAPPER-EDIT-INVALID" } };
}
