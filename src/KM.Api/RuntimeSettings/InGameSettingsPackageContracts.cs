// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Output;

namespace KM.Api.RuntimeSettings;

public static class InGameSettingsPackageContract
{
    public const int MaximumCachedReviews = 16;
    public const int MaximumExecutableInputReasonCodeLength = 96;
    public const int MaximumOwnedCompositionRegionCount = 4096;
    public const int MaximumReturnedReadDependencies = 16;
    public const int MaximumReviewIdLength = 64;
    public const int MaximumReturnedTargets = 512;
}

public enum InGameSettingsPackageStateDto
{
    Unavailable,
    NotInstalled,
    Installed,
    UpgradeAvailable,
    CoexistenceConflict,
    Incomplete,
    Unmanaged,
    Conflict,
    Corrupt,
}

public enum InGameSettingsPackageOperationDto
{
    Install,
    Upgrade,
    Remove,
}

public enum InGameSettingsPackageTargetOperationDto
{
    Write,
    Delete,
}

public enum InGameSettingsExecutableInputSourceDto
{
    None,
    Base,
    StandaloneOutput,
}

public enum InGameSettingsExecutableCompatibilityDto
{
    Absent,
    RetailEquivalent,
    CompatiblePreservable,
    IncompatibleOwnedRegion,
    UnsupportedBuild,
    UnreadableOrAmbiguous,
}

public enum InGameSettingsPackageReadDependencyRoleDto
{
    StaticExecutableGuard,
    ExecutableCompositionSource,
}

public enum InGameSettingsExecutableCompositionStrategyDto
{
    StockPackage,
    RetailEquivalentStandalone,
    CompatibleStandalone,
}

public sealed record InGameSettingsPackageVersionDto(
    uint Major,
    uint Minor,
    uint Patch);

public sealed record InGameSettingsPackageDescriptorDto(
    string TitleId,
    string SupportedGameVersion,
    string BuildId,
    InGameSettingsPackageVersionDto PackageVersion,
    string BundleId,
    string ArchiveSha256,
    int TargetCount);

public sealed record InGameSettingsExecutableInputAssessmentDto(
    InGameSettingsExecutableInputSourceDto Source,
    InGameSettingsExecutableCompatibilityDto Compatibility,
    string ReasonCode,
    string? SourceRelativePath,
    string? SourceSha256,
    long? SourceLengthBytes);

public sealed record InGameSettingsPackageSnapshotDto(
    InGameSettingsPackageStateDto State,
    bool BlocksStaticEditor,
    string Revision,
    bool PackageAvailable,
    InGameSettingsPackageDescriptorDto? InstalledPackage,
    InGameSettingsPackageDescriptorDto? AvailablePackage,
    InGameSettingsExecutableInputAssessmentDto ExecutableInput,
    string? Detail = null);

public sealed record InspectInGameSettingsPackageRequest(OutputScopeDto Scope);

public sealed record InspectInGameSettingsPackageResponse(
    InGameSettingsPackageSnapshotDto Snapshot);

public sealed record PreviewInGameSettingsPackageRequest(
    OutputScopeDto Scope,
    string ExpectedRevision,
    InGameSettingsPackageOperationDto Operation);

public sealed record InGameSettingsPackageTargetDto(
    string RelativePath,
    InGameSettingsPackageTargetOperationDto Operation);

public sealed record InGameSettingsPackageReadDependencyDto(
    string RelativePath,
    InGameSettingsPackageReadDependencyRoleDto Role,
    bool Exists,
    string? Sha256,
    long? LengthBytes,
    bool Preserved);

public sealed record InGameSettingsExecutableCompositionDto(
    InGameSettingsExecutableCompositionStrategyDto Strategy,
    string DestinationRelativePath,
    bool SourcePreserved,
    bool PreservesBytesOutsideOwnedRegions,
    int OwnedRegionCount);

public sealed record PreviewInGameSettingsPackageResponse(
    string ReviewId,
    DateTimeOffset ExpiresAtUtc,
    InGameSettingsPackageOperationDto Operation,
    InGameSettingsPackageSnapshotDto Before,
    IReadOnlyList<InGameSettingsPackageTargetDto> Targets,
    bool TargetsTruncated,
    IReadOnlyList<InGameSettingsPackageReadDependencyDto> ReadDependencies,
    bool ReadDependenciesTruncated,
    InGameSettingsExecutableCompositionDto? Composition);

public sealed record ApplyInGameSettingsPackageRequest(
    OutputScopeDto Scope,
    string ReviewId);

public enum InGameSettingsPackageApplyOutcomeDto
{
    Committed,
    RolledBack,
    RecoveryRequired,
}

public sealed record ApplyInGameSettingsPackageResponse(
    string TransactionId,
    InGameSettingsPackageApplyOutcomeDto Outcome,
    InGameSettingsPackageSnapshotDto? Snapshot);
