// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SurfaceTextures(
            IEnumerable<(string Material, ZaSwShFresnelMaterial.TextureBinding[] Textures)> contracts,
            string path, bool rare, RestorationTexturePool pool)
        {
            var images = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var (material, bindings) in contracts)
            {
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var binding in bindings)
                {
                    token.ThrowIfCancellationRequested();
                    var bytes = Donor(TrinityPreviewReader.Resolve(path, binding.FileName));
                    var source = new ModelBuffer(bytes);
                    int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(at, 8)));
                    if (!source.Slice(0, 4).SequenceEqual("BNTX"u8) || source.U32(36) != 1)
                        throw new InvalidDataException("Source surface texture container changed.");
                    var texture = Pointer(Pointer(40)); var at = Pointer(texture + 96);
                    var original = System.Text.Encoding.UTF8.GetString(source.Slice(at + 2, source.U16(at)));
                    var renamed = ModelTextureIdentity.MaterialName(original, images.Count, 'h', rare ? 's' : 'n');
                    var format = source.U32(texture + 28);
                    var sourceSize = Math.Max(source.U32(texture + 36), source.U32(texture + 40));
                    if (format is 0x0b01 or 0x0b06 || sourceSize > 512 && format is not (0x2001 or 0x2006))
                        bytes = ZaSwShMaterialBaker.CompressAuthoredMips(bytes, token, textureEncoding);
                    var image = new ModelBuffer(bytes);
                    var info = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(image.Slice(
                        checked((int)BinaryPrimitives.ReadUInt64LittleEndian(image.Slice(40, 8))), 8)));
                    var largest = Math.Max(image.U32(info + 36), image.U32(info + 40));
                    if (largest > 512 && largest % 512 == 0)
                    {
                        var firstMip = (int)Math.Log2(largest / 512);
                        if (firstMip < image.U16(info + 22)) bytes = KM.Formats.SwSh.SwShTextureAllocation.MipTail(bytes, firstMip);
                    }
                    names.Add(binding.SourceRole, pool.Add(renamed, ModelTextureIdentity.Rename(bytes, original, renamed)));
                }
                images.Add(material, names);
            }
            return images;
        }
    }
}
