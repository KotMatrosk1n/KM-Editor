// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Pokemon;

namespace KM.ZA.Items;

internal static class ZaTechnicalMachineCompatibilityMigration
{
    internal readonly record struct Assignment(ushort OldMove, ushort NewMove, ushort BaseMove);

    // Resolve the whole batch against the same source. A move produced by one assignment
    // must never become the input of another assignment in this batch.
    public static byte[] ApplyBatch(byte[] bytes, byte[] baseBytes, IReadOnlyList<Assignment> assignments)
    {
        var active = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(bytes));
        var vanilla = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(baseBytes));
        var changes = assignments.Where(a => a.OldMove != 0 && a.NewMove != 0 && a.OldMove != a.NewMove).ToArray();
        if (changes.Length == 0) return bytes.ToArray();
        var restores = changes.Where(a => a.NewMove == a.BaseMove).ToArray();
        if (restores.Length > 0 && active.EntryLength != vanilla.EntryLength)
            throw new InvalidDataException("Active and vanilla Pokemon tables have different row counts.");
        var mappings = changes.Where(a => a.NewMove != a.BaseMove).ToDictionary(a => a.OldMove, a => a.NewMove);
        var targets = changes.Select(a => a.NewMove).ToHashSet();
        var replacements = new Dictionary<int, ushort[]>();
        for (var index = 0; index < active.EntryLength; index++)
        {
            var row = active.Entry(index);
            var baseRow = restores.Length > 0 ? vanilla.Entry(index) : null;
            if (restores.Length > 0 && ((row is null) != (baseRow is null)
                || row is { } current && baseRow is { } original
                && (current.Species?.Species != original.Species?.Species || current.Species?.Form != original.Species?.Form)))
                throw new InvalidDataException($"Pokemon compatibility row {index} does not match its vanilla identity.");
            if (row is null) continue;
            var before = row.Value.GetTmMovesArray();
            var baseMoves = baseRow?.GetTmMovesArray() ?? [];
            var rowRestores = restores.Where(a => baseMoves.Contains(a.BaseMove)).ToArray();
            var after = before.Select(move =>
            {
                var restore = rowRestores.FirstOrDefault(a => a.OldMove == move && !baseMoves.Contains(move));
                return restore.NewMove != 0 ? restore.NewMove : mappings.GetValueOrDefault(move, move);
            }).ToList();
            foreach (var restore in rowRestores)
            {
                if (after.Contains(restore.BaseMove)) continue;
                var empty = after.IndexOf(0);
                if (empty >= 0) after[empty] = restore.BaseMove;
                else after.Add(restore.BaseMove);
            }
            var seen = new HashSet<ushort>();
            for (var slot = 0; slot < after.Count; slot++)
                if (targets.Contains(after[slot]) && !seen.Add(after[slot])) after[slot] = 0;
            if (!before.SequenceEqual(after)) replacements.Add(index, after.ToArray());
        }
        if (replacements.Count == 0) return bytes.ToArray();
        byte[] output;
        if (replacements.Any(pair => pair.Value.Length != active.Entry(pair.Key)!.Value.TmMovesLength))
            output = ZaPokemonEditSessionService.RewriteTechnicalMachineCompatibility(bytes, replacements);
        else
        {
            output = bytes.ToArray();
            var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(output));
            foreach (var (index, moves) in replacements)
            {
                var row = table.Entry(index)!.Value;
                for (var slot = 0; slot < moves.Length; slot++)
                    if (row.TmMoves(slot) != moves[slot] && !row.MutateTmMove(slot, moves[slot]))
                        throw new InvalidDataException("Pokemon TM compatibility could not be written.");
            }
        }
        var verification = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(output));
        foreach (var (index, moves) in replacements)
            if (!verification.Entry(index)!.Value.GetTmMovesArray().SequenceEqual(moves))
                throw new InvalidDataException("Pokemon TM compatibility did not match the reviewed batch.");
        return output;
    }
}
