// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats.SwSh;

/// <summary>Updates an identified Critter without relocating its table or nested references.</summary>
public static class SwShPlacementSpeciesWriter
{
    private static readonly int[] Widths = [4, 4, 4, 4, 4, 4, 4, 8, 8, 8, 4, 4, 4, 4, 4, 1];

    public static byte[] Write(byte[] source, ulong actor, uint species, uint form)
    {
        if (species is 0 or > ushort.MaxValue || form > byte.MaxValue) throw new InvalidDataException("Invalid placement species or form.");
        var archive = SwShPlacementZoneArchive.Parse(source);
        var matches = archive.Zones.SelectMany(z => z.RawObjects).Where(o => o.ObjectType == "Critter"
            && o.Fields.Any(f => f.Field == "raw.Critter.Field_01.Field_00.Field_00.HashObjectName"
                && f.Value.Equals($"0x{actor:X16}", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Starter placement actor is missing or ambiguous.");
        var fields = matches[0].Fields;
        var speciesField = fields.Single(f => f.Field == "raw.Critter.Species");
        var formField = fields.Single(f => f.Field == "raw.Critter.Form");
        var table = speciesField.TableOffset;
        if (speciesField.ValueOffset <= 0 || table <= 0
            || archive.Zones.SelectMany(z => z.RawObjects).Count(o => o.Fields.Any(f => f.Field == "raw.Critter.Species" && f.TableOffset == table)) != 1)
            throw new InvalidDataException("Starter placement identity has no exclusive writable table.");
        var output = source.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(speciesField.ValueOffset), species);
        var formOffset = formField.ValueOffset;
        if (formOffset == 0 && form != 0)
        {
            var vtable = table - BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(table));
            var length = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vtable));
            var size = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vtable + 2));
            if (length > 4 + Widths.Length * 2 || length < 12 || table > source.Length - size)
                throw new InvalidDataException("Starter placement form layout is unsupported.");
            var used = new bool[size]; Array.Fill(used, true, 0, 4);
            for (var i = 0; i < (length - 4) / 2; i++)
            {
                var offset = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vtable + 4 + i * 2));
                if (offset == 0) continue;
                if (offset < 4 || offset + Widths[i] > size) throw new InvalidDataException("Placement table field extends outside its storage.");
                Array.Fill(used, true, offset, Widths[i]);
            }
            var storage = Enumerable.Range(4, Math.Max(0, size - 7)).FirstOrDefault(offset => (table + offset) % 4 == 0
                && Enumerable.Range(offset, 4).All(at => !used[at] && source[table + at] == 0));
            if (storage == 0) throw new InvalidDataException("Starter placement has no verified storage for a nonzero form.");
            // A private vtable exposes verified unused scalar storage; shared vtables and all references remain intact.
            var appended = (output.Length + 1) & ~1;
            Array.Resize(ref output, appended + length);
            source.AsSpan(vtable, length).CopyTo(output.AsSpan(appended));
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(appended + 10), checked((ushort)storage));
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(table), table - appended);
            formOffset = table + storage;
        }
        if (formOffset != 0) BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(formOffset), form);
        var verified = SwShPlacementZoneArchive.Parse(output).Zones.SelectMany(z => z.RawObjects)
            .Single(o => o.ObjectType == "Critter" && o.Fields.Any(f => f.Field == "raw.Critter.Species" && f.TableOffset == table));
        if (verified.Fields.Single(f => f.Field == "raw.Critter.Species").Value != species.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || verified.Fields.Single(f => f.Field == "raw.Critter.Form").Value != form.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidDataException("Starter placement identity failed verification.");
        return output;
    }
}
