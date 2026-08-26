// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.DynamaxAdventures;

internal static class SwShDynamaxAdventuresDiagnosticCodes
{
    public const string TableLayoutMismatch = "KM-SWSH-DYNAMAX-ADVENTURES-TABLE-LAYOUT-MISMATCH";
    public const string RowApiDomainInvalid = "KM-SWSH-DYNAMAX-ADVENTURES-ROW-API-DOMAIN-INVALID";
    public const string RowFormUnresolved = "KM-SWSH-DYNAMAX-ADVENTURES-ROW-FORM-UNRESOLVED";
    public const string HiddenRowChanged = "KM-SWSH-DYNAMAX-ADVENTURES-HIDDEN-ROW-CHANGED";
    public const string OptionsIncomplete = "KM-SWSH-DYNAMAX-ADVENTURES-OPTIONS-INCOMPLETE";
    public const string SpeciesOptionsMissing = "KM-SWSH-DYNAMAX-ADVENTURES-SPECIES-OPTIONS-MISSING";
    public const string PersonalDataMissing = "KM-SWSH-DYNAMAX-ADVENTURES-PERSONAL-DATA-MISSING";
    public const string MoveDataMissing = "KM-SWSH-DYNAMAX-ADVENTURES-MOVE-DATA-MISSING";
    public const string LearnsetDataMissing = "KM-SWSH-DYNAMAX-ADVENTURES-LEARNSET-DATA-MISSING";
    public const string SeedBoundsInvalid = "KM-SWSH-DYNAMAX-ADVENTURES-SEED-BOUNDS-INVALID";
    public const string ProjectUnsupported = "KM-SWSH-DYNAMAX-ADVENTURES-PROJECT-UNSUPPORTED";
    public const string SourceUnavailable = "KM-SWSH-DYNAMAX-ADVENTURES-SOURCE-UNAVAILABLE";
    public const string SourceUnsupported = "KM-SWSH-DYNAMAX-ADVENTURES-SOURCE-UNSUPPORTED";
    public const string LayoutUnsupported = "KM-SWSH-DYNAMAX-ADVENTURES-LAYOUT-UNSUPPORTED";
    public const string SavePreimageStale = "KM-SWSH-DYNAMAX-ADVENTURES-SAVE-PREIMAGE-STALE";
    public const string VerificationFailed = "KM-SWSH-DYNAMAX-ADVENTURES-VERIFICATION-FAILED";
    public const string RecoveryRequired = "KM-SWSH-DYNAMAX-ADVENTURES-RECOVERY-REQUIRED";
    public const string IoFailed = "KM-SWSH-DYNAMAX-ADVENTURES-IO-FAILED";

    public static bool IsTableRestoreTrigger(string? code)
    {
        return code is TableLayoutMismatch
            or RowApiDomainInvalid
            or RowFormUnresolved
            or HiddenRowChanged;
    }

    public static bool IsTableRestoreCompatible(string? code)
    {
        return IsTableRestoreTrigger(code)
            || code is OptionsIncomplete
                or SpeciesOptionsMissing
                or PersonalDataMissing
                or MoveDataMissing
                or LearnsetDataMissing;
    }
}
