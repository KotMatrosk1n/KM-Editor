// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Text.Json.Nodes;
using KM.Core.ModMerging;
using KM.Formats.SwSh;

namespace KM.Tools.ModMerging;

internal static class MergeFixedRecords
{
    private sealed record Field(string Name, int Offset, int Size, ulong Mask = 0, int Shift = 0);

    internal static MergeDocument? Read(string game, string path, byte[] bytes)
    {
        if (MergeInputs.Family(game) != "swsh") return null;
        if (path.StartsWith(SwShTrainerDataFile.TrainerDataRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            _ = SwShTrainerDataFile.Parse(bytes);
            return Create(bytes, 20, [new("ClassId", 0, 2), new("BattleMode", 2, 1), new("PokemonCount", 3, 1),
                new("Item1", 4, 2), new("Item2", 6, 2), new("Item3", 8, 2), new("Item4", 10, 2),
                .. Enumerable.Range(0, 32).Select(bit => new Field("AiFlag" + bit, 12, 4, 1UL << bit, bit)),
                new("Heal", 16, 1), new("Money", 17, 1), new("Gift", 18, 2)], "trainers");
        }
        if (path.StartsWith(SwShTrainerClassFile.TrainerClassRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            _ = SwShTrainerClassFile.Parse(bytes);
            return Create(bytes, SwShTrainerClassFile.Size, [new("Group", 1, 1), new("BallId", 2, 1)], "trainer-classes");
        }
        if (path.StartsWith(SwShTrainerTeamFile.TrainerPokeRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            _ = SwShTrainerTeamFile.Parse(bytes);
            return Create(bytes, 32, [new("Gender", 0, 1, 3), new("Ability", 0, 1, 0x30, 4), new("Nature", 1, 1),
                new("EvHp", 2, 1), new("EvAttack", 3, 1), new("EvDefense", 4, 1), new("EvSpecialAttack", 5, 1), new("EvSpecialDefense", 6, 1), new("EvSpeed", 7, 1),
                new("DynamaxLevel", 8, 1), new("CanGigantamax", 9, 1), new("Level", 10, 2), new("Species", 12, 2), new("Form", 14, 2), new("HeldItem", 16, 2),
                new("Move1", 18, 2), new("Move2", 20, 2), new("Move3", 22, 2), new("Move4", 24, 2),
                .. new[] { "Hp", "Attack", "Defense", "Speed", "SpecialAttack", "SpecialDefense" }.Select((name, index) => new Field("Iv" + name, 28, 4, 31UL << (index * 5), index * 5)),
                new("Shiny", 28, 4, 1UL << 30, 30), new("CanDynamax", 28, 4, 1UL << 31, 31)], "trainers");
        }
        return null;
    }

    private static MergeDocument Create(byte[] bytes, int rowSize, Field[] fields, string kind)
    {
        if (bytes.Length % rowSize != 0 || bytes.Length / rowSize > 50_000) throw new InvalidDataException("Invalid fixed record layout.");
        var masks = new byte[rowSize];
        foreach (var field in fields)
        {
            var mask = Mask(field);
            for (var part = 0; part < field.Size; part++) masks[field.Offset + part] |= (byte)(mask >> (part * 8));
        }
        var content = new JsonObject();
        for (var offset = 0; offset < bytes.Length; offset += rowSize)
        {
            var row = new JsonObject();
            foreach (var field in fields) row[field.Name] = (ReadInteger(bytes.AsSpan(offset + field.Offset, field.Size)) & Mask(field)) >> field.Shift;
            row["UnrecognizedFields"] = Convert.ToBase64String(Enumerable.Range(0, rowSize).Select(index => (byte)(bytes[offset + index] & ~masks[index])).ToArray());
            content[(offset / rowSize).ToString("D5")] = row;
        }
        return new(content, node =>
        {
            var rows = node.AsObject().OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            if (rows.Length != bytes.Length / rowSize) throw new InvalidDataException("Fixed record count changed.");
            var output = new byte[bytes.Length];
            for (var index = 0; index < rows.Length; index++)
            {
                if (rows[index].Key != index.ToString("D5")) throw new InvalidDataException("Fixed record identity changed.");
                var row = rows[index].Value!.AsObject();
                if (row.Count != fields.Length + 1) throw new InvalidDataException("Fixed record fields changed.");
                var residual = Convert.FromBase64String(row["UnrecognizedFields"]!.GetValue<string>());
                if (residual.Length != rowSize) throw new InvalidDataException("Fixed residual size changed.");
                for (var part = 0; part < rowSize; part++) output[index * rowSize + part] = (byte)(residual[part] & ~masks[part]);
                foreach (var field in fields)
                {
                    var value = row[field.Name]!.GetValue<ulong>();
                    if (value > (Mask(field) >> field.Shift)) throw new InvalidDataException("Merged field exceeds its storage range.");
                    value <<= field.Shift;
                    for (var part = 0; part < field.Size; part++) output[index * rowSize + field.Offset + part] |= (byte)(value >> (part * 8));
                }
            }
            return output;
        }, kind, $"fixed:{rowSize}:{bytes.Length}");
    }

    private static ulong Mask(Field field) => field.Mask != 0 ? field.Mask : field.Size == 8 ? ulong.MaxValue : (1UL << (field.Size * 8)) - 1;
    private static ulong ReadInteger(ReadOnlySpan<byte> bytes) => bytes.Length switch
    {
        1 => bytes[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes), 4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
        8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes), _ => throw new InvalidDataException("Unknown fixed field width."),
    };
}
