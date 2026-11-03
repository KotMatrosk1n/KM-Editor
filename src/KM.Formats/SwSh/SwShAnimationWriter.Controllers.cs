// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    public static byte[] Controller(byte[] nativeController, string donorStem, string targetStem,
        IReadOnlyList<string> materialNames, IReadOnlyDictionary<string, string[]> facialBones, bool compactMaterials = false)
    {
        if (materialNames.Count is < 1 or > 128 || materialNames.Distinct(StringComparer.Ordinal).Count() != materialNames.Count)
            throw new InvalidDataException("Controller material targets are invalid.");
        var source = new ModelBuffer(nativeController); var writer = new ModelTableBuilder(nativeController);
        var bindings = source.Table(source.Root, 6);
        foreach (var binding in source.Tables(bindings, 0, 2048))
        {
            var path = source.Text(binding, 1) ?? throw new InvalidDataException("Controller animation reference is missing.");
            if (!path.Contains(donorStem, StringComparison.Ordinal)) throw new InvalidDataException("Controller has an undeclared animation dependency.");
            writer.Text(binding, 1, path.Replace(donorStem, targetStem, StringComparison.Ordinal));
        }
        var layers = source.Tables(source.Table(source.Root, 0), 0, 128);
        var vectors = new Dictionary<int, int[]>();
        foreach (var layer in layers)
        {
            var masks = source.Table(source.Table(layer, 2), 1);
            if (masks == 0) throw new InvalidDataException("Controller material mask is missing.");
            vectors.Add(layer, writer.Vector(masks, 1, materialNames.Count));
        }
        for (var i = 0; i < materialNames.Count; i++)
        {
            var copy = compactMaterials ? 0 : writer.Append(nativeController); var name = materialNames[i];
            foreach (var layer in layers)
            {
                var masks = source.Table(source.Table(layer, 2), 1);
                var entries = source.Tables(masks, 1, 128).Where(t => source.Text(t, 0) == (name.Contains("eye", StringComparison.Ordinal) ? "Eye" : "Body01Vco")).ToArray();
                if (entries.Length != 1) throw new InvalidDataException("Native controller material mask cannot be resolved.");
                if (compactMaterials)
                {
                    var original = entries[0];
                    if (source.U16(original - source.I32(original)) != 12)
                        throw new InvalidDataException("Native material controller mask needs another adapter.");
                    var target = writer.Table(4); writer.Pointer(vectors[layer][i], target); writer.Text(target, 0, name);
                    for (var field = 1; field <= 3; field++)
                    {
                        var filter = source.Table(original, field);
                        if (filter == 0 || source.U16(filter - source.I32(filter)) > 8)
                            throw new InvalidDataException("Native material parameter mask needs another adapter.");
                        var absent = Enumerable.Range(0, 2).Where(index => source.Field(filter, index) == 0).ToHashSet();
                        var mode = absent.Contains(0) ? (byte)0 : source.U8(source.Field(filter, 0));
                        var replacement = writer.Table(2, absent.Contains(0) ? null : new Dictionary<int, byte[]> { [0] = [mode] }, absent: absent);
                        writer.Reference(target, field, replacement);
                        if (absent.Contains(1)) continue;
                        var parameters = source.Tables(filter, 1, 256);
                        var parameterSlots = writer.Vector(replacement, 1, parameters.Length);
                        for (var index = 0; index < parameters.Length; index++)
                        {
                            if (source.U16(parameters[index] - source.I32(parameters[index])) > 6)
                                throw new InvalidDataException("Native material parameter name needs another adapter.");
                            var parameter = writer.Table(1); writer.Pointer(parameterSlots[index], parameter);
                            writer.Text(parameter, 0, source.Text(parameters[index], 0) ?? throw new InvalidDataException("Native parameter mask is unnamed."));
                        }
                    }
                }
                else
                {
                    var target = copy + entries[0]; writer.Pointer(vectors[layer][i], target); writer.Text(target, 0, name);
                }
            }
        }
        foreach (var layer in layers)
        {
            var name = source.Text(layer, 1) ?? "";
            if (!facialBones.TryGetValue(name, out var bones)) continue;
            if (bones.Length == 0 || bones.Length > 128 || bones.Distinct(StringComparer.Ordinal).Count() != bones.Length)
                throw new InvalidDataException("Facial controller mask has invalid bone targets.");
            var mask = source.Table(layer, 2);
            var skeletal = writer.Table(2, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Integer(1) }); writer.Reference(mask, 0, skeletal);
            var slots = writer.Vector(skeletal, 1, bones.Length);
            for (var i = 0; i < bones.Length; i++) { var item = writer.Table(1); writer.Pointer(slots[i], item); writer.Text(item, 0, bones[i]); }
        }
        return writer.Finish(source.Root);
    }

    public static byte[] BattleIdle(byte[] primaryIdle)
    {
        // A secondary battle wait must keep the battle pose and still finish.
        // Ambient fidgets remain available to field and camp controllers.
        var source = new ModelBuffer(primaryIdle);
        var info = source.Table(source.Root, 0);
        if (source.Value(info, 0) != 1 || source.Value(info, 1) == 0 || source.Value(info, 2) == 0)
            throw new InvalidDataException("Battle idle requires a looping primary animation.");
        var writer = new ModelTableBuilder(primaryIdle);
        writer.Int32(source.Field(info, 0), 0);
        return writer.Finish(source.Root);
    }

    /// <summary>Copies an autonomous global loop into a controller without changing its ordinary states.</summary>
    public static byte[] AddGlobalLoop(byte[] controller, byte[] battleController)
    {
        var target = new ModelBuffer(controller); var donor = new ModelBuffer(battleController);
        var writer = new ModelTableBuilder(controller);
        var layers = target.Tables(target.Table(target.Root, 0), 0, 128);
        if (layers.Length is < 1 or >= 128 || layers.Any(t => target.Text(t, 1) == "gloop01")
            || layers.Where((t, i) => target.Value(t, 0) != i).Any())
            throw new InvalidDataException("Global loop requires an ordered controller without an existing loop.");
        var layer = donor.Tables(donor.Table(donor.Root, 0), 0, 128).Single(t => donor.Text(t, 1) == "gloop01");
        var machine = donor.Tables(donor.Table(donor.Root, 4), 0, 128).Single(t => donor.Text(t, 0) == "gloop01");
        var resource = donor.Tables(donor.Table(donor.Root, 6), 0, 2048).Single(t => donor.Text(t, 0) == "gloop01");
        var vectors = new List<(int[] Slots, int[] Entries, int Entry)>();
        foreach (var (field, entry, nameField) in new[] { (0, layer, 1), (4, machine, 0), (6, resource, 0) })
        {
            var parent = target.Table(target.Root, field);
            if (parent == 0) throw new InvalidDataException("Global loop controller table is missing.");
            var entries = target.Tables(parent, 0, 2048);
            if (entries.Any(t => target.Text(t, nameField) == "gloop01"))
                throw new InvalidDataException("Global loop controller identity is already assigned.");
            var slots = writer.Vector(parent, 0, entries.Length + 1);
            vectors.Add((slots, entries, entry));
        }
        var original = writer.Append(controller); var copy = writer.Append(battleController);
        writer.Int32(copy + donor.Field(layer, 0), layers.Length);
        foreach (var (slots, entries, entry) in vectors)
        {
            for (var i = 0; i < entries.Length; i++) writer.Pointer(slots[i], original + entries[i]);
            writer.Pointer(slots[^1], copy + entry);
        }
        return writer.Finish(target.Root);
    }

    public static byte[] BattleIdleController(byte[] controller)
    {
        var source = new ModelBuffer(controller); var writer = new ModelTableBuilder(controller);
        var bindings = source.Tables(source.Table(source.Root, 6), 0, 2048);
        var paths = bindings.Select(b => source.Text(b, 1) ?? "").ToArray();
        var replaced = 0;
        for (var i = 0; i < bindings.Length; i++)
        {
            if (!paths[i].EndsWith("_ba10_waitB01.gfbanm", StringComparison.Ordinal)) continue;
            var stem = paths[i][..^"_ba10_waitB01.gfbanm".Length];
            if (!paths.Contains(stem + "_ba10_waitA01.gfbanm", StringComparer.Ordinal))
                throw new InvalidDataException("Battle controller primary idle is missing.");
            writer.Text(bindings[i], 1, stem + "_battleidle01.gfbanm"); replaced++;
        }
        if (replaced != 1) throw new InvalidDataException("Battle controller secondary idle is missing or ambiguous.");
        return writer.Finish(source.Root);
    }

    public static byte[] GlobalMaterialLoop(byte[] nativeAnimation, IReadOnlyList<string> materialNames,
        IReadOnlyDictionary<string, System.Numerics.Vector2>? normalUvScales = null,
        IReadOnlyDictionary<string, System.Numerics.Vector4>? colorUvTransforms = null,
        IReadOnlyDictionary<string, SwShNormalTileQualification>? normalTileQualifications = null)
    {
        if (materialNames.Count is < 1 or > 128) throw new InvalidDataException("Global material animation target count is invalid.");
        if (normalUvScales?.Any(pair => !materialNames.Contains(pair.Key, StringComparer.Ordinal)
            || pair.Value.X is not (1 or 2) || pair.Value.Y is not (1 or 2 or 4)
            || pair.Value.Y == 4 && normalTileQualifications?.GetValueOrDefault(pair.Key)?.Matches(pair.Key, pair.Value) != true) == true
            || normalTileQualifications?.Any(pair => normalUvScales?.TryGetValue(pair.Key, out var scale) != true
                || !pair.Value.Matches(pair.Key, scale)) == true)
            throw new InvalidDataException("Global normal UV scale or material is unsupported.");
        if (colorUvTransforms?.Any(pair => !materialNames.Contains(pair.Key, StringComparer.Ordinal)
            || !float.IsFinite(pair.Value.X) || !float.IsFinite(pair.Value.Y)
            || !float.IsFinite(pair.Value.Z) || !float.IsFinite(pair.Value.W)
            || pair.Value.X <= 0 || pair.Value.Y <= 0
            || !float.IsFinite(-pair.Value.Z / pair.Value.X) || !float.IsFinite(-pair.Value.W / pair.Value.Y)) == true)
            throw new InvalidDataException("Global color UV transform or material is unsupported.");
        var source = new ModelBuffer(nativeAnimation); var writer = new ModelTableBuilder(nativeAnimation);
        var skeleton = source.Table(source.Root, 1); if (skeleton != 0) writer.Vector(skeleton, 0, 0);
        var material = source.Table(source.Root, 2);
        if (material == 0) throw new InvalidDataException("Native global material animation is missing.");
        var originals = source.Tables(material, 0, 128);
        var slots = writer.Vector(material, 0, materialNames.Count);
        for (var i = 0; i < materialNames.Count; i++)
        {
            var name = materialNames[i]; var matches = originals.Where(t => source.Text(t, 0) == (name.Contains("eye", StringComparison.Ordinal) ? "Eye" : "Body01Vco")).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Native global material animation target is ambiguous.");
            var original = matches[0]; var copy = writer.Append(nativeAnimation); var target = copy + original;
            writer.Pointer(slots[i], target); writer.Text(target, 0, name);
            var colorParameters = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scalar in source.Tables(original, 2, 256))
            {
                var parameter = source.Text(scalar, 0);
                var value = parameter switch
                {
                    "ColorUVScaleU" or "ColorUVScaleV" or "NormalMapUVScaleU" or "NormalMapUVScaleV" => 1f,
                    "ColorBaseU" or "ColorBaseV" or "ColorUVTranslateU" or "ColorUVTranslateV" => 0f,
                    _ => (float?)null,
                };
                if (normalUvScales?.TryGetValue(name, out var normalScale) == true)
                {
                    if (normalScale.X is not (1 or 2) || normalScale.Y is not (1 or 2 or 4)
                        || normalScale.Y == 4 && normalTileQualifications?.GetValueOrDefault(name)?.Matches(name, normalScale) != true)
                        throw new InvalidDataException("Global normal UV scale is unsupported.");
                    if (parameter == "NormalMapUVScaleU") value = normalScale.X;
                    if (parameter == "NormalMapUVScaleV") value = normalScale.Y;
                }
                if (colorUvTransforms?.TryGetValue(name, out var colorTransform) == true)
                {
                    value = parameter switch
                    {
                        "ColorUVScaleU" => colorTransform.X,
                        "ColorUVScaleV" => colorTransform.Y,
                        "ColorUVTranslateU" => -colorTransform.Z / colorTransform.X,
                        "ColorUVTranslateV" => -colorTransform.W / colorTransform.Y,
                        _ => value,
                    };
                    if (parameter is "ColorUVScaleU" or "ColorUVScaleV" or "ColorUVTranslateU" or "ColorUVTranslateV")
                        if (!colorParameters.Add(parameter)) throw new InvalidDataException("Global color UV parameter is duplicated.");
                }
                if (value is null) continue;
                var kindAt = source.Field(scalar, 1);
                if (kindAt == 0 || source.U8(kindAt) != 1) throw new InvalidDataException("Native global UV parameter is not constant.");
                var curve = source.Table(scalar, 2); var field = source.Field(curve, 0);
                // Native scalar tracks can share one constant curve. A cropped
                // atlas needs its own value without changing those aliases.
                if (field == 0 || normalUvScales?.ContainsKey(name) == true || colorUvTransforms?.ContainsKey(name) == true)
                {
                    var replacement = writer.Table(1, new Dictionary<int, byte[]> { [0] = ModelTableBuilder.Floats(value.Value) });
                    writer.Reference(copy + scalar, 2, replacement);
                }
                else writer.Single(copy + field, value.Value);
            }
            if (colorUvTransforms?.ContainsKey(name) == true && colorParameters.Count != 4)
                throw new InvalidDataException("Global color UV parameters are incomplete.");
        }
        return writer.Finish(source.Root);
    }
}
