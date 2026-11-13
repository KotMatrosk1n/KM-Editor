// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using KM.Core.Output;
using KM.Core.Projects;

namespace KM.SwSh.Restoration;

/// <summary>Reclaims removed preparation data without invalidating committed state or checkpoints.</summary>
public static class SwShRestorationAssetCleanup
{
    public static void Collect(ProjectPaths paths, SwShRestorationState state, IReadOnlySet<string>? activePreparedHashes = null)
    {
        var candidates = state.Selections.Where(s => s.RemovalId is not null).SelectMany(s => s.RemovedHashes)
            .Where(hash => activePreparedHashes?.Contains(hash) != true).Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return;
        OutputTransactionCoordinator.ForProject(paths).PrunePreservationArtifactsAsync(candidates,
            claims => claims.Where(SwShRestorationStateStore.IsStateClaim)
                .Select(c => SwShRestorationStateStore.StateHash([c])!).ToArray(),
            bytes =>
            {
                if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K') return [];
                if (SwShRestorationStateStore.CustomizationReferences(bytes) is { } dependencies) return dependencies;
                if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Restoration state exceeds its size limit.");
                var saved = JsonSerializer.Deserialize<SwShRestorationState>(bytes)
                    ?? throw new InvalidDataException("Restoration state is empty.");
                if (saved.Schema != 1 || saved.Selections is null || saved.Selections.Count > SwShRestorationDefinitions.All.Count)
                    throw new InvalidDataException("Restoration state is invalid.");
                return saved.Selections.Where(s => s.RemovalId is null)
                    .SelectMany(s => s.CustomizationHash is { } custom ? new[] { s.PreparedHash, custom } : [s.PreparedHash])
                    .Concat(saved.DataHash is { } dataHash ? [dataHash] : []).ToArray();
            }).GetAwaiter().GetResult();
    }
}
