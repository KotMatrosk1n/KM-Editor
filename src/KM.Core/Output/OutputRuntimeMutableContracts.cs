// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;

namespace KM.Core.Output;

public enum OutputRuntimeMutableKind
{
    GameplaySettingsJournalV1 = 1,
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
        ulong? minimumGeneration)
    {
        Kind = SemanticContractGuards.DefinedEnum(kind, nameof(kind));
        if (titleId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(titleId));
        }

        TitleId = titleId;
        MinimumGeneration = minimumGeneration;
    }

    public OutputRuntimeMutableKind Kind { get; }

    public ulong TitleId { get; }

    public ulong? MinimumGeneration { get; }

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

    internal void ValidateIdentity(RelativeOutputPath path, GameFamily gameFamily)
    {
        ArgumentNullException.ThrowIfNull(path);
        _ = ToSettingsFamily(gameFamily);
        var expectedPath = new RelativeOutputPath(
            $"config/km-editor/gameplay-settings/{TitleId:X16}/settings.bin");
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

    internal bool IsOwnedAtOrAfter(
        ReadOnlySpan<byte> bytes,
        GameFamily gameFamily,
        out ulong generation)
    {
        generation = 0;
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
        return IsGenerationAtOrAfter(generation, MinimumGeneration.Value);
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
}
