// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Gifts;

internal sealed class ZaGiftPokemonEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaGiftPokemonWorkflowService giftPokemonWorkflowService;

    public ZaGiftPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaGiftPokemonWorkflowService? giftPokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.giftPokemonWorkflowService = giftPokemonWorkflowService ?? new ZaGiftPokemonWorkflowService(this.fileSource);
    }

    public ZaGiftPokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int giftIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        return UpdateFields(
            paths,
            session,
            [new ZaGiftPokemonFieldUpdate(giftIndex, field, value)]);
    }

    public ZaGiftPokemonEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<ZaGiftPokemonFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = giftPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var currentWorkflow = OverlayPendingEdits(
            project,
            loadedWorkflow,
            currentSession.PendingEdits,
            diagnostics);

        if (!ZaEditSessionSupport.CanEdit(
                project,
                currentWorkflow.Summary,
                currentWorkflow.Diagnostics,
                ZaEditSessionSupport.GiftPokemonDomain,
                diagnostics))
        {
            return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        var projectedWorkflow = currentWorkflow;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Gift Pokemon batch update is missing a field or value.",
                    ZaEditSessionSupport.GiftPokemonDomain,
                    field: "updates",
                    expected: "Complete gift Pokemon field update"));
                return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
            }

            var gift = projectedWorkflow.Gifts.FirstOrDefault(
                candidate => candidate.GiftIndex == update.GiftIndex);
            if (gift is null)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Gift Pokemon {update.GiftIndex} is not present in the loaded Gift Pokemon workflow.",
                    ZaEditSessionSupport.GiftPokemonDomain,
                    field: "giftIndex",
                    expected: "Existing gift Pokemon record"));
                return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
            }

            var pendingEdit = CreatePendingEdit(
                projectedWorkflow,
                gift,
                update.Field,
                update.Value,
                diagnostics);
            if (pendingEdit is null)
            {
                return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
            }

            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
            projectedWorkflow = OverlayPendingEdit(projectedWorkflow, pendingEdit);
        }

        projectedWorkflow = OverlayPendingEdits(
            project,
            loadedWorkflow,
            updatedSession.PendingEdits,
            diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || !ValidateFinalSpeciesForms(loadedWorkflow, projectedWorkflow, diagnostics))
        {
            return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
        }

        return new ZaGiftPokemonEditResult(projectedWorkflow, updatedSession, diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = giftPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.GiftPokemonDomain,
            diagnostics);

        var effectiveWorkflow = workflow;
        foreach (var edit in session.PendingEdits)
        {
            var errorCount = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            ValidatePendingEdit(effectiveWorkflow, edit, diagnostics);
            if (diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == errorCount)
            {
                effectiveWorkflow = OverlayPendingEdit(effectiveWorkflow, edit);
            }
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            ValidateFinalSpeciesForms(workflow, effectiveWorkflow, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Gift Pokemon change is valid.",
                ZaEditSessionSupport.GiftPokemonDomain));
        }

        return new ZaEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        return ZaEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            session,
            ZaEditSessionSupport.GiftPokemonDomain,
            ZaDataPaths.PokemonDataArray,
            "Gift Pokemon",
            validation.Diagnostics,
            outputMode);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                ZaEditSessionSupport.GiftPokemonDomain,
                expected: "Current reviewed Gift Pokemon change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            var document = ZaPokemonDataDocument.Parse(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(document, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaWorkflowFileSource.Write(paths, ZaDataPaths.PokemonDataArray, document.Write(), outputMode);
            writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(ZaDataPaths.PokemonDataArray, outputMode));
            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                ZaEditSessionSupport.CreateApplyOutputMessage("Gift Pokemon", outputMode),
                ZaEditSessionSupport.GiftPokemonDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon output could not be written: {exception.Message}",
                ZaEditSessionSupport.GiftPokemonDomain,
                file: $"romfs/{ZaDataPaths.PokemonDataArray}",
                expected: "Readable source and writable output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        ZaGiftPokemonWorkflow workflow,
        ZaGiftPokemonEntry gift,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = ZaGiftPokemonWorkflowService.GetEditableField(workflow, normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = ZaEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            ZaEditSessionSupport.GiftPokemonDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return ZaEditSessionSupport.CreatePendingEdit(
            ZaEditSessionSupport.GiftPokemonDomain,
            $"Set {gift.Label} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(gift.Provenance.SourceLayer, gift.Provenance.SourceFile),
            ZaGiftPokemonWorkflowService.CreateGiftRecordId(gift.GiftIndex),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        ZaGiftPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Pokemon Legends Z-A Gift Pokemon.",
                ZaEditSessionSupport.GiftPokemonDomain,
                expected: ZaEditSessionSupport.GiftPokemonDomain));
            return;
        }

        var editableField = ZaGiftPokemonWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
            || workflow.Gifts.All(candidate => candidate.GiftIndex != giftIndex))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending gift Pokemon edit targets a record that is not loaded.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: "giftIndex",
                expected: "Existing gift Pokemon record"));
            return;
        }

        _ = ZaEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            ZaEditSessionSupport.GiftPokemonDomain,
            diagnostics);
    }

    private static bool ValidateFinalSpeciesForms(
        ZaGiftPokemonWorkflow loadedWorkflow,
        ZaGiftPokemonWorkflow projectedWorkflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var projectedByIndex = projectedWorkflow.Gifts.ToDictionary(gift => gift.GiftIndex);
        var isValid = true;

        foreach (var source in loadedWorkflow.Gifts)
        {
            if (!projectedByIndex.TryGetValue(source.GiftIndex, out var projected))
            {
                continue;
            }

            isValid &= ZaSpeciesFormPairValidation.ValidateChangedPair(
                loadedWorkflow.PokemonAvailability,
                source.SpeciesId,
                source.Form,
                projected.SpeciesId,
                projected.Form,
                ZaEditSessionSupport.GiftPokemonDomain,
                $"Gift Pokemon {source.GiftIndex}",
                diagnostics,
                source.Provenance.SourceFile);
        }

        return isValid;
    }

    private ZaGiftPokemonWorkflow OverlayPendingEdits(
        OpenedProject project,
        ZaGiftPokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        var pendingEdits = edits
            .Where(edit =>
                string.Equals(edit.Domain, ZaEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal)
                && ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out _)
                && int.TryParse(
                    edit.NewValue,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out _))
            .ToArray();

        if (pendingEdits.Length == 0)
        {
            return workflow;
        }

        try
        {
            var overlayDiagnostics = new List<ValidationDiagnostic>();
            var source = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            var labels = ZaTextLabelLookup.Load(project, fileSource, overlayDiagnostics, project.Paths);
            var abilityResolver = ZaGiftPokemonWorkflowService.ZaGiftAbilityResolver.Load(
                project,
                fileSource,
                labels,
                overlayDiagnostics);
            var document = ZaPokemonDataDocument.Parse(source.Bytes);
            foreach (var edit in pendingEdits)
            {
                ApplyEdit(document, edit, overlayDiagnostics);
            }

            if (overlayDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                if (diagnostics is not null)
                {
                    foreach (var diagnostic in overlayDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    {
                        diagnostics.Add(diagnostic);
                    }
                }

                return workflow;
            }

            var overlaySource = source with { Bytes = document.Write() };
            var giftsByIndex = ZaGiftPokemonWorkflowService
                .LoadRecords(overlaySource, labels, abilityResolver)
                .Select(gift => ZaGiftPokemonWorkflowService.WithFormOptions(
                    gift,
                    workflow.PokemonAvailability))
                .ToDictionary(gift => gift.GiftIndex);

            return workflow with
            {
                Gifts = workflow.Gifts
                    .Select(gift => giftsByIndex.TryGetValue(gift.GiftIndex, out var updatedGift) ? updatedGift : gift)
                    .ToArray(),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            diagnostics?.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon pending changes could not be previewed: {exception.Message}",
                ZaEditSessionSupport.GiftPokemonDomain,
                file: $"romfs/{ZaDataPaths.PokemonDataArray}",
                expected: "Readable Pokemon Legends Z-A gift Pokemon source"));
            return workflow;
        }
    }

    private static ZaGiftPokemonWorkflow OverlayPendingEdit(ZaGiftPokemonWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal)
            || !ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Gifts = workflow.Gifts
                .Select(gift => gift.GiftIndex == giftIndex
                    ? ZaGiftPokemonWorkflowService.WithFormOptions(
                        OverlayGift(workflow, gift, edit.Field, value),
                        workflow.PokemonAvailability)
                    : gift)
                .ToArray(),
        };
    }

    private static ZaGiftPokemonEntry OverlayGift(
        ZaGiftPokemonWorkflow workflow,
        ZaGiftPokemonEntry gift,
        string? field,
        int value)
    {
        return field switch
        {
            ZaGiftPokemonWorkflowService.SpeciesField => gift with
            {
                SpeciesId = value,
                Species = GetOptionLabel(workflow, field, value, "Pokemon"),
            },
            ZaGiftPokemonWorkflowService.FormField => gift with { Form = value },
            ZaGiftPokemonWorkflowService.LevelField => gift with { Level = value, MaxLevel = value },
            ZaGiftPokemonWorkflowService.HeldItemIdField => gift with
            {
                HeldItemId = value,
                HeldItem = value == 0 ? null : GetOptionLabel(workflow, field, value, "Item"),
            },
            ZaGiftPokemonWorkflowService.AbilityField => gift with
            {
                Ability = value,
                AbilityLabel = GetRecordOptionLabel(gift.AbilityOptions, value, "Ability mode"),
            },
            ZaGiftPokemonWorkflowService.NatureField => gift with
            {
                Nature = value,
                NatureLabel = GetOptionLabel(workflow, field, value, "Nature"),
            },
            ZaGiftPokemonWorkflowService.GenderField => gift with
            {
                Gender = value,
                GenderLabel = GetOptionLabel(workflow, field, value, "Gender"),
            },
            ZaGiftPokemonWorkflowService.ShinyLockField => gift with
            {
                ShinyLock = value,
                ShinyLockLabel = GetOptionLabel(workflow, field, value, "Shiny mode"),
            },
            ZaGiftPokemonWorkflowService.Move1IdField => OverlayMove(gift, 0, value, workflow, field),
            ZaGiftPokemonWorkflowService.Move2IdField => OverlayMove(gift, 1, value, workflow, field),
            ZaGiftPokemonWorkflowService.Move3IdField => OverlayMove(gift, 2, value, workflow, field),
            ZaGiftPokemonWorkflowService.Move4IdField => OverlayMove(gift, 3, value, workflow, field),
            ZaGiftPokemonWorkflowService.FlawlessIvCountField => OverlayIvPreset(gift, value),
            ZaGiftPokemonWorkflowService.IvHpField => OverlayIvs(gift, gift.Ivs with { HP = value }),
            ZaGiftPokemonWorkflowService.IvAttackField => OverlayIvs(gift, gift.Ivs with { Attack = value }),
            ZaGiftPokemonWorkflowService.IvDefenseField => OverlayIvs(gift, gift.Ivs with { Defense = value }),
            ZaGiftPokemonWorkflowService.IvSpecialAttackField => OverlayIvs(gift, gift.Ivs with { SpecialAttack = value }),
            ZaGiftPokemonWorkflowService.IvSpecialDefenseField => OverlayIvs(gift, gift.Ivs with { SpecialDefense = value }),
            ZaGiftPokemonWorkflowService.IvSpeedField => OverlayIvs(gift, gift.Ivs with { Speed = value }),
            _ => gift,
        };
    }

    private static ZaGiftPokemonEntry OverlayMove(
        ZaGiftPokemonEntry gift,
        int moveIndex,
        int value,
        ZaGiftPokemonWorkflow workflow,
        string field)
    {
        var moves = gift.Moves.ToList();
        while (moves.Count <= moveIndex)
        {
            moves.Add(new ZaGiftPokemonMoveRecord(moves.Count, 0, null, PointUps: 0));
        }

        moves[moveIndex] = moves[moveIndex] with
        {
            MoveId = value,
            Move = value <= ZaPokemonDataConstants.MoveAuto ? null : GetOptionLabel(workflow, field, value, "Move"),
        };

        return gift with { Moves = moves };
    }

    private static ZaGiftPokemonEntry OverlayIvPreset(ZaGiftPokemonEntry gift, int value)
    {
        return gift with
        {
            FlawlessIvCount = value,
            IvSummary = value == 0
                ? "Random IVs"
                : value == 1
                    ? "1 guaranteed perfect IV"
                    : $"{value.ToString(CultureInfo.InvariantCulture)} guaranteed perfect IVs",
        };
    }

    private static ZaGiftPokemonEntry OverlayIvs(ZaGiftPokemonEntry gift, ZaGiftPokemonIvsRecord ivs)
    {
        return gift with
        {
            Ivs = ivs,
            FlawlessIvCount = null,
            IvSummary = string.Create(
                CultureInfo.InvariantCulture,
                $"Fixed IVs: HP {ivs.HP}, Atk {ivs.Attack}, Def {ivs.Defense}, SpA {ivs.SpecialAttack}, SpD {ivs.SpecialDefense}, Spe {ivs.Speed}"),
        };
    }

    private static void ApplyEdit(
        ZaPokemonDataDocument document,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal)
            || !ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending gift Pokemon edit is not valid for apply.",
                ZaEditSessionSupport.GiftPokemonDomain,
                expected: "Valid gift Pokemon edit"));
            return;
        }

        var rows = ZaGiftPokemonWorkflowService.ResolveApplyTargets(document, giftIndex);
        var displayRow = ZaGiftPokemonWorkflowService.ResolveApplyDisplayEntry(document, giftIndex);
        if (rows.Count == 0 || displayRow is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending gift Pokemon edit target is not present in the source array.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: "giftIndex",
                expected: "Existing source gift Pokemon row"));
            return;
        }

        foreach (var row in rows)
        {
            ApplyField(row, edit.Field, value);
        }

        if (edit.Field is ZaGiftPokemonWorkflowService.SpeciesField or ZaGiftPokemonWorkflowService.FormField)
        {
            foreach (var row in rows)
            {
                row.DevNo = displayRow.DevNo;
                row.FormNo = displayRow.FormNo;
            }
        }
    }

    private static void ApplyField(
        ZaPokemonDataEntry row,
        string? field,
        int value)
    {
        switch (field)
        {
            case ZaGiftPokemonWorkflowService.SpeciesField:
                row.DevNo = value;
                break;
            case ZaGiftPokemonWorkflowService.FormField:
                row.FormNo = value;
                break;
            case ZaGiftPokemonWorkflowService.LevelField:
                row.MinLevel = value;
                row.MaxLevel = value;
                break;
            case ZaGiftPokemonWorkflowService.HeldItemIdField:
                row.HoldItem = value;
                break;
            case ZaGiftPokemonWorkflowService.AbilityField:
                row.Tokusei = value;
                break;
            case ZaGiftPokemonWorkflowService.NatureField:
                row.Seikaku = value;
                break;
            case ZaGiftPokemonWorkflowService.GenderField:
                row.Sex = value;
                break;
            case ZaGiftPokemonWorkflowService.ShinyLockField:
                row.Rare = value;
                break;
            case ZaGiftPokemonWorkflowService.Move1IdField:
                SetMove(row, 0, value);
                break;
            case ZaGiftPokemonWorkflowService.Move2IdField:
                SetMove(row, 1, value);
                break;
            case ZaGiftPokemonWorkflowService.Move3IdField:
                SetMove(row, 2, value);
                break;
            case ZaGiftPokemonWorkflowService.Move4IdField:
                SetMove(row, 3, value);
                break;
            case ZaGiftPokemonWorkflowService.FlawlessIvCountField:
                SetIvPreset(row, value);
                break;
            case ZaGiftPokemonWorkflowService.IvHpField:
                SetIv(row, value, ivs => ivs with { HP = value });
                break;
            case ZaGiftPokemonWorkflowService.IvAttackField:
                SetIv(row, value, ivs => ivs with { Attack = value });
                break;
            case ZaGiftPokemonWorkflowService.IvDefenseField:
                SetIv(row, value, ivs => ivs with { Defense = value });
                break;
            case ZaGiftPokemonWorkflowService.IvSpecialAttackField:
                SetIv(row, value, ivs => ivs with { SpecialAttack = value });
                break;
            case ZaGiftPokemonWorkflowService.IvSpecialDefenseField:
                SetIv(row, value, ivs => ivs with { SpecialDefense = value });
                break;
            case ZaGiftPokemonWorkflowService.IvSpeedField:
                SetIv(row, value, ivs => ivs with { Speed = value });
                break;
        }
    }

    private static void SetMove(ZaPokemonDataEntry row, int moveIndex, int moveId)
    {
        row.WazaList = (row.WazaList ?? new ZaPokemonDataMovesRecord(
                ZaPokemonDataConstants.MoveNone,
                ZaPokemonDataConstants.MoveNone,
                ZaPokemonDataConstants.MoveNone,
                ZaPokemonDataConstants.MoveNone))
            .SetMove(moveIndex, moveId);
    }

    private static void SetIvPreset(ZaPokemonDataEntry row, int value)
    {
        ZaPokemonDataIvEncoding.SetPreset(row, value);
    }

    private static void SetIv(
        ZaPokemonDataEntry row,
        int value,
        Func<ZaPokemonDataStatsRecord, ZaPokemonDataStatsRecord> update)
    {
        _ = value;
        ZaPokemonDataIvEncoding.SetFixedIvs(row, update);
    }

    private static string GetOptionLabel(
        ZaGiftPokemonWorkflow workflow,
        string? field,
        int value,
        string fallbackPrefix)
    {
        var options = workflow.EditableFields.FirstOrDefault(editableField =>
            string.Equals(editableField.Field, field, StringComparison.Ordinal));
        return GetRecordOptionLabel(options?.Options ?? [], value, fallbackPrefix);
    }

    private static string GetRecordOptionLabel(
        IReadOnlyList<ZaGiftPokemonEditableFieldOption> options,
        int value,
        string fallbackPrefix)
    {
        return options.FirstOrDefault(option => option.Value == value)?.Label
            ?? $"{fallbackPrefix} {value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Gift Pokemon field '{field}' is not supported by Pokemon Legends Z-A Gift Pokemon yet.",
            ZaEditSessionSupport.GiftPokemonDomain,
            field: "field",
            expected: "Supported Pokemon Legends Z-A gift Pokemon field");
    }
}
