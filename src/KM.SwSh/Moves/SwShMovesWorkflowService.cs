// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;

namespace KM.SwSh.Moves;

public sealed class SwShMovesWorkflowService
{
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

        return CreateSummary(SwShWorkflowAvailability.ReadOnly);
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
            .Where(source => source.GraphEntry.RelativePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(source => source.GraphEntry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (moveSources.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Moves data is not available for this project.",
                expected: $"{MoveDataDirectory}/**/*.bin"));
            return CreateWorkflow(summary, [], sourceFileCount: 0, diagnostics);
        }

        var moveNames = LoadOptionalTextTable(project, EnglishMoveNamePath, "Move names", diagnostics);
        var moveDescriptions = LoadOptionalTextTable(project, EnglishMoveDescriptionPath, "Move descriptions", diagnostics);
        var typeNames = LoadOptionalTextTable(project, EnglishTypeNamePath, "Type names", diagnostics);
        var moves = new List<SwShMoveRecord>();
        var parsedSourceFileCount = 0;

        foreach (var source in moveSources)
        {
            try
            {
                var moveFile = SwShMoveDataFile.Parse(File.ReadAllBytes(source.AbsolutePath));
                parsedSourceFileCount++;
                moves.Add(ToMoveRecord(
                    moveFile.Record,
                    moveNames,
                    moveDescriptions,
                    typeNames.Count > 0 ? typeNames : FallbackTypeNames,
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
            + (typeNames.Count > 0 ? 1 : 0);

        return CreateWorkflow(
            summary,
            moves.OrderBy(move => move.MoveId).ThenBy(move => move.Provenance.SourceFile, StringComparer.OrdinalIgnoreCase).ToArray(),
            sourceFileCount,
            diagnostics);
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

    private static string? GetOptionalIndexedText(int id, IReadOnlyList<string> values)
    {
        return (uint)id < (uint)values.Count && !string.IsNullOrWhiteSpace(values[id])
            ? values[id]
            : null;
    }

    private static IReadOnlyList<string> LoadOptionalTextTable(
        OpenedProject project,
        string relativePath,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = ResolveWorkflowFile(project, relativePath);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{label} are not available; numeric fallback labels will be shown.",
                expected: relativePath));
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

    private static SwShMovesWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShMoveRecord> moves,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShMovesWorkflow(
            summary,
            moves,
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

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
