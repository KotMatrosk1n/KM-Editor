// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.FairyGymBoosts;
using Xunit;

namespace KM.SwSh.Tests.FairyGymBoosts;

public sealed class SwShFairyGymBoostsBseqPatcherTests
{
    [Fact]
    public void ApplySelectionsWritesOnlyRequestedOwnedAnswerSlots()
    {
        var data = FairyGymBoostTestFixtures.CreateBseq(
            effectOne: 6,
            resultOne: 2,
            effectTwo: 6,
            resultTwo: 1);
        var slotThree = FairyGymBoostTestFixtures.ReadSlot(data, answerChoice: 3);

        var patched = SwShFairyGymBoostsBseqPatcher.ApplySelections(
            data,
            [
                new SwShFairyGymBoostAnswerPatch(1, 0, 0),
                new SwShFairyGymBoostAnswerPatch(2, 5, 2),
            ]);

        Assert.Equal((0, 0), FairyGymBoostTestFixtures.ReadSlot(patched, answerChoice: 1));
        Assert.Equal((5, 2), FairyGymBoostTestFixtures.ReadSlot(patched, answerChoice: 2));
        Assert.Equal(slotThree, FairyGymBoostTestFixtures.ReadSlot(patched, answerChoice: 3));
        Assert.Equal(data[0x3000], patched[0x3000]);
        Assert.All(
            Enumerable.Range(0, data.Length).Where(offset =>
                offset < SwShFairyGymBoostsBseqPatcher.PayloadOffset
                || offset >= SwShFairyGymBoostsBseqPatcher.PayloadOffset
                    + SwShFairyGymBoostsBseqPatcher.OwnedByteCount),
            offset => Assert.Equal(data[offset], patched[offset]));
    }

    [Fact]
    public void ReadAnswerSlotsReturnsOnlyOwnedPayload()
    {
        var data = FairyGymBoostTestFixtures.CreateBseq(
            effectOne: 3,
            resultOne: 2,
            effectTwo: 3,
            resultTwo: 1);

        var slots = SwShFairyGymBoostsBseqPatcher.ReadAnswerSlots(data);

        Assert.Equal(2, slots.Count);
        Assert.Equal(new SwShFairyGymBoostAnswerSlot(3, 2), slots[0]);
        Assert.Equal(new SwShFairyGymBoostAnswerSlot(3, 1), slots[1]);
    }

    [Fact]
    public void ApplySelectionsRejectsEmptyDuplicateAndSlotThreePatches()
    {
        var data = FairyGymBoostTestFixtures.CreateVanillaBseq(
            SwShFairyGymBoostsWorkflowService.AnnetteSequencePath);

        Assert.Throws<InvalidDataException>(() =>
            SwShFairyGymBoostsBseqPatcher.ApplySelections(data, []));
        Assert.Throws<InvalidDataException>(() =>
            SwShFairyGymBoostsBseqPatcher.ApplySelections(
                data,
                [
                    new SwShFairyGymBoostAnswerPatch(1, 1, 1),
                    new SwShFairyGymBoostAnswerPatch(1, 2, 1),
                ]));
        Assert.Throws<InvalidDataException>(() =>
            SwShFairyGymBoostsBseqPatcher.ApplySelections(
                data,
                [new SwShFairyGymBoostAnswerPatch(3, 1, 1)]));
    }

    [Fact]
    public void LayoutVerificationRejectsWrongLengthOffsetAndAmbiguousCommand()
    {
        var relativePath = SwShFairyGymBoostsWorkflowService.AnnetteSequencePath;
        var vanilla = FairyGymBoostTestFixtures.CreateVanillaBseq(relativePath);
        var truncated = vanilla[..^1];
        var shifted = vanilla.ToArray();
        shifted[0x153C + 0x0C] = 0;
        var ambiguous = FairyGymBoostTestFixtures.CreateVanillaBseq(
            relativePath,
            addAmbiguousCommand: true);

        Assert.Throws<InvalidDataException>(() =>
            SwShFairyGymBoostsBseqPatcher.ReadAnswerSlots(truncated));
        Assert.Throws<InvalidDataException>(() =>
            SwShFairyGymBoostsBseqPatcher.ReadAnswerSlots(shifted));
        Assert.Throws<InvalidDataException>(() =>
            SwShFairyGymBoostsBseqPatcher.ReadAnswerSlots(ambiguous));
    }

    [Fact]
    public void VerifiedRestoreCopiesBaseOwnedBytesAndPreservesUnownedEdits()
    {
        var relativePath = SwShFairyGymBoostsWorkflowService.TeresaSequencePath;
        var baseData = FairyGymBoostTestFixtures.CreateVanillaBseq(relativePath);
        var effective = SwShFairyGymBoostsBseqPatcher.ApplySelections(
            baseData,
            [new SwShFairyGymBoostAnswerPatch(1, 2, 1)]);
        effective[0x3000] ^= 0xFF;

        var output = SwShFairyGymBoostsBseqPatcher.ApplySelections(
            effective,
            baseData,
            FairyGymBoostTestFixtures.GetVanillaSlots(relativePath),
            [new SwShFairyGymBoostAnswerPatch(1, 5, 2)]);

        Assert.Equal((5, 2), FairyGymBoostTestFixtures.ReadSlot(output, 1));
        Assert.Equal(effective[0x3000], output[0x3000]);
        Assert.False(output.AsSpan().SequenceEqual(baseData));
    }

    [Fact]
    public void ValidateVanillaBaseRequiresExactMappedOwnedSlots()
    {
        var relativePath = SwShFairyGymBoostsWorkflowService.OpalAgeSequencePath;
        var baseData = FairyGymBoostTestFixtures.CreateVanillaBseq(relativePath);
        var wrongBase = SwShFairyGymBoostsBseqPatcher.ApplySelections(
            baseData,
            [new SwShFairyGymBoostAnswerPatch(1, 1, 1)]);

        SwShFairyGymBoostsBseqPatcher.ValidateVanillaBase(
            baseData,
            FairyGymBoostTestFixtures.GetVanillaSlots(relativePath));
        Assert.Throws<InvalidDataException>(() =>
            SwShFairyGymBoostsBseqPatcher.ValidateVanillaBase(
                wrongBase,
                FairyGymBoostTestFixtures.GetVanillaSlots(relativePath)));
    }
}
