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
                sources: [],
                currentSelections: new Dictionary<string, SwShFairyGymBoostSelection>(StringComparer.Ordinal),
                diagnostics);
        }

        var sources = SourceDefinitions
            .Select(source => CreateSource(project, source, diagnostics))
            .ToArray();
        var currentSelections = ReadCurrentSelections(project, diagnostics);

        return CreateWorkflow(summary, sources, currentSelections, diagnostics);
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

    private static SwShFairyGymBoostsWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShFairyGymBoostsSourceRecord> sources,
        IReadOnlyDictionary<string, SwShFairyGymBoostSelection> currentSelections,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var trainers = TrainerDefinitions
            .Select(trainer => ToTrainer(trainer, currentSelections))
            .ToArray();
        var boostCount = trainers.Sum(trainer => trainer.Boosts.Count);
        return new SwShFairyGymBoostsWorkflow(
            summary,
            trainers,
            sources,
            new SwShFairyGymBoostsWorkflowStats(
                trainers.Length,
                boostCount,
                sources.Count(source => source.Status == "available")),
            diagnostics);
    }

    private static SwShFairyGymBoostTrainer ToTrainer(
        SwShFairyGymBoostTrainerDefinition trainer,
        IReadOnlyDictionary<string, SwShFairyGymBoostSelection> currentSelections)
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
                        : CreateDefaultSelection(boost)))
                .ToArray());
    }

    private static SwShFairyGymBoostRecord ToBoostRecord(
        SwShFairyGymBoostDefinition boost,
        SwShFairyGymBoostSelection selection)
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
            effect.AffectedStats);
    }

    private static IReadOnlyDictionary<string, SwShFairyGymBoostSelection> ReadCurrentSelections(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var selections = new Dictionary<string, SwShFairyGymBoostSelection>(StringComparer.Ordinal);

        foreach (var sourceDefinition in SourceDefinitions)
        {
            var source = ResolveWorkflowFile(project, sourceDefinition.RelativePath);
            if (source is null)
            {
                continue;
            }

            IReadOnlyList<SwShFairyGymBoostAnswerSlot> slots;
            try
            {
                slots = SwShFairyGymBoostsBseqPatcher.ReadAnswerSlots(File.ReadAllBytes(source.AbsolutePath));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"{sourceDefinition.Label} could not be inspected: {exception.Message}. Known vanilla values will be shown for that sequence.",
                    file: sourceDefinition.RelativePath,
                    expected: "Fairy Gym quiz BSEQ command payload"));
                continue;
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"{sourceDefinition.Label} could not be read: {exception.Message}. Known vanilla values will be shown for that sequence.",
                    file: sourceDefinition.RelativePath,
                    expected: "Readable Fairy Gym quiz BSEQ file"));
                continue;
            }

            foreach (var boost in BoostDefinitions.Where(boost => string.Equals(boost.SequenceFile, sourceDefinition.RelativePath, StringComparison.Ordinal)))
            {
                var slotIndex = boost.AnswerChoice - 1;
                if (slotIndex < 0 || slotIndex >= slots.Count)
                {
                    continue;
                }

                var slot = slots[slotIndex];
                var resultKind = ToResultKind(slot.ResultValue);
                if (!IsSupportedSelection(slot.EffectId, resultKind))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"{sourceDefinition.Label} has an unsupported boost payload for {boost.AnswerText}. Known vanilla values will be shown for that answer.",
                        file: sourceDefinition.RelativePath,
                        expected: "Effect 0 with no effect, or effect 1-6 with boost/drop"));
                    continue;
                }

                selections[boost.BoostId] = new SwShFairyGymBoostSelection(
                    boost.BoostId,
                    slot.EffectId,
                    resultKind);
            }
        }

        return selections;
    }

    private static SwShFairyGymBoostsSourceRecord CreateSource(
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

            return new SwShFairyGymBoostsSourceRecord(
                source.SourceId,
                source.Label,
                source.RelativePath,
                "missing",
                new SwShFairyGymBoostsProvenance(
                    source.RelativePath,
                    ProjectFileLayer.Generated,
                    ProjectFileGraphEntryState.BaseOnly));
        }

        return new SwShFairyGymBoostsSourceRecord(
            source.SourceId,
            source.Label,
            workflowFile.Entry.RelativePath,
            "available",
            CreateProvenance(workflowFile.Entry));
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
}
