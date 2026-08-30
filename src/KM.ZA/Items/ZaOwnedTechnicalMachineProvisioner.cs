// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.ZA.Data;

namespace KM.ZA.Items;

internal sealed record ZaOwnedTechnicalMachineProvisioningResult(
    bool IsAvailable,
    bool Added,
    string? UnavailableReason = null);

/// <summary>
/// Owns validation and materialization for KM Editor's deliberately narrow
/// unused-TM extension. Projected slots TM162 through TM201 all begin unassigned.
/// </summary>
internal static class ZaOwnedTechnicalMachineProvisioner
{
    public static ZaOwnedTechnicalMachineProvisioningResult ProjectAvailableSlots(
        IList<ZaItemsEditSessionService.ItemRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (!TryValidateSupportedPhysicalSource(rows, out var unavailableReason))
        {
            return new ZaOwnedTechnicalMachineProvisioningResult(false, false, unavailableReason);
        }

        var added = false;
        for (var slot = ZaTechnicalMachineCatalog.FirstOwnedExtensionSlot;
             slot <= ZaTechnicalMachineCatalog.LastOwnedExtensionSlot;
             slot++)
        {
            var itemId = ZaTechnicalMachineCatalog.GetOwnedExtensionItemId(slot);
            if (rows.Any(row => row.Id == itemId))
            {
                continue;
            }

            var internalName = ZaTechnicalMachineCatalog.GetOwnedExtensionInternalName(slot);
            if (rows.Any(row => string.Equals(row.InternalName, internalName, StringComparison.Ordinal))
                || rows.Any(row => IsTechnicalMachine(row) && row.SortNum == slot))
            {
                continue;
            }

            InsertInIdOrder(
                rows,
                ZaItemsEditSessionService.ItemRow.CreateOwnedTechnicalMachineExtension(
                    slot,
                    moveId: 0));
            added = true;
        }

        return new ZaOwnedTechnicalMachineProvisioningResult(true, added);
    }

    public static ZaOwnedTechnicalMachineProvisioningResult ProvisionSlot(
        IList<ZaItemsEditSessionService.ItemRow> rows,
        int slot,
        int moveId)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (!ZaTechnicalMachineCatalog.IsOwnedExtensionSlot(slot)
            || moveId is <= 0 or > ushort.MaxValue)
        {
            return new ZaOwnedTechnicalMachineProvisioningResult(
                false,
                false,
                $"TM{slot.ToString(CultureInfo.InvariantCulture)} requires an owned slot from TM162 through TM201 and an assigned move.");
        }

        var itemId = ZaTechnicalMachineCatalog.GetOwnedExtensionItemId(slot);
        var ownedRows = rows.Where(row => row.Id == itemId).ToArray();
        if (ownedRows.Length > 0)
        {
            if (ownedRows.Length != 1
                || !IsOwnedTechnicalMachineExtensionRow(ownedRows[0]))
            {
                return new ZaOwnedTechnicalMachineProvisioningResult(
                    false,
                    false,
                    $"Item {itemId.ToString(CultureInfo.InvariantCulture)} already exists but does not match KM Editor's owned TM{slot.ToString(CultureInfo.InvariantCulture)} slot identity.");
            }

            if (!HasValidOwnedExtensions(rows))
            {
                return new ZaOwnedTechnicalMachineProvisioningResult(
                    false,
                    false,
                    $"TM{slot.ToString(CultureInfo.InvariantCulture)} collides with another item ID, TM number, internal token, or move assignment.");
            }

            return new ZaOwnedTechnicalMachineProvisioningResult(true, false);
        }

        if (!TryValidateSupportedPhysicalSource(rows, out var unavailableReason))
        {
            return new ZaOwnedTechnicalMachineProvisioningResult(false, false, unavailableReason);
        }

        var internalName = ZaTechnicalMachineCatalog.GetOwnedExtensionInternalName(slot);
        if (rows.Any(row => string.Equals(row.InternalName, internalName, StringComparison.Ordinal))
            || rows.Any(row => IsTechnicalMachine(row) && row.SortNum == slot)
            || rows.Any(row => IsTechnicalMachine(row) && row.MachineWaza == moveId))
        {
            return new ZaOwnedTechnicalMachineProvisioningResult(
                false,
                false,
                $"TM{slot.ToString(CultureInfo.InvariantCulture)} cannot be materialized because its item ID, internal token, TM number, or selected move is already owned.");
        }

        InsertInIdOrder(
            rows,
            ZaItemsEditSessionService.ItemRow.CreateOwnedTechnicalMachineExtension(
                slot,
                checked((ushort)moveId)));
        EnsureValidOwnedExtensions(rows);
        return new ZaOwnedTechnicalMachineProvisioningResult(true, true);
    }

    public static bool IsOwnedTechnicalMachineExtensionRow(
        ZaItemsEditSessionService.ItemRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ZaTechnicalMachineCatalog.HasOwnedExtensionIdentity(
                row.Id,
                row.ItemType,
                row.InternalName,
                row.Pocket,
                row.SortNum,
                row.MachineIndex)
            && row.MachineWaza > 0;
    }

    public static bool IsOwnedTechnicalMachineProjectionRow(
        ZaItemsEditSessionService.ItemRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ZaTechnicalMachineCatalog.HasOwnedExtensionIdentity(
            row.Id,
            row.ItemType,
            row.InternalName,
            row.Pocket,
            row.SortNum,
            row.MachineIndex);
    }

    private static bool TryValidateSupportedPhysicalSource(
        IEnumerable<ZaItemsEditSessionService.ItemRow> sourceRows,
        out string unavailableReason)
    {
        var rows = sourceRows.ToArray();
        var machines = rows.Where(IsTechnicalMachine).ToArray();
        var assignments = CreateAssignments(machines);
        var hasTemplate = rows.Count(row =>
            row.Id == ZaTechnicalMachineCatalog.OwnedExtensionTemplateItemId
            && row.ItemType == 5
            && row.Pocket == 6
            && row.MachineWaza > 0
            && string.Equals(
                row.InternalName,
                ZaTechnicalMachineCatalog.OwnedExtensionTemplateInternalName,
                StringComparison.Ordinal)) == 1;
        var ownedExtensionRowsAreValid = machines
            .Where(row => ZaTechnicalMachineCatalog.IsOwnedExtensionItemId(row.Id))
            .All(IsOwnedTechnicalMachineExtensionRow);
        var numberingIsValid = ZaTechnicalMachineCatalog.HasCompleteNumbering(assignments)
            || ZaTechnicalMachineCatalog.HasCompleteNumberingWithOwnedExtensions(assignments);
        if (rows.Select(row => row.Id).Distinct().Count() == rows.Length
            && hasTemplate
            && machines.Select(row => row.Id).Distinct().Count() == machines.Length
            && machines.Select(row => row.MachineWaza).Distinct().Count() == machines.Length
            && ownedExtensionRowsAreValid
            && numberingIsValid)
        {
            unavailableReason = string.Empty;
            return true;
        }

        unavailableReason =
            "Unused TM slots require the supported unique 160-TM source or a valid KM-owned TM162 through TM201 extension, with no item, token, number, or move collisions.";
        return false;
    }

    private static void EnsureValidOwnedExtensions(
        IEnumerable<ZaItemsEditSessionService.ItemRow> sourceRows)
    {
        if (!HasValidOwnedExtensions(sourceRows))
        {
            throw new InvalidDataException(
                "The KM-owned unused-TM extension collides with another item ID, TM number, internal token, or move assignment.");
        }
    }

    private static bool HasValidOwnedExtensions(
        IEnumerable<ZaItemsEditSessionService.ItemRow> sourceRows)
    {
        var rows = sourceRows.ToArray();
        var machines = rows.Where(IsTechnicalMachine).ToArray();
        var ownedRows = machines
            .Where(row => ZaTechnicalMachineCatalog.IsOwnedExtensionItemId(row.Id))
            .ToArray();
        return rows.Select(row => row.Id).Distinct().Count() == rows.Length
            && machines.Select(row => row.MachineWaza).Distinct().Count() == machines.Length
            && ownedRows.All(IsOwnedTechnicalMachineExtensionRow)
            && ownedRows.All(owned => rows.Count(row => string.Equals(
                row.InternalName,
                owned.InternalName,
                StringComparison.Ordinal)) == 1)
            && HasOwnedRowsInIdOrder(rows)
            && ZaTechnicalMachineCatalog.HasCompleteNumberingWithOwnedExtensions(
                CreateAssignments(machines));
    }

    public static bool HasOwnedRowInIdOrder(
        IEnumerable<ZaItemsEditSessionService.ItemRow> rows) =>
        HasOwnedRowsInIdOrder(rows);

    public static bool HasOwnedRowsInIdOrder(
        IEnumerable<ZaItemsEditSessionService.ItemRow> rows)
    {
        var orderedRows = rows.ToArray();
        for (var index = 0; index < orderedRows.Length; index++)
        {
            var row = orderedRows[index];
            if (!ZaTechnicalMachineCatalog.IsOwnedExtensionItemId(row.Id))
            {
                continue;
            }

            if (orderedRows.Take(index).Any(previous => previous.Id > row.Id)
                || orderedRows.Skip(index + 1).Any(next => next.Id < row.Id))
            {
                return false;
            }
        }

        return true;
    }

    private static void InsertInIdOrder(
        IList<ZaItemsEditSessionService.ItemRow> rows,
        ZaItemsEditSessionService.ItemRow row)
    {
        var insertionIndex = rows.Count;
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Id > row.Id)
            {
                insertionIndex = index;
                break;
            }
        }

        rows.Insert(insertionIndex, row);
    }

    private static bool IsTechnicalMachine(ZaItemsEditSessionService.ItemRow row) =>
        row.Pocket == 6
        && row.ItemType == 5
        && row.MachineWaza > 0;

    private static ZaTechnicalMachineNumberAssignment[] CreateAssignments(
        IEnumerable<ZaItemsEditSessionService.ItemRow> rows) =>
        rows
            .Select(row => new ZaTechnicalMachineNumberAssignment(
                row.Id,
                row.SortNum,
                row.MachineIndex))
            .ToArray();
}
