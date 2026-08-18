// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using KM.Core.Semantics;

namespace KM.Core.Output;

public static class OutputSupportDiagnosticCodes
{
    public const string IntegrityStale = "KM-OUTPUT-INTEGRITY-STALE";
    public const string RecoveryManualRequired = "KM-OUTPUT-RECOVERY-MANUAL-REQUIRED";
}

/// <summary>
/// A bounded diagnostic summary that contains no absolute paths, file payloads,
/// machine identity, account identity, environment values, or secrets.
/// </summary>
public sealed record OutputSupportReport
{
    public const int CurrentSchemaVersion = 1;

    public OutputSupportReport(
        string applicationVersion,
        GameFamily gameFamily,
        string outputMode,
        IEnumerable<string> diagnosticCodes,
        IEnumerable<OutputTransactionPhase> transactionPhases,
        IEnumerable<OutputIntegrityCount> integrityCounts,
        int ownershipFileCount,
        int checkpointCount,
        int historyReceiptCount,
        DateTimeOffset createdAtUtc)
    {
        ApplicationVersion = ValidateSafeText(applicationVersion, 128, nameof(applicationVersion));
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        OutputMode = SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        DiagnosticCodes = ValidateCodes(diagnosticCodes);
        TransactionPhases = ValidatePhases(transactionPhases);
        IntegrityCounts = ValidateIntegrityCounts(integrityCounts);

        if (ownershipFileCount < 0
            || ownershipFileCount > OutputLimits.MaximumIntegrityEntries
            || checkpointCount < 0
            || checkpointCount > OutputLimits.MaximumCheckpoints
            || historyReceiptCount < 0
            || historyReceiptCount > OutputLimits.MaximumHistoryReceipts
            || createdAtUtc == default)
        {
            throw new ArgumentException("Support-report counts and timestamp must be valid.");
        }

        OwnershipFileCount = ownershipFileCount;
        CheckpointCount = checkpointCount;
        HistoryReceiptCount = historyReceiptCount;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public int SchemaVersion => CurrentSchemaVersion;

    public string ApplicationVersion { get; }

    public GameFamily GameFamily { get; }

    public string OutputMode { get; }

    public ImmutableArray<string> DiagnosticCodes { get; }

    public ImmutableArray<OutputTransactionPhase> TransactionPhases { get; }

    public ImmutableArray<OutputIntegrityCount> IntegrityCounts { get; }

    public int OwnershipFileCount { get; }

    public int CheckpointCount { get; }

    public int HistoryReceiptCount { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    private static ImmutableArray<string> ValidateCodes(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inspected = 0;
        foreach (var code in codes)
        {
            if (inspected == 256)
            {
                throw new ArgumentException("A support report cannot inspect more than 256 diagnostic codes.", nameof(codes));
            }

            inspected++;
            var validated = SemanticContractGuards.StableCode(code, nameof(codes));
            if (!OutputOutcomeCodes.IsKmCode(validated))
            {
                throw new ArgumentException(
                    "Support-report codes must use the uppercase KM-prefixed format.",
                    nameof(codes));
            }

            if (!seen.Add(validated))
            {
                continue;
            }

            if (result.Count == 256)
            {
                throw new ArgumentException("A support report cannot contain more than 256 diagnostic codes.", nameof(codes));
            }

            result.Add(validated);
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<OutputTransactionPhase> ValidatePhases(
        IEnumerable<OutputTransactionPhase> phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        var maximum = Enum.GetValues<OutputTransactionPhase>().Length;
        var result = ImmutableArray.CreateBuilder<OutputTransactionPhase>();
        var seen = new HashSet<OutputTransactionPhase>();
        var inspected = 0;
        foreach (var phase in phases)
        {
            if (inspected == OutputLimits.MaximumRecoveryTransactions)
            {
                throw new ArgumentException("Support-report transaction phases are invalid.", nameof(phases));
            }

            inspected++;
            if (!Enum.IsDefined(phase))
            {
                throw new ArgumentException("Support-report transaction phases are invalid.", nameof(phases));
            }

            if (!seen.Add(phase))
            {
                continue;
            }

            if (result.Count == maximum)
            {
                throw new ArgumentException("Support-report transaction phases are invalid.", nameof(phases));
            }

            result.Add(phase);
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<OutputIntegrityCount> ValidateIntegrityCounts(
        IEnumerable<OutputIntegrityCount> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var maximum = Enum.GetValues<OutputIntegrityClassification>().Length;
        var result = ImmutableArray.CreateBuilder<OutputIntegrityCount>();
        var seen = new HashSet<OutputIntegrityClassification>();
        foreach (var count in counts)
        {
            if (count is null || !seen.Add(count.Classification) || result.Count == maximum)
            {
                throw new ArgumentException("Support-report integrity counts are invalid.", nameof(counts));
            }

            result.Add(count);
        }

        return result.ToImmutable();
    }

    private static string ValidateSafeText(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(char.IsControl)
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains(':'))
        {
            throw new ArgumentException("Support-report text must be bounded and path-free.", parameterName);
        }

        return value;
    }
}

public sealed record OutputIntegrityCount
{
    public OutputIntegrityCount(OutputIntegrityClassification classification, int count)
    {
        Classification = SemanticContractGuards.DefinedEnum(classification, nameof(classification));
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Count = count;
    }

    public OutputIntegrityClassification Classification { get; }

    public int Count { get; }
}
