// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;

namespace KM.Core.Output;

public readonly record struct OutputTransactionId
{
    public OutputTransactionId(string value)
    {
        if (value is null
            || value.Length != 32
            || value.Any(character =>
                !char.IsAsciiDigit(character)
                && character is not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "An output transaction id must be 32 lowercase hexadecimal characters.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static OutputTransactionId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}

public enum OutputTransactionPhase
{
    Preparing = 1,
    Prepared = 2,
    Committing = 3,
    Committed = 4,
    RollingBack = 5,
    RolledBack = 6,
    RecoveryRequired = 7,
    Finalizing = 8,
}

public enum OutputApplyOutcome
{
    Committed = 1,
    RolledBack = 2,
    RecoveryRequired = 3,
}

/// <summary>
/// Stable machine-readable outcome codes emitted by durable output transactions.
/// </summary>
public static class OutputOutcomeCodes
{
    public const string CommitFailed = "KM-OUTPUT-COMMIT-FAILED";
    public const string FinalizationFailed = "KM-OUTPUT-FINALIZATION-FAILED";
    public const string PostimageChanged = "KM-OUTPUT-POSTIMAGE-CHANGED";
    public const string RollbackTargetChanged = "KM-OUTPUT-ROLLBACK-TARGET-CHANGED";
    public const string BackupInvalid = "KM-OUTPUT-BACKUP-INVALID";
    public const string RollbackVerificationFailed = "KM-OUTPUT-ROLLBACK-VERIFICATION-FAILED";
    public const string RollbackFailed = "KM-OUTPUT-ROLLBACK-FAILED";
    public const string StartupRecovery = "KM-OUTPUT-STARTUP-RECOVERY";
    public const string UnknownTargetState = "KM-OUTPUT-UNKNOWN-TARGET-STATE";

    internal static bool IsKmCode(string value)
    {
        if (!value.StartsWith("KM-", StringComparison.Ordinal)
            || value[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;
        foreach (var character in value.AsSpan(3))
        {
            if (character == '-')
            {
                if (previousWasHyphen)
                {
                    return false;
                }

                previousWasHyphen = true;
                continue;
            }

            if (character is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            {
                return false;
            }

            previousWasHyphen = false;
        }

        return true;
    }
}

public sealed record OutputApplyReceiptTarget
{
    public OutputApplyReceiptTarget(
        RelativeOutputPath path,
        OutputMutationKind kind,
        OutputFileState preimage,
        OutputFileState postimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        OutputRuntimeMutableDescriptor? runtimeMutableDescriptor = null)
        : this(
            path,
            kind,
            preimage,
            postimage,
            (ownershipClaims ?? throw new ArgumentNullException(nameof(ownershipClaims))).ToImmutableArray(),
            runtimeMutableDescriptor)
    {
    }

    [JsonConstructor]
    public OutputApplyReceiptTarget(
        RelativeOutputPath path,
        OutputMutationKind kind,
        OutputFileState preimage,
        OutputFileState postimage,
        ImmutableArray<OwnedTarget> ownershipClaims,
        OutputRuntimeMutableDescriptor? runtimeMutableDescriptor = null)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Kind = SemanticContractGuards.DefinedEnum(kind, nameof(kind));
        Preimage = preimage ?? throw new ArgumentNullException(nameof(preimage));
        Postimage = postimage ?? throw new ArgumentNullException(nameof(postimage));
        OwnershipClaims = ownershipClaims;
        var maximumAddressableLength = Math.Max(Preimage.LengthBytes, Postimage.LengthBytes);
        if (OwnershipClaims.IsDefaultOrEmpty
            || OwnershipClaims.Length > OutputLimits.MaximumOwnershipClaimsPerMutation
            || OwnershipClaims.Distinct().Count() != OwnershipClaims.Length
            || OwnershipClaims.Any(claim =>
                claim is null
                || claim.Address.File != path
                || claim.Address.ByteRange is { } range
                && range.EndExclusive > maximumAddressableLength))
        {
            throw new ArgumentException("Receipt ownership claims are invalid or out of bounds.", nameof(ownershipClaims));
        }

        if (runtimeMutableDescriptor is not null)
        {
            runtimeMutableDescriptor.ValidateIdentity(path, OwnershipClaims[0].GameFamily);
            var state = Kind == OutputMutationKind.Write ? Postimage : Preimage;
            if (!runtimeMutableDescriptor.IsValidStateMetadata(state, Kind)
                || !OwnershipClaims.Any(claim => claim.Address.ScopeKind == OwnedTargetScopeKind.File))
            {
                throw new ArgumentException(
                    "A runtime-mutable receipt target has invalid state, generation, or ownership scope.",
                    nameof(runtimeMutableDescriptor));
            }
        }

        RuntimeMutableDescriptor = runtimeMutableDescriptor;
    }

    public RelativeOutputPath Path { get; }

    public OutputMutationKind Kind { get; }

    public OutputFileState Preimage { get; }

    public OutputFileState Postimage { get; }

    public ImmutableArray<OwnedTarget> OwnershipClaims { get; }

    public OutputRuntimeMutableDescriptor? RuntimeMutableDescriptor { get; }
}

public sealed record OutputApplyReceipt
{
    public OutputHistoryDetails? HistoryDetails { get; init; }

    public OutputApplyReceipt(
        OutputTransactionId transactionId,
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        string semanticReviewHash,
        OutputApplyOutcome outcome,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        IEnumerable<OutputApplyOrigin> origins,
        IEnumerable<OutputApplyReceiptTarget> targets,
        string? outcomeCode = null)
        : this(
            transactionId,
            projectId,
            gameFamily,
            outputMode,
            semanticReviewHash,
            outcome,
            startedAtUtc,
            completedAtUtc,
            (origins ?? throw new ArgumentNullException(nameof(origins))).ToImmutableArray(),
            (targets ?? throw new ArgumentNullException(nameof(targets))).ToImmutableArray(),
            outcomeCode)
    {
    }

    [JsonConstructor]
    public OutputApplyReceipt(
        OutputTransactionId transactionId,
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        string semanticReviewHash,
        OutputApplyOutcome outcome,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        ImmutableArray<OutputApplyOrigin> origins,
        ImmutableArray<OutputApplyReceiptTarget> targets,
        string? outcomeCode = null)
    {
        if (string.IsNullOrWhiteSpace(transactionId.Value))
        {
            throw new ArgumentException("An apply receipt requires a transaction id.", nameof(transactionId));
        }

        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));

        if (startedAtUtc == default || completedAtUtc < startedAtUtc)
        {
            throw new ArgumentException("An apply receipt requires an ordered UTC time range.");
        }

        TransactionId = transactionId;
        ProjectId = projectId;
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        OutputMode = SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        SemanticReviewHash = SemanticContractGuards.Sha256Fingerprint(semanticReviewHash, nameof(semanticReviewHash));
        Outcome = SemanticContractGuards.DefinedEnum(outcome, nameof(outcome));
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        Origins = ValidateCollection(origins, OutputLimits.MaximumOriginsPerApply, nameof(origins));
        Targets = ValidateCollection(targets, OutputLimits.MaximumMutationsPerApply, nameof(targets));
        if (Origins.Distinct().Count() != Origins.Length
            || Targets.Select(target => target.Path.CanonicalKey).Distinct(StringComparer.Ordinal).Count()
            != Targets.Length
            || Targets.Any(target => target.OwnershipClaims.Any(claim => claim.GameFamily != GameFamily)))
        {
            throw new ArgumentException("Apply receipt origins, targets, or ownership scope are invalid.");
        }

        if (outcomeCode is not null)
        {
            _ = SemanticContractGuards.StableCode(outcomeCode, nameof(outcomeCode));
            if (!OutputOutcomeCodes.IsKmCode(outcomeCode))
            {
                throw new ArgumentException(
                    "An outcome code must use the uppercase KM-prefixed format.",
                    nameof(outcomeCode));
            }
        }

        OutcomeCode = outcomeCode;
    }

    public OutputTransactionId TransactionId { get; }

    public ProjectId ProjectId { get; }

    public GameFamily GameFamily { get; }

    public string OutputMode { get; }

    public string SemanticReviewHash { get; }

    public OutputApplyOutcome Outcome { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset CompletedAtUtc { get; }

    public ImmutableArray<OutputApplyOrigin> Origins { get; }

    public ImmutableArray<OutputApplyReceiptTarget> Targets { get; }

    public string? OutcomeCode { get; }

    private static ImmutableArray<T> ValidateCollection<T>(
        IEnumerable<T> values,
        int maximumCount,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (var value in values)
        {
            if (value is null || builder.Count == maximumCount)
            {
                throw new ArgumentException("An apply receipt collection is invalid or out of bounds.", parameterName);
            }

            builder.Add(value);
        }

        if (builder.Count == 0)
        {
            throw new ArgumentException("An apply receipt collection cannot be empty.", parameterName);
        }

        return builder.ToImmutable();
    }
}

public sealed record OutputApplyResult(
    OutputApplyOutcome Outcome,
    OutputTransactionId TransactionId,
    OutputApplyReceipt Receipt);

public enum OutputRecoveryDisposition
{
    NoAction = 1,
    FinalizeCommit = 2,
    RollBack = 3,
    RecoveryRequired = 4,
}

public sealed record OutputRecoveryTransactionStatus
{
    public OutputRecoveryTransactionStatus(
        OutputTransactionId transactionId,
        OutputTransactionPhase phase,
        OutputRecoveryDisposition disposition,
        IEnumerable<RelativeOutputPath>? unknownTargets = null,
        bool journalReadable = true)
    {
        TransactionId = transactionId;
        Phase = SemanticContractGuards.DefinedEnum(phase, nameof(phase));
        Disposition = SemanticContractGuards.DefinedEnum(disposition, nameof(disposition));
        JournalReadable = journalReadable;
        UnknownTargets = (unknownTargets ?? []).ToImmutableArray();
        if (UnknownTargets.Length > OutputLimits.MaximumMutationsPerApply
            || UnknownTargets.Any(path => path is null))
        {
            throw new ArgumentException("Recovery status targets are invalid or out of bounds.", nameof(unknownTargets));
        }
    }

    public OutputTransactionId TransactionId { get; }

    public OutputTransactionPhase Phase { get; }

    public OutputRecoveryDisposition Disposition { get; }

    public bool JournalReadable { get; }

    public ImmutableArray<RelativeOutputPath> UnknownTargets { get; }
}

public sealed record OutputRecoveryReport
{
    public OutputRecoveryReport(
        OutputStateRevision revision,
        IEnumerable<OutputRecoveryTransactionStatus> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        if (string.IsNullOrWhiteSpace(revision.Value))
        {
            throw new ArgumentException("A recovery report requires a state revision.", nameof(revision));
        }

        Revision = revision;
        Transactions = transactions.ToImmutableArray();
        if (Transactions.Length > OutputLimits.MaximumRecoveryTransactions
            || Transactions.Any(status => status is null))
        {
            throw new ArgumentException("A recovery report is invalid or out of bounds.", nameof(transactions));
        }
    }

    public OutputStateRevision Revision { get; }

    public ImmutableArray<OutputRecoveryTransactionStatus> Transactions { get; }

    public bool RequiresRecovery => Transactions.Any(
        status => status.Disposition == OutputRecoveryDisposition.RecoveryRequired);
}

public sealed record OutputApplyHistory
{
    public OutputApplyHistory(
        OutputStateRevision revision,
        IEnumerable<OutputApplyReceipt> receipts)
    {
        if (string.IsNullOrWhiteSpace(revision.Value))
        {
            throw new ArgumentException("Output history requires a state revision.", nameof(revision));
        }

        ArgumentNullException.ThrowIfNull(receipts);
        Receipts = receipts.ToImmutableArray();
        if (Receipts.Length > OutputLimits.MaximumHistoryReceipts
            || Receipts.Any(receipt => receipt is null))
        {
            throw new ArgumentException("Output history is invalid or out of bounds.", nameof(receipts));
        }

        Revision = revision;
    }

    public OutputStateRevision Revision { get; }

    public ImmutableArray<OutputApplyReceipt> Receipts { get; }
}

public sealed record OutputTransactionCoordinatorOptions
{
    public int MaximumMutationsPerApply { get; init; } = 512;

    public int MaximumOwnershipClaimsPerMutation { get; init; } = 128;

    public int MaximumHistoryReceipts { get; init; } = 128;

    public int MaximumCheckpoints { get; init; } = 8;

    public int MaximumIntegrityEntries { get; init; } = 100_000;

    public int MaximumWriteBytesPerMutation { get; init; } = 128 * 1024 * 1024;

    public long MaximumWriteBytesPerApply { get; init; } = 512L * 1024L * 1024L;

    public long MaximumFingerprintFileBytes { get; init; } = 4L * 1024L * 1024L * 1024L;

    public long MaximumBackupBytesPerApply { get; init; } = 512L * 1024L * 1024L;

    public long MaximumCheckpointBytes { get; init; } = 512L * 1024L * 1024L;

    public TimeSpan WriterLockTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan WriterLockRetryDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    internal void Validate()
    {
        EnsureRange(MaximumMutationsPerApply, 1, OutputLimits.MaximumMutationsPerApply, nameof(MaximumMutationsPerApply));
        EnsureRange(MaximumOwnershipClaimsPerMutation, 1, OutputLimits.MaximumOwnershipClaimsPerMutation, nameof(MaximumOwnershipClaimsPerMutation));
        EnsureRange(MaximumHistoryReceipts, 1, OutputLimits.MaximumHistoryReceipts, nameof(MaximumHistoryReceipts));
        EnsureRange(MaximumCheckpoints, 1, OutputLimits.MaximumCheckpoints, nameof(MaximumCheckpoints));
        EnsureRange(MaximumIntegrityEntries, 1, OutputLimits.MaximumIntegrityEntries, nameof(MaximumIntegrityEntries));
        EnsureRange(MaximumWriteBytesPerMutation, 1, OutputLimits.MaximumWriteBytesPerMutation, nameof(MaximumWriteBytesPerMutation));
        EnsureRange(MaximumWriteBytesPerApply, 1, OutputLimits.MaximumWriteBytesPerApply, nameof(MaximumWriteBytesPerApply));
        EnsureRange(MaximumFingerprintFileBytes, 1, OutputLimits.MaximumFingerprintFileBytes, nameof(MaximumFingerprintFileBytes));
        EnsureRange(MaximumBackupBytesPerApply, 1, OutputLimits.MaximumBackupBytesPerApply, nameof(MaximumBackupBytesPerApply));
        EnsureRange(MaximumCheckpointBytes, 1, OutputLimits.MaximumCheckpointBytes, nameof(MaximumCheckpointBytes));
        if (MaximumWriteBytesPerApply < MaximumWriteBytesPerMutation)
        {
            throw new ArgumentException("The apply byte limit cannot be smaller than the per-mutation byte limit.");
        }

        if (WriterLockTimeout <= TimeSpan.Zero || WriterLockTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(WriterLockTimeout));
        }

        if (WriterLockRetryDelay <= TimeSpan.Zero || WriterLockRetryDelay > WriterLockTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(WriterLockRetryDelay));
        }
    }

    private static void EnsureRange(long value, long minimum, long maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"The value must be between {minimum} and {maximum}.");
        }
    }
}

public class OutputCoordinatorException : Exception
{
    public OutputCoordinatorException(string message)
        : base(message)
    {
    }

    public OutputCoordinatorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class OutputPathSecurityException : OutputCoordinatorException
{
    public OutputPathSecurityException()
        : base("The output path could not be proven safe.")
    {
    }

    protected OutputPathSecurityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class OutputMetadataLayoutUnavailableException : OutputPathSecurityException
{
    public OutputMetadataLayoutUnavailableException(OutputPathSecurityException innerException)
        : base(innerException.Message, innerException)
    {
    }
}

public sealed class OutputPreimageConflictException : OutputCoordinatorException
{
    public OutputPreimageConflictException(RelativeOutputPath path)
        : base($"The output target '{path}' no longer matches its reviewed preimage.")
    {
        Path = path;
    }

    public RelativeOutputPath Path { get; }
}

public sealed class OutputReviewStateConflictException : OutputCoordinatorException
{
    public OutputReviewStateConflictException()
        : base("The reviewed output state could not be revalidated.")
    {
    }

    public OutputReviewStateConflictException(Exception innerException)
        : base("The reviewed output state could not be revalidated.", innerException)
    {
    }
}

public sealed class OutputOwnershipConflictException : OutputCoordinatorException
{
    public OutputOwnershipConflictException(RelativeOutputPath path)
        : base($"Ownership of the output target '{path}' is not proven for this project.")
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public RelativeOutputPath Path { get; }
}

public sealed class OutputCheckpointConflictException : OutputCoordinatorException
{
    public OutputCheckpointConflictException(OutputCheckpointId checkpointId)
        : base("The output checkpoint or its reviewed output state changed.")
    {
        CheckpointId = checkpointId;
    }

    public OutputCheckpointId CheckpointId { get; }
}

public sealed class OutputCheckpointNotFoundException : OutputCoordinatorException
{
    public OutputCheckpointNotFoundException(OutputCheckpointId checkpointId)
        : base("The requested output checkpoint does not exist.")
    {
        CheckpointId = checkpointId;
    }

    public OutputCheckpointId CheckpointId { get; }
}

public sealed class OutputRecoveryRequiredException : OutputCoordinatorException
{
    public OutputRecoveryRequiredException(OutputRecoveryReport report)
        : base("Output recovery requires manual review before another write can begin.")
    {
        Report = report ?? throw new ArgumentNullException(nameof(report));
    }

    public OutputRecoveryReport Report { get; }
}

public sealed class OutputStateRevisionConflictException : OutputCoordinatorException
{
    public OutputStateRevisionConflictException(
        OutputStateRevision expected,
        OutputStateRevision actual)
        : base("Output state changed after it was reviewed.")
    {
        Expected = expected;
        Actual = actual;
    }

    public OutputStateRevision Expected { get; }

    public OutputStateRevision Actual { get; }
}

public sealed class OutputRootLockTimeoutException : OutputCoordinatorException
{
    public OutputRootLockTimeoutException(TimeSpan timeout)
        : base($"The output root remained busy for {timeout}.")
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}

public sealed class OutputLimitExceededException : OutputCoordinatorException
{
    public OutputLimitExceededException(string message)
        : base(message)
    {
    }
}
