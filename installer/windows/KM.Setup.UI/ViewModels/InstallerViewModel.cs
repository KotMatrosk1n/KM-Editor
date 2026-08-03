// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using KM.Setup.UI.Burn;
using KM.Setup.UI.Infrastructure;
using KM.Setup.UI.Localization;
using WixToolset.BootstrapperApplicationApi;

namespace KM.Setup.UI.ViewModels;

internal sealed class InstallerViewModel : ObservableObject
{
    private static readonly TimeSpan MinimumVisibleProgressTime = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan PassiveCompletionTime = TimeSpan.FromMilliseconds(1200);

    private readonly BurnEngineAdapter adapter;
    private readonly Dispatcher dispatcher;
    private readonly LocalizationService strings = LocalizationService.Current;
    private readonly RelayCommand installCommand;
    private readonly RelayCommand repairCommand;
    private readonly RelayCommand uninstallCommand;
    private readonly RelayCommand cancelCommand;
    private readonly RelayCommand closeCommand;
    private readonly RelayCommand tryAgainCommand;
    private readonly RelayCommand openLogCommand;
    private readonly RelayCommand restartNowCommand;

    private InstallerPhase phase = InstallerPhase.Initializing;
    private InstallerActivity activity = InstallerActivity.Initializing;
    private bool installed;
    private bool canSafelyUninstall;
    private bool completionPending;
    private int progressPercent;
    private string heading = string.Empty;
    private string description = string.Empty;
    private string currentPackageName = string.Empty;
    private string errorMessage = string.Empty;
    private string logPath = string.Empty;
    private string completionNotice = string.Empty;
    private bool restartRequired;
    private bool canRestartNow;
    private long progressPresentationStarted;
    private DispatcherTimer? completionDelayTimer;
    private DispatcherTimer? passiveCompletionTimer;

    public InstallerViewModel(BurnEngineAdapter adapter)
    {
        this.adapter = adapter;
        dispatcher = Dispatcher.CurrentDispatcher;

        installCommand = new RelayCommand(
            () => adapter.Plan(LaunchAction.Install),
            () => Phase == InstallerPhase.Ready && !Installed);
        repairCommand = new RelayCommand(
            () => adapter.Plan(LaunchAction.Repair),
            () => Phase == InstallerPhase.Ready && Installed);
        uninstallCommand = new RelayCommand(
            () => adapter.Plan(LaunchAction.Uninstall),
            () => ShowUninstallAction);
        cancelCommand = new RelayCommand(RequestClose, () => CanCancel);
        closeCommand = new RelayCommand(RequestClose);
        tryAgainCommand = new RelayCommand(
            adapter.Retry,
            () => Phase == InstallerPhase.Failed && adapter.IsInteractive);
        openLogCommand = new RelayCommand(adapter.OpenLog, () => !string.IsNullOrWhiteSpace(LogPath));
        restartNowCommand = new RelayCommand(adapter.RestartNow, () => ShowRestartAction);

        adapter.PhaseChanged += OnPhaseChanged;
        adapter.ActivityChanged += OnActivityChanged;
        adapter.DetectionCompleted += OnDetectionCompleted;
        adapter.ProgressChanged += value => Dispatch(() => ProgressPercent = value);
        adapter.PackageChanged += value => Dispatch(() =>
        {
            currentPackageName = value;
            OnPropertyChanged(nameof(CurrentPackage));
        });
        adapter.ErrorReported += value => Dispatch(() => ErrorMessage = value);
        adapter.ApplyCompleted += OnApplyCompleted;
        adapter.ExitRequested += () => Dispatch(() => ExitRequested?.Invoke());

        RefreshCopy();
    }

    public event Action? ExitRequested;

    public InstallerPhase Phase
    {
        get => phase;
        private set
        {
            var previousPhase = phase;
            if (!SetProperty(ref phase, value))
            {
                return;
            }

            if (value == InstallerPhase.Planning && previousPhase != InstallerPhase.Planning)
            {
                progressPresentationStarted = 0;
            }
            else if (value == InstallerPhase.Applying && previousPhase != InstallerPhase.Applying)
            {
                progressPresentationStarted = Stopwatch.GetTimestamp();
            }

            OnPropertyChanged(nameof(ShowReadyPage));
            OnPropertyChanged(nameof(ShowInstallOptions));
            OnPropertyChanged(nameof(ShowProgressPage));
            OnPropertyChanged(nameof(ShowSuccessPage));
            OnPropertyChanged(nameof(ShowFailurePage));
            OnPropertyChanged(nameof(ShowCancelledPage));
            OnPropertyChanged(nameof(ShowBlockedPage));
            OnPropertyChanged(nameof(ShowUninstallAction));
            OnPropertyChanged(nameof(ShowCompletionNotice));
            OnPropertyChanged(nameof(ShowRestartAction));
            OnPropertyChanged(nameof(IsIndeterminate));
            OnPropertyChanged(nameof(ShowProgressPercentage));
            OnPropertyChanged(nameof(CanCancel));
            RefreshCopy();
            RefreshCommands();
        }
    }

    public bool Installed
    {
        get => installed;
        private set
        {
            if (SetProperty(ref installed, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ShowInstallOptions));
                OnPropertyChanged(nameof(ShowUninstallAction));
                RefreshCopy();
                RefreshCommands();
            }
        }
    }

    public int ProgressPercent
    {
        get => progressPercent;
        private set
        {
            if (SetProperty(ref progressPercent, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string Heading
    {
        get => heading;
        private set => SetProperty(ref heading, value);
    }

    public string Description
    {
        get => description;
        private set => SetProperty(ref description, value);
    }

    public string CurrentPackage => string.IsNullOrWhiteSpace(currentPackageName)
        ? GetActivityStatus()
        : strings.Format("StatusWithPackage", GetActivityStatus(), currentPackageName);

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public string LogPath
    {
        get => logPath;
        private set
        {
            if (SetProperty(ref logPath, value))
            {
                openLogCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CompletionNotice
    {
        get => completionNotice;
        private set
        {
            if (SetProperty(ref completionNotice, value))
            {
                OnPropertyChanged(nameof(ShowCompletionNotice));
            }
        }
    }

    public string WindowTitle => adapter.IsUpdate || adapter.IsLegacyMigration
        ? strings["UpdateWindowTitle"]
        : Installed
            ? strings["ManageWindowTitle"]
            : strings["InstallWindowTitle"];

    public string VersionText => string.IsNullOrWhiteSpace(adapter.BundleVersion)
        ? string.Empty
        : strings.Format("VersionLabel", adapter.BundleVersion);

    public string ProgressText => strings.Format("ProgressLabel", ProgressPercent);

    public string PrimaryActionText => adapter.IsUpdate
        ? strings["UpdateButton"]
        : strings["InstallButton"];

    public bool ShowReadyPage => Phase == InstallerPhase.Ready;

    public bool ShowInstallOptions => ShowReadyPage && !Installed;

    public bool ShowProgressPage => Phase is
        InstallerPhase.Initializing or
        InstallerPhase.Detecting or
        InstallerPhase.Planning or
        InstallerPhase.Applying or
        InstallerPhase.Cancelling;

    public bool ShowSuccessPage => Phase == InstallerPhase.Succeeded;

    public bool ShowCompletionNotice => ShowSuccessPage && !string.IsNullOrWhiteSpace(CompletionNotice);

    public bool ShowRestartAction => ShowSuccessPage && restartRequired && canRestartNow;

    public bool ShowFailurePage => Phase == InstallerPhase.Failed;

    public bool ShowCancelledPage => Phase == InstallerPhase.Cancelled;

    public bool ShowBlockedPage => Phase == InstallerPhase.Blocked;

    public bool ShowUninstallAction => Installed &&
        (Phase == InstallerPhase.Ready || Phase == InstallerPhase.Blocked && canSafelyUninstall);

    public bool IsIndeterminate => Phase is InstallerPhase.Initializing or InstallerPhase.Detecting or InstallerPhase.Planning;

    public bool ShowProgressPercentage => ShowProgressPage && !IsIndeterminate;

    public bool CanCancel => !completionPending &&
        (Phase is InstallerPhase.Detecting or InstallerPhase.Planning or InstallerPhase.Applying);

    public ICommand InstallCommand => installCommand;

    public ICommand RepairCommand => repairCommand;

    public ICommand UninstallCommand => uninstallCommand;

    public ICommand CancelCommand => cancelCommand;

    public ICommand CloseCommand => closeCommand;

    public ICommand TryAgainCommand => tryAgainCommand;

    public ICommand OpenLogCommand => openLogCommand;

    public ICommand RestartNowCommand => restartNowCommand;

    public bool CreateStartMenuShortcut
    {
        get => adapter.CreateStartMenuShortcut;
        set
        {
            if (value == adapter.CreateStartMenuShortcut)
            {
                return;
            }

            adapter.CreateStartMenuShortcut = value;
            OnPropertyChanged();
        }
    }

    public bool CreateDesktopShortcut
    {
        get => adapter.CreateDesktopShortcut;
        set
        {
            if (value == adapter.CreateDesktopShortcut)
            {
                return;
            }

            adapter.CreateDesktopShortcut = value;
            OnPropertyChanged();
        }
    }

    public bool DeleteUserSettings
    {
        get => adapter.DeleteUserSettings;
        set
        {
            if (value == adapter.DeleteUserSettings)
            {
                return;
            }

            adapter.DeleteUserSettings = value;
            OnPropertyChanged();
        }
    }

    public void RequestClose()
    {
        if (completionPending)
        {
            return;
        }

        if (Phase is InstallerPhase.Detecting or InstallerPhase.Planning or InstallerPhase.Applying or InstallerPhase.Cancelling)
        {
            adapter.RequestCancel();
            return;
        }

        if (Phase == InstallerPhase.Ready)
        {
            adapter.ExitWithoutApply();
            return;
        }

        ExitRequested?.Invoke();
    }

    private void OnPhaseChanged(InstallerPhase value)
    {
        Dispatch(() => Phase = value);
    }

    private void OnActivityChanged(InstallerActivity value)
    {
        Dispatch(() =>
        {
            activity = value;
            OnPropertyChanged(nameof(CurrentPackage));
            RefreshCopy();
        });
    }

    private void OnDetectionCompleted(DetectionOutcome outcome)
    {
        Dispatch(() =>
        {
            Installed = outcome.Installed;
            canSafelyUninstall = outcome.CanSafelyUninstall;
            OnPropertyChanged(nameof(CreateStartMenuShortcut));
            OnPropertyChanged(nameof(CreateDesktopShortcut));
            OnPropertyChanged(nameof(ShowUninstallAction));
            uninstallCommand.RaiseCanExecuteChanged();
            LogPath = adapter.LogPath ?? string.Empty;

            if (outcome.Cancelled)
            {
                Phase = InstallerPhase.Cancelled;
                if (!adapter.ShouldShowWindow)
                {
                    ExitRequested?.Invoke();
                }
                return;
            }

            if (outcome.BlockingResourceKey is not null)
            {
                Phase = InstallerPhase.Blocked;
                ErrorMessage = strings[outcome.BlockingResourceKey];
                if (adapter.ShouldPlanAutomatically &&
                    Installed &&
                    outcome.CanSafelyUninstall &&
                    adapter.RequestedAction == LaunchAction.Uninstall)
                {
                    adapter.Plan(LaunchAction.Uninstall);
                }
                else if (!adapter.ShouldShowWindow)
                {
                    ExitRequested?.Invoke();
                }
                return;
            }

            if (outcome.FailureResourceKey is not null)
            {
                Phase = InstallerPhase.Failed;
                ErrorMessage = strings[outcome.FailureResourceKey];
                if (!adapter.ShouldShowWindow)
                {
                    ExitRequested?.Invoke();
                }
                return;
            }

            if (outcome.Status < 0)
            {
                Phase = InstallerPhase.Failed;
                ErrorMessage = strings["UnknownFailure"];
                if (!adapter.ShouldShowWindow)
                {
                    ExitRequested?.Invoke();
                }
                return;
            }

            Phase = InstallerPhase.Ready;
            if (adapter.ShouldPlanAutomatically)
            {
                adapter.Plan(adapter.RequestedAction);
            }
        });
    }

    private void OnApplyCompleted(ApplyOutcome outcome)
    {
        Dispatch(() => BeginApplyCompletion(outcome));
    }

    private void BeginApplyCompletion(ApplyOutcome outcome)
    {
        if (!outcome.Succeeded || !adapter.UsesVisibleTiming)
        {
            CommitApplyCompletion(outcome);
            return;
        }

        completionPending = true;
        activity = InstallerActivity.Finalizing;
        OnPropertyChanged(nameof(CurrentPackage));
        OnPropertyChanged(nameof(CanCancel));
        RefreshCopy();
        RefreshCommands();

        var elapsed = progressPresentationStarted == 0
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(progressPresentationStarted);
        var remaining = MinimumVisibleProgressTime - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            CommitApplyCompletion(outcome);
            return;
        }

        completionDelayTimer?.Stop();
        completionDelayTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = remaining,
        };
        completionDelayTimer.Tick += (_, _) =>
        {
            completionDelayTimer?.Stop();
            completionDelayTimer = null;
            CommitApplyCompletion(outcome);
        };
        completionDelayTimer.Start();
    }

    private void CommitApplyCompletion(ApplyOutcome outcome)
    {
        completionPending = false;
        ErrorMessage = outcome.ErrorMessage ?? (outcome.Succeeded ? string.Empty : strings["UnknownFailure"]);
        LogPath = outcome.LogPath ?? string.Empty;
        restartRequired = outcome.RestartRequired;
        canRestartNow = outcome.CanRestartNow;
        OnPropertyChanged(nameof(ShowRestartAction));
        OnPropertyChanged(nameof(CanCancel));
        restartNowCommand.RaiseCanExecuteChanged();
        CompletionNotice = outcome.RestartRequired
            ? strings[outcome.CanRestartNow ? "RestartRequiredNotice" : "RestartRequiredManualNotice"]
            : outcome.RelaunchWarning is null
                ? string.Empty
                : strings[outcome.RelaunchWarning];
        Phase = outcome.Succeeded
            ? InstallerPhase.Succeeded
            : outcome.Cancelled
                ? InstallerPhase.Cancelled
                : InstallerPhase.Failed;

        if (!outcome.Succeeded ||
            !adapter.ShouldExitAfterVisibleCompletion ||
            outcome.RestartRequired ||
            !string.IsNullOrWhiteSpace(CompletionNotice))
        {
            return;
        }

        passiveCompletionTimer?.Stop();
        passiveCompletionTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = PassiveCompletionTime,
        };
        passiveCompletionTimer.Tick += (_, _) =>
        {
            passiveCompletionTimer?.Stop();
            passiveCompletionTimer = null;
            adapter.ExitAfterVisibleCompletion();
        };
        passiveCompletionTimer.Start();
    }

    private void RefreshCopy()
    {
        (Heading, Description) = Phase switch
        {
            InstallerPhase.Initializing => (strings["InitializingHeading"], strings["InitializingDescription"]),
            InstallerPhase.Detecting => (strings["DetectingHeading"], strings["DetectingDescription"]),
            InstallerPhase.Ready when adapter.IsLegacyMigration => (strings["ReadyMigrationHeading"], strings["ReadyMigrationDescription"]),
            InstallerPhase.Ready when adapter.IsUpdate => (strings["ReadyUpdateHeading"], strings["ReadyUpdateDescription"]),
            InstallerPhase.Ready when Installed => (strings["ReadyMaintenanceHeading"], strings["ReadyMaintenanceDescription"]),
            InstallerPhase.Ready => (strings["ReadyInstallHeading"], strings["ReadyInstallDescription"]),
            InstallerPhase.Planning => GetOperationCopy("Planning"),
            InstallerPhase.Applying when activity == InstallerActivity.Caching => GetOperationCopy("Caching"),
            InstallerPhase.Applying when activity == InstallerActivity.Finalizing => GetOperationCopy("Finalizing"),
            InstallerPhase.Applying => GetOperationCopy("Executing"),
            InstallerPhase.Cancelling => (strings["CancellingHeading"], strings["CancellingDescription"]),
            InstallerPhase.Succeeded when adapter.PlannedAction == LaunchAction.Uninstall => (strings["UninstallSuccessHeading"], strings["UninstallSuccessDescription"]),
            InstallerPhase.Succeeded when adapter.PlannedAction == LaunchAction.Repair => (strings["RepairSuccessHeading"], strings["RepairSuccessDescription"]),
            InstallerPhase.Succeeded when adapter.IsUpdate => (strings["UpdateSuccessHeading"], strings["UpdateSuccessDescription"]),
            InstallerPhase.Succeeded => (strings["InstallSuccessHeading"], strings["InstallSuccessDescription"]),
            InstallerPhase.Cancelled => (strings["CancelledHeading"], strings["CancelledDescription"]),
            InstallerPhase.Blocked => (strings["BlockedHeading"], strings["BlockedDescription"]),
            InstallerPhase.Failed => (strings["FailureHeading"], strings["FailureDescription"]),
            _ => (strings["SetupWindowTitle"], strings["InitializingDescription"]),
        };

        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(CurrentPackage));
    }

    private (string Heading, string Description) GetOperationCopy(string stage)
    {
        var operation = adapter.PlannedAction switch
        {
            LaunchAction.Uninstall => "Uninstall",
            LaunchAction.Repair => "Repair",
            _ when adapter.IsUpdate => "Update",
            _ => "Install",
        };
        return (strings[$"{stage}{operation}Heading"], strings[$"{stage}{operation}Description"]);
    }

    private string GetActivityStatus()
    {
        var key = activity switch
        {
            InstallerActivity.Initializing => "StatusInitializing",
            InstallerActivity.Detecting => "StatusDetecting",
            InstallerActivity.Planning => $"StatusPlanning{GetOperationName()}",
            InstallerActivity.Caching => $"StatusCaching{GetOperationName()}",
            InstallerActivity.Executing => $"StatusExecuting{GetOperationName()}",
            InstallerActivity.Finalizing => $"StatusFinalizing{GetOperationName()}",
            InstallerActivity.Cancelling => "StatusCancelling",
            _ => "StatusInitializing",
        };
        return strings[key];
    }

    private string GetOperationName() => adapter.PlannedAction switch
    {
        LaunchAction.Uninstall => "Uninstall",
        LaunchAction.Repair => "Repair",
        _ when adapter.IsUpdate => "Update",
        _ => "Install",
    };

    private void RefreshCommands()
    {
        installCommand.RaiseCanExecuteChanged();
        repairCommand.RaiseCanExecuteChanged();
        uninstallCommand.RaiseCanExecuteChanged();
        cancelCommand.RaiseCanExecuteChanged();
        tryAgainCommand.RaiseCanExecuteChanged();
        restartNowCommand.RaiseCanExecuteChanged();
    }

    private void Dispatch(Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
