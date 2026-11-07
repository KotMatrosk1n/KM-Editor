// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.ZA;

public static partial class ZaSwShFresnelMaterial
{
    // Native SwSh supplies scene lighting, weather and giant effects. Use the
    // source forward material equations independently of the donor deferred
    // and aura scene variants; this is an environment policy, not a guessed
    // default for an authored material option.
    public const int NativeLightingVariant = 0;
    public sealed record Settings(int LayerUv, int AmbientUv, int RequiredUvs)
    {
        public int SourceVariant { get; init; } = NativeLightingVariant;
        public int MaskUv { get; init; }
        public bool MaskedEmission => SourceVariant is 6 or 8;
        public bool SecondaryUv => LayerUv == 1 || AmbientUv == 1 || MaskedEmission && MaskUv == 1;
        public string Program => ZaSwShFresnelMaterial.Program(LayerUv, AmbientUv, SourceVariant, MaskUv);
    }

    public static bool MatchesGeometryPrograms(string vertex, string fragment, bool secondaryUv)
    {
        if (vertex != fragment) return false;
        for (var layer = -1; layer <= 1; layer++) for (var ambient = -1; ambient <= 1; ambient++)
            if (vertex == Program(layer, ambient, NativeLightingVariant, 0))
                return secondaryUv == (layer == 1 || ambient == 1);
        // Exact Mega Crabominable, Froslass and Glalie FresnelEffect use this
        // forward, no-aura program, with all three source selectors zero.
        if (vertex == Program(0, 0, 6, 0)) return !secondaryUv;
        if (vertex == Program(1, 1, 6, 0)) return secondaryUv;
        return false;
    }

    public static Settings ReadSettings(byte[] bytes, string materialName)
    {
        _ = Read(bytes, materialName);
        var source = new ModelBuffer(bytes);
        var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == materialName);
        var shader = source.Tables(material, 1, 16).Single();
        var options = source.Tables(shader, 1, 128);
        var masked = QualifiesCrabominableEmissionMask(bytes, materialName)
            || QualifiesFroslassEmissionMask(bytes, materialName)
            || QualifiesGlalieEmissionMask(bytes, materialName)
            || QualifiesAbomasnowEmissionMask(bytes, materialName);
        var required = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EnableBaseColorMap"] = "True", ["EnableEmissionColorMap"] = "False",
            ["EnableEmissionNormalMap"] = "False", ["RefractionMode"] = "NormalOffset",
            ["EnableLocalIBL"] = "True", ["EnableFresnelTexture"] = "True",
            ["EnableFresnelEmission"] = "False", ["EnableAOMap"] = "True", ["EnableNormalMap"] = "True",
        };
        if (masked) required.Add("EnableEmissionMaskMap", "True");
        if (options.Length != required.Count + 1
            || options.Select(t => source.Text(t, 0)).Distinct(StringComparer.Ordinal).Count() != options.Length
            || required.Any(pair => !options.Any(t => source.Text(t, 0) == pair.Key && source.Text(t, 1) == pair.Value)))
            throw new InvalidDataException("ZA gem authored shader switches require another adapter.");
        var uvOption = options.SingleOrDefault(t => source.Text(t, 0) == "NumRequiredUV");
        var uvCount = uvOption == 0 ? null : source.Text(uvOption, 1);
        if (uvCount is not ("1" or "2")) throw new InvalidDataException("ZA gem source UV declaration is unsupported.");
        var rows = source.Tables(material, 9, 128);
        string[] names = ["CastShadow", "ReceiveShadow", "CategoryLabel", "EnableGlobalWeather", "UVIndexLayer1", "UVIndexAO"];
        if (masked) names = [.. names, "UVIndexLayer2"];
        if (!rows.Select(t => source.Text(t, 0)).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("ZA gem source integer controls require another adapter.");
        int Integer(string name)
        {
            var row = rows.Single(t => source.Text(t, 0) == name);
            var vt = row - source.I32(row); var count = (source.U16(vt) - 4) / 2;
            if (Enumerable.Range(2, Math.Max(0, count - 2)).Any(f => source.Field(row, f) != 0))
                throw new InvalidDataException("ZA gem source integer layout requires another adapter.");
            // Actual source facade/backend getters write zero for an omitted
            // value field, before converting it to the scalar uniform bank.
            var at = source.Field(row, 1); return at == 0 ? 0 : source.I32(at);
        }
        if (Integer("CastShadow") != 1 || Integer("ReceiveShadow") != 1 || Integer("CategoryLabel") != 6
            || Integer("EnableGlobalWeather") != 0)
            throw new InvalidDataException("ZA gem source draw controls require another adapter.");
        var layer = Integer("UVIndexLayer1"); var ambient = Integer("UVIndexAO");
        if (layer is < -1 or > 1 || ambient is < -1 or > 1 || uvCount == "1" && (layer == 1 || ambient == 1))
            throw new InvalidDataException("ZA gem source UV selectors require another adapter.");
        var abomasnow = QualifiesAbomasnowEmissionMask(bytes, materialName);
        if (masked && (layer != (abomasnow ? 1 : 0) || ambient != (abomasnow ? 1 : 0)
            || Integer("UVIndexLayer2") != 0 || uvCount != (abomasnow ? "2" : "1")))
            throw new InvalidDataException("ZA pinned masked gem requires its exact authored mask UV interface.");
        return new(layer, ambient, uvCount == "1" ? 1 : 2) { SourceVariant = masked ? 6 : NativeLightingVariant };
    }
}
