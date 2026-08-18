// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KM.Core.Workspace;

/// <summary>
/// Stores small, private, project-scoped workspace documents beneath an explicitly supplied app-data root.
/// </summary>
/// <remarks>
/// This store is intentionally independent of disposable derived-data caches. A cache-clearing operation
/// must not delete this store. Migrations are applied in memory; callers explicitly decide when to write
/// an upgraded document back to disk.
/// </remarks>
public sealed class VersionedWorkspaceDocumentStore
{
    private const string WorkspaceDirectoryName = "workspace-state-v1";
    private const string ProjectDirectoryName = "projects";
    private const int MaximumRegisteredMigrations = 256;
    private const UnixFileMode PrivateDirectoryUnixMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileUnixMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly string appDataRoot;
    private readonly string workspaceRoot;
    private readonly string projectsRoot;
    private readonly long maximumDocumentBytes;
    private readonly TimeSpan writerLockRetryDelay;
    private readonly TimeSpan writerLockTimeout;
    private readonly JsonDocumentOptions documentOptions;
    private readonly JsonSerializerOptions serializerOptions;
    private readonly Dictionary<MigrationKey, IWorkspaceDocumentMigration> migrations;
    private readonly SemaphoreSlim operationGate = new(1, 1);

    public VersionedWorkspaceDocumentStore(
        string appDataRoot,
        WorkspaceDocumentStoreOptions? options = null,
        IEnumerable<IWorkspaceDocumentMigration>? migrations = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            throw new ArgumentException("An app-data root must be supplied.", nameof(appDataRoot));
        }

        if (!Path.IsPathFullyQualified(appDataRoot))
        {
            throw new ArgumentException("The app-data root must be a fully qualified path.", nameof(appDataRoot));
        }

        options ??= new WorkspaceDocumentStoreOptions();
        options.Validate();

        this.appDataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDataRoot));
        workspaceRoot = GetContainedPath(this.appDataRoot, WorkspaceDirectoryName);
        projectsRoot = GetContainedPath(workspaceRoot, ProjectDirectoryName);
        maximumDocumentBytes = options.MaximumDocumentBytes;
        writerLockRetryDelay = options.WriterLockRetryDelay;
        writerLockTimeout = options.WriterLockTimeout;
        documentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = options.MaximumJsonDepth,
        };
        this.serializerOptions = serializerOptions is null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            : new JsonSerializerOptions(serializerOptions);
        this.serializerOptions.MaxDepth = options.MaximumJsonDepth;
        this.migrations = BuildMigrationMap(migrations ?? Array.Empty<IWorkspaceDocumentMigration>());
    }

    public async Task<bool> ExistsAsync(
        WorkspaceProjectIdentity projectIdentity,
        WorkspaceDocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(projectIdentity);
        ValidateDocumentId(documentId);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(appDataRoot))
            {
                return false;
            }

            var documentPath = GetDocumentPath(projectIdentity, documentId);
            ValidateExistingReadPath(documentPath);
            return File.Exists(documentPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WorkspaceDocumentStoreException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new WorkspaceDocumentStoreException("The private workspace document could not be inspected.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceDocumentStoreException("The private workspace document could not be inspected.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<WorkspaceDocumentReadResult<TDocument>?> ReadAsync<TDocument>(
        WorkspaceProjectIdentity projectIdentity,
        WorkspaceDocumentDefinition<TDocument> definition,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(projectIdentity);
        ArgumentNullException.ThrowIfNull(definition);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(appDataRoot))
            {
                return null;
            }

            var documentPath = GetDocumentPath(projectIdentity, definition.DocumentId);
            ValidateExistingReadPath(documentPath);
            if (!File.Exists(documentPath))
            {
                return null;
            }

            var bytes = await ReadBoundedAsync(documentPath, cancellationToken).ConfigureAwait(false);
            var etag = ComputeETag(bytes);
            using var jsonDocument = JsonDocument.Parse(bytes, documentOptions);
            var root = jsonDocument.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new WorkspaceDocumentFormatException(
                    "The private workspace document must contain a JSON object envelope.");
            }

            var storedVersion = ReadStoredVersion(root);
            var storedDocumentType = ReadRequiredString(root, "documentType");
            var writtenAtUtc = ReadRequiredDateTimeOffset(root, "writtenAtUtc");
            if (!string.Equals(storedDocumentType, definition.DocumentType, StringComparison.Ordinal))
            {
                throw new WorkspaceDocumentFormatException(
                    "The private workspace document type does not match its registered definition.");
            }

            if (!root.TryGetProperty("payload", out var payloadElement)
                || payloadElement.ValueKind == JsonValueKind.Null)
            {
                throw new WorkspaceDocumentFormatException(
                    "The private workspace document does not contain a payload.");
            }

            if (storedVersion > definition.CurrentSchemaVersion)
            {
                throw new UnsupportedWorkspaceDocumentVersionException(
                    storedVersion,
                    definition.CurrentSchemaVersion);
            }

            var payload = JsonNode.Parse(payloadElement.GetRawText(), documentOptions: documentOptions)
                ?? throw new WorkspaceDocumentFormatException(
                    "The private workspace document payload could not be parsed.");
            var effectiveVersion = storedVersion;
            while (effectiveVersion < definition.CurrentSchemaVersion)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!migrations.TryGetValue(
                        new MigrationKey(definition.DocumentType, effectiveVersion),
                        out var migration)
                    || migration.SourceVersion != effectiveVersion
                    || migration.TargetVersion <= effectiveVersion
                    || migration.TargetVersion > definition.CurrentSchemaVersion)
                {
                    throw new UnsupportedWorkspaceDocumentVersionException(
                        storedVersion,
                        definition.CurrentSchemaVersion);
                }

                payload = migration.Migrate(payload, cancellationToken)
                    ?? throw new WorkspaceDocumentFormatException(
                        "A workspace migration returned an empty payload.");
                effectiveVersion = migration.TargetVersion;
                await ValidateMigratedPayloadSizeAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var document = payload.Deserialize<TDocument>(serializerOptions);
            if (document is null)
            {
                throw new WorkspaceDocumentFormatException(
                    "The private workspace document payload was empty or incompatible.");
            }

            return new WorkspaceDocumentReadResult<TDocument>(
                document,
                storedVersion,
                effectiveVersion,
                etag,
                writtenAtUtc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WorkspaceDocumentStoreException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new WorkspaceDocumentFormatException(
                "The private workspace document contains invalid or incompatible JSON.");
        }
        catch (IOException)
        {
            throw new WorkspaceDocumentStoreException("The private workspace document could not be read.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceDocumentStoreException("The private workspace document could not be read.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task WriteAsync<TDocument>(
        WorkspaceProjectIdentity projectIdentity,
        WorkspaceDocumentDefinition<TDocument> definition,
        TDocument document,
        CancellationToken cancellationToken = default)
    {
        _ = await WriteCoreAsync(
                projectIdentity,
                definition,
                document,
                isConditional: false,
                expectedETag: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes only when the stored envelope still has <paramref name="expectedETag"/>.
    /// A null expected ETag means that the document must still be absent.
    /// </summary>
    public Task<WorkspaceDocumentWriteResult> WriteConditionalAsync<TDocument>(
        WorkspaceProjectIdentity projectIdentity,
        WorkspaceDocumentDefinition<TDocument> definition,
        TDocument document,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        return WriteCoreAsync(
            projectIdentity,
            definition,
            document,
            isConditional: true,
            NormalizeExpectedETag(expectedETag),
            cancellationToken);
    }

    private async Task<WorkspaceDocumentWriteResult> WriteCoreAsync<TDocument>(
        WorkspaceProjectIdentity projectIdentity,
        WorkspaceDocumentDefinition<TDocument> definition,
        TDocument document,
        bool isConditional,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(projectIdentity);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(document);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectDirectory = EnsureProjectDirectory(projectIdentity);
            var documentPath = GetDocumentPath(projectIdentity, definition.DocumentId);
            EnsureContained(documentPath);
            ValidateSafeDirectoryChain(projectDirectory);
            ValidateDestinationPath(documentPath);

            var writtenAtUtc = DateTimeOffset.UtcNow;
            var envelope = new WorkspaceDocumentEnvelope<TDocument>(
                definition.CurrentSchemaVersion,
                definition.DocumentType,
                writtenAtUtc,
                document);
            await using var serialized = new SizeLimitedMemoryStream(maximumDocumentBytes);
            await JsonSerializer.SerializeAsync(
                    serialized,
                    envelope,
                    serializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var etag = ComputeETag(serialized.GetBuffer().AsSpan(0, checked((int)serialized.Length)));

            await using var documentLock = await AcquireDocumentLockAsync(
                    projectDirectory,
                    definition.DocumentId,
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            ValidateSafeDirectoryChain(projectDirectory);
            ValidateDestinationPath(documentPath);
            TightenExistingPrivateFilePermissions(documentPath);
            var temporaryPath = GetTemporaryDocumentPath(projectDirectory, definition.DocumentId);
            ValidateDestinationPath(temporaryPath);
            TryDeleteTemporaryFile(temporaryPath);
            TryDeleteOrphanedTemporaryFiles(
                projectDirectory,
                definition.DocumentId,
                cancellationToken);

            if (isConditional)
            {
                var actualETag = await ReadCurrentETagAsync(documentPath, cancellationToken)
                    .ConfigureAwait(false);
                EnsureETagMatches(expectedETag, actualETag);
            }

            try
            {
                await using (var destination = OpenPrivateFileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 81920,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    serialized.Position = 0;
                    await serialized.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                    destination.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                ValidateSafeDirectoryChain(projectDirectory);
                ValidateDestinationPath(documentPath);
                ValidateDestinationPath(temporaryPath);
                TightenPrivateFilePermissions(temporaryPath);
                File.Move(temporaryPath, documentPath, overwrite: true);
                return new WorkspaceDocumentWriteResult(etag, writtenAtUtc);
            }
            finally
            {
                // This runs before the per-document lock is released, so cleanup can
                // never delete another conforming writer's deterministic temp file.
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WorkspaceDocumentStoreException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new WorkspaceDocumentFormatException(
                "The private workspace document could not be serialized as JSON.");
        }
        catch (IOException)
        {
            throw new WorkspaceDocumentStoreException("The private workspace document could not be written.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceDocumentStoreException("The private workspace document could not be written.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        WorkspaceProjectIdentity projectIdentity,
        WorkspaceDocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        var result = await DeleteCoreAsync(
                projectIdentity,
                documentId,
                isConditional: false,
                expectedETag: null,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Deleted;
    }

    /// <summary>
    /// Deletes only when the stored envelope still has <paramref name="expectedETag"/>.
    /// A null expected ETag means that the document must still be absent.
    /// </summary>
    public Task<WorkspaceDocumentDeleteResult> DeleteConditionalAsync(
        WorkspaceProjectIdentity projectIdentity,
        WorkspaceDocumentId documentId,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        return DeleteCoreAsync(
            projectIdentity,
            documentId,
            isConditional: true,
            NormalizeExpectedETag(expectedETag),
            cancellationToken);
    }

    private async Task<WorkspaceDocumentDeleteResult> DeleteCoreAsync(
        WorkspaceProjectIdentity projectIdentity,
        WorkspaceDocumentId documentId,
        bool isConditional,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(projectIdentity);
        ValidateDocumentId(documentId);

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectDirectory = EnsureProjectDirectory(projectIdentity);
            var documentPath = GetDocumentPath(projectIdentity, documentId);
            await using var documentLock = await AcquireDocumentLockAsync(
                    projectDirectory,
                    documentId,
                    cancellationToken)
                .ConfigureAwait(false);

            ValidateSafeDirectoryChain(projectDirectory);
            ValidateDestinationPath(documentPath);
            var temporaryPath = GetTemporaryDocumentPath(projectDirectory, documentId);
            ValidateDestinationPath(temporaryPath);
            TryDeleteTemporaryFile(temporaryPath);
            TryDeleteOrphanedTemporaryFiles(projectDirectory, documentId, cancellationToken);
            var actualETag = await ReadCurrentETagAsync(documentPath, cancellationToken)
                .ConfigureAwait(false);
            if (isConditional)
            {
                EnsureETagMatches(expectedETag, actualETag);
            }

            if (actualETag is null)
            {
                return new WorkspaceDocumentDeleteResult(
                    Deleted: false,
                    DeletedETag: null,
                    DeletedAtUtc: null);
            }

            File.Delete(documentPath);
            return new WorkspaceDocumentDeleteResult(
                Deleted: true,
                actualETag,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WorkspaceDocumentStoreException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new WorkspaceDocumentStoreException("The private workspace document could not be deleted.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new WorkspaceDocumentStoreException("The private workspace document could not be deleted.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    private static Dictionary<MigrationKey, IWorkspaceDocumentMigration> BuildMigrationMap(
        IEnumerable<IWorkspaceDocumentMigration> migrationSequence)
    {
        var migrationMap = new Dictionary<MigrationKey, IWorkspaceDocumentMigration>();
        foreach (var migration in migrationSequence)
        {
            ArgumentNullException.ThrowIfNull(migration);
            if (migrationMap.Count == MaximumRegisteredMigrations)
            {
                throw new ArgumentException(
                    $"A workspace store cannot register more than {MaximumRegisteredMigrations} migrations.",
                    nameof(migrationSequence));
            }

            var documentType = WorkspaceIdentifier.Normalize(
                migration.DocumentType,
                nameof(migrationSequence),
                maximumLength: 128);
            if (migration.SourceVersion <= 0 || migration.TargetVersion <= migration.SourceVersion)
            {
                throw new ArgumentException(
                    "Every workspace migration must advance from a positive source version.",
                    nameof(migrationSequence));
            }

            var key = new MigrationKey(documentType, migration.SourceVersion);
            if (!migrationMap.TryAdd(key, migration))
            {
                throw new ArgumentException(
                    "Only one workspace migration may start from a given document type and version.",
                    nameof(migrationSequence));
            }
        }

        return migrationMap;
    }

    private async Task<byte[]> ReadBoundedAsync(string documentPath, CancellationToken cancellationToken)
    {
        var fileLength = new FileInfo(documentPath).Length;
        if (fileLength > maximumDocumentBytes)
        {
            throw new WorkspaceDocumentTooLargeException(maximumDocumentBytes);
        }

        await using var source = new FileStream(
            documentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new SizeLimitedMemoryStream(maximumDocumentBytes);
        await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
        return destination.ToArray();
    }

    private async Task<string?> ReadCurrentETagAsync(
        string documentPath,
        CancellationToken cancellationToken)
    {
        ValidateDestinationPath(documentPath);
        if (!File.Exists(documentPath))
        {
            return null;
        }

        TightenPrivateFilePermissions(documentPath);
        var bytes = await ReadBoundedAsync(documentPath, cancellationToken).ConfigureAwait(false);
        return ComputeETag(bytes);
    }

    private async Task<FileStream> AcquireDocumentLockAsync(
        string projectDirectory,
        WorkspaceDocumentId documentId,
        CancellationToken cancellationToken)
    {
        var lockPath = GetContainedPath(projectDirectory, $".{documentId.Value}.lock");
        var startedAt = Stopwatch.GetTimestamp();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                ValidateSafeDirectoryChain(projectDirectory);
                ValidateDestinationPath(lockPath);
                var stream = OpenPrivateFileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
                try
                {
                    ValidateSafeDirectoryChain(projectDirectory);
                    ValidateDestinationPath(lockPath);
                    TightenPrivateFilePermissions(lockPath);
                    return stream;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(startedAt) < writerLockTimeout)
            {
                var remaining = writerLockTimeout - Stopwatch.GetElapsedTime(startedAt);
                var delay = remaining < writerLockRetryDelay ? remaining : writerLockRetryDelay;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                throw new WorkspaceDocumentLockTimeoutException(writerLockTimeout);
            }
        }
    }

    private async Task ValidateMigratedPayloadSizeAsync(
        JsonNode payload,
        CancellationToken cancellationToken)
    {
        await using var destination = new SizeLimitedMemoryStream(maximumDocumentBytes);
        await JsonSerializer.SerializeAsync(
                destination,
                payload,
                serializerOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string EnsureProjectDirectory(WorkspaceProjectIdentity projectIdentity)
    {
        EnsureSafeDirectory(appDataRoot, parentRoot: null);
        EnsureSafeDirectory(workspaceRoot, appDataRoot);
        EnsureSafeDirectory(projectsRoot, workspaceRoot);
        var projectDirectory = GetContainedPath(projectsRoot, projectIdentity.Value);
        EnsureSafeDirectory(projectDirectory, projectsRoot);
        return projectDirectory;
    }

    private void EnsureSafeDirectory(string directoryPath, string? parentRoot)
    {
        if (parentRoot is not null)
        {
            EnsureContained(directoryPath);
            ValidateSafeDirectoryChain(parentRoot);
        }

        var directory = new DirectoryInfo(directoryPath);
        directory.Refresh();
        if (directory.LinkTarget is not null
            || (File.Exists(directoryPath) && !Directory.Exists(directoryPath)))
        {
            throw new WorkspaceDocumentSecurityException();
        }

        if (!OperatingSystem.IsWindows() && IsStoreOwnedPath(directoryPath))
        {
            Directory.CreateDirectory(directoryPath, PrivateDirectoryUnixMode);
        }
        else
        {
            Directory.CreateDirectory(directoryPath);
        }

        ValidateDirectory(directoryPath);
        TightenPrivateDirectoryPermissionsIfOwned(directoryPath);
    }

    private void ValidateExistingReadPath(string documentPath)
    {
        EnsureContained(documentPath);
        ValidateSafeDirectoryChain(Path.GetDirectoryName(documentPath)!);
        ValidateDestinationPath(documentPath);
        TightenExistingPrivateFilePermissions(documentPath);
    }

    private void ValidateSafeDirectoryChain(string targetDirectory)
    {
        EnsureContained(targetDirectory);
        if (!Directory.Exists(appDataRoot))
        {
            return;
        }

        ValidateDirectory(appDataRoot);
        var relative = Path.GetRelativePath(appDataRoot, targetDirectory);
        if (string.Equals(relative, ".", StringComparison.Ordinal))
        {
            return;
        }

        var current = appDataRoot;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = GetContainedPath(current, segment);
            if (!Directory.Exists(current))
            {
                if (File.Exists(current) || new DirectoryInfo(current).LinkTarget is not null)
                {
                    throw new WorkspaceDocumentSecurityException();
                }

                return;
            }

            ValidateDirectory(current);
            TightenPrivateDirectoryPermissionsIfOwned(current);
        }
    }

    private FileStream OpenPrivateFileStream(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        int bufferSize,
        FileOptions options)
    {
        var streamOptions = new FileStreamOptions
        {
            Mode = mode,
            Access = access,
            Share = share,
            BufferSize = bufferSize,
            Options = options,
        };
        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = PrivateFileUnixMode;
        }

        return new FileStream(path, streamOptions);
    }

    private void TightenExistingPrivateFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path))
        {
            TightenPrivateFilePermissions(path);
        }
    }

    private void TightenPrivateFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (!IsStoreOwnedPath(path))
        {
            throw new WorkspaceDocumentSecurityException();
        }

        File.SetUnixFileMode(path, PrivateFileUnixMode);
    }

    private void TightenPrivateDirectoryPermissionsIfOwned(string directoryPath)
    {
        if (OperatingSystem.IsWindows() || !IsStoreOwnedPath(directoryPath))
        {
            return;
        }

        File.SetUnixFileMode(directoryPath, PrivateDirectoryUnixMode);
    }

    private bool IsStoreOwnedPath(string candidatePath)
    {
        var relative = Path.GetRelativePath(workspaceRoot, Path.GetFullPath(candidatePath));
        return string.Equals(relative, ".", StringComparison.Ordinal)
            || (!Path.IsPathRooted(relative)
                && !string.Equals(relative, "..", StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void ValidateDirectory(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);
        directory.Refresh();
        if (!directory.Exists
            || directory.LinkTarget is not null
            || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new WorkspaceDocumentSecurityException();
        }
    }

    private static void ValidateDestinationPath(string documentPath)
    {
        var file = new FileInfo(documentPath);
        file.Refresh();
        if (file.LinkTarget is not null
            || (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            || Directory.Exists(documentPath))
        {
            throw new WorkspaceDocumentSecurityException();
        }
    }

    private static int ReadStoredVersion(JsonElement root)
    {
        if (!root.TryGetProperty("schemaVersion", out var versionElement)
            || !versionElement.TryGetInt32(out var version)
            || version <= 0)
        {
            throw new WorkspaceDocumentFormatException(
                "The private workspace document has an invalid schema version.");
        }

        return version;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new WorkspaceDocumentFormatException(
                "The private workspace document has an invalid envelope.");
        }

        return property.GetString()!;
    }

    private static DateTimeOffset ReadRequiredDateTimeOffset(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || !property.TryGetDateTimeOffset(out var value))
        {
            throw new WorkspaceDocumentFormatException(
                "The private workspace document has an invalid envelope timestamp.");
        }

        return value.ToUniversalTime();
    }

    private static string ComputeETag(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string? NormalizeExpectedETag(string? expectedETag)
    {
        if (expectedETag is null)
        {
            return null;
        }

        if (expectedETag.Length != 64 || expectedETag.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A workspace document ETag must be a 64-character SHA-256 hexadecimal value.",
                nameof(expectedETag));
        }

        return expectedETag.ToLowerInvariant();
    }

    private static void EnsureETagMatches(string? expectedETag, string? actualETag)
    {
        if (!string.Equals(expectedETag, actualETag, StringComparison.Ordinal))
        {
            throw new WorkspaceDocumentConflictException(expectedETag, actualETag);
        }
    }

    private string GetDocumentPath(
        WorkspaceProjectIdentity projectIdentity,
        WorkspaceDocumentId documentId)
    {
        var projectDirectory = GetContainedPath(projectsRoot, projectIdentity.Value);
        return GetContainedPath(projectDirectory, documentId.Value + ".json");
    }

    private string GetTemporaryDocumentPath(
        string projectDirectory,
        WorkspaceDocumentId documentId)
    {
        // One deterministic temp name per document keeps crash residue bounded.
        // Every conforming writer holds that document's cross-process lock before
        // it creates, replaces, moves, or removes this file.
        return GetContainedPath(projectDirectory, $".{documentId.Value}.pending.tmp");
    }

    private void TryDeleteOrphanedTemporaryFiles(
        string projectDirectory,
        WorkspaceDocumentId documentId,
        CancellationToken cancellationToken)
    {
        var prefix = $".{documentId.Value}.";
        const string suffix = ".tmp";
        foreach (var candidatePath in Directory.EnumerateFiles(
                     projectDirectory,
                     prefix + "*" + suffix,
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(candidatePath);
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal)
                || !fileName.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var tokenLength = fileName.Length - prefix.Length - suffix.Length;
            if (tokenLength != 32
                || !ContainsOnlyHexDigits(fileName.AsSpan(prefix.Length, tokenLength)))
            {
                continue;
            }

            EnsureContained(candidatePath);
            ValidateDestinationPath(candidatePath);
            TryDeleteTemporaryFile(candidatePath);
        }
    }

    private string GetContainedPath(string parent, string child)
    {
        var result = Path.GetFullPath(Path.Combine(parent, child));
        EnsureContained(result);
        return result;
    }

    private void EnsureContained(string candidatePath)
    {
        var relative = Path.GetRelativePath(appDataRoot, Path.GetFullPath(candidatePath));
        if (Path.IsPathRooted(relative)
            || string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new WorkspaceDocumentSecurityException();
        }
    }

    private static void ValidateIdentity(WorkspaceProjectIdentity projectIdentity)
    {
        if (string.IsNullOrWhiteSpace(projectIdentity.Value)
            || projectIdentity.Value.Length != 66
            || !projectIdentity.Value.StartsWith("p-", StringComparison.Ordinal)
            || projectIdentity.Value[2..].Any(character =>
                !char.IsAsciiDigit(character)
                && character is not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "A workspace project identity must be initialized by FromStableIdentity.",
                nameof(projectIdentity));
        }
    }

    private static void ValidateDocumentId(WorkspaceDocumentId documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId.Value))
        {
            throw new ArgumentException("A workspace document id must be initialized.", nameof(documentId));
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool ContainsOnlyHexDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record WorkspaceDocumentEnvelope<TDocument>(
        int SchemaVersion,
        string DocumentType,
        DateTimeOffset WrittenAtUtc,
        TDocument Payload);

    private readonly record struct MigrationKey(string DocumentType, int SourceVersion);

    private sealed class SizeLimitedMemoryStream : MemoryStream
    {
        private readonly long maximumBytes;

        public SizeLimitedMemoryStream(long maximumBytes)
        {
            this.maximumBytes = maximumBytes;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacityFor(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacityFor(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureCapacityFor(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacityFor(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacityFor(1);
            base.WriteByte(value);
        }

        private void EnsureCapacityFor(int additionalBytes)
        {
            if (additionalBytes < 0 || Position > maximumBytes - additionalBytes)
            {
                throw new WorkspaceDocumentTooLargeException(maximumBytes);
            }
        }
    }
}
