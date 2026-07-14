// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;

namespace KM.SwSh.Moves;

public sealed class SwShMovesWorkflowService
{
    public const string TypeField = "type";
    public const string CanUseMoveField = "canUseMove";
    public const string QualityField = "quality";
    public const string CategoryField = "category";
    public const string PowerField = "power";
    public const string AccuracyField = "accuracy";
    public const string PpField = "pp";
    public const string PriorityField = "priority";
    public const string CritStageField = "critStage";
    public const string MaxMovePowerField = "maxMovePower";
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
    public const string GravityField = "gravity";
    public const string DefrostField = "defrost";
    public const string DistanceTripleField = "distanceTriple";
    public const string HealField = "heal";
    public const string IgnoreSubstituteField = "ignoreSubstitute";
    public const string FailSkyBattleField = "failSkyBattle";
    public const string AnimateAllyField = "animateAlly";
    public const string DanceField = "dance";
    public const string MetronomeField = "metronome";
    public const int MinimumByteValue = byte.MinValue;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MinimumSignedByteValue = sbyte.MinValue;
    public const int MaximumSignedByteValue = sbyte.MaxValue;
    public const int MaximumUnsignedShortValue = ushort.MaxValue;
    public const string MoveDataDirectory = SwShMoveDataFile.MoveDataRelativeDirectory;
    public const string EnglishMoveNamePath = "romfs/bin/message/English/common/wazaname.dat";
    public const string EnglishMoveDescriptionPath = "romfs/bin/message/English/common/wazainfo.dat";
    public const string EnglishTypeNamePath = "romfs/bin/message/English/common/typename.dat";

    private static readonly IReadOnlyList<string> FallbackTypeNames =
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
        [65535] = "Move-defined / scripted effect",
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
        "All Stats",
    ];

    private static readonly IReadOnlyList<string> QualityNames =
    [
        "Damage Only",
        "Status Only",
        "Stat Change Only",
        "Heal User",
        "Damage + Status",
        "Status + Target Stat",
        "Damage + Target Stat Drop",
        "Damage + User Stat Raise",
        "Damage Drain",
        "One-Hit KO",
        "Whole Field",
        "One Side Of Field",
        "Force Target Switch",
        "Unique Effect",
    ];

    private static readonly IReadOnlyList<SwShMoveEditableFieldOption> TypeOptions =
        CreateIndexedOptions(FallbackTypeNames);

    private static readonly IReadOnlyList<SwShMoveEditableFieldOption> CategoryOptions =
        CreateIndexedOptions(CategoryNames);

    private static readonly IReadOnlyList<SwShMoveEditableFieldOption> QualityOptions =
        CreateIndexedOptions(QualityNames);

    private static readonly IReadOnlyList<SwShMoveEditableFieldOption> TargetOptions =
        CreateIndexedOptions(TargetNames);

    private static readonly IReadOnlyList<SwShMoveEditableFieldOption> InflictOptions =
        InflictNames
            .OrderBy(entry => entry.Key)
            .Select(entry => new SwShMoveEditableFieldOption(entry.Key, $"{entry.Key:000} {entry.Value}"))
            .ToArray();

    private static readonly IReadOnlyList<SwShMoveEditableFieldOption> InflictDurationOptions =
    [
        new SwShMoveEditableFieldOption(0, "000 None"),
        new SwShMoveEditableFieldOption(1, "001 Permanent"),
        new SwShMoveEditableFieldOption(2, "002 Turn Count + Switch"),
        new SwShMoveEditableFieldOption(3, "003 Permanent + Switch"),
        new SwShMoveEditableFieldOption(4, "004 Turn Count + No Switch"),
    ];

    private static readonly IReadOnlyList<SwShMoveEditableFieldOption> StatOptions =
        CreateIndexedOptions(StatNames);

    private static readonly IReadOnlyList<SwShMoveEditableFieldOption> CritStageOptions =
    [
        new SwShMoveEditableFieldOption(0, "000 Normal critical-hit ratio"),
        new SwShMoveEditableFieldOption(1, "001 High critical-hit ratio"),
        new SwShMoveEditableFieldOption(2, "002 Very high critical-hit ratio"),
        new SwShMoveEditableFieldOption(6, "006 Always critical hit"),
    ];

    private static readonly IReadOnlyList<SwShMoveEditableFieldOption> MaxMovePowerOptions =
        new[] { 0, 1, 70, 75, 80, 85, 90, 95, 100, 110, 120, 130, 140, 150 }
            .Select(value => new SwShMoveEditableFieldOption(value, $"{value:000} Max Move power"))
            .ToArray();

    private static readonly IReadOnlyList<SwShMoveEditableField> EditableFields =
    [
        CreateField(CanUseMoveField, "Can use move", "boolean", 0, 1),
        CreateField(TypeField, "Type", "integer", 0, 17, TypeOptions),
        CreateField(QualityField, "Quality", "integer", 0, 13, QualityOptions),
        CreateField(CategoryField, "Category", "integer", 0, 2, CategoryOptions),
        CreateField(PowerField, "Power", "integer", MinimumByteValue, MaximumByteValue),
        CreateField(AccuracyField, "Accuracy", "integer", 0, 101),
        CreateField(PpField, "PP", "integer", 1, 40),
        CreateField(PriorityField, "Priority", "integer", -7, 5),
        CreateField(CritStageField, "Critical-hit stage", "integer", 0, 6, CritStageOptions),
        CreateField(MaxMovePowerField, "Max Move power", "integer", 0, 150, MaxMovePowerOptions),
        CreateField(TargetField, "Target", "integer", 0, 13, TargetOptions),
        CreateField(HitMinField, "Minimum hits", "integer", 0, 6),
        CreateField(HitMaxField, "Maximum hits", "integer", 0, 6),
        CreateField(TurnMinField, "Minimum inflict turns", "integer", 0, 15),
        CreateField(TurnMaxField, "Maximum inflict turns", "integer", 0, 15),
        CreateField(InflictField, "Inflicted condition", "integer", MinimumByteValue, MaximumUnsignedShortValue, InflictOptions),
        CreateField(InflictPercentField, "Inflict chance (%)", "integer", 0, 100),
        CreateField(RawInflictCountField, "Inflict duration", "integer", 0, 4, InflictDurationOptions),
        CreateField(FlinchField, "Flinch chance (%)", "integer", 0, 100),
        CreateField(EffectSequenceField, "Effect sequence ID (raw)", "integer", MinimumByteValue, MaximumUnsignedShortValue),
        CreateField(RecoilField, "Drain (+) / recoil (-) (%)", "integer", MinimumSignedByteValue, MaximumSignedByteValue),
        CreateField(RawHealingField, "HP recovery (+) / HP cost (-) (%) (raw)", "integer", MinimumSignedByteValue, MaximumSignedByteValue),
        CreateField(Stat1Field, "Stat Change 1: Stat", "integer", 0, 8, StatOptions),
        CreateField(Stat1StageField, "Stat Change 1: Stage Delta", "integer", -6, 6),
        CreateField(Stat1PercentField, "Stat Change 1: Chance (%)", "integer", 0, 100),
        CreateField(Stat2Field, "Stat Change 2: Stat", "integer", 0, 8, StatOptions),
        CreateField(Stat2StageField, "Stat Change 2: Stage Delta", "integer", -6, 6),
        CreateField(Stat2PercentField, "Stat Change 2: Chance (%)", "integer", 0, 100),
        CreateField(Stat3Field, "Stat Change 3: Stat", "integer", 0, 8, StatOptions),
        CreateField(Stat3StageField, "Stat Change 3: Stage Delta", "integer", -6, 6),
        CreateField(Stat3PercentField, "Stat Change 3: Chance (%)", "integer", 0, 100),
        CreateField(MakesContactField, "Makes contact", "boolean", 0, 1),
        CreateField(ChargeField, "Charge turn", "boolean", 0, 1),
        CreateField(RechargeField, "Recharge turn", "boolean", 0, 1),
        CreateField(ProtectField, "Blocked by Protect", "boolean", 0, 1),
        CreateField(ReflectableField, "Reflectable", "boolean", 0, 1),
        CreateField(SnatchField, "Snatchable", "boolean", 0, 1),
        CreateField(MirrorField, "Mirror Move", "boolean", 0, 1),
        CreateField(PunchField, "Punch move", "boolean", 0, 1),
        CreateField(SoundField, "Sound move", "boolean", 0, 1),
        CreateField(GravityField, "Fails under gravity", "boolean", 0, 1),
        CreateField(DefrostField, "Thaws user", "boolean", 0, 1),
        CreateField(DistanceTripleField, "Triple battle distance", "boolean", 0, 1),
        CreateField(HealField, "Heal move", "boolean", 0, 1),
        CreateField(IgnoreSubstituteField, "Ignores substitute", "boolean", 0, 1),
        CreateField(FailSkyBattleField, "Fails in Sky Battle", "boolean", 0, 1),
        CreateField(AnimateAllyField, "Animate ally", "boolean", 0, 1),
        CreateField(DanceField, "Dance move", "boolean", 0, 1),
        CreateField(MetronomeField, "Callable by Metronome", "boolean", 0, 1),
    ];

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Moves Data requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShMovesWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, [], sourceFileCount: 0, diagnostics);
        }

        var moveSources = ResolveWorkflowFiles(project, MoveDataDirectory)
            .Where(source => IsMoveDataFile(source.GraphEntry.RelativePath))
            .OrderBy(source => source.GraphEntry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (moveSources.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Moves data is not available for this project.",
                expected: $"{MoveDataDirectory}/**/*.wazabin or {MoveDataDirectory}/**/*.bin"));
            return CreateWorkflow(summary, [], sourceFileCount: 0, diagnostics);
        }

        var moveNames = LoadOptionalTextTable(project, "wazaname.dat", "Move names", diagnostics);
        var moveDescriptions = LoadOptionalTextTable(project, "wazainfo.dat", "Move descriptions", diagnostics);
        var loadedTypeNames = LoadOptionalTextTable(project, "typename.dat", "Type names", diagnostics);
        var typeNames = NormalizeTypeNames(loadedTypeNames);
        var moves = new List<SwShMoveRecord>();
        var parsedSourceFileCount = 0;

        foreach (var source in moveSources)
        {
            try
            {
                var moveFile = SwShMoveDataFile.Parse(File.ReadAllBytes(source.AbsolutePath));
                parsedSourceFileCount++;
                if (moveFile.Record.MoveId > int.MaxValue)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Move data source has unsupported move ID {moveFile.Record.MoveId}.",
                        file: source.GraphEntry.RelativePath,
                        expected: $"Move ID between 0 and {int.MaxValue}"));
                    continue;
                }

                moves.Add(ToMoveRecord(
                    moveFile.Record,
                    moveNames,
                    moveDescriptions,
                    typeNames,
                    CreateProvenance(source.GraphEntry)));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Move data source is not supported: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Sword/Shield Waza FlatBuffer"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move data source could not be read: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Readable Sword/Shield move data"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move data source could not be read: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Readable Sword/Shield move data"));
            }
        }

        var sourceFileCount =
            parsedSourceFileCount
            + (moveNames.Count > 0 ? 1 : 0)
            + (moveDescriptions.Count > 0 ? 1 : 0)
            + (loadedTypeNames.Count > 0 ? 1 : 0);

        var deduplicatedMoves = DeduplicateMoveRecords(moves);

        return CreateWorkflow(
            summary,
            deduplicatedMoves,
            sourceFileCount,
            diagnostics,
            typeNames);
    }

    private static IReadOnlyList<SwShMoveRecord> DeduplicateMoveRecords(IEnumerable<SwShMoveRecord> moves)
    {
        return moves
            .GroupBy(move => move.MoveId)
            .Select(group => group
                .OrderByDescending(move => IsCanonicalMoveDataFile(move.Provenance.SourceFile, move.MoveId))
                .ThenByDescending(move => move.Provenance.SourceLayer == ProjectFileLayer.Layered)
                .ThenByDescending(move => IsPreferredMoveDataFile(move.Provenance.SourceFile))
                .ThenBy(move => move.Provenance.SourceFile, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(move => move.MoveId)
            .ThenBy(move => move.Provenance.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsPreferredMoveDataFile(string relativePath)
    {
        return relativePath.EndsWith(".wazabin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCanonicalMoveDataFile(string relativePath, int moveId)
    {
        var fileName = Path.GetFileName(relativePath.Replace('/', Path.DirectorySeparatorChar));
        return string.Equals(fileName, $"waza{moveId:D4}.wazabin", StringComparison.OrdinalIgnoreCase);
    }

    private static SwShMoveRecord ToMoveRecord(
        SwShMoveDataRecord move,
        IReadOnlyList<string> moveNames,
        IReadOnlyList<string> moveDescriptions,
        IReadOnlyList<string> typeNames,
        SwShMoveProvenance provenance)
    {
        var moveId = checked((int)move.MoveId);
        var flags = ToFlagRecords(move.Flags);

        return new SwShMoveRecord(
            moveId,
            GetIndexedName(moveId, moveNames, "Move"),
            GetOptionalIndexedText(moveId, moveDescriptions),
            move.Version,
            move.CanUseMove,
            move.Core.Type,
            GetIndexedName(move.Core.Type, typeNames, "Type"),
            move.Core.Quality,
            move.Core.Category,
            GetIndexedName(move.Core.Category, CategoryNames, "Category"),
            move.Core.Power,
            move.Core.Accuracy,
            move.Core.PP,
            move.Core.Priority,
            move.Core.CritStage,
            move.Core.GigantamaxPower,
            move.Targeting.RawTarget,
            GetIndexedName(move.Targeting.RawTarget, TargetNames, "Target"),
            move.Targeting.HitMin,
            move.Targeting.HitMax,
            move.Targeting.TurnMin,
            move.Targeting.TurnMax,
            move.Secondary.Inflict,
            GetInflictName(move.Secondary.Inflict),
            move.Secondary.InflictPercent,
            move.Secondary.RawInflictCount,
            move.Secondary.Flinch,
            move.Secondary.EffectSequence,
            move.Secondary.Recoil,
            move.Secondary.RawHealing,
            move.StatChanges
                .Select(stat => new SwShMoveStatChangeRecord(
                    stat.Slot,
                    stat.Stat,
                    GetIndexedName(stat.Stat, StatNames, "Stat"),
                    stat.Stage,
                    stat.Percent))
                .ToArray(),
            flags,
            provenance);
    }

    private static bool IsMoveDataFile(string relativePath)
    {
        return relativePath.EndsWith(".wazabin", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SwShMoveFlagRecord> ToFlagRecords(SwShMoveFlags flags)
    {
        return
        [
            new("makesContact", "Makes Contact", flags.MakesContact),
            new("charge", "Charge Turn", flags.Charge),
            new("recharge", "Recharge Turn", flags.Recharge),
            new("protect", "Blocked By Protect", flags.Protect),
            new("reflectable", "Reflectable", flags.Reflectable),
            new("snatch", "Snatchable", flags.Snatch),
            new("mirror", "Mirror Move", flags.Mirror),
            new("punch", "Punch Move", flags.Punch),
            new("sound", "Sound Move", flags.Sound),
            new("gravity", "Fails Under Gravity", flags.Gravity),
            new("defrost", "Thaws User", flags.Defrost),
            new("distanceTriple", "Triple Battle Distance", flags.DistanceTriple),
            new("heal", "Heal Move", flags.Heal),
            new("ignoreSubstitute", "Ignores Substitute", flags.IgnoreSubstitute),
            new("failSkyBattle", "Fails In Sky Battle", flags.FailSkyBattle),
            new("animateAlly", "Animate Ally", flags.AnimateAlly),
            new("dance", "Dance Move", flags.Dance),
            new("metronome", "Callable By Metronome", flags.Metronome),
        ];
    }

    private static string GetInflictName(int inflict)
    {
        return InflictNames.TryGetValue(inflict, out var label)
            ? label
            : $"Inflict {inflict}";
    }

    private static string GetIndexedName(int id, IReadOnlyList<string> names, string fallbackPrefix)
    {
        if ((uint)id < (uint)names.Count && !string.IsNullOrWhiteSpace(names[id]))
        {
            return names[id];
        }

        return $"{fallbackPrefix} {id}";
    }

    private static IReadOnlyList<string> NormalizeTypeNames(IReadOnlyList<string> loadedTypeNames)
    {
        return FallbackTypeNames
            .Select((fallbackName, index) =>
                (uint)index < (uint)loadedTypeNames.Count
                && !string.IsNullOrWhiteSpace(loadedTypeNames[index])
                    ? loadedTypeNames[index]
                    : fallbackName)
            .ToArray();
    }

    private static string? GetOptionalIndexedText(int id, IReadOnlyList<string> values)
    {
        return (uint)id < (uint)values.Count && !string.IsNullOrWhiteSpace(values[id])
            ? values[id]
            : null;
    }

    private static IReadOnlyList<string> LoadOptionalTextTable(
        OpenedProject project,
        string fileName,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var relativePath = ResolveCommonTextPath(project, fileName);
        var source = ResolveWorkflowFile(project, relativePath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{label} are not available; numeric fallback labels will be shown.",
                expected: $"romfs/bin/message/{{language}}/common/{fileName}"));
            return [];
        }

        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{label} table could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield message .dat"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{label} table could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield message .dat"));
        }

        return [];
    }

    private static string ResolveCommonTextPath(OpenedProject project, string fileName)
    {
        var language = SwShGameTextLanguage.Resolve(project.Paths);
        var preferred = SwShGameTextLanguage.CommonMessagePath(language, fileName);
        if (ResolveWorkflowFile(project, preferred) is not null)
        {
            return preferred;
        }

        if (!string.Equals(language, SwShGameTextLanguage.English, StringComparison.OrdinalIgnoreCase))
        {
            var english = SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.English, fileName);
            if (ResolveWorkflowFile(project, english) is not null)
            {
                return english;
            }
        }

        return preferred;
    }

    internal static SwShMoveEditableField? GetEditableField(string? field)
    {
        return EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static WorkflowFileSource? ResolveMoveDataSource(OpenedProject project, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return ResolveWorkflowFile(project, relativePath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var normalizedRelativePath = targetRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, normalizedRelativePath));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);

        return PathContainment.IsWithinRoot(pathFromOutputRoot)
            ? targetPath
            : null;
    }

    private static SwShMoveEditableField CreateField(
        string field,
        string label,
        string valueKind,
        int? minimumValue,
        int? maximumValue,
        IReadOnlyList<SwShMoveEditableFieldOption>? options = null)
    {
        return new SwShMoveEditableField(
            field,
            label,
            valueKind,
            minimumValue,
            maximumValue,
            options ?? []);
    }

    private static IReadOnlyList<SwShMoveEditableFieldOption> CreateIndexedOptions(
        IReadOnlyList<string> labels)
    {
        return labels
            .Select((label, index) => new SwShMoveEditableFieldOption(index, $"{index:000} {label}"))
            .ToArray();
    }

    private static SwShMovesWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShMoveRecord> moves,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        IReadOnlyList<string>? typeNames = null)
    {
        var editableFields = typeNames is null
            ? EditableFields
            : EditableFields
                .Select(field => field.Field == TypeField
                    ? field with { Options = CreateIndexedOptions(typeNames) }
                    : field)
                .ToArray();

        return new SwShMovesWorkflow(
            summary,
            moves,
            editableFields,
            new SwShMovesWorkflowStats(
                moves.Count,
                moves.Count(move => move.CanUseMove),
                sourceFileCount,
                moves.Sum(move => move.Flags.Count(flag => flag.Enabled))),
            diagnostics);
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static IEnumerable<WorkflowFileSource> ResolveWorkflowFiles(
        OpenedProject project,
        string relativeDirectory)
    {
        var prefix = relativeDirectory.TrimEnd('/') + "/";

        return project.FileGraph.Entries
            .Where(entry => entry.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new
            {
                Entry = entry,
                SourcePath = ResolveSourcePath(project.Paths, entry),
            })
            .Where(source => source.SourcePath is not null && File.Exists(source.SourcePath))
            .Select(source => new WorkflowFileSource(source.Entry, source.SourcePath!));
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShMoveProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShMoveProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Moves,
            "Moves Data",
            "Move stats, target behavior, secondary effects, flags, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: "workflow.moves",
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
