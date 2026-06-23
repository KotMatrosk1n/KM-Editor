// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Raids;

internal sealed class SvTeraRaidsEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvTeraRaidsWorkflowService teraRaidsWorkflowService;

    public SvTeraRaidsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvTeraRaidsWorkflowService? teraRaidsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.teraRaidsWorkflowService = teraRaidsWorkflowService ?? new SvTeraRaidsWorkflowService(this.fileSource);
    }

    public SvTeraRaidsEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        string recordId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = teraRaidsWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.TeraRaidsDomain,
                diagnostics))
        {
            return new SvTeraRaidsEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, recordId, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvTeraRaidsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new SvTeraRaidsEditResult(
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits, diagnostics),
            updatedSession,
            diagnostics);
    }

    public SvTeraRaidsEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SvTeraRaidFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = teraRaidsWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.TeraRaidsDomain,
                diagnostics))
        {
            return new SvTeraRaidsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.RecordId) || string.IsNullOrWhiteSpace(update.Field) || update.Value is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Tera Raids batch update is missing a record, field, or value.",
                    SvEditSessionSupport.TeraRaidsDomain,
                    field: "updates",
                    expected: "Complete Tera Raids field update"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(workflow, update.RecordId, update.Field, update.Value, diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            updatedSession = SvEditSessionSupport.ReplacePendingEdit(updatedSession, pendingEdit);
        }

        return new SvTeraRaidsEditResult(
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits, diagnostics),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = teraRaidsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.TeraRaidsDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Tera Raids change is valid.",
                SvEditSessionSupport.TeraRaidsDomain));
        }

        return new SvEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Tera Raids edit before reviewing a change plan.",
                SvEditSessionSupport.TeraRaidsDomain,
                expected: "Pending Tera Raids edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        try
        {
            foreach (var virtualPath in GetTouchedVirtualPaths(session).Order(StringComparer.Ordinal))
            {
                var writeInfo = SvWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    virtualPath,
                    GetSourcesForVirtualPath(session, virtualPath),
                    outputMode);
                writes.Add(new PlannedFileWrite(
                    writeInfo.TargetRelativePath,
                    writeInfo.Sources,
                    writeInfo.ReplacesExistingOutput,
                    $"Apply pending Tera Raids edits to {virtualPath}."));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                var descriptorWriteInfo = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptorWriteInfo.TargetRelativePath,
                    descriptorWriteInfo.Sources,
                    descriptorWriteInfo.ReplacesExistingOutput,
                    "Patch Scarlet/Violet Trinity descriptor for standalone LayeredFS overrides."));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Tera Raids change plan could not resolve the output target: {exception.Message}",
                SvEditSessionSupport.TeraRaidsDomain,
                expected: "Writable output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Count} target files.",
            SvEditSessionSupport.TeraRaidsDomain));

        return new ChangePlan(session.Id, writes, diagnostics);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                SvEditSessionSupport.TeraRaidsDomain,
                expected: "Current reviewed Tera Raids change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var dataSet = teraRaidsWorkflowService.LoadDataSet(project, diagnostics);
            var moveResolver = SvDefaultMoveResolver.Load(project, fileSource, diagnostics);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(dataSet, edit, moveResolver, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            var touchedPaths = GetTouchedVirtualPaths(session).ToHashSet(StringComparer.Ordinal);
            foreach (var raidSource in dataSet.RaidSources)
            {
                if (!touchedPaths.Contains(raidSource.Definition.VirtualPath))
                {
                    continue;
                }

                SvWorkflowFileSource.Write(
                    paths,
                    raidSource.Definition.VirtualPath,
                    SvTeraRaidsWorkflowService.WriteRaidRows(raidSource.Rows),
                    outputMode);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(raidSource.Definition.VirtualPath, outputMode));
            }

            if (touchedPaths.Contains(SvDataPaths.TeraRaidFixedRewardItemArray))
            {
                SvWorkflowFileSource.Write(
                    paths,
                    SvDataPaths.TeraRaidFixedRewardItemArray,
                    SvTeraRaidsWorkflowService.WriteFixedRewardRows(dataSet.FixedRewards),
                    outputMode);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.TeraRaidFixedRewardItemArray, outputMode));
            }

            if (touchedPaths.Contains(SvDataPaths.TeraRaidLotteryRewardItemArray))
            {
                SvWorkflowFileSource.Write(
                    paths,
                    SvDataPaths.TeraRaidLotteryRewardItemArray,
                    SvTeraRaidsWorkflowService.WriteLotteryRewardRows(dataSet.LotteryRewards),
                    outputMode);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.TeraRaidLotteryRewardItemArray, outputMode));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                SvEditSessionSupport.CreateApplyOutputMessage("Tera Raids", outputMode),
                SvEditSessionSupport.TeraRaidsDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Tera Raids output could not be written: {exception.Message}",
                SvEditSessionSupport.TeraRaidsDomain,
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SvTeraRaidsWorkflow workflow,
        string recordId,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedRecordId = recordId.Trim();
        var normalizedField = field.Trim();
        if (!SvTeraRaidsWorkflowService.TryParseRecordId(normalizedRecordId, out var key))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Tera Raids record '{recordId}' is not valid.",
                SvEditSessionSupport.TeraRaidsDomain,
                field: "recordId",
                expected: "Existing Tera Raids record"));
            return null;
        }

        var editableField = SvTeraRaidsWorkflowService.GetEditableField(workflow, normalizedField);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        if (!FieldMatchesRecordKind(normalizedField, key.Kind))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Tera Raids field '{normalizedField}' cannot be applied to record '{normalizedRecordId}'.",
                SvEditSessionSupport.TeraRaidsDomain,
                field: "field",
                expected: "Field matching the selected Tera Raids record type"));
            return null;
        }

        var parsedValue = SvEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            SvEditSessionSupport.TeraRaidsDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        var source = FindSource(workflow, key);
        if (source is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Tera Raids record '{recordId}' is not present in the loaded workflow.",
                SvEditSessionSupport.TeraRaidsDomain,
                field: "recordId",
                expected: "Existing Tera Raids record"));
            return null;
        }

        var summary = $"Set {FormatRecordSummary(workflow, key)} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.";
        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.TeraRaidsDomain,
            summary,
            source,
            normalizedRecordId,
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SvTeraRaidsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.TeraRaidsDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Tera Raids.",
                SvEditSessionSupport.TeraRaidsDomain,
                expected: SvEditSessionSupport.TeraRaidsDomain));
            return;
        }

        if (!SvTeraRaidsWorkflowService.TryParseRecordId(edit.RecordId, out var key)
            || FindSource(workflow, key) is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Tera Raids edit targets a record that is not loaded.",
                SvEditSessionSupport.TeraRaidsDomain,
                field: "recordId",
                expected: "Existing Tera Raids record"));
            return;
        }

        var editableField = SvTeraRaidsWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? string.Empty));
            return;
        }

        if (!FieldMatchesRecordKind(edit.Field ?? string.Empty, key.Kind))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending Tera Raids field '{edit.Field}' does not match its record type.",
                SvEditSessionSupport.TeraRaidsDomain,
                field: "field",
                expected: "Field matching the selected Tera Raids record type"));
            return;
        }

        _ = SvEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            SvEditSessionSupport.TeraRaidsDomain,
            diagnostics);
    }

    private SvTeraRaidsWorkflow OverlayPendingEdits(
        OpenedProject project,
        SvTeraRaidsWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        var pendingEdits = edits
            .Where(edit =>
                string.Equals(edit.Domain, SvEditSessionSupport.TeraRaidsDomain, StringComparison.Ordinal)
                && SvTeraRaidsWorkflowService.TryParseRecordId(edit.RecordId, out _)
                && int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            .ToArray();

        if (pendingEdits.Length == 0)
        {
            return workflow;
        }

        try
        {
            var overlayDiagnostics = new List<ValidationDiagnostic>();
            var dataSet = teraRaidsWorkflowService.LoadDataSet(project, overlayDiagnostics);
            var labels = SvTextLabelLookup.Load(project, fileSource, overlayDiagnostics);
            var abilityResolver = SvTeraRaidsWorkflowService.SvTeraRaidAbilityResolver.Load(
                project,
                fileSource,
                labels,
                overlayDiagnostics);
            var moveResolver = SvDefaultMoveResolver.Load(project, fileSource, overlayDiagnostics);
            foreach (var edit in pendingEdits)
            {
                ApplyEdit(dataSet, edit, moveResolver, overlayDiagnostics);
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

            return teraRaidsWorkflowService.CreateWorkflow(project, labels, abilityResolver, moveResolver, dataSet, workflow.Diagnostics);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            diagnostics?.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Tera Raids pending changes could not be previewed: {exception.Message}",
                SvEditSessionSupport.TeraRaidsDomain,
                expected: "Readable S/V Tera Raids sources"));
            return workflow;
        }
    }

    private static void ApplyEdit(
        SvTeraRaidsWorkflowService.RaidDataSet dataSet,
        PendingEdit edit,
        SvDefaultMoveResolver moveResolver,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.TeraRaidsDomain, StringComparison.Ordinal)
            || !SvTeraRaidsWorkflowService.TryParseRecordId(edit.RecordId, out var key)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Tera Raids edit is not valid for apply.",
                SvEditSessionSupport.TeraRaidsDomain,
                expected: "Valid Tera Raids edit"));
            return;
        }

        if (key.Kind == "raid")
        {
            var source = dataSet.RaidSources.FirstOrDefault(candidate =>
                string.Equals(candidate.Definition.SourceKey, key.SourceKey, StringComparison.Ordinal));
            var row = source?.Rows.ElementAtOrDefault(key.Index)?.Info;
            if (row is null)
            {
                diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Tera raid record '{edit.RecordId}' is not present in the source table.",
                    SvEditSessionSupport.TeraRaidsDomain,
                    field: "recordId",
                    expected: "Existing source Tera raid row"));
                return;
            }

            if (string.Equals(edit.Field, SvTeraRaidsWorkflowService.FixedRewardTableField, StringComparison.Ordinal))
            {
                var rewardTable = dataSet.FixedRewards.ElementAtOrDefault(value);
                if (rewardTable is null)
                {
                    diagnostics.Add(CreateMissingRewardTableDiagnostic(edit.RecordId ?? string.Empty, edit.Field, value));
                    return;
                }

                row.DropTableFix = rewardTable.TableName;
                return;
            }

            if (string.Equals(edit.Field, SvTeraRaidsWorkflowService.LotteryRewardTableField, StringComparison.Ordinal))
            {
                var rewardTable = dataSet.LotteryRewards.ElementAtOrDefault(value);
                if (rewardTable is null)
                {
                    diagnostics.Add(CreateMissingRewardTableDiagnostic(edit.RecordId ?? string.Empty, edit.Field, value));
                    return;
                }

                row.DropTableRandom = rewardTable.TableName;
                return;
            }

            SvTeraRaidsWorkflowService.ApplyRaidField(row, edit.Field, value, moveResolver);
            return;
        }

        if (key.Kind == "fixed")
        {
            var row = dataSet.FixedRewards.ElementAtOrDefault(key.Index);
            if (row is null || key.Slot is not { } slot || slot >= row.Rewards.Length)
            {
                diagnostics.Add(CreateMissingRewardDiagnostic(edit.RecordId ?? string.Empty));
                return;
            }

            SvTeraRaidsWorkflowService.ApplyFixedRewardField(row.EnsureReward(slot), edit.Field, value);
            return;
        }

        if (key.Kind == "lottery")
        {
            var row = dataSet.LotteryRewards.ElementAtOrDefault(key.Index);
            if (row is null || key.Slot is not { } slot || slot >= row.Rewards.Length)
            {
                diagnostics.Add(CreateMissingRewardDiagnostic(edit.RecordId ?? string.Empty));
                return;
            }

            SvTeraRaidsWorkflowService.ApplyLotteryRewardField(row.EnsureReward(slot), edit.Field, value);
        }
    }

    private static IReadOnlyList<string> GetTouchedVirtualPaths(EditSession session)
    {
        return session.PendingEdits
            .Where(edit => string.Equals(edit.Domain, SvEditSessionSupport.TeraRaidsDomain, StringComparison.Ordinal))
            .SelectMany(edit => edit.Sources)
            .Select(source => NormalizeSourceVirtualPath(source.RelativePath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ProjectFileReference> GetSourcesForVirtualPath(EditSession session, string virtualPath)
    {
        return session.PendingEdits
            .Where(edit => string.Equals(edit.Domain, SvEditSessionSupport.TeraRaidsDomain, StringComparison.Ordinal))
            .SelectMany(edit => edit.Sources)
            .Where(source => string.Equals(NormalizeSourceVirtualPath(source.RelativePath), virtualPath, StringComparison.Ordinal))
            .Distinct()
            .ToArray();
    }

    private static string NormalizeSourceVirtualPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            ? normalized["romfs/".Length..]
            : normalized;
    }

    private static bool FieldMatchesRecordKind(string field, string kind)
    {
        return kind switch
        {
            "raid" => SvTeraRaidsWorkflowService.IsRaidField(field),
            "fixed" => SvTeraRaidsWorkflowService.IsFixedRewardField(field),
            "lottery" => SvTeraRaidsWorkflowService.IsLotteryRewardField(field),
            _ => false,
        };
    }

    private static ProjectFileReference? FindSource(
        SvTeraRaidsWorkflow workflow,
        SvTeraRaidsWorkflowService.TeraRaidRecordKey key)
    {
        if (key.Kind == "raid")
        {
            var recordId = SvTeraRaidsWorkflowService.CreateRaidRecordId(key.SourceKey, key.Index);
            var raid = workflow.Raids.FirstOrDefault(candidate => string.Equals(candidate.RecordId, recordId, StringComparison.Ordinal));
            return raid is null
                ? null
                : new ProjectFileReference(raid.Provenance.SourceLayer, raid.Provenance.SourceFile);
        }

        if (key.Kind == "fixed")
        {
            var reward = workflow.FixedRewardTables
                .SelectMany(table => table.Rewards)
                .FirstOrDefault(candidate => string.Equals(candidate.RecordId, SvTeraRaidsWorkflowService.CreateRewardRecordId("fixed", key.Index, key.Slot ?? -1), StringComparison.Ordinal));
            return reward is null
                ? null
                : new ProjectFileReference(reward.Provenance.SourceLayer, reward.Provenance.SourceFile);
        }

        if (key.Kind == "lottery")
        {
            var reward = workflow.LotteryRewardTables
                .SelectMany(table => table.Rewards)
                .FirstOrDefault(candidate => string.Equals(candidate.RecordId, SvTeraRaidsWorkflowService.CreateRewardRecordId("lottery", key.Index, key.Slot ?? -1), StringComparison.Ordinal));
            return reward is null
                ? null
                : new ProjectFileReference(reward.Provenance.SourceLayer, reward.Provenance.SourceFile);
        }

        return null;
    }

    private static string FormatRecordSummary(
        SvTeraRaidsWorkflow workflow,
        SvTeraRaidsWorkflowService.TeraRaidRecordKey key)
    {
        if (key.Kind == "raid")
        {
            var recordId = SvTeraRaidsWorkflowService.CreateRaidRecordId(key.SourceKey, key.Index);
            var raid = workflow.Raids.FirstOrDefault(candidate => string.Equals(candidate.RecordId, recordId, StringComparison.Ordinal));
            return raid is null
                ? recordId
                : $"{raid.Region} {raid.StarLabel} {raid.Species}";
        }

        return $"{key.Kind} reward {key.Index.ToString(CultureInfo.InvariantCulture)} slot {(key.Slot ?? 0).ToString(CultureInfo.InvariantCulture)}";
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Tera Raids field '{field}' is not supported by Scarlet/Violet Tera Raids yet.",
            SvEditSessionSupport.TeraRaidsDomain,
            field: "field",
            expected: "Supported S/V Tera Raids field");
    }

    private static ValidationDiagnostic CreateMissingRewardDiagnostic(string recordId)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Tera raid reward record '{recordId}' is not present in the source table.",
            SvEditSessionSupport.TeraRaidsDomain,
            field: "recordId",
            expected: "Existing source Tera raid reward row");
    }

    private static ValidationDiagnostic CreateMissingRewardTableDiagnostic(string recordId, string? field, int tableIndex)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Tera raid record '{recordId}' references reward table index {tableIndex.ToString(CultureInfo.InvariantCulture)}, but that table is not loaded.",
            SvEditSessionSupport.TeraRaidsDomain,
            field: field,
            expected: "Loaded Tera raid reward table");
    }
}
