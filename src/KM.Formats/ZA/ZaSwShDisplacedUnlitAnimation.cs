// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.ZA;

public static class ZaSwShDisplacedUnlitAnimation
{
    private static readonly string[] ResetParameters = ["EmissionIntensity", "DisplacementHeight", "UVScaleOffset", "UVScaleOffset3", "UVScaleOffset5"];

    public static PreviewClip Read(byte[] bytes, PreviewClip skeletal,
        IReadOnlyDictionary<string, ZaSwShDisplacedUnlit.Contract> contracts, Func<byte[], PreviewClip> decode)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var rate = source.Value(source.Table(source.Root, 0), 2, (uint)skeletal.Rate);
        if (rate == 0 || skeletal.Rate <= 0) throw new InvalidDataException("Source Unlit animation rate is invalid.");
        var tracks = new Dictionary<(string Material, string Parameter), PreviewKey[]>();
        PreviewKey[] Keys(int curve)
        {
            var keys = source.Tables(curve, 0, 18000).Select(k =>
            {
                if (source.Value(k, 4) > 1 || source.Value(k, 2) != 0 || source.Value(k, 3) != 0)
                    throw new InvalidDataException("Source Unlit interpolation requires another adapter.");
                float Value(int f) => source.Field(k, f) is var at && at != 0 ? source.Float(at) : 0;
                return new PreviewKey(Value(0) * skeletal.Rate / rate, [Value(1)]) { Step = source.Value(k, 4) == 0 };
            }).ToArray();
            if (keys.Length == 0 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || !float.IsFinite(k.Value[0]))
                || keys.Zip(keys.Skip(1)).Any(p => p.First.Frame >= p.Second.Frame))
                throw new InvalidDataException("Source Unlit keys are invalid.");
            return keys;
        }
        void Add(string material, string parameter, PreviewKey[] keys)
        {
            var identity = (material, parameter);
            if (tracks.TryGetValue(identity, out var previous) && (previous.Length != keys.Length || previous.Zip(keys)
                .Any(p => p.First.Frame != p.Second.Frame || p.First.Step != p.Second.Step || !p.First.Value.SequenceEqual(p.Second.Value))))
                throw new InvalidDataException("Source Unlit animation is ambiguous.");
            tracks[identity] = keys;
        }
        foreach (var timelineTrack in source.Tables(source.Root, 1, 512))
        {
            var timeline = source.Table(timelineTrack, 4); if (timeline == 0) continue;
            foreach (var material in source.Tables(timeline, 2, 128))
            {
                var name = source.Text(material, 0)!;
                if (!contracts.ContainsKey(name)) continue;
                foreach (var scalar in source.Tables(material, 1, 256))
                {
                    var parameter = source.Text(scalar, 0)!;
                    if (!ZaSwShDisplacedUnlit.Scalars.Contains(parameter) && parameter is not ("LayerMaskScale5" or "DiscardValue")) throw new InvalidDataException("Unknown source Unlit scalar animation.");
                    Add(name, ZaSwShDisplacedUnlit.Name(parameter), Keys(source.Table(scalar, 1)));
                }
                foreach (var vector in source.Tables(material, 2, 256))
                {
                    var parameter = source.Text(vector, 0)!;
                    if (!ZaSwShDisplacedUnlit.Vectors.Contains(parameter) && parameter is not ("BaseColorLayer5" or "UVScaleOffset5")) throw new InvalidDataException("Unknown source Unlit vector animation.");
                    var channels = source.Table(vector, 1);
                    for (var c = 0; c < 4; c++)
                    {
                        var curve = source.Table(channels, c);
                        if (curve != 0) Add(name, ZaSwShDisplacedUnlit.Name(parameter) + c, Keys(curve));
                    }
                }
                writer.Vector(material, 1, 0); writer.Vector(material, 2, 0);
            }
        }
        var decoded = decode(writer.Finish(source.Root));
        foreach (var (name, contract) in contracts)
            foreach (var parameter in ResetParameters)
            {
                var scalar = Array.IndexOf(ZaSwShDisplacedUnlit.Scalars, parameter);
                if (scalar >= 0) tracks.TryAdd((name, ZaSwShDisplacedUnlit.Name(parameter)), [new(0, [contract.Values[scalar]])]);
                else for (var c = 0; c < 4; c++)
                {
                    var index = ZaSwShDisplacedUnlit.ParameterIndex(parameter, c);
                    tracks.TryAdd((name, ZaSwShDisplacedUnlit.Name(parameter) + c), [new(0, [contract.Values[index]])]);
                }
            }
        return decoded with { Materials = decoded.Materials.Concat(tracks.Select(p => new PreviewMaterialTrack(p.Key.Material, "EyeScalar:" + p.Key.Parameter, [p.Value]))).ToArray() };
    }

    public static byte[] Controller(byte[] bytes, IReadOnlySet<string> materials, string[]? ownedNames = null)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            if (source.Text(layer, 1) is "eye01" or "mouth01") continue;
            foreach (var material in source.Tables(source.Table(source.Table(layer, 2), 1), 1, 128))
            {
                if (!materials.Contains(source.Text(material, 0)!)) continue;
                var mask = source.Table(material, 2);
                if (mask == 0) throw new InvalidDataException("Source Unlit scalar controller mask is missing.");
                var names = source.Tables(mask, 1, 256).Select(t => source.Text(t, 0)!).Concat(ownedNames ?? ZaSwShDisplacedUnlit.Names).Distinct(StringComparer.Ordinal).ToArray();
                var replacement = writer.Table(2, new Dictionary<int, byte[]> { [0] = [source.Field(mask, 0) is var at && at != 0 ? source.U8(at) : (byte)0] });
                writer.Reference(material, 2, replacement); var slots = writer.Vector(replacement, 1, names.Length);
                for (var i = 0; i < names.Length; i++) { var row = writer.Table(1); writer.Pointer(slots[i], row); writer.Text(row, 0, names[i]); }
            }
        }
        return writer.Finish(source.Root);
    }
}
