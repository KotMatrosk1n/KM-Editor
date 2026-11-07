// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Security.Cryptography;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

/// <summary>Exact source ownership for shared tangent-normal body textures.</summary>
public static class ZaSwShTangentBody
{
    public sealed record Contract(string Program, string Normal, Vector2 Wrap, string? Occlusion = null, Vector2? OcclusionWrap = null);

    public static IReadOnlyDictionary<string, Contract> Contracts(byte[] bytes, PreviewScene scene)
    {
        var glalie = scene.Primitives.Any(p => p.Name.StartsWith("pm0362_51_00_", StringComparison.Ordinal));
        var falinks = scene.Primitives.Any(p => p.Name.StartsWith("pm0923_51_00_", StringComparison.Ordinal));
        if (!glalie && !falinks) return new Dictionary<string, Contract>(StringComparer.Ordinal);
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        if (glalie ? sha is not ("1F8BAD8FA650AFEA95F5BC8955C0D39316C4195973C1C22490EAC53799358D54" or "14679C9EEBACA146550BAD1138CB30BB01F1197FD9015E072EE2719951C538E1")
            : sha is not ("50B8C1E185F84CB601300F900714F36958D382CEF8DF828CD3565328746235D9" or "554165EF2C9B791F187826B6C8B9F3B39174CECD5AC316BF4B3E5DE5A519BBFF"))
            throw new InvalidDataException("ZA shared tangent body source identity changed.");
        var source = new ModelBuffer(bytes); var result = new Dictionary<string, Contract>(StringComparer.Ordinal);
        foreach (var name in glalie ? new[] { "body_a_01", "body_b_00", "body_b_01" } : ["body_a", "body_b"])
        {
            var material = source.Tables(source.Root, 1, 128).Single(m => source.Text(m, 0) == name);
            var vectors = source.Tables(material, 7, 128);
            var normalUv = vectors.Single(v => source.Text(v, 0) == "UVScaleOffsetNormal");
            var at = source.Field(normalUv, 1);
            var uv = new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12));
            var height = source.Tables(material, 4, 128).Single(s => source.Text(s, 0) == "NormalHeight");
            var normal = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "NormalMap");
            var samplers = source.Tables(material, 3, 32); var sampler = checked((int)source.Value(normal, 2));
            if (uv != new Vector4(1, 1, 0, 0) || source.Float(source.Field(height, 1)) != 1
                || sampler >= samplers.Length || !scene.Primitives.Any(p => p.Material.Name == name)
                || scene.Primitives.Where(p => p.Material.Name == name).Any(p => p.Material.Alpha != "Opaque"))
                throw new InvalidDataException("ZA shared tangent body requires its unit-height opaque source normal.");
            var wrap = new Vector2(source.Value(samplers[sampler], 9), source.Value(samplers[sampler], 10));
            _ = TrinityTextureAddressing.SwSh(wrap.X); _ = TrinityTextureAddressing.SwSh(wrap.Y);
            string? occlusion = null;
            Vector2? occlusionWrap = null;
            var program = glalie ? SwShZaTangentBodyShader.Body : name == "body_a" ? SwShZaTangentBodyShader.SourceBase
                : SwShZaTangentBodyShader.SharedUvBase;
            if (glalie)
            {
                var ao = source.Tables(material, 2, 64).Single(t => source.Text(t, 0) == "OcclusionMap");
                var aoSampler = checked((int)source.Value(ao, 2));
                if (aoSampler >= samplers.Length) throw new InvalidDataException("Glalie body occlusion sampler is absent.");
                occlusionWrap = new Vector2(source.Value(samplers[aoSampler], 9), source.Value(samplers[aoSampler], 10));
                var baseUv = vectors.Single(v => source.Text(v, 0) == "UVScaleOffset");
                var baseAt = source.Field(baseUv, 1);
                var baseTransform = new Vector4(source.Float(baseAt), source.Float(baseAt + 4), source.Float(baseAt + 8), source.Float(baseAt + 12));
                var mirrored = name is "body_b_00" or "body_b_01";
                if (baseTransform != new Vector4(mirrored ? 2 : 1, 1, 0, 0)
                    || occlusionWrap != new Vector2(mirrored ? 6 : 1, 1))
                    throw new InvalidDataException("Glalie body occlusion transform changed.");
                if (mirrored) program = SwShZaTangentBodyShader.BodyAoMirror;
                occlusion = source.Text(ao, 1)!;
            }
            result.Add(name, new(program, source.Text(normal, 1)!, wrap, occlusion, occlusionWrap));
        }
        return result;
    }

    public static byte[] Bind(byte[] bytes, IReadOnlyDictionary<string, Contract> contracts, IReadOnlyDictionary<string, string> names)
    {
        var source = new ModelBuffer(bytes); var writer = new ModelTableBuilder(bytes);
        int Program(int field, string name)
        {
            var (start, count) = source.Vector(source.Root, field, 4, 256);
            return Enumerable.Range(0, count).Single(i => source.StringAt(start + i * 4) == name);
        }
        foreach (var material in source.Tables(source.Root, 6, 128))
        {
            if (!names.TryGetValue(source.Text(material, 0)!, out var original) || !contracts.TryGetValue(original, out var contract)) continue;
            writer.Int32(writer.Field(material, 8), Program(3, contract.Program));
            writer.Int32(writer.Field(material, 10), Program(5, contract.Program));
            foreach (var role in contract.Occlusion is null ? new[] { "NormalMapTex" } : ["NormalMapTex", "AmbientTex"])
            {
            var wrap = role == "AmbientTex" ? contract.OcclusionWrap!.Value : contract.Wrap;
            var texture = source.Tables(material, 11, 64).Single(t => source.Text(t, 0) == role);
            var sampler = source.Table(texture, 2);
            var absent = Enumerable.Range(0, 9).Where(f => source.Field(sampler, f) == 0 && f is not (1 or 2)).ToHashSet();
            var values = Enumerable.Range(0, 9).Where(f => !absent.Contains(f)).ToDictionary(f => f,
                f => f is 1 or 2 ? ModelTableBuilder.Integer(TrinityTextureAddressing.SwSh(wrap[f - 1]))
                    : source.Slice(source.Field(sampler, f), 4).ToArray());
            writer.Reference(texture, 2, writer.Table(9, values, absent: absent));
            }
        }
        return writer.Finish(source.Root);
    }
}
