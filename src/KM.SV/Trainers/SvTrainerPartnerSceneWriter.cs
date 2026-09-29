// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.SV.Trainers;

internal static class SvTrainerPartnerSceneWriter
{
    public static byte[] WriteSpecies(byte[] source, string actorName, ushort species)
    {
        var result = source.ToArray();
        var reader = new SceneReader(result);
        var actors = 0;
        var fieldComponents = 0;
        var modelComponents = 0;
        var visited = new HashSet<int>();
        var payloads = new List<SceneReader>();
        var identityFields = new List<(SceneReader Payload, int Offset)>();
        foreach (var entry in reader.Tables(reader.Root, 4))
        {
            Walk(entry, false, 0);
        }

        if (actors != 1 || fieldComponents != 1 || modelComponents > 1)
        {
            throw new InvalidDataException("The trainer partner scene must contain one named actor and one field Pokemon identity.");
        }

        foreach (var (payload, offset) in identityFields)
        {
            if (payloads.Any(other => !ReferenceEquals(other, payload)
                && offset < other.Origin + other.Length && offset + 2 > other.Origin))
            {
                throw new InvalidDataException("The trainer partner identity shares storage with another component.");
            }

            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset, 2), species);
        }

        return result;

        void Walk(int entry, bool isPartner, int depth)
        {
            if (depth > 64 || visited.Count >= 100_000 || !visited.Add(entry))
            {
                throw new InvalidDataException("The trainer partner scene has an unsupported object hierarchy.");
            }

            var type = reader.String(entry, 0);
            var payload = reader.Payload(entry, 1);
            payloads.Add(payload);
            if (type == "trinity_SceneObject")
            {
                isPartner = payload.String(payload.Root, 0) == actorName;
                if (isPartner)
                {
                    actors++;
                }
            }

            if (isPartner && type is "ti_FieldPokemonComponent" or "ti_PokemonModelComponent")
            {
                identityFields.Add((payload, payload.SpeciesOffset()));
                if (type == "ti_FieldPokemonComponent")
                {
                    fieldComponents++;
                }
                else
                {
                    modelComponents++;
                }
            }

            foreach (var child in reader.Tables(entry, 2))
            {
                Walk(child, type == "trinity_SceneObject" && isPartner, depth + 1);
            }
        }
    }

    private sealed class SceneReader(Memory<byte> data, int origin = 0)
    {
        private static readonly UTF8Encoding Utf8 = new(false, true);

        public int Root => Indirect(0);
        public int Origin => origin;
        public int Length => data.Length;

        public string String(int table, int index)
        {
            var field = Field(table, index);
            if (field is null)
            {
                return string.Empty;
            }

            var start = Indirect(field.Value);
            var length = Int32(start);
            return Utf8.GetString(Slice(checked(start + 4), length).Span);
        }

        public int[] Tables(int table, int index)
        {
            var field = Field(table, index);
            if (field is null)
            {
                return [];
            }

            var start = Indirect(field.Value);
            var count = Int32(start);
            if (count < 0 || count > 100_000)
            {
                throw new InvalidDataException("The trainer partner scene has an invalid object count.");
            }

            Slice(checked(start + 4), checked(count * 4));
            return Enumerable.Range(0, count).Select(i => Indirect(checked(start + 4 + i * 4))).ToArray();
        }

        public SceneReader Payload(int table, int index)
        {
            var field = Field(table, index)
                ?? throw new InvalidDataException("The trainer partner scene is missing component data.");
            var start = Indirect(field);
            return new SceneReader(Slice(checked(start + 4), Int32(start)), checked(origin + start + 4));
        }

        public int SpeciesOffset()
        {
            var field = Field(Root, 0, 2)
                ?? throw new InvalidDataException("The trainer partner component is missing its species field.");
            Slice(field, 2);
            return checked(origin + field);
        }

        private int? Field(int table, int index, int width = 4)
        {
            var vtable = checked(table - Int32(table));
            var size = UInt16(vtable);
            var objectSize = UInt16(checked(vtable + 2));
            if (size < 4 || size % 2 != 0 || objectSize < 4)
            {
                throw new InvalidDataException("The trainer partner scene contains an invalid table.");
            }

            Slice(vtable, size);
            Slice(table, objectSize);
            var slot = checked(4 + index * 2);
            var offset = slot < size ? UInt16(checked(vtable + slot)) : 0;
            if (offset == 0)
            {
                return null;
            }

            if (offset < 4 || offset > objectSize - width)
            {
                throw new InvalidDataException("The trainer partner scene contains an invalid field.");
            }

            return checked(table + offset);
        }

        private int Indirect(int position)
        {
            var offset = Int32(position);
            if (offset <= 0)
            {
                throw new InvalidDataException("The trainer partner scene contains an invalid reference.");
            }

            var target = checked(position + offset);
            Slice(target, 4);
            return target;
        }

        private int Int32(int position) => BinaryPrimitives.ReadInt32LittleEndian(Slice(position, 4).Span);
        private ushort UInt16(int position) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(position, 2).Span);

        private Memory<byte> Slice(int start, int length)
        {
            if (start < 0 || length < 0 || start > data.Length - length)
            {
                throw new InvalidDataException("The trainer partner scene is truncated.");
            }

            return data.Slice(start, length);
        }
    }
}
