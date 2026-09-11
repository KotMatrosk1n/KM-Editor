// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SV.Starmobiles;
using KM.SV.Workflows;
using KM.SV.Data;

namespace KM.SV.Starmobiles;

public sealed record SvStarmobilesWorkflow(SvWorkflowSummary Summary, string SourceRevision,
    IReadOnlyList<SvStarmobileRow> Rows, IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public IReadOnlyList<SvStarmobileMoveOption> MoveOptions { get; init; } = [];
    public IReadOnlyList<SvStarmobileAbilityOption> AbilityOptions { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> VanillaValues { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, int>>();
}
public sealed record SvStarmobileMoveOption(int Value, string Label, bool CanSelect);
public sealed record SvStarmobileAbilityOption(int Value, string Label);
public sealed record SvStarmobileUpdate(string RowId, string Field, int Value);
public sealed record SvStarmobilesEditResult(SvStarmobilesWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed class SvStarmobilesService
{
    public const string Domain = "workflow.starmobiles";
    public const string VirtualPath = "world/data/danbattle/boss/dan_car_battle_data/dan_car_battle_data_array.bin";
    private readonly ProjectWorkspaceService projects;
    private readonly SvWorkflowFileSource files;
    private sealed record Intent(string Revision, int Previous, int Value);

    public SvStarmobilesService(ProjectWorkspaceService projects)
    {
        this.projects = projects;
        files = new SvWorkflowFileSource(bypassReusableBaseCache: true,
            maximumReadBytes: SvStarmobileDocument.MaximumSourceBytes);
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project) => SvWorkflowSupport.CreateSummary(
        project, "starmobiles", "Starmobiles", "Edit Team Star Starmobile levels and combat stats.");

    public SvStarmobilesWorkflow Load(ProjectPaths paths, EditSession? session = null)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var project = projects.Open(paths);
        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame) || summary.Availability == SvWorkflowAvailability.Disabled)
            return new(summary, "", [], diagnostics);
        try
        {
            var source = files.Read(project, VirtualPath);
            var document = new SvStarmobileDocument(source.Bytes);
            var vanillaValues = LoadVanillaValues(project, document, diagnostics);
            var moveOptions = LoadMoveOptions(project, diagnostics);
            var abilityOptions = LoadAbilityOptions(project, diagnostics);
            var values = document.Rows.ToDictionary(row => row.Id, row => row.Values.ToDictionary());
            foreach (var stagedEdit in session?.PendingEdits.Where(edit => edit.Domain == Domain) ?? [])
            {
                var edit = RebindMissingOutput(stagedEdit, source, document);
                if (TryResolve(document, edit, moveOptions, abilityOptions, out var value, diagnostics))
                    values[edit.RecordId!][edit.Field!] = value;
            }
            return new(summary, document.Revision,
                document.Rows.Select(row => row with { Values = values[row.Id] }).ToArray(), diagnostics)
                { MoveOptions = moveOptions, AbilityOptions = abilityOptions, VanillaValues = vanillaValues };
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            diagnostics.Add(Error("Starmobile data could not be read. Check the selected source and reload the editor.", "source"));
            return new(summary with { Availability = SvWorkflowAvailability.Disabled }, "", [], diagnostics);
        }
    }

    public SvStarmobilesEditResult Stage(ProjectPaths paths, EditSession? session,
        string revision, IReadOnlyList<SvStarmobileUpdate> updates)
    {
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        try
        {
            var project = projects.Open(paths);
            var workflow = Load(paths, current);
            if (!SvEditSessionSupport.CanEdit(project, workflow.Summary, workflow.Diagnostics, Domain, diagnostics))
                return new(workflow, current, diagnostics);
            if (updates.Count is < 1 or > 512 || current.PendingEdits.Count(edit => edit.Domain == Domain) > 512)
                throw new InvalidDataException("Invalid Starmobile edit session.");
            var source = files.Read(project, VirtualPath);
            var document = new SvStarmobileDocument(source.Bytes);
            if (revision != document.Revision)
            {
                diagnostics.Add(Error("Starmobile data changed. Reload before staging.", "sourceRevision"));
                return new(workflow, current, diagnostics);
            }
            var edits = current.PendingEdits.Select(edit => RebindMissingOutput(edit, source, document)).ToList();
            var targets = new HashSet<(string, string)>();
            foreach (var update in updates)
            {
                var row = document.Rows.SingleOrDefault(row => row.Id == update.RowId);
                if (row is null || !row.Values.TryGetValue(update.Field, out var previous)
                    || !ValidValue(row, update.Field, update.Value, workflow.MoveOptions, workflow.AbilityOptions) || !targets.Add((update.RowId, update.Field)))
                {
                    diagnostics.Add(Error(update.Field == "speed" ? "Starmobile Speed is determined by its locked signature move." : "Select an existing Starmobile field and enter a value within its range.", update.Field));
                    continue;
                }
                edits.RemoveAll(edit => edit.Domain == Domain && edit.RecordId == update.RowId && edit.Field == update.Field);
                if (previous != update.Value)
                    edits.Add(new PendingEdit(Domain,
                        $"Set Starmobile {update.RowId} {update.Field} to {update.Value}.",
                        [new ProjectFileReference(source.SourceLayer, source.RelativePath)],
                        update.RowId, update.Field,
                        JsonSerializer.Serialize(new Intent(document.Revision, previous, update.Value))));
            }
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                return new(workflow, current, diagnostics);
            ValidateMoveOrder(document, edits, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                return new(workflow, current, diagnostics);
            var staged = current with { PendingEdits = edits };
            return new(Load(paths, staged), staged, diagnostics);
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            diagnostics.Add(Error("Starmobile changes could not be staged. Reload the editor and try again.", "session"));
            return new(Load(paths, current), current, diagnostics);
        }
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        try
        {
            var project = projects.Open(paths);
            var workflow = Load(paths);
            SvEditSessionSupport.CanEdit(project, workflow.Summary, workflow.Diagnostics, Domain, diagnostics);
            if (session.PendingEdits.Count is < 1 or > 512)
                diagnostics.Add(Error("Stage a bounded set of Starmobile changes before review.", "session"));
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                return new(session, false, diagnostics);
            var source = files.Read(project, VirtualPath);
            var document = new SvStarmobileDocument(source.Bytes);
            session = session with
            {
                PendingEdits = session.PendingEdits.Select(edit => RebindMissingOutput(edit, source, document)).ToArray(),
            };
            var targets = new HashSet<(string?, string?)>();
            foreach (var edit in session.PendingEdits)
            {
                if (!targets.Add((edit.RecordId, edit.Field)))
                    diagnostics.Add(Error("A Starmobile field is staged more than once.", "session"));
                TryResolve(document, edit, workflow.MoveOptions, workflow.AbilityOptions, out _, diagnostics);
                if (edit.Sources.Count != 1 || edit.Sources[0] != new ProjectFileReference(source.SourceLayer, source.RelativePath))
                    diagnostics.Add(Error("The staged Starmobile source changed. Reload and stage again.", "sourceRevision"));
            }
            if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
                ValidateMoveOrder(document, session.PendingEdits, diagnostics);
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            diagnostics.Add(Error("Starmobile changes could not be validated against the current source.", "source"));
        }
        return new(session, diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error), diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, SvOutputMode mode)
    {
        var validation = Validate(paths, session);
        return SvChangePlanSourceGuard.Capture(paths, validation.Session,
            SvEditSessionSupport.CreateSingleFileChangePlan(paths, validation.Session, Domain, VirtualPath,
                "Starmobiles", validation.Diagnostics, mode), mode);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed, SvOutputMode mode)
    {
        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
        using var fresh = SvWorkflowFileSource.BeginFreshReadScope(paths);
        var id = Guid.NewGuid().ToString("N");
        var time = DateTimeOffset.UtcNow;
        var current = CreateChangePlan(paths, session, mode);
        var diagnostics = current.Diagnostics.ToList();
        var written = new List<ProjectFileReference>();
        if (!ChangePlanReview.Matches(reviewed, current))
            diagnostics.Add(Error("The Starmobile review is stale. Review Changes again before applying.", "review"));
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            try
            {
                var source = files.Read(projects.Open(paths), VirtualPath);
                var document = new SvStarmobileDocument(source.Bytes);
                session = session with
                {
                    PendingEdits = session.PendingEdits.Select(edit => RebindMissingOutput(edit, source, document)).ToArray(),
                };
                var moveOptions = session.PendingEdits.Any(edit => SvStarmobileDocument.MoveFields.Contains(edit.Field ?? ""))
                    ? LoadMoveOptions(projects.Open(paths), diagnostics) : [];
                var changes = new List<(string, string, int)>();
                var abilityOptions = session.PendingEdits.Any(edit => edit.Field == "ability")
                    ? LoadAbilityOptions(projects.Open(paths), diagnostics) : [];
                foreach (var edit in session.PendingEdits)
                    if (TryResolve(document, edit, moveOptions, abilityOptions, out var value, diagnostics)) changes.Add((edit.RecordId!, edit.Field!, value));
                if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
                {
                    SvWorkflowFileSource.Write(paths, VirtualPath, document.Write(changes), mode);
                    written.Add(SvEditSessionSupport.GeneratedReference(VirtualPath, mode));
                    if (mode == SvOutputMode.Standalone) written.Add(SvEditSessionSupport.GeneratedDescriptorReference());
                }
            }
            catch (Exception exception) when (IsSourceFailure(exception))
            {
                diagnostics.Add(Error("Starmobile output could not be written. Review the output status before retrying.", "output"));
            }
        }
        return SvEditSessionSupport.CreateApplyResult(id, time, current, written, diagnostics);
    }

    private static PendingEdit RebindMissingOutput(PendingEdit edit, SvWorkflowFile source,
        SvStarmobileDocument document)
    {
        // The file reader returns Base only after checking every supported output form.
        // Retain the pending value against vanilla when its former output is gone.
        if (edit.Domain != Domain || source.SourceLayer != ProjectFileLayer.Base
            || edit.Sources.Count != 1 || edit.Sources[0].Layer != ProjectFileLayer.Layered
            || !string.Equals(edit.Sources[0].RelativePath, source.RelativePath, StringComparison.OrdinalIgnoreCase)
            || edit.Field is null || edit.NewValue is null || edit.NewValue.Length > 256)
            return edit;
        try
        {
            var intent = JsonSerializer.Deserialize<Intent>(edit.NewValue);
            var row = document.Rows.SingleOrDefault(row => row.Id == edit.RecordId);
            if (intent is null || row is null || !row.Values.TryGetValue(edit.Field, out var previous))
                return edit;
            return edit with
            {
                Sources = [new ProjectFileReference(source.SourceLayer, source.RelativePath)],
                NewValue = JsonSerializer.Serialize(new Intent(document.Revision, previous, intent.Value)),
            };
        }
        catch (JsonException)
        {
            return edit;
        }
    }

    private static bool TryResolve(SvStarmobileDocument document, PendingEdit edit,
        IReadOnlyList<SvStarmobileMoveOption> moveOptions, IReadOnlyList<SvStarmobileAbilityOption> abilityOptions, out int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        value = 0;
        try
        {
            if (edit.Domain != Domain || edit.NewValue is null || edit.NewValue.Length > 256)
                throw new InvalidDataException();
            var intent = JsonSerializer.Deserialize<Intent>(edit.NewValue);
            var row = document.Rows.SingleOrDefault(row => row.Id == edit.RecordId);
            if (intent is null || intent.Revision != document.Revision || row is null || edit.Field is null
                || !row.Values.TryGetValue(edit.Field, out var previous) || previous != intent.Previous)
                throw new InvalidDataException();
            if (!ValidValue(row, edit.Field, intent.Value, moveOptions, abilityOptions))
            {
                diagnostics.Add(Error(edit.Field == "speed" ? "Starmobile Speed is determined by its locked signature move." : "Select an existing Starmobile field and enter a value within its range.", edit.Field));
                return false;
            }
            value = intent.Value;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            diagnostics.Add(Error("A staged Starmobile field is invalid or its source changed. Reload and stage it again.", "sourceRevision"));
            return false;
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> LoadVanillaValues(
        OpenedProject project, SvStarmobileDocument document, ICollection<ValidationDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, int>>();
        try
        {
            var vanilla = new SvStarmobileDocument(files.ReadBase(project, VirtualPath).Bytes);
            foreach (var row in document.Rows)
            {
                var matches = vanilla.Rows.Where(candidate => candidate.BossType == row.BossType
                    && candidate.Difficulty == row.Difficulty && candidate.TrainerId == row.TrainerId
                    && candidate.EventId == row.EventId).ToArray();
                if (matches.Length == 1 && row.Values.Keys.All(matches[0].Values.ContainsKey))
                    result.Add(row.Id, matches[0].Values);
            }
            if (result.Count == document.Rows.Count) return result;
        }
        catch (Exception exception) when (IsSourceFailure(exception)) { }
        diagnostics.Add(new(DiagnosticSeverity.Warning,
            "Original Starmobile values are unavailable for one or more records. Check the base source before resetting them.",
            $"romfs/{VirtualPath}", Domain, "vanilla"));
        return result;
    }

    private static bool ValidValue(SvStarmobileRow row, string field, int value,
        IReadOnlyList<SvStarmobileMoveOption> moveOptions, IReadOnlyList<SvStarmobileAbilityOption> abilityOptions)
    {
        if (field is "type1" or "type2") return value is >= 0 and <= 17;
        if (field == "ability") return abilityOptions.Any(option => option.Value == value);
        if (SvStarmobileDocument.Fields.Contains(field) && field != "speed")
            return value >= 1 && value <= (field is "level" or "hpMultiplier" ? 100 : byte.MaxValue);
        return field is "move2" or "move3" or "move4"
            && row.Values.TryGetValue(field, out var current) && !SvStarmobileDocument.IsSignatureMove(current)
            && !SvStarmobileDocument.IsSignatureMove(value)
            && moveOptions.Any(option => option.Value == value && option.CanSelect);
    }

    private static IReadOnlyList<SvStarmobileMoveOption> LoadMoveOptions(OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = new SvWorkflowFileSource();
            var labels = SvTextLabelLookup.LoadMoveNames(project, source, diagnostics);
            var table = global::SvMoveDataArray.GetRootAsSvMoveDataArray(
                new ByteBuffer(source.Read(project, SvDataPaths.MoveDataArray).Bytes));
            source.EnsureBoundedTableCount(table.ValuesLength, "The S/V move table");
            var options = new Dictionary<int, SvStarmobileMoveOption>();
            for (var index = 0; index < table.ValuesLength; index++)
            {
                if (table.Values(index) is not { } move) continue;
                if (!options.TryAdd(move.MoveId, new(move.MoveId, labels.Move(move.MoveId),
                        move.MoveId > 0 && move.CanUseMove && !SvStarmobileDocument.IsSignatureMove(move.MoveId))))
                    throw new InvalidDataException("Duplicate move ID.");
            }
            if (options.Count == 0) throw new InvalidDataException("Missing move records.");
            options[0] = new(0, "None", true);
            return options.Values.OrderBy(option => option.Value).ToArray();
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            diagnostics.Add(new(DiagnosticSeverity.Warning,
                "Starmobile moves could not be loaded. Reload the editor before editing moves.",
                $"romfs/{SvDataPaths.MoveDataArray}", Domain, "moves"));
            return [];
        }
    }

    private static IReadOnlyList<SvStarmobileAbilityOption> LoadAbilityOptions(OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var labels = SvTextLabelLookup.LoadAbilityNames(project, new SvWorkflowFileSource(), diagnostics);
            if (labels.AbilityNameCount is < 2 or > 65536) throw new InvalidDataException("Missing ability records.");
            return Enumerable.Range(0, labels.AbilityNameCount)
                .Select(value => new SvStarmobileAbilityOption(value, value == 0 ? "None" : labels.Ability(value))).ToArray();
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            diagnostics.Add(new(DiagnosticSeverity.Warning,
                "Starmobile abilities could not be loaded. Reload the editor before editing abilities.",
                $"romfs/{VirtualPath}", Domain, "ability"));
            return [];
        }
    }

    private static void ValidateMoveOrder(SvStarmobileDocument document, IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var group in edits.Where(edit => edit.Domain == Domain
                     && SvStarmobileDocument.MoveFields.Contains(edit.Field ?? "")).GroupBy(edit => edit.RecordId))
        {
            var row = document.Rows.Single(candidate => candidate.Id == group.Key);
            var values = row.Values.ToDictionary();
            foreach (var edit in group) values[edit.Field!] = JsonSerializer.Deserialize<Intent>(edit.NewValue!)!.Value;
            var empty = false;
            foreach (var field in SvStarmobileDocument.MoveFields)
            {
                if (values.GetValueOrDefault(field) == 0) empty = true;
                else if (empty)
                {
                    diagnostics.Add(Error("Fill Starmobile move slots in order, leaving empty slots at the end.", "moves"));
                    break;
                }
            }
        }
    }
    private static bool IsSourceFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or OverflowException;
    private static ValidationDiagnostic Error(string message, string field) => new(
        DiagnosticSeverity.Error, message, $"romfs/{VirtualPath}", Domain, field)
        { Code = field switch
            {
                "sourceRevision" or "review" => "KM-SV-STARMOBILES-SOURCE-STALE",
                "source" => "KM-SV-STARMOBILES-SOURCE-UNAVAILABLE",
                "output" => "KM-SV-STARMOBILES-OUTPUT-FAILED",
                _ => "KM-SV-STARMOBILES-EDIT-INVALID",
            }
        };
}
