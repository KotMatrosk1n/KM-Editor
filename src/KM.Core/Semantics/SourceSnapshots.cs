// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.Core.Semantics;

public sealed record ProjectSourceRevision
{
    public ProjectSourceRevision(
        ProjectId projectId,
        GameFamily gameFamily,
        long generation,
        string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(projectId.Value))
        {
            throw new ArgumentException("Project id cannot be empty or default.", nameof(projectId));
        }

        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), generation, "A source generation cannot be negative.");
        }

        ProjectId = projectId;
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        Generation = generation;
        Fingerprint = SemanticContractGuards.Sha256Fingerprint(fingerprint, nameof(fingerprint));
    }

    public ProjectId ProjectId { get; }

    public GameFamily GameFamily { get; }

    public long Generation { get; }

    public string Fingerprint { get; }
}

public enum SourceLayerKind
{
    Base = 1,
    Layered = 2,
    Pending = 3,
    ChangeSet = 4,
    ComparedMod = 5,
    Checkpoint = 6,
}

public sealed record SourceLayerRef
{
    public SourceLayerRef(SourceLayerKind kind, string? instanceId = null)
    {
        Kind = SemanticContractGuards.DefinedEnum(kind, nameof(kind));

        var needsInstanceId = kind is SourceLayerKind.ChangeSet
            or SourceLayerKind.ComparedMod
            or SourceLayerKind.Checkpoint;

        if (needsInstanceId && instanceId is null)
        {
            throw new ArgumentException($"The {kind} source layer requires a stable instance id.", nameof(instanceId));
        }

        if (!needsInstanceId && instanceId is not null)
        {
            throw new ArgumentException($"The {kind} source layer cannot have an instance id.", nameof(instanceId));
        }

        InstanceId = instanceId is null
            ? null
            : SemanticContractGuards.StableId(instanceId, nameof(instanceId));
    }

    public SourceLayerKind Kind { get; }

    public string? InstanceId { get; }
}

public sealed record SourceSnapshot
{
    public SourceSnapshot(
        SourceLayerRef layer,
        ProjectSourceRevision revision,
        string fingerprint)
    {
        Layer = layer ?? throw new ArgumentNullException(nameof(layer));
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        Fingerprint = SemanticContractGuards.Sha256Fingerprint(fingerprint, nameof(fingerprint));
    }

    public SourceLayerRef Layer { get; }

    public ProjectSourceRevision Revision { get; }

    public string Fingerprint { get; }
}
