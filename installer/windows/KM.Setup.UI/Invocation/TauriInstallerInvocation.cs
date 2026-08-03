// SPDX-License-Identifier: GPL-3.0-only

using WixToolset.BootstrapperApplicationApi;

namespace KM.Setup.UI.Invocation;

internal enum InvocationDisplayMode
{
    EngineDefault,
    Passive,
    Quiet,
}

internal sealed record TauriInstallerInvocation(
    InvocationDisplayMode DisplayMode,
    bool IsUpdate,
    bool RelaunchRequested,
    IReadOnlyList<string> RelaunchArguments)
{
    public static TauriInstallerInvocation Parse(IBootstrapperCommand command)
    {
        var displayMode = InvocationDisplayMode.EngineDefault;
        var isUpdate = false;
        var relaunchRequested = false;
        var relaunchArguments = Array.Empty<string>();
        var arguments = BootstrapperCommand.ParseCommandLineToArgs(command.CommandLine);

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];

            // Tauri places application arguments after /ARGS. They are opaque app input,
            // not installer switches, so parsing must stop at this delimiter.
            if (argument.Equals("/ARGS", StringComparison.OrdinalIgnoreCase))
            {
                relaunchArguments = arguments[(index + 1)..];
                break;
            }

            if (argument.Equals("/P", StringComparison.OrdinalIgnoreCase))
            {
                displayMode = InvocationDisplayMode.Passive;
            }
            else if (argument.Equals("/S", StringComparison.OrdinalIgnoreCase))
            {
                displayMode = InvocationDisplayMode.Quiet;
            }
            else if (argument.Equals("/R", StringComparison.OrdinalIgnoreCase))
            {
                relaunchRequested = true;
            }
            else if (argument.Equals("/UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                isUpdate = true;
            }
        }

        return new TauriInstallerInvocation(displayMode, isUpdate, relaunchRequested, relaunchArguments);
    }
}
