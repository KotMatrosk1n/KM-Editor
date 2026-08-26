// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.SV.Habitat;
using KM.SV.Workflows;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;

namespace KM.SV.HabitatCoordinates;

public sealed class SvHabitatCoordinatesEditSessionService
{
    public const string EditDomain = SvHabitatPendingEditCodec.Domain;

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvHabitatCoordinatesWorkflowService workflowService;

    public SvHabitatCoordinatesEditSessionService()
        : this(
            new ProjectWorkspaceService(),
            new SvWorkflowFileSource(
                bypassReusableBaseCache: true,
                maximumReadBytes: SvHabitatDistributionDocument.MaximumSourceBytes))
    {
    }

    internal SvHabitatCoordinatesEditSessionService(
        ProjectWorkspaceService projectWorkspaceService,
        SvWorkflowFileSource fileSource)
        : this(
            projectWorkspaceService,
            fileSource,
            new SvHabitatCoordinatesWorkflowService(fileSource))
    {
    }

    internal SvHabitatCoordinatesEditSessionService(
        ProjectWorkspaceService projectWorkspaceService,
        SvWorkflowFileSource fileSource,
        SvHabitatCoordinatesWorkflowService workflowService)
    {
        this.projectWorkspaceService = projectWorkspaceService;
        this.fileSource = fileSource;
        this.workflowService = workflowService;
    }

    public SvHabitatCoordinatesEditResult StageCoordinate(
        ProjectPaths paths,
        EditSession? session,
        SvHabitatCoordinatesQuery? query,
        string region,
        SvHabitatRowBinding binding,
        SvHabitatCoordinateChoice desiredCoordinate)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(desiredCoordinate);

        var currentSession = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        if (currentSession.PendingEdits.Count > SvHabitatDistributionDocument.MaximumMutationCount)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The habitat edit session exceeds its bounded mutation limit.",
                expected: $"At most {SvHabitatDistributionDocument.MaximumMutationCount:N0} pending habitat rows",
                code: SvHabitatCoordinatesDiagnosticCodes.EditSessionInvalid));
            return Result(paths, query, currentSession, diagnostics);
        }

        OpenedProject project;
        SvWorkflowSummary summary;
        SvHabitatBuildGateResult build;
        try
        {
            project = OpenFresh(paths);
            summary = workflowService.CreateSummary(project);
            build = SvHabitatCoordinatesProfiles.InspectBuild(paths);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"The Habitat Coordinates project could not be opened safely: {exception.Message}",
                expected: "Readable editable Scarlet/Violet project",
                code: SvHabitatCoordinatesDiagnosticCodes.ProjectUnsupported));
            return Result(paths, query, currentSession, diagnostics);
        }

        if (!build.IsSupported || summary.Availability != SvWorkflowAvailability.Available)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                build.IsSupported
                    ? "Habitat Coordinates requires an editable Scarlet/Violet project and output root."
                    : build.Message,
                expected: "Editable exact Scarlet/Violet 4.0.0 project",
                code: build.IsSupported
                    ? SvHabitatCoordinatesDiagnosticCodes.ProjectUnsupported
                    : SvHabitatCoordinatesDiagnosticCodes.BuildUnsupported));
            return Result(paths, query, currentSession, diagnostics);
        }

        SvHabitatRegionProfile profile;
        try
        {
            profile = SvHabitatCoordinatesProfiles.ResolveRegion(region);
        }
        catch (ArgumentException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                field: SvHabitatPendingEditCodec.Field,
                expected: "Supported habitat region and exact physical source binding",
                code: SvHabitatCoordinatesDiagnosticCodes.RowBindingStale));
            return Result(paths, query, currentSession, diagnostics);
        }

        if (!string.Equals(binding.SourceFile, profile.SourceFile, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The habitat row binding belongs to a different physical source file.",
                field: SvHabitatPendingEditCodec.Field,
                expected: "Exact selected-region source file",
                code: SvHabitatCoordinatesDiagnosticCodes.RowBindingStale));
            return Result(paths, query, currentSession, diagnostics);
        }

        SvHabitatLoadedRegion loaded;
        try
        {
            loaded = workflowService.LoadRegion(project, profile);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            var unavailable = SvHabitatCoordinatesWorkflowService.IsUnavailableSource(exception);
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"The habitat region source is unavailable or unsupported: {exception.Message}",
                file: $"romfs/{profile.SourceFile}",
                field: SvHabitatPendingEditCodec.Field,
                expected: "Exact supported region source or canonical KM output",
                code: unavailable
                    ? SvHabitatCoordinatesDiagnosticCodes.RegionSourceUnavailable
                    : SvHabitatCoordinatesDiagnosticCodes.RegionSourceUnsupported));
            return Result(paths, query, currentSession, diagnostics);
        }

        var row = ResolveBoundRow(loaded, binding, diagnostics);
        if (row is null)
        {
            return Result(paths, query, currentSession, diagnostics);
        }

        var desired = new SvHabitatCoordinate(desiredCoordinate.X, desiredCoordinate.Y);
        if (!loaded.BaseDocument.ObservedCoordinates.Contains(desired))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Select a coordinate observed in this exact region source.",
                field: SvHabitatPendingEditCodec.Field,
                expected: "Observed coordinate from the selected region",
                code: SvHabitatCoordinatesDiagnosticCodes.CoordinateUnobserved));
            return Result(paths, query, currentSession, diagnostics);
        }

        var remaining = currentSession.PendingEdits
            .Where(edit => !SvHabitatPendingEditCodec.IsSamePhysicalTarget(edit, region, binding))
            .ToList();
        if (row.Coordinate == desired)
        {
            var noOpSession = currentSession with { PendingEdits = remaining };
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "The selected habitat row already uses that coordinate, so no change is staged."));
            return Result(paths, query, noOpSession, diagnostics);
        }

        if (remaining.Count >= SvHabitatDistributionDocument.MaximumMutationCount)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The habitat edit session cannot accept another physical row mutation.",
                expected: $"At most {SvHabitatDistributionDocument.MaximumMutationCount:N0} pending habitat rows",
                code: SvHabitatCoordinatesDiagnosticCodes.EditSessionInvalid));
            return Result(paths, query, currentSession, diagnostics);
        }

        remaining.Add(CreatePendingEdit(loaded, binding, desiredCoordinate));
        var stagedSession = currentSession with { PendingEdits = remaining };
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            "The habitat coordinate is staged for change-plan review."));
        return Result(paths, query, stagedSession, diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var diagnostics = new List<ValidationDiagnostic>();
        if (session.PendingEdits.Count is 0 or > SvHabitatDistributionDocument.MaximumMutationCount)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                session.PendingEdits.Count == 0
                    ? "Stage an existing habitat coordinate before validating."
                    : "The habitat edit session exceeds its bounded mutation limit.",
                expected: "Bounded pending Habitat Coordinates edits",
                code: SvHabitatCoordinatesDiagnosticCodes.EditSessionInvalid));
            return new SvEditSessionValidation(session, IsValid: false, diagnostics);
        }

        OpenedProject project;
        SvWorkflowSummary summary;
        SvHabitatBuildGateResult build;
        try
        {
            project = OpenFresh(paths);
            summary = workflowService.CreateSummary(project);
            build = SvHabitatCoordinatesProfiles.InspectBuild(paths);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"The Habitat Coordinates project could not be opened safely: {exception.Message}",
                expected: "Readable editable Scarlet/Violet project",
                code: SvHabitatCoordinatesDiagnosticCodes.ProjectUnsupported));
            return new SvEditSessionValidation(session, IsValid: false, diagnostics);
        }

        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame)
            || summary.Availability != SvWorkflowAvailability.Available
            || !build.IsSupported)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                build.IsSupported
                    ? "Habitat Coordinates requires an editable Scarlet/Violet project."
                    : build.Message,
                expected: "Editable exact Scarlet/Violet 4.0.0 project",
                code: build.IsSupported
                    ? SvHabitatCoordinatesDiagnosticCodes.ProjectUnsupported
                    : SvHabitatCoordinatesDiagnosticCodes.BuildUnsupported));
            return new SvEditSessionValidation(session, IsValid: false, diagnostics);
        }

        var loadedRegions = new Dictionary<string, SvHabitatLoadedRegion>(StringComparer.Ordinal);
        var effective = new List<PendingEdit>();
        var seen = new HashSet<(string Region, int Group, int Row)>();
        foreach (var edit in session.PendingEdits)
        {
            if (!SvHabitatPendingEditCodec.TryDecode(edit, out var pending))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "A pending Habitat Coordinates edit has an invalid domain, field, row binding, or coordinate value.",
                    expected: "Versioned Habitat Coordinates pending edit",
                    code: SvHabitatCoordinatesDiagnosticCodes.EditSessionInvalid));
                continue;
            }

            var physicalKey = (
                pending.Region,
                pending.Mutation.Locator.OuterGroupOccurrence,
                pending.Mutation.Locator.RowOccurrence);
            if (!seen.Add(physicalKey))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "One physical habitat row may be staged only once per edit session.",
                    expected: "One coordinate mutation per physical row",
                    code: SvHabitatCoordinatesDiagnosticCodes.EditSessionInvalid));
                continue;
            }

            if (!loadedRegions.TryGetValue(pending.Region, out var loaded))
            {
                try
                {
                    loaded = workflowService.LoadRegion(project, pending.Region);
                    loadedRegions.Add(pending.Region, loaded);
                }
                catch (Exception exception) when (IsExpectedFailure(exception))
                {
                    var unavailable = SvHabitatCoordinatesWorkflowService.IsUnavailableSource(exception);
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"The staged habitat region source is unavailable or unsupported: {exception.Message}",
                        file: $"romfs/{pending.Mutation.Locator.SourceFile}",
                        field: SvHabitatPendingEditCodec.Field,
                        expected: "Exact supported region source or canonical KM output",
                        code: unavailable
                            ? SvHabitatCoordinatesDiagnosticCodes.RegionSourceUnavailable
                            : SvHabitatCoordinatesDiagnosticCodes.RegionSourceUnsupported));
                    continue;
                }
            }

            try
            {
                if (!string.Equals(
                    pending.SourceRevision,
                    loaded.CurrentDocument.SourceRevision,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The region source revision changed after the row was staged.");
                }

                var row = ResolveRow(loaded.CurrentDocument, pending.Mutation.Locator);
                if (row.Identity != pending.Mutation.Identity
                    || row.Coordinate != pending.Mutation.ExpectedCoordinate)
                {
                    throw new InvalidDataException("The staged row identity, preimage, or current coordinate is stale.");
                }

                if (!loaded.BaseDocument.ObservedCoordinates.Contains(pending.Mutation.DesiredCoordinate))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "A staged habitat coordinate is not present in the exact region's observed catalog.",
                        file: $"romfs/{pending.Mutation.Locator.SourceFile}",
                        field: SvHabitatPendingEditCodec.Field,
                        expected: "Observed coordinate from the exact region source",
                        code: SvHabitatCoordinatesDiagnosticCodes.CoordinateUnobserved));
                    continue;
                }

                if (row.Coordinate == pending.Mutation.DesiredCoordinate)
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Info,
                        "A no-op habitat coordinate edit was removed after source refresh."));
                    continue;
                }

                var freshBinding = new SvHabitatRowBinding(
                    loaded.Profile.SourceFile,
                    loaded.CurrentDocument.SourceRevision,
                    row.Locator.OuterGroupOccurrence,
                    row.Locator.RowOccurrence,
                    row.Locator.RowPreimageSha256,
                    row.Identity.DevNo,
                    row.Identity.FormNo,
                    row.Identity.VersionA,
                    row.Identity.VersionB,
                    row.Coordinate.X,
                    row.Coordinate.Y);
                effective.Add(CreatePendingEdit(
                    loaded,
                    freshBinding,
                    new SvHabitatCoordinateChoice(
                        pending.Mutation.DesiredCoordinate.X,
                        pending.Mutation.DesiredCoordinate.Y)));
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"A staged habitat row is stale or unsupported: {exception.Message}",
                    file: $"romfs/{pending.Mutation.Locator.SourceFile}",
                    field: SvHabitatPendingEditCodec.Field,
                    expected: "Fresh exact row preimage and observed region coordinate",
                    code: SvHabitatCoordinatesDiagnosticCodes.RowBindingStale));
            }
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                effective.Count == 0
                    ? "No Habitat Coordinates changes remain after source refresh."
                    : "Pending Habitat Coordinates changes are valid for review."));
        }

        var effectiveSession = session with { PendingEdits = effective };
        return new SvEditSessionValidation(
            effectiveSession,
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
        var effectiveSession = validation.Session;
        if (effectiveSession.PendingEdits.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Create a pending Habitat Coordinates edit before reviewing a change plan.",
                expected: "Pending existing-row coordinate mutation",
                code: SvHabitatCoordinatesDiagnosticCodes.EditSessionInvalid));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, [], diagnostics) { EffectivePendingEdits = null };
        }

        var writes = new List<PlannedFileWrite>();
        try
        {
            foreach (var group in effectiveSession.PendingEdits
                .Select(edit => (Edit: edit, Decoded: DecodeRequired(edit)))
                .GroupBy(pair => pair.Decoded.Region, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var profile = SvHabitatCoordinatesProfiles.ResolveRegion(group.Key);
                var writeInfo = SvWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    profile.SourceFile,
                    group.SelectMany(pair => pair.Edit.Sources).Distinct().ToArray(),
                    outputMode);
                writes.Add(new PlannedFileWrite(
                    writeInfo.TargetRelativePath,
                    writeInfo.Sources,
                    writeInfo.ReplacesExistingOutput,
                    $"Apply reviewed existing-cell habitat coordinates for {profile.Label}."));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                var descriptor = SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
                writes.Add(new PlannedFileWrite(
                    descriptor.TargetRelativePath,
                    descriptor.Sources,
                    descriptor.ReplacesExistingOutput,
                    "Patch the Scarlet/Violet Trinity descriptor for reviewed Habitat Coordinates output."));
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Habitat Coordinates targets could not be resolved: {exception.Message}",
                expected: "Writable bounded output targets",
                code: SvHabitatCoordinatesDiagnosticCodes.TargetResolutionFailed));
            return new ChangePlan(session.Id, [], diagnostics) { EffectivePendingEdits = null };
        }

        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Habitat Coordinates review contains {writes.Count:N0} target file(s).")));
        return SvChangePlanSourceGuard.Capture(
            paths,
            effectiveSession,
            new ChangePlan(session.Id, writes, diagnostics),
            outputMode);
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

        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
        return ApplyChangePlanLocked(paths, session, reviewedPlan, outputMode);
    }

    private ApplyResult ApplyChangePlanLocked(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvOutputMode outputMode)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        if (!ChangePlanReview.Matches(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The reviewed Habitat Coordinates plan is stale. Review it again before applying.",
                expected: "Fresh reviewed Habitat Coordinates plan",
                code: SvHabitatCoordinatesDiagnosticCodes.ReviewedPlanStale));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || currentPlan.EffectivePendingEdits is not { Count: > 0 } effectiveEdits)
        {
            return SvEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                diagnostics);
        }

        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[]? expectedDescriptor = null;
        try
        {
            foreach (var group in effectiveEdits
                .Select(edit => DecodeRequired(edit))
                .GroupBy(pending => pending.Region, StringComparer.Ordinal))
            {
                var profile = SvHabitatCoordinatesProfiles.ResolveRegion(group.Key);
                var baseBytes = fileSource.ReadBaseBytesFresh(paths, profile.SourceFile);
                var baseSha = Convert.ToHexString(SHA256.HashData(baseBytes));
                if (!string.Equals(baseSha, profile.ExactBaseSha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The exact habitat base source changed before output preparation.");
                }

                var currentBytes = fileSource.ReadCurrentBytesFresh(paths, profile.SourceFile);
                outputs[profile.SourceFile] = SvHabitatDistributionDocument.Apply(
                    baseBytes,
                    currentBytes,
                    profile.SourceFile,
                    group.Select(pending => pending.Mutation).ToArray());
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                expectedDescriptor = SvWorkflowFileSource.CreateStandaloneDescriptorPreview(
                    paths,
                    outputs.Keys);
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Habitat Coordinates output could not be prepared: {exception.Message}",
                expected: "Fresh exact build, source, row preimage, and observed coordinates",
                code: SvHabitatCoordinatesDiagnosticCodes.OutputPreparationFailed));
            return SvEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                currentPlan,
                writtenFiles,
                diagnostics);
        }

        try
        {
            var context = new SvOutputApplyContext(
                OutputReviewFingerprint.FromChangePlan(currentPlan),
                new OwnershipOwnerId("workflow.sv.habitat-coordinates"),
                [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, EditDomain)]);
            SvWorkflowFileSource.WriteBatch(
                paths,
                outputs
                    .OrderBy(output => output.Key, StringComparer.Ordinal)
                    .Select(output => new SvWorkflowFileWrite(output.Key, output.Value, context))
                    .ToArray(),
                outputMode,
                context,
                () => ChangePlanReview.Matches(
                    reviewedPlan,
                    CreateChangePlan(paths, session, outputMode)));
            foreach (var output in outputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var readback = SvWorkflowFileSource.ReadOutputBytesForVerification(
                    paths,
                    output.Key,
                    outputMode);
                if (!readback.AsSpan().SequenceEqual(output.Value))
                {
                    throw new OutputVerificationException(
                        $"Habitat Coordinates output '{output.Key}' failed exact byte readback.");
                }

                VerifyWrittenHabitat(paths, output.Key, readback);
                writtenFiles.Add(SvEditSessionSupport.GeneratedReference(output.Key, outputMode));
            }

            if (outputMode == SvOutputMode.Standalone)
            {
                if (!SvWorkflowFileSource.IsDeferredOutputBatchActive(paths, outputMode))
                {
                    var descriptorPath = SvWorkflowFileSource.ResolveOutputPath(
                        paths,
                        SvWorkflowFileSource.DescriptorVirtualPath,
                        outputMode);
                    if (expectedDescriptor is null
                        || !File.ReadAllBytes(descriptorPath).AsSpan().SequenceEqual(expectedDescriptor))
                    {
                        throw new OutputVerificationException(
                            "The Habitat Coordinates Trinity descriptor failed exact byte readback.");
                    }
                }

                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                SvEditSessionSupport.CreateApplyOutputMessage("Habitat Coordinates", outputMode)));
        }
        catch (OutputVerificationException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                expected: "Exact written bytes and full structural reparse",
                code: SvHabitatCoordinatesDiagnosticCodes.OutputVerificationFailed));
            writtenFiles.Clear();
        }
        catch (Exception exception) when (exception is OutputCoordinatorException
            || IsExpectedFailure(exception))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Habitat Coordinates output could not be committed: {exception.Message}",
                expected: "Writable output root with exact readback",
                code: SvHabitatCoordinatesDiagnosticCodes.OutputCommitFailed));
            writtenFiles.Clear();
        }

        return SvEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
    }

    private SvHabitatCoordinatesEditResult Result(
        ProjectPaths paths,
        SvHabitatCoordinatesQuery? query,
        EditSession session,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var workflow = workflowService.Load(OpenFresh(paths), query, session);
        return new SvHabitatCoordinatesEditResult(workflow, session, diagnostics);
    }

    private OpenedProject OpenFresh(ProjectPaths paths)
    {
        projectWorkspaceService.ClearMemoryCache();
        return projectWorkspaceService.Open(paths, DateTimeOffset.UtcNow);
    }

    private static SvHabitatDistributionRow? ResolveBoundRow(
        SvHabitatLoadedRegion loaded,
        SvHabitatRowBinding binding,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(binding.SourceRevision, loaded.CurrentDocument.SourceRevision, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "The habitat region source changed after this row was loaded.",
                field: SvHabitatPendingEditCodec.Field,
                expected: "Fresh region source revision",
                code: SvHabitatCoordinatesDiagnosticCodes.RowBindingStale));
            return null;
        }

        try
        {
            var locator = new SvHabitatPhysicalLocator(
                binding.SourceFile,
                binding.OuterGroupOccurrence,
                binding.RowOccurrence,
                binding.RowPreimageSha256);
            var row = ResolveRow(loaded.CurrentDocument, locator);
            if (row.Identity.DevNo != binding.DevNo
                || row.Identity.FormNo != binding.FormNo
                || row.Identity.VersionA != binding.VersionA
                || row.Identity.VersionB != binding.VersionB
                || row.Coordinate.X != binding.CurrentX
                || row.Coordinate.Y != binding.CurrentY)
            {
                throw new InvalidDataException("The habitat row semantic identity or coordinate changed.");
            }

            return row;
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                field: SvHabitatPendingEditCodec.Field,
                expected: "Exact current row occurrence, semantic identity, and preimage",
                code: SvHabitatCoordinatesDiagnosticCodes.RowBindingStale));
            return null;
        }
    }

    private static SvHabitatDistributionRow ResolveRow(
        SvHabitatDistributionDocument document,
        SvHabitatPhysicalLocator locator)
    {
        if (!string.Equals(locator.SourceFile, document.SourceFile, StringComparison.Ordinal)
            || (uint)locator.OuterGroupOccurrence >= (uint)document.Groups.Count)
        {
            throw new InvalidDataException("The habitat physical group occurrence is stale.");
        }

        var group = document.Groups[locator.OuterGroupOccurrence];
        if ((uint)locator.RowOccurrence >= (uint)group.Rows.Count)
        {
            throw new InvalidDataException("The habitat physical row occurrence is stale.");
        }

        var row = group.Rows[locator.RowOccurrence];
        if (!string.Equals(
            locator.RowPreimageSha256,
            row.Locator.RowPreimageSha256,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("The habitat exact row preimage is stale.");
        }

        return row;
    }

    private static IReadOnlyList<ProjectFileReference> CreateSources(SvHabitatLoadedRegion loaded) =>
        new[]
        {
            loaded.CurrentSource,
            new ProjectFileReference(ProjectFileLayer.Base, $"romfs/{loaded.Profile.SourceFile}"),
        }
        .Distinct()
        .ToArray();

    private static PendingEdit CreatePendingEdit(
        SvHabitatLoadedRegion loaded,
        SvHabitatRowBinding binding,
        SvHabitatCoordinateChoice desiredCoordinate)
    {
        return new PendingEdit(
            EditDomain,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Move {loaded.Profile.Label} habitat cell {binding.OuterGroupOccurrence + 1}:{binding.RowOccurrence + 1} from {binding.CurrentX},{binding.CurrentY} to {desiredCoordinate.X},{desiredCoordinate.Y}."),
            CreateSources(loaded),
            SvHabitatPendingEditCodec.CreateRecordId(loaded.Profile.Region, binding),
            SvHabitatPendingEditCodec.Field,
            SvHabitatPendingEditCodec.CreateValue(desiredCoordinate));
    }

    private void VerifyWrittenHabitat(
        ProjectPaths paths,
        string sourceFile,
        byte[] readback)
    {
        try
        {
            var baseBytes = fileSource.ReadBaseBytesFresh(paths, sourceFile);
            SvHabitatDistributionDocument.ValidateSupportedCurrent(baseBytes, readback, sourceFile);
            _ = SvHabitatDistributionDocument.Parse(readback, sourceFile);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            throw new OutputVerificationException(
                $"Habitat Coordinates output '{sourceFile}' failed full structural verification: {exception.Message}");
        }
    }

    private static SvHabitatPendingMutation DecodeRequired(PendingEdit edit)
    {
        return SvHabitatPendingEditCodec.TryDecode(edit, out var pending)
            ? pending
            : throw new InvalidDataException("A reviewed Habitat Coordinates pending edit is invalid.");
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null,
        string? code = null) =>
        SvHabitatCoordinatesWorkflowService.CreateDiagnostic(
            severity,
            message,
            file,
            field,
            expected,
            code);

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is IOException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or UnauthorizedAccessException
            or SecurityException
            or OverflowException;
    }

    private sealed class OutputVerificationException(string message) : Exception(message);
}
