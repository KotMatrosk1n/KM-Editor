// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

public sealed record SwShRestorationFormBlock(int Species, int FirstRow, int AlternateCount, int NativeFirstRow, int NativeAlternateCount)
{
    public int PersonalRow(int form) => form > 0 && form <= AlternateCount ? FirstRow + form - 1
        : throw new ArgumentOutOfRangeException(nameof(form));
}

/// <summary>Stable future form reservations. Reading this plan never changes a game table.</summary>
public static class SwShRestorationFormSlots
{
    public static IReadOnlyList<SwShRestorationFormBlock> Blocks { get; } = Array.AsReadOnly(new SwShRestorationFormBlock[]
    {
        new(3, 981, 1, 0, 0),
        new(6, 899, 2, 0, 0),
        new(9, 1028, 1, 0, 0),
        new(15, 1130, 1, 0, 0),
        new(18, 1163, 1, 0, 0),
        new(19, 1164, 1, 0, 0),
        new(20, 991, 1, 0, 0),
        new(26, 920, 3, 910, 1),
        new(36, 992, 1, 0, 0),
        new(58, 993, 1, 0, 0),
        new(59, 994, 1, 0, 0),
        new(65, 995, 1, 0, 0),
        new(71, 996, 1, 0, 0),
        new(74, 997, 1, 0, 0),
        new(75, 998, 1, 0, 0),
        new(76, 999, 1, 0, 0),
        new(80, 926, 2, 926, 2),
        new(88, 1000, 1, 0, 0),
        new(89, 1001, 1, 0, 0),
        new(94, 1002, 1, 0, 0),
        new(100, 1003, 1, 0, 0),
        new(101, 1004, 1, 0, 0),
        new(115, 1005, 1, 0, 0),
        new(121, 1006, 1, 0, 0),
        new(127, 1007, 1, 0, 0),
        new(128, 969, 3, 0, 0),
        new(130, 1008, 1, 0, 0),
        new(142, 939, 1, 0, 0),
        new(149, 940, 1, 0, 0),
        new(150, 929, 2, 0, 0),
        new(154, 941, 1, 0, 0),
        new(157, 942, 1, 0, 0),
        new(160, 943, 1, 0, 0),
        new(181, 944, 1, 0, 0),
        new(194, 945, 1, 0, 0),
        new(208, 946, 1, 0, 0),
        new(211, 947, 1, 0, 0),
        new(212, 948, 1, 0, 0),
        new(214, 949, 1, 0, 0),
        new(215, 950, 1, 0, 0),
        new(227, 951, 1, 0, 0),
        new(229, 952, 1, 0, 0),
        new(248, 953, 1, 0, 0),
        new(254, 954, 1, 0, 0),
        new(257, 955, 1, 0, 0),
        new(260, 956, 1, 0, 0),
        new(282, 957, 1, 0, 0),
        new(302, 958, 1, 0, 0),
        new(303, 959, 1, 0, 0),
        new(306, 960, 1, 0, 0),
        new(308, 961, 1, 0, 0),
        new(310, 962, 1, 0, 0),
        new(319, 963, 1, 0, 0),
        new(323, 964, 1, 0, 0),
        new(334, 965, 1, 0, 0),
        new(354, 1033, 1, 0, 0),
        new(358, 1034, 1, 0, 0),
        new(359, 1155, 2, 0, 0),
        new(362, 1035, 1, 0, 0),
        new(373, 1036, 1, 0, 0),
        new(376, 1037, 1, 0, 0),
        new(380, 1038, 1, 0, 0),
        new(381, 1039, 1, 0, 0),
        new(382, 1040, 1, 0, 0),
        new(383, 1041, 1, 0, 0),
        new(384, 972, 1, 972, 1),
        new(386, 1082, 3, 0, 0),
        new(398, 1042, 1, 0, 0),
        new(428, 1043, 1, 0, 0),
        new(445, 1157, 2, 0, 0),
        new(448, 1159, 2, 0, 0),
        new(460, 1044, 1, 0, 0),
        new(475, 1045, 1, 0, 0),
        new(478, 1046, 1, 0, 0),
        new(483, 1047, 1, 0, 0),
        new(484, 1048, 1, 0, 0),
        new(485, 1049, 1, 0, 0),
        new(491, 1050, 1, 0, 0),
        new(500, 1051, 1, 0, 0),
        new(503, 1052, 1, 0, 0),
        new(530, 1053, 1, 0, 0),
        new(531, 1054, 1, 0, 0),
        new(545, 1055, 1, 0, 0),
        new(549, 1056, 1, 0, 0),
        new(560, 1057, 1, 0, 0),
        new(570, 1058, 1, 0, 0),
        new(571, 1059, 1, 0, 0),
        new(604, 1060, 1, 0, 0),
        new(609, 1061, 1, 0, 0),
        new(623, 1062, 1, 0, 0),
        new(628, 1063, 1, 0, 0),
        new(652, 1064, 1, 0, 0),
        new(655, 1065, 1, 0, 0),
        new(658, 978, 3, 0, 0),
        new(668, 1066, 1, 0, 0),
        new(670, 1015, 6, 0, 0),
        new(678, 1127, 3, 1114, 1),
        new(687, 1067, 1, 0, 0),
        new(689, 1068, 1, 0, 0),
        new(691, 1069, 1, 0, 0),
        new(701, 1070, 1, 0, 0),
        new(705, 1071, 1, 0, 0),
        new(706, 1072, 1, 0, 0),
        new(713, 1073, 1, 0, 0),
        new(718, 973, 5, 1123, 4),
        new(719, 1074, 1, 0, 0),
        new(724, 1075, 1, 0, 0),
        new(740, 1076, 1, 0, 0),
        new(768, 1077, 1, 0, 0),
        new(780, 1078, 1, 0, 0),
        new(801, 1152, 3, 1168, 1),
        new(807, 1079, 1, 0, 0),
        new(870, 1080, 1, 0, 0),
        // Appended blocks preserve every native and previously reserved form row.
        new(550, 1430, 2, 1009, 1),
        new(664, 1439, 19, 0, 0),
        new(665, 1458, 19, 0, 0),
        new(666, 1477, 19, 0, 0),
        new(669, 1496, 4, 0, 0),
        new(671, 1500, 4, 0, 0),
        new(720, 1513, 1, 0, 0),
        new(741, 1514, 3, 0, 0),
        new(774, 1517, 13, 0, 0),
        new(902, 1532, 1, 0, 0),
        new(905, 1533, 1, 0, 0),
    });
    private static readonly IReadOnlyDictionary<int, SwShRestorationFormBlock> BySpecies = Blocks.ToDictionary(b => b.Species);

    public static int? PlannedPersonalRow(int species, int form) => BySpecies.TryGetValue(species, out var block)
        && form > 0 && form <= block.AlternateCount ? block.PersonalRow(form) : null;

    public static int RecordCapacity => Math.Max(SwShRestorationSpecies.NativeRecordCount, Blocks.Max(b => b.FirstRow + b.AlternateCount));

    internal static bool IsPlannedRow(int row) => Blocks.Any(b => row >= b.FirstRow && row < b.FirstRow + b.AlternateCount);

    /// <summary>Check native ownership and effective destination rows before using a reservation.</summary>
    public static void Validate(ReadOnlySpan<byte> nativeData, ReadOnlySpan<byte> currentData)
    {
        var native = SwShPersonalTable.Parse(nativeData, 1192).Records;
        var current = SwShPersonalTable.Parse(currentData, 4096).Records;
        if (native.Count != SwShRestorationSpecies.NativeRecordCount || current.Count < native.Count)
            throw new InvalidDataException("Form reservations require a compatible personal table.");
        var nativeOwners = SwShEffectivePokemonCatalog.Identities(native).ToDictionary(e => e.PersonalId, e => e.Identity);
        var currentOwners = SwShEffectivePokemonCatalog.Identities(current).ToDictionary(e => e.PersonalId, e => e.Identity);
        var reserved = new HashSet<int>();
        foreach (var block in Blocks)
        {
            var baseRow = SwShRestorationSpecies.BaseRow(block.Species);
            var original = baseRow < native.Count ? native[baseRow] : null;
            if ((original?.FormStatsIndex ?? 0) != block.NativeFirstRow || Math.Max(0, (original?.FormCount ?? 0) - 1) != block.NativeAlternateCount)
                throw new InvalidDataException($"Species {block.Species} has incompatible native form ownership.");
            for (var form = 1; form <= block.AlternateCount; form++)
            {
                var row = block.PersonalRow(form);
                var identity = new SwShPokemonIdentity(block.Species, form);
                if (row <= SwShEffectivePokemonCatalog.MaximumSpecies || !reserved.Add(row)
                    || SwShRestorationSpecies.SpeciesAtReservedRow(row) is not null
                    || nativeOwners.TryGetValue(row, out var owner) && owner != identity
                    || currentOwners.TryGetValue(row, out owner) && owner != identity)
                    throw new InvalidDataException($"Planned form row {row} is already owned by another identity.");
                if (!currentOwners.ContainsKey(row) && row < current.Count && (row < native.Count
                    ? !currentData.Slice(row * SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize)
                        .SequenceEqual(nativeData.Slice(row * SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize))
                    : currentData.Slice(row * SwShPersonalTable.RecordSize, SwShPersonalTable.RecordSize).ContainsAnyExcept((byte)0)))
                    throw new InvalidDataException($"Planned form row {row} contains other personal data.");
            }
        }
    }
}
