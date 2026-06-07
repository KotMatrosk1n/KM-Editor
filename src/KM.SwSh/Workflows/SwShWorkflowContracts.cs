// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;

namespace KM.SwSh.Workflows;

public static class SwShWorkflowIds
{
    public const string Items = "items";
    public const string Text = "text";
    public const string Trainers = "trainers";
    public const string Shops = "shops";
    public const string Encounters = "encounters";
    public const string RaidRewards = "raidRewards";
    public const string Placement = "placement";
    public const string FlagworkSave = "flagworkSave";
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
