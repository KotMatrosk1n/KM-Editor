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
