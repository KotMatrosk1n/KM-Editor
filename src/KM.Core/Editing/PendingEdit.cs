// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.Core.Editing;

public sealed record PendingEdit(
    string Domain,
    string Summary,
    IReadOnlyList<ProjectFileReference> Sources,
    string? RecordId = null,
    string? Field = null,
    string? NewValue = null,
    string? Owner = null,
    PendingEditAssociation? Association = null);

/// <summary>
/// Versioned authoring ownership for a pending edit. This is deliberately
/// separate from <see cref="PendingEdit.Owner"/>, which identifies the workflow
/// that produced an edit and is not an authoring container id.
/// </summary>
public sealed record PendingEditAssociation
{
    public const int CurrentVersion = 1;
    public const int MaximumIdLength = 128;

    public PendingEditAssociation(int version, string changeSetId, string operationId)
    {
        if (version != CurrentVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                $"A pending-edit association must use version {CurrentVersion}.");
        }

        Version = version;
        ChangeSetId = ValidateId(changeSetId, nameof(changeSetId));
        OperationId = ValidateId(operationId, nameof(operationId));
    }

    public int Version { get; }

    public string ChangeSetId { get; }

    public string OperationId { get; }

    private static string ValidateId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > MaximumIdLength
            || !char.IsAsciiLetterOrDigit(value[0])
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '-' or '_')))
        {
            throw new ArgumentException(
                "A pending-edit association id is invalid.",
                parameterName);
        }

        return value;
    }
}

public static class PendingEditOwners
{
    public const string DumpImporterItemsPrice = "workflow.dump-import.items-price";
}
