// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.GameModules;
using KM.SwSh.Items;

namespace KM.SwSh.BattleCafeRewards;

public sealed class SwShBattleCafeRewardsEditSessionService
{
    private const string RecordId = "battle-cafe-rewards";
    private const string RowsField = "rows";
    private const string PayloadVersion = "v1";
    private const int MaximumPayloadLength = 4096;
    private const long MaximumSourceBytesPerFile = 64L * 1024L * 1024L;
    private const long MaximumTotalSourceBytes = 128L * 1024L * 1024L;

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShBattleCafeRewardsWorkflowService workflowService;

    public SwShBattleCafeRewardsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShBattleCafeRewardsWorkflowService? workflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.workflowService = workflowService ?? new SwShBattleCafeRewardsWorkflowService();
    }

    public SwShBattleCafeRewardsEditResult StageRows(
        ProjectPaths paths,
        IReadOnlyList<SwShBattleCafeRewardsRowEdit> edits,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(edits);
        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = workflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "Battle Cafe Rewards requires a Sword or Shield project.",
                SwShBattleCafeRewardsDiagnosticCodes.ProjectUnsupported,
                expected: "Pokemon Sword or Pokemon Shield project"));
            return new SwShBattleCafeRewardsEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit =>
                !string.Equals(
                    edit.Domain,
                    SwShBattleCafeRewardsWorkflowService.EditDomain,
                    StringComparison.Ordinal)))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "Battle Cafe Rewards needs its own edit session before staging.",
                SwShBattleCafeRewardsDiagnosticCodes.SessionInvalid,
                expected: "Battle Cafe Rewards only edit session"));
            return new SwShBattleCafeRewardsEditResult(workflow, currentSession, diagnostics);
        }

        SwShBattleCafeRewardsLoadedSource loaded;
        try
        {
            loaded = workflowService.LoadVerified(project);
        }
        catch (Exception exception) when (IsLoadException(exception))
        {
            diagnostics.Add(SourceDiagnostic(exception));
            return new SwShBattleCafeRewardsEditResult(workflow, currentSession, diagnostics);
        }

        var currentByRow = loaded.Rewards.ToDictionary(reward => reward.RowIndex);
        var merged = new Dictionary<int, SwShBattleCafeRewardsRowEdit>();
        var existingEdits = currentSession.PendingEdits.Where(edit =>
            string.Equals(
                edit.Domain,
                SwShBattleCafeRewardsWorkflowService.EditDomain,
                StringComparison.Ordinal)).ToArray();
        if (existingEdits.Length > 1 || existingEdits.Any(edit =>
                !string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal)
                || !string.Equals(edit.Field, RowsField, StringComparison.Ordinal)))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "The existing Battle Cafe draft does not use the canonical row identity.",
                SwShBattleCafeRewardsDiagnosticCodes.SessionInvalid,
                field: RowsField,
                expected: "One canonical Battle Cafe reward row batch"));
            return new SwShBattleCafeRewardsEditResult(workflow, currentSession, diagnostics);
        }

        var existing = existingEdits.SingleOrDefault();
        if (existing is not null)
        {
            foreach (var pending in Decode(existing.NewValue, diagnostics))
            {
                if (!ExpectedMatchesCurrent(pending, currentByRow))
                {
                    diagnostics.Add(Diagnostic(
                        DiagnosticSeverity.Error,
                        "The existing Battle Cafe draft no longer matches the current reward table.",
                        SwShBattleCafeRewardsDiagnosticCodes.ReviewedPlanStale,
                        field: RowsField,
                        expected: "Draft rows matching the current source"));
                    return new SwShBattleCafeRewardsEditResult(workflow, currentSession, diagnostics);
                }

                merged[pending.RowIndex] = pending;
            }
        }

        if (edits.Count is 0 or > 23
            || edits.Any(edit => edit is null)
            || edits.Select(edit => edit.RowIndex).Distinct().Count() != edits.Count)
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "Battle Cafe row staging requires one to 23 distinct physical rows.",
                SwShBattleCafeRewardsDiagnosticCodes.RowInvalid,
                field: RowsField,
                expected: "Distinct physical rows 1 through 23"));
            return new SwShBattleCafeRewardsEditResult(workflow, currentSession, diagnostics);
        }

        foreach (var edit in edits)
        {
            if (!IsBoundedEdit(edit, loaded.ItemNames)
                || !ExpectedMatchesCurrent(edit, currentByRow))
            {
                diagnostics.Add(Diagnostic(
                    DiagnosticSeverity.Error,
                    "A Battle Cafe row edit is invalid or no longer matches its source row.",
                    SwShBattleCafeRewardsDiagnosticCodes.RowInvalid,
                    field: edit.RowIndex.ToString(CultureInfo.InvariantCulture),
                    expected: "Current physical row and bounded item and percentage values"));
                continue;
            }

            if (IsNoOp(edit))
            {
                merged.Remove(edit.RowIndex);
            }
            else
            {
                merged[edit.RowIndex] = edit;
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShBattleCafeRewardsEditResult(workflow, currentSession, diagnostics);
        }

        var ordered = merged.Values.OrderBy(edit => edit.RowIndex).ToArray();
        var pendingEdits = currentSession.PendingEdits
            .Where(edit => !string.Equals(
                edit.Domain,
                SwShBattleCafeRewardsWorkflowService.EditDomain,
                StringComparison.Ordinal))
            .ToList();
        if (ordered.Length > 0)
        {
            var payload = Encode(ordered);
            var sources = workflowService.GetPlanSources(project, loaded)
                .Append(PendingPayloadSource(payload))
                .ToArray();
            pendingEdits.Add(new PendingEdit(
                SwShBattleCafeRewardsWorkflowService.EditDomain,
                "Stage Battle Cafe reward rows.",
                sources,
                RecordId,
                RowsField,
                payload));
        }

        var updatedSession = currentSession with { PendingEdits = pendingEdits };
        var preview = ApplyDraft(loaded.Rewards, ordered);
        var totals = Totals(preview);
        if (ordered.Length == 0)
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Info,
                "The Battle Cafe draft is clean.",
                SwShBattleCafeRewardsDiagnosticCodes.NoChanges));
        }
        else if (!TotalsAreExact(totals))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Warning,
                "The Battle Cafe draft is staged, but every owner total must equal 100 before review.",
                SwShBattleCafeRewardsDiagnosticCodes.TotalsInvalid,
                field: RowsField,
                expected: "Dwight 100, Bernard 100, and Richard 100"));
        }
        else
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Info,
                "Battle Cafe reward rows are staged for change plan review.",
                SwShBattleCafeRewardsDiagnosticCodes.DraftStaged));
        }

        return new SwShBattleCafeRewardsEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        var diagnostics = new List<ValidationDiagnostic>();
        if (!IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "Battle Cafe Rewards requires a Sword or Shield project.",
                SwShBattleCafeRewardsDiagnosticCodes.ProjectUnsupported));
            return new SwShEditSessionValidation(session, false, diagnostics);
        }

        if (session.PendingEdits.Count != 1
            || !string.Equals(
                session.PendingEdits[0].Domain,
                SwShBattleCafeRewardsWorkflowService.EditDomain,
                StringComparison.Ordinal)
            || !string.Equals(session.PendingEdits[0].RecordId, RecordId, StringComparison.Ordinal)
            || !string.Equals(session.PendingEdits[0].Field, RowsField, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "Battle Cafe Rewards expects exactly one canonical staged row batch.",
                SwShBattleCafeRewardsDiagnosticCodes.SessionInvalid,
                expected: "One Battle Cafe Rewards pending edit"));
            return new SwShEditSessionValidation(session, false, diagnostics);
        }

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        SwShBattleCafeRewardsLoadedSource loaded;
        try
        {
            loaded = workflowService.LoadVerified(project);
        }
        catch (Exception exception) when (IsLoadException(exception))
        {
            diagnostics.Add(SourceDiagnostic(exception));
            return new SwShEditSessionValidation(session, false, diagnostics);
        }

        var edit = session.PendingEdits[0];
        var rows = Decode(edit.NewValue, diagnostics);
        if (rows.Count == 0 || !string.Equals(edit.NewValue, Encode(rows), StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "The staged Battle Cafe row batch is empty or not canonical.",
                SwShBattleCafeRewardsDiagnosticCodes.SessionInvalid,
                field: RowsField,
                expected: "Canonical bounded Battle Cafe row batch"));
        }

        var currentByRow = loaded.Rewards.ToDictionary(reward => reward.RowIndex);
        if (rows.Any(row => !IsBoundedEdit(row, loaded.ItemNames)
            || !ExpectedMatchesCurrent(row, currentByRow)
            || IsNoOp(row)))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "A staged Battle Cafe row is invalid, unchanged, or stale.",
                SwShBattleCafeRewardsDiagnosticCodes.RowInvalid,
                field: RowsField,
                expected: "Changed rows bound to the current physical table"));
        }

        var draft = ApplyDraft(loaded.Rewards, rows);
        var totals = Totals(draft);
        if (!TotalsAreExact(totals))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "Every Battle Cafe owner percentage total must equal 100 before review.",
                SwShBattleCafeRewardsDiagnosticCodes.TotalsInvalid,
                field: RowsField,
                expected: "Dwight 100, Bernard 100, and Richard 100"));
        }

        if (draft.Select(row => row.ItemId).Distinct().Count() != draft.Count)
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "Battle Cafe reward item choices must remain unique across all 23 rows.",
                SwShBattleCafeRewardsDiagnosticCodes.RowInvalid,
                field: RowsField,
                expected: "One unique item per physical reward row"));
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            try
            {
                var transformed = SwShBattleCafeRewardTransform.Apply(
                    loaded.Bytes,
                    loaded.ItemNames,
                    rows.Select(ToTransformEdit).ToArray());
                if (transformed.ChangedRowCount != rows.Count)
                {
                    diagnostics.Add(Diagnostic(
                        DiagnosticSeverity.Error,
                        "The Battle Cafe row batch did not produce the exact reviewed changes.",
                        SwShBattleCafeRewardsDiagnosticCodes.RowInvalid,
                        expected: "One transformed row per staged row"));
                }
            }
            catch (Exception exception) when (exception is
                InvalidDataException or ArgumentException or OverflowException)
            {
                diagnostics.Add(Diagnostic(
                    DiagnosticSeverity.Error,
                    "The staged Battle Cafe batch failed exact transform validation.",
                    SwShBattleCafeRewardsDiagnosticCodes.RowInvalid,
                    expected: "Verified complete 23 row reward table"));
            }
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (!validation.IsValid)
        {
            return new ChangePlan(session.Id, [], diagnostics);
        }

        try
        {
            projectWorkspaceService.ClearMemoryCache();
            var project = projectWorkspaceService.Open(paths);
            var loaded = workflowService.LoadVerified(project);
            var payload = session.PendingEdits.Single().NewValue ?? string.Empty;
            var sources = workflowService.GetPlanSources(project, loaded)
                .Append(PendingPayloadSource(payload))
                .Distinct()
                .ToArray();
            var target = SwShBattleCafeRewardsWorkflowService.ResolveOutputPath(paths);
            if (target is null)
            {
                diagnostics.Add(Diagnostic(
                    DiagnosticSeverity.Error,
                    "The Battle Cafe output target is not safely contained by Output Root.",
                    SwShBattleCafeRewardsDiagnosticCodes.TargetResolutionFailed,
                    file: SwShBattleCafeRewardSourceReader.SourceRelativePath,
                    expected: "Configured output root"));
                return new ChangePlan(session.Id, [], diagnostics);
            }

            var plan = new ChangePlan(
                session.Id,
                [new PlannedFileWrite(
                    SwShBattleCafeRewardSourceReader.SourceRelativePath,
                    sources,
                    File.Exists(target),
                    "Apply the reviewed Battle Cafe reward rows while preserving every unowned AMX cell.")],
                diagnostics);
            return SwShChangePlanSourceGuard.CaptureBounded(
                paths,
                plan,
                MaximumSourceBytesPerFile,
                MaximumTotalSourceBytes);
        }
        catch (Exception exception) when (IsLoadException(exception))
        {
            diagnostics.Add(SourceDiagnostic(exception));
            return new ChangePlan(session.Id, [], diagnostics);
        }
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        projectWorkspaceService.ClearMemoryCache();
        try
        {
            return ApplyChangePlanCore(paths, session, reviewedPlan);
        }
        finally
        {
            projectWorkspaceService.ClearMemoryCache();
        }
    }

    private ApplyResult ApplyChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = NormalizeGuardDiagnostics(currentPlan.Diagnostics).ToList();
        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "The reviewed Battle Cafe change plan is stale.",
                SwShBattleCafeRewardsDiagnosticCodes.ReviewedPlanStale,
                expected: "Current reviewed Battle Cafe change plan"));
        }

        diagnostics.AddRange(NormalizeGuardDiagnostics(
            SwShChangePlanSourceGuard.Validate(paths, reviewedPlan)));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return Result(applyId, appliedAt, currentPlan, [], diagnostics);
        }

        if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                currentPlan,
                out var scope,
                out var acquireDiagnostics))
        {
            return Result(
                applyId,
                appliedAt,
                currentPlan,
                [],
                NormalizeGuardDiagnostics(acquireDiagnostics));
        }

        using var verifiedScope = scope!;
        var snapshotPlan = CreateChangePlan(verifiedScope.ApplyPaths, session);
        if (!verifiedScope.TryPrepareSnapshotPlan(snapshotPlan, out var preparedPlan))
        {
            var stale = NormalizeGuardDiagnostics(preparedPlan.Diagnostics).ToList();
            stale.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "The Battle Cafe sources changed while the apply snapshot was prepared.",
                SwShBattleCafeRewardsDiagnosticCodes.ReviewedPlanStale,
                expected: "Sources matching the reviewed plan"));
            return Result(applyId, appliedAt, currentPlan, [], stale);
        }

        var snapshotResult = ApplyPreparedPlan(
            verifiedScope.ApplyPaths,
            session,
            preparedPlan,
            applyId,
            appliedAt);
        return verifiedScope.Commit(snapshotResult);
    }

    private ApplyResult ApplyPreparedPlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan preparedPlan,
        string applyId,
        DateTimeOffset appliedAt)
    {
        var diagnostics = NormalizeGuardDiagnostics(preparedPlan.Diagnostics).ToList();
        var written = new List<ProjectFileReference>();
        try
        {
            projectWorkspaceService.ClearMemoryCache();
            var project = projectWorkspaceService.Open(paths);
            var loaded = workflowService.LoadVerified(project);
            var rows = Decode(session.PendingEdits.Single().NewValue, diagnostics);
            var transform = SwShBattleCafeRewardTransform.Apply(
                loaded.Bytes,
                loaded.ItemNames,
                rows.Select(ToTransformEdit).ToArray());
            if (transform.ChangedRowCount != rows.Count)
            {
                throw new InvalidDataException(
                    "The prepared Battle Cafe output did not contain every reviewed row change.");
            }

            var target = SwShBattleCafeRewardsWorkflowService.ResolveOutputPath(paths)
                ?? throw new IOException("The Battle Cafe output target is unavailable.");
            WriteAtomically(target, transform.Bytes);
            var readback = File.ReadAllBytes(target);
            var reparsed = SwShBattleCafeRewardSourceReader.Parse(
                readback,
                loaded.ItemNames).Rewards;
            var expected = ApplyDraft(loaded.Rewards, rows);
            if (!NumericRows(reparsed).SequenceEqual(NumericRows(expected)))
            {
                throw new InvalidDataException(
                    "The Battle Cafe output readback did not retain the reviewed table.");
            }

            written.Add(new ProjectFileReference(
                ProjectFileLayer.Generated,
                SwShBattleCafeRewardSourceReader.SourceRelativePath));
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Info,
                "Battle Cafe reward changes were applied.",
                SwShBattleCafeRewardsDiagnosticCodes.Applied,
                file: SwShBattleCafeRewardSourceReader.SourceRelativePath));
        }
        catch (Exception exception) when (exception is
            InvalidDataException or OverflowException or IOException or
            UnauthorizedAccessException or SecurityException)
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "The verified Battle Cafe output could not be prepared or written.",
                exception is InvalidDataException
                    ? SwShBattleCafeRewardsDiagnosticCodes.OutputPreparationFailed
                    : SwShBattleCafeRewardsDiagnosticCodes.OutputWriteFailed,
                file: SwShBattleCafeRewardSourceReader.SourceRelativePath,
                expected: "Writable verified Battle Cafe output"));
        }

        return Result(applyId, appliedAt, preparedPlan, written, diagnostics);
    }

    private static IReadOnlyList<SwShBattleCafeRewardsRowEdit> Decode(
        string? payload,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrEmpty(payload)
            || payload.Length > MaximumPayloadLength
            || !payload.StartsWith(PayloadVersion + '|', StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "The staged Battle Cafe row payload is invalid.",
                SwShBattleCafeRewardsDiagnosticCodes.SessionInvalid,
                field: RowsField,
                expected: "Canonical bounded row payload"));
            return [];
        }

        var rows = new List<SwShBattleCafeRewardsRowEdit>();
        foreach (var encoded in payload[(PayloadVersion.Length + 1)..]
                     .Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var values = encoded.Split(',');
            if (values.Length != 9
                || values.Any(value => !int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _)))
            {
                diagnostics.Add(Diagnostic(
                    DiagnosticSeverity.Error,
                    "A staged Battle Cafe row payload is malformed.",
                    SwShBattleCafeRewardsDiagnosticCodes.SessionInvalid,
                    field: RowsField,
                    expected: "Nine canonical integer values per row"));
                return [];
            }

            var parsed = values.Select(value => int.Parse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture)).ToArray();
            rows.Add(new SwShBattleCafeRewardsRowEdit(
                parsed[0], parsed[1], parsed[2], parsed[3], parsed[4],
                parsed[5], parsed[6], parsed[7], parsed[8]));
        }

        if (rows.Count is 0 or > 23
            || rows.Select(row => row.RowIndex).Distinct().Count() != rows.Count)
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Error,
                "The staged Battle Cafe row payload has an invalid row set.",
                SwShBattleCafeRewardsDiagnosticCodes.SessionInvalid,
                field: RowsField,
                expected: "One to 23 distinct rows"));
            return [];
        }

        return rows.OrderBy(row => row.RowIndex).ToArray();
    }

    private static string Encode(IReadOnlyList<SwShBattleCafeRewardsRowEdit> rows)
    {
        return PayloadVersion + '|' + string.Join(';', rows
            .OrderBy(row => row.RowIndex)
            .Select(row => string.Join(',', new[]
            {
                row.RowIndex,
                row.ExpectedItemId,
                row.ExpectedDwightPercent,
                row.ExpectedBernardPercent,
                row.ExpectedRichardPercent,
                row.ItemId,
                row.DwightPercent,
                row.BernardPercent,
                row.RichardPercent,
            }.Select(value => value.ToString(CultureInfo.InvariantCulture)))));
    }

    private static bool IsBoundedEdit(
        SwShBattleCafeRewardsRowEdit edit,
        IReadOnlyDictionary<int, string> itemNames)
    {
        return edit.RowIndex is >= 1 and <= 23
            && itemNames.ContainsKey(edit.ExpectedItemId)
            && itemNames.ContainsKey(edit.ItemId)
            && edit.ExpectedDwightPercent is >= 0 and <= 100
            && edit.ExpectedBernardPercent is >= 0 and <= 100
            && edit.ExpectedRichardPercent is >= 0 and <= 100
            && edit.DwightPercent is >= 0 and <= 100
            && edit.BernardPercent is >= 0 and <= 100
            && edit.RichardPercent is >= 0 and <= 100;
    }

    private static bool ExpectedMatchesCurrent(
        SwShBattleCafeRewardsRowEdit edit,
        IReadOnlyDictionary<int, SwShBattleCafeRewardEntry> currentByRow)
    {
        return currentByRow.TryGetValue(edit.RowIndex, out var current)
            && current.ItemId == edit.ExpectedItemId
            && current.DwightPercent == edit.ExpectedDwightPercent
            && current.BernardPercent == edit.ExpectedBernardPercent
            && current.RichardPercent == edit.ExpectedRichardPercent;
    }

    private static bool IsNoOp(SwShBattleCafeRewardsRowEdit edit)
    {
        return edit.ExpectedItemId == edit.ItemId
            && edit.ExpectedDwightPercent == edit.DwightPercent
            && edit.ExpectedBernardPercent == edit.BernardPercent
            && edit.ExpectedRichardPercent == edit.RichardPercent;
    }

    private static IReadOnlyList<SwShBattleCafeRewardEntry> ApplyDraft(
        IReadOnlyList<SwShBattleCafeRewardEntry> current,
        IReadOnlyList<SwShBattleCafeRewardsRowEdit> edits)
    {
        var byRow = edits.ToDictionary(edit => edit.RowIndex);
        return current.Select(row => byRow.TryGetValue(row.RowIndex, out var edit)
            ? row with
            {
                ItemId = edit.ItemId,
                DwightPercent = edit.DwightPercent,
                BernardPercent = edit.BernardPercent,
                RichardPercent = edit.RichardPercent,
            }
            : row).ToArray();
    }

    private static SwShBattleCafeRewardsTotals Totals(
        IReadOnlyList<SwShBattleCafeRewardEntry> rows)
    {
        return new SwShBattleCafeRewardsTotals(
            rows.Sum(row => row.DwightPercent),
            rows.Sum(row => row.BernardPercent),
            rows.Sum(row => row.RichardPercent));
    }

    private static bool TotalsAreExact(SwShBattleCafeRewardsTotals totals)
    {
        return totals.DwightPercent == 100
            && totals.BernardPercent == 100
            && totals.RichardPercent == 100;
    }

    private static SwShBattleCafeRewardEdit ToTransformEdit(
        SwShBattleCafeRewardsRowEdit edit)
    {
        return new SwShBattleCafeRewardEdit(
            edit.RowIndex,
            edit.ExpectedItemId,
            edit.ExpectedDwightPercent,
            edit.ExpectedBernardPercent,
            edit.ExpectedRichardPercent,
            edit.ItemId,
            edit.DwightPercent,
            edit.BernardPercent,
            edit.RichardPercent);
    }

    private static IEnumerable<(int Row, int Item, int Dwight, int Bernard, int Richard)>
        NumericRows(IReadOnlyList<SwShBattleCafeRewardEntry> rows)
    {
        return rows.Select(row => (
            row.RowIndex,
            row.ItemId,
            row.DwightPercent,
            row.BernardPercent,
            row.RichardPercent));
    }

    private static ProjectFileReference PendingPayloadSource(string payload)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/battle-cafe-rewards/{hash}");
    }

    private static void WriteAtomically(string target, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(target)
            ?? throw new IOException("The Battle Cafe output directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.km.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static IReadOnlyList<ValidationDiagnostic> NormalizeGuardDiagnostics(
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return diagnostics.Select(diagnostic => diagnostic.Code is not null
            ? diagnostic
            : diagnostic.Domain == "workflow.changePlan"
                ? diagnostic with { Code = SwShBattleCafeRewardsDiagnosticCodes.ReviewedPlanStale }
                : diagnostic).ToArray();
    }

    private static bool IsSupportedGame(ProjectGame? game)
    {
        return game is ProjectGame.Sword or ProjectGame.Shield;
    }

    private static bool IsLoadException(Exception exception)
    {
        return exception is
            SwShBattleCafeItemCatalogException or InvalidDataException or OverflowException or IOException or
            UnauthorizedAccessException or SecurityException;
    }

    private static ValidationDiagnostic SourceDiagnostic(Exception exception)
    {
        var itemCatalog = exception is SwShBattleCafeItemCatalogException;
        return Diagnostic(
            DiagnosticSeverity.Error,
            itemCatalog
                ? "Battle Cafe reward item choices could not be loaded from the current project."
                : exception is FileNotFoundException
                ? "The Battle Cafe reward source is unavailable."
                : "The Battle Cafe reward source does not match the verified layout.",
            itemCatalog
                ? SwShBattleCafeRewardsDiagnosticCodes.ItemCatalogUnavailable
                : exception is FileNotFoundException
                ? SwShBattleCafeRewardsDiagnosticCodes.SourceUnavailable
                : SwShBattleCafeRewardsDiagnosticCodes.SourceUnsupported,
            file: itemCatalog
                ? SwShItemsWorkflowService.ItemDataPath
                : SwShBattleCafeRewardSourceReader.SourceRelativePath,
            expected: itemCatalog
                ? "Readable bounded Sword and Shield item catalog"
                : "Exact bounded 23 row Battle Cafe reward source");
    }

    private static ValidationDiagnostic Diagnostic(
        DiagnosticSeverity severity,
        string message,
        string code,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return SwShBattleCafeRewardsWorkflowService.CreateDiagnostic(
            severity,
            message,
            code,
            file,
            field,
            expected);
    }

    private static ApplyResult Result(
        string applyId,
        DateTimeOffset appliedAt,
        ChangePlan plan,
        IReadOnlyList<ProjectFileReference> written,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            written,
            new WriteManifest(applyId, appliedAt, plan.Writes),
            diagnostics);
    }
}
