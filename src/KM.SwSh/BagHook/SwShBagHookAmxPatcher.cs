// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
namespace KM.SwSh.BagHook;

internal enum SwShBagHookInstallKind
{
    NotInstalled,
    InstalledV2,
    LegacySingleGrant,
    Conflict,
}

internal sealed record SwShBagHookSlotPatch(
    int Slot,
    int? ItemId,
    int? Quantity);

internal sealed record SwShBagHookSlotState(
    int Slot,
    string Status,
    int? ItemId,
    int? Quantity,
    string Notes);

internal sealed record SwShBagHookAnalysis(
    SwShBagHookInstallKind Kind,
    string Message,
    IReadOnlyList<SwShBagHookSlotState> Slots);

internal static class SwShBagHookAmxPatcher
{
    public const int SlotCount = 20;
    public const int SlotWidth = 5;
    public const int RoyalCandySlot = 1;
    public const int FirstStartingItemSlot = 2;
    public const int LastStartingItemSlot = 20;
    public const int RoyalCandyItemId = 1128;

    private const ushort PawnMagic16 = 0xF1E2;
    private const ushort PawnMagic32 = 0xF1E0;
    private const ushort PawnMagic64 = 0xF1E1;
    private const short PawnFlagCompact = 0x0004;
    private const int OpCall = 49;
    private const int OpProc = 46;
    private const int OpRetn = 48;
    private const int OpZeroPri = 89;
    private const int OpSysreqN = 135;
    private const int OpPushmPc = 188;
    private const uint DuplicatedNativeHash = 0x0473BE4E;
    private const uint AddItemNativeHash = 0x8D631FFE;
    private const int FreedNativeIndex = 70;
    private const int DuplicateNativeIndex = 76;
    private const int DuplicateNativeCallCell = 3686;
    private const int OriginalNoOpGrantStubCell = 4991;
    private const int GrantStubCallerCell = 5020;
    private const int HookProcedureHeaderCellCount = 1;
    private const int HookTrailerCellCount = 2;
    private const int HookMarkerCellCount = 5;
    private const int HookCodeCellCount = HookProcedureHeaderCellCount + (SlotCount * SlotWidth) + HookTrailerCellCount;
    private const ulong MarkerCell0 = 0x4741425F48535753; // SWSH_BAG
    private const ulong MarkerCell1 = 0x32565F4B4F4F485F; // _HOOK_V2

    private enum MarkerPlacement
    {
        DataSection,
        LegacyCodeSection,
    }

    public static SwShBagHookAnalysis Analyze(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            var decoded = Decode(data);
            var codeCells = ReadCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, decoded.CellSize);
            ValidateSupportedVanillaShape(decoded, codeCells, requireFreedNativeHash: false);

            var targetCell = TryDecodeLocalCallTarget(codeCells, GrantStubCallerCell, decoded.CellSize);
            if (targetCell is null)
            {
                return CreateConflict("Bag Hook call site is not a local AMX CALL with a readable target.");
            }

            if (TryReadV2Slots(decoded, codeCells, targetCell.Value, out var slots, out var markerPlacement))
            {
                var message = markerPlacement == MarkerPlacement.LegacyCodeSection
                    ? "Bag Hook V2 is installed with the legacy code-section marker. Stage and apply Bag Hook, Royal Candy, or Starting Items to migrate the marker into AMX data and keep the script VM-safe."
                    : "Bag Hook V2 is installed. Slot 1 is reserved for Royal Candy; slots 2-20 are available for Starting Items.";

                return new SwShBagHookAnalysis(
                    SwShBagHookInstallKind.InstalledV2,
                    message,
                    slots);
            }

            if (TryReadLegacySingleGrant(codeCells, targetCell.Value, out var legacySlot))
            {
                return new SwShBagHookAnalysis(
                    SwShBagHookInstallKind.LegacySingleGrant,
                    "A legacy one-item Bag-event grant is installed. Reinstall Bag Hook V2 before managing shared slots.",
                    CreateLegacySlots(legacySlot));
            }

            if (targetCell.Value == OriginalNoOpGrantStubCell)
            {
                return new SwShBagHookAnalysis(
                    SwShBagHookInstallKind.NotInstalled,
                    "Bag Hook is not installed. Installing it creates 20 disabled grant slots and grants no items by itself.",
                    CreateEmptySlots("unavailable", "Install Bag Hook V2 before claiming this slot."));
            }

            return CreateConflict($"Bag Hook call site points to AMX cell {targetCell.Value}, which is not the vanilla no-op routine, Bag Hook V2, or a recognized legacy grant.");
        }
        catch (InvalidDataException exception)
        {
            return CreateConflict(exception.Message);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return CreateConflict(exception.Message);
        }
    }

    public static byte[] InstallEmptyHook(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var analysis = Analyze(data);
        if (analysis.Kind == SwShBagHookInstallKind.InstalledV2)
        {
            return MigrateLegacyCodeMarkerIfNeeded(data) ?? data.ToArray();
        }

        if (analysis.Kind is not SwShBagHookInstallKind.NotInstalled)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var decoded = Decode(data);
        var codeCells = ReadCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, decoded.CellSize);
        ValidateSupportedVanillaShape(decoded, codeCells, requireFreedNativeHash: true);
        ExpectLocalCall(codeCells, GrantStubCallerCell, OriginalNoOpGrantStubCell, decoded.CellSize, "Bag-event no-op caller");

        var bagProcCell = codeCells.Length;
        var hookCells = CreateEmptyHookCells();
        var markerCells = CreateHookMarkerCells();
        var hookCodeLength = hookCells.Length * decoded.CellSize;
        var markerLength = markerCells.Length * decoded.CellSize;
        var patchedHeader = decoded.Header with
        {
            Dat = decoded.Header.Dat + hookCodeLength,
            Hea = decoded.Header.Hea + hookCodeLength + markerLength,
            Stp = decoded.Header.Stp + hookCodeLength + markerLength,
        };
        var patchedExpanded = InsertAmxCodeCellsAndAppendDataMarker(
            decoded.Expanded,
            decoded.Header,
            patchedHeader,
            hookCells,
            markerCells,
            decoded.CellSize);

        codeCells = ReadCells(patchedExpanded, patchedHeader.Cod, patchedHeader.Dat - patchedHeader.Cod, decoded.CellSize);
        codeCells[DuplicateNativeCallCell + 1] = DuplicateNativeIndex;
        codeCells[GrantStubCallerCell + 1] = unchecked((ulong)((bagProcCell - GrantStubCallerCell) * decoded.CellSize));
        WriteCells(patchedExpanded, patchedHeader.Cod, codeCells, decoded.CellSize);

        var patchedPrefix = data[..decoded.Header.Cod].ToArray();
        WriteAmxHeaderFields(patchedPrefix, patchedHeader);
        WriteAmxHeaderFields(patchedExpanded, patchedHeader);
        WriteNativeHash(patchedPrefix, decoded.Header, FreedNativeIndex, AddItemNativeHash);
        WriteNativeHash(patchedExpanded, decoded.Header, FreedNativeIndex, AddItemNativeHash);

        var patched = BuildCompactAmx(patchedPrefix, patchedHeader, patchedExpanded, decoded.CellSize);
        VerifyExpandedMemory(patched, patchedExpanded);
        return patched;
    }

    public static byte[] ApplySlotPatches(byte[] data, IReadOnlyList<SwShBagHookSlotPatch> patches)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(patches);

        var decoded = Decode(data);
        var codeCells = ReadCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, decoded.CellSize);
        ValidateSupportedVanillaShape(decoded, codeCells, requireFreedNativeHash: false);
        var bagProcCell = TryDecodeLocalCallTarget(codeCells, GrantStubCallerCell, decoded.CellSize)
            ?? throw new InvalidDataException("Bag Hook call site is not a readable local AMX CALL.");

        if (!TryReadV2Slots(decoded, codeCells, bagProcCell, out _, out var markerPlacement))
        {
            throw new InvalidDataException("Bag Hook V2 must be installed before slot grants can be edited.");
        }

        if (markerPlacement == MarkerPlacement.LegacyCodeSection)
        {
            decoded = MoveLegacyCodeMarkerToDataSection(decoded, bagProcCell);
            codeCells = ReadCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, decoded.CellSize);
            bagProcCell = TryDecodeLocalCallTarget(codeCells, GrantStubCallerCell, decoded.CellSize)
                ?? throw new InvalidDataException("Bag Hook call site is not a readable local AMX CALL after marker migration.");
            if (!TryReadV2Slots(decoded, codeCells, bagProcCell, out _, out _))
            {
                throw new InvalidDataException("Bag Hook V2 marker migration did not produce a readable slot bank.");
            }
        }

        foreach (var patch in patches)
        {
            if (patch.Slot is < 1 or > SlotCount)
            {
                throw new InvalidDataException($"Bag Hook slot {patch.Slot} is outside the supported 1-20 range.");
            }

            if (patch.ItemId is null || patch.Quantity is null || patch.Quantity <= 0)
            {
                WriteEmptySlot(codeCells, GetSlotStartCell(bagProcCell, patch.Slot));
                continue;
            }

            if (patch.ItemId is < 0 or > 0xFFFF)
            {
                throw new InvalidDataException($"Bag Hook slot {patch.Slot} item id {patch.ItemId} must fit in 16 bits.");
            }

            if (patch.Quantity is < 1 or > 999)
            {
                throw new InvalidDataException($"Bag Hook slot {patch.Slot} quantity {patch.Quantity} must be between 1 and 999.");
            }

            WriteActiveSlot(codeCells, GetSlotStartCell(bagProcCell, patch.Slot), patch.ItemId.Value, patch.Quantity.Value);
        }

        WriteCells(decoded.Expanded, decoded.Header.Cod, codeCells, decoded.CellSize);
        var patchedPrefix = data[..decoded.Header.Cod].ToArray();
        WriteAmxHeaderFields(patchedPrefix, decoded.Header);
        WriteNativeHash(patchedPrefix, decoded.Header, FreedNativeIndex, AddItemNativeHash);
        var patched = BuildCompactAmx(patchedPrefix, decoded.Header, decoded.Expanded, decoded.CellSize);
        VerifyExpandedMemory(patched, decoded.Expanded);
        return patched;
    }

    private static SwShBagHookAnalysis CreateConflict(string message)
    {
        return new SwShBagHookAnalysis(
            SwShBagHookInstallKind.Conflict,
            message,
            CreateEmptySlots("conflict", "Slot state cannot be trusted until the Bag-event script conflict is resolved."));
    }

    private static byte[]? MigrateLegacyCodeMarkerIfNeeded(byte[] data)
    {
        var decoded = Decode(data);
        var codeCells = ReadCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, decoded.CellSize);
        var bagProcCell = TryDecodeLocalCallTarget(codeCells, GrantStubCallerCell, decoded.CellSize);
        if (bagProcCell is null
            || !TryReadV2Slots(decoded, codeCells, bagProcCell.Value, out _, out var markerPlacement)
            || markerPlacement != MarkerPlacement.LegacyCodeSection)
        {
            return null;
        }

        decoded = MoveLegacyCodeMarkerToDataSection(decoded, bagProcCell.Value);
        var patchedPrefix = data[..decoded.Header.Cod].ToArray();
        WriteAmxHeaderFields(patchedPrefix, decoded.Header);
        var patched = BuildCompactAmx(patchedPrefix, decoded.Header, decoded.Expanded, decoded.CellSize);
        VerifyExpandedMemory(patched, decoded.Expanded);
        return patched;
    }

    private static IReadOnlyList<SwShBagHookSlotState> CreateEmptySlots(string status, string notes)
    {
        return Enumerable.Range(1, SlotCount)
            .Select(slot => new SwShBagHookSlotState(slot, status, null, null, notes))
            .ToArray();
    }

    private static IReadOnlyList<SwShBagHookSlotState> CreateLegacySlots(SwShBagHookSlotState legacySlot)
    {
        return Enumerable.Range(1, SlotCount)
            .Select(slot => slot == 1
                ? legacySlot
                : new SwShBagHookSlotState(slot, "unavailable", null, null, "Legacy one-item grant has no shared Bag Hook slot bank."))
            .ToArray();
    }

    private static bool TryReadV2Slots(
        DecodedAmx decoded,
        IReadOnlyList<ulong> codeCells,
        int bagProcCell,
        out IReadOnlyList<SwShBagHookSlotState> slots,
        out MarkerPlacement markerPlacement)
    {
        slots = Array.Empty<SwShBagHookSlotState>();
        markerPlacement = MarkerPlacement.DataSection;
        var minimumLength = bagProcCell + HookCodeCellCount;
        if (bagProcCell < 0 || minimumLength > codeCells.Count)
        {
            return false;
        }

        if (SignedCellValue(codeCells[bagProcCell], 8) != OpProc
            || SignedCellValue(codeCells[bagProcCell + 101], 8) != OpZeroPri
            || SignedCellValue(codeCells[bagProcCell + 102], 8) != OpRetn)
        {
            return false;
        }

        if (TryReadDataMarker(decoded))
        {
            markerPlacement = MarkerPlacement.DataSection;
        }
        else if (TryReadLegacyCodeMarker(codeCells, bagProcCell))
        {
            markerPlacement = MarkerPlacement.LegacyCodeSection;
        }
        else
        {
            return false;
        }

        var result = new List<SwShBagHookSlotState>(SlotCount);
        for (var slot = 1; slot <= SlotCount; slot++)
        {
            var slotStart = GetSlotStartCell(bagProcCell, slot);
            result.Add(ReadSlot(codeCells, slot, slotStart));
        }

        slots = result;
        return true;
    }

    private static bool TryReadLegacySingleGrant(
        IReadOnlyList<ulong> codeCells,
        int bagProcCell,
        out SwShBagHookSlotState slot)
    {
        slot = new SwShBagHookSlotState(1, "conflict", null, null, "Legacy grant could not be decoded.");
        if (bagProcCell < 0 || bagProcCell + 8 > codeCells.Count)
        {
            return false;
        }

        if (SignedCellValue(codeCells[bagProcCell], 8) != OpProc
            || !TryUnpackPushmPc(codeCells[bagProcCell + 1], out var quantity)
            || !TryUnpackPushmPc(codeCells[bagProcCell + 2], out var itemId)
            || SignedCellValue(codeCells[bagProcCell + 3], 8) != OpSysreqN
            || SignedCellValue(codeCells[bagProcCell + 4], 8) != FreedNativeIndex
            || SignedCellValue(codeCells[bagProcCell + 5], 8) != 16
            || SignedCellValue(codeCells[bagProcCell + 6], 8) != OpZeroPri
            || SignedCellValue(codeCells[bagProcCell + 7], 8) != OpRetn)
        {
            return false;
        }

        slot = new SwShBagHookSlotState(
            1,
            "occupied",
            checked((int)itemId),
            checked((int)quantity),
            "Legacy one-item Bag-event grant occupies the Royal Candy slot shape.");
        return true;
    }

    private static SwShBagHookSlotState ReadSlot(IReadOnlyList<ulong> codeCells, int slot, int slotStart)
    {
        if (codeCells.Skip(slotStart).Take(SlotWidth).All(cell => SignedCellValue(cell, 8) == OpZeroPri))
        {
            return new SwShBagHookSlotState(slot, "empty", null, null, "Disabled no-op grant slot.");
        }

        if (TryUnpackPushmPc(codeCells[slotStart], out var quantity)
            && TryUnpackPushmPc(codeCells[slotStart + 1], out var itemId)
            && SignedCellValue(codeCells[slotStart + 2], 8) == OpSysreqN
            && SignedCellValue(codeCells[slotStart + 3], 8) == FreedNativeIndex
            && SignedCellValue(codeCells[slotStart + 4], 8) == 16)
        {
            return new SwShBagHookSlotState(
                slot,
                "occupied",
                checked((int)itemId),
                checked((int)quantity),
                "Active AddItem grant slot.");
        }

        return new SwShBagHookSlotState(
            slot,
            "conflict",
            null,
            null,
            "Slot cells do not match an empty slot or the Bag Hook V2 AddItem shape.");
    }

    private static int GetSlotStartCell(int bagProcCell, int slot)
    {
        return bagProcCell + HookProcedureHeaderCellCount + ((slot - 1) * SlotWidth);
    }

    private static ulong[] CreateEmptyHookCells()
    {
        var cells = new ulong[HookCodeCellCount];
        cells[0] = OpProc;
        for (var slot = 1; slot <= SlotCount; slot++)
        {
            WriteEmptySlot(cells, GetSlotStartCell(0, slot));
        }

        cells[101] = OpZeroPri;
        cells[102] = OpRetn;
        return cells;
    }

    private static ulong[] CreateHookMarkerCells()
    {
        return
        [
            MarkerCell0,
            MarkerCell1,
            SlotCount,
            SlotWidth,
            FreedNativeIndex,
        ];
    }

    private static void WriteEmptySlot(IList<ulong> cells, int slotStart)
    {
        for (var index = 0; index < SlotWidth; index++)
        {
            cells[slotStart + index] = OpZeroPri;
        }
    }

    private static void WriteActiveSlot(IList<ulong> cells, int slotStart, int itemId, int quantity)
    {
        cells[slotStart] = PackAmxInstruction(OpPushmPc, quantity, 8);
        cells[slotStart + 1] = PackAmxInstruction(OpPushmPc, itemId, 8);
        cells[slotStart + 2] = OpSysreqN;
        cells[slotStart + 3] = FreedNativeIndex;
        cells[slotStart + 4] = 16;
    }

    private static DecodedAmx Decode(byte[] data)
    {
        var header = SwShAmxHeader.Read(data);
        var cellSize = GetPawnCellSize(header.Magic);
        if (cellSize != 8)
        {
            throw new InvalidDataException($"Expected 64-bit AMX cells in Bag-event script; found {cellSize * 8}-bit cells.");
        }

        if ((header.Flags & PawnFlagCompact) == 0)
        {
            throw new InvalidDataException("Bag-event script is not compact AMX; the Bag Hook patcher expects the vanilla compact layout.");
        }

        var expanded = ExpandAmxIfNeeded(data, header, cellSize);
        VerifyCompactRoundTrip(data, header, expanded, cellSize);
        if (header.Publics != header.Natives)
        {
            throw new InvalidDataException("Bag-event script has public entries; refusing to patch without public-table analysis.");
        }

        return new DecodedAmx(header, cellSize, expanded);
    }

    private static void ValidateSupportedVanillaShape(
        DecodedAmx decoded,
        IReadOnlyList<ulong> codeCells,
        bool requireFreedNativeHash)
    {
        var nativeHashes = ReadNativeHashes(decoded.Expanded, decoded.Header);
        var expectedFreedHash = requireFreedNativeHash ? DuplicatedNativeHash : AddItemNativeHash;
        if (!requireFreedNativeHash && nativeHashes.Length > FreedNativeIndex && nativeHashes[FreedNativeIndex] == DuplicatedNativeHash)
        {
            expectedFreedHash = DuplicatedNativeHash;
        }

        ExpectNative(nativeHashes, FreedNativeIndex, expectedFreedHash);
        ExpectNative(nativeHashes, DuplicateNativeIndex, DuplicatedNativeHash);
        ExpectCell(codeCells, DuplicateNativeCallCell, OpSysreqN, "duplicate native SYSREQ.N");
        ExpectCell(codeCells, DuplicateNativeCallCell + 2, 8, "duplicate native parameter byte count");
        ExpectCell(codeCells, OriginalNoOpGrantStubCell, OpProc, "Bag-event original no-op PROC");
        ExpectCell(codeCells, OriginalNoOpGrantStubCell + 1, OpZeroPri, "Bag-event original no-op ZERO.pri");
        ExpectCell(codeCells, OriginalNoOpGrantStubCell + 2, OpRetn, "Bag-event original no-op RETN");
    }

    private static int? TryDecodeLocalCallTarget(IReadOnlyList<ulong> cells, int callCell, int cellSize)
    {
        if ((uint)(callCell + 1) >= (uint)cells.Count || SignedCellValue(cells[callCell], cellSize) != OpCall)
        {
            return null;
        }

        var relativeBytes = SignedCellValue(cells[callCell + 1], cellSize);
        if (relativeBytes % cellSize != 0)
        {
            return null;
        }

        return checked((int)(callCell + relativeBytes / cellSize));
    }

    private static void ExpectLocalCall(IReadOnlyList<ulong> cells, int callCell, int expectedTargetCell, int cellSize, string label)
    {
        var targetCell = TryDecodeLocalCallTarget(cells, callCell, cellSize);
        if (targetCell is null)
        {
            throw new InvalidDataException($"{label} cell {callCell} is not a readable local CALL.");
        }

        if (targetCell.Value != expectedTargetCell)
        {
            throw new InvalidDataException($"{label} call cell {callCell} targets {targetCell.Value}; expected {expectedTargetCell}.");
        }
    }

    private static byte[] InsertAmxCodeCellsAndAppendDataMarker(
        byte[] expanded,
        SwShAmxHeader header,
        SwShAmxHeader patchedHeader,
        ulong[] cellsToAppend,
        ulong[] markerCells,
        int cellSize)
    {
        if (patchedHeader.Cod != header.Cod)
        {
            throw new InvalidDataException("AMX code insertion cannot change COD.");
        }

        var appendLength = cellsToAppend.Length * cellSize;
        var markerLength = markerCells.Length * cellSize;
        if (patchedHeader.Dat != header.Dat + appendLength || patchedHeader.Hea != header.Hea + appendLength + markerLength)
        {
            throw new InvalidDataException("AMX patched header does not match the requested appended code length.");
        }

        var result = new byte[patchedHeader.Hea];
        Array.Copy(expanded, 0, result, 0, header.Dat);
        WriteCells(result, header.Dat, cellsToAppend, cellSize);
        Array.Copy(expanded, header.Dat, result, patchedHeader.Dat, header.Hea - header.Dat);
        WriteCells(result, patchedHeader.Hea - markerLength, markerCells, cellSize);
        return result;
    }

    private static DecodedAmx MoveLegacyCodeMarkerToDataSection(DecodedAmx decoded, int bagProcCell)
    {
        var markerLength = HookMarkerCellCount * decoded.CellSize;
        var codeMarkerOffset = decoded.Header.Cod + (bagProcCell + HookCodeCellCount) * decoded.CellSize;
        if (codeMarkerOffset + markerLength != decoded.Header.Dat)
        {
            throw new InvalidDataException("Legacy Bag Hook marker is not at the end of the AMX code section; refusing automatic migration.");
        }

        var patchedHeader = decoded.Header with
        {
            Dat = decoded.Header.Dat - markerLength,
        };
        var patchedExpanded = new byte[decoded.Header.Hea];
        Array.Copy(decoded.Expanded, 0, patchedExpanded, 0, codeMarkerOffset);
        Array.Copy(decoded.Expanded, decoded.Header.Dat, patchedExpanded, patchedHeader.Dat, decoded.Header.Hea - decoded.Header.Dat);
        Array.Copy(decoded.Expanded, codeMarkerOffset, patchedExpanded, decoded.Header.Hea - markerLength, markerLength);
        WriteAmxHeaderFields(patchedExpanded, patchedHeader);
        return new DecodedAmx(patchedHeader, decoded.CellSize, patchedExpanded);
    }

    private static bool TryReadDataMarker(DecodedAmx decoded)
    {
        var markerLength = HookMarkerCellCount * decoded.CellSize;
        if (decoded.Header.Hea - decoded.Header.Dat < markerLength)
        {
            return false;
        }

        var markerStart = decoded.Header.Hea - markerLength;
        var markerCells = ReadCells(decoded.Expanded, markerStart, markerLength, decoded.CellSize);
        return IsHookMarker(markerCells);
    }

    private static bool TryReadLegacyCodeMarker(IReadOnlyList<ulong> codeCells, int bagProcCell)
    {
        var markerStart = bagProcCell + HookCodeCellCount;
        if (markerStart + HookMarkerCellCount > codeCells.Count)
        {
            return false;
        }

        var markerCells = codeCells.Skip(markerStart).Take(HookMarkerCellCount).ToArray();
        return IsHookMarker(markerCells);
    }

    private static bool IsHookMarker(IReadOnlyList<ulong> markerCells)
    {
        return markerCells.Count == HookMarkerCellCount
            && markerCells[0] == MarkerCell0
            && markerCells[1] == MarkerCell1
            && SignedCellValue(markerCells[2], 8) == SlotCount
            && SignedCellValue(markerCells[3], 8) == SlotWidth
            && SignedCellValue(markerCells[4], 8) == FreedNativeIndex;
    }

    private static void WriteAmxHeaderFields(byte[] data, SwShAmxHeader header)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x00), header.Size);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), header.Cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), header.Dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), header.Hea);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), header.Stp);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x1C), header.Cip);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x20), header.Publics);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x24), header.Natives);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x28), header.Libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x2C), header.PubVars);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), header.Tags);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x34), header.NameTable);
    }

    private static int GetPawnCellSize(ushort magic) => magic switch
    {
        PawnMagic16 => 2,
        PawnMagic32 => 4,
        PawnMagic64 => 8,
        _ => throw new InvalidDataException($"Unknown AMX magic 0x{magic:X4}."),
    };

    private static uint[] ReadNativeHashes(byte[] data, SwShAmxHeader header)
    {
        if (header.DefSize <= 0 || header.Libraries < header.Natives)
        {
            return [];
        }

        var count = (header.Libraries - header.Natives) / header.DefSize;
        var hashes = new uint[count];
        for (var i = 0; i < count; i++)
        {
            var offset = header.Natives + i * header.DefSize;
            if (offset + header.DefSize > data.Length)
            {
                break;
            }

            hashes[i] = header.DefSize >= 12
                ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8))
                : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        }

        return hashes;
    }

    private static void WriteNativeHash(byte[] data, SwShAmxHeader header, int nativeIndex, uint hash)
    {
        var offset = header.Natives + nativeIndex * header.DefSize + (header.DefSize >= 12 ? 8 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), hash);
    }

    private static byte[] ExpandAmxIfNeeded(byte[] data, SwShAmxHeader header, int cellSize)
    {
        if ((header.Flags & PawnFlagCompact) == 0)
        {
            return data;
        }

        if (header.Hea < header.Cod || header.Size < header.Cod || header.Size > data.Length)
        {
            throw new InvalidDataException("AMX compact header has inconsistent code/data bounds.");
        }

        var expanded = new byte[header.Hea];
        Array.Copy(data, expanded, Math.Min(header.Cod, data.Length));

        var src = header.Size - header.Cod;
        var dst = header.Hea - header.Cod;
        if (dst % cellSize != 0)
        {
            throw new InvalidDataException($"Expanded AMX memory size 0x{dst:X} is not aligned to {cellSize}-byte cells.");
        }

        while (src > 0)
        {
            ulong cell = 0;
            var shift = 0;
            var signSource = 0;
            do
            {
                src--;
                signSource = header.Cod + src;
                var current = data[signSource];
                cell |= (ulong)(current & 0x7F) << shift;
                shift += 7;
            } while (src > 0 && (data[header.Cod + src - 1] & 0x80) != 0);

            if ((data[signSource] & 0x40) != 0)
            {
                while (shift < cellSize * 8)
                {
                    cell |= 0xFFUL << shift;
                    shift += 8;
                }
            }

            dst -= cellSize;
            if (dst < 0)
            {
                throw new InvalidDataException("AMX compact expansion produced more cells than the header allows.");
            }

            WriteCell(expanded, header.Cod + dst, cell, cellSize);
        }

        if (dst != 0)
        {
            throw new InvalidDataException($"AMX compact expansion stopped with 0x{dst:X} bytes unwritten.");
        }

        return expanded;
    }

    private static void VerifyCompactRoundTrip(byte[] original, SwShAmxHeader header, byte[] expanded, int cellSize)
    {
        var rebuilt = BuildCompactAmx(original[..header.Cod], header, expanded, cellSize);
        VerifyExpandedMemory(rebuilt, expanded);
    }

    private static byte[] BuildCompactAmx(byte[] prefix, SwShAmxHeader header, byte[] expanded, int cellSize)
    {
        if (prefix.Length != header.Cod)
        {
            throw new InvalidDataException($"AMX compact prefix length 0x{prefix.Length:X} does not match COD 0x{header.Cod:X}.");
        }

        if (expanded.Length < header.Hea)
        {
            throw new InvalidDataException("Expanded AMX memory is shorter than HEA.");
        }

        var compactBody = CompactAmxMemory(expanded, header, cellSize);
        var result = new byte[header.Cod + compactBody.Length];
        Array.Copy(prefix, result, prefix.Length);
        Array.Copy(compactBody, 0, result, header.Cod, compactBody.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0), result.Length);
        return result;
    }

    private static byte[] CompactAmxMemory(byte[] expanded, SwShAmxHeader header, int cellSize)
    {
        var compact = new List<byte>(Math.Max(0, header.Size - header.Cod));
        for (var offset = header.Cod; offset < header.Hea; offset += cellSize)
        {
            var signed = SignedCellValue(ReadCell(expanded, offset, cellSize), cellSize);
            var chunks = new List<byte>();
            var value = signed;
            while (true)
            {
                var payload = (byte)(value & 0x7F);
                chunks.Add(payload);
                value >>= 7;
                var signBitSet = (payload & 0x40) != 0;
                if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                {
                    break;
                }
            }

            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                var current = chunks[i];
                if (i != 0)
                {
                    current |= 0x80;
                }

                compact.Add(current);
            }
        }

        return compact.ToArray();
    }

    private static void VerifyExpandedMemory(byte[] compactData, byte[] expectedExpanded)
    {
        var header = SwShAmxHeader.Read(compactData);
        var cellSize = GetPawnCellSize(header.Magic);
        var expanded = ExpandAmxIfNeeded(compactData, header, cellSize);
        var normalizedExpected = expectedExpanded.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(normalizedExpected.AsSpan(0x00), compactData.Length);
        if (!expanded.AsSpan(0, normalizedExpected.Length).SequenceEqual(normalizedExpected))
        {
            throw new InvalidDataException("AMX compact round trip for Bag Hook did not preserve expanded memory.");
        }
    }

    private static ulong[] ReadCells(byte[] data, int offset, int length, int cellSize)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
        {
            throw new InvalidDataException($"AMX cell read is outside expanded data: offset 0x{offset:X}, length 0x{length:X}.");
        }

        if (length % cellSize != 0)
        {
            throw new InvalidDataException($"AMX cell span length 0x{length:X} is not aligned to {cellSize}-byte cells.");
        }

        var cells = new ulong[length / cellSize];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = ReadCell(data, offset + i * cellSize, cellSize);
        }

        return cells;
    }

    private static void WriteCells(byte[] data, int offset, IReadOnlyList<ulong> cells, int cellSize)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            WriteCell(data, offset + i * cellSize, cells[i], cellSize);
        }
    }

    private static ulong ReadCell(byte[] data, int offset, int cellSize) => cellSize switch
    {
        2 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)),
        4 => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)),
        8 => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset)),
        _ => throw new ArgumentOutOfRangeException(nameof(cellSize)),
    };

    private static void WriteCell(byte[] data, int offset, ulong value, int cellSize)
    {
        switch (cellSize)
        {
            case 2:
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), checked((ushort)value));
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), checked((uint)value));
                break;
            case 8:
                BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset), value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cellSize));
        }
    }

    private static long SignedCellValue(ulong value, int cellSize) => cellSize switch
    {
        2 => unchecked((short)(ushort)value),
        4 => unchecked((int)(uint)value),
        8 => unchecked((long)value),
        _ => throw new ArgumentOutOfRangeException(nameof(cellSize)),
    };

    private static bool TryUnpackPushmPc(ulong cell, out long operand)
    {
        operand = 0;
        if ((cell & 0xFFFFFFFFUL) != OpPushmPc)
        {
            return false;
        }

        operand = unchecked((int)(uint)(cell >> 32));
        return true;
    }

    private static void ExpectNative(uint[] nativeHashes, int index, uint expectedHash)
    {
        if ((uint)index >= (uint)nativeHashes.Length)
        {
            throw new InvalidDataException($"Bag-event native index {index} is outside import table length {nativeHashes.Length}.");
        }

        if (nativeHashes[index] != expectedHash)
        {
            throw new InvalidDataException($"Bag-event native index {index} is 0x{nativeHashes[index]:X8}; expected 0x{expectedHash:X8}.");
        }
    }

    private static void ExpectCell(IReadOnlyList<ulong> cells, int index, long expected, string label)
    {
        if ((uint)index >= (uint)cells.Count)
        {
            throw new InvalidDataException($"{label} cell {index} is outside code cell count {cells.Count}.");
        }

        var actual = unchecked((long)cells[index]);
        if (actual != expected)
        {
            throw new InvalidDataException($"{label} cell {index} is {actual} (0x{cells[index]:X16}); expected {expected}.");
        }
    }

    private static ulong PackAmxInstruction(int opcode, long operand, int cellSize)
    {
        if (cellSize != 8)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Packed AMX instruction helper currently supports only 64-bit cells.");
        }

        return ((ulong)unchecked((uint)operand) << 32) | (uint)opcode;
    }

    private sealed record DecodedAmx(
        SwShAmxHeader Header,
        int CellSize,
        byte[] Expanded);

    private sealed record SwShAmxHeader(
        int Size,
        ushort Magic,
        byte FileVersion,
        byte AmxVersion,
        short Flags,
        short DefSize,
        int Cod,
        int Dat,
        int Hea,
        int Stp,
        int Cip,
        int Publics,
        int Natives,
        int Libraries,
        int PubVars,
        int Tags,
        int NameTable)
    {
        internal static SwShAmxHeader Read(byte[] data)
        {
            if (data.Length < 0x38)
            {
                throw new InvalidDataException("AMX file is too small for a standard header.");
            }

            var header = new SwShAmxHeader(
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x00)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x04)),
                data[0x06],
                data[0x07],
                BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0x08)),
                BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0x0A)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x0C)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x10)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x14)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x18)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x1C)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x20)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x24)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x28)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x2C)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x30)),
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x34)));

            if (header.Size < 0 || header.Size > data.Length)
            {
                throw new InvalidDataException($"AMX header size 0x{header.Size:X} is outside the file length 0x{data.Length:X}.");
            }

            if (header.Cod < 0 || header.Dat < header.Cod || header.Hea < header.Dat || header.Stp < header.Hea)
            {
                throw new InvalidDataException("AMX header has invalid COD/DAT/HEA/STP ordering.");
            }

            return header;
        }
    }
}
