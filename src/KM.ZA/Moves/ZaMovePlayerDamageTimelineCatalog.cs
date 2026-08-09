// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;

namespace KM.ZA.Moves;

/// <summary>
/// Verified vanilla launch evidence extracted from exact Title/Bullet/Shoot actions in
/// battle effect timelines. Damage rows without a reachable timeline launch are absent.
/// </summary>
internal static class ZaMovePlayerDamageTimelineCatalog
{
    private const string VerifiedBaseBulletCatalogSha256 =
        "6C0156A8F205820EFC091D1783139F9449E8497154F973E7F61973B9D43BB739";

    private static readonly IReadOnlyDictionary<
        (int AttackId, int DamageBulletId),
        IReadOnlyList<CatalogTimelineLaunchRecord>> LaunchesByDamageRow =
        new Dictionary<
            (int AttackId, int DamageBulletId),
            IReadOnlyList<CatalogTimelineLaunchRecord>>
        {
            [(16, 21)] =
            [
                new(21, "emw0076_0", "ik_effect/data/battle/emw00/emw0076/emw0076_0.trtml", null),
                new(21, "emw0076", "ik_effect/data/battle/emw00/emw0076/emw0076.trtml", null),
            ],
            [(17, 22)] =
            [
                new(22, "emw0076_1", "ik_effect/data/battle/emw00/emw0076/emw0076_1.trtml", null),
                new(22, "emw0076", "ik_effect/data/battle/emw00/emw0076/emw0076.trtml", "Heat2_Not_Performed"),
            ],
            [(838, 689)] =
            [
                new(689, "emw0022", "ik_effect/data/battle/emw00/emw0022/emw0022.trtml", null),
            ],
            [(845, 696)] =
            [
                new(696, "emw0029", "ik_effect/data/battle/emw00/emw0029/emw0029.trtml", null),
            ],
            [(852, 703)] =
            [
                new(703, "emw0036", "ik_effect/data/battle/emw00/emw0036/emw0036.trtml", null),
            ],
            [(854, 705)] =
            [
                new(705, "emw0038", "ik_effect/data/battle/emw00/emw0038/emw0038.trtml", null),
            ],
            [(858, 709)] =
            [
                new(709, "emw0042", "ik_effect/data/battle/emw00/emw0042/emw0042.trtml", null),
                new(709, "emw0042", "ik_effect/data/battle/emw00/emw0042/emw0042.trtml", "Heat2_Not_Performed"),
            ],
            [(860, 711)] =
            [
                new(711, "emw0044", "ik_effect/data/battle/emw00/emw0044/emw0044.trtml", null),
            ],
            [(872, 723)] =
            [
                new(723, "emw0056", "ik_effect/data/battle/emw00/emw0056/emw0056.trtml", null),
            ],
            [(873, 724)] =
            [
                new(724, "emw0057", "ik_effect/data/battle/emw00/emw0057/emw0057.trtml", null),
                new(724, "emw0057", "ik_effect/data/battle/emw00/emw0057/emw0057.trtml", "BosoKaiooga"),
            ],
            [(874, 725)] =
            [
                new(725, "emw0058", "ik_effect/data/battle/emw00/emw0058/emw0058.trtml", null),
                new(725, "emw0058", "ik_effect/data/battle/emw00/emw0058/emw0058.trtml", "Poke0952"),
            ],
            [(875, 726)] =
            [
                new(726, "emw0059", "ik_effect/data/battle/emw00/emw0059/emw0059.trtml", null),
            ],
            [(876, 727)] =
            [
                new(727, "emw0060", "ik_effect/data/battle/emw00/emw0060/emw0060.trtml", null),
            ],
            [(879, 730)] =
            [
                new(730, "emw0063", "ik_effect/data/battle/emw00/emw0063/emw0063.trtml", null),
            ],
            [(904, 755)] =
            [
                new(755, "emw0089", "ik_effect/data/battle/emw00/emw0089/emw0089.trtml", null),
            ],
            [(924, 775)] =
            [
                new(775, "emw0109", "ik_effect/data/battle/emw01/emw0109/emw0109.trtml", null),
            ],
            [(937, 788)] =
            [
                new(788, "emw0122", "ik_effect/data/battle/emw01/emw0122/emw0122.trtml", null),
            ],
            [(941, 792)] =
            [
                new(792, "emw0126", "ik_effect/data/battle/emw01/emw0126/emw0126.trtml", null),
                new(792, "emw0126", "ik_effect/data/battle/emw01/emw0126/emw0126.trtml", "BosoGuraadon"),
            ],
            [(942, 793)] =
            [
                new(793, "emw0127_u", "ik_effect/data/battle/emw01/emw0127_u/emw0127_u.trtml", null),
                new(793, "emw0127", "ik_effect/data/battle/emw01/emw0127/emw0127.trtml", null),
            ],
            [(978, 829)] =
            [
                new(829, "emw0163", "ik_effect/data/battle/emw01/emw0163/emw0163.trtml", null),
            ],
            [(1003, 854)] =
            [
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", null),
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_1A"),
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_1B"),
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_1C"),
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_2A"),
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_2B"),
            ],
            [(1038, 889)] =
            [
                new(889, "emw0223", "ik_effect/data/battle/emw02/emw0223/emw0223.trtml", null),
            ],
            [(1040, 891)] =
            [
                new(891, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null),
            ],
            [(1054, 905)] =
            [
                new(905, "emw0239", "ik_effect/data/battle/emw02/emw0239/emw0239.trtml", null),
                new(905, "emw0239", "ik_effect/data/battle/emw02/emw0239/emw0239.trtml", "Heat2_Not_Performed"),
            ],
            [(1057, 908)] =
            [
                new(908, "emw0242", "ik_effect/data/battle/emw02/emw0242/emw0242.trtml", null),
            ],
            [(1062, 913)] =
            [
                new(913, "emw0247", "ik_effect/data/battle/emw02/emw0247/emw0247.trtml", null),
            ],
            [(1065, 916)] =
            [
                new(916, "emw0250", "ik_effect/data/battle/emw02/emw0250/emw0250.trtml", null),
                new(916, "emw0250", "ik_effect/data/battle/emw02/emw0250/emw0250.trtml", "Heat2_Not_Performed"),
            ],
            [(1076, 927)] =
            [
                new(927, "emw0261", "ik_effect/data/battle/emw02/emw0261/emw0261.trtml", null),
            ],
            [(1095, 946)] =
            [
                new(946, "emw0280", "ik_effect/data/battle/emw02/emw0280/emw0280.trtml", null),
            ],
            [(1097, 948)] =
            [
                new(948, "emw0282", "ik_effect/data/battle/emw02/emw0282/emw0282.trtml", null),
            ],
            [(1130, 981)] =
            [
                new(981, "emw0315", "ik_effect/data/battle/emw03/emw0315/emw0315.trtml", null),
            ],
            [(1146, 997)] =
            [
                new(997, "emw0331", "ik_effect/data/battle/emw03/emw0331/emw0331.trtml", null),
                new(997, "emw0331", "ik_effect/data/battle/emw03/emw0331/emw0331.trtml", "BosoMega_Control_1A"),
                new(997, "emw0331", "ik_effect/data/battle/emw03/emw0331/emw0331.trtml", "BosoMega_Control_1B"),
                new(997, "emw0331", "ik_effect/data/battle/emw03/emw0331/emw0331.trtml", "BosoMega_Control_1C"),
                new(997, "emw0331", "ik_effect/data/battle/emw03/emw0331/emw0331.trtml", "BosoMega_Control_2A"),
                new(997, "emw0331", "ik_effect/data/battle/emw03/emw0331/emw0331.trtml", "BosoMega_Control_2B"),
            ],
            [(1147, 998)] =
            [
                new(998, "emw0332", "ik_effect/data/battle/emw03/emw0332/emw0332.trtml", null),
            ],
            [(1185, 1036)] =
            [
                new(1036, "emw0370", "ik_effect/data/battle/emw03/emw0370/emw0370.trtml", null),
            ],
            [(1213, 1064)] =
            [
                new(1064, "emw0398", "ik_effect/data/battle/emw03/emw0398/emw0398.trtml", null),
            ],
            [(1214, 1065)] =
            [
                new(1065, "emw0399", "ik_effect/data/battle/emw03/emw0399/emw0399.trtml", null),
            ],
            [(1218, 1069)] =
            [
                new(1069, "emw0403", "ik_effect/data/battle/emw04/emw0403/emw0403.trtml", "Poke0149"),
            ],
            [(1226, 1077)] =
            [
                new(1077, "emw0411", "ik_effect/data/battle/emw04/emw0411/emw0411.trtml", null),
            ],
            [(1228, 1079)] =
            [
                new(1079, "emw0413", "ik_effect/data/battle/emw04/emw0413/emw0413.trtml", null),
            ],
            [(1243, 1094)] =
            [
                new(1094, "emw0428", "ik_effect/data/battle/emw04/emw0428/emw0428.trtml", null),
            ],
            [(1245, 1096)] =
            [
                new(1096, "emw0430", "ik_effect/data/battle/emw04/emw0430/emw0430.trtml", null),
            ],
            [(1250, 1101)] =
            [
                new(1101, "emw0435", "ik_effect/data/battle/emw04/emw0435/emw0435.trtml", null),
            ],
            [(1251, 1102)] =
            [
                new(1102, "emw0436", "ik_effect/data/battle/emw04/emw0436/emw0436.trtml", "Heat2_Not_Performed"),
            ],
            [(1257, 1108)] =
            [
                new(1108, "emw0442", "ik_effect/data/battle/emw04/emw0442/emw0442.trtml", null),
            ],
            [(1279, 1130)] =
            [
                new(1130, "emw0464", "ik_effect/data/battle/emw04/emw0464/emw0464.trtml", "BosoDaakurai"),
            ],
            [(1297, 1148)] =
            [
                new(1148, "emw0482", "ik_effect/data/battle/emw04/emw0482/emw0482.trtml", null),
            ],
            [(1343, 1194)] =
            [
                new(1194, "emw0528", "ik_effect/data/battle/emw05/emw0528/emw0528.trtml", null),
            ],
            [(1357, 1208)] =
            [
                new(1208, "emw0542", "ik_effect/data/battle/emw05/emw0542/emw0542.trtml", null),
            ],
            [(1357, 1982)] =
            [
                new(1982, "emw0542", "ik_effect/data/battle/emw05/emw0542/emw0542.trtml", "Heat2_Not_Performed"),
            ],
            [(1370, 1221)] =
            [
                new(1221, "emw0555", "ik_effect/data/battle/emw05/emw0555/emw0555.trtml", null),
                new(1221, "emw0555", "ik_effect/data/battle/emw05/emw0555/emw0555.trtml", "BosoDaakurai"),
                new(1221, "emw0555", "ik_effect/data/battle/emw05/emw0555/emw0555.trtml", "BosoMegaDaakurai"),
            ],
            [(1381, 1232)] =
            [
                new(1232, "emw0566", "ik_effect/data/battle/emw05/emw0566/emw0566.trtml", null),
            ],
            [(1400, 1251)] =
            [
                new(1251, "emw0585", "ik_effect/data/battle/emw05/emw0585/emw0585.trtml", null),
            ],
            [(1420, 1271)] =
            [
                new(1271, "emw0605", "ik_effect/data/battle/emw06/emw0605/emw0605.trtml", null),
            ],
            [(1427, 1278)] =
            [
                new(1278, "emw0612_u", "ik_effect/data/battle/emw06/emw0612_u/emw0612_u.trtml", null),
                new(1278, "emw0612", "ik_effect/data/battle/emw06/emw0612/emw0612.trtml", null),
            ],
            [(1430, 1281)] =
            [
                new(1281, "emw0615", "ik_effect/data/battle/emw06/emw0615/emw0615.trtml", null),
            ],
            [(1433, 1284)] =
            [
                new(1284, "emw0618", "ik_effect/data/battle/emw06/emw0618/emw0618.trtml", null),
            ],
            [(1435, 1286)] =
            [
                new(1286, "emw0620", "ik_effect/data/battle/emw06/emw0620/emw0620.trtml", null),
            ],
            [(1502, 1353)] =
            [
                new(1353, "emw0687", "ik_effect/data/battle/emw06/emw0687/emw0687.trtml", null),
            ],
            [(1508, 1359)] =
            [
                new(1359, "emw0693", "ik_effect/data/battle/emw06/emw0693/emw0693.trtml", null),
                new(1359, "emw0693", "ik_effect/data/battle/emw06/emw0693/emw0693.trtml", "BosoDaakurai"),
            ],
            [(1536, 1387)] =
            [
                new(1387, "emw0721", "ik_effect/data/battle/emw07/emw0721/emw0721.trtml", null),
            ],
            [(1599, 1450)] =
            [
                new(1450, "emw0784", "ik_effect/data/battle/emw07/emw0784/emw0784.trtml", null),
            ],
            [(1615, 1466)] =
            [
                new(1466, "emw0800", "ik_effect/data/battle/emw08/emw0800/emw0800.trtml", null),
            ],
            [(1819, 1670)] =
            [
                new(1670, "emw0436_3", "ik_effect/data/battle/emw04/emw0436_3/emw0436_3.trtml", null),
            ],
            [(1820, 1671)] =
            [
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(854, 1671, "landing"),
                        ],
                    ],
                },
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_1A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(854, 1671, "landing"),
                        ],
                    ],
                },
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_1B")
                {
                    RelationshipPaths =
                    [
                        [
                            new(854, 1671, "landing"),
                        ],
                    ],
                },
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_1C")
                {
                    RelationshipPaths =
                    [
                        [
                            new(854, 1671, "landing"),
                        ],
                    ],
                },
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_2A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(854, 1671, "landing"),
                        ],
                    ],
                },
                new(854, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", "BosoMega_Control_2B")
                {
                    RelationshipPaths =
                    [
                        [
                            new(854, 1671, "landing"),
                        ],
                    ],
                },
            ],
            [(1821, 1672)] =
            [
                new(1757, "emw0188", "ik_effect/data/battle/emw01/emw0188/emw0188.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1757, 1672, "landing"),
                        ],
                    ],
                },
            ],
            [(1889, 1744)] =
            [
                new(1018, "emw0352", "ik_effect/data/battle/emw03/emw0352/emw0352.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1018, 1744, "child"),
                        ],
                    ],
                },
            ],
            [(1908, 1763)] =
            [
                new(709, "emw0042", "ik_effect/data/battle/emw00/emw0042/emw0042.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(709, 1763, "landing"),
                        ],
                    ],
                },
                new(709, "emw0042", "ik_effect/data/battle/emw00/emw0042/emw0042.trtml", "Heat2_Not_Performed")
                {
                    RelationshipPaths =
                    [
                        [
                            new(709, 1763, "landing"),
                        ],
                    ],
                },
                new(1762, "emw0042", "ik_effect/data/battle/emw00/emw0042/emw0042.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1762, 1763, "landing"),
                        ],
                    ],
                },
            ],
            [(1909, 1764)] =
            [
                new(1764, "emw0679", "ik_effect/data/battle/emw06/emw0679/emw0679.trtml", null),
            ],
            [(1914, 1769)] =
            [
                new(1769, "emw0036", "ik_effect/data/battle/emw00/emw0036/emw0036.trtml", "Heat2_Not_Performed"),
            ],
            [(1922, 1777)] =
            [
                new(1091, "emw0425", "ik_effect/data/battle/emw04/emw0425/emw0425.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1091, 1777, "child"),
                        ],
                    ],
                },
            ],
            [(1928, 1783)] =
            [
                new(1080, "emw0414", "ik_effect/data/battle/emw04/emw0414/emw0414.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1080, 1783, "child"),
                        ],
                    ],
                },
            ],
            [(1929, 1784)] =
            [
                new(1249, "emw0583", "ik_effect/data/battle/emw05/emw0583/emw0583.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1249, 1784, "child"),
                        ],
                    ],
                },
            ],
            [(1930, 1785)] =
            [
                new(1072, "emw0406", "ik_effect/data/battle/emw04/emw0406/emw0406.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1072, 1785, "child"),
                        ],
                    ],
                },
            ],
            [(1931, 1786)] =
            [
                new(753, "emw0087", "ik_effect/data/battle/emw00/emw0087/emw0087.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(753, 1786, "child"),
                        ],
                    ],
                },
                new(753, "emw0087", "ik_effect/data/battle/emw00/emw0087/emw0087.trtml", "BosoMega_Control_1A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(753, 1786, "child"),
                        ],
                    ],
                },
                new(753, "emw0087", "ik_effect/data/battle/emw00/emw0087/emw0087.trtml", "BosoMega_Control_1B")
                {
                    RelationshipPaths =
                    [
                        [
                            new(753, 1786, "child"),
                        ],
                    ],
                },
                new(753, "emw0087", "ik_effect/data/battle/emw00/emw0087/emw0087.trtml", "BosoMega_Control_1C")
                {
                    RelationshipPaths =
                    [
                        [
                            new(753, 1786, "child"),
                        ],
                    ],
                },
                new(753, "emw0087", "ik_effect/data/battle/emw00/emw0087/emw0087.trtml", "BosoMega_Control_2A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(753, 1786, "child"),
                        ],
                    ],
                },
                new(753, "emw0087", "ik_effect/data/battle/emw00/emw0087/emw0087.trtml", "BosoMega_Control_2B")
                {
                    RelationshipPaths =
                    [
                        [
                            new(753, 1786, "child"),
                        ],
                    ],
                },
            ],
            [(1933, 1788)] =
            [
                new(1788, "emw0435_2", "ik_effect/data/battle/emw04/emw0435_2/emw0435_2.trtml", null),
            ],
            [(1941, 1796)] =
            [
                new(1796, "emw0250", "ik_effect/data/battle/emw02/emw0250/emw0250.trtml", null),
            ],
            [(1942, 1797)] =
            [
                new(1797, "emw0250", "ik_effect/data/battle/emw02/emw0250/emw0250.trtml", null),
            ],
            [(1943, 1798)] =
            [
                new(1798, "emw0250", "ik_effect/data/battle/emw02/emw0250/emw0250.trtml", null),
            ],
            [(1947, 1802)] =
            [
                new(1802, "emw0421", "ik_effect/data/battle/emw04/emw0421/emw0421.trtml", null),
            ],
            [(1953, 1808)] =
            [
                new(1808, "emw0399", "ik_effect/data/battle/emw03/emw0399/emw0399.trtml", "Heat2_Not_Performed"),
            ],
            [(1970, 1825)] =
            [
                new(1825, "emw0560", "ik_effect/data/battle/emw05/emw0560/emw0560.trtml", null),
            ],
            [(1973, 1828)] =
            [
                new(1828, "emw0605", "ik_effect/data/battle/emw06/emw0605/emw0605.trtml", null),
            ],
            [(1974, 1829)] =
            [
                new(1271, "emw0605", "ik_effect/data/battle/emw06/emw0605/emw0605.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1271, 1829, "child"),
                        ],
                        [
                            new(1271, 1829, "landing"),
                        ],
                    ],
                },
            ],
            [(1975, 1830)] =
            [
                new(1828, "emw0605", "ik_effect/data/battle/emw06/emw0605/emw0605.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1828, 1830, "landing"),
                        ],
                    ],
                },
            ],
            [(1976, 1831)] =
            [
                new(1831, "emw0560", "ik_effect/data/battle/emw05/emw0560/emw0560.trtml", null),
            ],
            [(1982, 1837)] =
            [
                new(1837, "emw0560", "ik_effect/data/battle/emw05/emw0560/emw0560.trtml", null),
            ],
            [(1985, 1840)] =
            [
                new(1840, "emw0157_1_bullet", "ik_effect/data/battle/emw01/emw0157/emw0157_1_bullet.trtml", null),
            ],
            [(2000, 1855)] =
            [
                new(1855, "emw0370", "ik_effect/data/battle/emw03/emw0370/emw0370.trtml", null),
            ],
            [(2002, 1857)] =
            [
                new(862, "emw0196", "ik_effect/data/battle/emw01/emw0196/emw0196.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(862, 1857, "child"),
                        ],
                    ],
                },
                new(862, "emw0196", "ik_effect/data/battle/emw01/emw0196/emw0196.trtml", "BosoMega_Control_1A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(862, 1857, "child"),
                        ],
                    ],
                },
                new(862, "emw0196", "ik_effect/data/battle/emw01/emw0196/emw0196.trtml", "BosoMega_Control_1B")
                {
                    RelationshipPaths =
                    [
                        [
                            new(862, 1857, "child"),
                        ],
                    ],
                },
                new(862, "emw0196", "ik_effect/data/battle/emw01/emw0196/emw0196.trtml", "BosoMega_Control_1C")
                {
                    RelationshipPaths =
                    [
                        [
                            new(862, 1857, "child"),
                        ],
                    ],
                },
                new(862, "emw0196", "ik_effect/data/battle/emw01/emw0196/emw0196.trtml", "BosoMega_Control_2A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(862, 1857, "child"),
                        ],
                    ],
                },
                new(862, "emw0196", "ik_effect/data/battle/emw01/emw0196/emw0196.trtml", "BosoMega_Control_2B")
                {
                    RelationshipPaths =
                    [
                        [
                            new(862, 1857, "child"),
                        ],
                    ],
                },
                new(862, "emw0196", "ik_effect/data/battle/emw01/emw0196/emw0196.trtml", "Heat2_Performed")
                {
                    RelationshipPaths =
                    [
                        [
                            new(862, 1857, "child"),
                        ],
                    ],
                },
            ],
            [(2045, 1900)] =
            [
                new(1900, "emw0453", "ik_effect/data/battle/emw04/emw0453/emw0453.trtml", null),
            ],
            [(2048, 1903)] =
            [
                new(1903, "emw0453", "ik_effect/data/battle/emw04/emw0453/emw0453.trtml", null),
            ],
            [(2100, 1955)] =
            [
                new(970, "emw0304", "ik_effect/data/battle/emw03/emw0304/emw0304.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(970, 1955, "child"),
                        ],
                    ],
                },
            ],
            [(2101, 1956)] =
            [
                new(1956, "emw0239", "ik_effect/data/battle/emw02/emw0239/emw0239.trtml", null),
            ],
            [(2102, 1957)] =
            [
                new(1957, "emw0239", "ik_effect/data/battle/emw02/emw0239/emw0239.trtml", null),
            ],
            [(2103, 1958)] =
            [
                new(1958, "emw0370", "ik_effect/data/battle/emw03/emw0370/emw0370.trtml", null),
            ],
            [(2110, 1965)] =
            [
                new(1965, "emw0200_u", "ik_effect/data/battle/emw02/emw0200_u/emw0200_u.trtml", null),
                new(1965, "emw0200", "ik_effect/data/battle/emw02/emw0200/emw0200.trtml", null),
            ],
            [(2112, 1967)] =
            [
                new(1967, "emw0416", "ik_effect/data/battle/emw04/emw0416/emw0416.trtml", null),
            ],
            [(2121, 1976)] =
            [
                new(994, "emw0328", "ik_effect/data/battle/emw03/emw0328/emw0328.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(994, 1976, "child"),
                        ],
                    ],
                },
                new(994, "emw0328", "ik_effect/data/battle/emw03/emw0328/emw0328.trtml", "BosoMega_Control_1A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(994, 1976, "child"),
                        ],
                    ],
                },
                new(994, "emw0328", "ik_effect/data/battle/emw03/emw0328/emw0328.trtml", "BosoMega_Control_1B")
                {
                    RelationshipPaths =
                    [
                        [
                            new(994, 1976, "child"),
                        ],
                    ],
                },
            ],
            [(2124, 1979)] =
            [
                new(1104, "emw0438", "ik_effect/data/battle/emw04/emw0438/emw0438.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1104, 1979, "child"),
                        ],
                    ],
                },
                new(1979, "emw0438", "ik_effect/data/battle/emw04/emw0438/emw0438.trtml", null),
            ],
            [(2129, 1984)] =
            [
                new(751, "emw0085", "ik_effect/data/battle/emw00/emw0085/emw0085.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(751, 1984, "child"),
                        ],
                    ],
                },
            ],
            [(2130, 1985)] =
            [
                new(1073, "emw0407_u", "ik_effect/data/battle/emw04/emw0407_u/emw0407_u.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1073, 1985, "child"),
                        ],
                    ],
                },
                new(1073, "emw0407", "ik_effect/data/battle/emw04/emw0407/emw0407.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1073, 1985, "child"),
                        ],
                    ],
                },
            ],
            [(2138, 1993)] =
            [
                new(1993, "emw0063", "ik_effect/data/battle/emw00/emw0063/emw0063.trtml", null),
            ],
            [(2140, 1995)] =
            [
                new(1994, "emw0063", "ik_effect/data/battle/emw00/emw0063/emw0063.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1994, 1995, "landing"),
                        ],
                    ],
                },
            ],
            [(2141, 1996)] =
            [
                new(720, "emw0053", "ik_effect/data/battle/emw00/emw0053/emw0053.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(720, 1996, "child"),
                        ],
                    ],
                },
            ],
            [(2142, 1997)] =
            [
                new(1110, "emw0444", "ik_effect/data/battle/emw04/emw0444/emw0444.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1110, 1974, "child"),
                            new(1974, 1997, "child"),
                        ],
                    ],
                },
                new(1110, "emw0444", "ik_effect/data/battle/emw04/emw0444/emw0444.trtml", "Heat2_Not_Performed")
                {
                    RelationshipPaths =
                    [
                        [
                            new(1110, 1974, "child"),
                            new(1974, 1997, "child"),
                        ],
                    ],
                },
            ],
            [(2186, 2044)] =
            [
                new(1282, "emw0616", "ik_effect/data/battle/emw06/emw0616/emw0616.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1282, 2044, "child"),
                        ],
                    ],
                },
                new(1282, "emw0616", "ik_effect/data/battle/emw06/emw0616/emw0616.trtml", "Zigarude_100%")
                {
                    RelationshipPaths =
                    [
                        [
                            new(1282, 2044, "child"),
                        ],
                    ],
                },
            ],
            [(2187, 2045)] =
            [
                new(2045, "emw0687", "ik_effect/data/battle/emw06/emw0687/emw0687.trtml", null),
            ],
            [(2188, 2046)] =
            [
                new(2046, "emw0687", "ik_effect/data/battle/emw06/emw0687/emw0687.trtml", null),
            ],
            [(2248, 2106)] =
            [
                new(2106, "emw0556_4_bullet", "ik_effect/data/battle/emw05/emw0556/emw0556_4_bullet.trtml", null),
            ],
            [(2288, 2196)] =
            [
                new(2196, "emw0036", "ik_effect/data/battle/emw00/emw0036/emw0036.trtml", null),
            ],
            [(2297, 2205)] =
            [
                new(1980, "emw0063", "ik_effect/data/battle/emw00/emw0063/emw0063.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1980, 1981, "landing"),
                            new(1981, 2205, "child"),
                        ],
                    ],
                },
            ],
            [(2298, 2206)] =
            [
                new(1980, "emw0063", "ik_effect/data/battle/emw00/emw0063/emw0063.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1980, 1981, "landing"),
                            new(1981, 2205, "child"),
                            new(2205, 2206, "child"),
                        ],
                    ],
                },
            ],
            [(2299, 2001)] =
            [
                new(2001, "emw0403", "ik_effect/data/battle/emw04/emw0403/emw0403.trtml", null),
            ],
            [(2300, 2002)] =
            [
                new(2002, "emw0403", "ik_effect/data/battle/emw04/emw0403/emw0403.trtml", "Poke0359"),
            ],
            [(2304, 2209)] =
            [
                new(1280, "emw0614", "ik_effect/data/battle/emw06/emw0614/emw0614.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1280, 2209, "child"),
                        ],
                    ],
                },
                new(2209, "emw0614_1_bullet", "ik_effect/data/battle/emw06/emw0614/emw0614_1_bullet.trtml", null),
            ],
            [(2305, 2210)] =
            [
                new(2210, "emw0245", "ik_effect/data/battle/emw02/emw0245/emw0245.trtml", null),
            ],
            [(2308, 2213)] =
            [
                new(2213, "emw0556_1_bullet", "ik_effect/data/battle/emw05/emw0556/emw0556_1_bullet.trtml", null),
            ],
            [(2692, 2597)] =
            [
                new(2597, "emw0076_u", "ik_effect/data/battle/emw00/emw0076_u/emw0076_u.trtml", null),
            ],
            [(2698, 2603)] =
            [
                new(2603, "emw0076_u", "ik_effect/data/battle/emw00/emw0076_u/emw0076_u.trtml", "BosoGuraadon"),
            ],
            [(2699, 2604)] =
            [
                new(2598, "emw0087_u", "ik_effect/data/battle/emw00/emw0087_u/emw0087_u.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(2598, 2604, "child"),
                        ],
                    ],
                },
                new(2598, "emw0087_u", "ik_effect/data/battle/emw00/emw0087_u/emw0087_u.trtml", "BosoKaiooga")
                {
                    RelationshipPaths =
                    [
                        [
                            new(2598, 2604, "child"),
                        ],
                    ],
                },
            ],
            [(2703, 2608)] =
            [
                new(2608, "emw0122", "ik_effect/data/battle/emw01/emw0122/emw0122.trtml", null),
            ],
            [(2704, 2609)] =
            [
                new(792, "emw0126", "ik_effect/data/battle/emw01/emw0126/emw0126.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(792, 2609, "landing"),
                        ],
                        [
                            new(792, 2609, "child"),
                        ],
                    ],
                },
                new(792, "emw0126", "ik_effect/data/battle/emw01/emw0126/emw0126.trtml", "BosoGuraadon")
                {
                    RelationshipPaths =
                    [
                        [
                            new(792, 2609, "landing"),
                        ],
                        [
                            new(792, 2609, "child"),
                        ],
                    ],
                },
            ],
            [(2705, 2610)] =
            [
                new(2610, "emw0183", "ik_effect/data/battle/emw01/emw0183/emw0183.trtml", null),
            ],
            [(2707, 2612)] =
            [
                new(891, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(891, 2612, "landing"),
                        ],
                        [
                            new(891, 2612, "coreLanding"),
                        ],
                    ],
                },
                new(2645, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(2645, 2612, "landing"),
                        ],
                        [
                            new(2645, 2612, "coreLanding"),
                        ],
                    ],
                },
                new(2894, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(2894, 2612, "landing"),
                        ],
                        [
                            new(2894, 2612, "coreLanding"),
                        ],
                    ],
                },
                new(2895, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(2895, 2612, "coreLanding"),
                        ],
                        [
                            new(2895, 2612, "landing"),
                        ],
                    ],
                },
                new(2896, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(2896, 2612, "landing"),
                        ],
                        [
                            new(2896, 2612, "coreLanding"),
                        ],
                    ],
                },
            ],
            [(2710, 2615)] =
            [
                new(2615, "emw0315", "ik_effect/data/battle/emw03/emw0315/emw0315.trtml", "Heat2_Not_Performed"),
            ],
            [(2714, 2619)] =
            [
                new(1100, "emw0434", "ik_effect/data/battle/emw04/emw0434/emw0434.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1100, 2619, "landing"),
                        ],
                        [
                            new(1100, 2619, "coreLanding"),
                        ],
                    ],
                },
            ],
            [(2715, 2620)] =
            [
                new(1129, "emw0463", "ik_effect/data/battle/emw04/emw0463/emw0463.trtml", "BosoMega_Control_1C")
                {
                    RelationshipPaths =
                    [
                        [
                            new(1129, 2620, "child"),
                        ],
                    ],
                },
            ],
            [(2717, 2622)] =
            [
                new(2622, "emw0523", "ik_effect/data/battle/emw05/emw0523/emw0523.trtml", null),
            ],
            [(2718, 2623)] =
            [
                new(2623, "emw0527", "ik_effect/data/battle/emw05/emw0527/emw0527.trtml", null),
            ],
            [(2719, 2624)] =
            [
                new(2624, "emw0528", "ik_effect/data/battle/emw05/emw0528/emw0528.trtml", "Heat2_Not_Performed"),
            ],
            [(2728, 2633)] =
            [
                new(1481, "emw0815", "ik_effect/data/battle/emw08/emw0815/emw0815.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1481, 2633, "child"),
                        ],
                    ],
                },
                new(1481, "emw0815", "ik_effect/data/battle/emw08/emw0815/emw0815.trtml", "BosoGuraadon")
                {
                    RelationshipPaths =
                    [
                        [
                            new(1481, 2633, "child"),
                        ],
                    ],
                },
            ],
            [(2734, 2639)] =
            [
                new(2639, "emw0094", "ik_effect/data/battle/emw00/emw0094/emw0094.trtml", null),
            ],
            [(2736, 2641)] =
            [
                new(2641, "emw0122", "ik_effect/data/battle/emw01/emw0122/emw0122.trtml", null),
                new(2641, "emw0122", "ik_effect/data/battle/emw01/emw0122/emw0122.trtml", "BosoMega_Control_1A"),
                new(2641, "emw0122", "ik_effect/data/battle/emw01/emw0122/emw0122.trtml", "BosoMega_Control_1B"),
                new(2641, "emw0122", "ik_effect/data/battle/emw01/emw0122/emw0122.trtml", "Heat2_Not_Performed"),
            ],
            [(2738, 2643)] =
            [
                new(2643, "emw0183", "ik_effect/data/battle/emw01/emw0183/emw0183.trtml", null),
            ],
            [(2740, 2645)] =
            [
                new(2645, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null),
            ],
            [(2741, 2646)] =
            [
                new(2646, "emw0248_3_bullet", "ik_effect/data/battle/emw02/emw0248/emw0248_3_bullet.trtml", null),
                new(2646, "emw0248_bullet", "ik_effect/data/battle/emw02/emw0248/emw0248_bullet.trtml", null),
            ],
            [(2750, 2655)] =
            [
                new(2621, "emw0464", "ik_effect/data/battle/emw04/emw0464/emw0464.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(2621, 2655, "landing"),
                        ],
                    ],
                },
            ],
            [(2752, 2657)] =
            [
                new(1193, "emw0527", "ik_effect/data/battle/emw05/emw0527/emw0527.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(1193, 2657, "coreLanding"),
                        ],
                    ],
                },
            ],
            [(2761, 2666)] =
            [
                new(2666, "emw0814", "ik_effect/data/battle/emw08/emw0814/emw0814.trtml", null),
            ],
            [(2784, 2688)] =
            [
                new(2688, "emw0200_u", "ik_effect/data/battle/emw02/emw0200_u/emw0200_u.trtml", null),
            ],
            [(2785, 2689)] =
            [
                new(2689, "emw0800", "ik_effect/data/battle/emw08/emw0800/emw0800.trtml", "BosoRekkuuza"),
            ],
            [(2787, 2691)] =
            [
                new(2690, "emw0304", "ik_effect/data/battle/emw03/emw0304/emw0304.trtml", "BosoRekkuuza")
                {
                    RelationshipPaths =
                    [
                        [
                            new(2690, 2691, "child"),
                        ],
                    ],
                },
            ],
            [(2800, 2704)] =
            [
                new(2704, "emw0094", "ik_effect/data/battle/emw00/emw0094/emw0094.trtml", null),
            ],
            [(2812, 2716)] =
            [
                new(2716, "emw0044", "ik_effect/data/battle/emw00/emw0044/emw0044.trtml", null),
            ],
            [(2814, 2718)] =
            [
                new(2654, "emw0463", "ik_effect/data/battle/emw04/emw0463/emw0463.trtml", "BosoMega_Control_2C")
                {
                    RelationshipPaths =
                    [
                        [
                            new(2654, 2718, "child"),
                        ],
                    ],
                },
            ],
            [(2984, 2894)] =
            [
                new(2894, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null),
            ],
            [(2985, 2895)] =
            [
                new(2895, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null),
            ],
            [(2986, 2896)] =
            [
                new(2896, "emw0225", "ik_effect/data/battle/emw02/emw0225/emw0225.trtml", null),
            ],
            [(2989, 2899)] =
            [
                new(2898, "emw0463", "ik_effect/data/battle/emw04/emw0463/emw0463.trtml", null)
                {
                    RelationshipPaths =
                    [
                        [
                            new(2898, 2899, "child"),
                        ],
                    ],
                },
                new(2898, "emw0463", "ik_effect/data/battle/emw04/emw0463/emw0463.trtml", "BosoMega_Control_1A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(2898, 2899, "child"),
                        ],
                    ],
                },
                new(2898, "emw0463", "ik_effect/data/battle/emw04/emw0463/emw0463.trtml", "BosoMega_Control_1B")
                {
                    RelationshipPaths =
                    [
                        [
                            new(2898, 2899, "child"),
                        ],
                    ],
                },
                new(2898, "emw0463", "ik_effect/data/battle/emw04/emw0463/emw0463.trtml", "BosoMega_Control_2A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(2898, 2899, "child"),
                        ],
                    ],
                },
                new(2898, "emw0463", "ik_effect/data/battle/emw04/emw0463/emw0463.trtml", "BosoMega_Control_2B")
                {
                    RelationshipPaths =
                    [
                        [
                            new(2898, 2899, "child"),
                        ],
                    ],
                },
                new(2898, "emw0463", "ik_effect/data/battle/emw04/emw0463/emw0463.trtml", "BosoMega_Control_3A")
                {
                    RelationshipPaths =
                    [
                        [
                            new(2898, 2899, "child"),
                        ],
                    ],
                },
            ],
            [(3089, 2999)] =
            [
                new(2999, "emw0721", "ik_effect/data/battle/emw07/emw0721/emw0721.trtml", null),
            ],
            [(3094, 3004)] =
            [
                new(3004, "emw0721", "ik_effect/data/battle/emw07/emw0721/emw0721.trtml", null),
            ],
            [(3122, 3032)] =
            [
                new(3032, "emw0247_u", "ik_effect/data/battle/emw02/emw0247_u/emw0247_u.trtml", "BosoMegaDaakurai"),
                new(3032, "emw0247_u", "ik_effect/data/battle/emw02/emw0247_u/emw0247_u.trtml", "Nyaonikusu_Single_Bullet"),
            ],
            [(3123, 3033)] =
            [
                new(3033, "emw0585_u", "ik_effect/data/battle/emw05/emw0585_u/emw0585_u.trtml", null),
            ],
            [(3125, 3035)] =
            [
                new(3035, "emw0280_u", "ik_effect/data/battle/emw02/emw0280_u/emw0280_u.trtml", null),
            ],
            [(3130, 3040)] =
            [
                new(3040, "emw0247_u", "ik_effect/data/battle/emw02/emw0247_u/emw0247_u.trtml", null),
            ],
            [(3136, 3046)] =
            [
                new(3046, "emw0566_u", "ik_effect/data/battle/emw05/emw0566_u/emw0566_u.trtml", null),
            ],
            [(3139, 3049)] =
            [
                new(3049, "emw0585_u", "ik_effect/data/battle/emw05/emw0585_u/emw0585_u.trtml", "BosoMega_Control_3A"),
            ],
            [(3140, 3050)] =
            [
                new(3050, "emw0585_u", "ik_effect/data/battle/emw05/emw0585_u/emw0585_u.trtml", "BosoMega_Control_3B"),
            ],
        };

    public static IReadOnlyList<ZaMovePlayerDamageTimelineLaunchRecord> GetLaunches(
        int attackId,
        int damageBulletId)
    {
        if (!LaunchesByDamageRow.TryGetValue((attackId, damageBulletId), out var launches))
        {
            return [];
        }

        return launches
            .Select((launch, index) => new ZaMovePlayerDamageTimelineLaunchRecord(
                FormattableString.Invariant($"{attackId}:{damageBulletId}:{index + 1}"),
                launch.RootBulletId,
                launch.TimelineName,
                launch.TimelinePath,
                ClassifyLocalCondition(launch.ConditionTag))
            {
                RelationshipPaths = launch.RelationshipPaths,
            })
            .ToArray();
    }

    private static ZaMovePlayerDamageLocalConditionRecord ClassifyLocalCondition(
        string? conditionTag) => conditionTag switch
        {
            null => new("verified-none", "when-reached", "when-reached", null),
            "Heat2_Not_Performed" => new(
                "verified",
                "hp-phase-transition",
                "before-hp-phase-2-transition-completes",
                conditionTag),
            "Heat2_Performed" => new(
                "verified",
                "hp-phase-transition",
                "after-hp-phase-2-transition-completes",
                conditionTag),
            "BosoMega_Control_1A" => ControllerPattern("boss-mega-control-1a", conditionTag),
            "BosoMega_Control_1B" => ControllerPattern("boss-mega-control-1b", conditionTag),
            "BosoMega_Control_1C" => ControllerPattern("boss-mega-control-1c", conditionTag),
            "BosoMega_Control_2A" => ControllerPattern("boss-mega-control-2a", conditionTag),
            "BosoMega_Control_2B" => ControllerPattern("boss-mega-control-2b", conditionTag),
            "BosoMega_Control_2C" => ControllerPattern("boss-mega-control-2c", conditionTag),
            "BosoMega_Control_3A" => ControllerPattern("boss-mega-control-3a", conditionTag),
            "BosoMega_Control_3B" => ControllerPattern("boss-mega-control-3b", conditionTag),
            "BosoGuraadon" => Identity("groudon", conditionTag),
            "BosoDaakurai" => Identity("darkrai", conditionTag),
            "BosoKaiooga" => Identity("kyogre", conditionTag),
            "BosoMegaDaakurai" => Identity("mega-darkrai", conditionTag),
            "BosoRekkuuza" => Identity("rayquaza", conditionTag),
            "Zigarude_100%" => Identity("zygarde-complete", conditionTag),
            "Poke0952" => Identity("tatsugiri", conditionTag),
            "Poke0149" => Identity("dragonite", conditionTag),
            "Poke0359" => Identity("absol", conditionTag),
            "Nyaonikusu_Single_Bullet" => new(
                "verified",
                "choreography",
                "meowstic-single-bullet",
                conditionTag),
            _ => new("unclassified", "unknown", "unclassified", conditionTag),
        };

    private static ZaMovePlayerDamageLocalConditionRecord ControllerPattern(
        string semanticKey,
        string rawTag) => new("verified", "controller-pattern", semanticKey, rawTag);

    private static ZaMovePlayerDamageLocalConditionRecord Identity(
        string semanticKey,
        string rawTag) => new("verified", "identity", semanticKey, rawTag);

    public static bool MatchesVerifiedBaseBulletCatalog(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return string.Equals(
            Convert.ToHexString(SHA256.HashData(bytes)),
            VerifiedBaseBulletCatalogSha256,
            StringComparison.Ordinal);
    }

    private sealed record CatalogTimelineLaunchRecord(
        int RootBulletId,
        string TimelineName,
        string TimelinePath,
        string? ConditionTag)
    {
        public IReadOnlyList<IReadOnlyList<ZaMovePlayerDamageTimelinePathEdgeRecord>>
            RelationshipPaths { get; init; } = [];
    }
}
