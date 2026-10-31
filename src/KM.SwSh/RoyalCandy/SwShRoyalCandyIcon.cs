// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.Models;

namespace KM.SwSh.RoyalCandy;

internal static class SwShRoyalCandyIcon
{
    public const string Path = "romfs/bin/appli/icon_item/item_1128.bntx";
    private static readonly Lazy<byte[]> Pixels = new(() =>
    {
        using var stream = typeof(SwShRoyalCandyIcon).Assembly.GetManifestResourceStream(
            "KM.SwSh.RoyalCandy.Assets.royal-candy.rgba")
            ?? throw new InvalidDataException("Royal Candy artwork is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var pixels = buffer.ToArray();
        if (pixels.Length != 64 * 64 * 4)
            throw new InvalidDataException("Royal Candy artwork must be 64 by 64 RGBA pixels.");
        return pixels;
    });

    public static byte[] Create(byte[] template)
    {
        var texture = new ModelTextureDocument(template);
        if (texture.Width != 64 || texture.Height != 64 || texture.MipCount != 1 || texture.Format != 0x1a01)
            throw new InvalidDataException("Royal Candy requires the native 64 by 64 BC1 item texture.");
        // Validate both the texture name and its dictionary without changing the native container.
        _ = ModelTextureIdentity.Rename(template, "item_1128", "item_1128");
        return texture.EncodeMipImages([Pixels.Value]);
    }

    public static bool IsOwned(byte[] candidate, byte[] template) => candidate.AsSpan().SequenceEqual(Create(template));
}
