// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using KM.Core.Projects;

namespace KM.Core.Semantics;

public enum CapabilityKind
{
    Navigation = 1,
    Command = 2,
    SemanticSearch = 3,
    Comparison = 4,
    References = 5,
    Impact = 6,
    BulkOperation = 7,
    Analyzer = 8,
    RecipeImport = 9,
    RecipeExport = 10,
    OutputOwnership = 11,
    Recovery = 12,
}

public enum CapabilityMaturity
{
    Editable = 1,
    ReadOnly = 2,
    AnalysisOnly = 3,
    Research = 4,
    Unavailable = 5,
}

public sealed record CapabilityAvailability
{
    private const int MaximumSupportedBuilds = 256;
    private const int MaximumSupportedOutputModes = 64;

    public CapabilityAvailability(
        GameFamily gameFamily,
        CapabilityMaturity maturity,
        IEnumerable<ProjectGame>? supportedGames = null,
        IEnumerable<SourceLayerKind>? supportedSourceLayers = null,
        IEnumerable<string>? supportedBuilds = null,
        IEnumerable<string>? supportedOutputModes = null,
        string? reasonCode = null)
    {
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        Maturity = SemanticContractGuards.DefinedEnum(maturity, nameof(maturity));
        SupportedGames = ValidateGames(gameFamily, supportedGames);
        SupportedSourceLayers = ValidateSourceLayers(supportedSourceLayers);
        SupportedBuilds = ValidateStableIds(supportedBuilds, nameof(supportedBuilds));
        SupportedOutputModes = ValidateContractKeys(supportedOutputModes, nameof(supportedOutputModes));

        if (maturity == CapabilityMaturity.Unavailable && reasonCode is null)
        {
            throw new ArgumentException("An unavailable capability requires a stable reason code.", nameof(reasonCode));
        }

        ReasonCode = reasonCode is null
            ? null
            : SemanticContractGuards.StableCode(reasonCode, nameof(reasonCode));
    }

    public GameFamily GameFamily { get; }

    public CapabilityMaturity Maturity { get; }

    public bool IsAvailable => Maturity != CapabilityMaturity.Unavailable;

    /// <summary>An empty collection means every game in <see cref="GameFamily"/>.</summary>
    public ImmutableArray<ProjectGame> SupportedGames { get; }

    /// <summary>An empty collection means the capability does not consume a source layer.</summary>
    public ImmutableArray<SourceLayerKind> SupportedSourceLayers { get; }

    /// <summary>An empty collection means no build-specific restriction is declared.</summary>
    public ImmutableArray<string> SupportedBuilds { get; }

    /// <summary>An empty collection means no output-mode-specific restriction is declared.</summary>
    public ImmutableArray<string> SupportedOutputModes { get; }

    public string? ReasonCode { get; }

    private static ImmutableArray<ProjectGame> ValidateGames(
        GameFamily gameFamily,
        IEnumerable<ProjectGame>? games)
    {
        var maximumGamesInFamily = gameFamily is GameFamily.LegendsZA ? 1 : 2;
        var validated = SemanticContractGuards.DistinctImmutableItems(
            games,
            nameof(games),
            maximumGamesInFamily);

        foreach (var game in validated)
        {
            if (!gameFamily.Contains(game))
            {
                throw new ArgumentException(
                    $"The game {game} does not belong to the {gameFamily} capability family.",
                    nameof(games));
            }
        }

        return validated;
    }

    private static ImmutableArray<SourceLayerKind> ValidateSourceLayers(IEnumerable<SourceLayerKind>? sourceLayers)
    {
        var validated = SemanticContractGuards.DistinctImmutableItems(
            sourceLayers,
            nameof(sourceLayers),
            Enum.GetValues<SourceLayerKind>().Length);
        foreach (var sourceLayer in validated)
        {
            SemanticContractGuards.DefinedEnum(sourceLayer, nameof(sourceLayers));
        }

        return validated;
    }

    private static ImmutableArray<string> ValidateContractKeys(IEnumerable<string>? keys, string parameterName)
    {
        if (keys is null)
        {
            return ImmutableArray<string>.Empty;
        }

        return ValidateStrings(
            keys,
            parameterName,
            MaximumSupportedOutputModes,
            SemanticContractGuards.ContractKey);
    }

    private static ImmutableArray<string> ValidateStableIds(IEnumerable<string>? ids, string parameterName)
    {
        if (ids is null)
        {
            return ImmutableArray<string>.Empty;
        }

        return ValidateStrings(
            ids,
            parameterName,
            MaximumSupportedBuilds,
            SemanticContractGuards.StableId);
    }

    private static ImmutableArray<string> ValidateStrings(
        IEnumerable<string> values,
        string parameterName,
        int maximumCount,
        Func<string, string, string> validate)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var validated = validate(value, parameterName);
            if (!seen.Add(validated))
            {
                throw new ArgumentException("A capability constraint cannot contain duplicate values.", parameterName);
            }

            if (builder.Count == maximumCount)
            {
                throw new ArgumentException(
                    $"A capability constraint cannot contain more than {maximumCount} values.",
                    parameterName);
            }

            builder.Add(validated);
        }

        return builder.ToImmutable();
    }
}

public sealed record CapabilityDescriptor
{
    public CapabilityDescriptor(
        CapabilityId id,
        CapabilityKind kind,
        CapabilityAvailability availability,
        SemanticDomainKey? domain = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Kind = SemanticContractGuards.DefinedEnum(kind, nameof(kind));
        Availability = availability ?? throw new ArgumentNullException(nameof(availability));
        Domain = domain;
    }

    public CapabilityId Id { get; }

    public CapabilityKind Kind { get; }

    public CapabilityAvailability Availability { get; }

    public SemanticDomainKey? Domain { get; }
}
