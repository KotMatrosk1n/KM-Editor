// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using KM.Api.Output;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

/// <summary>Bounded, allowlisted summaries of recorded output, never game files or edited values.</summary>
internal static class SupportChangeInventory
{
    private const int MaximumRestorations = 468;
    private static readonly Dictionary<string, HashSet<string>> Catalog = LoadCatalog();

    public static async Task<SupportChangeInventoryDto> ReadAsync(ProjectPaths paths, CancellationToken token)
    {
        var ownershipStatus = "not-configured";
        var historyStatus = "not-configured";
        var restorationStatus = "not-configured";
        OutputOwnershipRecord[]? files = null;
        IReadOnlyList<OutputApplyReceipt>? receipts = null;
        int? otherScope = null, selections = null, removedSelections = null;
        var restorations = new List<SupportRestorationDto>();
        var omitted = 0;
        OutputTransactionCoordinator? reader = null;
        ProjectId project = default;
        GameFamily family = default;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        var ct = budget.Token;
        try
        {
            if (paths.SelectedGame is null || string.IsNullOrWhiteSpace(paths.OutputRootPath)) return Snapshot();
            project = ProjectIdentity.FromPaths(paths);
            family = paths.SelectedGame.Value.ToGameFamily();
            reader = OutputTransactionCoordinator.ForProject(paths);
            ownershipStatus = historyStatus = restorationStatus = "not-recorded";
            try
            {
                var inventory = await reader.ReadSupportOwnershipAsync(ct).ConfigureAwait(false);
                if (inventory is not null)
                {
                    if (inventory.SchemaVersion != OutputOwnershipInventory.CurrentSchemaVersion)
                        throw new InvalidDataException("Unsupported ownership schema.");
                    var scopedFiles = inventory.Files.Where(InScope).ToArray();
                    // Keep totals exact in the JavaScript report as well as .NET.
                    if (scopedFiles.Sum(f => (decimal)f.CurrentState.LengthBytes) > 9_007_199_254_740_991m)
                        throw new InvalidDataException("Recorded output lengths exceed the report's numeric range.");
                    files = scopedFiles;
                    otherScope = inventory.Files.Length - files.Length;
                    ownershipStatus = "available";
                }
            }
            catch (Exception e) when (Expected(e)) { ownershipStatus = Status(e); }
            try
            {
                var history = await reader.ReadSupportHistoryAsync(ct).ConfigureAwait(false);
                if (history is not null)
                {
                    receipts = history.Where(r => r.ProjectId == project && r.GameFamily == family).ToArray();
                    historyStatus = "available";
                }
            }
            catch (Exception e) when (Expected(e)) { historyStatus = Status(e); }
            if (family == GameFamily.SwordShield && files is not null)
            {
                try
                {
                    var hash = SwShRestorationStateStore.StateHashForSupport(files.SelectMany(f => f.Claims));
                    if (hash is null) restorationStatus = "not-recorded";
                    else
                    {
                        var bytes = await reader.ReadSupportRestorationStateAsync(hash, ct).ConfigureAwait(false);
                        var state = JsonSerializer.Deserialize<SwShRestorationState>(bytes)
                            ?? throw new InvalidDataException("Empty restoration state.");
                        SwShRestorationStateStore.ValidateForSupport(state);
                        selections = state.Selections.Count;
                        removedSelections = state.Selections.Count(s => s.RemovalId is not null);
                        foreach (var selection in state.Selections.OrderBy(s => s.DefinitionId, StringComparer.Ordinal))
                        {
                            ct.ThrowIfCancellationRequested();
                            // Removed selections are retained for asset cleanup, not installed restorations.
                            if (selection.RemovalId is not null) continue;
                            var definition = SwShRestorationDefinitions.Get(selection.DefinitionId);
                            foreach (var identity in definition.Identities)
                            {
                                if (restorations.Count == MaximumRestorations) { omitted++; continue; }
                                restorations.Add(new(identity.Species, identity.Form, selection.Enabled,
                                    selection.AssetRevision, definition.AssetRevision, selection.CustomizationHash is not null,
                                    selection.AssetHashes.Count, selection.RemovedHashes.Count));
                            }
                        }
                        restorationStatus = "available";
                    }
                }
                catch (Exception e) when (Expected(e)) { restorationStatus = Status(e); restorations.Clear(); selections = removedSelections = null; omitted = 0; }
            }
            else if (family != GameFamily.SwordShield) restorationStatus = "not-applicable";
            else restorationStatus = ownershipStatus;
            return Snapshot();
        }
        catch (Exception e) when (Expected(e))
        {
            ownershipStatus = historyStatus = restorationStatus = Status(e);
            return Snapshot();
        }

        bool InScope(OutputOwnershipRecord record) => record.ProjectId == project && record.GameFamily == family;

        SupportChangeInventoryDto Snapshot()
        {
            var owners = new Dictionary<string, Group>(StringComparer.Ordinal);
            var domains = new Dictionary<string, Group>(StringComparer.Ordinal);
            foreach (var file in files ?? [])
            {
                // File membership is counted once per group, even with many claims.
                AddClaims(owners, file, c => Key("owners", c.OwnerId.Value));
                AddClaims(domains, file, c => c.Address.Record is { } record ? Key("domains", record.Domain.Value) : "other");
            }
            var activeFiles = (files ?? []).GroupBy(f => f.TransactionId).ToDictionary(g => g.Key, g => g.Count());
            var historyGroups = new Dictionary<string, (int Applies, int Changes, HashSet<OutputTransactionId> Transactions)>();
            foreach (var receipt in receipts ?? [])
            {
                if (receipt.Outcome != OutputApplyOutcome.Committed) continue;
                var changes = receipt.HistoryDetails?.Changes ?? [];
                var counts = changes.GroupBy(c => Key("domains", c.Domain)).ToDictionary(g => g.Key, g => g.Count());
                var ids = receipt.Origins.Where(o => o.Kind == OutputApplyOriginKind.Workflow)
                    .Select(o => Key("domains", o.Id)).Concat(changes.Select(c => Key("domains", c.Domain)))
                    .Distinct(StringComparer.Ordinal).DefaultIfEmpty("other");
                foreach (var id in ids)
                {
                    var group = historyGroups.GetValueOrDefault(id, (Applies: 0, Changes: 0, Transactions: new HashSet<OutputTransactionId>()));
                    group.Transactions.Add(receipt.TransactionId);
                    historyGroups[id] = (group.Applies + 1, group.Changes + counts.GetValueOrDefault(id), group.Transactions);
                }
            }
            return new(1, "recorded-output-metadata", "game-files-not-checked", ownershipStatus,
                files?.Length, files?.Sum(f => f.CurrentState.LengthBytes), files?.Count(f => f.RuntimeMutableDescriptor is not null),
                otherScope, Groups(owners), Groups(domains), historyStatus, receipts?.Count, OutputLimits.MaximumHistoryReceipts,
                receipts?.Count(r => r.Outcome == OutputApplyOutcome.Committed && r.HistoryDetails is null),
                receipts?.Where(r => r.Outcome == OutputApplyOutcome.Committed).Sum(r => r.HistoryDetails is { } d ? (long)d.TotalChangeCount - d.Changes.Count : 0),
                historyGroups.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new SupportHistoryGroupDto(p.Key,
                    p.Value.Applies, p.Value.Changes, files is null ? null : p.Value.Transactions.Sum(t => activeFiles.GetValueOrDefault(t)))).ToArray(),
                restorationStatus, selections, removedSelections, omitted, restorations);
        }
    }

    private sealed class Group { public int Files, Claims, Records; }
    private static void AddClaims(Dictionary<string, Group> groups, OutputOwnershipRecord file, Func<OwnedTarget, string> key)
    {
        foreach (var claims in file.Claims.GroupBy(key, StringComparer.Ordinal))
        {
            if (!groups.TryGetValue(claims.Key, out var group)) groups[claims.Key] = group = new();
            group.Files++; group.Claims += claims.Count();
            group.Records += claims.Where(c => c.Address.Record is not null).Select(c => c.Address.Record).Distinct().Count();
        }
    }
    private static SupportInventoryGroupDto[] Groups(Dictionary<string, Group> groups) => groups
        .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new SupportInventoryGroupDto(p.Key, p.Value.Files, p.Value.Claims, p.Value.Records)).ToArray();
    private static string Key(string kind, string value) => Catalog[kind].Contains(value) ? value : "other";
    private static Dictionary<string, HashSet<string>> LoadCatalog()
    {
        using var stream = typeof(SupportChangeInventory).Assembly.GetManifestResourceStream("KM.Tools.SupportInventoryCatalog.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(stream)!;
    }
    private static bool Expected(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
        or System.Security.SecurityException or JsonException or OutputCoordinatorException or OperationCanceledException;
    private static string Status(Exception e) => e switch
    {
        OperationCanceledException => "timeout", OutputLimitExceededException => "size-limit",
        UnauthorizedAccessException or System.Security.SecurityException or OutputMetadataSecurityException => "access-denied",
        OutputPathSecurityException => "unsafe-path", JsonException or InvalidDataException or ArgumentException => "invalid-data",
        IOException => "io-error", _ => "unavailable",
    };
}
