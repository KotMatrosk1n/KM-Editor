// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.Restoration;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Restoration;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Tools.Application;
using KM.Formats.SwSh;
using KM.SwSh;

namespace KM.Tools.Bridge;

public sealed partial class ProjectBridgeDispatcher
{
    private readonly SwShRestorationPreparationJobs restorationJobs = new();

    private static IDisposable? OpenRestorationReadScope(string json, ProjectGame? game)
    {
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)) return null;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("paths", out var rawPaths) || rawPaths.ValueKind != JsonValueKind.Object) return null;
        var paths = rawPaths.Deserialize<ProjectPathsDto>(RequestSerializerOptions);
        return paths is null ? null : SwShRestorationReadScope.Begin(ProjectBridgeMapper.ToCore(paths));
    }

    private static KM.SwSh.DynamaxAdventures.SwShDynamaxAdventuresEditResult MergeDynamaxSession(
        KM.SwSh.DynamaxAdventures.SwShDynamaxAdventuresEditResult result, EditSession? original) => original is null ? result : result with
        { Session = result.Session with { PendingEdits = original.PendingEdits.Where(e => e.Domain != "workflow.dynamaxAdventures").Concat(result.Session.PendingEdits).ToArray() } };

    private IDisposable? OpenRestorationReadView(string json, string? command, ProjectGame? game)
    {
        if (game is not (ProjectGame.Sword or ProjectGame.Shield) || command is null ||
            !new[] { "pokemon.", "trainers.", "encounters.", "giftPokemon.", "tradePokemon.", "rentalPokemon.",
                "staticEncounters.", "raidBattles.", "placement.", "swshPlacement.", "shakingTrees.", "models.", "dynamaxAdventures.",
                "behavior.", "heldItemChance.", "spreadsheetImport.", "gameDump.", "semantic." }
                .Any(prefix => command.StartsWith(prefix, StringComparison.Ordinal))
            || command == KmCommandNames.SetDynamaxAdventureSaveSeed) return null;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("speciesSession", out var sessionJson) || sessionJson.ValueKind == JsonValueKind.Null
            || !document.RootElement.TryGetProperty("payload", out var payload)) return null;
        if (!payload.TryGetProperty("paths", out var pathsJson)
            && (!payload.TryGetProperty("scope", out var scope) || !scope.TryGetProperty("paths", out pathsJson))) return null;
        var dto = sessionJson.Deserialize<EditSessionDto>(RequestSerializerOptions);
        if (dto is null) return null;
        var session = EditSessionBridgeMapper.ToCore(dto);
        if (!HasRestoration(session)) return null;
        var paths = ProjectBridgeMapper.ToCore(pathsJson.Deserialize<ProjectPathsDto>(RequestSerializerOptions)!);
        var preview = new SwShRestorationService().Preview(paths, session);
        var view = new SwShRestorationView(paths);
        try
        {
            SwShRestorationService.WritePreview(view.Paths, preview);
            view.State = new(preview.StateHash, preview.State);
            ClearCriticalSwShApplyCaches();
            return new RestorationReadLease(ProjectBridgeMapper.UseProspectivePaths(paths, view.Paths), view, ClearCriticalSwShApplyCaches);
        }
        catch { view.Dispose(); throw; }
    }
    private sealed class RestorationReadLease(IDisposable mapping, SwShRestorationView view, Action clear) : IDisposable
    {
        public void Dispose() { mapping.Dispose(); clear(); view.Dispose(); }
    }

    private string DispatchRestoration(string requestJson, string command)
    {
        if (command == KmCommandNames.InspectRestorationDonor)
        {
            var request = DeserializeRequest<InspectRestorationDonorRequest>(requestJson);
            return SerializeSuccess(restorationJobs.InspectDonor(request.Payload.Donor, request.Payload.DonorRomFsPath,
                request.Payload.CompressionSupportFolderPath), request.RequestId);
        }
        if (command == KmCommandNames.LoadRestoration)
        {
            var request = DeserializeRequest<LoadRestorationRequest>(requestJson);
            return SerializeSuccess(new LoadRestorationResponse(LoadRestoration(ProjectBridgeMapper.ToCore(request.Payload.Paths),
                request.Payload.Session is null ? null : EditSessionBridgeMapper.ToCore(request.Payload.Session))), request.RequestId);
        }
        if (command == KmCommandNames.PrepareRestoration)
        {
            var request = DeserializeRequest<PrepareRestorationRequest>(requestJson);
            return SerializeSuccess(new RestorationPreparationResponse(restorationJobs.Start(ProjectBridgeMapper.ToCore(request.Payload.Paths),
                request.Payload.DefinitionId, request.Payload.DonorRomFsPath, request.Payload.CompressionSupportFolderPath)), request.RequestId);
        }
        if (command is KmCommandNames.GetRestorationPreparation or KmCommandNames.CancelRestorationPreparation)
        {
            var request = DeserializeRequest<RestorationPreparationRequest>(requestJson);
            try
            {
                return SerializeSuccess(new RestorationPreparationResponse(restorationJobs.Get(ProjectBridgeMapper.ToCore(request.Payload.Paths),
                    request.Payload.PreparationId, command == KmCommandNames.CancelRestorationPreparation)), request.RequestId);
            }
            catch (InvalidDataException exception)
            {
                return SerializeFailure(SwShRestorationService.SessionCode, exception.Message, request.RequestId);
            }
        }
        var stage = DeserializeRequest<StageRestorationRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(stage.Payload.Paths);
        var result = new SwShRestorationService().Stage(paths, stage.Payload.DefinitionId, stage.Payload.PreparedHash, stage.Payload.Enabled,
            stage.Payload.Session is null ? null : EditSessionBridgeMapper.ToCore(stage.Payload.Session), stage.Payload.RemoveAssets, preparedAssets: stage.Payload.RemoveAssets ? restorationJobs.ActivePreparedHashes(paths, stage.Payload.DefinitionId) : null,
            updateAssets: stage.Payload.UpdateAssets, adoptExternal: stage.Payload.AdoptExternal);
        return SerializeSuccess(new StageRestorationResponse(LoadRestoration(paths, result.Session), EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray()), stage.RequestId);
    }

    private IReadOnlyList<ValidationDiagnostic> CleanupRemovedRestoration(ProjectPaths paths)
    {
        try
        {
            var state = SwShRestorationStateStore.Read(paths).State;
            foreach (var removed in state.Selections.Where(s => s.RemovalId is not null))
                restorationJobs.ReleaseRemoved(paths, removed.DefinitionId, removed.RemovalId!);
            SwShRestorationAssetCleanup.Collect(paths, state, restorationJobs.ActivePreparedHashes(paths));
            return [];
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or JsonException or KM.Core.Output.OutputCoordinatorException)
        {
            return [new(DiagnosticSeverity.Warning, "Removed Pokemon assets could not be fully cleared. Refresh to retry.",
                Domain: SwShRestorationDefinitions.Domain) { Code = BridgeErrorCodes.RestorationCleanupPending }];
        }
    }

    private RestorationWorkflowDto LoadRestoration(ProjectPaths paths, EditSession? session)
    {
        if (session is not null) session = SwShRestorationService.UpgradeSession(paths, session);
        var state = SwShRestorationStateStore.Read(paths);
        var cleanupDiagnostics = CleanupRemovedRestoration(paths).Select(ProjectBridgeMapper.ToDto).ToArray();
        var catalog = SwShRestorationCatalog.Discover(paths);
        var intents = session?.PendingEdits.Where(e => e.Domain == SwShRestorationDefinitions.Domain).ToDictionary(e => e.RecordId!,
            e => JsonSerializer.Deserialize<SwShRestorationIntent>(e.NewValue!)!) ?? new Dictionary<string, SwShRestorationIntent>();
        return new(paths.SelectedGame is ProjectGame.Sword or ProjectGame.Shield && !string.IsNullOrWhiteSpace(paths.OutputRootPath)
            && catalog.UnavailableReason is null, catalog.Revision + ":" + state.Hash,
            catalog.Entries.Select(e =>
            {
                var selected = state.State.Selections.SingleOrDefault(s => s.DefinitionId == e.DefinitionId);
                var intent = e.DefinitionId is { } id ? intents.GetValueOrDefault(id) : null;
                return new RestorationEntryDto(e.SpeciesId, e.Name, e.DefinitionId, e.Support.ToString(), e.Installation.ToString(), e.Family,
                    intent?.RemovalId is not null ? null : intent?.PreparedHash ?? (selected?.RemovalId is null ? selected?.PreparedHash : null),
                    intent?.Enabled ?? selected?.Enabled == true, intent is not null)
                    { Form = e.Form, FormLabel = e.FormLabel, PlannedPersonalId = e.PlannedPersonalId, Donor = e.Donor?.ToString(),
                        AssetsRemoved = selected?.RemovalId, RemovalStaged = intent?.RemovalId is not null,
                        AvailableForAssignment = e.AvailableForAssignment,
                        UpdateAvailable = intent?.RemovalId is null && e.DefinitionId is { } definitionId
                            && (e.Installation == SwShRestorationInstallation.External
                                ? e.Support != SwShRestorationSupport.NotSupported
                                : SwShRestorationDefinitions.Get(definitionId).HasAssetUpdate(selected)) };
            }).ToArray(), catalog.UnavailableReason is null ? cleanupDiagnostics : [.. cleanupDiagnostics, ProjectBridgeMapper.ToDto(new ValidationDiagnostic(DiagnosticSeverity.Error, catalog.UnavailableReason,
                Domain: SwShRestorationDefinitions.Domain) { Code = SwShRestorationService.SourceCode })]);
    }
    private static bool HasRestoration(EditSession session) => session.PendingEdits.Any(e => e.Domain == SwShRestorationDefinitions.Domain);
    private sealed record RestorationBatch(ChangePlan Plan, SwShRestorationPreview? Output);

    private ChangePlan CreateRestorationBatchPlan(ProjectPaths paths, EditSession session) => PrepareRestorationBatch(paths, session).Plan;

    private RestorationBatch PrepareRestorationBatch(ProjectPaths paths, EditSession session)
    {
        try
        {
            var service = new SwShRestorationService();
            session = SwShRestorationService.UpgradeSession(paths, session);
            var restoration = session with { PendingEdits = session.PendingEdits.Where(e => e.Domain == SwShRestorationDefinitions.Domain).ToArray() };
            var ordinary = session with { PendingEdits = session.PendingEdits.Where(e => e.Domain != SwShRestorationDefinitions.Domain).ToArray() };
            var domains = ordinary.PendingEdits.Select(e => GetEditSessionDomain(e.Domain)).Distinct().ToList();
            if (domains.Any(d => !IsNormalSwShDomain(d) && d != EditSessionDomain.DynamaxAdventures))
                throw new InvalidDataException("This editor cannot be combined with a Pokemon restoration. Apply its changes separately.");
            OrderDomainsBySourceDependencies(domains);
            var initial = service.Preview(paths, restoration);
            var selections = initial.State.Selections.ToDictionary(s => s.DefinitionId, StringComparer.Ordinal);
            var sources = initial.Sources.ToHashSet();
            var targets = initial.Files.Keys.ToHashSet(StringComparer.Ordinal);
            using var view = new SwShRestorationView(paths);
            var enable = restoration with { PendingEdits = restoration.PendingEdits.Where(IsEnable).ToArray() };
            var remove = restoration with { PendingEdits = restoration.PendingEdits.Where(e => !IsEnable(e)).ToArray() };
            if (enable.PendingEdits.Count > 0)
            {
                var preview = service.Preview(view.Paths, enable);
                SwShRestorationService.WritePreview(view.Paths, preview);
                view.State = new(preview.StateHash, preview.State);
            }
            foreach (var domain in domains)
            {
                ClearCriticalSwShApplyCaches();
                var slice = domain == EditSessionDomain.ShakingTrees ? ordinary : SliceSession(ordinary, domain);
                var plan = CreateSingleSwShChangePlan(view.Paths, slice, domain);
                if (!plan.CanApply) return new(new(session.Id, [], plan.Diagnostics), null);
                foreach (var write in plan.Writes)
                {
                    targets.Add(write.TargetRelativePath);
                    foreach (var source in write.Sources) sources.Add(source);
                }
                var result = ApplySingleSwShChangePlan(view.Paths, slice, plan, domain);
                if (result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return new(new(session.Id, [], result.Diagnostics), null);
            }
            if (remove.PendingEdits.Count > 0)
            {
                var removalSession = remove with { PendingEdits = [] };
                foreach (var edit in remove.PendingEdits)
                {
                    var intent = JsonSerializer.Deserialize<SwShRestorationIntent>(edit.NewValue!)!;
                    var staged = service.Stage(view.Paths, intent.DefinitionId, intent.PreparedHash, false, removalSession, intent.RemovalId is not null, intent.RemovalId, intent.AdditionalPreparedHashes);
                    if (staged.Diagnostics.Count > 0) return new(new(session.Id, [], staged.Diagnostics), null);
                    removalSession = staged.Session;
                }
                var preview = service.Preview(view.Paths, removalSession);
                SwShRestorationService.WritePreview(view.Paths, preview);
                view.State = new(preview.StateHash, preview.State);
                foreach (var selection in preview.State.Selections)
                    selections[selection.DefinitionId] = selection;
                foreach (var source in preview.Sources) sources.Add(source);
                foreach (var file in preview.Files.Keys) targets.Add(file);
                var references = SwShPokemonReferenceIndex.FindIdentities(view.Paths, initial.RemovedIdentities.ToHashSet());
                if (references.References.Count > 0) return new(new(session.Id, [], SwShRestorationService.ReferenceDiagnostics(references)), null);
                foreach (var source in references.Sources) sources.Add(source);
            }
            var finalState = new SwShRestorationState(1, selections.Values.OrderBy(s => s.DefinitionId, StringComparer.Ordinal).ToArray());
            var stored = SwShRestorationStateStore.Store(paths, finalState); using (stored.Lease) { }
            var files = targets.ToDictionary(p => p, p => File.Exists(view.Output(p)) ? File.ReadAllBytes(view.Output(p)) : null, StringComparer.Ordinal);
            var originalSources = RebaseRestorationSources(paths, sources);
            var writes = files.Keys.Order(StringComparer.Ordinal).Select(p => new PlannedFileWrite(p,
                p == SwShRestorationComposer.PersonalPath ? originalSources : [],
                File.Exists(Path.Combine(paths.OutputRootPath!, p)), "Apply Pokemon restoration and dependent editor changes together.")).ToArray();
            var combined = SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, []) { EffectivePendingEdits = session.PendingEdits }, preserveExplicitSourceLayers: true);
            return new(combined, new(files, stored.Hash, initial.PreviousStateHash, finalState, originalSources, initial.RemovedSpecies)
                { RemovedIdentities = initial.RemovedIdentities, AdoptedWholeFiles = initial.AdoptedWholeFiles });
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or JsonException or OverflowException or KM.Core.Output.OutputCoordinatorException)
        {
            return new(new(session.Id, [], [new(DiagnosticSeverity.Error, exception.Message, Domain: SwShRestorationDefinitions.Domain)
                { Code = SwShRestorationService.ConflictCode }]), null);
        }
        finally { ClearCriticalSwShApplyCaches(); }
    }

    private static bool IsEnable(PendingEdit edit)
    {
        var intent = JsonSerializer.Deserialize<SwShRestorationIntent>(edit.NewValue!)
            ?? throw new InvalidDataException("Restoration selection is missing.");
        return intent.Enabled || intent.UpdateAssets;
    }

    private static ProjectFileReference[] RebaseRestorationSources(ProjectPaths paths, IEnumerable<ProjectFileReference> sources) => sources
        .Where(s => s.Layer != ProjectFileLayer.Pending)
        .SelectMany(s =>
        {
            if (s.Layer == ProjectFileLayer.Generated) return Array.Empty<ProjectFileReference>();
            var native = s.RelativePath.StartsWith("romfs/", StringComparison.Ordinal)
                ? Path.Combine(paths.BaseRomFsPath!, s.RelativePath[6..])
                : s.RelativePath.StartsWith("exefs/", StringComparison.Ordinal) && paths.BaseExeFsPath is not null
                    ? Path.Combine(paths.BaseExeFsPath, s.RelativePath[6..]) : null;
            var layered = File.Exists(Path.Combine(paths.OutputRootPath!, s.RelativePath));
            if (s.Layer == ProjectFileLayer.Base && native is not null && File.Exists(native)) return new[] { new ProjectFileReference(ProjectFileLayer.Base, s.RelativePath) };
            if (layered) return new[] { new ProjectFileReference(ProjectFileLayer.Layered, s.RelativePath) };
            if (native is not null && File.Exists(native)) return new[] { new ProjectFileReference(ProjectFileLayer.Base, s.RelativePath) };
            return Array.Empty<ProjectFileReference>();
        }).Distinct().OrderBy(s => s.RelativePath, StringComparer.Ordinal).ThenBy(s => s.Layer).ToArray();

    private ApplyResult ApplyRestorationBatch(ProjectPaths paths, EditSession session, ChangePlan reviewed)
    {
        var batch = PrepareRestorationBatch(paths, session);
        if (!batch.Plan.CanApply || batch.Output is null) return CreateStaleSourceApplyResult(reviewed, batch.Plan.Diagnostics);
        if (!ChangePlanReview.Matches(reviewed, batch.Plan)) return CreateStaleSourceApplyResult(reviewed, [CreateStaleSwShPlanDiagnostic()]);
        SwShChangePlanSourceGuard.BindRestorationState(paths, batch.Output.PreviousStateHash, batch.Output.StateHash, batch.Output.AdoptedWholeFiles);
        var written = SwShRestorationService.WritePreview(paths, batch.Output);
        return CreateCombinedApplyResult(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, reviewed, written, []);
    }
}
