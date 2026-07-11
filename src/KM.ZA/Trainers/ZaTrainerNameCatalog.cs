// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.ZA.Trainers;

internal static class ZaTrainerNameCatalog
{
    private const string RivalNameKeys = "rival_01|rival_02";

    private static readonly IReadOnlyDictionary<string, string> EventTrainerNameKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ev_d00_1000_02"] = "executive01",
            ["Ev_d00_1000_02_strong"] = "executive01",
            ["Ev_d02_0010_02"] = "executive02",
            ["Ev_d02_0010_02_strong"] = "executive02",
            ["Ev_m01_0070"] = "alias01",
            ["Ev_m01_0110"] = "alias03",
            ["Ev_m02_0030_hono"] = RivalNameKeys,
            ["Ev_m02_0030_kusa"] = RivalNameKeys,
            ["Ev_m02_0030_mizu"] = RivalNameKeys,
            ["Ev_m02_2090"] = "rank_z",
            ["Ev_m03_0090"] = "battleg",
            ["Ev_m03_0125"] = "detective",
            ["Ev_m03_0500"] = "rank_y",
            ["Ev_m03_1300"] = "friend_01",
            ["Ev_m03_1500"] = "rank_x",
            ["Ev_m03_2300"] = "friend_02",
            ["Ev_m03_2500"] = "rank_w",
            ["Ev_m04_0030_hono"] = RivalNameKeys,
            ["Ev_m04_0030_kusa"] = RivalNameKeys,
            ["Ev_m04_0030_mizu"] = RivalNameKeys,
            ["Ev_m04_0070"] = "secretary",
            ["Ev_m04_3620"] = "friend_01",
            ["Ev_m04_4200"] = "mania",
            ["Ev_m04_5010"] = "executive01",
            ["Ev_m04_5040"] = "boss01",
            ["Ev_m04_boss_0071_multi"] = "friend_01",
            ["Ev_m04_boss_0080_multi_hono"] = RivalNameKeys,
            ["Ev_m04_boss_0080_multi_kusa"] = RivalNameKeys,
            ["Ev_m04_boss_0080_multi_mizu"] = RivalNameKeys,
            ["Ev_m04_boss_0323_multi"] = "friend_02",
            ["Ev_m04_multi_hono"] = RivalNameKeys,
            ["Ev_m04_multi_kusa"] = RivalNameKeys,
            ["Ev_m04_multi_mizu"] = RivalNameKeys,
            ["Ev_m05_1100"] = "battleg",
            ["Ev_m05_1500"] = "boss01",
            ["Ev_m05_2610"] = "executive02",
            ["Ev_m05_3000"] = "boss02",
            ["Ev_m05_boss_0354_multi"] = "friend_01",
            ["Ev_m06_1000"] = "TRNAME_STAR_M",
            ["Ev_m06_1100"] = "executive03",
            ["Ev_m06_2510"] = "executive02",
            ["Ev_m06_2955_01"] = "celebritym01",
            ["Ev_m06_2955_02"] = "celebrityw01",
            ["Ev_m06_2970"] = "representative",
            ["Ev_m06_4000"] = "boss03",
            ["Ev_m07_0026"] = "friend_01",
            ["Ev_m07_1100"] = "kimono",
            ["Ev_m07_4810"] = "celebrityw02",
            ["Ev_m07_4820"] = "celebritym02",
            ["Ev_m07_4830"] = "executive04",
            ["Ev_m07_5100"] = "boss01",
            ["Ev_m07_5200"] = "boss02",
            ["Ev_m07_5300"] = "boss03",
            ["Ev_m07_5510"] = "boss04",
            ["Ev_m07_boss_0478_multi"] = "friend_01",
            ["Ev_m08_3500"] = "executive03",
            ["Ev_m08_4010"] = "detective",
            ["Ev_m08_4310"] = "executive05",
            ["Ev_m08_7000"] = "boss05",
            ["Ev_m08_boss_0248_multi"] = "friend_01",
            ["Ev_m09_1000_hono"] = RivalNameKeys,
            ["Ev_m09_1000_kusa"] = RivalNameKeys,
            ["Ev_m09_1000_mizu"] = RivalNameKeys,
            ["Ev_m10_2000_hono"] = RivalNameKeys,
            ["Ev_m10_2000_kusa"] = RivalNameKeys,
            ["Ev_m10_2000_mizu"] = RivalNameKeys,
            ["Ev_m10_9710"] = "oldboss",
            ["Ev_sub_140_020_hono"] = RivalNameKeys,
            ["Ev_sub_140_020_kusa"] = RivalNameKeys,
            ["Ev_sub_140_020_mizu"] = RivalNameKeys,
            ["Ev_sub_142_010"] = "friend_01",
            ["Ev_sub_161_010_02"] = "executive01",
            ["Ev_sub_162_010_02"] = "executive02",
            ["Ev_sub_166_010_multi"] = "executive02",
            ["Ev_sub_167_010_02"] = "executive02",
        };

    private static readonly string?[] InfiniteRosterNameKeys =
    [
        null,
        null,
        "detective",
        "friend_01",
        "friend_02",
        "secretary",
        "boss01",
        "executive01",
        "boss02",
        "executive02",
        "boss03",
        "executive03",
        "boss04",
        "executive04",
        "boss05",
        "executive05",
        "rank_z",
        "rank_y",
        "rank_x",
        "rank_w",
        "battleg",
        "mania",
        "kimono",
        "alias01",
        "xyleader3_01",
    ];

    private static readonly string[] RankInfinityOddNameKeys =
    [
        "za_rank_x_19",
        "za_rank_b_14",
        "za_rank_d_12",
        "za_rank_b_23",
        "za_rank_b_27",
        "za_rank_u_06",
        "za_rank_e_06",
        "za_rank_v_05",
        "za_rank_y_08",
        "za_rank_u_04",
        "za_rank_g_15",
        "za_rank_f_11",
        "za_rank_w_01",
        "za_rank_x_07",
        "za_rank_b_13",
        "za_rank_b_04",
        "za_rank_g_10",
        "za_rank_e_04",
        "za_rank_y_01",
        "za_rank_x_02",
        "za_rank_z_01",
        "za_rank_f_10",
        "za_rank_e_09",
        "za_rank_d_08",
        "za_rank_f_12",
        "za_rank_z_02",
        "za_rank_d_13",
        "za_rank_b_09",
        "za_rank_x_10",
        "za_rank_d_07",
        "za_rank_b_35",
        "za_rank_b_28",
        "za_rank_v_07",
        "za_rank_g_11",
        "za_rank_g_25",
        "alias03",
        "za_rank_b_26",
        "za_rank_g_21",
        "za_rank_d_14",
        "za_rank_c_14",
    ];

    private static readonly string[] RankInfinityEvenNameKeys =
    [
        "za_rank_u_10",
        "za_rank_g_23",
        "za_rank_d_10",
        "za_rank_d_18",
        "za_rank_b_32",
        "za_rank_b_21",
        "za_rank_b_21",
        "za_rank_c_17",
        "za_rank_b_11",
        "za_rank_b_36",
        "za_rank_b_25",
        "workerm",
        "za_rank_g_20",
        "za_rank_e_15",
        "za_rank_f_15",
        "za_rank_b_19",
        "za_rank_z_strong_01",
        "za_rank_b_31",
        "za_rank_f_07",
        "za_rank_f_02",
        "za_rank_b_07",
        "za_rank_b_21",
        "za_rank_b_21",
        "za_rank_f_16",
        "za_rank_e_18",
        "za_rank_b_37",
        "za_rank_b_10",
        "za_rank_b_34",
        "za_rank_c_03",
        "za_rank_w_10",
        "za_rank_y_02",
        "za_rank_d_05",
        "za_rank_f_03",
        "za_rank_x_20",
        "za_rank_b_08",
        "nuvo01",
        "nuvo02",
        "za_rank_b_24",
        "celebritym02",
        "celebrityw01",
    ];

    public static bool IsHyperspaceTrainer(string? trainerId)
    {
        return !string.IsNullOrWhiteSpace(trainerId)
            && trainerId.StartsWith("dim_rank_", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ResolveTrainerNameKeys(string? trainerId)
    {
        if (string.IsNullOrWhiteSpace(trainerId))
        {
            return [];
        }

        if (EventTrainerNameKeys.TryGetValue(trainerId, out var eventNameKeys))
        {
            return SplitNameKeys(eventNameKeys);
        }

        var infiniteKeys = ResolveInfiniteBattleNameKeys(trainerId);
        if (infiniteKeys.Count > 0)
        {
            return infiniteKeys;
        }

        return ResolveRankInfinityNameKeys(trainerId);
    }

    private static IReadOnlyList<string> ResolveInfiniteBattleNameKeys(string trainerId)
    {
        if (TryParseTrainerIndex(trainerId, "za_inf_strongest_", out var strongestIndex))
        {
            return ResolveInfiniteRosterNameKeys(strongestIndex, maximumIndex: 24, firstSlotIsRival: true);
        }

        if (TryParseTrainerIndex(trainerId, "za_inf_strong_", out var strongIndex))
        {
            return ResolveInfiniteRosterNameKeys(strongIndex, maximumIndex: 23, firstSlotIsRival: true);
        }

        if (TryParseTrainerIndex(trainerId, "za_inf_", out var index))
        {
            return ResolveInfiniteRosterNameKeys(index, maximumIndex: 22, firstSlotIsRival: false);
        }

        return [];
    }

    private static IReadOnlyList<string> ResolveInfiniteRosterNameKeys(
        int index,
        int maximumIndex,
        bool firstSlotIsRival)
    {
        if (firstSlotIsRival && index == 1)
        {
            return SplitNameKeys(RivalNameKeys);
        }

        if (index < 2 || index > maximumIndex || index >= InfiniteRosterNameKeys.Length)
        {
            return [];
        }

        var key = InfiniteRosterNameKeys[index];
        return string.IsNullOrWhiteSpace(key) ? [] : [key];
    }

    private static IReadOnlyList<string> ResolveRankInfinityNameKeys(string trainerId)
    {
        var parts = trainerId.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !parts[0].Equals("za", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("rank", StringComparison.OrdinalIgnoreCase)
            || !parts[2].StartsWith("inf", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[2]["inf".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var group)
            || group is < 1 or > 4
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index < 1)
        {
            return [];
        }

        var keys = group % 2 == 1
            ? RankInfinityOddNameKeys
            : RankInfinityEvenNameKeys;
        return index <= keys.Length ? [keys[index - 1]] : [];
    }

    private static bool TryParseTrainerIndex(string trainerId, string prefix, out int index)
    {
        index = 0;
        if (!trainerId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = trainerId[prefix.Length..];
        var separator = suffix.IndexOf('_', StringComparison.Ordinal);
        var indexToken = separator < 0 ? suffix : suffix[..separator];
        return int.TryParse(indexToken, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    private static IReadOnlyList<string> SplitNameKeys(string keys)
    {
        return keys.Split(
            '|',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
