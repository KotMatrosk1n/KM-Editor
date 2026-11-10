// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Output;

namespace KM.SwSh.Restoration;

internal static class SwShRestorationSourceBudget
{
    // Use the same limit to read an installed Common.bnk and to save a new one.
    internal const int MaximumVoiceBankBytes = OutputLimits.MaximumWriteBytesPerMutation;

    internal static int MaximumBytes(string relativePath) => relativePath == SwShRestorationComposer.VoicesPath
        ? MaximumVoiceBankBytes : 64 * 1024 * 1024;
}
