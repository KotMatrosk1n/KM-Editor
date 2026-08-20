// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Editing;

public sealed record EditSession(
    EditSessionId Id,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PendingEdit> PendingEdits,
    EditSessionAuthoringBinding? AuthoringBinding = null)
{
    public bool HasPendingChanges => PendingEdits.Count > 0;

    public static EditSession Start(DateTimeOffset? createdAt = null)
    {
        return new EditSession(EditSessionId.New(), createdAt ?? DateTimeOffset.UtcNow, Array.Empty<PendingEdit>());
    }

    public EditSession WithPendingEdit(PendingEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return this with
        {
            PendingEdits = PendingEdits.Append(edit).ToArray(),
        };
    }

    public EditSession WithoutPendingEditsOwnedBy(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return this with
        {
            PendingEdits = PendingEdits
                .Where(edit => !string.Equals(edit.Owner, owner, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    public ChangePlan CreateEmptyChangePlan()
    {
        return ChangePlan.Empty(Id);
    }
}

public sealed record EditSessionAuthoringBinding
{
    public const int CurrentVersion = 1;

    public EditSessionAuthoringBinding(
        int version,
        string projectId,
        string workspaceETag,
        string workspaceFingerprint,
        IReadOnlyList<string> selectedChangeSetIds,
        string? outputProfileId,
        string outputRootFingerprint,
        string? workspacePersonalStateETag = null,
        string? outputMode = null)
    {
        if (version != CurrentVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        Version = version;
        ProjectId = ValidateFingerprintOrProjectId(projectId, nameof(projectId), isFingerprint: false);
        WorkspaceETag = ValidateFingerprintOrProjectId(workspaceETag, nameof(workspaceETag), isFingerprint: true);
        WorkspaceFingerprint = ValidateFingerprintOrProjectId(
            workspaceFingerprint,
            nameof(workspaceFingerprint),
            isFingerprint: true);
        ArgumentNullException.ThrowIfNull(selectedChangeSetIds);
        if (selectedChangeSetIds.Count > 64
            || selectedChangeSetIds.Distinct(StringComparer.Ordinal).Count() != selectedChangeSetIds.Count
            || selectedChangeSetIds.Any(id => !IsAssociationId(id)))
        {
            throw new ArgumentException("The selected change-set ids are invalid.", nameof(selectedChangeSetIds));
        }

        SelectedChangeSetIds = selectedChangeSetIds.ToArray();
        if (outputProfileId is not null && !IsAssociationId(outputProfileId))
        {
            throw new ArgumentException("The output profile id is invalid.", nameof(outputProfileId));
        }

        OutputProfileId = outputProfileId;
        if (!IsFingerprint(outputRootFingerprint)
            || (outputProfileId is null
                ? workspacePersonalStateETag is not null
                : !IsFingerprint(workspacePersonalStateETag)))
        {
            throw new ArgumentException("The output-profile authoring binding is invalid.");
        }

        if (outputMode is not null
            && outputMode is not ("standalone" or "trinityModManager" or "trinityBypass"))
        {
            throw new ArgumentException("The output mode authoring binding is invalid.", nameof(outputMode));
        }

        WorkspacePersonalStateETag = workspacePersonalStateETag?.ToLowerInvariant();
        OutputRootFingerprint = outputRootFingerprint.ToLowerInvariant();
        OutputMode = outputMode;
    }

    public int Version { get; }

    public string ProjectId { get; }

    public string WorkspaceETag { get; }

    public string WorkspaceFingerprint { get; }

    public IReadOnlyList<string> SelectedChangeSetIds { get; }

    public string? OutputProfileId { get; }

    public string? WorkspacePersonalStateETag { get; }

    public string OutputRootFingerprint { get; }

    public string? OutputMode { get; }

    private static string ValidateFingerprintOrProjectId(
        string value,
        string parameterName,
        bool isFingerprint)
    {
        var valid = isFingerprint
            ? value is { Length: 64 } && value.All(Uri.IsHexDigit)
            : value is { Length: > 0 and <= 128 }
                && value == value.Trim()
                && !value.Any(char.IsControl);
        if (!valid)
        {
            throw new ArgumentException("An edit-session authoring binding is invalid.", parameterName);
        }

        return isFingerprint ? value.ToLowerInvariant() : value;
    }

    private static bool IsAssociationId(string? value)
    {
        return value is { Length: > 0 and <= PendingEditAssociation.MaximumIdLength }
            && char.IsAsciiLetterOrDigit(value[0])
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_');
    }

    private static bool IsFingerprint(string? value)
    {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }
}
