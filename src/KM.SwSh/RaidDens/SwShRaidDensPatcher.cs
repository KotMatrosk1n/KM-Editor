// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Formats.SwSh;
using KM.SwSh.Scripts;

namespace KM.SwSh.RaidDens;

public static class SwShRaidDensPatcher
{
    public const string RelativePath = "romfs/bin/script/amx/wide_road.amx";
    private const string VanillaSha256 = "40584E97C01D1013753FE68C59D6DB24816E10CE9B37961FC6363A7FD701EEC1";
    private const long DenCase = 4330679359147574173;
    private const int TableCell = 5842;
    private const int EnabledTarget = 5747;
    private const int DisabledTarget = 5837;

    public static bool ReadDisabled(byte[] vanilla, byte[] source)
    {
        return Inspect(vanilla, source).Disabled;
    }

    public static byte[] Apply(byte[] vanilla, byte[] source, bool disabled)
    {
        var state = Inspect(vanilla, source);
        if (state.Disabled == disabled) return source.ToArray();
        // Pawn case destinations are signed byte offsets relative to the case value cell.
        var valueCell = TableCell + 3 + state.CaseIndex * 2;
        var oldTarget = state.Disabled ? DisabledTarget : EnabledTarget;
        var newTarget = disabled ? DisabledTarget : EnabledTarget;
        var output = SwShAmxCellPatcher.ReplaceSignedCodeCell(
            source, valueCell + 1, (oldTarget - valueCell) * 8L, (newTarget - valueCell) * 8L);
        if (Inspect(vanilla, output).Disabled != disabled)
            throw new InvalidDataException("Raid Dens output did not retain the requested interaction state.");
        var restored = SwShAmxCellPatcher.ReplaceSignedCodeCell(
            output, valueCell + 1, (newTarget - valueCell) * 8L, (oldTarget - valueCell) * 8L);
        if (!restored.AsSpan().SequenceEqual(source))
            throw new InvalidDataException("Raid Dens output changed data outside the owned interaction destination.");
        return output;
    }

    private static (bool Disabled, int CaseIndex) Inspect(byte[] vanilla, byte[] source)
    {
        ArgumentNullException.ThrowIfNull(vanilla);
        ArgumentNullException.ThrowIfNull(source);
        if (Convert.ToHexString(SHA256.HashData(vanilla)) != VanillaSha256)
            throw new InvalidDataException("Raid Dens requires a supported vanilla Sword/Shield den script in Base RomFS.");
        var baseline = SwShAmxDocument.Parse(vanilla);
        var current = SwShAmxDocument.Parse(source);
        // Only the no-op binding is owned by the disabled path. Preserve unrelated native edits.
        if (current.NativeHashes.Count <= 70 || baseline.NativeHashes[70] != current.NativeHashes[70])
            throw new InvalidDataException("The den script no-op binding differs from the supported script.");

        // Verify the dispatcher entry, den branch, existing no-op and normal return path.
        // Other case destinations and unrelated script/data edits remain untouched.
        int[] anchors = [5743, 5744, 5745, 5747, 5748, 5750, 5837, 5840, 5881, 5882,
            7510, 7511, 7512, 7513, 7515, 7516];
        foreach (var cell in anchors)
        {
            var expected = baseline.Instructions.Single(instruction => instruction.OriginalCell == cell);
            var actual = current.Instructions.SingleOrDefault(instruction => instruction.OriginalCell == cell);
            if (actual is null || Signature(expected) != Signature(actual))
                throw new InvalidDataException("The den interaction dispatch or return path contains incompatible changes.");
        }

        var table = current.Instructions.SingleOrDefault(instruction => instruction.OriginalCell == TableCell);
        if (table is null || !table.IsSwitchTable || table.IsIndirectSwitchTable
            || table.DefaultDestination?.Target.OriginalCell != DisabledTarget)
            throw new InvalidDataException("The den interaction dispatch table is unsupported.");
        var cases = table.SwitchCases.Select((entry, index) => (entry, index))
            .Where(item => item.entry.Value == DenCase).ToArray();
        if (cases.Length != 1)
            throw new InvalidDataException("The den interaction case must occur exactly once.");
        var target = cases[0].entry.Destination.Target.OriginalCell;
        if (target is not (EnabledTarget or DisabledTarget))
            throw new InvalidDataException("The den interaction destination contains an unsupported edit.");
        return (target == DisabledTarget, cases[0].index);
    }

    private static string Signature(SwShAmxInstruction instruction) =>
        $"{instruction.Opcode}:{string.Join(',', instruction.Operands.Select(operand =>
            operand.Kind == SwShAmxOperandKind.Literal ? $"v{operand.LiteralValue}" : $"t{operand.Target.OriginalCell}"))}";
}
