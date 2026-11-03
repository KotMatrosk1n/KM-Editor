// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private static Vector2[] ColorTileScales(PreviewScene scene, IReadOnlyList<ColorUvTile>? tiles)
        {
            var result = Enumerable.Repeat(Vector2.One, scene.Primitives.Count).ToArray();
            foreach (var tile in tiles ?? [])
            {
                if (tile.Scale == Vector2.One)
                    throw new InvalidDataException("Z-A color tile requires a nonidentity scale.");
                var matches = scene.Primitives.Select((primitive, index) => (primitive, index))
                    .Where(pair => pair.primitive.Material.Name == tile.Material && pair.primitive.Name == tile.Mesh).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("Z-A color tile section is missing or ambiguous.");
                var (primitive, index) = matches[0];
                if (result[index] != Vector2.One) throw new InvalidDataException("Z-A color tile sections overlap.");
                _ = SwShStaticUvAtlas.CenteredRepeat(primitive, tile.Scale);
                result[index] = tile.Scale;
            }
            return result;
        }

        private static void RequireStaticColorTiles(PreviewClip clip, IReadOnlyList<ColorUvTile>? tiles)
        {
            if (clip.Materials.Any(track => tiles?.Any(tile => tile.Material == track.Material) == true))
                throw new InvalidDataException("Animated Z-A color tile materials require another adapter.");
        }

        private static Vector2[] NormalTileScales(PreviewScene scene, IReadOnlyList<NormalUvTile>? tiles)
        {
            var result = Enumerable.Repeat(Vector2.One, scene.Primitives.Count).ToArray();
            foreach (var tile in tiles ?? [])
            {
                if (tile.Scale == Vector2.One || tile.Scale.X is not (1 or 2) || tile.Scale.Y is not (1 or 2))
                    throw new InvalidDataException("Z-A normal tile requires a supported nonidentity scale.");
                var matches = scene.Primitives.Select((primitive, index) => (primitive, index))
                    .Where(pair => pair.primitive.Material.Name == tile.Material
                        && (tile.Mesh is null || pair.primitive.Name == tile.Mesh)).ToArray();
                if (matches.Length == 0) throw new InvalidDataException("Z-A normal tile section is missing.");
                foreach (var (primitive, index) in matches)
                {
                    if (result[index] != Vector2.One)
                        throw new InvalidDataException("Z-A normal tile sections overlap.");
                    _ = SwShStaticUvAtlas.For(primitive, tile.Scale);
                    result[index] = tile.Scale;
                }
            }
            return result;
        }

        private static SwShBakedMaterial NormalTile(PreviewPrimitive primitive, SwShBakedMaterial material, Vector2 scale)
        {
            if (scale == Vector2.One) return material;
            if (scale.X is not (1 or 2) || scale.Y is not (1 or 2) || material.NormalUvScale != Vector2.One)
                throw new InvalidDataException("Z-A normal tile requires an unscaled supported allocation.");
            var tile = SwShStaticUvAtlas.For(primitive, scale);
            var source = new ModelTextureDocument(material.Normal);
            if (source.Width / scale.X < 4 || source.Height / scale.Y < 4)
                throw new InvalidDataException("Z-A normal tile is smaller than a compressed block.");
            // Crop the already encoded field, including its existing mip levels.
            // The native repeating sampler subtracts the integer tile origin.
            var target = SwShTextureAllocation.Create(material.Normal, source.Width / (int)scale.X,
                source.Height / (int)scale.Y, false);
            return material with { Normal = tile.Reference(material.Normal, target), NormalUvScale = scale };
        }

        private static void RequireStaticNormalTiles(PreviewClip clip, IReadOnlyList<NormalUvTile>? tiles)
        {
            // Layer palette and emission values do not move normal coordinates.
            // UVs and every other animated material parameter still require an adapter.
            static bool NormalIndependent(PreviewMaterialTrack track) => Enumerable.Range(1, 4).Any(layer =>
                track.Parameter == "BaseColorLayer" + layer || track.Parameter == "EmissionColorLayer" + layer
                || track.Parameter == "EmissionIntensityLayer" + layer);
            if (clip.Materials.Any(track => tiles?.Any(tile => tile.Material == track.Material) == true
                && !NormalIndependent(track)))
                throw new InvalidDataException("Animated Z-A normal tile materials require another adapter.");
        }
    }
}
