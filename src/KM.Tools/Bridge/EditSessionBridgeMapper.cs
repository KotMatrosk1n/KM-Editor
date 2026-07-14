// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Editing;
using KM.Api.Diagnostics;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;

namespace KM.Tools.Bridge;

public static class EditSessionBridgeMapper
{
    public static EditSessionDto ToDto(EditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new EditSessionDto(
            session.Id.Value,
            session.HasPendingChanges,
            session.PendingEdits.Select(ToDto).ToArray());
    }

    public static EditSession ToCore(EditSessionDto session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new EditSession(
            new EditSessionId(session.SessionId),
            DateTimeOffset.UtcNow,
            session.PendingEdits.Select(ToCore).ToArray());
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
            applyResult.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
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

    private static PendingEditDto ToDto(PendingEdit edit)
    {
        return new PendingEditDto(
            edit.Domain,
            edit.Summary,
            edit.Sources.Select(ToDto).ToArray(),
            edit.RecordId,
            edit.Field,
            edit.NewValue);
    }

    private static PendingEdit ToCore(PendingEditDto edit)
    {
        return new PendingEdit(
            edit.Domain,
            edit.Summary,
            edit.Sources.Select(ToCore).ToArray(),
            edit.RecordId,
            edit.Field,
            edit.NewValue);
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
        return new ValidationDiagnostic(
            ToCore(diagnostic.Severity),
            diagnostic.Message,
            diagnostic.File,
            diagnostic.Domain,
            diagnostic.Field,
            diagnostic.Expected);
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
