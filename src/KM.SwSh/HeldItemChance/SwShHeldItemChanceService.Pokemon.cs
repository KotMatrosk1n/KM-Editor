// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Pokemon;

namespace KM.SwSh.HeldItemChance;

public sealed partial class SwShHeldItemChanceService
{
    private SwShHeldItemChanceWorkflow PopulatePokemon(ProjectPaths paths, State? state, EditSession? session,
        List<ValidationDiagnostic> diagnostics)
    {
        var result = new SwShHeldItemChanceWorkflow(state?.CanEdit == true, paths.SelectedGame, state?.Rates ?? [],
            state?.Layer.ToString().ToLowerInvariant() ?? "missing", diagnostics);
        if (state is null) return result;
        try
        {
            var pokemon = new SwShPokemonEditSessionService(workspace).ReadEffective(paths, session);
            diagnostics.AddRange(pokemon.Diagnostics);
            var desired = session is not null && session.PendingEdits.Any(edit => edit.Domain == Domain)
                ? Desired(paths, state, session with { PendingEdits = session.PendingEdits.Where(edit => edit.Domain == Domain).ToArray() }, diagnostics)
                : (Rates: state.Rates, Overrides: state.Overrides);
            return result with { CanEdit = result.CanEdit && !HasErrors(diagnostics), Rates = desired.Rates,
                Pokemon = pokemon.Workflow.Pokemon.Where(row => row.SpeciesId is > 0 and <= 898 && row.Form is >= 0 and <= 255)
                    .Select(row => {
                        var custom = desired.Overrides.SingleOrDefault(value => value.Species == row.SpeciesId && value.Form == row.Form);
                        return new SwShHeldItemChancePokemon(row.PersonalId, row.SpeciesId, row.Form,
                            row.Name, row.FormLabel, row.Type1, row.Type2,
                            [row.Personal.HeldItem1, row.Personal.HeldItem2, row.Personal.HeldItem3], custom?.Rates ?? desired.Rates, custom is not null);
                    }).ToArray(),
                ItemOptions = pokemon.Workflow.EditableFields.Single(field => field.Field == "heldItem1").Options
                    .Select(option => new SwShHeldItemOption(option.Value, option.Label)).ToArray() };
        }
        catch (Exception exception) when (IsSourceError(exception) || exception is InvalidOperationException)
        {
            diagnostics.Add(Error(SourceCode, "Pokemon held item data could not be loaded. Check sources and refresh."));
            return result with { CanEdit = false };
        }
    }

    public SwShHeldItemChanceResult StagePokemon(ProjectPaths paths, IReadOnlyList<SwShHeldItemChanceUpdate> updates, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        var workflow = Load(paths, current);
        diagnostics.AddRange(workflow.Diagnostics);
        if (!workflow.CanEdit || updates.Count is < 1 or > 2048 || updates.Select(row => row.PersonalId).Distinct().Count() != updates.Count)
            diagnostics.Add(Error(SessionCode, "Choose distinct Pokemon records in an editable project."));
        foreach (var update in updates)
            if (!workflow.Pokemon.Any(row => row.PersonalId == update.PersonalId)
                || update.Items.Count != 3 || update.Items.Any(item => !workflow.ItemOptions.Any(option => option.Value == item))
                || update.Rates is not null && !SwShHeldItemChancePatcher.AreValid(update.Rates))
                diagnostics.Add(Error(RatesCode, "Choose three valid items and normal and boosted percentages totaling at most 100% each."));
        if (HasErrors(diagnostics)) return new(workflow, current, diagnostics);
        var state = Read(paths, diagnostics);
        if (state is null) return new(workflow, current, diagnostics);
        var candidate = current;
        var editor = new SwShPokemonEditSessionService(workspace);
        var itemChanges = updates.SelectMany(update => {
            var row = workflow.Pokemon.Single(value => value.PersonalId == update.PersonalId);
            return Enumerable.Range(0, 3).Where(slot => update.Items[slot] != row.Items[slot])
                .Select(slot => new SwShPokemonFieldUpdate(row.PersonalId, $"heldItem{slot + 1}", update.Items[slot].ToString(CultureInfo.InvariantCulture)));
        }).ToArray();
        if (itemChanges.Length > 0)
        {
            var changed = editor.UpdateFields(paths, candidate, itemChanges);
            diagnostics.AddRange(changed.Diagnostics);
            if (HasErrors(diagnostics)) return new(workflow, current, diagnostics);
            candidate = changed.Session;
        }
        foreach (var update in updates)
        {
            var row = workflow.Pokemon.Single(value => value.PersonalId == update.PersonalId);
            var retained = candidate.PendingEdits.Where(edit => edit.Domain != Domain || edit.RecordId != row.PersonalId.ToString(CultureInfo.InvariantCulture));
            var existing = state.Overrides.SingleOrDefault(value => value.Species == row.Species && value.Form == row.Form);
            var equal = update.Rates is null ? existing is null : existing is not null && existing.Rates.SequenceEqual(update.Rates);
            candidate = candidate with { PendingEdits = (equal ? retained : retained.Append(CreatePokemonEdit(row.PersonalId, row.Species, row.Form, update.Rates))).ToArray() };
        }
        var loaded = Load(paths, candidate);
        diagnostics.AddRange(loaded.Diagnostics);
        return HasErrors(diagnostics) ? new(workflow, current, diagnostics) : new(loaded, candidate, diagnostics);
    }

    private static PendingEdit CreatePokemonEdit(int personalId, int species, int form, IReadOnlyList<int>? rates)
    {
        var payload = string.Join(',', new[] { species, form }.Concat(rates ?? [-1]).Select(value => value.ToString(CultureInfo.InvariantCulture)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new(Domain, "Stage Pokemon held item percentages.", [new(ProjectFileLayer.Pending, $"pending/held-item-chance/pokemon/{hash}")],
            personalId.ToString(CultureInfo.InvariantCulture), "rates", payload);
    }

    private (int[] Rates, IReadOnlyList<SwShHeldItemChanceOverride> Overrides) Desired(ProjectPaths paths, State state,
        EditSession session, List<ValidationDiagnostic> diagnostics)
    {
        var rates = state.Rates;
        var rows = state.Overrides.ToDictionary(row => (row.Species, row.Form));
        var edits = session.PendingEdits;
        if (edits.Count is < 1 or > 2049 || edits.Any(edit => edit.Domain != Domain)
            || edits.Select(edit => edit.RecordId).Distinct().Count() != edits.Count)
        { diagnostics.Add(Error(SessionCode, "Stage distinct Pokemon held item percentages before review.")); return (rates, rows.Values.ToArray()); }
        var global = edits.Where(edit => edit.RecordId == "global-held-items").ToArray();
        if (global.Length > 0) rates = Decode(session with { PendingEdits = global }, diagnostics);
        var pokemonEdits = edits.Where(edit => edit.RecordId != "global-held-items").ToArray();
        if (pokemonEdits.Length == 0) return (rates, rows.Values.ToArray());
        var pokemon = new SwShPokemonWorkflowService().Load(workspace.Open(paths));
        diagnostics.AddRange(pokemon.Diagnostics);
        foreach (var edit in pokemonEdits)
        {
            var row = pokemon.Pokemon.SingleOrDefault(row => row.PersonalId.ToString(CultureInfo.InvariantCulture) == edit.RecordId);
            var values = (edit.NewValue ?? "").Split(',').Select(value => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : -2).ToArray();
            var reset = values.Length == 3 && values[2] == -1;
            if (row is null || values.Length < 2 || row.SpeciesId != values[0] || row.Form != values[1]
                || !reset && !SwShHeldItemChancePatcher.AreValid(values.Skip(2).ToArray()))
            { diagnostics.Add(Error(SessionCode, "The staged Pokemon identity or percentages changed. Stage the record again.")); continue; }
            var expected = CreatePokemonEdit(row.PersonalId, row.SpeciesId, row.Form, reset ? null : values.Skip(2).ToArray());
            if (edit.Field != expected.Field || edit.Summary != expected.Summary || edit.NewValue != expected.NewValue || !edit.Sources.SequenceEqual(expected.Sources))
            { diagnostics.Add(Error(SessionCode, "The staged Pokemon percentages are inconsistent. Stage them again.")); continue; }
            if (reset) rows.Remove((row.SpeciesId, row.Form));
            else rows[(row.SpeciesId, row.Form)] = new(row.SpeciesId, row.Form, values.Skip(2).ToArray());
        }
        return (rates, rows.Values.ToArray());
    }

    private static bool SameSettings(IReadOnlyList<int> leftRates, IReadOnlyList<SwShHeldItemChanceOverride> left,
        IReadOnlyList<int> rightRates, IReadOnlyList<SwShHeldItemChanceOverride> right) => leftRates.SequenceEqual(rightRates)
        && left.Count == right.Count && left.All(row => right.Any(other => row.Species == other.Species && row.Form == other.Form && row.Rates.SequenceEqual(other.Rates)));
}
