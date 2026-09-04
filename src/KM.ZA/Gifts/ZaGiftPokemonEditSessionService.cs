// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.ZA.Gifts;

internal sealed class ZaGiftPokemonEditSessionService
{
    private const string VerifiedBaseGiftRowsField = "verifiedBaseGiftRows";
    private const string VerifiedBaseGiftRowsValuePrefix = "sha256:";

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
            var stagedSession = updatedSession;
            updatedSession = RemoveSourceEquivalentPendingEdits(loadedWorkflow, stagedSession);
            projectedWorkflow = ReferenceEquals(stagedSession, updatedSession)
                ? OverlayPendingEdit(projectedWorkflow, pendingEdit)
                : OverlayPendingEdits(
                    project,
                    loadedWorkflow,
                    updatedSession.PendingEdits,
                    diagnostics);
        }

        projectedWorkflow = OverlayPendingEdits(
            project,
            loadedWorkflow,
            updatedSession.PendingEdits,
            diagnostics);
        var finalValuesAreValid =
            ValidateFinalSpeciesForms(loadedWorkflow, projectedWorkflow, diagnostics)
            & ValidateFinalAlphaSettings(projectedWorkflow, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || !finalValuesAreValid)
        {
            return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
        }

        return new ZaGiftPokemonEditResult(projectedWorkflow, updatedSession, diagnostics);
    }

    public ZaGiftPokemonEditResult StageGiftVanilla(
        ProjectPaths paths,
        EditSession? session,
        int giftIndex)
    {
        ArgumentNullException.ThrowIfNull(paths);

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
                loadedWorkflow.Summary,
                loadedWorkflow.Diagnostics,
                ZaEditSessionSupport.GiftPokemonDomain,
                diagnostics))
        {
            return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
        }

        var targetGift = loadedWorkflow.Gifts.FirstOrDefault(candidate => candidate.GiftIndex == giftIndex);
        if (targetGift is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon {giftIndex} is not present in the loaded Gift Pokemon workflow.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: "giftIndex",
                expected: "Existing Z-A Gift Pokemon record"));
            return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
        }

        if (!targetGift.CanRevertToVanilla)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                targetGift.RevertToVanillaBlockedReason
                    ?? "This Gift Pokemon cannot be matched safely to verified vanilla event rows.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: "giftIndex",
                expected: "Exact matching active and verified vanilla Gift Pokemon rows"));
            return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
        }

        ZaWorkflowFile activeSource;
        ZaWorkflowFile baseSource;
        IReadOnlyList<(ZaPokemonDataEntry Active, ZaPokemonDataEntry Vanilla)> restoreRows;
        try
        {
            activeSource = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            baseSource = fileSource.ReadBase(project, ZaDataPaths.PokemonDataArray);
            var activeDocument = ZaPokemonDataDocument.Parse(activeSource.Bytes);
            var baseDocument = ZaPokemonDataDocument.Parse(baseSource.Bytes);
            if (!ZaGiftPokemonWorkflowService.TryResolveVanillaRestoreRows(
                    activeDocument,
                    baseDocument,
                    giftIndex,
                    out restoreRows,
                    out var blockedReason))
            {
                throw new InvalidDataException(
                    blockedReason ?? "The selected Gift Pokemon could not be matched to verified vanilla rows.");
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Verified vanilla Gift Pokemon data could not be read: {exception.Message}",
                ZaEditSessionSupport.GiftPokemonDomain,
                file: $"romfs/{ZaDataPaths.PokemonDataArray}",
                expected: "Readable active and verified base tables with exact matching Gift Pokemon event IDs"));
            return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
        }

        var recordId = ZaGiftPokemonWorkflowService.CreateGiftRecordId(giftIndex);
        var retainedEdits = currentSession.PendingEdits
            .Where(edit =>
                !string.Equals(edit.Domain, ZaEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal)
                || !string.Equals(edit.RecordId, recordId, StringComparison.Ordinal))
            .ToArray();
        var removedEditCount = currentSession.PendingEdits.Count - retainedEdits.Length;
        var updatedSession = currentSession with { PendingEdits = retainedEdits };
        var needsRestore = restoreRows.Any(pair =>
            !ZaGiftPokemonWorkflowService.HasSameEditableValues(pair.Active, pair.Vanilla));
        if (needsRestore)
        {
            var restoreEdit = new PendingEdit(
                ZaEditSessionSupport.GiftPokemonDomain,
                $"Restore {targetGift.Label} from its exact verified vanilla event row{(restoreRows.Count == 1 ? string.Empty : "s")}.",
                [
                    new ProjectFileReference(activeSource.SourceLayer, activeSource.RelativePath),
                    new ProjectFileReference(baseSource.SourceLayer, baseSource.RelativePath),
                ],
                recordId,
                VerifiedBaseGiftRowsField,
                CreateVanillaRestoreFingerprint(restoreRows));
            updatedSession = ZaEditSessionSupport.ReplacePendingEdit(updatedSession, restoreEdit);
        }

        var stagedWorkflow = OverlayPendingEdits(
            project,
            loadedWorkflow,
            updatedSession.PendingEdits,
            diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ZaGiftPokemonEditResult(currentWorkflow, currentSession, diagnostics);
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            needsRestore
                ? $"Staged the exact verified vanilla values for {restoreRows.Count.ToString(CultureInfo.InvariantCulture)} Gift Pokemon event row(s)."
                : removedEditCount > 0
                    ? "The selected Gift Pokemon already matches verified vanilla values. Its pending edits were cleared."
                    : "The selected Gift Pokemon already matches verified vanilla values.",
            ZaEditSessionSupport.GiftPokemonDomain));
        return new ZaGiftPokemonEditResult(stagedWorkflow, updatedSession, diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = giftPokemonWorkflowService.Load(project);
        var effectiveSession = RemoveSourceEquivalentPendingEdits(workflow, session);
        var diagnostics = new List<ValidationDiagnostic>();

        var restoreContexts = new Dictionary<int, IReadOnlyList<(ZaPokemonDataEntry Active, ZaPokemonDataEntry Vanilla)>>();
        if (effectiveSession.PendingEdits.Any(IsVerifiedBaseGiftRowsEdit))
        {
            try
            {
                var activeDocument = ZaPokemonDataDocument.Parse(
                    fileSource.Read(project, ZaDataPaths.PokemonDataArray).Bytes);
                var baseDocument = ZaPokemonDataDocument.Parse(
                    fileSource.ReadBase(project, ZaDataPaths.PokemonDataArray).Bytes);
                foreach (var edit in effectiveSession.PendingEdits.Where(IsVerifiedBaseGiftRowsEdit))
                {
                    var blockedReason = "The staged Gift Pokemon restore does not target an existing Gift Pokemon record.";
                    if (!ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
                        || !ZaGiftPokemonWorkflowService.TryResolveVanillaRestoreRows(
                            activeDocument,
                            baseDocument,
                            giftIndex,
                            out var rows,
                            out blockedReason))
                    {
                        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            blockedReason
                                ?? "The staged Gift Pokemon restore no longer matches exact active and verified base rows.",
                            ZaEditSessionSupport.GiftPokemonDomain,
                            field: VerifiedBaseGiftRowsField,
                            expected: "Restage the selected Gift Pokemon restore"));
                        continue;
                    }

                    restoreContexts[giftIndex] = rows;
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Staged Gift Pokemon restore data could not be verified: {exception.Message}",
                    ZaEditSessionSupport.GiftPokemonDomain,
                    file: $"romfs/{ZaDataPaths.PokemonDataArray}",
                    expected: "Readable active and verified base Gift Pokemon tables"));
            }
        }

        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            ZaEditSessionSupport.GiftPokemonDomain,
            diagnostics);

        var effectiveWorkflow = OverlayPendingEdits(
            project,
            workflow,
            effectiveSession.PendingEdits,
            diagnostics);
        foreach (var edit in effectiveSession.PendingEdits)
        {
            ValidatePendingEdit(effectiveWorkflow, edit, restoreContexts, diagnostics);
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            ValidateFinalSpeciesForms(workflow, effectiveWorkflow, diagnostics);
            ValidateFinalAlphaSettings(effectiveWorkflow, diagnostics);
        }

        if (effectiveSession.PendingEdits.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Gift Pokemon change is valid.",
                ZaEditSessionSupport.GiftPokemonDomain));
        }

        return new ZaEditSessionValidation(
            effectiveSession,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        return ZaChangePlanSourceGuard.Capture(
            paths,
            session,
            effectiveSession => CreateChangePlanCore(paths, effectiveSession, outputMode),
            outputMode,
            candidate => Validate(paths, candidate).Session);
    }

    private ChangePlan CreateChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var effectiveSession = validation.Session;
        var plan = ZaEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            effectiveSession,
            ZaEditSessionSupport.GiftPokemonDomain,
            ZaDataPaths.PokemonDataArray,
            "Gift Pokemon",
            validation.Diagnostics,
            outputMode);
        if (!plan.CanApply)
        {
            return plan;
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var sourceFingerprint = CreatePlanSourceFingerprint(project, effectiveSession);
            return plan with
            {
                Writes = plan.Writes
                    .Select((write, index) => index == 0
                        ? write with { SourceFingerprint = sourceFingerprint }
                        : write)
                    .ToArray(),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            var diagnostics = plan.Diagnostics
                .Append(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Gift Pokemon source fingerprint could not be created: {exception.Message}",
                    ZaEditSessionSupport.GiftPokemonDomain,
                    file: $"romfs/{ZaDataPaths.PokemonDataArray}",
                    expected: "Readable active Gift Pokemon data and verified base data for restores"))
                .ToArray();
            return new ChangePlan(plan.SessionId, Array.Empty<PlannedFileWrite>(), diagnostics);
        }
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
            var effectiveSession = RemoveSourceEquivalentPendingEdits(
                giftPokemonWorkflowService.Load(project),
                session);
            var source = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            var originalDocument = ZaPokemonDataDocument.Parse(source.Bytes);
            var document = ZaPokemonDataDocument.Parse(source.Bytes);
            var baseDocument = effectiveSession.PendingEdits.Any(IsVerifiedBaseGiftRowsEdit)
                ? ZaPokemonDataDocument.Parse(
                    fileSource.ReadBase(project, ZaDataPaths.PokemonDataArray).Bytes)
                : null;
            foreach (var edit in effectiveSession.PendingEdits.OrderByDescending(IsVerifiedBaseGiftRowsEdit))
            {
                ApplyEdit(document, baseDocument, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var outputBytes = document.Write();
            VerifySerializedOutput(
                originalDocument,
                document,
                baseDocument,
                ZaPokemonDataDocument.Parse(outputBytes),
                effectiveSession,
                diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            ZaWorkflowFileSource.Write(
                paths,
                ZaDataPaths.PokemonDataArray,
                outputBytes,
                outputMode,
                revalidateReviewedState: () =>
                    ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                        reviewedPlan,
                        CreateChangePlan(paths, session, outputMode)));
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
        catch (Exception exception) when (!ZaEditSessionSupport.IsOutputSafetyException(exception))
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

        if (!ValidateAlphaEditability(gift, normalizedField, diagnostics))
        {
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
        IReadOnlyDictionary<int, IReadOnlyList<(ZaPokemonDataEntry Active, ZaPokemonDataEntry Vanilla)>> restoreContexts,
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

        if (IsVerifiedBaseGiftRowsEdit(edit))
        {
            if (edit.NewValue is null
                || !edit.NewValue.StartsWith(VerifiedBaseGiftRowsValuePrefix, StringComparison.Ordinal)
                || !HasVanillaRestoreSourceMarker(edit)
                || !ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var restoreGiftIndex)
                || workflow.Gifts.FirstOrDefault(gift => gift.GiftIndex == restoreGiftIndex) is not { } restoreGift
                || !restoreGift.CanRevertToVanilla
                || !restoreContexts.TryGetValue(restoreGiftIndex, out var restoreRows)
                || !string.Equals(
                    edit.NewValue,
                    CreateVanillaRestoreFingerprint(restoreRows),
                    StringComparison.Ordinal))
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "The Gift Pokemon restoration marker is not tied to exact verified vanilla event rows.",
                    ZaEditSessionSupport.GiftPokemonDomain,
                    field: VerifiedBaseGiftRowsField,
                    expected: "Existing Gift Pokemon record with matching active and verified base event IDs"));
            }

            return;
        }

        var editableField = ZaGiftPokemonWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending gift Pokemon edit targets a record that is not loaded.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: "giftIndex",
                expected: "Existing gift Pokemon record"));
            return;
        }

        var gift = workflow.Gifts.FirstOrDefault(candidate => candidate.GiftIndex == giftIndex);
        if (gift is null)
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending gift Pokemon edit targets a record that is not loaded.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: "giftIndex",
                expected: "Existing gift Pokemon record"));
            return;
        }

        if (!ValidateAlphaEditability(gift, edit.Field, diagnostics))
        {
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
                && (IsVerifiedBaseGiftRowsEdit(edit)
                    || int.TryParse(
                        edit.NewValue,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out _)))
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
            var baseDocument = pendingEdits.Any(IsVerifiedBaseGiftRowsEdit)
                ? ZaPokemonDataDocument.Parse(
                    fileSource.ReadBase(project, ZaDataPaths.PokemonDataArray).Bytes)
                : null;
            foreach (var edit in pendingEdits.OrderByDescending(IsVerifiedBaseGiftRowsEdit))
            {
                ApplyEdit(document, baseDocument, edit, overlayDiagnostics);
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
                    .Select(gift => giftsByIndex.TryGetValue(gift.GiftIndex, out var updatedGift)
                        ? updatedGift with
                        {
                            CanRevertToVanilla = gift.CanRevertToVanilla,
                            RevertToVanillaBlockedReason = gift.RevertToVanillaBlockedReason,
                        }
                        : gift)
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
            ZaGiftPokemonWorkflowService.AlphaChancePercentField => gift with
            {
                AlphaProbability = value,
                AlphaChancePercent = value,
            },
            ZaGiftPokemonWorkflowService.AlphaLevelBonusField => gift with { AlphaAdditionalLevel = value },
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
        ZaPokemonDataDocument? baseDocument,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (IsVerifiedBaseGiftRowsEdit(edit))
        {
            ApplyVerifiedBaseGiftRows(document, baseDocument, edit, diagnostics);
            return;
        }

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

        if (IsAlphaField(edit.Field)
            && !ZaGiftPokemonWorkflowService.CanEditAlphaSettings(displayRow))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Alpha settings edit no longer targets a supported restoration gift row.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: edit.Field,
                expected: "Restoration gift with whole-number Alpha chance and Alpha level bonus from 0 through 100"));
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

    private static void ApplyVerifiedBaseGiftRows(
        ZaPokemonDataDocument document,
        ZaPokemonDataDocument? baseDocument,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (baseDocument is null
            || !HasVanillaRestoreSourceMarker(edit)
            || !ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The selected Gift Pokemon restore is not tied to the verified base table.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: VerifiedBaseGiftRowsField,
                expected: "Exact matching active and verified base Gift Pokemon event IDs"));
            return;
        }

        if (!ZaGiftPokemonWorkflowService.TryResolveVanillaRestoreRows(
                document,
                baseDocument,
                giftIndex,
                out var restoreRows,
                out var blockedReason))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                blockedReason
                    ?? "The selected Gift Pokemon could not be matched to exact verified vanilla event rows.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: VerifiedBaseGiftRowsField,
                expected: "Exact matching active and verified base Gift Pokemon event IDs"));
            return;
        }

        var expectedFingerprint = CreateVanillaRestoreFingerprint(restoreRows);
        if (!string.Equals(edit.NewValue, expectedFingerprint, StringComparison.Ordinal))
        {
            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Verified vanilla Gift Pokemon data changed after this restore was staged.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: VerifiedBaseGiftRowsField,
                expected: "Restage the selected Gift Pokemon restore against the current verified base data"));
            return;
        }

        foreach (var (active, vanilla) in restoreRows)
        {
            ZaGiftPokemonWorkflowService.RestoreEditableValues(active, vanilla);
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
            case ZaGiftPokemonWorkflowService.AlphaChancePercentField:
                row.OyabunProbability = value;
                break;
            case ZaGiftPokemonWorkflowService.AlphaLevelBonusField:
                row.OyabunAdditionalLevel = value;
                break;
        }
    }

    private static bool ValidateAlphaEditability(
        ZaGiftPokemonEntry gift,
        string? field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsAlphaField(field) || gift.CanEditAlphaSettings)
        {
            return true;
        }

        diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Alpha chance and Alpha level bonus can only be edited for supported fossil restoration gifts.",
            ZaEditSessionSupport.GiftPokemonDomain,
            field: field,
            expected: "Restoration gift with whole-number Alpha chance and Alpha level bonus from 0 through 100"));
        return false;
    }

    private static bool ValidateFinalAlphaSettings(
        ZaGiftPokemonWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var isValid = true;
        foreach (var gift in workflow.Gifts.Where(candidate => candidate.CanEditAlphaSettings))
        {
            if (gift.AlphaChancePercent is not int alphaChancePercent)
            {
                diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Restoration gift '{gift.EventLabel}' does not have a supported whole-number Alpha chance.",
                    ZaEditSessionSupport.GiftPokemonDomain,
                    field: ZaGiftPokemonWorkflowService.AlphaChancePercentField,
                    expected: "Whole-number percent from 0 through 100"));
                isValid = false;
                continue;
            }

            if (alphaChancePercent <= 0)
            {
                continue;
            }

            var alphaLevel = (long)gift.Level + gift.AlphaAdditionalLevel;
            if (alphaLevel <= 100)
            {
                continue;
            }

            diagnostics.Add(ZaEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Restoration gift '{gift.EventLabel}' would become level {alphaLevel.ToString(CultureInfo.InvariantCulture)} "
                + $"because base level {gift.Level.ToString(CultureInfo.InvariantCulture)} and Alpha level bonus "
                + $"{gift.AlphaAdditionalLevel.ToString(CultureInfo.InvariantCulture)} are combined when Alpha chance is enabled.",
                ZaEditSessionSupport.GiftPokemonDomain,
                field: ZaGiftPokemonWorkflowService.AlphaLevelBonusField,
                expected: "When Alpha chance is above 0 percent, base level plus Alpha level bonus must be at most 100"));
            isValid = false;
        }

        return isValid;
    }

    private static bool IsAlphaField(string? field)
    {
        return field is ZaGiftPokemonWorkflowService.AlphaChancePercentField
            or ZaGiftPokemonWorkflowService.AlphaLevelBonusField;
    }

    private static void SetMove(ZaPokemonDataEntry row, int moveIndex, int moveId)
    {
        var moves = (row.WazaList ?? new ZaPokemonDataMovesRecord(
                ZaPokemonDataConstants.MoveAuto,
                ZaPokemonDataConstants.MoveAuto,
                ZaPokemonDataConstants.MoveAuto,
                ZaPokemonDataConstants.MoveAuto))
            .SetMove(moveIndex, moveId);
        row.WazaList = moves.Values.All(move => move == ZaPokemonDataConstants.MoveAuto)
            ? null
            : moves;
    }

    private static EditSession RemoveSourceEquivalentPendingEdits(
        ZaGiftPokemonWorkflow sourceWorkflow,
        EditSession session)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSourceEquivalentEdit(sourceWorkflow, session, edit))
            .ToArray();
        return pendingEdits.Length == session.PendingEdits.Count
            ? session
            : session with { PendingEdits = pendingEdits };
    }

    private static bool IsSourceEquivalentEdit(
        ZaGiftPokemonWorkflow sourceWorkflow,
        EditSession session,
        PendingEdit edit)
    {
        var hasRestoreMarkerForRecord = session.PendingEdits.Any(candidate =>
            IsVerifiedBaseGiftRowsEdit(candidate)
            && string.Equals(candidate.RecordId, edit.RecordId, StringComparison.Ordinal));
        if (!string.Equals(edit.Domain, ZaEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal)
            || hasRestoreMarkerForRecord
            || !ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
            || !int.TryParse(
                edit.NewValue,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value)
            || sourceWorkflow.Gifts.FirstOrDefault(gift => gift.GiftIndex == giftIndex) is not { } sourceGift)
        {
            return false;
        }

        return edit.Field switch
        {
            ZaGiftPokemonWorkflowService.SpeciesField => sourceGift.SpeciesId == value,
            ZaGiftPokemonWorkflowService.FormField => sourceGift.Form == value,
            ZaGiftPokemonWorkflowService.LevelField => sourceGift.Level == value && sourceGift.MaxLevel == value,
            ZaGiftPokemonWorkflowService.HeldItemIdField => sourceGift.HeldItemId == value,
            ZaGiftPokemonWorkflowService.AbilityField => sourceGift.Ability == value,
            ZaGiftPokemonWorkflowService.NatureField => sourceGift.Nature == value,
            ZaGiftPokemonWorkflowService.GenderField => sourceGift.Gender == value,
            ZaGiftPokemonWorkflowService.ShinyLockField => sourceGift.ShinyLock == value,
            ZaGiftPokemonWorkflowService.Move1IdField => SourceMoveEquals(sourceGift, 0, value),
            ZaGiftPokemonWorkflowService.Move2IdField => SourceMoveEquals(sourceGift, 1, value),
            ZaGiftPokemonWorkflowService.Move3IdField => SourceMoveEquals(sourceGift, 2, value),
            ZaGiftPokemonWorkflowService.Move4IdField => SourceMoveEquals(sourceGift, 3, value),
            ZaGiftPokemonWorkflowService.FlawlessIvCountField => sourceGift.FlawlessIvCount == value,
            ZaGiftPokemonWorkflowService.IvHpField =>
                sourceGift.FlawlessIvCount is null && sourceGift.Ivs.HP == value,
            ZaGiftPokemonWorkflowService.IvAttackField =>
                sourceGift.FlawlessIvCount is null && sourceGift.Ivs.Attack == value,
            ZaGiftPokemonWorkflowService.IvDefenseField =>
                sourceGift.FlawlessIvCount is null && sourceGift.Ivs.Defense == value,
            ZaGiftPokemonWorkflowService.IvSpecialAttackField =>
                sourceGift.FlawlessIvCount is null && sourceGift.Ivs.SpecialAttack == value,
            ZaGiftPokemonWorkflowService.IvSpecialDefenseField =>
                sourceGift.FlawlessIvCount is null && sourceGift.Ivs.SpecialDefense == value,
            ZaGiftPokemonWorkflowService.IvSpeedField =>
                sourceGift.FlawlessIvCount is null && sourceGift.Ivs.Speed == value,
            ZaGiftPokemonWorkflowService.AlphaChancePercentField => sourceGift.AlphaChancePercent == value,
            ZaGiftPokemonWorkflowService.AlphaLevelBonusField => sourceGift.AlphaAdditionalLevel == value,
            _ => false,
        };
    }

    private static bool SourceMoveEquals(ZaGiftPokemonEntry gift, int slot, int value)
    {
        return gift.Moves.FirstOrDefault(move => move.Slot == slot)?.MoveId == value;
    }

    private static bool IsVerifiedBaseGiftRowsEdit(PendingEdit edit)
    {
        return string.Equals(edit.Domain, ZaEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal)
            && string.Equals(edit.Field, VerifiedBaseGiftRowsField, StringComparison.Ordinal);
    }

    private static bool HasVanillaRestoreSourceMarker(PendingEdit edit)
    {
        var virtualPath = ZaDataPaths.PokemonDataArray;
        var relativePath = $"romfs/{virtualPath}";
        return edit.Sources.Any(source =>
            source.Layer == ProjectFileLayer.Base
            && (string.Equals(source.RelativePath, virtualPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(source.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)));
    }

    private static string CreateVanillaRestoreFingerprint(
        IReadOnlyList<(ZaPokemonDataEntry Active, ZaPokemonDataEntry Vanilla)> rows)
    {
        var builder = new StringBuilder();
        foreach (var (active, vanilla) in rows)
        {
            AppendFingerprintValue(builder, active.Id);
            AppendFingerprintValue(builder, vanilla.DevNo);
            AppendFingerprintValue(builder, vanilla.MinLevel);
            AppendFingerprintValue(builder, vanilla.MaxLevel);
            AppendFingerprintValue(builder, vanilla.Sex);
            AppendFingerprintValue(builder, vanilla.FormNo);
            AppendFingerprintValue(builder, vanilla.Rare);
            AppendFingerprintValue(builder, vanilla.Tokusei);
            AppendFingerprintValue(builder, vanilla.Seikaku);
            AppendFingerprintValue(builder, vanilla.TalentScale);
            AppendFingerprintValue(builder, vanilla.TalentVNum);
            AppendFingerprintValue(builder, vanilla.OyabunProbability.ToString("R", CultureInfo.InvariantCulture));
            AppendFingerprintValue(builder, vanilla.OyabunAdditionalLevel);
            AppendFingerprintValue(builder, vanilla.HoldItem);
            AppendFingerprintValue(builder, vanilla.TalentValue?.HP);
            AppendFingerprintValue(builder, vanilla.TalentValue?.Attack);
            AppendFingerprintValue(builder, vanilla.TalentValue?.Defense);
            AppendFingerprintValue(builder, vanilla.TalentValue?.SpecialAttack);
            AppendFingerprintValue(builder, vanilla.TalentValue?.SpecialDefense);
            AppendFingerprintValue(builder, vanilla.TalentValue?.Speed);
            AppendFingerprintValue(builder, vanilla.WazaList?.Move1);
            AppendFingerprintValue(builder, vanilla.WazaList?.Move2);
            AppendFingerprintValue(builder, vanilla.WazaList?.Move3);
            AppendFingerprintValue(builder, vanilla.WazaList?.Move4);
        }

        return VerifiedBaseGiftRowsValuePrefix
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private string CreatePlanSourceFingerprint(OpenedProject project, EditSession session)
    {
        var activeSource = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
        var builder = new StringBuilder();
        AppendFingerprintValue(builder, activeSource.SourceLayer.ToString());
        AppendFingerprintValue(builder, activeSource.RelativePath);
        AppendFingerprintValue(builder, Convert.ToHexString(SHA256.HashData(activeSource.Bytes)));
        if (session.PendingEdits.Any(IsVerifiedBaseGiftRowsEdit))
        {
            var baseSource = fileSource.ReadBase(project, ZaDataPaths.PokemonDataArray);
            AppendFingerprintValue(builder, baseSource.SourceLayer.ToString());
            AppendFingerprintValue(builder, baseSource.RelativePath);
            AppendFingerprintValue(builder, Convert.ToHexString(SHA256.HashData(baseSource.Bytes)));
        }

        foreach (var edit in session.PendingEdits
                     .OrderBy(edit => edit.Domain, StringComparer.Ordinal)
                     .ThenBy(edit => edit.RecordId, StringComparer.Ordinal)
                     .ThenBy(edit => edit.Field, StringComparer.Ordinal))
        {
            AppendFingerprintValue(builder, edit.Domain);
            AppendFingerprintValue(builder, edit.RecordId);
            AppendFingerprintValue(builder, edit.Field);
            AppendFingerprintValue(builder, edit.NewValue);
            foreach (var source in edit.Sources
                         .OrderBy(source => source.Layer)
                         .ThenBy(source => source.RelativePath, StringComparer.Ordinal))
            {
                AppendFingerprintValue(builder, source.Layer.ToString());
                AppendFingerprintValue(builder, source.RelativePath);
            }
        }

        // Plan fingerprints are raw SHA-256 digests; only restore payloads use the marker prefix.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void VerifySerializedOutput(
        ZaPokemonDataDocument originalDocument,
        ZaPokemonDataDocument expectedDocument,
        ZaPokemonDataDocument? baseDocument,
        ZaPokemonDataDocument outputDocument,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var originalBySourceIndex = originalDocument.Entries.ToDictionary(entry => entry.SourceIndex);
        var expectedBySourceIndex = expectedDocument.Entries.ToDictionary(entry => entry.SourceIndex);
        var outputBySourceIndex = outputDocument.Entries.ToDictionary(entry => entry.SourceIndex);
        if (originalBySourceIndex.Count != expectedBySourceIndex.Count
            || expectedBySourceIndex.Count != outputBySourceIndex.Count)
        {
            diagnostics.Add(CreateOutputVerificationDiagnostic(
                "Serialized Gift Pokemon output changed the Pokemon data row topology."));
            return;
        }

        var editedSourceIndexes = new HashSet<int>();
        foreach (var edit in session.PendingEdits)
        {
            if (!ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex))
            {
                continue;
            }

            foreach (var target in ZaGiftPokemonWorkflowService.ResolveApplyTargets(originalDocument, giftIndex))
            {
                editedSourceIndexes.Add(target.SourceIndex);
            }
        }

        foreach (var (sourceIndex, original) in originalBySourceIndex)
        {
            if (!outputBySourceIndex.TryGetValue(sourceIndex, out var output))
            {
                diagnostics.Add(CreateOutputVerificationDiagnostic(
                    $"Serialized Gift Pokemon output lost source row {sourceIndex.ToString(CultureInfo.InvariantCulture)}."));
                return;
            }

            if (!string.Equals(original.Id, output.Id, StringComparison.Ordinal)
                || !HaveSameActivationConditions(original.ActivationConditions, output.ActivationConditions))
            {
                diagnostics.Add(CreateOutputVerificationDiagnostic(
                    $"Serialized Gift Pokemon output changed the identity or activation conditions of source row {sourceIndex.ToString(CultureInfo.InvariantCulture)}."));
                return;
            }

            if (!expectedBySourceIndex.TryGetValue(sourceIndex, out var expected)
                || !HaveSameSerializedValues(expected, output))
            {
                diagnostics.Add(CreateOutputVerificationDiagnostic(
                    $"Serialized Gift Pokemon output did not exactly preserve the expected values for source row {sourceIndex.ToString(CultureInfo.InvariantCulture)}."));
                return;
            }

            if (!editedSourceIndexes.Contains(sourceIndex)
                && !HaveSameSerializedValues(original, output))
            {
                diagnostics.Add(CreateOutputVerificationDiagnostic(
                    $"Serialized Gift Pokemon output changed unrelated source row {sourceIndex.ToString(CultureInfo.InvariantCulture)}."));
                return;
            }
        }

        if (baseDocument is null)
        {
            return;
        }

        foreach (var edit in session.PendingEdits.Where(IsVerifiedBaseGiftRowsEdit))
        {
            var selectedEdits = session.PendingEdits.Where(candidate =>
                string.Equals(candidate.Domain, ZaEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal)
                && string.Equals(candidate.RecordId, edit.RecordId, StringComparison.Ordinal));
            if (selectedEdits.Any(candidate => !IsVerifiedBaseGiftRowsEdit(candidate)))
            {
                continue;
            }

            if (!ZaGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
                || !ZaGiftPokemonWorkflowService.TryResolveVanillaRestoreRows(
                    outputDocument,
                    baseDocument,
                    giftIndex,
                    out var restoredRows,
                    out _)
                || restoredRows.Any(pair =>
                    !ZaGiftPokemonWorkflowService.HasSameEditableValues(pair.Active, pair.Vanilla)))
            {
                diagnostics.Add(CreateOutputVerificationDiagnostic(
                    "Serialized Gift Pokemon restore does not exactly match the selected verified vanilla values."));
                return;
            }
        }
    }

    private static ValidationDiagnostic CreateOutputVerificationDiagnostic(string message)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message + " No output was written.",
            ZaEditSessionSupport.GiftPokemonDomain,
            file: $"romfs/{ZaDataPaths.PokemonDataArray}",
            expected: "Only the selected Gift Pokemon-owned fields change and verified restores round-trip exactly");
    }

    private static bool HaveSameSerializedValues(
        ZaPokemonDataEntry left,
        ZaPokemonDataEntry right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && left.DevNo == right.DevNo
            && left.MinLevel == right.MinLevel
            && left.MaxLevel == right.MaxLevel
            && left.Sex == right.Sex
            && left.FormNo == right.FormNo
            && left.Rare == right.Rare
            && left.Tokusei == right.Tokusei
            && left.Seikaku == right.Seikaku
            && left.TalentScale == right.TalentScale
            && left.TalentVNum == right.TalentVNum
            && left.OyabunProbability == right.OyabunProbability
            && left.OyabunAdditionalLevel == right.OyabunAdditionalLevel
            && Equals(left.TalentValue, right.TalentValue)
            && Equals(left.WazaList, right.WazaList)
            && left.HoldItem == right.HoldItem
            && HaveSameActivationConditions(left.ActivationConditions, right.ActivationConditions);
    }

    private static bool HaveSameActivationConditions(
        IReadOnlyList<ZaPokemonDataActivationConditionRecord> left,
        IReadOnlyList<ZaPokemonDataActivationConditionRecord> right)
    {
        return left.Count == right.Count
            && left.Zip(right).All(pair =>
                pair.First.Elements.Count == pair.Second.Elements.Count
                && pair.First.Elements.Zip(pair.Second.Elements).All(elementPair =>
                    elementPair.First.Params.Count == elementPair.Second.Params.Count
                    && elementPair.First.Params.Zip(elementPair.Second.Params).All(paramPair =>
                        string.Equals(paramPair.First.Condition, paramPair.Second.Condition, StringComparison.Ordinal)
                        && paramPair.First.Op == paramPair.Second.Op
                        && paramPair.First.Params.SequenceEqual(
                            paramPair.Second.Params,
                            StringComparer.Ordinal))));
    }

    private static void AppendFingerprintValue(StringBuilder builder, int? value)
    {
        AppendFingerprintValue(
            builder,
            value?.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendFingerprintValue(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-|");
            return;
        }

        builder
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('|');
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
