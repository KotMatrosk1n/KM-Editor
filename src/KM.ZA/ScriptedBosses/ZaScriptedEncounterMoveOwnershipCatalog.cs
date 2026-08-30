// SPDX-License-Identifier: GPL-3.0-only

namespace KM.ZA.ScriptedBosses;

public sealed record ZaScriptedBossAffectedScopeRecord(
    string Key,
    string Label,
    IReadOnlyList<string> BattleContexts,
    IReadOnlyList<int> SpeciesIds,
    bool IncludesPrimaryController);

public sealed record ZaScriptedEncounterMoveOwnershipRecord(
    string State,
    string Authority,
    string ProfileKey,
    string ProfileName,
    bool EncounterMoveListAuthoritative,
    string Caveat,
    IReadOnlyList<int> SelectorActionIds,
    IReadOnlyList<ZaScriptedBossAffectedScopeRecord> AffectedScopes);

internal static class ZaScriptedEncounterMoveOwnershipCatalog
{
    public const string ScriptedControllerState = "scripted-controller";
    public const string DedicatedFollowerActionTemplateAuthority =
        "dedicated-follower-action-template";
    public const string SharedPrimaryControllerAuthority = "shared-primary-controller";

    private const string KakunaFollowerProfileKey = "14:0:boss_0015:follower";
    private const string BeedrillFollowerProfileKey = "15:0:boss_0015:follower";
    private const string BinacleFollowerProfileKey = "688:0:boss_0689:follower";
    private const string BanettePrimaryProfileKey = "354:1";

    private static readonly ZaScriptedBossAffectedScopeRecord SharedBeedrillFollowersScope =
        new(
            "beedrill-battle-kakuna-and-beedrill-followers",
            "Every Kakuna and Beedrill follower in the Beedrill boss battle",
            ["story", "simulation", "simulation-dlc", "rematch", "rush"],
            [14, 15],
            IncludesPrimaryController: false);

    private static readonly ZaScriptedBossAffectedScopeRecord KakunaFollowersScope =
        new(
            "beedrill-battle-kakuna-followers",
            "Every Kakuna follower in the Beedrill boss battle",
            ["story", "simulation", "simulation-dlc", "rematch", "rush"],
            [14],
            IncludesPrimaryController: false);

    private static readonly ZaScriptedBossAffectedScopeRecord BeedrillFollowersScope =
        new(
            "beedrill-battle-beedrill-followers",
            "Every Beedrill follower in the Beedrill boss battle",
            ["story", "simulation", "simulation-dlc", "rematch", "rush"],
            [15],
            IncludesPrimaryController: false);

    // boss_btl_data_global consumes spn_boss_0689_sim2 and spn_boss_0689_re even though
    // those follower tables are not materialized. The boss-context resolver intentionally
    // routes them to the retained _sim and _rus Binacle follower lineages, respectively.
    private static readonly ZaScriptedBossAffectedScopeRecord BarbaracleBinacleFollowersScope =
        new(
            "barbaracle-battle-binacle-followers",
            "Both Binacle follower pools in every Barbaracle boss battle",
            ["story", "simulation", "simulation-dlc", "rematch", "rush"],
            [688],
            IncludesPrimaryController: false);

    private static readonly ZaScriptedBossAffectedScopeRecord BanetteControllerScope =
        new(
            "banette-primary-and-clone-controllers",
            "Primary Banette and every scripted Banette clone",
            ["story", "simulation", "simulation-dlc", "rematch", "rush"],
            [354],
            IncludesPrimaryController: true);

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<ZaScriptedBossAffectedScopeRecord>>
        AffectedScopesBySelector =
            new Dictionary<int, IReadOnlyList<ZaScriptedBossAffectedScopeRecord>>
            {
                [17536] = [SharedBeedrillFollowersScope], // Poison Sting
                [17629] = [SharedBeedrillFollowersScope], // String Shot
                [22312] = [KakunaFollowersScope], // Harden
                [22313] = [BeedrillFollowersScope], // Pin Missile
                [22254] = [BarbaracleBinacleFollowersScope], // Water Gun
                [22255] = [BarbaracleBinacleFollowersScope], // Slash
                [22256] = [BarbaracleBinacleFollowersScope], // Bulldoze
                [17631] = [BanetteControllerScope], // Confuse Ray
                [17617] = [BanetteControllerScope], // Shadow Ball
                [17616] = [BanetteControllerScope], // Shadow Claw
                [17618] = [BanetteControllerScope], // Shadow Sneak
                [17634] = [BanetteControllerScope], // Phantom Force
            };

    private static readonly IReadOnlyList<OwnershipDefinition> Definitions =
    [
        new(
            KakunaFollowerProfileKey,
            SpeciesId: 14,
            Form: 0,
            DedicatedFollowerActionTemplateAuthority,
            "This encounter WazaList is loaded but does not choose attacks. The boss-follower "
                + "action template invokes the listed selectors directly; replacements preserve "
                + "the existing choreography but remain subject to targeting and animation compatibility.",
            [22312, 17629, 17536],
            CreatePlacements(
                ("spn_boss_0015_01_follower01", "ect_boss_0015_01_follower01"),
                ("spn_boss_0015_sim_follower01", "ect_boss_0015_sim_follower01"),
                ("spn_boss_0015_rus_follower01", "ect_boss_0015_rush_follower01"))),
        new(
            BeedrillFollowerProfileKey,
            SpeciesId: 15,
            Form: 0,
            DedicatedFollowerActionTemplateAuthority,
            "This encounter WazaList is loaded but does not choose attacks. The boss-follower "
                + "action template invokes the listed selectors directly; replacements preserve "
                + "the existing choreography but remain subject to targeting and animation compatibility.",
            [17629, 17536, 22313],
            CreatePlacements(
                ("spn_boss_0015_01_follower02", "ect_boss_0015_01_follower02"),
                ("spn_boss_0015_sim_follower02", "ect_boss_0015_sim_follower02"),
                ("spn_boss_0015_rus_follower02", "ect_boss_0015_rush_follower02"))),
        new(
            BinacleFollowerProfileKey,
            SpeciesId: 688,
            Form: 0,
            DedicatedFollowerActionTemplateAuthority,
            "This encounter WazaList is loaded but does not choose attacks. The Barbaracle "
                + "boss-follower action template invokes the listed selectors directly for both "
                + "Binacle follower pools; replacements preserve the existing choreography but "
                + "remain subject to targeting and animation compatibility.",
            [22254, 22255, 22256],
            CreatePlacements(
                ("spn_boss_0689_01_follower01", "ect_boss_0689_01_follower01"),
                ("spn_boss_0689_01_follower02", "ect_boss_0689_01_follower02"),
                ("spn_boss_0689_sim_follower01", "ect_boss_0689_sim_follower01"),
                ("spn_boss_0689_sim_follower02", "ect_boss_0689_sim_follower02"),
                ("spn_boss_0689_rus_follower01", "ect_boss_0689_rush_follower01"),
                ("spn_boss_0689_rus_follower02", "ect_boss_0689_rush_follower02"))),
        new(
            BanettePrimaryProfileKey,
            SpeciesId: 354,
            Form: 1,
            SharedPrimaryControllerAuthority,
            "Banette clones use the primary Banette controller selectors. There is no independent "
                + "clone move pool. These values are a read-only projection of the primary Rogue Mega "
                + "Banette actions. Edit that primary encounter to change the boss and every scripted "
                + "clone in all listed battle contexts.",
            [17631, 17617, 17616, 17618, 17634],
            CreatePlacements(
                ("spn_boss_0354_01_follower01", "ect_boss_0354_01_follower01"),
                ("spn_boss_0354_01_follower02", "ect_boss_0354_01_follower01"),
                ("spn_boss_0354_02_follower01", "ect_boss_0354_01_follower01"),
                ("spn_boss_0354_02_follower02", "ect_boss_0354_01_follower01"),
                ("spn_boss_0354_02_follower03", "ect_boss_0354_01_follower01"),
                ("spn_boss_0354_02_follower04", "ect_boss_0354_01_follower01"),
                ("spn_boss_0354_sim1_follower01", "ect_boss_0354_sim_follower01"),
                ("spn_boss_0354_sim1_follower02", "ect_boss_0354_sim_follower01"),
                ("spn_boss_0354_sim2_follower01", "ect_boss_0354_sim_follower01"),
                ("spn_boss_0354_sim2_follower02", "ect_boss_0354_sim_follower01"),
                ("spn_boss_0354_sim2_follower03", "ect_boss_0354_sim_follower01"),
                ("spn_boss_0354_sim2_follower04", "ect_boss_0354_sim_follower01"),
                ("spn_boss_0354_rus1_follower01", "ect_boss_0354_rush_follower01"),
                ("spn_boss_0354_rus1_follower02", "ect_boss_0354_rush_follower01"),
                ("spn_boss_0354_rus2_follower01", "ect_boss_0354_rush_follower01"),
                ("spn_boss_0354_rus2_follower02", "ect_boss_0354_rush_follower01"),
                ("spn_boss_0354_rus2_follower03", "ect_boss_0354_rush_follower01"),
                ("spn_boss_0354_rus2_follower04", "ect_boss_0354_rush_follower01"))),
    ];

    public static IReadOnlyList<ZaScriptedBossAffectedScopeRecord> GetAffectedScopes(
        int selectorActionId)
    {
        return AffectedScopesBySelector.GetValueOrDefault(
            selectorActionId,
            Array.Empty<ZaScriptedBossAffectedScopeRecord>());
    }

    public static ZaScriptedEncounterMoveOwnershipRecord? Resolve(
        IReadOnlyList<ZaScriptedBossProfileRecord> profiles,
        string? rawSpawnerId,
        string? encounterDataId,
        int speciesId,
        int form)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (string.IsNullOrWhiteSpace(rawSpawnerId)
            || string.IsNullOrWhiteSpace(encounterDataId))
        {
            return null;
        }

        var definition = Definitions.FirstOrDefault(candidate =>
            candidate.Placements.TryGetValue(rawSpawnerId, out var expectedEncounterDataId)
            && string.Equals(
                expectedEncounterDataId,
                encounterDataId,
                StringComparison.Ordinal));
        if (definition is null)
        {
            return null;
        }

        var profile = profiles.FirstOrDefault(candidate => string.Equals(
            candidate.Key,
            definition.ProfileKey,
            StringComparison.Ordinal));
        if (profile is null
            || definition.SelectorActionIds.Any(selectorActionId =>
                profile.Actions.All(action => action.SelectorActionId != selectorActionId)))
        {
            return null;
        }

        var affectedScopes = definition.SelectorActionIds
            .SelectMany(GetAffectedScopes)
            .DistinctBy(scope => scope.Key, StringComparer.Ordinal)
            .ToArray();
        var caveat = speciesId == definition.SpeciesId && form == definition.Form
            ? definition.Caveat
            : $"The encounter identity has changed from the controller's authored species {definition.SpeciesId}, form {definition.Form}, but the exact scripted spawner still owns these selectors. {definition.Caveat}";
        return new ZaScriptedEncounterMoveOwnershipRecord(
            ScriptedControllerState,
            definition.Authority,
            profile.Key,
            profile.Name,
            EncounterMoveListAuthoritative: false,
            caveat,
            definition.SelectorActionIds,
            affectedScopes);
    }

    private static IReadOnlyDictionary<string, string> CreatePlacements(
        params (string SpawnerId, string EncounterDataId)[] placements)
    {
        return placements.ToDictionary(
            placement => placement.SpawnerId,
            placement => placement.EncounterDataId,
            StringComparer.Ordinal);
    }

    private sealed record OwnershipDefinition(
        string ProfileKey,
        int SpeciesId,
        int Form,
        string Authority,
        string Caveat,
        IReadOnlyList<int> SelectorActionIds,
        IReadOnlyDictionary<string, string> Placements);
}
