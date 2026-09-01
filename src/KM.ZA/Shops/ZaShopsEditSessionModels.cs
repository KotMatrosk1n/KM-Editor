// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.ZA.Shops;

public sealed record ZaShopsEditResult(
    ZaShopsWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaShopInventoryItemUpdate(
    string ShopId,
    int Slot,
    string Field,
    string Value,
    string? RowId = null);
