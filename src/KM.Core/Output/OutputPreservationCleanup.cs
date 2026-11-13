// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Semantics;

namespace KM.Core.Output;

public sealed partial class OutputTransactionCoordinator
{
    /// <summary>Deletes only requested unreferenced artifacts under the recovery and workspace lock.</summary>
    public Task PrunePreservationArtifactsAsync(IReadOnlyCollection<string> candidates,
        Func<IEnumerable<OwnedTarget>, IReadOnlyList<string>> roots,
        Func<byte[], IReadOnlyList<string>> references, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(references);
        if (candidates.Count > OutputLimits.MaximumPreservationArtifacts) throw new OutputLimitExceededException("The restoration cleanup set exceeds its supported size.");
        var requested = candidates.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var hash in requested) _ = PreservationArtifactPath(hash);
        return ExecuteExclusiveOutputOperationAsync(async token =>
        {
            if (!requested.Any(hash => File.Exists(PreservationArtifactPath(hash)))) return true;
            var inventory = await ReadInventoryAsync(token).ConfigureAwait(false);
            var checkpoints = await ReadCheckpointManifestsAsync(removeIncomplete: false, token).ConfigureAwait(false);
            var transactions = await DiscoverTransactionsAsync(scavengeRetiredMaterial: false, token).ConfigureAwait(false);
            if (transactions.Any(t => t.Journal is null)) throw new InvalidDataException("Restoration cleanup requires readable recovery records.");
            var claims = inventory.Files.SelectMany(f => f.Claims)
                .Concat(checkpoints.SelectMany(c => c.Entries).SelectMany(e => e.OwnershipClaims))
                .Concat(transactions.SelectMany(t => t.Journal!.Entries).SelectMany(e => e.OwnershipClaims));
            var retained = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>(roots(claims));
            long inspected = 0;
            while (pending.TryDequeue(out var hash))
            {
                token.ThrowIfCancellationRequested();
                if (!retained.Add(hash)) continue;
                if (retained.Count > OutputLimits.MaximumPreservationArtifacts) throw new OutputLimitExceededException("The restoration reference graph exceeds its supported size.");
                var bytes = await ReadPreservationArtifactCoreAsync(hash, token).ConfigureAwait(false);
                inspected += bytes.LongLength;
                if (inspected > OutputLimits.MaximumCheckpointBytes) throw new OutputLimitExceededException("The restoration reference graph exceeds its supported size.");
                foreach (var child in references(bytes)) pending.Enqueue(child);
            }
            foreach (var hash in requested.Where(h => !retained.Contains(h)))
            {
                token.ThrowIfCancellationRequested();
                var path = PreservationArtifactPath(hash);
                paths.ValidateMetadataFile(path);
                if (!File.Exists(path)) continue;
                // Preparation and read leases protect in-flight use. A later cleanup can retry.
                using (metadata.OpenPrivateFile(path, FileMode.Open, FileAccess.Read, FileShare.None, FileOptions.SequentialScan)) { }
                File.Delete(path);
            }
            return true;
        }, cancellationToken);
    }
}
