// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
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
        OwnershipOwnerId? ownershipActor,
        bool? restoredFileDeleteEligibility,
        OutputRuntimeMutableDescriptor? runtimeMutableDescriptor,
        OutputLegacyAdoptionDeleteAuthority? legacyAdoptionDeleteAuthority,
        OutputVerifiedBaseDeleteAuthority? verifiedBaseDeleteAuthority)
    {
        Kind = kind;
        Path = path;
        Postimage = postimage;
        ExpectedPreimage = expectedPreimage;
        PlannedPostimage = plannedPostimage;
        OwnershipClaims = ownershipClaims;
        OwnershipOutputMode = ownershipOutputMode;
        OwnershipActor = ownershipActor;
        RestoredFileDeleteEligibility = restoredFileDeleteEligibility;
        RuntimeMutableDescriptor = runtimeMutableDescriptor;
        LegacyAdoptionDeleteAuthority = legacyAdoptionDeleteAuthority;
        VerifiedBaseDeleteAuthority = verifiedBaseDeleteAuthority;
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
    /// Optional owner authorized to add or release only its own claims during
    /// one ordinary static composed write. Foreign claims remain immutable.
    /// </summary>
    public OwnershipOwnerId? OwnershipActor { get; }

    /// <summary>
    /// A checkpoint-only eligibility value carried through the durable journal.
    /// Ordinary callers cannot assert cleanup provenance through this field.
    /// </summary>
    internal bool? RestoredFileDeleteEligibility { get; }

    /// <summary>
    /// Present only for an exact, title-scoped file whose valid generation may
    /// advance outside the editor after the reviewed transaction commits.
    /// </summary>
    public OutputRuntimeMutableDescriptor? RuntimeMutableDescriptor { get; }

    /// <summary>
    /// Explicit authority for deleting one exact legacy output that predates the
    /// ownership inventory. Ordinary unowned deletes remain prohibited.
    /// </summary>
    public OutputLegacyAdoptionDeleteAuthority? LegacyAdoptionDeleteAuthority { get; }

    /// <summary>
    /// Explicit authority for removing a shared static output only after a
    /// game-specific verifier proved that removing it exposes the held base file.
    /// </summary>
    public OutputVerifiedBaseDeleteAuthority? VerifiedBaseDeleteAuthority { get; }

    public static OutputMutation Write(
        RelativeOutputPath path,
        ReadOnlyMemory<byte> postimage,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        string? ownershipOutputMode = null,
        OwnershipOwnerId? ownershipActor = null)
    {
        return CreateWrite(
            path,
            postimage,
            expectedPreimage,
            ownershipClaims,
            ownershipOutputMode,
            ownershipActor,
            restoredFileDeleteEligibility: null,
            runtimeMutableDescriptor: null,
            legacyAdoptionDeleteAuthority: null,
            verifiedBaseDeleteAuthority: null);
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
            ownershipActor: null,
            restoredFileDeleteEligibility,
            runtimeMutableDescriptor: null,
            legacyAdoptionDeleteAuthority: null,
            verifiedBaseDeleteAuthority: null);
    }

    public static OutputMutation WriteRuntimeMutableBootstrap(
        RelativeOutputPath path,
        ReadOnlyMemory<byte> postimage,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        GameFamily gameFamily,
        ulong titleId,
        string? ownershipOutputMode = null)
    {
        ArgumentNullException.ThrowIfNull(expectedPreimage);
        if (expectedPreimage.Exists)
        {
            throw new ArgumentException(
                "A runtime-mutable bootstrap requires a missing reviewed preimage.",
                nameof(expectedPreimage));
        }

        var descriptor = OutputRuntimeMutableDescriptor.ValidateBootstrap(
            path,
            gameFamily,
            titleId,
            postimage.Span);
        return CreateWrite(
            path,
            postimage,
            expectedPreimage,
            ownershipClaims,
            ownershipOutputMode,
            ownershipActor: null,
            restoredFileDeleteEligibility: null,
            descriptor,
            legacyAdoptionDeleteAuthority: null,
            verifiedBaseDeleteAuthority: null);
    }

    public static OutputMutation WriteRuntimeMutableTransition(
        RelativeOutputPath path,
        ReadOnlyMemory<byte> reviewedPreimage,
        ReadOnlyMemory<byte> postimage,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        GameFamily gameFamily,
        ulong titleId,
        string? ownershipOutputMode = null)
    {
        ArgumentNullException.ThrowIfNull(expectedPreimage);
        if (!expectedPreimage.Exists || ComputeState(reviewedPreimage.Span) != expectedPreimage)
        {
            throw new ArgumentException(
                "A runtime-mutable update requires the exact reviewed preimage bytes.",
                nameof(reviewedPreimage));
        }

        var descriptor = OutputRuntimeMutableDescriptor.ValidateTransition(
            path,
            gameFamily,
            titleId,
            reviewedPreimage.Span,
            postimage.Span);
        return CreateWrite(
            path,
            postimage,
            expectedPreimage,
            ownershipClaims,
            ownershipOutputMode,
            ownershipActor: null,
            restoredFileDeleteEligibility: null,
            descriptor,
            legacyAdoptionDeleteAuthority: null,
            verifiedBaseDeleteAuthority: null);
    }

    private static OutputMutation CreateWrite(
        RelativeOutputPath path,
        ReadOnlyMemory<byte> postimage,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        string? ownershipOutputMode,
        OwnershipOwnerId? ownershipActor,
        bool? restoredFileDeleteEligibility,
        OutputRuntimeMutableDescriptor? runtimeMutableDescriptor,
        OutputLegacyAdoptionDeleteAuthority? legacyAdoptionDeleteAuthority,
        OutputVerifiedBaseDeleteAuthority? verifiedBaseDeleteAuthority)
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
            ownershipActor,
            restoredFileDeleteEligibility,
            runtimeMutableDescriptor,
            legacyAdoptionDeleteAuthority,
            verifiedBaseDeleteAuthority);
    }

    public static OutputMutation Delete(
        RelativeOutputPath path,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        string? ownershipOutputMode = null)
    {
        return CreateDelete(
            path,
            expectedPreimage,
            ownershipClaims,
            ownershipOutputMode,
            runtimeMutableDescriptor: null,
            legacyAdoptionDeleteAuthority: null,
            verifiedBaseDeleteAuthority: null);
    }

    public static OutputMutation DeleteLegacyAdoption(
        RelativeOutputPath path,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        OutputLegacyAdoptionDeleteAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return CreateDelete(
            path,
            expectedPreimage,
            ownershipClaims,
            authority.OutputMode,
            runtimeMutableDescriptor: null,
            legacyAdoptionDeleteAuthority: authority,
            verifiedBaseDeleteAuthority: null);
    }

    public static OutputMutation DeleteVerifiedBase(
        RelativeOutputPath path,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        OutputVerifiedBaseDeleteAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return CreateDelete(
            path,
            expectedPreimage,
            ownershipClaims,
            authority.OutputMode,
            runtimeMutableDescriptor: null,
            legacyAdoptionDeleteAuthority: null,
            verifiedBaseDeleteAuthority: authority);
    }

    public static OutputMutation DeleteRuntimeMutable(
        RelativeOutputPath path,
        ReadOnlyMemory<byte> reviewedPreimage,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        GameFamily gameFamily,
        ulong titleId,
        string? ownershipOutputMode = null)
    {
        ArgumentNullException.ThrowIfNull(expectedPreimage);
        if (!expectedPreimage.Exists || ComputeState(reviewedPreimage.Span) != expectedPreimage)
        {
            throw new ArgumentException(
                "A runtime-mutable delete requires the exact reviewed preimage bytes.",
                nameof(reviewedPreimage));
        }

        var descriptor = OutputRuntimeMutableDescriptor.ValidateExplicitDeletion(
            path,
            gameFamily,
            titleId,
            reviewedPreimage.Span);
        return CreateDelete(
            path,
            expectedPreimage,
            ownershipClaims,
            ownershipOutputMode,
            descriptor,
            legacyAdoptionDeleteAuthority: null,
            verifiedBaseDeleteAuthority: null);
    }

    private static OutputMutation CreateDelete(
        RelativeOutputPath path,
        OutputFileState expectedPreimage,
        IEnumerable<OwnedTarget> ownershipClaims,
        string? ownershipOutputMode,
        OutputRuntimeMutableDescriptor? runtimeMutableDescriptor,
        OutputLegacyAdoptionDeleteAuthority? legacyAdoptionDeleteAuthority,
        OutputVerifiedBaseDeleteAuthority? verifiedBaseDeleteAuthority)
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
            ownershipActor: null,
            restoredFileDeleteEligibility: null,
            runtimeMutableDescriptor,
            legacyAdoptionDeleteAuthority,
            verifiedBaseDeleteAuthority);
    }

    private static OutputFileState ComputeState(ReadOnlySpan<byte> bytes)
    {
        return OutputFileState.Existing(
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bytes.Length);
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

public sealed record OutputLegacyAdoptionDeleteAuthority
{
    public OutputLegacyAdoptionDeleteAuthority(
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        RelativeOutputPath target,
        OwnershipOwnerId ownerId,
        PreservationRuleDescriptor preservationRule,
        OutputFileState reviewedPreimage)
    {
        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        ProjectId = projectId;
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        OutputMode = SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        OwnerId = ownerId ?? throw new ArgumentNullException(nameof(ownerId));
        PreservationRule = preservationRule ?? throw new ArgumentNullException(nameof(preservationRule));
        ReviewedPreimage = reviewedPreimage ?? throw new ArgumentNullException(nameof(reviewedPreimage));
        if (!ReviewedPreimage.Exists)
        {
            throw new ArgumentException(
                "Legacy output deletion authority requires an existing reviewed preimage.",
                nameof(reviewedPreimage));
        }
    }

    public ProjectId ProjectId { get; }

    public GameFamily GameFamily { get; }

    public string OutputMode { get; }

    public RelativeOutputPath Target { get; }

    public OwnershipOwnerId OwnerId { get; }

    public PreservationRuleDescriptor PreservationRule { get; }

    public OutputFileState ReviewedPreimage { get; }

    internal void ValidateBinding(
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        OutputMutationKind kind,
        RelativeOutputPath target,
        OutputFileState expectedPreimage,
        IReadOnlyList<OwnedTarget> ownershipClaims)
    {
        if (ProjectId != projectId
            || GameFamily != gameFamily
            || !string.Equals(OutputMode, outputMode, StringComparison.Ordinal)
            || kind != OutputMutationKind.Delete
            || Target != target
            || ReviewedPreimage != expectedPreimage
            || ownershipClaims.Count != 1)
        {
            throw new ArgumentException("Legacy output deletion authority does not match its apply binding.");
        }

        var claim = ownershipClaims[0];
        if (claim.GameFamily != GameFamily
            || claim.Address.File != Target
            || claim.Address.ScopeKind != OwnedTargetScopeKind.File
            || claim.OwnerId != OwnerId
            || claim.PreservationRule != PreservationRule)
        {
            throw new ArgumentException(
                "Legacy output deletion authority requires one exact whole-file ownership claim.");
        }
    }

}

public sealed record OutputVerifiedBaseDeleteAuthority
{
    public OutputVerifiedBaseDeleteAuthority(
        ProjectId projectId,
        GameFamily gameFamily,
        OwnershipOwnerId actingOwnerId,
        string outputMode,
        RelativeOutputPath target,
        OutputFileState reviewedPreimage,
        OutputFileState verifiedBaseState,
        IEnumerable<OwnedTarget> ownershipClaims)
    {
        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        ProjectId = projectId;
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        ArgumentNullException.ThrowIfNull(actingOwnerId);
        _ = SemanticContractGuards.StableId(actingOwnerId.Value, nameof(actingOwnerId));
        ActingOwnerId = actingOwnerId;
        OutputMode = SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        ReviewedPreimage = reviewedPreimage ?? throw new ArgumentNullException(nameof(reviewedPreimage));
        VerifiedBaseState = verifiedBaseState ?? throw new ArgumentNullException(nameof(verifiedBaseState));
        if (!ReviewedPreimage.Exists || !VerifiedBaseState.Exists)
        {
            throw new ArgumentException(
                "Verified-base deletion authority requires existing reviewed output and base states.");
        }

        ArgumentNullException.ThrowIfNull(ownershipClaims);
        var claims = ownershipClaims.ToImmutableArray();
        if (claims.IsEmpty
            || claims.Length > OutputLimits.MaximumOwnershipClaimsPerMutation
            || claims.Any(claim => claim is null
                                   || claim.GameFamily != GameFamily
                                   || claim.Address.File != Target)
            || !claims.Any(claim => claim.OwnerId == ActingOwnerId
                                    && !OutputCreatorProvenance.IsClaim(claim))
            || claims.Any(claim => claim.OwnerId != ActingOwnerId
                                   && !OutputCreatorProvenance.IsClaim(claim))
            || claims.Distinct().Count() != claims.Length)
        {
            throw new ArgumentException(
                "Verified-base deletion authority requires one acting editor and provenance-only foreign claims.",
                nameof(ownershipClaims));
        }

        OwnershipClaims = claims;
    }

    public ProjectId ProjectId { get; }

    public GameFamily GameFamily { get; }

    public OwnershipOwnerId ActingOwnerId { get; }

    public string OutputMode { get; }

    public RelativeOutputPath Target { get; }

    public OutputFileState ReviewedPreimage { get; }

    public OutputFileState VerifiedBaseState { get; }

    public ImmutableArray<OwnedTarget> OwnershipClaims { get; }

    internal void ValidateBinding(
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        OutputMutationKind kind,
        RelativeOutputPath target,
        OutputFileState expectedPreimage,
        IReadOnlyList<OwnedTarget> ownershipClaims)
    {
        if (ProjectId != projectId
            || GameFamily != gameFamily
            || !string.Equals(OutputMode, outputMode, StringComparison.Ordinal)
            || kind != OutputMutationKind.Delete
            || Target != target
            || ReviewedPreimage != expectedPreimage
            || ownershipClaims.Count != OwnershipClaims.Length
            || !OwnershipClaims.All(ownershipClaims.Contains)
            || !ownershipClaims.Any(claim => claim.OwnerId == ActingOwnerId
                                              && !OutputCreatorProvenance.IsClaim(claim))
            || ownershipClaims.Any(claim => claim.OwnerId != ActingOwnerId
                                             && !OutputCreatorProvenance.IsClaim(claim)))
        {
            throw new ArgumentException(
                "Verified-base deletion authority does not match its apply binding.");
        }
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
        IEnumerable<OutputDirectoryMembershipDependency>? directoryMembershipDependencies = null,
        OutputStateRevision? ownershipInventoryRevision = null)
    {
        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));
        ProjectId = projectId;
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        OutputMode = SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        SemanticReviewHash = SemanticContractGuards.Sha256Fingerprint(
            semanticReviewHash,
            nameof(semanticReviewHash));
        Origins = ValidateOrigins(origins);
        Mutations = ValidateMutations(projectId, gameFamily, OutputMode, mutations);
        ReadDependencies = ValidateDependencies(
            readDependencies,
            dependency => dependency.Path.CanonicalKey,
            nameof(readDependencies));
        DirectoryMembershipDependencies = ValidateDependencies(
            directoryMembershipDependencies,
            dependency => dependency.Directory.CanonicalKey,
            nameof(directoryMembershipDependencies));
        if (ownershipInventoryRevision is { } revision
            && string.IsNullOrWhiteSpace(revision.Value))
        {
            throw new ArgumentException(
                "An ownership inventory revision dependency must be an exact non-empty revision.",
                nameof(ownershipInventoryRevision));
        }

        var ownershipActors = Mutations
            .Select(mutation => mutation.OwnershipActor)
            .Where(actor => actor is not null)
            .Select(actor => actor!)
            .Concat(Mutations
                .Select(mutation => mutation.VerifiedBaseDeleteAuthority?.ActingOwnerId)
                .Where(actor => actor is not null)
                .Select(actor => actor!))
            .Distinct()
            .ToArray();
        if (ownershipActors.Any(actor => !Origins.Any(origin =>
                origin.Kind == OutputApplyOriginKind.Workflow
                && string.Equals(origin.Id, actor.Value, StringComparison.Ordinal))))
        {
            throw new ArgumentException(
                "Every ownership actor transition requires a matching workflow authority origin.",
                nameof(origins));
        }

        if (ownershipInventoryRevision is null
            && (ownershipActors.Length > 0
                || Mutations.Any(mutation => mutation.VerifiedBaseDeleteAuthority is not null)))
        {
            throw new ArgumentException(
                "Ownership transitions and verified-base cleanup require the exact reviewed ownership inventory revision.",
                nameof(ownershipInventoryRevision));
        }

        OwnershipInventoryRevision = ownershipInventoryRevision;
    }

    public ProjectId ProjectId { get; }

    public GameFamily GameFamily { get; }

    public string OutputMode { get; }

    public string SemanticReviewHash { get; }

    public ImmutableArray<OutputApplyOrigin> Origins { get; }

    public ImmutableArray<OutputMutation> Mutations { get; }

    public ImmutableArray<OutputReadDependency> ReadDependencies { get; }

    public ImmutableArray<OutputDirectoryMembershipDependency> DirectoryMembershipDependencies { get; }

    /// <summary>
    /// Optional optimistic-concurrency dependency for the exact ownership
    /// inventory used while composing this plan's ownership claims.
    /// </summary>
    public OutputStateRevision? OwnershipInventoryRevision { get; }

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
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
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

            if (mutation.RuntimeMutableDescriptor is { } runtimeMutable)
            {
                runtimeMutable.ValidateIdentity(mutation.Path, gameFamily);
                var state = mutation.Kind == OutputMutationKind.Write
                    ? mutation.PlannedPostimage
                    : mutation.ExpectedPreimage;
                if (state.LengthBytes != GameplaySettingsJournal.JournalSize
                    || mutation.Kind == OutputMutationKind.Write && runtimeMutable.MinimumGeneration is null
                    || mutation.RestoredFileDeleteEligibility.HasValue
                    || !mutation.OwnershipClaims.Any(
                        claim => claim.Address.ScopeKind == OwnedTargetScopeKind.File))
                {
                    throw new ArgumentException(
                        "A runtime-mutable mutation has invalid state, generation, or ownership scope.",
                        nameof(mutations));
                }
            }

            if (mutation.OwnershipActor is not null
                && (mutation.Kind != OutputMutationKind.Write
                    || mutation.RuntimeMutableDescriptor is not null
                    || mutation.RestoredFileDeleteEligibility.HasValue
                    || mutation.LegacyAdoptionDeleteAuthority is not null))
            {
                throw new ArgumentException(
                    "An ownership actor is valid only for an ordinary static composed write.",
                    nameof(mutations));
            }

            if (mutation.LegacyAdoptionDeleteAuthority is { } legacyAdoption)
            {
                legacyAdoption.ValidateBinding(
                    projectId,
                    gameFamily,
                    outputMode,
                    mutation.Kind,
                    mutation.Path,
                    mutation.ExpectedPreimage,
                    mutation.OwnershipClaims);
                if (!string.Equals(mutation.OwnershipOutputMode, outputMode, StringComparison.Ordinal)
                    || mutation.RuntimeMutableDescriptor is not null
                    || mutation.RestoredFileDeleteEligibility.HasValue)
                {
                    throw new ArgumentException(
                        "Legacy output deletion authority cannot be combined with another mutation authority.",
                        nameof(mutations));
                }
            }

            if (mutation.VerifiedBaseDeleteAuthority is { } verifiedBaseDelete)
            {
                verifiedBaseDelete.ValidateBinding(
                    projectId,
                    gameFamily,
                    outputMode,
                    mutation.Kind,
                    mutation.Path,
                    mutation.ExpectedPreimage,
                    mutation.OwnershipClaims);
                if (!string.Equals(mutation.OwnershipOutputMode, outputMode, StringComparison.Ordinal)
                    || mutation.RuntimeMutableDescriptor is not null
                    || mutation.RestoredFileDeleteEligibility.HasValue
                    || mutation.LegacyAdoptionDeleteAuthority is not null
                    || mutation.OwnershipActor is not null)
                {
                    throw new ArgumentException(
                        "Verified-base deletion authority cannot be combined with another mutation authority.",
                        nameof(mutations));
                }
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
