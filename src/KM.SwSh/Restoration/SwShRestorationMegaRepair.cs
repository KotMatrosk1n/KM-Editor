// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;

namespace KM.SwSh.Restoration;

/// <summary>Repairs the omitted Mega classification without replacing installed gameplay edits.</summary>
internal static class SwShRestorationMegaRepair
{
    internal static bool Applies(SwShPreparedRestoration previous, SwShPreparedRestoration next) =>
        previous.DefinitionId == "baxcalibur-mega" && previous.AssetRevision < 5 && IsCorrected(next);

    private static bool IsCorrected(SwShPreparedRestoration package) =>
        package.DefinitionId == "baxcalibur-mega" && package.AssetRevision >= 5;

    internal static bool IncludesItem(SwShPreparedRestoration package, SwShRestorationContribution item) =>
        IsCorrected(package) && SwShRestorationMegaItems.IsItemContribution(item);

    internal static bool IncludesPath(SwShPreparedRestoration package, string path) =>
        IsCorrected(package) && package.Contributions.Any(item => item.Path == path && IncludesItem(package, item));

    internal static bool IsPersonal(SwShRestorationContribution item) =>
        item.Writer == SwShRestorationWriter.Personal && item.Key == "1534";

    internal static void CorrectPersonal(SwShPreparedRestoration previous, SwShPreparedRestoration next,
        SwShRestorationContribution original, SwShRestorationContribution replacement, byte[] current)
    {
        if (!Applies(previous, next) || !IsPersonal(original)) return;
        if (original.After.Length != SwShPersonalTable.RecordSize || replacement.After.Length != SwShPersonalTable.RecordSize
            || current.Length != SwShPersonalTable.RecordSize)
            throw new InvalidDataException("Mega form personal data has an invalid record size.");
        // Only clear the erroneous regional bit. Other flags and gameplay fields remain owned edits.
        if ((original.After[0x5a] & 1) != 0 && (replacement.After[0x5a] & 1) == 0)
            current[0x5a] &= 0xfe;
    }
}
