// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

/// <summary>Changes only a registered regional route in an existing native parent's table.</summary>
internal static class SwShRestorationNativeEvolution
{
    internal static (byte[] Bytes, SwShRestorationContribution? Retained) Compose(
        SwShRestorationContribution item, SwShPokemonIdentity target, byte[]? current,
        bool enable, bool allowOwnedCustomizations, bool adopt, bool captureOnly)
    {
        if (item.Before is null || current is null)
            throw new InvalidDataException($"Native evolution source is missing: {item.Path}.");
        var before = SwShEvolutionSet.Parse(item.Before);
        var after = SwShEvolutionSet.Parse(item.After);
        var actual = SwShEvolutionSet.Parse(current);
        bool Matches(SwShEvolutionRecord row) => row.Species == target.Species && row.Form == target.Form;
        var desired = after.Evolutions.Where(Matches).ToArray();
        var present = actual.Evolutions.Where(Matches).ToArray();
        if (before.Evolutions.Any(Matches) || desired.Length != 1 || present.Length > 1)
            throw new InvalidDataException($"Regional evolution identity is missing or ambiguous: {item.Path}.");

        var result = current.ToArray();
        var desiredBytes = item.After.AsSpan(desired[0].Slot * SwShEvolutionSet.RecordSize, SwShEvolutionSet.RecordSize);
        if (captureOnly)
            return (result, present.Length == 0 ? null : Retain(present[0].Slot * SwShEvolutionSet.RecordSize));
        if (present.Length == 0)
        {
            if (!enable) return (result, null);
            var slot = Enumerable.Range(0, SwShEvolutionSet.MaxEvolutionCount)
                .FirstOrDefault(slot => actual.Evolutions.All(row => row.Slot != slot), -1);
            if (slot < 0) throw new InvalidDataException($"The native parent's evolution table is full: {item.Path}.");
            desiredBytes.CopyTo(result.AsSpan(slot * SwShEvolutionSet.RecordSize));
            return (result, null);
        }

        var offset = present[0].Slot * SwShEvolutionSet.RecordSize;
        var actualBytes = current.AsSpan(offset, SwShEvolutionSet.RecordSize);
        if (!actualBytes.SequenceEqual(desiredBytes) && !allowOwnedCustomizations && !adopt)
            throw new InvalidDataException($"Restoration contribution conflicts with the current regional evolution: {item.Path}.");
        if (enable)
        {
            if (adopt) desiredBytes.CopyTo(result.AsSpan(offset));
            return (result, null);
        }

        // Retain the regional customization without importing unrelated parent routes.
        result.AsSpan(offset, SwShEvolutionSet.RecordSize).Clear();
        return (result, Retain(offset));

        SwShRestorationContribution Retain(int offset)
        {
            var retained = item.After.ToArray();
            current.AsSpan(offset, SwShEvolutionSet.RecordSize)
                .CopyTo(retained.AsSpan(desired[0].Slot * SwShEvolutionSet.RecordSize));
            return item with { After = retained };
        }
    }
}
