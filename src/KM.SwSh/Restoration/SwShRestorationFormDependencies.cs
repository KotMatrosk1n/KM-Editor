// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

/// <summary>Requires the destinations requested by restored native form abilities.</summary>
internal static class SwShRestorationFormDependencies
{
    // Native Shields Down can move in both directions between each color's
    // meteor and core. Check at Review, after all selections have been staged,
    // so the two-way dependency can be installed or removed atomically.
    internal static void ValidateNative(SwShRestorationState state, Func<string, byte[]?> read)
        => Validate(state, read, Enumerable.Range(0, 14).Select(form =>
            (new SwShPokemonIdentity(774, form), 197, new SwShPokemonIdentity(774, form < 7 ? form + 7 : form - 7))).ToArray(),
            _ => "Shields Down");

    private static void Validate(SwShRestorationState state, Func<string, byte[]?> read,
        IReadOnlyList<(SwShPokemonIdentity Source, int Ability, SwShPokemonIdentity Destination)> requirements,
        Func<int, string> abilityName)
    {
        var enabled = SwShRestorationDependencies.Enabled(state);
        var requests = requirements.Where(r => enabled.Contains(r.Source)).ToArray();
        if (requests.Length == 0) return;
        var personal = read(SwShRestorationComposer.PersonalPath)
            ?? throw new InvalidDataException("Restored form abilities require personal data.");
        if (personal.Length % SwShPersonalTable.RecordSize != 0)
            throw new InvalidDataException("Restored form ability personal records are incomplete.");
        foreach (var request in requests)
        {
            var source = Record(SwShRestorationForms.PersonalRow(request.Source));
            var needed = false;
            for (var slot = 0; slot < 3; slot++)
                needed |= BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(24 + slot * 2, 2)) == request.Ability;
            if (!needed) continue;
            if (!enabled.Contains(request.Destination))
                throw new InvalidDataException($"{abilityName(request.Ability)} requires restored Pokemon {request.Destination.Species}, form {request.Destination.Form}, to be enabled in the same selection.");
            var destination = SwShRestorationForms.PersonalRow(request.Destination);
            var header = Record(SwShRestorationSpecies.BaseRow(request.Source.Species));
            var first = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(30, 2));
            if (request.Destination.Form != 0 && (header[32] <= request.Destination.Form || first == 0
                || first + request.Destination.Form - 1 != destination))
                throw new InvalidDataException("The restored form ability destination has no matching personal form header.");
            // Match SwShPersonalTable's IsPresentInGame flag, including any
            // effective personal customization applied in this transaction.
            if ((Record(destination)[0x21] & 0x40) == 0)
                throw new InvalidDataException("The restored form ability destination is not present in effective personal data.");
        }

        ReadOnlySpan<byte> Record(int row)
        {
            var offset = checked(row * SwShPersonalTable.RecordSize);
            if (offset < 0 || offset > personal.Length - SwShPersonalTable.RecordSize)
                throw new InvalidDataException("The restored form ability personal row is missing.");
            return personal.AsSpan(offset, SwShPersonalTable.RecordSize);
        }
    }
}
