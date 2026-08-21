// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.ZA.Data;

namespace KM.ZA.Encounters;

internal sealed record ZaBossBattleTableContext(
    string RawSpawnerId,
    ZaBossBattleContext PrimaryContext,
    IReadOnlyList<ZaBossBattleContext> Contexts,
    string? WaveLabel,
    int? WaveRank);

internal sealed class ZaBossBattleContextResolver
{
    private static readonly ZaBossBattleContext StoryContext = new(
        "story",
        "Main Battle",
        0);
    private static readonly ZaBossBattleContext SimulatorMissionContext = new(
        "simulator-mission",
        "Simulator During Mission",
        1);
    private static readonly ZaBossBattleContext SimulationContext = new(
        "simulation",
        "Simulation",
        2);
    private static readonly ZaBossBattleContext SimulationDlcContext = new(
        "simulation-dlc",
        "Simulation 2",
        3);
    private static readonly ZaBossBattleContext RematchContext = new(
        "rematch",
        "Rematch",
        4);
    private static readonly ZaBossBattleContext RushContext = new(
        "rush",
        "Rush",
        5);

    private readonly IReadOnlyDictionary<string, IReadOnlyList<ZaBossBattleContext>> mainContextsBySpawnerId;
    private readonly IReadOnlyDictionary<SupportIdentityKey, IReadOnlyList<ZaBossBattleContext>>
        directSupportContextsByIdentity;
    private readonly IReadOnlyDictionary<SpawnerLineageKey, IReadOnlyList<ZaBossBattleContext>>
        simulationAliasContextsByLineage;
    private readonly ISet<SpawnerLineageKey> storyWaveReuseLineages;
    private readonly ISet<SpawnerLineageKey> missingSimulationTwoReuseLineages;
    private readonly ISet<SpawnerLineageModeKey> missingRematchReuseLineages;
    private readonly ISet<SpawnerLineageKey> dimensionRematchLineages;

    public ZaBossBattleContextResolver(
        IReadOnlyList<ZaBossBattleConsumerRecord>? consumerRecords,
        IEnumerable<string?> availableSpawnerIds)
    {
        ArgumentNullException.ThrowIfNull(availableSpawnerIds);

        var availableBossSpawners = availableSpawnerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => TryParseSpawnerId(id!, out var identity) ? identity : null)
            .Where(identity => identity is not null)
            .Cast<BossSpawnerIdentity>()
            .ToArray();
        var availableSupportSpawners = availableBossSpawners
            .Where(identity => identity.Role == BossSpawnerRole.Support)
            .ToArray();
        var availableSupportIdentities = availableSupportSpawners
            .Select(CreateSupportIdentityKey)
            .ToHashSet();
        var availableSimulationOneLineages = availableSupportSpawners
            .Where(identity => string.Equals(
                GetTerminalMode(identity.Variant),
                "sim1",
                StringComparison.OrdinalIgnoreCase))
            .Select(CreateLineageKey)
            .ToHashSet();
        var preferredRematchModes = availableSupportSpawners
            .GroupBy(CreateLineageKey)
            .Select(group => new
            {
                Lineage = group.Key,
                Mode = group.Any(identity => string.Equals(
                    GetTerminalMode(identity.Variant),
                    "rus2",
                    StringComparison.OrdinalIgnoreCase))
                        ? "rus2"
                        : group.Any(identity => string.Equals(
                            GetTerminalMode(identity.Variant),
                            "rus",
                            StringComparison.OrdinalIgnoreCase))
                                ? "rus"
                                : null,
            })
            .Where(candidate => candidate.Mode is not null)
            .ToDictionary(candidate => candidate.Lineage, candidate => candidate.Mode!);

        var parsedRecords = (consumerRecords ?? [])
            .Select(record => new
            {
                Record = record,
                Main = TryParseSpawnerId(record.MainSpawnerId, out var main) ? main : null,
                Support = TryParseSpawnerId(record.SupportSpawnerGroupId, out var support) ? support : null,
            })
            .Where(record => record.Main is not null || record.Support is not null)
            .ToArray();
        dimensionRematchLineages = parsedRecords
            .Select(record => record.Main ?? record.Support!)
            .Where(identity => IsDimensionRematchVariant(identity.Variant))
            .Select(CreateLineageKey)
            .ToHashSet();
        var consumers = parsedRecords
            .Select(record => new ParsedConsumer(
                record.Main,
                record.Support,
                ResolveConsumerContext(
                    record.Record,
                    record.Main,
                    record.Support,
                    dimensionRematchLineages.Contains(CreateLineageKey(record.Main ?? record.Support!)))))
            .ToArray();

        var mainContexts = new Dictionary<string, List<ZaBossBattleContext>>(StringComparer.OrdinalIgnoreCase);
        var directSupportContexts = new Dictionary<SupportIdentityKey, List<ZaBossBattleContext>>();
        var simulationAliasContexts = new Dictionary<SpawnerLineageKey, List<ZaBossBattleContext>>();
        storyWaveReuseLineages = new HashSet<SpawnerLineageKey>();
        missingSimulationTwoReuseLineages = new HashSet<SpawnerLineageKey>();
        missingRematchReuseLineages = new HashSet<SpawnerLineageModeKey>();
        foreach (var consumer in consumers)
        {
            if (consumer.Main is not null)
            {
                AddContext(mainContexts, consumer.Main.RawId, consumer.Context);
            }

            if (consumer.Support is null)
            {
                continue;
            }

            var supportIdentity = CreateSupportIdentityKey(consumer.Support);
            var supportLineage = CreateLineageKey(consumer.Support);
            AddContext(directSupportContexts, supportIdentity, consumer.Context);
            if (string.Equals(
                GetTerminalMode(consumer.Support.Variant),
                "sim",
                StringComparison.OrdinalIgnoreCase))
            {
                AddContext(simulationAliasContexts, supportLineage, consumer.Context);
            }

            if (consumer.Context.Key == StoryContext.Key
                && TryGetPositiveIntegerTail(consumer.Support.Variant, out _))
            {
                storyWaveReuseLineages.Add(supportLineage);
            }

            var hasDirectSupportSpawner = availableSupportIdentities.Contains(supportIdentity)
                || (string.Equals(
                        GetTerminalMode(consumer.Support.Variant),
                        "sim",
                        StringComparison.OrdinalIgnoreCase)
                    && availableSimulationOneLineages.Contains(supportLineage));
            if (hasDirectSupportSpawner)
            {
                continue;
            }

            if (consumer.Context.Key == SimulationDlcContext.Key)
            {
                missingSimulationTwoReuseLineages.Add(supportLineage);
            }

            if (consumer.Context.Key == RematchContext.Key
                && string.Equals(
                    GetTerminalMode(consumer.Support.Variant),
                    "re",
                    StringComparison.OrdinalIgnoreCase)
                && preferredRematchModes.TryGetValue(supportLineage, out var preferredRematchMode))
            {
                missingRematchReuseLineages.Add(new SpawnerLineageModeKey(
                    supportLineage,
                    preferredRematchMode));
            }
        }

        mainContextsBySpawnerId = FreezeContextIndex(mainContexts, StringComparer.OrdinalIgnoreCase);
        directSupportContextsByIdentity = FreezeContextIndex(directSupportContexts);
        simulationAliasContextsByLineage = FreezeContextIndex(simulationAliasContexts);
    }

    public ZaBossBattleTableContext? Resolve(
        string? rawSpawnerId,
        IEnumerable<string> encounterDataIds)
    {
        if (!TryParseSpawnerId(rawSpawnerId, out var spawner))
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(encounterDataIds);

        var candidates = new List<ContextCandidate>();
        if (spawner.Role == BossSpawnerRole.Main
            && mainContextsBySpawnerId.TryGetValue(spawner.RawId, out var mainContexts))
        {
            AddCandidates(candidates, mainContexts, 0);
        }

        if (spawner.Role == BossSpawnerRole.Support)
        {
            var supportIdentity = CreateSupportIdentityKey(spawner);
            var supportLineage = CreateLineageKey(spawner);
            if (directSupportContextsByIdentity.TryGetValue(supportIdentity, out var directContexts))
            {
                AddCandidates(candidates, directContexts, 1);
            }

            var terminalMode = GetTerminalMode(spawner.Variant);
            if (string.Equals(terminalMode, "sim1", StringComparison.OrdinalIgnoreCase)
                && simulationAliasContextsByLineage.TryGetValue(supportLineage, out var aliasContexts))
            {
                AddCandidates(candidates, aliasContexts, 1);
            }

            if (TryGetPositiveIntegerTail(spawner.Variant, out _)
                && storyWaveReuseLineages.Contains(supportLineage))
            {
                candidates.Add(new ContextCandidate(StoryContext, 3));
            }

            if ((string.Equals(terminalMode, "sim1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(terminalMode, "sim", StringComparison.OrdinalIgnoreCase))
                && missingSimulationTwoReuseLineages.Contains(supportLineage))
            {
                candidates.Add(new ContextCandidate(SimulationDlcContext, 3));
            }

            if (missingRematchReuseLineages.Contains(new SpawnerLineageModeKey(
                supportLineage,
                terminalMode)))
            {
                candidates.Add(new ContextCandidate(RematchContext, 3));
            }
        }

        var fallbackContext = ResolveVariantContext(
            spawner.Variant,
            dimensionRematchLineages.Contains(CreateLineageKey(spawner)));
        candidates.Add(new ContextCandidate(
            fallbackContext ?? CreateVariantContext(spawner.Variant),
            fallbackContext is null ? 5 : 2));

        if (encounterDataIds.Any(IsRushEncounterDataId))
        {
            candidates.Add(new ContextCandidate(RushContext, 3));
        }

        var bestCandidates = candidates
            .GroupBy(candidate => candidate.Context.Key, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Context.Rank)
                .First())
            .ToArray();
        var primary = bestCandidates
            .OrderBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Context.Rank)
            .First()
            .Context;
        var contexts = bestCandidates
            .Select(candidate => candidate.Context)
            .OrderBy(context => context.Rank)
            .ThenBy(context => context.Label, StringComparer.Ordinal)
            .ToArray();
        var (waveLabel, waveRank) = ResolveWave(spawner);
        return new ZaBossBattleTableContext(
            spawner.RawId,
            primary,
            contexts,
            waveLabel,
            waveRank);
    }

    private static void AddCandidates(
        ICollection<ContextCandidate> candidates,
        IEnumerable<ZaBossBattleContext> contexts,
        int priority)
    {
        foreach (var context in contexts)
        {
            candidates.Add(new ContextCandidate(context, priority));
        }
    }

    private static void AddContext<TKey>(
        IDictionary<TKey, List<ZaBossBattleContext>> contextsByKey,
        TKey key,
        ZaBossBattleContext context)
        where TKey : notnull
    {
        if (!contextsByKey.TryGetValue(key, out var contexts))
        {
            contexts = [];
            contextsByKey.Add(key, contexts);
        }

        contexts.Add(context);
    }

    private static IReadOnlyDictionary<TKey, IReadOnlyList<ZaBossBattleContext>> FreezeContextIndex<TKey>(
        IReadOnlyDictionary<TKey, List<ZaBossBattleContext>> contextsByKey,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var frozen = new Dictionary<TKey, IReadOnlyList<ZaBossBattleContext>>(
            contextsByKey.Count,
            comparer ?? EqualityComparer<TKey>.Default);
        foreach (var pair in contextsByKey)
        {
            frozen.Add(
                pair.Key,
                pair.Value
                .GroupBy(context => context.Key, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(context => context.Rank)
                    .First())
                .ToArray());
        }

        return frozen;
    }

    private static ZaBossBattleContext ResolveConsumerContext(
        ZaBossBattleConsumerRecord record,
        BossSpawnerIdentity? main,
        BossSpawnerIdentity? support,
        bool hasDimensionRematch)
    {
        var explicitContext = ResolveExplicitConsumerContext(record);
        if (explicitContext is not null)
        {
            return explicitContext;
        }

        var supportContext = support is null
            ? null
            : ResolveUnambiguousSupportContext(support.Variant);
        if (supportContext is not null)
        {
            return supportContext;
        }

        var identity = main ?? support!;
        if (IsDimensionRematchVariant(identity.Variant))
        {
            return RematchContext;
        }

        if (string.Equals(GetTerminalMode(identity.Variant), "re", StringComparison.OrdinalIgnoreCase)
            && (hasDimensionRematch || HasCompoundVariantStem(identity.Variant)))
        {
            return RushContext;
        }

        return ResolveVariantContext(identity.Variant, hasDimensionRematch)
            ?? CreateVariantContext(identity.Variant);
    }

    private static ZaBossBattleContext? ResolveUnambiguousSupportContext(string variant)
    {
        var mode = GetTerminalMode(variant);
        if (string.Equals(mode, "sim2", StringComparison.OrdinalIgnoreCase))
        {
            return SimulationDlcContext;
        }

        if (mode.StartsWith("sim", StringComparison.OrdinalIgnoreCase))
        {
            return SimulationContext;
        }

        if (mode.StartsWith("rus", StringComparison.OrdinalIgnoreCase)
            || mode.StartsWith("rush", StringComparison.OrdinalIgnoreCase))
        {
            return RushContext;
        }

        if (string.Equals(mode, "y", StringComparison.OrdinalIgnoreCase))
        {
            return SimulatorMissionContext;
        }

        return TryGetPositiveIntegerTail(variant, out _)
            ? StoryContext
            : null;
    }

    private static ZaBossBattleContext? ResolveExplicitConsumerContext(
        ZaBossBattleConsumerRecord record)
    {
        return ResolveExplicitContextValue(record.BattleId)
            ?? ResolveExplicitContextValue(record.EventId);
    }

    private static ZaBossBattleContext? ResolveExplicitContextValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var tokens = value
            .Split(
                ['_', '-', '/', ':', '.', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .ToArray();
        if (tokens.Contains("rematch", StringComparer.Ordinal)
            || tokens
                .Zip(tokens.Skip(1), (left, right) => (left, right))
                .Any(pair => pair.left == "re" && pair.right == "dim"))
        {
            return RematchContext;
        }

        if (tokens.Contains("sim2", StringComparer.Ordinal)
            || tokens.Contains("simulation2", StringComparer.Ordinal)
            || tokens
                .Zip(tokens.Skip(1), (left, right) => (left, right))
                .Any(pair => pair.left == "simulation" && pair.right == "2"))
        {
            return SimulationDlcContext;
        }

        if (tokens.Contains("simulation", StringComparer.Ordinal)
            || tokens.Contains("sim", StringComparer.Ordinal))
        {
            return SimulationContext;
        }

        if (tokens.Contains("rush", StringComparer.Ordinal))
        {
            return RushContext;
        }

        if (tokens.Contains("y", StringComparer.Ordinal)
            || tokens.Contains("simulator", StringComparer.Ordinal))
        {
            return SimulatorMissionContext;
        }

        if (tokens.Contains("story", StringComparer.Ordinal)
            || tokens.Contains("main", StringComparer.Ordinal))
        {
            return StoryContext;
        }

        return null;
    }

    private static ZaBossBattleContext? ResolveVariantContext(
        string variant,
        bool hasDimensionRematch)
    {
        var mode = GetTerminalMode(variant);
        if (string.IsNullOrWhiteSpace(variant) || TryGetPositiveIntegerTail(variant, out _))
        {
            return StoryContext;
        }

        if (IsDimensionRematchVariant(variant))
        {
            return RematchContext;
        }

        if (string.Equals(mode, "re", StringComparison.OrdinalIgnoreCase))
        {
            return hasDimensionRematch || HasCompoundVariantStem(variant)
                ? RushContext
                : RematchContext;
        }

        if (mode.StartsWith("rus", StringComparison.OrdinalIgnoreCase)
            || mode.StartsWith("rush", StringComparison.OrdinalIgnoreCase))
        {
            return RushContext;
        }

        if (string.Equals(mode, "sim2", StringComparison.OrdinalIgnoreCase))
        {
            return SimulationDlcContext;
        }

        if (mode.StartsWith("sim", StringComparison.OrdinalIgnoreCase))
        {
            return SimulationContext;
        }

        if (string.Equals(mode, "y", StringComparison.OrdinalIgnoreCase))
        {
            return SimulatorMissionContext;
        }

        return null;
    }

    private static ZaBossBattleContext CreateVariantContext(string variant)
    {
        var normalizedVariant = string.IsNullOrWhiteSpace(variant)
            ? "main"
            : variant.ToLowerInvariant().Replace('_', '-');
        var label = string.IsNullOrWhiteSpace(variant)
            ? "Main Battle"
            : string.Join(
                " ",
                variant
                    .Split('_', StringSplitOptions.RemoveEmptyEntries)
                    .Select(FormatVariantToken));
        return new ZaBossBattleContext(
            $"variant:{normalizedVariant}",
            label,
            9);
    }

    private static string FormatVariantToken(string token)
    {
        return token.Length == 1
            ? token.ToUpperInvariant()
            : char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }

    private static bool IsRushEncounterDataId(string encounterDataId)
    {
        return !string.IsNullOrWhiteSpace(encounterDataId)
            && encounterDataId.Contains("rush", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDimensionRematchVariant(string variant)
    {
        return string.Equals(variant, "re_dim", StringComparison.OrdinalIgnoreCase)
            || variant.EndsWith("_re_dim", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetPositiveIntegerTail(string variant, out int value)
    {
        var mode = GetTerminalMode(variant);
        return int.TryParse(
            mode,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value)
            && value > 0;
    }

    private static bool HasCompoundVariantStem(string variant)
    {
        return !string.IsNullOrWhiteSpace(GetVariantStem(variant));
    }

    private static (string? Label, int? Rank) ResolveWave(BossSpawnerIdentity spawner)
    {
        if (spawner.Role != BossSpawnerRole.Support)
        {
            return (null, null);
        }

        if (TryGetPositiveIntegerTail(spawner.Variant, out var numericWave))
        {
            return ($"Wave {numericWave.ToString(CultureInfo.InvariantCulture)}", numericWave);
        }

        var mode = GetTerminalMode(spawner.Variant);
        var rushPrefixLength = mode.StartsWith("rush", StringComparison.OrdinalIgnoreCase)
            ? "rush".Length
            : mode.StartsWith("rus", StringComparison.OrdinalIgnoreCase)
                ? "rus".Length
                : 0;
        if (rushPrefixLength > 0
            && int.TryParse(
                mode[rushPrefixLength..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var rushWave)
            && rushWave > 0)
        {
            return ($"Wave {rushWave.ToString(CultureInfo.InvariantCulture)}", rushWave);
        }

        return (null, null);
    }

    private static SupportIdentityKey CreateSupportIdentityKey(BossSpawnerIdentity identity)
    {
        return new SupportIdentityKey(
            identity.Species.ToLowerInvariant(),
            identity.Variant.ToLowerInvariant());
    }

    private static SpawnerLineageKey CreateLineageKey(BossSpawnerIdentity identity)
    {
        return new SpawnerLineageKey(
            identity.Species.ToLowerInvariant(),
            GetVariantStem(identity.Variant));
    }

    private static string GetTerminalMode(string variant)
    {
        var tokens = variant.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        if (tokens.Length >= 2
            && string.Equals(tokens[^2], "re", StringComparison.OrdinalIgnoreCase)
            && string.Equals(tokens[^1], "dim", StringComparison.OrdinalIgnoreCase))
        {
            return "re_dim";
        }

        return tokens[^1].ToLowerInvariant();
    }

    private static string GetVariantStem(string variant)
    {
        var tokens = variant.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var mode = GetTerminalMode(variant);
        var removeCount = string.Equals(mode, "re_dim", StringComparison.OrdinalIgnoreCase)
            ? 2
            : IsStructuredMode(mode) || int.TryParse(mode, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                ? 1
                : 0;
        return removeCount == 0
            ? variant.ToLowerInvariant()
            : string.Join('_', tokens[..^removeCount]).ToLowerInvariant();
    }

    private static bool IsStructuredMode(string mode)
    {
        return string.Equals(mode, "re", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "y", StringComparison.OrdinalIgnoreCase)
            || mode.StartsWith("sim", StringComparison.OrdinalIgnoreCase)
            || mode.StartsWith("rus", StringComparison.OrdinalIgnoreCase)
            || mode.StartsWith("rush", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSpawnerId(string? value, out BossSpawnerIdentity identity)
    {
        identity = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        const string mainPrefix = "btl_spn_boss_";
        const string supportPrefix = "spn_boss_";
        BossSpawnerRole role;
        string suffix;
        if (value.StartsWith(mainPrefix, StringComparison.OrdinalIgnoreCase))
        {
            role = BossSpawnerRole.Main;
            suffix = value[mainPrefix.Length..];
        }
        else if (value.StartsWith(supportPrefix, StringComparison.OrdinalIgnoreCase))
        {
            role = BossSpawnerRole.Support;
            suffix = value[supportPrefix.Length..];
        }
        else
        {
            return false;
        }

        var tokens = suffix.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var variantTokens = tokens.Skip(1).ToArray();
        if (role == BossSpawnerRole.Support)
        {
            var followerIndex = Array.FindIndex(
                variantTokens,
                token => token.StartsWith("follower", StringComparison.OrdinalIgnoreCase));
            if (followerIndex >= 0)
            {
                variantTokens = variantTokens[..followerIndex];
            }
        }

        identity = new BossSpawnerIdentity(
            value,
            tokens[0],
            role,
            string.Join('_', variantTokens).ToLowerInvariant());
        return true;
    }

    private sealed record ParsedConsumer(
        BossSpawnerIdentity? Main,
        BossSpawnerIdentity? Support,
        ZaBossBattleContext Context);

    private sealed record BossSpawnerIdentity(
        string RawId,
        string Species,
        BossSpawnerRole Role,
        string Variant);

    private readonly record struct ContextCandidate(
        ZaBossBattleContext Context,
        int Priority);

    private readonly record struct SupportIdentityKey(
        string Species,
        string Variant);

    private readonly record struct SpawnerLineageKey(
        string Species,
        string VariantStem);

    private readonly record struct SpawnerLineageModeKey(
        SpawnerLineageKey Lineage,
        string Mode);

    private enum BossSpawnerRole
    {
        Main,
        Support,
    }
}
