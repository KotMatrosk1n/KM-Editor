// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;

namespace KM.Core.Output;

public sealed record OutputHistoryChange(string Domain, string Summary, string? RecordId, string? Field, string? NewValue);

public sealed record OutputRepairReference(string Kind, string Path, string RecordId, string Value);

// Advisory evidence of a committed repair, retained independently of rolling receipts.
public sealed record OutputRepairStamp(string Kind, string Path, string RecordId, string Value,
    string Sha256, DateTimeOffset AppliedAtUtc);

public sealed record OutputHistoryDetails(int TotalChangeCount, IReadOnlyList<OutputHistoryChange> Changes, bool Truncated)
{
    public const int MaximumChanges = 4096;
    public const int MaximumTextLength = 2048;
    public const int MaximumTextCharacters = 262144;

    // Repair evidence must survive truncation of the human-readable change list.
    public IReadOnlyList<OutputRepairReference> Repairs { get; init; } = [];

    public static OutputHistoryDetails? Capture(IReadOnlyList<PendingEdit>? edits)
    {
        if (edits is null) return null;
        var changes = new List<OutputHistoryChange>();
        var remaining = MaximumTextCharacters;
        var truncated = false;
        string? Bound(string? value)
        {
            if (value is null) return null;
            if (value.Length <= MaximumTextLength) return value;
            truncated = true;
            return value[..MaximumTextLength];
        }
        foreach (var edit in edits)
        {
            var change = new OutputHistoryChange(Bound(edit.Domain)!, Bound(edit.Summary)!, Bound(edit.RecordId), Bound(edit.Field), Bound(edit.NewValue));
            var length = TextLength(change);
            if (changes.Count == MaximumChanges || length > remaining) { truncated = true; break; }
            remaining -= length;
            changes.Add(change);
        }
        return new(edits.Count, changes.ToArray(), truncated)
        {
            Repairs = edits.Where(edit => edit.Owner == PendingEditOwners.SwShAiFlagsRepair
                && edit.Domain == "workflow.trainers" && edit.Field == "aiFlags" && edit.Sources.Count == 1
                && edit.RecordId is not null && edit.NewValue is not null)
                .Select(edit => new OutputRepairReference(edit.Owner!, edit.Sources[0].RelativePath, edit.RecordId!, edit.NewValue!))
                .ToArray(),
        };
    }

    public void Validate()
    {
        if (Changes is null || TotalChangeCount < Changes.Count || Changes.Count > MaximumChanges ||
            (!Truncated && TotalChangeCount != Changes.Count) ||
            Changes.Any(change => change is null || string.IsNullOrWhiteSpace(change.Domain) || change.Summary is null ||
                new[] { change.Domain, change.Summary, change.RecordId, change.Field, change.NewValue }.Any(value => value?.Length > MaximumTextLength)) ||
            Changes.Sum(change => (long)TextLength(change)) > MaximumTextCharacters ||
            Repairs is null || Repairs.Count > 4096 || Repairs.Any(repair => repair is null
                || repair.Kind != PendingEditOwners.SwShAiFlagsRepair || repair.Path is not { Length: > 0 and <= 512 }
                || repair.RecordId is not { Length: > 0 and <= 32 } || repair.Value is not { Length: > 0 and <= 32 }))
            throw new InvalidDataException("Output change history exceeds its supported bounds.");
    }

    private static int TextLength(OutputHistoryChange change) => change.Domain.Length + change.Summary.Length +
        (change.RecordId?.Length ?? 0) + (change.Field?.Length ?? 0) + (change.NewValue?.Length ?? 0);
}
