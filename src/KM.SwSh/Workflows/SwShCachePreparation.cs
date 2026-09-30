// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.Workflows;

public sealed partial class SwShCacheManager
{
    private readonly Dictionary<string, PreparedArtifactStamp> preparedArtifacts = new(StringComparer.Ordinal);
    private sealed record PreparedFileStamp(long Length, long Written, long Created);
    private sealed record PreparedArtifactStamp(PreparedFileStamp Source, PreparedFileStamp Metadata, PreparedFileStamp Payload);

    // Reuse the persisted artifact as the completion record. Verify its checksum on
    // first observation, without materializing a complete editor category just for status.
    internal bool IsArtifactPrepared<T>(SwShCacheSourceIdentity source, SwShCacheArtifactDescriptor artifact)
    {
        artifact = NormalizeArtifactDescriptor(artifact);
        ValidateSourceIdentity(source);
        var identityKey = GetIdentityKey(source);
        var artifactKey = GetArtifactKey(artifact);
        var key = GetMemoryKey(identityKey, artifactKey) + ":" + GetTypeIdentity(typeof(T));
        lock (syncRoot)
        {
            try
            {
                var settings = ReadSettings();
                if (!CanPersist(source, artifact, settings)) return false;
                var stamp = CapturePreparedStamp(identityKey, artifactKey);
                if (stamp is null) { preparedArtifacts.Remove(key); return false; }
                if (preparedArtifacts.TryGetValue(key, out var previous) && stamp == previous) return true;
                preparedArtifacts.Remove(key);
                if (!TryReadPersistentArtifact(source, artifact, identityKey, artifactKey,
                        GetTypeIdentity(typeof(T)), settings, out _, out T _, deserialize: false)) return false;
                // Reading an artifact can update access timestamps. Observe the final state.
                stamp = CapturePreparedStamp(identityKey, artifactKey);
                if (stamp is null) return false;
                if (preparedArtifacts.Count >= MaximumPruneDirectoryCount) preparedArtifacts.Clear();
                preparedArtifacts[key] = stamp;
                return true;
            }
            catch (Exception exception) when (IsDisposableCacheException(exception))
            { preparedArtifacts.Remove(key); return false; }
        }
    }

    private PreparedArtifactStamp? CapturePreparedStamp(string identityKey, string artifactKey)
    {
        var source = Capture(Path.Combine(GetIdentityDirectory(identityKey), SourceManifestFileName));
        var metadata = Capture(GetArtifactMetadataPath(identityKey, artifactKey));
        var payload = Capture(GetArtifactPath(identityKey, artifactKey));
        return source is null || metadata is null || payload is null ? null : new(source, metadata, payload);

        static PreparedFileStamp? Capture(string path)
        {
            var file = new FileInfo(path);
            return !file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 ? null
                : new(file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks);
        }
    }
}
