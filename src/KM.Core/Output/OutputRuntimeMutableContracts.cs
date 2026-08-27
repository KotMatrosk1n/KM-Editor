// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;

namespace KM.Core.Output;

public enum OutputRuntimeMutableKind
{
    GameplaySettingsJournalV1 = 1,
    BooleanToggleListV1 = 2,
}

/// <summary>
/// Identifies a KM-owned file whose valid contents may advance outside the editor.
/// The minimum generation binds later observations to the last editor-reviewed state.
/// </summary>
public sealed record OutputRuntimeMutableDescriptor
{
    [JsonConstructor]
    public OutputRuntimeMutableDescriptor(
        OutputRuntimeMutableKind kind,
        ulong titleId,
        ulong? minimumGeneration,
        string? semanticIdentity = null,
        string? previousSemanticIdentity = null)
    {
        Kind = SemanticContractGuards.DefinedEnum(kind, nameof(kind));
        if (titleId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(titleId));
        }

        TitleId = titleId;
        MinimumGeneration = minimumGeneration;
        SemanticIdentity = kind switch
        {
            OutputRuntimeMutableKind.GameplaySettingsJournalV1
                when semanticIdentity is null
                     && previousSemanticIdentity is null => null,
            OutputRuntimeMutableKind.BooleanToggleListV1
                when minimumGeneration is null
                     && IsSha256(semanticIdentity)
                     && (previousSemanticIdentity is null || IsSha256(previousSemanticIdentity)) => semanticIdentity,
            _ => throw new ArgumentException(
                "The runtime-mutable descriptor has invalid generation or semantic identity metadata."),
        };
        PreviousSemanticIdentity = previousSemanticIdentity;
    }

    public OutputRuntimeMutableKind Kind { get; }

    public ulong TitleId { get; }

    public ulong? MinimumGeneration { get; }

    /// <summary>
    /// Exact semantic inventory identity for a mutable text document. Boolean
    /// values may change, while names, syntax, title, and path remain fixed.
    /// </summary>
    public string? SemanticIdentity { get; }

    /// <summary>
    /// A reviewed predecessor inventory used only to authorize a semantic
    /// transition or deletion. Ownership records retain only the current identity.
    /// </summary>
    public string? PreviousSemanticIdentity { get; }

    internal static OutputRuntimeMutableDescriptor ValidateBootstrap(
        RelativeOutputPath path,
        GameFamily gameFamily,
        ulong titleId,
        ReadOnlySpan<byte> postimage)
    {
        var descriptor = new OutputRuntimeMutableDescriptor(
            OutputRuntimeMutableKind.GameplaySettingsJournalV1,
            titleId,
            minimumGeneration: 1);
        descriptor.ValidateIdentity(path, gameFamily);
        var bytes = postimage.ToArray();
        var inspection = GameplaySettingsJournal.Inspect(
            bytes,
            ToSettingsFamily(gameFamily),
            titleId);
        if (inspection.Disposition != GameplaySettingsJournalDisposition.Ready
            || inspection.ActiveSlotIndex != 0
            || inspection.ActiveSnapshot is null
            || inspection.ActiveSnapshot.Generation != 1
            || inspection.SlotB.Classification != GameplaySettingsSlotClassification.Empty)
        {
            throw new ArgumentException(
                "A runtime-mutable bootstrap must be the supported one-slot settings image.",
                nameof(postimage));
        }

        var canonical = GameplaySettingsJournal.CreateBootstrap(
            inspection.ActiveSnapshot.Family,
            titleId,
            inspection.ActiveSnapshot.WriterVersion,
            inspection.ActiveSnapshot.Presence);
        if (!postimage.SequenceEqual(canonical))
        {
            throw new ArgumentException(
                "A runtime-mutable bootstrap must use the canonical byte representation.",
                nameof(postimage));
        }

        return descriptor;
    }

    internal static OutputRuntimeMutableDescriptor ValidateTransition(
        RelativeOutputPath path,
        GameFamily gameFamily,
        ulong titleId,
        ReadOnlySpan<byte> preimage,
        ReadOnlySpan<byte> postimage)
    {
        var family = ToSettingsFamily(gameFamily);
        var before = GameplaySettingsJournal.Inspect(preimage.ToArray(), family, titleId);
        var after = GameplaySettingsJournal.Inspect(postimage.ToArray(), family, titleId);
        if (!before.WritesAllowed
            || before.ActiveSlotIndex is null
            || before.ActiveSnapshot is null
            || !after.WritesAllowed
            || after.ActiveSlotIndex is null
            || after.ActiveSnapshot is null
            || after.ActiveSlotIndex == before.ActiveSlotIndex
            || after.ActiveSnapshot.Generation != unchecked(before.ActiveSnapshot.Generation + 1))
        {
            throw new ArgumentException(
                "A runtime-mutable update must be one supported inactive-slot generation transition.",
                nameof(postimage));
        }

        var preservedSlotOffset = before.ActiveSlotIndex.Value * GameplaySettingsJournal.SlotSize;
        if (!postimage.Slice(preservedSlotOffset, GameplaySettingsJournal.SlotSize)
            .SequenceEqual(preimage.Slice(preservedSlotOffset, GameplaySettingsJournal.SlotSize)))
        {
            throw new ArgumentException(
                "A runtime-mutable update must preserve the reviewed active slot exactly.",
                nameof(postimage));
        }

        var descriptor = new OutputRuntimeMutableDescriptor(
            OutputRuntimeMutableKind.GameplaySettingsJournalV1,
            titleId,
            after.ActiveSnapshot.Generation);
        descriptor.ValidateIdentity(path, gameFamily);
        return descriptor;
    }

    internal static OutputRuntimeMutableDescriptor ValidateToggleBootstrap(
        RelativeOutputPath path,
        GameFamily gameFamily,
        ulong titleId,
        ReadOnlySpan<byte> postimage)
    {
        var identity = AtmosphereCheatToggleDocument.ComputeInventoryIdentity(postimage);
        var descriptor = new OutputRuntimeMutableDescriptor(
            OutputRuntimeMutableKind.BooleanToggleListV1,
            titleId,
            minimumGeneration: null,
            identity,
            previousSemanticIdentity: null);
        descriptor.ValidateIdentity(path, gameFamily);
        return descriptor;
    }

    internal static OutputRuntimeMutableDescriptor ValidateToggleTransition(
        RelativeOutputPath path,
        GameFamily gameFamily,
        ulong titleId,
        ReadOnlySpan<byte> preimage,
        ReadOnlySpan<byte> postimage)
    {
        var beforeIdentity = AtmosphereCheatToggleDocument.ComputeInventoryIdentity(preimage);
        var afterIdentity = AtmosphereCheatToggleDocument.ComputeInventoryIdentity(postimage);
        if (preimage.SequenceEqual(postimage))
        {
            throw new ArgumentException(
                "A runtime-mutable cheat selection update must change the reviewed bytes.",
                nameof(postimage));
        }

        var descriptor = new OutputRuntimeMutableDescriptor(
            OutputRuntimeMutableKind.BooleanToggleListV1,
            titleId,
            minimumGeneration: null,
            afterIdentity,
            beforeIdentity);
        descriptor.ValidateIdentity(path, gameFamily);
        return descriptor;
    }

    internal static OutputRuntimeMutableDescriptor ValidateExplicitDeletion(
        RelativeOutputPath path,
        GameFamily gameFamily,
        ulong titleId,
        ReadOnlySpan<byte> preimage)
    {
        var family = ToSettingsFamily(gameFamily);
        var bytes = preimage.ToArray();
        if (!GameplaySettingsJournal.CanDeleteOwned(bytes, family, titleId))
        {
            throw new ArgumentException(
                "A runtime-mutable delete requires strict current-title ownership proof.",
                nameof(preimage));
        }

        var inspection = GameplaySettingsJournal.Inspect(bytes, family, titleId);
        var descriptor = new OutputRuntimeMutableDescriptor(
            OutputRuntimeMutableKind.GameplaySettingsJournalV1,
            titleId,
            inspection.ActiveSnapshot?.Generation);
        descriptor.ValidateIdentity(path, gameFamily);
        return descriptor;
    }

    internal static OutputRuntimeMutableDescriptor ValidateToggleDeletion(
        RelativeOutputPath path,
        GameFamily gameFamily,
        ulong titleId,
        ReadOnlySpan<byte> preimage)
    {
        var identity = AtmosphereCheatToggleDocument.ComputeInventoryIdentity(preimage);
        var descriptor = new OutputRuntimeMutableDescriptor(
            OutputRuntimeMutableKind.BooleanToggleListV1,
            titleId,
            minimumGeneration: null,
            identity,
            identity);
        descriptor.ValidateIdentity(path, gameFamily);
        return descriptor;
    }

    internal void ValidateIdentity(RelativeOutputPath path, GameFamily gameFamily)
    {
        ArgumentNullException.ThrowIfNull(path);
        _ = ToSettingsFamily(gameFamily);
        var expectedPath = Kind switch
        {
            OutputRuntimeMutableKind.GameplaySettingsJournalV1 => new RelativeOutputPath(
                $"config/km-editor/gameplay-settings/{TitleId:X16}/settings.bin"),
            OutputRuntimeMutableKind.BooleanToggleListV1 => new RelativeOutputPath(
                $"atmosphere/contents/{TitleId:X16}/cheats/toggles.txt"),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
        };
        if (path != expectedPath)
        {
            throw new ArgumentException(
                "A runtime-mutable descriptor does not match its exact title-scoped output path.",
                nameof(path));
        }
    }

    internal bool HasSameIdentity(OutputRuntimeMutableDescriptor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Kind == other.Kind && TitleId == other.TitleId;
    }

    internal bool CanMutateOwnedDescriptor(
        OutputRuntimeMutableDescriptor owned,
        OutputMutationKind mutationKind)
    {
        ArgumentNullException.ThrowIfNull(owned);
        if (!HasSameIdentity(owned))
        {
            return false;
        }

        return Kind switch
        {
            OutputRuntimeMutableKind.GameplaySettingsJournalV1 =>
                owned.MinimumGeneration is { } ownedGeneration
                && (MinimumGeneration is { } mutationGeneration
                    ? IsGenerationAtOrAfter(
                        mutationKind == OutputMutationKind.Write
                            ? unchecked(mutationGeneration - 1)
                            : mutationGeneration,
                        ownedGeneration)
                    : mutationKind == OutputMutationKind.Delete),
            OutputRuntimeMutableKind.BooleanToggleListV1 =>
                owned.SemanticIdentity is not null
                && string.Equals(
                    PreviousSemanticIdentity,
                    owned.SemanticIdentity,
                    StringComparison.Ordinal),
            _ => false,
        };
    }

    internal bool IsSemanticallyOwned(
        ReadOnlySpan<byte> bytes,
        GameFamily gameFamily,
        out ulong? generation)
    {
        generation = null;
        if (Kind == OutputRuntimeMutableKind.BooleanToggleListV1)
        {
            _ = ToSettingsFamily(gameFamily);
            return SemanticIdentity is not null
                && AtmosphereCheatToggleDocument.HasExactInventory(bytes, SemanticIdentity);
        }

        if (MinimumGeneration is null)
        {
            return false;
        }
        var inspection = GameplaySettingsJournal.Inspect(
            bytes.ToArray(),
            ToSettingsFamily(gameFamily),
            TitleId);
        if (inspection.Disposition is not (
                GameplaySettingsJournalDisposition.Ready
                or GameplaySettingsJournalDisposition.ReadyWithRepairableCompanion)
            || inspection.ActiveSnapshot is null)
        {
            return false;
        }

        generation = inspection.ActiveSnapshot.Generation;
        return IsGenerationAtOrAfter(generation.Value, MinimumGeneration.Value);
    }

    internal bool IsValidStateMetadata(OutputFileState state, OutputMutationKind mutationKind)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Exists)
        {
            return false;
        }

        return Kind switch
        {
            OutputRuntimeMutableKind.GameplaySettingsJournalV1 =>
                state.LengthBytes == GameplaySettingsJournal.JournalSize
                && (mutationKind != OutputMutationKind.Write || MinimumGeneration is not null),
            OutputRuntimeMutableKind.BooleanToggleListV1 =>
                state.LengthBytes is >= 1 and <= AtmosphereCheatToggleDocument.MaximumDocumentBytes
                && MinimumGeneration is null
                && SemanticIdentity is not null,
            _ => false,
        };
    }

    internal int MaximumObservedBytes => Kind switch
    {
        OutputRuntimeMutableKind.GameplaySettingsJournalV1 => GameplaySettingsJournal.JournalSize,
        OutputRuntimeMutableKind.BooleanToggleListV1 => AtmosphereCheatToggleDocument.MaximumDocumentBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    internal OutputRuntimeMutableDescriptor WithObservedGeneration(ulong? generation)
    {
        return Kind switch
        {
            OutputRuntimeMutableKind.GameplaySettingsJournalV1 when generation is not null =>
                new OutputRuntimeMutableDescriptor(Kind, TitleId, generation, SemanticIdentity),
            OutputRuntimeMutableKind.BooleanToggleListV1 when generation is null => this,
            _ => throw new ArgumentException("A runtime observation has invalid generation metadata."),
        };
    }

    internal OutputRuntimeMutableDescriptor AsOwnershipDescriptor()
    {
        return PreviousSemanticIdentity is null
            ? this
            : new OutputRuntimeMutableDescriptor(Kind, TitleId, MinimumGeneration, SemanticIdentity);
    }

    internal static bool IsGenerationAtOrAfter(ulong candidate, ulong baseline)
    {
        var distance = unchecked(candidate - baseline);
        return distance < 1UL << 63;
    }

    private static GameplaySettingsFamily ToSettingsFamily(GameFamily gameFamily)
    {
        return SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily)) switch
        {
            GameFamily.SwordShield => GameplaySettingsFamily.SwordShield,
            GameFamily.ScarletViolet => GameplaySettingsFamily.ScarletViolet,
            GameFamily.LegendsZA => GameplaySettingsFamily.LegendsZA,
            _ => throw new ArgumentOutOfRangeException(nameof(gameFamily)),
        };
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    }
}
