// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Api.ModMerger;

public static class MergeWorkspaceErrorCodes
{
    public const string ArchiveUnreadable = "KM-MERGE-ARCHIVE-UNREADABLE";
    public const string BaseMissing = "KM-MERGE-BASE-MISSING";
    public const string DescriptorMismatch = "KM-MERGE-DESCRIPTOR-MISMATCH";
    public const string DescriptorRequired = "KM-MERGE-DESCRIPTOR-REQUIRED";
    public const string DuplicatePath = "KM-MERGE-DUPLICATE-PATH";
    public const string PackagesRequired = "KM-MERGE-PACKAGES-REQUIRED";
    public const string PackagesStale = "KM-MERGE-PACKAGES-STALE";
    public const string PackageManifestInvalid = "KM-MERGE-PACKAGE-MANIFEST-INVALID";
    public const string PackageSelectionInvalid = "KM-MERGE-PACKAGE-SELECTION-INVALID";
    public const string ExportFailed = "KM-MERGE-EXPORT-FAILED";
    public const string FormatOpaque = "KM-MERGE-FORMAT-OPAQUE";
    public const string SemanticInvalid = "KM-MERGE-SEMANTIC-INVALID";
    public const string PatchInvalid = "KM-MERGE-PATCH-INVALID";
    public const string ExecutableBaseRequired = "KM-MERGE-EXECUTABLE-BASE-REQUIRED";
    public const string GameMismatch = "KM-MERGE-GAME-MISMATCH";
    public const string GameUnknown = "KM-MERGE-GAME-UNKNOWN";
    public const string LimitExceeded = "KM-MERGE-LIMIT-EXCEEDED";
    public const string MixedGames = "KM-MERGE-MIXED-GAMES";
    public const string OutputConflict = "KM-MERGE-OUTPUT-CONFLICT";
    public const string OutputRecovery = "KM-MERGE-OUTPUT-RECOVERY";
    public const string OutputUnavailable = "KM-MERGE-OUTPUT-UNAVAILABLE";
    public const string PackedLayout = "KM-MERGE-PACKED-LAYOUT";
    public const string PackedOverlap = "KM-MERGE-PACKED-OVERLAP";
    public const string PathUnsafe = "KM-MERGE-PATH-UNSAFE";
    public const string PickerFailed = "KM-MERGE-PICKER-FAILED";
    public const string RequestInvalid = "KM-MERGE-REQUEST-INVALID";
    public const string ReviewStale = "KM-MERGE-REVIEW-STALE";
    public const string SupportRequired = "KM-MERGE-SUPPORT-REQUIRED";
    public const string VanillaRequired = "KM-MERGE-VANILLA-REQUIRED";
}
