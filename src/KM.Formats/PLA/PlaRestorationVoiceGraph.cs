// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Audio;
using KM.Formats.SwSh;

namespace KM.Formats.PLA;

/// <summary>Resolves the five ordinary cry events from PLA's version 135 voice bank.</summary>
public static class PlaRestorationVoiceGraph
{
    public static IReadOnlyDictionary<string, uint> Resolve(SoundBankDocument bank, int species)
    {
        if (bank.Version != 135 || species is not (46 or 47 or 265 or 266 or 267 or 268 or 269 or 399 or 400))
            throw new InvalidDataException("PLA cry identity or bank version is unsupported.");
        SoundBankObject Object(byte type, uint id) => bank.Objects.SingleOrDefault(o => o.Type == type && o.Id == id)
            ?? throw new InvalidDataException("PLA cry graph is incomplete.");
        uint Word(byte[] bytes, int at) => at >= 0 && at <= bytes.Length - 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)) : throw new InvalidDataException("PLA cry graph is truncated.");
        var media = new uint[5];
        for (var variant = 0; variant < media.Length; variant++)
        {
            var evt = Object(4, SwShVoiceWriter.Hash($"Play_PV_{species:000}_00_{variant:00}")).Payload;
            if (evt.Length != 5 || evt[0] != 1) throw new InvalidDataException("PLA cry event has an unsupported action set.");
            var action = Object(3, Word(evt, 1)).Payload;
            if (action.Length != 14 || action[0] != 3 || action[1] != 4
                || !action.AsSpan(6).SequenceEqual(Convert.FromHexString("00000004BB7180F9")))
                throw new InvalidDataException("PLA cry action requires another adapter.");
            var selector = Object(6, Word(action, 2));
            var bytes = selector.Payload;
            // Retain the ordinary default state. The other branches apply the donor's
            // size effects and are not the normal SwSh cry.
            if (bytes.Length < 100 || !bytes.AsSpan(0, 7).SequenceEqual(new byte[7])
                || !bytes.AsSpan(11, 20).SequenceEqual(Convert.FromHexString("0000000000000000000001000000000000000000"))
                || Word(bytes, 31) != 0xC5C1D34D || bytes[39] != 0 || Word(bytes, 40) != 3)
                throw new InvalidDataException("PLA cry selector has unconverted routing or properties.");
            var children = new HashSet<uint>();
            for (var i = 0; i < 3; i++) if (!children.Add(Word(bytes, 44 + i * 4))) throw new InvalidDataException("PLA cry children are duplicated.");
            var at = 56; var count = Word(bytes, at); at += 4;
            if (count != 3) throw new InvalidDataException("PLA cry states are incomplete.");
            var states = new Dictionary<uint, uint>();
            for (var i = 0; i < 3; i++)
            {
                var state = Word(bytes, at); var childCount = Word(bytes, at + 4); var child = Word(bytes, at + 8); at += 12;
                if (childCount != 1 || !children.Contains(child) || !states.TryAdd(state, child))
                    throw new InvalidDataException("PLA cry state is unsupported.");
            }
            if (!states.TryGetValue(Word(bytes, 35), out var selected) || Word(bytes, at) != 3)
                throw new InvalidDataException("PLA cry default state is missing.");
            at += 4; var configured = new HashSet<uint>();
            for (var i = 0; i < 3; i++)
            {
                var child = Word(bytes, at); at += 4;
                if (!children.Contains(child) || !configured.Add(child) || at > bytes.Length - 10
                    || !bytes.AsSpan(at, 10).SequenceEqual(Convert.FromHexString("00010000000000000000")))
                    throw new InvalidDataException("PLA cry transitions need another adapter.");
                at += 10;
            }
            if (at != bytes.Length) throw new InvalidDataException("PLA cry selector contains unconverted data.");
            var sound = Object(2, selected).Payload;
            if (sound.Length != 44 || Word(sound, 0) != 0x00040001 || sound[4] != 2
                || sound.AsSpan(13, 8).ContainsAnyExcept((byte)0) || Word(sound, 21) != selector.Id
                || !sound.AsSpan(25).SequenceEqual(Convert.FromHexString("00000000000000000000010000000000000000")))
                throw new InvalidDataException("PLA ordinary cry has unsupported codec, effects, or playback properties.");
            media[variant] = Word(sound, 5);
        }
        // SwSh exposes more contexts than PLA's five authored events. Reuse the
        // matching ordinary mood without inventing additional source recordings.
        return SwShVoiceWriter.States.ToDictionary(state => state, state => media[state switch
        {
            "GLAD1" or "GLAD2" or "GLAD3" => 1, "ANGER" => 2, "SAD" => 3,
            "ATK" or "SPATK" => 4, _ => 0,
        }], StringComparer.Ordinal);
    }
}
