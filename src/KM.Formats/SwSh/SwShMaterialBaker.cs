// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using KM.Formats.Models;
using KM.Formats.SV;

namespace KM.Formats.SwSh;

public sealed record SwShBakedMaterial(string ColorName, byte[] Color, string NormalName, byte[] Normal,
    string AmbientName, byte[] Ambient, double CoveredFraction, int ConflictingSamples)
{
    public Vector2 NormalUvScale { get; init; } = Vector2.One;
    public SwShNormalTileQualification? NormalTileQualification { get; init; }
    public Vector4 ColorUvTransform { get; init; } = new(1, 1, 0, 0);
}

public sealed record SwShNormalConflict(int PreviousTriangle, int Triangle, int X, int Y, float NormalAgreement);

/// <summary>Bakes source material channels into a native object normal and ambient shader contract.</summary>
public static class SwShMaterialBaker
{
    private sealed record Image(int Width, int Height, bool Srgb, byte[] Pixels);
    public static bool IsEyeMaterial(ModelBuffer source, int material) =>
        (source.Text(material, 0) ?? "").Contains("eye", StringComparison.Ordinal)
        || source.Tables(material, 1, 16).Select(shader => source.Text(shader, 0))
            .SequenceEqual(new[] { "EyeClearCoat", "Eye" });

    public static SwShBakedMaterial Bake(PreviewPrimitive primitive, byte[] sourceMaterials, int materialIndex,
        Func<string, byte[]> readTexture, CancellationToken cancellationToken = default, bool sourceNormalDimensions = false, bool opaqueEyeSurface = false,
        bool cornealEye = false, float cornealNormalHeight = 1, Vector2? normalUvScale = null, int normalUvVerticalTile = 0,
        bool authoredAlpha = false, Vector2? staticUvScale = null, ModelTextureMipCache? encodingCache = null,
        SwShStaticUvAtlas? staticUvAtlas = null, bool unlitSurface = false, bool opaqueBlendCornealEye = false,
        float cornealPrimaryNormalHeight = 1, bool cornealPrimarySurface = false, bool staticLitEyeSurface = false, bool preserveSourceColor = false,
        Action<SwShNormalConflict>? normalConflict = null,
        SwShStaticUvAtlas? colorUvAtlas = null, bool sourceTangentNormals = false, SvSwShNormalAtlas? normalAtlas = null)
    {
        if (materialIndex is < 0 or > 127) throw new InvalidDataException("Material texture identity is outside the supported range.");
        _ = SwShModelWriter.Bounds(primitive, 1);
        var source = new ModelBuffer(sourceMaterials);
        var tables = source.Tables(source.Root, 1, 128).Where(t => source.Text(t, 0) == primitive.Material.Name).ToArray();
        if (tables.Length != 1) throw new InvalidDataException("Source material identity is ambiguous.");
        var table = tables[0];
        if (sourceTangentNormals)
        {
            SvSwShArchaludonNormals.RequireMaterial(sourceMaterials, primitive.Material.Name);
            if (cornealEye || cornealPrimarySurface || unlitSurface || staticUvAtlas is not null || staticUvScale is not null
                || colorUvAtlas is not null || normalUvScale is { } transform && transform != Vector2.One || normalUvVerticalTile != 0)
                throw new InvalidDataException("Authored tangent normals require their untransformed native consumer.");
        }
        if (source.Tables(table, 1, 16).Any(shader => source.Text(shader, 0) == "InsideEmissionParallax"))
            throw new InvalidDataException("Source inside emission parallax needs its own native material adapter.");
        var textures = source.Tables(table, 2, 64).ToDictionary(t => source.Text(t, 0)!, t => source.Text(t, 1)!, StringComparer.Ordinal);
        var originalRead = readTexture;
        var minimumImages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        readTexture = name => minimumImages.TryGetValue(name, out var image) ? image
            : originalRead(name);
        void PromoteMinimum(string name) => minimumImages[name] = PromoteConstantImage(originalRead(name));
        var eye = IsEyeMaterial(source, table);
        if (staticLitEyeSurface)
        {
            if (!eye || cornealEye || cornealPrimarySurface || opaqueEyeSurface || opaqueBlendCornealEye || unlitSurface)
                throw new InvalidDataException("Static lit eye conversion cannot combine surface profiles.");
            KM.Formats.SV.SvSwShStaticLitEye.RequireMaterial(primitive, sourceMaterials);
            eye = false;
        }
        var atlas = staticUvAtlas ?? (staticUvScale is { } scale ? SwShStaticUvAtlas.For(primitive, scale) : null);
        if (colorUvAtlas is not null && (atlas is not null || cornealEye || cornealPrimarySurface || preserveSourceColor
            || colorUvAtlas != SvSwShMouseFaceAtlas.For(sourceMaterials, primitive)))
            throw new InvalidDataException("Independent color allocation requires its qualified mouse face surface.");
        if (cornealEye || cornealPrimarySurface)
        {
            var shaders = source.Tables(table, 1, 16).Select(shader => source.Text(shader, 0)).ToArray();
            var heights = source.Tables(table, 4, 256).Where(parameter => source.Text(parameter, 0) is "NormalHeight" or "NormalHeight1")
                .Select(parameter => (Name: source.Text(parameter, 0), Value: source.Field(parameter, 1) is var offset && offset != 0 ? source.Float(offset) : 0)).ToArray();
            if (!eye || opaqueEyeSurface || cornealEye && cornealPrimarySurface || primitive.Material.Alpha != "BlendPreMultiAlpha"
                    && !(opaqueBlendCornealEye && primitive.Material.Alpha == "Blend")
                || !shaders.SequenceEqual(new[] { "EyeClearCoat", "Eye" }) || !textures.ContainsKey("NormalMap1")
                || !float.IsFinite(cornealNormalHeight) || cornealNormalHeight is < 0 or > 1
                || !float.IsFinite(cornealPrimaryNormalHeight) || cornealPrimaryNormalHeight is < 0 or > 2
                || !textures.ContainsKey("NormalMap")
                || heights.Length != 2 || heights.Select(height => height.Name).Distinct().Count() != 2
                || heights.Any(height => height.Value != (height.Name == "NormalHeight1" ? cornealNormalHeight : cornealPrimaryNormalHeight)))
                throw new InvalidDataException("Corneal eye conversion requires the qualified paired normal surface.");
            if (primitive.Material.Alpha == "Blend")
            {
                // Straight and premultiplied blending agree only at full opacity.
                // Qualify every authored mip, including palette-specific images.
                var baseImage = new ModelTextureDocument(readTexture(textures["BaseColorMap"]));
                for (var mip = 0; mip < baseImage.MipCount; mip++)
                {
                    var pixels = baseImage.Pixels(mip);
                    for (var at = 3; at < pixels.Length; at += 4)
                        if (pixels[at] != 255) throw new InvalidDataException("Straight-alpha corneal eyes require fully opaque source mips.");
                }
                var colors = source.Tables(table, 7, 128).Where(parameter => source.Text(parameter, 0) is
                    "BaseColor" or "BaseColorLayer1" or "BaseColorLayer2" or "BaseColorLayer3" or "BaseColorLayer4").ToArray();
                if (colors.Length is < 4 or > 5 || colors.Select(parameter => source.Text(parameter, 0)).Distinct().Count() != colors.Length
                    || Enumerable.Range(1, 4).Any(layer => !colors.Any(parameter => source.Text(parameter, 0) == "BaseColorLayer" + layer))
                    || colors.Any(parameter => source.Field(parameter, 1) is var at && (at == 0 || source.Float(at + 12) != 1)))
                    throw new InvalidDataException("Straight-alpha corneal eyes require opaque authored color layers.");
            }
        }
        else if (cornealNormalHeight != 1 || cornealPrimaryNormalHeight != 1) throw new InvalidDataException("Corneal normal strength requires an eye conversion profile.");
        if (opaqueBlendCornealEye && !cornealEye && !cornealPrimarySurface)
            throw new InvalidDataException("Straight-alpha corneal eyes require a paired normal profile.");
        var templatePath = textures[eye ? "LayerMaskMap" : "BaseColorMap"];
        var template = readTexture(templatePath);
        var sampledScalarAlbedo = false;
        // Some authored opaque albedos store one BC4 channel and explicitly
        // sample it as (R,R,R,1). Resolve that source mapping through the normal
        // image reader, then use the existing native color allocation/encoder.
        // The editing document itself continues to reject non-color payloads.
        if (!eye && primitive.Material.Alpha == "Opaque" && templatePath == textures["BaseColorMap"])
        {
            var metadata = new ModelBuffer(template);
            int Pointer(int at) => checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(metadata.Slice(at, 8)));
            var info = Pointer(Pointer(40));
            if (metadata.U32(info + 28) == 0x1d01 && metadata.U32(info + 88) == 0x01020202)
            {
                sampledScalarAlbedo = true;
                var sampled = PreviewTexture.Read(template);
                if (sampled.Width < 4 || sampled.Height < 4)
                {
                    PromoteMinimum(templatePath);
                    template = readTexture(templatePath);
                }
                else template = SwShTextureAllocation.Create(template, sampled.Width, sampled.Height, srgb: false);
            }
        }
        var container = new ModelTextureDocument(template);
        var colorWidth = container.Width; var colorHeight = container.Height;
        // Qualified source-color shaders sample the original mask separately.
        // Keep the albedo's own allocation; enlarging it to the mask dimensions
        // would regenerate the source alpha and its authored compressed mips.
        foreach (var role in preserveSourceColor ? new[] { "BaseColorMap" } : new[] { "BaseColorMap", "LayerMaskMap" })
            if (textures.TryGetValue(role, out var path))
            {
                var image = PreviewTexture.ReadBaseLevel(readTexture(path));
                colorWidth = Math.Max(colorWidth, image.Width); colorHeight = Math.Max(colorHeight, image.Height);
            }
        if (colorWidth < 4 || colorHeight < 4)
        {
            foreach (var role in new[] { "BaseColorMap", "LayerMaskMap" })
                if (textures.TryGetValue(role, out var path)) PromoteMinimum(path);
            template = readTexture(templatePath); container = new ModelTextureDocument(template);
            colorWidth = container.Width; colorHeight = container.Height;
        }
        template = SwShTextureAllocation.Create(template, colorWidth, colorHeight, container.Srgb);
        container = new ModelTextureDocument(template);
        var normalRole = cornealEye ? "NormalMap1" : "NormalMap";
        var geometricNormalSurface = SvSwShGeometricNormalMaterial.IsQualified(primitive, source, table);
        var normalSource = textures.TryGetValue(normalRole, out var normalPath) ? Read(readTexture(normalPath))
            : geometricNormalSurface ? null
            : unlitSurface ? new Image(4, 4, false, Enumerable.Range(0, 16).SelectMany(_ => new byte[] { 128, 128, 255, 255 }).ToArray())
            : throw new InvalidDataException("A lit material requires its authored normal map.");
        if (normalSource is not null && (normalSource.Width < 4 || normalSource.Height < 4))
        {
            PromoteMinimum(normalPath!); normalSource = Read(readTexture(normalPath!));
        }
        var width = normalSource?.Width ?? colorWidth;
        var height = normalSource?.Height ?? colorHeight;
        var tangentNormals = normalSource is null ? null : Resize(normalSource.Pixels, normalSource.Width, normalSource.Height, width, height);
        var normals = new Vector3[width * height]; var covered = new bool[normals.Length]; var conflicts = 0;
        var owners = normalConflict is null ? null : new int[normals.Length];
        if (normalAtlas is not null && (sourceTangentNormals || geometricNormalSurface || cornealEye || cornealPrimarySurface
            || unlitSurface || atlas is not null || colorUvAtlas is not null || normalUvScale is { } sharedTransform && sharedTransform != Vector2.One
            || normalUvVerticalTile != 0))
            throw new InvalidDataException("Shared object-normal fields require their qualified untransformed body surface.");
        var normalGeometry = normalAtlas?.Geometry(primitive, sourceMaterials) ?? primitive;
        var vertices = normalGeometry.Vertices;
        Vector3 Position(int i) => new(vertices[i * 16], vertices[i * 16 + 1], vertices[i * 16 + 2]);
        Vector3 Normal(int i) => new(vertices[i * 16 + 3], vertices[i * 16 + 4], vertices[i * 16 + 5]);
        Vector2 Uv(int i) => new(vertices[i * 16 + 6], vertices[i * 16 + 7]);
        for (var triangle = 0; !sourceTangentNormals && triangle < normalGeometry.Indices.Length; triangle += 3)
        {
            if ((triangle & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = checked((int)normalGeometry.Indices[triangle]); var b = checked((int)normalGeometry.Indices[triangle + 1]); var c = checked((int)normalGeometry.Indices[triangle + 2]);
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
                var index = y * width + x; var nx = tangentNormals is null ? 0 : tangentNormals[index * 4] / 127.5f - 1;
                var ny = tangentNormals is null ? 0 : tangentNormals[index * 4 + 1] / 127.5f - 1;
                var normalHeight = cornealEye ? cornealNormalHeight : cornealPrimarySurface ? cornealPrimaryNormalHeight : 1;
                nx *= normalHeight; ny *= normalHeight;
                var value = geometricNormalSurface ? normal : Unit(t * nx + bt * ny + normal * MathF.Sqrt(MathF.Max(0, 1 - nx * nx - ny * ny)));
                if (covered[index] && Vector3.Dot(normals[index], value) < .8f)
                {
                    conflicts++;
                    normalConflict?.Invoke(new(owners![index], triangle / 3, x, y, Vector3.Dot(normals[index], value)));
                }
                normals[index] = value; covered[index] = true;
                if (owners is not null) owners[index] = triangle / 3;
            }
        }
        var coverage = covered.Count(v => v);
        if (coverage == 0 && !sourceTangentNormals) throw new InvalidDataException("Material has no rasterizable UV coverage.");
        var nearest = sourceTangentNormals ? [] : Nearest(covered, width, height, cancellationToken);
        var normalPixels = new byte[width * height * 4];
        var ambientTemplateName = Path.GetFileNameWithoutExtension(templatePath);
        var ambientWidth = 4; var ambientHeight = 4;
        byte[]? occlusion = null;
        if (textures.TryGetValue("AOMap", out var aoPath))
        {
            var ao = Read(readTexture(aoPath));
            if (ao.Width < 4 || ao.Height < 4)
            {
                PromoteMinimum(aoPath); ao = Read(readTexture(aoPath));
            }
            ambientWidth = ao.Width; ambientHeight = ao.Height;
            occlusion = Resize(ao.Pixels, ao.Width, ao.Height, ambientWidth, ambientHeight);
        }
        var ambientPixels = new byte[ambientWidth * ambientHeight * 4];
        for (var i = 0; i < nearest.Length; i++)
        {
            var n = normals[nearest[i]];
            normalPixels[i * 4] = Channel((n.X + 1) / 2); normalPixels[i * 4 + 1] = Channel((n.Y + 1) / 2); normalPixels[i * 4 + 2] = Channel((n.Z + 1) / 2); normalPixels[i * 4 + 3] = 255;
        }
        var normalScale = normalUvScale ?? Vector2.One;
        if (normalUvVerticalTile is not (0 or 1) || normalUvVerticalTile != 0 && normalScale == Vector2.One)
            throw new InvalidDataException("Normal texture tile requires a declared static crop.");
        if (normalScale != Vector2.One)
        {
            if (normalScale.Y == 4) SvSwShMouseNormalTile.Require(sourceMaterials, primitive, normalScale);
            // A native repeating normal sampler can address one exact UV tile
            // without changing geometry, pixel density, or the other material maps.
            if (atlas is not null || cornealEye || normalScale.X is not (1 or 2) || normalScale.Y is not (1 or 2 or 4))
                throw new InvalidDataException("Normal texture cropping requires a supported static tile.");
            var uvs = primitive.Indices.Distinct().Select(index => Uv(checked((int)index)) - new Vector2(0, normalUvVerticalTile)).ToArray();
            var tileX = (int)MathF.Floor(uvs.Min(uv => uv.X) * normalScale.X);
            var tileY = (int)MathF.Floor(uvs.Min(uv => 1 - uv.Y) * normalScale.Y);
            if (tileX < 0 || tileY < 0 || tileX >= normalScale.X || tileY >= normalScale.Y
                || uvs.Any(uv => uv.X * normalScale.X <= tileX || uv.X * normalScale.X >= tileX + 1
                    || (1 - uv.Y) * normalScale.Y <= tileY || (1 - uv.Y) * normalScale.Y >= tileY + 1))
                throw new InvalidDataException("Normal texture crop would remove authored UV coverage.");
            var croppedWidth = width / (int)normalScale.X; var croppedHeight = height / (int)normalScale.Y;
            var cropped = new byte[croppedWidth * croppedHeight * 4];
            for (var row = 0; row < croppedHeight; row++)
                normalPixels.AsSpan(((tileY * croppedHeight + row) * width + tileX * croppedWidth) * 4, croppedWidth * 4)
                    .CopyTo(cropped.AsSpan(row * croppedWidth * 4));
            normalPixels = cropped; width = croppedWidth; height = croppedHeight;
        }
        for (var i = 0; i < ambientWidth * ambientHeight; i++)
        {
            ambientPixels[i * 4] = occlusion is null ? (byte)0 : (byte)(255 - occlusion[i * 4]);
            ambientPixels[i * 4 + 2] = eye ? (byte)166 : (byte)128; ambientPixels[i * 4 + 3] = 255;
        }
        var colorName = Path.GetFileNameWithoutExtension(templatePath);
        // These qualified albedos can reference authored _ao/_msk images.
        // Retain their real container identity; the native material binding
        // supplies the color role independently of the donor's filename.
        if (!sampledScalarAlbedo && !colorName.EndsWith(eye ? "_lym" : "_alb", StringComparison.Ordinal)) throw new InvalidDataException("Material texture naming needs a definition adapter.");
        var normalName = ModelTextureIdentity.MaterialName(colorName, materialIndex, 'n')[..^3] + "nor";
        var ambientName = ModelTextureIdentity.MaterialName(ambientTemplateName, materialIndex, 'a')[..^3] + "amb";
        // Retain the projected authored geometry normals in RGBA8 for this source surface.
        var normalTemplate = SwShTextureAllocation.Create(template, width, height, false, compressed: !geometricNormalSurface);
        var ambientTemplate = SwShTextureAllocation.Create(template, ambientWidth, ambientHeight, false);
        var normalImage = ModelTextureIdentity.Rename(normalTemplate, colorName, normalName, linear: true);
        var ambientImage = ModelTextureIdentity.Rename(ambientTemplate, ambientTemplateName, ambientName, linear: true);
        var normalContainer = new ModelTextureDocument(normalImage);
        var ambientContainer = new ModelTextureDocument(ambientImage);
        var colorPixels = Read(readTexture(templatePath)).Pixels;
        if (eye || textures.ContainsKey("LayerMaskMap"))
        {
            var baseImage = Read(readTexture(textures["BaseColorMap"])); var basePixels = Resize(baseImage.Pixels, baseImage.Width, baseImage.Height, colorWidth, colorHeight);
            var layerImage = Read(readTexture(textures["LayerMaskMap"]));
            var mask = Resize(layerImage.Pixels, layerImage.Width, layerImage.Height, colorWidth, colorHeight);
            var colors = source.Tables(table, 7, 128).ToDictionary(t => source.Text(t, 0)!, t =>
            { var at = source.Field(t, 1); return new Vector3(source.Float(at), source.Float(at + 4), source.Float(at + 8)); }, StringComparer.Ordinal);
            var scalars = source.Tables(table, 4, 128).ToDictionary(t => source.Text(t, 0)!, t => source.Field(t, 1) is var at && at != 0 ? source.Float(at) : 0, StringComparer.Ordinal);
            var srgb = (PreviewTexture.ReadBaseLevel(template).Format & 255) == 6;
            var pixels = new byte[mask.Length];
            float Linear(byte value) { var f = value / 255f; return !baseImage.Srgb ? f : f <= .04045f ? f / 12.92f : MathF.Pow((f + .055f) / 1.055f, 2.4f); }
            byte Encoded(float value) => Channel(!srgb ? value : value <= .0031308f ? 12.92f * value : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f);
            for (var i = 0; i < colorWidth * colorHeight; i++)
            {
                var original = new Vector3(Linear(basePixels[i * 4]), Linear(basePixels[i * 4 + 1]), Linear(basePixels[i * 4 + 2]));
                if (authoredAlpha) original *= colors.GetValueOrDefault("BaseColor", Vector3.One);
                var rgb = original;
                for (var layer = 0; layer < 4; layer++)
                {
                    if (!colors.TryGetValue("BaseColorLayer" + (layer + 1), out var tint)) throw new InvalidDataException("Material color layer is missing.");
                    rgb = Vector3.Lerp(rgb, original * tint, Math.Clamp(mask[i * 4 + layer] / 255f * scalars.GetValueOrDefault("LayerMaskScale" + (layer + 1), 1), 0, 1));
                }
                pixels[i * 4] = Encoded(rgb.X); pixels[i * 4 + 1] = Encoded(rgb.Y); pixels[i * 4 + 2] = Encoded(rgb.Z); pixels[i * 4 + 3] = basePixels[i * 4 + 3];
            }
            colorPixels = pixels;
        }
        if (opaqueEyeSurface && (!eye || primitive.Material.Alpha != "BlendPreMultiAlpha"
            || Enumerable.Range(0, colorPixels.Length / 4).Any(index => colorPixels[index * 4 + 3] != 0)))
            throw new InvalidDataException("Opaque eye conversion requires the qualified zero alpha premultiplied surface.");
        if (primitive.Material.Alpha == "Opaque" || opaqueEyeSurface)
            for (var index = 3; index < colorPixels.Length; index += 4) colorPixels[index] = 255;
        var mipImages = SwShTextureFiltering.Mips(colorPixels, colorWidth, colorHeight, container.MipCount, container.Srgb, sharpen: true);
        byte[]? alphaReference = null;
        if (authoredAlpha && primitive.Material.Alpha != "Opaque")
        {
            var baseTexture = readTexture(textures["BaseColorMap"]); var authored = new ModelTextureDocument(baseTexture);
            if (authored.Width != colorWidth || authored.Height != colorHeight)
                throw new InvalidDataException("Authored transparency requires matching source image dimensions.");
            // Source containers can stop their mip chain before 1 x 1. Keep
            // that authored chain so its alpha blocks remain valid references.
            if (authored.MipCount != container.MipCount)
                mipImages = SwShTextureFiltering.Mips(colorPixels, colorWidth, colorHeight, authored.MipCount, authored.Srgb, sharpen: true);
            var baseName = Path.GetFileNameWithoutExtension(textures["BaseColorMap"]);
            // Rare base images can have a longer identity than their shared mask.
            // Keep that original container name; the preparation pool assigns its
            // final per palette identity after encoding.
            colorName = baseName;
            container = authored;
            alphaReference = baseTexture;
            for (var mip = 0; mip < mipImages.Length; mip++)
            {
                var original = authored.Pixels(mip);
                for (var at = 3; at < original.Length; at += 4) mipImages[mip][at] = original[at];
            }
        }
        var normalMips = sourceTangentNormals ? []
            : SwShTextureFiltering.Mips(normalPixels, width, height, normalContainer.MipCount, normals: true);
        var ambientMips = SwShTextureFiltering.Mips(ambientPixels, ambientWidth, ambientHeight, ambientContainer.MipCount);
        if (atlas is not null)
        {
            var colorCrop = atlas.Images(mipImages, colorWidth, colorHeight);
            var basis = alphaReference ?? template;
            // A centered crop can straddle BC7 blocks in its smallest mips.
            // RGBA retains the decoded authored alpha exactly in that case;
            // the cropped image remains smaller than the original atlas.
            var blockAlignedOrigin = atlas.TileX % 1 == 0 && atlas.TileY % 1 == 0;
            var colorImage = SwShTextureAllocation.Create(basis, colorCrop.Width, colorCrop.Height, container.Srgb,
                compressed: alphaReference is null || blockAlignedOrigin);
            if (alphaReference is not null && blockAlignedOrigin) colorImage = atlas.Reference(alphaReference, colorImage);
            container = new(colorImage); mipImages = colorCrop.Images;
            var normalCrop = atlas.Images(normalMips, width, height);
            normalContainer = new(SwShTextureAllocation.Create(normalImage, normalCrop.Width, normalCrop.Height, false)); normalMips = normalCrop.Images;
            var ambientCrop = atlas.Images(ambientMips, ambientWidth, ambientHeight);
            ambientContainer = new(SwShTextureAllocation.Create(ambientImage, ambientCrop.Width, ambientCrop.Height, false)); ambientMips = ambientCrop.Images;
        }
        // These authored translucent surfaces cannot retain smooth alpha after
        // recoloring into BC7's shared color and alpha index layout.
        if (primitive.Name == "pm0483_12_00_gem_mesh_shape" && primitive.Material.Name == "body_b_01"
            || primitive.Name == "pm0484_12_00_cover_mesh_shape" && primitive.Material.Name == "body_a_02")
        {
            var size = primitive.Name.StartsWith("pm0483_", StringComparison.Ordinal) ? 512 : 1024;
            if (primitive.Material.Alpha != "BlendPreMultiAlpha" || colorWidth != size || colorHeight != size
                || eye || atlas is not null || preserveSourceColor)
                throw new InvalidDataException("Origin translucent surface allocation changed.");
            container = new(SwShTextureAllocation.Create(template, colorWidth, colorHeight, container.Srgb,
                compressed: false, mipCount: container.MipCount));
        }
        byte[] color;
        byte[]? croppedColor = null; byte[]? croppedAmbient = null;
        if (colorUvAtlas is not null)
        {
            var cropped = colorUvAtlas.Images(mipImages, colorWidth, colorHeight);
            // Keep the full-density bake's original BC7 blocks, including its
            // authored mip colors at the smallest cropped dimensions.
            croppedColor = colorUvAtlas.Reference(container.EncodeMipImages(mipImages, cancellationToken, encodingCache),
                SwShTextureAllocation.Create(template, cropped.Width, cropped.Height, container.Srgb));
            // The qualified native body program selects color coordinates for
            // AmbientTex too (SwitchAmbientTexUV is false). Keep its complete
            // authored face states in the same cropped coordinate domain.
            var ambientCrop = colorUvAtlas.Images(ambientMips, ambientWidth, ambientHeight);
            croppedAmbient = colorUvAtlas.Reference(ambientContainer.EncodeMipImages(ambientMips, cancellationToken, encodingCache),
                SwShTextureAllocation.Create(ambientImage, ambientCrop.Width, ambientCrop.Height, false));
        }
        if (preserveSourceColor)
        {
            if (eye || atlas is not null || unlitSurface || (authoredAlpha || primitive.Material.Alpha != "Opaque"))
                throw new InvalidDataException("Source albedo requires a qualified untransformed layered surface.");
            var original = originalRead(textures["BaseColorMap"]);
            // The qualified scalar mapping above is sampled as (R,R,R,1).
            // Retain that native BC4 container, including its channel mapping
            // and authored mip chain; it does not need a recoloring document.
            var scalar = sampledScalarAlbedo ? PreviewTexture.Read(original) : null;
            var document = sampledScalarAlbedo ? null : new ModelTextureDocument(original);
            // The allocation above already enforces every native dimension bound.
            // Retain the original compressed blocks and every authored mip only
            // when that exact allocation and color encoding agree.
            if ((scalar?.Width ?? document!.Width) != colorWidth || (scalar?.Height ?? document!.Height) != colorHeight
                || (scalar is not null ? (scalar.Format & 255) == 6 : document!.Srgb) != container.Srgb)
                throw new InvalidDataException("Source albedo allocation differs from its qualified native surface.");
            var originalName = Path.GetFileNameWithoutExtension(textures["BaseColorMap"]);
            colorName = ModelTextureIdentity.MaterialName(originalName, materialIndex, 'c');
            color = ModelTextureIdentity.Rename(original, originalName, colorName);
        }
        else
        {
            try { color = croppedColor ?? container.EncodeMipImages(mipImages, cancellationToken, encodingCache); }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"Color conversion failed for material {primitive.Material.Name} on {primitive.Name}: {error.Message}", error);
            }
        }
        byte[] normalBytes;
        if (sourceTangentNormals)
        {
            // Both palettes share the original normal image. Its encoded name
            // length can differ from the rare albedo's identity.
            var sourceNormalName = "pm1132_00_00_" + primitive.Material.Name + "_nrm";
            normalName = ModelTextureIdentity.MaterialName(sourceNormalName, materialIndex, 'n')[..^3] + "nor";
            normalBytes = ModelTextureIdentity.Rename(SvSwShArchaludonNormals.Texture(sourceMaterials, primitive.Material.Name, originalRead),
                sourceNormalName, normalName, linear: true);
        }
        else normalBytes = normalContainer.EncodeMipImages(normalMips, cancellationToken, encodingCache);
        return new(colorName, color, normalName, normalBytes,
            ambientName, croppedAmbient ?? ambientContainer.EncodeMipImages(ambientMips, cancellationToken, encodingCache), coverage / (double)covered.Length, conflicts)
            { NormalUvScale = normalScale, NormalTileQualification = normalScale.Y == 4
                ? new SwShNormalTileQualification(primitive.Name, primitive.Material.Name) : null,
                ColorUvTransform = colorUvAtlas is null ? new(1, 1, 0, 0)
                    : new(1, colorUvAtlas.DivisionsY, 0, 1 - colorUvAtlas.DivisionsY) };
    }

    public static byte[] Icon(byte[] sourceTexture, byte[] targetTemplate, CancellationToken cancellationToken = default,
        ModelTextureMipCache? encodingCache = null)
    {
        var source = Read(sourceTexture); var target = new ModelTextureDocument(targetTemplate);
        if (source.Width > target.Width || source.Height > target.Height) throw new InvalidDataException("Source icon does not fit the target icon frame.");
        var pixels = new byte[target.Width * target.Height * 4]; var x = (target.Width - source.Width) / 2; var y = (target.Height - source.Height) / 2;
        for (var row = 0; row < source.Height; row++) source.Pixels.AsSpan(row * source.Width * 4, source.Width * 4).CopyTo(pixels.AsSpan(((row + y) * target.Width + x) * 4));
        return target.EncodeMipImages(Mips(pixels, target.Width, target.Height, target.MipCount), cancellationToken, encodingCache);
    }
    internal static byte[] PromoteConstantImage(byte[] bytes)
    {
        var texture = PreviewTexture.ReadBaseLevel(bytes);
        if (texture.Width >= 4 && texture.Height >= 4) return bytes;
        // Native allocations start at 4x4. A constant 2x2 placeholder has the
        // same samples at every UV and LOD after expansion; other small images
        // still need a separate adapter. Check every authored mip, including tails.
        if (texture.Width != 2 || texture.Height != 2)
            throw new InvalidDataException("Small source texture requires a constant 2x2 adapter.");
        var metadata = new ModelBuffer(bytes);
        int Pointer(int at) => checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(metadata.Slice(at, 8)));
        var mipCount = metadata.U16(Pointer(Pointer(40)) + 22);
        if (mipCount is < 1 or > 32) throw new InvalidDataException("Small source texture mip count is unsupported.");
        var first = Read(PreviewTexture.ReadLevel(bytes, 0)).Pixels.AsSpan(0, 4).ToArray();
        for (var mip = 0; mip < mipCount; mip++)
        {
            var pixels = Read(PreviewTexture.ReadLevel(bytes, mip)).Pixels;
            for (var at = 0; at < pixels.Length; at += 4)
                if (!pixels.AsSpan(at, 4).SequenceEqual(first))
                    throw new InvalidDataException("Small source texture is not constant across its authored mips.");
        }
        var allocated = new ModelTextureDocument(SwShTextureAllocation.Create(bytes, 4, 4, (texture.Format & 255) == 6, compressed: false));
        return allocated.EncodeMipImages(Enumerable.Range(0, allocated.MipCount)
            .Select(mip => Enumerable.Range(0, Math.Max(1, 4 >> mip) * Math.Max(1, 4 >> mip))
                .SelectMany(_ => first).ToArray()).ToArray());
    }

    private static Image Read(byte[] bytes) => Read(PreviewTexture.ReadBaseLevel(bytes));

    private static Image Read(PreviewTexture texture)
    {
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
