// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.SwSh.DynamaxAdventures;

internal static class SwShDynamaxAdventureSafetyRules
{
    public const int BossEntryStartIndex = 226;
    public const int MaximumVerifiedNormalReplacementSpecies = 898;

    private static readonly HashSet<int> SpecialNormalRouteSpecies =
    [
        144, 145, 146, 150,
        151,
        243, 244, 245, 249, 250, 251, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386,
        480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493, 494,
        638, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649,
        716, 717, 718, 719, 720, 721, 772, 773, 785, 786, 787, 788, 789, 790, 791, 792, 793, 794, 795, 796, 797, 798, 799,
        800, 801, 802, 803, 804, 805, 806, 807, 808, 809,
        888, 889, 890, 891, 892, 893, 894, 895, 896, 897, 898,
    ];

    private static readonly HashSet<int> BattleFusionSpecies = [646, 800, 898];

    private static readonly int[] TechnicalMachineMoveIds =
    [
        5, 25, 6, 7, 8, 9, 19, 42, 63, 416,
        345, 76, 669, 83, 86, 91, 103, 113, 115, 219,
        120, 156, 157, 168, 173, 182, 184, 196, 202, 204,
        211, 213, 201, 240, 241, 258, 250, 251, 261, 263,
        129, 270, 279, 280, 286, 291, 311, 313, 317, 328,
        331, 333, 340, 341, 350, 362, 369, 371, 372, 374,
        384, 385, 683, 409, 419, 421, 422, 423, 424, 427,
        433, 472, 478, 440, 474, 490, 496, 506, 512, 514,
        521, 523, 527, 534, 541, 555, 566, 577, 580, 581,
        604, 678, 595, 598, 206, 403, 684, 693, 707, 784,
    ];

    private static readonly int[] TechnicalRecordMoveIds =
    [
        14, 34, 53, 56, 57, 58, 59, 67, 85, 87,
        89, 94, 97, 116, 118, 126, 127, 133, 141, 161,
        164, 179, 188, 191, 200, 473, 203, 214, 224, 226,
        227, 231, 242, 247, 248, 253, 257, 269, 271, 276,
        285, 299, 304, 315, 322, 330, 334, 337, 339, 347,
        348, 349, 360, 370, 390, 394, 396, 398, 399, 402,
        404, 405, 406, 408, 411, 412, 413, 414, 417, 428,
        430, 437, 438, 441, 442, 444, 446, 447, 482, 484,
        486, 492, 500, 502, 503, 526, 528, 529, 535, 542,
        583, 599, 605, 663, 667, 675, 676, 706, 710, 776,
    ];

    private static readonly int[] TypeTutorMoveIds =
    [
        520, 519, 518, 338, 307, 308, 434, 796,
    ];

    private static readonly int[] ArmorTutorMoveIds =
    [
        805, 807, 812, 804, 803, 813, 811, 810, 815,
        814, 797, 806, 800, 809, 799, 808, 798, 802,
    ];

    public static bool IsNormalEntryIndex(int entryIndex)
    {
        return entryIndex < BossEntryStartIndex;
    }

    public static bool IsBossEntryIndex(int entryIndex)
    {
        return entryIndex >= BossEntryStartIndex;
    }

    public static bool IsSpecialNormalRouteSpecies(int species)
    {
        return SpecialNormalRouteSpecies.Contains(species);
    }

    public static bool IsBattleFusionSpecies(int species)
    {
        return BattleFusionSpecies.Contains(species);
    }

    public static bool CanUseAsNormalReplacement(
        int species,
        int form,
        IReadOnlySet<(int Species, int Form)> usedSpeciesForms,
        IReadOnlySet<(int Species, int Form)> bossSpeciesForms,
        IReadOnlyList<SwShPersonalRecord> personalRecords)
    {
        var personal = ResolvePersonalRecord(species, form, personalRecords);
        if (form != 0
            || species <= 0
            || species > MaximumVerifiedNormalReplacementSpecies
            || personal?.IsPresentInGame != true
            || personal.Form != 0
            || usedSpeciesForms.Contains((species, form))
            || bossSpeciesForms.Contains((species, form))
            || IsSpecialNormalRouteSpecies(species))
        {
            return false;
        }

        return !personal.CanNotDynamax;
    }

    public static SwShPersonalRecord? ResolvePersonalRecord(
        int species,
        int form,
        IReadOnlyList<SwShPersonalRecord> personalRecords)
    {
        if ((uint)species >= (uint)personalRecords.Count)
        {
            return null;
        }

        var record = personalRecords[species];
        if (form <= 0 || record.FormStatsIndex <= 0)
        {
            return record;
        }

        var formPersonalId = record.FormStatsIndex + form - 1;
        return (uint)formPersonalId < (uint)personalRecords.Count
            ? personalRecords[formPersonalId]
            : record;
    }

    public static bool CanLearnMove(
        SwShPersonalRecord personal,
        SwShPokemonLearnsetRecord? learnset,
        int moveId,
        int level)
    {
        return learnset?.Moves.Any(move => move.MoveId == moveId && move.Level <= level) == true
            || HasFlaggedCompatibility(personal.TechnicalMachines, TechnicalMachineMoveIds, moveId)
            || HasFlaggedCompatibility(personal.TechnicalRecords, TechnicalRecordMoveIds, moveId)
            || HasFlaggedCompatibility(personal.TypeTutors, TypeTutorMoveIds, moveId)
            || HasFlaggedCompatibility(personal.ArmorTutors, ArmorTutorMoveIds, moveId);
    }

    private static bool HasFlaggedCompatibility(IReadOnlyList<bool> flags, IReadOnlyList<int> moveIds, int moveId)
    {
        for (var index = 0; index < flags.Count && index < moveIds.Count; index++)
        {
            if (flags[index] && moveIds[index] == moveId)
            {
                return true;
            }
        }

        return false;
    }
}
