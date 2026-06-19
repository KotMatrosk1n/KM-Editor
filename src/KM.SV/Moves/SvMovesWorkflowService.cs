// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Moves;

internal sealed class SvMovesWorkflowService
{
    private const string WorkflowLabel = "Moves";
    private const string WorkflowDescription = "Edit Scarlet/Violet battle move data from avalon waza records.";

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
        [65535] = "Tri Attack Status",
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
        "All",
    ];

    private static readonly IReadOnlyList<SvMoveEditableFieldOption> BooleanOptions =
    [
        new(0, "No"),
        new(1, "Yes"),
    ];

    private static readonly IReadOnlyList<SvMoveEditableFieldOption> TypeOptions =
        CreateIndexedOptions(TypeNames);

    private static readonly IReadOnlyList<SvMoveEditableFieldOption> CategoryOptions =
        CreateIndexedOptions(CategoryNames);

    private static readonly IReadOnlyList<SvMoveEditableFieldOption> TargetOptions =
        CreateIndexedOptions(TargetNames);

    private static readonly IReadOnlyList<SvMoveEditableFieldOption> StatOptions =
        [new(-1, "-1 Unused"), .. CreateIndexedOptions(StatNames)];

    private static readonly IReadOnlyList<SvMoveEditableFieldOption> InflictOptions =
        InflictNames
            .OrderBy(entry => entry.Key)
            .Select(entry => new SvMoveEditableFieldOption(entry.Key, $"{entry.Key:000} {entry.Value}"))
            .ToArray();

    private static readonly IReadOnlyList<SvMoveEditableFieldOption> HealingOptions =
    [
        new(0, "000 None"),
        new(-3, "253 Quarter HP (-3 raw)"),
        new(-2, "254 Half HP (-2 raw)"),
        new(-1, "255 Full HP (-1 raw)"),
    ];

    private static readonly IReadOnlyList<SvMoveEditableField> EditableFields =
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
        Field(RawHealingField, "Healing behavior", "integer", sbyte.MinValue, sbyte.MaxValue, HealingOptions),
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

    private readonly SvWorkflowFileSource fileSource;

    public SvMovesWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Moves,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvMovesWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        SvWorkflowFile? source = null;
        var labels = SvTextLabelLookup.None();
        var moves = Array.Empty<SvMoveRecord>();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics);
            source = fileSource.Read(project, SvDataPaths.MoveDataArray);
            moves = LoadRecords(source, labels).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Moves could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.MoveDataArray}"));
        }

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Moves,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SvMovesWorkflow(
            summary,
            moves,
            EditableFields,
            new SvMovesWorkflowStats(
                moves.Length,
                moves.Count(move => move.CanUseMove),
                source is null ? 0 : 1,
                moves.Sum(move => move.Flags.Count(flag => flag.Enabled))),
            diagnostics);
    }

    internal static SvMoveEditableField? GetEditableField(string? field)
    {
        return EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
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

    private static IEnumerable<SvMoveRecord> LoadRecords(
        SvWorkflowFile source,
        SvTextLabelLookup labels)
    {
        var table = global::SvMoveDataArray.GetRootAsSvMoveDataArray(new ByteBuffer(source.Bytes));
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var move = table.Values(index);
            if (move is not null)
            {
                yield return ToRecord(move.Value, labels, source);
            }
        }
    }

    private static SvMoveRecord ToRecord(
        global::SvMoveData move,
        SvTextLabelLookup labels,
        SvWorkflowFile source)
    {
        var moveId = move.MoveId;
        var inflict = move.Inflict ?? default;
        var statChanges = move.StatChanges ?? default;
        var flags = ToFlagRecords(move);

        return new SvMoveRecord(
            moveId,
            labels.Move(moveId),
            Description: null,
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
            [
                new(1, statChanges.Stat1, FormatStat(statChanges.Stat1), statChanges.Stat1Stage, statChanges.Stat1Chance),
                new(2, statChanges.Stat2, FormatStat(statChanges.Stat2), statChanges.Stat2Stage, statChanges.Stat2Chance),
                new(3, statChanges.Stat3, FormatStat(statChanges.Stat3), statChanges.Stat3Stage, statChanges.Stat3Chance),
            ],
            flags,
            new SvMoveProvenance(source.RelativePath, source.SourceLayer, source.FileState));
    }

    private static IReadOnlyList<SvMoveFlagRecord> ToFlagRecords(global::SvMoveData move)
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

    private static SvMoveEditableField Field(
        string field,
        string label,
        string valueKind,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SvMoveEditableFieldOption>? options = null)
    {
        return new SvMoveEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? []);
    }

    private static IReadOnlyList<SvMoveEditableFieldOption> CreateIndexedOptions(IReadOnlyList<string> names)
    {
        return names
            .Select((name, index) => new SvMoveEditableFieldOption(
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
