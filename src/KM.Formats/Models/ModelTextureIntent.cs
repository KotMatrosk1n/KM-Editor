// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KM.Formats.Models;

public sealed record ModelTextureIntent(string Game, string Model, string Texture, string SourceHash, ModelTextureColorChange[] Changes, string EncodedHash = "",
    string Kind = "texture", ModelMaterialChange[]? MaterialChanges = null)
{
    public const string Domain = "workflow.modelTextures";
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public string Serialize() => JsonSerializer.Serialize(this);
    public static ModelTextureIntent Parse(string? text)
    {
        if (text is not { Length: > 0 and <= 262144 }) throw new InvalidDataException("Model edit is invalid.");
        var intent = JsonSerializer.Deserialize<ModelTextureIntent>(text) ?? throw new InvalidDataException("Texture edit is missing.");
        intent.Validate(); return intent;
    }
    public void Validate()
    {
        if (Game is not ("Sword" or "Shield" or "Scarlet" or "Violet" or "ZA") || Model is not { Length: > 0 and <= 1024 }
            || Texture is not { Length: > 0 and <= 1024 } || Kind is not ("texture" or "material" or "restore")
            || (Kind == "texture" && !Texture.EndsWith(".bntx", StringComparison.Ordinal))
            || (Kind == "material" && Path.GetExtension(Texture) is not (".trmtr" or ".gfbmdl"))
            || TrinityPreviewReader.Resolve("catalog", Texture) != Texture || !Fingerprint(SourceHash)
            || (EncodedHash != "" && !Fingerprint(EncodedHash)) || Changes is null || Changes.Length > 32
            || Changes.Any(change => change is null) || (Kind != "texture" && Changes.Length != 0)
            || MaterialChanges is { Length: > 512 } || MaterialChanges?.Any(c => c is null) == true
            || (Kind != "material" && MaterialChanges is { Length: > 0 })) throw new InvalidDataException("Model edit identity is invalid.");
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
    public static ModelTextureEncoding Encode(byte[] source, ModelTextureIntent intent, byte[]? vanilla = null)
    {
        intent.Validate();
        if (ModelTextureIntent.Hash(source) != intent.SourceHash) throw new InvalidDataException("Texture source changed. Reload before editing.");
        if (intent.Kind == "restore")
        {
            if (vanilla is null) throw new InvalidDataException("Vanilla model asset is unavailable.");
            return Check(new(vanilla, source.AsSpan().SequenceEqual(vanilla) ? 0 : 1, 0, 0));
        }
        var key = ModelTextureIntent.Hash(Encoding.UTF8.GetBytes(intent.SourceHash + intent.Kind + Path.GetExtension(intent.Texture)
            + JsonSerializer.Serialize(intent.Changes) + JsonSerializer.Serialize(intent.MaterialChanges)));
        lock (Gate)
            if (Entries.TryGetValue(key, out var existing)) return Check(existing);
        ModelTextureEncoding result;
        if (intent.Kind == "material")
        {
            var packed = intent.Texture.EndsWith(".gfbmdl", StringComparison.Ordinal);
            var encoded = new ModelMaterialDocument(source, packed).Apply(intent.MaterialChanges ?? []);
            _ = new ModelMaterialDocument(encoded, packed);
            result = new(encoded, source.AsSpan().SequenceEqual(encoded) ? 0 : 1, 0, 0);
        }
        else result = new ModelTextureDocument(source).Recolor(intent.Changes);
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
