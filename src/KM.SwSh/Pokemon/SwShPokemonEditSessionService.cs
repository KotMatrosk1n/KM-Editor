// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Pokemon;

public sealed class SwShPokemonEditSessionService
{
    private const string PokemonEditDomain = "workflow.pokemon";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShPokemonWorkflowService pokemonWorkflowService;

    public SwShPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShPokemonWorkflowService? pokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.pokemonWorkflowService = pokemonWorkflowService ?? new SwShPokemonWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShPokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int personalId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = pokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditPokemon(project, workflow, diagnostics))
        {
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var selectedPokemon = workflow.Pokemon.FirstOrDefault(pokemon => pokemon.PersonalId == personalId);
        if (selectedPokemon is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal record {personalId} is not present in the loaded Pokemon Data workflow.",
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(selectedPokemon, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingPokemonEdit(currentSession, pendingEdit);

        return new SwShPokemonEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = pokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditPokemon(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Pokemon Data change is valid."));
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Pokemon Data edit before reviewing a change plan.",
                expected: "Pending Pokemon Data edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = pokemonWorkflowService.Load(project);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var sources = session.PendingEdits
            .Select(edit => GetSourceReference(workflow, edit))
            .Where(source => source is not null)
            .Select(source => source!)
            .Distinct()
            .ToArray();
        var reason = session.PendingEdits.Count == 1
            ? $"Apply pending Pokemon Data edit: {session.PendingEdits[0].Summary}"
            : $"Apply {session.PendingEdits.Count} pending Pokemon Data edits.";
        var writes = new[]
        {
            new PlannedFileWrite(
                SwShPokemonWorkflowService.PersonalDataPath,
                sources,
                File.Exists(targetPath),
                reason),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Change plan preview contains 1 target file."));

        return new ChangePlan(session.Id, writes, diagnostics);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Pokemon Data change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var source = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon personal data source could not be resolved for apply.",
                file: SwShPokemonWorkflowService.PersonalDataPath,
                expected: "Loaded Sword/Shield personal_total.bin"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var table = SwShPersonalTable.Parse(sourceBytes);
            var records = table.Records.ToArray();

            foreach (var edit in session.PendingEdits)
            {
                if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId)
                    || (uint)personalId >= (uint)records.Length)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending Pokemon Data edit targets a record that is not loaded.",
                        field: "personalId",
                        expected: "Existing Pokemon personal record"));
                    continue;
                }

                if (TryParseEditableValue(edit.Field, edit.NewValue, diagnostics) is not { } value)
                {
                    continue;
                }

                records[personalId] = ApplyPersonalDataField(records[personalId], edit.Field!, value);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var outputBytes = SwShPersonalTable.Write(records, sourceBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, outputBytes);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShPokemonWorkflowService.PersonalDataPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Pokemon Data change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon personal data source could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield personal_total.bin"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data output file could not be read or written: {exception.Message}",
                file: SwShPokemonWorkflowService.PersonalDataPath,
                expected: "Readable source and writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pokemon Data output file could not be read or written: {exception.Message}",
                file: SwShPokemonWorkflowService.PersonalDataPath,
                expected: "Readable source and writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool CanEditPokemon(
        OpenedProject project,
        SwShPokemonWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon Data edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static void ValidatePendingEdit(
        SwShPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Pokemon Data workflow.",
                expected: PokemonEditDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId)
            || workflow.Pokemon.All(pokemon => pokemon.PersonalId != personalId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Pokemon Data edit targets a record that is not loaded.",
                field: "personalId",
                expected: "Existing Pokemon personal record"));
            return;
        }

        TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShPokemonRecord selectedPokemon,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var parsedValue = TryParseEditableValue(normalizedField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return new PendingEdit(
            PokemonEditDomain,
            CreatePendingEditSummary(selectedPokemon, normalizedField, parsedValue.Value),
            [new ProjectFileReference(selectedPokemon.Provenance.SourceLayer, selectedPokemon.Provenance.SourceFile)],
            RecordId: selectedPokemon.PersonalId.ToString(CultureInfo.InvariantCulture),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static int? TryParseEditableValue(
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editableField = SwShPokemonWorkflowService.GetEditableField(field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(field ?? "(missing)"));
            return null;
        }

        var parsedValue = editableField.ValueKind == "boolean"
            ? TryParseBooleanValue(value, out var booleanValue) ? booleanValue : (int?)null
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue)
                ? integerValue
                : (int?)null;

        if (parsedValue is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be a valid {editableField.ValueKind} value.",
                field: editableField.Field,
                expected: $"Safe Pokemon {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (parsedValue.Value < (editableField.MinimumValue ?? int.MinValue)
            || parsedValue.Value > (editableField.MaximumValue ?? int.MaxValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: $"Safe Pokemon {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        return parsedValue.Value;
    }

    private static bool TryParseBooleanValue(string? value, out int parsedValue)
    {
        parsedValue = 0;
        if (string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = 1;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.Ordinal)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = 0;
            return true;
        }

        return false;
    }

    private static EditSession ReplacePendingPokemonEdit(EditSession session, PendingEdit pendingEdit)
    {
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => !(
                    string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)
                    && string.Equals(edit.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
                    && string.Equals(edit.Field, pendingEdit.Field, StringComparison.Ordinal)))
                .Append(pendingEdit)
                .ToArray(),
        };
    }

    public static SwShPokemonWorkflow OverlayPendingEdits(
        SwShPokemonWorkflow workflow,
        IReadOnlyList<PendingEdit> edits)
    {
        if (edits.Count == 0)
        {
            return workflow;
        }

        var overlaid = workflow.Pokemon.ToDictionary(pokemon => pokemon.PersonalId);
        foreach (var edit in edits.Where(edit => string.Equals(edit.Domain, PokemonEditDomain, StringComparison.Ordinal)))
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId)
                || !overlaid.TryGetValue(personalId, out var pokemon)
                || !int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            overlaid[personalId] = ApplyPokemonViewField(pokemon, edit.Field!, value);
        }

        return workflow with
        {
            Pokemon = workflow.Pokemon
                .Select(pokemon => overlaid.TryGetValue(pokemon.PersonalId, out var updated) ? updated : pokemon)
                .ToArray(),
        };
    }

    private static SwShPokemonRecord ApplyPokemonViewField(SwShPokemonRecord pokemon, string field, int value)
    {
        return field switch
        {
            SwShPokemonWorkflowService.HPField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, hp: value) },
            SwShPokemonWorkflowService.AttackField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, attack: value) },
            SwShPokemonWorkflowService.DefenseField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, defense: value) },
            SwShPokemonWorkflowService.SpecialAttackField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, specialAttack: value) },
            SwShPokemonWorkflowService.SpecialDefenseField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, specialDefense: value) },
            SwShPokemonWorkflowService.SpeedField => pokemon with { BaseStats = UpdateStats(pokemon.BaseStats, speed: value) },
            SwShPokemonWorkflowService.Type1Field => pokemon with { Type1 = FormatType(value), Personal = pokemon.Personal with { Type1 = value } },
            SwShPokemonWorkflowService.Type2Field => pokemon with { Type2 = FormatType(value), Personal = pokemon.Personal with { Type2 = value } },
            SwShPokemonWorkflowService.CatchRateField => pokemon with { CatchRate = value, Personal = pokemon.Personal with { CatchRate = value } },
            SwShPokemonWorkflowService.EvolutionStageField => pokemon with { EvolutionStage = value, Personal = pokemon.Personal with { EvolutionStage = value } },
            SwShPokemonWorkflowService.EVYieldHPField => pokemon with { Personal = pokemon.Personal with { EVYieldHP = value } },
            SwShPokemonWorkflowService.EVYieldAttackField => pokemon with { Personal = pokemon.Personal with { EVYieldAttack = value } },
            SwShPokemonWorkflowService.EVYieldDefenseField => pokemon with { Personal = pokemon.Personal with { EVYieldDefense = value } },
            SwShPokemonWorkflowService.EVYieldSpecialAttackField => pokemon with { Personal = pokemon.Personal with { EVYieldSpecialAttack = value } },
            SwShPokemonWorkflowService.EVYieldSpecialDefenseField => pokemon with { Personal = pokemon.Personal with { EVYieldSpecialDefense = value } },
            SwShPokemonWorkflowService.EVYieldSpeedField => pokemon with { Personal = pokemon.Personal with { EVYieldSpeed = value } },
            SwShPokemonWorkflowService.HeldItem1Field => pokemon with { Personal = pokemon.Personal with { HeldItem1 = value } },
            SwShPokemonWorkflowService.HeldItem2Field => pokemon with { Personal = pokemon.Personal with { HeldItem2 = value } },
            SwShPokemonWorkflowService.HeldItem3Field => pokemon with { Personal = pokemon.Personal with { HeldItem3 = value } },
            SwShPokemonWorkflowService.GenderRatioField => pokemon with { GenderRatio = value, Personal = pokemon.Personal with { GenderRatio = value } },
            SwShPokemonWorkflowService.HatchCyclesField => pokemon with { Personal = pokemon.Personal with { HatchCycles = value } },
            SwShPokemonWorkflowService.BaseFriendshipField => pokemon with { Personal = pokemon.Personal with { BaseFriendship = value } },
            SwShPokemonWorkflowService.ExpGrowthField => pokemon with { Personal = pokemon.Personal with { ExpGrowth = value } },
            SwShPokemonWorkflowService.EggGroup1Field => pokemon with { Personal = pokemon.Personal with { EggGroup1 = value } },
            SwShPokemonWorkflowService.EggGroup2Field => pokemon with { Personal = pokemon.Personal with { EggGroup2 = value } },
            SwShPokemonWorkflowService.Ability1Field => pokemon with { Abilities = pokemon.Abilities with { Ability1 = value } },
            SwShPokemonWorkflowService.Ability2Field => pokemon with { Abilities = pokemon.Abilities with { Ability2 = value } },
            SwShPokemonWorkflowService.HiddenAbilityField => pokemon with { Abilities = pokemon.Abilities with { HiddenAbility = value } },
            SwShPokemonWorkflowService.FormStatsIndexField => pokemon with { Personal = pokemon.Personal with { FormStatsIndex = value } },
            SwShPokemonWorkflowService.FormCountField => pokemon with { Personal = pokemon.Personal with { FormCount = value } },
            SwShPokemonWorkflowService.ColorField => pokemon with { Personal = pokemon.Personal with { Color = value } },
            SwShPokemonWorkflowService.IsPresentInGameField => pokemon with
            {
                DexPresence = pokemon.DexPresence with { IsPresentInGame = value != 0 },
                Personal = pokemon.Personal with { IsPresentInGame = value != 0 },
            },
            SwShPokemonWorkflowService.HasSpriteFormField => pokemon with { Personal = pokemon.Personal with { HasSpriteForm = value != 0 } },
            SwShPokemonWorkflowService.BaseExperienceField => pokemon with { BaseExperience = value, Personal = pokemon.Personal with { BaseExperience = value } },
            SwShPokemonWorkflowService.HeightField => pokemon with { Height = value, Personal = pokemon.Personal with { Height = value } },
            SwShPokemonWorkflowService.WeightField => pokemon with { Weight = value, Personal = pokemon.Personal with { Weight = value } },
            SwShPokemonWorkflowService.ModelIdField => pokemon with { Personal = pokemon.Personal with { ModelId = checked((uint)value) } },
            SwShPokemonWorkflowService.HatchedSpeciesField => pokemon with { Personal = pokemon.Personal with { HatchedSpecies = value } },
            SwShPokemonWorkflowService.LocalFormIndexField => pokemon with { Personal = pokemon.Personal with { LocalFormIndex = value } },
            SwShPokemonWorkflowService.IsRegionalFormField => pokemon with { Personal = pokemon.Personal with { IsRegionalForm = value != 0 } },
            SwShPokemonWorkflowService.CanNotDynamaxField => pokemon with { Personal = pokemon.Personal with { CanNotDynamax = value != 0 } },
            SwShPokemonWorkflowService.RegionalDexIndexField => pokemon with
            {
                DexPresence = pokemon.DexPresence with { RegionalDexIndex = value, IsInAnyDex = value != 0 || pokemon.DexPresence.ArmorDexIndex != 0 || pokemon.DexPresence.CrownDexIndex != 0 },
                Personal = pokemon.Personal with { RegionalDexIndex = value },
            },
            SwShPokemonWorkflowService.FormField => pokemon with { Form = value, Personal = pokemon.Personal with { Form = value } },
            SwShPokemonWorkflowService.ArmorDexIndexField => pokemon with
            {
                DexPresence = pokemon.DexPresence with { ArmorDexIndex = value, IsInAnyDex = pokemon.DexPresence.RegionalDexIndex != 0 || value != 0 || pokemon.DexPresence.CrownDexIndex != 0 },
                Personal = pokemon.Personal with { ArmorDexIndex = value },
            },
            SwShPokemonWorkflowService.CrownDexIndexField => pokemon with
            {
                DexPresence = pokemon.DexPresence with { CrownDexIndex = value, IsInAnyDex = pokemon.DexPresence.RegionalDexIndex != 0 || pokemon.DexPresence.ArmorDexIndex != 0 || value != 0 },
                Personal = pokemon.Personal with { CrownDexIndex = value },
            },
            _ => pokemon,
        };
    }

    private static SwShPokemonBaseStats UpdateStats(
        SwShPokemonBaseStats stats,
        int? hp = null,
        int? attack = null,
        int? defense = null,
        int? specialAttack = null,
        int? specialDefense = null,
        int? speed = null)
    {
        var updated = stats with
        {
            HP = hp ?? stats.HP,
            Attack = attack ?? stats.Attack,
            Defense = defense ?? stats.Defense,
            SpecialAttack = specialAttack ?? stats.SpecialAttack,
            SpecialDefense = specialDefense ?? stats.SpecialDefense,
            Speed = speed ?? stats.Speed,
        };

        return updated with
        {
            Total = updated.HP + updated.Attack + updated.Defense + updated.SpecialAttack + updated.SpecialDefense + updated.Speed,
        };
    }

    private static SwShPersonalRecord ApplyPersonalDataField(SwShPersonalRecord record, string field, int value)
    {
        return field switch
        {
            SwShPokemonWorkflowService.HPField => record with { HP = value },
            SwShPokemonWorkflowService.AttackField => record with { Attack = value },
            SwShPokemonWorkflowService.DefenseField => record with { Defense = value },
            SwShPokemonWorkflowService.SpecialAttackField => record with { SpecialAttack = value },
            SwShPokemonWorkflowService.SpecialDefenseField => record with { SpecialDefense = value },
            SwShPokemonWorkflowService.SpeedField => record with { Speed = value },
            SwShPokemonWorkflowService.Type1Field => record with { Type1 = value },
            SwShPokemonWorkflowService.Type2Field => record with { Type2 = value },
            SwShPokemonWorkflowService.CatchRateField => record with { CatchRate = value },
            SwShPokemonWorkflowService.EvolutionStageField => record with { EvolutionStage = value },
            SwShPokemonWorkflowService.EVYieldHPField => record with { EVYieldHP = value },
            SwShPokemonWorkflowService.EVYieldAttackField => record with { EVYieldAttack = value },
            SwShPokemonWorkflowService.EVYieldDefenseField => record with { EVYieldDefense = value },
            SwShPokemonWorkflowService.EVYieldSpecialAttackField => record with { EVYieldSpecialAttack = value },
            SwShPokemonWorkflowService.EVYieldSpecialDefenseField => record with { EVYieldSpecialDefense = value },
            SwShPokemonWorkflowService.EVYieldSpeedField => record with { EVYieldSpeed = value },
            SwShPokemonWorkflowService.HeldItem1Field => record with { HeldItem1 = value },
            SwShPokemonWorkflowService.HeldItem2Field => record with { HeldItem2 = value },
            SwShPokemonWorkflowService.HeldItem3Field => record with { HeldItem3 = value },
            SwShPokemonWorkflowService.GenderRatioField => record with { GenderRatio = value },
            SwShPokemonWorkflowService.HatchCyclesField => record with { HatchCycles = value },
            SwShPokemonWorkflowService.BaseFriendshipField => record with { BaseFriendship = value },
            SwShPokemonWorkflowService.ExpGrowthField => record with { ExpGrowth = value },
            SwShPokemonWorkflowService.EggGroup1Field => record with { EggGroup1 = value },
            SwShPokemonWorkflowService.EggGroup2Field => record with { EggGroup2 = value },
            SwShPokemonWorkflowService.Ability1Field => record with { Ability1 = value },
            SwShPokemonWorkflowService.Ability2Field => record with { Ability2 = value },
            SwShPokemonWorkflowService.HiddenAbilityField => record with { HiddenAbility = value },
            SwShPokemonWorkflowService.FormStatsIndexField => record with { FormStatsIndex = value },
            SwShPokemonWorkflowService.FormCountField => record with { FormCount = value },
            SwShPokemonWorkflowService.ColorField => record with { Color = value },
            SwShPokemonWorkflowService.IsPresentInGameField => record with { IsPresentInGame = value != 0 },
            SwShPokemonWorkflowService.HasSpriteFormField => record with { HasSpriteForm = value != 0 },
            SwShPokemonWorkflowService.BaseExperienceField => record with { BaseExperience = value },
            SwShPokemonWorkflowService.HeightField => record with { Height = value },
            SwShPokemonWorkflowService.WeightField => record with { Weight = value },
            SwShPokemonWorkflowService.ModelIdField => record with { ModelId = checked((uint)value) },
            SwShPokemonWorkflowService.HatchedSpeciesField => record with { HatchedSpecies = value },
            SwShPokemonWorkflowService.LocalFormIndexField => record with { LocalFormIndex = value },
            SwShPokemonWorkflowService.IsRegionalFormField => record with { IsRegionalForm = value != 0 },
            SwShPokemonWorkflowService.CanNotDynamaxField => record with { CanNotDynamax = value != 0 },
            SwShPokemonWorkflowService.RegionalDexIndexField => record with { RegionalDexIndex = value },
            SwShPokemonWorkflowService.FormField => record with { Form = value },
            SwShPokemonWorkflowService.ArmorDexIndexField => record with { ArmorDexIndex = value },
            SwShPokemonWorkflowService.CrownDexIndexField => record with { CrownDexIndex = value },
            _ => record,
        };
    }

    private static ProjectFileReference? GetSourceReference(SwShPokemonWorkflow workflow, PendingEdit edit)
    {
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var personalId))
        {
            return null;
        }

        var pokemon = workflow.Pokemon.FirstOrDefault(candidate => candidate.PersonalId == personalId);
        return pokemon is null
            ? null
            : new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile);
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon Data apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShPokemonWorkflowService.ResolveOutputPath(paths, SwShPokemonWorkflowService.PersonalDataPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pokemon Data apply target must stay inside the configured output root.",
                file: SwShPokemonWorkflowService.PersonalDataPath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        if (!reviewedPlan.CanApply
            || reviewedPlan.SessionId != currentPlan.SessionId
            || reviewedPlan.Writes.Count != currentPlan.Writes.Count)
        {
            return false;
        }

        var reviewedTargets = reviewedPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currentTargets = currentPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return reviewedTargets.SequenceEqual(currentTargets, StringComparer.Ordinal);
    }

    private static ApplyResult CreateApplyResult(
        string applyId,
        DateTimeOffset appliedAt,
        ChangePlan currentPlan,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, currentPlan.Writes),
            diagnostics);
    }

    private static string CreatePendingEditSummary(SwShPokemonRecord pokemon, string field, int value)
    {
        var editableField = SwShPokemonWorkflowService.GetEditableField(field);
        var label = editableField?.Label ?? field;
        var displayValue = editableField?.ValueKind == "boolean"
            ? value == 0 ? "disabled" : "enabled"
            : value.ToString(CultureInfo.InvariantCulture);

        return $"Set {pokemon.Name} {label.ToLowerInvariant()} to {displayValue}.";
    }

    private static string FormatType(int value)
    {
        return value switch
        {
            0 => "Normal",
            1 => "Fighting",
            2 => "Flying",
            3 => "Poison",
            4 => "Ground",
            5 => "Rock",
            6 => "Bug",
            7 => "Ghost",
            8 => "Steel",
            9 => "Fire",
            10 => "Water",
            11 => "Grass",
            12 => "Electric",
            13 => "Psychic",
            14 => "Ice",
            15 => "Dragon",
            16 => "Dark",
            17 => "Fairy",
            _ => $"Type {value}",
        };
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Pokemon field '{field}' is not supported by the Pokemon Data workflow yet.",
            field: "field",
            expected: "Supported Pokemon personal data field");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: PokemonEditDomain,
            Field: field,
            Expected: expected);
    }
}
