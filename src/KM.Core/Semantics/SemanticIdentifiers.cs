// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Semantics;

public sealed record SemanticDomainKey
{
    public SemanticDomainKey(string value)
    {
        Value = SemanticContractGuards.ContractKey(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SemanticRecordKind
{
    public SemanticRecordKind(string key, int schemaVersion)
    {
        Key = SemanticContractGuards.ContractKey(key, nameof(key));
        SchemaVersion = SemanticContractGuards.PositiveVersion(schemaVersion, nameof(schemaVersion));
    }

    public string Key { get; }

    public int SchemaVersion { get; }

    public override string ToString() => $"{Key}@{SchemaVersion}";
}

public sealed record SemanticRecordId
{
    public SemanticRecordId(string value)
    {
        Value = SemanticContractGuards.StableId(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SemanticSubrecordId
{
    public SemanticSubrecordId(string value)
    {
        Value = SemanticContractGuards.StableId(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SemanticFieldKey
{
    public SemanticFieldKey(string value)
    {
        Value = SemanticContractGuards.ContractKey(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SemanticAdapterId
{
    public SemanticAdapterId(string value)
    {
        Value = SemanticContractGuards.ContractKey(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SemanticOperationKind
{
    public SemanticOperationKind(string value)
    {
        Value = SemanticContractGuards.ContractKey(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SemanticMutationId
{
    public SemanticMutationId(string value)
    {
        Value = SemanticContractGuards.StableId(value, nameof(value));
    }

    public string Value { get; }

    public static SemanticMutationId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}

public sealed record SemanticProviderId
{
    public SemanticProviderId(string value)
    {
        Value = SemanticContractGuards.ContractKey(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record CapabilityId
{
    public CapabilityId(string value)
    {
        Value = SemanticContractGuards.ContractKey(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record OwnershipOwnerId
{
    public OwnershipOwnerId(string value)
    {
        Value = SemanticContractGuards.ContractKey(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
