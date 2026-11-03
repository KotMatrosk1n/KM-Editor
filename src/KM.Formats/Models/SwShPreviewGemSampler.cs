// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.Models;

/// <summary>Sampler states implemented by the source gem preview renderer.</summary>
internal static class SwShPreviewGemSampler
{
    public static void Require(ModelBuffer data, int binding, bool cube)
    {
        var sampler = data.Table(binding, 2);
        if (sampler == 0) throw new InvalidDataException("ZA gem preview sampler is missing.");
        var vt = sampler - data.I32(sampler); var length = data.U16(vt); var size = data.U16(vt + 2);
        if (length is < 4 or > 22 || length % 2 != 0 || size < 4)
            throw new InvalidDataException("ZA gem preview sampler layout is invalid.");
        var fields = (length - 4) / 2;
        for (var f = 0; f < fields; f++)
        {
            var at = data.Field(sampler, f);
            if (at != 0 && (at < sampler + 4 || at + (f == 4 ? 16 : 4) > sampler + size))
                throw new InvalidDataException("ZA gem preview sampler inline field is invalid.");
        }
        var border = data.Field(sampler, 4);
        var u = data.Value(sampler, 1); var v = data.Value(sampler, 2);
        if (Enumerable.Range(0, fields).Any(f => f is not (1 or 2 or 3 or 4) && data.Field(sampler, f) != 0)
            || data.Value(sampler, 3) != 4 || border == 0
            || Enumerable.Range(0, 4).Any(c => data.Float(border + c * 4) != 1)
            || (cube ? u != 4 || v != 4 : !((u <= 1 && v <= 1) || (u == 2 && v == 0))))
            throw new InvalidDataException("ZA gem preview sampler requires another adapter.");
    }
}
