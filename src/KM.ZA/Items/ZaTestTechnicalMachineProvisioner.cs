// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.Data;

namespace KM.ZA.Items;

internal sealed record ZaTestTechnicalMachineProvisioningResult(
    bool IsAvailable,
    bool Added,
    string? UnavailableReason = null);

internal static class ZaTestTechnicalMachineProvisioner
{
    public static ZaTestTechnicalMachineProvisioningResult Provision(byte[] itemData, out byte[] provisionedItemData)
    {
        ArgumentNullException.ThrowIfNull(itemData);

        var rows = ZaItemsEditSessionService.ReadRows(itemData);
        var result = Provision(rows);
        provisionedItemData = result.Added
            ? ZaItemsEditSessionService.WriteRows(rows)
            : itemData;
        if (result.IsAvailable)
        {
            EnsureValidOwnedExtension(ZaItemsEditSessionService.ReadRows(provisionedItemData));
        }

        return result;
    }

    public static ZaTestTechnicalMachineProvisioningResult Provision(
        IList<ZaItemsEditSessionService.ItemRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var ownedRows = rows
            .Where(row => row.Id == ZaTechnicalMachineCatalog.TestTechnicalMachineItemId)
            .ToArray();
        if (ownedRows.Length > 0)
        {
            if (ownedRows.Length != 1 || !IsOwnedTestTechnicalMachineRow(ownedRows[0]))
            {
                return new ZaTestTechnicalMachineProvisioningResult(
                    IsAvailable: false,
                    Added: false,
                    $"Item {ZaTechnicalMachineCatalog.TestTechnicalMachineItemId} already exists but does not match KM Editor's owned TM162 Bug Buzz identity.");
            }

            if (!HasValidOwnedExtension(rows))
            {
                return new ZaTestTechnicalMachineProvisioningResult(
                    IsAvailable: false,
                    Added: false,
                    "TM162 Bug Buzz collides with another item ID, TM number, or move assignment.");
            }

            return new ZaTestTechnicalMachineProvisioningResult(IsAvailable: true, Added: false);
        }

        if (!TryValidateUnprovisionedSource(rows, out var unavailableReason))
        {
            return new ZaTestTechnicalMachineProvisioningResult(
                IsAvailable: false,
                Added: false,
                unavailableReason);
        }

        var insertionIndex = rows.Count;
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Id > ZaTechnicalMachineCatalog.TestTechnicalMachineItemId)
            {
                insertionIndex = index;
                break;
            }
        }

        rows.Insert(insertionIndex, ZaItemsEditSessionService.ItemRow.CreateTestTechnicalMachine());
        EnsureValidOwnedExtension(rows);
        return new ZaTestTechnicalMachineProvisioningResult(IsAvailable: true, Added: true);
    }

    public static bool IsOwnedTestTechnicalMachineRow(ZaItemsEditSessionService.ItemRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return ZaTechnicalMachineCatalog.HasTestTechnicalMachineIdentity(
            row.Id,
            row.ItemType,
            row.InternalName,
            row.IconName,
            row.Pocket,
            row.SortNum,
            row.MachineIndex,
            row.MachineWaza);
    }

    private static bool TryValidateUnprovisionedSource(
        IEnumerable<ZaItemsEditSessionService.ItemRow> sourceRows,
        out string unavailableReason)
    {
        var rows = sourceRows.ToArray();
        var machines = rows.Where(IsTechnicalMachine).ToArray();
        var assignments = CreateAssignments(machines);
        var hasUniqueItemIds = rows.Select(row => row.Id).Distinct().Count() == rows.Length;
        var hasTemplate = rows.Count(row =>
            row.Id == ZaTechnicalMachineCatalog.TestTechnicalMachineTemplateItemId
            && row.ItemType == 5
            && row.Pocket == 6
            && row.MachineWaza > 0
            && string.Equals(row.InternalName, "WAZAMASIN161", StringComparison.Ordinal)) == 1;
        var hasReservedSlotCollision = machines.Any(row =>
            row.SortNum == ZaTechnicalMachineCatalog.TestTechnicalMachineSlot);
        var hasMoveCollision = machines.Any(row =>
            row.MachineWaza == ZaTechnicalMachineCatalog.TestTechnicalMachineMoveId);
        var hasInternalNameCollision = rows.Any(row => string.Equals(
            row.InternalName,
            ZaTechnicalMachineCatalog.TestTechnicalMachineInternalName,
            StringComparison.Ordinal));
        if (hasUniqueItemIds
            && hasTemplate
            && machines.Length == 160
            && machines.Select(row => row.Id).Distinct().Count() == machines.Length
            && machines.Select(row => row.MachineWaza).Distinct().Count() == machines.Length
            && !hasReservedSlotCollision
            && !hasMoveCollision
            && !hasInternalNameCollision
            && ZaTechnicalMachineCatalog.HasCompleteNumbering(assignments))
        {
            unavailableReason = string.Empty;
            return true;
        }

        unavailableReason =
            "TM162 Bug Buzz requires the supported unique 160-TM source, an unclaimed item 2222 and WAZAMASIN162 token, and unclaimed TM number 162 and Bug Buzz assignments.";
        return false;
    }

    private static void EnsureValidOwnedExtension(
        IEnumerable<ZaItemsEditSessionService.ItemRow> sourceRows)
    {
        if (!HasValidOwnedExtension(sourceRows))
        {
            throw new InvalidDataException(
                "TM162 Bug Buzz collides with another item ID, TM number, or move assignment.");
        }
    }

    private static bool HasValidOwnedExtension(
        IEnumerable<ZaItemsEditSessionService.ItemRow> sourceRows)
    {
        var rows = sourceRows.ToArray();
        var machines = rows.Where(IsTechnicalMachine).ToArray();
        return rows.Select(row => row.Id).Distinct().Count() == rows.Length
            && machines.Select(row => row.MachineWaza).Distinct().Count() == machines.Length
            && rows.Count(row => string.Equals(
                row.InternalName,
                ZaTechnicalMachineCatalog.TestTechnicalMachineInternalName,
                StringComparison.Ordinal)) == 1
            && HasOwnedRowInIdOrder(rows)
            && ZaTechnicalMachineCatalog.HasCompleteNumberingWithTestTechnicalMachineExtension(
                CreateAssignments(machines));
    }

    public static bool HasOwnedRowInIdOrder(
        IEnumerable<ZaItemsEditSessionService.ItemRow> rows)
    {
        var orderedRows = rows.ToArray();
        var ownedIndexes = orderedRows
            .Select((row, index) => (row, index))
            .Where(entry => entry.row.Id == ZaTechnicalMachineCatalog.TestTechnicalMachineItemId)
            .Select(entry => entry.index)
            .ToArray();
        if (ownedIndexes.Length != 1)
        {
            return false;
        }

        var ownedIndex = ownedIndexes[0];
        return orderedRows.Take(ownedIndex).All(row =>
                row.Id < ZaTechnicalMachineCatalog.TestTechnicalMachineItemId)
            && orderedRows.Skip(ownedIndex + 1).All(row =>
                row.Id > ZaTechnicalMachineCatalog.TestTechnicalMachineItemId);
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
