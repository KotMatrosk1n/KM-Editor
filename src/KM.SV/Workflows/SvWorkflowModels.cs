// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;

namespace KM.SV.Workflows;

public static class SvWorkflowIds
{
    public const string Items = "items";
    public const string Moves = "moves";
    public const string Pokemon = "pokemon";
    public const string Trainers = "trainers";
    public const string Encounters = "encounters";
    public const string TeraRaids = "teraRaids";
    public const string StaticEncounters = "staticEncounters";
    public const string GiftPokemon = "giftPokemon";
    public const string TradePokemon = "tradePokemon";
    public const string Placement = "placement";
    public const string TypeChart = "typeChart";
    public const string FashionUnlock = "fashionUnlock";
    public const string HyperspaceBypass = "hyperspaceBypass";
    public const string ModMerger = "modMerger";
}

public enum SvWorkflowAvailability
{
    Disabled,
    ReadOnly,
    Available,
}

public sealed record SvWorkflowSummary(
    string Id,
    string Label,
    string Description,
    SvWorkflowAvailability Availability,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvWorkflowList(IReadOnlyList<SvWorkflowSummary> Workflows);

public sealed record SvEditSessionValidation(
    EditSession Session,
    bool IsValid,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
