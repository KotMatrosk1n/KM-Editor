// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

public static partial class SwShRestorationForms
{
    internal static byte[] BaselinePersonal(byte[] native, byte[] personal, SwShPokemonIdentity[] forms)
    {
        var result = personal.ToArray();
        foreach (var group in forms.GroupBy(i => i.Species))
        {
            var block = SwShRestorationFormSlots.Blocks.Single(b => b.Species == group.Key);
            if (block.NativeFirstRow == block.FirstRow) continue;
            for (var form = 1; form <= block.NativeAlternateCount; form++)
            {
                if (group.Any(i => i.Form == form)) continue;
                var source = checked((block.NativeFirstRow + form - 1) * SwShPersonalTable.RecordSize);
                var destination = checked(block.PersonalRow(form) * SwShPersonalTable.RecordSize);
                if (source + SwShPersonalTable.RecordSize > native.Length || destination + SwShPersonalTable.RecordSize > result.Length)
                    throw new InvalidDataException("Native form defaults have an incompatible length.");
                native.AsSpan(source, SwShPersonalTable.RecordSize).CopyTo(result.AsSpan(destination));
            }
        }
        return ApplyHeaders(result, forms);
    }

    internal static void ValidateEggContributions(Func<string, byte[]?> native, SwShPreparedRestoration prepared)
    {
        var identities = SwShRestorationDefinitions.Get(prepared.DefinitionId).Identities;
        var eggs = prepared.Contributions.Where(c => c.Path.StartsWith("romfs/bin/pml/tamagowaza/", StringComparison.Ordinal)).ToArray();
        if (eggs.Length != identities.Count) throw new InvalidDataException("Prepare this restoration again with the current egg-move mapping.");
        foreach (var identity in identities)
        {
            var matches = eggs.Where(c => c.Path == EggPath(EggRow(identity))).ToArray();
            if (matches.Length != 1 || matches[0].Writer != SwShRestorationWriter.File)
                throw new InvalidDataException("Prepare this restoration again with separate form egg-move reservations.");
            ValidateEgg(matches[0].After);
            var first = identity.Form == 0 && identity.Species <= 898
                ? BinaryPrimitives.ReadUInt16LittleEndian(native(EggPath(identity.Species))
                    ?? throw new InvalidDataException("Native egg list is missing.")) : EggRow(identity);
            if (BinaryPrimitives.ReadUInt16LittleEndian(matches[0].After) != first)
                throw new InvalidDataException("Prepare this restoration again to preserve its native egg-move mapping.");
        }
    }

    internal static SwShRestorationContribution NormalizeRetainedEgg(SwShRestorationContribution item)
    {
        if (item.Writer != SwShRestorationWriter.File || item.Before is null
            || !item.Path.StartsWith("romfs/bin/pml/tamagowaza/", StringComparison.Ordinal)) return item;
        var block = SwShRestorationFormSlots.Blocks.SingleOrDefault(b => EggPath(b.Species) == item.Path);
        if (block is null || !Enumerable.Range(1, block.AlternateCount).Any(form => HasSeparateEggFiles(new(block.Species, form))))
            return item;

        ValidateEgg(item.Before); ValidateEgg(item.After);
        var nativeFirst = BinaryPrimitives.ReadUInt16LittleEndian(item.Before);
        var restoredFirst = checked((ushort)(OlderFormEggOffset + block.FirstRow));
        if (nativeFirst == restoredFirst || BinaryPrimitives.ReadUInt16LittleEndian(item.After) != restoredFirst) return item;

        // The alternate form owns this derived pointer. Keep any edits to the move list.
        var egg = item.After.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(egg, nativeFirst);
        return item with { After = egg };
    }

    /// <summary>Remove or derive shared form mappings around ordinary contribution composition.</summary>
    internal static IReadOnlyDictionary<string, byte[]?> Storage(Func<string, byte[]?> native,
        Func<string, byte[]?> current, SwShRestorationState state, bool normalize)
    {
        var outputs = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        byte[] Required(string path) => current(path) ?? throw new InvalidDataException($"Form storage is missing: {path}");
        var originalPersonal = native(SwShRestorationComposer.PersonalPath)
            ?? throw new InvalidDataException("Native personal data is missing.");
        var personal = Required(SwShRestorationComposer.PersonalPath);
        if (normalize) personal = NormalizeHeaders(originalPersonal, personal, state);
        outputs[SwShRestorationComposer.PersonalPath] = personal;

        void CloneFile(string source, string destination)
        {
            var value = Required(source);
            var baseline = native(destination);
            var actual = current(destination);
            if (!(normalize ? Equal(actual, value) : Equal(actual, baseline)))
                throw new InvalidDataException($"Native form copy changed outside the restoration workflow: {destination}");
            outputs[destination] = normalize ? baseline : value.ToArray();
        }

        void CloneRecord(string path, int size, int source, int destination)
        {
            var value = outputs.TryGetValue(path, out var changed) ? changed! : Required(path).ToArray();
            var original = native(path) ?? throw new InvalidDataException("Native form table is missing.");
            if (value.Length % size != 0 || original.Length % size != 0 || (source + 1) * size > value.Length)
                throw new InvalidDataException("Native form table has an incompatible length.");
            var expected = value.AsSpan(source * size, size).ToArray();
            var baseline = destination < original.Length / size ? original.AsSpan(destination * size, size).ToArray()
                : SwShRestorationSpecies.EmptyRecord(size);
            var actual = destination < value.Length / size ? value.AsSpan(destination * size, size).ToArray()
                : SwShRestorationSpecies.EmptyRecord(size);
            if (!actual.AsSpan().SequenceEqual(normalize ? expected : baseline))
                throw new InvalidDataException("Native form record changed outside the restoration workflow.");
            if (destination >= value.Length / size)
            {
                var first = value.Length / size;
                Array.Resize(ref value, checked((destination + 1) * size));
                for (var row = first; row <= destination; row++) SwShRestorationSpecies.EmptyRecord(size).CopyTo(value, row * size);
            }
            (normalize ? baseline : expected).CopyTo(value, destination * size);
            outputs[path] = value;
        }

        foreach (var group in EnabledForms(state).GroupBy(i => i.Species))
        {
            var block = SwShRestorationFormSlots.Blocks.Single(b => b.Species == group.Key);
            if (block.NativeFirstRow != block.FirstRow)
                for (var form = 1; form <= block.NativeAlternateCount; form++)
                {
                    if (group.Any(i => i.Form == form)) continue;
                    var source = block.NativeFirstRow + form - 1;
                    var destination = block.PersonalRow(form);
                    CloneRecord(SwShRestorationComposer.PersonalPath, SwShPersonalTable.RecordSize, source, destination);
                    CloneRecord(SwShRestorationComposer.LearnsetPath, 260, source, destination);
                    CloneFile($"romfs/bin/pml/evolution/evo_{source:000}.bin", $"romfs/bin/pml/evolution/evo_{destination:000}.bin");
                }

            if (!group.Any(HasSeparateEggFiles)) continue;
            var eggPath = EggPath(group.Key);
            var nativeEgg = native(eggPath) ?? throw new InvalidDataException("Native base egg list is missing.");
            var egg = Required(eggPath).ToArray();
            ValidateEgg(nativeEgg); ValidateEgg(egg);
            var nativeFirst = BinaryPrimitives.ReadUInt16LittleEndian(nativeEgg);
            var restoredFirst = checked((ushort)(OlderFormEggOffset + block.FirstRow));
            var currentFirst = BinaryPrimitives.ReadUInt16LittleEndian(egg);
            // Older base egg lists used the species number as their form pointer.
            var legacyMegaMapping = !normalize && currentFirst == group.Key && group.Any(IsMega);
            if (currentFirst != (normalize ? restoredFirst : nativeFirst) && !legacyMegaMapping)
                throw new InvalidDataException("Form egg mapping changed outside the restoration workflow.");
            BinaryPrimitives.WriteUInt16LittleEndian(egg, normalize ? nativeFirst : restoredFirst);
            outputs[eggPath] = egg;
            for (var form = 1; form <= block.NativeAlternateCount; form++)
                if (!group.Any(i => i.Form == form))
                    CloneFile(EggPath(nativeFirst + form - 1), EggPath(OlderFormEggOffset + block.PersonalRow(form)));
        }
        if (!normalize) outputs[SwShRestorationComposer.PersonalPath] = ApplyHeaders(outputs[SwShRestorationComposer.PersonalPath]!, EnabledForms(state));
        return outputs;
    }

    private static void ValidateEgg(byte[] data)
    {
        if (data.Length < 4 || data.Length != 4 + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)) * 2)
            throw new InvalidDataException("Egg move list has an incompatible length.");
    }
    private static bool Equal(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
}
