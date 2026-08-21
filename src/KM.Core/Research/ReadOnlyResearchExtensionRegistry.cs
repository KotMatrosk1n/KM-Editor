// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;

namespace KM.Core.Research;

public enum ReadOnlyResearchExtensionKind
{
    HostRegistered,
    DeclarativeData,
}

public enum ReadOnlyResearchExtensionCoverage
{
    Complete,
    Partial,
    Unavailable,
}

public sealed record ReadOnlyResearchExtensionDescriptor(
    string ExtensionId,
    ReadOnlyResearchExtensionKind Kind,
    int SchemaVersion,
    ImmutableArray<string> Features,
    ImmutableArray<string> GameFamilies,
    ReadOnlyResearchExtensionCoverage Coverage,
    string? ReasonCode);

/// <summary>
/// An immutable registry for source-defined, read-only research projections.
/// </summary>
/// <remarks>
/// The registry intentionally accepts descriptors only. It does not discover assemblies, load
/// scripts or native helpers, grant filesystem access, or expose mutation callbacks.
/// </remarks>
public sealed class ReadOnlyResearchExtensionRegistry
{
    public const int MaximumRegistrations = 64;
    private static readonly HashSet<string> AllowedFeatures =
        ["sourceComparison", "byteWindows", "semanticProjection", "ownershipEvidence"];
    private static readonly HashSet<string> AllowedGameFamilies =
        ["swordShield", "scarletViolet", "legendsZA"];
    private readonly ImmutableArray<ReadOnlyResearchExtensionDescriptor> descriptors;

    public ReadOnlyResearchExtensionRegistry(
        IEnumerable<ReadOnlyResearchExtensionDescriptor>? descriptors = null)
    {
        var supplied = (descriptors ?? BuiltInDescriptors()).Take(MaximumRegistrations + 1).ToArray();
        if (supplied.Length > MaximumRegistrations)
        {
            throw new ArgumentException(
                "Too many read-only research extensions were registered.",
                nameof(descriptors));
        }

        var normalized = supplied.Select(Validate)
            .OrderBy(descriptor => descriptor.ExtensionId, StringComparer.Ordinal)
            .ToImmutableArray();

        if (normalized.Select(descriptor => descriptor.ExtensionId).Distinct(StringComparer.Ordinal).Count()
            != normalized.Length)
        {
            throw new ArgumentException(
                "A read-only research extension id may be registered only once.",
                nameof(descriptors));
        }

        this.descriptors = normalized;
    }

    public ImmutableArray<ReadOnlyResearchExtensionDescriptor> Descriptors => descriptors;

    private static IEnumerable<ReadOnlyResearchExtensionDescriptor> BuiltInDescriptors()
    {
        yield return new ReadOnlyResearchExtensionDescriptor(
            "core.opaque-file-comparison.v1",
            ReadOnlyResearchExtensionKind.HostRegistered,
            1,
            ["sourceComparison", "byteWindows"],
            ["swordShield", "scarletViolet", "legendsZA"],
            ReadOnlyResearchExtensionCoverage.Partial,
            ReasonCode: "host-registered-descriptors-only");
    }

    private static ReadOnlyResearchExtensionDescriptor Validate(
        ReadOnlyResearchExtensionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateExtensionId(descriptor.ExtensionId);
        if (!Enum.IsDefined(descriptor.Kind)
            || !Enum.IsDefined(descriptor.Coverage)
            || descriptor.SchemaVersion <= 0)
        {
            throw new ArgumentException("A read-only research extension descriptor is invalid.");
        }

        if (descriptor.Features.IsDefault || descriptor.GameFamilies.IsDefault
            || descriptor.Features.Length is < 1 or > 16
            || descriptor.GameFamilies.Length is < 1 or > 3
            || descriptor.Features.Distinct(StringComparer.Ordinal).Count()
                != descriptor.Features.Length
            || descriptor.GameFamilies.Distinct(StringComparer.Ordinal).Count()
                != descriptor.GameFamilies.Length)
        {
            throw new ArgumentException("A read-only research extension descriptor has invalid coverage.");
        }

        foreach (var feature in descriptor.Features)
        {
            if (!AllowedFeatures.Contains(feature))
            {
                throw new ArgumentException("A read-only research extension feature is unsupported.");
            }
        }

        foreach (var gameFamily in descriptor.GameFamilies)
        {
            if (!AllowedGameFamilies.Contains(gameFamily))
            {
                throw new ArgumentException("A read-only research extension game family is unsupported.");
            }
        }

        if (descriptor.ReasonCode is not null)
        {
            ValidateContractKey(descriptor.ReasonCode, nameof(descriptor.ReasonCode));
        }

        if (descriptor.Coverage == ReadOnlyResearchExtensionCoverage.Complete
            != (descriptor.ReasonCode is null))
        {
            throw new ArgumentException(
                "Complete extension coverage must not have a reason and incomplete coverage must have one.");
        }

        return descriptor with
        {
            Features = descriptor.Features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            GameFamilies = descriptor.GameFamilies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
        };
    }

    private static void ValidateExtensionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !char.IsAsciiLetterOrDigit(value[0])
            || !char.IsAsciiLetterOrDigit(value[^1])
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_')))
        {
            throw new ArgumentException(
                "A read-only research extension identifier is invalid.",
                nameof(value));
        }
    }

    private static void ValidateContractKey(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128
            || !IsLowerAlphaNumeric(value[0])
            || !IsLowerAlphaNumeric(value[^1])
            || value.Any(character => !(IsLowerAlphaNumeric(character)
                || character is '.' or '-' or '_')))
        {
            throw new ArgumentException("A read-only research extension reason is invalid.", name);
        }
    }

    private static bool IsLowerAlphaNumeric(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';
}
