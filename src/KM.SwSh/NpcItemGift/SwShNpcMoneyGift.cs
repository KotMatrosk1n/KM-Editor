// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.SwSh.NpcItemGift;

internal static class SwShNpcMoneyGift
{
    public const string RelativePath = "romfs/bin/script/amx/main_event_0180.amx";
    public const int AmountCell = 4813;
    public const int DefaultAmount = 30_000;
    public const int MaximumAmount = 9_999_999;
    public const string AmountInvalidCode = "KM-SWSH-NPC-GIFT-MONEY-AMOUNT-INVALID";
    public const string SourceInvalidCode = "KM-SWSH-NPC-GIFT-MONEY-SOURCE-INVALID";

    public static bool IsValidAmount(int amount) => amount is >= 0 and <= MaximumAmount;

    public static bool TryVerifySource(string? path)
    {
        if (path is null) return false;
        try
        {
            VerifySource(File.ReadAllBytes(path));
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException
            or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void VerifySource(byte[] source)
    {
        var document = SwShAmxDocument.Parse(source);
        var instructions = document.Instructions.ToDictionary(instruction => instruction.OriginalCell!.Value);
        if (document.NativeHashes.Count <= 41 || document.NativeHashes[41] != 0x53987674
            || !Matches(AmountCell, "push.p.c", (long?)null)
            || !Matches(4814, "push.p.c", 8)
            || !instructions.TryGetValue(4815, out var call) || call.Mnemonic != "call"
            || call.Operands[0].Target.OriginalCell != 933
            || !Matches(933, "proc") || !Matches(934, "push.p.s", 24)
            || !Matches(935, "sysreq.n", 41, 8) || !Matches(938, "retn"))
        {
            throw new InvalidDataException("The pocket money grant no longer has its supported amount, helper and native call layout.");
        }

        bool Matches(int cell, string mnemonic, params long?[] operands) =>
            instructions.TryGetValue(cell, out var instruction) && instruction.Mnemonic == mnemonic
            && instruction.Operands.Count == operands.Length
            && operands.Select((value, index) => value is null
                || instruction.Operands[index].LiteralValue == value).All(matches => matches);
    }
}
