// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.ZA.Workflows;

public static class ZaWorkflowIds
{
    public const string Items = "items";
    public const string Moves = "moves";
    public const string Text = "text";
    public const string Pokemon = "pokemon";
    public const string Trainers = "trainers";
    public const string Encounters = "encounters";
    public const string StaticEncounters = "staticEncounters";
    public const string Shops = "shops";
    public const string GiftPokemon = "giftPokemon";
    public const string TradePokemon = "tradePokemon";
    public const string Placement = "placement";
    public const string TypeChart = "typeChart";
    public const string FashionUnlock = "fashionUnlock";
    public const string SpreadsheetImport = "spreadsheetImport";
    public const string ModMerger = "modMerger";
}

public enum ZaWorkflowAvailability
{
    Disabled,
    ReadOnly,
    Available,
}

public sealed record ZaWorkflowSummary(
    string Id,
    string Label,
    string Description,
    ZaWorkflowAvailability Availability,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaWorkflowList(IReadOnlyList<ZaWorkflowSummary> Workflows);

public sealed record ZaEditSessionValidation(
    EditSession Session,
    bool IsValid,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaMoveFieldUpdate(int MoveId, string Field, string Value);
