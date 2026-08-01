// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;

namespace KM.ZA.Items;

internal static class ZaTechnicalMachineCompatibilityMigration
{
    public static Inspection Inspect(byte[] bytes, ushort oldMoveId, ushort newMoveId)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(bytes));
        var affectedRows = 0;
        var affectedValues = 0;
        var conflictingRows = 0;
        var existingTargetRows = 0;
        for (var rowIndex = 0; rowIndex < table.EntryLength; rowIndex++)
        {
            if (table.Entry(rowIndex) is not { } row)
            {
                continue;
            }

            var moves = row.GetTmMovesArray();
            var oldCount = moves.Count(move => move == oldMoveId);
            if (moves.Contains(newMoveId))
            {
                existingTargetRows++;
            }
            if (oldCount == 0)
            {
                continue;
            }

            affectedRows++;
            affectedValues += oldCount;
            if (moves.Contains(newMoveId))
            {
                conflictingRows++;
            }
        }

        return new Inspection(affectedRows, affectedValues, conflictingRows, existingTargetRows);
    }

    public static byte[] Apply(byte[] bytes, ushort oldMoveId, ushort newMoveId, out Inspection inspection)
    {
        inspection = Inspect(bytes, oldMoveId, newMoveId);
        if (inspection.ExistingTargetRows > 0)
        {
            throw new InvalidOperationException(
                "The selected move already appears in Pokemon TM compatibility, so ownership is ambiguous.");
        }

        var output = bytes.ToArray();
        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(output));
        var changedValues = 0;
        for (var rowIndex = 0; rowIndex < table.EntryLength; rowIndex++)
        {
            if (table.Entry(rowIndex) is not { } row)
            {
                continue;
            }

            for (var moveIndex = 0; moveIndex < row.TmMovesLength; moveIndex++)
            {
                if (row.TmMoves(moveIndex) == oldMoveId && row.MutateTmMove(moveIndex, newMoveId))
                {
                    changedValues++;
                }
            }
        }

        if (changedValues != inspection.AffectedValues)
        {
            throw new InvalidDataException(
                "TM compatibility migration did not update the expected number of move references.");
        }

        var verification = Inspect(output, oldMoveId, newMoveId);
        if (verification.AffectedValues != 0)
        {
            throw new InvalidDataException("TM compatibility migration left old move references behind.");
        }

        return output;
    }

    public static BaseRestoreInspection InspectBaseRestore(
        byte[] bytes,
        byte[] baseBytes,
        ushort currentMoveId,
        ushort baseMoveId)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(baseBytes);

        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(bytes));
        var baseTable = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(baseBytes));
        return InspectBaseRestore(table, baseTable, currentMoveId, baseMoveId);
    }

    public static byte[] RestoreBaseAssignment(
        byte[] bytes,
        byte[] baseBytes,
        ushort currentMoveId,
        ushort baseMoveId,
        out BaseRestoreInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(baseBytes);

        var output = bytes.ToArray();
        var table = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(output));
        var baseTable = ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(baseBytes));
        inspection = InspectBaseRestore(table, baseTable, currentMoveId, baseMoveId);

        var changedValues = 0;
        for (var rowIndex = 0; rowIndex < table.EntryLength; rowIndex++)
        {
            if (table.Entry(rowIndex) is not { } row
                || baseTable.Entry(rowIndex) is not { } baseRow)
            {
                continue;
            }

            for (var moveIndex = 0; moveIndex < row.TmMovesLength; moveIndex++)
            {
                if (baseRow.TmMoves(moveIndex) == baseMoveId
                    && row.TmMoves(moveIndex) == currentMoveId
                    && row.MutateTmMove(moveIndex, baseMoveId))
                {
                    changedValues++;
                }
            }
        }

        if (changedValues != inspection.ChangedValues)
        {
            throw new InvalidDataException(
                "TM compatibility restoration did not update the expected number of verified base positions.");
        }

        var verification = InspectBaseRestore(
            ZaPersonalTable.GetRootAsZaPersonalTable(new ByteBuffer(output)),
            baseTable,
            currentMoveId,
            baseMoveId);
        if (verification.ChangedValues != 0)
        {
            throw new InvalidDataException(
                "TM compatibility restoration left verified base positions unrestored.");
        }

        return output;
    }

    private static BaseRestoreInspection InspectBaseRestore(
        ZaPersonalTable table,
        ZaPersonalTable baseTable,
        ushort currentMoveId,
        ushort baseMoveId)
    {
        if (table.EntryLength != baseTable.EntryLength)
        {
            throw new InvalidDataException(
                "The active and verified vanilla Pokemon tables do not contain the same number of rows.");
        }

        var ownedRows = 0;
        var ownedValues = 0;
        var changedRows = 0;
        var changedValues = 0;
        for (var rowIndex = 0; rowIndex < table.EntryLength; rowIndex++)
        {
            var row = table.Entry(rowIndex);
            var baseRow = baseTable.Entry(rowIndex);
            if ((row is null) != (baseRow is null))
            {
                throw new InvalidDataException(
                    $"Pokemon compatibility row {rowIndex} is not present in both the active and verified vanilla tables.");
            }
            if (row is null || baseRow is null)
            {
                continue;
            }

            var identity = row.Value.Species;
            var baseIdentity = baseRow.Value.Species;
            if ((identity is null) != (baseIdentity is null)
                || identity is { } activeSpecies
                && baseIdentity is { } vanillaSpecies
                && (activeSpecies.Species != vanillaSpecies.Species
                    || activeSpecies.Form != vanillaSpecies.Form))
            {
                throw new InvalidDataException(
                    $"Pokemon compatibility row {rowIndex} does not match the verified vanilla species and form identity.");
            }

            if (row.Value.TmMovesLength != baseRow.Value.TmMovesLength)
            {
                throw new InvalidDataException(
                    $"Pokemon compatibility row {rowIndex} does not have the verified vanilla vector length.");
            }

            var ownsRow = false;
            var changesRow = false;
            for (var moveIndex = 0; moveIndex < row.Value.TmMovesLength; moveIndex++)
            {
                if (baseRow.Value.TmMoves(moveIndex) != baseMoveId)
                {
                    continue;
                }

                ownsRow = true;
                ownedValues++;
                var activeValue = row.Value.TmMoves(moveIndex);
                if (activeValue == baseMoveId)
                {
                    continue;
                }
                if (activeValue != currentMoveId)
                {
                    throw new InvalidOperationException(
                        $"Pokemon compatibility row {rowIndex}, position {moveIndex} contains move {activeValue} where verified vanilla expects the selected TM's base move.");
                }

                changesRow = true;
                changedValues++;
            }

            ownedRows += ownsRow ? 1 : 0;
            changedRows += changesRow ? 1 : 0;
        }

        return new BaseRestoreInspection(
            ownedRows,
            ownedValues,
            changedRows,
            changedValues);
    }

    public readonly record struct Inspection(
        int AffectedRows,
        int AffectedValues,
        int ConflictingRows,
        int ExistingTargetRows);

    public readonly record struct BaseRestoreInspection(
        int OwnedRows,
        int OwnedValues,
        int ChangedRows,
        int ChangedValues);
}
