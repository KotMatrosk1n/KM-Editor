// SPDX-License-Identifier: GPL-3.0-only

using WixToolset.BootstrapperApplicationApi;
using System.Runtime.InteropServices;
using System.Windows;

namespace KM.Setup.UI;

internal static class Program
{
    // WiX owns the COM apartment used to connect this entry thread to Burn,
    // then invokes the BA's Run callback on its dedicated UI thread. Do not
    // mark Main as STA: that fails with RPC_E_CHANGED_MODE before WPF starts.
    private static int Main()
    {
        var suppressStartupDialog = ShouldSuppressStartupDialog();
        Environment.SetEnvironmentVariable(
            "KM_SETUP_SUPPRESS_STARTUP_DIALOG",
            null,
            EnvironmentVariableTarget.Process);

        try
        {
            ManagedBootstrapperApplication.Run(new KmBootstrapperApplication());
            return 0;
        }
        catch (Exception exception)
        {
            var errorCode = Marshal.GetHRForException(exception);
            if (!suppressStartupDialog)
            {
                MessageBox.Show(
                    $"KM Editor Setup could not start. Try a fresh installer download.\n\nError: 0x{errorCode:X8}",
                    "KM Editor Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return errorCode;
        }
    }

    private static bool ShouldSuppressStartupDialog()
    {
        try
        {
            return string.Equals(
                Environment.GetEnvironmentVariable("KM_SETUP_SUPPRESS_STARTUP_DIALOG"),
                "1",
                StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // A startup failure must still return its error code even if the
            // process command line could not be inspected safely.
        }

        return false;
    }
}
