// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.ZA;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private sealed record LayerSurfacePalette(IReadOnlyDictionary<string, ZaSwShLayerSurface.Contract> Contracts,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Images);

        private LayerSurfacePalette LayerSurfaceTextures(byte[] materials, string path, bool rare, RestorationTexturePool pool)
        {
            var contracts = ZaSwShLayerSurface.Contracts(materials);
            var images = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var (name, contract) in contracts)
            {
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var binding in contract.Textures)
                {
                    var bytes = Donor(TrinityPreviewReader.Resolve(path, binding.FileName)); var b = new ModelBuffer(bytes);
                    int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(at, 8)));
                    if (!b.Slice(0, 4).SequenceEqual("BNTX"u8) || b.U32(36) != 1)
                        throw new InvalidDataException("Source layered surface texture container changed.");
                    var at = Pointer(Pointer(Pointer(40)) + 96);
                    var original = System.Text.Encoding.UTF8.GetString(b.Slice(at + 2, b.U16(at)));
                    var renamed = ModelTextureIdentity.MaterialName(original, images.Count, 'h', rare ? 's' : 'n');
                    var format = b.U32(Pointer(Pointer(40)) + 28);
                    var sourceImage = PreviewTexture.ReadBaseLevel(bytes);
                    if (format is 0x0b01 or 0x0b06 || Math.Max(sourceImage.Width, sourceImage.Height) > 512 && format is not (0x2001 or 0x2006))
                        bytes = ZaSwShMaterialBaker.CompressAuthoredMips(bytes, token, textureEncoding);
                    var sampled = PreviewTexture.ReadBaseLevel(bytes);
                    var largest = Math.Max(sampled.Width, sampled.Height);
                    if (largest > 512)
                    {
                        // Match the layered body's 512 pixel allocation using
                        // only authored BC7 mips, retaining their exact blocks.
                        var firstMip = (int)Math.Log2(largest / 512);
                        // Animated source masks can have a single authored
                        // level. Preserve that image when no stored tail exists.
                        if (firstMip < new ModelTextureDocument(bytes).MipCount)
                            bytes = SwShTextureAllocation.MipTail(bytes, firstMip);
                    }
                    names.Add(binding.SourceRole, pool.Add(renamed, ModelTextureIdentity.Rename(bytes, original, renamed)));
                }
                images.Add(name, names);
            }
            return new(contracts, images);
        }
    }
}
