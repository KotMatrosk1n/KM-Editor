// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using KM.Setup.UI.Invocation;
using WixToolset.BootstrapperApplicationApi;
using File = System.IO.File;
using Path = System.IO.Path;

namespace KM.Setup.UI.Burn;

internal enum InstallerPhase
{
    Initializing,
    Detecting,
    Ready,
    Planning,
    Applying,
    Cancelling,
    Succeeded,
    Failed,
    Cancelled,
    Blocked,
}

internal enum InstallerActivity
{
    Initializing,
    Detecting,
    Planning,
    Caching,
    Executing,
    Finalizing,
    Cancelling,
}

internal enum FreshInstallScope
{
    Default,
    PerUser,
    PerMachine,
}

internal sealed record DetectionOutcome(
    bool Installed,
    int Status,
    bool Cancelled = false,
    bool CanSafelyUninstall = false,
    string? BlockingResourceKey = null,
    string? FailureResourceKey = null);

internal sealed record ApplyOutcome(
    bool Succeeded,
    bool Cancelled,
    int Status,
    string? ErrorMessage,
    string? LogPath,
    string? RelaunchWarning,
    bool RestartRequired,
    bool CanRestartNow);

internal sealed class BurnEngineAdapter
{
    private const int ErrorInstallUserExit = 1602;
    private const int ErrorInstallFailure = 1603;
    private const int ErrorSuccessRebootInitiated = 1641;
    private const int ErrorSuccessRebootRequired = 3010;
    private const int HResultInstallUserExit = unchecked((int)0x80070642);
    private const string InstalledExecutableName = "km-editor-desktop.exe";
    private const string LegacyDisplayName = "KM Editor";
    private const string LegacyPublisher = "kmeditor";
    private const string LegacyUninstallerName = "uninstall.exe";

    private readonly IEngine engine;
    private readonly IBootstrapperCommand command;
    private readonly IBootstrapperApplicationData applicationData;
    private readonly TauriInstallerInvocation invocation;
    private readonly bool invocationWasBridged;
    private readonly bool isUpdate;
    private readonly bool relaunchRequested;
    private readonly Restart restartBehavior;
    private readonly IReadOnlyList<string> relaunchArguments;
    private readonly string? invocationFailureResourceKey;
    private readonly Dictionary<string, RelatedMsiDetection> relatedMsiProducts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> seenPerUserRelatedBundleIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<bool>> relatedBundleDuplicateOccurrences =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> relatedBundlePlanOccurrenceIndices =
        new(StringComparer.OrdinalIgnoreCase);

    private bool cancelRequested;
    private bool installed;
    private bool legacyMigrationRequired;
    private bool relatedMsiConflict;
    private bool restartApproved;
    private bool restartCanBeInitiated;
    private bool shortcutPreferencesEdited;
    private bool shortcutPreferencesLoaded;
    private bool deleteUserSettings;
    private IntPtr parentWindowHandle;
    private LaunchAction retryActionAfterDetection = LaunchAction.Unknown;
    private FreshInstallScope plannedFreshInstallScope = FreshInstallScope.Default;
    private FreshInstallScope retryFreshInstallScopeAfterDetection = FreshInstallScope.Default;
    private BundleScope? currentMsiScope;
    private BundleScope? requestedPlanScope;
    private BundleScope? shortcutPreferenceScope;
    private BundleScope? relatedMsiScope;
    private BundleScope? supersededUnregisterScope;
    private string? currentMsiInstallFolder;
    private string? legacyNsisInstallFolder;
    private string? relatedMsiInstallFolder;
    private string? blockingResourceKey;
    private string? lastError;

    public BurnEngineAdapter(
        KmBootstrapperApplication bootstrapper,
        IEngine engine,
        IBootstrapperCommand command,
        IBootstrapperApplicationData applicationData,
        TauriInstallerInvocation invocation)
    {
        this.engine = engine;
        this.command = command;
        this.applicationData = applicationData;
        this.invocation = invocation;
        invocationWasBridged = ReadBooleanVariable("KMInvocationBridged");
        if (invocationWasBridged)
        {
            isUpdate = ReadBooleanVariable("KMUpdateMode");
            relaunchRequested = ReadBooleanVariable("KMAutoLaunch");
            var encodedArguments = TryGetVariableString("KMLaunchArgumentsBase64") ?? string.Empty;
            if (!BridgedInvocationPayload.TryDecode(encodedArguments, out relaunchArguments))
            {
                relaunchArguments = Array.Empty<string>();
                invocationFailureResourceKey = "InvocationBridgeFailure";
                engine.Log(LogLevel.Error, "The native updater invocation envelope was invalid; setup will stop before Plan.");
            }
        }
        else
        {
            isUpdate = invocation.IsUpdate;
            relaunchRequested = invocation.RelaunchRequested;
            relaunchArguments = invocation.RelaunchArguments;
        }

        // Passive Burn defaults to Automatic restart. A bridged Tauri update
        // never inherits that default: /R requests an application relaunch,
        // not permission to reboot Windows. Explicit Prompt/Always modes from
        // the compatibility launcher are still preserved.
        restartBehavior = invocationWasBridged && isUpdate && bootstrapper.RestartBehavior == Restart.Automatic
            ? Restart.Never
            : bootstrapper.RestartBehavior;

        bootstrapper.DetectBegin += OnDetectBegin;
        bootstrapper.DetectRelatedBundle += OnDetectRelatedBundle;
        bootstrapper.DetectRelatedMsiPackage += OnDetectRelatedMsiPackage;
        bootstrapper.DetectComplete += OnDetectComplete;
        bootstrapper.PlanBegin += OnPlanBegin;
        bootstrapper.PlanRelatedBundleType += OnPlanRelatedBundleType;
        bootstrapper.PlanComplete += OnPlanComplete;
        bootstrapper.ApplyBegin += OnApplyBegin;
        bootstrapper.CacheBegin += OnCacheBegin;
        bootstrapper.Progress += OnProgress;
        bootstrapper.CacheAcquireProgress += OnCacheAcquireProgress;
        bootstrapper.CacheContainerOrPayloadVerifyProgress += OnCacheContainerOrPayloadVerifyProgress;
        bootstrapper.CachePayloadExtractProgress += OnCachePayloadExtractProgress;
        bootstrapper.CacheVerifyProgress += OnCacheVerifyProgress;
        bootstrapper.ExecutePackageBegin += OnExecutePackageBegin;
        bootstrapper.ExecuteBegin += OnExecuteBegin;
        bootstrapper.ExecuteComplete += OnExecuteComplete;
        bootstrapper.ExecuteProgress += OnExecuteProgress;
        bootstrapper.Error += OnError;
        bootstrapper.ApplyComplete += OnApplyComplete;
        bootstrapper.Shutdown += OnShutdown;
    }

    public event Action<InstallerPhase>? PhaseChanged;

    public event Action<InstallerActivity>? ActivityChanged;

    public event Action<DetectionOutcome>? DetectionCompleted;

    public event Action<int>? ProgressChanged;

    public event Action<string>? PackageChanged;

    public event Action<string>? ErrorReported;

    public event Action<ApplyOutcome>? ApplyCompleted;

    public event Action? ExitRequested;

    public bool IsUpdate => isUpdate || legacyMigrationRequired || relatedMsiProducts.Count != 0;

    public bool IsLegacyMigration => legacyMigrationRequired;

    public bool IsInteractive =>
        command.Display == Display.Full && invocation.DisplayMode == InvocationDisplayMode.EngineDefault && !isUpdate;

    public bool ShouldShowWindow =>
        invocation.DisplayMode != InvocationDisplayMode.Quiet &&
        command.Display is not Display.None and not Display.Embedded;

    public bool ShouldPlanAutomatically => !IsInteractive;

    public bool UsesVisibleTiming => ShouldShowWindow;

    public bool ShouldExitAfterVisibleCompletion => ShouldShowWindow && ShouldPlanAutomatically;

    public LaunchAction RequestedAction => isUpdate
        ? LaunchAction.Install
        : command.Action == LaunchAction.Unknown
            ? LaunchAction.Install
            : command.Action;

    public LaunchAction PlannedAction { get; private set; } = LaunchAction.Unknown;

    public int ExitCode { get; private set; }

    public string BundleVersion => TryGetVariableString("WixBundleVersion") ?? string.Empty;

    public string? LogPath => GetLogPath();

    public bool CreateStartMenuShortcut
    {
        get => ReadBooleanVariable("KMCreateStartMenuShortcut");
        set
        {
            if (SetNumericVariable("KMCreateStartMenuShortcut", value ? 1 : 0))
            {
                shortcutPreferencesEdited = true;
            }
        }
    }

    public bool CreateDesktopShortcut
    {
        get => ReadBooleanVariable("KMCreateDesktopShortcut");
        set
        {
            if (SetNumericVariable("KMCreateDesktopShortcut", value ? 1 : 0))
            {
                shortcutPreferencesEdited = true;
            }
        }
    }

    public bool DeleteUserSettings
    {
        get => deleteUserSettings;
        set => deleteUserSettings = value;
    }

    public void AttachParentWindow(IntPtr handle)
    {
        parentWindowHandle = handle;
    }

    public void Detect()
    {
        ActivityChanged?.Invoke(InstallerActivity.Detecting);
        PhaseChanged?.Invoke(InstallerPhase.Detecting);
        engine.Detect();
    }

    public void Plan(LaunchAction action, FreshInstallScope freshInstallScope = FreshInstallScope.Default)
    {
        var unregisterSupersededBundle = supersededUnregisterScope.HasValue;
        var registeredMsiScope = BundleScope.PerUser;
        var registeredMsiInstallFolder = string.Empty;
        var safeBlockedUninstall = blockingResourceKey is not null &&
            installed &&
            action == LaunchAction.Uninstall &&
            TryGetRegisteredBundleMsi(out registeredMsiScope, out registeredMsiInstallFolder);

        if (blockingResourceKey is not null && !safeBlockedUninstall)
        {
            ExitCode = ErrorInstallFailure;
            PhaseChanged?.Invoke(InstallerPhase.Blocked);
            return;
        }

        if (unregisterSupersededBundle && action != LaunchAction.Uninstall)
        {
            FailBeforePlan("A superseded setup can only unregister itself. No changes were planned.");
            return;
        }

        cancelRequested = false;
        lastError = null;
        ProgressChanged?.Invoke(0);
        PackageChanged?.Invoke(string.Empty);
        PlannedAction = action;
        plannedFreshInstallScope = freshInstallScope;
        var scope = supersededUnregisterScope ??
            (safeBlockedUninstall ? registeredMsiScope : ResolvePlanScope(freshInstallScope));
        if (!unregisterSupersededBundle &&
            !TryConfigurePlanVariables(
                scope,
                safeBlockedUninstall ? registeredMsiInstallFolder : null,
                loadShortcutPreferences: action != LaunchAction.Uninstall))
        {
            FailBeforePlan("Setup could not verify its installation scope and path. No changes were planned.");
            return;
        }

        if (!unregisterSupersededBundle && !TryConfigureDeleteUserSettings(action))
        {
            FailBeforePlan("Setup could not verify the user-settings removal choice. No changes were planned.");
            return;
        }

        if (!unregisterSupersededBundle)
        {
            SetUpdaterVariables();
        }

        if (!TrySetRequestedPlanScope(scope))
        {
            FailBeforePlan("Setup could not verify its requested installation scope. No changes were planned.");
            return;
        }

        ActivityChanged?.Invoke(InstallerActivity.Planning);
        PhaseChanged?.Invoke(InstallerPhase.Planning);
        engine.Log(
            LogLevel.Verbose,
            unregisterSupersededBundle
                ? $"Planning superseded bundle self-unregistration with proven bundle scope {scope}."
                : $"Planning {action} with bundle scope {scope}.");
        engine.Plan(action, scope);
    }

    public void RequestCancel()
    {
        cancelRequested = true;
        ActivityChanged?.Invoke(InstallerActivity.Cancelling);
        PhaseChanged?.Invoke(InstallerPhase.Cancelling);
    }

    public void ExitAfterVisibleCompletion()
    {
        if (ShouldExitAfterVisibleCompletion)
        {
            ExitRequested?.Invoke();
        }
    }

    public void Retry()
    {
        retryActionAfterDetection = PlannedAction;
        retryFreshInstallScopeAfterDetection = plannedFreshInstallScope;
        cancelRequested = false;
        lastError = null;
        ProgressChanged?.Invoke(0);
        PackageChanged?.Invoke(string.Empty);
        Detect();
    }

    public void ExitWithoutApply()
    {
        ExitCode = ErrorInstallUserExit;
        ExitRequested?.Invoke();
    }

    public void OpenLog()
    {
        var path = GetLogPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            engine.Log(LogLevel.Error, $"Could not open the bundle log: {exception.Message}");
        }
    }

    public void RestartNow()
    {
        if (!restartCanBeInitiated)
        {
            return;
        }

        restartApproved = true;
        ExitCode = ErrorSuccessRebootInitiated;
        ExitRequested?.Invoke();
    }

    public static int NormalizeExitCode(int status)
    {
        return (status & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)
            ? status & 0xFFFF
            : status;
    }

    private void OnDetectBegin(object? sender, DetectBeginEventArgs e)
    {
        installed = e.RegistrationType == RegistrationType.Full;
        blockingResourceKey = null;
        legacyMigrationRequired = false;
        legacyNsisInstallFolder = null;
        relatedMsiConflict = false;
        restartApproved = false;
        restartCanBeInitiated = false;
        relatedMsiProducts.Clear();
        seenPerUserRelatedBundleIds.Clear();
        relatedBundleDuplicateOccurrences.Clear();
        relatedBundlePlanOccurrenceIndices.Clear();
        currentMsiScope = null;
        requestedPlanScope = null;
        relatedMsiScope = null;
        supersededUnregisterScope = null;
        currentMsiInstallFolder = null;
        relatedMsiInstallFolder = null;
        if (!shortcutPreferencesEdited)
        {
            shortcutPreferencesLoaded = false;
            shortcutPreferenceScope = null;
        }
        PhaseChanged?.Invoke(InstallerPhase.Detecting);
        ActivityChanged?.Invoke(InstallerActivity.Detecting);
    }

    private void OnDetectRelatedBundle(object? sender, DetectRelatedBundleEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.ProductCode))
        {
            return;
        }

        // Burn 7 enumerates both registry views on 64-bit Windows and can emit
        // the same per-user bundle registration twice. Preserve a per-machine
        // occurrence with the same bundle ID because that can be a genuinely
        // separate registration in another scope.
        var duplicate = !e.PerMachine && !seenPerUserRelatedBundleIds.Add(e.ProductCode);
        if (!relatedBundleDuplicateOccurrences.TryGetValue(e.ProductCode, out var occurrences))
        {
            occurrences = new List<bool>();
            relatedBundleDuplicateOccurrences[e.ProductCode] = occurrences;
        }

        occurrences.Add(duplicate);
        if (duplicate)
        {
            engine.Log(
                LogLevel.Verbose,
                $"Detected duplicate per-user related bundle occurrence {e.ProductCode}; its duplicate plan entry will be suppressed.");
        }
    }

    private void OnDetectRelatedMsiPackage(object? sender, DetectRelatedMsiPackageEventArgs e)
    {
        if (!applicationData.Bundle.Packages.TryGetValue(e.PackageId, out var package) ||
            string.IsNullOrWhiteSpace(package.UpgradeCode) ||
            !string.Equals(package.UpgradeCode, e.UpgradeCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(e.ProductCode))
        {
            relatedMsiConflict = true;
            engine.Log(LogLevel.Error, "Burn reported a related KM MSI without a product identity.");
            return;
        }

        var isCurrentProduct = string.Equals(package.ProductCode, e.ProductCode, StringComparison.OrdinalIgnoreCase);
        var detection = new RelatedMsiDetection(
            e.PerMachine ? BundleScope.PerMachine : BundleScope.PerUser,
            MsiProductInfo.TryGetInstallLocation(e.ProductCode, mustExist: !isCurrentProduct));

        if (isCurrentProduct)
        {
            if (currentMsiScope.HasValue &&
                (currentMsiScope.Value != detection.Scope ||
                 !string.Equals(currentMsiInstallFolder, detection.InstallFolder, StringComparison.OrdinalIgnoreCase)))
            {
                relatedMsiConflict = true;
                currentMsiScope = null;
                currentMsiInstallFolder = null;
            }
            else if (detection.InstallFolder is not null)
            {
                currentMsiScope = detection.Scope;
                currentMsiInstallFolder = detection.InstallFolder;
            }
        }

        if (isCurrentProduct)
        {
            relatedMsiConflict |= detection.InstallFolder is null;
        }
        else
        {
            if (relatedMsiProducts.TryGetValue(e.ProductCode, out var existing) &&
                (existing.Scope != detection.Scope ||
                 !string.Equals(existing.InstallFolder, detection.InstallFolder, StringComparison.OrdinalIgnoreCase)))
            {
                relatedMsiConflict = true;
            }
            else
            {
                relatedMsiProducts[e.ProductCode] = detection;
            }

            relatedMsiConflict |= relatedMsiProducts.Count > 1 || detection.InstallFolder is null;
        }

        if (relatedMsiConflict)
        {
            relatedMsiScope = null;
            relatedMsiInstallFolder = null;
        }
        else
        {
            relatedMsiScope = detection.Scope;
            relatedMsiInstallFolder = detection.InstallFolder;
        }

        engine.Log(LogLevel.Verbose, $"Detected related KM MSI {e.ProductCode} in {detection.Scope} scope.");
    }

    private void OnDetectComplete(object? sender, DetectCompleteEventArgs e)
    {
        ExitCode = e.Status;
        if (e.Status < 0)
        {
            retryActionAfterDetection = LaunchAction.Unknown;
            retryFreshInstallScopeAfterDetection = FreshInstallScope.Default;
            PhaseChanged?.Invoke(InstallerPhase.Failed);
            DetectionCompleted?.Invoke(new DetectionOutcome(installed, e.Status));
            return;
        }

        if (cancelRequested)
        {
            retryActionAfterDetection = LaunchAction.Unknown;
            retryFreshInstallScopeAfterDetection = FreshInstallScope.Default;
            ExitCode = ErrorInstallUserExit;
            PhaseChanged?.Invoke(InstallerPhase.Cancelled);
            DetectionCompleted?.Invoke(new DetectionOutcome(
                installed,
                ErrorInstallUserExit,
                Cancelled: true));
            return;
        }

        if (invocationFailureResourceKey is not null)
        {
            retryActionAfterDetection = LaunchAction.Unknown;
            retryFreshInstallScopeAfterDetection = FreshInstallScope.Default;
            ExitCode = ErrorInstallFailure;
            PhaseChanged?.Invoke(InstallerPhase.Failed);
            DetectionCompleted?.Invoke(new DetectionOutcome(
                installed,
                ErrorInstallFailure,
                FailureResourceKey: invocationFailureResourceKey));
            return;
        }

        var capturedExactCurrentMsi = TryCaptureExactCurrentMsi(out var exactCurrentMsiRegistered);
        supersededUnregisterScope = CanSafelyUnregisterSupersededBundle(
                capturedExactCurrentMsi,
                exactCurrentMsiRegistered)
            ? relatedMsiScope
            : null;
        var unregisterSupersededBundle = supersededUnregisterScope.HasValue;
        if (unregisterSupersededBundle)
        {
            engine.Log(
                LogLevel.Standard,
                "A newer related bundle replaced this bundle's MSI; the embedded upgrade handoff may unregister only this superseded bundle.");
        }
        else
        {
            if ((installed || exactCurrentMsiRegistered || currentMsiScope.HasValue) && !capturedExactCurrentMsi)
            {
                relatedMsiConflict = true;
                engine.Log(LogLevel.Error, "The exact current KM MSI product, scope, and install path could not be proven.");
            }

            if (currentMsiScope.HasValue && relatedMsiProducts.Count != 0)
            {
                relatedMsiConflict = true;
                engine.Log(LogLevel.Error, "The current KM MSI has another MSI owner in the same upgrade family.");
            }

            if (!installed &&
                HasExistingMsiRegistration() &&
                !currentMsiScope.HasValue &&
                relatedMsiProducts.Count == 0)
            {
                relatedMsiConflict = true;
                engine.Log(LogLevel.Error, "Windows Installer reported a KM product, but Burn did not provide an exact package identity and path.");
            }

            var existingMsiVersionState = GetExistingMsiVersionState();
            if (relatedMsiConflict ||
                RelatedMsiScopeConflictsWithRegisteredBundle() ||
                existingMsiVersionState == ExistingMsiVersionState.Invalid)
            {
                blockingResourceKey = "ExistingInstallConflictDescription";
            }
            else if (existingMsiVersionState == ExistingMsiVersionState.Newer)
            {
                blockingResourceKey = "NewerInstallDescription";
            }
            else
            {
                var legacyState = DetectLegacyNsisState(out var validatedLegacyFolder, out var legacyVersion);
                if (legacyState == LegacyNsisState.Confirmed &&
                    (installed || relatedMsiProducts.Count != 0 || HasExistingMsiRegistration()))
                {
                    blockingResourceKey = "ExistingInstallConflictDescription";
                }
                else if (legacyState == LegacyNsisState.Confirmed && IsLegacyVersionNewer(legacyVersion))
                {
                    blockingResourceKey = "NewerInstallDescription";
                }
                else if (legacyState == LegacyNsisState.Confirmed)
                {
                    legacyMigrationRequired = true;
                    legacyNsisInstallFolder = validatedLegacyFolder;
                    engine.Log(LogLevel.Standard, "A verified legacy NSIS installation is eligible for the rollback-backed MSI takeover.");
                }
                else if (legacyState == LegacyNsisState.Ambiguous)
                {
                    blockingResourceKey = "ExistingInstallConflictDescription";
                }
            }
        }

        if (blockingResourceKey is null &&
            !unregisterSupersededBundle &&
            !EnsureStableShortcutPreferences(ResolvePlanScope(FreshInstallScope.Default)))
        {
            blockingResourceKey = "ShortcutPreferenceFailure";
        }

        if (blockingResourceKey is not null)
        {
            ExitCode = ErrorInstallFailure;
            engine.Log(LogLevel.Error, "An existing KM installation could not be changed safely; install, update, and repair planning are blocked.");
            PhaseChanged?.Invoke(InstallerPhase.Blocked);
        }
        else
        {
            PhaseChanged?.Invoke(InstallerPhase.Ready);
        }

        var retryAction = blockingResourceKey is null
            ? retryActionAfterDetection
            : LaunchAction.Unknown;
        var retryFreshInstallScope = retryFreshInstallScopeAfterDetection;
        retryActionAfterDetection = LaunchAction.Unknown;
        retryFreshInstallScopeAfterDetection = FreshInstallScope.Default;
        DetectionCompleted?.Invoke(new DetectionOutcome(
            installed,
            e.Status,
            CanSafelyUninstall: installed &&
                (unregisterSupersededBundle || TryGetRegisteredBundleMsi(out _, out _)),
            BlockingResourceKey: blockingResourceKey));

        // Burn requires a fresh Detect before another Plan/Apply attempt.
        // Resume the exact failed action only after that detection succeeded.
        if (retryAction != LaunchAction.Unknown)
        {
            Plan(retryAction, retryFreshInstallScope);
        }
    }

    private void OnPlanBegin(object? sender, PlanBeginEventArgs e)
    {
        relatedBundlePlanOccurrenceIndices.Clear();
        ActivityChanged?.Invoke(InstallerActivity.Planning);
        PhaseChanged?.Invoke(InstallerPhase.Planning);
    }

    private void OnPlanRelatedBundleType(object? sender, PlanRelatedBundleTypeEventArgs e)
    {
        var occurrenceIndex = relatedBundlePlanOccurrenceIndices.TryGetValue(e.BundleCode, out var nextIndex)
            ? nextIndex
            : 0;
        relatedBundlePlanOccurrenceIndices[e.BundleCode] = occurrenceIndex + 1;

        if (relatedBundleDuplicateOccurrences.TryGetValue(e.BundleCode, out var occurrences) &&
            occurrenceIndex < occurrences.Count &&
            occurrences[occurrenceIndex])
        {
            e.Type = RelatedBundlePlanType.None;
            engine.Log(
                LogLevel.Verbose,
                $"Suppressed duplicate per-user related bundle plan occurrence {e.BundleCode}.");
        }
    }

    private void OnPlanComplete(object? sender, PlanCompleteEventArgs e)
    {
        ExitCode = e.Status;
        if (e.Status < 0)
        {
            PhaseChanged?.Invoke(InstallerPhase.Failed);
            ApplyCompleted?.Invoke(new ApplyOutcome(
                false,
                false,
                e.Status,
                lastError,
                GetLogPath(),
                null,
                RestartRequired: false,
                CanRestartNow: false));
            if (!ShouldShowWindow)
            {
                ExitRequested?.Invoke();
            }
            return;
        }

        if (cancelRequested)
        {
            ExitCode = ErrorInstallUserExit;
            PhaseChanged?.Invoke(InstallerPhase.Cancelled);
            ApplyCompleted?.Invoke(new ApplyOutcome(
                false,
                true,
                ErrorInstallUserExit,
                null,
                GetLogPath(),
                null,
                RestartRequired: false,
                CanRestartNow: false));
            return;
        }

        if (!RequestedPlanScopeMatchesBurn())
        {
            FailBeforePlan("Setup could not verify the final installation scope. No changes were applied.");
            return;
        }

        ActivityChanged?.Invoke(InstallerActivity.Caching);
        PhaseChanged?.Invoke(InstallerPhase.Applying);
        try
        {
            engine.Apply(parentWindowHandle);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException)
        {
            engine.Log(LogLevel.Error, $"Burn could not begin Apply: {exception}");
            FailBeforePlan("Setup could not begin applying changes. No changes were applied.");
        }
    }

    private void OnApplyBegin(object? sender, ApplyBeginEventArgs e)
    {
        ActivityChanged?.Invoke(InstallerActivity.Caching);
        PhaseChanged?.Invoke(InstallerPhase.Applying);
    }

    private void OnCacheBegin(object? sender, CacheBeginEventArgs e)
    {
        ActivityChanged?.Invoke(InstallerActivity.Caching);
        e.Cancel = cancelRequested;
    }

    private void OnProgress(object? sender, ProgressEventArgs e)
    {
        ProgressChanged?.Invoke(Math.Clamp(e.OverallPercentage, 0, 100));
        e.Cancel = cancelRequested;
    }

    private void OnCacheAcquireProgress(object? sender, CacheAcquireProgressEventArgs e)
    {
        e.Cancel = cancelRequested;
    }

    private void OnCacheContainerOrPayloadVerifyProgress(object? sender, CacheContainerOrPayloadVerifyProgressEventArgs e)
    {
        e.Cancel = cancelRequested;
    }

    private void OnCachePayloadExtractProgress(object? sender, CachePayloadExtractProgressEventArgs e)
    {
        e.Cancel = cancelRequested;
    }

    private void OnCacheVerifyProgress(object? sender, CacheVerifyProgressEventArgs e)
    {
        e.Cancel = cancelRequested;
    }

    private void OnExecuteProgress(object? sender, ExecuteProgressEventArgs e)
    {
        e.Cancel = cancelRequested;
    }

    private void OnExecuteBegin(object? sender, ExecuteBeginEventArgs e)
    {
        ActivityChanged?.Invoke(InstallerActivity.Executing);
        e.Cancel = cancelRequested;
    }

    private void OnExecuteComplete(object? sender, ExecuteCompleteEventArgs e)
    {
        ActivityChanged?.Invoke(InstallerActivity.Finalizing);
    }

    private void OnExecutePackageBegin(object? sender, ExecutePackageBeginEventArgs e)
    {
        var packageName = applicationData.Bundle.Packages.TryGetValue(e.PackageId, out var package)
            ? package.DisplayName
            : e.PackageId;
        PackageChanged?.Invoke(packageName ?? e.PackageId);
        e.Cancel = cancelRequested;
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        lastError = e.ErrorMessage;
        ErrorReported?.Invoke(e.ErrorMessage);
        if (cancelRequested)
        {
            e.Result = Result.Cancel;
        }
    }

    private void OnApplyComplete(object? sender, ApplyCompleteEventArgs e)
    {
        ExitCode = e.Status;
        var wasCancelled = cancelRequested || e.Status is ErrorInstallUserExit or HResultInstallUserExit;
        var succeeded = e.Status >= 0 && !wasCancelled;
        var engineRestartRequired = succeeded && e.Restart == ApplyRestart.RestartRequired;
        var restartInitiated = succeeded && e.Restart == ApplyRestart.RestartInitiated;
        var restartRequired = engineRestartRequired ||
            (succeeded && !restartInitiated && restartBehavior == Restart.Always);
        string? relaunchWarning = null;

        if (restartInitiated)
        {
            ExitCode = ErrorSuccessRebootInitiated;
        }
        else if (restartRequired)
        {
            ExitCode = ErrorSuccessRebootRequired;
        }

        var canInitiateSystemRestart = restartRequired && CanInitiateSystemRestart();
        restartCanBeInitiated = canInitiateSystemRestart &&
            restartBehavior == Restart.Prompt &&
            ShouldShowWindow;
        var restartAutomatically = canInitiateSystemRestart &&
            (restartBehavior == Restart.Always ||
             engineRestartRequired && restartBehavior == Restart.Automatic);
        if (restartAutomatically)
        {
            ExitCode = ErrorSuccessRebootInitiated;
            e.Action = BOOTSTRAPPER_APPLYCOMPLETE_ACTION.Restart;
        }

        if (succeeded &&
            !restartRequired &&
            !restartInitiated &&
            relaunchRequested &&
            PlannedAction != LaunchAction.Uninstall)
        {
            relaunchWarning = TryRelaunch();
        }

        ActivityChanged?.Invoke(InstallerActivity.Finalizing);

        ApplyCompleted?.Invoke(new ApplyOutcome(
            succeeded,
            wasCancelled,
            e.Status,
            lastError,
            GetLogPath(),
            relaunchWarning,
            restartRequired,
            restartCanBeInitiated));

        var waitForRestartChoice = restartRequired && restartCanBeInitiated;
        if (succeeded && (restartAutomatically || restartInitiated))
        {
            ExitRequested?.Invoke();
        }
        else if (succeeded && ShouldPlanAutomatically && !waitForRestartChoice && !ShouldShowWindow)
        {
            ExitRequested?.Invoke();
        }
        else if (!ShouldShowWindow)
        {
            ExitRequested?.Invoke();
        }
    }

    private void OnShutdown(object? sender, ShutdownEventArgs e)
    {
        if (restartApproved)
        {
            e.Action = BOOTSTRAPPER_SHUTDOWN_ACTION.Restart;
        }
    }

    private BundleScope ResolvePlanScope(FreshInstallScope freshInstallScope)
    {
        if (legacyMigrationRequired)
        {
            return BundleScope.PerUser;
        }

        var detectedBundleScope = TryGetDetectedBundleScope();
        if (detectedBundleScope.HasValue)
        {
            return detectedBundleScope.Value;
        }

        if (currentMsiScope.HasValue)
        {
            return currentMsiScope.Value;
        }

        if (relatedMsiScope.HasValue)
        {
            return relatedMsiScope.Value;
        }

        var existingMsiVersion = TryGetVariableString("ExistingMsiVersion");
        if (!string.IsNullOrWhiteSpace(existingMsiVersion) && existingMsiVersion != "0.0.0.0" &&
            TryGetNumericVariable("ExistingMsiAssignment", out var assignment))
        {
            return assignment == 1 ? BundleScope.PerMachine : BundleScope.PerUser;
        }

        return freshInstallScope switch
        {
            FreshInstallScope.PerUser => BundleScope.PerUser,
            FreshInstallScope.PerMachine => BundleScope.PerMachine,
            _ => BundleScope.PerUser,
        };
    }

    private bool TryConfigureDeleteUserSettings(LaunchAction action)
    {
        var value = action == LaunchAction.Uninstall && deleteUserSettings ? 1L : 0L;
        return SetNumericVariable("KMDeleteUserSettings", value) &&
            TryGetNumericVariable("KMDeleteUserSettings", out var verifiedValue) &&
            verifiedValue == value;
    }

    private bool TryConfigurePlanVariables(
        BundleScope scope,
        string? exactInstallFolder,
        bool loadShortcutPreferences)
    {
        var expectedScope = scope == BundleScope.PerMachine ? "perMachine" : "perUser";
        if (!SetStringVariable("KMInstallScope", expectedScope) ||
            !string.Equals(TryGetVariableString("KMInstallScope"), expectedScope, StringComparison.Ordinal))
        {
            return false;
        }

        if (!SynchronizeInstallPaths(scope, exactInstallFolder))
        {
            return false;
        }

        // Shortcut choices have no bearing on ownership proof or removal. A
        // malformed saved value must never strand an otherwise safe uninstall.
        if (loadShortcutPreferences && !EnsureStableShortcutPreferences(scope))
        {
            return false;
        }

        var migrationValue = legacyMigrationRequired ? 1L : 0L;
        if (!SetNumericVariable("KMMigrateLegacyNsis", migrationValue) ||
            !TryGetNumericVariable("KMMigrateLegacyNsis", out var verifiedMigrationValue) ||
            verifiedMigrationValue != migrationValue)
        {
            return false;
        }

        if (!legacyMigrationRequired)
        {
            return true;
        }

        var verifiedFolder = InstallPathPolicy.TryNormalizeLocalFolder(
            TryGetVariableString("KMEditorInstallFolder"),
            mustExist: true);
        return verifiedFolder is not null &&
            string.Equals(verifiedFolder, legacyNsisInstallFolder, StringComparison.OrdinalIgnoreCase);
    }

    private bool SynchronizeInstallPaths(BundleScope scope, string? exactInstallFolder = null)
    {
        var installFolder = exactInstallFolder ?? currentMsiInstallFolder ?? relatedMsiInstallFolder ?? legacyNsisInstallFolder;
        var executableVariable = scope == BundleScope.PerMachine
            ? "KMPerMachineExecutablePath"
            : "KMPerUserExecutablePath";

        if (string.IsNullOrWhiteSpace(installFolder))
        {
            var defaultExecutablePath = TryGetFormattedVariableString(executableVariable);
            installFolder = string.IsNullOrWhiteSpace(defaultExecutablePath)
                ? null
                : InstallPathPolicy.TryNormalizeLocalFolder(Path.GetDirectoryName(defaultExecutablePath), mustExist: false);
        }

        if (string.IsNullOrWhiteSpace(installFolder))
        {
            return false;
        }

        var normalizedFolder = InstallPathPolicy.TryNormalizeLocalFolder(installFolder, mustExist: false);
        if (normalizedFolder is null)
        {
            return false;
        }

        installFolder = normalizedFolder;
        var executablePath = Path.Combine(installFolder, InstalledExecutableName);
        if (!SetStringVariable("KMEditorInstallFolder", installFolder) ||
            !SetStringVariable(executableVariable, executablePath) ||
            !SetStringVariable("KMEditorExecutablePath", executablePath))
        {
            return false;
        }

        var verifiedFolder = InstallPathPolicy.TryNormalizeLocalFolder(
            TryGetVariableString("KMEditorInstallFolder"),
            mustExist: false);
        var verifiedScopeExecutable = TryGetVariableString(executableVariable);
        var verifiedEditorExecutable = TryGetVariableString("KMEditorExecutablePath");
        return verifiedFolder is not null &&
            string.Equals(verifiedFolder, installFolder, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(verifiedScopeExecutable, executablePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(verifiedEditorExecutable, executablePath, StringComparison.OrdinalIgnoreCase);
    }

    private bool EnsureStableShortcutPreferences(BundleScope scope)
    {
        if (shortcutPreferencesEdited ||
            (shortcutPreferencesLoaded && shortcutPreferenceScope == scope))
        {
            return true;
        }

        if (!TryGetNumericVariable("KMDefaultShortcutPreference", out var defaultShortcutPreference) ||
            defaultShortcutPreference is < 0 or > 1)
        {
            engine.Log(LogLevel.Error, "The authored default shortcut preference was not 0 or 1.");
            return false;
        }

        if (!SetNumericVariable("KMCreateStartMenuShortcut", defaultShortcutPreference) ||
            !SetNumericVariable("KMCreateDesktopShortcut", defaultShortcutPreference))
        {
            return false;
        }

        var scopePrefix = scope == BundleScope.PerMachine ? "KMPerMachine" : "KMPerUser";
        if (!TryApplyShortcutPreference(
                $"{scopePrefix}StartMenuShortcutPreference",
                "KMCreateStartMenuShortcut") ||
            !TryApplyShortcutPreference(
                $"{scopePrefix}DesktopShortcutPreference",
                "KMCreateDesktopShortcut"))
        {
            return false;
        }

        shortcutPreferencesLoaded = true;
        shortcutPreferenceScope = scope;
        return true;
    }

    private bool TryApplyShortcutPreference(string searchVariable, string targetVariable)
    {
        bool searchVariableExists;
        try
        {
            searchVariableExists = engine.ContainsVariable(searchVariable);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException)
        {
            engine.Log(LogLevel.Error, $"Could not inspect shortcut preference {searchVariable}: {exception.Message}");
            return false;
        }

        if (!searchVariableExists)
        {
            return true;
        }

        long value;
        if (!TryGetNumericVariable(searchVariable, out value))
        {
            var rawValue = TryGetVariableString(searchVariable);
            if (rawValue == string.Empty)
            {
                return true;
            }

            if (rawValue is null ||
                (rawValue != "0" && rawValue != "1") ||
                !long.TryParse(rawValue, out value))
            {
                engine.Log(LogLevel.Error, $"Shortcut preference {searchVariable} was not 0 or 1.");
                return false;
            }
        }

        if (value is < 0 or > 1 || !SetNumericVariable(targetVariable, value))
        {
            return false;
        }

        return TryGetNumericVariable(targetVariable, out var verifiedValue) && verifiedValue == value;
    }

    private bool TryCaptureExactCurrentMsi(out bool registered)
    {
        registered = false;
        var detectedBundleScope = TryGetDetectedBundleScope();
        var msiPackages = applicationData.Bundle.Packages.Values
            .Where(package => package.Type == PackageType.Msi && !string.IsNullOrWhiteSpace(package.ProductCode))
            .ToArray();
        if (msiPackages.Length != 1)
        {
            return false;
        }

        registered = MsiProductInfo.IsRegistered(msiPackages[0].ProductCode);
        if (!registered)
        {
            return false;
        }

        var registration = MsiProductInfo.TryGetRegistration(msiPackages[0].ProductCode);
        if (registration is null ||
            (installed && (!detectedBundleScope.HasValue || registration.Scope != detectedBundleScope.Value)) ||
            (currentMsiScope.HasValue &&
             (currentMsiScope.Value != registration.Scope ||
              !string.Equals(currentMsiInstallFolder, registration.InstallFolder, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        currentMsiScope = registration.Scope;
        currentMsiInstallFolder = registration.InstallFolder;
        return true;
    }

    private bool TryGetRegisteredBundleMsi(out BundleScope scope, out string installFolder)
    {
        scope = default;
        installFolder = string.Empty;
        if (!currentMsiScope.HasValue || string.IsNullOrWhiteSpace(currentMsiInstallFolder))
        {
            return false;
        }

        var detectedBundleScope = TryGetDetectedBundleScope();
        if (detectedBundleScope.HasValue && detectedBundleScope.Value != currentMsiScope.Value)
        {
            return false;
        }

        scope = currentMsiScope.Value;
        installFolder = currentMsiInstallFolder;
        return true;
    }

    private void SetUpdaterVariables()
    {
        if (invocationWasBridged)
        {
            return;
        }

        SetStringVariable("KMUpdateMode", isUpdate ? "1" : "0");
        SetStringVariable("KMAutoLaunch", relaunchRequested ? "1" : "0");
    }

    private bool TrySetRequestedPlanScope(BundleScope scope)
    {
        var value = scope == BundleScope.PerMachine ? 1L : 2L;
        if (!SetNumericVariable("KMRequestedPlanScope", value) ||
            !TryGetNumericVariable("KMRequestedPlanScope", out var verifiedValue) ||
            verifiedValue != value)
        {
            requestedPlanScope = null;
            return false;
        }

        requestedPlanScope = scope;
        return true;
    }

    private bool RequestedPlanScopeMatchesBurn()
    {
        if (!requestedPlanScope.HasValue)
        {
            return false;
        }

        var expectedValue = requestedPlanScope.Value == BundleScope.PerMachine ? 1L : 2L;
        return TryGetNumericVariable("KMRequestedPlanScope", out var requestedValue) &&
            requestedValue == expectedValue &&
            TryGetNumericVariable("WixBundlePlannedScope", out var plannedValue) &&
            plannedValue == expectedValue;
    }

    private void FailBeforePlan(string message)
    {
        ExitCode = ErrorInstallFailure;
        lastError = message;
        engine.Log(LogLevel.Error, message);
        ErrorReported?.Invoke(message);
        PhaseChanged?.Invoke(InstallerPhase.Failed);
        if (!ShouldShowWindow)
        {
            ExitRequested?.Invoke();
        }
    }

    private bool CanInitiateSystemRestart()
    {
        return ReadBooleanVariable("WixCanRestart") || ReadBooleanVariable("WixBundleElevated");
    }

    private string? TryRelaunch()
    {
        var executablePath = TryGetVariableString("KMEditorExecutablePath");
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            engine.Log(LogLevel.Error, "KM Editor update succeeded, but the relaunch executable was not found.");
            return "RelaunchWarning";
        }

        try
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            };

            foreach (var argument in relaunchArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            engine.Log(LogLevel.Error, $"KM Editor update succeeded, but relaunch failed: {exception.Message}");
            return "RelaunchWarning";
        }
    }

    private string? GetLogPath()
    {
        var variable = applicationData.Bundle.LogVariable;
        if (!string.IsNullOrWhiteSpace(variable))
        {
            var path = TryGetVariableString(variable);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return TryGetVariableString("WixBundleLog");
    }

    private bool ReadBooleanVariable(string name)
    {
        if (TryGetNumericVariable(name, out var numericValue))
        {
            return numericValue != 0;
        }

        var stringValue = TryGetVariableString(name);
        return stringValue is not null &&
            (stringValue == "1" || bool.TryParse(stringValue, out var booleanValue) && booleanValue);
    }

    private bool TryGetNumericVariable(string name, out long value)
    {
        value = 0;
        try
        {
            if (!engine.ContainsVariable(name))
            {
                return false;
            }

            value = engine.GetVariableNumeric(name);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private string? TryGetVariableString(string name)
    {
        try
        {
            return engine.ContainsVariable(name) ? engine.GetVariableString(name) : null;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private string? TryGetFormattedVariableString(string name)
    {
        try
        {
            return engine.ContainsVariable(name)
                ? engine.FormatString($"[{name}]")
                : null;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException)
        {
            engine.Log(LogLevel.Error, $"Could not format Burn variable {name}: {exception.Message}");
            return null;
        }
    }

    private bool SetStringVariable(string name, string value)
    {
        try
        {
            if (!engine.ContainsVariable(name))
            {
                return false;
            }

            engine.SetVariableString(name, value, false);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException)
        {
            engine.Log(LogLevel.Error, $"Could not set Burn variable {name}: {exception.Message}");
            return false;
        }
    }

    private bool SetNumericVariable(string name, long value)
    {
        try
        {
            if (!engine.ContainsVariable(name))
            {
                return false;
            }

            engine.SetVariableNumeric(name, value);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException)
        {
            engine.Log(LogLevel.Error, $"Could not set Burn variable {name}: {exception.Message}");
            return false;
        }
    }

    private BundleScope? TryGetDetectedBundleScope()
    {
        if (!TryGetNumericVariable("WixBundleDetectedScope", out var detectedScope))
        {
            return null;
        }

        return detectedScope switch
        {
            1 => BundleScope.PerMachine,
            2 => BundleScope.PerUser,
            _ => null,
        };
    }

    private bool RelatedMsiScopeConflictsWithRegisteredBundle()
    {
        var detectedBundleScope = TryGetDetectedBundleScope();
        return detectedBundleScope.HasValue &&
            relatedMsiScope.HasValue &&
            detectedBundleScope.Value != relatedMsiScope.Value;
    }

    private bool CanSafelyUnregisterSupersededBundle(
        bool capturedExactCurrentMsi,
        bool exactCurrentMsiRegistered)
    {
        if (command.Relation != RelationType.Upgrade ||
            command.Action != LaunchAction.Uninstall ||
            command.Display != Display.Embedded ||
            !installed ||
            capturedExactCurrentMsi ||
            exactCurrentMsiRegistered ||
            currentMsiScope.HasValue ||
            relatedMsiConflict ||
            relatedMsiProducts.Count != 1 ||
            !relatedMsiScope.HasValue ||
            string.IsNullOrWhiteSpace(relatedMsiInstallFolder) ||
            ReadBooleanVariable("KMLegacyNsisDetected") ||
            GetExistingMsiVersionState() != ExistingMsiVersionState.Newer)
        {
            return false;
        }

        var detectedBundleScope = TryGetDetectedBundleScope();
        if (!TryGetNumericVariable("ExistingMsiAssignment", out var existingMsiAssignment) ||
            existingMsiAssignment is < 0 or > 1)
        {
            return false;
        }

        var existingMsiScope = existingMsiAssignment == 1
            ? BundleScope.PerMachine
            : BundleScope.PerUser;
        var productInstallLocationVariable = relatedMsiScope == BundleScope.PerMachine
            ? "KMPerMachineProductInstallLocation"
            : "KMLegacyProductInstallLocation";
        var productInstallFolder = InstallPathPolicy.TryNormalizeLocalFolder(
            TryGetVariableString(productInstallLocationVariable),
            mustExist: true);
        return detectedBundleScope == relatedMsiScope &&
            existingMsiScope == relatedMsiScope &&
            productInstallFolder is not null &&
            string.Equals(
                productInstallFolder,
                relatedMsiInstallFolder,
                StringComparison.OrdinalIgnoreCase);
    }

    private LegacyNsisState DetectLegacyNsisState(out string? installFolder, out Version? version)
    {
        installFolder = null;
        version = null;
        var legacyProductPathValue = TryGetVariableString("KMLegacyProductInstallLocation");
        var legacyProductFolder = InstallPathPolicy.TryNormalizeLocalFolder(
            legacyProductPathValue,
            mustExist: false);
        var defaultPerUserExecutable = TryGetFormattedVariableString("KMPerUserExecutablePath");
        var defaultPerUserFolder = InstallPathPolicy.TryNormalizeLocalFolder(
            Path.GetDirectoryName(defaultPerUserExecutable),
            mustExist: false);

        if (!ReadBooleanVariable("KMLegacyNsisDetected"))
        {
            var invalidProductMarker = !string.IsNullOrWhiteSpace(legacyProductPathValue) &&
                legacyProductFolder is null;
            var unexpectedProductMarker = legacyProductFolder is not null &&
                !IsKnownMsiInstallFolder(legacyProductFolder);
            var productUninstallerExists = legacyProductFolder is not null &&
                File.Exists(Path.Combine(legacyProductFolder, LegacyUninstallerName));
            var defaultUninstallerExists = defaultPerUserFolder is not null &&
                File.Exists(Path.Combine(defaultPerUserFolder, LegacyUninstallerName));
            var defaultApplicationWithoutMsiOwner = defaultPerUserFolder is not null &&
                !IsKnownMsiInstallFolder(defaultPerUserFolder) &&
                File.Exists(Path.Combine(defaultPerUserFolder, InstalledExecutableName));

            if (invalidProductMarker ||
                unexpectedProductMarker ||
                productUninstallerExists ||
                defaultUninstallerExists ||
                defaultApplicationWithoutMsiOwner)
            {
                engine.Log(LogLevel.Error, "Partial legacy KM registration or exact legacy files remain without the published NSIS uninstall identity.");
                return LegacyNsisState.Ambiguous;
            }

            return LegacyNsisState.None;
        }

        if (!string.Equals(TryGetVariableString("KMLegacyNsisDisplayName"), LegacyDisplayName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(TryGetVariableString("KMLegacyNsisPublisher"), LegacyPublisher, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(TryGetVariableString("KMLegacyNsisMainBinaryName"), InstalledExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            engine.Log(LogLevel.Error, "The KM uninstall key exists, but it does not match the complete published NSIS identity.");
            return LegacyNsisState.Ambiguous;
        }

        if (!Version.TryParse(TryGetVariableString("KMLegacyNsisVersion"), out version))
        {
            engine.Log(LogLevel.Error, "The legacy KM identity was present, but its registered version was invalid.");
            return LegacyNsisState.Ambiguous;
        }

        var uninstallInstallFolder = InstallPathPolicy.TryNormalizeLocalFolder(
            TryGetVariableString("KMLegacyNsisInstallLocation"),
            mustExist: false);
        var productInstallFolder = legacyProductFolder;
        var uninstallExecutable = ExtractSingleExecutablePath(
            TryGetVariableString("KMLegacyNsisUninstallString"));
        var uninstallExecutableFolder = InstallPathPolicy.TryNormalizeLocalFolder(
            Path.GetDirectoryName(uninstallExecutable),
            mustExist: false);

        if (uninstallInstallFolder is null ||
            productInstallFolder is null ||
            uninstallExecutableFolder is null ||
            !string.Equals(uninstallInstallFolder, productInstallFolder, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uninstallInstallFolder, uninstallExecutableFolder, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(uninstallExecutable), LegacyUninstallerName, StringComparison.OrdinalIgnoreCase))
        {
            engine.Log(LogLevel.Error, "The legacy KM identity was present, but its registered paths were incomplete or unsafe.");
            return LegacyNsisState.Ambiguous;
        }

        var expectedUninstaller = Path.Combine(uninstallInstallFolder, LegacyUninstallerName);
        var expectedApplication = Path.Combine(uninstallInstallFolder, InstalledExecutableName);
        if (!File.Exists(expectedUninstaller) && !File.Exists(expectedApplication))
        {
            engine.Log(LogLevel.Error, "The legacy KM registration exists, but no installer-owned binaries remain to prove its state.");
            return LegacyNsisState.Ambiguous;
        }

        installFolder = uninstallInstallFolder;
        return LegacyNsisState.Confirmed;
    }

    private bool HasExistingMsiRegistration()
    {
        var existingMsiVersion = TryGetVariableString("ExistingMsiVersion");
        return !string.IsNullOrWhiteSpace(existingMsiVersion) && existingMsiVersion != "0.0.0.0";
    }

    private ExistingMsiVersionState GetExistingMsiVersionState()
    {
        var existingVersionValue = TryGetVariableString("ExistingMsiVersion");
        if (string.IsNullOrWhiteSpace(existingVersionValue) || existingVersionValue == "0.0.0.0")
        {
            return ExistingMsiVersionState.None;
        }

        if (!Version.TryParse(existingVersionValue, out var existingVersion) ||
            !Version.TryParse(BundleVersion, out var bundleVersion))
        {
            return ExistingMsiVersionState.Invalid;
        }

        return existingVersion > bundleVersion
            ? ExistingMsiVersionState.Newer
            : ExistingMsiVersionState.SameOrOlder;
    }

    private bool IsKnownMsiInstallFolder(string folder)
    {
        if (string.Equals(currentMsiInstallFolder, folder, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return relatedMsiProducts.Values.Any(detection =>
            string.Equals(detection.InstallFolder, folder, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsLegacyVersionNewer(Version? legacyVersion)
    {
        return legacyVersion is null ||
            !Version.TryParse(BundleVersion, out var bundleVersion) ||
            legacyVersion > bundleVersion;
    }

    private static string? ExtractSingleExecutablePath(string? commandLine)
    {
        commandLine = commandLine?.Trim();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        if (commandLine[0] != '"')
        {
            return commandLine;
        }

        var closingQuote = commandLine.IndexOf('"', 1);
        return closingQuote > 1 && string.IsNullOrWhiteSpace(commandLine[(closingQuote + 1)..])
            ? commandLine[1..closingQuote]
            : null;
    }

    private sealed record RelatedMsiDetection(BundleScope Scope, string? InstallFolder);

    private enum LegacyNsisState
    {
        None,
        Confirmed,
        Ambiguous,
    }

    private enum ExistingMsiVersionState
    {
        None,
        SameOrOlder,
        Newer,
        Invalid,
    }
}
