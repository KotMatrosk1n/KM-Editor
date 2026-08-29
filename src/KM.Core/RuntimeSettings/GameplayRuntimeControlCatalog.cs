// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.Core.RuntimeSettings;

public enum GameplayRuntimeControlId : ushort
{
    ExperienceShare = 1,
    ExperienceRate = 2,
    LevelCap = 3,
    PicnicExperience = 4,
    ShinyNotifications = 5,
    NearbySpawnLimit = 6,
    AnimationRateProfile = 7,
    WildPokemonDetail = 8,
    CaptureProgressBonuses = 9,
    BattleCameraMode = 10,
    NearCameraModelFade = 11,
    StaticNpcDetail = 12,
    WanderingNpcDetail = 13,
    AutoBattleEvRecipients = 14,
    ColorProfile = 15,
    GameLanguage = 16,
}

public enum GameplayRuntimeControlSection
{
    Progression,
    Encounters,
    Presentation,
    System,
}

public enum GameplayRuntimeControlValueKind
{
    Toggle,
    BoundedInteger,
    Choice,
}

public enum GameplayRuntimeControlDefaultSource
{
    FixedValue,
    PreserveRetailValue,
}

public enum GameplayRuntimeControlApplicability
{
    Applicable,
    NotApplicable,
}

public enum GameplayRuntimeMenuRoute
{
    None,
    StockOptionsExtension,
    ScriptedSettingsPage,
    OwnedSettingsPage,
}

public enum GameplayRuntimeControlProofState
{
    ContractModeled,
    PartialConsumerMap,
    ExactConsumerMapped,
    RuntimeValidated,
    NotApplicable,
}

public enum GameplayRuntimeControlStorageKind
{
    Unassigned,
    GameplaySettingsJournalSchema1,
}

public sealed record GameplayRuntimeControlChoice(
    int Value,
    string StableId,
    string DisplayName);

public sealed record GameplayRuntimeControlValueDomain(
    GameplayRuntimeControlValueKind Kind,
    int MinimumValue,
    int MaximumValue,
    int Step,
    string UnitCode,
    GameplayRuntimeControlDefaultSource DefaultSource,
    int? DefaultValue,
    IReadOnlyList<GameplayRuntimeControlChoice> NamedValues)
{
    public bool Accepts(int value)
    {
        if (value < MinimumValue || value > MaximumValue)
        {
            return false;
        }

        return Kind switch
        {
            GameplayRuntimeControlValueKind.Toggle => value is 0 or 1,
            GameplayRuntimeControlValueKind.BoundedInteger =>
                (value - MinimumValue) % Step == 0,
            GameplayRuntimeControlValueKind.Choice =>
                NamedValues.Any(choice => choice.Value == value),
            _ => false,
        };
    }
}

public sealed record GameplayRuntimeFamilyControlDescriptor(
    GameplaySettingsFamily Family,
    GameplayRuntimeControlApplicability Applicability,
    GameplayRuntimeMenuRoute PlannedMenuRoute,
    GameplayRuntimeControlProofState ProofState,
    string ReasonCode,
    string FailClosedBehavior,
    GameplayRuntimeControlValueDomain? ValueDomain,
    IReadOnlyList<string> MissingProof)
{
    public bool RuntimeDeliverySupported =>
        Applicability == GameplayRuntimeControlApplicability.Applicable
        && ProofState == GameplayRuntimeControlProofState.RuntimeValidated;

    public bool ExactBuildBetaDeliverySupported =>
        Applicability == GameplayRuntimeControlApplicability.Applicable
        && ProofState is GameplayRuntimeControlProofState.ExactConsumerMapped
            or GameplayRuntimeControlProofState.PartialConsumerMap;
}

public sealed record GameplayRuntimeControlDefinition(
    GameplayRuntimeControlId Id,
    string StableId,
    string DisplayName,
    GameplayRuntimeControlSection Section,
    GameplayRuntimeControlStorageKind StorageKind,
    GameplaySettingPresence? JournalPresence,
    GameplayDeferredFeatureId? DeferredFeature,
    IReadOnlyList<GameplayRuntimeFamilyControlDescriptor> Families)
{
    public GameplayRuntimeFamilyControlDescriptor ForFamily(GameplaySettingsFamily family)
    {
        return Families.Single(binding => binding.Family == family);
    }

    public bool CanExposeControl(GameplaySettingsFamily family)
    {
        return StorageKind != GameplayRuntimeControlStorageKind.Unassigned
            && ForFamily(family).RuntimeDeliverySupported;
    }

    /// <summary>
    /// Authorizes only the exact-build cheat-list beta channel. It does not claim that a
    /// future KM-owned native menu, its persistence lifecycle, or physical hardware canaries
    /// have completed stable runtime validation.
    /// </summary>
    public bool CanExposeExactBuildBetaControl(GameplaySettingsFamily family)
    {
        return StorageKind == GameplayRuntimeControlStorageKind.GameplaySettingsJournalSchema1
            && JournalPresence is not null
            && ForFamily(family).ExactBuildBetaDeliverySupported;
    }
}

/// <summary>
/// Versioned semantic contract for KM-owned in-game controls. A modeled descriptor records the
/// intended value and menu shape only. It never authorizes a writer or visible in-game row. Runtime
/// stable owned-menu exposure remains fail-closed until the exact family binding is runtime validated
/// and has assigned durable storage. The separately gated exact-build beta channel may expose only
/// consumer-mapped controls while retaining its explicit hardware-validation caveats.
/// </summary>
public static class GameplayRuntimeControlCatalog
{
    public const ushort SchemaVersion = 1;
    public const string CatalogId = "km-gameplay-runtime-controls";

    private const string OmitControl = "control-omitted-and-no-writer";

    private static readonly IReadOnlyList<GameplayRuntimeControlDefinition> Definitions =
        CreateDefinitions();

    public static IReadOnlyList<GameplayRuntimeControlDefinition> All => Definitions;

    public static string SchemaFingerprint { get; } = ComputeSchemaFingerprint(Definitions);

    public static GameplayRuntimeControlDefinition Get(GameplayRuntimeControlId id)
    {
        return Definitions.Single(definition => definition.Id == id);
    }

    public static IReadOnlyList<GameplayRuntimeControlDefinition> ForFamily(
        GameplaySettingsFamily family)
    {
        ValidateFamily(family);
        return Array.AsReadOnly(Definitions
            .Where(definition => definition.ForFamily(family).Applicability
                == GameplayRuntimeControlApplicability.Applicable)
            .ToArray());
    }

    private static IReadOnlyList<GameplayRuntimeControlDefinition> CreateDefinitions()
    {
        var share = Toggle(defaultValue: 1);
        var rate = BoundedInteger(0, 50_000, 1_000, "basis-points", defaultValue: 10_000);
        var cap = BoundedInteger(
            0,
            100,
            1,
            "level-or-off",
            defaultValue: 0,
            Choice(0, "off", "Off"));
        var picnicExperience = Toggle(defaultValue: 1);
        var shinyNotifications = ChoiceDomain(
            defaultValue: 0,
            "notice-mode",
            Choice(0, "off", "Off"),
            Choice(1, "subtle", "Subtle"),
            Choice(2, "full", "Full"));
        var nearbySpawnLimit = ChoiceDomain(
            defaultValue: 15,
            "actor-count",
            Choice(15, "retail", "Retail"),
            Choice(20, "twenty", "20"),
            Choice(30, "thirty", "30"),
            Choice(40, "forty", "40"));
        var animationRateProfile = ChoiceDomain(
            defaultValue: 0,
            "animation-profile",
            Choice(0, "retail", "Retail"),
            Choice(1, "high", "High"),
            Choice(2, "medium", "Medium"),
            Choice(3, "low", "Low"),
            Choice(4, "minimum", "Minimum"));
        var modelDetail = ChoiceDomain(
            defaultValue: 0,
            "detail-profile",
            Choice(0, "retail", "Retail"),
            Choice(1, "high", "High"),
            Choice(2, "medium", "Medium"),
            Choice(3, "low", "Low"));
        var battleCamera = ChoiceDomain(
            defaultValue: 0,
            "camera-mode",
            Choice(0, "retail", "Retail"),
            Choice(1, "battle", "Battle camera"),
            Choice(2, "free", "Free camera"));
        var autoBattleEvRecipients = ChoiceDomain(
            defaultValue: 0,
            "recipient-mode",
            Choice(0, "disabled", "Disabled"),
            Choice(1, "leader", "Lead Pokemon"),
            Choice(2, "party", "Party"));
        var retailToggle = Toggle(defaultValue: 0);
        var standardLanguages = ChoiceDomain(
            GameplayRuntimeControlDefaultSource.PreserveRetailValue,
            "language",
            Choice(0, "japanese", "Japanese"),
            Choice(1, "english", "English"),
            Choice(2, "spanish", "Spanish"),
            Choice(3, "french", "French"),
            Choice(4, "german", "German"),
            Choice(5, "italian", "Italian"),
            Choice(6, "korean", "Korean"),
            Choice(7, "simplified-chinese", "Simplified Chinese"),
            Choice(8, "traditional-chinese", "Traditional Chinese"));
        var zaLanguages = ChoiceDomain(
            GameplayRuntimeControlDefaultSource.PreserveRetailValue,
            "language",
            Choice(0, "japanese", "Japanese"),
            Choice(1, "english", "English"),
            Choice(2, "spanish", "Spanish"),
            Choice(3, "latin-american-spanish", "Latin American Spanish"),
            Choice(4, "french", "French"),
            Choice(5, "german", "German"),
            Choice(6, "italian", "Italian"),
            Choice(7, "korean", "Korean"),
            Choice(8, "simplified-chinese", "Simplified Chinese"),
            Choice(9, "traditional-chinese", "Traditional Chinese"));

        var definitions = new[]
        {
            CoreDefinition(
                GameplayRuntimeControlId.ExperienceShare,
                "experience-share",
                "Experience Share",
                GameplaySettingPresence.ExperienceShare,
                CoreBindings(
                    share,
                    GameplayRuntimeControlProofState.ExactConsumerMapped,
                    "exp-share-runtime-delivery-proof-missing",
                    "runtime-hook-canary",
                    "menu-lifecycle-canary",
                    "journal-durability-canary",
                    "package-handshake-canary")),
            CoreDefinition(
                GameplayRuntimeControlId.ExperienceRate,
                "experience-rate",
                "Experience Rate",
                GameplaySettingPresence.ExperienceRate,
                CoreBindings(
                    rate,
                    GameplayRuntimeControlProofState.ExactConsumerMapped,
                    "exp-rate-runtime-delivery-proof-missing",
                    "runtime-hook-canary",
                    "source-coverage-canary",
                    "menu-lifecycle-canary",
                    "journal-durability-canary",
                    "package-handshake-canary")),
            CoreDefinition(
                GameplayRuntimeControlId.LevelCap,
                "level-cap",
                "Supported EXP Level Cap",
                GameplaySettingPresence.LevelCap,
                CoreBindings(
                    cap,
                    GameplayRuntimeControlProofState.PartialConsumerMap,
                    "level-cap-runtime-delivery-proof-missing",
                    "growth-helper-abi-canary",
                    "all-exp-source-census",
                    "candy-consumption-safety-canary",
                    "menu-lifecycle-canary",
                    "journal-durability-canary",
                    "package-handshake-canary")),
            DeferredDefinition(
                GameplayRuntimeControlId.PicnicExperience,
                "picnic-experience",
                "Picnic EXP Rewards",
                GameplayRuntimeControlSection.Progression,
                GameplayDeferredFeatureId.PicnicExperience,
                _ => picnicExperience),
            DeferredDefinition(
                GameplayRuntimeControlId.ShinyNotifications,
                "shiny-notifications",
                "Shiny Notifications",
                GameplayRuntimeControlSection.Encounters,
                GameplayDeferredFeatureId.ShinyLifecycleNotices,
                _ => shinyNotifications),
            DeferredDefinition(
                GameplayRuntimeControlId.NearbySpawnLimit,
                "nearby-spawn-limit",
                "Nearby Spawn Limit",
                GameplayRuntimeControlSection.Encounters,
                GameplayDeferredFeatureId.SpawnPopulationControls,
                _ => nearbySpawnLimit),
            DeferredDefinition(
                GameplayRuntimeControlId.AnimationRateProfile,
                "animation-rate-profile",
                "Animation Rate Profile",
                GameplayRuntimeControlSection.Presentation,
                GameplayDeferredFeatureId.StaticAnimationControls,
                _ => animationRateProfile),
            DeferredDefinition(
                GameplayRuntimeControlId.WildPokemonDetail,
                "wild-pokemon-detail",
                "Wild Pokemon Detail",
                GameplayRuntimeControlSection.Presentation,
                GameplayDeferredFeatureId.StaticLodControls,
                _ => modelDetail),
            DeferredDefinition(
                GameplayRuntimeControlId.CaptureProgressBonuses,
                "capture-progress-bonuses",
                "Capture Progress Bonuses",
                GameplayRuntimeControlSection.Encounters,
                GameplayDeferredFeatureId.CaptureBonuses,
                _ => retailToggle),
            DeferredDefinition(
                GameplayRuntimeControlId.BattleCameraMode,
                "battle-camera-mode",
                "Battle Camera Mode",
                GameplayRuntimeControlSection.Presentation,
                GameplayDeferredFeatureId.MapPresentationControls,
                _ => battleCamera),
            DeferredDefinition(
                GameplayRuntimeControlId.NearCameraModelFade,
                "near-camera-model-fade",
                "Near Camera Model Fade",
                GameplayRuntimeControlSection.Presentation,
                GameplayDeferredFeatureId.StaticLodControls,
                _ => retailToggle),
            DeferredDefinition(
                GameplayRuntimeControlId.StaticNpcDetail,
                "static-npc-detail",
                "Static NPC Detail",
                GameplayRuntimeControlSection.Presentation,
                GameplayDeferredFeatureId.StaticLodControls,
                _ => modelDetail),
            DeferredDefinition(
                GameplayRuntimeControlId.WanderingNpcDetail,
                "wandering-npc-detail",
                "Wandering NPC Detail",
                GameplayRuntimeControlSection.Presentation,
                GameplayDeferredFeatureId.StaticLodControls,
                _ => modelDetail),
            DeferredDefinition(
                GameplayRuntimeControlId.AutoBattleEvRecipients,
                "auto-battle-ev-recipients",
                "Auto Battle EV Recipients",
                GameplayRuntimeControlSection.Progression,
                GameplayDeferredFeatureId.AutoBattleEvs,
                _ => autoBattleEvRecipients),
            DeferredDefinition(
                GameplayRuntimeControlId.ColorProfile,
                "color-profile",
                "Color Profile",
                GameplayRuntimeControlSection.Presentation,
                GameplayDeferredFeatureId.StaticColorControls,
                _ => retailToggle),
            DeferredDefinition(
                GameplayRuntimeControlId.GameLanguage,
                "game-language",
                "Game Language",
                GameplayRuntimeControlSection.System,
                GameplayDeferredFeatureId.LanguageSwitching,
                family => family == GameplaySettingsFamily.LegendsZA
                    ? zaLanguages
                    : standardLanguages),
        };

        ValidateDefinitions(definitions);
        return Array.AsReadOnly(definitions);
    }

    private static GameplayRuntimeControlDefinition CoreDefinition(
        GameplayRuntimeControlId id,
        string stableId,
        string displayName,
        GameplaySettingPresence journalPresence,
        IReadOnlyList<GameplayRuntimeFamilyControlDescriptor> bindings)
    {
        return new GameplayRuntimeControlDefinition(
            id,
            stableId,
            displayName,
            GameplayRuntimeControlSection.Progression,
            GameplayRuntimeControlStorageKind.GameplaySettingsJournalSchema1,
            journalPresence,
            DeferredFeature: null,
            bindings);
    }

    private static GameplayRuntimeControlDefinition DeferredDefinition(
        GameplayRuntimeControlId id,
        string stableId,
        string displayName,
        GameplayRuntimeControlSection section,
        GameplayDeferredFeatureId deferredFeature,
        Func<GameplaySettingsFamily, GameplayRuntimeControlValueDomain> domainFactory)
    {
        return new GameplayRuntimeControlDefinition(
            id,
            stableId,
            displayName,
            section,
            GameplayRuntimeControlStorageKind.Unassigned,
            JournalPresence: null,
            deferredFeature,
            Enum.GetValues<GameplaySettingsFamily>()
                .Select(family => DeferredBinding(
                    family,
                    deferredFeature,
                    domainFactory(family)))
                .ToArray());
    }

    private static IReadOnlyList<GameplayRuntimeFamilyControlDescriptor> CoreBindings(
        GameplayRuntimeControlValueDomain domain,
        GameplayRuntimeControlProofState proofState,
        string reasonSuffix,
        params string[] missingProof)
    {
        return Array.AsReadOnly(Enum.GetValues<GameplaySettingsFamily>()
            .Select(family => new GameplayRuntimeFamilyControlDescriptor(
                family,
                GameplayRuntimeControlApplicability.Applicable,
                PlannedRoute(family),
                proofState,
                $"{FamilyToken(family)}-{reasonSuffix}",
                OmitControl,
                domain,
                Array.AsReadOnly(missingProof
                    .Select(proof => $"{FamilyToken(family)}-{proof}")
                    .ToArray())))
            .ToArray());
    }

    private static GameplayRuntimeFamilyControlDescriptor DeferredBinding(
        GameplaySettingsFamily family,
        GameplayDeferredFeatureId feature,
        GameplayRuntimeControlValueDomain domain)
    {
        var assessment = GameplayDeferredFeatureCatalog.ForFamily(family)
            .Single(candidate => candidate.Feature == feature);
        if (assessment.State == GameplayDeferredFeatureState.NotApplicable)
        {
            return new GameplayRuntimeFamilyControlDescriptor(
                family,
                GameplayRuntimeControlApplicability.NotApplicable,
                GameplayRuntimeMenuRoute.None,
                GameplayRuntimeControlProofState.NotApplicable,
                assessment.ReasonCode,
                assessment.FailClosedBehavior,
                ValueDomain: null,
                assessment.MissingProof);
        }

        return new GameplayRuntimeFamilyControlDescriptor(
            family,
            GameplayRuntimeControlApplicability.Applicable,
            PlannedRoute(family),
            GameplayRuntimeControlProofState.ContractModeled,
            assessment.ReasonCode,
            assessment.FailClosedBehavior,
            domain,
            assessment.MissingProof);
    }

    private static GameplayRuntimeControlValueDomain Toggle(int defaultValue)
    {
        return CreateDomain(
            GameplayRuntimeControlValueKind.Toggle,
            0,
            1,
            1,
            "boolean",
            GameplayRuntimeControlDefaultSource.FixedValue,
            defaultValue,
            Choice(0, "disabled", "Disabled"),
            Choice(1, "enabled", "Enabled"));
    }

    private static GameplayRuntimeControlValueDomain BoundedInteger(
        int minimum,
        int maximum,
        int step,
        string unitCode,
        int defaultValue,
        params GameplayRuntimeControlChoice[] namedValues)
    {
        return CreateDomain(
            GameplayRuntimeControlValueKind.BoundedInteger,
            minimum,
            maximum,
            step,
            unitCode,
            GameplayRuntimeControlDefaultSource.FixedValue,
            defaultValue,
            namedValues);
    }

    private static GameplayRuntimeControlValueDomain ChoiceDomain(
        int defaultValue,
        string unitCode,
        params GameplayRuntimeControlChoice[] choices)
    {
        return CreateDomain(
            GameplayRuntimeControlValueKind.Choice,
            choices.Min(choice => choice.Value),
            choices.Max(choice => choice.Value),
            1,
            unitCode,
            GameplayRuntimeControlDefaultSource.FixedValue,
            defaultValue,
            choices);
    }

    private static GameplayRuntimeControlValueDomain ChoiceDomain(
        GameplayRuntimeControlDefaultSource defaultSource,
        string unitCode,
        params GameplayRuntimeControlChoice[] choices)
    {
        return CreateDomain(
            GameplayRuntimeControlValueKind.Choice,
            choices.Min(choice => choice.Value),
            choices.Max(choice => choice.Value),
            1,
            unitCode,
            defaultSource,
            defaultValue: null,
            choices);
    }

    private static GameplayRuntimeControlValueDomain CreateDomain(
        GameplayRuntimeControlValueKind kind,
        int minimum,
        int maximum,
        int step,
        string unitCode,
        GameplayRuntimeControlDefaultSource defaultSource,
        int? defaultValue,
        params GameplayRuntimeControlChoice[] namedValues)
    {
        var domain = new GameplayRuntimeControlValueDomain(
            kind,
            minimum,
            maximum,
            step,
            unitCode,
            defaultSource,
            defaultValue,
            Array.AsReadOnly(namedValues.ToArray()));
        ValidateDomain(domain);
        return domain;
    }

    private static GameplayRuntimeControlChoice Choice(
        int value,
        string stableId,
        string displayName)
    {
        return new GameplayRuntimeControlChoice(value, stableId, displayName);
    }

    private static GameplayRuntimeMenuRoute PlannedRoute(GameplaySettingsFamily family)
    {
        return family switch
        {
            GameplaySettingsFamily.ScarletViolet =>
                GameplayRuntimeMenuRoute.StockOptionsExtension,
            GameplaySettingsFamily.SwordShield =>
                GameplayRuntimeMenuRoute.ScriptedSettingsPage,
            GameplaySettingsFamily.LegendsZA =>
                GameplayRuntimeMenuRoute.OwnedSettingsPage,
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
    }

    private static string FamilyToken(GameplaySettingsFamily family)
    {
        return family switch
        {
            GameplaySettingsFamily.ScarletViolet => "sv",
            GameplaySettingsFamily.SwordShield => "swsh",
            GameplaySettingsFamily.LegendsZA => "za",
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
    }

    private static void ValidateDefinitions(
        IReadOnlyList<GameplayRuntimeControlDefinition> definitions)
    {
        var expectedIds = Enum.GetValues<GameplayRuntimeControlId>();
        var expectedFamilies = Enum.GetValues<GameplaySettingsFamily>();
        var journalMappings = new Dictionary<GameplayRuntimeControlId, GameplaySettingPresence>
        {
            [GameplayRuntimeControlId.ExperienceShare] = GameplaySettingPresence.ExperienceShare,
            [GameplayRuntimeControlId.ExperienceRate] = GameplaySettingPresence.ExperienceRate,
            [GameplayRuntimeControlId.LevelCap] = GameplaySettingPresence.LevelCap,
        };
        var exactBuildBetaIds = journalMappings.Keys.ToHashSet();

        if (definitions.Count != expectedIds.Length
            || !definitions.Select(definition => definition.Id).SequenceEqual(expectedIds)
            || definitions.Select(definition => definition.StableId).Distinct(StringComparer.Ordinal).Count()
                != definitions.Count
            || definitions.Any(definition => !IsStableToken(definition.StableId))
            || definitions.Any(definition => string.IsNullOrWhiteSpace(definition.DisplayName))
            || definitions.Any(definition => definition.Families.Count != expectedFamilies.Length)
            || definitions.Any(definition => !definition.Families
                .Select(binding => binding.Family)
                .SequenceEqual(expectedFamilies))
            || definitions.Any(definition => !HasValidStorage(definition, journalMappings))
            || definitions.Any(definition => definition.Families.Any(binding =>
                !HasValidBinding(binding)))
            || definitions.Any(definition => definition.Families.Any(binding =>
                binding.RuntimeDeliverySupported))
            || definitions.Any(definition => expectedFamilies.Any(definition.CanExposeControl))
            || definitions.Any(definition => expectedFamilies.Any(family =>
                definition.CanExposeExactBuildBetaControl(family)
                    != exactBuildBetaIds.Contains(definition.Id))))
        {
            throw new InvalidOperationException(
                "The gameplay runtime control catalog is incomplete or invalid.");
        }
    }

    private static bool HasValidStorage(
        GameplayRuntimeControlDefinition definition,
        IReadOnlyDictionary<GameplayRuntimeControlId, GameplaySettingPresence> journalMappings)
    {
        if (definition.StorageKind == GameplayRuntimeControlStorageKind.GameplaySettingsJournalSchema1)
        {
            return definition.DeferredFeature is null
                && definition.JournalPresence is not null
                && journalMappings.TryGetValue(definition.Id, out var expectedPresence)
                && definition.JournalPresence == expectedPresence;
        }

        return definition.StorageKind == GameplayRuntimeControlStorageKind.Unassigned
            && definition.JournalPresence is null
            && definition.DeferredFeature is not null
            && !journalMappings.ContainsKey(definition.Id);
    }

    private static bool HasValidBinding(GameplayRuntimeFamilyControlDescriptor binding)
    {
        if (!IsStableToken(binding.ReasonCode)
            || !IsStableToken(binding.FailClosedBehavior)
            || binding.MissingProof.Count > 8
            || binding.MissingProof.Any(proof => !IsStableToken(proof)))
        {
            return false;
        }

        if (binding.Applicability == GameplayRuntimeControlApplicability.NotApplicable)
        {
            return binding.PlannedMenuRoute == GameplayRuntimeMenuRoute.None
                && binding.ProofState == GameplayRuntimeControlProofState.NotApplicable
                && binding.ValueDomain is null
                && binding.MissingProof.Count == 0;
        }

        return binding.PlannedMenuRoute != GameplayRuntimeMenuRoute.None
            && binding.ProofState != GameplayRuntimeControlProofState.NotApplicable
            && binding.ValueDomain is not null;
    }

    private static void ValidateDomain(GameplayRuntimeControlValueDomain domain)
    {
        var namedValues = domain.NamedValues;
        if (domain.MinimumValue > domain.MaximumValue
            || domain.Step <= 0
            || !IsStableToken(domain.UnitCode)
            || namedValues.Select(choice => choice.Value).Distinct().Count() != namedValues.Count
            || namedValues.Select(choice => choice.StableId).Distinct(StringComparer.Ordinal).Count()
                != namedValues.Count
            || namedValues.Any(choice => !IsStableToken(choice.StableId)
                || string.IsNullOrWhiteSpace(choice.DisplayName)
                || choice.Value < domain.MinimumValue
                || choice.Value > domain.MaximumValue)
            || domain.Kind == GameplayRuntimeControlValueKind.Toggle
                && (domain.MinimumValue != 0
                    || domain.MaximumValue != 1
                    || domain.Step != 1
                    || namedValues.Count != 2)
            || domain.Kind == GameplayRuntimeControlValueKind.Choice
                && namedValues.Count == 0
            || domain.DefaultSource == GameplayRuntimeControlDefaultSource.FixedValue
                && (domain.DefaultValue is null || !domain.Accepts(domain.DefaultValue.Value))
            || domain.DefaultSource == GameplayRuntimeControlDefaultSource.PreserveRetailValue
                && domain.DefaultValue is not null)
        {
            throw new InvalidOperationException("A gameplay runtime control value domain is invalid.");
        }
    }

    private static string ComputeSchemaFingerprint(
        IReadOnlyList<GameplayRuntimeControlDefinition> definitions)
    {
        var canonical = new StringBuilder();
        canonical.Append(CatalogId)
            .Append('|')
            .Append(SchemaVersion.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (var definition in definitions)
        {
            canonical.Append((ushort)definition.Id).Append('|')
                .Append(definition.StableId).Append('|')
                .Append((int)definition.Section).Append('|')
                .Append((int)definition.StorageKind).Append('|')
                .Append(definition.JournalPresence is null
                    ? "none"
                    : ((ulong)definition.JournalPresence.Value).ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(definition.DeferredFeature?.ToString() ?? "none")
                .Append('\n');
            foreach (var binding in definition.Families)
            {
                canonical.Append((ushort)binding.Family).Append('|')
                    .Append((int)binding.Applicability).Append('|')
                    .Append((int)binding.PlannedMenuRoute).Append('|')
                    .Append((int)binding.ProofState).Append('|')
                    .Append(binding.ReasonCode).Append('|')
                    .Append(binding.FailClosedBehavior).Append('|');
                if (binding.ValueDomain is null)
                {
                    canonical.Append("none");
                }
                else
                {
                    var domain = binding.ValueDomain;
                    canonical.Append((int)domain.Kind).Append(',')
                        .Append(domain.MinimumValue.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(domain.MaximumValue.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(domain.Step.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(domain.UnitCode).Append(',')
                        .Append((int)domain.DefaultSource).Append(',')
                        .Append(domain.DefaultValue?.ToString(CultureInfo.InvariantCulture) ?? "retail");
                    foreach (var choice in domain.NamedValues)
                    {
                        canonical.Append(',')
                            .Append(choice.Value.ToString(CultureInfo.InvariantCulture)).Append(':')
                            .Append(choice.StableId);
                    }
                }

                canonical.Append('|')
                    .AppendJoin(',', binding.MissingProof)
                    .Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static bool IsStableToken(string value)
    {
        return value is { Length: > 0 and <= 128 }
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static void ValidateFamily(GameplaySettingsFamily family)
    {
        if (!Enum.IsDefined(family))
        {
            throw new ArgumentOutOfRangeException(nameof(family));
        }
    }
}
