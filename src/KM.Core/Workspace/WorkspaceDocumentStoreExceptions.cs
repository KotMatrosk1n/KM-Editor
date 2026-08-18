// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Workspace;

public class WorkspaceDocumentStoreException : Exception
{
    public WorkspaceDocumentStoreException(string message)
        : base(message)
    {
    }
}

public sealed class WorkspaceDocumentSecurityException : WorkspaceDocumentStoreException
{
    public WorkspaceDocumentSecurityException()
        : base("The private workspace location failed a path-safety check.")
    {
    }
}

public sealed class WorkspaceDocumentTooLargeException : WorkspaceDocumentStoreException
{
    public WorkspaceDocumentTooLargeException(long maximumDocumentBytes)
        : base($"The private workspace document exceeds the {maximumDocumentBytes}-byte size limit.")
    {
        MaximumDocumentBytes = maximumDocumentBytes;
    }

    public long MaximumDocumentBytes { get; }
}

public sealed class WorkspaceDocumentConflictException : WorkspaceDocumentStoreException
{
    public WorkspaceDocumentConflictException(string? expectedETag, string? actualETag)
        : base("The private workspace document changed after it was read.")
    {
        ExpectedETag = expectedETag;
        ActualETag = actualETag;
    }

    public string? ExpectedETag { get; }

    public string? ActualETag { get; }
}

public sealed class WorkspaceDocumentLockTimeoutException : WorkspaceDocumentStoreException
{
    public WorkspaceDocumentLockTimeoutException(TimeSpan timeout)
        : base("The private workspace document is busy in another process.")
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}

public sealed class WorkspaceDocumentFormatException : WorkspaceDocumentStoreException
{
    public WorkspaceDocumentFormatException(string message)
        : base(message)
    {
    }
}

public sealed class UnsupportedWorkspaceDocumentVersionException : WorkspaceDocumentStoreException
{
    public UnsupportedWorkspaceDocumentVersionException(
        int storedSchemaVersion,
        int currentSchemaVersion)
        : base(
            storedSchemaVersion > currentSchemaVersion
                ? "The private workspace document was written by a newer schema version."
                : "The private workspace document has no complete migration path to the current schema version.")
    {
        StoredSchemaVersion = storedSchemaVersion;
        CurrentSchemaVersion = currentSchemaVersion;
    }

    public int StoredSchemaVersion { get; }

    public int CurrentSchemaVersion { get; }
}
