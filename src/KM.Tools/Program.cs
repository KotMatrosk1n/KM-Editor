// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Tools;

using KM.Tools.Bridge;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["bridge-once"])
        {
            return await new BridgeLineRunner().RunOnceAsync(Console.In, Console.Out).ConfigureAwait(false);
        }

        return 0;
    }
}
