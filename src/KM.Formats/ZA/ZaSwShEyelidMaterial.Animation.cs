using System.Numerics;
using KM.Formats.Models;

namespace KM.Formats.ZA;

public static partial class ZaSwShEyelidMaterial
{
    public static (Dictionary<string, float> Scalars, Dictionary<string, Vector3> Vectors) Parameters(Contract contract, Func<string, int, float?> sample)
    {
        Vector4 Vector(string name)
        {
            var fallback = contract.Vectors.GetValueOrDefault(name, name.StartsWith("UVScaleOffset", StringComparison.Ordinal) ? new(1, 1, 0, 0) : new(.5f, .5f, 0, 0));
            return new(sample(name, 0) ?? fallback.X, sample(name, 1) ?? fallback.Y, sample(name, 2) ?? fallback.Z, sample(name, 3) ?? fallback.W);
        }
        var basis = Vector("UVScaleOffset"); var center = Vector("UVCenter0"); var normal = Vector("UVScaleOffsetNormal");
        if (contract.NormalAtlasHalf is not null && normal != contract.Vectors["UVScaleOffsetNormal"])
            throw new InvalidDataException("Cropped eye normal animation needs another coverage adapter.");
        if (contract.StaticNormalCoverage && normal != new Vector4(1, 1, 0, 0))
            throw new InvalidDataException("Mirror-once eye normal animation needs another coverage adapter.");
        var scalars = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["ColorUVScaleU"] = basis.X, ["ColorUVScaleV"] = basis.Y,
            ["ColorUVTranslateU"] = center.X + basis.Z - contract.Origin.X,
            ["ColorUVTranslateV"] = center.Y + basis.W - contract.Origin.Y,
            ["ColorBaseU"] = center.X, ["ColorBaseV"] = center.Y,
            ["Layer1UVScaleU"] = normal.X, ["Layer1UVScaleV"] = -normal.Y,
            ["Layer1UVTranslateU"] = (contract.Origin.X - normal.Z) * normal.X,
            ["Layer1UVTranslateV"] = 1 - (contract.Origin.Y - normal.W) * normal.Y
        };
        if (contract.NormalAtlasHalf is { } half)
        {
            if (half is not (0 or 1)) throw new InvalidDataException("Eye normal atlas half is invalid.");
            scalars["Layer1UVScaleU"] *= 2;
            scalars["Layer1UVTranslateU"] = scalars["Layer1UVTranslateU"] * 2 - half;
        }
        var vectors = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        // Each authored addressing mode is supplied by its native sampler.
        if (contract.LayerEmission)
            scalars.Add("ScaleUV", 0);
        if (contract.LayerTint)
            for (var layer = 0; layer < 4; layer++)
            {
                var color = Vector("BaseColorLayer" + (layer + 1));
                if (color.X < 0 || color.Y < 0 || color.Z < 0)
                    throw new InvalidDataException("Eye tint animation must remain nonnegative.");
                var intensity = sample("EmissionIntensityLayer" + (layer + 1), 0)
                    ?? contract.Scalars["EmissionIntensityLayer" + (layer + 1)];
                var glow = contract.Vectors["EmissionColorLayer" + (layer + 1)];
                if (intensity < 0 || intensity != 0 && new Vector3(glow.X, glow.Y, glow.Z) != TintEmissionColors[layer])
                    throw new InvalidDataException("Eye tint emission requires the qualified color palette.");
                scalars.Add(EmissionScalarNames[layer], intensity);
                vectors.Add(EmissionVectorNames[layer], new(color.X, color.Y, color.Z));
            }
        if (contract.LayerEmission)
            for (var layer = 0; layer < 4; layer++)
            {
                var intensity = sample("EmissionIntensityLayer" + (layer + 1), 0)
                    ?? contract.Scalars["EmissionIntensityLayer" + (layer + 1)];
                var color = Vector("EmissionColorLayer" + (layer + 1));
                if (intensity < 0 || color.X < 0 || color.Y < 0 || color.Z < 0)
                    throw new InvalidDataException("Eye emission animation must remain nonnegative.");
                scalars.Add(EmissionScalarNames[layer], intensity);
                vectors.Add(EmissionVectorNames[layer], new(color.X, color.Y, color.Z));
            }
        if (contract.IndependentHighlights)
        {
            // UV1 is an affine field independent of the iris UV0 animation.
            // Conjugate its V transform with the source texture-origin flip.
            var highlight = Vector("UVScaleOffset1");
            scalars.Add(HighlightScalarNames[0], highlight.X);
            scalars.Add(HighlightScalarNames[1], highlight.Y);
            scalars.Add(HighlightScalarNames[2], contract.Origin.X * highlight.X + highlight.Z);
            scalars.Add(HighlightScalarNames[3], contract.Origin.Y * highlight.Y + 1 - highlight.Y - highlight.W);
            scalars.Add("ScaleUV", 0);
            var color = contract.Vectors["EmissionColorLayer5"] * contract.Scalars["EmissionIntensityLayer5"];
            vectors.Add("EffectColor01", new(color.X, color.Y, color.Z));
        }
        foreach (var (channel, first) in new[] { (3, 0), (4, 2), (2, 4) })
        {
            var role = channel == 3 ? "UpperEyelidColorMap" : channel == 4 ? "LowerEyelidColorMap" : "EyelidShadowMaskMap";
            var wrap = contract.Wraps.GetValueOrDefault(role, Vector2.One);
            for (var component = 0; component < 2; component++)
                scalars.Add(ScalarNames[10 + first + component], wrap[component] switch
                {
                    1 or 6 or 7 => 0,
                    _ => throw new InvalidDataException("Eye addressing needs another adapter.")
                });
            var transform = Vector("UVScaleOffset" + channel);
            var angle = sample("UVRotation" + channel, 0) ?? contract.Scalars.GetValueOrDefault("UVRotation" + channel);
            var cosine = MathF.Cos(angle); var sine = MathF.Sin(angle);
            var horizontal = contract.Origin.X - .5f; var vertical = contract.Origin.Y - .5f;
            Vector3[] rows =
            [
                new(cosine * transform.X, sine * transform.X,
                    (cosine * horizontal + sine * vertical - transform.Z) * transform.X + .5f),
                new(sine * transform.Y, -cosine * transform.Y,
                    .5f - (cosine * vertical - sine * horizontal - transform.W) * transform.Y)
            ];
            for (var row = 0; row < 2; row++)
                if (channel == 2)
                    for (var component = 0; component < 3; component++)
                        scalars.Add(ScalarNames[16 + row * 3 + component], rows[row][component]);
                else vectors.Add(VectorNames[first + row], rows[row]);
        }
        if (scalars.Values.Any(value => !float.IsFinite(value)) || vectors.Values.Any(value => !float.IsFinite(value.X + value.Y + value.Z)))
            throw new InvalidDataException("Eye animation transform is invalid.");
        return (scalars, vectors);
    }

    public static PreviewMaterialTrack[] ReadTracks(ModelBuffer source, int material, float ratio)
    {
        var name = source.Text(material, 0) ?? throw new InvalidDataException("Eye animation is unnamed.");
        var result = new List<PreviewMaterialTrack>();
        PreviewKey[] Keys(int curve) => source.Tables(curve, 0, 18000).Select(key =>
        {
            float Value(int field) => source.Field(key, field) is var offset && offset != 0 ? source.Float(offset) : 0;
            return new PreviewKey(Value(0) * ratio, [Value(1)]);
        }).ToArray();
        foreach (var vector in source.Tables(material, 2, 256))
        {
            var parameter = source.Text(vector, 0)!;
            if (parameter is not ("UVScaleOffset" or "UVScaleOffset1" or "UVScaleOffset2" or "UVScaleOffset3" or "UVScaleOffset4" or "UVScaleOffsetNormal"
                or "EmissionColorLayer1" or "EmissionColorLayer2" or "EmissionColorLayer3" or "EmissionColorLayer4"
                or "BaseColorLayer1" or "BaseColorLayer2" or "BaseColorLayer3" or "BaseColorLayer4"))
                throw new InvalidDataException($"Eye vector animation needs another adapter: {parameter}.");
            var channels = source.Table(vector, 1);
            result.Add(new(name, parameter, Enumerable.Range(0, 4).Select(component => Keys(source.Table(channels, component))).ToArray()));
        }
        foreach (var scalar in source.Tables(material, 1, 256))
        {
            var parameter = source.Text(scalar, 0)!;
            if (parameter is not ("UVRotation2" or "UVRotation3" or "UVRotation4" or "LayerMaskScale5"
                or "EmissionIntensityLayer1" or "EmissionIntensityLayer2" or "EmissionIntensityLayer3" or "EmissionIntensityLayer4"))
                throw new InvalidDataException($"Eye scalar animation needs another adapter: {parameter}.");
            result.Add(new(name, parameter, [Keys(source.Table(scalar, 1))]));
        }
        foreach (var channel in result.SelectMany(track => track.Channels))
            if (channel.Any(key => !float.IsFinite(key.Frame) || key.Frame < 0 || !float.IsFinite(key.Value[0]))
                || channel.Zip(channel.Skip(1)).Any(pair => pair.First.Frame >= pair.Second.Frame))
                throw new InvalidDataException("Eye source animation keys are invalid.");
        return result.ToArray();
    }

    public static PreviewMaterialTrack[] Tracks(Contract contract, IReadOnlyList<PreviewMaterialTrack> source, int frames)
    {
        if (frames is < 1 or > 18000 || source.GroupBy(track => track.Parameter).Any(group => group.Count() != 1))
            throw new InvalidDataException("Eye animation timing or identity is invalid.");
        var raw = source.ToDictionary(track => track.Parameter, StringComparer.Ordinal);
        if (!contract.LayerEmission && source.Any(track => track.Parameter.StartsWith("Emission", StringComparison.Ordinal)
                && !(contract.LayerTint && track.Parameter.StartsWith("EmissionIntensityLayer", StringComparison.Ordinal))))
            throw new InvalidDataException("Eye emission animation requires a qualified material contract.");
        if (!contract.LayerTint && source.Any(track => track.Parameter.StartsWith("BaseColor", StringComparison.Ordinal)))
            throw new InvalidDataException("Eye tint animation requires a qualified material contract.");
        float? Sample(string parameter, int component, int frame)
        {
            if (!raw.TryGetValue(parameter, out var track) || component >= track.Channels.Length || track.Channels[component].Length == 0) return null;
            var keys = track.Channels[component];
            if (frame <= keys[0].Frame) return keys[0].Value[0];
            for (var index = 1; index < keys.Length; index++)
                if (frame < keys[index].Frame)
                    return keys[index - 1].Value[0] + (keys[index].Value[0] - keys[index - 1].Value[0])
                        * (frame - keys[index - 1].Frame) / (keys[index].Frame - keys[index - 1].Frame);
            return keys[^1].Value[0];
        }
        var states = Enumerable.Range(0, frames).Select(frame => Parameters(contract, (name, component) => Sample(name, component, frame))).ToArray();
        PreviewKey[] Curve(Func<int, float> value)
        {
            var keys = Enumerable.Range(0, frames).Select(frame => new PreviewKey(frame, [value(frame)])).ToArray();
            return keys.All(key => key.Value[0] == keys[0].Value[0]) ? [keys[0]] : keys;
        }
        return ScalarNames.Take(10).Concat(ScalarNames.Skip(16)).Concat(contract.IndependentHighlights ? HighlightScalarNames : [])
            .Concat(contract.LayerEmission || contract.LayerTint ? EmissionScalarNames : [])
            .Select(name => new PreviewMaterialTrack(contract.Name, "EyeScalar:" + name, [Curve(frame => states[frame].Scalars[name])]))
            .Concat(VectorNames.Concat(contract.LayerEmission || contract.LayerTint ? EmissionVectorNames : []).Select(name => new PreviewMaterialTrack(contract.Name, "EyeVector:" + name,
                Enumerable.Range(0, 3).Select(component => Curve(frame => states[frame].Vectors[name][component])).ToArray())))
            .Concat(source.Where(track => track.Parameter == "LayerMaskScale5")).ToArray();
    }

    public static byte[] KeepAnimationLocal(byte[] animation, IReadOnlySet<string> materials, bool independentHighlights = false, bool layerEmission = false, bool layerTint = false)
    {
        var source = new ModelBuffer(animation); var writer = new ModelTableBuilder(animation);
        var references = new List<(int Slot, int Target)>();
        foreach (var material in source.Tables(source.Table(source.Root, 2), 0, 128))
        {
            if (!materials.Contains(source.Text(material, 0)!)) continue;
            foreach (var field in new[] { 2, 3 })
            {
                var tracks = source.Tables(material, field, 256);
                var retained = tracks.Where(track => field == 2
                    ? !ScalarNames.Contains(source.Text(track, 0), StringComparer.Ordinal)
                        && source.Text(track, 0) is not ("NormalMapUVScaleU" or "NormalMapUVScaleV" or "Layer1BaseU" or "Layer1BaseV")
                        && (!independentHighlights || !HighlightScalarNames.Contains(source.Text(track, 0), StringComparer.Ordinal)
                            && source.Text(track, 0) != "ScaleUV")
                        && (!(layerEmission || layerTint) || !EmissionScalarNames.Contains(source.Text(track, 0), StringComparer.Ordinal))
                    : !VectorNames.Contains(source.Text(track, 0), StringComparer.Ordinal)
                        && (!independentHighlights || source.Text(track, 0) != "EffectColor01")
                        && (!(layerEmission || layerTint) || !EmissionVectorNames.Contains(source.Text(track, 0), StringComparer.Ordinal))).ToArray();
                if (retained.Length == tracks.Length) continue;
                var slots = writer.Vector(material, field, retained.Length);
                for (var index = 0; index < retained.Length; index++) references.Add((slots[index], retained[index]));
            }
        }
        if (references.Count > 0)
        {
            var copy = writer.Append(animation);
            foreach (var (slot, target) in references) writer.Pointer(slot, copy + target);
        }
        return writer.Finish(source.Root);
    }
}
