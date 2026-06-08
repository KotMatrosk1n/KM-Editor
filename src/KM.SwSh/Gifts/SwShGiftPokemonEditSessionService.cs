// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Gifts;

public sealed class SwShGiftPokemonEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShGiftPokemonWorkflowService giftPokemonWorkflowService;

    public SwShGiftPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShGiftPokemonWorkflowService? giftPokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.giftPokemonWorkflowService = giftPokemonWorkflowService ?? new SwShGiftPokemonWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShGiftPokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int giftIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var workflow = giftPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditGiftPokemon(project, workflow, diagnostics))
        {
            return new SwShGiftPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var gift = workflow.Gifts.FirstOrDefault(candidate => candidate.GiftIndex == giftIndex);
        if (gift is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon index {giftIndex} is not present in the loaded workflow.",
                field: "giftIndex",
                expected: "Existing gift Pokemon record"));
            return new SwShGiftPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(gift, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShGiftPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = ReplacePendingGiftEdit(currentSession, pendingEdit);

        return new SwShGiftPokemonEditResult(
            OverlayPendingEdits(workflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = giftPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditGiftPokemon(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Gift Pokemon change is valid."));
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
                "Create a pending Gift Pokemon edit before reviewing a change plan.",
                expected: "Pending gift Pokemon edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var giftSource = SwShGiftPokemonWorkflowService.ResolveGiftPokemonDataSource(project);
        if (giftSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon change plan could not resolve the source table.",
                expected: SwShGiftPokemonWorkflowService.GiftPokemonDataPath));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var targetPath = SwShGiftPokemonWorkflowService.ResolveOutputPath(paths, giftSource.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon apply target must stay inside the configured output root.",
                file: giftSource.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, [], diagnostics);
        }

        var write = new PlannedFileWrite(
            giftSource.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(giftSource.GraphEntry), giftSource.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Gift Pokemon edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Gift Pokemon edits.");

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Change plan preview contains 1 target file."));

        return new ChangePlan(session.Id, [write], diagnostics);
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
                expected: "Current reviewed Gift Pokemon change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var giftSource = SwShGiftPokemonWorkflowService.ResolveGiftPokemonDataSource(project);
        if (giftSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon apply could not resolve the source table.",
                expected: SwShGiftPokemonWorkflowService.GiftPokemonDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, giftSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var archive = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(giftSource.AbsolutePath));
            var edits = session.PendingEdits
                .Select(edit => ToGiftEdit(edit, diagnostics))
                .Where(edit => edit is not null)
                .Select(edit => edit!)
                .ToArray();

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var output = archive.WriteEdits(edits);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, output);
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, giftSource.GraphEntry.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Gift Pokemon change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon source file could not be decoded: {exception.Message}",
                file: giftSource.GraphEntry.RelativePath,
                expected: "Sword/Shield gift Pokemon table"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon output file could not be written: {exception.Message}",
                file: giftSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon output file could not be written: {exception.Message}",
                file: giftSource.GraphEntry.RelativePath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShGiftPokemonEntry gift,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = SwShGiftPokemonWorkflowService.GetEditableField(normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = TryParseFieldValue(editableField, value, diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        AddLinkedPlacementWarning(normalizedField, diagnostics);

        return new PendingEdit(
            SwShGiftPokemonWorkflowService.GiftPokemonEditDomain,
            $"Set {gift.Label} {editableField.Label} to {parsedValue.Value}.",
            [new ProjectFileReference(gift.Provenance.SourceLayer, gift.Provenance.SourceFile)],
            RecordId: SwShGiftPokemonWorkflowService.CreateGiftRecordId(gift.GiftIndex),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SwShGiftPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SwShGiftPokemonWorkflowService.GiftPokemonEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Gift Pokemon workflow.",
                expected: SwShGiftPokemonWorkflowService.GiftPokemonEditDomain));
            return;
        }

        var editableField = SwShGiftPokemonWorkflowService.GetEditableField(edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!SwShGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Gift Pokemon edit targets an invalid record.",
                field: "giftIndex",
                expected: "Gift Pokemon record"));
            return;
        }

        if (workflow.Gifts.All(gift => gift.GiftIndex != giftIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Gift Pokemon edit targets a record that is not loaded.",
                field: "giftIndex",
                expected: "Existing gift Pokemon record"));
            return;
        }

        TryParseFieldValue(editableField, edit.NewValue, diagnostics);
        AddLinkedPlacementWarning(edit.Field, diagnostics);
    }

    private static int? TryParseFieldValue(
        SwShGiftPokemonEditableField editableField,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be an integer value.",
                field: editableField.Field,
                expected: "Integer value"));
            return null;
        }

        if (editableField.Field == SwShGiftPokemonWorkflowService.FlawlessIvCountField
            && parsedValue is not 0 and not 3 and not 6)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon IV preset must be 0, 3, or 6.",
                field: editableField.Field,
                expected: "Supported IV preset"));
            return null;
        }

        if (editableField.Field == SwShGiftPokemonWorkflowService.IvHpField
            && !IsValidHpIvValue(parsedValue))
        {
            diagnostics.Add(CreateIvDiagnostic(editableField.Field));
            return null;
        }

        if (IsNonHpIvField(editableField.Field) && !IsValidIvValue(parsedValue))
        {
            diagnostics.Add(CreateIvDiagnostic(editableField.Field));
            return null;
        }

        if ((editableField.MinimumValue is not null && parsedValue < editableField.MinimumValue.Value)
            || (editableField.MaximumValue is not null && parsedValue > editableField.MaximumValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: "Supported Gift Pokemon field value"));
            return null;
        }

        return parsedValue;
    }

    private static bool IsValidHpIvValue(int value)
    {
        return value == SwShGiftPokemonArchive.ThreePerfectIvSentinel || IsValidIvValue(value);
    }

    private static bool IsValidIvValue(int value)
    {
        return value == SwShGiftPokemonArchive.RandomIvValue
            || value is >= SwShGiftPokemonArchive.MinimumFixedIvValue and <= SwShGiftPokemonArchive.MaximumFixedIvValue;
    }

    private static bool IsNonHpIvField(string field)
    {
        return field is
            SwShGiftPokemonWorkflowService.IvAttackField
            or SwShGiftPokemonWorkflowService.IvDefenseField
            or SwShGiftPokemonWorkflowService.IvSpeedField
            or SwShGiftPokemonWorkflowService.IvSpecialAttackField
            or SwShGiftPokemonWorkflowService.IvSpecialDefenseField;
    }

    private static void AddLinkedPlacementWarning(
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field is SwShGiftPokemonWorkflowService.SpeciesField or SwShGiftPokemonWorkflowService.FormField)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Species and form edits update the gift table only; some visible overworld placements may need a separate placement review.",
                field: field,
                expected: "Review linked placement assets when changing visible gift Pokemon"));
        }
    }

    private static bool CanEditGiftPokemon(
        OpenedProject project,
        SwShGiftPokemonWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon edit sessions require valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static EditSession ReplacePendingGiftEdit(EditSession session, PendingEdit pendingEdit)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameGiftEdit(edit, pendingEdit))
            .Append(pendingEdit)
            .ToArray();

        return session with { PendingEdits = pendingEdits };
    }

    private static bool IsSameGiftEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShGiftPokemonWorkflow OverlayPendingEdits(
        SwShGiftPokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShGiftPokemonWorkflow OverlayPendingEdit(
        SwShGiftPokemonWorkflow workflow,
        PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SwShGiftPokemonWorkflowService.GiftPokemonEditDomain, StringComparison.Ordinal)
            || !SwShGiftPokemonWorkflowService.IsEditableField(edit.Field)
            || !SwShGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Gifts = workflow.Gifts
                .Select(gift => gift.GiftIndex == giftIndex
                    ? OverlayGiftField(workflow, gift, edit.Field!, value)
                    : gift)
                .ToArray(),
        };
    }

    private static SwShGiftPokemonEntry OverlayGiftField(
        SwShGiftPokemonWorkflow workflow,
        SwShGiftPokemonEntry gift,
        string field,
        int value)
    {
        var updatedGift = field switch
        {
            SwShGiftPokemonWorkflowService.SpeciesField => gift with
            {
                SpeciesId = value,
                Species = GetOptionLabel(workflow, field, value, "Species"),
            },
            SwShGiftPokemonWorkflowService.FormField => gift with { Form = value },
            SwShGiftPokemonWorkflowService.LevelField => gift with { Level = value },
            SwShGiftPokemonWorkflowService.HeldItemIdField => gift with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : GetOptionLabel(workflow, field, value, "Item"),
            },
            SwShGiftPokemonWorkflowService.BallItemIdField => gift with
            {
                BallItemId = value,
                BallItem = GetOptionLabel(workflow, field, value, "Item"),
            },
            SwShGiftPokemonWorkflowService.AbilityField => gift with
            {
                Ability = value,
                AbilityLabel = GetOptionLabel(workflow, field, value, "Ability slot"),
            },
            SwShGiftPokemonWorkflowService.NatureField => gift with
            {
                Nature = value,
                NatureLabel = GetOptionLabel(workflow, field, value, "Nature"),
            },
            SwShGiftPokemonWorkflowService.GenderField => gift with
            {
                Gender = value,
                GenderLabel = GetOptionLabel(workflow, field, value, "Gender"),
            },
            SwShGiftPokemonWorkflowService.ShinyLockField => gift with
            {
                ShinyLock = value,
                ShinyLockLabel = GetOptionLabel(workflow, field, value, "Shiny lock"),
            },
            SwShGiftPokemonWorkflowService.DynamaxLevelField => gift with { DynamaxLevel = value },
            SwShGiftPokemonWorkflowService.CanGigantamaxField => gift with { CanGigantamax = value != 0 },
            SwShGiftPokemonWorkflowService.SpecialMoveIdField => gift with
            {
                SpecialMoveId = value,
                SpecialMove = value == 0 ? null : GetOptionLabel(workflow, field, value, "Move"),
            },
            SwShGiftPokemonWorkflowService.IvHpField => gift with { Ivs = gift.Ivs with { HP = value } },
            SwShGiftPokemonWorkflowService.IvAttackField => gift with { Ivs = gift.Ivs with { Attack = value } },
            SwShGiftPokemonWorkflowService.IvDefenseField => gift with { Ivs = gift.Ivs with { Defense = value } },
            SwShGiftPokemonWorkflowService.IvSpeedField => gift with { Ivs = gift.Ivs with { Speed = value } },
            SwShGiftPokemonWorkflowService.IvSpecialAttackField => gift with { Ivs = gift.Ivs with { SpecialAttack = value } },
            SwShGiftPokemonWorkflowService.IvSpecialDefenseField => gift with { Ivs = gift.Ivs with { SpecialDefense = value } },
            SwShGiftPokemonWorkflowService.FlawlessIvCountField => gift with { Ivs = CreateIvPreset(value) },
            _ => gift,
        };

        var flawlessIvCount = GetFlawlessIvCount(updatedGift.Ivs);
        updatedGift = updatedGift with
        {
            FlawlessIvCount = flawlessIvCount,
            IvSummary = SwShGiftPokemonWorkflowService.FormatIvSummary(updatedGift.Ivs, flawlessIvCount),
            Label = $"Gift {(updatedGift.GiftIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {updatedGift.Species}{(updatedGift.IsEgg ? " Egg" : string.Empty)} Lv. {updatedGift.Level} Form {updatedGift.Form}",
        };

        return updatedGift;
    }

    private static SwShGiftPokemonIvsRecord CreateIvPreset(int flawlessIvCount)
    {
        return flawlessIvCount switch
        {
            0 => new SwShGiftPokemonIvsRecord(-1, -1, -1, -1, -1, -1),
            3 => new SwShGiftPokemonIvsRecord(-4, -1, -1, -1, -1, -1),
            6 => new SwShGiftPokemonIvsRecord(31, 31, 31, 31, 31, 31),
            _ => throw new ArgumentOutOfRangeException(nameof(flawlessIvCount)),
        };
    }

    private static int? GetFlawlessIvCount(SwShGiftPokemonIvsRecord ivs)
    {
        return SwShGiftPokemonArchive.GetFlawlessIvCount(
            new SwShGiftPokemonIvs(
                ivs.HP,
                ivs.Attack,
                ivs.Defense,
                ivs.Speed,
                ivs.SpecialAttack,
                ivs.SpecialDefense));
    }

    private static string GetOptionLabel(
        SwShGiftPokemonWorkflow workflow,
        string field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal))?.Options ?? [];

        return SwShGiftPokemonWorkflowService.GetOptionLabel(options, value, fallbackPrefix);
    }

    private static SwShGiftPokemonEdit? ToGiftEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || MapField(edit.Field) is not { } field)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Gift Pokemon edit does not include a valid target, field, or value.",
                field: edit.Field,
                expected: "Valid Gift Pokemon edit"));
            return null;
        }

        return new SwShGiftPokemonEdit(giftIndex, field, value);
    }

    private static SwShGiftPokemonField? MapField(string? field)
    {
        return field switch
        {
            SwShGiftPokemonWorkflowService.SpeciesField => SwShGiftPokemonField.Species,
            SwShGiftPokemonWorkflowService.FormField => SwShGiftPokemonField.Form,
            SwShGiftPokemonWorkflowService.LevelField => SwShGiftPokemonField.Level,
            SwShGiftPokemonWorkflowService.HeldItemIdField => SwShGiftPokemonField.HeldItem,
            SwShGiftPokemonWorkflowService.BallItemIdField => SwShGiftPokemonField.BallItemId,
            SwShGiftPokemonWorkflowService.AbilityField => SwShGiftPokemonField.Ability,
            SwShGiftPokemonWorkflowService.NatureField => SwShGiftPokemonField.Nature,
            SwShGiftPokemonWorkflowService.GenderField => SwShGiftPokemonField.Gender,
            SwShGiftPokemonWorkflowService.ShinyLockField => SwShGiftPokemonField.ShinyLock,
            SwShGiftPokemonWorkflowService.DynamaxLevelField => SwShGiftPokemonField.DynamaxLevel,
            SwShGiftPokemonWorkflowService.CanGigantamaxField => SwShGiftPokemonField.CanGigantamax,
            SwShGiftPokemonWorkflowService.SpecialMoveIdField => SwShGiftPokemonField.SpecialMove,
            SwShGiftPokemonWorkflowService.IvHpField => SwShGiftPokemonField.IvHp,
            SwShGiftPokemonWorkflowService.IvAttackField => SwShGiftPokemonField.IvAttack,
            SwShGiftPokemonWorkflowService.IvDefenseField => SwShGiftPokemonField.IvDefense,
            SwShGiftPokemonWorkflowService.IvSpeedField => SwShGiftPokemonField.IvSpeed,
            SwShGiftPokemonWorkflowService.IvSpecialAttackField => SwShGiftPokemonField.IvSpecialAttack,
            SwShGiftPokemonWorkflowService.IvSpecialDefenseField => SwShGiftPokemonField.IvSpecialDefense,
            SwShGiftPokemonWorkflowService.FlawlessIvCountField => SwShGiftPokemonField.FlawlessIvCount,
            _ => null,
        };
    }

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShGiftPokemonWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Gift Pokemon apply target must stay inside the configured output root.",
                file: targetRelativePath,
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

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
    }

    private static ValidationDiagnostic CreateIvDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Gift Pokemon IV values must be -1 for random or 0-31 for fixed values; HP IV also accepts -4 for the 3-perfect sentinel.",
            field: field,
            expected: "Supported gift IV value");
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Gift Pokemon field '{field}' is not supported by the workflow yet.",
            field: "field",
            expected: "Supported Gift Pokemon field");
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
            Domain: SwShGiftPokemonWorkflowService.GiftPokemonEditDomain,
            Field: field,
            Expected: expected);
    }
}
