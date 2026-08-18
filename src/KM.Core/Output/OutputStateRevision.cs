// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Semantics;

namespace KM.Core.Output;

/// <summary>
/// An opaque fingerprint of coordinator-visible output metadata or file state.
/// Its value is suitable only for optimistic concurrency checks.
/// </summary>
public readonly record struct OutputStateRevision
{
    public OutputStateRevision(string value)
    {
        Value = SemanticContractGuards.Sha256Fingerprint(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
