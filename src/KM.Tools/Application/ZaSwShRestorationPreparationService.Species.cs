// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private static bool TryProfile(KM.SwSh.Pokemon.SwShPokemonIdentity identity, out SpeciesProfile profile)
    {
        if (MegaProfiles.TryGetValue(identity, out profile!)) return true;
        if (identity.Species == 6 && identity.Form is 1 or 2)
        {
            var resource = 50 + identity.Form;
            profile = new(6, 4, 240, 152, 170, "_body_a_alb", SourceFraming: true, IdleFraming: true,
                MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31,
                ResourceStem: $"pm0006_{resource}_00", OutputStem: $"pm0006_{identity.Form:00}",
                PairedFlames: true, FlameMaterials: identity.Form == 1 ? ["fire_a", "fire_b"] : ["fire"],
                AirborneBattle: true);
            return true;
        }
        profile = null!;
        return identity.Form == 0 && Profiles.TryGetValue(identity.Species, out profile!);
    }
    private static SpeciesProfile Profile(KM.SwSh.Pokemon.SwShPokemonIdentity identity) =>
        TryProfile(identity, out var profile) ? profile : throw new InvalidDataException("Restoration profile is unavailable.");

    private static string SourceStem(int species, int gender, SpeciesProfile profile)
    {
        var stem = profile.ResourceStem ?? $"pm{species:0000}_00_00";
        return profile.SeparateFemale ? stem[..7] + gender.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + stem[9..] : stem;
    }

    private sealed record SpeciesProfile(int NativeTemplate, int HatchedSpecies, int Experience,
        float BattleHeight, float FieldHeight, string TextureSuffix, bool SourceFraming = false, bool MeshVisibility = false, int FemalePercent = 50, byte GenderThreshold = 127, bool SeparateFemale = false, bool IdleFraming = false, bool MatchNativeGiantHeight = false,
        System.Numerics.Vector3? MouthPosition = null, float? MealVerticalOffset = null, bool NativeFlames = false, string? EmissionMaterial = null, string? ResourceStem = null, bool PairedFlames = false, bool LayeredEyes = false,
        IReadOnlyDictionary<string, System.Numerics.Vector2>? EyeAtlasOffsets = null,
        string[]? FlameMaterials = null, string? OutputStem = null, bool AirborneBattle = false, bool PlaAssets = false,
        int GenderKind = 0, int AnimationOffset = 0, IReadOnlyDictionary<string, string>? MotionOverrides = null, bool LayerEmission = false,
        int? ConventionalCatchRate = null, bool EmptyEyes = false, int HatchedForm = 0, int? SourceSpecies = null, int? SourceVoiceForm = null,
        IReadOnlyList<NormalUvTile>? NormalTiles = null, bool BakeNormalTiles = false, int RemoveDetailLevels = 0,
        IReadOnlyList<ColorUvTile>? ColorTiles = null, bool IndependentEyeHighlights = false, bool LayeredEyeTint = false, bool LayeredEyeEmission = false,
        bool CompactTextureReferences = false,
        IReadOnlyDictionary<string, (int Frames, bool Loop)>? StandaloneMotionTimelines = null,
        int? EvolutionFamilySpecies = null);

    private sealed record NormalUvTile(string Material, System.Numerics.Vector2 Scale, string? Mesh = null);
    private sealed record ColorUvTile(string Material, System.Numerics.Vector2 Scale, string Mesh);

    private static readonly IReadOnlyDictionary<int, SpeciesProfile> Profiles = new Dictionary<int, SpeciesProfile>
    {
        [179] = new(831, 179, 56, 60, 60, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, LayerEmission: true),
        [180] = new(25, 179, 128, 80, 80, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, LayerEmission: true),
        [181] = new(849, 179, 255, 140, 140, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, LayerEmission: true),
        [228] = new(58, 228, 66, 60, 60, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true),
        [229] = new(59, 228, 175, 140, 140, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, SeparateFemale: true),
        [316] = new(109, 316, 60, 40, 40, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, SeparateFemale: true),
        [317] = new(569, 316, 163, 170, 170, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, SeparateFemale: true),
        [322] = new(220, 322, 61, 70, 70, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, SeparateFemale: true),
        [323] = new(324, 322, 161, 170, 190, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, SeparateFemale: true),
        [335] = new(760, 335, 160, 130, 130, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true),
        [336] = new(350, 336, 160, 170, 270, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true),
        [353] = new(355, 353, 59, 60, 60, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, AnimationOffset: 20000),
        [354] = new(356, 353, 159, 110, 110, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true),
        [396] = new(821, 396, 49, 30, 30, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, SeparateFemale: true),
        [397] = new(822, 396, 119, 60, 60, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, SeparateFemale: true),
        [398] = new(823, 396, 243, 120, 120, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, SeparateFemale: true),
        [491] = new(150, 491, 300, 150, 150, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, FemalePercent: 0, GenderThreshold: 255, GenderKind: 3, AnimationOffset: 20000),
        [602] = new(846, 602, 55, 20, 20, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, AnimationOffset: 10000, LayerEmission: true),
        [603] = new(147, 602, 142, 120, 120, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, AnimationOffset: 10000, LayeredEyes: true),
        [604] = new(350, 602, 232, 170, 210, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, AnimationOffset: 10000, LayeredEyes: true, MotionOverrides: new Dictionary<string, string> { ["eye01"] = "08000" }),
        [46] = new(98, 46, 57, 30, 30, "_body_alb", MeshVisibility: true, IdleFraming: true, PlaAssets: true),
        [47] = new(558, 46, 142, 100, 100, "_body_a_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true, PlaAssets: true),
        [56] = new(236, 56, 61, 50, 50, "_body_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true),
        [57] = new(766, 56, 159, 100, 100, "_body_a_alb", SourceFraming: true, MeshVisibility: true, IdleFraming: true),
        [650] = new(7, 650, 63, 40, 40, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, ResourceStem: "pm0720_00_00"),
        [651] = new(8, 650, 142, 70, 70, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, ResourceStem: "pm0721_00_00"),
        [652] = new(9, 650, 239, 160, 160, "_body_a_alb", MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, IdleFraming: true, ResourceStem: "pm0722_00_00"),
        [653] = new(37, 653, 61, 40, 40, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, ResourceStem: "pm0717_00_00"),
        [654] = new(5, 653, 143, 100, 100, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, IdleFraming: true, ResourceStem: "pm0718_00_00", PairedFlames: true),
        [655] = new(282, 653, 240, 150, 150, "_body_a_alb", MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, IdleFraming: true, ResourceStem: "pm0719_00_00", PairedFlames: true),
        [656] = new(7, 656, 63, 30, 30, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, ResourceStem: "pm0723_00_00", LayeredEyes: true),
        [657] = new(8, 656, 142, 60, 60, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, ResourceStem: "pm0724_00_00", LayeredEyes: true,
            EyeAtlasOffsets: new Dictionary<string, System.Numerics.Vector2> { ["r_eye_b"] = new(.5f, -2.75f), ["l_eye_b"] = new(.5f, -2.75f) }),
        [658] = new(254, 656, 239, 150, 150, "_body_a_alb", MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, ResourceStem: "pm0725_11_00", LayeredEyes: true),
        [498] = new(220, 498, 62, 50, 50, "_body_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, EmissionMaterial: "body_01"),
        [499] = new(5, 498, 146, 100, 100, "_body_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31),
        [500] = new(467, 498, 238, 160, 160, "_body_a_alb", MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, IdleFraming: true, NativeFlames: true),
        [152] = new(1, 152, 64, 90, 90, "_body_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31,
            MouthPosition: new(.023f, 0, .11f), MealVerticalOffset: 0),
        [153] = new(2, 152, 142, 120, 120, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31,
            MouthPosition: new(.08f, 0, .202f)),
        [154] = new(3, 152, 236, 180, 180, "_body_a_alb", MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, SeparateFemale: true, IdleFraming: true,
            MouthPosition: new(.12f, 0, .303f)),
        [158] = new(7, 158, 63, 60, 60, "_body_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31,
            MouthPosition: new(.044f, 0, .253f)),
        [159] = new(8, 158, 142, 110, 110, "_body_a_alb", SourceFraming: true, MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31,
            MouthPosition: new(.12f, 0, .366f)),
        // Native SwSh compresses large actors in battle. Tyranitar's 155 fits the
        // comparable upright silhouette; retain 230 for field and camp scale.
        [160] = new(9, 158, 239, 155, 230, "_body_a_alb", MeshVisibility: true, FemalePercent: 12, GenderThreshold: 31, IdleFraming: true, MatchNativeGiantHeight: true,
            MouthPosition: new(.70f, 0, .21f)),
        [13] = new(10, 13, 39, 30, 40, "_body_alb"),
        [14] = new(11, 13, 72, 60, 45, "_body_alb"),
        [15] = new(12, 13, 178, 100, 100, "_body_b_alb", SourceFraming: true),
        [16] = new(821, 16, 50, 30, 30, "_body_a_alb", MeshVisibility: true),
        [17] = new(822, 16, 122, 110, 110, "_body_a_alb", MeshVisibility: true),
        [18] = new(823, 16, 216, 150, 150, "_body_a_alb", MeshVisibility: true),
        [352] = new(560, 352, 154, 100, 100, "_body_a_alb", MeshVisibility: true),
    };
}
