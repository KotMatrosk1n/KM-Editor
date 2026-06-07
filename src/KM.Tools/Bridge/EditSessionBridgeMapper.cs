// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Editing;
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
