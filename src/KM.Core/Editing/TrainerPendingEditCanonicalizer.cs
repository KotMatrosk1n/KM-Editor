// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.Core.Editing;

public readonly record struct TrainerPokemonSlotIdentity(int TrainerId, int Slot);

public static class TrainerPendingEditCanonicalizer
{
    public static EditSession Canonicalize(
        EditSession session,
        string domain,
        string speciesIdField,
        string formField,
        Func<string?, bool> isPokemonField,
        Func<PendingEdit, TrainerPokemonSlotIdentity?> resolveSlotIdentity,
        Func<int, IReadOnlyDictionary<int, bool>?> resolveSourceTrainerSlots,
        Func<PendingEdit, int?> resolveSourceFieldValue)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesIdField);
        ArgumentException.ThrowIfNullOrWhiteSpace(formField);
        ArgumentNullException.ThrowIfNull(isPokemonField);
        ArgumentNullException.ThrowIfNull(resolveSlotIdentity);
        ArgumentNullException.ThrowIfNull(resolveSourceTrainerSlots);
        ArgumentNullException.ThrowIfNull(resolveSourceFieldValue);

        var indexedEdits = session.PendingEdits
            .Select((edit, index) => new IndexedPendingEdit(edit, index))
            .ToArray();
        var groups = indexedEdits
            .Where(candidate => IsResolvablePokemonEdit(
                candidate.Edit,
                domain,
                isPokemonField,
                resolveSlotIdentity,
                resolveSourceTrainerSlots))
            .GroupBy(candidate => resolveSlotIdentity(candidate.Edit)!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        if (groups.Count == 0)
        {
            return session;
        }

        var firstIndexes = groups.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Min(candidate => candidate.Index));
        var normalizedGroups = groups.ToDictionary(
            pair => pair.Key,
            pair => CanonicalizeSlot(
                pair.Value,
                speciesIdField,
                formField,
                resolveSourceFieldValue));
        var pendingEdits = new List<PendingEdit>(session.PendingEdits.Count);

        foreach (var candidate in indexedEdits)
        {
            var identity = resolveSlotIdentity(candidate.Edit);
            if (identity is null
                || !groups.ContainsKey(identity.Value)
                || !string.Equals(candidate.Edit.Domain, domain, StringComparison.Ordinal)
                || !isPokemonField(candidate.Edit.Field)
                || resolveSourceTrainerSlots(identity.Value.TrainerId) is not { } slots
                || !slots.ContainsKey(identity.Value.Slot))
            {
                pendingEdits.Add(candidate.Edit);
                continue;
            }

            if (candidate.Index == firstIndexes[identity.Value])
            {
                pendingEdits.AddRange(normalizedGroups[identity.Value]);
            }
        }

        return session with { PendingEdits = pendingEdits };
    }

    private static IReadOnlyList<PendingEdit> CanonicalizeSlot(
        IReadOnlyList<IndexedPendingEdit> candidates,
        string speciesIdField,
        string formField,
        Func<PendingEdit, int?> resolveSourceFieldValue)
    {
        var latestByField = candidates
            .GroupBy(candidate => candidate.Edit.Field, StringComparer.Ordinal)
            .Select(group => group.MaxBy(candidate => candidate.Index)!)
            .OrderBy(candidate => candidate.Index)
            .Where(candidate => !IsSourceEquivalent(candidate.Edit, resolveSourceFieldValue))
            .ToArray();
        var speciesEdit = latestByField.LastOrDefault(candidate => string.Equals(
            candidate.Edit.Field,
            speciesIdField,
            StringComparison.Ordinal));
        var explicitlyClearsSpecies = speciesEdit is not null
            && int.TryParse(
                speciesEdit.Edit.NewValue,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var speciesId)
            && speciesId == 0;

        return latestByField
            .Where(candidate => !explicitlyClearsSpecies
                || string.Equals(candidate.Edit.Field, speciesIdField, StringComparison.Ordinal))
            .OrderBy(candidate => GetPriority(candidate.Edit.Field, speciesIdField, formField))
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Edit)
            .ToArray();
    }

    private static bool IsResolvablePokemonEdit(
        PendingEdit edit,
        string domain,
        Func<string?, bool> isPokemonField,
        Func<PendingEdit, TrainerPokemonSlotIdentity?> resolveSlotIdentity,
        Func<int, IReadOnlyDictionary<int, bool>?> resolveSourceTrainerSlots)
    {
        var identity = resolveSlotIdentity(edit);
        return string.Equals(edit.Domain, domain, StringComparison.Ordinal)
            && isPokemonField(edit.Field)
            && identity is not null
            && resolveSourceTrainerSlots(identity.Value.TrainerId)?.ContainsKey(identity.Value.Slot) == true;
    }

    private static bool IsSourceEquivalent(
        PendingEdit edit,
        Func<PendingEdit, int?> resolveSourceFieldValue)
    {
        return int.TryParse(
                edit.NewValue,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value)
            && resolveSourceFieldValue(edit) == value;
    }

    private static int GetPriority(string? field, string speciesIdField, string formField)
    {
        if (string.Equals(field, speciesIdField, StringComparison.Ordinal))
        {
            return 0;
        }

        return string.Equals(field, formField, StringComparison.Ordinal) ? 1 : 2;
    }

    private sealed record IndexedPendingEdit(PendingEdit Edit, int Index);
}
