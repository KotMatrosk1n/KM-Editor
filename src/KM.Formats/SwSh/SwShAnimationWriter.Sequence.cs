// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Formats.SwSh;

public static partial class SwShAnimationWriter
{
    /// <summary>Retains authored skeletal, UV and mesh visibility segments of a battle action.</summary>
    public static byte[] SkeletalSequence(IReadOnlyList<byte[]> segments, PreviewBone[] bones, bool layeredEyes = false)
    {
        if (segments.Count is < 2 or > 8 || bones.Length is < 1 or > 255)
            throw new InvalidDataException("Battle sequence inputs are invalid.");
        if (layeredEyes) return EyeMaterialSequence(segments, bones);
        var sources = segments.Select(bytes => new ModelBuffer(bytes)).ToArray();
        var clips = sources.Select(source =>
        {
            var warnings = new List<string>();
            var clip = SwShPreviewAnimation.Read(source, PreviewRigReader.Animation(source, "segment", bones), warnings);
            if (warnings.Count > 0 || clip.Materials.Any(t => t.Parameter != "UVScaleOffset") || source.Table(source.Root, 4) != 0)
                throw new InvalidDataException("Battle sequence material channels need another adapter.");
            return clip;
        }).ToArray();
        var first = clips[0];
        if (clips.Any(clip => clip.Rate != first.Rate) || first.Loop || clips[^1].Loop)
            throw new InvalidDataException("Battle sequence timing is incompatible.");
        var frames = clips.Sum(clip => clip.Frames);
        if (frames > 18000) throw new InvalidDataException("Battle sequence exceeds its frame budget.");
        var meshNames = first.Visibility.Select(track => track.Name).ToHashSet(StringComparer.Ordinal);
        if (meshNames.Count != first.Visibility.Length || clips.Any(clip => clip.Visibility.Length != meshNames.Count
            || clip.Visibility.Select(track => track.Name).Distinct(StringComparer.Ordinal).Count() != meshNames.Count
            || clip.Visibility.Any(track => !meshNames.Contains(track.Name))))
            throw new InvalidDataException("Battle sequence visibility targets differ.");
        var visibility = first.Visibility.Select(track =>
        {
            var keys = new List<PreviewKey>(); var offset = 0;
            foreach (var clip in clips)
            {
                var part = clip.Visibility.Single(candidate => candidate.Name == track.Name);
                ValidateKeys(part.Keys);
                if (part.Keys.Length == 0 || part.Keys[0].Frame != 0
                    || part.Keys.Any(key => key.Frame >= clip.Frames || key.Value.Length != 1 || key.Value[0] is not (0 or 1)))
                    throw new InvalidDataException("Battle sequence visibility samples are invalid.");
                var keyIndex = 0;
                for (var frame = 0; frame < clip.Frames; frame++)
                {
                    while (keyIndex + 1 < part.Keys.Length && part.Keys[keyIndex + 1].Frame <= frame) keyIndex++;
                    var value = part.Keys[keyIndex].Value[0];
                    if (keys.Count == 0 || keys[^1].Value[0] != value) keys.Add(new(offset + frame, [value]));
                }
                offset += clip.Frames;
            }
            return track with { Keys = keys.ToArray() };
        }).ToArray();
        var materials = first.Materials.Select(track =>
        {
            var parts = clips.Select(clip => clip.Materials.SingleOrDefault(t => t.Material == track.Material && t.Parameter == track.Parameter)
                ?? throw new InvalidDataException("Battle sequence material channels are incomplete.")).ToArray();
            if (parts.Any(part => part.Channels.Length != 4))
                throw new InvalidDataException("Battle sequence material channel count is unsupported.");
            var channels = Enumerable.Range(0, 4).Select(channel =>
            {
                var populated = parts[0].Channels[channel].Length != 0;
                if (parts.Any(part => (part.Channels[channel].Length != 0) != populated))
                    throw new InvalidDataException("Battle sequence material defaults need another adapter.");
                if (!populated) return Array.Empty<PreviewKey>();
                var offset = 0; var keys = new List<PreviewKey>();
                for (var segment = 0; segment < parts.Length; segment++)
                {
                    var sourceKeys = parts[segment].Channels[channel]; ValidateKeys(sourceKeys);
                    for (var frame = 0; frame < clips[segment].Frames; frame++)
                        keys.Add(new(offset + frame, [Interpolate(sourceKeys, frame)]));
                    offset += clips[segment].Frames;
                }
                return keys.ToArray();
            }).ToArray();
            return track with { Channels = channels };
        }).ToArray();
        if (clips.Any(clip => clip.Materials.Length != materials.Length))
            throw new InvalidDataException("Battle sequence material targets differ.");
        var writer = new ModelTableBuilder(segments[0]);
        writer.Int32(sources[0].Field(sources[0].Table(sources[0].Root, 0), 1), frames);
        var skeleton = writer.Table(2, absent: sources[0].Table(sources[0].Table(sources[0].Root, 1), 1) == 0 ? new HashSet<int> { 1 } : null);
        writer.Reference(sources[0].Root, 1, skeleton);
        var slots = writer.Vector(skeleton, 0, bones.Length);
        var targets = slots.Select(_ => writer.Table(7)).ToArray();
        for (var i = 0; i < slots.Length; i++) writer.Pointer(slots[i], targets[i]);
        var copy = writer.Append(segments[0]);
        var initial = sources[0].Table(sources[0].Table(sources[0].Root, 1), 1);
        if (initial != 0) writer.Reference(skeleton, 1, copy + initial);
        var tracks = sources.Select(source => source.Tables(source.Table(source.Root, 1), 0, 4096)
            .ToDictionary(track => source.Text(track, 0) ?? throw new InvalidDataException("Battle sequence track is unnamed."), StringComparer.Ordinal)).ToArray();
        for (var bone = 0; bone < bones.Length; bone++)
        {
            writer.Text(targets[bone], 0, bones[bone].Name);
            foreach (var field in new[] { 1, 3, 5 })
            {
                var stride = field == 3 ? 6 : 12; var values = new List<byte>(); var indices = new List<ushort>(); var offset = 0;
                for (var segment = 0; segment < sources.Length; segment++)
                {
                    var source = sources[segment]; var track = tracks[segment].GetValueOrDefault(bones[bone].Name);
                    var at = track == 0 ? 0 : source.Field(track, field); var kind = at == 0 ? 0 : source.U8(at);
                    if (kind > 4) throw new InvalidDataException("Battle sequence curve encoding is unsupported.");
                    if (kind == 0)
                    {
                        var fallback = field == 1 ? bones[bone].Scale : field == 3 ? bones[bone].Rotation : bones[bone].Translation;
                        indices.Add(checked((ushort)offset)); values.AddRange(field == 3 ? PackRotation(fallback) : ModelTableBuilder.Floats(fallback));
                    }
                    else
                    {
                        var curve = source.Table(track, field + 1);
                        var (start, count) = kind == 1 ? (source.Field(curve, 0), 1) : source.Vector(curve, kind == 2 ? 0 : 1, stride, 18000);
                        var (keys, keyCount) = kind >= 3 ? source.Vector(curve, 0, kind == 3 ? 2 : 1, 18000) : (0, count);
                        if (count < 1 || count != keyCount) throw new InvalidDataException("Battle sequence keys are incomplete.");
                        for (var i = 0; i < count; i++)
                        {
                            var frame = kind == 1 ? 0 : kind == 2 ? i : kind == 3 ? source.U16(keys + i * 2) : source.U8(keys + i);
                            indices.Add(checked((ushort)(offset + frame))); values.AddRange(source.Slice(start + i * stride, stride).ToArray());
                        }
                    }
                    var end = offset + clips[segment].Frames - 1;
                    if (indices[^1] < end) { indices.Add(checked((ushort)end)); values.AddRange(values.TakeLast(stride).ToArray()); }
                    offset = end + 1;
                }
                writer.Int32(writer.Field(targets[bone], field), 3);
                var replacement = writer.Table(2); writer.Reference(targets[bone], field + 1, replacement);
                writer.Blob(replacement, 0, indices.SelectMany(BitConverter.GetBytes).ToArray(), indices.Count);
                writer.Blob(replacement, 1, values.ToArray(), indices.Count);
            }
        }
        Materials(writer, sources[0].Root, materials, frames, false);
        var result = writer.Finish(sources[0].Root);
        if (visibility.Length > 0) result = Visibility(result, visibility, meshNames);
        var warnings = new List<string>();
        var verified = SwShPreviewAnimation.Read(new(result), PreviewRigReader.Animation(new(result), "sequence", bones), warnings);
        bool SameVisibility(PreviewVisibility expected, PreviewVisibility actual)
        {
            if (actual.Keys.Length == 0 || actual.Keys[0].Frame != 0) return false;
            var expectedIndex = 0; var actualIndex = 0;
            for (var frame = 0; frame < frames; frame++)
            {
                while (expectedIndex + 1 < expected.Keys.Length && expected.Keys[expectedIndex + 1].Frame <= frame) expectedIndex++;
                while (actualIndex + 1 < actual.Keys.Length && actual.Keys[actualIndex + 1].Frame <= frame) actualIndex++;
                if (actual.Keys[actualIndex].Value.Length != 1 || actual.Keys[actualIndex].Value[0] != expected.Keys[expectedIndex].Value[0]) return false;
            }
            return true;
        }
        if (verified.Frames != frames || verified.Loop || warnings.Count != 0 || verified.Materials.Length != materials.Length
            || verified.Visibility.Length != visibility.Length || visibility.Any(track => !verified.Visibility.Any(actual => actual.Name == track.Name
                && SameVisibility(track, actual)))
            || materials.Any(track => !verified.Materials.Any(actual => actual.Material == track.Material
                && actual.Parameter == track.Parameter && Enumerable.Range(0, 4).All(channel =>
                    track.Channels[channel].Length == 0 ? actual.Channels[channel].Length == 0
                        : actual.Channels[channel].Length > 0 && track.Channels[channel].All(key =>
                            Interpolate(actual.Channels[channel], key.Frame) == key.Value[0])))))
            throw new InvalidDataException("Battle sequence failed timing or material verification.");
        return result;
    }
}
