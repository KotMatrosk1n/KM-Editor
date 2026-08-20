// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Projects;

namespace KM.Core.Semantics;

/// <summary>
/// Captures the bounded executable identity that makes a project a verified game/build.
/// </summary>
public static class SemanticProjectBuildIdentity
{
    private const long MaximumExecutableBytes = 512L * 1024L * 1024L;
    private const long MaximumNpdmBytes = 16L * 1024L * 1024L;
    private const int NsoIdentityBytes = 0x100;
    private const long MaximumAggregateObservedBytes = 128L * 1024L * 1024L;
    private const int ReadBufferBytes = 128 * 1024;

    public static string Capture(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            throw new InvalidDataException("The semantic build identity requires a configured Base ExeFS root.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "semantic-project-build-identity-v1");
        AppendText(hash, paths.SelectedGame?.ToString() ?? string.Empty);

        long aggregateBytes = 0;
        AppendRequiredFile(
            hash,
            "base-main-npdm",
            Path.Combine(paths.BaseExeFsPath, "main.npdm"),
            MaximumNpdmBytes,
            contentBytes: null,
            ref aggregateBytes);
        AppendOptionalFile(
            hash,
            "base-main",
            Path.Combine(paths.BaseExeFsPath, "main"),
            MaximumExecutableBytes,
            NsoIdentityBytes,
            ref aggregateBytes);

        var layeredNpdm = ResolveLayeredExeFsPath(paths, "main.npdm");
        AppendOptionalFile(
            hash,
            "layered-main-npdm",
            layeredNpdm,
            MaximumNpdmBytes,
            contentBytes: null,
            ref aggregateBytes);
        var layeredMain = ResolveLayeredExeFsPath(paths, "main");
        AppendOptionalFile(
            hash,
            "layered-main",
            layeredMain,
            MaximumExecutableBytes,
            NsoIdentityBytes,
            ref aggregateBytes);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static string CaptureBoundedFile(string path, string identityKind, long maximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKind);
        if (maximumBytes <= 0 || maximumBytes > MaximumAggregateObservedBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "semantic-bounded-file-identity-v1");
        long aggregateBytes = 0;
        AppendRequiredFile(
            hash,
            identityKind,
            path,
            maximumBytes,
            contentBytes: null,
            ref aggregateBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string? ResolveLayeredExeFsPath(ProjectPaths paths, string fileName)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return null;
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var candidate = Path.GetFullPath(Path.Combine(outputRoot, "exefs", fileName));
        return KM.Core.Files.PathContainment.IsOutsideRoot(Path.GetRelativePath(outputRoot, candidate))
            ? null
            : candidate;
    }

    private static void AppendRequiredFile(
        IncrementalHash hash,
        string role,
        string path,
        long maximumBytes,
        int? contentBytes,
        ref long aggregateBytes)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A required semantic build-identity file is unavailable.");
        }

        AppendFile(hash, role, path, maximumBytes, contentBytes, ref aggregateBytes);
    }

    private static void AppendOptionalFile(
        IncrementalHash hash,
        string role,
        string? path,
        long maximumBytes,
        int? contentBytes,
        ref long aggregateBytes)
    {
        AppendText(hash, role);
        var exists = path is not null && File.Exists(path);
        AppendBoolean(hash, exists);
        if (exists)
        {
            AppendFile(hash, "content", path!, maximumBytes, contentBytes, ref aggregateBytes);
        }
    }

    private static void AppendFile(
        IncrementalHash hash,
        string role,
        string path,
        long maximumBytes,
        int? contentBytes,
        ref long aggregateBytes)
    {
        AppendText(hash, role);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ReadBufferBytes,
            FileOptions.SequentialScan);
        var length = stream.Length;
        var observedLength = contentBytes is null ? length : Math.Min(length, contentBytes.Value);
        if (length < 0
            || length > maximumBytes
            || observedLength > MaximumAggregateObservedBytes - aggregateBytes
            || contentBytes is not null && length < contentBytes.Value)
        {
            throw new InvalidDataException("The semantic build identity exceeds its bounded byte limit.");
        }

        AppendInt64(hash, length);
        long readBytes = 0;
        byte[]? identityBytes = null;
        if (contentBytes is not null)
        {
            identityBytes = new byte[contentBytes.Value];
            stream.ReadExactly(identityBytes);
            hash.AppendData(identityBytes);
            readBytes = identityBytes.Length;
        }
        else
        {
            var buffer = new byte[ReadBufferBytes];
            while (readBytes < observedLength)
            {
                var request = (int)Math.Min(buffer.Length, observedLength - readBytes);
                var read = stream.Read(buffer, 0, request);
                if (read == 0)
                {
                    throw new EndOfStreamException("A semantic build-identity file changed while it was being observed.");
                }

                hash.AppendData(buffer.AsSpan(0, read));
                readBytes = checked(readBytes + read);
            }
        }

        if (stream.Length != length)
        {
            throw new IOException("A semantic build-identity file changed while it was being observed.");
        }

        if (contentBytes == NsoIdentityBytes
            && (identityBytes is null
                || identityBytes[0] != (byte)'N'
                || identityBytes[1] != (byte)'S'
                || identityBytes[2] != (byte)'O'
                || identityBytes[3] != (byte)'0'))
        {
            throw new InvalidDataException("A semantic build-identity executable has an invalid NSO header.");
        }

        aggregateBytes = checked(aggregateBytes + observedLength);
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendBoolean(IncrementalHash hash, bool value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value ? (byte)1 : (byte)0;
        hash.AppendData(bytes);
    }
}
