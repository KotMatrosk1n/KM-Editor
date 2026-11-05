// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Tools.Application;

internal static class SwShRestorationFraming
{
    internal static byte[] SummaryCorrection(byte[] configuration, string definition)
    {
        // Summary uses its own scale and vertical placement. Keep battle and field
        // dimensions intact, including airborne poses and authored crouches.
        var adjustment = definition switch
        {
            "hisuian-growlithe" => (1.15f, 0f),
            "hisuian-arcanine" => (1.15f, 0f),
            "hisuian-samurott" => (1.15f, 0f),
            "doduo" => (1.35f, 0f),
            "dodrio" => (1f, 105f),
            "voltorb" or "hisuian-voltorb" => (1.6f, 50f),
            "electrode" or "hisuian-electrode" => (1f, 75f),
            "muk" or "alolan-muk" => (.8f, 20f),
            "venonat" => (1f, -44f),
            "venomoth" => (1.15f, 80f),
            "primeape" => (1.3f, 0f),
            "mega-charizard-x" => (1f, -100f),
            "venusaur-mega" => (1.8f, 0f),
            "blastoise-mega" => (1.8f, 50f),
            "absol-mega" => (1.2f, 0f),
            "pidgeot-mega" => (1.8f, 0f),
            "clefable-mega" => (1f, 100f),
            "alakazam-mega" => (1.4f, 60f),
            "mega-charizard-y" => (1.3f, -50f),
            "quilava" => (1f, 35f),
            "totodile" => (.85f, -37f),
            "croconaw" => (1.2f, -20f),
            "tepig" => (1f, -35f),
            "samurott" => (1.2f, 0f),
            "chespin" => (1f, -48f),
            "braixen" => (1.25f, -4f),
            "delphox" => (1.15f, 0f),
            "froakie" => (.95f, -48f),
            "frogadier" => (1.35f, 12f),
            "greninja" => (1f, 65f),
            "geodude" => (1f, 50f),
            "alolan-geodude" => (1.25f, 60f),
            "weedle" => (.55f, -8f),
            "kakuna" => (.85f, 0f),
            "beedrill" => (1f, 65f),
            "monferno" => (1.15f, 22f),
            "infernape" => (1f, -45f),
            "pidgey" => (1f, -35f),
            "pidgeotto" => (1.2f, -28f),
            "ampharos" => (1.55f, -14.5f),
            "banette" => (1.15f, -52.8f),
            "cacnea" => (0.7f, -2.1f),
            "camerupt" => (1.75f, -5.6f),
            "darkrai" => (1.4f, -22.1f),
            "donphan" => (0.58f, -51.7f),
            "eelektrik" => (0.85f, -36.6f),
            "flaaffy" => (1f, -31.4f),
            "forretress" => (0.75f, -46.8f),
            "gligar" => (0.9f, 66.3f),
            "gliscor" => (0.75f, 120f),
            "gulpin" => (1f, -25.4f),
            "houndoom" => (1.6f, -10f),
            "houndour" => (1f, -43.7f),
            "mareep" => (1f, -39.2f),
            "mightyena" => (0.65f, -117f),
            "numel" => (1.1f, -73.7f),
            "phanpy" => (1.7f, -80.8f),
            "pineco" => (1.5f, -50.9f),
            "poochyena" => (1f, -49.8f),
            "seviper" => (1.4f, -26.2f),
            "shroomish" => (1f, -25.4f),
            "slakoth" => (0.45f, 30f),
            "staraptor" => (1.4f, -20f),
            "starly" => (0.65f, -35f),
            "swalot" => (1.7f, -32.9f),
            "tynamo" => (0.3f, -55.1f),
            "vigoroth" => (1f, 169.6f),
            "yanma" => (0.85f, 145.2f),
            "yanmega" => (0.5f, 34.6f),
            "zangoose" => (1.4f, -52.7f),
            // Keep the Summary view centered and fully framed across idle poses.
            "victreebel" => (1.6f, 70f),
            "furret" => (0.55f, -20f),
            "spinarak" => (0.5f, -55f),
            "hoppip" => (1.5f, 80f),
            "skiploom" => (0.75f, 5f),
            "wurmple" => (0.23f, 10f),
            "silcoon" => (0.55f, -25f),
            "cascoon" => (0.65f, -30f),
            "masquerain" => (0.65f, 130f),
            "hariyama" => (0.9f, 90f),
            "meditite" => (0.65f, 0f),
            "volbeat" or "illumise" => (1.5f, 0f),
            "spoink" => (1f, 0f),
            "chingling" => (0.8f, -50f),
            "tropius" => (0.8f, 0f),
            "scatterbug" or "scatterbug-polar" or "scatterbug-tundra" or
            "scatterbug-continental" or "scatterbug-garden" or "scatterbug-elegant" or
            "scatterbug-meadow" or "scatterbug-modern" or "scatterbug-marine" or
            "scatterbug-archipelago" or "scatterbug-high-plains" or "scatterbug-sandstorm" or
            "scatterbug-river" or "scatterbug-monsoon" or "scatterbug-savanna" or
            "scatterbug-sun" or "scatterbug-ocean" or "scatterbug-jungle" or
            "scatterbug-fancy" or "scatterbug-poke-ball" => (0.5f, -45f),
            "spewpa" or "spewpa-polar" or "spewpa-tundra" or
            "spewpa-continental" or "spewpa-garden" or "spewpa-elegant" or
            "spewpa-meadow" or "spewpa-modern" or "spewpa-marine" or
            "spewpa-archipelago" or "spewpa-high-plains" or "spewpa-sandstorm" or
            "spewpa-river" or "spewpa-monsoon" or "spewpa-savanna" or
            "spewpa-sun" or "spewpa-ocean" or "spewpa-jungle" or
            "spewpa-fancy" or "spewpa-poke-ball" => (0.6f, -40f),
            "pyroar" => (1.9f, 40f),
            "flabebe" or "flabebe-yellow" or "flabebe-orange" or
            "flabebe-blue" or "flabebe-white" => (0.55f, -55f),
            "floette" or "floette-yellow" or "floette-orange" or
            "floette-blue" or "floette-white" or "floette-eternal" => (0.65f, -70f),
            "florges" or "florges-yellow" or "florges-orange" or
            "florges-blue" or "florges-white" => (1.4f, 45f),
            "gogoat" => (1.8f, 40f),
            "trumbeak" => (1f, 145f),
            "toucannon" => (1f, 160f),
            "yungoos" => (0.4f, 0f),
            "gumshoos" => (0.5f, -25f),
            "crabominable" => (1.5f, 0f),
            "minior" or "minior-meteor-orange" or "minior-meteor-yellow" or
            "minior-meteor-green" or "minior-meteor-blue" or "minior-meteor-indigo" or
            "minior-meteor-violet" or "minior-core-red" or "minior-core-orange" or
            "minior-core-yellow" or "minior-core-green" or "minior-core-blue" or
            "minior-core-indigo" or "minior-core-violet" => (1.25f, 100f),
            "bruxish" => (1f, 130f),
            _ => (1f, 0f),
        };
        if (adjustment == (1f, 0f) && definition != "raichu-mega-y") return configuration;
        var result = configuration.ToArray(); var data = new ModelBuffer(result);
        var scaleAt = data.Field(data.Root, 17); var verticalAt = data.Field(data.Root, 18);
        if (scaleAt == 0 || verticalAt == 0 || !float.IsFinite(data.Float(scaleAt)) || data.Float(scaleAt) <= 0
            || !float.IsFinite(data.Float(verticalAt)))
            throw new InvalidDataException("Summary framing configuration is incomplete or invalid.");
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(scaleAt, 4), data.Float(scaleAt) * adjustment.Item1);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(verticalAt, 4), data.Float(verticalAt) + adjustment.Item2);
        if (definition == "raichu-mega-y")
        {
            // The long tail clips the right edge at the source Summary angle.
            // View it from the opposite side, retaining native-comparable body size.
            foreach (var field in new[] { 21, 22 })
            {
                var angleAt = data.Field(data.Root, field);
                if (angleAt == 0 || !float.IsFinite(data.Float(angleAt)))
                    throw new InvalidDataException("Mega Raichu Y Summary angle is incomplete or invalid.");
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(angleAt, 4), field == 21 ? 20f : -20f);
            }
        }
        if (definition == "monferno")
        {
            // The inherited Summary angle hides the tail flame behind the head.
            // This view exposes the authored flame without moving its geometry.
            foreach (var field in new[] { 21, 22 })
            {
                var at = data.Field(data.Root, field);
                if (at == 0 || !float.IsFinite(data.Float(at)))
                    throw new InvalidDataException("Summary view angle is incomplete or invalid.");
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(at, 4), field == 21 ? 10f : -10f);
            }
        }
        return result;
    }

    internal static byte[] SummaryFields(byte[] configuration, byte[] reference)
    {
        var result = configuration.ToArray();
        var source = new ModelBuffer(reference); var target = new ModelBuffer(result);
        for (var field = 17; field <= 22; field++)
        {
            var from = source.Field(source.Root, field); var to = target.Field(target.Root, field);
            if (from == 0 || to == 0 || !float.IsFinite(source.Float(from)) || (field == 17 && source.Float(from) <= 0))
                throw new InvalidDataException("Summary framing reference is incomplete or invalid.");
            source.Slice(from, 4).CopyTo(result.AsSpan(to, 4));
        }
        return result;
    }

    internal static bool BattleEntry(IDictionary<string, byte[]> members, string modelFolder, string animationFolder, string stem)
    {
        var scene = new SwShPreviewReader(path => members[modelFolder + Path.GetFileName(path)]).Load(stem + ".gfbmdl");
        var recovery = SwShAnimationWriter.BattleEntryRecovery(members[animationFolder + stem + "_ba01_landC01.gfbanm"],
            members[animationFolder + stem + "_ba10_waitA01.gfbanm"], scene.Rig.Bones);
        if (recovery is null) return false;
        members[animationFolder + stem + "_battleentry01.gfbanm"] = recovery;
        return true;
    }

    internal static void MatchGiantGround(byte[] giantConfiguration, byte[] nativeConfiguration, byte[] convertedConfiguration)
    {
        var giant = new ModelBuffer(giantConfiguration);
        float Read(ModelBuffer data, int field)
        {
            var at = data.Field(data.Root, field);
            if (at == 0) throw new InvalidDataException("Giant ground placement metadata is incomplete.");
            var value = data.Float(at);
            if (!float.IsFinite(value)) throw new InvalidDataException("Giant ground placement metadata is nonfinite.");
            return value;
        }
        float Floor(byte[] configuration)
        {
            var data = new ModelBuffer(configuration);
            var height = Read(data, 8); var battleHeight = Read(data, 9);
            if (height <= 0 || battleHeight <= 0) throw new InvalidDataException("Giant ground placement height is invalid.");
            return Read(data, 12) * battleHeight / height;
        }
        var scale = Read(giant, 4);
        if (scale <= 0) throw new InvalidDataException("Giant ground placement scale is invalid.");
        // Ordinary flight clearance must not become a many-metre gap when enlarged.
        // Preserve the native actor's floor while retaining the imported pose and size.
        SetGiantVerticalOffset(giantConfiguration, Read(giant, 8)
            + (Floor(nativeConfiguration) - Floor(convertedConfiguration)) * scale);
    }

    internal static void SetGiantVerticalOffset(byte[] giantConfiguration, float offset)
    {
        var giant = new ModelBuffer(giantConfiguration);
        var offsetAt = giant.Field(giant.Root, 8);
        if (!float.IsFinite(offset) || (offsetAt == 0 && offset != 0))
            throw new InvalidDataException("Giant vertical placement configuration is invalid.");
        // Floating native actors can carry an offset that is inappropriate for the imported rig.
        if (offsetAt != 0)
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(giantConfiguration.AsSpan(offsetAt, 4), offset);
    }

    internal static void SetGiantScale(byte[] giantConfiguration, float scale)
    {
        var giant = new ModelBuffer(giantConfiguration);
        var scaleAt = giant.Field(giant.Root, 4);
        if (scaleAt == 0 || !float.IsFinite(scale) || scale <= 0)
            throw new InvalidDataException("Giant sizing configuration is invalid.");
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(giantConfiguration.AsSpan(scaleAt, 4), scale);
    }

    internal static void MatchGiantHeight(byte[] giantConfiguration, byte[] nativeConfiguration, float battleHeight)
    {
        var giant = new ModelBuffer(giantConfiguration); var native = new ModelBuffer(nativeConfiguration);
        var scaleAt = giant.Field(giant.Root, 4); var heightAt = native.Field(native.Root, 9);
        if (scaleAt == 0 || heightAt == 0)
            throw new InvalidDataException("Native giant sizing configuration is incomplete.");
        var scale = giant.Float(scaleAt); var nativeHeight = native.Float(heightAt);
        if (!float.IsFinite(scale) || !float.IsFinite(nativeHeight) || !float.IsFinite(battleHeight)
            || scale <= 0 || nativeHeight <= 0 || battleHeight <= 0)
            throw new InvalidDataException("Giant sizing configuration is invalid.");
        // Giant scale multiplies ordinary battle height. Preserve the native camera's
        // actor height without changing the imported model's ordinary dimensions.
        var fitted = scale * nativeHeight / battleHeight;
        if (!float.IsFinite(fitted) || fitted <= 0)
            throw new InvalidDataException("Converted giant sizing is invalid.");
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(giantConfiguration.AsSpan(scaleAt, 4), fitted);
    }

    internal static SwShConvertedGeometry IdleBounds(IReadOnlyDictionary<string, byte[]> members, string modelFolder,
        string animationFolder, string stem, SwShConvertedGeometry geometry, bool fullCycle = false)
    {
        // SwSh scales the actor using its posed idle height, including in the party screen.
        var scene = new SwShPreviewReader(path => members[modelFolder + Path.GetFileName(path)]).Load(stem + ".gfbmdl");
        var bones = scene.Rig.Bones;
        var clip = PreviewRigReader.Animation(new(members[animationFolder + stem + "_ba10_waitA01.gfbanm"]), stem, bones);
        var world = new Matrix4x4[bones.Length]; var skin = new Matrix4x4[bones.Length];
        var scales = new Vector3[bones.Length];
        static Vector3 Vector(float[] values) => new(values[0], values[1], values[2]);
        static float[] Sample(PreviewKey[]? keys, float[] fallback, int frame, bool rotation = false)
        {
            if (keys is not { Length: > 0 }) return fallback;
            if (frame <= keys[0].Frame) return keys[0].Value;
            if (frame >= keys[^1].Frame) return keys[^1].Value;
            var upper = Array.FindIndex(keys, key => key.Frame > frame);
            var a = keys[upper - 1]; var b = keys[upper];
            var fraction = (frame - a.Frame) / (b.Frame - a.Frame);
            if (rotation)
            {
                var q = Quaternion.Slerp(new(a.Value[0], a.Value[1], a.Value[2], a.Value[3]),
                    new(b.Value[0], b.Value[1], b.Value[2], b.Value[3]), fraction);
                return [q.X, q.Y, q.Z, q.W];
            }
            return a.Value.Zip(b.Value, (x, y) => x + (y - x) * fraction).ToArray();
        }
        var minimum = new Vector3(float.PositiveInfinity); var maximum = new Vector3(float.NegativeInfinity);
        // Spoink's first frame is compressed; its full authored bounce defines the camera envelope.
        for (var frame = 0; frame < (fullCycle ? clip.Frames : 1); frame++)
        {
            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i]; var track = clip.Tracks.SingleOrDefault(t => t.Bone == i);
                var scale = Sample(track?.Scale, bone.Scale, frame); var rotation = Sample(track?.Rotation, bone.Rotation, frame, true);
                if (scale.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Source idle scale is invalid.");
                scales[i] = Vector(scale);
                var local = Matrix4x4.CreateScale(scales[i])
                    * Matrix4x4.CreateFromQuaternion(new(rotation[0], rotation[1], rotation[2], rotation[3]));
                if (bone.CompensateScale && bone.Parent >= 0)
                {
                    float Inverse(float value) => MathF.Abs(value) > .000001f ? 1 / value : 0;
                    var parent = scales[bone.Parent];
                    local *= Matrix4x4.CreateScale(Inverse(parent.X), Inverse(parent.Y), Inverse(parent.Z));
                }
                local *= Matrix4x4.CreateTranslation(Vector(Sample(track?.Translation, bone.Translation, frame)));
                world[i] = bone.Parent < 0 ? local : local * world[bone.Parent];
                var b = bone.InverseBind;
                var inverse = new Matrix4x4(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7],
                    b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]);
                skin[i] = inverse * world[i];
            }
            foreach (var primitive in scene.Primitives)
                for (var vertex = 0; vertex < primitive.Vertices.Length; vertex += 16)
                {
                    var values = primitive.Vertices; var position = new Vector3(values[vertex], values[vertex + 1], values[vertex + 2]);
                    var posed = Vector3.Zero;
                    for (var influence = 0; influence < 4; influence++)
                        if (values[vertex + 12 + influence] > 0)
                            posed += Vector3.Transform(position, skin[(int)values[vertex + 8 + influence]]) * values[vertex + 12 + influence];
                    minimum = Vector3.Min(minimum, posed); maximum = Vector3.Max(maximum, posed);
                }
        }
        if (!float.IsFinite(minimum.X + minimum.Y + minimum.Z + maximum.X + maximum.Y + maximum.Z) || maximum.Y <= minimum.Y)
            throw new InvalidDataException("Source idle framing is invalid.");
        return geometry with { Minimum = minimum, Maximum = maximum };
    }
}
