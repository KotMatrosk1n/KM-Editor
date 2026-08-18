// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Output;

public enum OutputMutationKind
{
    Write = 1,
    Delete = 2,
}

/// <summary>
/// A normalized whole-file mutation. Game-owned compilers remain responsible for
/// preserving unowned records and byte ranges before producing the postimage.
/// </summary>
public sealed record OutputMutation
{
    private OutputMutation(
        OutputMutationKind kind,
        RelativeOutputPath path,
        ImmutableArray<byte> postimage,
        OutputFileState expectedPreimage,
        OutputFileState plannedPostimage,
        ImmutableArray<OwnedTarget> ownershipClaims,
        string? ownershipOutputMode,
        bool? restoredFileDeleteEligibility)
    {
        Kind = kind;
        Path = path;
        Postimage = postimage;
        ExpectedPreimage = expectedPreimage;
        PlannedPostimage = plannedPostimage;
        OwnershipClaims = ownershipClaims;
        OwnershipOutputMode = ownershipOutputMode;
        RestoredFileDeleteEligibility = restoredFileDeleteEligibility;
    }

    public OutputMutationKind Kind { get; }

    public RelativeOutputPath Path { get; }

    public ImmutableArray<byte> Postimage { get; }

    public OutputFileState ExpectedPreimage { get; }

    public OutputFileState PlannedPostimage { get; }

    public ImmutableArray<OwnedTarget> OwnershipClaims { get; }

    /// <summary>
    /// Optional per-file provenance used when restoring a checkpoint containing
    /// files originally produced by different output modes.
    /// </summary>
    public string? OwnershipOutputMode { get; }

    /// <summary>
    /// A checkpoint-only eligibility value carried through the durable journal.
    /// Ordinary callers cannot assert cleanup provenance through this field.
    /// </summary>
    internal bool? RestoredFileDeleteEligibility { get; }

    public static OutputMutation Write(
        RelativeOutputPath path,
        ReadOnlyMemory<byte> postimage,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        string? ownershipOutputMode = null)
    {
        return CreateWrite(
            path,
            postimage,
            expectedPreimage,
            ownershipClaims,
            ownershipOutputMode,
            restoredFileDeleteEligibility: null);
    }

    internal static OutputMutation WriteCheckpointRestore(
        RelativeOutputPath path,
        ReadOnlyMemory<byte> postimage,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        string ownershipOutputMode,
        bool restoredFileDeleteEligibility)
    {
        return CreateWrite(
            path,
            postimage,
            expectedPreimage,
            ownershipClaims,
            ownershipOutputMode,
            restoredFileDeleteEligibility);
    }

    private static OutputMutation CreateWrite(
        RelativeOutputPath path,
        ReadOnlyMemory<byte> postimage,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        string? ownershipOutputMode,
        bool? restoredFileDeleteEligibility)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(expectedPreimage);
        if (postimage.Length > OutputLimits.MaximumWriteBytesPerMutation)
        {
            throw new ArgumentException(
                $"A single output postimage cannot exceed {OutputLimits.MaximumWriteBytesPerMutation} bytes.",
                nameof(postimage));
        }

        var immutablePostimage = ImmutableArray.Create(postimage.Span);
        var postimageHash = Convert.ToHexStringLower(SHA256.HashData(postimage.Span));
        var plannedPostimage = OutputFileState.Existing(postimageHash, postimage.Length);
        if (plannedPostimage == expectedPreimage)
        {
            throw new ArgumentException("An output write must change the exact target state.", nameof(postimage));
        }

        return new OutputMutation(
            OutputMutationKind.Write,
            path,
            immutablePostimage,
            expectedPreimage,
            plannedPostimage,
            ValidateOwnership(path, ownershipClaims, plannedPostimage.LengthBytes),
            ValidateOptionalOutputMode(ownershipOutputMode),
            restoredFileDeleteEligibility);
    }

    public static OutputMutation Delete(
        RelativeOutputPath path,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        string? ownershipOutputMode = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(expectedPreimage);
        if (!expectedPreimage.Exists)
        {
            throw new ArgumentException("An output delete requires an existing exact preimage.", nameof(expectedPreimage));
        }

        return new OutputMutation(
            OutputMutationKind.Delete,
            path,
            ImmutableArray<byte>.Empty,
            expectedPreimage,
            OutputFileState.Missing,
            ValidateOwnership(path, ownershipClaims, expectedPreimage.LengthBytes),
            ValidateOptionalOutputMode(ownershipOutputMode),
            restoredFileDeleteEligibility: null);
    }

    private static string? ValidateOptionalOutputMode(string? outputMode)
    {
        return outputMode is null
            ? null
            : SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
    }

    private static ImmutableArray<OwnedTarget> ValidateOwnership(
        RelativeOutputPath path,
        IEnumerable<OwnedTarget> ownershipClaims,
        long maximumAddressableLength)
    {
        ArgumentNullException.ThrowIfNull(ownershipClaims);
        var builder = ImmutableArray.CreateBuilder<OwnedTarget>();
        var seen = new HashSet<OwnedTarget>();
        foreach (var claim in ownershipClaims)
        {
            if (claim is null)
            {
                throw new ArgumentException("An output mutation cannot contain a null ownership claim.", nameof(ownershipClaims));
            }

            if (claim.Address.File != path)
            {
                throw new ArgumentException(
                    "Every ownership claim must address the mutation's normalized file path.",
                    nameof(ownershipClaims));
            }

            if (claim.Address.ByteRange is { } range
                && range.EndExclusive > maximumAddressableLength)
            {
                throw new ArgumentException(
                    "An owned byte range cannot extend beyond the exact file image.",
                    nameof(ownershipClaims));
            }

            if (!seen.Add(claim))
            {
                throw new ArgumentException("An output mutation cannot contain duplicate ownership claims.", nameof(ownershipClaims));
            }

            if (builder.Count == OutputLimits.MaximumOwnershipClaimsPerMutation)
            {
                throw new ArgumentException(
                    $"An output mutation cannot contain more than {OutputLimits.MaximumOwnershipClaimsPerMutation} ownership claims.",
                    nameof(ownershipClaims));
            }

            builder.Add(claim);
        }

        if (builder.Count == 0)
        {
            throw new ArgumentException("An output mutation requires at least one ownership claim.", nameof(ownershipClaims));
        }

        return builder.ToImmutable();
    }
}

public enum OutputApplyOriginKind
{
    Workflow = 1,
    ChangeSet = 2,
    Recipe = 3,
    Importer = 4,
    Generator = 5,
    Cleanup = 6,
    Checkpoint = 7,
}

public sealed record OutputApplyOrigin
{
    public OutputApplyOrigin(OutputApplyOriginKind kind, string id)
    {
        Kind = SemanticContractGuards.DefinedEnum(kind, nameof(kind));
        Id = SemanticContractGuards.StableId(id, nameof(id));
    }

    public OutputApplyOriginKind Kind { get; }

    public string Id { get; }
}

public sealed record OutputReadDependency(
    RelativeOutputPath Path,
    OutputFileState ExpectedState)
{
    public RelativeOutputPath Path { get; } = Path ?? throw new ArgumentNullException(nameof(Path));

    public OutputFileState ExpectedState { get; } =
        ExpectedState ?? throw new ArgumentNullException(nameof(ExpectedState));
}

public sealed record OutputDirectoryMembershipDependency
{
    public OutputDirectoryMembershipDependency(
        RelativeOutputPath directory,
        OutputStateRevision expectedRevision)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        if (string.IsNullOrWhiteSpace(expectedRevision.Value))
        {
            throw new ArgumentException(
                "A directory dependency requires a membership revision.",
                nameof(expectedRevision));
        }

        ExpectedRevision = expectedRevision;
    }

    public RelativeOutputPath Directory { get; }

    public OutputStateRevision ExpectedRevision { get; }
}

public sealed record OutputDirectoryMembershipEntry(
    RelativeOutputPath Path,
    bool IsDirectory);

public sealed record OutputDirectoryMembershipSnapshot
{
    public OutputDirectoryMembershipSnapshot(
        RelativeOutputPath directory,
        bool exists,
        OutputStateRevision revision,
        IEnumerable<OutputDirectoryMembershipEntry> entries)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        if (string.IsNullOrWhiteSpace(revision.Value))
        {
            throw new ArgumentException(
                "A directory snapshot requires a membership revision.",
                nameof(revision));
        }

        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToImmutableArray();
        if (materialized.Length > OutputLimits.MaximumIntegrityEntries
            || !exists && !materialized.IsEmpty
            || materialized.Any(entry => entry is null
                                         || entry.Path is null
                                         || !IsStrictDescendant(directory, entry.Path))
            || materialized.Select(entry => entry.Path.CanonicalKey)
                .Distinct(StringComparer.Ordinal)
                .Count() != materialized.Length)
        {
            throw new ArgumentException(
                "A directory membership snapshot is invalid or out of bounds.",
                nameof(entries));
        }

        Exists = exists;
        Revision = revision;
        Entries = materialized;
    }

    public RelativeOutputPath Directory { get; }

    public bool Exists { get; }

    public OutputStateRevision Revision { get; }

    public ImmutableArray<OutputDirectoryMembershipEntry> Entries { get; }

    public OutputDirectoryMembershipDependency ToDependency() => new(Directory, Revision);

    private static bool IsStrictDescendant(
        RelativeOutputPath directory,
        RelativeOutputPath candidate)
    {
        return candidate.CanonicalKey.StartsWith(
            directory.CanonicalKey + "/",
            StringComparison.Ordinal);
    }
}

public sealed record OutputApplyPlan
{
    public OutputApplyPlan(
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        string semanticReviewHash,
        IEnumerable<OutputApplyOrigin> origins,
        IEnumerable<OutputMutation> mutations,
        IEnumerable<OutputReadDependency>? readDependencies = null,
        IEnumerable<OutputDirectoryMembershipDependency>? directoryMembershipDependencies = null)
    {
        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        ProjectId = projectId;
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        OutputMode = SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        SemanticReviewHash = SemanticContractGuards.Sha256Fingerprint(
            semanticReviewHash,
            nameof(semanticReviewHash));
        Origins = ValidateOrigins(origins);
        Mutations = ValidateMutations(gameFamily, mutations);
        ReadDependencies = ValidateDependencies(
            readDependencies,
            dependency => dependency.Path.CanonicalKey,
            nameof(readDependencies));
        DirectoryMembershipDependencies = ValidateDependencies(
            directoryMembershipDependencies,
            dependency => dependency.Directory.CanonicalKey,
            nameof(directoryMembershipDependencies));
    }

    public ProjectId ProjectId { get; }

    public GameFamily GameFamily { get; }

    public string OutputMode { get; }

    public string SemanticReviewHash { get; }

    public ImmutableArray<OutputApplyOrigin> Origins { get; }

    public ImmutableArray<OutputMutation> Mutations { get; }

    public ImmutableArray<OutputReadDependency> ReadDependencies { get; }

    public ImmutableArray<OutputDirectoryMembershipDependency> DirectoryMembershipDependencies { get; }

    private static ImmutableArray<OutputApplyOrigin> ValidateOrigins(IEnumerable<OutputApplyOrigin> origins)
    {
        ArgumentNullException.ThrowIfNull(origins);
        var builder = ImmutableArray.CreateBuilder<OutputApplyOrigin>();
        var seen = new HashSet<OutputApplyOrigin>();
        foreach (var origin in origins)
        {
            if (origin is null || !seen.Add(origin))
            {
                throw new ArgumentException("Output apply origins must be non-null and distinct.", nameof(origins));
            }

            if (builder.Count == OutputLimits.MaximumOriginsPerApply)
            {
                throw new ArgumentException(
                    $"An output apply cannot declare more than {OutputLimits.MaximumOriginsPerApply} origins.",
                    nameof(origins));
            }

            builder.Add(origin);
        }

        if (builder.Count == 0)
        {
            throw new ArgumentException("An output apply requires at least one origin.", nameof(origins));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<OutputMutation> ValidateMutations(
        GameFamily gameFamily,
        IEnumerable<OutputMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        var builder = ImmutableArray.CreateBuilder<OutputMutation>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        long totalWriteBytes = 0;
        foreach (var mutation in mutations)
        {
            if (mutation is null)
            {
                throw new ArgumentException("An output apply cannot contain a null mutation.", nameof(mutations));
            }

            if (!paths.Add(mutation.Path.CanonicalKey))
            {
                throw new ArgumentException("An output apply can mutate each normalized file only once.", nameof(mutations));
            }

            if (mutation.OwnershipClaims.Any(claim => claim.GameFamily != gameFamily))
            {
                throw new ArgumentException(
                    "Every output ownership claim must belong to the apply plan's game family.",
                    nameof(mutations));
            }

            totalWriteBytes = checked(totalWriteBytes + mutation.Postimage.Length);
            if (totalWriteBytes > OutputLimits.MaximumWriteBytesPerApply)
            {
                throw new ArgumentException(
                    $"An output apply cannot carry more than {OutputLimits.MaximumWriteBytesPerApply} postimage bytes.",
                    nameof(mutations));
            }

            if (builder.Count == OutputLimits.MaximumMutationsPerApply)
            {
                throw new ArgumentException(
                    $"An output apply cannot contain more than {OutputLimits.MaximumMutationsPerApply} mutations.",
                    nameof(mutations));
            }

            builder.Add(mutation);
        }

        if (builder.Count == 0)
        {
            throw new ArgumentException("An output apply requires at least one mutation.", nameof(mutations));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<T> ValidateDependencies<T>(
        IEnumerable<T>? dependencies,
        Func<T, string> getKey,
        string parameterName)
        where T : class
    {
        if (dependencies is null)
        {
            return ImmutableArray<T>.Empty;
        }

        var result = ImmutableArray.CreateBuilder<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in dependencies)
        {
            if (dependency is null || !seen.Add(getKey(dependency)))
            {
                throw new ArgumentException(
                    "Output dependencies must be non-null and distinct.",
                    parameterName);
            }

            if (result.Count == OutputLimits.MaximumIntegrityEntries)
            {
                throw new ArgumentException(
                    "The output dependency collection is out of bounds.",
                    parameterName);
            }

            result.Add(dependency);
        }

        return result.ToImmutable();
    }
}
