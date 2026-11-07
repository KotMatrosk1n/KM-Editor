// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;
using KM.Formats.ZA;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private sealed record UnlitPalette(IReadOnlyDictionary<string, ZaSwShDisplacedUnlit.Contract> Contracts,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Images);

        private UnlitPalette UnlitTextures(byte[] materials, string path, bool rare, RestorationTexturePool pool)
        {
            var contracts = ZaSwShDisplacedUnlit.Contracts(materials);
            var images = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var (name, contract) in contracts)
            {
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var binding in contract.Textures)
                {
                    var bytes = Donor(TrinityPreviewReader.Resolve(path, binding.FileName)); var b = new ModelBuffer(bytes);
                    int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(at, 8)));
                    if (!b.Slice(0, 4).SequenceEqual("BNTX"u8) || b.U32(36) != 1)
                        throw new InvalidDataException("Source Unlit texture container changed.");
                    var at = Pointer(Pointer(Pointer(40)) + 96);
                    var original = System.Text.Encoding.UTF8.GetString(b.Slice(at + 2, b.U16(at)));
                    var renamed = ModelTextureIdentity.MaterialName(original, images.Count, 'u', rare ? 's' : 'n');
                    names.Add(binding.SourceRole, pool.Add(renamed, ModelTextureIdentity.Rename(bytes, original, renamed)));
                }
                images.Add(name, names);
            }
            return new(contracts, images);
        }
    }
}
