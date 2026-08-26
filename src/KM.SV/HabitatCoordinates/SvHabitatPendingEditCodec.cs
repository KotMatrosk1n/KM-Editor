// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Formats.SV.Habitat;
using System.Globalization;

namespace KM.SV.HabitatCoordinates;

internal sealed record SvHabitatPendingMutation(
    string Region,
    string SourceRevision,
    SvHabitatCoordinateMutation Mutation);

internal static class SvHabitatPendingEditCodec
{
    public const string Domain = "workflow.habitatCoordinates";
    public const string Field = "coordinate";
    private const string RecordVersion = "v1";
    private const int MaximumRecordIdLength = 512;
    private const int MaximumCoordinateValueLength = 32;

    public static string CreateRecordId(string region, SvHabitatRowBinding binding)
    {
        return string.Join(
            ':',
            RecordVersion,
            region,
            binding.OuterGroupOccurrence.ToString(CultureInfo.InvariantCulture),
            binding.RowOccurrence.ToString(CultureInfo.InvariantCulture),
            binding.DevNo.ToString(CultureInfo.InvariantCulture),
            binding.FormNo.ToString(CultureInfo.InvariantCulture),
            binding.VersionA ? "1" : "0",
            binding.VersionB ? "1" : "0",
            binding.CurrentX.ToString(CultureInfo.InvariantCulture),
            binding.CurrentY.ToString(CultureInfo.InvariantCulture),
            binding.RowPreimageSha256,
            binding.SourceRevision);
    }

    public static string CreateValue(SvHabitatCoordinateChoice coordinate) =>
        string.Create(CultureInfo.InvariantCulture, $"{coordinate.X},{coordinate.Y}");

    public static bool TryDecode(PendingEdit edit, out SvHabitatPendingMutation value)
    {
        value = null!;
        if (!string.Equals(edit.Domain, Domain, StringComparison.Ordinal)
            || !string.Equals(edit.Field, Field, StringComparison.Ordinal)
            || edit.RecordId is null
            || edit.NewValue is null
            || edit.RecordId.Length > MaximumRecordIdLength
            || edit.NewValue.Length > MaximumCoordinateValueLength)
        {
            return false;
        }

        var parts = edit.RecordId.Split(':');
        var coordinateParts = edit.NewValue.Split(',');
        if (parts.Length != 12
            || coordinateParts.Length != 2
            || !string.Equals(parts[0], RecordVersion, StringComparison.Ordinal)
            || !SvHabitatCoordinatesProfiles.Regions.Any(profile => string.Equals(
                profile.Region,
                parts[1],
                StringComparison.Ordinal))
            || !TryNonNegativeInt(parts[2], out var groupOccurrence)
            || !TryNonNegativeInt(parts[3], out var rowOccurrence)
            || !int.TryParse(parts[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var devNo)
            || !int.TryParse(parts[5], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var formNo)
            || parts[6] is not ("0" or "1")
            || parts[7] is not ("0" or "1")
            || !int.TryParse(parts[8], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var currentX)
            || !int.TryParse(parts[9], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var currentY)
            || !IsSha256(parts[10])
            || !IsSha256(parts[11])
            || !int.TryParse(coordinateParts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var desiredX)
            || !int.TryParse(coordinateParts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var desiredY))
        {
            return false;
        }

        var profile = SvHabitatCoordinatesProfiles.ResolveRegion(parts[1]);
        var locator = new SvHabitatPhysicalLocator(
            profile.SourceFile,
            groupOccurrence,
            rowOccurrence,
            parts[10].ToLowerInvariant());
        value = new SvHabitatPendingMutation(
            profile.Region,
            parts[11].ToLowerInvariant(),
            new SvHabitatCoordinateMutation(
                locator,
                new SvHabitatSemanticIdentity(
                    devNo,
                    formNo,
                    parts[6] == "1",
                    parts[7] == "1"),
                new SvHabitatCoordinate(currentX, currentY),
                new SvHabitatCoordinate(desiredX, desiredY)));
        return true;
    }

    public static bool IsSamePhysicalTarget(PendingEdit edit, string region, SvHabitatRowBinding binding)
    {
        return TryDecode(edit, out var decoded)
            && string.Equals(decoded.Region, region, StringComparison.Ordinal)
            && decoded.Mutation.Locator.OuterGroupOccurrence == binding.OuterGroupOccurrence
            && decoded.Mutation.Locator.RowOccurrence == binding.RowOccurrence;
    }

    private static bool TryNonNegativeInt(string value, out int result)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result)
            && result >= 0;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
}
