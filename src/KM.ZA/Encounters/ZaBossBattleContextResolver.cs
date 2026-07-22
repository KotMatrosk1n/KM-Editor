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

    private readonly IReadOnlyList<ParsedConsumer> consumers;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<BossSpawnerIdentity>> supportSpawnersBySpecies;
    private readonly ISet<string> dimensionRematchLineages;

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
        supportSpawnersBySpecies = availableBossSpawners
            .Where(identity => identity.Role == BossSpawnerRole.Support)
            .GroupBy(identity => identity.Species, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BossSpawnerIdentity>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

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
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        consumers = parsedRecords
            .Select(record => new ParsedConsumer(
                record.Record,
                record.Main,
                record.Support,
                ResolveConsumerContext(
                    record.Record,
                    record.Main,
                    record.Support,
                    dimensionRematchLineages.Contains(CreateLineageKey(record.Main ?? record.Support!)))))
            .ToArray();
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
        foreach (var consumer in consumers)
        {
            if (consumer.Main is not null
                && string.Equals(
                    consumer.Record.MainSpawnerId,
                    spawner.RawId,
                    StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(new ContextCandidate(consumer.Context, 0));
            }

            if (consumer.Support is not null
                && TryGetDirectSupportMatchRank(consumer.Support, spawner, out var matchRank))
            {
                candidates.Add(new ContextCandidate(consumer.Context, matchRank));
            }

            if (IsStoryWaveReuse(consumer, spawner)
                || IsMissingSimulationTwoSupportReuse(consumer, spawner)
                || IsMissingRematchSupportReuse(consumer, spawner))
            {
                candidates.Add(new ContextCandidate(consumer.Context, 3));
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

    private bool IsStoryWaveReuse(ParsedConsumer consumer, BossSpawnerIdentity spawner)
    {
        return consumer.Context.Key == StoryContext.Key
            && consumer.Support is not null
            && spawner.Role == BossSpawnerRole.Support
            && string.Equals(consumer.Support.Species, spawner.Species, StringComparison.OrdinalIgnoreCase)
            && TryGetPositiveIntegerTail(consumer.Support.Variant, out _)
            && TryGetPositiveIntegerTail(spawner.Variant, out _)
            && string.Equals(
                GetVariantStem(consumer.Support.Variant),
                GetVariantStem(spawner.Variant),
                StringComparison.OrdinalIgnoreCase);
    }

    private bool IsMissingSimulationTwoSupportReuse(
        ParsedConsumer consumer,
        BossSpawnerIdentity spawner)
    {
        if (consumer.Context.Key != SimulationDlcContext.Key
            || consumer.Support is null
            || spawner.Role != BossSpawnerRole.Support
            || !string.Equals(consumer.Support.Species, spawner.Species, StringComparison.OrdinalIgnoreCase)
            || HasDirectSupportSpawner(consumer.Support))
        {
            return false;
        }

        var mode = GetTerminalMode(spawner.Variant);
        return (string.Equals(mode, "sim1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "sim", StringComparison.OrdinalIgnoreCase))
            && string.Equals(
                GetVariantStem(consumer.Support.Variant),
                GetVariantStem(spawner.Variant),
                StringComparison.OrdinalIgnoreCase);
    }

    private bool IsMissingRematchSupportReuse(
        ParsedConsumer consumer,
        BossSpawnerIdentity spawner)
    {
        if (consumer.Context.Key != RematchContext.Key
            || consumer.Support is null
            || !string.Equals(GetTerminalMode(consumer.Support.Variant), "re", StringComparison.OrdinalIgnoreCase)
            || spawner.Role != BossSpawnerRole.Support
            || !string.Equals(consumer.Support.Species, spawner.Species, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                GetVariantStem(consumer.Support.Variant),
                GetVariantStem(spawner.Variant),
                StringComparison.OrdinalIgnoreCase)
            || HasDirectSupportSpawner(consumer.Support)
            || !supportSpawnersBySpecies.TryGetValue(spawner.Species, out var supportSpawners))
        {
            return false;
        }

        var matchingLineageSpawners = supportSpawners
            .Where(candidate => string.Equals(
                GetVariantStem(consumer.Support.Variant),
                GetVariantStem(candidate.Variant),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var preferredVariant = matchingLineageSpawners.Any(candidate =>
            string.Equals(GetTerminalMode(candidate.Variant), "rus2", StringComparison.OrdinalIgnoreCase))
                ? "rus2"
                : matchingLineageSpawners.Any(candidate =>
                    string.Equals(GetTerminalMode(candidate.Variant), "rus", StringComparison.OrdinalIgnoreCase))
                        ? "rus"
                        : null;
        return preferredVariant is not null
            && string.Equals(
                GetTerminalMode(spawner.Variant),
                preferredVariant,
                StringComparison.OrdinalIgnoreCase);
    }

    private bool HasDirectSupportSpawner(BossSpawnerIdentity supportReference)
    {
        return supportSpawnersBySpecies.TryGetValue(supportReference.Species, out var spawners)
            && spawners.Any(spawner =>
                TryGetDirectSupportMatchRank(supportReference, spawner, out _));
    }

    private static bool TryGetDirectSupportMatchRank(
        BossSpawnerIdentity supportReference,
        BossSpawnerIdentity spawner,
        out int rank)
    {
        rank = int.MaxValue;
        if (supportReference.Role != BossSpawnerRole.Support
            || spawner.Role != BossSpawnerRole.Support
            || !string.Equals(supportReference.Species, spawner.Species, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(supportReference.Variant, spawner.Variant, StringComparison.OrdinalIgnoreCase))
        {
            rank = 1;
            return true;
        }

        if (string.Equals(GetTerminalMode(supportReference.Variant), "sim", StringComparison.OrdinalIgnoreCase)
            && string.Equals(GetTerminalMode(spawner.Variant), "sim1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                GetVariantStem(supportReference.Variant),
                GetVariantStem(spawner.Variant),
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 1;
            return true;
        }

        return false;
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

    private static string CreateLineageKey(BossSpawnerIdentity identity)
    {
        return $"{identity.Species}|{GetVariantStem(identity.Variant)}";
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
        ZaBossBattleConsumerRecord Record,
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

    private enum BossSpawnerRole
    {
        Main,
        Support,
    }
}
