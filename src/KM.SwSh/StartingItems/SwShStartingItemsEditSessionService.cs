// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.BagHook;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.StartingItems;

public sealed class SwShStartingItemsEditSessionService
{
    public const string StartingItemsEditDomain = "workflow.startingItems";

    private const string RecordId = "starting-items";
    private const string GrantsField = "grants";

    private readonly SwShStartingItemsWorkflowService startingItemsWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShStartingItemsEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShStartingItemsWorkflowService? startingItemsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.startingItemsWorkflowService = startingItemsWorkflowService ?? new SwShStartingItemsWorkflowService();
    }

    public SwShStartingItemsEditResult StageGrants(
        ProjectPaths paths,
        IReadOnlyList<SwShStartingItemGrantSelection> grants,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(grants);

        var currentSession = session ?? EditSession.Start();
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = startingItemsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (currentSession.PendingEdits.Any(edit => !string.Equals(edit.Domain, StartingItemsEditDomain, StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items needs its own edit session before staging.",
                expected: "A Starting Items-only edit session"));
            return new SwShStartingItemsEditResult(workflow, currentSession, diagnostics);
        }

        var normalized = NormalizeGrants(
            workflow.ItemOptions,
            startingItemsWorkflowService.HasInstalledRoyalCandy(project),
            grants,
            diagnostics);
        if (!CanStage(project, workflow, diagnostics)
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShStartingItemsEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = currentSession with
        {
            PendingEdits = [CreatePendingEdit(normalized, startingItemsWorkflowService.GetPlanSources(project))],
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "Starting Items grants are staged for change-plan review."));

        return new SwShStartingItemsEditResult(workflow, updatedSession, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = startingItemsWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage Starting Items grants before validating."
                    : "Starting Items requires exactly one canonical grants edit.",
                expected: "Exactly one pending Starting Items grants edit"));
        }
        else
        {
            var edit = session.PendingEdits[0];
            if (!string.Equals(edit.Domain, StartingItemsEditDomain, StringComparison.Ordinal)
                || !string.Equals(edit.RecordId, RecordId, StringComparison.Ordinal)
                || !string.Equals(edit.Field, GrantsField, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending edit does not target the Starting Items grants record.",
                    field: edit.Field,
                    expected: $"{StartingItemsEditDomain}/{RecordId}/{GrantsField}"));
            }
            else
            {
                var parsedGrants = ParsePendingGrants(edit.NewValue, diagnostics);
                var normalized = NormalizeGrants(
                    workflow.ItemOptions,
                    startingItemsWorkflowService.HasInstalledRoyalCandy(project),
                    parsedGrants,
                    diagnostics);
                if (!string.Equals(edit.NewValue, SerializeGrants(normalized), StringComparison.Ordinal))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending Starting Items grants are not in the canonical staged format.",
                        field: GrantsField,
                        expected: "Unique, ordered slot:itemId:quantity entries produced by Starting Items staging"));
                }
            }
        }

        CanStage(project, workflow, diagnostics);
        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Starting Items grants are valid for change-plan review."));
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

        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (targetPath is null)
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var canonicalPayload = session.PendingEdits[0].NewValue ?? string.Empty;
        var sources = startingItemsWorkflowService.GetPlanSources(project)
            .Append(CreatePendingPayloadSource(canonicalPayload))
            .ToArray();
        var writes = new[]
        {
            new PlannedFileWrite(
                SwShBagHookWorkflowService.BagEventScriptPath,
                sources,
                File.Exists(targetPath),
                "Update Bag Hook slots 2-20 with reviewed Starting Items grants. Slot 1 remains reserved for Royal Candy."),
        };

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(CultureInfo.InvariantCulture, $"Starting Items change plan preview contains {writes.Length:N0} target file(s).")));

        return SwShChangePlanSourceGuard.Capture(
            paths,
            new ChangePlan(session.Id, writes, diagnostics));
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

        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed Starting Items change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Starting Items change plan"));
        }

        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var grants = ParsePendingGrants(session.PendingEdits[0].NewValue, diagnostics)
            .Where(grant => grant.ItemId is not null)
            .ToDictionary(grant => grant.Slot);
        var project = projectWorkspaceService.Open(paths);
        var source = ResolveWorkflowFile(project, SwShBagHookWorkflowService.BagEventScriptPath);
        var targetPath = ResolveOutputPath(paths, diagnostics);
        if (source is null || targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items source or output target could not be resolved.",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Installed Bag Hook V2 and writable LayeredFS target"));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var sourceBytes = File.ReadAllBytes(source.AbsolutePath);
            var sourceAnalysis = SwShBagHookAmxPatcher.Analyze(sourceBytes);
            if (sourceAnalysis.Kind != SwShBagHookInstallKind.InstalledV2)
            {
                throw new InvalidDataException("Bag Hook V2 is no longer installed in the reviewed source.");
            }

            var patches = Enumerable.Range(
                    SwShBagHookAmxPatcher.FirstStartingItemSlot,
                    SwShBagHookAmxPatcher.LastStartingItemSlot - SwShBagHookAmxPatcher.FirstStartingItemSlot + 1)
                .Select(slot => grants.TryGetValue(slot, out var grant)
                    ? new SwShBagHookSlotPatch(slot, grant.ItemId, grant.Quantity)
                    : new SwShBagHookSlotPatch(slot, null, null))
                .ToArray();
            var output = SwShBagHookAmxPatcher.ApplySlotPatches(sourceBytes, patches);
            WriteOutputAtomically(
                targetPath,
                output,
                roundTrip => VerifyOutput(sourceAnalysis, roundTrip, grants));
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, SwShBagHookWorkflowService.BagEventScriptPath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Starting Items grants to Bag Hook slots 2-20 in the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Starting Items source file could not be patched: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Installed Bag Hook V2 with readable slots 1-20"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Starting Items output file could not be written: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Writable output root"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Starting Items output file could not be written: {exception.Message}",
                file: SwShBagHookWorkflowService.BagEventScriptPath,
                expected: "Writable output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static IReadOnlyList<SwShStartingItemGrantSelection> NormalizeGrants(
        IReadOnlyList<SwShStartingItemOptionRecord> itemOptions,
        bool royalCandyInstalled,
        IReadOnlyList<SwShStartingItemGrantSelection> grants,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var itemLookup = itemOptions.ToDictionary(item => item.ItemId);
        var normalized = new List<SwShStartingItemGrantSelection>();
        var seenSlots = new HashSet<int>();
        foreach (var grant in grants)
        {
            if (grant.Slot is < SwShBagHookAmxPatcher.FirstStartingItemSlot or > SwShBagHookAmxPatcher.LastStartingItemSlot)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting Items slot {grant.Slot} is not available.",
                    field: GrantsField,
                    expected: "Bag Hook slots 2-20 only"));
                continue;
            }

            if (!seenSlots.Add(grant.Slot))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting Items slot {grant.Slot} was supplied more than once.",
                    field: GrantsField,
                    expected: "One item per slot"));
                continue;
            }

            if (grant.ItemId is null or 0)
            {
                normalized.Add(new SwShStartingItemGrantSelection(grant.Slot, null, 1));
                continue;
            }

            if (!itemLookup.TryGetValue(grant.ItemId.Value, out var item))
            {
                if (royalCandyInstalled && grant.ItemId.Value == SwShBagHookAmxPatcher.RoyalCandyItemId)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Royal Candy and EXP Candy XL are reserved by Royal Candy while Royal Candy is installed.",
                        field: GrantsField,
                        expected: "Use Royal Candy slot 1; choose a different Starting Items item for slots 2-20"));
                    continue;
                }

                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting item id {grant.ItemId} is not available.",
                    field: GrantsField,
                    expected: "Known item id"));
                continue;
            }

            if (royalCandyInstalled && SwShStartingItemsWorkflowService.IsReservedRoyalCandyStartingItem(item.ItemId, item.Name))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Royal Candy and EXP Candy XL cannot be added through Starting Items while Royal Candy is installed.",
                    field: GrantsField,
                    expected: "Use Royal Candy slot 1; choose a different Starting Items item for slots 2-20"));
                continue;
            }

            var quantity = item.IsKeyItem ? 1 : grant.Quantity;
            if (quantity is < 1 or > 999)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting item '{item.Name}' quantity must be between 1 and 999.",
                    field: GrantsField,
                    expected: "Quantity 1-999"));
                continue;
            }

            normalized.Add(new SwShStartingItemGrantSelection(grant.Slot, grant.ItemId, quantity));
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? Array.Empty<SwShStartingItemGrantSelection>()
            : normalized.OrderBy(grant => grant.Slot).ToArray();
    }

    private static IReadOnlyList<SwShStartingItemGrantSelection> ParsePendingGrants(
        string? value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var grants = new List<SwShStartingItemGrantSelection>();
        var seenSlots = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return grants;
        }

        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = part.Split(':', StringSplitOptions.TrimEntries);
            if (fields.Length != 3
                || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
                || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var quantity))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting Items entry '{part}' is not valid.",
                    field: GrantsField,
                    expected: "slot:itemId:quantity"));
                continue;
            }

            if (slot is < SwShBagHookAmxPatcher.FirstStartingItemSlot or > SwShBagHookAmxPatcher.LastStartingItemSlot)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting Items slot {slot} is not available.",
                    field: GrantsField,
                    expected: "Bag Hook slots 2-20 only"));
                continue;
            }

            if (!seenSlots.Add(slot))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Starting Items slot {slot} was supplied more than once.",
                    field: GrantsField,
                    expected: "Unique slot entries"));
                continue;
            }

            grants.Add(new SwShStartingItemGrantSelection(slot, itemId == 0 ? null : itemId, quantity));
        }

        return grants;
    }

    private static bool CanStage(
        OpenedProject project,
        SwShStartingItemsWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows || workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items apply requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.InstallStatus != "available")
        {
            var message = workflow.BlockerKind switch
            {
                SwShStartingItemsWorkflowService.BagHookMissingBlockerKind =>
                    "Starting Items requires installed Bag Hook V2 before staging grants.",
                SwShStartingItemsWorkflowService.BagHookDamagedBlockerKind =>
                    "Starting Items cannot stage grants while the Bag Hook slot bank is damaged or incompatible.",
                SwShStartingItemsWorkflowService.ItemMetadataUnavailableBlockerKind =>
                    "Starting Items cannot stage grants until item metadata is readable.",
                _ => "Starting Items is not currently available for staging.",
            };
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                message,
                expected: "Available Starting Items workflow"));
        }

        foreach (var diagnostic in workflow.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(diagnostic);
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static PendingEdit CreatePendingEdit(
        IReadOnlyList<SwShStartingItemGrantSelection> grants,
        IReadOnlyList<ProjectFileReference> sources)
    {
        return new PendingEdit(
            StartingItemsEditDomain,
            "Stage Starting Items grants in Bag Hook slots 2-20.",
            sources,
            RecordId,
            GrantsField,
            SerializeGrants(grants));
    }

    private static string SerializeGrants(IReadOnlyList<SwShStartingItemGrantSelection> grants)
    {
        return string.Join(
            ';',
            grants
                .Where(grant => grant.ItemId is not null)
                .OrderBy(grant => grant.Slot)
                .Select(grant => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{grant.Slot}:{grant.ItemId}:{grant.Quantity}")));
    }

    private static ProjectFileReference CreatePendingPayloadSource(string canonicalPayload)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
        return new ProjectFileReference(
            ProjectFileLayer.Pending,
            $"pending/starting-items/{hash}");
    }

    private static void VerifyOutput(
        SwShBagHookAnalysis sourceAnalysis,
        byte[] output,
        IReadOnlyDictionary<int, SwShStartingItemGrantSelection> grants)
    {
        var outputAnalysis = SwShBagHookAmxPatcher.Analyze(output);
        if (outputAnalysis.Kind != SwShBagHookInstallKind.InstalledV2)
        {
            throw new InvalidDataException("Patched Bag-event script did not round-trip as Bag Hook V2.");
        }

        var sourceRoyalSlot = sourceAnalysis.Slots.Single(slot => slot.Slot == SwShBagHookAmxPatcher.RoyalCandySlot);
        var outputRoyalSlot = outputAnalysis.Slots.Single(slot => slot.Slot == SwShBagHookAmxPatcher.RoyalCandySlot);
        if (sourceRoyalSlot.Status != outputRoyalSlot.Status
            || sourceRoyalSlot.ItemId != outputRoyalSlot.ItemId
            || sourceRoyalSlot.Quantity != outputRoyalSlot.Quantity)
        {
            throw new InvalidDataException("Starting Items patch changed the Royal Candy slot.");
        }

        foreach (var slot in outputAnalysis.Slots.Where(slot => slot.Slot is >=
            SwShBagHookAmxPatcher.FirstStartingItemSlot and <= SwShBagHookAmxPatcher.LastStartingItemSlot))
        {
            if (grants.TryGetValue(slot.Slot, out var grant))
            {
                if (slot.Status != "occupied" || slot.ItemId != grant.ItemId || slot.Quantity != grant.Quantity)
                {
                    throw new InvalidDataException($"Starting Items slot {slot.Slot} did not round-trip with the reviewed grant.");
                }
            }
            else if (slot.Status != "empty" || slot.ItemId is not null || slot.Quantity is not null)
            {
                throw new InvalidDataException($"Starting Items slot {slot.Slot} did not round-trip as empty.");
            }
        }
    }

    private static void WriteOutputAtomically(string targetPath, byte[] output, Action<byte[]> verifyRoundTrip)
    {
        var directoryPath = Path.GetDirectoryName(targetPath)
            ?? throw new IOException("Starting Items output directory could not be resolved.");
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
                throw new IOException("Starting Items temporary output did not round-trip byte-for-byte.");
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

    private static string? ResolveOutputPath(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items apply requires a configured output root.",
                expected: "Valid output root"));
            return null;
        }

        var targetPath = SwShBagHookWorkflowService.ResolveOutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Starting Items target must stay inside the configured output root.",
                expected: "Output-root-contained target"));
        }

        return targetPath;
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

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = SwShBagHookWorkflowService.ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
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
            Domain: StartingItemsEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}
