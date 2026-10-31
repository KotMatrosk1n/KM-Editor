// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KM.SwSh.CustomGifts;

internal static class SwShCustomGiftCatalogCodec
{
    internal const int PayloadSize = 262144;
    internal const int MaximumEntries = 128;
    internal const int EntrySize = 768;
    internal const int EntriesOffset = 64;
    internal const int MetadataOffset = EntriesOffset + MaximumEntries * EntrySize;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        RespectRequiredConstructorParameters = true,
    };

    internal static string Serialize(SwShCustomGiftCatalog catalog) => JsonSerializer.Serialize(catalog, JsonOptions);
    internal static SwShCustomGiftCatalog Deserialize(string json) =>
        JsonSerializer.Deserialize<SwShCustomGiftCatalog>(json, JsonOptions) ?? throw new InvalidDataException("The gift catalog is missing.");

    internal static byte[] Write(SwShCustomGiftCatalog catalog, bool sword)
        => WritePayload(SwShCustomGiftCatalogOdds.WithChances(catalog), sword, legacy: false);

    internal static byte[] WriteLegacy(SwShCustomGiftCatalog catalog, bool sword) => WritePayload(catalog, sword, legacy: true);

    private static byte[] WritePayload(SwShCustomGiftCatalog catalog, bool sword, bool legacy)
    {
        Validate(catalog, legacy);
        var bytes = new byte[PayloadSize];
        "KMGIFT01"u8.CopyTo(bytes);
        void U32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        U32(8, 1);
        U32(12, sword ? 1 : 2);
        U32(16, catalog.Mode switch { "mystery" => 1, "royal" => 2, _ => 0 });
        var entries = catalog.Mode == "mystery"
            ? catalog.CodeGifts.Select(gift => (gift.Code, Badge: 255, gift.ClaimId, gift.OncePerSave, gift.Pokemon, Weight: 0)).ToArray()
            : catalog.Mode == "royal"
                ? catalog.RoyalPools.SelectMany(pool => pool.Pokemon.Select((pokemon, index) =>
                    (Code: "", Badge: pool.Badges, ClaimId: 2000 + pool.Badges, OncePerSave: true, Pokemon: pokemon,
                        Weight: legacy ? 0 : checked((int)(pool.Chances![index] * 100))))).ToArray()
                : [];
        U32(20, entries.Length);
        var metadata = Encoding.UTF8.GetBytes(Serialize(catalog));
        if (metadata.Length > PayloadSize - MetadataOffset) throw new InvalidDataException("The gift catalog is too large.");
        U32(24, metadata.Length);
        metadata.CopyTo(bytes, MetadataOffset);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var offset = EntriesOffset + EntrySize * index;
            Encoding.ASCII.GetBytes(entry.Code).CopyTo(bytes, offset);
            bytes[offset + 17] = checked((byte)entry.Badge);
            bytes[offset + 18] = checked((byte)entry.Pokemon.GenderRatio);
            bytes[offset + 19] = checked((byte)entry.Pokemon.AbilityMask);
            U32(offset + 20, entry.Weight);
            SwShCustomGiftCard.Write(entry.Pokemon, entry.ClaimId, entry.OncePerSave).CopyTo(bytes, offset + 32);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), Digest(bytes));
        return bytes;
    }

    internal static SwShCustomGiftCatalog Read(ReadOnlySpan<byte> bytes, bool sword, bool legacy = false)
    {
        if (bytes.Length != PayloadSize || !bytes[..8].SequenceEqual("KMGIFT01"u8)
            || BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) != 1
            || BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]) != (sword ? 1 : 2)
            || BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]) != Digest(bytes))
            throw new InvalidDataException("The custom gift payload is invalid or belongs to another edition.");
        var size = BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]);
        if (size is <= 0 or > PayloadSize - MetadataOffset) throw new InvalidDataException("The custom gift metadata is invalid.");
        var catalog = Deserialize(new UTF8Encoding(false, true).GetString(bytes.Slice(MetadataOffset, size)));
        if (!(legacy ? WriteLegacy(catalog, sword) : Write(catalog, sword)).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("The custom gift catalog and runtime data disagree.");
        return catalog;
    }

    internal static void Validate(SwShCustomGiftCatalog catalog, bool legacy = false)
    {
        if (catalog is null || catalog.Version != 1 || catalog.Mode is not ("none" or "mystery" or "royal")
            || catalog.CodeGifts is null || catalog.RoyalPools is null || catalog.CodeGifts.Count > MaximumEntries
            || catalog.NextClaimId is < 1800 or > 2000 || catalog.RoyalPools.Count != 9 || catalog.RoyalPools.Any(pool => pool is null)
            || !catalog.RoyalPools.Select(pool => pool.Badges).Order().SequenceEqual(Enumerable.Range(0, 9)))
            throw new InvalidDataException("The gift catalog version, mode, or badge tiers are invalid.");
        var identities = new HashSet<int>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gift in catalog.CodeGifts)
        {
            if (gift is null || gift.ClaimId is < 1800 or >= 2000 || gift.ClaimId >= catalog.NextClaimId
                || !identities.Add(gift.ClaimId) || string.IsNullOrWhiteSpace(gift.Name) || gift.Name.Length > 80
                || gift.Name.Any(char.IsControl) || gift.Code is null || gift.Code.Length is < 5 or > 16
                || gift.Code.Any(value => value is not (>= 'A' and <= 'H' or >= 'J' and <= 'N' or >= 'P' and <= 'Y' or >= '0' and <= '9'))
                || gift.Code.All(value => value == '0') || !codes.Add(gift.Code))
                throw new InvalidDataException("Each code gift needs a unique identity, name, and uppercase code of 5 through 16 letters or digits, excluding I, O, Z, and all zero codes.");
            SwShCustomGiftCard.Validate(gift.Pokemon);
        }
        var count = 0;
        foreach (var pool in catalog.RoyalPools)
        {
            if (pool.Pokemon is null || pool.Pokemon.Count > (legacy ? MaximumEntries : SwShCustomGiftCatalogOdds.MaximumPoolSize))
                throw new InvalidDataException("Each badge pool can contain at most 14 Pokemon. Remove extra entries before staging.");
            if (legacy ? pool.Chances is not null : pool.Chances is null || pool.Chances.Count != pool.Pokemon.Count
                || pool.Chances.Any(chance => chance < 0 || chance > 100 || decimal.Round(chance, 2) != chance)
                || pool.Pokemon.Count > 0 && pool.Chances.Sum() != 100m)
                throw new InvalidDataException("Each populated badge pool must total exactly 100% odds, with up to two decimal places per slot.");
            count += pool.Pokemon.Count;
            foreach (var pokemon in pool.Pokemon) SwShCustomGiftCard.Validate(pokemon);
        }
        if (count > MaximumEntries || catalog.Mode == "mystery" && catalog.CodeGifts.Count == 0
            || catalog.Mode == "royal" && count == 0)
            throw new InvalidDataException("Install a gift mode with at least one reward and no more than 128 rewards.");
        if (Encoding.UTF8.GetByteCount(Serialize(catalog)) > PayloadSize - MetadataOffset)
            throw new InvalidDataException("The gift definitions exceed the receiver catalog size. Shorten gift names or remove unused definitions.");
    }

    internal static ulong Digest(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037;
        for (var index = 0; index < bytes.Length; index++) hash = unchecked((hash ^ (index is >= 32 and < 40 ? (byte)0 : bytes[index])) * 1099511628211);
        return hash;
    }
}
