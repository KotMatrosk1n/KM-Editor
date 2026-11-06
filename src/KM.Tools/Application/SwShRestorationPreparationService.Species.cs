// SPDX-License-Identifier: GPL-3.0-only
using KM.SwSh.Pokemon;
using KM.Formats.SwSh;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed record SpeciesProfile(int NativeTemplate, int HatchedSpecies, int Experience,
        int FemalePercent, byte GenderThreshold, float BattleHeight, float FieldHeight, bool AuthoredMaterials = false, int FramingTemplate = 0,
        string BattleIdle = "00001", bool NativeFlames = false, string? ResourceStem = null, bool CompactResources = true, string FlameMaterial = "fire",
        System.Numerics.Vector3? MouthPosition = null, bool MeshVisibility = false, bool SharedMaterialTextures = false, bool OpaqueEyeSurface = false,
        bool CornealEyes = false, float? GiantScale = null, int AnimationOffset = 0,
        IReadOnlyDictionary<string, float>? CornealNormalHeights = null,
        IReadOnlyDictionary<string, System.Numerics.Vector2>? NormalUvScales = null, float? GiantVerticalOffset = null, string? SourceStem = null, int HatchedForm = 0,
        IReadOnlyDictionary<string, int>? NormalUvVerticalTiles = null, int? SourceVoiceForm = null, bool LayerEmission = false,
        bool ScalarEyes = false, IReadOnlyDictionary<string, string>? MotionOverrides = null, bool AuthoredAlpha = false,
        int? SourceSpecies = null, bool EmptyMouth = false, bool TransparentEyes = false,
        IReadOnlyDictionary<string, System.Numerics.Vector2>? StaticUvScales = null, string? SummaryFramingStem = null,
        int GenderKind = 0, bool SurfaceFlow = false, bool CompactAnimationResources = false, float FlameColorExposure = 1, bool LayeredEyes = false,
        bool TransparentBody = false, bool CenteredStaticUv = false, bool LavaLayers = false, bool EmptyEyes = false,
        int? ColorSelector = null, IReadOnlySet<string>? StandaloneAnimations = null,
        bool SourceLayeredAlbedo = false, bool CompactTextureReferences = false, bool LayeredEyeEmission = false,
        IReadOnlyDictionary<string, float>? CornealPrimaryNormalHeights = null, int RemoveDetailLevels = 0, bool SeparateNormalSections = false,
        IReadOnlyDictionary<string, int>? NormalSharingGroups = null,
        IReadOnlyList<SwShNormalSectionPartition>? NormalSectionPartitions = null);

    private static IReadOnlyDictionary<string, System.Numerics.Vector2> DeoxysAtlas(string form) =>
        new Dictionary<string, System.Numerics.Vector2>
        {
            [$"pm0386_{form}_00_{(form == "11" ? "jewel" : "body_c")}_mesh_shape"] = new(4, 4),
            [$"pm0386_{form}_00_sphere_mesh_shape"] = new(4, 4),
        };

    private static readonly IReadOnlyDictionary<SwShPokemonIdentity, SpeciesProfile> FormProfiles = new Dictionary<SwShPokemonIdentity, SpeciesProfile>
    {
        [new(720, 1)] = new(150, 720, 270, 0, 255, 650, 650, AuthoredMaterials: true, MeshVisibility: true,
            SharedMaterialTextures: true, CornealEyes: true, SourceStem: "pm0774_12_00", SourceVoiceForm: 1,
            GenderKind: 3, AnimationOffset: 20000, RemoveDetailLevels: 3),
        [new(211, 1)] = new(211, 211, 88, 50, 127, 50, 50,
            AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceStem: "pm0211_00_41", HatchedForm: 0, GenderKind: 0, SourceVoiceForm: 0, AnimationOffset: 10000),
        [new(483, 1)] = new(483, 483, 306, 0, 255, 270, 700,
            AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, LayerEmission: true, SourceStem: "pm0483_12_00", HatchedForm: 0, GenderKind: 3, SourceVoiceForm: 0),
        [new(484, 1)] = new(484, 484, 306, 0, 255, 240, 630,
            AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, LayerEmission: true, SourceStem: "pm0484_12_00", HatchedForm: 0, GenderKind: 3, SourceVoiceForm: 0),
        [new(705, 1)] = new(705, 704, 158, 50, 127, 70, 70,
            AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceStem: "pm0764_00_41", HatchedForm: 0, GenderKind: 0, SourceVoiceForm: 0),
        [new(706, 1)] = new(706, 704, 270, 50, 127, 170, 170,
            AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceStem: "pm0765_00_41", HatchedForm: 0, GenderKind: 0, SourceVoiceForm: 0),
        [new(724, 1)] = new(724, 722, 239, 12, 31, 160, 160,
            AuthoredMaterials: true, MeshVisibility: true, CornealEyes: true, LayeredEyes: true, LayeredEyeEmission: true, SourceStem: "pm0843_00_41", HatchedForm: 0, GenderKind: 0, SourceVoiceForm: 0),

        [new(386, 1)] = new(150, 386, 300, 0, 255, 170, 170, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            CornealEyes: true, LayerEmission: true, SourceStem: "pm0386_12_00", SourceVoiceForm: 0, GenderKind: 3, TransparentBody: true,
            EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }, StaticUvScales: DeoxysAtlas("12"), CenteredStaticUv: true),
        [new(386, 2)] = new(150, 386, 300, 0, 255, 170, 170, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            CornealEyes: true, LayerEmission: true, SourceStem: "pm0386_13_00", SourceVoiceForm: 0, GenderKind: 3, TransparentBody: true,
            EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }, StaticUvScales: DeoxysAtlas("13"), CenteredStaticUv: true),
        [new(386, 3)] = new(150, 386, 300, 0, 255, 170, 170, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            CornealEyes: true, LayerEmission: true, SourceStem: "pm0386_14_00", SourceVoiceForm: 0, GenderKind: 3, TransparentBody: true,
            EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }, StaticUvScales: DeoxysAtlas("14"), CenteredStaticUv: true),
        [new(58, 1)] = new(58, 58, 70, 25, 63, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            SourceStem: "pm0058_00_41", HatchedForm: 1, ScalarEyes: true, SourceVoiceForm: 0,
            MotionOverrides: new Dictionary<string, string> { ["eye01"] = "00000" }),
        [new(59, 1)] = new(59, 58, 194, 25, 63, 150, 200, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            SourceStem: "pm0059_00_41", HatchedForm: 1, CornealEyes: true, SourceVoiceForm: 0),
        [new(88, 1)] = new(568, 88, 65, 50, 127, 70, 70, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            SourceStem: "pm0088_00_11", HatchedForm: 1, CornealEyes: true, BattleIdle: "00000", SourceVoiceForm: 0, SurfaceFlow: true),
        [new(89, 1)] = new(569, 88, 175, 50, 127, 100, 100, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            SourceStem: "pm0089_00_11", HatchedForm: 1, CornealEyes: true, BattleIdle: "00000", SourceVoiceForm: 0, SurfaceFlow: true, CompactAnimationResources: true),
        [new(100, 1)] = new(109, 100, 66, 0, 255, 50, 50, AuthoredMaterials: true, MeshVisibility: true,
            SourceStem: "pm0100_00_41", HatchedForm: 1, GenderKind: 3, GiantVerticalOffset: 0, LayerEmission: true, SourceVoiceForm: 0,
            ScalarEyes: true, EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }),
        [new(101, 1)] = new(362, 100, 172, 0, 255, 120, 120, AuthoredMaterials: true, MeshVisibility: true,
            SourceStem: "pm0101_00_41", HatchedForm: 1, GenderKind: 3, LayerEmission: true, SourceVoiceForm: 0,
            ScalarEyes: true, EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }),
        [new(157, 1)] = new(467, 155, 240, 12, 31, 160, 160, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            SourceStem: "pm0157_00_41", NativeFlames: true, CornealEyes: true, SourceVoiceForm: 0, FlameColorExposure: .25f),
        [new(503, 1)] = new(365, 501, 238, 12, 31, 150, 150, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            SourceStem: "pm0503_00_41", CornealEyes: true, SourceVoiceForm: 0),
        [new(571, 1)] = new(571, 570, 179, 12, 31, 150, 160, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            SourceStem: "pm0571_00_41", CornealEyes: true, SourceVoiceForm: 0, SurfaceFlow: true, CompactAnimationResources: true),
        [new(74, 1)] = new(109, 74, 60, 50, 127, 50, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            AnimationOffset: 20000, CornealEyes: true, GiantVerticalOffset: 0, SourceStem: "pm0074_00_11", HatchedForm: 1, SourceVoiceForm: 0),
        [new(75, 1)] = new(525, 74, 137, 50, 127, 100, 100, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            CornealEyes: true, SourceStem: "pm0075_00_11", HatchedForm: 1, SourceVoiceForm: 0, LayerEmission: true),
        [new(76, 1)] = new(112, 74, 223, 50, 127, 170, 170, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true,
            CornealEyes: true, SourceStem: "pm0076_00_11", HatchedForm: 1,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .01f, ["r_eye"] = .01f },
            NormalUvScales: new Dictionary<string, System.Numerics.Vector2>
            {
                ["pm0076_00_11_bodyinc_mesh_shape"] = new(1, 2),
                ["pm0076_00_11_bodybinc_mesh_shape"] = new(1, 2),
            }, NormalUvVerticalTiles: new Dictionary<string, int> { ["pm0076_00_11_bodybinc_mesh_shape"] = 1 }, SourceVoiceForm: 0, LayerEmission: true),
    };
    private static bool TryProfile(SwShPokemonIdentity identity, out SpeciesProfile profile)
    {
        if (identity.Form == 0 ? !Profiles.TryGetValue(identity.Species, out profile!)
            : !FormProfiles.TryGetValue(identity, out profile!))
        {
            if (identity.Species == 774 && identity.Form is >= 1 and <= 13
                && Profiles.TryGetValue(774, out var colorBasis))
            {
                profile = colorBasis with { SourceVoiceForm = 0, HatchedForm = identity.Form };
                if (identity.Form >= 7)
                    profile = profile with { SourceStem = "pm0824_12_00", HatchedForm = identity.Form - 7 };
            }
            else if (identity.Species == 741 && identity.Form is >= 1 and <= 3
                && Profiles.TryGetValue(741, out var basis))
                profile = basis with { SourceStem = $"pm0825_{11 + identity.Form:00}_00",
                    HatchedForm = identity.Form, SourceVoiceForm = identity.Form };
            else if (identity is { Species: 550, Form: 2 })
                profile = Profiles[902] with { NativeTemplate = 550, HatchedSpecies = 550, Experience = 161,
                    SourceSpecies = 550, SourceStem = "pm0550_00_41", GenderKind = 0, GenderThreshold = 127,
                    FemalePercent = 50, BattleHeight = 100, FieldHeight = 100, HatchedForm = 2,
                    SourceVoiceForm = 0, SurfaceFlow = false };
            else if (identity is { Species: 902, Form: 1 })
                profile = Profiles[902] with { SourceStem = "pm1006_12_41", SourceVoiceForm = 0,
                    GenderKind = 2, GenderThreshold = 254 };
            else if (identity is { Species: 905, Form: 1 })
                profile = Profiles[905] with { SourceStem = "pm1004_12_00", SourceVoiceForm = 1, LayerEmission = true };
            else return false;
        }
        if (identity.Species == 774 && identity.Form <= 13)
            profile = profile with { ColorSelector = identity.Form % 7, LayerEmission = true, AnimationOffset = 20000, CornealEyes = false };
        if (identity.Species == 774 && identity.Form is >= 7 and <= 13)
            // The core graph also owns seven three-frame poses. They share code
            // 05148 and remain distinct files, outside the shared native aliases.
            profile = profile with { StandaloneAnimations = Enumerable.Range(1, 7)
                .Select(i => $"pm0824_12_00_05148_campose48_all_{i}").ToHashSet(StringComparer.Ordinal) };
        if (identity.Form == 0 && identity.Species is 731 or 779)
        {
            var primary = identity.Species == 731 ? .7f : .8f;
            var corneal = identity.Species == 731 ? .7f : .5f;
            profile = profile with { LayeredEyes = true, LayeredEyeEmission = true,
                CornealPrimaryNormalHeights = new Dictionary<string, float> { ["l_eye"] = primary, ["r_eye"] = primary },
                CornealNormalHeights = new Dictionary<string, float> { ["l_eye"] = corneal, ["r_eye"] = corneal } };
        }
        if (identity.Species is 523 or 905) profile = profile with { LayerEmission = true };
        if (identity is { Species: 902, Form: 0 }) profile = profile with { GenderThreshold = 0 };
        if (identity.Form == 0 && identity.Species is 409 or 429)
            profile = profile with { SeparateNormalSections = true, NormalSectionPartitions =
                [identity.Species == 409
                    ? new("pm0409_00_00_body_mesh_shape", "body_b", 3917, 3058, [0, 1])
                    : new("pm0429_00_00_body_mesh_shape", "body_b", 3627, 2404, [220])] };
        if (identity is { Species: 724, Form: 1 })
            profile = profile with { SeparateNormalSections = true, NormalSharingGroups = new Dictionary<string, int>
            {
                ["pm0843_00_41_hair_mesh_shape"] = 0, ["pm0843_00_41_body_mesh_shape"] = 0,
                ["pm0843_00_41_ivy_a_mesh_shape"] = 0, ["pm0843_00_41_ivy_b_mesh_shape"] = 0,
                ["pm0843_00_41_cloak_a_mesh_shape"] = 1, ["pm0843_00_41_arrow_a_mesh_shape"] = 1,
                ["pm0843_00_41_arrow_c_mesh_shape"] = 1,
            } };
        return true;
    }
    private static SpeciesProfile Profile(SwShPokemonIdentity identity) => TryProfile(identity, out var profile) ? profile
        : throw new InvalidDataException("Restoration form profile is missing.");

    private static readonly IReadOnlyDictionary<int, SpeciesProfile> Profiles = new Dictionary<int, SpeciesProfile>
    {
        [198] = new(821, 198, 81, 50, 127, 50, 50, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: false, CornealEyes: true, CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .05f, ["r_eye"] = .05f }, BattleIdle: "20000", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["mouth01"] = "28100", ["kw32_happyB01"] = "20011", ["kw35_playA01"] = "20011" }),
        [200] = new(425, 200, 87, 50, 127, 70, 70, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .02f, ["r_eye"] = .02f }, BattleIdle: "20001", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["mouth01"] = "28100", ["fi20_walk01"] = "20030", ["fi21_run01"] = "20100", ["kw20_drowseA01"] = "20280", ["kw20_drowseB01"] = "20281", ["kw20_drowseC01"] = "20282", ["kw21_sleepA01"] = "20280", ["kw21_sleepB01"] = "20281", ["kw30_hate01"] = "20563", ["kw32_happyA01"] = "20550", ["kw32_happyB01"] = "20011", ["kw32_happyC01"] = "20550", ["kw33_moveA01"] = "20150", ["kw33_moveC01"] = "20155", ["kw34_lonely01"] = "20560", ["kw35_playA01"] = "20011", ["kw36_mad01"] = "20563", ["kw50_eatA01"] = "20290", ["kw50_eatB01"] = "20291", ["kw50_eatC01"] = "20292", ["kw60_touch01"] = "20320" }),
        [216] = new(674, 216, 66, 50, 127, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [217] = new(675, 216, 175, 50, 127, 170, 180, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [408] = new(696, 408, 70, 12, 31, 90, 90, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [409] = new(697, 408, 173, 12, 31, 160, 160, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .2f, ["r_eye"] = .2f }),
        [410] = new(698, 410, 70, 12, 31, 50, 50, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .5f, ["r_eye"] = .5f }, BattleIdle: "00000"),
        [411] = new(699, 410, 173, 12, 31, 130, 130, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [429] = new(426, 200, 173, 50, 127, 90, 90, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .01f, ["r_eye"] = .01f }, BattleIdle: "20001", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["mouth01"] = "28100", ["fi20_walk01"] = "20030", ["fi21_run01"] = "20100", ["kw20_drowseA01"] = "20280", ["kw20_drowseB01"] = "20281", ["kw20_drowseC01"] = "20282", ["kw21_sleepA01"] = "20280", ["kw21_sleepB01"] = "20281", ["kw30_hate01"] = "20563", ["kw32_happyA01"] = "20550", ["kw32_happyB01"] = "20011", ["kw32_happyC01"] = "20550", ["kw33_moveA01"] = "20150", ["kw33_moveC01"] = "20155", ["kw34_lonely01"] = "20560", ["kw35_playA01"] = "20011", ["kw36_mad01"] = "20563", ["kw50_eatA01"] = "20290", ["kw50_eatB01"] = "20291", ["kw50_eatC01"] = "20292", ["kw60_touch01"] = "20320" }),
        [430] = new(823, 198, 177, 50, 127, 90, 90, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "20000", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["mouth01"] = "28100", ["kw32_happyB01"] = "20011", ["kw35_playA01"] = "20011" }),
        [522] = new(77, 522, 59, 50, 127, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, LayerEmission: true),
        [523] = new(78, 522, 174, 50, 127, 160, 160, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [540] = new(10, 540, 62, 50, 127, 30, 30, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [541] = new(11, 540, 133, 50, 127, 50, 50, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [542] = new(123, 540, 225, 50, 127, 120, 120, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [901] = new(675, 216, 275, 50, 127, 170, 240, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceSpecies: 901, SourceStem: "pm1007_00_00"),
        [902] = new(550, 550, 265, 50, 127, 170, 300, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceSpecies: 902, BattleIdle: "10001", SourceStem: "pm1006_11_41", SurfaceFlow: true, GenderKind: 1, HatchedForm: 2, MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "10152", ["ba01_landB01"] = "10153", ["ba01_landC01"] = "10155", ["ba10_waitB01"] = "10010", ["ba20_buturi01"] = "10400", ["ba21_tokusyu01"] = "10450", ["ba30_damageS01"] = "10500", ["ba41_down01"] = "10520", ["ba02_roar01"] = "10300", ["eye01"] = "18000", ["mouth01"] = "18100", ["fi20_walk01"] = "10030", ["fi21_run01"] = "10100", ["kw20_drowseA01"] = "10280", ["kw20_drowseB01"] = "10281", ["kw20_drowseC01"] = "10282", ["kw21_sleepA01"] = "10280", ["kw21_sleepB01"] = "10280", ["kw30_hate01"] = "10563", ["kw32_happyA01"] = "10550", ["kw32_happyB01"] = "10011", ["kw32_happyC01"] = "10550", ["kw33_moveA01"] = "10150", ["kw33_moveC01"] = "10155", ["kw34_lonely01"] = "10560", ["kw35_playA01"] = "10011", ["kw36_mad01"] = "10563", ["kw50_eatA01"] = "10290", ["kw50_eatB01"] = "10291", ["kw50_eatC01"] = "10292", ["kw60_touch01"] = "10320" }),
        [904] = new(211, 211, 179, 50, 127, 170, 250, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceSpecies: 904, BattleIdle: "10001", SourceStem: "pm1005_00_41", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "10152", ["ba01_landB01"] = "10153", ["ba01_landC01"] = "10155", ["ba10_waitB01"] = "10010", ["ba20_buturi01"] = "10400", ["ba21_tokusyu01"] = "10450", ["ba30_damageS01"] = "10500", ["ba41_down01"] = "10520", ["ba02_roar01"] = "10300", ["eye01"] = "18000", ["mouth01"] = "18100", ["fi20_walk01"] = "10030", ["fi21_run01"] = "10100", ["kw20_drowseA01"] = "10280", ["kw20_drowseB01"] = "10281", ["kw20_drowseC01"] = "10282", ["kw21_sleepA01"] = "10280", ["kw21_sleepB01"] = "10280", ["kw30_hate01"] = "10563", ["kw32_happyA01"] = "10550", ["kw32_happyB01"] = "10011", ["kw32_happyC01"] = "10550", ["kw33_moveA01"] = "10150", ["kw33_moveC01"] = "10155", ["kw34_lonely01"] = "10560", ["kw35_playA01"] = "10011", ["kw36_mad01"] = "10563", ["kw50_eatA01"] = "10290", ["kw50_eatB01"] = "10291", ["kw50_eatC01"] = "10292", ["kw60_touch01"] = "10320" }),
        [905] = new(641, 905, 116, 0, 254, 160, 160, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceSpecies: 905, BattleIdle: "20001", SourceStem: "pm1004_11_00", GenderKind: 2, MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["mouth01"] = "28100", ["fi20_walk01"] = "20030", ["fi21_run01"] = "20100", ["kw20_drowseA01"] = "20280", ["kw20_drowseB01"] = "20281", ["kw20_drowseC01"] = "20282", ["kw21_sleepA01"] = "20280", ["kw21_sleepB01"] = "20280", ["kw30_hate01"] = "20563", ["kw32_happyA01"] = "20550", ["kw32_happyB01"] = "20011", ["kw32_happyC01"] = "20550", ["kw33_moveA01"] = "20150", ["kw33_moveC01"] = "20155", ["kw34_lonely01"] = "20560", ["kw35_playA01"] = "20011", ["kw36_mad01"] = "20563", ["kw50_eatA01"] = "20290", ["kw50_eatB01"] = "20291", ["kw50_eatC01"] = "20292", ["kw60_touch01"] = "20320" }),

        [161] = new(263, 161, 43, 50, 127, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [162] = new(264, 161, 145, 50, 127, 170, 180, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [187] = new(546, 187, 50, 50, 127, 40, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "20000", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["mouth01"] = "28100", ["kw32_happyB01"] = "20011", ["kw35_playA01"] = "20011" }),
        [188] = new(547, 187, 119, 50, 127, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "20000", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["mouth01"] = "28100", ["kw32_happyB01"] = "20011", ["kw35_playA01"] = "20011" }),
        [189] = new(547, 187, 207, 50, 127, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "20000", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["mouth01"] = "28100", ["kw32_happyB01"] = "20011", ["kw35_playA01"] = "20011" }),
        [191] = new(43, 191, 36, 50, 127, 30, 30, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [192] = new(45, 191, 149, 50, 127, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [209] = new(835, 209, 60, 75, 191, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [210] = new(836, 209, 158, 75, 191, 140, 140, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .06f, ["r_eye"] = .06f }),
        [235] = new(766, 235, 88, 50, 127, 120, 120, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [283] = new(751, 283, 54, 50, 127, 50, 50, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "10001", EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "10152", ["ba01_landB01"] = "10153", ["ba01_landC01"] = "10155", ["ba10_waitB01"] = "10010", ["ba20_buturi01"] = "10400", ["ba21_tokusyu01"] = "10450", ["ba30_damageS01"] = "10500", ["ba41_down01"] = "10520", ["ba02_roar01"] = "10300", ["mouth01"] = "10000", ["fi20_walk01"] = "10030", ["fi21_run01"] = "10100", ["kw20_drowseA01"] = "10280", ["kw20_drowseB01"] = "10281", ["kw20_drowseC01"] = "10282", ["kw21_sleepA01"] = "10280", ["kw21_sleepB01"] = "10281", ["kw30_hate01"] = "10563", ["kw32_happyA01"] = "10550", ["kw32_happyB01"] = "10011", ["kw32_happyC01"] = "10550", ["kw33_moveA01"] = "10150", ["kw33_moveC01"] = "10155", ["kw34_lonely01"] = "10560", ["kw35_playA01"] = "10011", ["kw36_mad01"] = "10563", ["kw50_eatA01"] = "10290", ["kw50_eatB01"] = "10291", ["kw50_eatC01"] = "10292", ["kw60_touch01"] = "10320" }),
        [284] = new(738, 283, 159, 50, 127, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, EmptyMouth: true, BattleIdle: "20000", MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "20000", ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["fi20_walk01"] = "20030", ["fi21_run01"] = "20100", ["kw20_drowseA01"] = "20280", ["kw20_drowseB01"] = "20281", ["kw20_drowseC01"] = "20282", ["kw21_sleepA01"] = "20280", ["kw21_sleepB01"] = "20281", ["kw30_hate01"] = "20563", ["kw32_happyA01"] = "20550", ["kw32_happyB01"] = "20011", ["kw32_happyC01"] = "20550", ["kw33_moveA01"] = "20150", ["kw33_moveC01"] = "20155", ["kw34_lonely01"] = "20560", ["kw35_playA01"] = "20011", ["kw36_mad01"] = "20563", ["kw50_eatA01"] = "20290", ["kw50_eatB01"] = "20291", ["kw50_eatC01"] = "20292", ["kw60_touch01"] = "20320" }),
        [296] = new(66, 296, 47, 25, 63, 100, 100, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, EmptyEyes: true, MotionOverrides: new Dictionary<string, string> { ["eye01"] = "00000" }),
        [297] = new(68, 296, 166, 25, 63, 170, 230, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }),
        [311] = new(25, 311, 142, 50, 127, 40, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [312] = new(25, 312, 142, 50, 127, 40, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [313] = new(595, 313, 151, 0, 0, 70, 70, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, GenderKind: 1, LayerEmission: true),
        [314] = new(595, 314, 151, 0, 254, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, GenderKind: 2),
        [357] = new(780, 357, 161, 50, 127, 170, 200, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [370] = new(170, 370, 116, 75, 191, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "10000", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "10152", ["ba01_landB01"] = "10153", ["ba01_landC01"] = "10155", ["ba10_waitB01"] = "10010", ["ba20_buturi01"] = "10400", ["ba21_tokusyu01"] = "10450", ["ba30_damageS01"] = "10500", ["ba41_down01"] = "10520", ["ba02_roar01"] = "10300", ["eye01"] = "18000", ["mouth01"] = "18100", ["fi20_walk01"] = "10030", ["fi21_run01"] = "10100", ["kw20_drowseA01"] = "10280", ["kw20_drowseB01"] = "10281", ["kw20_drowseC01"] = "10282", ["kw21_sleepA01"] = "10280", ["kw21_sleepB01"] = "10281", ["kw30_hate01"] = "10563", ["kw32_happyA01"] = "10550", ["kw32_happyB01"] = "10011", ["kw32_happyC01"] = "10550", ["kw33_moveA01"] = "10150", ["kw33_moveC01"] = "10155", ["kw34_lonely01"] = "10560", ["kw35_playA01"] = "10011", ["kw36_mad01"] = "10563", ["kw50_eatA01"] = "10290", ["kw50_eatB01"] = "10291", ["kw50_eatC01"] = "10292", ["kw60_touch01"] = "10320" }),
        [731] = new(821, 731, 53, 50, 127, 30, 30, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceStem: "pm0807_00_00"),
        [732] = new(822, 731, 124, 50, 127, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "20000", SourceStem: "pm0808_00_00", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "20152", ["ba01_landB01"] = "20153", ["ba01_landC01"] = "20155", ["ba10_waitB01"] = "20010", ["ba20_buturi01"] = "20400", ["ba21_tokusyu01"] = "20450", ["ba30_damageS01"] = "20500", ["ba41_down01"] = "20520", ["ba02_roar01"] = "20300", ["eye01"] = "28000", ["mouth01"] = "28100", ["kw32_happyB01"] = "20011", ["kw35_playA01"] = "20011" }),
        [733] = new(521, 731, 218, 50, 127, 110, 110, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, LayerEmission: true, SourceLayeredAlbedo: true, CompactTextureReferences: true, BattleIdle: "00000", SourceStem: "pm0809_00_00"),
        [734] = new(263, 734, 51, 50, 127, 40, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceStem: "pm0826_00_00"),
        [735] = new(264, 734, 146, 50, 127, 70, 70, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "00000", SourceStem: "pm0827_00_00"),
        [741] = new(122, 741, 167, 75, 191, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, SourceStem: "pm0825_11_00"),
        [774] = new(362, 774, 154, 0, 255, 30, 30, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: false, EmptyEyes: true, BattleIdle: "00001", SourceStem: "pm0824_11_00", GenderKind: 3, MotionOverrides: new Dictionary<string, string> { ["eye01"] = "00000", ["ba01_landA01"] = "00152", ["ba01_landB01"] = "00153", ["ba01_landC01"] = "00155", ["ba10_waitB01"] = "00010", ["ba20_buturi01"] = "00400", ["ba21_tokusyu01"] = "00450", ["ba30_damageS01"] = "00500", ["ba41_down01"] = "00520", ["ba02_roar01"] = "00300", ["mouth01"] = "08100", ["fi20_walk01"] = "00030", ["fi21_run01"] = "00100", ["kw20_drowseA01"] = "00280", ["kw20_drowseB01"] = "00281", ["kw20_drowseC01"] = "00282", ["kw21_sleepA01"] = "00280", ["kw21_sleepB01"] = "00281", ["kw30_hate01"] = "00563", ["kw32_happyA01"] = "00550", ["kw32_happyB01"] = "00011", ["kw32_happyC01"] = "00550", ["kw33_moveA01"] = "00150", ["kw33_moveC01"] = "00155", ["kw34_lonely01"] = "00560", ["kw35_playA01"] = "00011", ["kw36_mad01"] = "00563", ["kw50_eatA01"] = "00290", ["kw50_eatB01"] = "00291", ["kw50_eatC01"] = "00292", ["kw60_touch01"] = "00320" }),
        [775] = new(446, 775, 168, 50, 127, 40, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, EmptyEyes: true, SourceStem: "pm0811_00_00", MotionOverrides: new Dictionary<string, string> { ["eye01"] = "00000" }),
        [779] = new(318, 779, 166, 50, 127, 90, 90, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "10000", SourceStem: "pm0855_00_00", MotionOverrides: new Dictionary<string, string> { ["ba01_landA01"] = "10152", ["ba01_landB01"] = "10153", ["ba01_landC01"] = "10155", ["ba10_waitB01"] = "10010", ["ba20_buturi01"] = "10400", ["ba21_tokusyu01"] = "10450", ["ba30_damageS01"] = "10500", ["ba41_down01"] = "10520", ["ba02_roar01"] = "10300", ["eye01"] = "18000", ["mouth01"] = "18100", ["fi20_walk01"] = "10030", ["fi21_run01"] = "10100", ["kw20_drowseA01"] = "10280", ["kw20_drowseB01"] = "10281", ["kw20_drowseC01"] = "10282", ["kw21_sleepA01"] = "10280", ["kw21_sleepB01"] = "10281", ["kw30_hate01"] = "10563", ["kw32_happyA01"] = "10550", ["kw32_happyB01"] = "10011", ["kw32_happyC01"] = "10550", ["kw33_moveA01"] = "10150", ["kw33_moveC01"] = "10155", ["kw34_lonely01"] = "10560", ["kw35_playA01"] = "10011", ["kw36_mad01"] = "10563", ["kw50_eatA01"] = "10290", ["kw50_eatB01"] = "10291", ["kw50_eatC01"] = "10292", ["kw60_touch01"] = "10320" }),
        [193] = new(12, 193, 78, 50, 127, 120, 120, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, AnimationOffset: 20000),
        [203] = new(78, 203, 159, 50, 127, 150, 150, AuthoredMaterials: true, MeshVisibility: true, CornealEyes: true),
        [204] = new(273, 204, 58, 50, 127, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, LayeredEyes: true, CornealEyes: true, BattleIdle: "00000", EmptyMouth: true,
            MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }),
        [205] = new(598, 204, 163, 50, 127, 120, 120, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, LayeredEyes: true, CornealEyes: true, AnimationOffset: 20000, BattleIdle: "00000", EmptyMouth: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .1f, ["r_eye"] = .1f },
            MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }),
        [207] = new(330, 207, 86, 50, 127, 110, 110, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [218] = new(568, 218, 50, 50, 127, 70, 70, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, LayeredEyes: true, LayerEmission: true, LavaLayers: true),
        [219] = new(324, 218, 151, 50, 127, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, LayeredEyes: true, LayerEmission: true, LavaLayers: true),
        [231] = new(220, 231, 66, 50, 127, 50, 50, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .75f, ["r_eye"] = .75f }),
        [232] = new(473, 231, 175, 50, 127, 110, 110, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [261] = new(263, 261, 56, 50, 127, 50, 50, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [262] = new(264, 261, 147, 50, 127, 100, 100, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .05f, ["r_eye"] = .05f }),
        [285] = new(590, 285, 59, 50, 127, 40, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [286] = new(106, 285, 161, 50, 127, 120, 120, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .17f, ["r_eye"] = .17f }),
        [287] = new(759, 287, 56, 50, 127, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "00000"),
        [288] = new(760, 287, 154, 50, 127, 140, 140, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [289] = new(143, 287, 285, 50, 127, 170, 200, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "00000"),
        [331] = new(556, 331, 67, 50, 127, 40, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, LayeredEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .1f, ["r_eye"] = .1f }),
        [332] = new(556, 331, 166, 50, 127, 130, 130, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, LayeredEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .02f, ["r_eye"] = .02f }),
        [386] = new(150, 386, 300, 0, 255, 170, 170, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, LayerEmission: true, SourceStem: "pm0386_11_00", SourceVoiceForm: 0, GenderKind: 3, TransparentBody: true,
            EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }, StaticUvScales: DeoxysAtlas("11"), CenteredStaticUv: true),
        [469] = new(738, 193, 180, 50, 127, 170, 190, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, AnimationOffset: 20000, ScalarEyes: true),
        [472] = new(330, 207, 179, 50, 127, 170, 200, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .05f, ["r_eye"] = .05f }),
        [84] = new(83, 84, 62, 50, 127, 110, 140, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [85] = new(521, 84, 165, 50, 127, 140, 180, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [86] = new(363, 86, 65, 50, 127, 90, 110, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            ScalarEyes: true, EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }),
        [87] = new(365, 86, 166, 50, 127, 120, 170, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            BattleIdle: "10001", MotionOverrides: new Dictionary<string, string>
            {
                ["ba01_landA01"] = "10152", ["ba01_landB01"] = "10153", ["ba01_landC01"] = "10155",
                ["ba10_waitB01"] = "10010", ["ba20_buturi01"] = "10400", ["ba21_tokusyu01"] = "10450",
                ["ba30_damageS01"] = "10500", ["ba41_down01"] = "10520", ["ba02_roar01"] = "10300",
                ["eye01"] = "18000", ["mouth01"] = "18100",
                ["kw32_happyB01"] = "00550", ["kw35_playA01"] = "00550",
            }),
        [88] = new(568, 88, 65, 50, 127, 90, 90, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "00000", SurfaceFlow: true),
        [89] = new(569, 88, 175, 50, 127, 120, 120, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true, BattleIdle: "00000", SurfaceFlow: true),
        [96] = new(239, 96, 66, 50, 127, 100, 100, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [97] = new(122, 96, 169, 50, 127, 140, 160, AuthoredMaterials: true, MeshVisibility: true, CornealEyes: true,
            ScalarEyes: true, EmptyMouth: true, MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }),
        [100] = new(109, 100, 66, 0, 255, 50, 50, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            BattleIdle: "00000", GenderKind: 3, GiantVerticalOffset: 0, ScalarEyes: true, EmptyMouth: true,
            MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" }),
        [101] = new(362, 100, 172, 0, 255, 120, 120, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            BattleIdle: "00000", GenderKind: 3),
        [48] = new(595, 48, 61, 50, 127, 100, 100, AuthoredMaterials: true, FramingTemplate: 596, MeshVisibility: true,
            CornealEyes: true, ScalarEyes: true, BattleIdle: "00000", MotionOverrides: new Dictionary<string, string>
            { ["ba10_waitB01"] = "00000", ["kw32_happyB01"] = "00550", ["kw35_playA01"] = "00550", ["mouth01"] = "00000" }, EmptyMouth: true),
        [49] = new(12, 48, 158, 50, 127, 100, 150, AuthoredMaterials: true, MeshVisibility: true,
            AnimationOffset: 20000, BattleIdle: "00000", GiantVerticalOffset: 0, AuthoredAlpha: true, TransparentEyes: true,
            StaticUvScales: new Dictionary<string, System.Numerics.Vector2>
            {
                ["pm0049_00_00_l_eye_a_mesh_shape"] = new(1, 2),
                ["pm0049_00_00_r_eye_a_mesh_shape"] = new(1, 2),
                ["pm0049_00_00_l_eye_b_mesh_shape"] = new(4, 2),
                ["pm0049_00_00_r_eye_b_mesh_shape"] = new(4, 2),
                ["pm0049_00_00_l_hghtlight_mesh_shape"] = new(4, 8),
                ["pm0049_00_00_r_hghtlight_mesh_shape"] = new(4, 8),
            }),
        [979] = new(766, 56, 268, 50, 127, 150, 120, AuthoredMaterials: true, MeshVisibility: true,
            SummaryFramingStem: "pm0815_00",
            SourceStem: "pm1114_00_00", SourceSpecies: 1010, CornealEyes: true, EmptyMouth: true,
            MotionOverrides: new Dictionary<string, string> { ["mouth01"] = "00000" },
            NormalUvScales: new Dictionary<string, System.Numerics.Vector2>
            {
                ["pm1114_00_00_ring_mesh_shape"] = new(1, 2),
                ["pm1114_00_00_foot_mesh_shape"] = new(1, 2),
            },
            NormalUvVerticalTiles: new Dictionary<string, int>
            {
                ["pm1114_00_00_ring_mesh_shape"] = 1,
                ["pm1114_00_00_foot_mesh_shape"] = 1,
            }),
        [74] = new(109, 74, 60, 50, 127, 50, 40, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, AnimationOffset: 20000, CornealEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .05f }, GiantVerticalOffset: 0),
        [75] = new(525, 74, 137, 50, 127, 100, 100, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [76] = new(112, 74, 223, 50, 127, 140, 140, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true,
            CornealNormalHeights: new Dictionary<string, float> { ["l_eye"] = .01f, ["r_eye"] = .01f },
            NormalUvScales: new Dictionary<string, System.Numerics.Vector2>
            {
                ["pm0076_00_00_face_mesh_shape"] = new(1, 2),
                ["pm0076_00_00_leftfoot_mesh_shape"] = new(2, 1),
                ["pm0076_00_00_rightfoot_mesh_shape"] = new(2, 1),
                ["pm0076_00_00_lefthand_mesh_shape"] = new(2, 1),
                ["pm0076_00_00_righthand_mesh_shape"] = new(2, 1),
            }),
        [495] = new(252, 495, 62, 12, 31, 60, 60, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true),
        [496] = new(253, 495, 145, 12, 31, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, OpaqueEyeSurface: true),
        [497] = new(350, 495, 238, 12, 31, 160, 330, AuthoredMaterials: true, BattleIdle: "00000", MeshVisibility: true, SharedMaterialTextures: true, GiantScale: 12),
        [501] = new(7, 501, 62, 12, 31, 50, 50, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [502] = new(8, 501, 145, 12, 31, 80, 80, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true, CornealEyes: true),
        [503] = new(365, 501, 238, 12, 31, 150, 150, AuthoredMaterials: true, MeshVisibility: true, SharedMaterialTextures: true),
        [155] = new(4, 155, 62, 12, 31, 50, 50, AuthoredMaterials: true, NativeFlames: true, FlameMaterial: "body_b",
            MouthPosition: new(.115f, 0, .162f)),
        [156] = new(5, 155, 142, 12, 31, 90, 90, AuthoredMaterials: true, NativeFlames: true, FlameMaterial: "body_b",
            MouthPosition: new(.187f, 0, .095f)),
        [157] = new(467, 155, 240, 12, 31, 170, 170, AuthoredMaterials: true, NativeFlames: true, FlameMaterial: "body_b",
            MouthPosition: new(.33f, 0, .135f)),
        [23] = new(147, 23, 58, 50, 127, 50, 50, AuthoredMaterials: true, FramingTemplate: 7),
        [24] = new(350, 23, 157, 50, 127, 160, 200, AuthoredMaterials: true),
        [387] = new(1, 387, 64, 12, 31, 60, 40, AuthoredMaterials: true, BattleIdle: "00000", CompactResources: true),
        [388] = new(2, 387, 142, 12, 31, 100, 110, AuthoredMaterials: true, CompactResources: true),
        [389] = new(3, 387, 263, 12, 31, 220, 220, AuthoredMaterials: true, CompactResources: true),
        [390] = new(810, 390, 62, 12, 31, 65, 50, AuthoredMaterials: true, NativeFlames: true, ResourceStem: "pm0948_00_00", CompactResources: true),
        [391] = new(811, 390, 142, 12, 31, 100, 90, AuthoredMaterials: true, NativeFlames: true, ResourceStem: "pm0949_00_00", CompactResources: true),
        [392] = new(766, 390, 267, 12, 31, 150, 120, AuthoredMaterials: true, NativeFlames: true, ResourceStem: "pm0815_00", CompactResources: true),
        [393] = new(7, 393, 63, 12, 31, 66, 40),
        [394] = new(8, 393, 142, 12, 31, 100, 80),
        [395] = new(9, 393, 265, 12, 31, 150, 170),
    };
}
