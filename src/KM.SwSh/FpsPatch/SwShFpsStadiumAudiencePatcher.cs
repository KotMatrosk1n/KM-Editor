// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.SwSh.FpsPatch;

internal static partial class SwShFpsStadiumAudiencePatcher
{
    private const string InvalidAnimation = "60FPS Patch found unsupported stadium animation data.";

    internal static byte[] ConvertArchive(string relativePath, byte[] source, bool includeMonitors = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            Require(FieldArchives.ContainsKey(relativePath));
            var archive = SwShGfPackFile.Parse(source);
            var clips = FieldArchives[relativePath].AsEnumerable();
            if (includeMonitors && MonitorClips.TryGetValue(relativePath, out var monitors))
                clips = clips.Concat(monitors);
            foreach (var clip in clips)
            {
                Require(archive.TryGetUniqueFileHash(clip.Name, out var hash));
                var animation = archive.ReadFileByHash(hash, 4 * 1024 * 1024);
                archive.ReplaceFileByHash(hash, ConvertAnimation(animation, clip));
            }
            return archive.Write();
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException
            or ArgumentOutOfRangeException or System.Text.DecoderFallbackException)
        {
            throw new InvalidDataException(InvalidAnimation, exception);
        }
    }

    private static byte[] ConvertAnimation(byte[] source, ClipDefinition clip)
    {
        var sourceFrames = clip.Frames;
        var targetFrames = checked((sourceFrames - 1) * 2 + 1);
        var data = new ModelBuffer(source);
        var info = data.Table(data.Root, 0);
        Require(info != 0 && data.Value(info, 1) == sourceFrames && data.Value(info, 2) == 30);
        Require(data.Field(data.Root, 4) == 0);
        var output = new List<byte>(source);
        var references = new Dictionary<int, int>();
        var expandedVectors = new Dictionary<(int Start, bool Smooth), int>();
        var sparseVectors = new Dictionary<(int Start, int Width), int>();
        var foundMaterials = new HashSet<string>(StringComparer.Ordinal);
        var foundSmooth = new HashSet<(string Material, string Parameter)>();
        var foundDiscrete = new HashSet<(string Material, string Parameter)>();

        void PointTo(int field, int vector)
        {
            Require(field != 0 && vector > field);
            if (references.TryGetValue(field, out var previous)) Require(previous == vector);
            references[field] = vector;
        }

        void RetimeSparse(int track, int kindField, int valueWidth)
        {
            var kindAt = data.Field(track, kindField);
            var kind = kindAt == 0 ? 0 : data.U8(kindAt);
            if (kind <= 1) return;
            Require(kind is 3 or 4);
            var value = data.Table(track, kindField + 1);
            Require(value != 0);
            var width = kind == 3 ? 2 : 1;
            var (start, count) = data.Vector(value, 0, width, sourceFrames);
            var (_, valueCount) = data.Vector(value, 1, valueWidth, sourceFrames);
            Require(count > 0 && valueCount == count);
            if (!sparseVectors.TryGetValue((start, width), out var newVector))
            {
                newVector = BeginVector(output, count);
                var previous = -1;
                for (var index = 0; index < count; index++)
                {
                    var frame = width == 2 ? data.U16(start + index * 2) : data.U8(start + index);
                    Require(frame < sourceFrames && frame >= previous);
                    previous = frame;
                    var scaled = checked((ushort)(frame * 2));
                    output.Add((byte)scaled);
                    output.Add((byte)(scaled >> 8));
                }
                sparseVectors.Add((start, width), newVector);
            }
            // Preserve every original value, including packed rotations. Only key
            // positions change, so fog, signs, and lights retain their elapsed timing.
            output[kindAt] = 3;
            PointTo(data.Field(value, 0), newVector);
        }

        var visibility = data.Table(data.Root, 3);
        if (visibility != 0)
            foreach (var track in data.Tables(visibility, 0, 512))
            {
                var kindAt = data.Field(track, 1);
                // These clips have constant visibility. Preserve their crowd on/off
                // state and reject other encodings until their timing is supported.
                Require(kindAt == 0 || data.U8(kindAt) <= 1);
            }

        var skeleton = data.Table(data.Root, 1);
        if (skeleton != 0)
            foreach (var bone in data.Tables(skeleton, 0, 512))
            {
                RetimeSparse(bone, 1, 12);
                RetimeSparse(bone, 3, 6);
                RetimeSparse(bone, 5, 12);
            }

        var materials = data.Table(data.Root, 2);
        Require(materials != 0);
        foreach (var material in data.Tables(materials, 0, 128))
        {
            var name = data.Text(material, 0) ?? string.Empty;
            Require(data.Tables(material, 1, 256).Length == 0);
            foreach (var vector in data.Tables(material, 3, 256))
                RetimeSparse(vector, 1, 12);
            foreach (var scalar in data.Tables(material, 2, 256))
            {
                var kindAt = data.Field(scalar, 1);
                var kind = kindAt == 0 ? 0 : data.U8(kindAt);
                var parameter = data.Text(scalar, 0) ?? string.Empty;
                if (clip.DiscreteParameters.Contains((name, parameter)))
                {
                    Require(foundDiscrete.Add((name, parameter)) && kind is 3 or 4);
                    var table = ExpandMonitorTrack(data, scalar, kind, sourceFrames, targetFrames, output);
                    output[kindAt] = 2;
                    PointTo(data.Field(scalar, 2), table);
                    continue;
                }
                if (kind != 2)
                {
                    RetimeSparse(scalar, 1, 4);
                    continue;
                }

                var smooth = clip.SmoothParameters.Contains((name, parameter));
                Require(smooth ? foundSmooth.Add((name, parameter))
                    : clip.FlipbookMaterials.Contains(name, StringComparer.Ordinal)
                        && parameter == "TranslateV0" && foundMaterials.Add(name));
                var value = data.Table(scalar, 2);
                Require(value != 0);
                var (start, count) = data.Vector(value, 0, 4, sourceFrames);
                Require(count == sourceFrames);
                if (!expandedVectors.TryGetValue((start, smooth), out var newVector))
                {
                    newVector = BeginVector(output, targetFrames);
                    for (var index = 0; index < count; index++)
                    {
                        Require(float.IsFinite(data.Float(start + index * 4)));
                        var bytes = data.Slice(start + index * 4, 4).ToArray();
                        output.AddRange(bytes);
                        if (index < count - 1)
                        {
                            if (smooth)
                            {
                                var next = data.Float(start + (index + 1) * 4);
                                Require(float.IsFinite(next));
                                // Continuous UV motion keeps its original linear curve.
                                var halfway = (float)(((double)data.Float(start + index * 4) + next) / 2);
                                BinaryPrimitives.WriteSingleLittleEndian(bytes, halfway);
                            }
                            output.AddRange(bytes);
                        }
                    }
                    expandedVectors.Add((start, smooth), newVector);
                }
                PointTo(data.Field(value, 0), newVector);
            }
        }
        Require(foundMaterials.SetEquals(clip.FlipbookMaterials) && foundSmooth.SetEquals(clip.SmoothParameters)
            && foundDiscrete.SetEquals(clip.DiscreteParameters));
        var result = output.ToArray();
        foreach (var (field, vector) in references)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(field, 4), checked((uint)(vector - field)));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(data.Field(info, 1), 4), checked((uint)targetFrames));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(data.Field(info, 2), 4), 60);
        return result;
    }

    private static int BeginVector(List<byte> output, int count)
    {
        while (output.Count % 4 != 0) output.Add(0);
        var start = output.Count;
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        output.AddRange(bytes);
        return start;
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidDataException(InvalidAnimation);
    }
}
