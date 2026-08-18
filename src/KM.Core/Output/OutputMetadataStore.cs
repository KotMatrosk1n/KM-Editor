// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using KM.Core.Projects;

namespace KM.Core.Output;

internal sealed class OutputMetadataStore
{
    private const UnixFileMode PrivateFileUnixMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly OutputPathSafety paths;
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 64,
        WriteIndented = false,
        Converters =
        {
            new ProjectIdJsonConverter(),
            new OutputTransactionIdJsonConverter(),
            new OutputCheckpointIdJsonConverter(),
            new OutputStateRevisionJsonConverter(),
        },
    };

    public OutputMetadataStore(OutputPathSafety paths)
    {
        this.paths = paths;
    }

    public async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        paths.ValidateMetadataFile(path);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = OpenPrivateFile(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > OutputLimits.MaximumMetadataDocumentBytes)
        {
            throw new OutputLimitExceededException("An output metadata document exceeds its size limit.");
        }

        return await JsonSerializer.DeserializeAsync<T>(stream, serializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        paths.ValidateMetadataFile(path);
        await using var serialized = new SizeLimitedMemoryStream(OutputLimits.MaximumMetadataDocumentBytes);
        await JsonSerializer.SerializeAsync(serialized, value, serializerOptions, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var parent = Path.GetDirectoryName(path)!;
        var temporaryPath = paths.GetContainedMetadataPath(parent, "." + Path.GetFileName(path) + ".pending.tmp");
        paths.ValidateMetadataFile(temporaryPath);
        TryDelete(temporaryPath);
        try
        {
            await using (var destination = OpenPrivateFile(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                serialized.Position = 0;
                await serialized.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            paths.ValidateMetadataFile(path);
            paths.ValidateMetadataFile(temporaryPath);
            OutputFileSystemDurability.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public int GetJsonByteCount<T>(T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        using var serialized = new SizeLimitedMemoryStream(OutputLimits.MaximumMetadataDocumentBytes);
        JsonSerializer.Serialize(serialized, value, serializerOptions);
        return checked((int)serialized.Length);
    }

    public FileStream OpenPrivateFile(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        FileOptions options)
    {
        paths.ValidateMetadataFile(path);
        var streamOptions = new FileStreamOptions
        {
            Mode = mode,
            Access = access,
            Share = share,
            Options = options,
            BufferSize = 81920,
        };
        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = PrivateFileUnixMode;
        }

        var stream = new FileStream(path, streamOptions);
        try
        {
            paths.ValidateMetadataFile(path);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(stream.SafeFileHandle, PrivateFileUnixMode);
            }
            else
            {
                paths.EnsurePrivateMetadataFile(stream);
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void ProtectExistingFile(string path)
    {
        using var stream = OpenPrivateFile(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.SequentialScan);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class SizeLimitedMemoryStream : MemoryStream
    {
        private readonly int maximumBytes;

        public SizeLimitedMemoryStream(int maximumBytes)
        {
            this.maximumBytes = maximumBytes;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacityFor(buffer.Length);
            base.Write(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacityFor(count);
            base.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacityFor(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
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

        private void EnsureCapacityFor(int additionalBytes)
        {
            if (additionalBytes < 0 || Position > maximumBytes - additionalBytes)
            {
                throw new OutputLimitExceededException("An output metadata document exceeds its size limit.");
            }
        }
    }

    private abstract class ValidatedStringValueJsonConverter<T> : JsonConverter<T>
    {
        protected abstract T Create(string value);

        protected abstract string GetValue(T value);

        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return Create(reader.GetString() ?? throw new JsonException("A metadata identity is null."));
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("A metadata identity has an invalid JSON shape.");
            }

            string? value = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("A metadata identity has an invalid JSON shape.");
                }

                var propertyName = reader.GetString();
                if (!reader.Read())
                {
                    throw new JsonException("A metadata identity is truncated.");
                }

                if (string.Equals(propertyName, "value", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType != JsonTokenType.String || value is not null)
                    {
                        throw new JsonException("A metadata identity value is invalid.");
                    }

                    value = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }

            return Create(value ?? throw new JsonException("A metadata identity value is missing."));
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(GetValue(value));
        }
    }

    private sealed class ProjectIdJsonConverter : ValidatedStringValueJsonConverter<ProjectId>
    {
        protected override ProjectId Create(string value) => new(value);

        protected override string GetValue(ProjectId value) => value.Value;
    }

    private sealed class OutputTransactionIdJsonConverter : ValidatedStringValueJsonConverter<OutputTransactionId>
    {
        protected override OutputTransactionId Create(string value) => new(value);

        protected override string GetValue(OutputTransactionId value) => value.Value;
    }

    private sealed class OutputCheckpointIdJsonConverter : ValidatedStringValueJsonConverter<OutputCheckpointId>
    {
        protected override OutputCheckpointId Create(string value) => new(value);

        protected override string GetValue(OutputCheckpointId value) => value.Value;
    }

    private sealed class OutputStateRevisionJsonConverter : ValidatedStringValueJsonConverter<OutputStateRevision>
    {
        protected override OutputStateRevision Create(string value) => new(value);

        protected override string GetValue(OutputStateRevision value) => value.Value;
    }
}
