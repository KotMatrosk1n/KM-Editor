// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.Data;

namespace KM.ZA.ScriptedBosses;

public sealed record ZaScriptedBossActionRecord(
    string Key,
    string Kind,
    int? MoveId,
    int? RuntimeMoveId,
    string Name,
    bool UsesBattleParameters,
    bool UsesTimingParameters);

public sealed record ZaScriptedBossProfileRecord(
    string Key,
    string LineageKey,
    int SpeciesId,
    int Form,
    string Name,
    string Scope,
    IReadOnlyList<ZaScriptedBossActionRecord> Actions);

internal static class ZaScriptedBossActionCatalog
{
    public const string BattleMoveKind = "battle-move";
    public const string MovementHelperKind = "movement-helper";
    public const string ScriptedMechanicKind = "scripted-mechanic";
    public const string BaseRogueMegaScope = "base-rogue-mega";

    private static readonly IReadOnlyList<ProfileDefinition> Profiles =
    [
        Profile(359, 1,
            BattleMove(163),
            BattleMove(403),
            BattleMove(399)),
        Profile(80, 1,
            BattleMove(60),
            BattleMove(250),
            BattleMove(352)),
        Profile(323, 1,
            BattleMove(29),
            BattleMove(414),
            ScriptedMechanic("volcanic-eruption", "Volcanic eruption sequence")),
        Profile(71, 1,
            BattleMove(22),
            BattleMove(188),
            BattleMove(331)),
        Profile(15, 1,
            BattleMove(42),
            BattleMove(398),
            BattleMove(679)),
        Profile(701, 1,
            BattleMove(332),
            BattleMove(280),
            BattleMove(403),
            BattleMove(560)),
        Profile(354, 1,
            BattleMove(109),
            BattleMove(247),
            BattleMove(421),
            BattleMove(425),
            BattleMove(566),
            ScriptedMechanic("clone-sequence", "Double Team clone sequence")),
        Profile(303, 1,
            BattleMove(14),
            BattleMove(98),
            BattleMove(242),
            BattleMove(442),
            BattleMove(583),
            BattleMove(605)),
        Profile(689, 1,
            BattleMove(127),
            BattleMove(157),
            BattleMove(370),
            BattleMove(612)),
        Profile(181, 1,
            BattleMove(87),
            BattleMove(268),
            BattleMove(406),
            BattleMove(435),
            BattleMove(784)),
        Profile(478, 1,
            BattleMove(59),
            BattleMove(196),
            BattleMove(247),
            BattleMove(261),
            BattleMove(556),
            BattleMove(566)),
        Profile(334, 1,
            BattleMove(239),
            BattleMove(406),
            BattleMove(413),
            BattleMove(585),
            MovementHelper(340)),
        Profile(3, 1,
            BattleMove(72),
            BattleMove(73),
            BattleMove(76),
            BattleMove(331),
            BattleMove(438),
            BattleMove(482)),
        Profile(149, 1,
            BattleMove(19),
            BattleMove(53),
            BattleMove(63),
            BattleMove(85),
            BattleMove(200),
            BattleMove(403),
            BattleMove(407),
            BattleMove(542),
            MovementHelper(340)),
        Profile(248, 1,
            BattleMove(89),
            BattleMove(157),
            BattleMove(200),
            BattleMove(242),
            BattleMove(328),
            BattleMove(416),
            BattleMove(444)),
        Profile(121, 1,
            BattleMove(56),
            BattleMove(339),
            BattleMove(352),
            BattleMove(428),
            BattleMove(453)),
    ];

    public static IReadOnlyList<ZaScriptedBossProfileRecord> Project(ZaTextLabelLookup labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        return Profiles
            .Select(profile =>
            {
                var speciesName = labels.Pokemon(profile.SpeciesId);
                return new ZaScriptedBossProfileRecord(
                    profile.Key,
                    $"boss_{profile.SpeciesId:D4}",
                    profile.SpeciesId,
                    profile.Form,
                    ZaLabels.PokemonWithForm(profile.SpeciesId, profile.Form, speciesName),
                    profile.Scope,
                    profile.Actions
                        .Select(action => new ZaScriptedBossActionRecord(
                            action.Key,
                            action.Kind,
                            action.MoveId,
                            action.MoveId is null ? null : checked(2000 + action.MoveId.Value),
                            action.MoveId is null ? action.Name! : labels.Move(action.MoveId.Value),
                            action.UsesBattleParameters,
                            action.UsesTimingParameters))
                        .ToArray());
            })
            .ToArray();
    }

    private static ProfileDefinition Profile(
        int speciesId,
        int form,
        params ActionDefinition[] actions)
    {
        return new ProfileDefinition(
            $"{speciesId}:{form}",
            speciesId,
            form,
            BaseRogueMegaScope,
            actions);
    }

    private static ActionDefinition BattleMove(int moveId)
    {
        return new ActionDefinition(
            $"{BattleMoveKind}:{moveId}",
            BattleMoveKind,
            moveId,
            null,
            UsesBattleParameters: true,
            UsesTimingParameters: true);
    }

    private static ActionDefinition MovementHelper(int moveId)
    {
        return new ActionDefinition(
            $"{MovementHelperKind}:{moveId}",
            MovementHelperKind,
            moveId,
            null,
            UsesBattleParameters: false,
            UsesTimingParameters: true);
    }

    private static ActionDefinition ScriptedMechanic(string key, string name)
    {
        return new ActionDefinition(
            $"{ScriptedMechanicKind}:{key}",
            ScriptedMechanicKind,
            MoveId: null,
            name,
            UsesBattleParameters: false,
            UsesTimingParameters: false);
    }

    private sealed record ProfileDefinition(
        string Key,
        int SpeciesId,
        int Form,
        string Scope,
        IReadOnlyList<ActionDefinition> Actions);

    private sealed record ActionDefinition(
        string Key,
        string Kind,
        int? MoveId,
        string? Name,
        bool UsesBattleParameters,
        bool UsesTimingParameters);
}
