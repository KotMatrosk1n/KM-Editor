// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Editing;

public static class TrainerFieldUpdateOrdering
{
    public static IReadOnlyList<TUpdate> IdentityFirst<TUpdate>(
        IReadOnlyList<TUpdate> updates,
        Func<TUpdate, string?> fieldSelector,
        string speciesIdField,
        string formField)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(fieldSelector);

        return updates
            .Select((update, index) => new
            {
                Index = index,
                Priority = GetPriority(fieldSelector(update), speciesIdField, formField),
                Update = update,
            })
            .OrderBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Update)
            .ToArray();
    }

    private static int GetPriority(string? field, string speciesIdField, string formField)
    {
        var normalizedField = field?.Trim();
        if (string.Equals(normalizedField, speciesIdField, StringComparison.Ordinal))
        {
            return 0;
        }

        return string.Equals(normalizedField, formField, StringComparison.Ordinal) ? 1 : 2;
    }
}
