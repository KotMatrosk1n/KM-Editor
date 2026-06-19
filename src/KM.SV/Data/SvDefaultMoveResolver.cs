// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Data;

internal sealed class SvDefaultMoveResolver
{
    private static readonly IReadOnlyList<int> EmptyMoveSet = [0, 0, 0, 0];

    private readonly IReadOnlyDictionary<string, IReadOnlyList<LevelupMove>> movesBySpeciesForm;

    private SvDefaultMoveResolver(IReadOnlyDictionary<string, IReadOnlyList<LevelupMove>> movesBySpeciesForm)
    {
        this.movesBySpeciesForm = movesBySpeciesForm;
    }

    public static SvDefaultMoveResolver Empty { get; } = new(
        new Dictionary<string, IReadOnlyList<LevelupMove>>(StringComparer.Ordinal));

    public static SvDefaultMoveResolver Load(
        OpenedProject project,
        SvWorkflowFileSource fileSource,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = fileSource.Read(project, SvDataPaths.PersonalArray);
            var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(source.Bytes));
            var lookup = new Dictionary<string, IReadOnlyList<LevelupMove>>(StringComparer.Ordinal);

            for (var index = 0; index < table.EntryLength; index++)
            {
                var row = table.Entry(index);
                if (row?.Species is not { } species || species.Species == 0)
                {
                    continue;
                }

                lookup.TryAdd(CreateKey(species.Species, species.Form), ReadLevelupMoves(row.Value));
            }

            return new SvDefaultMoveResolver(lookup);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Warning(
                $"Automatic Pokemon moves could not be resolved from Pokemon Data: {exception.Message}",
                $"romfs/{SvDataPaths.PersonalArray}"));
            return Empty;
        }
    }

    public IReadOnlyList<int> Resolve(int species, int form, int level)
    {
        var learnset = movesBySpeciesForm.TryGetValue(CreateKey(species, form), out var exact)
            ? exact
            : movesBySpeciesForm.TryGetValue(CreateKey(species, 0), out var baseForm)
                ? baseForm
                : [];

        if (learnset.Count == 0)
        {
            return EmptyMoveSet;
        }

        var moves = learnset
            .Where(move => move.Level <= level)
            .OrderBy(move => move.Level)
            .ThenBy(move => move.Index)
            .Select(move => move.Move)
            .TakeLast(4)
            .ToList();

        while (moves.Count < 4)
        {
            moves.Add(0);
        }

        return moves;
    }

    private static IReadOnlyList<LevelupMove> ReadLevelupMoves(global::personal row)
    {
        var moves = new List<LevelupMove>();
        for (var index = 0; index < row.LevelupMovesLength; index++)
        {
            var learnedMove = row.LevelupMoves(index);
            if (learnedMove is null || learnedMove.Value.Move == 0)
            {
                continue;
            }

            moves.Add(new LevelupMove(index, learnedMove.Value.Move, learnedMove.Value.Level));
        }

        return moves;
    }

    private static string CreateKey(int species, int form)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{species}:{form}");
    }

    private sealed record LevelupMove(int Index, int Move, int Level);
}
