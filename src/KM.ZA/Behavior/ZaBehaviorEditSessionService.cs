// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.ZA.Workflows;

namespace KM.ZA.Behavior;

internal sealed class ZaBehaviorEditSessionService(ProjectWorkspaceService workspace,
    ZaBehaviorWorkflowService workflow, ZaWorkflowFileSource files)
{
    internal ZaBehaviorEditResult Update(ProjectPaths paths, EditSession? session, IReadOnlyList<ZaBehaviorUpdate?>? updates)
    {
        var original = session ?? EditSession.Start();
        using var reads = ZaWorkflowFileSource.BeginFreshReadScope(paths);
        workspace.ClearMemoryCache();
        var project = workspace.Open(paths);
        var state = workflow.LoadState(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var pending = original.PendingEdits.ToList();
        if (!ZaEditSessionSupport.CanEdit(project, state.Workflow.Summary, state.Workflow.Diagnostics,
                ZaBehaviorSettings.Domain, diagnostics)) return new(state.Workflow, original, diagnostics);
        if (updates is null || updates.Count is 0 or > 4096)
            diagnostics.Add(Error("VALUE-INVALID", "Provide one or more Behavior changes."));
        var seen = new HashSet<(string, string)>();
        foreach (var update in updates ?? [])
        {
            if (update is null || update.EntryId is null || !state.Sources.TryGetValue(update.EntryId, out var source)
                || update.Field is null || update.Value is null || !seen.Add((update.EntryId, update.Field)))
            { diagnostics.Add(Error("SELECTION-INVALID", "The Behavior selection is unavailable or repeated.")); break; }
            try
            {
                var value = Normalize(update.Field, update.Value);
                var species = state.Workflow.Resources.Single(r => r.EntryId == update.EntryId).SpeciesId;
                if (update.Field == "cityRoaming")
                {
                    if (state.City is null || !ZaCityRoamingDocument.Accepts(state.City.Status(species), value)) throw new InvalidDataException();
                    // AI routines belong to the species, including all its resource variants.
                    pending.RemoveAll(e => e.Domain == ZaBehaviorSettings.Domain && e.Field == "cityRoaming"
                        && e.RecordId is not null && e.RecordId.StartsWith(update.EntryId[..4] + "_", StringComparison.Ordinal));
                }
                if (update.Field == "restoreVanilla") pending.RemoveAll(e => Owns(e, update.EntryId) && e.Field != "cityRoaming");
                pending.RemoveAll(e => Owns(e, update.EntryId) && e.Field == update.Field);
                var baseline = new ZaBehaviorDocument(source.Bytes);
                var equivalent = update.Field == "cityRoaming" ? value == ZaCityRoamingDocument.Choice(state.City!.Status(species))
                    : update.Field == "cityProfile" ? value == ZaBehaviorSettings.CityProfile(baseline.Tags)
                    : update.Field == "profile" ? value == ZaBehaviorSettings.Profile(baseline.Tags)
                    : Array.FindIndex(ZaBehaviorSettings.Fields, f => f.Field == update.Field) is var i && i >= 0
                        && value == ZaBehaviorSettings.Format(baseline.Values[i]);
                // A scalar/profile override after restore is compared to the restored state during compilation.
                if (!equivalent || pending.Any(e => Owns(e, update.EntryId) && e.Field == "restoreVanilla"))
                {
                    var references = new List<ProjectFileReference> { new(source.SourceLayer, source.RelativePath) };
                    if (state.Catalog is not null) references.Add(new(state.Catalog.SourceLayer, state.Catalog.RelativePath));
                    if (update.Field == "restoreVanilla") references.Add(new(ProjectFileLayer.Base, source.RelativePath));
                    if (update.Field == "cityRoaming") references.AddRange(state.CitySources!.Values.Select(s => new ProjectFileReference(s.SourceLayer, s.RelativePath)));
                    pending.Add(new(ZaBehaviorSettings.Domain, $"Behavior {update.EntryId}: {update.Field} = {value}",
                        references, update.EntryId, update.Field, value));
                }
            }
            catch (Exception e) when (ZaBehaviorSettings.SourceFailure(e))
            { diagnostics.Add(Error("VALUE-INVALID", "The Behavior value is invalid.", update.Field)); }
        }
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return new(state.Workflow, original, diagnostics);
        var candidate = original with { PendingEdits = pending.ToArray() };
        var outputs = Compile(project, state, candidate, diagnostics);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return new(state.Workflow, original, diagnostics);
        // Return current source values. The editor overlays the persisted session so refresh and discard
        // have the same behavior as initial load, including staged vanilla restoration.
        return new(state.Workflow, candidate, diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        using var reads = ZaWorkflowFileSource.BeginFreshReadScope(paths);
        var project = workspace.Open(paths); var state = workflow.LoadState(project);
        var diagnostics = new List<ValidationDiagnostic>();
        if (session.PendingEdits.Any(e => e.Domain != ZaBehaviorSettings.Domain) || session.PendingEdits.Count == 0)
            diagnostics.Add(Error("SESSION-INVALID", "Stage Behavior changes before reviewing this session."));
        if (ZaEditSessionSupport.CanEdit(project, state.Workflow.Summary, state.Workflow.Diagnostics, ZaBehaviorSettings.Domain, diagnostics))
            _ = Compile(project, state, session, diagnostics);
        return new(session, diagnostics.All(d => d.Severity != DiagnosticSeverity.Error), diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, ZaOutputMode mode) =>
        ZaChangePlanSourceGuard.Capture(paths, session, () =>
        {
            var validation = Validate(paths, session);
            if (!validation.IsValid) return new(session.Id, [], validation.Diagnostics);
            var project = workspace.Open(paths); var state = workflow.LoadState(project);
            var diagnostics = validation.Diagnostics.ToList();
            var outputs = Compile(project, state, session, diagnostics);
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return new(session.Id, [], diagnostics);
            var writes = new List<PlannedFileWrite>();
            foreach (var path in outputs.Keys)
            {
                var info = ZaWorkflowFileSource.CreatePlannedWrite(paths, path,
                    session.PendingEdits.SelectMany(e => e.Sources).Distinct().ToArray(), mode);
                writes.Add(new(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput, "Apply reviewed Behavior settings."));
            }
            if (mode == ZaOutputMode.Standalone)
            {
                var info = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new(info.TargetRelativePath, info.Sources, info.ReplacesExistingOutput, "Register Behavior output resources."));
            }
            return new(session.Id, writes, validation.Diagnostics);
        }, mode);

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed, ZaOutputMode mode)
    {
        using var reads = ZaWorkflowFileSource.BeginFreshReadScope(paths);
        var current = CreateChangePlan(paths, session, mode);
        var diagnostics = current.Diagnostics.ToList();
        var written = new List<ProjectFileReference>(); OutputApplyResult? transaction = null;
        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewed, current))
            diagnostics.Add(Error("PLAN-STALE", "Behavior sources or output changed. Review the changes again."));
        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            try
            {
                var project = workspace.Open(paths); var state = workflow.LoadState(project);
                var outputs = Compile(project, state, session, diagnostics);
                if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
                {
                    transaction = ZaWorkflowFileSource.WriteBatch(paths,
                        outputs.Select(p => new ZaWorkflowFileWrite(p.Key, p.Value)).ToArray(), mode,
                        revalidateReviewedState: () => ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewed, CreateChangePlan(paths, session, mode)));
                    written.AddRange(outputs.Keys.Select(path => ZaEditSessionSupport.GeneratedReference(path, mode)));
                    if (mode == ZaOutputMode.Standalone) written.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
                }
            }
            catch (Exception e) when (ZaBehaviorSettings.SourceFailure(e))
            {
                if (e is ZaOutputApplyNotCommittedException failed) transaction = failed.Result;
                written.Clear(); diagnostics.Add(Error("APPLY-FAILED", "Behavior output could not be applied. Check the sources and output folder, then review again."));
            }
        }
        return ZaEditSessionSupport.CreateApplyResult(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, current, written, diagnostics, transaction);
    }

    private Dictionary<string, byte[]> Compile(OpenedProject project, ZaBehaviorState state, EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var group in session.PendingEdits.Where(e => e.Domain == ZaBehaviorSettings.Domain).GroupBy(e => e.RecordId, StringComparer.Ordinal))
        {
            try
            {
                if (group.Key is null || !state.Sources.TryGetValue(group.Key, out var source)
                    || group.Select(e => e.Field).Distinct().Count() != group.Count()) throw new InvalidDataException();
                var edits = group.ToArray();
                foreach (var edit in edits) _ = Normalize(edit.Field ?? "", edit.NewValue ?? "");
                if (edits.All(e => e.Field == "cityRoaming")) continue;
                var bytes = edits.Any(e => e.Field == "restoreVanilla") ? files.ReadBase(project, source.VirtualPath).Bytes : source.Bytes;
                var doc = new ZaBehaviorDocument(bytes); var tags = doc.Tags.ToList(); var values = doc.Values.ToArray();
                foreach (var edit in edits.OrderBy(e => e.Field == "initialize" ? 1 : 0))
                {
                    var value = Normalize(edit.Field ?? "", edit.NewValue ?? "");
                    if (edit.Field is "restoreVanilla" or "cityRoaming") continue;
                    if (edit.Field == "profile")
                    { tags.RemoveAll(ZaBehaviorSettings.IsTemperament); tags.AddRange(ZaBehaviorSettings.Profiles[value]); }
                    else if (edit.Field == "cityProfile")
                    {
                        // Retail city AI can replace Warlike with Gentle. Use its native, per-resource
                        // override instead of changing the shared conversion or combat routines.
                        tags.RemoveAll(ZaBehaviorSettings.CityProfiles.ContainsValue);
                        if (value != "default") tags.Add(ZaBehaviorSettings.CityProfiles[value]);
                    }
                    else if (edit.Field == "initialize")
                    {
                        foreach (var tag in new[] { "State_usually", "WildTeam" }) if (!tags.Contains(tag)) tags.Add(tag);
                        if (!tags.Any(ZaBehaviorSettings.IsTemperament)) tags.Add("Warlike");
                    }
                    else values[Array.FindIndex(ZaBehaviorSettings.Fields, f => f.Field == edit.Field)] = float.Parse(value, CultureInfo.InvariantCulture);
                }
                outputs.Add(source.VirtualPath, doc.Write(tags.ToArray(), values));
            }
            catch (Exception e) when (ZaBehaviorSettings.SourceFailure(e))
            { diagnostics.Add(Error("SESSION-INVALID", "A staged Behavior change cannot be resolved against the current source.")); }
        }
        var cityEdits = session.PendingEdits.Where(e => e.Domain == ZaBehaviorSettings.Domain && e.Field == "cityRoaming").ToArray();
        if (cityEdits.Length != 0)
        {
            try
            {
                var changes = new Dictionary<int, string>();
                foreach (var edit in cityEdits)
                {
                    var resource = state.Workflow.Resources.Single(r => r.EntryId == edit.RecordId);
                    var choice = Normalize("cityRoaming", edit.NewValue ?? "");
                    if (!changes.TryAdd(resource.SpeciesId, choice) && changes[resource.SpeciesId] != choice) throw new InvalidDataException();
                }
                if (state.City is null) throw new InvalidDataException();
                var result = state.City.Write(changes);
                outputs.Add(ZaCityRoamingDocument.TemplatePath, result.Templates);
                if (result.Scenarios is { } scenario)
                    outputs.Add(ZaCityRoamingDocument.ScenarioPath, scenario);
            }
            catch (Exception e) when (ZaBehaviorSettings.SourceFailure(e))
            { diagnostics.Add(Error("CITY-ROAMING-UNAVAILABLE", "City roaming cannot be changed with the current AI data. Review the sources and stage again.", "cityRoaming")); }
        }
        return outputs;
    }
    private static string Normalize(string field, string value)
    {
        if (field == "profile" && ZaBehaviorSettings.Profiles.ContainsKey(value)) return value;
        if (field == "cityProfile" && (value == "default" || ZaBehaviorSettings.CityProfiles.ContainsKey(value))) return value;
        if (field is "initialize" or "restoreVanilla" && value == "true") return value;
        if (field == "cityRoaming" && value is "true" or "false" or "default" or "off" or "ground" or "hover") return value;
        if (ZaBehaviorSettings.Fields.Any(f => f.Field == field)
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && float.IsFinite(number) && number >= 0) return ZaBehaviorSettings.Format(number);
        throw new InvalidDataException();
    }
    private static bool Owns(PendingEdit edit, string id) => edit.Domain == ZaBehaviorSettings.Domain && edit.RecordId == id;
    private static ValidationDiagnostic Error(string suffix, string message, string? field = null) => ZaBehaviorSettings.Error(suffix, message, field);
}
