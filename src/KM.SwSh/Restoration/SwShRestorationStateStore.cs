// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text.Json;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SwSh.Editing;

namespace KM.SwSh.Restoration;

public sealed record SwShRestorationSelection(string DefinitionId, string PreparedHash, bool Enabled, string? CustomizationHash = null)
{
    public int AssetRevision { get; init; } = 1;
    public string? RemovalId { get; init; }
    public IReadOnlyList<string> AssetHashes { get; init; } = [];
    public IReadOnlyList<string> RemovedHashes { get; init; } = [];
}
public sealed record SwShRestorationState(int Schema, IReadOnlyList<SwShRestorationSelection> Selections)
{
    public static SwShRestorationState Empty { get; } = new(1, []);
}
public sealed record SwShRestorationStateSnapshot(string? Hash, SwShRestorationState State);
internal sealed record SwShRestorationStateTransition(string? Before, string After);

/// <summary>Publishes immutable state through the same ownership transaction as its game data.</summary>
public static class SwShRestorationStateStore
{
    private const string ClaimPrefix = "pokemon-restoration-state.";
    private const string Owner = "sword-shield-verified-editor";

    // Read-only diagnostics use the committed selection identity without loading
    // prepared assets or normalizing state through the live editor workflow.
    public static string? StateHashForSupport(IEnumerable<OwnedTarget> claims) => StateHash(claims);
    public static void ValidateForSupport(SwShRestorationState state) => Validate(state);

    public static SwShRestorationStateSnapshot Read(ProjectPaths project)
    {
        if (SwShChangePlanSourceGuard.RestorationView(project)?.State is { } prospective) return prospective;
        var paths = SwShChangePlanSourceGuard.OriginalPaths(project);
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || !new OutputWorkspaceStorage(paths).HasMaterial)
            return new(null, SwShRestorationState.Empty);
        var coordinator = OutputTransactionCoordinator.ForProject(paths);
        var record = coordinator.GetOwnershipRecordAsync(new(SwShRestorationComposer.PersonalPath)).GetAwaiter().GetResult();
        if (record is null) return new(null, SwShRestorationState.Empty);
        if (!coordinator.OwnershipScopeMatches(record, ProjectIdentity.FromPaths(paths), GameFamily.SwordShield))
            throw new InvalidDataException("Restoration state belongs to another project scope.");
        var hash = StateHash(record.Claims);
        if (hash is null) return new(null, SwShRestorationState.Empty);
        return ReadState(paths, hash);
    }
    internal static SwShRestorationStateSnapshot ReadState(ProjectPaths project, string hash)
    {
        ValidateHash(hash);
        var paths = SwShChangePlanSourceGuard.OriginalPaths(project);
        var bytes = SwShRestorationReadScope.Find(paths)?.ReadBytes(hash)
            ?? OutputTransactionCoordinator.ForProject(paths).ReadPreservationArtifactAsync(hash).GetAwaiter().GetResult();
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Restoration state exceeds its size limit.");
        var state = JsonSerializer.Deserialize<SwShRestorationState>(bytes) ?? throw new InvalidDataException("Restoration state is empty.");
        Validate(state); return new(hash, SwShRestorationCompatibility.Normalize(paths, state));
    }
    public static SwShPreparedRestoration ReadPrepared(ProjectPaths project, string hash)
    {
        ValidateHash(hash);
        var paths = SwShChangePlanSourceGuard.OriginalPaths(project);
        if (SwShRestorationReadScope.Find(paths) is { } scope) return scope.ReadPrepared([hash])[hash];
        var bundle = SwShPreparedRestoration.Decode(OutputTransactionCoordinator.ForProject(paths).ReadPreservationArtifactAsync(hash).GetAwaiter().GetResult());
        if (bundle.Game != paths.SelectedGame) throw new InvalidDataException("Prepared restoration belongs to another game edition.");
        return bundle;
    }
    internal static IReadOnlyDictionary<string, SwShPreparedRestoration> ReadPreparedMany(ProjectPaths project, IEnumerable<string> hashes)
    {
        var identities = hashes.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var identity in identities) ValidateHash(identity);
        var paths = SwShChangePlanSourceGuard.OriginalPaths(project);
        if (SwShRestorationReadScope.Find(paths) is { } scope) return scope.ReadPrepared(identities);
        var artifacts = OutputTransactionCoordinator.ForProject(paths).ReadPreservationArtifactsAsync(identities).GetAwaiter().GetResult();
        return artifacts.ToDictionary(pair => pair.Key, pair =>
        {
            var bundle = SwShPreparedRestoration.Decode(pair.Value);
            if (bundle.Game != paths.SelectedGame) throw new InvalidDataException("Prepared restoration belongs to another game edition.");
            return bundle;
        }, StringComparer.Ordinal);
    }
    public static (string Hash, FileStream Lease) Store(ProjectPaths project, SwShPreparedRestoration prepared, CancellationToken token = default)
    {
        if (project.SelectedGame != prepared.Game) throw new InvalidDataException("Prepared restoration belongs to another game edition.");
        return StoreBytes(project, prepared.Encode(), token);
    }
    public static (string Hash, FileStream Lease) Store(ProjectPaths project, SwShRestorationState state, CancellationToken token = default)
    {
        Validate(state); return StoreBytes(project, JsonSerializer.SerializeToUtf8Bytes(state), token);
    }
    private static (string Hash, FileStream Lease) StoreBytes(ProjectPaths project, byte[] bytes, CancellationToken token)
    {
        var paths = SwShChangePlanSourceGuard.OriginalPaths(project);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var lease = OutputTransactionCoordinator.ForProject(paths).StorePreservationArtifactAsync(bytes, token).GetAwaiter().GetResult();
        return (hash, lease);
    }
    internal static bool IsStateClaim(OwnedTarget claim) => claim.OwnerId.Value == Owner && claim.Address.File.Value == SwShRestorationComposer.PersonalPath
        && claim.PreservationRule.Key.StartsWith(ClaimPrefix, StringComparison.Ordinal);
    internal static string? StateHash(IEnumerable<OwnedTarget> claims)
    {
        var hashes = claims.Where(IsStateClaim).Select(c => c.PreservationRule.Key[ClaimPrefix.Length..]).Distinct(StringComparer.Ordinal).ToArray();
        if (hashes.Length > 1) throw new InvalidDataException("Restoration state has conflicting committed identities.");
        if (hashes.Length == 0) return null;
        ValidateHash(hashes[0]); return hashes[0];
    }
    internal static OwnedTarget Claim(string hash)
    {
        ValidateHash(hash);
        return new(GameFamily.SwordShield, new(new(SwShRestorationComposer.PersonalPath)), new(Owner), new(ClaimPrefix + hash, 1, true, true));
    }
    private static void ValidateHash(string hash)
    { if (hash is null || hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigitLower(c))) throw new InvalidDataException("Restoration artifact identity is invalid."); }
    private static void Validate(SwShRestorationState state)
    {
        if (state.Schema != 1 || state.Selections is null || state.Selections.Any(s => s is null) || state.Selections.Count > SwShRestorationDefinitions.All.Count || state.Selections.Select(s => s.DefinitionId).Distinct(StringComparer.Ordinal).Count() != state.Selections.Count)
            throw new InvalidDataException("Restoration state schema or selection count is invalid.");
        foreach (var selection in state.Selections)
        {
            _ = SwShRestorationDefinitions.Get(selection.DefinitionId); ValidateHash(selection.PreparedHash);
            if (selection.AssetRevision < 1) throw new InvalidDataException("Restoration asset revision is invalid.");
            if (selection.CustomizationHash is not null) ValidateHash(selection.CustomizationHash);
            if (selection.RemovalId is not null && (!Guid.TryParseExact(selection.RemovalId, "N", out _) || selection.Enabled || selection.CustomizationHash is not null)
                || selection.RemovedHashes is null || selection.RemovedHashes.Count > 1024 || selection.AssetHashes is null || selection.AssetHashes.Count > 1024)
                throw new InvalidDataException("Restoration removal state is invalid.");
            foreach (var hash in selection.RemovedHashes.Concat(selection.AssetHashes)) ValidateHash(hash);
        }
    }
}
