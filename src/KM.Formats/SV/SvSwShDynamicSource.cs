// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SV;

/// <summary>Independent source mask weights and base tint, retaining the native lighting prefix.</summary>
public static class SvSwShDynamicSource
{
    public static readonly string[] Names = ["SourceMaskScale1", "SourceMaskScale2", "SourceMaskScale3", "SourceMaskScale4",
        "SourceBaseColor0", "SourceBaseColor1", "SourceBaseColor2", "SourceBaseColor3"];
    private static float[] Values(SvSwShLayerEmission.Contract c) => [c.MaskScale.X, c.MaskScale.Y, c.MaskScale.Z, c.MaskScale.W,
        c.GlobalBaseColor.X, c.GlobalBaseColor.Y, c.GlobalBaseColor.Z, c.GlobalBaseColor.W];

    public static byte[] Template(byte[] bytes, string program)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var (start, count) = source.Vector(source.Root, 3, 4, 256);
        var index = Enumerable.Range(0, count).Single(i => source.StringAt(start + i * 4) == program);
        var material = source.Tables(source.Root, 6, 128).Single(m => source.Value(m, 8) == index);
        var rows = source.Tables(material, 13, 256);
        if (rows.Length != 52) throw new InvalidDataException("Dynamic source requires the native scalar prefix.");
        var slots = writer.Vector(material, 13, 52 + Names.Length);
        for (var i = 0; i < slots.Length; i++)
        {
            var at = i < 52 ? source.Field(rows[i], 1) : 0; var absent = i < 52 && at == 0;
            var row = writer.Table(2, absent ? null : new Dictionary<int, byte[]> { [1] = i < 52 ? source.Slice(at, 4).ToArray() : ModelTableBuilder.Floats(1) },
                absent: absent ? new HashSet<int> { 1 } : null);
            writer.Pointer(slots[i], row); writer.Text(row, 0, i < 52 ? source.Text(rows[i], 0)! : Names[i - 52]);
        }
        return writer.Finish(source.Root);
    }

    public static byte[] Bind(byte[] bytes, IReadOnlyDictionary<string, SvSwShLayerEmission.Contract> contracts, IReadOnlyDictionary<string, string> originals)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            if (!contracts.TryGetValue(originals[source.Text(material, 0)!], out var contract) || !contract.DynamicSource) continue;
            var rows = source.Tables(material, 13, 256);
            if (rows.Length != 60 || !rows.Skip(52).Select(t => source.Text(t, 0)).SequenceEqual(Names))
                throw new InvalidDataException("Dynamic source scalar interface changed.");
            var values = Values(contract);
            for (var i = 0; i < values.Length; i++) writer.Single(writer.Field(rows[52 + i], 1), values[i]);
        }
        return writer.Finish(source.Root);
    }

    public static PreviewClip Read(byte[] bytes, PreviewClip skeletal, IReadOnlyDictionary<string, SvSwShLayerEmission.Contract> contracts, Func<byte[], PreviewClip> decode)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        var rate = source.Value(source.Table(source.Root, 0), 2, (uint)skeletal.Rate);
        if (rate == 0) throw new InvalidDataException("Dynamic source animation rate is invalid.");
        var tracks = new Dictionary<(string Material, int Index), PreviewKey[]>();
        var retained = new List<(int Slot, int Target)>();
        void Add(string name, int index, int curve)
        {
            var keys = source.Tables(curve, 0, 18000).Select(k =>
            {
                if (source.Field(k, 2) != 0)
                    throw new InvalidDataException("Dynamic source interpolation changed.");
                float Value(int f) => source.Field(k, f) is var at && at != 0 ? source.Float(at) : 0;
                return new PreviewKey(Value(0) * skeletal.Rate / rate, [Value(1)]);
            }).ToArray();
            if (keys.Length == 0 || keys.Any(k => !float.IsFinite(k.Frame) || k.Frame < 0 || !float.IsFinite(k.Value[0]) || k.Value[0] < 0)
                || keys.Zip(keys.Skip(1)).Any(p => p.First.Frame >= p.Second.Frame))
                throw new InvalidDataException("Dynamic source keys are invalid.");
            if (tracks.TryGetValue((name, index), out var prior) && (prior.Length != keys.Length || prior.Zip(keys)
                .Any(p => p.First.Frame != p.Second.Frame || !p.First.Value.SequenceEqual(p.Second.Value))))
                throw new InvalidDataException("Dynamic source repeated animation differs.");
            tracks[(name, index)] = keys;
        }
        foreach (var track in source.Tables(source.Root, 1, 512).Where(t => source.Table(t, 4) != 0))
        foreach (var material in source.Tables(source.Table(track, 4), 2, 128))
        {
            var name = source.Text(material, 0)!;
            if (!contracts.TryGetValue(name, out var contract) || !contract.DynamicSource) continue;
            foreach (var field in new[] { 1, 2 })
            {
                var kept = new List<int>();
                foreach (var parameter in source.Tables(material, field, 256))
                {
                    var parameterName = source.Text(parameter, 0)!;
                    var index = field == 1 ? Array.IndexOf(new[] { "LayerMaskScale1", "LayerMaskScale2", "LayerMaskScale3", "LayerMaskScale4" }, parameterName) : parameterName == "BaseColor" ? 4 : -1;
                    if (index < 0) { kept.Add(parameter); continue; }
                    var curve = source.Table(parameter, 1);
                    if (field == 1) Add(name, index, curve);
                    else for (var c = 0; c < 4; c++)
                    {
                        var component = source.Table(curve, c);
                        if (component != 0) Add(name, 4 + c, component);
                    }
                }
                var slots = writer.Vector(material, field, kept.Count);
                for (var i = 0; i < slots.Length; i++) retained.Add((slots[i], kept[i]));
            }
        }
        if (retained.Count != 0)
        {
            var copy = writer.Append(bytes);
            foreach (var (slot, target) in retained) writer.Pointer(slot, copy + target);
        }
        var decoded = decode(writer.Finish(source.Root));
        foreach (var (name, contract) in contracts.Where(p => p.Value.DynamicSource))
        {
            var values = Values(contract);
            for (var i = 0; i < Names.Length; i++) tracks.TryAdd((name, i), [new(0, [values[i]])]);
        }
        return decoded with { Materials = decoded.Materials.Concat(tracks.Select(p => new PreviewMaterialTrack(p.Key.Material,
            "EyeScalar:" + Names[p.Key.Index], [p.Value]))).ToArray() };
    }

    public static byte[] Controller(byte[] bytes, IReadOnlySet<string> materials)
        => KM.Formats.ZA.ZaSwShDisplacedUnlitAnimation.Controller(bytes, materials, Names);
}
