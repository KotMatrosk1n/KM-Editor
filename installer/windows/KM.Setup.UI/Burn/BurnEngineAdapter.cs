// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using KM.Setup.UI.Invocation;
using WixToolset.BootstrapperApplicationApi;
using Directory = System.IO.Directory;
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
    Finalizing,
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
    private const int CancellationAllowed = 0;
    private const int CancellationRequested = 1;
    private const int CancellationClosed = 2;
    private const int ErrorInstallUserExit = 1602;
    private const int ErrorInstallFailure = 1603;
    private const int ErrorSuccessRebootInitiated = 1641;
    private const int ErrorSuccessRebootRequired = 3010;
    private const int HResultInstallUserExit = unchecked((int)0x80070642);
    private const string KmMsiPackageId = "PkgKmEditorMsi";
    private const string InstalledExecutableName = "km-editor-desktop.exe";
    private const string InstalledBridgeName = "km-tools-bridge.exe";
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
    private readonly Dictionary<string, RelatedBundleDetection> relatedBundleOwners =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> seenPerUserRelatedBundleIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<bool>> relatedBundleDuplicateOccurrences =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> relatedBundlePlanOccurrenceIndices =
        new(StringComparer.OrdinalIgnoreCase);

    private int cancellationState;
    private bool installed;
    private bool legacyMigrationRequired;
    private bool recoveryRequired;
    private bool forceReinstallMsi;
    private bool missingMsiRecovery;
    private bool currentBundleMsiScopeMismatch;
    private bool msiDetectionMetadataMismatch;
    private bool relatedMsiConflict;
    private bool relaunchAttempted;
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
    private BundleScope? missingMsiRecoveryScope;
    private BundleScope? requestedPlanScope;
    private BundleScope? shortcutPreferenceScope;
    private BundleScope? relatedMsiScope;
    private BundleScope? supersededUnregisterScope;
    private string? currentMsiInstallFolder;
    private string? missingMsiRecoveryInstallFolder;
    private ProductMarkerState missingMsiRecoveryMarkerState;
    private Version? currentMsiVersion;
    private Version? missingMsiRecoveryVersion;
    private string? legacyNsisInstallFolder;
    private string? relatedMsiInstallFolder;
    private Version? relatedMsiVersion;
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
        bootstrapper.PlanPackageBegin += OnPlanPackageBegin;
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

    public bool IsUpdate =>
        isUpdate ||
        legacyMigrationRequired ||
        recoveryRequired ||
        relatedMsiProducts.Count != 0;

    public bool IsLegacyMigration => legacyMigrationRequired;

    public bool IsRecovery => recoveryRequired;

    public bool IsInteractive =>
        command.Display == Display.Full && invocation.DisplayMode == InvocationDisplayMode.EngineDefault && !isUpdate;

    public bool ShouldShowWindow =>
        invocation.DisplayMode != InvocationDisplayMode.Quiet &&
        command.Display is not Display.None and not Display.Embedded;

    public bool ShouldPlanAutomatically => !IsInteractive;

    public bool UsesVisibleTiming => ShouldShowWindow;

    public bool CanRequestCancel => Volatile.Read(ref cancellationState) != CancellationClosed;

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
        ResetCancellation();
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

        BundleScope? recoveryScope = null;
        string? recoveryInstallFolder = null;
        if (forceReinstallMsi && action == LaunchAction.Install)
        {
            if (!TryRevalidateRecoveryMsi(out var verifiedScope, out var verifiedInstallFolder))
            {
                FailBeforePlan("The recoverable KM installation changed after detection. No changes were planned.");
                return;
            }

            recoveryScope = verifiedScope;
            recoveryInstallFolder = verifiedInstallFolder;
        }

        ResetCancellation();
        lastError = null;
        ProgressChanged?.Invoke(0);
        PackageChanged?.Invoke(string.Empty);
        PlannedAction = action;
        plannedFreshInstallScope = freshInstallScope;
        var scope = supersededUnregisterScope ??
            recoveryScope ??
            (safeBlockedUninstall ? registeredMsiScope : ResolvePlanScope(freshInstallScope));
        if (!unregisterSupersededBundle &&
            !TryConfigurePlanVariables(
                scope,
                recoveryInstallFolder ?? (safeBlockedUninstall ? registeredMsiInstallFolder : null),
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
        if (Interlocked.CompareExchange(
                ref cancellationState,
                CancellationRequested,
                CancellationAllowed) != CancellationAllowed)
        {
            return;
        }

        ActivityChanged?.Invoke(InstallerActivity.Cancelling);
        PhaseChanged?.Invoke(InstallerPhase.Cancelling);
    }

    private bool IsCancellationRequested()
    {
        return Volatile.Read(ref cancellationState) == CancellationRequested;
    }

    private void ResetCancellation()
    {
        Interlocked.Exchange(ref cancellationState, CancellationAllowed);
    }

    private void CloseCancellation()
    {
        Interlocked.Exchange(ref cancellationState, CancellationClosed);
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
        ResetCancellation();
        lastError = null;
        ProgressChanged?.Invoke(0);
        PackageChanged?.Invoke(string.Empty);
        Detect();
    }

    public void ExitWithoutApply()
    {
        ExitCode = ErrorInstallUserExit;
        TryRelaunchAtTerminal();
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
        recoveryRequired = false;
        forceReinstallMsi = false;
        missingMsiRecovery = false;
        currentBundleMsiScopeMismatch = false;
        msiDetectionMetadataMismatch = false;
        legacyNsisInstallFolder = null;
        relatedMsiConflict = false;
        restartApproved = false;
        restartCanBeInitiated = false;
        relatedMsiProducts.Clear();
        relatedBundleOwners.Clear();
        seenPerUserRelatedBundleIds.Clear();
        relatedBundleDuplicateOccurrences.Clear();
        relatedBundlePlanOccurrenceIndices.Clear();
        currentMsiScope = null;
        missingMsiRecoveryScope = null;
        requestedPlanScope = null;
        relatedMsiScope = null;
        supersededUnregisterScope = null;
        currentMsiInstallFolder = null;
        missingMsiRecoveryInstallFolder = null;
        missingMsiRecoveryMarkerState = ProductMarkerState.Absent;
        currentMsiVersion = null;
        missingMsiRecoveryVersion = null;
        relatedMsiInstallFolder = null;
        relatedMsiVersion = null;
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
            return;
        }

        var scope = e.PerMachine ? BundleScope.PerMachine : BundleScope.PerUser;
        var key = $"{e.ProductCode}|{scope}";
        var version = TryNormalizeVersion(e.Version);
        if (relatedBundleOwners.TryGetValue(key, out var existingOwner))
        {
            relatedBundleOwners[key] = new RelatedBundleDetection(
                scope,
                existingOwner.RelationType == e.RelationType
                    ? e.RelationType
                    : RelationType.None,
                existingOwner.Version == version ? version : null,
                existingOwner.MissingFromCache && e.MissingFromCache);
        }
        else
        {
            relatedBundleOwners[key] = new RelatedBundleDetection(
                scope,
                e.RelationType,
                version,
                e.MissingFromCache);
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
        var eventScope = e.PerMachine ? BundleScope.PerMachine : BundleScope.PerUser;
        var eventVersion = TryNormalizeVersion(e.Version);
        var registration = MsiProductInfo.TryGetRegistration(e.ProductCode);
        if (registration is null)
        {
            relatedMsiConflict = true;
            engine.Log(
                LogLevel.Error,
                $"Windows Installer could not prove the scope, version, and install path for KM MSI {e.ProductCode}.");
            return;
        }

        if (registration.Scope != eventScope ||
            eventVersion is null ||
            registration.Version != eventVersion)
        {
            if (GetProductMarkerState(
                    registration.Scope,
                    registration.InstallFolder,
                    registration.Version) != ProductMarkerState.Matching)
            {
                relatedMsiConflict = true;
                engine.Log(
                    LogLevel.Error,
                    $"Burn and Windows Installer disagreed about KM MSI {e.ProductCode}, and no matching scope-correct KM product marker proved the registered location.");
                return;
            }

            msiDetectionMetadataMismatch = true;
            engine.Log(
                LogLevel.Standard,
                $"Burn reported stale metadata for KM MSI {e.ProductCode}; Windows Installer registration and the matching KM product marker will be used for recovery.");
        }

        // A complete Windows Installer registration remains a safe recovery
        // source even when the application folder was partially or completely
        // removed. The new package recreates that exact normalized location.
        var detection = new RelatedMsiDetection(
            registration.Scope,
            registration.InstallFolder,
            registration.Version);

        if (isCurrentProduct)
        {
            if (currentMsiScope.HasValue &&
                (currentMsiScope.Value != detection.Scope ||
                 !string.Equals(currentMsiInstallFolder, detection.InstallFolder, StringComparison.OrdinalIgnoreCase) ||
                 currentMsiVersion != detection.Version))
            {
                relatedMsiConflict = true;
                currentMsiScope = null;
                currentMsiInstallFolder = null;
                currentMsiVersion = null;
            }
            else
            {
                currentMsiScope = detection.Scope;
                currentMsiInstallFolder = detection.InstallFolder;
                currentMsiVersion = detection.Version;
            }
        }
        else
        {
            if (relatedMsiProducts.TryGetValue(e.ProductCode, out var existing) &&
                (existing.Scope != detection.Scope ||
                 !string.Equals(existing.InstallFolder, detection.InstallFolder, StringComparison.OrdinalIgnoreCase) ||
                 existing.Version != detection.Version))
            {
                relatedMsiConflict = true;
            }
            else
            {
                relatedMsiProducts[e.ProductCode] = detection;
            }

            relatedMsiConflict |= relatedMsiProducts.Count > 1;
            if (relatedMsiConflict)
            {
                relatedMsiScope = null;
                relatedMsiInstallFolder = null;
                relatedMsiVersion = null;
            }
            else
            {
                relatedMsiScope = detection.Scope;
                relatedMsiInstallFolder = detection.InstallFolder;
                relatedMsiVersion = detection.Version;
            }
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
            TryRelaunchAtTerminal();
            DetectionCompleted?.Invoke(new DetectionOutcome(installed, e.Status));
            return;
        }

        if (IsCancellationRequested())
        {
            retryActionAfterDetection = LaunchAction.Unknown;
            retryFreshInstallScopeAfterDetection = FreshInstallScope.Default;
            ExitCode = ErrorInstallUserExit;
            PhaseChanged?.Invoke(InstallerPhase.Cancelled);
            TryRelaunchAtTerminal();
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
            TryRelaunchAtTerminal();
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
            var recoverableFullBundleMsi = CanRecoverFullBundleFromRelatedMsi(
                capturedExactCurrentMsi,
                exactCurrentMsiRegistered);
            var recoverableMissingMsi = TryCaptureMissingMsiRecovery(
                capturedExactCurrentMsi,
                exactCurrentMsiRegistered);
            if ((installed || exactCurrentMsiRegistered || currentMsiScope.HasValue) &&
                !capturedExactCurrentMsi &&
                !recoverableFullBundleMsi &&
                !recoverableMissingMsi)
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
                RelatedMsiScopeConflictsWithRegisteredBundle() &&
                    !recoverableFullBundleMsi &&
                    !recoverableMissingMsi ||
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
            !legacyMigrationRequired)
        {
            ConfigureRecoveryTakeover(capturedExactCurrentMsi);
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
            TryRelaunchAtTerminal();
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

    private void OnPlanPackageBegin(object? sender, PlanPackageBeginEventArgs e)
    {
        if (!forceReinstallMsi ||
            PlannedAction != LaunchAction.Install ||
            !string.Equals(e.PackageId, KmMsiPackageId, StringComparison.Ordinal) ||
            !applicationData.Bundle.Packages.TryGetValue(e.PackageId, out var package) ||
            package.Type != PackageType.Msi)
        {
            return;
        }

        // Recovery always lays down the complete package from this setup. This
        // repairs an exact orphan product and guarantees that a related older
        // product is replaced by the newest package before Burn becomes owner.
        e.State = RequestState.ForcePresent;
        engine.Log(
            LogLevel.Standard,
            $"Forced KM recovery package {e.PackageId} present from the current setup payload.");
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
            var relaunchWarning = TryRelaunchAtTerminal();
            ApplyCompleted?.Invoke(new ApplyOutcome(
                false,
                false,
                e.Status,
                lastError,
                GetLogPath(),
                relaunchWarning,
                RestartRequired: false,
                CanRestartNow: false));
            if (!ShouldShowWindow)
            {
                ExitRequested?.Invoke();
            }
            return;
        }

        if (IsCancellationRequested())
        {
            ExitCode = ErrorInstallUserExit;
            PhaseChanged?.Invoke(InstallerPhase.Cancelled);
            var relaunchWarning = TryRelaunchAtTerminal();
            ApplyCompleted?.Invoke(new ApplyOutcome(
                false,
                true,
                ErrorInstallUserExit,
                null,
                GetLogPath(),
                relaunchWarning,
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
        e.Cancel = IsCancellationRequested();
    }

    private void OnProgress(object? sender, ProgressEventArgs e)
    {
        ProgressChanged?.Invoke(Math.Clamp(e.OverallPercentage, 0, 100));
        e.Cancel = IsCancellationRequested();
    }

    private void OnCacheAcquireProgress(object? sender, CacheAcquireProgressEventArgs e)
    {
        e.Cancel = IsCancellationRequested();
    }

    private void OnCacheContainerOrPayloadVerifyProgress(object? sender, CacheContainerOrPayloadVerifyProgressEventArgs e)
    {
        e.Cancel = IsCancellationRequested();
    }

    private void OnCachePayloadExtractProgress(object? sender, CachePayloadExtractProgressEventArgs e)
    {
        e.Cancel = IsCancellationRequested();
    }

    private void OnCacheVerifyProgress(object? sender, CacheVerifyProgressEventArgs e)
    {
        e.Cancel = IsCancellationRequested();
    }

    private void OnExecuteProgress(object? sender, ExecuteProgressEventArgs e)
    {
        e.Cancel = IsCancellationRequested();
    }

    private void OnExecuteBegin(object? sender, ExecuteBeginEventArgs e)
    {
        ActivityChanged?.Invoke(InstallerActivity.Executing);
        e.Cancel = IsCancellationRequested();
    }

    private void OnExecuteComplete(object? sender, ExecuteCompleteEventArgs e)
    {
        CloseCancellation();
        ActivityChanged?.Invoke(InstallerActivity.Finalizing);
        PhaseChanged?.Invoke(InstallerPhase.Finalizing);
    }

    private void OnExecutePackageBegin(object? sender, ExecutePackageBeginEventArgs e)
    {
        var packageName = applicationData.Bundle.Packages.TryGetValue(e.PackageId, out var package)
            ? package.DisplayName
            : e.PackageId;
        PackageChanged?.Invoke(packageName ?? e.PackageId);
        e.Cancel = IsCancellationRequested();
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        lastError = e.ErrorMessage;
        ErrorReported?.Invoke(e.ErrorMessage);
        if (IsCancellationRequested())
        {
            e.Result = Result.Cancel;
        }
    }

    private void OnApplyComplete(object? sender, ApplyCompleteEventArgs e)
    {
        CloseCancellation();
        ExitCode = e.Status;
        // A cancel request is not a terminal result. Burn can finish successfully
        // after the final cancellable boundary, so only the engine's explicit
        // user-exit statuses classify the transaction as cancelled.
        var wasCancelled = e.Status is ErrorInstallUserExit or HResultInstallUserExit;
        var succeeded = e.Status >= 0 && !wasCancelled;
        var applyReportedRestartRequired = e.Restart == ApplyRestart.RestartRequired;
        var applyReportedRestartInitiated = e.Restart == ApplyRestart.RestartInitiated;
        var engineRestartRequired = succeeded && applyReportedRestartRequired;
        var restartInitiated = succeeded && applyReportedRestartInitiated;
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

        if (!applyReportedRestartRequired && !applyReportedRestartInitiated)
        {
            relaunchWarning = TryRelaunchAtTerminal();
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

        if (recoveryRequired && currentMsiScope.HasValue)
        {
            return currentMsiScope.Value;
        }

        if (recoveryRequired && missingMsiRecoveryScope.HasValue)
        {
            return missingMsiRecoveryScope.Value;
        }

        if (recoveryRequired && relatedMsiScope.HasValue)
        {
            return relatedMsiScope.Value;
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
        var bundleVersion = TryNormalizeVersion(BundleVersion);
        var bundleScopeMismatch = installed &&
            (!detectedBundleScope.HasValue || registration is not null && registration.Scope != detectedBundleScope.Value);
        if (registration is null ||
            bundleVersion is null ||
            registration.Version != bundleVersion ||
            (bundleScopeMismatch &&
             GetProductMarkerState(
                 registration.Scope,
                 registration.InstallFolder,
                 registration.Version) != ProductMarkerState.Matching) ||
            (currentMsiScope.HasValue &&
             (currentMsiScope.Value != registration.Scope ||
              !string.Equals(currentMsiInstallFolder, registration.InstallFolder, StringComparison.OrdinalIgnoreCase) ||
              currentMsiVersion != registration.Version)))
        {
            return false;
        }

        currentMsiScope = registration.Scope;
        currentMsiInstallFolder = registration.InstallFolder;
        currentMsiVersion = registration.Version;
        currentBundleMsiScopeMismatch = bundleScopeMismatch;
        return true;
    }

    private bool TryCaptureMissingMsiRecovery(
        bool capturedExactCurrentMsi,
        bool exactCurrentMsiRegistered)
    {
        if (!installed ||
            capturedExactCurrentMsi ||
            exactCurrentMsiRegistered ||
            currentMsiScope.HasValue ||
            relatedMsiProducts.Count != 0 ||
            relatedMsiConflict ||
            HasExistingMsiRegistration())
        {
            return false;
        }

        var detectedBundleScope = TryGetDetectedBundleScope();
        var bundleVersion = TryNormalizeVersion(BundleVersion);
        if (!detectedBundleScope.HasValue ||
            bundleVersion is null ||
            !applicationData.Bundle.Packages.TryGetValue(KmMsiPackageId, out var package) ||
            package.Type != PackageType.Msi ||
            string.IsNullOrWhiteSpace(package.ProductCode) ||
            string.IsNullOrWhiteSpace(package.UpgradeCode))
        {
            relatedMsiConflict = true;
            engine.Log(
                LogLevel.Error,
                "The Full bundle registration did not provide a complete authored MSI identity and scope for recovery.");
            return false;
        }

        var relatedProductCodes = MsiProductInfo.TryGetRelatedProductCodes(package.UpgradeCode);
        if (relatedProductCodes is null || relatedProductCodes.Count != 0)
        {
            relatedMsiConflict = true;
            engine.Log(
                LogLevel.Error,
                "The Full bundle has no detected MSI, but Windows Installer could not prove that the authored upgrade family is empty.");
            return false;
        }

        if (!TryResolveMissingMsiRecoveryEvidence(
                detectedBundleScope.Value,
                bundleVersion,
                out var installFolder,
                out var markerState))
        {
            relatedMsiConflict = true;
            engine.Log(
                LogLevel.Error,
                "The Full bundle has no MSI, and its scope-correct KM product marker conflicts with the authored recovery path.");
            return false;
        }

        missingMsiRecovery = true;
        missingMsiRecoveryScope = detectedBundleScope.Value;
        missingMsiRecoveryInstallFolder = installFolder;
        missingMsiRecoveryMarkerState = markerState;
        missingMsiRecoveryVersion = bundleVersion;
        engine.Log(
            LogLevel.Standard,
            markerState == ProductMarkerState.Matching
                ? "The Full bundle has no MSI; setup will reinstall the current package at the matching KM product marker path."
                : "The Full bundle has no MSI or KM product marker; setup will reinstall the current package at the authored scope default.");
        return true;
    }

    private bool TryResolveMissingMsiRecoveryEvidence(
        BundleScope scope,
        Version version,
        out string installFolder,
        out ProductMarkerState markerState)
    {
        installFolder = string.Empty;
        markerState = ProductMarkerState.Mismatch;
        var prefix = scope == BundleScope.PerMachine
            ? "KMPerMachineProduct"
            : "KMPerUserProduct";
        var markerLocationValue = TryGetVariableString($"{prefix}InstallLocation");
        var markerValues = new[]
        {
            markerLocationValue,
            TryGetVariableString($"{prefix}InstallDir"),
            TryGetVariableString($"{prefix}InstallScope"),
            TryGetVariableString($"{prefix}InstallerFamily"),
            TryGetVariableString($"{prefix}MainBinaryName"),
            TryGetVariableString($"{prefix}Version"),
        };

        if (markerValues.All(string.IsNullOrWhiteSpace))
        {
            var executableVariable = scope == BundleScope.PerMachine
                ? "KMPerMachineExecutablePath"
                : "KMPerUserExecutablePath";
            var defaultExecutablePath = TryGetFormattedVariableString(executableVariable);
            var defaultInstallFolder = InstallPathPolicy.TryNormalizeLocalFolder(
                Path.GetDirectoryName(defaultExecutablePath),
                mustExist: false);
            if (defaultInstallFolder is null)
            {
                return false;
            }

            installFolder = defaultInstallFolder;
            markerState = ProductMarkerState.Absent;
            return true;
        }

        var markerInstallFolder = InstallPathPolicy.TryNormalizeLocalFolder(
            markerLocationValue,
            mustExist: false);
        if (markerInstallFolder is null ||
            GetProductMarkerState(scope, markerInstallFolder, version) != ProductMarkerState.Matching)
        {
            return false;
        }

        installFolder = markerInstallFolder;
        markerState = ProductMarkerState.Matching;
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

        if (!installed ||
            currentMsiVersion is null ||
            GetProductMarkerState(
                currentMsiScope.Value,
                currentMsiInstallFolder,
                currentMsiVersion) == ProductMarkerState.Mismatch)
        {
            return false;
        }

        var detectedBundleScope = TryGetDetectedBundleScope();
        if (!detectedBundleScope.HasValue || detectedBundleScope.Value != currentMsiScope.Value)
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
        TryRelaunchAtTerminal();
        if (!ShouldShowWindow)
        {
            ExitRequested?.Invoke();
        }
    }

    private bool CanInitiateSystemRestart()
    {
        return ReadBooleanVariable("WixCanRestart") || ReadBooleanVariable("WixBundleElevated");
    }

    private string? TryRelaunchAtTerminal()
    {
        var terminalAction = PlannedAction == LaunchAction.Unknown
            ? RequestedAction
            : PlannedAction;
        if (!relaunchRequested ||
            relaunchAttempted ||
            terminalAction == LaunchAction.Uninstall)
        {
            return null;
        }

        relaunchAttempted = true;
        var executablePath = TryResolveRelaunchExecutable();
        if (executablePath is null)
        {
            engine.Log(LogLevel.Error, "KM Editor setup reached a terminal state, but the relaunch executable was not found.");
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
            engine.Log(LogLevel.Error, $"KM Editor setup reached a terminal state, but relaunch failed: {exception.Message}");
            return "RelaunchWarning";
        }
    }

    private string? TryResolveRelaunchExecutable()
    {
        if (!string.IsNullOrWhiteSpace(currentMsiInstallFolder))
        {
            return TryResolveInstalledExecutable(currentMsiInstallFolder);
        }

        if (!string.IsNullOrWhiteSpace(missingMsiRecoveryInstallFolder))
        {
            return TryResolveInstalledExecutable(missingMsiRecoveryInstallFolder);
        }

        if (relatedMsiProducts.Count == 1 && !string.IsNullOrWhiteSpace(relatedMsiInstallFolder))
        {
            return TryResolveInstalledExecutable(relatedMsiInstallFolder);
        }

        if (!string.IsNullOrWhiteSpace(legacyNsisInstallFolder))
        {
            return TryResolveInstalledExecutable(legacyNsisInstallFolder);
        }

        var authoritativeScope = requestedPlanScope ?? TryGetDetectedBundleScope();
        if (!authoritativeScope.HasValue)
        {
            return null;
        }

        if (requestedPlanScope.HasValue)
        {
            var requestedExecutable = TryResolveInstalledExecutablePath(
                TryGetVariableString("KMEditorExecutablePath"));
            if (requestedExecutable is not null)
            {
                return requestedExecutable;
            }
        }

        var scopedVariable = authoritativeScope.Value == BundleScope.PerMachine
            ? "KMPerMachineExecutablePath"
            : "KMPerUserExecutablePath";
        return TryResolveInstalledExecutablePath(TryGetFormattedVariableString(scopedVariable));
    }

    private static string? TryResolveInstalledExecutable(string? folder)
    {
        var normalizedFolder = InstallPathPolicy.TryNormalizeLocalFolder(folder, mustExist: true);
        if (normalizedFolder is null)
        {
            return null;
        }

        var executablePath = Path.Combine(normalizedFolder, InstalledExecutableName);
        return File.Exists(executablePath) ? executablePath : null;
    }

    private static string? TryResolveInstalledExecutablePath(string? path)
    {
        if (!string.Equals(Path.GetFileName(path), InstalledExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return TryResolveInstalledExecutable(Path.GetDirectoryName(path));
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

    private void ConfigureRecoveryTakeover(bool capturedExactCurrentMsi)
    {
        if (installed &&
            missingMsiRecovery &&
            missingMsiRecoveryScope.HasValue &&
            !string.IsNullOrWhiteSpace(missingMsiRecoveryInstallFolder) &&
            missingMsiRecoveryVersion is not null)
        {
            forceReinstallMsi = true;
            recoveryRequired = true;
            engine.Log(
                LogLevel.Standard,
                "The Full bundle registration is missing its MSI; setup will force the current package present in the verified bundle scope.");
            return;
        }

        if (GetExistingMsiVersionState() != ExistingMsiVersionState.SameOrOlder)
        {
            return;
        }

        if (installed)
        {
            if (capturedExactCurrentMsi &&
                currentMsiScope.HasValue &&
                !string.IsNullOrWhiteSpace(currentMsiInstallFolder) &&
                currentMsiVersion is not null)
            {
                var markerState = GetProductMarkerState(
                    currentMsiScope.Value,
                    currentMsiInstallFolder,
                    currentMsiVersion);
                if (markerState == ProductMarkerState.Mismatch ||
                    currentBundleMsiScopeMismatch && markerState != ProductMarkerState.Matching)
                {
                    blockingResourceKey = "ExistingInstallConflictDescription";
                    engine.Log(
                        LogLevel.Error,
                        currentBundleMsiScopeMismatch
                            ? "The Full bundle and exact current KM MSI have different scopes without a matching scope-correct KM product marker."
                            : "The exact current KM MSI has a conflicting product marker.");
                    return;
                }

                var applicationMissing =
                    !File.Exists(Path.Combine(currentMsiInstallFolder, InstalledExecutableName)) ||
                    !File.Exists(Path.Combine(currentMsiInstallFolder, InstalledBridgeName));
                if (currentBundleMsiScopeMismatch ||
                    msiDetectionMetadataMismatch ||
                    markerState == ProductMarkerState.Absent ||
                    applicationMissing)
                {
                    forceReinstallMsi = true;
                    recoveryRequired = true;
                    engine.Log(
                        LogLevel.Standard,
                        "The Full bundle and exact KM MSI need ownership or file recovery; setup will force the current package present in the proven MSI scope.");
                }
            }
            else if (!capturedExactCurrentMsi &&
                relatedMsiProducts.Count == 1 &&
                relatedMsiScope.HasValue &&
                !string.IsNullOrWhiteSpace(relatedMsiInstallFolder) &&
                relatedMsiVersion is not null &&
                FullRelatedMarkerAllowsRecovery())
            {
                forceReinstallMsi = true;
                recoveryRequired = true;
                engine.Log(
                    LogLevel.Standard,
                    "The Full bundle registration has one proven related KM MSI; setup will reinstall in the MSI scope and replace stale bundle ownership.");
            }

            return;
        }

        if (capturedExactCurrentMsi)
        {
            if (!currentMsiScope.HasValue ||
                string.IsNullOrWhiteSpace(currentMsiInstallFolder) ||
                currentMsiVersion is null ||
                GetProductMarkerState(
                    currentMsiScope.Value,
                    currentMsiInstallFolder,
                    currentMsiVersion) == ProductMarkerState.Mismatch)
            {
                blockingResourceKey = "ExistingInstallConflictDescription";
                engine.Log(
                    LogLevel.Error,
                    "The exact orphan KM MSI did not have a matching scope-correct KM product marker.");
                return;
            }

            // The MSI is exact, but this bundle has no Full registration. A
            // forced install repairs the product and makes Burn the sole public
            // maintenance owner without invoking an untrusted uninstall path.
            forceReinstallMsi = true;
            recoveryRequired = true;
            engine.Log(
                LogLevel.Standard,
                "The exact KM MSI is registered without this bundle owner; setup will repair and adopt it.");
            return;
        }

        if (relatedMsiProducts.Count != 1 ||
            !relatedMsiScope.HasValue ||
            string.IsNullOrWhiteSpace(relatedMsiInstallFolder) ||
            relatedMsiVersion is null)
        {
            return;
        }

        if (GetProductMarkerState(
                relatedMsiScope.Value,
                relatedMsiInstallFolder,
                relatedMsiVersion) == ProductMarkerState.Mismatch)
        {
            blockingResourceKey = "ExistingInstallConflictDescription";
            engine.Log(
                LogLevel.Error,
                "The related KM MSI did not have a matching scope-correct KM product marker.");
            return;
        }

        // A normal major upgrade already replaces the related MSI. Force the
        // current package present as well so a partial source folder cannot turn
        // that replacement into a no-op.
        forceReinstallMsi = true;

        var matchingOwners = relatedBundleOwners.Values.Count(owner =>
            owner.RelationType == RelationType.Upgrade &&
            owner.Scope == relatedMsiScope.Value &&
            owner.Version == relatedMsiVersion &&
            !owner.MissingFromCache);
        var completeApplication = Directory.Exists(relatedMsiInstallFolder) &&
            File.Exists(Path.Combine(relatedMsiInstallFolder, InstalledExecutableName));
        recoveryRequired = matchingOwners != 1 ||
            relatedBundleOwners.Count != 1 ||
            !completeApplication ||
            msiDetectionMetadataMismatch;

        if (recoveryRequired)
        {
            engine.Log(
                LogLevel.Standard,
                "A single same-or-older KM MSI is recoverable, but its bundle ownership or application files are incomplete; setup will reinstall and adopt it.");
        }
    }

    private ProductMarkerState GetProductMarkerState(
        BundleScope scope,
        string installFolder,
        Version version)
    {
        var prefix = scope == BundleScope.PerMachine
            ? "KMPerMachineProduct"
            : "KMPerUserProduct";
        var locationValue = TryGetVariableString($"{prefix}InstallLocation");
        var installDirValue = TryGetVariableString($"{prefix}InstallDir");
        var installScopeValue = TryGetVariableString($"{prefix}InstallScope");
        var installerFamilyValue = TryGetVariableString($"{prefix}InstallerFamily");
        var mainBinaryValue = TryGetVariableString($"{prefix}MainBinaryName");
        var versionValue = TryGetVariableString($"{prefix}Version");
        var values = new[]
        {
            locationValue,
            installDirValue,
            installScopeValue,
            installerFamilyValue,
            mainBinaryValue,
            versionValue,
        };
        if (values.All(string.IsNullOrWhiteSpace))
        {
            return ProductMarkerState.Absent;
        }

        var markerLocation = InstallPathPolicy.TryNormalizeLocalFolder(locationValue, mustExist: false);
        var markerInstallDir = InstallPathPolicy.TryNormalizeLocalFolder(installDirValue, mustExist: false);
        var markerVersion = TryNormalizeVersion(versionValue);
        var expectedScope = scope == BundleScope.PerMachine ? "perMachine" : "perUser";
        return markerLocation is not null &&
            markerInstallDir is not null &&
            markerVersion == version &&
            string.Equals(markerLocation, installFolder, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(markerInstallDir, installFolder, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(installScopeValue, expectedScope, StringComparison.Ordinal) &&
            string.Equals(installerFamilyValue, "BurnMsi", StringComparison.Ordinal) &&
            string.Equals(mainBinaryValue, InstalledExecutableName, StringComparison.OrdinalIgnoreCase)
                ? ProductMarkerState.Matching
                : ProductMarkerState.Mismatch;
    }

    private bool TryRevalidateRecoveryMsi(out BundleScope scope, out string installFolder)
    {
        scope = default;
        installFolder = string.Empty;

        if (missingMsiRecovery)
        {
            return TryRevalidateMissingMsiRecovery(out scope, out installFolder);
        }

        string productCode;
        BundleScope expectedScope;
        string expectedInstallFolder;
        Version expectedVersion;
        var recoveringRelatedProduct = false;
        if (currentMsiScope.HasValue &&
            !string.IsNullOrWhiteSpace(currentMsiInstallFolder) &&
            currentMsiVersion is not null &&
            applicationData.Bundle.Packages.TryGetValue(KmMsiPackageId, out var currentPackage) &&
            currentPackage.Type == PackageType.Msi &&
            !string.IsNullOrWhiteSpace(currentPackage.ProductCode))
        {
            productCode = currentPackage.ProductCode;
            expectedScope = currentMsiScope.Value;
            expectedInstallFolder = currentMsiInstallFolder;
            expectedVersion = currentMsiVersion;
        }
        else if (relatedMsiProducts.Count == 1)
        {
            var relatedProduct = relatedMsiProducts.Single();
            recoveringRelatedProduct = true;
            productCode = relatedProduct.Key;
            expectedScope = relatedProduct.Value.Scope;
            expectedInstallFolder = relatedProduct.Value.InstallFolder;
            expectedVersion = relatedProduct.Value.Version;
        }
        else
        {
            return false;
        }

        var registration = MsiProductInfo.TryGetRegistration(productCode);
        var bundleVersion = TryNormalizeVersion(BundleVersion);
        var markerState = registration is null
            ? ProductMarkerState.Mismatch
            : GetProductMarkerState(
                registration.Scope,
                registration.InstallFolder,
                registration.Version);
        var detectedBundleScope = TryGetDetectedBundleScope();
        if (registration is null ||
            bundleVersion is null ||
            registration.Scope != expectedScope ||
            registration.Version != expectedVersion ||
            registration.Version > bundleVersion ||
            !string.Equals(registration.InstallFolder, expectedInstallFolder, StringComparison.OrdinalIgnoreCase) ||
            markerState == ProductMarkerState.Mismatch ||
            installed &&
                !recoveringRelatedProduct &&
                currentBundleMsiScopeMismatch &&
                markerState != ProductMarkerState.Matching ||
            installed &&
                recoveringRelatedProduct &&
                (!detectedBundleScope.HasValue ||
                 detectedBundleScope.Value != registration.Scope) &&
                markerState != ProductMarkerState.Matching)
        {
            return false;
        }

        scope = registration.Scope;
        installFolder = registration.InstallFolder;
        return true;
    }

    private bool TryRevalidateMissingMsiRecovery(out BundleScope scope, out string installFolder)
    {
        scope = default;
        installFolder = string.Empty;
        if (!installed ||
            !missingMsiRecoveryScope.HasValue ||
            string.IsNullOrWhiteSpace(missingMsiRecoveryInstallFolder) ||
            missingMsiRecoveryVersion is null ||
            !applicationData.Bundle.Packages.TryGetValue(KmMsiPackageId, out var package) ||
            package.Type != PackageType.Msi ||
            string.IsNullOrWhiteSpace(package.ProductCode) ||
            string.IsNullOrWhiteSpace(package.UpgradeCode) ||
            MsiProductInfo.IsRegistered(package.ProductCode) ||
            HasExistingMsiRegistration())
        {
            return false;
        }

        var detectedBundleScope = TryGetDetectedBundleScope();
        var bundleVersion = TryNormalizeVersion(BundleVersion);
        var relatedProductCodes = MsiProductInfo.TryGetRelatedProductCodes(package.UpgradeCode);
        if (!detectedBundleScope.HasValue ||
            detectedBundleScope.Value != missingMsiRecoveryScope.Value ||
            bundleVersion != missingMsiRecoveryVersion ||
            relatedProductCodes is null ||
            relatedProductCodes.Count != 0 ||
            !TryResolveMissingMsiRecoveryEvidence(
                missingMsiRecoveryScope.Value,
                missingMsiRecoveryVersion,
                out var verifiedInstallFolder,
                out var verifiedMarkerState) ||
            verifiedMarkerState != missingMsiRecoveryMarkerState ||
            !string.Equals(
                verifiedInstallFolder,
                missingMsiRecoveryInstallFolder,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        scope = missingMsiRecoveryScope.Value;
        installFolder = verifiedInstallFolder;
        return true;
    }

    private bool CanRecoverFullBundleFromRelatedMsi(
        bool capturedExactCurrentMsi,
        bool exactCurrentMsiRegistered)
    {
        return installed &&
            !capturedExactCurrentMsi &&
            !exactCurrentMsiRegistered &&
            !currentMsiScope.HasValue &&
            !relatedMsiConflict &&
            relatedMsiProducts.Count == 1 &&
            relatedMsiScope.HasValue &&
            !string.IsNullOrWhiteSpace(relatedMsiInstallFolder) &&
            relatedMsiVersion is not null &&
            GetExistingMsiVersionState() == ExistingMsiVersionState.SameOrOlder &&
            FullRelatedMarkerAllowsRecovery();
    }

    private bool FullRelatedMarkerAllowsRecovery()
    {
        if (!relatedMsiScope.HasValue ||
            string.IsNullOrWhiteSpace(relatedMsiInstallFolder) ||
            relatedMsiVersion is null)
        {
            return false;
        }

        var markerState = GetProductMarkerState(
            relatedMsiScope.Value,
            relatedMsiInstallFolder,
            relatedMsiVersion);
        var detectedBundleScope = TryGetDetectedBundleScope();
        return detectedBundleScope.HasValue &&
            (detectedBundleScope.Value == relatedMsiScope.Value
                ? markerState != ProductMarkerState.Mismatch
                : markerState == ProductMarkerState.Matching);
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
                !missingMsiRecovery &&
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
        var searchVersion = string.IsNullOrWhiteSpace(existingVersionValue) || existingVersionValue == "0.0.0.0"
            ? null
            : TryNormalizeVersion(existingVersionValue);
        var detectedVersion = currentMsiVersion ?? relatedMsiVersion;

        if (searchVersion is null && detectedVersion is null)
        {
            return ExistingMsiVersionState.None;
        }

        var bundleVersion = TryNormalizeVersion(BundleVersion);
        if (bundleVersion is null ||
            !string.IsNullOrWhiteSpace(existingVersionValue) &&
            existingVersionValue != "0.0.0.0" &&
            searchVersion is null ||
            searchVersion is not null &&
            detectedVersion is not null &&
            searchVersion != detectedVersion)
        {
            return ExistingMsiVersionState.Invalid;
        }

        var existingVersion = detectedVersion ?? searchVersion!;
        return existingVersion > bundleVersion
            ? ExistingMsiVersionState.Newer
            : ExistingMsiVersionState.SameOrOlder;
    }

    private static Version? TryNormalizeVersion(string? value)
    {
        if (!Version.TryParse(value, out var version))
        {
            return null;
        }

        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

    private bool IsKnownMsiInstallFolder(string folder)
    {
        if (string.Equals(currentMsiInstallFolder, folder, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(missingMsiRecoveryInstallFolder, folder, StringComparison.OrdinalIgnoreCase))
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

    private sealed record RelatedMsiDetection(
        BundleScope Scope,
        string InstallFolder,
        Version Version);

    private sealed record RelatedBundleDetection(
        BundleScope Scope,
        RelationType RelationType,
        Version? Version,
        bool MissingFromCache);

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

    private enum ProductMarkerState
    {
        Absent,
        Matching,
        Mismatch,
    }
}
