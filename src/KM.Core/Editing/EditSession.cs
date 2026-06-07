// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Editing;

public sealed record EditSession(
    EditSessionId Id,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PendingEdit> PendingEdits)
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

    public ChangePlan CreateEmptyChangePlan()
    {
        return ChangePlan.Empty(Id);
    }
}
