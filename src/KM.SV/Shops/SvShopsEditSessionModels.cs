// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SV.Shops;

public sealed record SvShopsEditResult(
    SvShopsWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvShopInventoryItemUpdate(
    string ShopId,
    int Slot,
    string Field,
    string Value,
    string? RowId = null);
