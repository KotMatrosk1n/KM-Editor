// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.MainMissions;

public sealed record ZaMainMission20Slot(string Id, int Species, int OriginalSpecies);
public sealed record ZaMainMission20SpeciesOption(int Value, string Label);
public sealed record ZaMainMission20Update(string SlotId, int Species);
public sealed record ZaMainMission20Workflow(ZaWorkflowSummary Summary, string SourceRevision,
    string MissionTitle, IReadOnlyList<ZaMainMission20Slot> Slots,
    IReadOnlyList<ZaMainMission20SpeciesOption> SpeciesOptions, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record ZaMainMission20EditResult(EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed class ZaMainMission20WorkflowService(ProjectWorkspaceService workspace, ZaWorkflowFileSource files)
{
    internal const string Domain = "workflow.main-mission-20";
    private const string Label = "Main Mission 20";
    private const string Description = "Replace the Pokémon in the Old Building entrance cutscene.";
    private const string SpeciesField = "species";
    private const int MaximumSpecies = 65535;

    internal ZaWorkflowSummary CreateSummary(OpenedProject project) =>
        ZaWorkflowSupport.CreateSummary(project, ZaWorkflowIds.MainMission20, Label, Description);

    internal ZaMainMission20Workflow Load(ProjectPaths paths)
    {
        var project = workspace.Open(paths);
        return LoadState(project).ToWorkflow(project, files);
    }

    internal ZaMainMission20EditResult Stage(ProjectPaths paths, EditSession? session, string sourceRevision,
        IReadOnlyList<ZaMainMission20Update?>? updates)
    {
        using var reads = ZaWorkflowFileSource.BeginFreshReadScope(paths);
        workspace.ClearMemoryCache();
        var project = workspace.Open(paths);
        var state = LoadState(project);
        var original = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        if (!ZaEditSessionSupport.CanEdit(project, state.Summary, state.Diagnostics, Domain, diagnostics))
            return new(original, diagnostics.Concat(state.Diagnostics).Distinct().ToArray());

        if (sourceRevision != state.Script!.SourceRevision)
        {
            diagnostics.Add(Error("SOURCE-STALE", "The entrance cutscene source changed. Reload the editor before staging."));
            return new(original, diagnostics);
        }

        if (updates is null || updates.Count == 0 || updates.Count > state.Script.Slots.Count)
        {
            diagnostics.Add(Error("SELECTION-INVALID", "Select one or more entrance cutscene Pokémon."));
            return new(original, diagnostics);
        }

        var pending = original.PendingEdits.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (pending.Any(edit => edit.Domain == Domain && edit.Owner != sourceRevision))
        {
            diagnostics.Add(Error("SOURCE-STALE", "The staged cutscene selections refer to an earlier script. Discard them before staging new selections."));
            return new(original, diagnostics);
        }
        foreach (var update in updates)
        {
            var slot = state.Script.Slots.FirstOrDefault(slot => slot.Id == update?.SlotId);
            if (update is null || slot is null || !seen.Add(slot.Id) || !IsSpeciesAvailable(state.Availability!, update.Species))
            {
                diagnostics.Add(Error("SELECTION-INVALID", "Choose an available Pokémon for each changed cutscene slot."));
                return new(original, diagnostics);
            }

            pending.RemoveAll(edit => edit.Domain == Domain && edit.RecordId == slot.Id && edit.Field == SpeciesField);
            if (slot.Species == update.Species)
                continue;

            var value = update.Species.ToString(CultureInfo.InvariantCulture);
            pending.Add(new(Domain, $"{Label} {slot.Id} Pokémon = {value}", [state.Source!],
                slot.Id, SpeciesField, value, sourceRevision));
        }

        return new(original with { PendingEdits = pending.ToArray() }, diagnostics);
    }

    internal ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        using var reads = ZaWorkflowFileSource.BeginFreshReadScope(paths);
        var project = workspace.Open(paths);
        var state = LoadState(project);
        var diagnostics = new List<ValidationDiagnostic>();
        if (ZaEditSessionSupport.CanEdit(project, state.Summary, state.Diagnostics, Domain, diagnostics))
            _ = ResolveChanges(state, session, diagnostics);
        else
            diagnostics.AddRange(state.Diagnostics);

        return new(session, diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error), diagnostics.Distinct().ToArray());
    }

    internal ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, ZaOutputMode mode) =>
        ZaChangePlanSourceGuard.Capture(paths, session, () =>
            ZaEditSessionSupport.CreateSingleFileChangePlan(paths, session, Domain, ZaMainMission20Script.Path,
                Label, Validate(paths, session).Diagnostics, mode), mode);

    internal ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan, ZaOutputMode mode)
    {
        using var reads = ZaWorkflowFileSource.BeginFreshReadScope(paths);
        var current = CreateChangePlan(paths, session, mode);
        var diagnostics = current.Diagnostics.ToList();
        var written = new List<ProjectFileReference>();
        OutputApplyResult? transaction = null;
        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, current))
            diagnostics.Add(Error("PLAN-STALE", "The entrance cutscene source or output changed. Review the changes again."));

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            var state = LoadState(workspace.Open(paths));
            if (state.Script is null)
                diagnostics.AddRange(state.Diagnostics);
            else if (ResolveChanges(state, session, diagnostics) is { } changes)
            {
                try
                {
                    transaction = ZaWorkflowFileSource.WriteBatch(paths,
                        [new(ZaMainMission20Script.Path, state.Script.Write(changes))], mode,
                        revalidateReviewedState: () => ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                            reviewedPlan, CreateChangePlan(paths, session, mode)));
                    written.Add(ZaEditSessionSupport.GeneratedReference(ZaMainMission20Script.Path, mode));
                    if (mode == ZaOutputMode.Standalone)
                        written.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
                }
                catch (Exception exception) when (IsSourceFailure(exception))
                {
                    if (exception is ZaOutputApplyNotCommittedException failed)
                        transaction = failed.Result;
                    diagnostics.Add(Error("APPLY-FAILED", "The entrance cutscene output could not be applied."));
                }
            }
        }
        return ZaEditSessionSupport.CreateApplyResult(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            current, written, diagnostics, transaction);
    }

    private SourceState LoadState(OpenedProject project)
    {
        var summary = CreateSummary(project);
        if (summary.Availability == ZaWorkflowAvailability.Disabled)
            return new(summary, null, null, null, summary.Diagnostics);

        var diagnostics = new List<ValidationDiagnostic>();
        try
        {
            var baseline = files.ReadBase(project, ZaMainMission20Script.Path);
            var source = files.Read(project, ZaMainMission20Script.Path);
            var script = new ZaMainMission20Script(baseline.Bytes, source.Bytes);
            var availability = ZaPokemonAvailability.Load(project, files, diagnostics, Label);
            if (!availability.HasKnownAvailability)
                throw new InvalidDataException("The list of available Pokémon could not be read.");
            return new(summary, script, ZaWorkflowFileSource.CreateReference(source), availability, diagnostics);
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            diagnostics.Add(Error("SOURCE-UNAVAILABLE", "The entrance cutscene could not be loaded. Check the Legends Z-A 2.0.2 source and the current mod output."));
            return new(summary with { Availability = ZaWorkflowAvailability.Disabled }, null, null, null, diagnostics);
        }
    }

    private static Dictionary<string, int>? ResolveChanges(SourceState state, EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        var changes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var edit in session.PendingEdits)
        {
            if (edit.Domain != Domain || edit.Field != SpeciesField || edit.RecordId is null
                || edit.Owner != state.Script!.SourceRevision || !edit.Sources.SequenceEqual([state.Source!])
                || !state.Script.Slots.Any(slot => slot.Id == edit.RecordId)
                || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var species)
                || !IsSpeciesAvailable(state.Availability!, species) || !changes.TryAdd(edit.RecordId, species))
            {
                diagnostics.Add(Error("SESSION-INVALID", "The staged entrance cutscene selections are invalid or out of date. Stage the selections again."));
                return null;
            }
        }

        if (changes.Count != 0)
            return changes;

        diagnostics.Add(Error("SESSION-EMPTY", "Stage entrance cutscene Pokémon before reviewing changes."));
        return null;
    }

    private static bool IsSpeciesAvailable(ZaPokemonAvailability availability, int species) =>
        species is > 0 and <= MaximumSpecies && availability.IsPresentSpeciesForm(species, 0);

    private static bool IsSourceFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException;

    private static ValidationDiagnostic Error(string code, string message) =>
        new(DiagnosticSeverity.Error, message, $"romfs/{ZaMainMission20Script.Path}", Domain) { Code = $"KM-ZA-MAIN-MISSION-20-{code}" };

    private sealed record SourceState(ZaWorkflowSummary Summary, ZaMainMission20Script? Script,
        ProjectFileReference? Source, ZaPokemonAvailability? Availability, IReadOnlyList<ValidationDiagnostic> Diagnostics)
    {
        internal ZaMainMission20Workflow ToWorkflow(OpenedProject project, ZaWorkflowFileSource files)
        {
            if (Script is null || Availability is null)
                return new(Summary, "", "", [], [], Diagnostics);

            var diagnostics = Diagnostics.ToList();
            var labels = ZaTextLabelLookup.Load(project, files, diagnostics, project.Paths);
            var options = Availability.CreateSpeciesOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: false)
                .Where(option => IsSpeciesAvailable(Availability, option.Value))
                .Select(option => new ZaMainMission20SpeciesOption(option.Value, option.Label))
                .ToArray();
            var title = ZaMissionCatalog.TryGetMainMission(20, out var mission) ? labels.MissionTitle(mission) : Label;
            return new(Summary, Script.SourceRevision, title, Script.Slots, options, diagnostics);
        }
    }
}
