// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.ZA.Workflows;

namespace KM.ZA.Items;

internal static class ZaItemEffectRules
{
    internal const int KingsRockItemId = 221;
    internal const int MinimumPercentage = 0;
    internal const int MaximumPercentage = 100;

    internal const string KingsRockFieldLabel = "Added flinch chance (%)";

    internal static bool IsKingsRockEquipPower(int itemId, string field) =>
        itemId == KingsRockItemId
        && string.Equals(field, ZaItemsWorkflowService.EquipPowerField, StringComparison.Ordinal);

    internal static bool IsSupportedPercentage(int value) =>
        value is >= MinimumPercentage and <= MaximumPercentage;

    internal static string ResolveFieldLabel(
        int itemId,
        ZaItemEditableField field) =>
        IsKingsRockEquipPower(itemId, field.Field)
            ? KingsRockFieldLabel
            : field.Label;

    internal static bool ValidateFieldValue(
        ZaItemRecord item,
        ZaItemEditableField field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsKingsRockEquipPower(item.ItemId, field.Field)
            || IsSupportedPercentage(value))
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"{KingsRockFieldLabel} must be between {MinimumPercentage} and {MaximumPercentage}.",
            ZaEditSessionSupport.ItemsDomain,
            field: field.Field,
            expected: "A percentage from 0 through 100; used only when the move's native flinch chance is zero"));
        return false;
    }
}
