// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using KM.Setup.UI.ViewModels;

namespace KM.Setup.UI.Views;

internal partial class MainWindow : Window
{
    private const uint MonitorDefaultToNearest = 2;
    private const double WorkAreaMargin = 12;
    private const double PreferredWidth = 760;
    private const double PreferredHeight = 440;

    private readonly InstallerViewModel viewModel;
    private bool engineApprovedClose;

    public MainWindow(InstallerViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ExitRequested += CloseFromInstaller;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FitToCurrentMonitorWorkArea();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (engineApprovedClose)
        {
            return;
        }

        e.Cancel = true;
        viewModel.RequestClose();
    }

    private void CloseFromInstaller()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(CloseFromInstaller);
            return;
        }

        engineApprovedClose = true;
        Close();
        Application.Current.Shutdown();
    }

    private void FitToCurrentMonitorWorkArea()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };

        if (monitorHandle == IntPtr.Zero || !GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return;
        }

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return;
        }

        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new Point(monitorInfo.Work.Left, monitorInfo.Work.Top));
        var bottomRight = fromDevice.Transform(new Point(monitorInfo.Work.Right, monitorInfo.Work.Bottom));
        var workArea = new Rect(topLeft, bottomRight);
        var availableWidth = Math.Max(1, workArea.Width - (WorkAreaMargin * 2));
        var availableHeight = Math.Max(1, workArea.Height - (WorkAreaMargin * 2));

        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        Width = Math.Min(PreferredWidth, availableWidth);
        Height = Math.Min(PreferredHeight, availableHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = workArea.Left + ((workArea.Width - Width) / 2);
        Top = workArea.Top + ((workArea.Height - Height) / 2);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
