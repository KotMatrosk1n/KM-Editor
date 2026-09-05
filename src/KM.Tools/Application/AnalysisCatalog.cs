// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using KM.Api.Semantics;

namespace KM.Tools.Application;

internal static class AnalysisCatalog
{
    private static readonly JsonSerializerOptions IdentityOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly ConditionalWeakTable<BalanceLabStudyData, MetricCatalog> MetricCatalogs = new();

    public static void Validate(string? searchText, string? metric = null)
    {
        if (searchText is { Length: > 256 } || metric is { Length: > 1024 }
            || (searchText?.Any(char.IsControl) ?? false)
            || (metric?.Any(char.IsControl) ?? false))
        {
            throw new SemanticExploreValidationException(
                "The analysis catalog search is invalid.", SemanticExploreFailureKind.InvalidData);
        }
    }

    public static string[] SearchTerms(string? searchText) =>
        (searchText ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Matches(string[] terms, IEnumerable<string?> values)
    {
        return terms.Length == 0 || terms.All(term => values.Any(value =>
            value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));
    }

    public static string FactKey(BalanceLabFactDto fact)
    {
        var marker = fact.FactId.LastIndexOf(".fact.", StringComparison.Ordinal);
        return marker < 0 ? fact.FactId : fact.FactId[(marker + 6)..];
    }

    public static string MetricIdentity(BalanceLabFactDto fact) =>
        JsonSerializer.Serialize(new[] { fact.ProviderId, FactKey(fact), fact.Unit }, IdentityOptions);

    public static bool IsNumeric(BalanceLabFactDto fact) =>
        fact.Value.Kind is SemanticValueKindDto.SignedInteger or SemanticValueKindDto.UnsignedInteger
            or SemanticValueKindDto.Decimal
        && double.TryParse(fact.Value.CanonicalValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
        && (fact.Value.Kind == SemanticValueKindDto.Decimal || Math.Abs(value) <= 9007199254740991d);

    public static IReadOnlyList<BalanceLabMetricDto> Metrics(BalanceLabStudyData study, CancellationToken cancellationToken)
    {
        return MetricCatalogs.GetValue(study, value =>
        {
            var metrics = new Dictionary<string, BalanceLabMetricDto>(StringComparer.Ordinal);
            foreach (var point in value.Points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var fact in point.Facts.Where(IsNumeric))
                {
                    var identity = MetricIdentity(fact);
                    if (!seen.Add(identity)) continue;
                    metrics[identity] = metrics.TryGetValue(identity, out var current)
                        ? current with { SupportCount = current.SupportCount + 1 }
                        : new(identity, FactKey(fact), fact.Label, fact.ProviderId, 1, fact.Unit);
                }
            }

            return new MetricCatalog(metrics.Values.OrderByDescending(metric => metric.SupportCount)
                .ThenBy(metric => metric.Label, StringComparer.Ordinal).ToArray());
        }).Metrics;
    }

    private sealed record MetricCatalog(IReadOnlyList<BalanceLabMetricDto> Metrics);
}
