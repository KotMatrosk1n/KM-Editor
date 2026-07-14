// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Moves;

public sealed class SwShMovesEditSessionService
{
    private const string MovesEditDomain = "workflow.moves";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShMovesWorkflowService movesWorkflowService;

    public SwShMovesEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShMovesWorkflowService? movesWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.movesWorkflowService = movesWorkflowService ?? new SwShMovesWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShMovesEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int moveId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = movesWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditMoves(project, workflow, diagnostics))
        {
            return new SwShMovesEditResult(workflow, currentSession, diagnostics);
        }

        var selectedMove = workflow.Moves.FirstOrDefault(move => move.MoveId == moveId);
        if (selectedMove is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} is not present in the loaded Moves Data workflow.",
                field: "moveId",
                expected: "Existing move record"));
            return new SwShMovesEditResult(workflow, currentSession, diagnostics);
        }

        var baselineMove = loadedWorkflow.Moves.First(move => move.MoveId == moveId);
        var pendingEdit = CreatePendingEdit(
            selectedMove,
            baselineMove,
            field,
            value,
            validateImmediatePairs: true,
            diagnostics: diagnostics);
        if (pendingEdit is null)
        {
            return new SwShMovesEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SetPendingMoveEdit(currentSession, pendingEdit, baselineMove);

        return new SwShMovesEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShMovesEditResult UpdateFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShMoveFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = movesWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditMoves(project, workflow, diagnostics))
        {
            return new SwShMovesEditResult(workflow, currentSession, diagnostics);
        }

        var candidateSession = currentSession;
        foreach (var update in updates)
        {
            if (update is null
                || string.IsNullOrWhiteSpace(update.Field)
                || update.Value is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Move batch update is missing a field or value.",
                    field: "updates",
                    expected: "Complete move field update"));
                continue;
            }

            var effectiveWorkflow = OverlayPendingEdits(loadedWorkflow, candidateSession.PendingEdits);
            var selectedMove = effectiveWorkflow.Moves.FirstOrDefault(move => move.MoveId == update.MoveId);
            var baselineMove = loadedWorkflow.Moves.FirstOrDefault(move => move.MoveId == update.MoveId);
            if (selectedMove is null || baselineMove is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Move {update.MoveId} is not present in the loaded Moves Data workflow.",
                    field: "moveId",
                    expected: "Existing move record"));
                continue;
            }

            var pendingEdit = CreatePendingEdit(
                selectedMove,
                baselineMove,
                update.Field,
                update.Value,
                validateImmediatePairs: false,
                diagnostics: diagnostics);
            if (pendingEdit is not null)
            {
                candidateSession = SetPendingMoveEdit(candidateSession, pendingEdit, baselineMove);
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShMovesEditResult(workflow, currentSession, diagnostics);
        }

        ValidatePendingPairs(loadedWorkflow, candidateSession.PendingEdits, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShMovesEditResult(workflow, currentSession, diagnostics);
        }

        return new SwShMovesEditResult(
            OverlayPendingEdits(loadedWorkflow, candidateSession.PendingEdits),
            candidateSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = movesWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditMoves(project, workflow, diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        ValidateUniquePendingMoveEdits(session.PendingEdits, diagnostics);
        ValidatePendingPairs(workflow, session.PendingEdits, diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending move change is valid."));
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
                "Create a pending Moves Data edit before reviewing a change plan.",
                expected: "Pending move edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = movesWorkflowService.Load(project);
        var writes = CreatePlannedWrites(workflow, paths, session.PendingEdits, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Change plan preview contains {writes.Count} target file{(writes.Count == 1 ? string.Empty : "s")}."));

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
                expected: "Current reviewed Moves Data change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = movesWorkflowService.Load(project);
        var pendingOutputs = new List<MoveOutput>();

        foreach (var editGroup in session.PendingEdits.GroupBy(edit => GetTargetRelativePath(workflow, edit), StringComparer.OrdinalIgnoreCase))
        {
            var targetRelativePath = editGroup.Key;
            if (string.IsNullOrWhiteSpace(targetRelativePath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending move edit does not include a valid target source file.",
                    expected: "Loaded move data source"));
                continue;
            }

            var source = SwShMovesWorkflowService.ResolveMoveDataSource(project, targetRelativePath);
            if (source is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Moves Data apply could not resolve source file '{targetRelativePath}'.",
                    file: targetRelativePath,
                    expected: "Loaded Sword/Shield move data source"));
                continue;
            }

            var targetPath = ResolveOutputPath(paths, source.GraphEntry.RelativePath, diagnostics);
            if (targetPath is null)
            {
                continue;
            }

            try
            {
                var moveFile = SwShMoveDataFile.Parse(File.ReadAllBytes(source.AbsolutePath));
                var editedRecord = ApplyMoveEdits(moveFile.Record, editGroup, diagnostics);

                if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
                {
                    pendingOutputs.Add(new MoveOutput(
                        source.GraphEntry.RelativePath,
                        targetPath,
                        moveFile.WriteEdited(editedRecord)));
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Moves Data source file could not be decoded or edited safely: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Sword/Shield move data file with a losslessly editable source layout"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Moves Data source file could not be read: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Readable move data source file"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Moves Data source file could not be read: {exception.Message}",
                    file: source.GraphEntry.RelativePath,
                    expected: "Readable move data source file"));
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        if (pendingOutputs.Count > 0
            && TryWriteOutputsTransactionally(pendingOutputs, applyId, diagnostics))
        {
            writtenFiles.AddRange(pendingOutputs.Select(output =>
                new ProjectFileReference(ProjectFileLayer.Generated, output.RelativePath)));
        }

        if (writtenFiles.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Moves Data change plan to the configured LayeredFS output root."));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static bool TryWriteOutputsTransactionally(
        IReadOnlyList<MoveOutput> outputs,
        string transactionId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var staged = new List<StagedMoveOutput>();
        MoveOutput? currentOutput = null;

        try
        {
            foreach (var output in outputs)
            {
                currentOutput = output;
                Directory.CreateDirectory(Path.GetDirectoryName(output.AbsolutePath)!);
                var temporaryPath = $"{output.AbsolutePath}.{transactionId}.tmp";
                var backupPath = $"{output.AbsolutePath}.{transactionId}.bak";
                staged.Add(new StagedMoveOutput(
                    output,
                    temporaryPath,
                    backupPath,
                    File.Exists(output.AbsolutePath)));
                File.WriteAllBytes(temporaryPath, output.Contents);
            }
        }
        catch (IOException exception)
        {
            CleanupTransactionArtifacts(
                staged,
                diagnostics,
                reportFailures: true,
                preserveUnrestoredBackups: false);
            AddTransactionalWriteDiagnostic(
                currentOutput,
                exception,
                "during staging; output target files were not changed",
                diagnostics);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            CleanupTransactionArtifacts(
                staged,
                diagnostics,
                reportFailures: true,
                preserveUnrestoredBackups: false);
            AddTransactionalWriteDiagnostic(
                currentOutput,
                exception,
                "during staging; output target files were not changed",
                diagnostics);
            return false;
        }

        try
        {
            foreach (var output in staged)
            {
                currentOutput = output.Output;
                if (output.OriginalExisted)
                {
                    File.Move(output.Output.AbsolutePath, output.BackupPath);
                    output.OriginalMoved = true;
                }

                File.Move(output.TemporaryPath, output.Output.AbsolutePath);
                output.NewInstalled = true;
            }
        }
        catch (IOException exception)
        {
            var rollbackSucceeded = RollBackTransaction(staged, diagnostics);
            CleanupTransactionArtifacts(
                staged,
                diagnostics,
                reportFailures: true,
                preserveUnrestoredBackups: true);
            AddTransactionalWriteDiagnostic(
                currentOutput,
                exception,
                rollbackSucceeded
                    ? "during promotion; all promoted output target changes were rolled back"
                    : "during promotion, and rollback was incomplete; recovery artifacts were preserved where possible",
                diagnostics);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            var rollbackSucceeded = RollBackTransaction(staged, diagnostics);
            CleanupTransactionArtifacts(
                staged,
                diagnostics,
                reportFailures: true,
                preserveUnrestoredBackups: true);
            AddTransactionalWriteDiagnostic(
                currentOutput,
                exception,
                rollbackSucceeded
                    ? "during promotion; all promoted output target changes were rolled back"
                    : "during promotion, and rollback was incomplete; recovery artifacts were preserved where possible",
                diagnostics);
            return false;
        }

        CleanupTransactionArtifacts(
            staged,
            diagnostics,
            reportFailures: true,
            preserveUnrestoredBackups: false);
        return true;
    }

    private static bool RollBackTransaction(
        IReadOnlyList<StagedMoveOutput> staged,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var succeeded = true;
        foreach (var output in staged.Reverse())
        {
            try
            {
                if (output.NewInstalled && File.Exists(output.Output.AbsolutePath))
                {
                    File.Delete(output.Output.AbsolutePath);
                }

                output.NewInstalled = false;
                if (output.OriginalMoved && !File.Exists(output.BackupPath))
                {
                    throw new IOException("The original output backup is missing.");
                }

                if (output.OriginalMoved)
                {
                    File.Move(output.BackupPath, output.Output.AbsolutePath);
                    output.OriginalMoved = false;
                }
            }
            catch (IOException exception)
            {
                succeeded = false;
                AddRollbackDiagnostic(output.Output, exception, diagnostics);
            }
            catch (UnauthorizedAccessException exception)
            {
                succeeded = false;
                AddRollbackDiagnostic(output.Output, exception, diagnostics);
            }
        }

        return succeeded;
    }

    private static bool CleanupTransactionArtifacts(
        IEnumerable<StagedMoveOutput> staged,
        ICollection<ValidationDiagnostic> diagnostics,
        bool reportFailures,
        bool preserveUnrestoredBackups)
    {
        var succeeded = true;
        foreach (var output in staged)
        {
            var paths = new[]
            {
                (Path: output.TemporaryPath, IsBackup: false),
                (Path: output.BackupPath, IsBackup: true),
            };
            foreach (var artifact in paths)
            {
                if (preserveUnrestoredBackups && artifact.IsBackup && output.OriginalMoved)
                {
                    continue;
                }

                try
                {
                    if (File.Exists(artifact.Path))
                    {
                        File.Delete(artifact.Path);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    succeeded = false;
                    if (reportFailures)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Warning,
                            $"Moves Data transaction cleanup could not remove a temporary or backup file: {exception.Message}",
                            file: output.Output.RelativePath,
                            expected: "Removable transaction artifact"));
                    }
                }
            }
        }

        return succeeded;
    }

    private static void AddTransactionalWriteDiagnostic(
        MoveOutput? output,
        Exception exception,
        string outcome,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Moves Data output transaction failed {outcome}: {exception.Message}",
            file: output?.RelativePath,
            expected: "Writable output root"));
    }

    private static void AddRollbackDiagnostic(
        MoveOutput output,
        Exception exception,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Moves Data output rollback could not restore a target: {exception.Message}",
            file: output.RelativePath,
            expected: "Restored pre-apply output"));
    }

    private static bool CanEditMoves(
        OpenedProject project,
        SwShMovesWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Moves Data edit sessions require valid base paths and a valid output root.",
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
        SwShMovesWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, MovesEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by the Moves Data workflow.",
                expected: MovesEditDomain));
            return;
        }

        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || workflow.Moves.All(move => move.MoveId != moveId))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending move edit targets a record that is not loaded.",
                field: "moveId",
                expected: "Existing move record"));
            return;
        }

        TryParseEditableValue(edit.Field, edit.NewValue, diagnostics);
    }

    private static void ValidateUniquePendingMoveEdits(
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var seenFields = new HashSet<(int MoveId, string Field)>();
        foreach (var edit in edits.Where(edit =>
            string.Equals(edit.Domain, MovesEditDomain, StringComparison.Ordinal)))
        {
            if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
                || string.IsNullOrWhiteSpace(edit.Field))
            {
                continue;
            }

            var key = (moveId, edit.Field);
            if (seenFields.Add(key))
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Move {moveId} has more than one pending edit for field '{edit.Field}'.",
                field: edit.Field,
                expected: "One pending value per move field"));
        }
    }

    private static void ValidatePendingPairs(
        SwShMovesWorkflow workflow,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var editedMoveIds = edits
            .Where(edit => string.Equals(edit.Domain, MovesEditDomain, StringComparison.Ordinal))
            .Select(edit => int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
                ? moveId
                : (int?)null)
            .Where(moveId => moveId is not null)
            .Select(moveId => moveId!.Value)
            .Distinct()
            .ToHashSet();

        if (editedMoveIds.Count == 0)
        {
            return;
        }

        var overlaidWorkflow = OverlayPendingEdits(workflow, edits);
        foreach (var move in overlaidWorkflow.Moves.Where(move => editedMoveIds.Contains(move.MoveId)))
        {
            var baselineMove = workflow.Moves.First(candidate => candidate.MoveId == move.MoveId);
            ValidatePairIfChanged(
                move.HitMin,
                move.HitMax,
                baselineMove.HitMin,
                baselineMove.HitMax,
                "hits",
                SwShMovesWorkflowService.HitMinField,
                diagnostics,
                move.MoveId);
            ValidatePairIfChanged(
                move.TurnMin,
                move.TurnMax,
                baselineMove.TurnMin,
                baselineMove.TurnMax,
                "inflict turns",
                SwShMovesWorkflowService.TurnMinField,
                diagnostics,
                move.MoveId);
        }
    }

    private static PendingEdit? CreatePendingEdit(
        SwShMoveRecord selectedMove,
        SwShMoveRecord baselineMove,
        string field,
        string value,
        bool validateImmediatePairs,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var preservedValues = new HashSet<int>();
        if (GetMoveFieldValue(selectedMove, normalizedField) is { } selectedValue)
        {
            preservedValues.Add(selectedValue);
        }

        if (GetMoveFieldValue(baselineMove, normalizedField) is { } baselineValue)
        {
            preservedValues.Add(baselineValue);
        }

        var parsedValue = TryParseEditableValue(normalizedField, value, diagnostics, preservedValues);
        if (parsedValue is null)
        {
            return null;
        }

        if (validateImmediatePairs
            && !ValidateImmediatePairs(
                selectedMove,
                baselineMove,
                normalizedField,
                parsedValue.Value,
                diagnostics))
        {
            return null;
        }

        if (normalizedField == SwShMovesWorkflowService.CanUseMoveField
            && !baselineMove.CanUseMove
            && parsedValue.Value != 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Enabling a base-disabled move does not restore missing animations, effects, or other game resources. Asset-verify the move in game before relying on it.",
                field: SwShMovesWorkflowService.CanUseMoveField,
                expected: "Asset-verified move resources and in-game behavior"));
        }

        return new PendingEdit(
            MovesEditDomain,
            CreatePendingEditSummary(selectedMove, normalizedField, parsedValue.Value),
            [new ProjectFileReference(selectedMove.Provenance.SourceLayer, selectedMove.Provenance.SourceFile)],
            RecordId: selectedMove.MoveId.ToString(CultureInfo.InvariantCulture),
            Field: normalizedField,
            NewValue: parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool ValidateImmediatePairs(
        SwShMoveRecord selectedMove,
        SwShMoveRecord baselineMove,
        string field,
        int value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var hitMin = field == SwShMovesWorkflowService.HitMinField ? value : selectedMove.HitMin;
        var hitMax = field == SwShMovesWorkflowService.HitMaxField ? value : selectedMove.HitMax;
        var turnMin = field == SwShMovesWorkflowService.TurnMinField ? value : selectedMove.TurnMin;
        var turnMax = field == SwShMovesWorkflowService.TurnMaxField ? value : selectedMove.TurnMax;

        if (!ValidatePairIfChanged(
                hitMin,
                hitMax,
                baselineMove.HitMin,
                baselineMove.HitMax,
                "hits",
                SwShMovesWorkflowService.HitMinField,
                diagnostics))
        {
            return false;
        }

        if (!ValidatePairIfChanged(
                turnMin,
                turnMax,
                baselineMove.TurnMin,
                baselineMove.TurnMax,
                "inflict turns",
                SwShMovesWorkflowService.TurnMinField,
                diagnostics))
        {
            return false;
        }

        return true;
    }

    private static bool ValidatePairIfChanged(
        int minimum,
        int maximum,
        int baselineMinimum,
        int baselineMaximum,
        string label,
        string field,
        ICollection<ValidationDiagnostic> diagnostics,
        int? moveId = null)
    {
        return minimum == baselineMinimum && maximum == baselineMaximum
            || ValidatePair(minimum, maximum, label, field, diagnostics, moveId);
    }

    private static bool ValidatePair(
        int minimum,
        int maximum,
        string label,
        string field,
        ICollection<ValidationDiagnostic> diagnostics,
        int? moveId = null)
    {
        var movePrefix = moveId is null ? "Move" : $"Move {moveId.Value}";
        if ((minimum == 0) != (maximum == 0))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{movePrefix} {label} must use either the 0/0 sentinel or two nonzero values.",
                field: field,
                expected: $"0/0 or nonzero minimum {label} less than or equal to maximum"));
            return false;
        }

        if (minimum > maximum)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{movePrefix} minimum {label} cannot be greater than maximum {label}.",
                field: field,
                expected: $"Minimum {label} less than or equal to maximum"));
            return false;
        }

        return true;
    }

    private static int? TryParseEditableValue(
        string? field,
        string? value,
        ICollection<ValidationDiagnostic> diagnostics,
        IReadOnlySet<int>? preservedValues = null)
    {
        var editableField = SwShMovesWorkflowService.GetEditableField(field);
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
                expected: $"Safe move {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        var isPreservedValue = preservedValues?.Contains(parsedValue.Value) == true;
        if (!isPreservedValue
            && (parsedValue.Value < (editableField.MinimumValue ?? int.MinValue)
                || parsedValue.Value > (editableField.MaximumValue ?? int.MaxValue)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must be between {editableField.MinimumValue} and {editableField.MaximumValue}.",
                field: editableField.Field,
                expected: $"Safe move {editableField.Label.ToLowerInvariant()}"));
            return null;
        }

        if (!isPreservedValue
            && editableField.Options.Count > 0
            && editableField.Options.All(option => option.Value != parsedValue.Value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"{editableField.Label} must use a supported Sword/Shield value.",
                field: editableField.Field,
                expected: string.Join(", ", editableField.Options.Select(option => option.Value))));
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
            return true;
        }

        return false;
    }

    private static EditSession SetPendingMoveEdit(
        EditSession session,
        PendingEdit pendingEdit,
        SwShMoveRecord baselineMove)
    {
        var pendingEdits = session.PendingEdits
            .Where(edit => !IsSameMoveEdit(edit, pendingEdit))
            .ToList();

        if (!int.TryParse(pendingEdit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || GetMoveFieldValue(baselineMove, pendingEdit.Field!) != value)
        {
            pendingEdits.Add(pendingEdit);
        }

        return session with { PendingEdits = pendingEdits.ToArray() };
    }

    private static bool IsSameMoveEdit(PendingEdit candidate, PendingEdit pendingEdit)
    {
        return string.Equals(candidate.Domain, pendingEdit.Domain, StringComparison.Ordinal)
            && string.Equals(candidate.RecordId, pendingEdit.RecordId, StringComparison.Ordinal)
            && string.Equals(candidate.Field, pendingEdit.Field, StringComparison.Ordinal);
    }

    private static SwShMovesWorkflow OverlayPendingEdits(
        SwShMovesWorkflow workflow,
        IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;

        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShMovesWorkflow OverlayPendingEdit(SwShMovesWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, MovesEditDomain, StringComparison.Ordinal)
            || !int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            || TryParseEditableValue(edit.Field, edit.NewValue, new List<ValidationDiagnostic>()) is not { } value)
        {
            return workflow;
        }

        var typeName = string.Equals(edit.Field, SwShMovesWorkflowService.TypeField, StringComparison.Ordinal)
            ? ResolveTypeName(workflow, value)
            : null;
        var moves = workflow.Moves
            .Select(move => move.MoveId == moveId ? OverlayMoveField(move, edit.Field!, value, typeName) : move)
            .ToArray();

        return workflow with
        {
            Moves = moves,
            Stats = workflow.Stats with
            {
                TotalMoveCount = moves.Length,
                EnabledMoveCount = moves.Count(move => move.CanUseMove),
                ActiveFlagCount = moves.Sum(move => move.Flags.Count(flag => flag.Enabled)),
            },
        };
    }

    private static SwShMoveRecord OverlayMoveField(
        SwShMoveRecord move,
        string field,
        int value,
        string? typeName)
    {
        return field switch
        {
            SwShMovesWorkflowService.CanUseMoveField => move with { CanUseMove = value != 0 },
            SwShMovesWorkflowService.TypeField => move with { Type = value, TypeName = typeName ?? FormatType(value) },
            SwShMovesWorkflowService.QualityField => move with { Quality = value },
            SwShMovesWorkflowService.CategoryField => move with { Category = value, CategoryName = FormatCategory(value) },
            SwShMovesWorkflowService.PowerField => move with { Power = value },
            SwShMovesWorkflowService.AccuracyField => move with { Accuracy = value },
            SwShMovesWorkflowService.PpField => move with { PP = value },
            SwShMovesWorkflowService.PriorityField => move with { Priority = value },
            SwShMovesWorkflowService.CritStageField => move with { CritStage = value },
            SwShMovesWorkflowService.MaxMovePowerField => move with { MaxMovePower = value },
            SwShMovesWorkflowService.TargetField => move with { Target = value, TargetName = FormatTarget(value) },
            SwShMovesWorkflowService.HitMinField => move with { HitMin = value },
            SwShMovesWorkflowService.HitMaxField => move with { HitMax = value },
            SwShMovesWorkflowService.TurnMinField => move with { TurnMin = value },
            SwShMovesWorkflowService.TurnMaxField => move with { TurnMax = value },
            SwShMovesWorkflowService.InflictField => move with { Inflict = value, InflictName = FormatInflict(value) },
            SwShMovesWorkflowService.InflictPercentField => move with { InflictPercent = value },
            SwShMovesWorkflowService.RawInflictCountField => move with { RawInflictCount = value },
            SwShMovesWorkflowService.FlinchField => move with { Flinch = value },
            SwShMovesWorkflowService.EffectSequenceField => move with { EffectSequence = value },
            SwShMovesWorkflowService.RecoilField => move with { Recoil = value },
            SwShMovesWorkflowService.RawHealingField => move with { RawHealing = value },
            SwShMovesWorkflowService.Stat1Field => move with { StatChanges = OverlayStatChange(move.StatChanges, slot: 1, stat => stat with { Stat = value, StatName = FormatStat(value) }) },
            SwShMovesWorkflowService.Stat1StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, slot: 1, stat => stat with { Stage = value }) },
            SwShMovesWorkflowService.Stat1PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, slot: 1, stat => stat with { Percent = value }) },
            SwShMovesWorkflowService.Stat2Field => move with { StatChanges = OverlayStatChange(move.StatChanges, slot: 2, stat => stat with { Stat = value, StatName = FormatStat(value) }) },
            SwShMovesWorkflowService.Stat2StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, slot: 2, stat => stat with { Stage = value }) },
            SwShMovesWorkflowService.Stat2PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, slot: 2, stat => stat with { Percent = value }) },
            SwShMovesWorkflowService.Stat3Field => move with { StatChanges = OverlayStatChange(move.StatChanges, slot: 3, stat => stat with { Stat = value, StatName = FormatStat(value) }) },
            SwShMovesWorkflowService.Stat3StageField => move with { StatChanges = OverlayStatChange(move.StatChanges, slot: 3, stat => stat with { Stage = value }) },
            SwShMovesWorkflowService.Stat3PercentField => move with { StatChanges = OverlayStatChange(move.StatChanges, slot: 3, stat => stat with { Percent = value }) },
            _ when IsFlagField(field) => move with { Flags = OverlayFlag(move.Flags, field, value != 0) },
            _ => move,
        };
    }

    private static string ResolveTypeName(SwShMovesWorkflow workflow, int type)
    {
        var loadedName = workflow.Moves
            .Where(move => move.Type == type && !string.IsNullOrWhiteSpace(move.TypeName))
            .Select(move => move.TypeName)
            .FirstOrDefault();
        if (loadedName is not null)
        {
            return loadedName;
        }

        var optionLabel = workflow.EditableFields
            .FirstOrDefault(field => field.Field == SwShMovesWorkflowService.TypeField)?
            .Options
            .FirstOrDefault(option => option.Value == type)?
            .Label;
        var numericPrefix = $"{type:000} ";
        return optionLabel?.StartsWith(numericPrefix, StringComparison.Ordinal) == true
            ? optionLabel[numericPrefix.Length..]
            : FormatType(type);
    }

    private static int? GetMoveFieldValue(SwShMoveRecord move, string field)
    {
        return field switch
        {
            SwShMovesWorkflowService.CanUseMoveField => move.CanUseMove ? 1 : 0,
            SwShMovesWorkflowService.TypeField => move.Type,
            SwShMovesWorkflowService.QualityField => move.Quality,
            SwShMovesWorkflowService.CategoryField => move.Category,
            SwShMovesWorkflowService.PowerField => move.Power,
            SwShMovesWorkflowService.AccuracyField => move.Accuracy,
            SwShMovesWorkflowService.PpField => move.PP,
            SwShMovesWorkflowService.PriorityField => move.Priority,
            SwShMovesWorkflowService.CritStageField => move.CritStage,
            SwShMovesWorkflowService.MaxMovePowerField => move.MaxMovePower,
            SwShMovesWorkflowService.TargetField => move.Target,
            SwShMovesWorkflowService.HitMinField => move.HitMin,
            SwShMovesWorkflowService.HitMaxField => move.HitMax,
            SwShMovesWorkflowService.TurnMinField => move.TurnMin,
            SwShMovesWorkflowService.TurnMaxField => move.TurnMax,
            SwShMovesWorkflowService.InflictField => move.Inflict,
            SwShMovesWorkflowService.InflictPercentField => move.InflictPercent,
            SwShMovesWorkflowService.RawInflictCountField => move.RawInflictCount,
            SwShMovesWorkflowService.FlinchField => move.Flinch,
            SwShMovesWorkflowService.EffectSequenceField => move.EffectSequence,
            SwShMovesWorkflowService.RecoilField => move.Recoil,
            SwShMovesWorkflowService.RawHealingField => move.RawHealing,
            SwShMovesWorkflowService.Stat1Field => GetStatChangeValue(move, 1, stat => stat.Stat),
            SwShMovesWorkflowService.Stat1StageField => GetStatChangeValue(move, 1, stat => stat.Stage),
            SwShMovesWorkflowService.Stat1PercentField => GetStatChangeValue(move, 1, stat => stat.Percent),
            SwShMovesWorkflowService.Stat2Field => GetStatChangeValue(move, 2, stat => stat.Stat),
            SwShMovesWorkflowService.Stat2StageField => GetStatChangeValue(move, 2, stat => stat.Stage),
            SwShMovesWorkflowService.Stat2PercentField => GetStatChangeValue(move, 2, stat => stat.Percent),
            SwShMovesWorkflowService.Stat3Field => GetStatChangeValue(move, 3, stat => stat.Stat),
            SwShMovesWorkflowService.Stat3StageField => GetStatChangeValue(move, 3, stat => stat.Stage),
            SwShMovesWorkflowService.Stat3PercentField => GetStatChangeValue(move, 3, stat => stat.Percent),
            _ when IsFlagField(field) => move.Flags.FirstOrDefault(flag =>
                string.Equals(flag.Field, field, StringComparison.Ordinal))?.Enabled == true ? 1 : 0,
            _ => null,
        };
    }

    private static int GetStatChangeValue(
        SwShMoveRecord move,
        int slot,
        Func<SwShMoveStatChangeRecord, int> selectValue)
    {
        var stat = move.StatChanges.FirstOrDefault(candidate => candidate.Slot == slot);
        return stat is null ? 0 : selectValue(stat);
    }

    private static IReadOnlyList<SwShMoveStatChangeRecord> OverlayStatChange(
        IReadOnlyList<SwShMoveStatChangeRecord> statChanges,
        int slot,
        Func<SwShMoveStatChangeRecord, SwShMoveStatChangeRecord> update)
    {
        var updated = statChanges.ToList();
        var index = updated.FindIndex(stat => stat.Slot == slot);
        if (index < 0)
        {
            updated.Add(update(new SwShMoveStatChangeRecord(slot, Stat: 0, "None", Stage: 0, Percent: 0)));
        }
        else
        {
            updated[index] = update(updated[index]);
        }

        return updated.OrderBy(stat => stat.Slot).ToArray();
    }

    private static IReadOnlyList<SwShMoveFlagRecord> OverlayFlag(
        IReadOnlyList<SwShMoveFlagRecord> flags,
        string field,
        bool enabled)
    {
        return flags
            .Select(flag => string.Equals(flag.Field, field, StringComparison.Ordinal)
                ? flag with { Enabled = enabled }
                : flag)
            .ToArray();
    }

    private static IReadOnlyList<PlannedFileWrite> CreatePlannedWrites(
        SwShMovesWorkflow workflow,
        ProjectPaths paths,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return edits
            .GroupBy(edit => GetTargetRelativePath(workflow, edit), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var targetRelativePath = group.Key;
                if (string.IsNullOrWhiteSpace(targetRelativePath))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending move edit does not include a valid target source file.",
                        expected: "Move data source"));
                    return null;
                }

                var targetPath = SwShMovesWorkflowService.ResolveOutputPath(paths, targetRelativePath);
                if (targetPath is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Moves Data apply target must stay inside the configured output root.",
                        file: targetRelativePath,
                        expected: "Output-root-contained target"));
                    return null;
                }

                var groupEdits = group.ToArray();
                var sources = groupEdits
                    .Select(edit => GetSourceReference(workflow, edit))
                    .Where(source => source is not null)
                    .Select(source => source!)
                    .Distinct()
                    .ToArray();
                var orderedSummaries = groupEdits
                    .OrderBy(edit => edit.RecordId, StringComparer.Ordinal)
                    .ThenBy(edit => edit.Field, StringComparer.Ordinal)
                    .Select(edit => CreateAuthoritativePlanSummary(workflow, edit))
                    .ToArray();
                var reason = groupEdits.Length == 1
                    ? $"Apply pending Moves Data edit: {orderedSummaries[0]}"
                    : $"Apply pending Moves Data edits: {string.Join(" ", orderedSummaries)}";

                return new PlannedFileWrite(
                    targetRelativePath,
                    sources,
                    File.Exists(targetPath),
                    reason);
            })
            .Where(write => write is not null)
            .Select(write => write!)
            .OrderBy(write => write.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CreateAuthoritativePlanSummary(
        SwShMovesWorkflow workflow,
        PendingEdit edit)
    {
        if (int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
            && int.TryParse(edit.NewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && edit.Field is not null
            && workflow.Moves.FirstOrDefault(move => move.MoveId == moveId) is { } move)
        {
            return CreatePendingEditSummary(move, edit.Field, value);
        }

        return $"Set move {edit.RecordId ?? "(missing)"} field {edit.Field ?? "(missing)"} to {edit.NewValue ?? "(missing)"}.";
    }

    private static string? GetTargetRelativePath(SwShMovesWorkflow workflow, PendingEdit edit)
    {
        return GetSourceReference(workflow, edit)?.RelativePath;
    }

    private static ProjectFileReference? GetSourceReference(SwShMovesWorkflow workflow, PendingEdit edit)
    {
        if (!int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId))
        {
            return null;
        }

        var move = workflow.Moves.FirstOrDefault(candidate => candidate.MoveId == moveId);
        return move is null
            ? null
            : new ProjectFileReference(move.Provenance.SourceLayer, move.Provenance.SourceFile);
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
                "Moves Data apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        if (Path.IsPathRooted(targetRelativePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Moves Data apply target must be relative to the output root.",
                file: targetRelativePath,
                expected: "Relative output target"));
            return null;
        }

        var targetPath = SwShMovesWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Moves Data apply target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static SwShMoveDataRecord ApplyMoveEdits(
        SwShMoveDataRecord sourceRecord,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var record = sourceRecord;

        foreach (var edit in edits)
        {
            if (!uint.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out var moveId)
                || record.MoveId != moveId)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending move edit target no longer matches the source move data file.",
                    field: "moveId",
                    expected: "Current move data source target"));
                continue;
            }

            if (TryParseEditableValue(edit.Field, edit.NewValue, diagnostics) is not { } value)
            {
                continue;
            }

            record = ApplyMoveDataField(record, edit.Field!, value);
        }

        return record;
    }

    private static SwShMoveDataRecord ApplyMoveDataField(SwShMoveDataRecord record, string field, int value)
    {
        return field switch
        {
            SwShMovesWorkflowService.CanUseMoveField => record with { CanUseMove = value != 0 },
            SwShMovesWorkflowService.TypeField => record with { Core = record.Core with { Type = checked((byte)value) } },
            SwShMovesWorkflowService.QualityField => record with { Core = record.Core with { Quality = checked((byte)value) } },
            SwShMovesWorkflowService.CategoryField => record with { Core = record.Core with { Category = checked((byte)value) } },
            SwShMovesWorkflowService.PowerField => record with { Core = record.Core with { Power = checked((byte)value) } },
            SwShMovesWorkflowService.AccuracyField => record with { Core = record.Core with { Accuracy = checked((byte)value) } },
            SwShMovesWorkflowService.PpField => record with { Core = record.Core with { PP = checked((byte)value) } },
            SwShMovesWorkflowService.PriorityField => record with { Core = record.Core with { Priority = checked((sbyte)value) } },
            SwShMovesWorkflowService.CritStageField => record with { Core = record.Core with { CritStage = checked((sbyte)value) } },
            SwShMovesWorkflowService.MaxMovePowerField => record with { Core = record.Core with { GigantamaxPower = checked((byte)value) } },
            SwShMovesWorkflowService.TargetField => record with { Targeting = record.Targeting with { RawTarget = checked((byte)value) } },
            SwShMovesWorkflowService.HitMinField => record with { Targeting = record.Targeting with { HitMin = checked((byte)value) } },
            SwShMovesWorkflowService.HitMaxField => record with { Targeting = record.Targeting with { HitMax = checked((byte)value) } },
            SwShMovesWorkflowService.TurnMinField => record with { Targeting = record.Targeting with { TurnMin = checked((byte)value) } },
            SwShMovesWorkflowService.TurnMaxField => record with { Targeting = record.Targeting with { TurnMax = checked((byte)value) } },
            SwShMovesWorkflowService.InflictField => record with { Secondary = record.Secondary with { Inflict = checked((ushort)value) } },
            SwShMovesWorkflowService.InflictPercentField => record with { Secondary = record.Secondary with { InflictPercent = checked((byte)value) } },
            SwShMovesWorkflowService.RawInflictCountField => record with { Secondary = record.Secondary with { RawInflictCount = checked((byte)value) } },
            SwShMovesWorkflowService.FlinchField => record with { Secondary = record.Secondary with { Flinch = checked((byte)value) } },
            SwShMovesWorkflowService.EffectSequenceField => record with { Secondary = record.Secondary with { EffectSequence = checked((ushort)value) } },
            SwShMovesWorkflowService.RecoilField => record with { Secondary = record.Secondary with { Recoil = checked((sbyte)value) } },
            SwShMovesWorkflowService.RawHealingField => record with { Secondary = record.Secondary with { RawHealing = checked((sbyte)value) } },
            SwShMovesWorkflowService.Stat1Field => record with { StatChanges = ApplyStatChange(record.StatChanges, slot: 1, stat => stat with { Stat = checked((byte)value) }) },
            SwShMovesWorkflowService.Stat1StageField => record with { StatChanges = ApplyStatChange(record.StatChanges, slot: 1, stat => stat with { Stage = checked((sbyte)value) }) },
            SwShMovesWorkflowService.Stat1PercentField => record with { StatChanges = ApplyStatChange(record.StatChanges, slot: 1, stat => stat with { Percent = checked((byte)value) }) },
            SwShMovesWorkflowService.Stat2Field => record with { StatChanges = ApplyStatChange(record.StatChanges, slot: 2, stat => stat with { Stat = checked((byte)value) }) },
            SwShMovesWorkflowService.Stat2StageField => record with { StatChanges = ApplyStatChange(record.StatChanges, slot: 2, stat => stat with { Stage = checked((sbyte)value) }) },
            SwShMovesWorkflowService.Stat2PercentField => record with { StatChanges = ApplyStatChange(record.StatChanges, slot: 2, stat => stat with { Percent = checked((byte)value) }) },
            SwShMovesWorkflowService.Stat3Field => record with { StatChanges = ApplyStatChange(record.StatChanges, slot: 3, stat => stat with { Stat = checked((byte)value) }) },
            SwShMovesWorkflowService.Stat3StageField => record with { StatChanges = ApplyStatChange(record.StatChanges, slot: 3, stat => stat with { Stage = checked((sbyte)value) }) },
            SwShMovesWorkflowService.Stat3PercentField => record with { StatChanges = ApplyStatChange(record.StatChanges, slot: 3, stat => stat with { Percent = checked((byte)value) }) },
            _ when IsFlagField(field) => record with { Flags = ApplyFlag(record.Flags, field, value != 0) },
            _ => record,
        };
    }

    private static IReadOnlyList<SwShMoveStatChange> ApplyStatChange(
        IReadOnlyList<SwShMoveStatChange> statChanges,
        int slot,
        Func<SwShMoveStatChange, SwShMoveStatChange> update)
    {
        var updated = statChanges.ToList();
        var index = updated.FindIndex(stat => stat.Slot == slot);
        if (index < 0)
        {
            updated.Add(update(new SwShMoveStatChange(slot, Stat: 0, Stage: 0, Percent: 0)));
        }
        else
        {
            updated[index] = update(updated[index]);
        }

        return updated.OrderBy(stat => stat.Slot).ToArray();
    }

    private static SwShMoveFlags ApplyFlag(SwShMoveFlags flags, string field, bool value)
    {
        return field switch
        {
            SwShMovesWorkflowService.MakesContactField => flags with { MakesContact = value },
            SwShMovesWorkflowService.ChargeField => flags with { Charge = value },
            SwShMovesWorkflowService.RechargeField => flags with { Recharge = value },
            SwShMovesWorkflowService.ProtectField => flags with { Protect = value },
            SwShMovesWorkflowService.ReflectableField => flags with { Reflectable = value },
            SwShMovesWorkflowService.SnatchField => flags with { Snatch = value },
            SwShMovesWorkflowService.MirrorField => flags with { Mirror = value },
            SwShMovesWorkflowService.PunchField => flags with { Punch = value },
            SwShMovesWorkflowService.SoundField => flags with { Sound = value },
            SwShMovesWorkflowService.GravityField => flags with { Gravity = value },
            SwShMovesWorkflowService.DefrostField => flags with { Defrost = value },
            SwShMovesWorkflowService.DistanceTripleField => flags with { DistanceTriple = value },
            SwShMovesWorkflowService.HealField => flags with { Heal = value },
            SwShMovesWorkflowService.IgnoreSubstituteField => flags with { IgnoreSubstitute = value },
            SwShMovesWorkflowService.FailSkyBattleField => flags with { FailSkyBattle = value },
            SwShMovesWorkflowService.AnimateAllyField => flags with { AnimateAlly = value },
            SwShMovesWorkflowService.DanceField => flags with { Dance = value },
            SwShMovesWorkflowService.MetronomeField => flags with { Metronome = value },
            _ => flags,
        };
    }

    private static bool IsFlagField(string field)
    {
        return field is SwShMovesWorkflowService.MakesContactField
            or SwShMovesWorkflowService.ChargeField
            or SwShMovesWorkflowService.RechargeField
            or SwShMovesWorkflowService.ProtectField
            or SwShMovesWorkflowService.ReflectableField
            or SwShMovesWorkflowService.SnatchField
            or SwShMovesWorkflowService.MirrorField
            or SwShMovesWorkflowService.PunchField
            or SwShMovesWorkflowService.SoundField
            or SwShMovesWorkflowService.GravityField
            or SwShMovesWorkflowService.DefrostField
            or SwShMovesWorkflowService.DistanceTripleField
            or SwShMovesWorkflowService.HealField
            or SwShMovesWorkflowService.IgnoreSubstituteField
            or SwShMovesWorkflowService.FailSkyBattleField
            or SwShMovesWorkflowService.AnimateAllyField
            or SwShMovesWorkflowService.DanceField
            or SwShMovesWorkflowService.MetronomeField;
    }

    private static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        if (!reviewedPlan.CanApply
            || reviewedPlan.SessionId != currentPlan.SessionId
            || reviewedPlan.Writes.Count != currentPlan.Writes.Count)
        {
            return false;
        }

        var reviewedWrites = reviewedPlan.Writes
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();
        var currentWrites = currentPlan.Writes
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();

        return reviewedWrites
            .Zip(currentWrites)
            .All(pair => PlannedWritesMatch(pair.First, pair.Second));
    }

    private static bool PlannedWritesMatch(PlannedFileWrite reviewed, PlannedFileWrite current)
    {
        if (!string.Equals(reviewed.TargetRelativePath, current.TargetRelativePath, StringComparison.Ordinal)
            || reviewed.ReplacesExistingOutput != current.ReplacesExistingOutput
            || !string.Equals(reviewed.Reason, current.Reason, StringComparison.Ordinal))
        {
            return false;
        }

        var reviewedSources = reviewed.Sources
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var currentSources = current.Sources
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return reviewedSources.SequenceEqual(currentSources);
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

    private static string CreatePendingEditSummary(SwShMoveRecord move, string field, int value)
    {
        var label = SwShMovesWorkflowService.GetEditableField(field)?.Label ?? field;
        var displayValue = SwShMovesWorkflowService.GetEditableField(field)?.ValueKind == "boolean"
            ? value == 0 ? "disabled" : "enabled"
            : value.ToString(CultureInfo.InvariantCulture);

        return $"Set {move.Name} {label.ToLowerInvariant()} to {displayValue}.";
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

    private static string FormatCategory(int value)
    {
        return value switch
        {
            0 => "Status",
            1 => "Physical",
            2 => "Special",
            _ => $"Category {value}",
        };
    }

    private static string FormatTarget(int value)
    {
        return value switch
        {
            0 => "Any Except Self",
            1 => "Ally Or Self",
            2 => "Ally",
            3 => "Opponent",
            4 => "All Adjacent",
            5 => "All Adjacent Opponents",
            6 => "All Allies",
            7 => "Self",
            8 => "All",
            9 => "Random Opponent",
            10 => "All Sides",
            11 => "Opponent Side",
            12 => "Self Side",
            13 => "Counter Target",
            _ => $"Target {value}",
        };
    }

    private static string FormatInflict(int value)
    {
        return value switch
        {
            0 => "None",
            1 => "Paralyze",
            2 => "Sleep",
            3 => "Freeze",
            4 => "Burn",
            5 => "Poison",
            6 => "Confusion",
            7 => "Infatuation",
            8 => "Trap",
            9 => "Nightmare",
            12 => "Torment",
            13 => "Disable",
            14 => "Drowsiness",
            15 => "Heal Block",
            17 => "Identify",
            18 => "Leech Seed",
            19 => "Embargo",
            20 => "Perish Song",
            21 => "Ingrain",
            24 => "Throat Chop",
            42 => "Tar Shot",
            65535 => "Move-defined / scripted effect",
            _ => $"Inflict {value}",
        };
    }

    private static string FormatStat(int value)
    {
        return value switch
        {
            0 => "None",
            1 => "Attack",
            2 => "Defense",
            3 => "Sp. Atk",
            4 => "Sp. Def",
            5 => "Speed",
            6 => "Accuracy",
            7 => "Evasion",
            8 => "All Stats",
            _ => $"Stat {value}",
        };
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Move field '{field}' is not supported by the Moves Data workflow yet.",
            field: "field",
            expected: "Supported move data field");
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
            Domain: MovesEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record MoveOutput(
        string RelativePath,
        string AbsolutePath,
        byte[] Contents);

    private sealed class StagedMoveOutput(
        MoveOutput output,
        string temporaryPath,
        string backupPath,
        bool originalExisted)
    {
        public MoveOutput Output { get; } = output;

        public string TemporaryPath { get; } = temporaryPath;

        public string BackupPath { get; } = backupPath;

        public bool OriginalExisted { get; } = originalExisted;

        public bool OriginalMoved { get; set; }

        public bool NewInstalled { get; set; }
    }
}
