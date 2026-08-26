// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.ScriptedBosses;

namespace KM.ZA.Encounters;

internal static class ZaEncounterBossActionRestoreResolver
{
    private const string UnavailableReason =
        "The selected encounter's editable boss controller actions cannot be matched exactly to verified base selector data.";

    public static bool TryResolve(
        ZaEncountersWorkflow workflow,
        ZaEncounterTableRecord table,
        ZaEncounterSlotRecord slot,
        out IReadOnlyList<ZaScriptedBossActionRecord> actions,
        out string blockedReason)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(slot);

        actions = [];
        blockedReason = string.Empty;

        if (string.Equals(
                table.ScriptedMoveOwnership?.Authority,
                ZaScriptedEncounterMoveOwnershipCatalog.SharedPrimaryControllerAuthority,
                StringComparison.Ordinal))
        {
            return true;
        }

        ZaScriptedBossProfileRecord? profile;
        IReadOnlySet<int>? ownedSelectorActionIds = null;
        if (IsPrimaryBossController(table.RawSpawnerId))
        {
            profile = ZaScriptedBossActionCatalog.FindProfile(
                workflow.ScriptedBosses,
                table.RawSpawnerId,
                slot.SpeciesId,
                slot.Form);
        }
        else if (table.ScriptedMoveOwnership is { } ownership
            && string.Equals(
                ownership.Authority,
                ZaScriptedEncounterMoveOwnershipCatalog.DedicatedFollowerActionTemplateAuthority,
                StringComparison.Ordinal))
        {
            profile = workflow.ScriptedBosses.FirstOrDefault(candidate => string.Equals(
                candidate.Key,
                ownership.ProfileKey,
                StringComparison.Ordinal));
            ownedSelectorActionIds = ownership.SelectorActionIds.ToHashSet();
        }
        else
        {
            return true;
        }

        if (profile is null)
        {
            return true;
        }

        var candidates = profile.Actions
            .Where(action =>
                action.SelectorActionId is not null
                && action.VanillaMoveId is not null
                && string.Equals(
                    action.Kind,
                    ZaScriptedBossActionCatalog.BattleMoveKind,
                    StringComparison.Ordinal)
                && (ownedSelectorActionIds is null
                    || ownedSelectorActionIds.Contains(action.SelectorActionId.Value)))
            .GroupBy(action => action.SelectorActionId!.Value)
            .ToArray();
        var resolved = new List<ZaScriptedBossActionRecord>(candidates.Length);
        foreach (var candidateGroup in candidates)
        {
            var candidate = candidateGroup.First();
            var selectorOwners = workflow.ScriptedBosses
                .SelectMany(owner => owner.Actions)
                .Where(action => action.SelectorActionId == candidateGroup.Key)
                .ToArray();
            if (candidateGroup.Any(action =>
                    action.VanillaMoveId != candidate.VanillaMoveId
                    || action.Variant != candidate.Variant))
            {
                blockedReason = UnavailableReason;
                return false;
            }

            var hasConsistentEditableOwnerShape = selectorOwners.Length > 0
                && selectorOwners.All(action =>
                    action.VanillaMoveId == candidate.VanillaMoveId
                    && action.Variant == candidate.Variant
                    && string.Equals(
                        action.Kind,
                        ZaScriptedBossActionCatalog.BattleMoveKind,
                        StringComparison.Ordinal));
            if (!hasConsistentEditableOwnerShape)
            {
                continue;
            }

            if (selectorOwners.Any(action => !action.CanEdit))
            {
                blockedReason = UnavailableReason;
                return false;
            }

            resolved.Add(candidate);
        }

        actions = resolved;
        return true;
    }

    public static bool IsPrimaryBossController(string? rawSpawnerId)
    {
        if (string.IsNullOrWhiteSpace(rawSpawnerId)
            || !rawSpawnerId.StartsWith(
                "btl_spn_boss_",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !rawSpawnerId
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.StartsWith(
                "follower",
                StringComparison.OrdinalIgnoreCase));
    }
}
