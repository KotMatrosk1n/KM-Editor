// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.ZA.MainMissions;

internal sealed class ZaMainMission20Script
{
    internal const string Path = "script/lua/bin/release/event_ik/event_ik.blua";
    private const string VerifiedBaseHash = "F238160475095B02E8C5302D907ADBD1B48B2FD793A4A8C72EE8C9689FDFD698";
    private const uint SpeciesLoadInstruction = 0x0281; // Lua 5.4 LOADI R5
    private const int SignedImmediateBias = 65535;
    private const int InstructionSize = sizeof(uint);
    private const int CallPrefixSize = 10 * InstructionSize;
    private const int CallSuffixSize = 3 * InstructionSize;

    // Species arguments in the three actor creation calls in the entrance scene.
    private static readonly (string Id, int Offset)[] Actors =
    [
        ("left", 0x314E8B),   // dyn_GENGAA_1
        ("center", 0x314EC3), // dyn_GENGAA_2
        ("right", 0x314EFB),  // dyn_GOOSU
    ];

    private readonly byte[] effectiveBytes;

    internal ZaMainMission20Script(byte[] baseBytes, byte[] effectiveBytes)
    {
        var baseHash = Hash(baseBytes);
        if (baseHash != VerifiedBaseHash || effectiveBytes.Length != baseBytes.Length)
            throw new InvalidDataException("The event script version is unsupported.");

        var slots = new List<ZaMainMission20Slot>(Actors.Length);
        foreach (var (id, offset) in Actors)
        {
            // Keep the actor ID, positioning, other arguments, and call bound to the verified scene.
            var prefix = offset - CallPrefixSize;
            var suffix = offset + InstructionSize;
            if (!effectiveBytes.AsSpan(prefix, CallPrefixSize).SequenceEqual(baseBytes.AsSpan(prefix, CallPrefixSize))
                || !effectiveBytes.AsSpan(suffix, CallSuffixSize).SequenceEqual(baseBytes.AsSpan(suffix, CallSuffixSize)))
                throw new InvalidDataException("An entrance scene actor call has changed.");

            slots.Add(new(id, ReadSpecies(effectiveBytes, offset), ReadSpecies(baseBytes, offset)));
        }

        this.effectiveBytes = effectiveBytes;
        Slots = slots;
        SourceRevision = ReferenceEquals(baseBytes, effectiveBytes) ? baseHash : Hash(effectiveBytes);
    }

    internal IReadOnlyList<ZaMainMission20Slot> Slots { get; }
    internal string SourceRevision { get; }

    internal byte[] Write(IReadOnlyDictionary<string, int> changes)
    {
        var output = effectiveBytes.ToArray();
        foreach (var (id, offset) in Actors)
        {
            if (changes.TryGetValue(id, out var species))
            {
                var instruction = ((uint)(species + SignedImmediateBias) << 15) | SpeciesLoadInstruction;
                BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset, InstructionSize), instruction);
            }
        }
        return output;
    }

    private static int ReadSpecies(byte[] bytes, int offset)
    {
        var word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, InstructionSize));
        var species = (int)(word >> 15) - SignedImmediateBias;
        if ((word & 0x7FFF) != SpeciesLoadInstruction || species <= 0)
            throw new InvalidDataException("An entrance scene species argument is invalid.");
        return species;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
