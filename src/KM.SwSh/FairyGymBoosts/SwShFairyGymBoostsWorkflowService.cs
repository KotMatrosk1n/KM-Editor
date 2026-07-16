// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.HyperTraining;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;

namespace KM.SwSh.FairyGymBoosts;

public sealed class SwShFairyGymBoostsWorkflowService
{
    public const string FairyGymBoostsEditDomain = "workflow.fairyGymBoosts";

    public const string AnnetteSequencePath = "romfs/bin/battle/waza/sequence/bk143.bseq";
    public const string TeresaSequencePath = "romfs/bin/battle/waza/sequence/bk144.bseq";
    public const string TheodoraSequencePath = "romfs/bin/battle/waza/sequence/bk145.bseq";
    public const string OpalNicknameSequencePath = "romfs/bin/battle/waza/sequence/bk171.bseq";
    public const string OpalColorSequencePath = "romfs/bin/battle/waza/sequence/bk173.bseq";
    public const string OpalAgeSequencePath = "romfs/bin/battle/waza/sequence/bk174.bseq";

    public const string ResultNone = "none";
    public const string ResultIncrease = "increase";
    public const string ResultDecrease = "decrease";

    private const string StatAttack = "atk";
    private const string StatDefense = "def";
    private const string StatSpecialAttack = "spAtk";
    private const string StatSpecialDefense = "spDef";
    private const string StatSpeed = "speed";

    private static readonly SwShFairyGymBoostSourceDefinition[] SourceDefinitions =
    [
        new("bk143", "Annette quiz sequence", AnnetteSequencePath),
        new("bk144", "Teresa quiz sequence", TeresaSequencePath),
        new("bk145", "Theodora quiz sequence", TheodoraSequencePath),
        new("bk171", "Opal nickname quiz sequence", OpalNicknameSequencePath),
        new("bk173", "Opal color quiz sequence", OpalColorSequencePath),
        new("bk174", "Opal age quiz sequence", OpalAgeSequencePath),
    ];

    private static readonly SwShFairyGymBoostTrainerDefinition[] TrainerDefinitions =
    [
        new(
            113,
            "Annette",
            0,
            [
                new("annette-weakness-poison", AnnetteSequencePath, 1, "Poison type", "Do you know about Fairy type's weaknesses?", ResultIncrease, 1),
                new("annette-weakness-steel", AnnetteSequencePath, 2, "Steel type", "Do you know about Fairy type's weaknesses?", ResultIncrease, 1),
            ]),
        new(
            114,
            "Teresa",
            1,
            [
                new("teresa-previous-trainer-annetta", TeresaSequencePath, 1, "Annetta", "What was the previous Trainer's name?", ResultDecrease, 5),
                new("teresa-previous-trainer-annette", TeresaSequencePath, 2, "Annette", "What was the previous Trainer's name?", ResultIncrease, 5),
            ]),
        new(
            115,
            "Theodora",
            2,
            [
                new("theodora-breakfast-curry", TheodoraSequencePath, 1, "Curry", "What do I eat for breakfast every morning?", ResultDecrease, 3),
                new("theodora-breakfast-omelets", TheodoraSequencePath, 2, "Omelets", "What do I eat for breakfast every morning?", ResultIncrease, 3),
            ]),
        new(
            108,
            "Opal",
            3,
            [
                new("opal-nickname-magic-user", OpalNicknameSequencePath, 1, "The magic-user", "Do you know my nickname?", ResultDecrease, 6),
                new("opal-nickname-wizard", OpalNicknameSequencePath, 2, "The wizard", "Do you know my nickname?", ResultIncrease, 6),
                new("opal-color-pink", OpalColorSequencePath, 1, "Pink", "What is my favorite color?", ResultDecrease, 4),
                new("opal-color-purple", OpalColorSequencePath, 2, "Purple", "What is my favorite color?", ResultIncrease, 4),
                new("opal-age-sixteen", OpalAgeSequencePath, 1, "16 years old", "How old am I?", ResultIncrease, 2),
                new("opal-age-eighty-eight", OpalAgeSequencePath, 2, "88 years old", "How old am I?", ResultDecrease, 2),
            ]),
    ];

    private static readonly SwShFairyGymBoostDefinition[] BoostDefinitions =
        TrainerDefinitions.SelectMany(trainer => trainer.Boosts).ToArray();

    internal static IReadOnlyList<SwShFairyGymBoostSourceDefinition> Sources => SourceDefinitions;

    internal static IReadOnlyList<SwShFairyGymBoostDefinition> Boosts => BoostDefinitions;

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!IsSupportedGame(project.Paths.SelectedGame))
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts requires Pokemon Sword or Pokemon Shield to be selected before it can load.",
                    expected: "Selected Pokemon Sword or Pokemon Shield project"));
        }

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShFairyGymBoostsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                IsSupportedGame(project.Paths.SelectedGame) ? project.Paths.SelectedGame : null,
                sources: [],
                currentSelections: new Dictionary<string, SwShFairyGymBoostSelection>(StringComparer.Ordinal),
                availableSourcePaths: new HashSet<string>(StringComparer.Ordinal),
                diagnostics);
        }

        var sources = new List<SwShFairyGymBoostsSourceRecord>(SourceDefinitions.Length);
        var currentSelections = new Dictionary<string, SwShFairyGymBoostSelection>(StringComparer.Ordinal);
        var availableSourcePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceDefinition in SourceDefinitions)
        {
            var inspection = InspectSource(project, sourceDefinition, diagnostics);
            sources.Add(inspection.Source);
            if (inspection.Slots is null)
            {
                continue;
            }

            availableSourcePaths.Add(sourceDefinition.RelativePath);
            foreach (var boost in BoostDefinitions.Where(boost => string.Equals(
                boost.SequenceFile,
                sourceDefinition.RelativePath,
                StringComparison.Ordinal)))
            {
                var slot = inspection.Slots[boost.AnswerChoice - 1];
                currentSelections.Add(
                    boost.BoostId,
                    new SwShFairyGymBoostSelection(
                        boost.BoostId,
                        slot.EffectId,
                        ToResultKind(slot.ResultValue)));
            }
        }

        return CreateWorkflow(
            summary,
            project.Paths.SelectedGame,
            sources,
            currentSelections,
            availableSourcePaths,
            diagnostics);
    }

    internal static SwShFairyGymBoostSelection CreateDefaultSelection(
        SwShFairyGymBoostDefinition boost)
    {
        ArgumentNullException.ThrowIfNull(boost);

        return new SwShFairyGymBoostSelection(boost.BoostId, boost.EffectId, boost.ResultKind);
    }

    internal static SwShFairyGymBoostDefinition? FindBoost(string boostId)
    {
        return BoostDefinitions.FirstOrDefault(
            boost => string.Equals(boost.BoostId, boostId, StringComparison.Ordinal));
    }

    internal static SwShFairyGymBoostEffect ResolveEffect(int effectId)
    {
        return effectId switch
        {
            0 => new SwShFairyGymBoostEffect("No effect", 0, []),
            1 => new SwShFairyGymBoostEffect("Attack and Sp. Atk", 1, [StatAttack, StatSpecialAttack]),
            2 => new SwShFairyGymBoostEffect("Attack and Sp. Atk", 2, [StatAttack, StatSpecialAttack]),
            3 => new SwShFairyGymBoostEffect("Defense and Sp. Def", 1, [StatDefense, StatSpecialDefense]),
            4 => new SwShFairyGymBoostEffect("Defense and Sp. Def", 2, [StatDefense, StatSpecialDefense]),
            5 => new SwShFairyGymBoostEffect("Speed", 1, [StatSpeed]),
            6 => new SwShFairyGymBoostEffect("Speed", 2, [StatSpeed]),
            _ => new SwShFairyGymBoostEffect("Unknown", 0, []),
        };
    }

    internal static bool IsSupportedSelection(int effectId, string resultKind)
    {
        return effectId switch
        {
            0 => string.Equals(resultKind, ResultNone, StringComparison.Ordinal),
            >= 1 and <= 6 => string.Equals(resultKind, ResultIncrease, StringComparison.Ordinal)
                || string.Equals(resultKind, ResultDecrease, StringComparison.Ordinal),
            _ => false,
        };
    }

    internal static int ToResultValue(string resultKind)
    {
        return resultKind switch
        {
            ResultNone => 0,
            ResultIncrease => 1,
            ResultDecrease => 2,
            _ => -1,
        };
    }

    internal static string ToResultKind(int resultValue)
    {
        return resultValue switch
        {
            0 => ResultNone,
            1 => ResultIncrease,
            2 => ResultDecrease,
            _ => "unknown",
        };
    }

    internal static SwShHyperTrainingWorkflowService.WorkflowFileSource? ResolveWorkflowFile(
        OpenedProject project,
        string relativePath)
    {
        return SwShTypeChartWorkflowService.ResolveWorkflowFile(project, relativePath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        return SwShTypeChartWorkflowService.ResolveOutputPath(paths, targetRelativePath);
    }

    internal static string? ResolveBaseSourcePath(ProjectPaths paths, string relativePath)
    {
        return SwShTypeChartWorkflowService.ResolveBaseSourcePath(paths, relativePath);
    }

    internal IReadOnlyList<ProjectFileReference> GetPlanSources(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var sources = new List<ProjectFileReference>(SourceDefinitions.Length * 2);
        foreach (var definition in SourceDefinitions)
        {
            sources.Add(new ProjectFileReference(ProjectFileLayer.Base, definition.RelativePath));
            var workflowFile = ResolveWorkflowFile(project, definition.RelativePath);
            if (workflowFile?.Entry.LayeredFile is not null)
            {
                sources.Add(new ProjectFileReference(ProjectFileLayer.Layered, definition.RelativePath));
            }
        }

        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<SwShFairyGymBoostAnswerSlot> GetVanillaSlots(string relativePath)
    {
        var boosts = BoostDefinitions
            .Where(boost => string.Equals(boost.SequenceFile, relativePath, StringComparison.Ordinal))
            .OrderBy(boost => boost.AnswerChoice)
            .ToArray();
        if (boosts.Length != SwShFairyGymBoostsBseqPatcher.OwnedSlotCount
            || boosts.Select(boost => boost.AnswerChoice).Distinct().Count() != boosts.Length
            || boosts[0].AnswerChoice != 1
            || boosts[1].AnswerChoice != 2)
        {
            throw new InvalidOperationException(
                $"Fairy Gym boost mapping for '{relativePath}' must define answer choices 1 and 2 exactly once.");
        }

        return boosts
            .Select(boost => new SwShFairyGymBoostAnswerSlot(
                boost.EffectId,
                ToResultValue(boost.ResultKind)))
            .ToArray();
    }

    internal static bool IsSupportedGame(ProjectGame? game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
    }

    private static SwShFairyGymBoostsWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        ProjectGame? detectedGame,
        IReadOnlyList<SwShFairyGymBoostsSourceRecord> sources,
        IReadOnlyDictionary<string, SwShFairyGymBoostSelection> currentSelections,
        IReadOnlySet<string> availableSourcePaths,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var trainers = TrainerDefinitions
            .Select(trainer => ToTrainer(trainer, currentSelections, availableSourcePaths))
            .ToArray();
        var boostCount = trainers.Sum(trainer => trainer.Boosts.Count);
        return new SwShFairyGymBoostsWorkflow(
            summary,
            detectedGame,
            trainers,
            sources,
            new SwShFairyGymBoostsWorkflowStats(
                trainers.Length,
                boostCount,
                sources.Count(source => source.Status == "available"),
                SourceDefinitions.Length * SwShFairyGymBoostsBseqPatcher.OwnedByteCount),
            diagnostics);
    }

    private static SwShFairyGymBoostTrainer ToTrainer(
        SwShFairyGymBoostTrainerDefinition trainer,
        IReadOnlyDictionary<string, SwShFairyGymBoostSelection> currentSelections,
        IReadOnlySet<string> availableSourcePaths)
    {
        return new SwShFairyGymBoostTrainer(
            trainer.TrainerId,
            trainer.NpcName,
            trainer.DisplayOrder,
            trainer.Boosts
                .Select(boost => ToBoostRecord(
                    boost,
                    currentSelections.TryGetValue(boost.BoostId, out var selection)
                        ? selection
                        : CreateDefaultSelection(boost),
                    availableSourcePaths.Contains(boost.SequenceFile)))
                .ToArray());
    }

    private static SwShFairyGymBoostRecord ToBoostRecord(
        SwShFairyGymBoostDefinition boost,
        SwShFairyGymBoostSelection selection,
        bool isAvailable)
    {
        var effect = ResolveEffect(selection.EffectId);
        return new SwShFairyGymBoostRecord(
            boost.BoostId,
            boost.SequenceFile,
            boost.AnswerChoice,
            boost.AnswerText,
            boost.QuestionText,
            boost.EffectId,
            boost.ResultKind,
            selection.ResultKind,
            selection.EffectId,
            effect.Label,
            effect.StageAmount,
            effect.AffectedStats,
            isAvailable);
    }

    private static FairyGymBoostSourceInspection InspectSource(
        OpenedProject project,
        SwShFairyGymBoostSourceDefinition source,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var workflowFile = ResolveWorkflowFile(project, source.RelativePath);
        if (workflowFile is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"{source.Label} is missing. Fairy Gym Boosts can show the known mapping, but patching needs this BSEQ file.",
                file: source.RelativePath,
                expected: "Sword/Shield Fairy Gym battle sequence file"));

            return new FairyGymBoostSourceInspection(CreateMissingSource(source), Slots: null);
        }

        var basePath = ResolveBaseSourcePath(project.Paths, source.RelativePath);
        if (basePath is null || !File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{source.Label} has no canonical base file. The effective sequence cannot be verified safely.",
                file: source.RelativePath,
                expected: "Readable vanilla Sword/Shield Fairy Gym BSEQ base file"));
            return new FairyGymBoostSourceInspection(
                CreateBlockedSource(source, workflowFile.Entry),
                Slots: null);
        }

        try
        {
            var baseBytes = File.ReadAllBytes(basePath);
            var effectiveBytes = File.ReadAllBytes(workflowFile.AbsolutePath);
            var vanillaSlots = GetVanillaSlots(source.RelativePath);
            SwShFairyGymBoostsBseqPatcher.ValidateVanillaBase(baseBytes, vanillaSlots);
            SwShFairyGymBoostsBseqPatcher.ValidateEffective(effectiveBytes);
            SwShFairyGymBoostsBseqPatcher.EnsureCompatible(baseBytes, effectiveBytes);
            var slots = SwShFairyGymBoostsBseqPatcher.ReadAnswerSlots(effectiveBytes);

            return new FairyGymBoostSourceInspection(
                new SwShFairyGymBoostsSourceRecord(
                    source.SourceId,
                    source.Label,
                    workflowFile.Entry.RelativePath,
                    "available",
                    SwShFairyGymBoostsBseqPatcher.PayloadOffsetHex,
                    SwShFairyGymBoostsBseqPatcher.OwnedRangeHex,
                    CreateProvenance(workflowFile.Entry)),
                slots);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{source.Label} is blocked because its verified Fairy Gym mapping is invalid: {exception.Message}",
                file: source.RelativePath,
                expected: "Length 0x4A10, one SpecialQuizResult payload at 0x1550, canonical base slots, and supported effective owned slots"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{source.Label} is blocked because its base or effective source could not be read: {exception.Message}",
                file: source.RelativePath,
                expected: "Readable vanilla base and compatible effective Fairy Gym BSEQ files"));
        }

        return new FairyGymBoostSourceInspection(
            CreateBlockedSource(source, workflowFile.Entry),
            Slots: null);
    }

    private static SwShFairyGymBoostsSourceRecord CreateMissingSource(
        SwShFairyGymBoostSourceDefinition source)
    {
        return new SwShFairyGymBoostsSourceRecord(
            source.SourceId,
            source.Label,
            source.RelativePath,
            "missing",
            "unknown",
            "unknown",
            new SwShFairyGymBoostsProvenance(
                source.RelativePath,
                ProjectFileLayer.Generated,
                ProjectFileGraphEntryState.BaseOnly));
    }

    private static SwShFairyGymBoostsSourceRecord CreateBlockedSource(
        SwShFairyGymBoostSourceDefinition source,
        ProjectFileGraphEntry entry)
    {
        return new SwShFairyGymBoostsSourceRecord(
            source.SourceId,
            source.Label,
            entry.RelativePath,
            "blocked",
            "unknown",
            "unknown",
            CreateProvenance(entry));
    }

    private static SwShFairyGymBoostsProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShFairyGymBoostsProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.FairyGymBoosts,
            "Fairy Gym Boosts",
            "Advanced editor for the Fairy Gym quiz boost and drop outcomes.",
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
            Domain: FairyGymBoostsEditDomain,
            Expected: expected);
    }

    private sealed record FairyGymBoostSourceInspection(
        SwShFairyGymBoostsSourceRecord Source,
        IReadOnlyList<SwShFairyGymBoostAnswerSlot>? Slots);
}
