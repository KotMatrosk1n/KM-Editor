// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

/// <summary>Maps registered form identities and derives their shared personal headers.</summary>
public static partial class SwShRestorationForms
{
    public static bool IsMega(SwShPokemonIdentity identity) =>
        SwShRestorationFormCandidates.ResolveFormLabel(identity.Species, identity.Form)
            ?.StartsWith("Mega", StringComparison.Ordinal) == true;

    // Native egg-file identities are independent of personal rows. New Mega
    // forms use separate files while earlier restorations retain their paths.
    public static int EggRow(SwShPokemonIdentity identity) => IsMega(identity) && identity.Species != 6
        ? checked(2048 + PersonalRow(identity)) : PersonalRow(identity);
    public static string EggPath(int row) => $"romfs/bin/pml/tamagowaza/tamagowaza_{row:0000}.bin";

    public static int PersonalRow(SwShPokemonIdentity identity) => identity.Form == 0 ? SwShRestorationSpecies.BaseRow(identity.Species)
        : SwShRestorationDefinitions.All.Any(d => d.Identities.Contains(identity))
            && SwShRestorationFormSlots.PlannedPersonalRow(identity.Species, identity.Form) is { } row ? row
        : throw new InvalidDataException("Restoration form has no registered personal reservation.");

    internal static bool IsRegisteredRow(int row) => row is >= 1 and <= SwShEffectivePokemonCatalog.MaximumSpecies
        || row == SwShRestorationSpecies.AnnihilapeRow
        || SwShRestorationDefinitions.All.SelectMany(d => d.Identities).Any(i => i.Form != 0 && PersonalRow(i) == row);

    internal static byte[] NormalizeHeaders(byte[] native, byte[] current, SwShRestorationState state)
    {
        SwShRestorationFormSlots.Validate(native, current);
        var result = current.ToArray();
        foreach (var group in EnabledForms(state).GroupBy(i => i.Species))
        {
            var offset = group.Key * SwShPersonalTable.RecordSize;
            var first = group.Select(i => PersonalRow(i) - i.Form + 1).Distinct().Single();
            if (BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(offset + 30)) != first || result[offset + 32] != Math.Max(SwShRestorationFormSlots.Blocks.Single(b => b.Species == group.Key).NativeAlternateCount, group.Max(i => i.Form)) + 1)
                throw new InvalidDataException("Restored form ownership changed outside the restoration workflow.");
            native.AsSpan(offset + 30, 3).CopyTo(result.AsSpan(offset + 30));
            // An independently restored base owns its ordinary one-form header.
            if (state.Selections.Where(s => s.Enabled).SelectMany(s => SwShRestorationDefinitions.Get(s.DefinitionId).Identities)
                .Contains(new(group.Key, 0))) result[offset + 32] = 1;
        }
        return result;
    }

    public static byte[] ApplyHeaders(byte[] current, IEnumerable<SwShPokemonIdentity> identities)
    {
        var result = current.ToArray();
        foreach (var group in identities.Where(i => i.Form != 0).GroupBy(i => i.Species))
        {
            var offset = checked(group.Key * SwShPersonalTable.RecordSize);
            var first = group.Select(i => PersonalRow(i) - i.Form + 1).Distinct().Single();
            if (result.Length % SwShPersonalTable.RecordSize != 0 || offset + SwShPersonalTable.RecordSize > result.Length
                || group.Any(i => PersonalRow(i) >= result.Length / SwShPersonalTable.RecordSize)
                || BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(offset + 30)) != SwShRestorationFormSlots.Blocks.Single(b => b.Species == group.Key).NativeFirstRow)
                throw new InvalidDataException("Restoration cannot replace another form block.");
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset + 30), checked((ushort)first));
            result[offset + 32] = checked((byte)(Math.Max(SwShRestorationFormSlots.Blocks.Single(b => b.Species == group.Key).NativeAlternateCount, group.Max(i => i.Form)) + 1));
        }
        return result;
    }

    internal static IEnumerable<SwShPokemonIdentity> EnabledForms(SwShRestorationState state) => state.Selections.Where(s => s.Enabled)
        .SelectMany(s => SwShRestorationDefinitions.Get(s.DefinitionId).Identities).Where(i => i.Form != 0);
}
