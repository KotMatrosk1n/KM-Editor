// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.SwSh;

namespace KM.SwSh.CustomGifts;

internal static class SwShCustomGiftCard
{
    internal const int Size = 720;

    internal static byte[] Write(SwShCustomGiftPokemon pokemon, int claimId, bool once)
    {
        Validate(pokemon);
        if (claimId is < 1 or > 2047) throw new InvalidDataException("The gift claim identity is outside its supported range.");
        var card = new byte[Size];
        void U16(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(card.AsSpan(offset), checked((ushort)value));
        U16(8, claimId);
        U16(14, 3);
        card[16] = once ? (byte)1 : (byte)0;
        card[17] = 1;
        U16(0x22C, pokemon.BallItemId);
        U16(0x22E, pokemon.HeldItemId);
        U16(0x230, pokemon.SpecialMoveId);
        U16(0x240, pokemon.SpeciesId);
        card[0x242] = checked((byte)pokemon.Form);
        card[0x243] = pokemon.Gender switch { 0 => 3, 1 => 0, 2 => 1, _ => throw new InvalidDataException() };
        card[0x244] = checked((byte)pokemon.Level);
        card[0x245] = pokemon.IsEgg ? (byte)1 : (byte)0;
        card[0x246] = pokemon.Nature == 25 ? (byte)255 : checked((byte)pokemon.Nature);
        card[0x247] = pokemon.Ability switch { 0 => 3, 1 => 0, 2 => 1, 3 => 2, _ => throw new InvalidDataException() };
        card[0x248] = pokemon.ShinyLock switch { 0 => 1, 1 => 2, 2 => 0, _ => throw new InvalidDataException() };
        card[0x249] = checked((byte)pokemon.Level);
        card[0x24A] = checked((byte)pokemon.DynamaxLevel);
        card[0x24B] = pokemon.CanGigantamax ? (byte)1 : (byte)0;
        card[0x272] = 2;
        card.AsSpan(0x24C, 32).Fill(255);
        var ivs = pokemon.Ivs;
        var values = new[] { ivs.HP, ivs.Attack, ivs.Defense, ivs.Speed, ivs.SpecialAttack, ivs.SpecialDefense };
        for (var i = 0; i < values.Length; i++)
            card[0x26C + i] = pokemon.FlawlessIvCount == 6 ? (byte)31 : values[i] < 0 ? (byte)255 : checked((byte)values[i]);
        if (pokemon.FlawlessIvCount != 6 && (pokemon.FlawlessIvCount == 3 || ivs.HP == -4))
        {
            card.AsSpan(0x26C, 6).Fill(255);
            card[0x26C] = 254;
        }
        U16(0x2CC, Crc(card));
        return card;
    }

    internal static void Validate(SwShCustomGiftPokemon p)
    {
        if (p is null || p.Ivs is null || p.SpeciesId is < 1 or > 65535 || p.Form is < 0 or > 255
            || p.Level is < 1 or > 100 || p.HeldItemId is < 0 or > 65535
            || !SwShGiftPokemonArchive.IsValidBallItemId(p.BallItemId) || p.Ability is < 0 or > 3
            || p.Nature is < 0 or > 25 || p.Gender is < 0 or > 2 || p.ShinyLock is < 0 or > 2
            || p.DynamaxLevel is < 0 or > 10 || p.SpecialMoveId is < 0 or > 65535
            || p.FlawlessIvCount is not (0 or 3 or 6) || p.GenderRatio is < 0 or > 255
            || p.AbilityMask is < 1 or > 7 || (p.AbilityMask & 1) == 0)
            throw new InvalidDataException("The gift Pokemon contains invalid field values.");
        if (p.Ivs.HP is < -4 or > 31 || p.Ivs.HP is -3 or -2
            || new[] { p.Ivs.Attack, p.Ivs.Defense, p.Ivs.Speed, p.Ivs.SpecialAttack, p.Ivs.SpecialDefense }.Any(value => value is < -1 or > 31))
            throw new InvalidDataException("Gift IVs must be random, fixed from 0 through 31, or use a supported preset.");
    }

    internal static ushort Crc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 65535;
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = index is 0x2CC or 0x2CD ? (byte)0 : bytes[index];
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0));
        }
        return crc;
    }
}
