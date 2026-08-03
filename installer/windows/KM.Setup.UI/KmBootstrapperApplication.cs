// SPDX-License-Identifier: GPL-3.0-only

using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using KM.Setup.UI.Burn;
using KM.Setup.UI.Invocation;
using KM.Setup.UI.Localization;
using KM.Setup.UI.ViewModels;
using KM.Setup.UI.Views;
using WixToolset.BootstrapperApplicationApi;

namespace KM.Setup.UI;

internal sealed class KmBootstrapperApplication : BootstrapperApplication
{
    internal IBootstrapperCommand Command { get; private set; } = null!;

    internal IBootstrapperApplicationData ApplicationData { get; private set; } = null!;

    internal IEngine Engine => engine;

    internal Restart RestartBehavior { get; private set; }

    protected override void OnCreate(CreateEventArgs args)
    {
        base.OnCreate(args);
        Command = args.Command;
        ApplicationData = new BootstrapperApplicationData();

        // Burn deliberately leaves unknown command-line tokens for the BA. The
        // managed API parses only plain Name=Value tokens as variables, then
        // applies only variables authored with bal:Overridable="yes". Without
        // this explicit step, the native updater bridge would remain at its
        // authored defaults even though the launcher supplied valid values.
        var mbaCommand = Command.ParseCommandLine();
        mbaCommand.SetOverridableVariables(ApplicationData.Bundle.OverridableVariables, Engine);

        var explicitPromptRestart = mbaCommand.UnknownCommandLineArgs.Any(argument =>
            string.Equals(argument, "-promptrestart", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "/promptrestart", StringComparison.OrdinalIgnoreCase));
        var defaultRestart = Command.Display < Display.Full ? Restart.Automatic : Restart.Prompt;
        if (explicitPromptRestart && mbaCommand.Restart != defaultRestart)
        {
            throw new ArgumentException("Conflicting system-restart switches were supplied.");
        }

        RestartBehavior = explicitPromptRestart ? Restart.Prompt : mbaCommand.Restart;
    }

    protected override void Run()
    {
        var exitCode = 1603;
        var suppressFailureUi = Command.Display is Display.None or Display.Embedded;

        try
        {
            LocalizationService.Current.UseSystemCulture();
            var invocation = TauriInstallerInvocation.Parse(Command);
            suppressFailureUi |= invocation.DisplayMode == InvocationDisplayMode.Quiet;
            var adapter = new BurnEngineAdapter(this, Engine, Command, ApplicationData, invocation);
            var viewModel = new InstallerViewModel(adapter);

            if (adapter.ShouldShowWindow)
            {
                RunInteractive(adapter, viewModel);
            }
            else
            {
                RunNonvisual(adapter, viewModel);
            }
            exitCode = adapter.ExitCode;
        }
        catch (Exception exception)
        {
            Engine.Log(LogLevel.Error, $"KM Setup UI failed: {exception}");
            if (!suppressFailureUi)
            {
                MessageBox.Show(
                    LocalizationService.Current["FatalBootstrapperFailure"],
                    LocalizationService.Current["SetupWindowTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            Engine.Quit(BurnEngineAdapter.NormalizeExitCode(exitCode));
        }
    }

    private static void RunInteractive(BurnEngineAdapter adapter, InstallerViewModel viewModel)
    {
        var application = new InstallerApplication();
        var window = new MainWindow(viewModel);

        application.MainWindow = window;
        adapter.AttachParentWindow(new WindowInteropHelper(window).EnsureHandle());
        window.Show();
        adapter.Detect();
        application.Run();
    }

    private static void RunNonvisual(BurnEngineAdapter adapter, InstallerViewModel viewModel)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        viewModel.ExitRequested += () => dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);

        using var burnParent = new HwndSource(new HwndSourceParameters("KM Editor Setup Host")
        {
            Width = 1,
            Height = 1,
            WindowStyle = 0,
        });
        if (burnParent.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create the nonvisual Burn parent window.");
        }

        // Burn v7 requires a non-null parent for Apply, even in quiet mode.
        // This native host remains hidden and does not load the themed WPF UI.
        adapter.AttachParentWindow(burnParent.Handle);
        adapter.Detect();
        Dispatcher.Run();
    }
}
