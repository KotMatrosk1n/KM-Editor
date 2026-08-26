// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Tools.Application;

namespace KM.Tools.Bridge;

public static class RowClipboardBridgeMapper
{
    private static readonly string[] ExcludedFieldKinds =
        ["identity", "pointer", "archiveOffset", "unknown", "presentation"];

    public static RowClipboardEnvelopeV1 ToCore(RowClipboardEnvelopeV1Dto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Scope is null
            || source.Editor is null
            || source.Source is null
            || source.Source.LogicalIdentity is null
            || source.Dependencies is null
            || source.Rows is null
            || source.ExcludedFieldKinds is null
            || source.EnvelopeSchemaVersion != RowClipboardLimits.EnvelopeSchemaVersion
            || !source.ExcludedFieldKinds.SequenceEqual(ExcludedFieldKinds, StringComparer.Ordinal))
        {
            throw new ArgumentException("The logical-row envelope version or exclusion contract is invalid.", nameof(source));
        }

        var game = ToCore(source.Scope.Game);
        var scope = new RowClipboardScope(source.Scope.ProjectId, game, source.Scope.ProfileId);
        if (!string.Equals(source.Scope.GameFamily, GameFamilyName(scope.GameFamily), StringComparison.Ordinal))
        {
            throw new ArgumentException("The logical-row envelope game family is invalid.", nameof(source));
        }

        var editor = new RowClipboardEditorSchema(
            source.Editor.EditorId,
            source.Editor.RowKind,
            source.Editor.RowSchemaVersion);
        var draft = new RowClipboardEnvelopeDraftV1(
            source.ProducerVersion,
            scope,
            editor,
            new RowClipboardSource(
                source.Source.ProjectRevision,
                ToCore(source.Source.LogicalIdentity)),
            source.Dependencies.Select(ToCore).ToArray(),
            source.Rows.Select(ToCore).ToArray());
        return RowClipboardEnvelopeV1.Validate(
            draft,
            source.Checksum,
            RowClipboardAdapterCatalog.Resolve(editor, scope));
    }

    public static RowClipboardPasteMode ToCorePasteMode(string mode) => mode switch
    {
        "replace" => RowClipboardPasteMode.Replace,
        "insert" => RowClipboardPasteMode.Insert,
        "append" => RowClipboardPasteMode.Append,
        "merge" => RowClipboardPasteMode.Merge,
        _ => throw new ArgumentException("The logical-row paste mode is invalid.", nameof(mode)),
    };

    public static RowClipboardPasteTarget ToCore(RowClipboardPasteTargetDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new RowClipboardPasteTarget(
            source.Kind,
            source.PersonalId,
            source.TableId,
            source.TrainerId,
            source.Slot);
    }

    public static PrepareRowClipboardCopyResponse ToDto(RowClipboardCopyContext source) =>
        new(
            source.Scope is null ? null : ToDto(source.Scope),
            source.SourceRevision,
            source.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());

    public static PreviewRowClipboardPasteResponse ToDto(RowClipboardPastePreviewResult source) =>
        new(
            source.Preview is null ? null : ToDto(source.Preview),
            source.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());

    public static StageRowClipboardPasteResponse ToDto(RowClipboardStageResult source) =>
        new(
            EditSessionBridgeMapper.ToDto(source.Session),
            source.Receipt is null
                ? null
                : new RowClipboardStageReceiptDto(
                    source.Receipt.HistoryEventId,
                    source.Receipt.OperationCount,
                    true,
                    source.Receipt.ClipboardChecksum,
                    source.Receipt.TargetRevision),
            source.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());

    public static PreviewRowClipboardPasteResponse InvalidPreviewEnvelope(string message) =>
        new(null, [ApiError(RowClipboardDiagnosticCodes.EnvelopeInvalid, message)]);

    public static StageRowClipboardPasteResponse InvalidStageEnvelope(
        EditSessionDto? session,
        string message) =>
        new(
            session ?? EditSessionBridgeMapper.ToDto(EditSession.Start()),
            null,
            [ApiError(RowClipboardDiagnosticCodes.EnvelopeInvalid, message)]);

    private static RowClipboardPastePreviewDto ToDto(RowClipboardPastePreview source)
    {
        var binding = source.Binding;
        return new RowClipboardPastePreviewDto(
            RowClipboardPreviewBinding.PreviewSchemaVersion,
            source.AuthorizationId,
            binding.ClipboardChecksum,
            ToDto(binding.Scope),
            ToDto(binding.Editor),
            PasteModeName(binding.Mode),
            ToDto(binding.TargetIdentity),
            binding.TargetRevision,
            binding.OperationCount,
            true,
            source.CanStage,
            source.Rows.Select(row => new RowClipboardPreviewRowDto(
                ToDto(row.TargetIdentity),
                row.Before.Select(ToDto).ToArray(),
                row.After.Select(ToDto).ToArray())).ToArray());
    }

    private static RowClipboardScopeDto ToDto(RowClipboardScope source) =>
        new(
            source.ProjectId,
            ProjectBridgeMapper.ToDto(source.Game),
            GameFamilyName(source.GameFamily),
            source.ProfileId);

    private static RowClipboardEditorSchemaDto ToDto(RowClipboardEditorSchema source) =>
        new(source.EditorId, source.RowKind, source.RowSchemaVersion);

    private static RowClipboardLogicalIdentityDto ToDto(RowClipboardLogicalIdentity source) =>
        new(source.Kind, source.Key);

    private static RowClipboardOwnedValueDto ToDto(RowClipboardOwnedValue source) =>
        new(source.FieldKey, ToDto(source.Value));

    private static RowClipboardValueDto ToDto(RowClipboardValue source) => source switch
    {
        RowClipboardBooleanValue value => new RowClipboardBooleanValueDto(value.Value),
        RowClipboardSignedIntegerValue value => new RowClipboardSignedIntegerValueDto(value.CanonicalValue),
        RowClipboardUnsignedIntegerValue value => new RowClipboardUnsignedIntegerValueDto(value.CanonicalValue),
        RowClipboardDecimalValue value => new RowClipboardDecimalValueDto(value.CanonicalValue),
        RowClipboardStringValue value => new RowClipboardStringValueDto(value.Value),
        RowClipboardDependencyValue value => new RowClipboardDependencyValueDto(
            new RowClipboardDependencyReferenceDto(
                value.Value.Kind,
                value.Value.Id,
                value.Value.Form)),
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static RowClipboardLogicalRow ToCore(RowClipboardLogicalRowV1Dto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceIdentity is null || source.Values is null)
        {
            throw new ArgumentException("A logical row is incomplete.", nameof(source));
        }

        return new RowClipboardLogicalRow(
            ToCore(source.SourceIdentity),
            source.Values.Select(ToCore).ToArray());
    }

    private static RowClipboardOwnedValue ToCore(RowClipboardOwnedValueDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Value is null)
        {
            throw new ArgumentException("A logical-row value is incomplete.", nameof(source));
        }

        return new RowClipboardOwnedValue(source.FieldKey, ToCore(source.Value));
    }

    private static RowClipboardLogicalIdentity ToCore(RowClipboardLogicalIdentityDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new RowClipboardLogicalIdentity(source.Kind, source.Key);
    }

    private static RowClipboardDependencyReference ToCore(RowClipboardDependencyReferenceDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new RowClipboardDependencyReference(source.Kind, source.Id, source.Form);
    }

    private static RowClipboardValue ToCore(RowClipboardValueDto source) => source switch
    {
        RowClipboardBooleanValueDto value => new RowClipboardBooleanValue(value.Value),
        RowClipboardSignedIntegerValueDto value => new RowClipboardSignedIntegerValue(value.Value),
        RowClipboardUnsignedIntegerValueDto value => new RowClipboardUnsignedIntegerValue(value.Value),
        RowClipboardDecimalValueDto value => new RowClipboardDecimalValue(value.Value),
        RowClipboardStringValueDto value => new RowClipboardStringValue(value.Value),
        RowClipboardDependencyValueDto value => new RowClipboardDependencyValue(ToCore(value.Value)),
        _ => throw new ArgumentException("The logical-row value kind is invalid.", nameof(source)),
    };

    private static ProjectGame ToCore(ProjectGameDto game) => game switch
    {
        ProjectGameDto.Sword => ProjectGame.Sword,
        ProjectGameDto.Shield => ProjectGame.Shield,
        ProjectGameDto.Scarlet => ProjectGame.Scarlet,
        ProjectGameDto.Violet => ProjectGame.Violet,
        ProjectGameDto.ZA => ProjectGame.ZA,
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };

    private static string PasteModeName(RowClipboardPasteMode mode) => mode switch
    {
        RowClipboardPasteMode.Replace => "replace",
        RowClipboardPasteMode.Insert => "insert",
        RowClipboardPasteMode.Append => "append",
        RowClipboardPasteMode.Merge => "merge",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static string GameFamilyName(GameFamily family) => family switch
    {
        GameFamily.SwordShield => "swordShield",
        GameFamily.ScarletViolet => "scarletViolet",
        GameFamily.LegendsZA => "legendsZA",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
    };

    private static ApiDiagnostic ApiError(string code, string message) =>
        new(ApiDiagnosticSeverity.Error, message, Domain: "rowClipboard") { Code = code };
}
