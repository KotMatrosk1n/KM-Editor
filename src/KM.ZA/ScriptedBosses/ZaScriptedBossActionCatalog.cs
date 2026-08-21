// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Moves;
using KM.ZA.Workflows;

namespace KM.ZA.ScriptedBosses;

public sealed record ZaScriptedBossPhaseRecord(
    string Key,
    int Stage,
    int HpPhase,
    int SpeciesId,
    int Form,
    string StageName,
    int MinimumHpPercent,
    int MaximumHpPercent);

public sealed record ZaScriptedBossPhaseModelRecord(
    string State,
    string Kind,
    IReadOnlyList<ZaScriptedBossPhaseRecord> Phases);

public sealed record ZaScriptedBossPhaseAvailabilityRecord(
    string PhaseKey,
    string State);

public sealed record ZaScriptedBossActionRecord(
    string Key,
    string Kind,
    int? SelectorActionId,
    int? MoveId,
    int? VanillaMoveId,
    int? RuntimeMoveId,
    int? Variant,
    string Name,
    bool UsesBattleParameters,
    bool UsesTimingParameters,
    bool CanEdit,
    string RuntimeState,
    string CompatibilityState,
    string? CompatibilityReason,
    string? LockReason,
    IReadOnlyList<ZaScriptedBossPhaseAvailabilityRecord> PhaseAvailability,
    string? PhaseContext)
{
    public IReadOnlyList<ZaScriptedBossAffectedScopeRecord> AffectedScopes { get; init; } = [];
}

public sealed record ZaScriptedBossProfileRecord(
    string Key,
    string LineageKey,
    int SpeciesId,
    int Form,
    string Name,
    string Scope,
    ZaScriptedBossPhaseModelRecord PhaseModel,
    IReadOnlyList<ZaScriptedBossActionRecord> Actions);

public sealed record ZaScriptedBossMoveOptionRecord(
    int MoveId,
    int RuntimeMoveId,
    int Variant,
    string Name,
    string DefaultCompatibilityState,
    IReadOnlyList<ZaScriptedBossMoveCompatibilityRecord> SelectorCompatibilities);

public sealed record ZaScriptedBossMoveCompatibilityRecord(
    int SelectorActionId,
    string State,
    string? Reason);

internal sealed record ZaScriptedBossCatalogProjection(
    IReadOnlyList<ZaScriptedBossProfileRecord> Profiles,
    IReadOnlyList<ZaScriptedBossMoveOptionRecord> MoveOptions,
    int SourceFileCount,
    bool HasSelectorSource);

internal static class ZaScriptedBossActionCatalog
{
    public const string BattleMoveKind = "battle-move";
    public const string MovementHelperKind = "movement-helper";
    public const string ScriptedMechanicKind = "scripted-mechanic";
    public const string BaseRogueMegaScope = "base-rogue-mega";
    public const string VerifiedScriptedBossScope = "verified-scripted-boss";
    public const string VerifiedScriptedFollowerScope = "verified-scripted-follower";

    public const string RuntimeDataPresentRuntimeState = "runtime-data-present";
    public const string MissingBattleRuntimeState = "missing-battle";
    public const string MissingTimingRuntimeState = "missing-timing";
    public const string MissingBattleAndTimingRuntimeState = "missing-battle-and-timing";
    public const string TimingOnlyRuntimeState = "timing-only";
    public const string InvalidReferenceRuntimeState = "invalid-reference";
    public const string UnavailableRuntimeState = "unavailable";
    public const string NotApplicableRuntimeState = "not-applicable";

    public const string BaseVerifiedCompatibilityState = "base-verified";
    public const string GameplayTestedCompatibilityState = "gameplay-tested";
    public const string KnownIncompatibleCompatibilityState = "known-incompatible";
    public const string ExperimentalCompatibilityState = "experimental";
    public const string UnavailableCompatibilityState = "unavailable";
    public const string NotApplicableCompatibilityState = "not-applicable";

    public const string NoDamageCompatibilityReason = "no-damage";
    public const string AllyTargetingCompatibilityReason = "ally-targeting";

    public const string VerifiedPhaseModelState = "verified";
    public const string AvailablePhaseState = "available";
    public const string ContextOnlyPhaseState = "context-only";
    public const string UnavailablePhaseState = "unavailable";
    public const string UnverifiedPhaseState = "unverified";

    public const string HpBandsPhaseModelKind = "hp-bands";
    public const string BattleStagesPhaseModelKind = "battle-stages";
    public const string BattleStagesWithHpBandsPhaseModelKind =
        "battle-stages-with-hp-bands";

    public const string ControllerScriptLockReason = "controller-script";
    public const string TimingChoreographyLockReason = "timing-choreography";
    public const string SelectorUnavailableLockReason = "selector-unavailable";
    public const string RuntimeCatalogUnavailableLockReason = "runtime-catalog-unavailable";

    private const int BossMoveOffset = 2000;
    public const int MaximumBaseMoveId = 999;
    private const int NormalMoveVariant = 0;
    private const int PlusMoveVariant = 1;
    private const int BossMoveVariant = 2;

    // Gameplay evidence is recorded per boss profile, then aggregated across the exact owner set
    // of each selector. A move tested for one boss must not be promoted for an untested shared owner.
    private static readonly IReadOnlyDictionary<(string ProfileKey, int MoveId), CompatibilityEvidence>
        ReviewedCompatibilityEvidence =
            new Dictionary<(string ProfileKey, int MoveId), CompatibilityEvidence>
            {
                [("323:1", 53)] = new(
                    GameplayTestedCompatibilityState,
                    Reason: null), // Camerupt + Flamethrower
                [("323:1", 126)] = new(
                    KnownIncompatibleCompatibilityState,
                    NoDamageCompatibilityReason), // Camerupt + Fire Blast
                [("71:1", 482)] = new(
                    GameplayTestedCompatibilityState,
                    Reason: null), // Victreebel + Sludge Wave
                [("71:1", 398)] = new(
                    KnownIncompatibleCompatibilityState,
                    AllyTargetingCompatibilityReason), // Victreebel + Poison Jab
            };
    // Actual boss spawners replace the executable's fallback thresholds. Ordinary encounters use
    // two HP phases (50 < HP <= 100 and 0 < HP <= 50), while multi-form encounters can define a
    // separate schedule for each chained battle stage. Availability is therefore profile- and
    // phase-local; it must never be inferred from a shared selector ID alone.
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<int>> VerifiedPhaseScheduleMoves =
        new Dictionary<string, IReadOnlySet<int>>(StringComparer.Ordinal)
        {
            ["3:1"] = new HashSet<int> { 72, 73, 76, 331, 438, 482 },
            ["15:1"] = new HashSet<int> { 42, 398, 679 },
            ["71:1"] = new HashSet<int> { 22, 188, 331 },
            ["80:1"] = new HashSet<int> { 60, 250, 352 },
            ["121:1"] = new HashSet<int> { 56, 339, 352, 428, 453 },
            ["149:1"] = new HashSet<int> { 19, 53, 63, 85, 200, 340, 403, 407, 542 },
            ["181:1"] = new HashSet<int> { 87, 268, 406, 435, 784 },
            ["248:1"] = new HashSet<int> { 89, 157, 200, 242, 328, 416, 444 },
            ["303:1"] = new HashSet<int> { 14, 98, 242, 442, 583, 605 },
            ["323:1"] = new HashSet<int> { 29, 414 },
            ["334:1"] = new HashSet<int> { 239, 340, 406, 413, 585 },
            ["354:1"] = new HashSet<int> { 109, 247, 421, 425, 566 },
            ["359:1"] = new HashSet<int> { 163, 399, 403 },
            ["478:1"] = new HashSet<int> { 59, 196, 247, 261, 556, 566 },
            ["689:1"] = new HashSet<int> { 127, 157, 370, 612 },
            ["701:1"] = new HashSet<int> { 280, 332, 403, 560 },
        };

    private static readonly IReadOnlySet<(string ProfileKey, int MoveId)> ContextOnlyActions =
        new HashSet<(string ProfileKey, int MoveId)>
        {
            ("303:1", 14), // Mawile: Swords Dance is invoked by after-stun choreography.
        };

    private static readonly IReadOnlySet<(string ProfileKey, int MoveId)> Phase2OnlyActions =
        new HashSet<(string ProfileKey, int MoveId)>
        {
            ("3:1", 482),   // Venusaur: Sludge Wave
            ("121:1", 56),  // Starmie: Hydro Pump
            ("121:1", 339), // Starmie: Bulk Up
            ("149:1", 19),  // Dragonite: Fly
            ("149:1", 63),  // Dragonite: Hyper Beam
            ("149:1", 407), // Dragonite: Dragon Rush
            ("181:1", 268), // Ampharos: Charge
            ("248:1", 89),  // Tyranitar: Earthquake
            ("248:1", 200), // Tyranitar: Outrage
            ("248:1", 416), // Tyranitar: Giga Impact
            ("334:1", 340), // Altaria: Bounce timing choreography
            ("334:1", 413), // Altaria: Brave Bird
            ("701:1", 280), // Hawlucha: Brick Break
        };

    private static readonly IReadOnlySet<(string ProfileKey, int MoveId)> Phase1OnlyActions =
        new HashSet<(string ProfileKey, int MoveId)>
        {
            ("149:1", 85), // Dragonite: Thunderbolt
        };

    private static readonly IReadOnlyDictionary<int, int> SelectorActionIds =
        new Dictionary<int, int>
        {
            [14] = 17621,
            [19] = 20451,
            [22] = 17566,
            [29] = 17567,
            [42] = 17585,
            [53] = 20456,
            [56] = 22265,
            [59] = 19951,
            [60] = 17569,
            [63] = 20452,
            [72] = 17532,
            [73] = 17537,
            [76] = 17531,
            [85] = 20453,
            [87] = 17624,
            [89] = 20951,
            [98] = 17570,
            [109] = 17631,
            [127] = 17639,
            [157] = 17640,
            [163] = 17571,
            [188] = 17572,
            [196] = 19952,
            [200] = 20952,
            [239] = 20201,
            [242] = 17623,
            [247] = 17617,
            [250] = 17573,
            [261] = 19953,
            [268] = 17625,
            [280] = 17635,
            [328] = 20955,
            [331] = 17574,
            [332] = 17636,
            [339] = 22264,
            [340] = 20205,
            [352] = 17575,
            [370] = 17641,
            [398] = 17576,
            [399] = 17577,
            [403] = 17650,
            [406] = 17626,
            [407] = 20454,
            [413] = 20203,
            [414] = 17578,
            [416] = 20953,
            [421] = 17616,
            [425] = 17618,
            [428] = 22262,
            [435] = 17627,
            [438] = 22260,
            [442] = 17695,
            [444] = 20954,
            [453] = 22263,
            [482] = 22261,
            [542] = 20455,
            [556] = 19954,
            [560] = 17637,
            [566] = 17634,
            [583] = 17622,
            [585] = 20204,
            [605] = 17620,
            [612] = 22253,
            [679] = 17580,
            [784] = 17628,
        };

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
            ContextOnlyScriptedMechanic(
                "volcanic-eruption",
                "Volcanic eruption sequence",
                "bomb-rock-deployed",
                1)),
        Profile(71, 1,
            BattleMove(22),
            BattleMove(188),
            BattleMove(331)),
        Profile(15, 1,
            BattleMove(42),
            BattleMove(398),
            BattleMove(679)),
        ScriptedFollowerProfile(
            "14:0:boss_0015:follower",
            "boss_0015_follower_kakuna",
            14,
            0,
            SelectorBattleMove(22312, 106, NormalMoveVariant),
            SelectorBattleMove(17629, 81, NormalMoveVariant),
            SelectorBattleMove(17536, 40, NormalMoveVariant)),
        ScriptedFollowerProfile(
            "15:0:boss_0015:follower",
            "boss_0015_follower_beedrill",
            15,
            0,
            SelectorBattleMove(17629, 81, NormalMoveVariant),
            SelectorBattleMove(17536, 40, NormalMoveVariant),
            SelectorBattleMove(22313, 42, NormalMoveVariant)),
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
            ScriptedMechanic("clone-sequence", "Double Team clone sequence", 1)),
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
        ScriptedBossProfile("359:2:z", "boss_0359_z_01", 359, 2,
            HpBandPhases(359, 2),
            SelectorBattleMove(29701, 122),
            SelectorBattleMove(29702, 282),
            SelectorBattleMove(31205, 555),
            SelectorBattleMove(17618, 425)),
        ScriptedBossProfile("382:controller", "boss_0382", 382, 0,
            FullStagePhases((382, 0), (382, 1)),
            SelectorBattleMove(29951, 618, BossMoveVariant, 2),
            SelectorBattleMove(29952, 57, BossMoveVariant, 1, 2),
            SelectorBattleMove(17624, 87, BossMoveVariant, 1, 2) with
            {
                ContextOnlyStages = new HashSet<int> { 1 },
            },
            SelectorBattleMove(31952, 58, BossMoveVariant, 1, 2)),
        ScriptedBossProfile("383:controller", "boss_0383", 383, 0,
            FullStagePhases((383, 0), (383, 1)),
            SelectorBattleMove(30201, 619, BossMoveVariant, 2),
            SelectorBattleMove(30202, 815, BossMoveVariant, 1, 2),
            SelectorBattleMove(30203, 126, BossMoveVariant, 1, 2),
            SelectorBattleMove(17531, 76, BossMoveVariant, 1, 2) with
            {
                ContextOnlyStages = new HashSet<int> { 1 },
            }),
        ScriptedBossProfile("384:controller", "boss_0384", 384, 0,
            FullStagePhases((384, 0), (384, 1)),
            SelectorBattleMove(30451, 434, BossMoveVariant, 1, 2),
            SelectorBattleMove(30452, 620, BossMoveVariant, 2),
            SelectorBattleMove(30453, 800, BossMoveVariant, 1, 2),
            SelectorBattleMove(20952, 200, BossMoveVariant, 1, 2),
            SelectorBattleMove(20202, 304, BossMoveVariant, 1, 2)),
        ScriptedBossProfile("398:1:controller", "boss_0398", 398, 1,
            HpBandPhases(398, 1),
            SelectorBattleMove(30701, 183),
            SelectorBattleMove(30702, 814),
            SelectorBattleMove(30703, 411),
            SelectorBattleMove(30704, 38),
            SelectorMovementHelper(20205, 340)),
        ScriptedBossProfile("485:1:controller", "boss_0485", 485, 1,
            HpBandPhases(485, 1),
            SelectorBattleMove(30951, 315),
            SelectorBattleMove(30952, 463),
            SelectorBattleMove(30953, 430),
            SelectorBattleMove(30954, 523)),
        ScriptedBossProfile("491:controller", "boss_0491", 491, 0,
            FullStagePhases((491, 0), (491, 1)),
            SelectorBattleMove(31201, 464, BossMoveVariant, 1, 2),
            SelectorBattleMove(31202, 44, BossMoveVariant, 2),
            SelectorBattleMove(31203, 693, BossMoveVariant, 1, 2),
            SelectorBattleMove(31204, 248, BossMoveVariant, 1, 2),
            SelectorBattleMove(17634, 566, BossMoveVariant, 1, 2),
            SelectorBattleMove(31205, 555, BossMoveVariant, 1, 2),
            SelectorBattleMove(17617, 247, BossMoveVariant, 2),
            ScriptedMechanic(
                "darkrai-nightmare-sequence",
                "Nightmare sequence",
                1,
                2),
            ScriptedMechanic("darkrai-clone-sequence", "Clone sequence", 2)),
        ScriptedBossProfile("678:2:controller", "boss_0678", 678, 2,
            HpBandPhases(678, 2),
            SelectorBattleMove(31451, 94),
            SelectorBattleMove(17617, 247),
            SelectorBattleMove(20204, 585),
            SelectorBattleMove(17569, 60),
            SelectorBattleMove(31452, 100),
            SelectorBattleMove(31453, 113, NormalMoveVariant),
            SelectorBattleMove(31454, 115, NormalMoveVariant)),
        ScriptedBossProfile("718:controller", "boss_0718", 718, 2,
            HpBandStagePhases((718, 2), (718, 3), (718, 4)),
            SelectorBattleMove(17623, 242, BossMoveVariant, 1, 2),
            SelectorBattleMove(21451, 245, BossMoveVariant, 1),
            SelectorBattleMove(21452, 614, BossMoveVariant, 1, 3),
            SelectorBattleMove(17640, 157, BossMoveVariant, 2),
            SelectorBattleMove(17626, 406, BossMoveVariant, 2),
            SelectorBattleMove(21951, 616, BossMoveVariant, 2, 3),
            SelectorBattleMove(22201, 615, BossMoveVariant, 3),
            SelectorBattleMove(22202, 687, BossMoveVariant, 3),
            SelectorMovementHelper(22203, 150, 3)),
        ScriptedBossProfile("807:1:controller", "boss_0807", 807, 1,
            HpBandPhases(807, 1),
            SelectorBattleMove(31701, 721),
            SelectorBattleMove(31702, 527),
            SelectorBattleMove(31703, 223),
            SelectorBattleMove(31704, 528),
            SelectorBattleMove(31705, 209),
            SelectorBattleMove(22253, 612),
            SelectorBattleMove(17635, 280)),
        ScriptedBossProfile("952:3:controller", "boss_0952", 952, 3,
            HpBandPhases(952, 3),
            SelectorBattleMove(31951, 225),
            SelectorBattleMove(20454, 407),
            SelectorBattleMove(17639, 127),
            SelectorBattleMove(31952, 58)),
    ];

    public static ZaScriptedBossCatalogProjection Load(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        ZaTextLabelLookup labels,
        ICollection<ValidationDiagnostic> diagnostics,
        bool includeMoveOptions = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fileSource);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ZaBossMoveSelectorDocument? activeSelectors = null;
        ZaBossMoveSelectorDocument? baseSelectors = null;
        IReadOnlySet<(int MoveId, int Variant)>? battleMoveVariants = null;
        IReadOnlySet<int>? timingRuntimeMoveIds = null;
        var sourceFileCount = 0;
        var hasSelectorSource = false;

        try
        {
            activeSelectors = ZaBossMoveSelectorDocument.Parse(
                fileSource.Read(project, ZaDataPaths.BossMoveSelectorArray).Bytes,
                fileSource.BoundedTableRecordLimit,
                fileSource.BoundedNestedRecordLimit);
            baseSelectors = ZaBossMoveSelectorDocument.Parse(
                fileSource.ReadBase(project, ZaDataPaths.BossMoveSelectorArray).Bytes,
                fileSource.BoundedTableRecordLimit,
                fileSource.BoundedNestedRecordLimit);
            sourceFileCount++;
            hasSelectorSource = true;
        }
        catch (Exception exception) when (
            (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or OverflowException)
            && !fileSource.IsBoundedSemanticLimit(exception))
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Boss action move assignments are read-only because their selector data could not be verified: {exception.Message}",
                $"romfs/{ZaDataPaths.BossMoveSelectorArray}",
                expected: "Readable active and verified base boss move selector data"));
        }

        try
        {
            var battleTable = ZaRuntimeMoveData.ReadBattle(
                fileSource.Read(project, ZaDataPaths.BattleMoveParameterArray).Bytes,
                fileSource.BoundedTableRecordLimit,
                fileSource.BoundedNestedRecordLimit);
            var timingTable = ZaRuntimeMoveData.ReadTiming(
                fileSource.Read(project, ZaDataPaths.MoveTimingParameterArray).Bytes,
                fileSource.BoundedTableRecordLimit,
                fileSource.BoundedNestedRecordLimit);
            var battleRows = ZaRuntimeMoveData.BattleRows(battleTable).ToArray();
            if (includeMoveOptions)
            {
                battleMoveVariants = battleRows
                    .GroupBy(row => (
                        MoveId: checked((int)row.MoveId),
                        Variant: checked((int)row.VariantType)))
                    .Where(group => group
                        .Select(row => ZaRuntimeMoveData.CreateBattleRowsFingerprint([row]))
                        .Distinct(StringComparer.Ordinal)
                        .Count() == 1)
                    .Select(group => group.Key)
                    .ToHashSet();
            }
            else
            {
                var identities = new HashSet<(int MoveId, int Variant)>();
                foreach (var row in battleRows)
                {
                    if (!identities.Add((
                            checked((int)row.MoveId),
                            checked((int)row.VariantType))))
                    {
                        throw new InvalidDataException(
                            "The Z-A battle-move table contains a duplicate move and variant identity.");
                    }
                }

                battleMoveVariants = identities;
            }
            timingRuntimeMoveIds = ZaRuntimeMoveData.TimingRows(timingTable)
                .Select(row => checked((int)row.MoveId))
                .ToHashSet();
            sourceFileCount += 2;
        }
        catch (Exception exception) when (
            (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or OverflowException)
            && !fileSource.IsBoundedSemanticLimit(exception))
        {
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Boss action replacements are unavailable because move runtime data could not be verified: {exception.Message}",
                $"romfs/{ZaDataPaths.BattleMoveParameterArray}",
                expected: "Readable move battle and timing parameter data"));
        }

        var referencedVariants = Profiles
            .SelectMany(profile => profile.Actions)
            .Where(action => action.Variant is not null)
            .Select(action => action.Variant!.Value)
            .ToHashSet();
        var availableCatalogVariants = battleMoveVariants is null || timingRuntimeMoveIds is null
            ? new HashSet<int>()
            : battleMoveVariants
                .Where(key => key.MoveId is >= 0 and <= MaximumBaseMoveId)
                .Where(key => referencedVariants.Contains(key.Variant))
                .Where(key => timingRuntimeMoveIds.Contains(ToRuntimeMoveId(
                    key.MoveId,
                    key.Variant)))
                .Select(key => key.Variant)
                .ToHashSet();
        var moveOptions = !includeMoveOptions
            || battleMoveVariants is null
            || timingRuntimeMoveIds is null
            ? Array.Empty<ZaScriptedBossMoveOptionRecord>()
            : battleMoveVariants
                .Where(key => key.MoveId is >= 0 and <= MaximumBaseMoveId)
                .Where(key => availableCatalogVariants.Contains(key.Variant))
                .Where(key => timingRuntimeMoveIds.Contains(ToRuntimeMoveId(
                    key.MoveId,
                    key.Variant)))
                .OrderBy(key => key.Variant)
                .ThenBy(key => key.MoveId)
                .Select(key => new ZaScriptedBossMoveOptionRecord(
                    key.MoveId,
                    ToRuntimeMoveId(key.MoveId, key.Variant),
                    key.Variant,
                    labels.Move(key.MoveId),
                    ExperimentalCompatibilityState,
                    CreateSelectorCompatibilities(key.MoveId, key.Variant)))
                .ToArray();

        return new ZaScriptedBossCatalogProjection(
            Project(
                labels,
                activeSelectors,
                baseSelectors,
                battleMoveVariants,
                timingRuntimeMoveIds,
                availableCatalogVariants),
            moveOptions,
            sourceFileCount,
            hasSelectorSource);
    }

    public static IReadOnlyList<ZaScriptedBossProfileRecord> Project(ZaTextLabelLookup labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        return Project(labels, null, null, null, null, new HashSet<int>());
    }

    public static int ToRuntimeMoveId(int moveId)
    {
        return ToRuntimeMoveId(moveId, BossMoveVariant);
    }

    public static int ToRuntimeMoveId(int moveId, int variant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(moveId);
        if (moveId > MaximumBaseMoveId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(moveId),
                moveId,
                $"Move base IDs must be between 0 and {MaximumBaseMoveId}.");
        }

        return variant switch
        {
            NormalMoveVariant => moveId,
            PlusMoveVariant => 1000 + moveId,
            BossMoveVariant => BossMoveOffset + moveId,
            _ => throw new ArgumentOutOfRangeException(
                nameof(variant),
                variant,
                "Move variants must be Normal (0), Plus (1), or Boss (2)."),
        };
    }

    public static string CreateEditField(int selectorActionId)
    {
        return $"bossAction.{selectorActionId}.moveId";
    }

    public static ZaScriptedBossMoveCompatibilityRecord ResolveMoveCompatibility(
        ZaScriptedBossMoveOptionRecord option,
        int selectorActionId)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(selectorActionId);

        return option.SelectorCompatibilities.FirstOrDefault(compatibility =>
                compatibility.SelectorActionId == selectorActionId)
            ?? new ZaScriptedBossMoveCompatibilityRecord(
                selectorActionId,
                option.DefaultCompatibilityState,
                Reason: null);
    }

    public static bool TryParseEditField(string? field, out int selectorActionId)
    {
        selectorActionId = 0;
        const string prefix = "bossAction.";
        const string suffix = ".moveId";
        return field is not null
            && field.StartsWith(prefix, StringComparison.Ordinal)
            && field.EndsWith(suffix, StringComparison.Ordinal)
            && field.Length > prefix.Length + suffix.Length
            && int.TryParse(
                field.AsSpan(prefix.Length, field.Length - prefix.Length - suffix.Length),
                out selectorActionId)
            && selectorActionId > 0;
    }

    public static string CreateRecordId(int selectorActionId)
    {
        return $"boss-action:{selectorActionId}";
    }

    public static bool TryParseRecordId(string? recordId, out int selectorActionId)
    {
        selectorActionId = 0;
        const string prefix = "boss-action:";
        return recordId is not null
            && recordId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(recordId.AsSpan(prefix.Length), out selectorActionId)
            && selectorActionId > 0;
    }

    public static ZaScriptedBossProfileRecord? FindProfile(
        IReadOnlyList<ZaScriptedBossProfileRecord> profiles,
        string? rawSpawnerId,
        int speciesId,
        int form)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var lineageKey = CreateLineageKey(rawSpawnerId);
        if (lineageKey is not null)
        {
            return profiles.FirstOrDefault(profile => string.Equals(
                profile.LineageKey,
                lineageKey,
                StringComparison.OrdinalIgnoreCase));
        }

        return profiles.FirstOrDefault(profile =>
            profile.SpeciesId == speciesId && profile.Form == form);
    }

    private static IReadOnlyList<ZaScriptedBossProfileRecord> Project(
        ZaTextLabelLookup labels,
        ZaBossMoveSelectorDocument? activeSelectors,
        ZaBossMoveSelectorDocument? baseSelectors,
        IReadOnlySet<(int MoveId, int Variant)>? battleMoveVariants,
        IReadOnlySet<int>? timingRuntimeMoveIds,
        IReadOnlySet<int> runtimeCatalogVariants)
    {
        return Profiles
            .Select(profile =>
            {
                var speciesName = labels.Pokemon(profile.SpeciesId);
                return new ZaScriptedBossProfileRecord(
                    profile.Key,
                    profile.LineageKey,
                    profile.SpeciesId,
                    profile.Form,
                    ZaLabels.PokemonWithForm(profile.SpeciesId, profile.Form, speciesName),
                    profile.Scope,
                    new ZaScriptedBossPhaseModelRecord(
                        VerifiedPhaseModelState,
                        profile.PhaseModelKind,
                        profile.Phases
                            .Select(phase =>
                            {
                                var stageSpeciesName = labels.Pokemon(phase.SpeciesId);
                                return new ZaScriptedBossPhaseRecord(
                                    phase.Key,
                                    phase.Stage,
                                    phase.HpPhase,
                                    phase.SpeciesId,
                                    phase.Form,
                                    ZaLabels.PokemonWithForm(
                                        phase.SpeciesId,
                                        phase.Form,
                                        stageSpeciesName),
                                    phase.MinimumHpPercent,
                                    phase.MaximumHpPercent);
                            })
                            .ToArray()),
                    profile.Actions
                        .Select(action => ProjectAction(
                            profile,
                            action,
                            labels,
                            activeSelectors,
                            baseSelectors,
                            battleMoveVariants,
                            timingRuntimeMoveIds,
                            runtimeCatalogVariants))
                        .ToArray());
            })
            .ToArray();
    }

    private static ZaScriptedBossActionRecord ProjectAction(
        ProfileDefinition profile,
        ActionDefinition action,
        ZaTextLabelLookup labels,
        ZaBossMoveSelectorDocument? activeSelectors,
        ZaBossMoveSelectorDocument? baseSelectors,
        IReadOnlySet<(int MoveId, int Variant)>? battleMoveVariants,
        IReadOnlySet<int>? timingRuntimeMoveIds,
        IReadOnlySet<int> runtimeCatalogVariants)
    {
        if (action.Kind == ScriptedMechanicKind)
        {
            return new ZaScriptedBossActionRecord(
                action.Key,
                action.Kind,
                null,
                null,
                null,
                null,
                null,
                action.Name!,
                action.UsesBattleParameters,
                action.UsesTimingParameters,
                CanEdit: false,
                NotApplicableRuntimeState,
                NotApplicableCompatibilityState,
                CompatibilityReason: null,
                ControllerScriptLockReason,
                PhaseAvailability: ProjectPhaseAvailability(profile, action),
                PhaseContext: ProjectPhaseContext(profile, action));
        }

        ZaBossMoveSelectorRow? activeRow = null;
        ZaBossMoveSelectorRow? baseRow = null;
        var variant = action.Variant!.Value;
        var selectorVerified =
            activeSelectors?.TryGetRow(action.SelectorActionId!.Value, out activeRow) == true
            && activeRow.CanEdit
            && baseSelectors?.TryGetRow(action.SelectorActionId.Value, out baseRow) == true
            && baseRow.CanEdit
            && baseRow.RuntimeMoveId == ToRuntimeMoveId(action.VanillaMoveId!.Value, variant);
        var runtimeMoveId = selectorVerified
            ? activeRow!.RuntimeMoveId
            : (int?)null;
        var moveId = runtimeMoveId is not null
            && TryGetBaseMoveId(runtimeMoveId.Value, variant, out var activeMoveId)
                ? activeMoveId
                : (int?)null;
        var runtimeState = selectorVerified
            ? CreateRuntimeState(
                action,
                moveId,
                runtimeMoveId,
                battleMoveVariants,
                timingRuntimeMoveIds)
            : UnavailableRuntimeState;
        var canEdit = action.Kind == BattleMoveKind
            && selectorVerified
            && runtimeCatalogVariants.Contains(variant);
        var lockReason = canEdit
            ? null
            : action.Kind == MovementHelperKind
                ? TimingChoreographyLockReason
                : !selectorVerified
                    ? SelectorUnavailableLockReason
                    : RuntimeCatalogUnavailableLockReason;
        var name = moveId is null
            ? labels.Move(action.VanillaMoveId!.Value)
            : labels.Move(moveId.Value);
        var compatibility = action.Kind != BattleMoveKind
            ? new CompatibilityEvidence(NotApplicableCompatibilityState, Reason: null)
            : moveId is null
                ? new CompatibilityEvidence(UnavailableCompatibilityState, Reason: null)
                : ResolveCompatibility(action.SelectorActionId!.Value, moveId.Value, variant);

        return new ZaScriptedBossActionRecord(
            action.Key,
            action.Kind,
            action.SelectorActionId,
            moveId,
            action.VanillaMoveId,
            runtimeMoveId,
            variant,
            name,
            action.UsesBattleParameters,
            action.UsesTimingParameters,
            canEdit,
            runtimeState,
            compatibility.State,
            compatibility.Reason,
            lockReason,
            ProjectPhaseAvailability(profile, action),
            ProjectPhaseContext(profile, action))
        {
            AffectedScopes = action.SelectorActionId is { } selectorActionId
                ? ZaScriptedEncounterMoveOwnershipCatalog.GetAffectedScopes(selectorActionId)
                : [],
        };
    }

    private static string? ProjectPhaseContext(ProfileDefinition profile, ActionDefinition action)
    {
        if (action.PhaseContext is not null)
        {
            return action.PhaseContext;
        }

        return action.ContextOnlyStages is not null
            || (action.AvailableStages is null
                && action.VanillaMoveId is not null
                && ContextOnlyActions.Contains((profile.Key, action.VanillaMoveId.Value)))
                ? "after-stun"
                : null;
    }

    private static IReadOnlyList<ZaScriptedBossPhaseAvailabilityRecord> ProjectPhaseAvailability(
        ProfileDefinition profile,
        ActionDefinition action)
    {
        if (action.AvailableStages is not null)
        {
            return profile.Phases
                .Select(phase => new ZaScriptedBossPhaseAvailabilityRecord(
                    phase.Key,
                    action.ContextOnlyStages?.Contains(phase.Stage) == true
                        ? ContextOnlyPhaseState
                        : action.AvailableStages.Contains(phase.Stage)
                        ? AvailablePhaseState
                        : UnavailablePhaseState))
                .ToArray();
        }

        if (action.VanillaMoveId is null)
        {
            return profile.Phases
                .Select(phase => new ZaScriptedBossPhaseAvailabilityRecord(
                    phase.Key,
                    UnverifiedPhaseState))
                .ToArray();
        }

        var profileAction = (profile.Key, action.VanillaMoveId.Value);
        if (ContextOnlyActions.Contains(profileAction))
        {
            return profile.Phases
                .Select(phase => new ZaScriptedBossPhaseAvailabilityRecord(
                    phase.Key,
                    ContextOnlyPhaseState))
                .ToArray();
        }

        if (!VerifiedPhaseScheduleMoves.TryGetValue(profile.Key, out var verifiedMoves)
            || !verifiedMoves.Contains(action.VanillaMoveId.Value))
        {
            return profile.Phases
                .Select(phase => new ZaScriptedBossPhaseAvailabilityRecord(
                    phase.Key,
                    UnverifiedPhaseState))
                .ToArray();
        }

        if (Phase2OnlyActions.Contains(profileAction))
        {
            return profile.Phases
                .Select(phase => new ZaScriptedBossPhaseAvailabilityRecord(
                    phase.Key,
                    phase.HpPhase == 2 ? AvailablePhaseState : UnavailablePhaseState))
                .ToArray();
        }

        if (Phase1OnlyActions.Contains(profileAction))
        {
            return profile.Phases
                .Select(phase => new ZaScriptedBossPhaseAvailabilityRecord(
                    phase.Key,
                    phase.HpPhase == 1 ? AvailablePhaseState : UnavailablePhaseState))
                .ToArray();
        }

        return profile.Phases
            .Select(phase => new ZaScriptedBossPhaseAvailabilityRecord(
                phase.Key,
                AvailablePhaseState))
            .ToArray();
    }

    private static IReadOnlyList<ZaScriptedBossMoveCompatibilityRecord> CreateSelectorCompatibilities(
        int moveId,
        int variant)
    {
        return Profiles
            .SelectMany(profile => profile.Actions.Select(action => (Profile: profile, Action: action)))
            .Where(owner => owner.Action.SelectorActionId is not null)
            .Select(owner => owner.Action.SelectorActionId!.Value)
            .Distinct()
            .Order()
            .Select(selectorActionId =>
            {
                var evidence = ResolveCompatibility(selectorActionId, moveId, variant);
                return new ZaScriptedBossMoveCompatibilityRecord(
                    selectorActionId,
                    evidence.State,
                    evidence.Reason);
            })
            .Where(compatibility =>
                !string.Equals(
                    compatibility.State,
                    ExperimentalCompatibilityState,
                    StringComparison.Ordinal)
                && !string.Equals(
                    compatibility.State,
                    UnavailableCompatibilityState,
                    StringComparison.Ordinal))
            .ToArray();
    }

    private static CompatibilityEvidence ResolveCompatibility(
        int selectorActionId,
        int moveId,
        int variant)
    {
        var owners = Profiles
            .SelectMany(profile => profile.Actions
                .Where(action => action.SelectorActionId == selectorActionId)
                .Select(action => (Profile: profile, Action: action)))
            .ToArray();
        if (owners.Length == 0
            || owners.Any(owner =>
                !string.Equals(owner.Action.Kind, BattleMoveKind, StringComparison.Ordinal)
                || owner.Action.Variant != variant))
        {
            return new CompatibilityEvidence(UnavailableCompatibilityState, Reason: null);
        }

        if (owners.All(owner => owner.Action.VanillaMoveId == moveId))
        {
            return new CompatibilityEvidence(BaseVerifiedCompatibilityState, Reason: null);
        }

        var ownerProfiles = owners
            .Select(owner => owner.Profile.Key)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var reviewedEvidence = ownerProfiles
            .Select(profileKey => ReviewedCompatibilityEvidence.TryGetValue(
                (profileKey, moveId),
                out var evidence)
                    ? evidence
                    : null)
            .ToArray();
        if (reviewedEvidence.FirstOrDefault(evidence => string.Equals(
                evidence?.State,
                KnownIncompatibleCompatibilityState,
                StringComparison.Ordinal)) is { } knownIncompatible)
        {
            return knownIncompatible;
        }

        return reviewedEvidence.All(evidence => string.Equals(
                evidence?.State,
                GameplayTestedCompatibilityState,
                StringComparison.Ordinal))
            ? new CompatibilityEvidence(GameplayTestedCompatibilityState, Reason: null)
            : new CompatibilityEvidence(ExperimentalCompatibilityState, Reason: null);
    }

    private static string CreateRuntimeState(
        ActionDefinition action,
        int? moveId,
        int? runtimeMoveId,
        IReadOnlySet<(int MoveId, int Variant)>? battleMoveVariants,
        IReadOnlySet<int>? timingRuntimeMoveIds)
    {
        if (moveId is null)
        {
            return InvalidReferenceRuntimeState;
        }

        if (battleMoveVariants is null || timingRuntimeMoveIds is null)
        {
            return UnavailableRuntimeState;
        }

        var hasBattle = battleMoveVariants.Contains((moveId.Value, action.Variant!.Value));
        var hasTiming = runtimeMoveId is not null
            && timingRuntimeMoveIds.Contains(runtimeMoveId.Value);
        if (action.Kind == MovementHelperKind)
        {
            return hasTiming ? TimingOnlyRuntimeState : MissingTimingRuntimeState;
        }

        return (hasBattle, hasTiming) switch
        {
            (true, true) => RuntimeDataPresentRuntimeState,
            (false, true) => MissingBattleRuntimeState,
            (true, false) => MissingTimingRuntimeState,
            _ => MissingBattleAndTimingRuntimeState,
        };
    }

    private static bool TryGetBaseMoveId(int runtimeMoveId, int variant, out int moveId)
    {
        var offset = variant switch
        {
            NormalMoveVariant => 0,
            PlusMoveVariant => 1000,
            BossMoveVariant => BossMoveOffset,
            _ => -1,
        };
        moveId = runtimeMoveId - offset;
        return offset >= 0 && moveId is >= 0 and <= MaximumBaseMoveId;
    }

    private static string? CreateLineageKey(string? rawSpawnerId)
    {
        if (string.IsNullOrWhiteSpace(rawSpawnerId)
            || !rawSpawnerId.StartsWith("btl_spn_boss_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tokens = rawSpawnerId["btl_spn_boss_".Length..]
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (tokens.Count == 0 || !tokens[0].All(char.IsDigit))
        {
            return null;
        }

        var species = tokens[0];
        tokens.RemoveAt(0);
        var followerIndex = tokens.FindIndex(token =>
            token.StartsWith("follower", StringComparison.OrdinalIgnoreCase));
        if (followerIndex >= 0)
        {
            tokens.RemoveRange(followerIndex, tokens.Count - followerIndex);
        }

        if (tokens.Count >= 2
            && string.Equals(tokens[^2], "re", StringComparison.OrdinalIgnoreCase)
            && string.Equals(tokens[^1], "dim", StringComparison.OrdinalIgnoreCase))
        {
            tokens.RemoveRange(tokens.Count - 2, 2);
        }
        else if (tokens.Count > 0 && IsTerminalMode(tokens[^1]))
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        if (tokens.Count == 1 && tokens[0].All(char.IsDigit))
        {
            tokens.Clear();
        }

        return string.Join('_', new[] { "boss", species }.Concat(tokens)).ToLowerInvariant();
    }

    private static bool IsTerminalMode(string token)
    {
        return string.Equals(token, "re", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "y", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("sim", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("rus", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("rush", StringComparison.OrdinalIgnoreCase);
    }

    private static ProfileDefinition Profile(
        int speciesId,
        int form,
        params ActionDefinition[] actions)
    {
        return new ProfileDefinition(
            $"{speciesId}:{form}",
            $"boss_{speciesId:D4}",
            speciesId,
            form,
            BaseRogueMegaScope,
            HpBandsPhaseModelKind,
            HpBandPhases(speciesId, form),
            actions);
    }

    private static ProfileDefinition ScriptedBossProfile(
        string key,
        string lineageKey,
        int speciesId,
        int form,
        IReadOnlyList<PhaseDefinition> phases,
        params ActionDefinition[] actions)
    {
        var stageCount = phases.Select(phase => phase.Stage).Distinct().Count();
        var hasMultipleHpPhases = phases
            .GroupBy(phase => phase.Stage)
            .Any(stage => stage.Count() > 1);
        return new ProfileDefinition(
            key,
            lineageKey,
            speciesId,
            form,
            VerifiedScriptedBossScope,
            stageCount > 1
                ? hasMultipleHpPhases
                    ? BattleStagesWithHpBandsPhaseModelKind
                    : BattleStagesPhaseModelKind
                : HpBandsPhaseModelKind,
            phases,
            actions);
    }

    private static ProfileDefinition ScriptedFollowerProfile(
        string key,
        string lineageKey,
        int speciesId,
        int form,
        params ActionDefinition[] actions)
    {
        return new ProfileDefinition(
            key,
            lineageKey,
            speciesId,
            form,
            VerifiedScriptedFollowerScope,
            BattleStagesPhaseModelKind,
            FullStagePhases((speciesId, form)),
            actions);
    }

    private static IReadOnlyList<PhaseDefinition> HpBandPhases(int speciesId, int form)
    {
        return HpBandStagePhases((speciesId, form));
    }

    private static IReadOnlyList<PhaseDefinition> HpBandStagePhases(
        params (int SpeciesId, int Form)[] stages)
    {
        return stages
            .SelectMany((stage, index) => new[]
            {
                new PhaseDefinition(
                    $"stage-{index + 1}-phase-1",
                    index + 1,
                    HpPhase: 1,
                    stage.SpeciesId,
                    stage.Form,
                    MinimumHpPercent: 50,
                    MaximumHpPercent: 100),
                new PhaseDefinition(
                    $"stage-{index + 1}-phase-2",
                    index + 1,
                    HpPhase: 2,
                    stage.SpeciesId,
                    stage.Form,
                    MinimumHpPercent: 0,
                    MaximumHpPercent: 50),
            })
            .ToArray();
    }

    private static IReadOnlyList<PhaseDefinition> FullStagePhases(
        params (int SpeciesId, int Form)[] stages)
    {
        return stages
            .Select((stage, index) => new PhaseDefinition(
                $"stage-{index + 1}-phase-1",
                index + 1,
                HpPhase: 1,
                stage.SpeciesId,
                stage.Form,
                MinimumHpPercent: 0,
                MaximumHpPercent: 100))
            .ToArray();
    }

    private static ActionDefinition BattleMove(int moveId)
    {
        var selectorActionId = SelectorActionIds[moveId];
        return new ActionDefinition(
            $"{BattleMoveKind}:{selectorActionId}",
            BattleMoveKind,
            selectorActionId,
            moveId,
            BossMoveVariant,
            null,
            UsesBattleParameters: true,
            UsesTimingParameters: true);
    }

    private static ActionDefinition SelectorBattleMove(
        int selectorActionId,
        int moveId,
        int variant = BossMoveVariant,
        params int[] availableStages)
    {
        return new ActionDefinition(
            $"{BattleMoveKind}:{selectorActionId}",
            BattleMoveKind,
            selectorActionId,
            moveId,
            variant,
            Name: null,
            UsesBattleParameters: true,
            UsesTimingParameters: true,
            AvailableStages: availableStages.Length == 0
                ? new HashSet<int> { 1 }
                : availableStages.ToHashSet());
    }

    private static ActionDefinition MovementHelper(int moveId)
    {
        var selectorActionId = SelectorActionIds[moveId];
        return new ActionDefinition(
            $"{MovementHelperKind}:{selectorActionId}",
            MovementHelperKind,
            selectorActionId,
            moveId,
            BossMoveVariant,
            null,
            UsesBattleParameters: false,
            UsesTimingParameters: true);
    }

    private static ActionDefinition SelectorMovementHelper(
        int selectorActionId,
        int moveId,
        params int[] availableStages)
    {
        return new ActionDefinition(
            $"{MovementHelperKind}:{selectorActionId}",
            MovementHelperKind,
            selectorActionId,
            moveId,
            BossMoveVariant,
            Name: null,
            UsesBattleParameters: false,
            UsesTimingParameters: true,
            AvailableStages: availableStages.Length == 0
                ? new HashSet<int> { 1 }
                : availableStages.ToHashSet());
    }

    private static ActionDefinition ScriptedMechanic(
        string key,
        string name,
        params int[] availableStages)
    {
        return new ActionDefinition(
            $"{ScriptedMechanicKind}:{key}",
            ScriptedMechanicKind,
            SelectorActionId: null,
            VanillaMoveId: null,
            Variant: null,
            name,
            UsesBattleParameters: false,
            UsesTimingParameters: false,
            AvailableStages: availableStages.Length == 0
                ? null
                : availableStages.ToHashSet());
    }

    private static ActionDefinition ContextOnlyScriptedMechanic(
        string key,
        string name,
        string phaseContext,
        params int[] availableStages)
    {
        var stages = availableStages.Length == 0
            ? new HashSet<int> { 1 }
            : availableStages.ToHashSet();
        return new ActionDefinition(
            $"{ScriptedMechanicKind}:{key}",
            ScriptedMechanicKind,
            SelectorActionId: null,
            VanillaMoveId: null,
            Variant: null,
            name,
            UsesBattleParameters: false,
            UsesTimingParameters: false,
            AvailableStages: stages)
        {
            ContextOnlyStages = stages,
            PhaseContext = phaseContext,
        };
    }

    private sealed record ProfileDefinition(
        string Key,
        string LineageKey,
        int SpeciesId,
        int Form,
        string Scope,
        string PhaseModelKind,
        IReadOnlyList<PhaseDefinition> Phases,
        IReadOnlyList<ActionDefinition> Actions);

    private sealed record PhaseDefinition(
        string Key,
        int Stage,
        int HpPhase,
        int SpeciesId,
        int Form,
        int MinimumHpPercent,
        int MaximumHpPercent);

    private sealed record CompatibilityEvidence(
        string State,
        string? Reason);

    private sealed record ActionDefinition(
        string Key,
        string Kind,
        int? SelectorActionId,
        int? VanillaMoveId,
        int? Variant,
        string? Name,
        bool UsesBattleParameters,
        bool UsesTimingParameters,
        IReadOnlySet<int>? AvailableStages = null)
    {
        public IReadOnlySet<int>? ContextOnlyStages { get; init; }

        public string? PhaseContext { get; init; }
    }
}
