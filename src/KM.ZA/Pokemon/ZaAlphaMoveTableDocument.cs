// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;

namespace KM.ZA.Pokemon;

internal sealed class ZaAlphaMoveTableDocument
{
    private readonly byte[] activeBytes;
    private readonly IReadOnlyDictionary<PhysicalKey, int?> movePositions;

    private ZaAlphaMoveTableDocument(
        byte[] activeBytes,
        IReadOnlyList<ZaAlphaMoveSpeciesEntry?> rows,
        IReadOnlyList<ZaAlphaMoveEntry> entries,
        IReadOnlyDictionary<PhysicalKey, int?> movePositions)
    {
        this.activeBytes = activeBytes;
        this.movePositions = movePositions;
        Rows = rows;
        Entries = entries;
    }

    public IReadOnlyList<ZaAlphaMoveSpeciesEntry?> Rows { get; }

    public IReadOnlyList<ZaAlphaMoveEntry> Entries { get; }

    public static ZaAlphaMoveTableDocument Parse(
        byte[] bytes,
        int? maximumTableRecords = null,
        int? maximumNestedRecords = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var activeBytes = bytes.ToArray();
        var table = ZaAlphaMoveTable.GetRootAsZaAlphaMoveTable(new ByteBuffer(activeBytes));
        EnsureBoundedCount(table.RootLength, maximumTableRecords, "The Z-A alpha-move species table");
        var rows = new List<ZaAlphaMoveSpeciesEntry?>(table.RootLength);
        var entries = new List<ZaAlphaMoveEntry>();
        var movePositions = new Dictionary<PhysicalKey, int?>();
        var nestedRecords = 0;
        for (var speciesRowIndex = 0; speciesRowIndex < table.RootLength; speciesRowIndex++)
        {
            var species = table.Root(speciesRowIndex);
            if (species is null)
            {
                rows.Add(null);
                continue;
            }

            EnsureBoundedCount(
                species.Value.FormTableListLength,
                maximumTableRecords,
                "A Z-A alpha-move form table");
            nestedRecords = checked(nestedRecords + species.Value.FormTableListLength);
            EnsureBoundedCount(nestedRecords, maximumNestedRecords, "The Z-A alpha-move form rows");
            var forms = new List<ZaAlphaMoveEntry?>(species.Value.FormTableListLength);
            for (var formRowIndex = 0; formRowIndex < species.Value.FormTableListLength; formRowIndex++)
            {
                var form = species.Value.FormTableList(formRowIndex);
                if (form is null)
                {
                    forms.Add(null);
                    continue;
                }

                var entry = new ZaAlphaMoveEntry(
                    speciesRowIndex,
                    formRowIndex,
                    species.Value.DevNo,
                    form.Value.FormNo,
                    form.Value.WazaNo,
                    form.Value.HasWazaNo);
                forms.Add(entry);
                entries.Add(entry);
                movePositions.Add(
                    new PhysicalKey(speciesRowIndex, formRowIndex),
                    form.Value.WazaNoPosition);
            }

            rows.Add(new ZaAlphaMoveSpeciesEntry(
                speciesRowIndex,
                species.Value.DevNo,
                forms));
        }

        return new ZaAlphaMoveTableDocument(activeBytes, rows, entries, movePositions);
    }

    private static void EnsureBoundedCount(int count, int? maximum, string label)
    {
        if (maximum is not null && (count < 0 || count > maximum.Value))
        {
            throw new InvalidDataException($"{label} exceeds the bounded semantic record limit.");
        }
    }

    public ZaAlphaMoveEntry? FindFirstExact(ushort speciesId, ushort formId)
    {
        foreach (var row in Rows)
        {
            if (row is null || row.SpeciesId != speciesId)
            {
                continue;
            }

            foreach (var entry in row.Forms)
            {
                if (entry is not null && entry.FormId == formId)
                {
                    return entry;
                }
            }
        }

        return null;
    }

    public bool TryApplyReplacements(
        IReadOnlyList<ZaAlphaMoveReplacement> replacements,
        out byte[] output,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(replacements);

        output = activeBytes.ToArray();
        var canonicalKeys = new HashSet<CanonicalKey>();
        var targets = new List<TargetWrite>(replacements.Count);
        foreach (var replacement in replacements)
        {
            if (replacement is null)
            {
                error = "An alpha move replacement is missing.";
                return false;
            }

            var canonicalKey = new CanonicalKey(replacement.SpeciesId, replacement.FormId);
            if (!canonicalKeys.Add(canonicalKey))
            {
                error = $"Alpha move replacement {replacement.SpeciesId}:{replacement.FormId} is specified more than once.";
                return false;
            }

            var entry = FindFirstExact(replacement.SpeciesId, replacement.FormId);
            if (entry is null)
            {
                error = $"Alpha move replacement {replacement.SpeciesId}:{replacement.FormId} has no existing exact table entry.";
                return false;
            }

            if (!entry.HasMoveId)
            {
                error = $"Alpha move replacement {replacement.SpeciesId}:{replacement.FormId} uses an omitted default move field and cannot be patched safely.";
                return false;
            }

            var physicalKey = new PhysicalKey(entry.SpeciesRowIndex, entry.FormRowIndex);
            if (!movePositions.TryGetValue(physicalKey, out var movePosition)
                || movePosition is null
                || movePosition < 0
                || movePosition > output.Length - sizeof(ushort))
            {
                error = $"Alpha move replacement {replacement.SpeciesId}:{replacement.FormId} does not have verified scalar storage.";
                return false;
            }

            if (movePositions.Any(pair =>
                    pair.Key != physicalKey
                    && pair.Value == movePosition))
            {
                error = $"Alpha move replacement {replacement.SpeciesId}:{replacement.FormId} shares scalar storage with another entry and cannot be patched independently.";
                return false;
            }

            targets.Add(new TargetWrite(entry, replacement, movePosition.Value));
        }

        var mutableTable = ZaAlphaMoveTable.GetRootAsZaAlphaMoveTable(new ByteBuffer(output));
        foreach (var target in targets)
        {
            var species = mutableTable.Root(target.Entry.SpeciesRowIndex);
            if (species is null
                || species.Value.DevNo != target.Entry.SpeciesId
                || target.Entry.FormRowIndex >= species.Value.FormTableListLength)
            {
                output = activeBytes.ToArray();
                error = "The alpha move table changed while its replacement targets were being resolved.";
                return false;
            }

            var form = species.Value.FormTableList(target.Entry.FormRowIndex);
            var mutableForm = form.GetValueOrDefault();
            if (form is null
                || mutableForm.FormNo != target.Entry.FormId
                || !mutableForm.HasWazaNo
                || mutableForm.WazaNoPosition != target.MovePosition
                || !mutableForm.MutateWazaNo(target.Replacement.MoveId))
            {
                output = activeBytes.ToArray();
                error = $"Alpha move replacement {target.Entry.SpeciesId}:{target.Entry.FormId} could not be applied to its verified scalar.";
                return false;
            }
        }

        if (!TryVerifyOutput(output, targets, out error))
        {
            output = activeBytes.ToArray();
            return false;
        }

        return true;
    }

    private bool TryVerifyOutput(
        byte[] output,
        IReadOnlyList<TargetWrite> targets,
        out string? error)
    {
        if (output.Length != activeBytes.Length)
        {
            error = "The patched alpha move table length does not match the active source.";
            return false;
        }

        var targetByPosition = targets.ToDictionary(
            target => new PhysicalKey(target.Entry.SpeciesRowIndex, target.Entry.FormRowIndex));
        var reparsed = Parse(output);
        if (reparsed.Rows.Count != Rows.Count)
        {
            error = "The patched alpha move table changed the species row count.";
            return false;
        }

        for (var speciesRowIndex = 0; speciesRowIndex < Rows.Count; speciesRowIndex++)
        {
            var beforeSpecies = Rows[speciesRowIndex];
            var afterSpecies = reparsed.Rows[speciesRowIndex];
            if ((beforeSpecies is null) != (afterSpecies is null))
            {
                error = $"The patched alpha move table changed species row {speciesRowIndex}.";
                return false;
            }

            if (beforeSpecies is null || afterSpecies is null)
            {
                continue;
            }

            if (beforeSpecies.SpeciesRowIndex != afterSpecies.SpeciesRowIndex
                || beforeSpecies.SpeciesId != afterSpecies.SpeciesId
                || beforeSpecies.Forms.Count != afterSpecies.Forms.Count)
            {
                error = $"The patched alpha move table changed the shape or identity of species row {speciesRowIndex}.";
                return false;
            }

            for (var formRowIndex = 0; formRowIndex < beforeSpecies.Forms.Count; formRowIndex++)
            {
                var beforeForm = beforeSpecies.Forms[formRowIndex];
                var afterForm = afterSpecies.Forms[formRowIndex];
                if ((beforeForm is null) != (afterForm is null))
                {
                    error = $"The patched alpha move table changed form row {speciesRowIndex}:{formRowIndex}.";
                    return false;
                }

                if (beforeForm is null || afterForm is null)
                {
                    continue;
                }

                var physicalKey = new PhysicalKey(speciesRowIndex, formRowIndex);
                var expectedMoveId = targetByPosition.TryGetValue(physicalKey, out var target)
                    ? target.Replacement.MoveId
                    : beforeForm.MoveId;
                if (beforeForm.SpeciesRowIndex != afterForm.SpeciesRowIndex
                    || beforeForm.FormRowIndex != afterForm.FormRowIndex
                    || beforeForm.SpeciesId != afterForm.SpeciesId
                    || beforeForm.FormId != afterForm.FormId
                    || beforeForm.HasMoveId != afterForm.HasMoveId
                    || afterForm.MoveId != expectedMoveId
                    || !PositionsMatch(reparsed, physicalKey))
                {
                    error = $"The patched alpha move table did not preserve form row {speciesRowIndex}:{formRowIndex}.";
                    return false;
                }
            }
        }

        foreach (var target in targets)
        {
            var first = reparsed.FindFirstExact(target.Entry.SpeciesId, target.Entry.FormId);
            if (first is null
                || first.SpeciesRowIndex != target.Entry.SpeciesRowIndex
                || first.FormRowIndex != target.Entry.FormRowIndex
                || first.MoveId != target.Replacement.MoveId
                || !first.HasMoveId)
            {
                error = $"Alpha move replacement {target.Entry.SpeciesId}:{target.Entry.FormId} did not remain the first exact match after verification.";
                return false;
            }
        }

        var allowedBytePositions = new HashSet<int>();
        foreach (var target in targets)
        {
            allowedBytePositions.Add(target.MovePosition);
            allowedBytePositions.Add(target.MovePosition + 1);
        }

        for (var index = 0; index < activeBytes.Length; index++)
        {
            if (activeBytes[index] != output[index] && !allowedBytePositions.Contains(index))
            {
                error = $"The alpha move patch changed byte {index} outside a requested move scalar.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private bool PositionsMatch(ZaAlphaMoveTableDocument reparsed, PhysicalKey physicalKey)
    {
        return movePositions.TryGetValue(physicalKey, out var beforePosition)
            && reparsed.movePositions.TryGetValue(physicalKey, out var afterPosition)
            && beforePosition == afterPosition;
    }

    private readonly record struct CanonicalKey(ushort SpeciesId, ushort FormId);

    private readonly record struct PhysicalKey(int SpeciesRowIndex, int FormRowIndex);

    private sealed record TargetWrite(
        ZaAlphaMoveEntry Entry,
        ZaAlphaMoveReplacement Replacement,
        int MovePosition);
}

internal sealed record ZaAlphaMoveSpeciesEntry(
    int SpeciesRowIndex,
    ushort SpeciesId,
    IReadOnlyList<ZaAlphaMoveEntry?> Forms);

internal sealed record ZaAlphaMoveEntry(
    int SpeciesRowIndex,
    int FormRowIndex,
    ushort SpeciesId,
    ushort FormId,
    ushort MoveId,
    bool HasMoveId);

internal sealed record ZaAlphaMoveReplacement(
    ushort SpeciesId,
    ushort FormId,
    ushort MoveId);
