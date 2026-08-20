// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Editing;
using KM.Api.Diagnostics;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;

namespace KM.Tools.Bridge;

public static class EditSessionBridgeMapper
{
    public static EditSessionDto ToDto(EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new EditSessionDto(
            session.Id.Value,
            session.HasPendingChanges,
            session.PendingEdits.Select(ToPendingEditDto).ToArray(),
            ToDto(session.AuthoringBinding));
    }

    public static EditSession ToCore(EditSessionDto session)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            if (session.PendingEdits is null || session.PendingEdits.Any(edit => edit is null))
            {
                throw new ArgumentException("The edit session pending edits are invalid.");
            }

            return new EditSession(
                new EditSessionId(session.SessionId),
                DateTimeOffset.UtcNow,
                session.PendingEdits.Select(ToPendingEditCoreUnchecked).ToArray(),
                ToCore(session.AuthoringBinding));
        }
        catch (ArgumentException exception)
        {
            throw new EditSessionContractException("The edit session contract is invalid.", exception);
        }
    }

    public static EditSession ToCoreAllowingMalformedPendingEdits(EditSessionDto session)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            var pendingEdits = session.PendingEdits is null
                ? null!
                : session.PendingEdits
                    .Select(edit => edit is null ? null! : ToCoreAllowingMalformedSources(edit))
                    .ToArray();
            return new EditSession(
                new EditSessionId(session.SessionId),
                DateTimeOffset.UtcNow,
                pendingEdits,
                ToCore(session.AuthoringBinding));
        }
        catch (ArgumentException exception)
        {
            throw new EditSessionContractException("The edit session contract is invalid.", exception);
        }
    }

    private static PendingEdit ToCoreAllowingMalformedSources(PendingEditDto edit)
    {
        var sources = edit.Sources is null
            ? null!
            : edit.Sources
                .Select(source => source is null ? null! : ToCore(source))
                .ToArray();
        return new PendingEdit(
            edit.Domain,
            edit.Summary,
            sources,
            edit.RecordId,
            edit.Field,
            edit.NewValue,
            edit.Owner,
            ToCore(edit.Association));
    }

    public static ChangePlanDto ToDto(ChangePlan changePlan)
    {
        ArgumentNullException.ThrowIfNull(changePlan);

        return new ChangePlanDto(
            changePlan.SessionId.Value,
            changePlan.CanApply,
            changePlan.Writes.Select(ToDto).ToArray(),
            changePlan.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ChangePlan ToCore(ChangePlanDto changePlan)
    {
        ArgumentNullException.ThrowIfNull(changePlan);

        var diagnostics = changePlan.Diagnostics.Select(ToCore).ToList();
        if (!changePlan.CanApply && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(new ValidationDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is not applyable."));
        }

        return new ChangePlan(
            new EditSessionId(changePlan.SessionId),
            changePlan.Writes.Select(ToCore).ToArray(),
            diagnostics);
    }

    public static ApplyResultDto ToDto(ApplyResult applyResult)
    {
        ArgumentNullException.ThrowIfNull(applyResult);

        return new ApplyResultDto(
            applyResult.ApplyId,
            applyResult.WrittenFiles.Select(file => file.RelativePath).ToArray(),
            applyResult.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray(),
            applyResult.OutputTransaction is null
                ? null
                : new OutputTransactionResultDto(
                    applyResult.OutputTransaction.TransactionId.Value,
                    ToDto(applyResult.OutputTransaction.Outcome),
                    applyResult.OutputTransaction.Receipt.CompletedAtUtc,
                    applyResult.OutputTransaction.Receipt.Targets.Length,
                    applyResult.OutputTransaction.Receipt.OutcomeCode));
    }

    private static OutputApplyOutcomeDto ToDto(OutputApplyOutcome outcome)
    {
        return outcome switch
        {
            OutputApplyOutcome.Committed => OutputApplyOutcomeDto.Committed,
            OutputApplyOutcome.RolledBack => OutputApplyOutcomeDto.RolledBack,
            OutputApplyOutcome.RecoveryRequired => OutputApplyOutcomeDto.RecoveryRequired,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };
    }

    private static PlannedFileWriteDto ToDto(PlannedFileWrite write)
    {
        return new PlannedFileWriteDto(
            write.TargetRelativePath,
            write.Sources.Select(ToDto).ToArray(),
            write.ReplacesExistingOutput,
            write.Reason,
            write.SourceFingerprint);
    }

    private static PlannedFileWrite ToCore(PlannedFileWriteDto write)
    {
        return new PlannedFileWrite(
            write.TargetRelativePath,
            write.Sources.Select(ToCore).ToArray(),
            write.ReplacesExistingOutput,
            write.Reason,
            write.SourceFingerprint);
    }

    public static PendingEditDto ToPendingEditDto(PendingEdit edit)
    {
        return new PendingEditDto(
            edit.Domain,
            edit.Summary,
            edit.Sources.Select(ToDto).ToArray(),
            edit.RecordId,
            edit.Field,
            edit.NewValue,
            edit.Owner,
            ToDto(edit.Association));
    }

    public static PendingEdit ToPendingEditCore(PendingEditDto edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        try
        {
            return ToPendingEditCoreUnchecked(edit);
        }
        catch (ArgumentException exception)
        {
            throw new EditSessionContractException(
                "The pending edit contract is invalid.",
                exception);
        }
    }

    private static PendingEdit ToPendingEditCoreUnchecked(PendingEditDto edit)
    {
        if (edit.Sources is null || edit.Sources.Any(source => source is null))
        {
            throw new ArgumentException("The pending edit sources are invalid.");
        }

        return new PendingEdit(
            edit.Domain,
            edit.Summary,
            edit.Sources.Select(ToCore).ToArray(),
            edit.RecordId,
            edit.Field,
            edit.NewValue,
            edit.Owner,
            ToCore(edit.Association));
    }

    private static PendingEditAssociationDto? ToDto(PendingEditAssociation? association)
    {
        return association is null
            ? null
            : new PendingEditAssociationDto(
                association.Version,
                association.ChangeSetId,
                association.OperationId);
    }

    private static PendingEditAssociation? ToCore(PendingEditAssociationDto? association)
    {
        if (association is null)
        {
            return null;
        }

        try
        {
            return new PendingEditAssociation(
                association.Version,
                association.ChangeSetId,
                association.OperationId);
        }
        catch (ArgumentException exception)
        {
            throw new EditSessionContractException(
                "The pending edit association is invalid.",
                exception);
        }
    }

    private static EditSessionAuthoringBindingDto? ToDto(EditSessionAuthoringBinding? binding)
    {
        return binding is null
            ? null
            : new EditSessionAuthoringBindingDto(
                binding.Version,
                binding.ProjectId,
                binding.WorkspaceETag,
                binding.WorkspaceFingerprint,
                binding.SelectedChangeSetIds,
                binding.OutputProfileId,
                binding.OutputRootFingerprint,
                binding.WorkspacePersonalStateETag,
                binding.OutputMode switch
                {
                    "standalone" => ChangePlanOutputModeDto.Standalone,
                    "trinityModManager" => ChangePlanOutputModeDto.TrinityModManager,
                    "trinityBypass" => ChangePlanOutputModeDto.TrinityBypass,
                    null => null,
                    _ => throw new ArgumentOutOfRangeException(nameof(binding)),
                });
    }

    private static EditSessionAuthoringBinding? ToCore(EditSessionAuthoringBindingDto? binding)
    {
        return binding is null
            ? null
            : new EditSessionAuthoringBinding(
                binding.Version,
                binding.ProjectId,
                binding.WorkspaceETag,
                binding.WorkspaceFingerprint,
                binding.SelectedChangeSetIds,
                binding.OutputProfileId,
                binding.OutputRootFingerprint,
                binding.WorkspacePersonalStateETag,
                binding.OutputMode switch
                {
                    ChangePlanOutputModeDto.Standalone => "standalone",
                    ChangePlanOutputModeDto.TrinityModManager => "trinityModManager",
                    ChangePlanOutputModeDto.TrinityBypass => "trinityBypass",
                    null => null,
                    _ => throw new ArgumentOutOfRangeException(nameof(binding)),
                });
    }

    private static FileProvenanceDto ToDto(ProjectFileReference source)
    {
        return new FileProvenanceDto(ToDto(source.Layer), source.RelativePath);
    }

    private static ProjectFileReference ToCore(FileProvenanceDto source)
    {
        return new ProjectFileReference(ToCore(source.Layer), source.RelativePath);
    }

    private static ValidationDiagnostic ToCore(ApiDiagnostic diagnostic)
    {
        var sanitized = BridgeDiagnosticSanitizer.Sanitize(diagnostic);

        return new ValidationDiagnostic(
            ToCore(sanitized.Severity),
            sanitized.Message,
            sanitized.File,
            sanitized.Domain,
            sanitized.Field,
            sanitized.Expected)
        {
            Code = sanitized.Code,
        };
    }

    private static DiagnosticSeverity ToCore(ApiDiagnosticSeverity severity)
    {
        return severity switch
        {
            ApiDiagnosticSeverity.Info => DiagnosticSeverity.Info,
            ApiDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
            ApiDiagnosticSeverity.Error => DiagnosticSeverity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
        };
    }

    private static FileLayerDto ToDto(ProjectFileLayer layer)
    {
        return layer switch
        {
            ProjectFileLayer.Base => FileLayerDto.Base,
            ProjectFileLayer.Layered => FileLayerDto.Layered,
            ProjectFileLayer.Pending => FileLayerDto.Pending,
            ProjectFileLayer.Generated => FileLayerDto.Generated,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
        };
    }

    private static ProjectFileLayer ToCore(FileLayerDto layer)
    {
        return layer switch
        {
            FileLayerDto.Base => ProjectFileLayer.Base,
            FileLayerDto.Layered => ProjectFileLayer.Layered,
            FileLayerDto.Pending => ProjectFileLayer.Pending,
            FileLayerDto.Generated => ProjectFileLayer.Generated,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
        };
    }
}

public sealed class EditSessionContractException : Exception
{
    public EditSessionContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
