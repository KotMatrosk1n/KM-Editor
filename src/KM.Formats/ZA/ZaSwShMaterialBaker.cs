// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

/// <summary>Bakes Legends Z-A layered colors, eye highlights, and independent normal UVs into native SwSh textures.</summary>
public static class ZaSwShMaterialBaker
{
    internal sealed record Image(int Width, int Height, bool Srgb, byte[] Pixels);
    public static byte[] Icon(byte[] sourceTexture, byte[] targetTemplate, CancellationToken cancellationToken = default,
        ModelTextureMipCache? encodingCache = null)
    {
        var source = Read(sourceTexture); var target = new ModelTextureDocument(targetTemplate);
        var scale = Math.Min(1d, Math.Min(target.Width / (double)source.Width, target.Height / (double)source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale)); var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var pixels = new byte[target.Width * target.Height * 4]; var left = (target.Width - width) / 2; var top = (target.Height - height) / 2;
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var minX = x * source.Width / (double)width; var maxX = (x + 1) * source.Width / (double)width;
                var minY = y * source.Height / (double)height; var maxY = (y + 1) * source.Height / (double)height;
                var color = Vector3.Zero; double alpha = 0, total = 0;
                for (var sy = (int)minY; sy < Math.Ceiling(maxY); sy++) for (var sx = (int)minX; sx < Math.Ceiling(maxX); sx++)
                {
                    var weight = (Math.Min(maxX, sx + 1) - Math.Max(minX, sx)) * (Math.Min(maxY, sy + 1) - Math.Max(minY, sy));
                    var at = (sy * source.Width + sx) * 4; var opacity = source.Pixels[at + 3] / 255d;
                    color += new Vector3(source.Pixels[at], source.Pixels[at + 1], source.Pixels[at + 2]) * (float)(opacity * weight);
                    alpha += opacity * weight; total += weight;
                }
                var output = ((y + top) * target.Width + x + left) * 4;
                if (alpha > 0)
                {
                    color /= (float)alpha;
                    pixels[output] = Channel(color.X / 255); pixels[output + 1] = Channel(color.Y / 255); pixels[output + 2] = Channel(color.Z / 255);
                }
                pixels[output + 3] = Channel((float)(alpha / total));
            }
        }
        return target.EncodeMipImages(Mips(pixels, target.Width, target.Height, target.MipCount), cancellationToken, encodingCache);
    }
    public static SwShBakedMaterial Bake(PreviewPrimitive primitive, byte[] sourceMaterials, int materialIndex, bool shiny, byte[] textureTemplate, string templateName,
        Func<string, byte[]> readTexture, CancellationToken cancellationToken = default, bool layeredEyes = false,
        bool authoredAlpha = false, ModelTextureMipCache? encodingCache = null, Vector2? normalUvScale = null,
        Vector2? colorUvScale = null, bool unlit = false, ZaSwShNormalAtlas? normalAtlas = null,
        int maximumTextureDimension = 2048)
    {
        if (maximumTextureDimension is < 64 or > 2048 || (maximumTextureDimension & (maximumTextureDimension - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTextureDimension));
        if (materialIndex is < 0 or > 127) throw new InvalidDataException("Material texture identity is outside the supported range.");
        _ = SwShModelWriter.Bounds(primitive, 1);
        var source = new ModelBuffer(sourceMaterials);
        var tables = source.Tables(source.Root, 1, 128).Where(t => source.Text(t, 0) == primitive.Material.Name).ToArray();
        if (tables.Length != 1) throw new InvalidDataException("Source material identity is ambiguous.");
        var table = tables[0];
        var textures = source.Tables(table, 2, 64).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1)!, StringComparer.Ordinal);
        var eye = primitive.Material.Name.Contains("eye", StringComparison.Ordinal);
        var template = ZaSwShTextureTemplate.Convert(primitive, textureTemplate, templateName);
        _ = new ModelTextureDocument(template);
        var parameters = source.Tables(table, 7, 128).ToDictionary(t => source.Text(t, 0)!, t =>
        { var at = source.Field(t, 1); return new Vector4(source.Float(at), source.Float(at + 4), source.Float(at + 8), source.Float(at + 12)); }, StringComparer.Ordinal);
        var scalarParameters = source.Tables(table, 4, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0, StringComparer.Ordinal);
        var uvTransform = parameters.GetValueOrDefault("UVScaleOffset", new(1, 1, 0, 0));
        // -1 inherits the transformed base coordinates; 0 explicitly selects
        // the original UV0. Croconaw's asymmetric belly mask uses the latter
        // even though its base texture doubles and mirrors U.
        var maskUvIndex = source.Tables(table, 9, 128).SingleOrDefault(t => source.Text(t, 0) == "UVIndexLayerMask");
        var maskTransform = (maskUvIndex == 0 ? uint.MaxValue : source.Value(maskUvIndex, 1)) switch
        {
            uint.MaxValue => uvTransform,
            0 => new Vector4(1, 1, 0, 0),
            _ => throw new InvalidDataException("Z-A layer mask UV set needs another material adapter."),
        };
        var normalUv = parameters.GetValueOrDefault("UVScaleOffsetNormal", new(1, 1, 0, 0));
        var normalScale = normalUvScale ?? Vector2.One;
        if (normalScale.X is not (1 or 2) || normalScale.Y is not (1 or 2)
            || normalScale != Vector2.One && normalUv != new Vector4(1, 1, 0, 0))
            throw new InvalidDataException("Z-A generated normal tile requires an untransformed supported allocation.");
        var normalTile = normalScale == Vector2.One ? null : SwShStaticUvAtlas.For(primitive, normalScale);
        if (normalAtlas is not null && (normalScale != Vector2.One || layeredEyes || authoredAlpha || unlit))
            throw new InvalidDataException("Shared Z-A normal atlases require their qualified independent full allocation.");
        var normalGeometry = normalAtlas?.Geometry(primitive, sourceMaterials) ?? primitive;
        var colorScale = colorUvScale ?? Vector2.One;
        if (colorScale != Vector2.One && (authoredAlpha || layeredEyes))
            throw new InvalidDataException("Static Z-A color tiles require an opaque independent material.");
        var colorTile = colorScale == Vector2.One ? null : SwShStaticUvAtlas.CenteredRepeat(primitive, colorScale);
        // These authored eye normals use separate 2-by-4 atlases. Resample the
        // qualified atlas into UV0 before converting its tangent field.
        var atlasOffset = primitive.Name.Length >= 13 ? primitive.Name[..13] switch
        {
            "pm0070_00_00_" or "pm0707_00_00_" => new Vector2(.25f, .125f),
            "pm0708_11_00_" => new Vector2(.5f, .25f),
            "pm0511_00_00_" or "pm0513_00_00_" or "pm0515_00_00_" or "pm0516_00_00_"
                or "pm0706_00_00_" or "pm0774_11_00_" or "pm0860_00_00_" => Vector2.Zero,
            _ => (Vector2?)null
        } : null;
        var eyeAtlas = atlasOffset is { } eyeOffset && normalUv == new Vector4(2, 4, eyeOffset.X, eyeOffset.Y);
        if (normalUv != new Vector4(1, 1, 0, 0) && (!layeredEyes || !eye || !eyeAtlas && (normalUv.X != 1 || normalUv.Y != 1)
            || !float.IsFinite(normalUv.Z + normalUv.W) || MathF.Abs(normalUv.Z) > 4 || MathF.Abs(normalUv.W) > 4))
            throw new InvalidDataException("Z-A normal UV transform needs another material adapter.");
        if (unlit)
        {
            _ = ZaSwShUnlitMaterial.ClefablePalette(sourceMaterials, primitive);
            if (normalScale != Vector2.One || colorScale != Vector2.One || layeredEyes || authoredAlpha)
                throw new InvalidDataException("Z-A unlit baking requires its qualified opaque wing allocation.");
        }
        // This source shader has no normal input. Its lighting-independent native
        // program ignores the neutral object normal; other missing maps still fail.
        var normalSource = unlit
            ? new Image(4, 4, false, Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 128, 128, 255, 255 }).ToArray())
            : Read(readTexture(textures["NormalMap"]));
        if (!unlit && (normalSource.Width < 4 || normalSource.Height < 4))
            normalSource = Read(SwShMaterialBaker.PromoteConstantImage(readTexture(textures["NormalMap"])));
        // Object normal images are unique to each primitive. Bound those
        // repeated allocations independently of the layered color detail.
        var width = Math.Min(512, normalSource.Width); var height = Math.Min(512, normalSource.Height);
        if (normalTile is not null && (width / normalScale.X < 4 || height / normalScale.Y < 4))
            throw new InvalidDataException("Z-A generated normal tile is smaller than a compressed block.");
        var tangentNormals = Resize(normalSource.Pixels, normalSource.Width, normalSource.Height, width, height);
        if (normalUv != new Vector4(1, 1, 0, 0))
        {
            var wrap = primitive.Material.Surface?.Wraps[0] ?? throw new InvalidDataException("Z-A eye normal addressing is missing.");
            for (var row = 0; row < height; row++)
                for (var column = 0; column < width; column++)
                {
                    var sample = Sample(normalSource, new((column + .5f) / width, (row + .5f) / height), normalUv, new(wrap[0], wrap[1]));
                    var offset = (row * width + column) * 4;
                    tangentNormals[offset] = Channel(sample.X); tangentNormals[offset + 1] = Channel(sample.Y);
                }
        }
        var (normalPixels, coverage, conflicts) = ObjectNormalPixels(normalGeometry, tangentNormals, width, height, cancellationToken);
        if (normalAtlas is not null && conflicts != 0)
            throw new InvalidDataException("Shared Z-A object-normal atlas contains incompatible overlapping sections.");
        var ambientWidth = 4; var ambientHeight = 4;
        byte[]? occlusion = null;
        if (textures.TryGetValue("OcclusionMap", out var aoPath))
        {
            var ao = Read(readTexture(aoPath));
            ambientWidth = Math.Min(maximumTextureDimension, ao.Width); ambientHeight = Math.Min(maximumTextureDimension, ao.Height);
            occlusion = Resize(ao.Pixels, ao.Width, ao.Height, ambientWidth, ambientHeight);
        }
        var ambientPixels = new byte[ambientWidth * ambientHeight * 4];
        for (var i = 0; i < ambientWidth * ambientHeight; i++)
        {
            ambientPixels[i * 4] = occlusion is null ? (byte)0 : (byte)(255 - occlusion[i * 4]);
            ambientPixels[i * 4 + 2] = eye ? (byte)166 : (byte)128; ambientPixels[i * 4 + 3] = 255;
        }
        var colorName = ModelTextureIdentity.MaterialName(templateName, materialIndex, 'z', shiny ? 's' : 'n', lowerCase: true);
        var normalName = ModelTextureIdentity.MaterialName(templateName, materialIndex, 'n', 'n', lowerCase: true);
        var ambientName = ModelTextureIdentity.MaterialName(templateName, materialIndex, 'a', 'n', lowerCase: true);
        var baseImage = Read(readTexture(textures["BaseColorMap"]));
        var maskImage = textures.TryGetValue("LayerMaskMap", out var layerMaskPath) ? Read(readTexture(layerMaskPath)) : null;
        var highlightImage = textures.TryGetValue("HighlightMaskMap", out var highlightPath) ? Read(readTexture(highlightPath)) : null;
        var colorWidth = Math.Max(baseImage.Width, Math.Max(maskImage?.Width ?? 4, highlightImage?.Width ?? 4));
        var colorHeight = Math.Max(baseImage.Height, Math.Max(maskImage?.Height ?? 4, highlightImage?.Height ?? 4));
        if (!authoredAlpha)
        {
            colorWidth = Math.Min(maximumTextureDimension, colorWidth);
            colorHeight = Math.Min(maximumTextureDimension, colorHeight);
        }
        var colorContainer = new ModelTextureDocument(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(template, colorWidth, colorHeight, true), templateName, colorName));
        var normalContainer = new ModelTextureDocument(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(template, width, height, false), templateName, normalName));
        var ambientContainer = new ModelTextureDocument(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(template, ambientWidth, ambientHeight, false), templateName, ambientName));
        var highlightTransform = parameters.GetValueOrDefault("UVScaleOffset1", new(1, 1, 0, 0));
        var baseColor = parameters.GetValueOrDefault("BaseColor", Vector4.One);
        var highlightColor = parameters.GetValueOrDefault("EmissionColorLayer5", Vector4.Zero) * scalarParameters.GetValueOrDefault("EmissionIntensityLayer5");
        var pixels = new byte[colorWidth * colorHeight * 4];
        byte ColorChannel(float value) => Channel(value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f);
        for (var y = 0; y < colorHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < colorWidth; x++)
            {
                var uv = new Vector2((x + .5f) / colorWidth, (y + .5f) / colorHeight);
                var original = Sample(baseImage, uv, uvTransform, new(primitive.Material.Wrap.X, primitive.Material.Wrap.Y)) * baseColor;
                var mask = maskImage is null ? Vector4.Zero : Sample(maskImage, uv, maskTransform, new(primitive.Material.Wrap.Z, primitive.Material.Wrap.W));
                var rgb = original;
                for (var layer = 0; maskImage is not null && layer < 4; layer++)
                {
                    var name = "BaseColorLayer" + (layer + 1);
                    if (!parameters.TryGetValue(name, out var tint)) throw new InvalidDataException("Z-A color layer is missing.");
                    var weight = Math.Clamp(mask[layer] * scalarParameters.GetValueOrDefault("LayerMaskScale" + (layer + 1), 1), 0, 1);
                    rgb = Vector4.Lerp(rgb, original * tint, weight);
                }
                if (highlightImage is not null)
                {
                    rgb += Sample(highlightImage, uv, highlightTransform, new(primitive.Material.HighlightWrap.X, primitive.Material.HighlightWrap.Y)).X * highlightColor;
                }
                var at = (y * colorWidth + x) * 4;
                pixels[at] = ColorChannel(rgb.X); pixels[at + 1] = ColorChannel(rgb.Y); pixels[at + 2] = ColorChannel(rgb.Z);
                pixels[at + 3] = authoredAlpha ? Channel(original.W) : (byte)255;
            }
        }
        var colorUvTransform = new Vector4(1, 1, 0, 0);
        var colorMips = SwShTextureFiltering.Mips(pixels, colorWidth, colorHeight, colorContainer.MipCount, srgb: true, sharpen: true);
        if (authoredAlpha)
        {
            var authored = new ModelTextureDocument(readTexture(textures["BaseColorMap"]));
            if (authored.Width != colorWidth || authored.Height != colorHeight || authored.MipCount < colorMips.Length)
                throw new InvalidDataException("Authored transparent atlas mip dimensions are incompatible.");
            for (var mip = 0; mip < colorMips.Length; mip++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var w = Math.Max(1, colorWidth >> mip); var h = Math.Max(1, colorHeight >> mip);
                var image = new Image(w, h, authored.Srgb, authored.Pixels(mip));
                for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
                    colorMips[mip][(y * w + x) * 4 + 3] = Channel(Sample(image, new((x + .5f) / w, (y + .5f) / h),
                        uvTransform, new(primitive.Material.Wrap.X, primitive.Material.Wrap.Y)).W * baseColor.W);
            }
            // The source eye overlays occupy the middle half of a body atlas.
            // Keep that full density rectangle in RGBA so every authored alpha
            // byte is retained without another lossy alpha representation.
            if (colorWidth != 512 || colorHeight != 1024 || primitive.Indices.Distinct().Any(index =>
                1 - primitive.Vertices[index * 16 + 7] <= .375f || 1 - primitive.Vertices[index * 16 + 7] >= .875f))
                throw new InvalidDataException("Transparent eye atlas requires another full density allocation.");
            colorContainer = new(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(template, 512, 512, true, compressed: false), templateName, colorName));
            colorMips = Enumerable.Range(0, colorContainer.MipCount).Select(mip =>
            {
                var w = Math.Max(1, colorWidth >> mip); var sh = Math.Max(1, colorHeight >> mip);
                var h = Math.Max(1, 512 >> mip); var top = Math.Min(sh - h, sh * 3 / 8);
                return colorMips[mip].AsSpan(top * w * 4, w * h * 4).ToArray();
            }).ToArray();
            colorUvTransform = new(1, 2, 0, -.25f);
        }
        if (colorTile is not null)
        {
            // Bake and filter the complete authored field before retaining only
            // the mesh's static island. The crop keeps the original texel density.
            var tile = colorTile.Images(colorMips, colorWidth, colorHeight);
            colorContainer = new(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(template, tile.Width, tile.Height, true), templateName, colorName));
            colorMips = tile.Images;
            colorUvTransform = new(colorTile.DivisionsX, colorTile.DivisionsY,
                -colorTile.TileX - colorTile.RepeatX * colorTile.DivisionsX,
                -(colorTile.DivisionsY - 1 - colorTile.TileY - colorTile.RepeatY * colorTile.DivisionsY));
        }
        var color = colorContainer.EncodeMipImages(colorMips, cancellationToken, encodingCache);
        // Filter the complete generated object-normal field first. Each tile
        // retains that field's original mip footprint and texel density before
        // its first BC7 encoding; no authored compressed blocks are re-encoded.
        var normalMips = SwShTextureFiltering.Mips(normalPixels, width, height, normalContainer.MipCount, normals: true);
        if (normalTile is not null)
        {
            var tile = normalTile.Images(normalMips, width, height);
            normalContainer = new(ModelTextureIdentity.Rename(SwShTextureAllocation.Create(template, tile.Width, tile.Height, false), templateName, normalName));
            normalMips = tile.Images;
        }
        return new(colorName, color, normalName, normalContainer.EncodeMipImages(normalMips, cancellationToken, encodingCache),
            ambientName, ambientContainer.EncodeMipImages(SwShTextureFiltering.Mips(ambientPixels, ambientWidth, ambientHeight, ambientContainer.MipCount), cancellationToken, encodingCache), coverage, conflicts)
            { ColorUvTransform = colorUvTransform, NormalUvScale = normalScale };
    }

    private static (byte[] Pixels, double Coverage, int Conflicts) ObjectNormalPixels(PreviewPrimitive primitive, byte[] tangentNormals, int width, int height, CancellationToken cancellationToken)
    {
        var normals = new Vector3[width * height]; var covered = new bool[normals.Length]; var conflicts = 0;
        var vertices = primitive.Vertices;
        Vector3 Position(int i) => new(vertices[i * 16], vertices[i * 16 + 1], vertices[i * 16 + 2]);
        Vector3 Normal(int i) => new(vertices[i * 16 + 3], vertices[i * 16 + 4], vertices[i * 16 + 5]);
        Vector2 Uv(int i) => new(vertices[i * 16 + 6], vertices[i * 16 + 7]);
        for (var triangle = 0; triangle < primitive.Indices.Length; triangle += 3)
        {
            if ((triangle & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = checked((int)primitive.Indices[triangle]); var b = checked((int)primitive.Indices[triangle + 1]); var c = checked((int)primitive.Indices[triangle + 2]);
            var uvA = Uv(a); var uvB = Uv(b); var uvC = Uv(c);
            var wrap = MathF.Floor(MathF.Min(uvA.Y, MathF.Min(uvB.Y, uvC.Y))); uvA.Y -= wrap; uvB.Y -= wrap; uvC.Y -= wrap;
            var u = uvB - uvA; var v = uvC - uvA; var determinant = Cross(u, v);
            if (MathF.Abs(determinant) < 1e-12f) continue;
            var e1 = Position(b) - Position(a); var e2 = Position(c) - Position(a);
            var tangent = (e1 * v.Y - e2 * u.Y) / determinant; var bitangent = (e2 * u.X - e1 * v.X) / determinant;
            Vector2 ImagePoint(Vector2 uv) => new(uv.X * width, (1 - uv.Y) * height);
            var p = ImagePoint(uvA); var q = ImagePoint(uvB); var r = ImagePoint(uvC); var ab = q - p; var ac = r - p;
            var area = Cross(ab, ac);
            var left = Math.Max(0, (int)MathF.Floor(MathF.Min(p.X, MathF.Min(q.X, r.X))));
            var right = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(p.X, MathF.Max(q.X, r.X))));
            var top = Math.Max(0, (int)MathF.Floor(MathF.Min(p.Y, MathF.Min(q.Y, r.Y))));
            var bottom = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(p.Y, MathF.Max(q.Y, r.Y))));
            for (var y = top; y <= bottom; y++) for (var x = left; x <= right; x++)
            {
                var point = new Vector2(x + .5f, y + .5f) - p; var beta = Cross(point, ac) / area; var gamma = Cross(ab, point) / area; var alpha = 1 - beta - gamma;
                if (MathF.Min(alpha, MathF.Min(beta, gamma)) < -.00001f) continue;
                var normal = Unit(Normal(a) * alpha + Normal(b) * beta + Normal(c) * gamma);
                var t = Unit(tangent - normal * Vector3.Dot(normal, tangent)); var bt = Unit(Vector3.Cross(normal, t));
                if (Vector3.Dot(bt, bitangent) < 0) bt = -bt;
                var index = y * width + x; var nx = tangentNormals[index * 4] / 127.5f - 1; var ny = tangentNormals[index * 4 + 1] / 127.5f - 1;
                var value = Unit(t * nx + bt * ny + normal * MathF.Sqrt(MathF.Max(0, 1 - nx * nx - ny * ny)));
                if (covered[index] && Vector3.Dot(normals[index], value) < .8f) conflicts++;
                normals[index] = value; covered[index] = true;
            }
        }
        var coverage = covered.Count(v => v);
        if (coverage == 0) throw new InvalidDataException("Material has no rasterizable UV coverage.");
        var nearest = Nearest(covered, width, height, cancellationToken);
        var normalPixels = new byte[width * height * 4];
        for (var i = 0; i < nearest.Length; i++)
        {
            var n = normals[nearest[i]];
            normalPixels[i * 4] = Channel((n.X + 1) / 2); normalPixels[i * 4 + 1] = Channel((n.Y + 1) / 2); normalPixels[i * 4 + 2] = Channel((n.Z + 1) / 2); normalPixels[i * 4 + 3] = 255;
        }
        return (normalPixels, coverage / (double)covered.Length, conflicts);
    }

    internal static Image Read(byte[] bytes)
    {
        var texture = PreviewTexture.ReadBaseLevel(bytes);
        var format = texture.Format switch
        {
            0x0b01 or 0x0b06 => CompressionFormat.Rgba, 0x1a01 or 0x1a06 => CompressionFormat.Bc1WithAlpha,
            0x1b01 or 0x1b06 => CompressionFormat.Bc2, 0x1c01 or 0x1c06 => CompressionFormat.Bc3,
            0x1d01 => CompressionFormat.Bc4, 0x1e01 => CompressionFormat.Bc5, 0x2001 or 0x2006 => CompressionFormat.Bc7,
            _ => throw new InvalidDataException("Source material texture is unsupported."),
        };
        if ((long)texture.Width * texture.Height > 4 * 1024 * 1024) throw new InvalidDataException("Source material image exceeds its budget.");
        var pixels = format == CompressionFormat.Rgba ? texture.Blocks
            : ModelTextureDocument.PixelBytes(new BcDecoder().DecodeRaw(texture.Blocks, texture.Width, texture.Height, format));
        return new(texture.Width, texture.Height, (texture.Format & 255) == 6, pixels);
    }
    internal static Vector4 Sample(Image image, Vector2 uv, Vector4 transform, Vector2 wrap)
    {
        static float Fract(float value) => value - MathF.Floor(value);
        static float Address(float value, float mode) => mode switch
        {
            0 => Fract(value), 1 => Math.Clamp(value, 0, 1),
            6 => 1 - MathF.Abs(Fract(value * .5f) * 2 - 1), 7 => Math.Clamp(MathF.Abs(value), 0, 1),
            _ => throw new InvalidDataException("Z-A texture addressing mode is unsupported."),
        };
        var x = Address(uv.X * transform.X + transform.Z, wrap.X) * image.Width - .5f;
        var y = (1 - Address((1 - uv.Y) * transform.Y + 1 - transform.Y - transform.W, wrap.Y)) * image.Height - .5f;
        var ix = (int)MathF.Floor(x); var iy = (int)MathF.Floor(y);
        Vector4 Pixel(int px, int py)
        {
            // Repeating bilinear samples also wrap the neighboring texel across
            // the seam. Folding only the UV would clamp half of that footprint.
            static int Index(int value, int size, float mode) => mode == 0
                ? (value % size + size) % size : Math.Clamp(value, 0, size - 1);
            px = Index(px, image.Width, wrap.X); py = Index(py, image.Height, wrap.Y);
            var at = (py * image.Width + px) * 4;
            float C(int c)
            {
                var v = image.Pixels[at + c] / 255f;
                return !image.Srgb || c == 3 ? v : v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
            }
            return new(C(0), C(1), C(2), C(3));
        }
        return Vector4.Lerp(Vector4.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), x - ix),
            Vector4.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), x - ix), y - iy);
    }
    private static byte[][] Mips(byte[] pixels, int width, int height, int count) => Enumerable.Range(0, count)
        .Select(m => Resize(pixels, width, height, Math.Max(1, width >> m), Math.Max(1, height >> m))).ToArray();
    private static byte[] Resize(byte[] source, int width, int height, int w, int h)
    {
        if (w == width && h == height) return source.ToArray();
        var output = new byte[w * h * 4];
        // A tent filter expands with the reduction ratio so small mip levels
        // include their source footprint instead of aliasing four texels.
        var sx = width / (double)w; var sy = height / (double)h; var rx = Math.Max(1, sx); var ry = Math.Max(1, sy);
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var cx = (x + .5) * sx - .5; var cy = (y + .5) * sy - .5;
            var sums = new double[4]; double weight = 0;
            for (var iy = Math.Max(0, (int)Math.Ceiling(cy - ry)); iy <= Math.Min(height - 1, (int)Math.Floor(cy + ry)); iy++)
            for (var ix = Math.Max(0, (int)Math.Ceiling(cx - rx)); ix <= Math.Min(width - 1, (int)Math.Floor(cx + rx)); ix++)
            {
                var k = Math.Max(0, 1 - Math.Abs(ix - cx) / rx) * Math.Max(0, 1 - Math.Abs(iy - cy) / ry); weight += k;
                for (var c = 0; c < 4; c++) sums[c] += source[(iy * width + ix) * 4 + c] * k;
            }
            for (var c = 0; c < 4; c++) output[(y * w + x) * 4 + c] = (byte)Math.Clamp((int)Math.Round(sums[c] / weight), 0, 255);
        }
        return output;
    }
    private static int[] Nearest(bool[] covered, int width, int height, CancellationToken token)
    {
        var nearestX = new int[covered.Length]; Array.Fill(nearestX, -1);
        for (var y = 0; y < height; y++)
        {
            var found = -1;
            for (var x = 0; x < width; x++) { if (covered[y * width + x]) found = x; nearestX[y * width + x] = found; }
            found = -1;
            for (var x = width - 1; x >= 0; x--)
            {
                if (covered[y * width + x]) found = x;
                var old = nearestX[y * width + x]; if (found >= 0 && (old < 0 || found - x < x - old)) nearestX[y * width + x] = found;
            }
        }
        var result = new int[covered.Length]; var envelope = new int[height]; var starts = new double[height];
        for (var x = 0; x < width; x++)
        {
            token.ThrowIfCancellationRequested(); var count = 0;
            double Cost(int y) { var dx = nearestX[y * width + x] - x; return (double)y * y + (double)dx * dx; }
            for (var y = 0; y < height; y++)
            {
                if (nearestX[y * width + x] < 0) continue;
                double start = double.NegativeInfinity;
                while (count > 0)
                {
                    var old = envelope[count - 1]; start = (Cost(y) - Cost(old)) / (2 * (y - old));
                    if (start > starts[count - 1]) break; count--;
                }
                envelope[count] = y; starts[count] = count == 0 ? double.NegativeInfinity : start; count++;
            }
            var active = 0;
            for (var y = 0; y < height; y++)
            {
                while (active + 1 < count && starts[active + 1] < y) active++;
                var row = envelope[active]; result[y * width + x] = row * width + nearestX[row * width + x];
            }
        }
        return result;
    }
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static Vector3 Unit(Vector3 v) => v.LengthSquared() > 1e-20f ? Vector3.Normalize(v) : throw new InvalidDataException("Material tangent frame is degenerate.");
    private static byte Channel(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
