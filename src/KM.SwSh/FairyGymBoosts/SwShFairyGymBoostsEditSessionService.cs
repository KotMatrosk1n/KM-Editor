// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.FairyGymBoosts;

public sealed class SwShFairyGymBoostsEditSessionService
{
    public const string FairyGymBoostsEditDomain = SwShFairyGymBoostsWorkflowService.FairyGymBoostsEditDomain;

    private const string RecordId = "fairy-gym-boosts";
    private const string BoostSelectionsField = "boostSelections";
    private const string PendingSummary = "Stage Fairy Gym boost outcomes.";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShFairyGymBoostsWorkflowService fairyGymBoostsWorkflowService;
    private readonly Action? beforeAcquireApplyScope;
    private readonly Action<int, string>? beforeVerifiedPromotion;

    public SwShFairyGymBoostsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShFairyGymBoostsWorkflowService? fairyGymBoostsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fairyGymBoostsWorkflowService = fairyGymBoostsWorkflowService ?? new SwShFairyGymBoostsWorkflowService();
    }

    internal SwShFairyGymBoostsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShFairyGymBoostsWorkflowService? fairyGymBoostsWorkflowService,
        Action beforeAcquireApplyScope)
        : this(projectWorkspaceService, fairyGymBoostsWorkflowService)
    {
        this.beforeAcquireApplyScope = beforeAcquireApplyScope
            ?? throw new ArgumentNullException(nameof(beforeAcquireApplyScope));
    }

    internal SwShFairyGymBoostsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService,
        SwShFairyGymBoostsWorkflowService? fairyGymBoostsWorkflowService,
        Action<int, string> beforeVerifiedPromotion)
        : this(projectWorkspaceService, fairyGymBoostsWorkflowService)
    {
        this.beforeVerifiedPromotion = beforeVerifiedPromotion
            ?? throw new ArgumentNullException(nameof(beforeVerifiedPromotion));
    }

    public SwShFairyGymBoostsEditResult StageBoosts(
        ProjectPaths paths,
        IReadOnlyList<SwShFairyGymBoostSelection> selections,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(selections);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fairyGymBoostsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShFairyGymBoostsWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.Add(CreateWrongGameDiagnostic());
            return new SwShFairyGymBoostsEditResult(workflow, currentSession, diagnostics);
        }

        if (currentSession.PendingEdits.Any(edit => !string.Equals(
            edit.Domain,
            FairyGymBoostsEditDomain,
            StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts needs its own edit session before staging.",
                expected: "A Fairy Gym Boosts-only edit session"));
            return new SwShFairyGymBoostsEditResult(workflow, currentSession, diagnostics);
        }

        var normalizedSelections = NormalizeSelections(selections, diagnostics);
        var changedFileGroups = CreateChangedFileGroups(workflow, normalizedSelections).ToArray();
        if (!CanStage(project, workflow, diagnostics)
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShFairyGymBoostsEditResult(workflow, currentSession, diagnostics);
        }

        if (changedFileGroups.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts has no changed answer outcomes to stage.",
                field: BoostSelectionsField,
                expected: "At least one changed Fairy Gym boost outcome"));
            return new SwShFairyGymBoostsEditResult(workflow, currentSession, diagnostics);
        }

        var payload = EncodeSelections(normalizedSelections);
        var sourceReferences = CreateCanonicalSources(project, payload, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShFairyGymBoostsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingEdit(payload, sourceReferences)],
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Fairy Gym boost outcomes are staged for change-plan review."));

        return new SwShFairyGymBoostsEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fairyGymBoostsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SwShFairyGymBoostsWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.Add(CreateWrongGameDiagnostic());
        }

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage Fairy Gym boost outcomes before validating."
                    : "Fairy Gym Boosts expects exactly one canonical staged boost edit.",
                expected: "Exactly one pending Fairy Gym Boosts edit"));
        }
        else
        {
            var edit = session.PendingEdits[0];
            if (!IsCanonicalIdentity(edit))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit does not target the canonical Fairy Gym Boosts field.",
                    field: edit.Field,
                    expected: $"{FairyGymBoostsEditDomain}/{RecordId}/{BoostSelectionsField}"));
            }
            else
            {
                ValidateCanonicalEdit(project, workflow, edit, diagnostics);
            }
        }

        CanStage(project, workflow, diagnostics);
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Fairy Gym Boosts change is valid for change-plan review."));
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
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = fairyGymBoostsWorkflowService.Load(project);
        var edit = session.PendingEdits.Single();
        var selections = DecodeSelections(edit.NewValue, diagnostics);
        var canonicalSources = CreateCanonicalSources(project, edit.NewValue ?? string.Empty, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var fileGroups = CreateChangedFileGroups(workflow, selections)
            .OrderBy(group => group.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (fileGroups.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts staged values no longer change any owned answer slots.",
                field: BoostSelectionsField,
                expected: "At least one changed owned answer slot"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>(fileGroups.Length);
        foreach (var fileGroup in fileGroups)
        {
            var source = SwShFairyGymBoostsWorkflowService.ResolveWorkflowFile(project, fileGroup.RelativePath);
            var targetPath = ResolveOutputPath(paths, fileGroup.RelativePath, diagnostics);
            if (source is null || targetPath is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts source or output target could not be resolved.",
                    file: fileGroup.RelativePath,
                    expected: "Verified BSEQ source and writable LayeredFS target"));
                continue;
            }

            writes.Add(new PlannedFileWrite(
                fileGroup.RelativePath,
                canonicalSources,
                File.Exists(targetPath),
                "Update reviewed Fairy Gym quiz outcomes in owned answer slots while preserving every unowned byte."));
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            $"Fairy Gym Boosts change plan preview contains {writes.Count:N0} target file(s)."));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
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
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed Fairy Gym Boosts change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Fairy Gym Boosts change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        beforeAcquireApplyScope?.Invoke();
        if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                currentPlan,
                out var applyScope,
                out var acquireDiagnostics))
        {
            return CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                acquireDiagnostics);
        }

        using var verifiedScope = applyScope!;
        var snapshotPlan = CreateChangePlan(verifiedScope.ApplyPaths, session);
        if (!verifiedScope.TryPrepareSnapshotPlan(snapshotPlan, out var preparedPlan))
        {
            var staleDiagnostics = preparedPlan.Diagnostics.ToList();
            staleDiagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts sources changed while preparing the verified apply snapshot.",
                expected: "Sources matching the reviewed Fairy Gym Boosts change plan"));
            return CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                staleDiagnostics);
        }

        var snapshotResult = ApplyPreparedPlan(
            verifiedScope.ApplyPaths,
            session,
            preparedPlan,
            applyId,
            appliedAt);
        return verifiedScope.Commit(snapshotResult, beforeVerifiedPromotion);
    }

    private ApplyResult ApplyPreparedPlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan preparedPlan,
        string applyId,
        DateTimeOffset appliedAt)
    {
        projectWorkspaceService.ClearMemoryCache();
        var diagnostics = preparedPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        if (session.PendingEdits.Count != 1 || !IsCanonicalIdentity(session.PendingEdits[0]))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts prepared apply requires exactly one canonical pending edit.",
                expected: $"{FairyGymBoostsEditDomain}/{RecordId}/{BoostSelectionsField}"));
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var workflow = fairyGymBoostsWorkflowService.Load(project);
        var selections = DecodeSelections(session.PendingEdits[0].NewValue, diagnostics);
        var fileGroups = CreateChangedFileGroups(workflow, selections)
            .OrderBy(group => group.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var preparedOutputs = new List<PreparedFairyGymBoostOutput>(fileGroups.Length);
        foreach (var fileGroup in fileGroups)
        {
            var prepared = PrepareFileGroup(project, paths, fileGroup, diagnostics);
            if (prepared is not null)
            {
                preparedOutputs.Add(prepared);
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
        }

        foreach (var prepared in preparedOutputs)
        {
            try
            {
                if (prepared.DeleteTarget)
                {
                    File.Delete(prepared.TargetPath);
                    if (File.Exists(prepared.TargetPath) || Directory.Exists(prepared.TargetPath))
                    {
                        throw new IOException(
                            "Fairy Gym Boosts restore target still exists after verified deletion.");
                    }
                }
                else
                {
                    WriteOutputAtomically(
                        prepared.TargetPath,
                        prepared.Output,
                        roundTrip => VerifyPreparedOutput(prepared, roundTrip));
                }

                writtenFiles.Add(new ProjectFileReference(
                    ProjectFileLayer.Generated,
                    prepared.FileGroup.RelativePath));
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Info,
                    prepared.DeleteTarget
                        ? $"Restored Fairy Gym Boosts defaults in {prepared.FileGroup.RelativePath}."
                        : $"Applied Fairy Gym Boosts changes to {prepared.FileGroup.RelativePath}.",
                    file: prepared.FileGroup.RelativePath));
            }
            catch (Exception exception) when (exception is InvalidDataException
                or IOException
                or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fairy Gym Boosts verified output could not be written: {exception.Message}",
                    file: prepared.FileGroup.RelativePath,
                    expected: "Writable output with reviewed owned answer slots"));
                break;
            }
        }

        return CreateApplyResult(applyId, appliedAt, preparedPlan, writtenFiles, diagnostics);
    }

    private static PreparedFairyGymBoostOutput? PrepareFileGroup(
        OpenedProject project,
        ProjectPaths paths,
        FairyGymBoostFileGroup fileGroup,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShFairyGymBoostsWorkflowService.ResolveWorkflowFile(project, fileGroup.RelativePath);
        var basePath = SwShFairyGymBoostsWorkflowService.ResolveBaseSourcePath(paths, fileGroup.RelativePath);
        var targetPath = ResolveOutputPath(paths, fileGroup.RelativePath, diagnostics);
        if (source is null || basePath is null || !File.Exists(basePath) || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts source, vanilla base, or output target could not be resolved.",
                file: fileGroup.RelativePath,
                expected: "Verified effective source, canonical vanilla base, and writable LayeredFS target"));
            return null;
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var baseBytes = File.ReadAllBytes(basePath);
            var vanillaSlots = SwShFairyGymBoostsWorkflowService.GetVanillaSlots(fileGroup.RelativePath);
            var patches = CreateAnswerPatches(fileGroup);
            var output = SwShFairyGymBoostsBseqPatcher.ApplySelections(
                sourceBytes,
                baseBytes,
                vanillaSlots,
                patches);
            var prepared = new PreparedFairyGymBoostOutput(
                fileGroup,
                targetPath,
                sourceBytes,
                baseBytes,
                vanillaSlots,
                patches,
                output,
                DeleteTarget: output.AsSpan().SequenceEqual(baseBytes));
            VerifyPreparedOutput(prepared, output);
            return prepared;
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fairy Gym Boosts source file could not be patched safely: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Reviewed 0x4A10 BSEQ with owned answer slots at 0x1550-0x155F"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Fairy Gym Boosts source file could not be read: {exception.Message}",
                file: fileGroup.RelativePath,
                expected: "Readable vanilla base and compatible effective Fairy Gym BSEQ files"));
        }

        return null;
    }

    private static void VerifyPreparedOutput(
        PreparedFairyGymBoostOutput prepared,
        byte[] output)
    {
        var expected = SwShFairyGymBoostsBseqPatcher.ApplySelections(
            prepared.SourceBytes,
            prepared.BaseBytes,
            prepared.VanillaSlots,
            prepared.Patches);
        if (!output.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidDataException(
                "Fairy Gym Boosts output did not round-trip with the exact reviewed owned-slot patch.");
        }

        if (prepared.DeleteTarget != output.AsSpan().SequenceEqual(prepared.BaseBytes))
        {
            throw new InvalidDataException(
                "Fairy Gym Boosts restore decision does not match complete base equivalence.");
        }
    }

    private static IReadOnlyList<SwShFairyGymBoostAnswerPatch> CreateAnswerPatches(
        FairyGymBoostFileGroup fileGroup)
    {
        return fileGroup.Selections
            .OrderBy(selection => selection.Definition.AnswerChoice)
            .Select(selection => new SwShFairyGymBoostAnswerPatch(
                selection.Definition.AnswerChoice,
                selection.Selection.EffectId,
                SwShFairyGymBoostsWorkflowService.ToResultValue(selection.Selection.ResultKind)))
            .ToArray();
    }

    private static bool CanStage(
        OpenedProject project,
        SwShFairyGymBoostsWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error))
        {
            if (!diagnostics.Contains(diagnostic))
            {
                diagnostics.Add(diagnostic);
            }
        }

        if (!SwShFairyGymBoostsWorkflowService.IsSupportedGame(project.Paths.SelectedGame)
            || !project.Health.CanOpenEditableWorkflows
            || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts apply requires valid paths, Pokemon Sword or Shield, and a valid output root.",
                    expected: "Editable Pokemon Sword or Pokemon Shield project paths"));
            }

            return false;
        }

        foreach (var source in workflow.Sources.Where(source => source.Status != "available"))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                source.Status == "missing"
                    ? $"{source.Label} is missing and is required before Fairy Gym Boosts can be staged."
                    : $"{source.Label} is blocked and cannot be edited safely.",
                file: source.RelativePath,
                expected: "Available verified Fairy Gym quiz BSEQ source"));
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static IReadOnlyList<SwShFairyGymBoostSelection> NormalizeSelections(
        IReadOnlyList<SwShFairyGymBoostSelection> selections,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var byBoostId = new Dictionary<string, SwShFairyGymBoostSelection>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            if (selection is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts selection is missing.",
                    field: BoostSelectionsField,
                    expected: "A complete Fairy Gym boost selection"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(selection.BoostId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts selection is missing a boost id.",
                    field: BoostSelectionsField,
                    expected: "Known Fairy Gym boost id"));
                continue;
            }

            var definition = SwShFairyGymBoostsWorkflowService.FindBoost(selection.BoostId);
            if (definition is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fairy Gym Boosts selection '{selection.BoostId}' is not recognized.",
                    field: BoostSelectionsField,
                    expected: "Known Fairy Gym boost id"));
                continue;
            }

            if (!byBoostId.TryAdd(selection.BoostId, selection))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fairy Gym Boosts selection '{selection.BoostId}' is duplicated.",
                    field: BoostSelectionsField,
                    expected: "One selection per owned answer choice"));
                continue;
            }

            if (!SwShFairyGymBoostsWorkflowService.IsSupportedSelection(
                selection.EffectId,
                selection.ResultKind))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fairy Gym Boosts selection '{selection.BoostId}' is not a supported outcome.",
                    field: BoostSelectionsField,
                    expected: "No effect, or effect 1-6 with boost/drop"));
            }
        }

        foreach (var boost in SwShFairyGymBoostsWorkflowService.Boosts)
        {
            if (!byBoostId.ContainsKey(boost.BoostId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Fairy Gym Boosts selection '{boost.BoostId}' is missing.",
                    field: BoostSelectionsField,
                    expected: "All 12 unique Fairy Gym boost selections"));
            }
        }

        return SwShFairyGymBoostsWorkflowService.Boosts
            .Select(boost => byBoostId.TryGetValue(boost.BoostId, out var selection)
                ? selection
                : SwShFairyGymBoostsWorkflowService.CreateDefaultSelection(boost))
            .ToArray();
    }

    private static string EncodeSelections(IReadOnlyList<SwShFairyGymBoostSelection> selections)
    {
        return string.Join(
            ';',
            selections.Select(selection => string.Create(
                CultureInfo.InvariantCulture,
                $"{selection.BoostId}:{selection.EffectId}:{selection.ResultKind}")));
    }

    private static IReadOnlyList<SwShFairyGymBoostSelection> DecodeSelections(
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts pending edit has no outcome payload.",
                field: BoostSelectionsField,
                expected: "Canonical encoded Fairy Gym boost selections"));
            return [];
        }

        var selections = new List<SwShFairyGymBoostSelection>();
        foreach (var entry in value.Split(';', StringSplitOptions.None))
        {
            var parts = entry.Split(':', StringSplitOptions.None);
            if (parts.Length != 3
                || string.IsNullOrEmpty(parts[0])
                || !TryParseCanonicalInt(parts[1], out var effectId)
                || string.IsNullOrEmpty(parts[2]))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Fairy Gym Boosts pending edit payload is malformed.",
                    field: BoostSelectionsField,
                    expected: "boostId:effectId:resultKind entries"));
                continue;
            }

            selections.Add(new SwShFairyGymBoostSelection(parts[0], effectId, parts[2]));
        }

        return NormalizeSelections(selections, diagnostics);
    }

    private static bool TryParseCanonicalInt(string value, out int result)
    {
        return int.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out result)
            && string.Equals(
                value,
                result.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }

    private void ValidateCanonicalEdit(
        OpenedProject project,
        SwShFairyGymBoostsWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var selections = DecodeSelections(edit.NewValue, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        var canonicalPayload = EncodeSelections(selections);
        if (!string.Equals(edit.NewValue, canonicalPayload, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Fairy Gym Boosts selections are not in canonical payload format.",
                field: BoostSelectionsField,
                expected: "All 12 unique selections in canonical mapping order"));
        }

        if (!string.Equals(edit.Summary, PendingSummary, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Fairy Gym Boosts edit does not have the canonical staged summary.",
                field: BoostSelectionsField,
                expected: PendingSummary));
        }

        var sourceDiagnostics = new List<ValidationDiagnostic>();
        var expectedSources = CreateCanonicalSources(project, canonicalPayload, sourceDiagnostics);
        foreach (var diagnostic in sourceDiagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        if (!edit.Sources.SequenceEqual(expectedSources))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Fairy Gym Boosts sources do not match the canonical base, layered, and payload references.",
                field: BoostSelectionsField,
                expected: "Canonical ordered unique Fairy Gym Boosts source references"));
        }

        if (CreateChangedFileGroups(workflow, selections).Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Fairy Gym Boosts selections contain no changed outcomes.",
                field: BoostSelectionsField,
                expected: "At least one changed owned answer slot"));
        }
    }

    private IReadOnlyList<ProjectFileReference> CreateCanonicalSources(
        OpenedProject project,
        string payload,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sources = fairyGymBoostsWorkflowService.GetPlanSources(project).ToList();
        foreach (var definition in SwShFairyGymBoostsWorkflowService.Sources)
        {
            if (!sources.Contains(new ProjectFileReference(
                ProjectFileLayer.Base,
                definition.RelativePath)))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{definition.Label} vanilla base source could not be resolved.",
                    file: definition.RelativePath,
                    expected: "Readable vanilla Fairy Gym BSEQ base file"));
            }
        }

        sources.Add(CreatePendingPayloadSource(payload));
        return sources
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<FairyGymBoostFileGroup> CreateChangedFileGroups(
        SwShFairyGymBoostsWorkflow workflow,
        IReadOnlyList<SwShFairyGymBoostSelection> selections)
    {
        var currentByBoostId = workflow.Trainers
            .SelectMany(trainer => trainer.Boosts)
            .ToDictionary(boost => boost.BoostId, StringComparer.Ordinal);
        var definitionsByBoostId = SwShFairyGymBoostsWorkflowService.Boosts
            .ToDictionary(boost => boost.BoostId, StringComparer.Ordinal);

        return selections
            .Where(selection => currentByBoostId.TryGetValue(selection.BoostId, out var current)
                && (current.EffectId != selection.EffectId
                    || !string.Equals(
                        current.ResultKind,
                        selection.ResultKind,
                        StringComparison.Ordinal)))
            .Select(selection => new FairyGymBoostSelectionPatch(
                definitionsByBoostId[selection.BoostId],
                selection))
            .GroupBy(selection => selection.Definition.SequenceFile, StringComparer.Ordinal)
            .Select(group => new FairyGymBoostFileGroup(
                group.Key,
                group.OrderBy(selection => selection.Definition.AnswerChoice).ToArray()))
            .OrderBy(group => group.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static PendingEdit CreatePendingEdit(
        string payload,
        IReadOnlyList<ProjectFileReference> sourceReferences)
    {
        return new PendingEdit(
            FairyGymBoostsEditDomain,
            PendingSummary,
            sourceReferences,
            RecordId,
            BoostSelectionsField,
            payload);
    }

    private static ProjectFileReference CreatePendingPayloadSource(string canonicalPayload)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/fairy-gym-boosts/selections/{hash}");
    }

    private static bool IsCanonicalIdentity(PendingEdit edit)
    {
        return string.Equals(edit.Domain, FairyGymBoostsEditDomain, StringComparison.Ordinal)
            && string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, BoostSelectionsField, StringComparison.Ordinal);
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
                "Fairy Gym Boosts apply requires a configured output root.",
                file: targetRelativePath,
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShFairyGymBoostsWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Fairy Gym Boosts target must stay inside the configured output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static void WriteOutputAtomically(
        string targetPath,
        byte[] output,
        Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Fairy Gym Boosts output directory could not be resolved.");
        Directory.CreateDirectory(directoryPath);
        var temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, output);
            var roundTrip = File.ReadAllBytes(temporaryPath);
            if (!roundTrip.AsSpan().SequenceEqual(output))
            {
                throw new IOException(
                    "Fairy Gym Boosts temporary output did not round-trip byte-for-byte.");
            }

            verifyRoundTrip(roundTrip);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static ValidationDiagnostic CreateWrongGameDiagnostic()
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            "Fairy Gym Boosts only supports Pokemon Sword and Pokemon Shield projects.",
            expected: "Pokemon Sword or Pokemon Shield project");
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

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: FairyGymBoostsEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record FairyGymBoostFileGroup(
        string RelativePath,
        IReadOnlyList<FairyGymBoostSelectionPatch> Selections);

    private sealed record FairyGymBoostSelectionPatch(
        SwShFairyGymBoostDefinition Definition,
        SwShFairyGymBoostSelection Selection);

    private sealed record PreparedFairyGymBoostOutput(
        FairyGymBoostFileGroup FileGroup,
        string TargetPath,
        byte[] SourceBytes,
        byte[] BaseBytes,
        IReadOnlyList<SwShFairyGymBoostAnswerSlot> VanillaSlots,
        IReadOnlyList<SwShFairyGymBoostAnswerPatch> Patches,
        byte[] Output,
        bool DeleteTarget);
}
