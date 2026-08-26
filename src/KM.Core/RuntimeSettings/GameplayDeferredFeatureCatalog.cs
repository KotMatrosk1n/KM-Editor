// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.RuntimeSettings;

public enum GameplayDeferredFeatureId
{
    StaticAnimationControls,
    StaticLodControls,
    StaticColorControls,
    SpawnPopulationControls,
    SummaryUiControls,
    CombatMessagePacing,
    MapPresentationControls,
    StatusPresentationControls,
    AutoBattleEvs,
    PicnicExperience,
    CaptureBonuses,
    ShinyLifecycleNotices,
    OptimizedSpawnLogic,
    NovelEvolutionPredicates,
    EventResets,
    LanguageSwitching,
    FoodBehavior,
    MovementBehavior,
    FollowSynchronizationBehavior,
    NewNpcServices,
}

public enum GameplayDeferredFeatureState
{
    Implemented,
    ProofGated,
    NotApplicable,
}

public sealed record GameplayDeferredFeatureAssessment(
    GameplayDeferredFeatureId Feature,
    GameplayDeferredFeatureState State,
    string ReasonCode,
    string FailClosedBehavior,
    IReadOnlyList<string> MissingProof)
{
    public bool CanExposeControl => State == GameplayDeferredFeatureState.Implemented;
}

/// <summary>
/// Exhaustive fail-closed inventory for gameplay and presentation controls that are not part of
/// the initial settings schema. A feature stays absent from generated packages and the settings UI
/// until its title-specific entry is replaced by an implemented assessment.
/// </summary>
public static class GameplayDeferredFeatureCatalog
{
    private const string OmitControl = "control-omitted-and-no-writer";

    private static readonly IReadOnlyList<GameplayDeferredFeatureAssessment> ScarletViolet = Create(
        GameplaySettingsFamily.ScarletViolet,
        Gate(GameplayDeferredFeatureId.StaticAnimationControls, "sv-static-animation-proof-missing",
            "sv-animation-table-path-and-consumer-map",
            "sv-animation-table-noop-roundtrip",
            "sv-animation-runtime-presentation-canary"),
        Gate(GameplayDeferredFeatureId.StaticLodControls, "sv-static-lod-proof-missing",
            "sv-lod-table-path-and-field-map",
            "sv-lod-choice-to-live-consumer-map",
            "sv-lod-streaming-and-display-canary"),
        Gate(GameplayDeferredFeatureId.StaticColorControls, "sv-static-color-proof-missing",
            "sv-color-profile-table-and-enum-map",
            "sv-color-layout-and-render-consumer-map",
            "sv-color-handheld-docked-canary"),
        Gate(GameplayDeferredFeatureId.SpawnPopulationControls, "sv-spawn-population-proof-missing",
            "sv-public-choice-to-live-total-map",
            "sv-region-and-dlc-spawn-consumer-census",
            "sv-spawn-streaming-suspend-canary"),
        Gate(GameplayDeferredFeatureId.SummaryUiControls, "sv-summary-ui-proof-missing",
            "sv-summary-layout-and-widget-ownership",
            "sv-summary-stat-getter-map",
            "sv-summary-all-language-layout-canary"),
        Gate(GameplayDeferredFeatureId.CombatMessagePacing, "sv-message-pacing-proof-missing",
            "sv-battle-message-timing-source-inventory",
            "sv-battle-message-timing-preimages",
            "sv-animation-event-pacing-canary"),
        Gate(GameplayDeferredFeatureId.MapPresentationControls, "sv-map-presentation-proof-missing",
            "sv-map-table-layout-texture-message-join",
            "sv-map-region-and-dlc-consumer-map",
            "sv-map-handheld-docked-language-canary"),
        Gate(GameplayDeferredFeatureId.StatusPresentationControls, "sv-status-presentation-proof-missing",
            "sv-status-layout-and-getter-map",
            "sv-status-format-and-language-canary"),
        Gate(GameplayDeferredFeatureId.AutoBattleEvs, "sv-auto-battle-ev-proof-missing",
            "sv-auto-battle-award-producer-and-recipient-map",
            "sv-auto-battle-zero-award-side-effect-canary"),
        Gate(GameplayDeferredFeatureId.PicnicExperience, "sv-picnic-exp-proof-missing",
            "sv-picnic-exp-prototype-ownership",
            "sv-picnic-recipient-and-rate-policy-canary",
            "sv-picnic-exit-save-reload-canary"),
        Gate(GameplayDeferredFeatureId.CaptureBonuses, "sv-capture-bonus-proof-missing",
            "sv-retail-caught-count-accessor",
            "sv-generated-pokemon-mutation-target",
            "sv-shiny-and-hidden-ability-source-census",
            "sv-per-save-state-contract"),
        Gate(GameplayDeferredFeatureId.ShinyLifecycleNotices, "sv-shiny-notice-proof-missing",
            "sv-spawn-effect-attachment-owner",
            "sv-notice-cleanup-and-exclusion-map",
            "sv-despawn-respawn-save-reload-canary"),
        Gate(GameplayDeferredFeatureId.OptimizedSpawnLogic, "sv-spawn-optimization-proof-missing",
            "sv-profiler-baseline-and-allocation-ownership",
            "sv-streaming-thread-and-contention-map",
            "sv-fallback-suspend-and-conflict-canary"),
        Gate(GameplayDeferredFeatureId.NovelEvolutionPredicates, "sv-evolution-predicate-proof-missing",
            "sv-new-predicate-registration-abi",
            "sv-evolution-cancel-item-time-and-location-canary"),
        Gate(GameplayDeferredFeatureId.EventResets, "sv-event-reset-proof-missing",
            "sv-reset-flag-dependency-graphs",
            "sv-reward-respawn-map-dialogue-state-map",
            "sv-reset-atomicity-and-interruption-canary"),
        Gate(GameplayDeferredFeatureId.LanguageSwitching, "sv-language-switch-proof-missing",
            "sv-retail-language-write-path",
            "sv-message-grammar-and-fallback-inventory",
            "sv-capture-language-and-restart-canary"),
        Gate(GameplayDeferredFeatureId.FoodBehavior, "sv-food-behavior-proof-missing",
            "sv-food-and-picnic-consumer-census",
            "sv-egg-buff-and-save-lifecycle-canary"),
        Gate(GameplayDeferredFeatureId.MovementBehavior, "sv-movement-proof-missing",
            "sv-movement-and-fast-travel-target-map",
            "sv-transition-fall-recovery-and-suspend-canary"),
        Gate(GameplayDeferredFeatureId.FollowSynchronizationBehavior, "sv-follow-sync-proof-missing",
            "sv-follow-and-synchronization-controller-map",
            "sv-start-stop-transition-and-cleanup-canary"),
        Gate(GameplayDeferredFeatureId.NewNpcServices, "sv-npc-service-proof-missing",
            "sv-scene-object-insertion-writer",
            "sv-event-choice-and-cancel-graph",
            "sv-party-inventory-message-transaction",
            "sv-service-save-reload-and-multiplayer-canary"));

    private static readonly IReadOnlyList<GameplayDeferredFeatureAssessment> SwordShield = Create(
        GameplaySettingsFamily.SwordShield,
        Gate(GameplayDeferredFeatureId.StaticAnimationControls, "swsh-static-animation-proof-missing",
            "swsh-animation-asset-and-consumer-map",
            "swsh-animation-noop-roundtrip-and-runtime-canary"),
        Gate(GameplayDeferredFeatureId.StaticLodControls, "swsh-static-lod-proof-missing",
            "swsh-lod-asset-and-consumer-map",
            "swsh-lod-streaming-and-display-canary"),
        Gate(GameplayDeferredFeatureId.StaticColorControls, "swsh-static-color-proof-missing",
            "swsh-color-asset-and-consumer-map",
            "swsh-color-handheld-docked-canary"),
        Gate(GameplayDeferredFeatureId.SpawnPopulationControls, "swsh-spawn-population-proof-missing",
            "swsh-spawn-count-source-and-consumer-map",
            "swsh-wild-area-dlc-streaming-canary"),
        Gate(GameplayDeferredFeatureId.SummaryUiControls, "swsh-summary-ui-proof-missing",
            "swsh-summary-layout-and-stat-getter-map",
            "swsh-summary-language-and-navigation-canary"),
        Gate(GameplayDeferredFeatureId.CombatMessagePacing, "swsh-message-pacing-proof-missing",
            "swsh-battle-message-timing-source-inventory",
            "swsh-amx-native-animation-pacing-canary"),
        Gate(GameplayDeferredFeatureId.MapPresentationControls, "swsh-map-presentation-proof-missing",
            "swsh-map-table-layout-texture-message-join",
            "swsh-base-wild-area-dlc-render-canary"),
        Gate(GameplayDeferredFeatureId.StatusPresentationControls, "swsh-status-presentation-proof-missing",
            "swsh-status-layout-and-getter-map",
            "swsh-status-language-and-navigation-canary"),
        NotApplicable(GameplayDeferredFeatureId.AutoBattleEvs, "swsh-auto-battle-ev-not-applicable"),
        NotApplicable(GameplayDeferredFeatureId.PicnicExperience, "swsh-picnic-exp-not-applicable"),
        Gate(GameplayDeferredFeatureId.CaptureBonuses, "swsh-capture-bonus-proof-missing",
            "swsh-capture-counter-and-generated-pokemon-owner",
            "swsh-shiny-hidden-ability-and-save-state-canary"),
        Gate(GameplayDeferredFeatureId.ShinyLifecycleNotices, "swsh-shiny-notice-proof-missing",
            "swsh-spawn-effect-attachment-and-cleanup-map",
            "swsh-encounter-class-and-lifecycle-canary"),
        Gate(GameplayDeferredFeatureId.OptimizedSpawnLogic, "swsh-spawn-optimization-proof-missing",
            "swsh-profiler-and-streaming-thread-baseline",
            "swsh-fallback-suspend-and-conflict-canary"),
        Gate(GameplayDeferredFeatureId.NovelEvolutionPredicates, "swsh-evolution-predicate-proof-missing",
            "swsh-new-predicate-registration-abi",
            "swsh-evolution-cancel-item-time-and-location-canary"),
        Gate(GameplayDeferredFeatureId.EventResets, "swsh-event-reset-proof-missing",
            "swsh-known-flag-dependency-graphs",
            "swsh-reward-respawn-map-dialogue-state-map",
            "swsh-reset-atomicity-and-interruption-canary"),
        Gate(GameplayDeferredFeatureId.LanguageSwitching, "swsh-language-switch-proof-missing",
            "swsh-retail-language-write-path",
            "swsh-message-grammar-capture-and-restart-canary"),
        Gate(GameplayDeferredFeatureId.FoodBehavior, "swsh-food-behavior-proof-missing",
            "swsh-camp-curry-source-and-consumer-map",
            "swsh-food-award-and-save-lifecycle-canary"),
        Gate(GameplayDeferredFeatureId.MovementBehavior, "swsh-movement-proof-missing",
            "swsh-movement-and-fast-travel-target-map",
            "swsh-transition-fall-recovery-and-suspend-canary"),
        Gate(GameplayDeferredFeatureId.FollowSynchronizationBehavior, "swsh-follow-proof-missing",
            "swsh-follow-controller-and-region-map",
            "swsh-start-stop-transition-and-cleanup-canary"),
        Gate(GameplayDeferredFeatureId.NewNpcServices, "swsh-npc-service-proof-missing",
            "swsh-scene-object-and-amx-registration-writer",
            "swsh-choice-cancel-party-inventory-transaction",
            "swsh-service-save-reload-and-link-canary"));

    private static readonly IReadOnlyList<GameplayDeferredFeatureAssessment> LegendsZa = Create(
        GameplaySettingsFamily.LegendsZA,
        Gate(GameplayDeferredFeatureId.StaticAnimationControls, "za-static-animation-proof-missing",
            "za-animation-asset-and-consumer-map",
            "za-animation-noop-roundtrip-and-runtime-canary"),
        Gate(GameplayDeferredFeatureId.StaticLodControls, "za-static-lod-proof-missing",
            "za-lod-asset-and-consumer-map",
            "za-lod-streaming-and-display-canary"),
        Gate(GameplayDeferredFeatureId.StaticColorControls, "za-static-color-proof-missing",
            "za-color-asset-and-consumer-map",
            "za-color-handheld-docked-canary"),
        Gate(GameplayDeferredFeatureId.SpawnPopulationControls, "za-spawn-population-proof-missing",
            "za-serialized-count-to-live-actor-map",
            "za-sewer-zone-streaming-suspend-canary"),
        Gate(GameplayDeferredFeatureId.SummaryUiControls, "za-summary-ui-proof-missing",
            "za-summary-layout-and-stat-getter-map",
            "za-summary-language-and-navigation-canary"),
        Gate(GameplayDeferredFeatureId.CombatMessagePacing, "za-message-pacing-proof-missing",
            "za-battle-message-and-timing-consumer-map",
            "za-realtime-battle-animation-pacing-canary"),
        Gate(GameplayDeferredFeatureId.MapPresentationControls, "za-map-presentation-proof-missing",
            "za-map-table-layout-texture-message-join",
            "za-map-zone-language-and-display-canary"),
        Gate(GameplayDeferredFeatureId.StatusPresentationControls, "za-status-presentation-proof-missing",
            "za-status-layout-and-getter-map",
            "za-status-language-and-navigation-canary"),
        NotApplicable(GameplayDeferredFeatureId.AutoBattleEvs, "za-auto-battle-ev-not-applicable"),
        NotApplicable(GameplayDeferredFeatureId.PicnicExperience, "za-picnic-exp-not-applicable"),
        Gate(GameplayDeferredFeatureId.CaptureBonuses, "za-capture-bonus-proof-missing",
            "za-capture-counter-and-generated-pokemon-owner",
            "za-shiny-alpha-and-save-state-canary"),
        Gate(GameplayDeferredFeatureId.ShinyLifecycleNotices, "za-shiny-notice-proof-missing",
            "za-spawn-effect-attachment-and-cleanup-map",
            "za-encounter-class-and-lifecycle-canary"),
        Gate(GameplayDeferredFeatureId.OptimizedSpawnLogic, "za-spawn-optimization-proof-missing",
            "za-profiler-and-streaming-thread-baseline",
            "za-fallback-suspend-and-conflict-canary"),
        Gate(GameplayDeferredFeatureId.NovelEvolutionPredicates, "za-evolution-predicate-proof-missing",
            "za-evolution-method-struct-and-enum-map",
            "za-new-predicate-registration-abi",
            "za-evolution-cancel-item-time-and-location-canary"),
        Gate(GameplayDeferredFeatureId.EventResets, "za-event-reset-proof-missing",
            "za-event-flag-dependency-graphs",
            "za-reward-respawn-map-dialogue-state-map",
            "za-reset-atomicity-and-interruption-canary"),
        Gate(GameplayDeferredFeatureId.LanguageSwitching, "za-language-switch-proof-missing",
            "za-retail-language-write-path",
            "za-message-grammar-capture-and-restart-canary"),
        Gate(GameplayDeferredFeatureId.FoodBehavior, "za-food-behavior-proof-missing",
            "za-food-source-and-consumer-map",
            "za-food-award-and-save-lifecycle-canary"),
        Gate(GameplayDeferredFeatureId.MovementBehavior, "za-movement-proof-missing",
            "za-movement-and-fast-travel-target-map",
            "za-transition-fall-recovery-and-suspend-canary"),
        Gate(GameplayDeferredFeatureId.FollowSynchronizationBehavior, "za-follow-proof-missing",
            "za-follower-controller-asset-locator-action-map",
            "za-start-stop-transition-and-cleanup-canary"),
        Gate(GameplayDeferredFeatureId.NewNpcServices, "za-npc-service-proof-missing",
            "za-scene-object-template-insertion-writer",
            "za-event-choice-cancel-party-inventory-transaction",
            "za-service-save-reload-and-link-canary"));

    public static IReadOnlyList<GameplayDeferredFeatureAssessment> ForFamily(
        GameplaySettingsFamily family)
    {
        return family switch
        {
            GameplaySettingsFamily.ScarletViolet => ScarletViolet,
            GameplaySettingsFamily.SwordShield => SwordShield,
            GameplaySettingsFamily.LegendsZA => LegendsZa,
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
    }

    private static GameplayDeferredFeatureAssessment Gate(
        GameplayDeferredFeatureId feature,
        string reasonCode,
        params string[] missingProof)
    {
        return new GameplayDeferredFeatureAssessment(
            feature,
            GameplayDeferredFeatureState.ProofGated,
            reasonCode,
            OmitControl,
            Array.AsReadOnly(missingProof.ToArray()));
    }

    private static GameplayDeferredFeatureAssessment NotApplicable(
        GameplayDeferredFeatureId feature,
        string reasonCode)
    {
        return new GameplayDeferredFeatureAssessment(
            feature,
            GameplayDeferredFeatureState.NotApplicable,
            reasonCode,
            OmitControl,
            []);
    }

    private static IReadOnlyList<GameplayDeferredFeatureAssessment> Create(
        GameplaySettingsFamily family,
        params GameplayDeferredFeatureAssessment[] entries)
    {
        var expectedCount = Enum.GetValues<GameplayDeferredFeatureId>().Length;
        if (entries.Length != expectedCount
            || entries.Select(entry => entry.Feature).Distinct().Count() != expectedCount
            || entries.Any(entry => entry.CanExposeControl)
            || entries.Any(entry => entry.State == GameplayDeferredFeatureState.ProofGated
                && entry.MissingProof.Count == 0)
            || entries.Any(entry => entry.MissingProof.Count > 8)
            || entries.SelectMany(entry => entry.MissingProof)
                .Any(proof => !IsStableToken(proof)))
        {
            throw new InvalidOperationException(
                $"The {family} deferred gameplay feature inventory is incomplete or invalid.");
        }

        return Array.AsReadOnly(entries);
    }

    private static bool IsStableToken(string value)
    {
        return value is { Length: > 0 and <= 128 }
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }
}
