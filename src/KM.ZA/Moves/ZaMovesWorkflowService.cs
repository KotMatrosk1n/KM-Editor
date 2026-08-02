// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.BattleMoves;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Moves;

internal sealed class ZaMovesWorkflowService
{
    private const string WorkflowLabel = "Moves";
    private const string WorkflowDescription =
        "Edit Pokemon Legends Z-A runtime battle parameters, variants, accuracy, and cooldown data.";

    public const string CanUseMoveField = "canUseMove";
    public const string TypeField = "type";
    public const string QualityField = "quality";
    public const string CategoryField = "category";
    public const string PowerField = "power";
    public const string AccuracyField = "accuracy";
    public const string PpField = "pp";
    public const string PriorityField = "priority";
    public const string CritStageField = "critStage";
    public const string TargetField = "target";
    public const string HitMinField = "hitMin";
    public const string HitMaxField = "hitMax";
    public const string TurnMinField = "turnMin";
    public const string TurnMaxField = "turnMax";
    public const string InflictField = "inflict";
    public const string InflictPercentField = "inflictPercent";
    public const string RawInflictCountField = "rawInflictCount";
    public const string FlinchField = "flinch";
    public const string EffectSequenceField = "effectSequence";
    public const string RecoilField = "recoil";
    public const string RawHealingField = "rawHealing";
    public const string Stat1Field = "stat1";
    public const string Stat1StageField = "stat1Stage";
    public const string Stat1PercentField = "stat1Percent";
    public const string Stat2Field = "stat2";
    public const string Stat2StageField = "stat2Stage";
    public const string Stat2PercentField = "stat2Percent";
    public const string Stat3Field = "stat3";
    public const string Stat3StageField = "stat3Stage";
    public const string Stat3PercentField = "stat3Percent";
    public const string MakesContactField = "makesContact";
    public const string ChargeField = "charge";
    public const string RechargeField = "recharge";
    public const string ProtectField = "protect";
    public const string ReflectableField = "reflectable";
    public const string SnatchField = "snatch";
    public const string MirrorField = "mirror";
    public const string PunchField = "punch";
    public const string SoundField = "sound";
    public const string DanceField = "dance";
    public const string GravityField = "gravity";
    public const string DefrostField = "defrost";
    public const string DistanceTripleField = "distanceTriple";
    public const string HealField = "heal";
    public const string IgnoreSubstituteField = "ignoreSubstitute";
    public const string FailSkyBattleField = "failSkyBattle";
    public const string AnimateAllyField = "animateAlly";
    public const string MetronomeField = "metronome";
    public const string FailEncoreField = "failEncore";
    public const string FailMeFirstField = "failMeFirst";
    public const string FutureAttackField = "futureAttack";
    public const string PressureField = "pressure";
    public const string ComboField = "combo";
    public const string NoSleepTalkField = "noSleepTalk";
    public const string NoAssistField = "noAssist";
    public const string FailCopycatField = "failCopycat";
    public const string FailMimicField = "failMimic";
    public const string FailInstructField = "failInstruct";
    public const string PowderField = "powder";
    public const string BiteField = "bite";
    public const string BulletField = "bullet";
    public const string NoMultiHitField = "noMultiHit";
    public const string NoEffectivenessField = "noEffectiveness";
    public const string SheerForceField = "sheerForce";
    public const string SlicingField = "slicing";
    public const string WindField = "wind";
    public const string CantUseTwiceField = "cantUseTwice";

    private static readonly IReadOnlyList<string> TypeNames =
    [
        "Normal",
        "Fighting",
        "Flying",
        "Poison",
        "Ground",
        "Rock",
        "Bug",
        "Ghost",
        "Steel",
        "Fire",
        "Water",
        "Grass",
        "Electric",
        "Psychic",
        "Ice",
        "Dragon",
        "Dark",
        "Fairy",
    ];

    private static readonly IReadOnlyList<string> CategoryNames =
    [
        "Status",
        "Physical",
        "Special",
    ];

    private static readonly IReadOnlyList<string> TargetNames =
    [
        "Any Except Self",
        "Ally Or Self",
        "Ally",
        "Opponent",
        "All Adjacent",
        "All Adjacent Opponents",
        "All Allies",
        "Self",
        "All",
        "Random Opponent",
        "All Sides",
        "Opponent Side",
        "Self Side",
        "Counter Target",
    ];

    private static readonly IReadOnlyDictionary<int, string> InflictNames = new Dictionary<int, string>
    {
        [0] = "None",
        [1] = "Paralyze",
        [2] = "Sleep",
        [3] = "Freeze",
        [4] = "Burn",
        [5] = "Poison",
        [6] = "Confusion",
        [7] = "Infatuation",
        [8] = "Trap",
        [9] = "Nightmare",
        [11] = "Taunt",
        [12] = "Torment",
        [13] = "Disable",
        [14] = "Drowsiness",
        [15] = "Heal Block",
        [17] = "Identify",
        [18] = "Leech Seed",
        [19] = "Embargo",
        [20] = "Perish Song",
        [21] = "Ingrain",
        [24] = "Throat Chop",
        [42] = "Tar Shot",
        [46] = "Salt Cure",
        [65535] = "Tri Attack Status",
    };

    private static readonly IReadOnlyDictionary<int, string> RuntimeConditionNames =
        new Dictionary<int, string>
        {
            [0] = "None",
            [1] = "Paralysis",
            [2] = "Sleep",
            [3] = "Freeze",
            [4] = "Burn",
            [5] = "Poison",
            [6] = "Confusion",
            [8] = "Trap / Bind",
            [11] = "Taunt",
            [12] = "Torment",
            [15] = "Heal Block",
            [18] = "Leech Seed",
            [20] = "Perish Song",
            [46] = "Salt Cure",
        };

    private static readonly IReadOnlyList<string> StatNames =
    [
        "None",
        "Attack",
        "Defense",
        "Sp. Atk",
        "Sp. Def",
        "Speed",
        "Accuracy",
        "Evasion",
        "Critical Hit Rate",
        "All Stats",
    ];

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> TypeOptions =
        CreateIndexedOptions(TypeNames);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> CategoryOptions =
        CreateIndexedOptions(CategoryNames);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> TargetOptions =
        CreateIndexedOptions(TargetNames);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> StatOptions =
        [new(-1, "-1 Unused"), .. CreateIndexedOptions(StatNames)];

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeStatOptions =
        CreateIndexedOptions(StatNames);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeCriticalRankOptions =
    [
        new(0, "0 Normal (1 in 24)"),
        new(1, "1 High (1 in 8)"),
        new(2, "2 Very high (1 in 2)"),
        new(6, "6 Guaranteed"),
    ];

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeConditionModeOptions =
    [
        new(0, "0 Effect-defined / none"),
        new(1, "1 Persistent"),
        new(2, "2 Timed"),
    ];

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeConditionOptions =
        RuntimeConditionNames
            .OrderBy(entry => entry.Key)
            .Select(entry => new ZaMoveEditableFieldOption(
                entry.Key,
                $"{entry.Key.ToString(CultureInfo.InvariantCulture)} {entry.Value}"))
            .ToArray();

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeEffectCategoryOptions =
        CreateRawOptions(0, 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeValueEffectRatioOptions =
        CreateRawOptions(0, 2, 8, 15, 20);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeSpawnOriginOptions =
        CreateRawOptions(0, 1, 2, 4, 5, 6, 7);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeSpawnLocatorOptions =
        ZaRuntimeMoveData.SpawnLocators
            .Select((locator, index) => new ZaMoveEditableFieldOption(
                index,
                index == 0
                    ? "0 Empty / default"
                    : $"{index.ToString(CultureInfo.InvariantCulture)} {locator}"))
            .ToArray();

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeShotDirectionOptions =
        CreateRawOptions(0, 1, 2);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeTargetCorrectionOptions =
        CreateRawOptions(0, 1);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeMovementTypeOptions =
        CreateRawOptions(0, 1, 3);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeHeightToleranceOptions =
        CreateRawOptions(0, 2);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeEffectValueOptions =
        CreateRawOptions(0);

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> RuntimeMegaPowerBonusOptions =
    [
        new(0, "0"),
        new(5, "5"),
        new(5.75, "5.75"),
    ];

    private static readonly IReadOnlyList<ZaMoveEditableFieldOption> InflictOptions =
        InflictNames
            .OrderBy(entry => entry.Key)
            .Select(entry => new ZaMoveEditableFieldOption(entry.Key, $"{entry.Key:000} {entry.Value}"))
            .ToArray();

    private static readonly IReadOnlyList<ZaMoveEditableField> EditableFields =
    [
        Field(CanUseMoveField, "Can use move", "boolean", 0, 1, BooleanOptions),
        Field(TypeField, "Type", "integer", 0, byte.MaxValue, TypeOptions),
        Field(QualityField, "Quality", "integer", byte.MinValue, byte.MaxValue),
        Field(CategoryField, "Category", "integer", byte.MinValue, byte.MaxValue, CategoryOptions),
        Field(PowerField, "Power", "integer", byte.MinValue, byte.MaxValue),
        Field(AccuracyField, "Accuracy", "integer", byte.MinValue, byte.MaxValue),
        Field(PpField, "PP", "integer", byte.MinValue, byte.MaxValue),
        Field(PriorityField, "Priority", "integer", sbyte.MinValue, sbyte.MaxValue),
        Field(CritStageField, "Critical stage", "integer", byte.MinValue, byte.MaxValue),
        Field(TargetField, "Target", "integer", byte.MinValue, byte.MaxValue, TargetOptions),
        Field(HitMinField, "Minimum hits", "integer", byte.MinValue, byte.MaxValue),
        Field(HitMaxField, "Maximum hits", "integer", byte.MinValue, byte.MaxValue),
        Field(RawInflictCountField, "Inflict turn mode", "integer", byte.MinValue, byte.MaxValue),
        Field(TurnMinField, "Minimum inflict turns", "integer", byte.MinValue, byte.MaxValue),
        Field(TurnMaxField, "Maximum inflict turns", "integer", byte.MinValue, byte.MaxValue),
        Field(InflictField, "Inflicted condition", "integer", ushort.MinValue, ushort.MaxValue, InflictOptions),
        Field(InflictPercentField, "Inflict chance (%)", "integer", byte.MinValue, byte.MaxValue),
        Field(FlinchField, "Flinch chance (%)", "integer", byte.MinValue, byte.MaxValue),
        Field(EffectSequenceField, "Effect sequence ID", "integer", ushort.MinValue, ushort.MaxValue),
        Field(RecoilField, "Recoil/drain (%)", "integer", sbyte.MinValue, sbyte.MaxValue),
        Field(RawHealingField, "Healing behavior", "integer", sbyte.MinValue, sbyte.MaxValue),
        Field(Stat1Field, "Stat Change 1: Stat", "integer", sbyte.MinValue, sbyte.MaxValue, StatOptions),
        Field(Stat1StageField, "Stat Change 1: Stage Delta", "integer", sbyte.MinValue, sbyte.MaxValue),
        Field(Stat1PercentField, "Stat Change 1: Chance (%)", "integer", byte.MinValue, byte.MaxValue),
        Field(Stat2Field, "Stat Change 2: Stat", "integer", sbyte.MinValue, sbyte.MaxValue, StatOptions),
        Field(Stat2StageField, "Stat Change 2: Stage Delta", "integer", sbyte.MinValue, sbyte.MaxValue),
        Field(Stat2PercentField, "Stat Change 2: Chance (%)", "integer", byte.MinValue, byte.MaxValue),
        Field(Stat3Field, "Stat Change 3: Stat", "integer", sbyte.MinValue, sbyte.MaxValue, StatOptions),
        Field(Stat3StageField, "Stat Change 3: Stage Delta", "integer", sbyte.MinValue, sbyte.MaxValue),
        Field(Stat3PercentField, "Stat Change 3: Chance (%)", "integer", byte.MinValue, byte.MaxValue),
        Field(MakesContactField, "Makes contact", "boolean", 0, 1, BooleanOptions),
        Field(ChargeField, "Charge turn", "boolean", 0, 1, BooleanOptions),
        Field(RechargeField, "Recharge turn", "boolean", 0, 1, BooleanOptions),
        Field(ProtectField, "Blocked by Protect", "boolean", 0, 1, BooleanOptions),
        Field(ReflectableField, "Reflectable", "boolean", 0, 1, BooleanOptions),
        Field(SnatchField, "Snatchable", "boolean", 0, 1, BooleanOptions),
        Field(MirrorField, "Mirror Move", "boolean", 0, 1, BooleanOptions),
        Field(PunchField, "Punch move", "boolean", 0, 1, BooleanOptions),
        Field(SoundField, "Sound move", "boolean", 0, 1, BooleanOptions),
        Field(DanceField, "Dance move", "boolean", 0, 1, BooleanOptions),
        Field(GravityField, "Fails under gravity", "boolean", 0, 1, BooleanOptions),
        Field(DefrostField, "Thaws user", "boolean", 0, 1, BooleanOptions),
        Field(DistanceTripleField, "Triple battle distance", "boolean", 0, 1, BooleanOptions),
        Field(HealField, "Heal move", "boolean", 0, 1, BooleanOptions),
        Field(IgnoreSubstituteField, "Ignores substitute", "boolean", 0, 1, BooleanOptions),
        Field(FailSkyBattleField, "Fails in Sky Battle", "boolean", 0, 1, BooleanOptions),
        Field(AnimateAllyField, "Animate ally", "boolean", 0, 1, BooleanOptions),
        Field(MetronomeField, "Callable by Metronome", "boolean", 0, 1, BooleanOptions),
        Field(FailEncoreField, "Fails during Encore", "boolean", 0, 1, BooleanOptions),
        Field(FailMeFirstField, "Fails with Me First", "boolean", 0, 1, BooleanOptions),
        Field(FutureAttackField, "Future attack", "boolean", 0, 1, BooleanOptions),
        Field(PressureField, "Affected by Pressure", "boolean", 0, 1, BooleanOptions),
        Field(ComboField, "Combo move", "boolean", 0, 1, BooleanOptions),
        Field(NoSleepTalkField, "Blocked from Sleep Talk", "boolean", 0, 1, BooleanOptions),
        Field(NoAssistField, "Blocked from Assist", "boolean", 0, 1, BooleanOptions),
        Field(FailCopycatField, "Fails with Copycat", "boolean", 0, 1, BooleanOptions),
        Field(FailMimicField, "Fails with Mimic", "boolean", 0, 1, BooleanOptions),
        Field(FailInstructField, "Fails with Instruct", "boolean", 0, 1, BooleanOptions),
        Field(PowderField, "Powder move", "boolean", 0, 1, BooleanOptions),
        Field(BiteField, "Bite move", "boolean", 0, 1, BooleanOptions),
        Field(BulletField, "Bullet move", "boolean", 0, 1, BooleanOptions),
        Field(NoMultiHitField, "Cannot multi-hit", "boolean", 0, 1, BooleanOptions),
        Field(NoEffectivenessField, "Ignores type effectiveness", "boolean", 0, 1, BooleanOptions),
        Field(SheerForceField, "Boosted by Sheer Force", "boolean", 0, 1, BooleanOptions),
        Field(SlicingField, "Slicing move", "boolean", 0, 1, BooleanOptions),
        Field(WindField, "Wind move", "boolean", 0, 1, BooleanOptions),
        Field(CantUseTwiceField, "Cannot use twice in a row", "boolean", 0, 1, BooleanOptions),
    ];

    private static readonly IReadOnlyList<ZaMoveEditableField> RuntimeBaseEditableFields =
    [
        .. CreateRuntimeVariantFields(0),
        .. CreateRuntimeVariantFields(1),
        .. CreateRuntimeVariantFields(2),
        Field("timing.hitPercent", "Accuracy (%)", "integer", 0, 100),
        Field("timing.cooldown", "Cooldown (seconds)", "decimal", 0, 60),
    ];

    private static readonly IReadOnlyList<ZaMoveEditableField> RuntimeAdvancedTimingTemplates =
        CreateAdvancedTimingFields(0, ZaRuntimeMoveData.SpawnLocators, 3108);

    private readonly ZaWorkflowFileSource fileSource;

    public ZaMovesWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Moves,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaMovesWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        ZaWorkflowFile? source = null;
        ZaWorkflowFile? battleSource = null;
        ZaWorkflowFile? timingSource = null;
        IReadOnlyList<ZaMoveEditableFieldOption> projectileOptions = [];
        IReadOnlyList<ProjectFileReference> projectileCatalogSources = [];
        IReadOnlyList<string> spawnLocators = ZaRuntimeMoveData.SpawnLocators;
        IReadOnlyList<ZaMoveEditableField> runtimeEditableFields = RuntimeBaseEditableFields;
        var labels = ZaTextLabelLookup.None();
        var moves = Array.Empty<ZaMoveRecord>();

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            source = fileSource.Read(project, ZaDataPaths.MoveDataArray);
            battleSource = fileSource.Read(project, ZaDataPaths.BattleMoveParameterArray);
            timingSource = fileSource.Read(project, ZaDataPaths.MoveTimingParameterArray);

            var battleTable = ZaRuntimeMoveData.ReadBattle(battleSource.Bytes);
            var timingTable = ZaRuntimeMoveData.ReadTiming(timingSource.Bytes);
            var baseBattleTable = ZaRuntimeMoveData.ReadBattle(
                fileSource.ReadBase(project, ZaDataPaths.BattleMoveParameterArray).Bytes);
            var baseTimingTable = ZaRuntimeMoveData.ReadTiming(
                fileSource.ReadBase(project, ZaDataPaths.MoveTimingParameterArray).Bytes);

            try
            {
                var projectileSource = fileSource.Read(project, ZaDataPaths.AiBulletParamArray);
                var baseProjectileSource = fileSource.ReadBase(project, ZaDataPaths.AiBulletParamArray);
                projectileOptions = ZaMoveProjectileCatalog.ReadOptions(projectileSource.Bytes);
                projectileCatalogSources =
                [
                    new ProjectFileReference(projectileSource.SourceLayer, projectileSource.RelativePath),
                    new ProjectFileReference(baseProjectileSource.SourceLayer, baseProjectileSource.RelativePath),
                ];
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Projectile override fields are unavailable because the bullet catalog could not be verified: {exception.Message}",
                    $"romfs/{ZaDataPaths.AiBulletParamArray}",
                    expected: "A structurally valid active and verified-base bullet parameter catalog"));
            }

            var battleByMove = ZaRuntimeMoveData.BattleRows(battleTable)
                .GroupBy(row => checked((int)row.MoveId))
                .ToDictionary(group => group.Key, group => group.OrderBy(row => row.VariantType).ToArray());
            var timingByMove = ZaRuntimeMoveData.TimingRows(timingTable)
                .GroupBy(row => ZaRuntimeMoveData.GetTimingBaseMoveId(row.MoveId))
                .ToDictionary(group => group.Key, group => group.ToArray());
            var baseBattleByMove = ZaRuntimeMoveData.BattleRows(baseBattleTable)
                .GroupBy(row => checked((int)row.MoveId))
                .ToDictionary(group => group.Key, group => group.OrderBy(row => row.VariantType).ToArray());
            var baseTimingByMove = ZaRuntimeMoveData.TimingRows(baseTimingTable)
                .GroupBy(row => ZaRuntimeMoveData.GetTimingBaseMoveId(row.MoveId))
                .ToDictionary(group => group.Key, group => group.ToArray());

            spawnLocators = ZaRuntimeMoveData.SpawnLocators
                .Concat(ZaRuntimeMoveData.TimingRows(timingTable)
                    .Select(row => row.SpawnLocator ?? string.Empty))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var maximumTimingOccurrences = ZaRuntimeMoveData.TimingRows(timingTable)
                .GroupBy(row => row.MoveId)
                .Select(group => group.Count())
                .Concat(ZaRuntimeMoveData.TimingRows(baseTimingTable)
                    .GroupBy(row => row.MoveId)
                    .Select(group => group.Count()))
                .DefaultIfEmpty(0)
                .Max();
            runtimeEditableFields = CreateRuntimeEditableFields(
                maximumTimingOccurrences,
                spawnLocators,
                projectileOptions);

            moves = LoadRecords(source, labels)
                .Select(move => AddRuntimeData(
                    move,
                    battleByMove.GetValueOrDefault(move.MoveId) ?? [],
                    timingByMove.GetValueOrDefault(move.MoveId) ?? [],
                    baseBattleByMove.GetValueOrDefault(move.MoveId) ?? [],
                    baseTimingByMove.GetValueOrDefault(move.MoveId) ?? [],
                    battleSource.RelativePath,
                    battleSource.SourceLayer,
                    timingSource.RelativePath,
                    timingSource.SourceLayer,
                    runtimeEditableFields,
                    spawnLocators))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Moves could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.BattleMoveParameterArray}"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Moves,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new ZaMovesWorkflow(
            summary,
            moves,
            runtimeEditableFields,
            new ZaMovesWorkflowStats(
                moves.Length,
                moves.Count(move => move.CanUseMove),
                new[] { source, battleSource, timingSource }.Count(file => file is not null),
                moves.Sum(move => move.Flags.Count(flag => flag.Enabled))),
            diagnostics)
        {
            ProjectileOptions = projectileOptions,
            ProjectileCatalogSources = projectileCatalogSources,
            SpawnLocators = spawnLocators,
        };
    }

    internal static ZaMoveEditableField? GetEditableField(string? field)
    {
        var exact = RuntimeBaseEditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        if (!ZaRuntimeMoveData.TryParseTimingField(field, out _, out var occurrence, out var member))
        {
            return null;
        }

        var templates = occurrence is null
            ? RuntimeBaseEditableFields
            : RuntimeAdvancedTimingTemplates;
        var template = templates.FirstOrDefault(candidate =>
            ZaRuntimeMoveData.TryParseTimingField(candidate.Field, out _, out _, out var candidateMember)
            && string.Equals(candidateMember, member, StringComparison.Ordinal));
        return template is null ? null : template with { Field = field! };
    }

    internal static ZaMoveEditableField? GetEditableField(ZaMovesWorkflow workflow, string? field)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var exact = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
        if (exact is not null
            || !ZaRuntimeMoveData.TryParseTimingField(field, out _, out var occurrence, out var member))
        {
            return exact;
        }

        var template = workflow.EditableFields.FirstOrDefault(candidate =>
            ZaRuntimeMoveData.TryParseTimingField(candidate.Field, out _, out var candidateOccurrence, out var candidateMember)
            && (occurrence is null) == (candidateOccurrence is null)
            && string.Equals(candidateMember, member, StringComparison.Ordinal));
        return template is null ? null : template with { Field = field! };
    }

    internal static bool IsProjectileField(string? field)
    {
        return ZaRuntimeMoveData.TryParseTimingField(field, out _, out var occurrence, out var member)
            && occurrence is not null
            && ZaRuntimeMoveData.IsProjectileMember(member);
    }

    internal static string FormatType(int type) => FormatIndexed(type, TypeNames, "Type");

    internal static string FormatCategory(int category) => FormatIndexed(category, CategoryNames, "Category");

    internal static string FormatTarget(int target) => FormatIndexed(target, TargetNames, "Target");

    internal static string FormatStat(int stat) => stat < 0
        ? $"Unused ({stat.ToString(CultureInfo.InvariantCulture)} raw)"
        : FormatIndexed(stat, StatNames, "Stat");

    internal static string FormatInflict(int inflict)
    {
        return InflictNames.TryGetValue(inflict, out var label)
            ? label
            : $"Inflict {inflict}";
    }

    internal static bool IsEditableFlagField(string field)
    {
        return EditableFields.Any(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal)
            && candidate.ValueKind == "boolean"
            && !string.Equals(candidate.Field, CanUseMoveField, StringComparison.Ordinal));
    }

    private static IEnumerable<ZaMoveRecord> LoadRecords(
        ZaWorkflowFile source,
        ZaTextLabelLookup labels)
    {
        var table = ZaMoveDataArray.GetRootAsZaMoveDataArray(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var move = table.Values(index);
            if (move is not null)
            {
                yield return ToRecord(move.Value, labels, source);
            }
        }
    }

    private static ZaMoveRecord ToRecord(
        ZaMoveData move,
        ZaTextLabelLookup labels,
        ZaWorkflowFile source)
    {
        var moveId = move.MoveId;
        var inflict = move.Inflict ?? default;
        var flags = ToFlagRecords(move);

        return new ZaMoveRecord(
            moveId,
            labels.Move(moveId),
            labels.MoveDescription(moveId),
            Version: 0,
            move.CanUseMove,
            move.Type,
            FormatType(move.Type),
            move.Quality,
            move.Category,
            FormatCategory(move.Category),
            move.Power,
            move.Accuracy,
            move.Pp,
            move.Priority,
            move.CritStage,
            MaxMovePower: 0,
            move.RawTarget,
            FormatTarget(move.RawTarget),
            move.HitMin,
            move.HitMax,
            inflict.TurnMin,
            inflict.TurnMax,
            inflict.Condition,
            FormatInflict(inflict.Condition),
            inflict.Chance,
            inflict.TurnMode,
            move.Flinch,
            move.EffectSequence,
            move.Recoil,
            move.RawHealing,
            ToStatChangeRecords(move.StatChanges),
            flags,
            new ZaMoveProvenance(source.RelativePath, source.SourceLayer, source.FileState));
    }

    private static ZaMoveRecord AddRuntimeData(
        ZaMoveRecord move,
        IReadOnlyList<ZaBattleMoveParameterT> battleRows,
        IReadOnlyList<ZaMoveTimingParameterT> timingRows,
        IReadOnlyList<ZaBattleMoveParameterT> baseBattleRows,
        IReadOnlyList<ZaMoveTimingParameterT> baseTimingRows,
        string battleSourceFile,
        ProjectFileLayer battleSourceLayer,
        string timingSourceFile,
        ProjectFileLayer timingSourceLayer,
        IReadOnlyList<ZaMoveEditableField> runtimeEditableFields,
        IReadOnlyList<string> spawnLocators)
    {
        var (variants, ambiguousVariantIds) = ProjectBattleRows(battleRows);
        var (baseVariants, ambiguousBaseVariantIds) = ProjectBattleRows(baseBattleRows);
        var timingRecords = ProjectTimingRows(timingRows, spawnLocators);
        var baseTimingRecords = ProjectTimingRows(baseTimingRows, spawnLocators);
        var timing = timingRecords.FirstOrDefault(row => row.Variant == 0)
            ?? timingRecords.FirstOrDefault();
        var primary = variants.FirstOrDefault(variant => variant.Variant == 0) ?? variants.FirstOrDefault();
        var vanillaValues = new List<ZaMoveVanillaFieldValue>();
        var currentVariantIds = battleRows
            .Select(row => checked((int)row.VariantType))
            .Order()
            .ToArray();
        var baseVariantIds = baseBattleRows
            .Select(row => checked((int)row.VariantType))
            .Order()
            .ToArray();
        var hasRuntimeData = variants.Count > 0 || timingRows.Count > 0;
        var hasMatchingVariantShape = ambiguousVariantIds.Count == 0
            && ambiguousBaseVariantIds.Count == 0
            && currentVariantIds.SequenceEqual(baseVariantIds);
        var hasMatchingTiming = timingRows
            .Select(row => row.MoveId)
            .SequenceEqual(baseTimingRows.Select(row => row.MoveId));
        var canRevertToVanilla = hasRuntimeData
            && hasMatchingVariantShape
            && hasMatchingTiming;
        var battleVanillaFingerprint = baseBattleRows.Count == 0
            ? null
            : ZaRuntimeMoveData.CreateBattleRowsFingerprint(baseBattleRows);
        var timingVanillaFingerprint = baseTimingRows.Count == 0
            ? null
            : ZaRuntimeMoveData.CreateTimingRowsFingerprint(baseTimingRows);
        var battleDiffersFromVanilla = hasMatchingVariantShape
            && battleVanillaFingerprint is not null
            && !string.Equals(
                ZaRuntimeMoveData.CreateBattleRowsFingerprint(battleRows),
                battleVanillaFingerprint,
                StringComparison.Ordinal);
        var timingDiffersFromVanilla = hasMatchingTiming
            && timingVanillaFingerprint is not null
            && !string.Equals(
                ZaRuntimeMoveData.CreateTimingRowsFingerprint(timingRows),
                timingVanillaFingerprint,
                StringComparison.Ordinal);
        var revertBlockedReason = canRevertToVanilla
            ? null
            : !hasRuntimeData
                ? "This move does not have editable runtime battle or timing data."
                : !hasMatchingVariantShape
                    ? "The active and verified vanilla files do not contain one exact matching occurrence shape of unambiguous runtime variants for this move."
                    : "The active and verified vanilla files do not contain the same restorable timing-row shape for this move.";

        foreach (var baseVariant in baseVariants)
        {
            foreach (var field in runtimeEditableFields.Where(candidate =>
                         candidate.Field.StartsWith($"battle.{baseVariant.Variant}.", StringComparison.Ordinal)))
            {
                if (ZaRuntimeMoveData.TryParseBattleField(field.Field, out _, out var member)
                    && ZaRuntimeMoveData.GetValue(baseVariant, member) is { } value)
                {
                    vanillaValues.Add(new ZaMoveVanillaFieldValue(field.Field, value));
                }
            }
        }

        foreach (var baseTiming in baseTimingRecords)
        {
            foreach (var field in runtimeEditableFields.Where(candidate =>
                         candidate.Field.StartsWith(ZaRuntimeMoveData.TimingPrefix, StringComparison.Ordinal)))
            {
                if (!ZaRuntimeMoveData.TryParseTimingField(
                        field.Field,
                        out _,
                        out var templateOccurrence,
                        out var member)
                    || (templateOccurrence is null && baseTiming.Occurrence != 0)
                    || (templateOccurrence is not null && templateOccurrence != baseTiming.Occurrence)
                    || ZaRuntimeMoveData.GetValue(baseTiming, member) is not { } value)
                {
                    continue;
                }

                var exactField = templateOccurrence is null
                    ? ZaRuntimeMoveData.TimingSharedField(baseTiming.TimingMoveId, member)
                    : ZaRuntimeMoveData.TimingField(
                        baseTiming.TimingMoveId,
                        baseTiming.Occurrence,
                        member);
                vanillaValues.Add(new ZaMoveVanillaFieldValue(exactField, value));

                if (baseTiming.TimingMoveId == move.MoveId)
                {
                    vanillaValues.Add(new ZaMoveVanillaFieldValue(field.Field, value));
                }
            }
        }

        return (primary is null
                ? move
                : move with
                {
                    Type = primary.Type,
                    TypeName = primary.TypeName,
                    Category = primary.DamageType,
                    CategoryName = primary.DamageTypeName,
                    Power = primary.Power,
                    Accuracy = timing?.HitPercent ?? move.Accuracy,
                    CritStage = primary.CriticalRank,
                    HitMin = timing?.ProjectileCountMin ?? move.HitMin,
                    HitMax = timing?.ProjectileCountMax ?? move.HitMax,
                    TurnMin = primary.ConditionTurnMin,
                    TurnMax = primary.ConditionTurnMax,
                    Inflict = primary.ConditionId,
                    InflictName = FormatInflict(primary.ConditionId),
                    InflictPercent = primary.ConditionPercent,
                    RawInflictCount = primary.ConditionCount,
                    Recoil = primary.DamageDrainRatio,
                    RawHealing = primary.HpRecoverRatio,
                    StatChanges = primary.StatChanges,
                }) with
        {
            RuntimeVariants = variants,
            Timing = timing,
            TimingRows = timingRecords,
            VanillaValues = vanillaValues,
            RuntimeSourceFiles = [battleSourceFile, timingSourceFile],
            RuntimeBattleSourceLayer = battleSourceLayer,
            RuntimeTimingSourceLayer = timingSourceLayer,
            VanillaRuntimeVariants = baseVariants,
            AmbiguousRuntimeVariantIds = ambiguousVariantIds,
            VanillaTimingRows = baseTimingRecords,
            RuntimeBattleVanillaFingerprint = battleVanillaFingerprint,
            RuntimeTimingVanillaFingerprint = timingVanillaFingerprint,
            RuntimeBattleDiffersFromVanilla = battleDiffersFromVanilla,
            RuntimeTimingDiffersFromVanilla = timingDiffersFromVanilla,
            CanRevertToVanilla = canRevertToVanilla,
            RevertToVanillaBlockedReason = revertBlockedReason,
        };
    }

    private static (
        IReadOnlyList<ZaMoveRuntimeVariantRecord> Variants,
        IReadOnlySet<int> AmbiguousVariantIds) ProjectBattleRows(
        IReadOnlyList<ZaBattleMoveParameterT> rows)
    {
        var variants = new List<ZaMoveRuntimeVariantRecord>();
        var ambiguous = new HashSet<int>();
        foreach (var group in rows
                     .GroupBy(row => checked((int)row.VariantType))
                     .OrderBy(group => group.Key))
        {
            var occurrences = group.ToArray();
            var fingerprints = occurrences
                .Select(row => ZaRuntimeMoveData.CreateBattleRowsFingerprint([row]))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (fingerprints.Length > 1)
            {
                ambiguous.Add(group.Key);
            }

            variants.Add(ZaRuntimeMoveData.ToRecord(occurrences[0]));
        }

        return (variants, ambiguous);
    }

    private static IReadOnlyList<ZaMoveTimingRecord> ProjectTimingRows(
        IReadOnlyList<ZaMoveTimingParameterT> rows,
        IReadOnlyList<string> spawnLocators)
    {
        return rows
            .GroupBy(row => row.MoveId)
            .OrderBy(group => group.Key)
            .SelectMany(group => group.Select((row, occurrence) =>
                ZaRuntimeMoveData.ToRecord(row, occurrence, spawnLocators)))
            .ToArray();
    }

    private static IReadOnlyList<ZaMoveStatChangeRecord> ToStatChangeRecords(ZaMoveStatChanges? statChanges)
    {
        if (statChanges is not { } row)
        {
            return
            [
                new(1, 0, FormatStat(0), 0, 0),
                new(2, 0, FormatStat(0), 0, 0),
                new(3, 0, FormatStat(0), 0, 0),
            ];
        }

        return
        [
            new(1, row.Stat1, FormatStat(row.Stat1), row.Stat1Stage, row.Stat1Chance),
            new(2, row.Stat2, FormatStat(row.Stat2), row.Stat2Stage, row.Stat2Chance),
            new(3, row.Stat3, FormatStat(row.Stat3), row.Stat3Stage, row.Stat3Chance),
        ];
    }

    private static IReadOnlyList<ZaMoveFlagRecord> ToFlagRecords(ZaMoveData move)
    {
        return
        [
            new(MakesContactField, "Makes Contact", move.FlagMakesContact),
            new(ChargeField, "Charge Turn", move.FlagCharge),
            new(RechargeField, "Recharge Turn", move.FlagRecharge),
            new(ProtectField, "Blocked By Protect", move.FlagProtect),
            new(ReflectableField, "Reflectable", move.FlagReflectable),
            new(SnatchField, "Snatchable", move.FlagSnatch),
            new(MirrorField, "Mirror Move", move.FlagMirror),
            new(PunchField, "Punch Move", move.FlagPunch),
            new(SoundField, "Sound Move", move.FlagSound),
            new(DanceField, "Dance Move", move.FlagDance),
            new(GravityField, "Fails Under Gravity", move.FlagGravity),
            new(DefrostField, "Thaws User", move.FlagDefrost),
            new(DistanceTripleField, "Triple Battle Distance", move.FlagDistanceTriple),
            new(HealField, "Heal Move", move.FlagHeal),
            new(IgnoreSubstituteField, "Ignores Substitute", move.FlagIgnoreSubstitute),
            new(FailSkyBattleField, "Fails In Sky Battle", move.FlagFailSkyBattle),
            new(AnimateAllyField, "Animate Ally", move.FlagAnimateAlly),
            new(MetronomeField, "Callable By Metronome", move.FlagMetronome),
            new(FailEncoreField, "Fails During Encore", move.FlagFailEncore),
            new(FailMeFirstField, "Fails With Me First", move.FlagFailMeFirst),
            new(FutureAttackField, "Future Attack", move.FlagFutureAttack),
            new(PressureField, "Affected By Pressure", move.FlagPressure),
            new(ComboField, "Combo Move", move.FlagCombo),
            new(NoSleepTalkField, "Blocked From Sleep Talk", move.FlagNoSleepTalk),
            new(NoAssistField, "Blocked From Assist", move.FlagNoAssist),
            new(FailCopycatField, "Fails With Copycat", move.FlagFailCopycat),
            new(FailMimicField, "Fails With Mimic", move.FlagFailMimic),
            new(FailInstructField, "Fails With Instruct", move.FlagFailInstruct),
            new(PowderField, "Powder Move", move.FlagPowder),
            new(BiteField, "Bite Move", move.FlagBite),
            new(BulletField, "Bullet Move", move.FlagBullet),
            new(NoMultiHitField, "Cannot Multi-hit", move.FlagNoMultiHit),
            new(NoEffectivenessField, "Ignores Type Effectiveness", move.FlagNoEffectiveness),
            new(SheerForceField, "Boosted By Sheer Force", move.FlagSheerForce),
            new(SlicingField, "Slicing Move", move.FlagSlicing),
            new(WindField, "Wind Move", move.FlagWind),
            new("unknown56", "Unknown Flag 56", move.Unknown56),
            new("unknown57", "Unknown Flag 57", move.Unknown57),
            new("unknown58", "Unknown Flag 58", move.Unknown58),
            new("unknown59", "Unknown Flag 59", move.Unknown59),
            new("unknown60", "Unknown Flag 60", move.Unknown60),
            new(CantUseTwiceField, "Cannot Use Twice In A Row", move.FlagCantUseTwice),
        ];
    }

    private static ZaMoveEditableField Field(
        string field,
        string label,
        string valueKind,
        double? minimumValue,
        double? maximumValue,
        IReadOnlyList<ZaMoveEditableFieldOption>? options = null)
    {
        return new ZaMoveEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? []);
    }

    private static IReadOnlyList<ZaMoveEditableField> CreateRuntimeVariantFields(int variant)
    {
        var prefix = $"battle.{variant.ToString(CultureInfo.InvariantCulture)}.";
        return
        [
            Field(prefix + "effectCategory", "Effect category (raw)", "integer", 0, 13, RuntimeEffectCategoryOptions),
            Field(prefix + "type", "Type", "integer", 0, 17, TypeOptions),
            Field(prefix + "damageType", "Damage class", "integer", 0, 2, CategoryOptions),
            Field(prefix + "power", "Power", "integer", 0, byte.MaxValue),
            Field(prefix + "criticalRank", "Critical rank", "integer", 0, 6, RuntimeCriticalRankOptions),
            Field(prefix + "hpRecoverRatio", "HP recovery (%)", "integer", 0, 100),
            Field(prefix + "shrinkPercent", "Shrink (%)", "integer", 0, 100),
            Field(prefix + "conditionId", "Condition", "integer", 0, 46, RuntimeConditionOptions),
            Field(prefix + "conditionPercent", "Condition chance (%)", "integer", 0, 100),
            Field(prefix + "conditionCount", "Condition duration mode", "integer", 0, 2, RuntimeConditionModeOptions),
            Field(prefix + "conditionTurnMin", "Minimum condition turns", "integer", 0, 15),
            Field(prefix + "conditionTurnMax", "Maximum condition turns", "integer", 0, 15),
            Field(prefix + "stat1", "Stat change 1: Stat", "integer", 0, 9, RuntimeStatOptions),
            Field(prefix + "stat1Stage", "Stat change 1: Stage delta", "integer", -6, 6),
            Field(prefix + "stat1Percent", "Stat change 1: Chance (%)", "integer", 0, 100),
            Field(prefix + "stat2", "Stat change 2: Stat", "integer", 0, 9, RuntimeStatOptions),
            Field(prefix + "stat2Stage", "Stat change 2: Stage delta", "integer", -6, 6),
            Field(prefix + "stat2Percent", "Stat change 2: Chance (%)", "integer", 0, 100),
            Field(prefix + "stat3", "Stat change 3: Stat", "integer", 0, 9, RuntimeStatOptions),
            Field(prefix + "stat3Stage", "Stat change 3: Stage delta", "integer", -6, 6),
            Field(prefix + "stat3Percent", "Stat change 3: Chance (%)", "integer", 0, 100),
            Field(prefix + "damageRecoverRatio", "Recovery / recoil (%)", "integer", -100, 100),
            Field(prefix + "damageDrainRatio", "Damage drained as HP (%)", "integer", 0, 100),
            Field(prefix + "isGuard", "Guard move", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "isAvoidedByFloating", "Avoided by floating", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "makesContact", "Makes contact", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "isSlicing", "Slicing move", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "isWind", "Wind move", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "bypassesSubstitute", "Bypasses substitute", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "thawsUser", "Thaws user", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "restoresHp", "Restores HP", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "allowedWhileHealBlocked", "Allowed while heal blocked", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "callableByMetronome", "Callable by Metronome", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "appliesCondition", "Applies condition", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "blockedByProtect", "Blocked by Protect", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "cannotKnockOut", "Cannot knock out", "boolean", 0, 1, BooleanOptions),
            Field(prefix + "valueEffectRatio", "Value effect ratio (raw)", "integer", 0, 20, RuntimeValueEffectRatioOptions),
        ];
    }

    private static IReadOnlyList<ZaMoveEditableField> CreateAdvancedTimingFields(
        int occurrence,
        IReadOnlyList<string> spawnLocators,
        double projectileMaximum)
    {
        var prefix = $"timing.{occurrence.ToString(CultureInfo.InvariantCulture)}.";
        var locatorOptions = spawnLocators
            .Select((locator, index) => new ZaMoveEditableFieldOption(
                index,
                index == 0
                    ? "0 Empty / default"
                    : $"{index.ToString(CultureInfo.InvariantCulture)} {locator}"))
            .ToArray();
        return
        [
            Field(prefix + "chargeFrames", "Charge frames", "integer", 0, 500),
            Field(prefix + "attackLoopFrames", "Attack loop frames", "integer", 0, 380),
            Field(prefix + "spawnOrigin", "Spawn origin (raw)", "integer", 0, 7, RuntimeSpawnOriginOptions),
            Field(prefix + "spawnLocator", "Spawn locator", "integer", 0, locatorOptions.Length - 1, locatorOptions),
            Field(prefix + "spawnOffsetX", "Spawn offset X", "decimal", 0, 1),
            Field(prefix + "spawnOffsetY", "Spawn offset Y", "decimal", -0.25, 60),
            Field(prefix + "spawnOffsetZ", "Spawn offset Z", "decimal", -0.1, 5),
            Field(prefix + "shotDirection", "Shot direction (raw)", "integer", 0, 2, RuntimeShotDirectionOptions),
            Field(prefix + "targetCorrectionType", "Target correction (raw)", "integer", 0, 1, RuntimeTargetCorrectionOptions),
            Field(prefix + "impactMotionSpeed", "Impact motion speed", "decimal", 0, 2),
            Field(prefix + "movementType", "Movement type (raw)", "integer", 0, 3, RuntimeMovementTypeOptions),
            Field(prefix + "rangeMin", "Minimum range", "decimal", 0, 4),
            Field(prefix + "rangeMax", "Maximum range", "decimal", 0, 99),
            Field(prefix + "heightTolerance", "Height tolerance", "decimal", 0, 2, RuntimeHeightToleranceOptions),
            Field(prefix + "effectiveRange", "Effective range", "decimal", 0, 99),
            Field(prefix + "projectileCountMin", "Minimum projectile count", "integer", 0, 6),
            Field(prefix + "projectileCountMax", "Maximum projectile count", "integer", 0, 6),
            Field(prefix + "effectTime", "Effect time", "decimal", 0, 45),
            Field(prefix + "effectValue", "Effect value (raw)", "integer", 0, 0, RuntimeEffectValueOptions),
            Field(prefix + "megaPowerBonus", "Mega Power bonus", "decimal", 0, 5.75, RuntimeMegaPowerBonusOptions),
            Field(prefix + "playedMotionSpeed", "Played motion speed", "decimal", 0, 2),
            Field(prefix + "overwriteProjectile1", "Projectile override 1", "integer", 0, projectileMaximum),
            Field(prefix + "replacementProjectile1", "Projectile replacement 1", "integer", 0, projectileMaximum),
            Field(prefix + "overwriteProjectile2", "Projectile override 2", "integer", 0, projectileMaximum),
            Field(prefix + "replacementProjectile2", "Projectile replacement 2", "integer", 0, projectileMaximum),
            Field(prefix + "overwriteProjectile3", "Projectile override 3", "integer", 0, projectileMaximum),
            Field(prefix + "replacementProjectile3", "Projectile replacement 3", "integer", 0, projectileMaximum),
            Field(prefix + "overwriteProjectile4", "Projectile override 4", "integer", 0, projectileMaximum),
            Field(prefix + "replacementProjectile4", "Projectile replacement 4", "integer", 0, projectileMaximum),
            Field(prefix + "overwriteProjectile5", "Projectile override 5", "integer", 0, projectileMaximum),
            Field(prefix + "replacementProjectile5", "Projectile replacement 5", "integer", 0, projectileMaximum),
            Field(prefix + "projectileCorrectionScale", "Projectile correction scale", "decimal", 0, 10),
        ];
    }

    private static IReadOnlyList<ZaMoveEditableField> CreateRuntimeEditableFields(
        int timingOccurrenceCount,
        IReadOnlyList<string> spawnLocators,
        IReadOnlyList<ZaMoveEditableFieldOption> projectileOptions)
    {
        var projectileMaximum = projectileOptions.Count == 0
            ? 0
            : projectileOptions.Max(option => option.Value);
        return RuntimeBaseEditableFields
            .Concat(Enumerable.Range(0, timingOccurrenceCount)
                .SelectMany(occurrence => CreateAdvancedTimingFields(
                    occurrence,
                    spawnLocators,
                    projectileMaximum)))
            .Where(field => projectileOptions.Count > 0 || !IsProjectileField(field.Field))
            .ToArray();
    }

    private static IReadOnlyList<ZaMoveEditableFieldOption> CreateRawOptions(params int[] values)
    {
        return values
            .Select(value => new ZaMoveEditableFieldOption(
                value,
                value.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private static IReadOnlyList<ZaMoveEditableFieldOption> CreateIndexedOptions(IReadOnlyList<string> names)
    {
        return names
            .Select((name, index) => new ZaMoveEditableFieldOption(
                index,
                $"{index.ToString(CultureInfo.InvariantCulture)} {name}"))
            .ToArray();
    }

    private static string FormatIndexed(int value, IReadOnlyList<string> names, string fallbackPrefix)
    {
        return (uint)value < (uint)names.Count && !string.IsNullOrWhiteSpace(names[value])
            ? names[value]
            : $"{fallbackPrefix} {value.ToString(CultureInfo.InvariantCulture)}";
    }
}
