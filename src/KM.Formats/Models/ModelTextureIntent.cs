// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KM.Formats.Models;

public sealed record ModelTextureIntent(string Game, string Model, string Texture, string SourceHash, ModelTextureColorChange[] Changes, string EncodedHash = "")
{
    public const string Domain = "workflow.modelTextures";
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public string Serialize() => JsonSerializer.Serialize(this);
    public static ModelTextureIntent Parse(string? text)
    {
        if (text is not { Length: > 0 and <= 16384 }) throw new InvalidDataException("Texture edit is invalid.");
        var intent = JsonSerializer.Deserialize<ModelTextureIntent>(text) ?? throw new InvalidDataException("Texture edit is missing.");
        intent.Validate(); return intent;
    }
    public void Validate()
    {
        if (Game is not ("Sword" or "Shield" or "Scarlet" or "Violet" or "ZA") || Model is not { Length: > 0 and <= 1024 }
            || Texture is not { Length: > 0 and <= 1024 } || !Texture.EndsWith(".bntx", StringComparison.Ordinal)
            || TrinityPreviewReader.Resolve("catalog", Texture) != Texture || !Fingerprint(SourceHash)
            || (EncodedHash != "" && !Fingerprint(EncodedHash)) || Changes is null || Changes.Length > 32
            || Changes.Any(change => change is null)) throw new InvalidDataException("Texture edit identity is invalid.");
    }
    private static bool Fingerprint(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}

/// <summary>Bounded reuse of verified encodings during stage, review and apply.</summary>
public static class ModelTextureEncodingCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ModelTextureEncoding> Entries = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();
    private static long bytes;
    public static ModelTextureEncoding Encode(byte[] source, ModelTextureIntent intent)
    {
        intent.Validate();
        if (ModelTextureIntent.Hash(source) != intent.SourceHash) throw new InvalidDataException("Texture source changed. Reload before editing.");
        var key = ModelTextureIntent.Hash(Encoding.UTF8.GetBytes(intent.SourceHash + JsonSerializer.Serialize(intent.Changes)));
        lock (Gate)
            if (Entries.TryGetValue(key, out var existing)) return Check(existing);
        var result = new ModelTextureDocument(source).Recolor(intent.Changes);
        lock (Gate)
        {
            while (bytes + result.Bytes.Length > 64L * 1024 * 1024 && Order.TryDequeue(out var oldest))
                if (Entries.Remove(oldest, out var removed)) bytes -= removed.Bytes.Length;
            if (!Entries.ContainsKey(key)) { Entries.Add(key, result); Order.Enqueue(key); bytes += result.Bytes.Length; }
        }
        return Check(result);
        ModelTextureEncoding Check(ModelTextureEncoding encoding)
        {
            if (intent.EncodedHash != "" && ModelTextureIntent.Hash(encoding.Bytes) != intent.EncodedHash)
                throw new InvalidDataException("Texture encoding changed. Stage the edit again.");
            return encoding with { Bytes = encoding.Bytes.ToArray() };
        }
    }
}
