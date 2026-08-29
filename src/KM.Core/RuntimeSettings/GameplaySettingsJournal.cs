// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.Core.RuntimeSettings;

public enum GameplaySettingsFamily : ushort
{
    ScarletViolet = 1,
    SwordShield = 2,
    LegendsZA = 3,
}

[Flags]
public enum GameplaySettingPresence : ulong
{
    None = 0,
    ExperienceShare = 1UL << 0,
    ExperienceRate = 1UL << 1,
    LevelCap = 1UL << 2,
}

public readonly record struct GameplaySettingsWriterVersion(
    ushort Major,
    ushort Minor,
    ushort Patch);

public sealed record GameplaySettingsValues(
    bool ExperienceShareEnabled,
    uint ExperienceRateBasisPoints,
    bool LevelCapEnabled,
    byte LevelCap)
{
    public static GameplaySettingsValues Vanilla { get; } = new(
        ExperienceShareEnabled: true,
        ExperienceRateBasisPoints: 10_000,
        LevelCapEnabled: false,
        LevelCap: 100);
}

public sealed record GameplaySettingsSnapshot(
    GameplaySettingsFamily Family,
    ulong TitleId,
    ulong Generation,
    GameplaySettingsWriterVersion WriterVersion,
    GameplaySettingPresence Presence,
    GameplaySettingsValues Values);

public enum GameplaySettingsSlotClassification
{
    Empty,
    SupportedValid,
    NewerSchema,
    OwnedCorrupt,
    ForeignMisplaced,
    ForeignAmbiguous,
    Unavailable,
}

public sealed record GameplaySettingsSlotInspection(
    int SlotIndex,
    GameplaySettingsSlotClassification Classification,
    ushort? Schema,
    ulong? Generation,
    GameplaySettingsSnapshot? Snapshot,
    byte[]? RecordBytes);

public enum GameplaySettingsJournalDisposition
{
    Missing,
    AmbiguousLength,
    EmptyExisting,
    Ready,
    ReadyWithRepairableCompanion,
    ReadOnlyForeignConflict,
    ReadOnlyGenerationConflict,
    UnsupportedSchema,
    Corrupt,
}

public sealed record GameplaySettingsJournalInspection(
    GameplaySettingsJournalDisposition Disposition,
    GameplaySettingsSlotInspection SlotA,
    GameplaySettingsSlotInspection SlotB,
    int? ActiveSlotIndex,
    GameplaySettingsSnapshot? ActiveSnapshot,
    bool WritesAllowed,
    bool UsesVanillaDefaults);

public sealed record GameplaySettingsJournalUpdate(
    int SlotIndex,
    ulong Generation,
    byte[] SlotBytes,
    byte[] JournalBytes,
    GameplaySettingsSnapshot Snapshot);

public static class GameplaySettingsJournal
{
    public const int RecordSize = 0x100;
    public const int SlotSize = 0x1000;
    public const int JournalSize = SlotSize * 2;
    public const ushort SupportedSchema = 1;

    private const ushort HeaderSize = 0x50;
    private const ulong KnownPresenceMask = (ulong)(
        GameplaySettingPresence.ExperienceShare
        | GameplaySettingPresence.ExperienceRate
        | GameplaySettingPresence.LevelCap);
    private const ulong SerialHalfRange = 1UL << 63;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("KMGSET01");
    private static readonly byte[] Owner = Encoding.ASCII.GetBytes("KMEDITOR");

    public static byte[] CreateBootstrap(
        GameplaySettingsFamily family,
        ulong titleId,
        GameplaySettingsWriterVersion writerVersion,
        GameplaySettingPresence presence)
    {
        return CreateBootstrap(
            family,
            titleId,
            writerVersion,
            presence,
            GameplaySettingsValues.Vanilla);
    }

    public static byte[] CreateBootstrap(
        GameplaySettingsFamily family,
        ulong titleId,
        GameplaySettingsWriterVersion writerVersion,
        GameplaySettingPresence presence,
        GameplaySettingsValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateIdentity(family, titleId);
        var normalizedPresence = ValidatePresence(presence);
        var journal = new byte[JournalSize];
        SerializeSlot(
            family,
            titleId,
            generation: 1,
            writerVersion,
            normalizedPresence,
            CanonicalizeValues(normalizedPresence, values))
            .CopyTo(journal, 0);
        return journal;
    }

    public static GameplaySettingsJournalInspection Inspect(
        byte[]? journal,
        GameplaySettingsFamily expectedFamily,
        ulong expectedTitleId)
    {
        ValidateIdentity(expectedFamily, expectedTitleId);
        if (journal is null)
        {
            return CreateUnavailableInspection(GameplaySettingsJournalDisposition.Missing);
        }

        if (journal.Length != JournalSize)
        {
            return CreateUnavailableInspection(GameplaySettingsJournalDisposition.AmbiguousLength);
        }

        var slotA = InspectSlot(journal.AsSpan(0, SlotSize), 0, expectedFamily, expectedTitleId);
        var slotB = InspectSlot(journal.AsSpan(SlotSize, SlotSize), 1, expectedFamily, expectedTitleId);
        var slots = new[] { slotA, slotB };

        if (slots.Any(slot => slot.Classification is
                GameplaySettingsSlotClassification.ForeignMisplaced
                or GameplaySettingsSlotClassification.ForeignAmbiguous))
        {
            var readable = ResolveSupportedSlots(slotA, slotB, allowWrites: false);
            return readable.ActiveSnapshot is null
                ? new GameplaySettingsJournalInspection(
                    GameplaySettingsJournalDisposition.ReadOnlyForeignConflict,
                    slotA,
                    slotB,
                    null,
                    null,
                    WritesAllowed: false,
                    UsesVanillaDefaults: true)
                : readable with
                {
                    Disposition = GameplaySettingsJournalDisposition.ReadOnlyForeignConflict,
                    WritesAllowed = false,
                };
        }

        if (slots.Any(slot => slot.Classification == GameplaySettingsSlotClassification.NewerSchema))
        {
            return new GameplaySettingsJournalInspection(
                GameplaySettingsJournalDisposition.UnsupportedSchema,
                slotA,
                slotB,
                null,
                null,
                WritesAllowed: false,
                UsesVanillaDefaults: true);
        }

        var validCount = slots.Count(slot => slot.Classification == GameplaySettingsSlotClassification.SupportedValid);
        if (validCount == 0)
        {
            var disposition = slots.All(slot => slot.Classification == GameplaySettingsSlotClassification.Empty)
                ? GameplaySettingsJournalDisposition.EmptyExisting
                : GameplaySettingsJournalDisposition.Corrupt;
            return new GameplaySettingsJournalInspection(
                disposition,
                slotA,
                slotB,
                null,
                null,
                WritesAllowed: false,
                UsesVanillaDefaults: true);
        }

        var resolved = ResolveSupportedSlots(slotA, slotB, allowWrites: true);
        if (resolved.Disposition == GameplaySettingsJournalDisposition.ReadOnlyGenerationConflict)
        {
            return resolved;
        }

        var hasRepairableCompanion = slots.Any(slot =>
            slot.Classification == GameplaySettingsSlotClassification.OwnedCorrupt);
        return resolved with
        {
            Disposition = hasRepairableCompanion
                ? GameplaySettingsJournalDisposition.ReadyWithRepairableCompanion
                : GameplaySettingsJournalDisposition.Ready,
        };
    }

    public static GameplaySettingsJournalUpdate CreateUpdate(
        byte[] journal,
        GameplaySettingsFamily expectedFamily,
        ulong expectedTitleId,
        GameplaySettingsWriterVersion writerVersion,
        GameplaySettingPresence presence,
        GameplaySettingsValues values)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(values);
        var inspection = Inspect(journal, expectedFamily, expectedTitleId);
        if (!inspection.WritesAllowed
            || inspection.ActiveSlotIndex is null
            || inspection.ActiveSnapshot is null)
        {
            throw new InvalidOperationException("The gameplay settings journal is not writable in its current state.");
        }

        var normalizedPresence = ValidatePresence(presence);
        var normalizedValues = CanonicalizeValues(normalizedPresence, values);
        var generation = unchecked(inspection.ActiveSnapshot.Generation + 1);
        var targetSlot = inspection.ActiveSlotIndex.Value == 0 ? 1 : 0;
        var slotBytes = SerializeSlot(
            expectedFamily,
            expectedTitleId,
            generation,
            writerVersion,
            normalizedPresence,
            normalizedValues);
        var updatedJournal = journal.ToArray();
        slotBytes.CopyTo(updatedJournal, targetSlot * SlotSize);

        var verified = Inspect(updatedJournal, expectedFamily, expectedTitleId);
        if (!verified.WritesAllowed
            || verified.ActiveSlotIndex != targetSlot
            || verified.ActiveSnapshot is null
            || verified.ActiveSnapshot.Generation != generation
            || verified.ActiveSnapshot.Presence != normalizedPresence
            || verified.ActiveSnapshot.Values != normalizedValues)
        {
            throw new InvalidDataException("The gameplay settings journal update failed semantic readback validation.");
        }

        return new GameplaySettingsJournalUpdate(
            targetSlot,
            generation,
            slotBytes,
            updatedJournal,
            verified.ActiveSnapshot);
    }

    public static GameplaySettingsJournalUpdate CreatePresenceTransition(
        byte[] journal,
        GameplaySettingsFamily expectedFamily,
        ulong expectedTitleId,
        GameplaySettingsWriterVersion writerVersion,
        GameplaySettingPresence desiredPresence)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var inspection = Inspect(journal, expectedFamily, expectedTitleId);
        if (!inspection.WritesAllowed || inspection.ActiveSnapshot is null)
        {
            throw new InvalidOperationException("The gameplay settings journal cannot transition feature presence in its current state.");
        }

        var desired = ValidatePresence(desiredPresence);
        var retained = inspection.ActiveSnapshot.Presence & desired;
        var current = inspection.ActiveSnapshot.Values;
        var transitioned = new GameplaySettingsValues(
            retained.HasFlag(GameplaySettingPresence.ExperienceShare)
                ? current.ExperienceShareEnabled
                : GameplaySettingsValues.Vanilla.ExperienceShareEnabled,
            retained.HasFlag(GameplaySettingPresence.ExperienceRate)
                ? current.ExperienceRateBasisPoints
                : GameplaySettingsValues.Vanilla.ExperienceRateBasisPoints,
            retained.HasFlag(GameplaySettingPresence.LevelCap)
                ? current.LevelCapEnabled
                : GameplaySettingsValues.Vanilla.LevelCapEnabled,
            retained.HasFlag(GameplaySettingPresence.LevelCap)
                ? current.LevelCap
                : GameplaySettingsValues.Vanilla.LevelCap);
        return CreateUpdate(
            journal,
            expectedFamily,
            expectedTitleId,
            writerVersion,
            desired,
            transitioned);
    }

    public static bool CanDeleteOwned(
        byte[]? journal,
        GameplaySettingsFamily expectedFamily,
        ulong expectedTitleId)
    {
        ValidateIdentity(expectedFamily, expectedTitleId);
        if (journal is null || journal.Length != JournalSize)
        {
            return false;
        }

        var slotA = InspectSlot(journal.AsSpan(0, SlotSize), 0, expectedFamily, expectedTitleId);
        var slotB = InspectSlot(journal.AsSpan(SlotSize, SlotSize), 1, expectedFamily, expectedTitleId);
        var slots = new[] { slotA, slotB };
        return slots.Any(slot => slot.Classification is
                   GameplaySettingsSlotClassification.SupportedValid
                   or GameplaySettingsSlotClassification.NewerSchema)
            && slots.All(slot => slot.Classification is
                GameplaySettingsSlotClassification.Empty
                or GameplaySettingsSlotClassification.SupportedValid
                or GameplaySettingsSlotClassification.NewerSchema);
    }

    public static ulong PackAtomicSnapshot(
        GameplaySettingsSnapshot snapshot,
        GameplaySettingPresence profileExposure,
        GameplaySettingPresence validatedBundle)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var exposure = ValidatePresence(profileExposure);
        var bundle = ValidatePresence(validatedBundle);
        var effective = snapshot.Presence & exposure & bundle;
        var values = CanonicalizeValues(effective, snapshot.Values);

        ulong packed = values.ExperienceShareEnabled ? 1UL : 0UL;
        if (values.LevelCapEnabled)
        {
            packed |= 1UL << 1;
        }

        packed |= (ulong)values.LevelCap << 2;
        packed |= (ulong)values.ExperienceRateBasisPoints << 9;
        packed |= (ulong)effective << 41;
        packed |= (ulong)SupportedSchema << 44;
        return packed;
    }

    public static (GameplaySettingPresence Presence, GameplaySettingsValues Values) UnpackAtomicSnapshot(
        ulong packed)
    {
        if ((packed >> 48) != 0 || ((packed >> 44) & 0xFUL) != SupportedSchema)
        {
            throw new InvalidDataException("The gameplay settings callback snapshot has an unsupported envelope.");
        }

        var presence = ValidatePresence((GameplaySettingPresence)((packed >> 41) & KnownPresenceMask));
        var values = new GameplaySettingsValues(
            ExperienceShareEnabled: (packed & 1) != 0,
            ExperienceRateBasisPoints: (uint)((packed >> 9) & uint.MaxValue),
            LevelCapEnabled: (packed & (1UL << 1)) != 0,
            LevelCap: (byte)((packed >> 2) & 0x7F));
        var canonical = CanonicalizeValues(presence, values);
        if (canonical != values)
        {
            throw new InvalidDataException("The gameplay settings callback snapshot is not canonical.");
        }

        return (presence, values);
    }

    public static uint ComputeCrc32C(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0x82F63B78U & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }

    private static GameplaySettingsJournalInspection ResolveSupportedSlots(
        GameplaySettingsSlotInspection slotA,
        GameplaySettingsSlotInspection slotB,
        bool allowWrites)
    {
        var valid = new[] { slotA, slotB }
            .Where(slot => slot.Classification == GameplaySettingsSlotClassification.SupportedValid)
            .ToArray();
        if (valid.Length == 0)
        {
            return new GameplaySettingsJournalInspection(
                GameplaySettingsJournalDisposition.Corrupt,
                slotA,
                slotB,
                null,
                null,
                WritesAllowed: false,
                UsesVanillaDefaults: true);
        }

        if (valid.Length == 1)
        {
            return new GameplaySettingsJournalInspection(
                GameplaySettingsJournalDisposition.Ready,
                slotA,
                slotB,
                valid[0].SlotIndex,
                valid[0].Snapshot,
                WritesAllowed: allowWrites,
                UsesVanillaDefaults: false);
        }

        var generationA = valid[0].Generation!.Value;
        var generationB = valid[1].Generation!.Value;
        var difference = unchecked(generationA - generationB);
        if (difference == 0)
        {
            if (valid[0].RecordBytes!.AsSpan().SequenceEqual(valid[1].RecordBytes))
            {
                return new GameplaySettingsJournalInspection(
                    GameplaySettingsJournalDisposition.Ready,
                    slotA,
                    slotB,
                    valid[0].SlotIndex,
                    valid[0].Snapshot,
                    WritesAllowed: allowWrites,
                    UsesVanillaDefaults: false);
            }

            return CreateGenerationConflict(slotA, slotB, valid);
        }

        if (difference == SerialHalfRange)
        {
            return CreateGenerationConflict(slotA, slotB, valid);
        }

        var newest = difference < SerialHalfRange ? valid[0] : valid[1];
        return new GameplaySettingsJournalInspection(
            GameplaySettingsJournalDisposition.Ready,
            slotA,
            slotB,
            newest.SlotIndex,
            newest.Snapshot,
            WritesAllowed: allowWrites,
            UsesVanillaDefaults: false);
    }

    private static GameplaySettingsJournalInspection CreateGenerationConflict(
        GameplaySettingsSlotInspection slotA,
        GameplaySettingsSlotInspection slotB,
        IReadOnlyList<GameplaySettingsSlotInspection> valid)
    {
        var semanticMatch = HasSameSemanticValues(valid[0].Snapshot!, valid[1].Snapshot!);
        return new GameplaySettingsJournalInspection(
            GameplaySettingsJournalDisposition.ReadOnlyGenerationConflict,
            slotA,
            slotB,
            semanticMatch ? valid[0].SlotIndex : null,
            semanticMatch ? valid[0].Snapshot : null,
            WritesAllowed: false,
            UsesVanillaDefaults: !semanticMatch);
    }

    private static bool HasSameSemanticValues(
        GameplaySettingsSnapshot left,
        GameplaySettingsSnapshot right)
    {
        return left.Family == right.Family
            && left.TitleId == right.TitleId
            && left.Presence == right.Presence
            && left.Values == right.Values;
    }

    private static GameplaySettingsSlotInspection InspectSlot(
        ReadOnlySpan<byte> slot,
        int slotIndex,
        GameplaySettingsFamily expectedFamily,
        ulong expectedTitleId)
    {
        if (slot.IndexOfAnyExcept((byte)0) < 0)
        {
            return new GameplaySettingsSlotInspection(
                slotIndex,
                GameplaySettingsSlotClassification.Empty,
                null,
                null,
                null,
                null);
        }

        var hasOwnerEnvelope = slot[..8].SequenceEqual(Magic)
            && slot.Slice(8, 8).SequenceEqual(Owner);
        if (!hasOwnerEnvelope)
        {
            return new GameplaySettingsSlotInspection(
                slotIndex,
                GameplaySettingsSlotClassification.ForeignAmbiguous,
                null,
                null,
                null,
                null);
        }

        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(slot[0x10..]);
        var recordSize = BinaryPrimitives.ReadUInt16LittleEndian(slot[0x12..]);
        var schema = BinaryPrimitives.ReadUInt16LittleEndian(slot[0x14..]);
        var storedFamily = BinaryPrimitives.ReadUInt16LittleEndian(slot[0x16..]);
        var titleId = BinaryPrimitives.ReadUInt64LittleEndian(slot[0x18..]);
        var generation = BinaryPrimitives.ReadUInt64LittleEndian(slot[0x20..]);
        var identityMatches = storedFamily == (ushort)expectedFamily && titleId == expectedTitleId;
        var stableStructureValid = headerSize >= HeaderSize
            && recordSize is >= HeaderSize and <= SlotSize
            && headerSize <= recordSize
            && slot[recordSize..].IndexOfAnyExcept((byte)0) < 0
            && HasValidRecordCrc(slot[..recordSize]);
        if (!stableStructureValid)
        {
            return new GameplaySettingsSlotInspection(
                slotIndex,
                identityMatches
                    ? GameplaySettingsSlotClassification.OwnedCorrupt
                    : GameplaySettingsSlotClassification.ForeignAmbiguous,
                schema,
                generation,
                null,
                null);
        }

        if (!identityMatches)
        {
            return new GameplaySettingsSlotInspection(
                slotIndex,
                GameplaySettingsSlotClassification.ForeignMisplaced,
                schema,
                generation,
                null,
                slot[..recordSize].ToArray());
        }

        if (schema > SupportedSchema)
        {
            return new GameplaySettingsSlotInspection(
                slotIndex,
                GameplaySettingsSlotClassification.NewerSchema,
                schema,
                generation,
                null,
                slot[..recordSize].ToArray());
        }

        if (schema != SupportedSchema
            || headerSize != HeaderSize
            || recordSize != RecordSize
            || !TryParseSchemaOne(slot, expectedFamily, expectedTitleId, out var snapshot))
        {
            return new GameplaySettingsSlotInspection(
                slotIndex,
                GameplaySettingsSlotClassification.OwnedCorrupt,
                schema,
                generation,
                null,
                slot[..recordSize].ToArray());
        }

        return new GameplaySettingsSlotInspection(
            slotIndex,
            GameplaySettingsSlotClassification.SupportedValid,
            schema,
            generation,
            snapshot,
            slot[..recordSize].ToArray());
    }

    private static bool TryParseSchemaOne(
        ReadOnlySpan<byte> slot,
        GameplaySettingsFamily expectedFamily,
        ulong expectedTitleId,
        out GameplaySettingsSnapshot? snapshot)
    {
        snapshot = null;
        var familyFlagsSchema = BinaryPrimitives.ReadUInt16LittleEndian(slot[0x2E..]);
        var rawPresence = BinaryPrimitives.ReadUInt64LittleEndian(slot[0x30..]);
        var rate = BinaryPrimitives.ReadUInt32LittleEndian(slot[0x38..]);
        var share = slot[0x3C];
        var capEnabled = slot[0x3D];
        var cap = slot[0x3E];
        var familyFlags = BinaryPrimitives.ReadUInt64LittleEndian(slot[0x40..]);
        if (familyFlagsSchema != 0
            || familyFlags != 0
            || (rawPresence & ~KnownPresenceMask) != 0
            || share > 1
            || capEnabled > 1
            || cap is < 1 or > 100
            || slot[0x3F] != 0
            || slot.Slice(0x4C, 4).IndexOfAnyExcept((byte)0) >= 0
            || slot.Slice(0x50, RecordSize - 0x50).IndexOfAnyExcept((byte)0) >= 0)
        {
            return false;
        }

        var presence = (GameplaySettingPresence)rawPresence;
        var values = new GameplaySettingsValues(share != 0, rate, capEnabled != 0, cap);
        if (CanonicalizeValues(presence, values) != values)
        {
            return false;
        }

        snapshot = new GameplaySettingsSnapshot(
            expectedFamily,
            expectedTitleId,
            BinaryPrimitives.ReadUInt64LittleEndian(slot[0x20..]),
            new GameplaySettingsWriterVersion(
                BinaryPrimitives.ReadUInt16LittleEndian(slot[0x28..]),
                BinaryPrimitives.ReadUInt16LittleEndian(slot[0x2A..]),
                BinaryPrimitives.ReadUInt16LittleEndian(slot[0x2C..])),
            presence,
            values);
        return true;
    }

    private static byte[] SerializeSlot(
        GameplaySettingsFamily family,
        ulong titleId,
        ulong generation,
        GameplaySettingsWriterVersion writerVersion,
        GameplaySettingPresence presence,
        GameplaySettingsValues values)
    {
        ValidateIdentity(family, titleId);
        var normalizedPresence = ValidatePresence(presence);
        var normalizedValues = CanonicalizeValues(normalizedPresence, values);
        var slot = new byte[SlotSize];
        Magic.CopyTo(slot, 0x00);
        Owner.CopyTo(slot, 0x08);
        BinaryPrimitives.WriteUInt16LittleEndian(slot.AsSpan(0x10), HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(slot.AsSpan(0x12), RecordSize);
        BinaryPrimitives.WriteUInt16LittleEndian(slot.AsSpan(0x14), SupportedSchema);
        BinaryPrimitives.WriteUInt16LittleEndian(slot.AsSpan(0x16), (ushort)family);
        BinaryPrimitives.WriteUInt64LittleEndian(slot.AsSpan(0x18), titleId);
        BinaryPrimitives.WriteUInt64LittleEndian(slot.AsSpan(0x20), generation);
        BinaryPrimitives.WriteUInt16LittleEndian(slot.AsSpan(0x28), writerVersion.Major);
        BinaryPrimitives.WriteUInt16LittleEndian(slot.AsSpan(0x2A), writerVersion.Minor);
        BinaryPrimitives.WriteUInt16LittleEndian(slot.AsSpan(0x2C), writerVersion.Patch);
        BinaryPrimitives.WriteUInt64LittleEndian(slot.AsSpan(0x30), (ulong)normalizedPresence);
        BinaryPrimitives.WriteUInt32LittleEndian(slot.AsSpan(0x38), normalizedValues.ExperienceRateBasisPoints);
        slot[0x3C] = normalizedValues.ExperienceShareEnabled ? (byte)1 : (byte)0;
        slot[0x3D] = normalizedValues.LevelCapEnabled ? (byte)1 : (byte)0;
        slot[0x3E] = normalizedValues.LevelCap;
        var crc = ComputeCrc32C(slot.AsSpan(0, RecordSize));
        BinaryPrimitives.WriteUInt32LittleEndian(slot.AsSpan(0x48), crc);
        return slot;
    }

    private static bool HasValidRecordCrc(ReadOnlySpan<byte> record)
    {
        if (record.Length < HeaderSize)
        {
            return false;
        }

        var stored = BinaryPrimitives.ReadUInt32LittleEndian(record[0x48..]);
        var copy = record.ToArray();
        copy.AsSpan(0x48, sizeof(uint)).Clear();
        return ComputeCrc32C(copy) == stored;
    }

    private static GameplaySettingPresence ValidatePresence(GameplaySettingPresence presence)
    {
        if (((ulong)presence & ~KnownPresenceMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(presence));
        }

        return presence;
    }

    private static GameplaySettingsValues CanonicalizeValues(
        GameplaySettingPresence presence,
        GameplaySettingsValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.LevelCap is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "The level cap must be between 1 and 100.");
        }

        return new GameplaySettingsValues(
            presence.HasFlag(GameplaySettingPresence.ExperienceShare)
                ? values.ExperienceShareEnabled
                : GameplaySettingsValues.Vanilla.ExperienceShareEnabled,
            presence.HasFlag(GameplaySettingPresence.ExperienceRate)
                ? values.ExperienceRateBasisPoints
                : GameplaySettingsValues.Vanilla.ExperienceRateBasisPoints,
            presence.HasFlag(GameplaySettingPresence.LevelCap)
                ? values.LevelCapEnabled
                : GameplaySettingsValues.Vanilla.LevelCapEnabled,
            presence.HasFlag(GameplaySettingPresence.LevelCap) && values.LevelCapEnabled
                ? values.LevelCap
                : GameplaySettingsValues.Vanilla.LevelCap);
    }

    private static void ValidateIdentity(GameplaySettingsFamily family, ulong titleId)
    {
        if (!Enum.IsDefined(family))
        {
            throw new ArgumentOutOfRangeException(nameof(family));
        }

        if (titleId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(titleId));
        }
    }

    private static GameplaySettingsJournalInspection CreateUnavailableInspection(
        GameplaySettingsJournalDisposition disposition)
    {
        var slotA = new GameplaySettingsSlotInspection(
            0,
            GameplaySettingsSlotClassification.Unavailable,
            null,
            null,
            null,
            null);
        var slotB = slotA with { SlotIndex = 1 };
        return new GameplaySettingsJournalInspection(
            disposition,
            slotA,
            slotB,
            null,
            null,
            WritesAllowed: false,
            UsesVanillaDefaults: true);
    }
}
