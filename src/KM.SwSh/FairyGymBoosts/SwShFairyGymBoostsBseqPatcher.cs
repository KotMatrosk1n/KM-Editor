// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;

namespace KM.SwSh.FairyGymBoosts;

internal static class SwShFairyGymBoostsBseqPatcher
{
    public const int FileLength = 0x4A10;
    public const int PayloadOffset = 0x1550;
    public const int OwnedSlotCount = 2;
    public const int SlotSize = 8;
    public const int OwnedByteCount = OwnedSlotCount * SlotSize;
    public const string PayloadOffsetHex = "0x00001550";
    public const string OwnedRangeHex = "0x00001550-0x0000155F";

    private const int PayloadSlotCount = 3;

    public static IReadOnlyList<SwShFairyGymBoostAnswerSlot> ReadAnswerSlots(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        _ = AnalyzeLayout(data);
        return ReadOwnedSlots(data);
    }

    public static void ValidateVanillaBase(
        byte[] data,
        IReadOnlyList<SwShFairyGymBoostAnswerSlot> expectedSlots)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateExpectedSlots(expectedSlots);
        _ = AnalyzeLayout(data);

        var actualSlots = ReadOwnedSlots(data);
        if (!actualSlots.SequenceEqual(expectedSlots))
        {
            throw new InvalidDataException(
                "Fairy Gym boost base source does not contain the canonical vanilla answer slots.");
        }
    }

    public static void ValidateEffective(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _ = AnalyzeLayout(data);

        if (ReadOwnedSlots(data).Any(slot => !IsSupportedSlot(slot.EffectId, slot.ResultValue)))
        {
            throw new InvalidDataException(
                "Fairy Gym boost source has unsupported values in an owned answer slot.");
        }
    }

    public static void EnsureCompatible(byte[] baseData, byte[] effectiveData)
    {
        ArgumentNullException.ThrowIfNull(baseData);
        ArgumentNullException.ThrowIfNull(effectiveData);

        _ = AnalyzeLayout(baseData);
        _ = AnalyzeLayout(effectiveData);
        if (baseData.Length != effectiveData.Length)
        {
            throw new InvalidDataException(
                "Fairy Gym boost base and effective sources do not have compatible layouts.");
        }
    }

    public static byte[] ApplySelections(
        byte[] data,
        IReadOnlyList<SwShFairyGymBoostAnswerPatch> patches)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(patches);

        ValidateEffective(data);
        ValidatePatchSet(patches);
        var output = data.ToArray();
        foreach (var patch in patches)
        {
            WriteSlot(output, patch);
        }

        VerifyRequestedChanges(data, output, patches);
        return output;
    }

    public static byte[] ApplySelections(
        byte[] effectiveData,
        byte[] baseData,
        IReadOnlyList<SwShFairyGymBoostAnswerSlot> expectedVanillaSlots,
        IReadOnlyList<SwShFairyGymBoostAnswerPatch> patches)
    {
        ArgumentNullException.ThrowIfNull(effectiveData);
        ArgumentNullException.ThrowIfNull(baseData);
        ArgumentNullException.ThrowIfNull(expectedVanillaSlots);
        ArgumentNullException.ThrowIfNull(patches);

        ValidateVanillaBase(baseData, expectedVanillaSlots);
        ValidateEffective(effectiveData);
        EnsureCompatible(baseData, effectiveData);
        ValidatePatchSet(patches);

        var output = effectiveData.ToArray();
        foreach (var patch in patches)
        {
            var expectedVanilla = expectedVanillaSlots[patch.AnswerChoice - 1];
            if (patch.EffectId == expectedVanilla.EffectId
                && patch.ResultValue == expectedVanilla.ResultValue)
            {
                baseData.AsSpan(GetSlotOffset(patch.AnswerChoice), SlotSize)
                    .CopyTo(output.AsSpan(GetSlotOffset(patch.AnswerChoice), SlotSize));
            }
            else
            {
                WriteSlot(output, patch);
            }
        }

        VerifyRequestedChanges(effectiveData, output, patches);
        foreach (var patch in patches)
        {
            var expectedVanilla = expectedVanillaSlots[patch.AnswerChoice - 1];
            if (patch.EffectId == expectedVanilla.EffectId
                && patch.ResultValue == expectedVanilla.ResultValue
                && !output.AsSpan(GetSlotOffset(patch.AnswerChoice), SlotSize)
                    .SequenceEqual(baseData.AsSpan(GetSlotOffset(patch.AnswerChoice), SlotSize)))
            {
                throw new InvalidDataException(
                    "Fairy Gym boost restore did not copy the reviewed vanilla answer bytes.");
            }
        }

        return output;
    }

    public static void VerifyRequestedChanges(
        byte[] source,
        byte[] output,
        IReadOnlyList<SwShFairyGymBoostAnswerPatch> patches)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(patches);

        ValidateEffective(source);
        ValidateEffective(output);
        ValidatePatchSet(patches);
        if (source.Length != output.Length)
        {
            throw new InvalidDataException("Fairy Gym boost output changed the BSEQ file length.");
        }

        var selectedChoices = patches
            .Select(patch => patch.AnswerChoice)
            .ToHashSet();
        for (var offset = 0; offset < source.Length; offset++)
        {
            if (source[offset] == output[offset])
            {
                continue;
            }

            var ownedChoice = GetOwnedChoice(offset);
            if (ownedChoice is null || !selectedChoices.Contains(ownedChoice.Value))
            {
                throw new InvalidDataException(
                    "Fairy Gym boost output changed bytes outside the requested owned answer slots.");
            }
        }

        foreach (var patch in patches)
        {
            var slot = ReadSlot(output, patch.AnswerChoice);
            if (slot.EffectId != patch.EffectId || slot.ResultValue != patch.ResultValue)
            {
                throw new InvalidDataException(
                    $"Fairy Gym boost answer choice {patch.AnswerChoice} did not round-trip with the requested outcome.");
            }
        }
    }

    private static SwShBseqCommand AnalyzeLayout(byte[] data)
    {
        if (data.Length != FileLength)
        {
            throw new InvalidDataException(
                $"Fairy Gym boost BSEQ has length 0x{data.Length:X}; expected 0x{FileLength:X}.");
        }

        var file = SwShBseqFile.Parse(data);
        var command = file.GetSingleCommand(
            SwShBseqKnownCommands.SpecialQuizResult,
            SwShBseqKnownCommands.SpecialQuizResultName);
        if (command.PayloadLength != SwShBseqKnownCommands.SpecialQuizResultPayloadLength)
        {
            throw new InvalidDataException("Fairy Gym boost command payload has an unexpected size.");
        }

        if (command.PayloadOffset != PayloadOffset)
        {
            throw new InvalidDataException(
                $"Fairy Gym boost command payload is at 0x{command.PayloadOffset:X}; expected 0x{PayloadOffset:X}.");
        }

        if (command.PayloadOffset + (PayloadSlotCount * SlotSize) > data.Length)
        {
            throw new InvalidDataException("Fairy Gym boost command payload is truncated.");
        }

        return command;
    }

    private static IReadOnlyList<SwShFairyGymBoostAnswerSlot> ReadOwnedSlots(byte[] data)
    {
        return Enumerable.Range(1, OwnedSlotCount)
            .Select(answerChoice => ReadSlot(data, answerChoice))
            .ToArray();
    }

    private static SwShFairyGymBoostAnswerSlot ReadSlot(byte[] data, int answerChoice)
    {
        var slotOffset = GetSlotOffset(answerChoice);
        return new SwShFairyGymBoostAnswerSlot(
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(slotOffset, sizeof(int))),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(slotOffset + sizeof(int), sizeof(int))));
    }

    private static void WriteSlot(byte[] output, SwShFairyGymBoostAnswerPatch patch)
    {
        var slotOffset = GetSlotOffset(patch.AnswerChoice);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(slotOffset, sizeof(int)), patch.EffectId);
        BinaryPrimitives.WriteInt32LittleEndian(
            output.AsSpan(slotOffset + sizeof(int), sizeof(int)),
            patch.ResultValue);
    }

    private static int GetSlotOffset(int answerChoice)
    {
        return PayloadOffset + ((answerChoice - 1) * SlotSize);
    }

    private static int? GetOwnedChoice(int offset)
    {
        if (offset < PayloadOffset || offset >= PayloadOffset + OwnedByteCount)
        {
            return null;
        }

        return ((offset - PayloadOffset) / SlotSize) + 1;
    }

    private static void ValidateExpectedSlots(
        IReadOnlyList<SwShFairyGymBoostAnswerSlot> expectedSlots)
    {
        ArgumentNullException.ThrowIfNull(expectedSlots);
        if (expectedSlots.Count != OwnedSlotCount
            || expectedSlots.Any(slot => slot is null || !IsSupportedSlot(slot.EffectId, slot.ResultValue)))
        {
            throw new InvalidDataException(
                "Fairy Gym boost vanilla mapping must define exactly two supported owned answer slots.");
        }
    }

    private static void ValidatePatchSet(
        IReadOnlyList<SwShFairyGymBoostAnswerPatch> patches)
    {
        if (patches.Count == 0)
        {
            throw new InvalidDataException("Fairy Gym boost patch set must not be empty.");
        }

        var answerChoices = new HashSet<int>();
        foreach (var patch in patches)
        {
            if (patch is null)
            {
                throw new InvalidDataException("Fairy Gym boost patch is missing.");
            }

            if (patch.AnswerChoice is < 1 or > OwnedSlotCount)
            {
                throw new InvalidDataException(
                    "Fairy Gym boost patches only own answer choices 1 and 2.");
            }

            if (!answerChoices.Add(patch.AnswerChoice))
            {
                throw new InvalidDataException(
                    $"Fairy Gym boost answer choice {patch.AnswerChoice} is duplicated.");
            }

            if (!IsSupportedSlot(patch.EffectId, patch.ResultValue))
            {
                throw new InvalidDataException(
                    "Fairy Gym boost patches only support no effect, boost, or drop outcomes from the known command presets.");
            }
        }
    }

    private static bool IsSupportedSlot(int effectId, int resultValue)
    {
        return effectId switch
        {
            0 => resultValue == 0,
            >= 1 and <= 6 => resultValue is 1 or 2,
            _ => false,
        };
    }
}
