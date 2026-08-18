// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Nodes;

namespace KM.Core.Workspace;

public interface IWorkspaceDocumentMigration
{
    string DocumentType { get; }

    int SourceVersion { get; }

    int TargetVersion { get; }

    JsonNode Migrate(JsonNode payload, CancellationToken cancellationToken);
}

public sealed class WorkspaceDocumentMigration : IWorkspaceDocumentMigration
{
    private readonly Func<JsonNode, CancellationToken, JsonNode> migration;

    public WorkspaceDocumentMigration(
        string documentType,
        int sourceVersion,
        int targetVersion,
        Func<JsonNode, CancellationToken, JsonNode> migration)
    {
        if (sourceVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceVersion),
                sourceVersion,
                "A source schema version must be positive.");
        }

        if (targetVersion <= sourceVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetVersion),
                targetVersion,
                "A migration target version must be greater than its source version.");
        }

        DocumentType = WorkspaceIdentifier.Normalize(
            documentType,
            nameof(documentType),
            maximumLength: 128);
        SourceVersion = sourceVersion;
        TargetVersion = targetVersion;
        this.migration = migration ?? throw new ArgumentNullException(nameof(migration));
    }

    public string DocumentType { get; }

    public int SourceVersion { get; }

    public int TargetVersion { get; }

    public JsonNode Migrate(JsonNode payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();
        return migration(payload, cancellationToken)
            ?? throw new WorkspaceDocumentFormatException("A workspace migration returned an empty payload.");
    }
}
