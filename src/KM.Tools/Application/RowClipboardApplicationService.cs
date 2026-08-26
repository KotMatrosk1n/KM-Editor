// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Api.Editing;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;

namespace KM.Tools.Application;

public sealed record RowClipboardPasteTarget(
    string Kind,
    int? PersonalId = null,
    string? TableId = null,
    int? TrainerId = null,
    int? Slot = null);

public sealed record RowClipboardMutationPreviewRow(
    RowClipboardLogicalIdentity TargetIdentity,
    IReadOnlyList<RowClipboardOwnedValue> Before,
    IReadOnlyList<RowClipboardOwnedValue> After);

public sealed record RowClipboardMutationResult(
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    IReadOnlyList<RowClipboardMutationPreviewRow> Rows);

public sealed record RowClipboardCopyContext(
    RowClipboardScope? Scope,
    string SourceRevision,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record RowClipboardPastePreview(
    string AuthorizationId,
    RowClipboardPreviewBinding Binding,
    bool CanStage,
    IReadOnlyList<RowClipboardMutationPreviewRow> Rows);

public sealed record RowClipboardPastePreviewResult(
    RowClipboardPastePreview? Preview,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record RowClipboardStageReceipt(
    string HistoryEventId,
    int OperationCount,
    string ClipboardChecksum,
    string TargetRevision);

public sealed record RowClipboardStageResult(
    EditSession Session,
    RowClipboardStageReceipt? Receipt,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public delegate string RowClipboardSourceFingerprintProvider(ProjectPaths paths);

public delegate RowClipboardMutationResult RowClipboardMutationProvider(
    ProjectPaths paths,
    EditSession session,
    RowClipboardEnvelopeV1 envelope,
    RowClipboardPasteMode mode,
    RowClipboardPasteTarget target);

/// <summary>
/// Authoritative preview and staging gate for typed logical-row paste operations.
/// Preview grants are process-local, single-use, scope-bound, and have no time-based expiry.
/// </summary>
public sealed class RowClipboardApplicationService
{
    private const int MaximumAuthorizations = 128;
    private const string Domain = "rowClipboard";

    private readonly object authorizationSync = new();
    private readonly object stageSync = new();
    private readonly Dictionary<string, Authorization> authorizations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> targetMutationEpochs = new(StringComparer.Ordinal);
    private readonly RowClipboardSourceFingerprintProvider captureSourceFingerprint;
    private readonly RowClipboardMutationProvider mutate;

    public RowClipboardApplicationService(
        RowClipboardSourceFingerprintProvider captureSourceFingerprint,
        RowClipboardMutationProvider mutate)
    {
        this.captureSourceFingerprint = captureSourceFingerprint
            ?? throw new ArgumentNullException(nameof(captureSourceFingerprint));
        this.mutate = mutate ?? throw new ArgumentNullException(nameof(mutate));
    }

    public RowClipboardCopyContext PrepareCopy(ProjectPaths paths, EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var currentSession = session ?? EditSession.Start();
        if (paths.SelectedGame is not { } game)
        {
            return new RowClipboardCopyContext(
                null,
                string.Empty,
                [Error(RowClipboardDiagnosticCodes.ScopeMismatch, "Select a supported game before copying logical rows.")]);
        }

        try
        {
            var scope = CreateScope(paths, game);
            return new RowClipboardCopyContext(
                scope,
                CaptureProjectRevision(paths, currentSession),
                Array.Empty<ValidationDiagnostic>());
        }
        catch (Exception exception) when (IsExpectedContractException(exception))
        {
            return new RowClipboardCopyContext(
                null,
                string.Empty,
                [Error(RowClipboardDiagnosticCodes.ScopeMismatch, "The logical-row clipboard scope could not be established.")]);
        }
    }

    public RowClipboardPastePreviewResult Preview(
        ProjectPaths paths,
        EditSession? session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode,
        RowClipboardPasteTarget target)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(target);
        var currentSession = session ?? EditSession.Start();

        var boundaryDiagnostics = ValidateBoundary(paths, currentSession, envelope, mode, target);
        if (HasErrors(boundaryDiagnostics))
        {
            return new RowClipboardPastePreviewResult(null, boundaryDiagnostics);
        }

        var targetIdentity = TryResolveTargetIdentity(envelope.Editor, target);
        if (targetIdentity is null)
        {
            return new RowClipboardPastePreviewResult(
                null,
                [Error(RowClipboardDiagnosticCodes.TargetInvalid, "The paste target is not valid for this logical-row editor.")]);
        }

        var targetScope = CreateScope(paths, paths.SelectedGame!.Value);
        lock (stageSync)
        {
            RowClipboardMutationResult mutation;
            try
            {
                mutation = mutate(paths, currentSession, envelope, mode, target);
            }
            catch (Exception exception) when (IsExpectedMutationException(exception))
            {
                return new RowClipboardPastePreviewResult(
                    null,
                    [Error(RowClipboardDiagnosticCodes.BatchRejected, "The logical-row paste could not be validated as one atomic batch.")]);
            }

            var diagnostics = mutation.Diagnostics.ToArray();
            var canStage = !HasErrors(diagnostics);
            var targetRevision = CaptureTargetRevision(targetIdentity, mutation.Rows, after: false);
            var adapter = RowClipboardAdapterCatalog.Resolve(envelope.Editor, targetScope);
            if (!canStage)
            {
                return new RowClipboardPastePreviewResult(
                    new RowClipboardPastePreview(
                        string.Empty,
                        RowClipboardPreviewBinding.Bind(envelope, adapter, targetScope, mode, targetIdentity, targetRevision),
                        false,
                        mutation.Rows),
                    diagnostics);
            }

            var binding = RowClipboardPreviewBinding.Bind(
                envelope,
                adapter,
                targetScope,
                mode,
                targetIdentity,
                targetRevision);
            var authorizationId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var epochKey = MutationEpochKey(envelope.Scope, currentSession, targetIdentity);
            var epoch = targetMutationEpochs.GetValueOrDefault(epochKey);
            AddAuthorization(new Authorization(
                authorizationId,
                envelope.Checksum,
                envelope.Scope,
                envelope.Editor,
                mode,
                targetIdentity,
                targetRevision,
                currentSession.Id.Value,
                epoch));
            return new RowClipboardPastePreviewResult(
                new RowClipboardPastePreview(authorizationId, binding, true, mutation.Rows),
                diagnostics);
        }
    }

    public RowClipboardStageResult Stage(
        ProjectPaths paths,
        EditSession? session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode,
        RowClipboardPasteTarget target,
        string authorizationId,
        string expectedTargetRevision)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(target);
        var currentSession = session ?? EditSession.Start();
        if (!IsUpperSha256(authorizationId))
        {
            return Failure(currentSession, RowClipboardDiagnosticCodes.PreviewRequired, "Preview this logical-row paste before staging it.");
        }

        if (!IsUpperSha256(expectedTargetRevision))
        {
            return Failure(currentSession, RowClipboardDiagnosticCodes.PreviewMismatch, "The logical-row paste no longer matches its preview.");
        }

        lock (stageSync)
        {
            var authorization = TakeAuthorization(authorizationId);
            if (authorization is null)
            {
                return Failure(currentSession, RowClipboardDiagnosticCodes.PreviewRequired, "The logical-row paste preview is no longer available. Preview it again.");
            }

            var boundaryDiagnostics = ValidateBoundary(paths, currentSession, envelope, mode, target, validateSourceRevision: false);
            if (HasErrors(boundaryDiagnostics))
            {
                return new RowClipboardStageResult(currentSession, null, boundaryDiagnostics);
            }

            var targetIdentity = TryResolveTargetIdentity(envelope.Editor, target);
            if (targetIdentity is null
                || !string.Equals(authorization.EnvelopeChecksum, envelope.Checksum, StringComparison.Ordinal)
                || authorization.Scope != envelope.Scope
                || authorization.Editor != envelope.Editor
                || authorization.Mode != mode
                || authorization.TargetIdentity != targetIdentity
                || !string.Equals(authorization.SessionId, currentSession.Id.Value, StringComparison.Ordinal)
                || !string.Equals(authorization.TargetRevision, expectedTargetRevision, StringComparison.Ordinal))
            {
                return Failure(currentSession, RowClipboardDiagnosticCodes.PreviewMismatch, "The logical-row paste no longer matches its preview.");
            }

            RowClipboardMutationResult mutation;
            try
            {
                mutation = mutate(paths, currentSession, envelope, mode, target);
            }
            catch (Exception exception) when (IsExpectedMutationException(exception))
            {
                return Failure(currentSession, RowClipboardDiagnosticCodes.BatchRejected, "The logical-row paste was rejected without staging any changes.");
            }

            if (HasErrors(mutation.Diagnostics))
            {
                return new RowClipboardStageResult(currentSession, null, mutation.Diagnostics);
            }

            var currentTargetRevision = CaptureTargetRevision(targetIdentity, mutation.Rows, after: false);
            if (!FixedTimeEquals(authorization.TargetRevision, currentTargetRevision))
            {
                return Failure(currentSession, RowClipboardDiagnosticCodes.TargetStale, "The paste target changed after preview. Preview it again.");
            }

            var epochKey = MutationEpochKey(envelope.Scope, currentSession, targetIdentity);
            var currentEpoch = targetMutationEpochs.GetValueOrDefault(epochKey);
            if (authorization.MutationEpoch != currentEpoch)
            {
                return Failure(currentSession, RowClipboardDiagnosticCodes.TargetStale, "Another logical-row paste changed this target. Preview it again.");
            }

            SetMutationEpoch(epochKey, checked(currentEpoch + 1));
            var receipt = new RowClipboardStageReceipt(
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                envelope.Rows.Length,
                envelope.Checksum,
                CaptureTargetRevision(targetIdentity, mutation.Rows, after: true));
            return new RowClipboardStageResult(mutation.Session, receipt, mutation.Diagnostics);
        }
    }

    public int Clear(ProjectPaths? paths = null)
    {
        lock (stageSync)
        {
            lock (authorizationSync)
            {
                if (paths is null)
                {
                    var count = authorizations.Count;
                    authorizations.Clear();
                    targetMutationEpochs.Clear();
                    return count;
                }

                var projectId = ProjectIdentity.FromPaths(paths).Value;
                var keys = authorizations
                    .Where(pair => string.Equals(pair.Value.Scope.ProjectId, projectId, StringComparison.Ordinal))
                    .Select(pair => pair.Key)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    authorizations.Remove(key);
                }

                var epochPrefix = projectId + "\0";
                foreach (var key in targetMutationEpochs.Keys
                             .Where(key => key.StartsWith(epochPrefix, StringComparison.Ordinal))
                             .ToArray())
                {
                    targetMutationEpochs.Remove(key);
                }

                return keys.Count;
            }
        }
    }

    private IReadOnlyList<ValidationDiagnostic> ValidateBoundary(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode,
        RowClipboardPasteTarget target,
        bool validateSourceRevision = true)
    {
        if (paths.SelectedGame is not { } game)
        {
            return [Error(RowClipboardDiagnosticCodes.ScopeMismatch, "Select a supported game before pasting logical rows.")];
        }

        RowClipboardAdapterSchema adapter;
        try
        {
            adapter = RowClipboardAdapterCatalog.Resolve(envelope.Editor, envelope.Scope);
        }
        catch (ArgumentException)
        {
            return [Error(RowClipboardDiagnosticCodes.UnsupportedAdapter, "This logical-row clipboard schema is not supported.")];
        }

        var expectedScope = CreateScope(paths, game);
        if (envelope.Scope != expectedScope)
        {
            return [Error(RowClipboardDiagnosticCodes.ScopeMismatch, "Logical rows can only be pasted into the exact project, game, and data profile they were copied from.")];
        }

        if (!RowClipboardAdapterCatalog.SupportsExactScope(adapter, expectedScope)
            || !adapter.PasteModes.Contains(mode))
        {
            return [Error(RowClipboardDiagnosticCodes.ModeUnavailable, "The selected paste mode is not available for this logical-row editor.")];
        }

        if (envelope.Rows.Length == 0 || envelope.Rows.Length > RowClipboardLimits.MaximumRows)
        {
            return [Error(RowClipboardDiagnosticCodes.OperationLimit, "The logical-row paste exceeds the supported operation bounds.")];
        }

        if (TryResolveTargetIdentity(envelope.Editor, target) is null)
        {
            return [Error(RowClipboardDiagnosticCodes.TargetInvalid, "The paste target is not valid for this logical-row editor.")];
        }

        if (validateSourceRevision)
        {
            var currentSourceRevision = CaptureProjectRevision(paths, session);
            if (!string.Equals(envelope.Source.ProjectRevision, currentSourceRevision, StringComparison.Ordinal))
            {
                return [Error(RowClipboardDiagnosticCodes.SourceStale, "The project or pending edits changed after these logical rows were copied. Copy them again.")];
            }
        }

        return Array.Empty<ValidationDiagnostic>();
    }

    private RowClipboardScope CreateScope(ProjectPaths paths, ProjectGame game) =>
        new(ProjectIdentity.FromPaths(paths).Value, game, RowClipboardAdapterCatalog.ProfileId(game));

    private string CaptureProjectRevision(ProjectPaths paths, EditSession session)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "row-clipboard-project-revision-v1");
        Append(hash, captureSourceFingerprint(paths));
        Append(hash, session.Id.Value);
        foreach (var edit in session.PendingEdits
                     .OrderBy(value => value.Domain, StringComparer.Ordinal)
                     .ThenBy(value => value.RecordId ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(value => value.Field ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(value => value.NewValue ?? string.Empty, StringComparer.Ordinal))
        {
            Append(hash, edit.Domain);
            Append(hash, edit.RecordId ?? string.Empty);
            Append(hash, edit.Field ?? string.Empty);
            Append(hash, edit.NewValue ?? string.Empty);
            Append(hash, edit.Owner ?? string.Empty);
            foreach (var source in edit.Sources
                         .OrderBy(value => value.RelativePath, StringComparer.Ordinal)
                         .ThenBy(value => value.Layer))
            {
                Append(hash, source.Layer.ToString());
                Append(hash, source.RelativePath);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string CaptureTargetRevision(
        RowClipboardLogicalIdentity targetIdentity,
        IReadOnlyList<RowClipboardMutationPreviewRow> rows,
        bool after)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "row-clipboard-target-revision-v1");
        Append(hash, targetIdentity.Kind);
        Append(hash, targetIdentity.Key);
        foreach (var row in rows)
        {
            Append(hash, row.TargetIdentity.Kind);
            Append(hash, row.TargetIdentity.Key);
            foreach (var ownedValue in (after ? row.After : row.Before)
                         .OrderBy(value => value.FieldKey, StringComparer.Ordinal))
            {
                Append(hash, ownedValue.FieldKey);
                AppendValue(hash, ownedValue.Value);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static RowClipboardLogicalIdentity? TryResolveTargetIdentity(
        RowClipboardEditorSchema editor,
        RowClipboardPasteTarget target)
    {
        try
        {
            if (editor == RowClipboardAdapterCatalog.PokemonLearnset.Editor
                && string.Equals(target.Kind, RowClipboardAdapterCatalog.PokemonLearnsetRowKind, StringComparison.Ordinal)
                && target.PersonalId is >= 0
                && target.TableId is null
                && target.TrainerId is null
                && target.Slot is null or (>= 0 and < RowClipboardLimits.MaximumRows))
            {
                return new RowClipboardLogicalIdentity(
                    RowClipboardAdapterCatalog.PokemonLearnsetRowKind,
                    $"personal:{target.PersonalId.Value.ToString(CultureInfo.InvariantCulture)}:slot:{target.Slot?.ToString(CultureInfo.InvariantCulture) ?? "append"}");
            }

            if (editor == RowClipboardAdapterCatalog.EncounterSlot.Editor
                && string.Equals(target.Kind, RowClipboardAdapterCatalog.EncounterSlotRowKind, StringComparison.Ordinal)
                && target.TableId is { Length: > 0 and <= 384 }
                && target.TableId == target.TableId.Trim()
                && !target.TableId.Any(char.IsControl)
                && target.Slot is >= 0 and < RowClipboardLimits.MaximumRows
                && target.PersonalId is null
                && target.TrainerId is null)
            {
                return new RowClipboardLogicalIdentity(
                    RowClipboardAdapterCatalog.EncounterSlotRowKind,
                    $"{target.TableId}#{target.Slot.Value.ToString(CultureInfo.InvariantCulture)}");
            }

            if (editor == RowClipboardAdapterCatalog.TrainerParty.Editor
                && string.Equals(target.Kind, RowClipboardAdapterCatalog.TrainerPartyRowKind, StringComparison.Ordinal)
                && target.TrainerId is >= 0
                && target.Slot is >= 0 and < 6
                && target.PersonalId is null
                && target.TableId is null)
            {
                return new RowClipboardLogicalIdentity(
                    RowClipboardAdapterCatalog.TrainerPartyRowKind,
                    $"trainer:{target.TrainerId.Value.ToString(CultureInfo.InvariantCulture)}:slot:{target.Slot.Value.ToString(CultureInfo.InvariantCulture)}");
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        return null;
    }

    private static void AppendValue(IncrementalHash hash, RowClipboardValue value)
    {
        switch (value)
        {
            case RowClipboardBooleanValue boolean:
                Append(hash, "boolean");
                Append(hash, boolean.Value ? "true" : "false");
                break;
            case RowClipboardSignedIntegerValue signed:
                Append(hash, "signedInteger");
                Append(hash, signed.CanonicalValue);
                break;
            case RowClipboardUnsignedIntegerValue unsigned:
                Append(hash, "unsignedInteger");
                Append(hash, unsigned.CanonicalValue);
                break;
            case RowClipboardDecimalValue decimalValue:
                Append(hash, "decimal");
                Append(hash, decimalValue.CanonicalValue);
                break;
            case RowClipboardStringValue text:
                Append(hash, "string");
                Append(hash, text.Value);
                break;
            case RowClipboardDependencyValue dependency:
                Append(hash, "dependencyReference");
                Append(hash, dependency.Value.Kind);
                Append(hash, dependency.Value.Id);
                Append(hash, dependency.Value.Form ?? string.Empty);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static string MutationEpochKey(
        RowClipboardScope scope,
        EditSession session,
        RowClipboardLogicalIdentity targetIdentity) =>
        string.Join('\0', scope.ProjectId, session.Id.Value, targetIdentity.Kind, targetIdentity.Key);

    private void SetMutationEpoch(string key, long epoch)
    {
        if (!targetMutationEpochs.ContainsKey(key)
            && targetMutationEpochs.Count >= MaximumAuthorizations)
        {
            targetMutationEpochs.Remove(targetMutationEpochs.Keys.First());
        }

        targetMutationEpochs[key] = epoch;
    }

    private static bool FixedTimeEquals(string left, string right) =>
        left.Length == right.Length
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));

    private static bool IsUpperSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private void AddAuthorization(Authorization authorization)
    {
        lock (authorizationSync)
        {
            if (authorizations.Count >= MaximumAuthorizations)
            {
                authorizations.Remove(authorizations.Keys.First());
            }

            authorizations.Add(authorization.Id, authorization);
        }
    }

    private Authorization? TakeAuthorization(string authorizationId)
    {
        lock (authorizationSync)
        {
            if (!authorizations.Remove(authorizationId, out var authorization))
            {
                return null;
            }

            return authorization;
        }
    }

    private static RowClipboardStageResult Failure(EditSession session, string code, string message) =>
        new(session, null, [Error(code, message)]);

    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) =>
        diagnostics.Any(value => value.Severity == DiagnosticSeverity.Error);

    private static ValidationDiagnostic Error(string code, string message) =>
        new(DiagnosticSeverity.Error, message, Domain: Domain) { Code = code };

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsExpectedContractException(Exception exception) => exception is
        ArgumentException or
        InvalidOperationException or
        IOException or
        UnauthorizedAccessException;

    private static bool IsExpectedMutationException(Exception exception) =>
        IsExpectedContractException(exception) || exception is OverflowException;

    private sealed record Authorization(
        string Id,
        string EnvelopeChecksum,
        RowClipboardScope Scope,
        RowClipboardEditorSchema Editor,
        RowClipboardPasteMode Mode,
        RowClipboardLogicalIdentity TargetIdentity,
        string TargetRevision,
        string SessionId,
        long MutationEpoch);
}
