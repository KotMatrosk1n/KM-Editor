// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Application;

/// <summary>
/// Marker for lazily constructed semantic application modules.
/// </summary>
public interface ISemanticApplicationModule
{
}

/// <summary>
/// Declares a capability and an optional lazy module factory.
/// </summary>
public sealed class SemanticCapabilityRegistration
{
    private readonly Lazy<ISemanticApplicationModule>? module;

    private SemanticCapabilityRegistration(
        CapabilityDescriptor descriptor,
        Type? moduleContract,
        Func<ISemanticApplicationModule>? moduleFactory)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        ModuleContract = moduleContract;

        if ((moduleContract is null) != (moduleFactory is null))
        {
            throw new ArgumentException(
                "A semantic capability must declare both a module contract and factory, or neither.");
        }

        if (moduleContract is not null
            && (!typeof(ISemanticApplicationModule).IsAssignableFrom(moduleContract)
                || !moduleContract.IsInterface))
        {
            throw new ArgumentException(
                "A semantic module contract must be an application-module interface.",
                nameof(moduleContract));
        }

        module = moduleFactory is null
            ? null
            : new Lazy<ISemanticApplicationModule>(
                () => moduleFactory()
                    ?? throw new InvalidOperationException("A semantic module factory returned null."),
                LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public CapabilityDescriptor Descriptor { get; }

    public Type? ModuleContract { get; }

    public bool HasModule => module is not null;

    public bool IsModuleCreated => module?.IsValueCreated is true;

    public static SemanticCapabilityRegistration Declare(CapabilityDescriptor descriptor)
    {
        return new SemanticCapabilityRegistration(descriptor, moduleContract: null, moduleFactory: null);
    }

    public static SemanticCapabilityRegistration Create<TModule>(
        CapabilityDescriptor descriptor,
        Func<TModule> moduleFactory)
        where TModule : class, ISemanticApplicationModule
    {
        ArgumentNullException.ThrowIfNull(moduleFactory);
        if (!typeof(TModule).IsInterface)
        {
            throw new ArgumentException(
                "Semantic application modules must be registered through an interface contract.",
                nameof(TModule));
        }

        return new SemanticCapabilityRegistration(
            descriptor,
            typeof(TModule),
            () => moduleFactory());
    }

    internal TModule Resolve<TModule>()
        where TModule : class, ISemanticApplicationModule
    {
        if (ModuleContract is null || module is null)
        {
            throw new InvalidOperationException(
                $"Capability '{Descriptor.Id}' does not declare an application module.");
        }

        if (!typeof(TModule).IsAssignableFrom(ModuleContract))
        {
            throw new InvalidOperationException(
                $"Capability '{Descriptor.Id}' is not registered as {typeof(TModule).Name}.");
        }

        var resolved = module.Value;
        if (resolved is not TModule typedModule)
        {
            throw new InvalidOperationException(
                $"Capability '{Descriptor.Id}' produced an incompatible application module.");
        }

        return typedModule;
    }
}

/// <summary>
/// Immutable registry for semantic capability discovery and optional lazy module resolution.
/// </summary>
public sealed class SemanticCapabilityRegistry
{
    private const int MaximumRegistrations = 2_048;
    private readonly ImmutableArray<SemanticCapabilityRegistration> registrations;
    private readonly ImmutableDictionary<RegistryKey, ImmutableArray<SemanticCapabilityRegistration>> registrationsByKey;
    private readonly ImmutableDictionary<GameRegistryKey, SemanticCapabilityRegistration> registrationByGameKey;

    public SemanticCapabilityRegistry(IEnumerable<SemanticCapabilityRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var registrationBuilder = ImmutableArray.CreateBuilder<SemanticCapabilityRegistration>();
        foreach (var registration in registrations)
        {
            if (registration is null)
            {
                throw new ArgumentException(
                    "A semantic capability registry cannot contain null registrations.",
                    nameof(registrations));
            }

            if (registrationBuilder.Count == MaximumRegistrations)
            {
                throw new ArgumentException(
                    $"A semantic capability registry cannot contain more than {MaximumRegistrations} registrations.",
                    nameof(registrations));
            }

            registrationBuilder.Add(registration);
        }

        var ordered = registrationBuilder
            .OrderBy(registration => registration.Descriptor.Availability.GameFamily)
            .ThenBy(registration => registration.Descriptor.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        var byKey = new Dictionary<RegistryKey, List<SemanticCapabilityRegistration>>();
        var byGameKey = ImmutableDictionary.CreateBuilder<GameRegistryKey, SemanticCapabilityRegistration>();
        foreach (var registration in ordered)
        {
            var key = RegistryKey.From(registration.Descriptor);
            if (!byKey.TryGetValue(key, out var familyRegistrations))
            {
                familyRegistrations = [];
                byKey.Add(key, familyRegistrations);
            }

            familyRegistrations.Add(registration);
            foreach (var game in EnumerateSupportedGames(registration.Descriptor.Availability))
            {
                if (byGameKey.TryAdd(
                        new GameRegistryKey(game, registration.Descriptor.Id.Value),
                        registration))
                {
                    continue;
                }

                throw new ArgumentException(
                    $"Semantic capability '{registration.Descriptor.Id}' has overlapping registrations for {game}.",
                    nameof(registrations));
            }
        }

        this.registrations = ordered;
        registrationsByKey = byKey.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value.ToImmutableArray());
        registrationByGameKey = byGameKey.ToImmutable();
    }

    public ImmutableArray<CapabilityDescriptor> GetDescriptors(GameFamily gameFamily)
    {
        EnsureGameFamily(gameFamily);
        return registrations
            .Where(registration => registration.Descriptor.Availability.GameFamily == gameFamily)
            .Select(registration => registration.Descriptor)
            .ToImmutableArray();
    }

    public ImmutableArray<CapabilityDescriptor> GetDescriptors(
        GameFamily gameFamily,
        CapabilityKind kind)
    {
        EnsureGameFamily(gameFamily);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return registrations
            .Where(registration =>
                registration.Descriptor.Availability.GameFamily == gameFamily
                && registration.Descriptor.Kind == kind)
            .Select(registration => registration.Descriptor)
            .ToImmutableArray();
    }

    public ImmutableArray<CapabilityDescriptor> GetDescriptors(ProjectGame game)
    {
        EnsureProjectGame(game);
        return GetDescriptors(game.ToGameFamily())
            .Where(descriptor => SupportsGame(descriptor.Availability, game))
            .ToImmutableArray();
    }

    public ImmutableArray<CapabilityDescriptor> GetDescriptors(
        ProjectGame game,
        CapabilityKind kind)
    {
        EnsureProjectGame(game);
        return GetDescriptors(game.ToGameFamily(), kind)
            .Where(descriptor => SupportsGame(descriptor.Availability, game))
            .ToImmutableArray();
    }

    public bool TryGetDescriptor(
        GameFamily gameFamily,
        CapabilityId capabilityId,
        out CapabilityDescriptor? descriptor)
    {
        EnsureGameFamily(gameFamily);
        ArgumentNullException.ThrowIfNull(capabilityId);

        if (registrationsByKey.TryGetValue(
                new RegistryKey(gameFamily, capabilityId.Value),
                out var matchingRegistrations)
            && matchingRegistrations.Length == 1
            && matchingRegistrations[0].Descriptor.Availability.SupportedGames.IsEmpty)
        {
            descriptor = matchingRegistrations[0].Descriptor;
            return true;
        }

        descriptor = null;
        return false;
    }

    public bool TryGetDescriptor(
        ProjectGame game,
        CapabilityId capabilityId,
        out CapabilityDescriptor? descriptor)
    {
        EnsureProjectGame(game);
        ArgumentNullException.ThrowIfNull(capabilityId);
        if (registrationByGameKey.TryGetValue(
                new GameRegistryKey(game, capabilityId.Value),
                out var registration))
        {
            descriptor = registration.Descriptor;
            return true;
        }

        descriptor = null;
        return false;
    }

    public TModule Resolve<TModule>(GameFamily gameFamily, CapabilityId capabilityId)
        where TModule : class, ISemanticApplicationModule
    {
        EnsureGameFamily(gameFamily);
        ArgumentNullException.ThrowIfNull(capabilityId);

        if (!registrationsByKey.TryGetValue(
                new RegistryKey(gameFamily, capabilityId.Value),
                out var matchingRegistrations))
        {
            throw new KeyNotFoundException(
                $"Semantic capability '{capabilityId}' is not registered for {gameFamily}.");
        }

        if (matchingRegistrations.Length != 1)
        {
            throw new InvalidOperationException(
                $"Semantic capability '{capabilityId}' has title-specific registrations; "
                + "resolve it with an exact project game.");
        }

        var registration = matchingRegistrations[0];

        if (!registration.Descriptor.Availability.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Semantic capability '{capabilityId}' is unavailable for {gameFamily}; "
                + $"reason: {registration.Descriptor.Availability.ReasonCode}.");
        }

        if (!registration.Descriptor.Availability.SupportedGames.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Semantic capability '{capabilityId}' has title-specific availability; "
                + "resolve it with an exact project game.");
        }

        return registration.Resolve<TModule>();
    }

    public TModule Resolve<TModule>(ProjectGame game, CapabilityId capabilityId)
        where TModule : class, ISemanticApplicationModule
    {
        EnsureProjectGame(game);
        ArgumentNullException.ThrowIfNull(capabilityId);
        var gameFamily = game.ToGameFamily();

        if (!registrationByGameKey.TryGetValue(
                new GameRegistryKey(game, capabilityId.Value),
                out var registration))
        {
            throw new KeyNotFoundException(
                $"Semantic capability '{capabilityId}' is not registered for {game}.");
        }

        if (!registration.Descriptor.Availability.IsAvailable
            || !SupportsGame(registration.Descriptor.Availability, game))
        {
            throw new InvalidOperationException(
                $"Semantic capability '{capabilityId}' is unavailable for {game}; "
                + $"reason: {registration.Descriptor.Availability.ReasonCode ?? "game-not-supported"}.");
        }

        return registration.Resolve<TModule>();
    }

    public bool IsModuleCreated(GameFamily gameFamily, CapabilityId capabilityId)
    {
        EnsureGameFamily(gameFamily);
        ArgumentNullException.ThrowIfNull(capabilityId);
        return registrationsByKey.TryGetValue(
                new RegistryKey(gameFamily, capabilityId.Value),
                out var matchingRegistrations)
            && matchingRegistrations.Length == 1
            && matchingRegistrations[0].Descriptor.Availability.SupportedGames.IsEmpty
            && matchingRegistrations[0].IsModuleCreated;
    }

    public bool IsModuleCreated(ProjectGame game, CapabilityId capabilityId)
    {
        EnsureProjectGame(game);
        ArgumentNullException.ThrowIfNull(capabilityId);
        return registrationByGameKey.TryGetValue(
                new GameRegistryKey(game, capabilityId.Value),
                out var registration)
            && registration.IsModuleCreated;
    }

    private static void EnsureGameFamily(GameFamily gameFamily)
    {
        if (!Enum.IsDefined(gameFamily))
        {
            throw new ArgumentOutOfRangeException(nameof(gameFamily), gameFamily, null);
        }
    }

    private static void EnsureProjectGame(ProjectGame game)
    {
        if (!Enum.IsDefined(game))
        {
            throw new ArgumentOutOfRangeException(nameof(game), game, null);
        }
    }

    private static bool SupportsGame(CapabilityAvailability availability, ProjectGame game)
    {
        return availability.GameFamily.Contains(game)
            && (availability.SupportedGames.IsEmpty || availability.SupportedGames.Contains(game));
    }

    private static IEnumerable<ProjectGame> EnumerateSupportedGames(CapabilityAvailability availability)
    {
        return availability.SupportedGames.IsEmpty
            ? Enum.GetValues<ProjectGame>().Where(game => availability.GameFamily.Contains(game))
            : availability.SupportedGames;
    }

    private readonly record struct RegistryKey(GameFamily GameFamily, string CapabilityId)
    {
        public static RegistryKey From(CapabilityDescriptor descriptor)
        {
            return new RegistryKey(
                descriptor.Availability.GameFamily,
                descriptor.Id.Value);
        }
    }

    private readonly record struct GameRegistryKey(ProjectGame Game, string CapabilityId);
}
