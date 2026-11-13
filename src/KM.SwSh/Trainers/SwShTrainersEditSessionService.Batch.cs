// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Trainers;

public sealed partial class SwShTrainersEditSessionService
{
    internal SwShTrainersEditResult CreateBatchSession(ProjectPaths paths, IReadOnlyList<SwShTrainerFieldUpdate> updates)
    {
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var source = LoadForSession(project, StartSession(), updates);
        var diagnostics = new List<ValidationDiagnostic>();
        var empty = StartSession();
        if (!CanEditTrainers(project, source, diagnostics)) return new(source, empty, diagnostics);
        if (updates.Count > 250_000)
        {
            diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, "Trainer batch exceeds its supported size.", field: "updates"));
            return new(source, empty, diagnostics);
        }
        var originals = source.Trainers.ToDictionary(row => row.TrainerId);
        var effective = new Dictionary<int, SwShTrainerRecord>(originals);
        var pending = new Dictionary<(string? Record, string? Field), PendingEdit>();
        var resolver = SwShPokemonAbilityOptionResolver.Load(project);
        foreach (var update in TrainerFieldUpdateOrdering.IdentityFirst(RemoveDependentsOfBatchClears(updates),
                     static update => update.Field, SwShTrainersWorkflowService.SpeciesIdField, SwShTrainersWorkflowService.FormField))
        {
            if (update.Slot is null || !effective.TryGetValue(update.TrainerId, out var trainer))
            {
                diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error, "Trainer batch requires an existing trainer and party slot.", field: "updates"));
                continue;
            }
            var edit = CreatePendingEdit(trainer, update.Slot, update.Field, update.Value, diagnostics);
            if (edit is null) continue;
            edit = AddSemanticValidationSource(project, edit);
            var key = (edit.RecordId, edit.Field);
            if (GetTrainerFieldValue(originals[update.TrainerId], update.Slot, edit.Field) is { } original
                && edit.NewValue == original.ToString(CultureInfo.InvariantCulture)) pending.Remove(key);
            else pending[key] = edit;
            var single = source with { Trainers = [trainer] };
            effective[update.TrainerId] = OverlayPendingEdit(single, edit, resolver).Trainers[0];
        }
        if (diagnostics.Any(row => row.Severity == DiagnosticSeverity.Error)) return new(source, empty, diagnostics);
        var session = CanonicalizePendingTrainerEdits(source, empty with { PendingEdits = pending.Values.ToArray() });
        var projected = OverlayPendingEdits(source, session.PendingEdits, resolver);
        ValidateFinalTeamOrder(source, projected, session.PendingEdits, diagnostics);
        return diagnostics.Any(row => row.Severity == DiagnosticSeverity.Error)
            ? new(source, empty, diagnostics) : new(projected, session, diagnostics);
    }
}
