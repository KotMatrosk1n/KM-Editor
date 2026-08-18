// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Output;

public enum OutputCheckpointCoverage
{
    OwnedFiles = 1,
}

public readonly record struct OutputCheckpointId
{
    public OutputCheckpointId(string value)
    {
        if (value is null
            || value.Length != 32
            || value.Any(character =>
                !char.IsAsciiDigit(character)
                && character is not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "An output checkpoint id must be 32 lowercase hexadecimal characters.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static OutputCheckpointId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}

public sealed record OutputCheckpointSummary
{
    public OutputCheckpointSummary(
        OutputCheckpointId id,
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        DateTimeOffset createdAtUtc,
        int fileCount,
        long totalBytes,
        string manifestFingerprint,
        OutputCheckpointCoverage coverage = OutputCheckpointCoverage.OwnedFiles,
        string? label = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("A checkpoint requires checkpoint and project identities.");
        }

        _ = SemanticContractGuards.StableId(projectId.Value, nameof(projectId));

        if (fileCount < 0 || fileCount > OutputLimits.MaximumIntegrityEntries
            || totalBytes < 0 || totalBytes > OutputLimits.MaximumCheckpointBytes
            || createdAtUtc == default)
        {
            throw new ArgumentException("Checkpoint size metadata is invalid or out of bounds.");
        }

        if (label is { Length: > 256 } || label?.Any(char.IsControl) is true)
        {
            throw new ArgumentException("A checkpoint label is invalid or too large.", nameof(label));
        }

        Id = id;
        ProjectId = projectId;
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        OutputMode = SemanticContractGuards.ContractKey(outputMode, nameof(outputMode));
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        FileCount = fileCount;
        TotalBytes = totalBytes;
        ManifestFingerprint = SemanticContractGuards.Sha256Fingerprint(
            manifestFingerprint,
            nameof(manifestFingerprint));
        Coverage = SemanticContractGuards.DefinedEnum(coverage, nameof(coverage));
        Label = label;
    }

    public OutputCheckpointId Id { get; }

    public ProjectId ProjectId { get; }

    public GameFamily GameFamily { get; }

    public string OutputMode { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public int FileCount { get; }

    public long TotalBytes { get; }

    public string ManifestFingerprint { get; }

    /// <summary>
    /// Checkpoints contain only files proven current in the coordinator ownership inventory.
    /// Foreign and unmanaged output files are never copied or removed by checkpoint restore.
    /// </summary>
    public OutputCheckpointCoverage Coverage { get; }

    public string? Label { get; }
}

public sealed record OutputCheckpointList
{
    public OutputCheckpointList(
        OutputStateRevision revision,
        IEnumerable<OutputCheckpointSummary> checkpoints)
    {
        if (string.IsNullOrWhiteSpace(revision.Value))
        {
            throw new ArgumentException("A checkpoint list requires a state revision.", nameof(revision));
        }

        ArgumentNullException.ThrowIfNull(checkpoints);
        Revision = revision;
        Checkpoints = checkpoints.ToImmutableArray();
        if (Checkpoints.Length > OutputLimits.MaximumCheckpoints
            || Checkpoints.Any(checkpoint => checkpoint is null))
        {
            throw new ArgumentException("A checkpoint list is invalid or out of bounds.", nameof(checkpoints));
        }
    }

    public OutputStateRevision Revision { get; }

    public ImmutableArray<OutputCheckpointSummary> Checkpoints { get; }
}

public sealed record OutputCheckpointRestorePreview
{
    public OutputCheckpointRestorePreview(
        OutputCheckpointId checkpointId,
        string manifestFingerprint,
        OutputStateRevision outputRevision,
        IEnumerable<RelativeOutputPath> targets,
        int writeCount,
        int deleteCount,
        long writeBytes)
    {
        if (string.IsNullOrWhiteSpace(checkpointId.Value))
        {
            throw new ArgumentException("A restore preview requires a checkpoint id.", nameof(checkpointId));
        }

        ManifestFingerprint = SemanticContractGuards.Sha256Fingerprint(
            manifestFingerprint,
            nameof(manifestFingerprint));
        if (string.IsNullOrWhiteSpace(outputRevision.Value))
        {
            throw new ArgumentException("A restore preview requires an output revision.", nameof(outputRevision));
        }

        ArgumentNullException.ThrowIfNull(targets);
        Targets = targets.ToImmutableArray();
        if (Targets.Length > OutputLimits.MaximumMutationsPerApply
            || Targets.Any(path => path is null)
            || Targets.Select(path => path.CanonicalKey).Distinct(StringComparer.Ordinal).Count() != Targets.Length
            || writeCount < 0
            || deleteCount < 0
            || writeCount + deleteCount != Targets.Length
            || writeBytes < 0
            || writeBytes > OutputLimits.MaximumWriteBytesPerApply)
        {
            throw new ArgumentException("A restore preview is invalid or out of bounds.");
        }

        CheckpointId = checkpointId;
        OutputRevision = outputRevision;
        WriteCount = writeCount;
        DeleteCount = deleteCount;
        WriteBytes = writeBytes;
    }

    public OutputCheckpointId CheckpointId { get; }

    public string ManifestFingerprint { get; }

    public OutputStateRevision OutputRevision { get; }

    public ImmutableArray<RelativeOutputPath> Targets { get; }

    public int WriteCount { get; }

    public int DeleteCount { get; }

    public long WriteBytes { get; }

    public bool IsCurrent => Targets.IsEmpty;
}

public sealed record OutputCheckpointRestoreResult(
    OutputCheckpointRestorePreview Preview,
    OutputApplyResult? ApplyResult);

public sealed record OutputCheckpointDeleteResult(
    bool Deleted,
    OutputStateRevision Revision);
