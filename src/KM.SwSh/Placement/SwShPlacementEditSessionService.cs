// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Globalization;
using System.Text;
using PlacementArchiveField = KM.Formats.SwSh.SwShPlacementEditableField;

namespace KM.SwSh.Placement;

public sealed class SwShPlacementEditSessionService
{
    private const string PlacementEditDomain = "workflow.placement";

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShPlacementWorkflowService placementWorkflowService;

    public SwShPlacementEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShPlacementWorkflowService? placementWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.placementWorkflowService = placementWorkflowService ?? new SwShPlacementWorkflowService();
    }

    public EditSession StartSession()
    {
        return EditSession.Start();
    }

    public SwShPlacementEditResult UpdateObjectField(
        ProjectPaths paths,
        EditSession? session,
        string objectId,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var result = UpdateObjectFields(
            paths,
            session,
            [new SwShPlacementObjectFieldUpdate(objectId, field, value)]);

        var project = projectWorkspaceService.Open(paths);
        var fullWorkflow = placementWorkflowService.Load(project);
        IReadOnlyDictionary<int, ulong>? itemHashes = null;
        var diagnostics = result.Diagnostics.ToList();
        if (result.Session.PendingEdits.Any(edit =>
            edit.Domain == PlacementEditDomain
            && edit.Field == SwShPlacementWorkflowService.ItemIdField
            && edit.RecordId is not null
            && fullWorkflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == edit.RecordId) is { } placedObject
            && RequiresItemHash(placedObject)))
        {
            itemHashes = LoadItemHashes(project, diagnostics);
        }

        return result with
        {
            Workflow = OverlayPendingEdits(fullWorkflow, result.Session.PendingEdits, itemHashes),
            Diagnostics = diagnostics,
            UpdatedObjects = null,
        };
    }

    public SwShPlacementEditResult UpdateObjectFields(
        ProjectPaths paths,
        EditSession? session,
        IReadOnlyList<SwShPlacementObjectFieldUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(updates);

        var currentSession = session ?? StartSession();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = placementWorkflowService.LoadForEditing(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanEditPlacement(project, loadedWorkflow, diagnostics))
        {
            return new SwShPlacementEditResult(
                OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits),
                currentSession,
                diagnostics,
                []);
        }

        var objectIndexes = loadedWorkflow.Objects
            .Select((placedObject, index) => (placedObject.ObjectId, index))
            .ToDictionary(entry => entry.ObjectId, entry => entry.index, StringComparer.Ordinal);
        var needsItemHashLookup = currentSession.PendingEdits.Any(edit =>
                edit.Domain == PlacementEditDomain
                && edit.Field == SwShPlacementWorkflowService.ItemIdField
                && edit.RecordId is not null
                && objectIndexes.TryGetValue(edit.RecordId, out var index)
                && RequiresItemHash(loadedWorkflow.Objects[index]))
            || updates.Any(update =>
            update is not null
            && string.Equals(update.Field, SwShPlacementWorkflowService.ItemIdField, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(update.ObjectId)
            && objectIndexes.TryGetValue(update.ObjectId, out var index)
            && RequiresItemHash(loadedWorkflow.Objects[index]));
        IReadOnlyDictionary<int, ulong>? itemHashes = null;
        var itemHashLookupAvailable = true;
        if (needsItemHashLookup)
        {
            var diagnosticCount = diagnostics.Count;
            itemHashes = LoadItemHashes(project, diagnostics);
            itemHashLookupAvailable = !diagnostics
                .Skip(diagnosticCount)
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        var currentWorkflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits, itemHashes);

        var candidateSession = currentSession;
        var workingObjects = currentWorkflow.Objects.ToArray();
        var stagedUpdates = new List<SwShPlacementObjectFieldUpdate>();
        var incomingStorageTargets = new Dictionary<PlacementStorageIdentity, PendingEdit>();
        foreach (var update in updates)
        {
            if (update is null
                || string.IsNullOrWhiteSpace(update.ObjectId)
                || string.IsNullOrWhiteSpace(update.Field)
                || update.Value is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Placement field update is missing object, field, or value metadata.",
                    expected: "Complete placement field update"));
                continue;
            }

            if (!objectIndexes.TryGetValue(update.ObjectId, out var objectIndex))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Placement object '{update.ObjectId}' is not present in the loaded workflow.",
                    field: "objectId",
                    expected: "Existing placement object"));
                continue;
            }

            var placedObject = workingObjects[objectIndex];
            var pendingEdit = CreatePendingEdit(
                placedObject,
                update.Field,
                update.Value,
                itemHashes,
                itemHashLookupAvailable,
                diagnostics);
            if (pendingEdit is null)
            {
                continue;
            }

            if (TryCreateStorageIdentity(loadedWorkflow, pendingEdit, out var incomingIdentity))
            {
                if (incomingStorageTargets.TryGetValue(incomingIdentity, out var earlierEdit)
                    && (earlierEdit.RecordId != pendingEdit.RecordId
                        || earlierEdit.Field != pendingEdit.Field
                        || earlierEdit.NewValue != pendingEdit.NewValue))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Incoming placement updates '{earlierEdit.RecordId}:{earlierEdit.Field}' and '{pendingEdit.RecordId}:{pendingEdit.Field}' target the same underlying storage. Submit only one update for that value.",
                        field: "field",
                        expected: "One incoming update per placement storage value"));
                }

                incomingStorageTargets[incomingIdentity] = pendingEdit;
            }

            candidateSession = ReplacePendingPlacementEdit(loadedWorkflow, candidateSession, pendingEdit);
            var itemDisplayName = update.Field == SwShPlacementWorkflowService.ItemIdField
                && TryParseInt(pendingEdit.NewValue!, out var stagedItemId)
                    ? ResolveItemDisplayName(currentWorkflow, placedObject, stagedItemId)
                    : null;
            var itemHashDisplay = update.Field == SwShPlacementWorkflowService.ItemIdField
                && RequiresItemHash(placedObject)
                && TryParseInt(pendingEdit.NewValue!, out stagedItemId)
                && itemHashes?.TryGetValue(stagedItemId, out var stagedItemHash) == true
                    ? FormatHash(stagedItemHash)
                    : null;
            foreach (var affectedIndex in GetAffectedObjectIndexes(workingObjects, placedObject, update.Field))
            {
                workingObjects[affectedIndex] = OverlayPendingEdit(
                    workingObjects[affectedIndex],
                    update.Field,
                    pendingEdit.NewValue!,
                    itemDisplayName,
                    itemHashDisplay);
            }

            stagedUpdates.Add(update);
        }

        ValidateStorageConflicts(loadedWorkflow, candidateSession.PendingEdits, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShPlacementEditResult(currentWorkflow, currentSession, diagnostics, []);
        }

        var projectedWorkflow = currentWorkflow with { Objects = workingObjects };
        var updatedObjects = GetAffectedObjects(projectedWorkflow, stagedUpdates);
        return new SwShPlacementEditResult(
            projectedWorkflow,
            candidateSession,
            diagnostics,
            updatedObjects);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = placementWorkflowService.LoadForEditing(project);
        var projectedWorkflow = OverlayPendingEdits(workflow, session.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        CanEditPlacement(project, workflow, diagnostics);

        var needsItemHashLookup = session.PendingEdits.Any(edit =>
            edit.Domain == PlacementEditDomain
            && string.Equals(edit.Field, SwShPlacementWorkflowService.ItemIdField, StringComparison.Ordinal)
            && edit.RecordId is not null
            && workflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == edit.RecordId) is { } placedObject
            && RequiresItemHash(placedObject));
        IReadOnlyDictionary<int, ulong>? itemHashes = null;
        var itemHashLookupAvailable = true;
        if (needsItemHashLookup)
        {
            var diagnosticCount = diagnostics.Count;
            itemHashes = LoadItemHashes(project, diagnostics);
            itemHashLookupAvailable = !diagnostics
                .Skip(diagnosticCount)
                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, itemHashes, itemHashLookupAvailable, diagnostics);
        }

        ValidateStorageConflicts(workflow, session.PendingEdits, diagnostics);
        ValidateHiddenChancePools(projectedWorkflow, session.PendingEdits, diagnostics);

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending placement change is valid."));
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        return CreateChangePlan(paths, session, validateSession: true);
    }

    private ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, bool validateSession)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var diagnostics = validateSession
            ? Validate(paths, session).Diagnostics.ToList()
            : new List<ValidationDiagnostic>();
        if (!validateSession)
        {
            ValidatePendingEditEnvelope(session.PendingEdits, diagnostics);
        }

        if (session.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Placement edit before reviewing a change plan.",
                expected: "Pending placement edit"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShPlacementWorkflowService.ResolvePlacementDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Placement change plan could not resolve the source placement pack.",
                expected: SwShPlacementWorkflowService.PlacementDataPath));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var targetPath = SwShPlacementWorkflowService.ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Placement apply target must stay inside the configured output root.",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Output-root-contained target"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var write = new PlannedFileWrite(
            dataSource.GraphEntry.RelativePath,
            [new ProjectFileReference(GetSourceLayer(dataSource.GraphEntry), dataSource.GraphEntry.RelativePath)],
            File.Exists(targetPath),
            session.PendingEdits.Count == 1
                ? $"Apply pending Placement edit: {session.PendingEdits[0].Summary}"
                : $"Apply {session.PendingEdits.Count} pending Placement edits.");

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
        var currentPlan = CreateChangePlan(paths, session, validateSession: true);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Placement change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var project = projectWorkspaceService.Open(paths);
        var dataSource = SwShPlacementWorkflowService.ResolvePlacementDataSource(project);
        if (dataSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Placement apply could not resolve the source placement pack.",
                expected: SwShPlacementWorkflowService.PlacementDataPath));
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        var targetPath = ResolveOutputPath(paths, dataSource.GraphEntry.RelativePath, diagnostics);
        if (targetPath is null)
        {
            return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var pack = SwShGfPackFile.Parse(File.ReadAllBytes(dataSource.AbsolutePath));
            var workflow = placementWorkflowService.LoadForEditing(project);
            var needsItemHashLookup = session.PendingEdits.Any(edit =>
                string.Equals(edit.Field, SwShPlacementWorkflowService.ItemIdField, StringComparison.Ordinal)
                && edit.RecordId is not null
                && workflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == edit.RecordId) is { } placedObject
                && RequiresItemHash(placedObject));
            var itemHashes = needsItemHashLookup
                ? LoadItemHashes(project, diagnostics)
                : new Dictionary<int, ulong>();
            var itemIdsByHash = needsItemHashLookup
                ? itemHashes
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key)
                    .GroupBy(entry => entry.Value)
                    .ToDictionary(group => group.Key, group => group.First().Key)
                : new Dictionary<ulong, int>();

            foreach (var editGroup in session.PendingEdits.GroupBy(GetArchiveMemberFileName, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(editGroup.Key))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Pending placement edit does not include a valid archive member.",
                        expected: "Known Sword/Shield placement member"));
                    continue;
                }

                var archive = SwShPlacementZoneArchive.Parse(pack.GetFileByName(editGroup.Key), itemIdsByHash);
                var archiveEdits = new List<SwShPlacementObjectEdit>();
                var rawFieldEdits = new List<SwShPlacementRawFieldEdit>();
                foreach (var edit in editGroup)
                {
                    if (IsRawPlacementField(edit.Field))
                    {
                        var rawFieldEdit = ToRawFieldEdit(edit, diagnostics);
                        if (rawFieldEdit is not null)
                        {
                            rawFieldEdits.Add(rawFieldEdit);
                        }

                        continue;
                    }

                    var archiveEdit = ToArchiveEdit(archive, itemHashes, edit, diagnostics);
                    if (archiveEdit is not null)
                    {
                        archiveEdits.Add(archiveEdit);
                    }
                }

                if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
                }

                pack.SetFileByName(editGroup.Key, archive.WriteEdits(archiveEdits, rawFieldEdits));
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, pack.Write());
            projectWorkspaceService.ClearMemoryCache();
            placementWorkflowService.ClearMemoryCache();
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Generated, dataSource.GraphEntry.RelativePath));
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Placement change plan to the configured LayeredFS output root."));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement apply failed because the source data could not be decoded: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Supported Sword/Shield placement.gfpak"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement apply failed while writing output: {exception.Message}",
                file: dataSource.GraphEntry.RelativePath,
                expected: "Writable LayeredFS output root"));
        }

        return CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static SwShPlacementWorkflow OverlayPendingEdits(
        SwShPlacementWorkflow workflow,
        IReadOnlyList<PendingEdit> pendingEdits,
        IReadOnlyDictionary<int, ulong>? itemHashes = null)
    {
        if (pendingEdits.Count == 0)
        {
            return workflow;
        }

        var objects = workflow.Objects.ToArray();
        foreach (var edit in pendingEdits.Where(edit => edit.Domain == PlacementEditDomain))
        {
            var index = Array.FindIndex(objects, placedObject => placedObject.ObjectId == edit.RecordId);
            if (index < 0 || edit.Field is null || edit.NewValue is null)
            {
                continue;
            }

            var placedObject = objects[index];
            var itemDisplayName = edit.Field == SwShPlacementWorkflowService.ItemIdField
                && TryParseInt(edit.NewValue, out var itemId)
                    ? ResolveItemDisplayName(workflow, placedObject, itemId)
                    : null;
            var itemHashDisplay = edit.Field == SwShPlacementWorkflowService.ItemIdField
                && RequiresItemHash(placedObject)
                && TryParseInt(edit.NewValue, out itemId)
                && itemHashes?.TryGetValue(itemId, out var itemHash) == true
                    ? FormatHash(itemHash)
                    : null;
            foreach (var affectedIndex in GetAffectedObjectIndexes(objects, placedObject, edit.Field))
            {
                objects[affectedIndex] = OverlayPendingEdit(
                    objects[affectedIndex],
                    edit.Field,
                    edit.NewValue,
                    itemDisplayName,
                    itemHashDisplay);
            }
        }

        return workflow with { Objects = objects };
    }

    private static SwShPlacedObjectRecord OverlayPendingEdit(
        SwShPlacedObjectRecord placedObject,
        string field,
        string newValue,
        string? itemDisplayName = null,
        string? itemHashDisplay = null)
    {
        var topLevelField = ResolveTopLevelOverlayField(placedObject, field);
        var updated = topLevelField switch
        {
            SwShPlacementWorkflowService.LocationXField when TryParseFloat(newValue, out var value) => placedObject with { X = value },
            SwShPlacementWorkflowService.LocationYField when TryParseFloat(newValue, out var value) => placedObject with { Y = value },
            SwShPlacementWorkflowService.LocationZField when TryParseFloat(newValue, out var value) => placedObject with { Z = value },
            SwShPlacementWorkflowService.RotationYField when TryParseFloat(newValue, out var value) => placedObject with { RotationY = value },
            SwShPlacementWorkflowService.QuantityField when TryParseInt(newValue, out var value) => placedObject with { Quantity = value },
            SwShPlacementWorkflowService.ChanceField when TryParseInt(newValue, out var value) => placedObject with { Chance = value },
            SwShPlacementWorkflowService.ItemIdField when TryParseInt(newValue, out var value) => placedObject with
            {
                ItemId = (uint)value,
                ItemName = ResolveOverlayItemName(itemDisplayName, value),
                ItemHash = itemHashDisplay ?? placedObject.ItemHash,
                Label = CreateOverlayItemLabel(placedObject.ObjectType, itemDisplayName, value),
            },
            _ => placedObject,
        };

        var updatedFields = OverlayStructuredField(updated.Fields, field, newValue, itemDisplayName);
        if (field == SwShPlacementWorkflowService.ItemIdField
            && itemHashDisplay is not null
            && TryParseInt(newValue, out var itemId))
        {
            updatedFields = OverlayDerivedItemHashField(
                updatedFields,
                placedObject.ObjectType,
                itemHashDisplay,
                ResolveOverlayItemName(itemDisplayName, itemId),
                itemId);
        }

        return updated with
        {
            Fields = updatedFields,
        };
    }

    private static IReadOnlyList<SwShPlacementFieldValue>? OverlayDerivedItemHashField(
        IReadOnlyList<SwShPlacementFieldValue>? fields,
        string objectType,
        string itemHash,
        string itemName,
        int itemId)
    {
        if (fields is null)
        {
            return null;
        }

        var derivedField = objectType == "HiddenItem" ? "hiddenItem.hash" : "fieldItem.hash";
        return fields
            .Select(field => field.Field == derivedField
                ? field with
                {
                    Value = itemHash,
                    DisplayValue = $"{itemName} ({itemId.ToString(CultureInfo.InvariantCulture)})",
                }
                : field)
            .ToArray();
    }

    private static string ResolveTopLevelOverlayField(
        SwShPlacedObjectRecord placedObject,
        string field)
    {
        if (!IsRawPlacementField(field))
        {
            return field;
        }

        var selectedField = FindStructuredField(placedObject, field);
        var canonicalField = selectedField is { Group: "Transform" }
            ? selectedField.Label switch
            {
                "X" => SwShPlacementWorkflowService.LocationXField,
                "Y" => SwShPlacementWorkflowService.LocationYField,
                "Z" => SwShPlacementWorkflowService.LocationZField,
                "Rotation Y" => SwShPlacementWorkflowService.RotationYField,
                _ => null,
            }
            : null;
        if (canonicalField is null)
        {
            return field;
        }

        var primaryField = placedObject.Fields?.FirstOrDefault(candidate =>
            IsRawPlacementField(candidate.Field)
            && candidate.Group == "Transform"
            && candidate.Label == selectedField!.Label);
        return primaryField?.Field == field ? canonicalField : field;
    }

    private static IReadOnlyList<SwShPlacementFieldValue>? OverlayStructuredField(
        IReadOnlyList<SwShPlacementFieldValue>? fields,
        string field,
        string newValue,
        string? displayValue = null)
    {
        if (fields is null)
        {
            return null;
        }

        return fields
            .Select(value => value.Field == field
                ? value with
                {
                    Value = newValue,
                    DisplayValue = displayValue ?? value.Options?.FirstOrDefault(option =>
                        option.Value.ToString(CultureInfo.InvariantCulture) == newValue)?.Label ?? newValue,
                }
                : value)
            .ToArray();
    }

    private static PendingEdit? CreatePendingEdit(
        SwShPlacedObjectRecord placedObject,
        string field,
        string value,
        IReadOnlyDictionary<int, ulong>? itemHashes,
        bool itemHashLookupAvailable,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!ValidateFieldValue(
            placedObject,
            field,
            value,
            itemHashes,
            itemHashLookupAvailable,
            diagnostics))
        {
            return null;
        }

        var normalized = NormalizeValue(placedObject, field, value);
        return new PendingEdit(
            PlacementEditDomain,
            $"{placedObject.Label} {GetFieldLabel(placedObject, field)} -> {normalized}",
            [new ProjectFileReference(placedObject.Provenance.SourceLayer, placedObject.Provenance.SourceFile)],
            RecordId: placedObject.ObjectId,
            Field: field,
            NewValue: normalized);
    }

    private static bool ValidateFieldValue(
        SwShPlacedObjectRecord placedObject,
        string field,
        string value,
        IReadOnlyDictionary<int, ulong>? itemHashes,
        bool itemHashLookupAvailable,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsRawPlacementField(field) && !ValidateCanonicalFieldStorage(placedObject, field, diagnostics))
        {
            return false;
        }

        switch (field)
        {
            case SwShPlacementWorkflowService.LocationXField:
            case SwShPlacementWorkflowService.LocationYField:
            case SwShPlacementWorkflowService.LocationZField:
                return ValidateDouble(field, value, SwShPlacementWorkflowService.MinimumCoordinate, SwShPlacementWorkflowService.MaximumCoordinate, diagnostics);
            case SwShPlacementWorkflowService.RotationYField:
                return ValidateDouble(field, value, SwShPlacementWorkflowService.MinimumRotation, SwShPlacementWorkflowService.MaximumRotation, diagnostics);
            case SwShPlacementWorkflowService.ItemIdField:
                if (!ValidateInt(field, value, 0, SwShPlacementWorkflowService.MaximumItemId, diagnostics))
                {
                    return false;
                }

                if (!TryParseInt(value, out var itemId))
                {
                    return false;
                }

                if (RequiresItemHash(placedObject))
                {
                    if (!itemHashLookupAvailable)
                    {
                        return false;
                    }

                    if (itemHashes is null)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            "This placement item stores item hashes, and the item hash table is required before editing by item ID.",
                            field: SwShPlacementWorkflowService.ItemIdField,
                            expected: SwShPlacementWorkflowService.ItemHashPath));
                        return false;
                    }

                    if (!itemHashes.TryGetValue(itemId, out var itemHash) || itemHash == 0)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            DiagnosticSeverity.Error,
                            $"Item ID {itemId} is not present in the item hash table.",
                            field: SwShPlacementWorkflowService.ItemIdField,
                            expected: "Item ID with placement hash"));
                        return false;
                    }
                }

                return true;
            case SwShPlacementWorkflowService.QuantityField:
                return ValidateInt(field, value, 0, placedObject.ObjectType == "FieldItem" ? byte.MaxValue : SwShPlacementWorkflowService.MaximumQuantity, diagnostics);
            case SwShPlacementWorkflowService.ChanceField:
                if (placedObject.ObjectType != "HiddenItem")
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "Chance can only be edited for hidden placement item chances.",
                        field: "field",
                        expected: "HiddenItem record"));
                    return false;
                }

                return ValidateInt(field, value, 0, SwShPlacementWorkflowService.MaximumChance, diagnostics);
            default:
                if (IsRawPlacementField(field))
                {
                    return ValidateRawFieldValue(placedObject, field, value, diagnostics);
                }

                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Placement field '{field}' is not editable.",
                    field: "field",
                    expected: "Supported placement editable field"));
                return false;
        }
    }

    private static bool RequiresItemHash(SwShPlacedObjectRecord placedObject)
    {
        return placedObject.ItemUsesHashStorage;
    }

    private static bool ValidateCanonicalFieldStorage(
        SwShPlacedObjectRecord placedObject,
        string field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!IsCanonicalPlacementField(field))
        {
            return true;
        }

        var fieldValue = FindStructuredField(placedObject, field);
        if (fieldValue is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement field '{field}' is not present on the selected object.",
                field: "field",
                expected: "Existing placement field"));
            return false;
        }

        if (fieldValue.IsReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement field '{fieldValue.Label}' is omitted from this object's FlatBuffer storage and cannot be edited safely in place.",
                field: "field",
                expected: "Stored editable placement scalar"));
            return false;
        }

        return true;
    }

    private static IReadOnlyList<SwShPlacedObjectRecord> GetAffectedObjects(
        SwShPlacementWorkflow workflow,
        IReadOnlyList<SwShPlacementObjectFieldUpdate> updates)
    {
        var affectedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var update in updates)
        {
            var selectedObject = workflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == update.ObjectId);
            if (selectedObject is null)
            {
                continue;
            }

            foreach (var affectedIndex in GetAffectedObjectIndexes(workflow.Objects, selectedObject, update.Field))
            {
                affectedIds.Add(workflow.Objects[affectedIndex].ObjectId);
            }
        }

        return workflow.Objects
            .Where(placedObject => affectedIds.Contains(placedObject.ObjectId))
            .ToArray();
    }

    private static string ResolveItemDisplayName(
        SwShPlacementWorkflow workflow,
        SwShPlacedObjectRecord placedObject,
        int itemId)
    {
        var option = placedObject.Fields?
                .FirstOrDefault(field => field.Field == SwShPlacementWorkflowService.ItemIdField)?
                .Options?
                .FirstOrDefault(candidate => candidate.Value == itemId)
            ?? workflow.EditableFields
                .FirstOrDefault(field => field.Field == SwShPlacementWorkflowService.ItemIdField)?
                .Options
                .FirstOrDefault(candidate => candidate.Value == itemId);
        if (option is null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Item {itemId}");
        }

        var separator = option.Label.IndexOf(' ');
        return separator > 0
            && int.TryParse(option.Label[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var labelItemId)
            && labelItemId == itemId
            ? option.Label[(separator + 1)..]
            : option.Label;
    }

    private static string ResolveOverlayItemName(string? itemDisplayName, int itemId)
    {
        return itemDisplayName ?? string.Create(CultureInfo.InvariantCulture, $"Item {itemId}");
    }

    private static string CreateOverlayItemLabel(string objectType, string? itemDisplayName, int itemId)
    {
        var itemName = ResolveOverlayItemName(itemDisplayName, itemId);
        return objectType == "HiddenItem"
            ? $"Hidden item: {itemName}"
            : $"Field item: {itemName}";
    }

    private static IEnumerable<int> GetAffectedObjectIndexes(
        IReadOnlyList<SwShPlacedObjectRecord> objects,
        SwShPlacedObjectRecord selectedObject,
        string field)
    {
        var sharesHiddenParent = selectedObject.ObjectType == "HiddenItem"
            && (IsTransformField(field) || IsRawPlacementField(field));

        for (var index = 0; index < objects.Count; index++)
        {
            var candidate = objects[index];
            if (sharesHiddenParent
                ? candidate.ObjectType == "HiddenItem"
                    && candidate.ArchiveMember == selectedObject.ArchiveMember
                    && candidate.ZoneIndex == selectedObject.ZoneIndex
                    && candidate.ObjectIndex == selectedObject.ObjectIndex
                : candidate.ObjectId == selectedObject.ObjectId)
            {
                yield return index;
            }
        }
    }

    private static void ValidateStorageConflicts(
        SwShPlacementWorkflow workflow,
        IReadOnlyList<PendingEdit> pendingEdits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var storageTargets = pendingEdits
            .Where(edit => edit.Domain == PlacementEditDomain)
            .Select(edit => TryCreateStorageIdentity(workflow, edit, out var identity)
                ? new PlacementStorageTarget(identity, edit)
                : null)
            .Where(target => target is not null)
            .Cast<PlacementStorageTarget>()
            .GroupBy(target => target.Identity);

        foreach (var group in storageTargets)
        {
            var aliases = group
                .Select(target => (target.Edit.RecordId, target.Edit.Field))
                .Distinct()
                .ToArray();
            if (aliases.Length <= 1)
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement edits '{string.Join("', '", aliases.Select(alias => $"{alias.RecordId}:{alias.Field}"))}' target the same underlying storage. Keep only one edit for that value.",
                field: "field",
                expected: "One pending edit per placement storage value"));
        }
    }

    private static bool TryCreateStorageIdentity(
        SwShPlacementWorkflow workflow,
        PendingEdit edit,
        out PlacementStorageIdentity identity)
    {
        identity = default!;
        if (edit.RecordId is null || edit.Field is null)
        {
            return false;
        }

        var placedObject = workflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == edit.RecordId);
        if (placedObject is null)
        {
            return false;
        }

        var storageSlot = GetStorageSlot(placedObject, edit.Field);
        if (storageSlot is null)
        {
            return false;
        }

        identity = new PlacementStorageIdentity(
            placedObject.ArchiveMember,
            placedObject.ZoneIndex,
            placedObject.ObjectType,
            placedObject.ObjectIndex,
            storageSlot);
        return true;
    }

    private static string? GetStorageSlot(SwShPlacedObjectRecord placedObject, string field)
    {
        if (IsTransformField(field))
        {
            return $"transform:{field}";
        }

        if (field is SwShPlacementWorkflowService.ItemIdField
            or SwShPlacementWorkflowService.QuantityField
            or SwShPlacementWorkflowService.ChanceField)
        {
            return placedObject.ObjectType == "HiddenItem"
                ? $"chance:{placedObject.ChanceIndex}:{field}"
                : $"value:{field}";
        }

        if (!IsRawPlacementField(field))
        {
            return null;
        }

        if (placedObject.ObjectType == "FieldItem")
        {
            const string root = "raw.FieldItem.Field_00";
            if (TryGetCanonicalRawTransformSlot(field, $"{root}.Field_00", out var transformSlot))
            {
                return transformSlot;
            }

            if (field == $"{root}.Quantity")
            {
                return $"value:{SwShPlacementWorkflowService.QuantityField}";
            }

            if ((placedObject.ItemUsesHashStorage && field == $"{root}.Flags[0]")
                || (placedObject.ItemUsesDirectIdStorage && field == $"{root}.Items[0]"))
            {
                return $"value:{SwShPlacementWorkflowService.ItemIdField}";
            }
        }

        if (placedObject.ObjectType == "HiddenItem")
        {
            const string root = "raw.HiddenItem.Field_00";
            if (TryGetCanonicalRawTransformSlot(field, $"{root}.Field_00", out var transformSlot))
            {
                return transformSlot;
            }

            if (TryParseHiddenChanceRawField(field, out var chanceIndex, out var chanceField))
            {
                return $"chance:{chanceIndex}:{chanceField}";
            }
        }

        return $"raw:{field}";
    }

    private static bool TryGetCanonicalRawTransformSlot(
        string field,
        string transformRoot,
        out string storageSlot)
    {
        storageSlot = field switch
        {
            var value when value == $"{transformRoot}.LocationX" => $"transform:{SwShPlacementWorkflowService.LocationXField}",
            var value when value == $"{transformRoot}.LocationY" => $"transform:{SwShPlacementWorkflowService.LocationYField}",
            var value when value == $"{transformRoot}.LocationZ" => $"transform:{SwShPlacementWorkflowService.LocationZField}",
            var value when value == $"{transformRoot}.RotationY" => $"transform:{SwShPlacementWorkflowService.RotationYField}",
            _ => string.Empty,
        };
        return storageSlot.Length > 0;
    }

    private static bool TryParseHiddenChanceRawField(
        string field,
        out int chanceIndex,
        out string chanceField)
    {
        chanceIndex = 0;
        chanceField = string.Empty;
        const string root = "raw.HiddenItem.Field_00";
        const string marker = ".Field_02[";
        if (!field.StartsWith(root, StringComparison.Ordinal))
        {
            return false;
        }

        var markerIndex = field.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var indexStart = markerIndex + marker.Length;
        var indexEnd = field.IndexOf(']', indexStart);
        if (indexEnd < 0
            || !int.TryParse(field[indexStart..indexEnd], NumberStyles.Integer, CultureInfo.InvariantCulture, out chanceIndex))
        {
            return false;
        }

        var suffix = field[(indexEnd + 1)..];
        chanceField = suffix switch
        {
            ".Hash" => SwShPlacementWorkflowService.ItemIdField,
            ".Quantity" => SwShPlacementWorkflowService.QuantityField,
            ".Chance" => SwShPlacementWorkflowService.ChanceField,
            _ => string.Empty,
        };
        return chanceField.Length > 0;
    }

    private static void ValidateHiddenChancePools(
        SwShPlacementWorkflow workflow,
        IReadOnlyList<PendingEdit> pendingEdits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var touchedPools = pendingEdits
            .Where(edit => edit.Domain == PlacementEditDomain
                && edit.Field == SwShPlacementWorkflowService.ChanceField
                && edit.RecordId is not null)
            .Select(edit => workflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == edit.RecordId))
            .Where(placedObject => placedObject?.ObjectType == "HiddenItem")
            .Select(placedObject => (
                placedObject!.ArchiveMember,
                placedObject.ZoneIndex,
                placedObject.ObjectIndex))
            .ToHashSet();

        foreach (var pool in workflow.Objects
            .Where(placedObject => placedObject.ObjectType == "HiddenItem"
                && touchedPools.Contains((
                    placedObject.ArchiveMember,
                    placedObject.ZoneIndex,
                    placedObject.ObjectIndex)))
            .GroupBy(placedObject => (
                placedObject.ArchiveMember,
                placedObject.ZoneIndex,
                placedObject.ObjectIndex)))
        {
            var total = pool.Sum(placedObject => placedObject.Chance ?? 0);
            if (total == 100)
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Hidden item chance pool '{pool.Key.ArchiveMember}' zone {pool.Key.ZoneIndex} object {pool.Key.ObjectIndex} totals {total}; the complete pool must total 100 before output.",
                field: SwShPlacementWorkflowService.ChanceField,
                expected: "Hidden item chance pool total of 100"));
        }
    }

    private static bool IsCanonicalPlacementField(string field)
    {
        return field is
            SwShPlacementWorkflowService.LocationXField
            or SwShPlacementWorkflowService.LocationYField
            or SwShPlacementWorkflowService.LocationZField
            or SwShPlacementWorkflowService.RotationYField
            or SwShPlacementWorkflowService.ItemIdField
            or SwShPlacementWorkflowService.QuantityField
            or SwShPlacementWorkflowService.ChanceField;
    }

    private static bool IsTransformField(string field)
    {
        return field is
            SwShPlacementWorkflowService.LocationXField
            or SwShPlacementWorkflowService.LocationYField
            or SwShPlacementWorkflowService.LocationZField
            or SwShPlacementWorkflowService.RotationYField;
    }

    private static void ValidatePendingEdit(
        SwShPlacementWorkflow workflow,
        PendingEdit edit,
        IReadOnlyDictionary<int, ulong>? itemHashes,
        bool itemHashLookupAvailable,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (edit.Domain != PlacementEditDomain)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Placement.",
                expected: PlacementEditDomain));
            return;
        }

        if (edit.RecordId is null || edit.Field is null || edit.NewValue is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending placement edit is missing record, field, or value metadata.",
                expected: "Complete placement pending edit"));
            return;
        }

        var placedObject = workflow.Objects.FirstOrDefault(candidate => candidate.ObjectId == edit.RecordId);
        if (placedObject is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending placement object '{edit.RecordId}' is no longer present.",
                field: "recordId",
                expected: "Existing placement object"));
            return;
        }

        ValidateFieldValue(
            placedObject,
            edit.Field,
            edit.NewValue,
            itemHashes,
            itemHashLookupAvailable,
            diagnostics);
    }

    private static void ValidatePendingEditEnvelope(
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var edit in edits)
        {
            if (edit.Domain != PlacementEditDomain)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Pending edit domain '{edit.Domain}' is not supported by Placement.",
                    expected: PlacementEditDomain));
                continue;
            }

            if (edit.RecordId is null || edit.Field is null || edit.NewValue is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Pending placement edit is missing record, field, or value metadata.",
                    expected: "Complete placement pending edit"));
            }
        }
    }

    private static SwShPlacementObjectEdit? ToArchiveEdit(
        SwShPlacementZoneArchive archive,
        IReadOnlyDictionary<int, ulong> itemHashes,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (edit.RecordId is null
            || edit.Field is null
            || edit.NewValue is null
            || !SwShPlacementWorkflowService.TryParseObjectRecordId(
                edit.RecordId,
                out _,
                out var zoneIndex,
                out var objectType,
                out var objectIndex,
                out var chanceIndex))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending placement edit record id is invalid.",
                field: "recordId",
                expected: "Placement record id"));
            return null;
        }

        if (!TryParseField(edit.Field, out var field))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement field '{edit.Field}' is not editable.",
                field: "field",
                expected: "Supported placement editable field"));
            return null;
        }

        if (!TryParseDouble(edit.NewValue, out var value))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement value '{edit.NewValue}' is not numeric.",
                field: edit.Field,
                expected: "Numeric placement value"));
            return null;
        }

        var kind = objectType switch
        {
            "fieldItem" => SwShPlacementObjectKind.FieldItem,
            "hiddenItem" => SwShPlacementObjectKind.HiddenItem,
            _ => throw new InvalidDataException($"Placement object type '{objectType}' is not supported."),
        };

        ulong? hashValue = null;
        if (field == PlacementArchiveField.ItemId && RequiresHashForArchiveEdit(archive, kind, zoneIndex, objectIndex))
        {
            var itemId = checked((int)value);
            if (!itemHashes.TryGetValue(itemId, out var hash) || hash == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Item ID {itemId} is not present in the item hash table.",
                    field: SwShPlacementWorkflowService.ItemIdField,
                    expected: "Item ID with placement hash"));
                return null;
            }

            hashValue = hash;
        }

        return new SwShPlacementObjectEdit(zoneIndex, kind, objectIndex, chanceIndex, field, value, hashValue);
    }

    private static SwShPlacementRawFieldEdit? ToRawFieldEdit(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (edit.RecordId is null
            || edit.Field is null
            || edit.NewValue is null
            || !SwShPlacementWorkflowService.TryParseObjectRecordId(
                edit.RecordId,
                out _,
                out var zoneIndex,
                out var objectType,
                out var objectIndex,
                out _))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending placement raw edit record id is invalid.",
                field: "recordId",
                expected: "Placement record id"));
            return null;
        }

        return new SwShPlacementRawFieldEdit(
            zoneIndex,
            ToRawObjectType(objectType),
            objectIndex,
            edit.Field,
            edit.NewValue);
    }

    private static string ToRawObjectType(string objectType)
    {
        return objectType switch
        {
            "fieldItem" => "FieldItem",
            "hiddenItem" => "HiddenItem",
            _ => objectType,
        };
    }

    private static bool RequiresHashForArchiveEdit(
        SwShPlacementZoneArchive archive,
        SwShPlacementObjectKind kind,
        int zoneIndex,
        int objectIndex)
    {
        if ((uint)zoneIndex >= (uint)archive.Zones.Count)
        {
            return true;
        }

        var zone = archive.Zones[zoneIndex];
        if (kind == SwShPlacementObjectKind.HiddenItem)
        {
            return true;
        }

        return (uint)objectIndex >= (uint)zone.FieldItems.Count || zone.FieldItems[objectIndex].ItemHashOffsets.Count > 0;
    }

    private static bool TryParseField(string field, out PlacementArchiveField parsed)
    {
        parsed = field switch
        {
            SwShPlacementWorkflowService.LocationXField => PlacementArchiveField.LocationX,
            SwShPlacementWorkflowService.LocationYField => PlacementArchiveField.LocationY,
            SwShPlacementWorkflowService.LocationZField => PlacementArchiveField.LocationZ,
            SwShPlacementWorkflowService.RotationYField => PlacementArchiveField.RotationY,
            SwShPlacementWorkflowService.ItemIdField => PlacementArchiveField.ItemId,
            SwShPlacementWorkflowService.QuantityField => PlacementArchiveField.Quantity,
            SwShPlacementWorkflowService.ChanceField => PlacementArchiveField.Chance,
            _ => default,
        };

        return field is
            SwShPlacementWorkflowService.LocationXField
            or SwShPlacementWorkflowService.LocationYField
            or SwShPlacementWorkflowService.LocationZField
            or SwShPlacementWorkflowService.RotationYField
            or SwShPlacementWorkflowService.ItemIdField
            or SwShPlacementWorkflowService.QuantityField
            or SwShPlacementWorkflowService.ChanceField;
    }

    private static Dictionary<int, ulong> LoadItemHashes(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShPlacementWorkflowService.ResolveItemHashSource(project);
        if (source is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Item hash table is required to apply Placement item ID edits.",
                expected: SwShPlacementWorkflowService.ItemHashPath));
            return [];
        }

        try
        {
            return SwShItemHashTable.Parse(File.ReadAllBytes(source.AbsolutePath)).ToHashByItemId();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item hash table could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield item_hash_to_index.dat"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Item hash table could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield item_hash_to_index.dat"));
            return [];
        }
    }

    private static bool CanEditPlacement(
        OpenedProject project,
        SwShPlacementWorkflow workflow,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Placement edits require valid base RomFS, base ExeFS, and output root paths.",
                expected: "Editable project paths"));
            return false;
        }

        if (workflow.Summary.Availability != SwShWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Placement workflow is not available for editing.",
                expected: "Available Placement workflow"));
            return false;
        }

        if (workflow.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Resolve Placement workflow errors before editing.",
                expected: "Placement workflow without errors"));
            return false;
        }

        return true;
    }

    private static EditSession ReplacePendingPlacementEdit(
        SwShPlacementWorkflow workflow,
        EditSession session,
        PendingEdit edit)
    {
        var hasStorageIdentity = TryCreateStorageIdentity(workflow, edit, out var storageIdentity);
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(candidate =>
                    (candidate.Domain != edit.Domain
                        || candidate.RecordId != edit.RecordId
                        || candidate.Field != edit.Field)
                    && (!hasStorageIdentity
                        || !TryCreateStorageIdentity(workflow, candidate, out var candidateIdentity)
                        || candidateIdentity != storageIdentity))
                .Append(edit)
                .ToArray(),
        };
    }

    private static bool ValidateDouble(
        string field,
        string value,
        double minimum,
        double maximum,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseDouble(value, out var parsed) || double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < minimum || parsed > maximum)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement field '{field}' must be between {minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}.",
                field: "value",
                expected: "Placement numeric value"));
            return false;
        }

        return true;
    }

    private static bool ValidateInt(
        string field,
        string value,
        int minimum,
        int maximum,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!TryParseInt(value, out var parsed) || parsed < minimum || parsed > maximum)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement field '{field}' must be an integer between {minimum} and {maximum}.",
                field: "value",
                expected: "Placement integer value"));
            return false;
        }

        return true;
    }

    private static bool ValidateRawFieldValue(
        SwShPlacedObjectRecord placedObject,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var fieldValue = FindStructuredField(placedObject, field);
        if (fieldValue is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement field '{field}' is not present on the selected object.",
                field: "field",
                expected: "Existing placement field"));
            return false;
        }

        if (fieldValue.IsReadOnly)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Placement field '{fieldValue.Label}' is visible for context but cannot be edited in place.",
                field: "field",
                expected: "Editable scalar placement field"));
            return false;
        }

        return fieldValue.ValueKind switch
        {
            "boolean" => ValidateRawBoolean(fieldValue, value, diagnostics),
            "hash" => ValidateRawHash(fieldValue, value, diagnostics),
            "integer" => ValidateRawInteger(fieldValue, value, diagnostics),
            "number" => ValidateDouble(field, value, fieldValue.MinimumValue, fieldValue.MaximumValue, diagnostics),
            "text" => ValidateRawText(fieldValue, value, diagnostics),
            _ => ValidateRawText(fieldValue, value, diagnostics),
        };
    }

    private static bool ValidateRawBoolean(
        SwShPlacementFieldValue field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (TryParseBoolean(value, out _))
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Placement field '{field.Label}' must be true/false or 1/0.",
            field: "value",
            expected: "Boolean placement value"));
        return false;
    }

    private static bool ValidateRawHash(
        SwShPlacementFieldValue field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (TryParseHashValue(value, out _))
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Placement field '{field.Label}' must be a decimal value, 0x-prefixed hash, or None.",
            field: "value",
            expected: "64-bit placement hash"));
        return false;
    }

    private static bool ValidateRawInteger(
        SwShPlacementFieldValue field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (double.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && Math.Truncate(parsed) == parsed
            && parsed >= field.MinimumValue
            && parsed <= field.MaximumValue)
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Placement field '{field.Label}' must be an integer between {field.MinimumValue} and {field.MaximumValue}."),
            field: "value",
            expected: "Placement integer value"));
        return false;
    }

    private static bool ValidateRawText(
        SwShPlacementFieldValue field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (field.MaximumValue <= 0 || Encoding.UTF8.GetByteCount(value) <= field.MaximumValue)
        {
            return true;
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Error,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Placement field '{field.Label}' can use at most {field.MaximumValue} UTF-8 bytes in the current FlatBuffer layout."),
            field: "value",
            expected: "Text that fits the existing placement string allocation"));
        return false;
    }

    private static string NormalizeValue(SwShPlacedObjectRecord placedObject, string field, string value)
    {
        var structuredField = FindStructuredField(placedObject, field);
        if (structuredField is not null && IsRawPlacementField(field))
        {
            return structuredField.ValueKind switch
            {
                "boolean" => TryParseBoolean(value, out var parsedBoolean)
                    ? (parsedBoolean ? "true" : "false")
                    : value,
                "hash" => TryParseHashValue(value, out var parsedHash)
                    ? FormatHash(parsedHash)
                    : value,
                "integer" => double.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString("0", CultureInfo.InvariantCulture),
                "number" => NormalizeFloat32(value),
                _ => value,
            };
        }

        return field switch
        {
            SwShPlacementWorkflowService.LocationXField
                or SwShPlacementWorkflowService.LocationYField
                or SwShPlacementWorkflowService.LocationZField
                or SwShPlacementWorkflowService.RotationYField => NormalizeFloat32(value),
            _ => int.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string NormalizeFloat32(string value)
    {
        var parsed = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (parsed == 0f)
        {
            parsed = 0f;
        }

        return parsed.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static SwShPlacementFieldValue? FindStructuredField(SwShPlacedObjectRecord placedObject, string field)
    {
        return placedObject.Fields?.FirstOrDefault(candidate => candidate.Field == field);
    }

    private static bool IsRawPlacementField(string? field)
    {
        return field?.StartsWith("raw.", StringComparison.Ordinal) == true;
    }

    private static bool TryParseBoolean(string value, out bool parsed)
    {
        var trimmed = value.Trim();
        if (bool.TryParse(trimmed, out parsed))
        {
            return true;
        }

        if (trimmed == "1")
        {
            parsed = true;
            return true;
        }

        if (trimmed == "0")
        {
            parsed = false;
            return true;
        }

        if (trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            parsed = true;
            return true;
        }

        if (trimmed.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            parsed = false;
            return true;
        }

        parsed = false;
        return false;
    }

    private static bool TryParseHashValue(string value, out ulong parsed)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0
            || trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("empty", StringComparison.OrdinalIgnoreCase))
        {
            parsed = SwShPlacementZoneArchive.EmptyFnvHash;
            return true;
        }

        var hexIndex = trimmed.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
        if (hexIndex >= 0)
        {
            var hex = trimmed[(hexIndex + 2)..];
            var separatorIndex = hex.IndexOfAny(new[] { ' ', ')', ']' });
            if (separatorIndex >= 0)
            {
                hex = hex[..separatorIndex];
            }

            return ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        return ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static string FormatHash(ulong hash)
    {
        return hash == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"0x{hash:X16}");
    }

    private static string GetArchiveMemberFileName(PendingEdit edit)
    {
        if (edit.RecordId is null
            || !SwShPlacementWorkflowService.TryParseObjectRecordId(
                edit.RecordId,
                out var archiveMember,
                out _,
                out _,
                out _,
                out _))
        {
            return string.Empty;
        }

        return archiveMember;
    }

    private static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        return reviewedPlan.SessionId == currentPlan.SessionId
            && reviewedPlan.Writes.Select(write => write.TargetRelativePath).SequenceEqual(
                currentPlan.Writes.Select(write => write.TargetRelativePath),
                StringComparer.Ordinal)
            && currentPlan.Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
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

    private static string? ResolveOutputPath(
        ProjectPaths paths,
        string targetRelativePath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = SwShPlacementWorkflowService.ResolveOutputPath(paths, targetRelativePath);
        if (targetPath is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Placement output path could not be resolved inside the output root.",
                file: targetRelativePath,
                expected: "Output-root-contained target"));
        }

        return targetPath;
    }

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null ? ProjectFileLayer.Layered : ProjectFileLayer.Base;
    }

    private static string GetFieldLabel(SwShPlacedObjectRecord placedObject, string field)
    {
        var structuredField = FindStructuredField(placedObject, field);
        if (structuredField is not null)
        {
            return structuredField.Label;
        }

        return field switch
        {
            SwShPlacementWorkflowService.LocationXField => "X",
            SwShPlacementWorkflowService.LocationYField => "Y",
            SwShPlacementWorkflowService.LocationZField => "Z",
            SwShPlacementWorkflowService.RotationYField => "Rotation Y",
            SwShPlacementWorkflowService.ItemIdField => "Item ID",
            SwShPlacementWorkflowService.QuantityField => "Quantity",
            SwShPlacementWorkflowService.ChanceField => "Chance",
            _ => field,
        };
    }

    private static bool TryParseDouble(string value, out double parsed)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseFloat(string value, out float parsed)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseInt(string value, out int parsed)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
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
            Field: field,
            Domain: PlacementEditDomain,
            Expected: expected);
    }

    private sealed record PlacementStorageIdentity(
        string ArchiveMember,
        int ZoneIndex,
        string ObjectType,
        int ObjectIndex,
        string Slot);

    private sealed record PlacementStorageTarget(
        PlacementStorageIdentity Identity,
        PendingEdit Edit);
}
