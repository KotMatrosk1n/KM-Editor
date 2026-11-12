// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace KM.Core.Output;

public enum OutputDiagnosticStage
{
    ValidatePaths, CreateWorkspace, AcquireWorkspaceLock, ClaimLegacyMetadata,
    AcquireLegacyLock, CreateGameWorkspace, ResumePublication, MigrateLegacy,
    ValidateStore, CheckOtherGames, PrepareWorkingTransactions, Ready,
}

public enum OutputDiagnosticObject
{
    OutputRoot, WorkspaceDirectory, WorkspaceLock, LegacyMetadataDirectory,
    LegacyLock, GameWorkspaceDirectory, MetadataStore, MetadataDirectory,
    MetadataFile, MarkerTemporaryFile, WorkingTransactions,
}

public enum OutputOwnerCategory { Unknown, CurrentUser, Administrators, System, Other }

public sealed record OutputStorageDiagnostic(string Role, string Status, string FileSystem,
    bool? PersistentAcls, long? FreeBytes, int? NativeError);
public sealed record OutputAttemptDiagnostic(long Sequence, string Stage, string? LastCompletedStage,
    string ObjectRole, long ElapsedMs, int LockRetries, string Outcome, string? SecurityFailure,
    string? MigrationFailure, string ErrorKind, string? HResult, int? NativeError,
    string ObjectOwner, string TokenOwner, bool? Elevated, bool? AclProtected,
    int? AclRuleCount, IReadOnlyList<OutputStorageDiagnostic> Storage);
public sealed record OutputDiagnosticSnapshot(int SchemaVersion, string BackendVersion, string? BackendCommit,
    long AttemptCount, IReadOnlyList<OutputAttemptDiagnostic> Attempts);

/// <summary>Session only, bounded observations. Never retains paths, messages, identities, or file contents.</summary>
public static class OutputDiagnosticJournal
{
    private static readonly AsyncLocal<Attempt?> Current = new();
    private static readonly object Sync = new();
    private static readonly List<OutputAttemptDiagnostic> Recent = [];
    private static readonly ConditionalWeakTable<Exception, OutputAttemptDiagnostic> Failures = new();
    private static long sequence;

    public static Attempt Begin()
    {
        var attempt = new Attempt(Interlocked.Increment(ref sequence), Current.Value);
        Current.Value = attempt;
        return attempt;
    }

    public static void Step(OutputDiagnosticStage stage, OutputDiagnosticObject role) => Current.Value?.Step(stage, role);
    public static void Object(OutputDiagnosticObject role) { if (Current.Value is { } a) a.SetObject(role); }
    internal static OutputAttemptDiagnostic? FindFailure(Exception exception) =>
        Failures.TryGetValue(exception, out var entry) ? entry : null;
    public static void RetryLock() { if (Current.Value is { HasFailed: false } a) a.Retries++; }
    public static void Failed(Exception exception) => Current.Value?.Failed(exception);

    public static void ObserveOwner(SecurityIdentifier? owner, SecurityIdentifier? user, bool? protectedAcl = null, int? rules = null)
    {
        if (Current.Value is not { HasFailed: false } a || !OperatingSystem.IsWindows()) return;
        a.Owner = owner is null ? OutputOwnerCategory.Unknown : owner == user ? OutputOwnerCategory.CurrentUser
            : owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ? OutputOwnerCategory.Administrators
            : owner.IsWellKnown(WellKnownSidType.LocalSystemSid) ? OutputOwnerCategory.System : OutputOwnerCategory.Other;
        a.AclProtected = protectedAcl;
        a.AclRuleCount = rules;
    }

    public static OutputDiagnosticSnapshot Snapshot()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(OutputDiagnosticJournal).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "0.0";
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var commit = info is null ? null : Regex.Match(info, @"(?<=\+)[0-9a-fA-F]{40}(?=$|\.)").Value;
        lock (Sync) return new(1, version, string.IsNullOrEmpty(commit) ? null : commit.ToLowerInvariant(),
            Interlocked.Read(ref sequence), Recent.ToArray());
    }

    public sealed class Attempt : IDisposable
    {
        private readonly long id;
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly Attempt? previous;
        private readonly List<OutputStorageDiagnostic> storage = [];
        private OutputDiagnosticStage stage;
        private OutputDiagnosticStage? completed;
        private Exception? failure;
        internal bool HasFailed => failure is not null;
        private OutputOwnerCategory tokenOwner;
        private bool? elevated;
        internal OutputDiagnosticObject Role;
        internal int Retries;
        internal OutputOwnerCategory Owner;
        internal bool? AclProtected;
        internal int? AclRuleCount;

        internal Attempt(long id, Attempt? previous)
        {
            this.id = id;
            this.previous = previous;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var owner = identity.Owner;
                    tokenOwner = owner is null ? OutputOwnerCategory.Unknown : owner == identity.User ? OutputOwnerCategory.CurrentUser
                        : owner?.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) == true ? OutputOwnerCategory.Administrators
                        : owner is null ? OutputOwnerCategory.Unknown : OutputOwnerCategory.Other;
                    elevated = OutputVolumeCapabilities.ReadElevation(identity.AccessToken);
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or InvalidOperationException) { }
            }
        }

        public void Storage(string outputRoot, string workspaceRoot)
        {
            storage.Add(OutputVolumeCapabilities.Describe(outputRoot, "output"));
            storage.Add(OutputVolumeCapabilities.Describe(workspaceRoot, "workspace"));
        }

        internal void Step(OutputDiagnosticStage next, OutputDiagnosticObject role)
        {
            if (failure is not null) return;
            if (next != stage) completed = stage;
            stage = next;
            SetObject(role);
        }

        internal void SetObject(OutputDiagnosticObject role)
        {
            if (failure is not null) return;
            Role = role;
            Owner = OutputOwnerCategory.Unknown;
            AclProtected = null;
            AclRuleCount = null;
        }

        public void Failed(Exception exception)
        {
            failure ??= exception;
            // Preserve only controlled failure categories and numeric OS error codes on disposal.
        }

        public void Dispose()
        {
            string? security = null, migration = null, hresult = null;
            int? native = null;
            var kind = "none";
            var depth = 0;
            for (var cause = failure; cause is not null && depth++ < 8; cause = cause.InnerException)
            {
                if (cause is OutputMetadataSecurityException s) security = s.Failure.ToString();
                if (cause is OutputWorkspaceMigrationException m) migration = m.Failure.ToString();
                kind = cause switch
                {
                    OutputMetadataSecurityException => "security", OutputWorkspaceMigrationException => "migration",
                    OutputRootLockTimeoutException => "lock-timeout", UnauthorizedAccessException => "access",
                    IOException => "io", System.ComponentModel.Win32Exception => "native",
                    OperationCanceledException => "cancelled", OutputPathSecurityException => "unsafe-path", _ => "other",
                };
                hresult = $"0x{cause.HResult:X8}";
                if (cause is System.ComponentModel.Win32Exception w) native = w.NativeErrorCode;
            }
            var entry = new OutputAttemptDiagnostic(id, stage.ToString(), completed?.ToString(), Role.ToString(),
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, Retries, failure is null ? "success" : "failure",
                security, migration, kind, hresult, native, Owner.ToString(), tokenOwner.ToString(), elevated,
                AclProtected, AclRuleCount, storage.ToArray());
            lock (Sync)
            {
                Recent.Add(entry);
                while (Recent.Count > 8)
                {
                    var success = Recent.FindIndex(item => item.Outcome == "success");
                    Recent.RemoveAt(success >= 0 ? success : 0);
                }
            }
            if (failure is not null) Failures.AddOrUpdate(failure, entry);
            failure = null;
            Current.Value = previous;
        }
    }
}
