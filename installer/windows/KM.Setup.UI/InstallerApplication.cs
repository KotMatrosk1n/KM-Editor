// SPDX-License-Identifier: GPL-3.0-only

using System.Windows;

namespace KM.Setup.UI;

internal sealed class InstallerApplication : Application
{
    public InstallerApplication()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/KM.Setup.UI;component/Theme/KmTheme.xaml",
                UriKind.Absolute),
        });
    }
}
