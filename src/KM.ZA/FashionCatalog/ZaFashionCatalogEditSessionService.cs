// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security;
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.ZA.Workflows;

namespace KM.ZA.FashionCatalog;

internal sealed class ZaFashionCatalogEditSessionService
{
    private const string PendingFieldPrefix = "catalogField:";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaFashionCatalogWorkflowService workflowService;
    private readonly ZaFashionCatalogService catalogService;

    public ZaFashionCatalogEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaFashionCatalogWorkflowService? workflowService = null,
        ZaFashionCatalogService? catalogService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.catalogService = catalogService ?? new ZaFashionCatalogService();
        this.workflowService = workflowService
            ?? new ZaFashionCatalogWorkflowService(catalogService: this.catalogService);
    }

    public ZaFashionCatalogStageResult StageFieldEdit(
        ProjectPaths paths,
        EditSession? session,
        ZaFashionCatalogFieldEdit operation)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(operation);
        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        if (currentSession.PendingEdits.Any(edit => !string.Equals(
                edit.Domain,
                ZaFashionCatalogWorkflowService.Domain,
                StringComparison.Ordinal)))
        {
            diagnostics.Add(Error(
                "Fashion Catalog edits need their own edit session.",
                expected: "A Fashion Catalog-only edit session"));
            return new ZaFashionCatalogStageResult(
                workflowService.Load(project),
                currentSession,
                diagnostics);
        }

        if (!workflowService.TryLoadState(project, out var state, out var blockedWorkflow))
        {
            diagnostics.AddRange(blockedWorkflow!.Diagnostics);
            return new ZaFashionCatalogStageResult(
                blockedWorkflow,
                currentSession,
                diagnostics);
        }

        diagnostics.AddRange(state!.Workflow.Diagnostics);
        if (!state.Workflow.CanStage)
        {
            diagnostics.Add(Error(
                "Fashion Catalog requires valid editable project paths and complete supported source tables.",
                expected: "Editable Pokemon Legends Z-A project"));
            return new ZaFashionCatalogStageResult(
                state.Workflow,
                currentSession,
                diagnostics);
        }

        var replay = Replay(state, currentSession.PendingEdits, diagnostics);
        if (replay is null)
        {
            return new ZaFashionCatalogStageResult(
                state.Workflow,
                currentSession,
                diagnostics);
        }

        if (!ValidateOperationShape(operation, diagnostics))
        {
            return new ZaFashionCatalogStageResult(
                OverlayWorkflow(state.Workflow, replay.Snapshot),
                currentSession,
                diagnostics);
        }

        ZaFashionCatalogEditResult editResult;
        try
        {
            editResult = ApplyFieldEdit(replay.Sources, operation);
        }
        catch (Exception exception) when (exception is
            InvalidDataException or
            ArgumentException or
            OverflowException or
            FormatException)
        {
            diagnostics.Add(Error(
                $"Fashion Catalog edit was not staged: {exception.Message}",
                field: operation.Field,
                expected: "A value proven by the exact loaded catalog and current staged revision"));
            return new ZaFashionCatalogStageResult(
                OverlayWorkflow(state.Workflow, replay.Snapshot),
                currentSession,
                diagnostics);
        }

        var pendingEdit = new PendingEdit(
            ZaFashionCatalogWorkflowService.Domain,
            CreateSummary(operation),
            state.SourcesReferences,
            operation.Binding.PhysicalRowId,
            CreatePendingField(operation),
            JsonSerializer.Serialize(operation, PayloadOptions));
        var updatedSession = currentSession with
        {
            PendingEdits = currentSession.PendingEdits.Append(pendingEdit).ToArray(),
        };
        diagnostics.Add(Info(
            $"Staged {FormatFile(operation.CatalogFile)} row {operation.Binding.PhysicalIndex + 1} field '{operation.Field}'."));
        return new ZaFashionCatalogStageResult(
            OverlayWorkflow(state.Workflow, editResult.Snapshot),
            updatedSession,
            diagnostics);
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        var diagnostics = new List<ValidationDiagnostic>();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(Error(
                "Fashion Catalog has no staged edits to validate.",
                expected: "At least one pending Fashion Catalog field edit"));
        }

        if (session.PendingEdits.Any(edit => !string.Equals(
                edit.Domain,
                ZaFashionCatalogWorkflowService.Domain,
                StringComparison.Ordinal)))
        {
            diagnostics.Add(Error(
                "Fashion Catalog validation cannot mix editor domains.",
                expected: "Fashion Catalog-only pending edits"));
        }

        var project = projectWorkspaceService.Open(paths);
        if (!workflowService.TryLoadState(project, out var state, out var blockedWorkflow))
        {
            diagnostics.AddRange(blockedWorkflow!.Diagnostics);
            return new ZaEditSessionValidation(session, IsValid: false, diagnostics);
        }

        diagnostics.AddRange(state!.Workflow.Diagnostics);
        var replay = Replay(state, session.PendingEdits, diagnostics);
        if (replay is not null
            && session.PendingEdits.Count > 0
            && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(Info(
                $"The {session.PendingEdits.Count} staged Fashion Catalog edits compose into one valid reviewed catalog state."));
        }

        return new ZaEditSessionValidation(
            session,
            replay is not null
                && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
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
            () => CreateChangePlanCore(paths, session, outputMode),
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
            diagnostics.Add(Error(
                "The reviewed Fashion Catalog plan is stale. Review the exact current sources and staged edits again before applying.",
                expected: "Current reviewed Fashion Catalog change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ZaEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                diagnostics);
        }

        OutputApplyResult? outputTransaction = null;
        try
        {
            var project = projectWorkspaceService.Open(paths);
            if (!workflowService.TryLoadState(project, out var state, out var blockedWorkflow))
            {
                diagnostics.AddRange(blockedWorkflow!.Diagnostics);
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            var replayDiagnostics = new List<ValidationDiagnostic>();
            var replay = Replay(state!, session.PendingEdits, replayDiagnostics);
            diagnostics.AddRange(replayDiagnostics);
            if (replay is null
                || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            var reparsed = catalogService.CreateSnapshot(replay.Sources);
            if (!string.Equals(
                    reparsed.SourceRevision,
                    replay.Snapshot.SourceRevision,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(Error(
                    "The final Fashion Catalog output failed complete rebuild and reparse verification.",
                    expected: "Exact reviewed fixed-count catalog state"));
                return ZaEditSessionSupport.CreateApplyResult(
                    applyId,
                    appliedAt,
                    currentPlan,
                    writtenFiles,
                    diagnostics);
            }

            var writes = replay.ChangedFiles
                .OrderBy(file => file)
                .Select(file => new ZaWorkflowFileWrite(
                    GetVirtualPath(file),
                    GetBytes(replay.Sources, file)))
                .ToArray();
            outputTransaction = ZaWorkflowFileSource.WriteBatch(
                paths,
                writes,
                outputMode,
                revalidateReviewedState: () =>
                    ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(
                        reviewedPlan,
                        CreateChangePlan(paths, session, outputMode)));
            foreach (var file in replay.ChangedFiles.OrderBy(file => file))
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedReference(
                    GetVirtualPath(file),
                    outputMode));
            }

            if (outputMode == ZaOutputMode.Standalone)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(Info(
                $"Applied {session.PendingEdits.Count} composed Fashion Catalog edits across {replay.ChangedFiles.Count} catalog files. "
                + ZaEditSessionSupport.CreateApplyOutputMessage("Fashion Catalog", outputMode)));
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException)
        {
            if (exception is ZaOutputApplyNotCommittedException notCommitted)
            {
                outputTransaction = notCommitted.Result;
            }

            diagnostics.Add(Error(
                $"Fashion Catalog output could not be applied atomically: {exception.Message}",
                expected: "Fresh readable sources and a writable transactional output target"));
            writtenFiles.Clear();
        }

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles,
            diagnostics,
            outputTransaction);
    }

    private ChangePlan CreateChangePlanCore(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode)
    {
        var validation = Validate(paths, session);
        var diagnostics = validation.Diagnostics.ToList();
        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(Error(
                "Create a pending Fashion Catalog edit before reviewing a change plan.",
                expected: "Pending Fashion Catalog edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var changedFiles = new HashSet<ZaFashionCatalogFile>();
        foreach (var edit in session.PendingEdits)
        {
            if (TryDecodePendingEdit(edit, diagnostics, out var operation))
            {
                changedFiles.Add(operation!.CatalogFile);
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        try
        {
            var sources = session.PendingEdits
                .SelectMany(edit => edit.Sources)
                .Distinct()
                .ToArray();
            var writes = changedFiles
                .OrderBy(file => file)
                .Select(file =>
                {
                    var info = ZaWorkflowFileSource.CreatePlannedWrite(
                        paths,
                        GetVirtualPath(file),
                        sources,
                        outputMode);
                    return new PlannedFileWrite(
                        info.TargetRelativePath,
                        info.Sources,
                        info.ReplacesExistingOutput,
                        $"Apply composed Fashion Catalog edits to {FormatFile(file)}.");
                })
                .ToList();
            if (outputMode == ZaOutputMode.Standalone)
            {
                var descriptor = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptor.TargetRelativePath,
                    descriptor.Sources,
                    descriptor.ReplacesExistingOutput,
                    "Patch Pokemon Legends Z-A Trinity descriptor for standalone LayeredFS overrides."));
            }

            diagnostics.Add(Info($"Change plan preview contains {writes.Count} target files."));
            return new ChangePlan(session.Id, writes, diagnostics);
        }
        catch (Exception exception) when (exception is
            IOException or
            InvalidOperationException or
            ArgumentException or
            UnauthorizedAccessException)
        {
            diagnostics.Add(Error(
                $"Fashion Catalog change plan could not resolve the output targets: {exception.Message}",
                expected: "Writable output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }
    }

    private ReplayResult? Replay(
        ZaFashionCatalogLoadedState state,
        IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sources = state.Sources;
        var snapshot = state.Workflow.Snapshot;
        var changedFiles = new HashSet<ZaFashionCatalogFile>();
        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            if (!SourcesMatch(edit.Sources, state.SourcesReferences))
            {
                diagnostics.Add(Error(
                    $"Staged Fashion Catalog edit {index + 1} does not match the current authoritative source set.",
                    expected: "Exact Fashion Catalog and shop-lineup source set"));
                return null;
            }

            if (!TryDecodePendingEdit(edit, diagnostics, out var operation))
            {
                return null;
            }

            try
            {
                var result = ApplyFieldEdit(sources, operation!);
                sources = result.Sources;
                snapshot = result.Snapshot;
                changedFiles.Add(result.ChangedFile);
            }
            catch (Exception exception) when (exception is
                InvalidDataException or
                ArgumentException or
                OverflowException or
                FormatException)
            {
                diagnostics.Add(Error(
                    $"Staged Fashion Catalog edit {index + 1} no longer applies: {exception.Message}",
                    field: operation!.Field,
                    expected: "Sequential edit bound to the exact preceding staged catalog revision"));
                return null;
            }
        }

        return new ReplayResult(sources, snapshot, changedFiles);
    }

    private ZaFashionCatalogEditResult ApplyFieldEdit(
        ZaFashionCatalogSourceSet sources,
        ZaFashionCatalogFieldEdit operation)
    {
        return operation.CatalogFile switch
        {
            ZaFashionCatalogFile.DressUpItems => catalogService.UpdateDressUpItem(
                sources,
                operation.Binding,
                CreateDressUpItemPatch(operation)),
            ZaFashionCatalogFile.DressUpGroups => catalogService.UpdateDressUpGroup(
                sources,
                operation.Binding,
                CreateDressUpGroupPatch(operation)),
            ZaFashionCatalogFile.HairAndMakeup => catalogService.UpdateHairAndMakeup(
                sources,
                operation.Binding,
                CreateHairAndMakeupPatch(operation)),
            ZaFashionCatalogFile.DressUpLineups or
            ZaFashionCatalogFile.HairAndMakeupLineups => catalogService.UpdateLineupEntry(
                sources,
                operation.CatalogFile,
                operation.Binding,
                CreateLineupEntryPatch(operation)),
            _ => throw new InvalidDataException("The Fashion Catalog source file is not supported."),
        };
    }

    private static ZaDressUpItemPatch CreateDressUpItemPatch(ZaFashionCatalogFieldEdit operation)
    {
        EnsureNotClear(operation);
        return operation.Field switch
        {
            ZaFashionCatalogFields.ItemId => new(ItemId: ParseUInt(operation)),
            ZaFashionCatalogFields.ModelPart => new(ModelPart: RequireValue(operation)),
            ZaFashionCatalogFields.CatalogGroupCode => new(CatalogGroupCode: ParseUInt(operation)),
            ZaFashionCatalogFields.ModelVariant => new(ModelVariant: RequireValue(operation)),
            ZaFashionCatalogFields.CategoryCode => new(CategoryCode: ParseUInt(operation)),
            ZaFashionCatalogFields.ColorVariantCode => new(ColorVariantCode: ParseUInt(operation)),
            ZaFashionCatalogFields.PrimaryColorLabel => new(PrimaryColorLabel: RequireValue(operation)),
            ZaFashionCatalogFields.SecondaryColorLabel => new(SecondaryColorLabel: RequireValue(operation)),
            ZaFashionCatalogFields.DisplayOrder => new(DisplayOrder: ParseUInt(operation)),
            ZaFashionCatalogFields.VariantOrder => new(VariantOrder: ParseUInt(operation)),
            _ => throw UnsupportedField(operation),
        };
    }

    private static ZaDressUpGroupPatch CreateDressUpGroupPatch(ZaFashionCatalogFieldEdit operation)
    {
        EnsureNotClear(operation);
        return operation.Field switch
        {
            ZaFashionCatalogFields.ModelPart => new(ModelPart: RequireValue(operation)),
            ZaFashionCatalogFields.DisplayOrder => new(DisplayOrder: ParseUInt(operation)),
            ZaFashionCatalogFields.DisplayLabel => new(DisplayLabel: RequireValue(operation)),
            _ => throw UnsupportedField(operation),
        };
    }

    private static ZaHairAndMakeupPatch CreateHairAndMakeupPatch(
        ZaFashionCatalogFieldEdit operation)
    {
        if (operation.Clear
            && operation.Field is not ZaFashionCatalogFields.ColorValue
                and not ZaFashionCatalogFields.LabelKey)
        {
            throw new InvalidDataException(
                $"Fashion Catalog field '{operation.Field}' cannot be cleared.");
        }

        return operation.Field switch
        {
            ZaFashionCatalogFields.ItemId => new(ItemId: ParseUInt(operation)),
            ZaFashionCatalogFields.ModelKey => new(ModelKey: RequireValue(operation)),
            ZaFashionCatalogFields.CatalogTypeCode => new(CatalogTypeCode: ParseUInt(operation)),
            ZaFashionCatalogFields.ColorValue => new(
                ColorValue: operation.Clear
                    ? ZaOptionalCatalogText.Clear()
                    : ZaOptionalCatalogText.Set(RequireValue(operation))),
            ZaFashionCatalogFields.LabelKey => new(
                LabelKey: operation.Clear
                    ? ZaOptionalCatalogText.Clear()
                    : ZaOptionalCatalogText.Set(RequireValue(operation))),
            ZaFashionCatalogFields.DisplayOrder => new(DisplayOrder: ParseUInt(operation)),
            ZaFashionCatalogFields.GroupCode => new(GroupCode: ParseInt(operation)),
            ZaFashionCatalogFields.VariantCode => new(VariantCode: ParseInt(operation)),
            _ => throw UnsupportedField(operation),
        };
    }

    private static ZaFashionLineupEntryPatch CreateLineupEntryPatch(
        ZaFashionCatalogFieldEdit operation)
    {
        EnsureNotClear(operation);
        return operation.Field == ZaFashionCatalogFields.ItemId
            ? new ZaFashionLineupEntryPatch(ParseUInt(operation))
            : throw UnsupportedField(operation);
    }

    private static bool TryDecodePendingEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics,
        out ZaFashionCatalogFieldEdit? operation)
    {
        operation = null;
        if (!string.Equals(edit.Domain, ZaFashionCatalogWorkflowService.Domain, StringComparison.Ordinal)
            || edit.Field?.StartsWith(PendingFieldPrefix, StringComparison.Ordinal) != true
            || string.IsNullOrWhiteSpace(edit.NewValue))
        {
            diagnostics.Add(Error(
                "The pending edit is not a supported Fashion Catalog field operation.",
                expected: "Versioned Fashion Catalog field payload"));
            return false;
        }

        try
        {
            operation = JsonSerializer.Deserialize<ZaFashionCatalogFieldEdit>(
                edit.NewValue,
                PayloadOptions);
        }
        catch (JsonException)
        {
        }

        if (operation is null
            || !ValidateOperationShape(operation, diagnostics)
            || !string.Equals(edit.RecordId, operation.Binding.PhysicalRowId, StringComparison.Ordinal)
            || !string.Equals(edit.Field, CreatePendingField(operation), StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "The pending Fashion Catalog operation identity does not match its payload.",
                expected: "Exact physical row, catalog file, and field identity"));
            return false;
        }

        return true;
    }

    private static bool ValidateOperationShape(
        ZaFashionCatalogFieldEdit operation,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (operation.Binding is not null
            && !string.IsNullOrWhiteSpace(operation.Binding.SourceRevision)
            && operation.Binding.PhysicalIndex >= 0
            && !string.IsNullOrWhiteSpace(operation.Binding.PhysicalRowId)
            && !string.IsNullOrWhiteSpace(operation.Binding.RowRevision)
            && !string.IsNullOrWhiteSpace(operation.Field)
            && ((operation.Clear && operation.Value is null)
                || (!operation.Clear && operation.Value is not null)))
        {
            return true;
        }

        diagnostics.Add(Error(
            "A Fashion Catalog edit requires an exact source revision, physical row identity, field, and explicit value or clear action.",
            expected: "Complete revision-bound Fashion Catalog field edit"));
        return false;
    }

    private static uint ParseUInt(ZaFashionCatalogFieldEdit operation)
    {
        EnsureNotClear(operation);
        if (!uint.TryParse(
                RequireValue(operation),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new InvalidDataException(
                $"Fashion Catalog field '{operation.Field}' must be an unsigned 32-bit integer.");
        }

        return value;
    }

    private static int ParseInt(ZaFashionCatalogFieldEdit operation)
    {
        EnsureNotClear(operation);
        if (!int.TryParse(
                RequireValue(operation),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new InvalidDataException(
                $"Fashion Catalog field '{operation.Field}' must be a signed 32-bit integer.");
        }

        return value;
    }

    private static string RequireValue(ZaFashionCatalogFieldEdit operation) =>
        operation.Value
            ?? throw new InvalidDataException(
                $"Fashion Catalog field '{operation.Field}' requires a value.");

    private static void EnsureNotClear(ZaFashionCatalogFieldEdit operation)
    {
        if (operation.Clear)
        {
            throw new InvalidDataException(
                $"Fashion Catalog field '{operation.Field}' cannot be cleared.");
        }
    }

    private static InvalidDataException UnsupportedField(ZaFashionCatalogFieldEdit operation) =>
        new(
            $"Fashion Catalog field '{operation.Field}' is not supported for {FormatFile(operation.CatalogFile)}.");

    private static string CreatePendingField(ZaFashionCatalogFieldEdit operation) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{PendingFieldPrefix}{(int)operation.CatalogFile}:{operation.Field}");

    private static string CreateSummary(ZaFashionCatalogFieldEdit operation)
    {
        var value = operation.Clear ? "clear" : operation.Value;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Set {FormatFile(operation.CatalogFile)} row {operation.Binding.PhysicalIndex + 1} {operation.Field} to '{value}'.");
    }

    private static string GetVirtualPath(ZaFashionCatalogFile file) => file switch
    {
        ZaFashionCatalogFile.DressUpItems => Data.ZaDataPaths.DressUpDataArray,
        ZaFashionCatalogFile.DressUpGroups => ZaFashionCatalogWorkflowService.DressUpGroupCatalogPath,
        ZaFashionCatalogFile.HairAndMakeup => Data.ZaDataPaths.HairMakeDataArray,
        ZaFashionCatalogFile.DressUpLineups => Data.ZaDataPaths.ShopDressUpLineupArray,
        ZaFashionCatalogFile.HairAndMakeupLineups => Data.ZaDataPaths.ShopHairMakeLineupArray,
        _ => throw new InvalidDataException("The Fashion Catalog source file is not supported."),
    };

    private static byte[] GetBytes(
        ZaFashionCatalogSourceSet sources,
        ZaFashionCatalogFile file) => file switch
        {
            ZaFashionCatalogFile.DressUpItems => sources.DressUpItems,
            ZaFashionCatalogFile.DressUpGroups => sources.DressUpGroups,
            ZaFashionCatalogFile.HairAndMakeup => sources.HairAndMakeup,
            ZaFashionCatalogFile.DressUpLineups => sources.DressUpLineups,
            ZaFashionCatalogFile.HairAndMakeupLineups => sources.HairAndMakeupLineups,
            _ => throw new InvalidDataException("The Fashion Catalog source file is not supported."),
        };

    private static string FormatFile(ZaFashionCatalogFile file) => file switch
    {
        ZaFashionCatalogFile.DressUpItems => "dress-up items",
        ZaFashionCatalogFile.DressUpGroups => "dress-up groups",
        ZaFashionCatalogFile.HairAndMakeup => "hair and makeup",
        ZaFashionCatalogFile.DressUpLineups => "dress-up shop lineup",
        ZaFashionCatalogFile.HairAndMakeupLineups => "hair and makeup shop lineup",
        _ => "unknown catalog",
    };

    private static bool SourcesMatch(
        IReadOnlyList<ProjectFileReference> left,
        IReadOnlyList<ProjectFileReference> right) =>
        left.OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .SequenceEqual(right.OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal));

    private static ZaFashionCatalogWorkflow OverlayWorkflow(
        ZaFashionCatalogWorkflow workflow,
        ZaFashionCatalogSnapshot snapshot) =>
        workflow with
        {
            Snapshot = snapshot,
            Stats = new ZaFashionCatalogWorkflowStats(
                snapshot.DressUpItems.Count,
                snapshot.DressUpGroups.Count,
                snapshot.HairAndMakeup.Count,
                snapshot.DressUpLineups.Count,
                snapshot.HairAndMakeupLineups.Count),
        };

    private static ValidationDiagnostic Error(
        string message,
        string? field = null,
        string? expected = null) =>
        ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaFashionCatalogWorkflowService.Domain,
            field: field,
            expected: expected,
            code: ZaFashionCatalogDiagnosticCodes.EditSafety);

    private static ValidationDiagnostic Info(string message) =>
        ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info,
            message,
            ZaFashionCatalogWorkflowService.Domain,
            code: ZaFashionCatalogDiagnosticCodes.ReviewedState);

    private sealed record ReplayResult(
        ZaFashionCatalogSourceSet Sources,
        ZaFashionCatalogSnapshot Snapshot,
        IReadOnlySet<ZaFashionCatalogFile> ChangedFiles);
}
