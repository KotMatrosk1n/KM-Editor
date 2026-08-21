// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Workspace;

public sealed record WorkspaceDocumentDefinition<TDocument>
{
    public WorkspaceDocumentDefinition(
        WorkspaceDocumentId documentId,
        string documentType,
        int currentSchemaVersion)
    {
        if (string.IsNullOrWhiteSpace(documentId.Value))
        {
            throw new ArgumentException("A workspace document id must be initialized.", nameof(documentId));
        }

        if (currentSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentSchemaVersion),
                currentSchemaVersion,
                "A workspace schema version must be positive.");
        }

        DocumentId = documentId;
        DocumentType = WorkspaceIdentifier.Normalize(
            documentType,
            nameof(documentType),
            maximumLength: 128);
        CurrentSchemaVersion = currentSchemaVersion;
    }

    public WorkspaceDocumentId DocumentId { get; }

    public string DocumentType { get; }

    public int CurrentSchemaVersion { get; }
}

public sealed record WorkspaceDocumentReadResult<TDocument>(
    TDocument Document,
    int OriginalSchemaVersion,
    int EffectiveSchemaVersion,
    string ETag,
    DateTimeOffset WrittenAtUtc)
{
    public bool WasMigrated => OriginalSchemaVersion != EffectiveSchemaVersion;
}

public sealed record WorkspaceDocumentWriteResult(
    string ETag,
    DateTimeOffset WrittenAtUtc);

public sealed record WorkspaceDocumentDeleteResult(
    bool Deleted,
    string? DeletedETag,
    DateTimeOffset? DeletedAtUtc);

public sealed record WorkspaceDocumentStoreOptions
{
    private const int ProvisionMultiplier = 4;
    private const int HardCeilingMultiplier = 2;

    public const long ExpectedDocumentBytes = 4L * 1024L * 1024L;
    public const long ProvisionedDocumentBytes = checked(
        ExpectedDocumentBytes * ProvisionMultiplier);
    public const long MaximumDocumentHardCeilingBytes = checked(
        ProvisionedDocumentBytes * HardCeilingMultiplier);

    public long MaximumDocumentBytes { get; init; } = MaximumDocumentHardCeilingBytes;

    public int MaximumJsonDepth { get; init; } = 64;

    public TimeSpan WriterLockTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan WriterLockRetryDelay { get; init; } = TimeSpan.FromMilliseconds(25);

    internal void Validate()
    {
        if (MaximumDocumentBytes <= 0 || MaximumDocumentBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumDocumentBytes),
                MaximumDocumentBytes,
                $"The document size limit must be between 1 and {int.MaxValue} bytes.");
        }

        if (MaximumJsonDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumJsonDepth),
                MaximumJsonDepth,
                "The maximum JSON depth must be positive.");
        }

        if (WriterLockTimeout <= TimeSpan.Zero || WriterLockTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(WriterLockTimeout),
                WriterLockTimeout,
                "The writer-lock timeout must be between zero and one minute.");
        }

        if (WriterLockRetryDelay <= TimeSpan.Zero || WriterLockRetryDelay > WriterLockTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WriterLockRetryDelay),
                WriterLockRetryDelay,
                "The writer-lock retry delay must be positive and no greater than the timeout.");
        }
    }
}
