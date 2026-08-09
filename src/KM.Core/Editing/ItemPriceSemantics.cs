// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Editing;

public static class ItemPriceSemantics
{
    public static bool IsCompatiblePair(int buyPrice, int sellPrice)
    {
        return buyPrice >= 0
            && sellPrice >= 0
            && sellPrice == buyPrice / 2;
    }
}
