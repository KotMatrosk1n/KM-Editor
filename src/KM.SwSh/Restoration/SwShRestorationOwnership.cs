// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SwSh.Editing;

namespace KM.SwSh.Restoration;

internal static class SwShRestorationOwnership
{
    internal static IReadOnlySet<string> ExactFiles(ProjectPaths paths)
    {
        var original = SwShChangePlanSourceGuard.OriginalPaths(paths);
        if (string.IsNullOrWhiteSpace(original.OutputRootPath) || !new OutputWorkspaceStorage(original).HasMaterial) return new HashSet<string>();
        var coordinator = OutputTransactionCoordinator.ForProject(original);
        var records = coordinator.GetOwnershipInventorySnapshotAsync().GetAwaiter().GetResult().Inventory.Files;
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (!coordinator.OwnershipScopeMatches(record, ProjectIdentity.FromPaths(original), GameFamily.SwordShield)
                || record.RuntimeMutableDescriptor is not null
                || record.Claims.Any(c => c.OwnerId.Value != "sword-shield-verified-editor" && !OutputCreatorProvenance.IsClaim(c))
                || !record.Claims.Any(c => c.OwnerId.Value == "sword-shield-verified-editor")) continue;
            var file = Path.Combine(original.OutputRootPath, record.Path.Value.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) continue;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > SwShRestorationSourceBudget.MaximumBytes(record.Path.Value)) continue;
            var actual = OutputFileState.Existing(Convert.ToHexStringLower(SHA256.HashData(stream)), stream.Length);
            if (actual == record.CurrentState) result.Add(record.Path.Value);
        }
        return result;
    }
}
