// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Text.Json.Serialization;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Output;

public sealed record OutputOwnershipRecord
{
    public OutputOwnershipRecord(
        RelativeOutputPath path,
        OutputFileState currentState,
        IEnumerable<OwnedTarget> claims,
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        bool fileDeleteEligible,
        OutputTransactionId transactionId,
        DateTimeOffset updatedAtUtc)
        : this(
            path,
            currentState,
            (claims ?? throw new ArgumentNullException(nameof(claims))).ToImmutableArray(),
            projectId,
            gameFamily,
            outputMode,
            fileDeleteEligible,
            transactionId,
            updatedAtUtc)
    {
    }

    [JsonConstructor]
    public OutputOwnershipRecord(
        RelativeOutputPath path,
        OutputFileState currentState,
        ImmutableArray<OwnedTarget> claims,
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        bool fileDeleteEligible,
        OutputTransactionId transactionId,
        DateTimeOffset updatedAtUtc)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        CurrentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        if (!currentState.Exists)
        {
            throw new ArgumentException("An ownership record must describe an existing output file.", nameof(currentState));
        }

        Claims = claims;
        if (Claims.IsDefaultOrEmpty
            || Claims.Length > OutputLimits.MaximumOwnershipClaimsPerMutation
            || Claims.Distinct().Count() != Claims.Length
            || Claims.Any(claim =>
                claim is null
                || claim.Address.File != path
                || claim.Address.ByteRange is { } range
                && range.EndExclusive > currentState.LengthBytes))
        {
            throw new ArgumentException("Ownership claims are invalid or out of bounds.", nameof(claims));
        }

        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        if (string.IsNullOrWhiteSpace(transactionId.Value)
            || updatedAtUtc == default)
        {
            throw new ArgumentException("An ownership record requires project, transaction, and timestamp metadata.");
        }

        ProjectId = projectId;
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        OutputMode = SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        if (Claims.Any(claim => claim.GameFamily != GameFamily))
        {
            throw new ArgumentException("Ownership claims must match the record game family.", nameof(claims));
        }

        FileDeleteEligible = fileDeleteEligible;
        TransactionId = transactionId;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public RelativeOutputPath Path { get; }

    public OutputFileState CurrentState { get; }

    public ImmutableArray<OwnedTarget> Claims { get; }

    public ProjectId ProjectId { get; }

    public GameFamily GameFamily { get; }

    public string OutputMode { get; }

    /// <summary>
    /// True only when whole-file cleanup provenance was established by creating
    /// a previously missing file or inherited from an earlier eligible record.
    /// A current whole-file claim is still required before cleanup may delete it.
    /// </summary>
    public bool FileDeleteEligible { get; }

    public OutputTransactionId TransactionId { get; }

    public DateTimeOffset UpdatedAtUtc { get; }
}

public sealed record OutputCreatedDirectoryOwnership
{
    public OutputCreatedDirectoryOwnership(
        RelativeOutputPath path,
        RelativeOutputPath authorizationTarget,
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        OutputTransactionId transactionId,
        DateTimeOffset createdAtUtc)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        AuthorizationTarget = authorizationTarget ?? throw new ArgumentNullException(nameof(authorizationTarget));
        if (!IsStrictAncestor(Path, AuthorizationTarget))
        {
            throw new ArgumentException(
                "Created-directory ownership requires a strict descendant authorization target.",
                nameof(authorizationTarget));
        }

        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        if (string.IsNullOrWhiteSpace(transactionId.Value)
            || createdAtUtc == default)
        {
            throw new ArgumentException("Created-directory ownership requires project, transaction, and timestamp metadata.");
        }

        ProjectId = projectId;
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        OutputMode = SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        TransactionId = transactionId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public RelativeOutputPath Path { get; }

    public RelativeOutputPath AuthorizationTarget { get; }

    public ProjectId ProjectId { get; }

    public GameFamily GameFamily { get; }

    public string OutputMode { get; }

    public OutputTransactionId TransactionId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    private static bool IsStrictAncestor(RelativeOutputPath ancestor, RelativeOutputPath target)
    {
        return target.CanonicalKey.StartsWith(
            ancestor.CanonicalKey + "/",
            StringComparison.Ordinal);
    }
}

public sealed record OutputOwnershipInventory
{
    public const int CurrentSchemaVersion = 2;

    public OutputOwnershipInventory(
        int schemaVersion,
        IEnumerable<OutputOwnershipRecord> files,
        IEnumerable<OutputCreatedDirectoryOwnership> createdDirectories)
        : this(
            schemaVersion,
            (files ?? throw new ArgumentNullException(nameof(files))).ToImmutableArray(),
            (createdDirectories ?? throw new ArgumentNullException(nameof(createdDirectories))).ToImmutableArray())
    {
    }

    [JsonConstructor]
    public OutputOwnershipInventory(
        int schemaVersion,
        ImmutableArray<OutputOwnershipRecord> files,
        ImmutableArray<OutputCreatedDirectoryOwnership> createdDirectories)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        SchemaVersion = schemaVersion;
        Files = ValidateDistinct(files, OutputLimits.MaximumIntegrityEntries, record => record.Path.CanonicalKey, nameof(files));
        CreatedDirectories = ValidateDistinct(
            createdDirectories,
            OutputLimits.MaximumInventoryDirectories,
            record => record.Path.CanonicalKey,
            nameof(createdDirectories));
    }

    public int SchemaVersion { get; }

    public ImmutableArray<OutputOwnershipRecord> Files { get; }

    public ImmutableArray<OutputCreatedDirectoryOwnership> CreatedDirectories { get; }

    public static OutputOwnershipInventory Empty { get; } = new(
        CurrentSchemaVersion,
        ImmutableArray<OutputOwnershipRecord>.Empty,
        ImmutableArray<OutputCreatedDirectoryOwnership>.Empty);

    private static ImmutableArray<T> ValidateDistinct<T>(
        IEnumerable<T> values,
        int maximumCount,
        Func<T, string> getKey,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        var builder = ImmutableArray.CreateBuilder<T>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null || !keys.Add(getKey(value)) || builder.Count == maximumCount)
            {
                throw new ArgumentException("An ownership inventory collection is invalid or out of bounds.", parameterName);
            }

            builder.Add(value);
        }

        return builder.ToImmutable();
    }
}

public sealed record OutputOwnershipInventorySnapshot
{
    public OutputOwnershipInventorySnapshot(
        OutputStateRevision revision,
        OutputOwnershipInventory inventory)
    {
        if (string.IsNullOrWhiteSpace(revision.Value))
        {
            throw new ArgumentException("An ownership snapshot requires a state revision.", nameof(revision));
        }

        Revision = revision;
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public OutputStateRevision Revision { get; }

    public OutputOwnershipInventory Inventory { get; }
}

public enum OutputIntegrityClassification
{
    BaseEquivalent = 1,
    KmOwnedCurrent = 2,
    KmOwnedStale = 3,
    Foreign = 4,
    Conflicted = 5,
    Interrupted = 6,
    Unknown = 7,
}

public sealed record OutputBaselineEntry(RelativeOutputPath Path, OutputFileState State);

public sealed record OutputIntegrityEntry(
    RelativeOutputPath Path,
    OutputIntegrityClassification Classification,
    OutputFileState? CurrentState,
    OutputFileState? ExpectedOwnedState);

public sealed record OutputIntegrityReport
{
    public OutputIntegrityReport(
        OutputStateRevision revision,
        IEnumerable<OutputIntegrityEntry> entries,
        DateTimeOffset scannedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrWhiteSpace(revision.Value))
        {
            throw new ArgumentException("An integrity report requires a state revision.", nameof(revision));
        }

        Revision = revision;
        Entries = entries.ToImmutableArray();
        if (Entries.Length > OutputLimits.MaximumIntegrityEntries
            || Entries.Any(entry => entry is null)
            || scannedAtUtc == default)
        {
            throw new ArgumentException("An output integrity report is invalid or out of bounds.");
        }

        ScannedAtUtc = scannedAtUtc.ToUniversalTime();
    }

    public OutputStateRevision Revision { get; }

    public ImmutableArray<OutputIntegrityEntry> Entries { get; }

    public DateTimeOffset ScannedAtUtc { get; }

    public bool HasBlockingState => Entries.Any(entry => entry.Classification is
        OutputIntegrityClassification.Conflicted
        or OutputIntegrityClassification.Interrupted
        or OutputIntegrityClassification.Unknown);
}

public enum OutputCleanupDisposition
{
    Removed = 1,
    NotOwned = 2,
    FingerprintMismatch = 3,
    Missing = 4,
    ApplyNotCommitted = 5,
    ForgotMissing = 6,
}

public sealed record OutputCleanupEntry(RelativeOutputPath Path, OutputCleanupDisposition Disposition);

public sealed record OutputCleanupResult
{
    public OutputCleanupResult(IEnumerable<OutputCleanupEntry> entries, OutputApplyResult? applyResult)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries.ToImmutableArray();
        if (Entries.Length > OutputLimits.MaximumMutationsPerApply || Entries.Any(entry => entry is null))
        {
            throw new ArgumentException("A cleanup result is invalid or out of bounds.", nameof(entries));
        }

        ApplyResult = applyResult;
    }

    public ImmutableArray<OutputCleanupEntry> Entries { get; }

    public OutputApplyResult? ApplyResult { get; }
}
