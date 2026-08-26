// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;
using KM.Api.Diagnostics;
using KM.Api.Projects;

namespace KM.Api.Editing;

public static class RowClipboardDiagnosticCodes
{
    public const string BatchRejected = "KM-ROW-CLIPBOARD-BATCH-REJECTED";
    public const string EnvelopeInvalid = "KM-ROW-CLIPBOARD-ENVELOPE-INVALID";
    public const string ModeUnavailable = "KM-ROW-CLIPBOARD-MODE-UNAVAILABLE";
    public const string OperationLimit = "KM-ROW-CLIPBOARD-OPERATION-LIMIT";
    public const string PreviewMismatch = "KM-ROW-CLIPBOARD-PREVIEW-MISMATCH";
    public const string PreviewRequired = "KM-ROW-CLIPBOARD-PREVIEW-REQUIRED";
    public const string ScopeMismatch = "KM-ROW-CLIPBOARD-SCOPE-MISMATCH";
    public const string SourceStale = "KM-ROW-CLIPBOARD-SOURCE-STALE";
    public const string TargetInvalid = "KM-ROW-CLIPBOARD-TARGET-INVALID";
    public const string TargetStale = "KM-ROW-CLIPBOARD-TARGET-STALE";
    public const string UnsupportedAdapter = "KM-ROW-CLIPBOARD-ADAPTER-UNSUPPORTED";
}

public sealed record RowClipboardScopeDto(
    string ProjectId,
    ProjectGameDto Game,
    string GameFamily,
    string ProfileId);

public sealed record RowClipboardEditorSchemaDto(
    string EditorId,
    string RowKind,
    int RowSchemaVersion);

public sealed record RowClipboardLogicalIdentityDto(string Kind, string Key);

public sealed record RowClipboardDependencyReferenceDto(
    string Kind,
    string Id,
    string? Form);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RowClipboardBooleanValueDto), "boolean")]
[JsonDerivedType(typeof(RowClipboardSignedIntegerValueDto), "signedInteger")]
[JsonDerivedType(typeof(RowClipboardUnsignedIntegerValueDto), "unsignedInteger")]
[JsonDerivedType(typeof(RowClipboardDecimalValueDto), "decimal")]
[JsonDerivedType(typeof(RowClipboardStringValueDto), "string")]
[JsonDerivedType(typeof(RowClipboardDependencyValueDto), "dependencyReference")]
public abstract record RowClipboardValueDto;

public sealed record RowClipboardBooleanValueDto(bool Value) : RowClipboardValueDto;

public sealed record RowClipboardSignedIntegerValueDto(string Value) : RowClipboardValueDto;

public sealed record RowClipboardUnsignedIntegerValueDto(string Value) : RowClipboardValueDto;

public sealed record RowClipboardDecimalValueDto(string Value) : RowClipboardValueDto;

public sealed record RowClipboardStringValueDto(string Value) : RowClipboardValueDto;

public sealed record RowClipboardDependencyValueDto(
    RowClipboardDependencyReferenceDto Value) : RowClipboardValueDto;

public sealed record RowClipboardOwnedValueDto(
    string FieldKey,
    RowClipboardValueDto Value);

public sealed record RowClipboardLogicalRowV1Dto(
    RowClipboardLogicalIdentityDto SourceIdentity,
    IReadOnlyList<RowClipboardOwnedValueDto> Values);

public sealed record RowClipboardSourceV1Dto(
    string ProjectRevision,
    RowClipboardLogicalIdentityDto LogicalIdentity);

public sealed record RowClipboardEnvelopeV1Dto(
    int EnvelopeSchemaVersion,
    string ProducerVersion,
    RowClipboardScopeDto Scope,
    RowClipboardEditorSchemaDto Editor,
    RowClipboardSourceV1Dto Source,
    IReadOnlyList<RowClipboardDependencyReferenceDto> Dependencies,
    IReadOnlyList<RowClipboardLogicalRowV1Dto> Rows,
    IReadOnlyList<string> ExcludedFieldKinds,
    string Checksum);

public sealed record PrepareRowClipboardCopyRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record PrepareRowClipboardCopyResponse(
    RowClipboardScopeDto? Scope,
    string SourceRevision,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record RowClipboardPasteTargetDto(
    string Kind,
    int? PersonalId = null,
    string? TableId = null,
    int? TrainerId = null,
    int? Slot = null);

public sealed record PreviewRowClipboardPasteRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    RowClipboardEnvelopeV1Dto Envelope,
    string Mode,
    RowClipboardPasteTargetDto Target);

public sealed record RowClipboardPreviewRowDto(
    RowClipboardLogicalIdentityDto TargetIdentity,
    IReadOnlyList<RowClipboardOwnedValueDto> Before,
    IReadOnlyList<RowClipboardOwnedValueDto> After);

public sealed record RowClipboardPastePreviewDto(
    int PreviewSchemaVersion,
    string AuthorizationId,
    string ClipboardChecksum,
    RowClipboardScopeDto Scope,
    RowClipboardEditorSchemaDto Editor,
    string Mode,
    RowClipboardLogicalIdentityDto TargetIdentity,
    string TargetRevision,
    int OperationCount,
    bool AtomicHistoryEvent,
    bool CanStage,
    IReadOnlyList<RowClipboardPreviewRowDto> Rows);

public sealed record PreviewRowClipboardPasteResponse(
    RowClipboardPastePreviewDto? Preview,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageRowClipboardPasteRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    RowClipboardEnvelopeV1Dto Envelope,
    string Mode,
    RowClipboardPasteTargetDto Target,
    string AuthorizationId,
    string ExpectedTargetRevision);

public sealed record RowClipboardStageReceiptDto(
    string HistoryEventId,
    int OperationCount,
    bool AtomicHistoryEvent,
    string ClipboardChecksum,
    string TargetRevision);

public sealed record StageRowClipboardPasteResponse(
    EditSessionDto Session,
    RowClipboardStageReceiptDto? Receipt,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ClearRowClipboardAuthorizationsRequest(
    ProjectPathsDto? Paths = null);

public sealed record ClearRowClipboardAuthorizationsResponse(int ClearedCount);
