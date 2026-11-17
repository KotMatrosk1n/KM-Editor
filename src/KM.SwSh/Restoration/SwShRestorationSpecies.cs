// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

/// <summary>Explicit logical species identities whose base records use reserved storage.</summary>
public static class SwShRestorationSpecies
{
    public const int NativeRecordCount = 1192;
    public const int Annihilape = 979;
    public const int AnnihilapeRow = 1081;
    // Native Runerigus already owns icon resource 979. Keep the logical species
    // separate from its reserved miniature resource, as native species do.
    public const int AnnihilapeIconId = 1010;
    // Keep the established extended dispatch mapping beyond native egg files 1192..1250.
    private const int AppendedRowOffset = 352;
    private static readonly int[] AdditionalSpecies = [901, 902, 904, 905, 981, 983, 984, 985, 986, 987, 988, 989, 990, 991, 992, 993, 994, 995, 996, 997, 998, 1000, 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009, 1010, 1018, 1020, 1021, 1022, 1023];
    public static int IconSpecies(int species) => species == Annihilape ? AnnihilapeIconId
        : AdditionalSpecies.Contains(species) ? 1011 + species - 899 : species;
    public static IEnumerable<int> BaseSpecies => Enumerable.Range(1, SwShEffectivePokemonCatalog.MaximumSpecies).Append(Annihilape).Concat(AdditionalSpecies);
    public static bool IsSpecies(int species) => species is >= 1 and <= SwShEffectivePokemonCatalog.MaximumSpecies or Annihilape || AdditionalSpecies.Contains(species);
    public static int BaseRow(int species) => species == Annihilape ? AnnihilapeRow
        : species is >= 1 and <= SwShEffectivePokemonCatalog.MaximumSpecies ? species
        : AdditionalSpecies.Contains(species) ? species + AppendedRowOffset
        : throw new InvalidDataException("The logical Pokemon species is not registered.");
    public static int? SpeciesAtReservedRow(int row) => row == AnnihilapeRow ? Annihilape
        : AdditionalSpecies.Contains(row - AppendedRowOffset) ? row - AppendedRowOffset : null;
    public static string? DisplayName(int species) => species switch
    {
        901 => "Ursaluna", 902 => "Basculegion", 904 => "Overqwil", 905 => "Enamorus",
        981 => "Farigiraf",
        983 => "Kingambit",
        984 => "Great Tusk",
        985 => "Scream Tail",
        986 => "Brute Bonnet",
        987 => "Flutter Mane",
        988 => "Slither Wing",
        989 => "Sandy Shocks",
        990 => "Iron Treads",
        991 => "Iron Bundle",
        992 => "Iron Hands",
        993 => "Iron Jugulis",
        994 => "Iron Moth",
        995 => "Iron Thorns",
        996 => "Frigibax",
        997 => "Arctibax",
        998 => "Baxcalibur",
        1000 => "Gholdengo",
        1001 => "Wo-Chien",
        1002 => "Chien-Pao",
        1003 => "Ting-Lu",
        1004 => "Chi-Yu",
        1005 => "Roaring Moon",
        1006 => "Iron Valiant",
        1007 => "Koraidon",
        1008 => "Miraidon",
        1009 => "Walking Wake",
        1010 => "Iron Leaves",
        1018 => "Archaludon",
        1020 => "Gouging Fire",
        1021 => "Raging Bolt",
        1022 => "Iron Boulder",
        1023 => "Iron Crown",
        Annihilape => "Annihilape", _ => null,
    };

    public static byte[] EmptyRecord(int size) => size == SwShPersonalTable.RecordSize ? new byte[size]
        : size == 260 ? Enumerable.Repeat((byte)255, size).ToArray()
        : throw new ArgumentOutOfRangeException(nameof(size));

    /// <summary>The immutable native record, or no prior owner for appended storage.</summary>
    public static byte[]? NativeRecord(byte[] native, int row, int size)
    {
        if (native.Length != NativeRecordCount * size || row < 0)
            throw new InvalidDataException("The native Pokemon table layout is incompatible.");
        if (row < NativeRecordCount) return native.AsSpan(row * size, size).ToArray();
        if (SpeciesAtReservedRow(row) is null && !SwShRestorationFormSlots.IsPlannedRow(row))
            throw new InvalidDataException("The appended Pokemon record is not registered.");
        return null;
    }

    /// <summary>Keep indexed tables aligned and remove only empty owned storage at the tail.</summary>
    public static (byte[] Personal, byte[] Learnset) AlignTables(byte[] personal, byte[] learnset)
    {
        if (personal.Length % 176 != 0 || learnset.Length % 260 != 0)
            throw new InvalidDataException("Pokemon table records are incomplete.");
        var personalCount = personal.Length / 176; var learnsetCount = learnset.Length / 260;
        if (Math.Min(personalCount, learnsetCount) < NativeRecordCount || Math.Max(personalCount, learnsetCount) > 4096)
            throw new InvalidDataException("Pokemon table lengths are incompatible.");
        var count = Math.Max(personalCount, learnsetCount);
        bool Empty(int row) => (row >= personalCount || !personal.AsSpan(row * 176, 176).ContainsAnyExcept((byte)0))
            && (row >= learnsetCount || !learnset.AsSpan(row * 260, 260).ContainsAnyExcept((byte)255));
        // Rows beyond this reservation may belong to another feature. Their length,
        // including empty rows, is not ours to reduce.
        while (count > NativeRecordCount && count <= SwShRestorationFormSlots.RecordCapacity && Empty(count - 1)) count--;
        return (Resize(personal, 176, count), Resize(learnset, 260, count));

        static byte[] Resize(byte[] bytes, int size, int count)
        {
            if (bytes.Length == size * count) return bytes;
            var result = new byte[size * count];
            if (size == 260) result.AsSpan().Fill(255);
            bytes.AsSpan(0, Math.Min(bytes.Length, result.Length)).CopyTo(result);
            return result;
        }
    }

    public static void ValidateReservation(byte[] native, byte[] current)
    {
        if (native.Length != NativeRecordCount * SwShPersonalTable.RecordSize || current.Length < native.Length
            || current.Length % SwShPersonalTable.RecordSize != 0 || current.Length / SwShPersonalTable.RecordSize > 4096)
            throw new InvalidDataException("The new species reservation requires the supported native personal layout.");
        foreach (var data in new[] { native, current })
        {
            var records = SwShPersonalTable.Parse(data, 4096).Records;
            foreach (var species in AdditionalSpecies)
            {
                var icon = IconSpecies(species);
                var alternate = SwShRestorationFormSlots.Blocks.SingleOrDefault(block => block.Species == species);
                if (records.Where((record, row) => row != BaseRow(species)
                        && !(alternate is not null && row >= alternate.FirstRow && row < alternate.FirstRow + alternate.AlternateCount))
                    .Any(record => record.ModelId == icon))
                    throw new InvalidDataException("Another Pokemon claims an appended species icon resource.");
            }
            if (records.Where((record, row) => row != AnnihilapeRow).Any(record => record.ModelId == AnnihilapeIconId))
                throw new InvalidDataException("Another Pokemon claims the reserved new species icon resource.");
            for (var species = 1; species <= SwShEffectivePokemonCatalog.MaximumSpecies; species++)
            {
                var record = records[species];
                if (record.FormStatsIndex != 0 && record.FormCount > 1 && record.FormStatsIndex <= AnnihilapeRow
                    && record.FormStatsIndex + record.FormCount - 2 >= AnnihilapeRow)
                    throw new InvalidDataException("Another Pokemon claims the reserved new species row.");
            }
        }
        var before = native.AsSpan(AnnihilapeRow * SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize);
        if (Convert.ToHexString(SHA256.HashData(before)) != "E26B3853F98CF2778D4A9C63AD53CDA32C92DA679A5E4666E5D13E3991A051BF")
            throw new InvalidDataException("The reserved new species row is occupied in Base RomFS.");
        var row = current.AsSpan(AnnihilapeRow * SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize);
        // Accept the legacy resource identity so an earlier installation remains removable.
        var modelId = BinaryPrimitives.ReadUInt32LittleEndian(row[76..]);
        if (!row.SequenceEqual(before) && (modelId is not (Annihilape or AnnihilapeIconId)
            || (row[33] & 64) == 0 || row[32] != 1 || BinaryPrimitives.ReadUInt16LittleEndian(row[30..]) != 0))
            throw new InvalidDataException("The reserved new species row contains foreign data.");
    }
}
