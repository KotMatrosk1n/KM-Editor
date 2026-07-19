// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.AngeFight;

internal sealed class ZaAngeFightEditSessionService
{
    public const string AngeFightEditDomain = "workflow.angeFight";

    private const string SettingsRecordId = "za-ange-fight-v1-settings";
    private const string SettingsField = "settings";
    private const string UninstallRecordId = "za-ange-fight-v1-uninstall";
    private const string UninstallField = "uninstall";
    private const string UninstallValue = "true";

    private static readonly JsonSerializerOptions PendingJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaWorkflowFileSource fileSource;
    private readonly ZaAngeFightWorkflowService workflowService;

    public ZaAngeFightEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaWorkflowFileSource? fileSource = null,
        ZaAngeFightWorkflowService? workflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
        this.workflowService = workflowService ?? new ZaAngeFightWorkflowService(this.fileSource);
    }

    public ZaAngeFightEditResult StageSettings(
        ProjectPaths paths,
        ZaAngeFightSettings settings,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = workflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanUseExclusiveSession(currentSession, diagnostics)
            || !ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                AngeFightEditDomain,
                diagnostics)
            || !ValidateSettings(settings, diagnostics))
        {
            return new ZaAngeFightEditResult(workflow, currentSession, diagnostics);
        }

        try
        {
            var planSources = workflowService.GetPlanSources(project);
            ValidateSemanticSources(planSources);
            var canonicalSettings = Canonicalize(settings);
            var pendingEdit = new PendingEdit(
                AngeFightEditDomain,
                "Stage Ange Fight HP and direct-damage values.",
                CreatePendingSourceReferences(planSources),
                SettingsRecordId,
                SettingsField,
                EncodeSettings(canonicalSettings));
            var updatedSession = currentSession with
            {
                PendingEdits = currentSession.PendingEdits
                    .Where(edit => !string.Equals(
                        edit.Domain,
                        AngeFightEditDomain,
                        StringComparison.Ordinal))
                    .Append(pendingEdit)
                    .ToArray(),
            };
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Ange Fight values are staged for change-plan review."));
            return new ZaAngeFightEditResult(workflow, updatedSession, diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or ArgumentException
                or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ange Fight could not stage verified values: {exception.Message}",
                expected: "Verified Ange Fight source members"));
            return new ZaAngeFightEditResult(workflow, currentSession, diagnostics);
        }
    }

    public ZaAngeFightEditResult StageUninstall(
        ProjectPaths paths,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var workflow = workflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!CanUseExclusiveSession(currentSession, diagnostics)
            || !ZaEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                AngeFightEditDomain,
                diagnostics))
        {
            return new ZaAngeFightEditResult(workflow, currentSession, diagnostics);
        }

        if (!workflow.CanUninstall)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                workflow.UninstallMessage,
                expected: "Modified Ange Fight values with editable project paths"));
            return new ZaAngeFightEditResult(workflow, currentSession, diagnostics);
        }

        try
        {
            var planSources = workflowService.GetPlanSources(project);
            ValidateSemanticSources(planSources);
            var pendingEdit = new PendingEdit(
                AngeFightEditDomain,
                "Stage Ange Fight uninstall to verified vanilla values.",
                CreatePendingSourceReferences(planSources),
                UninstallRecordId,
                UninstallField,
                UninstallValue);
            var updatedSession = currentSession with
            {
                PendingEdits = currentSession.PendingEdits
                    .Where(edit => !string.Equals(
                        edit.Domain,
                        AngeFightEditDomain,
                        StringComparison.Ordinal))
                    .Append(pendingEdit)
                    .ToArray(),
            };
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Ange Fight uninstall is staged for change-plan review."));
            return new ZaAngeFightEditResult(workflow, updatedSession, diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or ArgumentException
                or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ange Fight uninstall could not be staged safely: {exception.Message}",
                expected: "Verified effective and vanilla Ange Fight members"));
            return new ZaAngeFightEditResult(workflow, currentSession, diagnostics);
        }
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = workflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        ZaEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            AngeFightEditDomain,
            diagnostics);

        if (session.PendingEdits.Count != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Ange Fight requires exactly one canonical staged settings or uninstall action.",
                expected: "One pending Ange Fight action"));
            return CreateValidation(session, diagnostics);
        }

        var edit = session.PendingEdits[0];
        if (!string.Equals(edit.Domain, AngeFightEditDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Ange Fight.",
                expected: AngeFightEditDomain));
            return CreateValidation(session, diagnostics);
        }

        var operation = DecodeOperation(edit, diagnostics);
        if (operation is null)
        {
            return CreateValidation(session, diagnostics);
        }

        if (operation.IsUninstall && !workflow.CanUninstall)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                workflow.UninstallMessage,
                expected: "Currently modified Ange Fight values"));
        }

        try
        {
            var planSources = workflowService.GetPlanSources(project);
            ValidateSemanticSources(planSources);
            var expectedSources = CreatePendingSourceReferences(planSources)
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .ToArray();
            var actualSources = edit.Sources
                .OrderBy(source => source.Layer)
                .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                .ToArray();
            if (!actualSources.SequenceEqual(expectedSources))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Ange Fight staged source ownership does not match the current verified source set.",
                    expected: "Current effective and vanilla flower, attack, and bullet sources"));
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or ArgumentException
                or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ange Fight source validation failed: {exception.Message}",
                expected: "Verified effective and vanilla Ange Fight sources"));
        }

        if (diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                operation.IsUninstall
                    ? "Pending Ange Fight uninstall is valid for change-plan review."
                    : "Pending Ange Fight values are valid for change-plan review."));
        }

        return CreateValidation(session, diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var diagnostics = Validate(paths, session).Diagnostics.ToList();
        if (outputMode != ZaOutputMode.Standalone)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Ange Fight uses standalone LayeredFS output so both Trinity members and the descriptor can be reviewed together.",
                expected: "Standalone output mode"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var operation = DecodeOperation(session.PendingEdits.Single(), diagnostics)
                ?? throw new InvalidDataException("Ange Fight pending operation is invalid.");
            var planSources = workflowService.GetPlanSources(project);
            ValidateSemanticSources(planSources);
            var preparedBatch = PrepareOutputs(paths, operation, planSources);
            var preparedOutputs = preparedBatch.Outputs;
            var plannedWriteVirtualPaths = preparedOutputs
                .Where(output => !output.DeleteOutput)
                .Select(output => output.VirtualPath)
                .ToArray();
            var plannedDeleteVirtualPaths = preparedOutputs
                .Where(output => output.DeleteOutput)
                .Select(output => output.VirtualPath)
                .ToArray();
            var fingerprintSources = CreateFingerprintSources(planSources);
            var pendingSources = CreatePendingSourceReferences(planSources);
            var payloadFingerprint = ZaAngeFightWorkflowService.Hash(
                Encoding.UTF8.GetBytes(operation.CanonicalValue));
            var writes = new List<PlannedFileWrite>();

            foreach (var preparedOutput in preparedOutputs)
            {
                var writeInfo = ZaWorkflowFileSource.CreatePlannedWrite(
                    paths,
                    preparedOutput.VirtualPath,
                    pendingSources,
                    outputMode);
                writes.Add(new PlannedFileWrite(
                    writeInfo.TargetRelativePath,
                    writeInfo.Sources,
                    writeInfo.ReplacesExistingOutput,
                    preparedOutput.DeleteOutput
                        ? "Remove the base-equivalent Ange Fight output member after restoring its owned values. "
                            + $"Action SHA-256 {payloadFingerprint}."
                        : preparedBatch.RestoresVanillaValues
                            ? "Restore Ange Fight-owned values while preserving unrelated member data. "
                            + $"Action SHA-256 {payloadFingerprint}."
                            : "Apply reviewed Ange Fight HP and direct-damage values. "
                                + $"Settings SHA-256 {payloadFingerprint}.",
                    CreatePlanSourceFingerprint(
                        paths,
                        preparedOutput.VirtualPath,
                        outputMode,
                        operation.CanonicalValue,
                        fingerprintSources)));
            }

            var descriptorPreview = ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                paths,
                plannedWriteVirtualPaths,
                plannedDeleteVirtualPaths);
            var deleteDescriptor = preparedBatch.RestoresVanillaValues
                && ZaWorkflowFileSource.CanDeleteStandaloneDescriptor(
                    paths,
                    descriptorPreview,
                    plannedWriteVirtualPaths,
                    plannedDeleteVirtualPaths);
            var descriptorWriteInfo = ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths);
            writes.Add(new PlannedFileWrite(
                descriptorWriteInfo.TargetRelativePath,
                descriptorWriteInfo.Sources
                    .Concat(pendingSources)
                    .Distinct()
                    .OrderBy(source => source.Layer)
                    .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
                    .ToArray(),
                descriptorWriteInfo.ReplacesExistingOutput,
                deleteDescriptor
                    ? "Remove the base-equivalent standalone Trinity descriptor after the reviewed Ange Fight removals. "
                        + $"Action SHA-256 {payloadFingerprint}."
                    : "Patch the Pokemon Legends Z-A Trinity descriptor for the reviewed Ange Fight writes and removals. "
                        + $"Action SHA-256 {payloadFingerprint}.",
                CreatePlanSourceFingerprint(
                    paths,
                    ZaWorkflowFileSource.DescriptorVirtualPath,
                    ZaOutputMode.Standalone,
                    operation.CanonicalValue,
                    [
                        .. fingerprintSources,
                        new FingerprintSource(
                            ZaWorkflowFileSource.DescriptorVirtualPath,
                            descriptorPreview,
                            "DescriptorPreview",
                            ZaWorkflowFileSource.DescriptorVirtualPath),
                    ])));

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Ange Fight change plan preview contains {writes.Count.ToString(CultureInfo.InvariantCulture)} target files."));
            return new ChangePlan(
                session.Id,
                writes.OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal).ToArray(),
                diagnostics);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ange Fight change plan could not be prepared: {exception.Message}",
                expected: "Verified sources and writable standalone output root"));
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
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
        IDisposable outputLock;
        try
        {
            outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
        }
        catch (Exception exception)
        {
            return ZaEditSessionSupport.CreateApplyResult(
                applyId,
                appliedAt,
                reviewedPlan,
                Array.Empty<ProjectFileReference>(),
                [
                    CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Ange Fight output is busy or unavailable: {exception.Message}",
                        expected: "Exclusive access to the configured output root"),
                ]);
        }

        using var acquiredOutputLock = outputLock;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();
        if (!ZaEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed Ange Fight change plan is stale. Review the change plan again before applying.",
                expected: "Current reviewed Ange Fight sources, values, destinations, and descriptor"));
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

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var operation = DecodeOperation(session.PendingEdits.Single(), diagnostics)
                ?? throw new InvalidDataException("Ange Fight pending operation is invalid.");
            var planSources = workflowService.GetPlanSources(project);
            ValidateSemanticSources(planSources);
            var preparedBatch = PrepareOutputs(paths, operation, planSources);
            var preparedOutputs = preparedBatch.Outputs;
            var retainedOutputs = preparedOutputs
                .Where(output => !output.DeleteOutput)
                .ToArray();
            var deletedOutputs = preparedOutputs
                .Where(output => output.DeleteOutput)
                .ToArray();

            var descriptorPreview = ZaWorkflowFileSource.CreateStandaloneDescriptorPreview(
                paths,
                retainedOutputs.Select(output => output.VirtualPath),
                deletedOutputs.Select(output => output.VirtualPath));
            var deleteDescriptor = preparedBatch.RestoresVanillaValues
                && ZaWorkflowFileSource.CanDeleteStandaloneDescriptor(
                    paths,
                    descriptorPreview,
                    retainedOutputs.Select(output => output.VirtualPath),
                    deletedOutputs.Select(output => output.VirtualPath));
            ZaWorkflowFileSource.ApplyBatch(
                paths,
                retainedOutputs
                    .Select(output => new ZaWorkflowFileWrite(
                        output.VirtualPath,
                        output.Bytes))
                    .ToArray(),
                deletedOutputs
                    .Select(output => output.VirtualPath)
                    .ToArray(),
                outputMode,
                descriptorPreview,
                deleteDescriptor);

            writtenFiles.AddRange(retainedOutputs.Select(output =>
                ZaEditSessionSupport.GeneratedReference(
                    output.VirtualPath,
                    outputMode)));
            if (!deleteDescriptor)
            {
                writtenFiles.Add(ZaEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                preparedBatch.RestoresVanillaValues
                    ? (operation.IsUninstall
                        ? "Uninstalled Ange Fight values to verified vanilla. "
                        : "Restored Ange Fight values to verified vanilla. ")
                        + $"Removed {deletedOutputs.Length.ToString(CultureInfo.InvariantCulture)} base-equivalent output member(s) "
                        + $"and retained {retainedOutputs.Length.ToString(CultureInfo.InvariantCulture)} member(s) with unrelated data. "
                        + (deleteDescriptor
                            ? "The base-equivalent standalone descriptor was removed."
                            : "The standalone descriptor was retained for remaining overrides.")
                    : ZaEditSessionSupport.CreateApplyOutputMessage(
                        ZaAngeFightWorkflowService.WorkflowLabel,
                        outputMode)));
        }
        catch (Exception exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ange Fight output could not be written: {exception.Message}",
                expected: "Verified source members and writable standalone output root"));
        }

        return ZaEditSessionSupport.CreateApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles,
            diagnostics);
    }

    private static PreparedAngeFightBatch PrepareOutputs(
        ProjectPaths paths,
        PendingOperation operation,
        IReadOnlyList<ZaAngeFightPlanSource> planSources)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var flowerSource = planSources.Single(source => source.Id == "flowers");
        var attackSource = planSources.Single(source => source.Id == "attacks");
        var bulletSource = planSources.Single(source => source.Id == "bullets");

        ZaAngeBulletMappingDocument.Validate(bulletSource.Effective.Bytes);
        ZaAngeBulletMappingDocument.Validate(bulletSource.Vanilla.Bytes);
        var effectiveFlower = ZaAngeFlowerDataDocument.Parse(flowerSource.Effective.Bytes);
        var vanillaFlower = ZaAngeFlowerDataDocument.Parse(flowerSource.Vanilla.Bytes);
        var effectiveAttack = ZaAngeAttackDataDocument.Parse(attackSource.Effective.Bytes);
        var vanillaAttack = ZaAngeAttackDataDocument.Parse(attackSource.Vanilla.Bytes);

        ZaAngeFlowerValues requestedFlowers;
        IReadOnlyList<ZaAngeAttackDamageValues> requestedAttacks;
        if (operation.IsUninstall)
        {
            requestedFlowers = vanillaFlower.Values;
            requestedAttacks = vanillaAttack.Values;
        }
        else
        {
            var settings = operation.Settings
                ?? throw new InvalidDataException("Ange Fight settings payload is missing.");
            requestedFlowers = new ZaAngeFlowerValues(
                settings.BlueFlowerHp,
                settings.RedFlowerHp);
            requestedAttacks = settings.Attacks
                .Select(selection => new ZaAngeAttackDamageValues(
                    selection.AttackId,
                    selection.DamageToPokemon,
                    selection.DamageToPlayer))
                .ToArray();
        }

        var restoresVanillaValues = requestedFlowers == vanillaFlower.Values
            && requestedAttacks.SequenceEqual(vanillaAttack.Values);
        var flowerOutput = effectiveFlower.Write(requestedFlowers);
        var attackOutput = effectiveAttack.Write(requestedAttacks);
        VerifyPreparedOutputs(
            flowerOutput,
            attackOutput,
            requestedFlowers,
            requestedAttacks);

        var flowerMatchesBaseExceptOwnedData = restoresVanillaValues
            && (flowerOutput.AsSpan().SequenceEqual(flowerSource.Vanilla.Bytes)
                || effectiveFlower.HasOnlyOwnedDifferencesFrom(vanillaFlower));
        var attackMatchesBaseExceptOwnedData = restoresVanillaValues
            && (attackOutput.AsSpan().SequenceEqual(attackSource.Vanilla.Bytes)
                || effectiveAttack.HasOnlyOwnedDifferencesFrom(vanillaAttack));
        var finalFlowerOutput = flowerMatchesBaseExceptOwnedData
            ? flowerSource.Vanilla.Bytes.ToArray()
            : flowerOutput;
        var finalAttackOutput = attackMatchesBaseExceptOwnedData
            ? attackSource.Vanilla.Bytes.ToArray()
            : attackOutput;

        return new PreparedAngeFightBatch(
            [
                new PreparedAngeFightOutput(
                    ZaDataPaths.FieldWazagimmickPublic,
                    finalFlowerOutput,
                    flowerMatchesBaseExceptOwnedData
                        && ZaWorkflowFileSource.CanDeleteStandaloneOutput(
                            paths,
                            flowerSource.Effective,
                            flowerSource.Vanilla.Bytes)),
                new PreparedAngeFightOutput(
                    ZaDataPaths.AiAttackParamArray,
                    finalAttackOutput,
                    attackMatchesBaseExceptOwnedData
                        && ZaWorkflowFileSource.CanDeleteStandaloneOutput(
                            paths,
                            attackSource.Effective,
                            attackSource.Vanilla.Bytes)),
            ],
            restoresVanillaValues);
    }

    private static void VerifyPreparedOutputs(
        byte[] flowerBytes,
        byte[] attackBytes,
        ZaAngeFlowerValues expectedFlowers,
        IReadOnlyList<ZaAngeAttackDamageValues> expectedAttacks)
    {
        var actualFlowers = ZaAngeFlowerDataDocument.Parse(flowerBytes).Values;
        if (actualFlowers != expectedFlowers)
        {
            throw new InvalidDataException(
                "Prepared Ange Fight flower output does not contain the requested HP values.");
        }

        var expectedById = expectedAttacks.ToDictionary(value => value.AttackId);
        var actualById = ZaAngeAttackDataDocument.Parse(attackBytes)
            .Values
            .ToDictionary(value => value.AttackId);
        if (expectedById.Count != actualById.Count
            || expectedById.Any(pair =>
                !actualById.TryGetValue(pair.Key, out var actual)
                || actual != pair.Value))
        {
            throw new InvalidDataException(
                "Prepared Ange Fight attack output does not contain the requested damage values.");
        }
    }

    private static bool CanUseExclusiveSession(
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (session.PendingEdits.Any(edit => !string.Equals(
                edit.Domain,
                AngeFightEditDomain,
                StringComparison.Ordinal)))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Ange Fight needs its own edit session before staging.",
                expected: "An Ange Fight-only edit session"));
            return false;
        }

        return true;
    }

    private static bool ValidateSettings(
        ZaAngeFightSettings settings,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (settings.BlueFlowerHp < 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Blue Flower HP must be between 1 and {int.MaxValue.ToString(CultureInfo.InvariantCulture)}.",
                field: "blueFlowerHp",
                expected: "Positive signed 32-bit integer"));
        }

        if (settings.RedFlowerHp < 1)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Red Flower HP must be between 1 and {int.MaxValue.ToString(CultureInfo.InvariantCulture)}.",
                field: "redFlowerHp",
                expected: "Positive signed 32-bit integer"));
        }

        if (settings.Attacks is null
            || settings.Attacks.Count != ZaAngeFightCatalog.Attacks.Count)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ange Fight requires exactly {ZaAngeFightCatalog.Attacks.Count.ToString(CultureInfo.InvariantCulture)} direct-damage rows.",
                field: "attacks",
                expected: "All ten non-Ember, timeline-referenced direct-damage attacks"));
            return false;
        }

        var expectedIds = ZaAngeFightCatalog.Attacks
            .Select(definition => definition.AttackId)
            .ToHashSet();
        var seen = new HashSet<int>();
        foreach (var attack in settings.Attacks)
        {
            if (attack is null
                || !expectedIds.Contains(attack.AttackId)
                || !seen.Add(attack.AttackId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Ange Fight attack selections contain an unknown or duplicate Attack ID.",
                    field: "attacks",
                    expected: "Each canonical Ange direct-damage Attack ID exactly once"));
                continue;
            }

            if (attack.DamageToPokemon < 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"AttackId {attack.AttackId} damage to Pokemon must be between 0 and {int.MaxValue.ToString(CultureInfo.InvariantCulture)}.",
                    field: "damageToPokemon",
                    expected: "Non-negative signed 32-bit integer"));
            }

            if (attack.DamageToPlayer < 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"AttackId {attack.AttackId} damage to player must be between 0 and {int.MaxValue.ToString(CultureInfo.InvariantCulture)}.",
                    field: "damageToPlayer",
                    expected: "Non-negative signed 32-bit integer"));
            }
        }

        if (!seen.SetEquals(expectedIds))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Ange Fight attack selections do not cover the exact canonical Attack ID set.",
                field: "attacks",
                expected: "Attack IDs 2146-2150 and 2153-2157"));
        }

        return diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
    }

    private static ZaAngeFightSettings Canonicalize(ZaAngeFightSettings settings)
    {
        var byId = settings.Attacks.ToDictionary(attack => attack.AttackId);
        return settings with
        {
            Attacks = ZaAngeFightCatalog.Attacks
                .Select(definition => byId[definition.AttackId])
                .ToArray(),
        };
    }

    private static string EncodeSettings(ZaAngeFightSettings settings)
    {
        return JsonSerializer.Serialize(Canonicalize(settings), PendingJsonOptions);
    }

    private static PendingOperation? DecodeOperation(
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.Equals(edit.RecordId, UninstallRecordId, StringComparison.Ordinal)
            && string.Equals(edit.Field, UninstallField, StringComparison.Ordinal)
            && string.Equals(edit.NewValue, UninstallValue, StringComparison.Ordinal))
        {
            return new PendingOperation(
                IsUninstall: true,
                Settings: null,
                CanonicalValue: UninstallValue);
        }

        if (!string.Equals(edit.RecordId, SettingsRecordId, StringComparison.Ordinal)
            || !string.Equals(edit.Field, SettingsField, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(edit.NewValue))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending edit does not target the canonical Ange Fight settings or uninstall action.",
                expected: $"{SettingsRecordId}/{SettingsField} or {UninstallRecordId}/{UninstallField}"));
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ZaAngeFightSettings>(
                edit.NewValue,
                PendingJsonOptions);
            if (settings is null || !ValidateSettings(settings, diagnostics))
            {
                return null;
            }

            var canonical = EncodeSettings(settings);
            if (!string.Equals(edit.NewValue, canonical, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Ange Fight settings payload is not in canonical versioned order.",
                    field: SettingsField,
                    expected: "Canonical Ange Fight settings payload"));
                return null;
            }

            return new PendingOperation(
                IsUninstall: false,
                Settings: Canonicalize(settings),
                CanonicalValue: canonical);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ange Fight settings payload is invalid: {exception.Message}",
                field: SettingsField,
                expected: "Canonical Ange Fight settings JSON"));
            return null;
        }
    }

    private static void ValidateSemanticSources(
        IReadOnlyList<ZaAngeFightPlanSource> sources)
    {
        if (sources.Count != 3
            || sources.Select(source => source.Id).Distinct(StringComparer.Ordinal).Count() != 3)
        {
            throw new InvalidDataException(
                "Ange Fight requires exactly three unique effective/base source pairs.");
        }

        var flower = sources.Single(source => source.Id == "flowers");
        var attack = sources.Single(source => source.Id == "attacks");
        var bullet = sources.Single(source => source.Id == "bullets");
        _ = ZaAngeFlowerDataDocument.Parse(flower.Effective.Bytes);
        _ = ZaAngeFlowerDataDocument.Parse(flower.Vanilla.Bytes);
        _ = ZaAngeAttackDataDocument.Parse(attack.Effective.Bytes);
        _ = ZaAngeAttackDataDocument.Parse(attack.Vanilla.Bytes);
        ZaAngeBulletMappingDocument.Validate(bullet.Effective.Bytes);
        ZaAngeBulletMappingDocument.Validate(bullet.Vanilla.Bytes);
    }

    private static IReadOnlyList<ProjectFileReference> CreatePendingSourceReferences(
        IReadOnlyList<ZaAngeFightPlanSource> sources)
    {
        return sources
            .SelectMany(source => new[]
            {
                ZaWorkflowFileSource.CreateReference(source.Effective),
                ZaWorkflowFileSource.CreateReference(source.Vanilla),
            })
            .Distinct()
            .OrderBy(source => source.Layer)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<FingerprintSource> CreateFingerprintSources(
        IReadOnlyList<ZaAngeFightPlanSource> sources)
    {
        return sources
            .SelectMany(source => new[]
            {
                new FingerprintSource(
                    source.VirtualPath,
                    source.Effective.Bytes,
                    $"Effective:{source.Effective.SourceLayer}",
                    source.Effective.RelativePath),
                new FingerprintSource(
                    source.VirtualPath,
                    source.Vanilla.Bytes,
                    "VanillaBase",
                    source.Vanilla.RelativePath),
            })
            .ToArray();
    }

    private static string CreatePlanSourceFingerprint(
        ProjectPaths paths,
        string virtualPath,
        ZaOutputMode outputMode,
        string canonicalAction,
        IReadOnlyList<FingerprintSource> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFingerprintValue(hash, "KM.ZA.AngeFight.Source.v1");
        AppendFingerprintValue(hash, virtualPath.Replace('\\', '/'));
        AppendFingerprintValue(hash, outputMode.ToString());
        AppendFingerprintValue(hash, canonicalAction);
        var targetPath = ZaWorkflowFileSource.ResolveOutputPath(
            paths,
            virtualPath,
            outputMode);
        AppendFingerprintValue(hash, NormalizeFingerprintPath(targetPath));

        foreach (var source in sources
            .OrderBy(source => source.VirtualPath, StringComparer.Ordinal)
            .ThenBy(source => source.SourceKind, StringComparer.Ordinal)
            .ThenBy(source => source.SourceIdentity, StringComparer.Ordinal))
        {
            AppendFingerprintValue(hash, source.VirtualPath.Replace('\\', '/'));
            AppendFingerprintValue(hash, source.SourceKind);
            AppendFingerprintValue(hash, source.SourceIdentity.Replace('\\', '/'));
            AppendFingerprintBytes(hash, source.Bytes);
        }

        if (File.Exists(targetPath))
        {
            AppendFingerprintValue(hash, "TargetFile");
            AppendFingerprintBytes(hash, File.ReadAllBytes(targetPath));
        }
        else if (Directory.Exists(targetPath))
        {
            AppendFingerprintValue(hash, "TargetDirectory");
        }
        else
        {
            AppendFingerprintValue(hash, "TargetMissing");
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string NormalizeFingerprintPath(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }

    private static void AppendFingerprintValue(
        IncrementalHash hash,
        string? value)
    {
        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, -1);
            hash.AppendData(lengthBytes);
            return;
        }

        var valueBytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, valueBytes.Length);
        hash.AppendData(lengthBytes);
        hash.AppendData(valueBytes);
    }

    private static void AppendFingerprintBytes(
        IncrementalHash hash,
        byte[] value)
    {
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, value.LongLength);
        hash.AppendData(lengthBytes);
        hash.AppendData(value);
    }

    private static ZaEditSessionValidation CreateValidation(
        EditSession session,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ZaEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return ZaAngeFightWorkflowService.CreateDiagnostic(
            severity,
            message,
            file,
            field,
            expected);
    }

    private sealed record PendingOperation(
        bool IsUninstall,
        ZaAngeFightSettings? Settings,
        string CanonicalValue);

    private sealed record FingerprintSource(
        string VirtualPath,
        byte[] Bytes,
        string SourceKind,
        string SourceIdentity);

    private sealed record PreparedAngeFightOutput(
        string VirtualPath,
        byte[] Bytes,
        bool DeleteOutput);

    private sealed record PreparedAngeFightBatch(
        IReadOnlyList<PreparedAngeFightOutput> Outputs,
        bool RestoresVanillaValues);
}
