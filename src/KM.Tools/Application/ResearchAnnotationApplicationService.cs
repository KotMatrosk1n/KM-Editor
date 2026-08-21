// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.Json;
using KM.Api.Research;
using KM.Api.Semantics;
using KM.Core.Projects;
using KM.Core.Workspace;

namespace KM.Tools.Application;

public sealed class ResearchAnnotationApplicationService
{
    private const int MaximumTagLength = 128;
    private static readonly WorkspaceDocumentDefinition<ResearchAnnotationDocumentDto>
        DocumentDefinition = new(
            new WorkspaceDocumentId("research-state"),
            "research-state",
            ResearchLabContract.SchemaVersion);
    private static readonly WorkspaceDocumentId AuthoringOperationLeaseId =
        new("change-sets-operation");
    private static readonly JsonSerializerOptions SizeOptions =
        PrivateWorkspaceJson.CreateSerializerOptions();
    private readonly VersionedWorkspaceDocumentStore store;

    public ResearchAnnotationApplicationService(VersionedWorkspaceDocumentStore? store = null)
    {
        this.store = store ?? new VersionedWorkspaceDocumentStore(GetDefaultAppDataRoot());
    }

    public async Task<ReadResearchAnnotationsResponse> ReadAsync(
        SemanticProjectRevisionDto revision,
        CancellationToken cancellationToken = default)
    {
        ValidateRevision(revision);
        var result = await store.ReadAsync(
                GetProjectIdentity(revision.ProjectId),
                DocumentDefinition,
                cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return new ReadResearchAnnotationsResponse(revision, false, null, null);
        }

        ValidateDocument(result.Document, requireCurrentRevision: null);
        return new ReadResearchAnnotationsResponse(revision, true, result.Document, result.ETag);
    }

    public async Task<MutateResearchAnnotationsResponse> MutateAsync(
        SemanticProjectRevisionDto revision,
        string? expectedETag,
        ResearchAnnotationMutationDto mutation,
        CancellationToken cancellationToken = default)
    {
        ValidateRevision(revision);
        ValidateExpectedETag(expectedETag);
        ArgumentNullException.ThrowIfNull(mutation);
        var identity = GetProjectIdentity(revision.ProjectId);
        using var lease = await store.AcquireProjectOperationLeaseAsync(
                identity,
                AuthoringOperationLeaseId,
                cancellationToken)
            .ConfigureAwait(false);

        var current = await store.ReadAsync(identity, DocumentDefinition, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(current?.ETag, expectedETag, StringComparison.Ordinal))
        {
            throw new WorkspaceDocumentConflictException(expectedETag, current?.ETag);
        }

        var currentDocument = current?.Document ?? new ResearchAnnotationDocumentDto(
            ResearchLabContract.SchemaVersion,
            [],
            DateTimeOffset.UnixEpoch);
        ValidateDocument(currentDocument, requireCurrentRevision: null);
        var now = DateTimeOffset.UtcNow;
        var annotations = currentDocument.Annotations.ToList();
        switch (mutation.Kind)
        {
            case ResearchAnnotationMutationKindDto.Upsert:
                ApplyUpsert(annotations, revision, mutation, now);
                break;
            case ResearchAnnotationMutationKindDto.Delete:
                ApplyDelete(annotations, mutation);
                break;
            default:
                throw Invalid("The research annotation mutation kind is unsupported.");
        }

        var document = new ResearchAnnotationDocumentDto(
            ResearchLabContract.SchemaVersion,
            annotations.OrderBy(annotation => annotation.AnnotationId, StringComparer.Ordinal).ToArray(),
            now);
        ValidateDocument(document, requireCurrentRevision: null);
        var result = await store.WriteConditionalAsync(
                identity,
                DocumentDefinition,
                document,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
        return new MutateResearchAnnotationsResponse(
            revision,
            document,
            result.WrittenAtUtc,
            result.ETag);
    }

    internal async Task<WorkspaceDocumentReadResult<ResearchAnnotationDocumentDto>?>
        ReadForRelocationAsync(string projectId, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(projectId, "project id", 256);
        var result = await store.ReadAsync(
                GetProjectIdentity(projectId),
                DocumentDefinition,
                cancellationToken)
            .ConfigureAwait(false);
        if (result is not null)
        {
            ValidateDocument(result.Document, requireCurrentRevision: null);
        }

        return result;
    }

    internal async Task<WorkspaceDocumentWriteResult> WriteForRelocationAsync(
        string projectId,
        ResearchAnnotationDocumentDto document,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        ValidateDocument(document, requireCurrentRevision: null);
        ValidateExpectedETag(expectedETag);
        return await store.WriteConditionalAsync(
                GetProjectIdentity(projectId),
                DocumentDefinition,
                document,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<WorkspaceDocumentDeleteResult> DeleteForRelocationAsync(
        string projectId,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        ValidateExpectedETag(expectedETag);
        return await store.DeleteConditionalAsync(
                GetProjectIdentity(projectId),
                DocumentDefinition.DocumentId,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static ResearchAnnotationDocumentDto RelocateDocument(
        ResearchAnnotationDocumentDto document,
        string destinationProjectId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateIdentifier(destinationProjectId, "destination project id", 256);
        ValidateDocument(document, requireCurrentRevision: null);
        return document;
    }

    private static void ApplyUpsert(
        List<ResearchAnnotationDto> annotations,
        SemanticProjectRevisionDto revision,
        ResearchAnnotationMutationDto mutation,
        DateTimeOffset now)
    {
        if (mutation.Upsert is null || mutation.AnnotationId is not null)
        {
            throw Invalid("A research annotation upsert has an invalid shape.");
        }

        string annotationId;
        int existingIndex;
        if (mutation.Upsert.AnnotationId is { } suppliedId)
        {
            ValidateOpaqueId(suppliedId, "annotation-", "annotation id");
            annotationId = suppliedId;
            existingIndex = annotations.FindIndex(annotation => string.Equals(
                annotation.AnnotationId,
                annotationId,
                StringComparison.Ordinal));
            if (existingIndex < 0)
            {
                throw Invalid("The selected research annotation does not exist.");
            }
        }
        else
        {
            do
            {
                annotationId = "annotation-" + Convert.ToHexStringLower(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
            }
            while (annotations.Any(annotation => string.Equals(
                annotation.AnnotationId,
                annotationId,
                StringComparison.Ordinal)));
            existingIndex = -1;
        }

        if (existingIndex >= 0
            && annotations[existingIndex].Target != mutation.Upsert.Target)
        {
            throw Invalid(
                "An existing research annotation target cannot be rebound during an edit.");
        }

        var createdAt = existingIndex >= 0 ? annotations[existingIndex].CreatedAtUtc : now;
        var annotation = new ResearchAnnotationDto(
            annotationId,
            mutation.Upsert.Target,
            mutation.Upsert.Text,
            mutation.Upsert.Tags,
            createdAt,
            now);
        ValidateAnnotation(annotation, existingIndex < 0 ? revision : null);
        if (existingIndex >= 0)
        {
            annotations[existingIndex] = annotation;
        }
        else
        {
            annotations.Add(annotation);
        }
    }

    private static void ApplyDelete(
        List<ResearchAnnotationDto> annotations,
        ResearchAnnotationMutationDto mutation)
    {
        if (mutation.Upsert is not null || mutation.AnnotationId is null)
        {
            throw Invalid("A research annotation delete has an invalid shape.");
        }

        ValidateOpaqueId(mutation.AnnotationId, "annotation-", "annotation id");
        var removed = annotations.RemoveAll(annotation => string.Equals(
            annotation.AnnotationId,
            mutation.AnnotationId,
            StringComparison.Ordinal));
        if (removed != 1)
        {
            throw Invalid("The selected research annotation does not exist.");
        }
    }

    private static void ValidateDocument(
        ResearchAnnotationDocumentDto document,
        SemanticProjectRevisionDto? requireCurrentRevision)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != ResearchLabContract.SchemaVersion
            || document.Annotations is null
            || document.Annotations.Count > ResearchLabContract.MaximumAnnotationCount
            || document.UpdatedAtUtc == default)
        {
            throw Invalid("The private research annotation document is invalid or too large.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var annotation in document.Annotations)
        {
            ValidateAnnotation(annotation, requireCurrentRevision);
            if (!ids.Add(annotation.AnnotationId))
            {
                throw Invalid("The private research annotation document contains duplicate ids.");
            }
        }

        if (document.Annotations.Any(annotation => annotation.UpdatedAtUtc > document.UpdatedAtUtc))
        {
            throw Invalid("The private research annotation document timestamp is inconsistent.");
        }

        if (JsonSerializer.SerializeToUtf8Bytes(document, SizeOptions).Length
            > ResearchLabContract.MaximumSerializedAnnotationDocumentBytes)
        {
            throw Invalid("The private research annotation document is too large.");
        }
    }

    private static void ValidateAnnotation(
        ResearchAnnotationDto annotation,
        SemanticProjectRevisionDto? requireCurrentRevision)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ValidateOpaqueId(annotation.AnnotationId, "annotation-", "annotation id");
        if (annotation.Target is null || annotation.Tags is null
            || annotation.CreatedAtUtc == default || annotation.UpdatedAtUtc == default
            || annotation.UpdatedAtUtc < annotation.CreatedAtUtc)
        {
            throw Invalid("A private research annotation is invalid.");
        }

        ValidatePrivateText(annotation.Text, ResearchLabContract.MaximumAnnotationTextLength, false);
        if (annotation.Tags.Count > ResearchLabContract.MaximumAnnotationTags)
        {
            throw Invalid("A private research annotation has too many tags.");
        }

        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in annotation.Tags)
        {
            ValidatePrivateText(tag, MaximumTagLength, true);
            if (!tags.Add(PortableTagIdentity(tag)))
            {
                throw Invalid("A private research annotation contains duplicate tags.");
            }
        }

        ValidateTarget(annotation.Target, requireCurrentRevision);
    }

    private static void ValidateTarget(
        ResearchAnnotationTargetDto target,
        SemanticProjectRevisionDto? requireCurrentRevision)
    {
        ValidateRevision(target.Revision);
        if (requireCurrentRevision is not null && target.Revision != requireCurrentRevision)
        {
            throw Invalid("A research annotation target is not bound to the exact project revision.");
        }

        var hasSemanticSnapshot = target.SemanticSnapshot is not null;
        var hasSemanticRecord = target.SemanticRecord is not null;
        var hasRange = target.RelativeRange is not null;
        var hasFinding = target.Finding is not null;
        switch (target.Kind)
        {
            case ResearchAnnotationTargetKindDto.SemanticRecord when hasSemanticSnapshot
                && hasSemanticRecord && !hasRange && !hasFinding:
                ValidateSemanticTarget(target);
                break;
            case ResearchAnnotationTargetKindDto.RelativeRange when !hasSemanticSnapshot
                && !hasSemanticRecord && hasRange && !hasFinding:
                ValidateRange(target.RelativeRange!);
                break;
            case ResearchAnnotationTargetKindDto.Finding when !hasSemanticSnapshot
                && !hasSemanticRecord && !hasRange && hasFinding:
                ValidateFinding(target.Finding!);
                break;
            default:
                throw Invalid("A research annotation target has an invalid discriminated shape.");
        }
    }

    private static void ValidateSemanticTarget(ResearchAnnotationTargetDto target)
    {
        if (target.SemanticSnapshot!.Revision != target.Revision
            || !IsSha256(target.SemanticSnapshot.Fingerprint)
            || target.SemanticRecord!.GameFamily != target.Revision.GameFamily
            || target.SemanticRecord.RecordKind.SchemaVersion <= 0
            || !Enum.IsDefined(target.SemanticSnapshot.Layer.Kind)
            || (target.SemanticSnapshot.Layer.Kind == SemanticSourceLayerKindDto.ComparedMod)
                != (target.SemanticSnapshot.Layer.InstanceId is not null))
        {
            throw Invalid("A semantic research annotation target is invalid.");
        }

        if (target.SemanticSnapshot.Layer.InstanceId is { } instanceId)
        {
            ValidateIdentifier(instanceId, "semantic source instance id", 1_024);
        }

        ValidateContractKey(target.SemanticRecord.Domain, "semantic domain");
        ValidateContractKey(target.SemanticRecord.RecordKind.Key, "semantic record kind");
        ValidateIdentifier(target.SemanticRecord.RecordId, "semantic record id", 1_024);
        if (target.SemanticRecord.SubrecordId is { } subrecordId)
        {
            ValidateIdentifier(subrecordId, "semantic subrecord id", 1_024);
        }
    }

    private static void ValidateRange(ResearchRelativeRangeRefDto range)
    {
        if (!IsSha256(range.ComparisonFingerprint) || range.Offset < 0
            || range.Offset > ResearchLabContract.MaximumFileBytes
            || range.Length <= 0
            || (long)range.Length > ResearchLabContract.MaximumFileBytes - range.Offset)
        {
            throw Invalid("A research annotation range is invalid.");
        }

        ResearchLabApplicationService.ValidateRelativePath(range.RelativePath);
    }

    private static void ValidateFinding(ResearchFindingRefDto finding)
    {
        if (!IsSha256(finding.ComparisonFingerprint))
        {
            throw Invalid("A research annotation finding fingerprint is invalid.");
        }

        ValidateOpaqueId(finding.FindingId, "finding-", "research finding id");
        ResearchLabApplicationService.ValidateRelativePath(finding.RelativePath);
    }

    private static void ValidateOpaqueId(string value, string prefix, string name)
    {
        if (value is null || value.Length != prefix.Length + 24
            || !value.StartsWith(prefix, StringComparison.Ordinal)
            || value.AsSpan(prefix.Length).ContainsAnyExcept("0123456789abcdef"))
        {
            throw Invalid($"The {name} is invalid.");
        }
    }

    private static void ValidateRevision(SemanticProjectRevisionDto revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ValidateIdentifier(revision.ProjectId, "project id", 256);
        if (!long.TryParse(
                revision.Generation,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var generation)
            || generation < 0
            || !string.Equals(
                revision.Generation,
                generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            || !Enum.IsDefined(revision.GameFamily)
            || !IsSha256(revision.Fingerprint))
        {
            throw Invalid("A semantic project revision is invalid.");
        }
    }

    private static void ValidatePrivateText(string value, int maximumLength, bool isTag)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !value.IsNormalized(NormalizationForm.FormC)
            || isTag && value.Contains(',', StringComparison.Ordinal)
            || value.Any(character => IsUnsafeAnnotationCharacter(character, isTag))
            || ContainsLocalPathSignature(value))
        {
            throw Invalid("A private research annotation contains invalid text.");
        }
    }

    private static void ValidateIdentifier(string value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !value.IsNormalized(NormalizationForm.FormC)
            || value.Any(IsUnsafeUnicode)
            || ContainsLocalPathSignature(value))
        {
            throw Invalid($"The {name} is invalid.");
        }
    }

    private static void ValidateContractKey(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128
            || !char.IsAsciiLetterOrDigit(value[0])
            || value[0] is not (>= 'a' and <= 'z') && !char.IsAsciiDigit(value[0])
            || !char.IsAsciiLetterOrDigit(value[^1])
            || value[^1] is not (>= 'a' and <= 'z') && !char.IsAsciiDigit(value[^1])
            || value.Any(character => !(character is >= 'a' and <= 'z'
                || char.IsAsciiDigit(character)
                || character is '.' or '_' or '-')))
        {
            throw Invalid($"The {name} is invalid.");
        }
    }

    private static WorkspaceProjectIdentity GetProjectIdentity(string projectId) =>
        WorkspaceProjectIdentity.FromProjectId(new ProjectId(projectId));

    private static void ValidateExpectedETag(string? expectedETag)
    {
        if (expectedETag is not null && !IsETag(expectedETag))
        {
            throw Invalid("The expected research annotation ETag is invalid.");
        }
    }

    private static bool IsSha256(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character)
            || character is >= 'a' and <= 'f');

    private static bool IsETag(string value) => value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character)
            || character is >= 'a' and <= 'f'
            || character is >= 'A' and <= 'F');

    private static bool IsUnsafeUnicode(char character) =>
        char.IsControl(character)
        || char.IsSurrogate(character)
        || character is '\u061c' or '\u200b' or '\u200c' or '\u200d' or '\u200e' or '\u200f'
            or '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e'
            or '\u2060' or '\u2061' or '\u2062' or '\u2063' or '\u2064'
            or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\ufeff';

    private static bool IsUnsafeAnnotationCharacter(char character, bool strict) =>
        char.IsSurrogate(character)
        || character == '\u061c'
        || character is '\u200b' or '\u200c' or '\u200d' or '\u200e' or '\u200f'
            or '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e'
            or '\u2060' or '\u2061' or '\u2062' or '\u2063' or '\u2064'
            or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\ufeff'
        || char.IsControl(character)
            && (strict || character is not ('\t' or '\n' or '\r'));

    private static bool ContainsLocalPathSignature(string value)
    {
        var candidate = value;
        for (var depth = 0; depth <= 3; depth++)
        {
            if (candidate.Contains('\\', StringComparison.Ordinal)
                || candidate.StartsWith('~')
                || ContainsDrivePath(candidate)
                || ContainsFileScheme(candidate)
                || ContainsUnixPath(candidate))
            {
                return true;
            }

            if (depth == 3 || !candidate.Contains('%', StringComparison.Ordinal))
            {
                break;
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(candidate);
            }
            catch (UriFormatException)
            {
                return true;
            }

            if (string.Equals(decoded, candidate, StringComparison.Ordinal))
            {
                return true;
            }

            candidate = decoded;
        }

        return false;
    }

    private static bool ContainsDrivePath(string value)
    {
        for (var index = 0; index + 2 < value.Length; index++)
        {
            if ((index == 0 || !char.IsAsciiLetterOrDigit(value[index - 1]))
                && char.IsAsciiLetter(value[index])
                && value[index + 1] == ':'
                && !char.IsWhiteSpace(value[index + 2]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsFileScheme(string value)
    {
        for (var index = 0; index + 4 < value.Length; index++)
        {
            if ((index == 0 || !char.IsAsciiLetterOrDigit(value[index - 1]))
                && value.AsSpan(index).StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsUnixPath(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '/'
                || index > 0 && char.IsAsciiLetterOrDigit(value[index - 1]))
            {
                continue;
            }

            var slash = value.IndexOf('/', index + 1);
            if (slash > index + 1 && slash + 1 < value.Length && !char.IsWhiteSpace(value[slash + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static string PortableTagIdentity(string value) => string.Create(
        value.Length,
        value,
        static (destination, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                destination[index] = character is >= 'A' and <= 'Z'
                    ? (char)(character + ('a' - 'A'))
                    : character;
            }
        });

    private static string GetDefaultAppDataRoot()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new InvalidOperationException("A private application-data location is unavailable.");
        }

        return Path.Combine(root, "KM Editor");
    }

    private static SemanticExploreValidationException Invalid(string message) =>
        new(message, SemanticExploreFailureKind.InvalidData);
}
