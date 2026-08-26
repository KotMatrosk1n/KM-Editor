// SPDX-License-Identifier: GPL-3.0-only

using KM.SV.Shops;
using System.Security.Cryptography;

namespace KM.SV.TmMachine;

internal enum SvTmRecipeAvailabilityKind
{
    ProgressionGated,
    AllAvailable,
    Customized,
    Unsupported,
}

internal sealed record SvTmRecipeAvailabilityAnalysis(
    SvTmRecipeAvailabilityKind Kind,
    string Message,
    int MatchingRecordCount,
    int TotalRecordCount);

internal static class SvTmRecipeAvailabilityPatcher
{
    public const int ExpectedRecipeCount = 229;

    private const string SupportedBaseSha256 =
        "3FF76D03EC63035B966FAF6C16C0A84778407C1D0A69E2EA0DE14E45D1050326";

    public static SvTmRecipeAvailabilityAnalysis Analyze(byte[] baseBytes, byte[] currentBytes)
    {
        ArgumentNullException.ThrowIfNull(baseBytes);
        ArgumentNullException.ThrowIfNull(currentBytes);

        if (!HasSupportedBase(baseBytes))
        {
            return Unsupported(
                "TM recipe availability supports only the exact Scarlet/Violet 4.0.0 recipe table.");
        }

        try
        {
            var baseRows = SvShopsWorkflowService.ReadTechnicalMachineRows(baseBytes);
            var currentRows = SvShopsWorkflowService.ReadTechnicalMachineRows(currentBytes);
            if (!HasExpectedIdentities(baseRows, currentRows))
            {
                return Unsupported(
                    "The current TM recipe table does not have the supported 229-row identity and ordering contract.");
            }

            var progressionCount = 0;
            var availableCount = 0;
            for (var index = 0; index < baseRows.Count; index++)
            {
                var baseRow = baseRows[index];
                var currentRow = currentRows[index];
                if (currentRow.ConditionKind == baseRow.ConditionKind
                    && string.Equals(currentRow.ConditionValue, baseRow.ConditionValue, StringComparison.Ordinal))
                {
                    progressionCount++;
                }

                if (currentRow.ConditionKind == CondEnum.NONE
                    && string.IsNullOrEmpty(currentRow.ConditionValue))
                {
                    availableCount++;
                }
            }

            if (progressionCount == ExpectedRecipeCount)
            {
                return new SvTmRecipeAvailabilityAnalysis(
                    SvTmRecipeAvailabilityKind.ProgressionGated,
                    "TM recipes use the standard progression requirements.",
                    progressionCount,
                    ExpectedRecipeCount);
            }

            if (availableCount == ExpectedRecipeCount)
            {
                return new SvTmRecipeAvailabilityAnalysis(
                    SvTmRecipeAvailabilityKind.AllAvailable,
                    "All TM recipes are available while their normal crafting costs remain unchanged.",
                    availableCount,
                    ExpectedRecipeCount);
            }

            return new SvTmRecipeAvailabilityAnalysis(
                SvTmRecipeAvailabilityKind.Customized,
                "TM recipe release requirements are customized. Applying a policy changes only release requirements.",
                Math.Max(progressionCount, availableCount),
                ExpectedRecipeCount);
        }
        catch (Exception exception) when (exception is ArgumentException
            or IndexOutOfRangeException
            or InvalidDataException
            or InvalidOperationException
            or OverflowException)
        {
            return Unsupported($"The TM recipe table could not be inspected safely: {exception.Message}");
        }
    }

    public static byte[] Apply(byte[] baseBytes, byte[] currentBytes, bool allAvailable)
    {
        var analysis = Analyze(baseBytes, currentBytes);
        if (analysis.Kind == SvTmRecipeAvailabilityKind.Unsupported)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var baseRows = SvShopsWorkflowService.ReadTechnicalMachineRows(baseBytes);
        var currentRows = SvShopsWorkflowService.ReadTechnicalMachineRows(currentBytes);
        var outputRows = currentRows
            .Select((row, index) => row with
            {
                ConditionKind = allAvailable ? CondEnum.NONE : baseRows[index].ConditionKind,
                ConditionValue = allAvailable ? string.Empty : baseRows[index].ConditionValue,
            })
            .ToArray();
        var output = SvShopsWorkflowService.WriteTechnicalMachineRows(outputRows);
        var outputAnalysis = Analyze(baseBytes, output);
        var expectedKind = allAvailable
            ? SvTmRecipeAvailabilityKind.AllAvailable
            : SvTmRecipeAvailabilityKind.ProgressionGated;
        if (outputAnalysis.Kind != expectedKind)
        {
            throw new InvalidDataException("The TM recipe policy did not survive a parse/write/parse validation.");
        }

        AssertUnownedFieldsPreserved(currentRows, outputRows);
        return output;
    }

    private static bool HasSupportedBase(byte[] bytes) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), SupportedBaseSha256, StringComparison.Ordinal);

    private static bool HasExpectedIdentities(
        IReadOnlyList<SvShopsWorkflowService.TechnicalMachineRow> baseRows,
        IReadOnlyList<SvShopsWorkflowService.TechnicalMachineRow> currentRows)
    {
        if (baseRows.Count != ExpectedRecipeCount || currentRows.Count != ExpectedRecipeCount)
        {
            return false;
        }

        for (var index = 0; index < ExpectedRecipeCount; index++)
        {
            var left = baseRows[index];
            var right = currentRows[index];
            if (left.SourceIndex != index
                || right.SourceIndex != index
                || left.WazaItemId != right.WazaItemId
                || left.MoveId != right.MoveId
                || left.Region != right.Region)
            {
                return false;
            }
        }

        return true;
    }

    private static void AssertUnownedFieldsPreserved(
        IReadOnlyList<SvShopsWorkflowService.TechnicalMachineRow> before,
        IReadOnlyList<SvShopsWorkflowService.TechnicalMachineRow> after)
    {
        for (var index = 0; index < before.Count; index++)
        {
            var expected = before[index] with
            {
                ConditionKind = after[index].ConditionKind,
                ConditionValue = after[index].ConditionValue,
            };
            if (expected != after[index])
            {
                throw new InvalidDataException(
                    $"TM recipe row {index + 1} changed outside its release requirement fields.");
            }
        }
    }

    private static SvTmRecipeAvailabilityAnalysis Unsupported(string message) =>
        new(SvTmRecipeAvailabilityKind.Unsupported, message, 0, ExpectedRecipeCount);
}
