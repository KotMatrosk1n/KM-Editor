// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.DynamaxAdventures;

internal static class SwShDynamaxAdventuresMainPatcher
{
    public const int SummaryOffset = 0x00775054;
    public const int SummaryEntrySize = 0x06;

    internal const int LocalSpeciesPresentMismatchBranchOffset = 0x00EA52AC;
    internal const int LocalSpeciesMissingMismatchBranchOffset = 0x00EA52C0;
    internal const int LocalFormPresentMismatchBranchOffset = 0x00EA52F4;
    internal const int LocalFormMissingMismatchBranchOffset = 0x00EA5308;
    internal const int LocalGigantamaxMismatchBranchOffset = 0x00EA5310;
    internal const int NestSpeciesPresentMismatchBranchOffset = 0x00EA76AC;
    internal const int NestSpeciesMissingMismatchBranchOffset = 0x00EA76C0;
    internal const int NestFormPresentMismatchBranchOffset = 0x00EA76F4;
    internal const int NestFormMissingMismatchBranchOffset = 0x00EA7708;
    internal const int NestGigantamaxMismatchBranchOffset = 0x00EA7710;
    internal const int DaiSpeciesPresentMismatchBranchOffset = 0x00EA78B4;
    internal const int DaiSpeciesMissingMismatchBranchOffset = 0x00EA78C8;
    internal const int DaiFormPresentMismatchBranchOffset = 0x00EA78FC;
    internal const int DaiFormMissingMismatchBranchOffset = 0x00EA7910;
    internal const int DaiGigantamaxMismatchBranchOffset = 0x00EA7918;

    private const uint NopInstruction = 0xD503201F;

    private static readonly (int Offset, uint VanillaInstruction, string Label)[] CommandMirrorFailureBranches =
    [
        (LocalSpeciesPresentMismatchBranchOffset, 0x1400001C, "LocalNestHolePokemon species-present mismatch"),
        (LocalSpeciesMissingMismatchBranchOffset, 0x540002E1, "LocalNestHolePokemon species-missing mismatch"),
        (LocalFormPresentMismatchBranchOffset, 0x1400000A, "LocalNestHolePokemon form-present mismatch"),
        (LocalFormMissingMismatchBranchOffset, 0x540000A1, "LocalNestHolePokemon form-missing mismatch"),
        (LocalGigantamaxMismatchBranchOffset, 0x35000068, "LocalNestHolePokemon Gigantamax mismatch"),
        (NestSpeciesPresentMismatchBranchOffset, 0x1400001C, "NestHolePokemon species-present mismatch"),
        (NestSpeciesMissingMismatchBranchOffset, 0x540002E1, "NestHolePokemon species-missing mismatch"),
        (NestFormPresentMismatchBranchOffset, 0x1400000A, "NestHolePokemon form-present mismatch"),
        (NestFormMissingMismatchBranchOffset, 0x540000A1, "NestHolePokemon form-missing mismatch"),
        (NestGigantamaxMismatchBranchOffset, 0x35000068, "NestHolePokemon Gigantamax mismatch"),
        (DaiSpeciesPresentMismatchBranchOffset, 0x1400001C, "DaiNestHolePokemon species-present mismatch"),
        (DaiSpeciesMissingMismatchBranchOffset, 0x540002E1, "DaiNestHolePokemon species-missing mismatch"),
        (DaiFormPresentMismatchBranchOffset, 0x1400000A, "DaiNestHolePokemon form-present mismatch"),
        (DaiFormMissingMismatchBranchOffset, 0x540000A1, "DaiNestHolePokemon form-missing mismatch"),
        (DaiGigantamaxMismatchBranchOffset, 0x35000068, "DaiNestHolePokemon Gigantamax mismatch"),
    ];

    public static byte[] Apply(
        byte[] mainBytes,
        SwShDynamaxAdventureArchive archive,
        bool patchCommandValidatorMirrors)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ArgumentNullException.ThrowIfNull(archive);

        var nso = SwShNsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.ToArray();
        var ro = nso.Ro.DecompressedData.ToArray();

        WriteSummary(ro, archive.Entries);
        if (patchCommandValidatorMirrors)
        {
            PatchCommandValidatorMirrors(text);
        }

        return nso.Write(textDecompressedData: text, roDecompressedData: ro);
    }

    public static byte[] RestoreFromBase(byte[] currentMainBytes, byte[] baseMainBytes, int entryCount)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        if (entryCount < 0)
        {
            throw new InvalidDataException("Dynamax Adventures restore requires a non-negative Adventure entry count.");
        }

        var currentNso = SwShNsoFile.Parse(currentMainBytes);
        var baseNso = SwShNsoFile.Parse(baseMainBytes);
        var currentText = currentNso.Text.DecompressedData.ToArray();
        var currentRo = currentNso.Ro.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        var baseRo = baseNso.Ro.DecompressedData;

        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("Dynamax Adventures restore requires current and base main NSO files with matching .text sizes.");
        }

        if (currentRo.Length != baseRo.Length)
        {
            throw new InvalidDataException("Dynamax Adventures restore requires current and base main NSO files with matching .ro sizes.");
        }

        RestoreSummaryFromBase(currentRo, baseRo, entryCount);
        RestoreCommandValidatorMirrors(currentText, baseText);

        return currentNso.Write(textDecompressedData: currentText, roDecompressedData: currentRo);
    }

    internal static void WriteSummary(byte[] ro, IReadOnlyList<SwShDynamaxAdventureRecord> entries)
    {
        ArgumentNullException.ThrowIfNull(ro);
        ArgumentNullException.ThrowIfNull(entries);

        var length = checked(entries.Count * SummaryEntrySize);
        EnsureRange(ro, SummaryOffset, length, "Dynamax Adventures hardcoded summary table");

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.EntryIndex != index)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Dynamax Adventure entry index {entry.EntryIndex} is not in table order at row {index}."));
            }

            var destination = ro.AsSpan(SummaryOffset + (index * SummaryEntrySize), SummaryEntrySize);
            destination[0] = entry.IsSingleCapture ? (byte)1 : (byte)0;
            destination[1] = 0;
            BinaryPrimitives.WriteInt16LittleEndian(
                destination[2..4],
                checked((short)ValidateSignedSummaryValue(entry.Species, short.MinValue, short.MaxValue, "species")));
            destination[4] = unchecked((byte)(sbyte)ValidateSignedSummaryValue(entry.Form, sbyte.MinValue, sbyte.MaxValue, "form"));
            destination[5] = unchecked((byte)(sbyte)ValidateSignedSummaryValue(entry.ShinyRoll, sbyte.MinValue, sbyte.MaxValue, "shiny roll"));
        }
    }

    internal static void PatchCommandValidatorMirrors(byte[] text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var (offset, vanillaInstruction, label) in CommandMirrorFailureBranches)
        {
            WriteNopIfVanillaOrOwned(text, offset, vanillaInstruction, label);
        }
    }

    private static void RestoreSummaryFromBase(byte[] currentRo, ReadOnlyMemory<byte> baseRo, int entryCount)
    {
        var length = checked(entryCount * SummaryEntrySize);
        EnsureRange(currentRo, SummaryOffset, length, "Dynamax Adventures hardcoded summary table");
        EnsureRange(baseRo.Span, SummaryOffset, length, "Base Dynamax Adventures hardcoded summary table");

        baseRo.Span.Slice(SummaryOffset, length).CopyTo(currentRo.AsSpan(SummaryOffset, length));
    }

    private static void RestoreCommandValidatorMirrors(byte[] currentText, ReadOnlyMemory<byte> baseText)
    {
        foreach (var (offset, _, label) in CommandMirrorFailureBranches)
        {
            RestoreInstructionIfOwned(currentText, baseText.Span, offset, label);
        }
    }

    private static int ValidateSignedSummaryValue(int value, int minimum, int maximum, string field)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventure {field} value {value} cannot be mirrored into the game's hardcoded summary table."));
        }

        return value;
    }

    private static void WriteNopIfVanillaOrOwned(byte[] text, int offset, uint vanillaInstruction, string label)
    {
        var actual = ReadInstruction(text, offset, label);
        if (actual == NopInstruction)
        {
            return;
        }

        if (actual != vanillaInstruction)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventures expected vanilla {label} branch at main.text+0x{offset:X}, but found 0x{actual:X8}."));
        }

        WriteInstruction(text, offset, NopInstruction, label);
    }

    private static void RestoreInstructionIfOwned(byte[] text, ReadOnlySpan<byte> baseText, int offset, string label)
    {
        var baseInstruction = ReadInstruction(baseText, offset, $"Base {label}");
        var actual = ReadInstruction(text, offset, label);
        if (actual == baseInstruction)
        {
            return;
        }

        if (actual != NopInstruction)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Dynamax Adventures restore expected owned {label} branch patch at main.text+0x{offset:X}, but found 0x{actual:X8}."));
        }

        WriteInstruction(text, offset, baseInstruction, label);
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset, string label)
    {
        EnsureRange(text, offset, sizeof(uint), $"{label} instruction");
        return BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction, string label)
    {
        EnsureRange(text, offset, sizeof(uint), $"{label} instruction");
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed NSO segment.");
        }
    }
}
