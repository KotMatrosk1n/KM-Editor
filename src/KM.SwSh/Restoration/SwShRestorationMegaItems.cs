// SPDX-License-Identifier: GPL-3.0-only
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

public sealed record SwShRestorationMegaItem(int Species, string FormLabel, int DonorItemId, int ItemId);

/// <summary>Stable held item assignments for known Mega forms.</summary>
public static class SwShRestorationMegaItems
{
    public static IReadOnlyList<SwShRestorationMegaItem> All { get; } = Array.AsReadOnly(new SwShRestorationMegaItem[]
    {
        new(94, "Mega", 656, 656),
        new(282, "Mega", 657, 657),
        new(181, "Mega", 658, 658),
        new(3, "Mega", 659, 659),
        new(6, "MegaX", 660, 660),
        new(9, "Mega", 661, 661),
        new(150, "MegaX", 662, 662),
        new(150, "MegaY", 663, 663),
        new(257, "Mega", 664, 664),
        new(308, "Mega", 665, 665),
        new(229, "Mega", 666, 666),
        new(306, "Mega", 667, 667),
        new(354, "Mega", 668, 668),
        new(248, "Mega", 669, 669),
        new(212, "Mega", 670, 670),
        new(127, "Mega", 671, 671),
        new(142, "Mega", 672, 672),
        new(448, "Mega", 673, 673),
        new(460, "Mega", 674, 674),
        new(115, "Mega", 675, 675),
        new(130, "Mega", 676, 676),
        new(359, "Mega", 677, 677),
        new(6, "MegaY", 678, 678),
        new(65, "Mega", 679, 679),
        new(214, "Mega", 680, 680),
        new(303, "Mega", 681, 681),
        new(310, "Mega", 682, 682),
        new(445, "Mega", 683, 683),
        new(380, "Mega", 684, 684),
        new(381, "Mega", 685, 685),
        new(260, "Mega", 752, 752),
        new(254, "Mega", 753, 753),
        new(302, "Mega", 754, 754),
        new(334, "Mega", 755, 755),
        new(475, "Mega", 756, 756),
        new(531, "Mega", 757, 757),
        new(376, "Mega", 758, 758),
        new(319, "Mega", 759, 759),
        new(80, "Mega", 760, 760),
        new(208, "Mega", 761, 761),
        new(18, "Mega", 762, 762),
        new(362, "Mega", 763, 763),
        new(719, "Mega", 764, 764),
        new(323, "Mega", 767, 767),
        new(428, "Mega", 768, 768),
        new(373, "Mega", 769, 769),
        new(15, "Mega", 770, 770),
        new(36, "Mega", 2559, 427),
        new(71, "Mega", 2560, 622),
        new(121, "Mega", 2561, 837),
        new(149, "Mega", 2562, 838),
        new(154, "Mega", 2563, 839),
        new(160, "Mega", 2564, 840),
        new(227, "Mega", 2565, 848),
        new(478, "Mega", 2566, 859),
        new(485, "Mega", 2567, 867),
        new(491, "Mega", 2568, 868),
        new(500, "Mega", 2569, 869),
        new(530, "Mega", 2570, 870),
        new(545, "Mega", 2571, 871),
        new(560, "Mega", 2572, 897),
        new(604, "Mega", 2573, 898),
        new(609, "Mega", 2574, 899),
        new(652, "Mega", 2575, 1058),
        new(655, "Mega", 2576, 1059),
        new(658, "Mega", 2577, 1060),
        new(668, "Mega", 2578, 1061),
        new(670, "Mega", 2579, 1062),
        new(687, "Mega", 2580, 1063),
        new(689, "Mega", 2581, 1064),
        new(691, "Mega", 2582, 1065),
        new(701, "Mega", 2583, 1066),
        new(718, "Mega", 2584, 1067),
        new(780, "Mega", 2585, 1068),
        new(807, "Mega", 2586, 1069),
        new(870, "Mega", 2587, 1070),
        new(26, "MegaX", 2635, 1071),
        new(26, "MegaY", 2636, 1072),
        new(358, "Mega", 2637, 1073),
        new(359, "MegaZ", 2638, 1078),
        new(398, "Mega", 2639, 1079),
        new(445, "MegaZ", 2640, 1082),
        new(448, "MegaZ", 2641, 1083),
        new(623, "Mega", 2642, 1101),
        new(678, "Mega", 2643, 1102),
        new(740, "Mega", 2644, 1268),
        new(768, "Mega", 2645, 1272),
        new(801, "Mega", 2646, 1273),
        new(952, "Mega", 2647, 1274),
        new(998, "Mega", 2648, 1275),
        new(978, "Mega", 2649, 1276),
        new(970, "Mega", 2650, 1277),
    });

    public static SwShRestorationMegaItem? For(SwShPokemonIdentity identity)
    {
        var label = SwShRestorationFormCandidates.ResolveFormLabel(identity.Species, identity.Form);
        label = label switch { "Mega X" => "MegaX", "Mega Y" => "MegaY", "Mega Z" => "MegaZ",
            "Mega (Male)" or "Mega (Female)" or "Mega (Standard Color)" or "Mega (Original Color)" => "Mega", _ => label };
        return All.SingleOrDefault(item => item.Species == identity.Species && item.FormLabel == label);
    }

    public static bool IsItemContribution(SwShRestorationContribution contribution) =>
        IsItemContribution(contribution.Path, contribution.Writer);

    internal static bool IsItemContribution(string path, SwShRestorationWriter writer) =>
        writer is SwShRestorationWriter.Items or SwShRestorationWriter.ItemText
        || writer == SwShRestorationWriter.File && All.Any(item => path == IconPath(item.ItemId));

    public static string IconPath(int itemId) => $"romfs/bin/appli/icon_item/item_{itemId:0000}.bntx";

    public static IReadOnlyList<string> TextLanguages => SwShGameTextLanguage.SupportedMessageLanguages;
    public static IReadOnlyList<string> TextFiles { get; } = Array.AsReadOnly(new[] { "itemname.dat", "iteminfo.dat",
        "itemname_acc.dat", "itemname_acc_classified.dat", "itemname_classified.dat", "itemname_plural.dat", "itemname_plural_classified.dat" });
    public static string TextPath(string language, string file) => SwShGameTextLanguage.CommonMessagePath(language, file);
    internal static bool IsTextPath(string path) => TextLanguages.Any(language => TextFiles.Any(file =>
        path == SwShGameTextLanguage.CommonMessagePath(language, file)));
}
