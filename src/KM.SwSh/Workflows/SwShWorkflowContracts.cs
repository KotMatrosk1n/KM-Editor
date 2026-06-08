// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;

namespace KM.SwSh.Workflows;

public static class SwShWorkflowIds
{
    public const string Items = "items";
    public const string Pokemon = "pokemon";
    public const string Moves = "moves";
    public const string Text = "text";
    public const string Trainers = "trainers";
    public const string GiftPokemon = "giftPokemon";
    public const string StaticEncounters = "staticEncounters";
    public const string Shops = "shops";
    public const string Encounters = "encounters";
    public const string RaidRewards = "raidRewards";
    public const string Placement = "placement";
    public const string FlagworkSave = "flagworkSave";
    public const string ExeFsPatches = "exefsPatches";
    public const string RoyalCandy = "royalCandy";
    public const string SpreadsheetImport = "spreadsheetImport";
}

public enum SwShWorkflowAvailability
{
    Disabled,
    ReadOnly,
    Available,
}

public sealed record SwShWorkflowSummary(
    string Id,
    string Label,
    string Description,
    SwShWorkflowAvailability Availability,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShWorkflowList(IReadOnlyList<SwShWorkflowSummary> Workflows);
