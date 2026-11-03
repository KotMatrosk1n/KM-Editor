// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.ZA;

/// <summary>Retains authored gem emission curves in separate native material parameters.</summary>
public static class ZaSwShFresnelAnimation
{
    // The gem material interface owns these scalar names at C5[57/58].
    // Native lighting, weather and global emission parameters remain separate.
    public const string BaseIntensity = "SourceFresnelEmission0";
    public const string LayerIntensity = "SourceFresnelEmission1";

    public static PreviewClip ReadClip(byte[] bytes, PreviewClip skeletal,
        IReadOnlyDictionary<string, (float Base, float Layer)> defaults,
        Func<byte[], PreviewClip> decodeOtherMaterials)
    {
        if (defaults.Count is < 1 or > 16 || defaults.Any(p => string.IsNullOrEmpty(p.Key)
                || !Valid(p.Value.Base) || !Valid(p.Value.Layer)))
            throw new InvalidDataException("ZA gem animation requires its authored emission defaults.");
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var rate = source.Value(source.Table(source.Root, 0), 2, (uint)skeletal.Rate);
        if (rate == 0 || skeletal.Rate <= 0)
            throw new InvalidDataException("ZA gem animation rate is invalid.");
        var tracks = new Dictionary<(string Material, string Parameter), PreviewKey[]>();
        foreach (var track in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(track, 4); if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            {
                var name = source.Text(material, 0)!;
                if (!defaults.ContainsKey(name)) continue;
                if (source.Tables(material, 2, 256).Length != 0)
                    throw new InvalidDataException("ZA gem vector animation requires another source adapter.");
                foreach (var scalar in source.Tables(material, 1, 256))
                {
                    var parameter = source.Text(scalar, 0) switch
                    {
                        "EmissionIntensity" => BaseIntensity,
                        "EmissionIntensityLayer1" => LayerIntensity,
                        _ => throw new InvalidDataException("ZA gem scalar animation requires another source adapter.")
                    };
                    var keys = source.Tables(source.Table(scalar, 1), 0, 18000).Select(key =>
                    {
                        // These qualified source scalar keys contain time/value only.
                        if (source.Field(key, 2) != 0)
                            throw new InvalidDataException("ZA gem key interpolation requires another source adapter.");
                        float Value(int field) => source.Field(key, field) is var at && at != 0 ? source.Float(at) : 0;
                        return new PreviewKey(Value(0) * skeletal.Rate / rate, [Value(1)]);
                    }).ToArray();
                    if (keys.Length == 0 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || !Valid(k.Value[0]))
                        || keys.Zip(keys.Skip(1)).Any(p => p.First.Frame >= p.Second.Frame))
                        throw new InvalidDataException("ZA gem emission keys are invalid.");
                    var identity = (name, parameter);
                    if (tracks.TryGetValue(identity, out var previous)
                        && (previous.Length != keys.Length || previous.Zip(keys).Any(p => !Same(p.First, p.Second))))
                        throw new InvalidDataException("ZA gem emission animation is ambiguous.");
                    tracks[identity] = keys;
                }
                // Only the two consumed scalar streams are removed from the
                // delegated input. All other materials/visibility/poses survive.
                writer.Vector(material, 1, 0);
            }
        }
        var decoded = decodeOtherMaterials(writer.Finish(source.Root));
        if (decoded.Materials.Any(t => defaults.ContainsKey(t.Material)))
            throw new InvalidDataException("ZA gem has an unconsumed material animation channel.");
        var additional = defaults.SelectMany(pair => new[]
        {
            Track(pair.Key, BaseIntensity, pair.Value.Base),
            Track(pair.Key, LayerIntensity, pair.Value.Layer)
        });
        return decoded with { Materials = decoded.Materials.Concat(additional).ToArray() };

        PreviewMaterialTrack Track(string material, string parameter, float initial)
            // The existing native scalar writer retains arbitrary parameter names.
            => new(material, "EyeScalar:" + parameter,
                [tracks.GetValueOrDefault((material, parameter)) ?? [new(0, [initial])]]);
    }

    public static byte[] Controller(byte[] bytes, IReadOnlySet<string> materials)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            if (source.Text(layer, 1) is "eye01" or "mouth01") continue;
            foreach (var material in source.Tables(source.Table(source.Table(layer, 2), 1), 1, 128))
            {
                if (!materials.Contains(source.Text(material, 0)!)) continue;
                var mask = source.Table(material, 2);
                if (mask == 0) throw new InvalidDataException("ZA gem native scalar mask is missing.");
                var names = source.Tables(mask, 1, 256).Select(t => source.Text(t, 0)!)
                    .Concat([BaseIntensity, LayerIntensity]).Distinct(StringComparer.Ordinal).ToArray();
                var replacement = writer.Table(2, new Dictionary<int, byte[]>
                {
                    [0] = [source.Field(mask, 0) is var at && at != 0 ? source.U8(at) : (byte)0]
                });
                writer.Reference(material, 2, replacement); var slots = writer.Vector(replacement, 1, names.Length);
                for (var i = 0; i < names.Length; i++)
                {
                    var entry = writer.Table(1); writer.Pointer(slots[i], entry); writer.Text(entry, 0, names[i]);
                }
            }
        }
        return writer.Finish(source.Root);
    }

    // The caller must first reject authored source curves for every default
    // which differs between palettes. Only synthesized constant resets change.
    public static byte[] PaletteDefaults(byte[] bytes,
        IReadOnlyDictionary<string, (float Base, float Layer)> normal,
        IReadOnlyDictionary<string, (float Base, float Layer)> palette,
        IReadOnlyDictionary<string, string> originalNames)
    {
        if (!normal.Keys.Order(StringComparer.Ordinal).SequenceEqual(palette.Keys.Order(StringComparer.Ordinal))
            || normal.Values.Concat(palette.Values).Any(v => !Valid(v.Base) || !Valid(v.Layer)))
            throw new InvalidDataException("ZA gem animation palette defaults are incomplete.");
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes); var changed = false;
        var materialRoot = source.Table(source.Root, 2);
        if (materialRoot == 0) return bytes;
        foreach (var material in source.Tables(materialRoot, 0, 128))
        {
            if (!originalNames.TryGetValue(source.Text(material, 0)!, out var original)
                || !normal.TryGetValue(original, out var before)) continue;
            var after = palette[original];
            foreach (var scalar in source.Tables(material, 2, 256))
            {
                var name = source.Text(scalar, 0);
                if (name is not (BaseIntensity or LayerIntensity)) continue;
                var oldValue = name == BaseIntensity ? before.Base : before.Layer;
                var newValue = name == BaseIntensity ? after.Base : after.Layer;
                if (BitConverter.SingleToUInt32Bits(oldValue) == BitConverter.SingleToUInt32Bits(newValue)) continue;
                var curve = source.Table(scalar, 2); var at = source.Field(curve, 0);
                if (source.Value(scalar, 1) != 1 || curve == 0
                    || BitConverter.SingleToUInt32Bits(at == 0 ? 0 : source.Float(at)) != BitConverter.SingleToUInt32Bits(oldValue))
                    throw new InvalidDataException("ZA gem palette reset is not its declared synthesized constant.");
                var replacement = writer.Table(1, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Floats([newValue]) });
                writer.Reference(scalar, 2, replacement); changed = true;
            }
        }
        return changed ? writer.Finish(source.Root) : bytes;
    }

    private static bool Valid(float value) => float.IsFinite(value) && value is >= 0 and <= 8;
    private static bool Same(PreviewKey a, PreviewKey b)
        => BitConverter.SingleToUInt32Bits(a.Frame) == BitConverter.SingleToUInt32Bits(b.Frame)
            && BitConverter.SingleToUInt32Bits(a.Value[0]) == BitConverter.SingleToUInt32Bits(b.Value[0]);
}
