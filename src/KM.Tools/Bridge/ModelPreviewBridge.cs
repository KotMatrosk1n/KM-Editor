// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Models;
using KM.Core.Projects;
using KM.Formats.Models;
using KM.SV.Models;
using KM.SwSh.Models;
using KM.ZA.Models;
namespace KM.Tools.Bridge;

internal static class ModelPreviewBridge
{
    internal static object Properties(ModelTexturesRequest request)
    {
        var project = Open(request.Paths);
        var assets = Assets(project, request.Id);
        return new {
            Assets = assets.Select(a => new { a.Id, Size = a.Bytes.Length, SourceHash = ModelTextureIntent.Hash(a.Bytes), a.Archive }),
            Materials = assets.Where(a => Path.GetExtension(a.Id) is ".trmtr" or ".gfbmdl")
                .Select(a => new { a.Id, SourceHash = ModelTextureIntent.Hash(a.Bytes), Fields = new ModelMaterialDocument(a.Bytes, a.Id.EndsWith(".gfbmdl", StringComparison.Ordinal)).Fields })
        };
    }

    internal static object StageAsset(ModelAssetStageRequest request)
    {
        var project = Open(request.Paths);
        var session = request.Session is null ? null : EditSessionBridgeMapper.ToCore(request.Session);
        ModelTextureIntent? intent = request.RestoreVanilla ? null : AssetIntent(project, request.Id,
            request.Change ?? throw new InvalidDataException("Model change is missing."));
        var updated = project.Paths.SelectedGame switch {
            ProjectGame.Sword or ProjectGame.Shield => request.RestoreVanilla
                ? new SwShModelTextureEditSessionService().RestoreVanilla(project.Paths, session, request.Id)
                : new SwShModelTextureEditSessionService().Stage(project.Paths, session, intent!),
            ProjectGame.ZA => request.RestoreVanilla
                ? new ZaModelTextureEditSessionService().RestoreVanilla(project.Paths, session, request.Id)
                : new ZaModelTextureEditSessionService().Stage(project.Paths, session, intent!),
            _ => request.RestoreVanilla ? new SvModelTextureEditSessionService().RestoreVanilla(project.Paths, session, request.Id)
                : new SvModelTextureEditSessionService().Stage(project.Paths, session, intent!)
        };
        return new { Session = EditSessionBridgeMapper.ToDto(updated) };
    }

    private static ModelTextureResource[] Assets(OpenedProject project, string id, bool vanilla = false) => project.Paths.SelectedGame switch {
        ProjectGame.Sword or ProjectGame.Shield => new SwShModelPreviewService().Assets(project, id, vanilla),
        ProjectGame.ZA => new ZaModelPreviewService().Assets(project, id, vanilla),
        _ => new SvModelPreviewService().Assets(project, id, vanilla)
    };
    private static ModelTextureIntent AssetIntent(OpenedProject project, string id, ModelAssetChangeDto change)
    {
        if (change.Changes is null || change.Changes.Length > 512 || change.Changes.Any(c => c is null)) throw new InvalidDataException("Model properties are invalid.");
        var intent = new ModelTextureIntent(project.Paths.SelectedGame.ToString()!, id, change.Asset, change.SourceHash, [],
            Kind: change.Restore ? "restore" : "material", MaterialChanges: change.Changes.Select(c => new ModelMaterialChange(c.Key, c.Values, c.Text)).ToArray());
        intent.Validate(); return intent;
    }

    internal static object Textures(ModelTexturesRequest request)
    {
        var project = Open(request.Paths);
        var resources = Resources(project, request.Id);
        if (request.Texture is not null)
        {
            if (request.Texture.Length > 1024 || request.Changes is { Length: > 32 } || request.Changes?.Any(c => c is null) == true)
                throw new InvalidDataException("Texture preview changes are invalid.");
            var resource = resources.SingleOrDefault(r => r.Id == request.Texture) ?? throw new InvalidDataException("Texture is not part of the model.");
            var hash = ModelTextureIntent.Hash(resource.Bytes);
            if (hash != request.SourceHash) throw new InvalidDataException("Texture source changed. Reload the model.");
            var texture = PreviewTexture.Read(resource.Bytes);
            byte[] pixels;
            try
            {
                var document = new ModelTextureDocument(resource.Bytes);
                pixels = document.Preview((request.Changes ?? []).Select(c => new ModelTextureColorChange(c.From, c.To, c.Tolerance)).ToArray());
            }
            catch (InvalidDataException) when (request.Changes is null or { Length: 0 })
            {
                pixels = texture.Format is 0x0b01 or 0x0b06 ? texture.Blocks.ToArray() : new BCnEncoder.Decoder.BcDecoder().DecodeRaw(texture.Blocks, texture.Width, texture.Height,
                    texture.Format switch { 0x1d01 => BCnEncoder.Shared.CompressionFormat.Bc4, 0x1e01 => BCnEncoder.Shared.CompressionFormat.Bc5,
                        0x1a01 or 0x1a06 => BCnEncoder.Shared.CompressionFormat.Bc1WithAlpha,
                        0x1b01 or 0x1b06 => BCnEncoder.Shared.CompressionFormat.Bc2,
                        0x1c01 or 0x1c06 => BCnEncoder.Shared.CompressionFormat.Bc3,
                        0x2001 or 0x2006 => BCnEncoder.Shared.CompressionFormat.Bc7,
                        _ => throw new InvalidDataException("Texture layout is unsupported.") }).SelectMany(p => new[] { p.r, p.g, p.b, p.a }).ToArray();
            }
            return new { texture.Width, texture.Height, SourceHash = hash, Pixels = Convert.ToBase64String(pixels) };
        }
        return resources.Select(resource => ModelDerivedCache<object>.Get(resource.Bytes,
            resource.Id + System.Text.Json.JsonSerializer.Serialize(resource.Materials), () =>
        {
            ModelTextureDocument? document = null;
            try { document = new ModelTextureDocument(resource.Bytes); } catch (InvalidDataException) { }
            var preview = PreviewTexture.Read(resource.Bytes);
            var pixels = document?.Pixels() ?? new BCnEncoder.Decoder.BcDecoder().DecodeRaw(preview.Blocks, preview.Width, preview.Height,
                preview.Format switch { 0x1d01 => BCnEncoder.Shared.CompressionFormat.Bc4, 0x1e01 => BCnEncoder.Shared.CompressionFormat.Bc5,
                    _ => throw new InvalidDataException("Texture layout is unsupported for color editing.") })
                .SelectMany(pixel => new[] { pixel.r, pixel.g, pixel.b, pixel.a }).ToArray();
            var width = Math.Min(256, preview.Width); var height = Math.Max(1, preview.Height * width / preview.Width);
            if (height > 256) { width = Math.Max(1, width * 256 / height); height = 256; }
            var thumbnail = new byte[width * height * 4];
            var colors = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var source = ((y * preview.Height / height) * preview.Width + x * preview.Width / width) * 4;
                pixels.AsSpan(source, 4).CopyTo(thumbnail.AsSpan((y * width + x) * 4));
                if (pixels[source + 3] == 0) continue;
                var color = $"#{pixels[source]:X2}{pixels[source + 1]:X2}{pixels[source + 2]:X2}";
                colors[color] = colors.GetValueOrDefault(color) + 1;
            }
            var info = checked((int)BitConverter.ToInt64(resource.Bytes, checked((int)BitConverter.ToInt64(resource.Bytes, 40))));
            return new { resource.Id, resource.Materials, SourceHash = ModelTextureIntent.Hash(resource.Bytes), preview.Width, preview.Height,
                Editable = document is not null, MipCount = BitConverter.ToUInt16(resource.Bytes, info + 22), Format = preview.Format.ToString("X4"), ThumbnailWidth = width, ThumbnailHeight = height,
                Pixels = Convert.ToBase64String(thumbnail), Colors = colors.OrderByDescending(pair => pair.Value).Take(16).Select(pair => pair.Key).ToArray() };
        }, value => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value).LongLength)).ToArray();
    }

    internal static object StageTexture(ModelTextureStageRequest request)
    {
        var project = Open(request.Paths);
        var intent = Intent(project, request.Id, request.Change);
        var session = request.Session is null ? null : EditSessionBridgeMapper.ToCore(request.Session);
        var updated = project.Paths.SelectedGame switch
        {
            ProjectGame.Sword or ProjectGame.Shield => new SwShModelTextureEditSessionService().Stage(project.Paths, session, intent),
            ProjectGame.ZA => new ZaModelTextureEditSessionService().Stage(project.Paths, session, intent),
            _ => new SvModelTextureEditSessionService().Stage(project.Paths, session, intent)
        };
        return new { Session = EditSessionBridgeMapper.ToDto(updated) };
    }

    private static ModelTextureResource[] Resources(OpenedProject project, string id) => project.Paths.SelectedGame switch
    {
        ProjectGame.Sword or ProjectGame.Shield => new SwShModelPreviewService().Textures(project, id),
        ProjectGame.ZA => new ZaModelPreviewService().Textures(project, id),
        _ => new SvModelPreviewService().Textures(project, id)
    };
    private static ModelTextureIntent Intent(OpenedProject project, string id, ModelTextureChangeDto change)
    {
        if (change?.Changes is null || change.Changes.Length > 32 || change.Changes.Any(color => color is null))
            throw new InvalidDataException("Texture color edits are invalid.");
        var intent = new ModelTextureIntent(project.Paths.SelectedGame.ToString() ?? throw new InvalidDataException("Select a game."), id, change.Texture, change.SourceHash,
            change.Changes.Select(color => new ModelTextureColorChange(color.From, color.To, color.Tolerance)).ToArray());
        intent.Validate(); return intent;
    }

    internal static object Catalog(ModelCatalogRequest request)
    {
        var project = Open(request.Paths);
        return project.Paths.SelectedGame switch
        {
            ProjectGame.Sword or ProjectGame.Shield => new SwShModelPreviewService().Catalog(project),
            ProjectGame.ZA => new ZaModelPreviewService().Catalog(project),
            _ => new SvModelPreviewService().Catalog(project)
        };
    }

    internal static object Prepare(ModelPrepareRequest request)
    {
        if (request.TransferId is not { Length: 32 } || !request.TransferId.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid model transfer identifier.");
        if (request.Resolution is not (1 or 2 or 4)) throw new InvalidDataException("Model preview resolution is invalid.");
        var project = Open(request.Paths);
        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (request.AssetChanges is { Length: > 0 } assetChanges)
        {
            if (assetChanges.Length > 2048 || assetChanges.Any(c => c is null) || assetChanges.Select(c => c.Asset).Distinct().Count() != assetChanges.Length)
                throw new InvalidDataException("Model preview changes are invalid.");
            var assets = Assets(project, request.Id).ToDictionary(a => a.Id, StringComparer.Ordinal);
            var vanilla = assetChanges.Any(c => c.Restore) ? Assets(project, request.Id, true).ToDictionary(a => a.Id, StringComparer.Ordinal) : null;
            foreach (var change in assetChanges)
            {
                var intent = AssetIntent(project, request.Id, change);
                if (!assets.TryGetValue(change.Asset, out var asset)) throw new InvalidDataException("Model asset association changed.");
                replacements.Add(asset.Id, ModelTextureEncodingCache.Encode(asset.Bytes, intent, vanilla?.GetValueOrDefault(asset.Id)?.Bytes).Bytes);
            }
        }
        byte[] Transform(string path, byte[] bytes) => replacements.GetValueOrDefault(path) ?? bytes;
        var scene = project.Paths.SelectedGame switch
        {
            ProjectGame.Sword or ProjectGame.Shield => new SwShModelPreviewService().Prepare(project, request.Id, request.Animation, transform: Transform),
            ProjectGame.ZA => new ZaModelPreviewService().Prepare(project, request.Id, request.Animation, transform: Transform),
            _ => new SvModelPreviewService().Prepare(project, request.Id, request.Animation, transform: Transform)
        };
        if (request.TextureChanges is { Length: > 0 } changes)
        {
            if (changes.Length > 32 || changes.Any(change => change is null) || changes.Select(change => change.Texture).Distinct().Count() != changes.Length)
                throw new InvalidDataException("Preview texture edits are invalid.");
            var resources = Resources(project, request.Id);
            var textures = scene.Textures.ToArray();
            foreach (var change in changes)
            {
                var intent = Intent(project, request.Id, change);
                var resource = resources.SingleOrDefault(texture => texture.Id == intent.Texture)
                    ?? throw new InvalidDataException("Preview texture is not associated with this model.");
                if (ModelTextureIntent.Hash(resource.Bytes) != intent.SourceHash) throw new InvalidDataException("Preview texture changed. Reload the model.");
                var document = new ModelTextureDocument(resource.Bytes);
                for (var i = 0; i < textures.Length; i++)
                    if (textures[i].SourcePath == resource.Id || textures[i].SourcePath == Path.GetFileName(resource.Id))
                        textures[i] = new(document.Width, document.Height, document.Srgb ? 0x0b06u : 0x0b01u, document.Preview(intent.Changes)) { SourcePath = textures[i].SourcePath };
            }
            scene = scene with { Textures = textures };
        }
        scene = scene with { Textures = scene.Textures.Select(texture => PreviewTextureResolution.Select(texture, request.Resolution)).ToArray() };
        var folder = Path.Combine(Path.GetTempPath(), "km-editor-model-preview");
        var path = Path.Combine(folder, request.TransferId + ".kmv");
        // The native host owns a delete-on-close handle. Even a cancelled worker or host
        // crash cannot leave a completed transfer accumulating on disk.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length != 0) throw new InvalidDataException("Model transfer is already populated.");
        scene.Write(stream, request.Resolution);
        if (stream.Length > 96L * 1024 * 1024) throw new InvalidDataException("Model preview exceeds the transfer budget.");
        return new { Ready = true };
    }

    private static OpenedProject Open(KM.Api.Projects.ProjectPathsDto paths)
    {
        var core = ProjectBridgeMapper.ToCore(paths);
        if (core.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield or ProjectGame.Scarlet or ProjectGame.Violet or ProjectGame.ZA))
            throw new InvalidDataException("Select a supported game for the 3D Model Editor.");
        return new ProjectWorkspaceService().ValidateAndOpen(core);
    }
}
