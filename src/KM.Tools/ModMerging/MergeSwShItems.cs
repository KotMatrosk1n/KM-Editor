// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using KM.Core.ModMerging;
using KM.Formats.SwSh;

namespace KM.Tools.ModMerging;

internal static class MergeSwShItems
{
    private sealed record Field(string Name, int Offset, int Size = 1, bool Signed = false, int Mask = 255, int Shift = 0);
    private static readonly Field[] Fields = CreateFields();

    public static MergeDocument Read(byte[] source)
    {
        _ = SwShItemTable.Parse(source, 50_000);
        var rowCount = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(4));
        var rowsStart = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(0x40));
        var machinePointer = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(2));
        var machinesStart = machinePointer == 0 ? -1 : 0x44 + machinePointer * 2;
        var metadata = source.ToArray();
        var rows = new JsonObject();
        for (var index = 0; index < rowCount; index++)
        {
            var row = new JsonObject();
            foreach (var field in Fields)
            {
                var position = rowsStart + index * 0x30 + field.Offset;
                row[field.Name] = ReadNumber(source, position, field);
                WriteNumber(metadata, position, field, 0);
            }
            rows[index.ToString("D5")] = row;
        }
        var machines = new JsonObject();
        if (machinesStart >= 0)
            for (var index = 0; index < 200; index++)
            {
                var offset = machinesStart + index * 4;
                machines[index.ToString("D3")] = new JsonObject {
                    ["ItemId"] = (int)BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset)),
                    ["MoveId"] = (int)BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset + 2)),
                };
                metadata.AsSpan(offset, 4).Clear();
            }
        var content = new JsonObject { ["Rows"] = rows, ["Machines"] = machines, ["UnrecognizedFields"] = Convert.ToBase64String(metadata) };
        var itemCount = BinaryPrimitives.ReadUInt16LittleEndian(source);
        var identity = Convert.ToHexStringLower(SHA256.HashData(source.AsSpan(0, 0x44 + itemCount * 2)));
        return new(content, node =>
        {
            var result = Convert.FromBase64String(node["UnrecognizedFields"]!.GetValue<string>());
            if (result.Length != source.Length || node["Rows"]!.AsObject().Count != rowCount || node["Machines"]!.AsObject().Count != machines.Count)
                throw new InvalidDataException("Item table layout changed.");
            for (var index = 0; index < rowCount; index++)
                foreach (var field in Fields)
                    WriteNumber(result, rowsStart + index * 0x30 + field.Offset, field,
                        node["Rows"]![index.ToString("D5")]![field.Name]!.GetValue<long>());
            if (machinesStart >= 0)
                for (var index = 0; index < 200; index++)
                {
                    var row = node["Machines"]![index.ToString("D3")]!;
                    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(machinesStart + index * 4), checked((ushort)row["ItemId"]!.GetValue<int>()));
                    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(machinesStart + index * 4 + 2), checked((ushort)row["MoveId"]!.GetValue<int>()));
                }
            _ = SwShItemTable.Parse(result, 50_000);
            return result;
        }, "items", identity);
    }

    private static long ReadNumber(byte[] bytes, int offset, Field field) => field.Size switch
    {
        4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)),
        2 => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset)),
        _ => field.Signed ? (sbyte)bytes[offset] : (bytes[offset] >> field.Shift) & field.Mask,
    };

    private static void WriteNumber(byte[] bytes, int offset, Field field, long value)
    {
        if (field.Size == 4) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), checked((uint)value));
        else if (field.Size == 2) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), checked((short)value));
        else if (field.Signed) bytes[offset] = unchecked((byte)checked((sbyte)value));
        else
        {
            if (value < 0 || value > field.Mask) throw new InvalidDataException("Item field is outside its storage range.");
            bytes[offset] = (byte)((bytes[offset] & ~(field.Mask << field.Shift)) | ((int)value << field.Shift));
        }
    }

    private static Field[] CreateFields()
    {
        var fields = new List<Field> {
            new("BuyPrice", 0, 4), new("WattsPrice", 4, 4), new("AlternatePrice", 8, 4),
            new("Pouch", 0x11, Mask: 15), new("PouchFlags", 0x11, Mask: 15, Shift: 4),
            new("FlingPower", 0x12), new("FieldUseType", 0x13), new("BattlePouch", 0x14), new("CanUseOnPokemon", 0x15),
            new("ItemType", 0x16), new("SortIndex", 0x18), new("ItemSprite", 0x1a, 2, true), new("GroupType", 0x1c), new("GroupIndex", 0x1d),
            new("AttackBoost", 0x1f, Mask: 15, Shift: 4), new("DefenseBoost", 0x20, Mask: 15), new("SpecialAttackBoost", 0x20, Mask: 15, Shift: 4),
            new("SpecialDefenseBoost", 0x21, Mask: 15), new("SpeedBoost", 0x21, Mask: 15, Shift: 4), new("AccuracyBoost", 0x22, Mask: 15),
            new("CriticalHitBoost", 0x22, Mask: 3, Shift: 4), new("PpUpFlag", 0x22, Mask: 1, Shift: 6), new("PpMaxFlag", 0x22, Mask: 1, Shift: 7),
            new("HealAmount", 0x2b), new("PpGain", 0x2c),
        };
        var cures = new[] { "CureSleep", "CurePoison", "CureBurn", "CureFreeze", "CureParalysis", "CureConfusion", "CureInfatuation", "GuardSpec" };
        for (var bit = 0; bit < cures.Length; bit++) fields.Add(new(cures[bit], 0x1e, Mask: 1, Shift: bit));
        var targets = new[] { "CanTargetFaintedPokemon", "RevivesWholeParty", "LevelUpItem", "EvolutionItem" };
        for (var bit = 0; bit < targets.Length; bit++) fields.Add(new(targets[bit], 0x1f, Mask: 1, Shift: bit));
        foreach (var offset in new[] { 0x23, 0x24 })
            for (var bit = 0; bit < 8; bit++) fields.Add(new($"UseFlags{offset - 0x22}Bit{bit}", offset, Mask: 1, Shift: bit));
        var evNames = new[] { "EvHp", "EvAttack", "EvDefense", "EvSpeed", "EvSpecialAttack", "EvSpecialDefense" };
        for (var index = 0; index < evNames.Length; index++) fields.Add(new(evNames[index], 0x25 + index, Signed: true));
        for (var index = 0; index < 3; index++) fields.Add(new($"FriendshipGain{index + 1}", 0x2d + index, Signed: true));
        return fields.ToArray();
    }
}
