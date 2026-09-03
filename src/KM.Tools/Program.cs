// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Tools;

using KM.Tools.Bridge;
using System.Text;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["bridge-once"])
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            Console.OutputEncoding = utf8;
            await using var input = Console.OpenStandardInput();
            return await new BridgeLineRunner().RunOnceAsync(input, Console.Out).ConfigureAwait(false);
        }

        if (args is ["bridge"])
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            Console.OutputEncoding = utf8;
            await using var input = Console.OpenStandardInput();
            return await new BridgeLineRunner().RunAsync(input, Console.Out).ConfigureAwait(false);
        }

        return 0;
    }
}
