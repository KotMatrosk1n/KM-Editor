// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Semantics;

public sealed record SemanticRecordRef
{
    public SemanticRecordRef(
        GameFamily gameFamily,
        SemanticDomainKey domain,
        SemanticRecordKind recordKind,
        SemanticRecordId recordId,
        SemanticSubrecordId? subrecordId = null)
    {
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        Domain = domain ?? throw new ArgumentNullException(nameof(domain));
        RecordKind = recordKind ?? throw new ArgumentNullException(nameof(recordKind));
        RecordId = recordId ?? throw new ArgumentNullException(nameof(recordId));
        SubrecordId = subrecordId;
    }

    public GameFamily GameFamily { get; }

    public SemanticDomainKey Domain { get; }

    public SemanticRecordKind RecordKind { get; }

    public SemanticRecordId RecordId { get; }

    public SemanticSubrecordId? SubrecordId { get; }
}

public sealed record SemanticFieldRef
{
    public SemanticFieldRef(SemanticRecordRef record, SemanticFieldKey fieldKey)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        FieldKey = fieldKey ?? throw new ArgumentNullException(nameof(fieldKey));
    }

    public SemanticRecordRef Record { get; }

    public SemanticFieldKey FieldKey { get; }
}
