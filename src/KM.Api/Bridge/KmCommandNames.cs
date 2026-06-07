// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.Bridge;

/// <summary>
/// Stable command names for the local UI/backend bridge.
/// </summary>
public static class KmCommandNames
{
    public const string OpenProject = "project.open";
    public const string ValidateProject = "project.validate";
    public const string RefreshFileGraph = "project.fileGraph.refresh";
    public const string ListWorkflows = "workflow.list";
    public const string LoadItemsWorkflow = "items.load";
    public const string UpdateItemField = "items.field.update";
    public const string LoadTextWorkflow = "text.load";
    public const string LoadTrainersWorkflow = "trainers.load";
    public const string LoadShopsWorkflow = "shops.load";
    public const string LoadEncountersWorkflow = "encounters.load";
    public const string LoadRaidRewardsWorkflow = "raidRewards.load";
    public const string LoadPlacementWorkflow = "placement.load";
    public const string StartEditSession = "editSession.start";
    public const string GetEditSession = "editSession.get";
    public const string DiscardEditSession = "editSession.discard";
    public const string ValidateEditSession = "editSession.validate";
    public const string CreateChangePlan = "changePlan.create";
    public const string ApplyChangePlan = "changePlan.apply";
}
