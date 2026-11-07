// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.ZA;

public static class ZaSwShLayerSurfaceAnimation
{
    private static readonly string[] ResetParameters = [.. ZaSwShLayerSurface.Scalars, .. ZaSwShLayerSurface.Vectors];

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> AnimatedParameters(
        IEnumerable<(byte[] Bytes, PreviewClip Skeletal)> clips,
        IReadOnlyDictionary<string, ZaSwShLayerSurface.Contract> contracts)
    {
        var names = contracts.Keys.ToDictionary(name => name, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var noResets = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var (bytes, skeletal) in clips)
            foreach (var track in Read(bytes, skeletal, contracts, _ => skeletal with { Materials = [] }, noResets).Materials)
                names[track.Material].Add(track.Parameter["EyeScalar:".Length..]);
        return names.ToDictionary(p => p.Key, p => (IReadOnlySet<string>)p.Value, StringComparer.Ordinal);
    }

    public static PreviewClip Read(byte[] bytes, PreviewClip skeletal,
        IReadOnlyDictionary<string, ZaSwShLayerSurface.Contract> contracts, Func<byte[], PreviewClip> decode,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? resetParameters = null)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var rate = source.Value(source.Table(source.Root, 0), 2, (uint)skeletal.Rate);
        if (rate == 0 || skeletal.Rate <= 0) throw new InvalidDataException("Source layer surface animation rate is invalid.");
        var tracks = new Dictionary<(string Material, string Parameter), PreviewKey[]>();
        PreviewKey[] Keys(int curve)
        {
            var keys = source.Tables(curve, 0, 18000).Select(k =>
            {
                if (source.Value(k, 4) > 1 || source.Value(k, 2) != 0 || source.Value(k, 3) != 0)
                    throw new InvalidDataException("Source layer surface interpolation requires another adapter.");
                float Value(int f) => source.Field(k, f) is var at && at != 0 ? source.Float(at) : 0;
                return new PreviewKey(Value(0) * skeletal.Rate / rate, [Value(1)]) { Step = source.Value(k, 4) == 0 };
            }).ToArray();
            if (keys.Length == 0 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || !float.IsFinite(k.Value[0]))
                || keys.Zip(keys.Skip(1)).Any(p => p.First.Frame >= p.Second.Frame))
                throw new InvalidDataException("Source layer surface keys are invalid.");
            return keys;
        }
        void Add(string material, string parameter, PreviewKey[] keys)
        {
            var identity = (material, parameter);
            if (tracks.TryGetValue(identity, out var previous) && (previous.Length != keys.Length || previous.Zip(keys)
                .Any(p => p.First.Frame != p.Second.Frame || p.First.Step != p.Second.Step || !p.First.Value.SequenceEqual(p.Second.Value))))
                throw new InvalidDataException("Source layer surface animation is ambiguous.");
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
                    if (!ZaSwShLayerSurface.Scalars.Contains(parameter)) throw new InvalidDataException("Unknown source Unlit scalar animation.");
                    Add(name, ZaSwShLayerSurface.Name(parameter), Keys(source.Table(scalar, 1)));
                }
                foreach (var vector in source.Tables(material, 2, 256))
                {
                    var parameter = source.Text(vector, 0)!;
                    if (!ZaSwShLayerSurface.Vectors.Contains(parameter)) throw new InvalidDataException("Unknown source Unlit vector animation.");
                    var channels = source.Table(vector, 1);
                    for (var c = 0; c < 4; c++)
                    {
                        var curve = source.Table(channels, c);
                        if (curve != 0) Add(name, ZaSwShLayerSurface.Name(parameter) + c, Keys(curve));
                    }
                }
                writer.Vector(material, 1, 0); writer.Vector(material, 2, 0);
            }
        }
        var decoded = decode(writer.Finish(source.Root));
        // The native material animator supplies seconds to the scrolling source
        // effect. It follows the same clip and playback rate as its geometry.
        foreach (var (name, contract) in contracts.Where(p => p.Value.Shader.EndsWith("Noise", StringComparison.Ordinal)))
            Add(name, ZaSwShLayerSurface.Name("EffectTime"), skeletal.Frames > 0
                ? [new(0, [0]), new(skeletal.Frames, [(float)skeletal.Frames / skeletal.Rate])]
                : [new(0, [0])]);
        foreach (var (name, contract) in contracts)
            foreach (var parameter in ResetParameters)
            {
                var scalar = Array.IndexOf(ZaSwShLayerSurface.Scalars, parameter);
                if (scalar >= 0)
                {
                    var native = ZaSwShLayerSurface.Name(parameter);
                    if (resetParameters is null || resetParameters.TryGetValue(name, out var owned) && owned.Contains(native))
                        tracks.TryAdd((name, native), [new(0, [contract.Values[scalar]])]);
                }
                else for (var c = 0; c < 4; c++)
                {
                    var index = ZaSwShLayerSurface.ParameterIndex(parameter, c);
                    var native = ZaSwShLayerSurface.Name(parameter) + c;
                    if (resetParameters is null || resetParameters.TryGetValue(name, out var owned) && owned.Contains(native))
                        tracks.TryAdd((name, native), [new(0, [contract.Values[index]])]);
                }
            }
        return decoded with { Materials = decoded.Materials.Concat(tracks.Select(p => new PreviewMaterialTrack(p.Key.Material, "EyeScalar:" + p.Key.Parameter, [p.Value]))).ToArray() };
    }

    public static byte[] Controller(byte[] bytes, IReadOnlySet<string> materials, string[]? ownedNames = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? animatedParameters = null)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var layer in source.Tables(source.Table(source.Root, 0), 0, 128))
        {
            if (source.Text(layer, 1) is "eye01" or "mouth01") continue;
            foreach (var material in source.Tables(source.Table(source.Table(layer, 2), 1), 1, 128))
            {
                if (!materials.Contains(source.Text(material, 0)!)) continue;
                var mask = source.Table(material, 2);
                if (mask == 0) throw new InvalidDataException("Source layer surface scalar controller mask is missing.");
                var owned = animatedParameters is null ? ownedNames ?? ZaSwShLayerSurface.Names
                    : animatedParameters[source.Text(material, 0)!].Order(StringComparer.Ordinal).ToArray();
                var names = source.Tables(mask, 1, 256).Select(t => source.Text(t, 0)!).Concat(owned).Distinct(StringComparer.Ordinal).ToArray();
                var replacement = writer.Table(2, new Dictionary<int, byte[]> { [0] = [source.Field(mask, 0) is var at && at != 0 ? source.U8(at) : (byte)0] });
                writer.Reference(material, 2, replacement); var slots = writer.Vector(replacement, 1, names.Length);
                for (var i = 0; i < names.Length; i++) { var row = writer.Table(1); writer.Pointer(slots[i], row); writer.Text(row, 0, names[i]); }
            }
        }
        return writer.Finish(source.Root);
    }
}
