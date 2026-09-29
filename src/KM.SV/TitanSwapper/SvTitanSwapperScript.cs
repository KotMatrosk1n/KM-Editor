// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Lua;
using KM.SV.RuntimeSettings;

namespace KM.SV.TitanSwapper;

internal static class SvTitanSwapperScript
{
    public const string VirtualPath = SvGameplayOptionsLuaTransformer.SourceRelativePath;
    private const int Insertion = 429030;
    private const int ChildCount = 30829;
    private const string Marker = "KM_TITAN_LABELS";
    private const string TmVisibilitySha256 = "7CA910D8781AFDB8168160020449C1627CFF29598406C6B06B1FA517E00D7F7A";
    private static readonly uint[] AddedCode = [79u | (69u << 7) | ((uint)ChildCount << 15), 68u | (69u << 7) | (1u << 16) | (1u << 24)];
    private static readonly Lua54Prototype Template = Lua54BinaryChunk.Parse(Convert.FromBase64String(SvTitanSwapperBytecode.Value)).Root;

    public static IReadOnlyList<string> ReadLabels(byte[] source) => Remove(source).Labels;

    public static byte[] Apply(byte[] source, IEnumerable<string> labels)
    {
        var current = Remove(source);
        var plain = current.Bytes;
        var list = labels.Order(StringComparer.Ordinal).ToArray();
        ValidateLabels(list);
        if (list.Length == 0) return plain;
        if (current.Labels.SequenceEqual(list)) return source.ToArray();
        var chunk = Lua54BinaryChunk.Parse(plain);
        var constants = Template.Constants.Select(constant => constant.TryGetUtf8String(out var text) && text == Marker
            ? Lua54Constant.FromUtf8String(string.Join(';', list)) : constant).ToArray();
        var helper = Clone(Template, Template.Code, constants, Template.Children, [new(0, 0, 0)]);
        var code = chunk.Root.Code.Take(Insertion).Concat(AddedCode).Concat(chunk.Root.Code.Skip(Insertion)).ToArray();
        return chunk.WithRoot(Clone(chunk.Root, code, chunk.Root.Constants, [.. chunk.Root.Children, helper], chunk.Root.Upvalues)).Serialize();
    }

    public static (byte[] Bytes, IReadOnlyList<string> Labels, bool HasGameplayOptions) Remove(byte[] source)
    {
        var hash = Convert.ToHexString(SHA256.HashData(source));
        if (hash is SvGameplayOptionsLuaTransformer.VanillaSourceSha256 or TmVisibilitySha256)
            return (source, [], false);
        if (hash == SvGameplayOptionsLuaTransformer.DerivedSourceSha256) return (source, [], true);
        var chunk = Lua54BinaryChunk.Parse(source);
        if (!chunk.Serialize().AsSpan().SequenceEqual(source)) throw new InvalidDataException("The script did not round trip.");
        var root = chunk.Root;
        IReadOnlyList<string> labels = [];
        if (root.Children.Count == ChildCount + 1)
        {
            if (root.Code.Count != 429036 || !root.Code.Skip(Insertion).Take(2).SequenceEqual(AddedCode))
                throw new InvalidDataException("The Titan Swapper script entry changed.");
            var helper = root.Children[^1];
            if (!helper.Upvalues.SequenceEqual(new Lua54Upvalue[] { new(0, 0, 0) }))
                throw new InvalidDataException("The Titan Swapper environment changed.");
            var markerIndex = Template.Constants.Select((constant, index) => (constant, index))
                .Single(pair => pair.constant.TryGetUtf8String(out var text) && text == Marker).index;
            if (helper.Constants.Count != Template.Constants.Count
                || !helper.Constants[markerIndex].TryGetUtf8String(out var text))
                throw new InvalidDataException("The Titan Swapper configuration is invalid.");
            labels = text.Split(';');
            ValidateLabels(labels);
            var restoredConstants = helper.Constants.ToArray();
            restoredConstants[markerIndex] = Lua54Constant.FromUtf8String(Marker);
            var normalized = Clone(helper, helper.Code, restoredConstants, helper.Children, Template.Upvalues);
            if (!chunk.WithRoot(normalized).Serialize().AsSpan().SequenceEqual(chunk.WithRoot(Template).Serialize()))
                throw new InvalidDataException("The Titan Swapper runtime changed outside its configuration.");
            root = Clone(root, root.Code.Take(Insertion).Concat(root.Code.Skip(Insertion + 2)).ToArray(),
                root.Constants, root.Children.Take(ChildCount).ToArray(), root.Upvalues);
            chunk = chunk.WithRoot(root);
        }
        var bytes = chunk.Serialize();
        return (bytes, labels, ValidateOtherFeatures(chunk));
    }

    private static bool ValidateOtherFeatures(Lua54BinaryChunk chunk)
    {
        if (chunk.Root.Children.Count != ChildCount || chunk.Root.Code.Count != 429034)
            throw new InvalidDataException("Unsupported Titan Swapper script build.");
        var children = chunk.Root.Children.ToArray();
        var tm = children[24349];
        if (tm.Code.Count != 412 || tm.Code[168] is not (0x000085C2u or 0x000B0580u))
            throw new InvalidDataException("The shared TM script instruction is unsupported.");
        var instructions = tm.Code.ToArray();
        instructions[168] = 0x000085C2;
        children[24349] = tm.WithCodeAndConstants(instructions, tm.Constants);
        var hash = Convert.ToHexString(SHA256.HashData(chunk.WithRoot(chunk.Root.WithChildren(children)).Serialize()));
        if (hash != SvGameplayOptionsLuaTransformer.VanillaSourceSha256 && hash != SvGameplayOptionsLuaTransformer.DerivedSourceSha256)
            throw new InvalidDataException("Titan Swapper requires a supported S/V 4.0.0 script or recognized composed output.");
        return hash == SvGameplayOptionsLuaTransformer.DerivedSourceSha256;
    }

    private static void ValidateLabels(IReadOnlyList<string> labels)
    {
        if (labels.Count > 13 || labels.Distinct(StringComparer.Ordinal).Count() != labels.Count
            || labels.Any(label => !SvTitanSwapperDocument.Encounters.Any(encounter => encounter.Label == label)))
            throw new InvalidDataException("Unsupported Titan encounter selection.");
    }

    private static Lua54Prototype Clone(Lua54Prototype p, IReadOnlyList<uint> code,
        IReadOnlyList<Lua54Constant> constants, IReadOnlyList<Lua54Prototype> children, IReadOnlyList<Lua54Upvalue> upvalues) =>
        new(p.DeclaredSourceBytes?.ToArray(), p.LineDefined, p.LastLineDefined, p.NumParams, p.IsVarArg, p.MaxStackSize,
            code, constants, upvalues, children, p.LineInfoBytes, p.AbsoluteLines, p.LocalVariables, p.UpvalueNameBytes);
}
