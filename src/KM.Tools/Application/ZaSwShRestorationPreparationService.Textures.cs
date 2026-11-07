// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed class RestorationTexturePool(Dictionary<string, byte[]> members, string folder, bool share)
    {
        private readonly Dictionary<string, string> identities = new(StringComparer.Ordinal);

        internal string Add(string name, byte[] bytes)
        {
            if (share)
            {
                // Names and pointer padding can differ for identical images. Compare
                // exact encoded mip blocks and all texture sampling metadata instead.
                string key;
                try { key = new ModelTextureDocument(bytes).ContentIdentity(); }
                catch (InvalidDataException)
                {
                    // Cube maps and formats outside the checked color reader still
                    // require byte-identical containers after neutral renaming.
                    var neutral = ModelTextureIdentity.Rename(bytes, name, new string('x', name.Length));
                    key = "container:" + Convert.ToHexString(SHA256.HashData(neutral));
                }
                if (identities.TryGetValue(key, out var existing)) return existing;
                identities.Add(key, name);
                if (members.TryGetValue(folder + name + ".bntx", out var previous) && !previous.AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException("Converted texture identity has different image content.");
            }
            members[folder + name + ".bntx"] = bytes;
            return name;
        }
    }
}
