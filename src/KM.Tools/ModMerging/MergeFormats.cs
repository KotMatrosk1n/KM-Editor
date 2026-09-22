// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KM.Core.ModMerging;
using KM.Formats.SwSh;
using KM.SV.Pokemon;
using KM.ZA.Pokemon;

namespace KM.Tools.ModMerging;

internal static class MergeFormats
{
    public static MergeDocument? Read(string game, string path, byte[] bytes)
    {
        if (bytes.Length > 64 * 1024 * 1024) return null;
        if (MergeFixedRecords.Read(game, path, bytes) is { } fixedDocument) return fixedDocument;
        if (MergeSwShStructured.Read(game, path, bytes) is { } swshDocument) return swshDocument;
        if (MergeSchemaCatalog.Read(game, path, bytes) is { } schemaDocument) return schemaDocument;
        if (path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
            && (path.StartsWith("romfs/message/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("romfs/ik_message/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("romfs/bin/message/", StringComparison.OrdinalIgnoreCase)))
        {
            var text = SwShGameTextFile.Parse(bytes, 50_000);
            return MergeRowDocument.Create(text.Lines, lines => text.WritePreserving(lines,
                MergeInputs.Family(game) == "swsh" ? GameTextNullLineEncoding.LegacyCountOne : GameTextNullLineEncoding.PayloadCountTwo), "text");
        }
        if (path.Equals("romfs/avalon/data/personal_array.bin", StringComparison.OrdinalIgnoreCase))
            return MergeInputs.Family(game) == "za" ? ZaPokemonMergeDocument.Read(bytes) : SvPokemonMergeDocument.Read(bytes);
        if (MergeInputs.Family(game) == "swsh" && path.Equals(SwShPersonalTable.PersonalDataRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            var table = SwShPersonalTable.Parse(bytes, 50_000);
            var document = MergeRowDocument.Create(table.Records, rows => SwShPersonalTable.Write(rows, bytes), "pokemon");
            return PreserveResidual(document, bytes, SwShPersonalTable.RecordSize,
                SwShPersonalTable.Write(table.Records, new byte[bytes.Length]),
                SwShPersonalTable.Write(table.Records, Enumerable.Repeat((byte)255, bytes.Length).ToArray()));
        }
        if (MergeInputs.Family(game) == "swsh" && path.Equals(SwShPokemonLearnsetTable.LearnsetDataRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            var table = SwShPokemonLearnsetTable.Parse(bytes, 50_000, 500_000);
            return MergeRowDocument.Create(table.Records, rows => SwShPokemonLearnsetTable.Write(rows, bytes), "learnsets");
        }
        if (path.Equals("romfs/" + KM.SV.Data.SvDataPaths.MoveDataArray, StringComparison.OrdinalIgnoreCase))
            return MergeInputs.Family(game) == "za" ? KM.ZA.Moves.ZaMovesMergeDocument.Read(bytes) : KM.SV.Moves.SvMovesMergeDocument.Read(bytes);
        if (path.Equals("romfs/" + KM.SV.Data.SvDataPaths.ItemDataArray, StringComparison.OrdinalIgnoreCase) && MergeInputs.Family(game) == "sv")
            return KM.SV.Items.SvItemsMergeDocument.Read(bytes);
        if (path.Equals("romfs/" + KM.ZA.Data.ZaDataPaths.ItemDataArray, StringComparison.OrdinalIgnoreCase) && MergeInputs.Family(game) == "za")
            return KM.ZA.Items.ZaItemsMergeDocument.Read(bytes);
        if (MergeInputs.Family(game) == "swsh" && path.StartsWith(SwShMoveDataFile.MoveDataRelativeDirectory + "/", StringComparison.OrdinalIgnoreCase))
        {
            var table = SwShMoveDataFile.Parse(bytes);
            return MergeRowDocument.Create(new[] { table.Record }, rows => table.WriteEdited(rows.Single()), "moves");
        }
        if (MergeInputs.Family(game) == "swsh" && path.StartsWith(SwShEvolutionSet.EvolutionDataRelativeDirectory + "/", StringComparison.OrdinalIgnoreCase))
        {
            var table = SwShEvolutionSet.Parse(bytes);
            // All nine physical slots remain present, including empty slots.
            var rows = Enumerable.Range(0, SwShEvolutionSet.MaxEvolutionCount).Select(index => table.Evolutions.FirstOrDefault(row => row.Slot == index) ?? new SwShEvolutionRecord(index, 0, 0, 0, 0, 0)).ToArray();
            return MergeRowDocument.Create(rows, SwShEvolutionSet.Write, "evolutions");
        }
        if (MergeInputs.Family(game) == "sv" && path.Equals("romfs/" + KM.SV.Data.SvDataPaths.TrainerDataArray, StringComparison.OrdinalIgnoreCase))
            return KM.SV.Trainers.SvTrainersMergeDocument.Read(bytes);
        if (MergeInputs.Family(game) == "za" && path.Equals("romfs/" + KM.ZA.Data.ZaDataPaths.TrainerDataArray, StringComparison.OrdinalIgnoreCase))
            return KM.ZA.Trainers.ZaTrainersMergeDocument.Read(bytes);
        if (MergeInputs.Family(game) == "sv" && path.Equals("romfs/" + KM.SV.Data.SvDataPaths.WildEncounterArray, StringComparison.OrdinalIgnoreCase))
            return KM.SV.Encounters.SvEncountersMergeDocument.Read(bytes);
        if (MergeInputs.Family(game) == "sv" && path.Equals("romfs/" + KM.SV.Data.SvDataPaths.FriendlyShopLineupDataArray, StringComparison.OrdinalIgnoreCase))
            return KM.SV.Shops.SvFriendlyShopMergeDocument.Read(bytes);
        if (MergeInputs.Family(game) == "sv" && path.Equals("romfs/" + KM.SV.Data.SvDataPaths.ShopWazaMachineDataArray, StringComparison.OrdinalIgnoreCase))
            return KM.SV.Shops.SvTechnicalMachinesShopMergeDocument.Read(bytes);
        if (MergeInputs.Family(game) == "za" && path.Equals("romfs/" + KM.ZA.Data.ZaDataPaths.ShopItemLineupArray, StringComparison.OrdinalIgnoreCase))
            return KM.ZA.Shops.ZaLineupShopMergeDocument.Read(bytes);
        if (bytes.AsSpan().StartsWith("GFLXPACK"u8))
        {
            // Validate compressed allocations before retaining the source-preserving editable archive.
            _ = SwShGfPackFile.ParseBoundedReadOnly(bytes, 50_000, 128 * 1024 * 1024);
            var pack = SwShGfPackFile.Parse(bytes);
            var members = new JsonObject();
            var structuredMembers = new Dictionary<string, MergeDocument>();
            var knownMembers = new Dictionary<ulong, (string Name, string Schema)>();
            if (MergeInputs.Family(game) == "swsh" && path.Equals("romfs/bin/archive/field/resident/data_table.gfpak", StringComparison.OrdinalIgnoreCase))
                foreach (var (name, schema) in new[] {
                    ("encount_symbol_k.bin", "SwShWildEncounters"), ("encount_k.bin", "SwShWildEncounters"),
                    ("encount_symbol_t.bin", "SwShWildEncounters"), ("encount_t.bin", "SwShWildEncounters"),
                    ("nest_hole_drop_rewards.bin", "SwShRewards"), ("nest_hole_bonus_rewards.bin", "SwShRewards"), ("nest_hole_encount.bin", "SwShNests") })
                    if (pack.TryGetUniqueFileHash(name, out var memberHash)) knownMembers.Add(memberHash, (name, schema));
            long materialized = 0;
            foreach (var hash in pack.AbsoluteHashes)
            {
                var key = hash.HashFnv1aPathFull.ToString("X16");
                var member = pack.ReadFileByHash(hash.HashFnv1aPathFull, 64 * 1024 * 1024);
                materialized += member.Length;
                if (materialized > 128 * 1024 * 1024 || members.ContainsKey(key)) throw new InvalidDataException("GFPAK members exceed the merge limit or have ambiguous hashes.");
                if (knownMembers.TryGetValue(hash.HashFnv1aPathFull, out var known))
                {
                    var document = MergeTableSchemas.Tables[known.Schema].Read(member, "archive-fields");
                    structuredMembers[key] = document;
                    members[key] = new JsonObject { ["Name"] = known.Name, ["Layout"] = document.LayoutIdentity, ["Fields"] = document.Content };
                }
                else members[key] = Convert.ToBase64String(member);
            }
            var identity = pack.MergeMetadataIdentity;
            var content = new JsonObject { ["ContainerLayout"] = identity, ["Members"] = members };
            return new(content, node =>
            {
                var writablePack = SwShGfPackFile.Parse(bytes);
                if (node["ContainerLayout"]!.GetValue<string>() != identity || node["Members"]!.AsObject().Count != pack.FileCount)
                    throw new InvalidDataException("GFPAK layout changes require a complete archive choice.");
                foreach (var member in node["Members"]!.AsObject())
                {
                    byte[] data;
                    if (structuredMembers.TryGetValue(member.Key, out var document))
                    {
                        if (member.Value?["Layout"]?.GetValue<string>() != document.LayoutIdentity) throw new InvalidDataException("The archive member layout changed.");
                        data = document.Write(member.Value!["Fields"]!);
                    }
                    else data = Convert.FromBase64String(member.Value!.GetValue<string>());
                    writablePack.ReplaceFileByHash(Convert.ToUInt64(member.Key, 16), data);
                }
                return writablePack.Write();
            }, structuredMembers.Count > 0 ? "archive-fields" : "archive-members");
        }
        if (MergeInputs.Family(game) == "swsh" && path.Equals(SwShItemTable.ItemDataRelativePath, StringComparison.OrdinalIgnoreCase))
            return MergeSwShItems.Read(bytes);
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            // Duplicate object keys cannot be represented without losing source information.
            var bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
            using var parsed = JsonDocument.Parse(bom ? bytes.AsMemory(3) : bytes, new JsonDocumentOptions { MaxDepth = 64 });
            CheckJson(parsed.RootElement);
            var text = new UTF8Encoding(false, true).GetString(bom ? bytes.AsSpan(3) : bytes);
            var node = JsonNode.Parse(text);
            return node is null ? null : new MergeDocument(node,
                value => Encoding.UTF8.GetBytes(value.ToJsonString(new JsonSerializerOptions { WriteIndented = true })), "structured");
        }
        return null;
    }

    private static MergeDocument PreserveResidual(MergeDocument document, byte[] bytes, int rowSize, byte[] zero, byte[] ones)
    {
        var mask = zero.Zip(ones, (first, second) => (byte)(first ^ second)).ToArray();
        var content = document.Content.AsObject();
        var index = 0;
        foreach (var row in content)
        {
            var residual = new byte[rowSize];
            for (var offset = 0; offset < rowSize; offset++) residual[offset] = (byte)(bytes[index + offset] & mask[index + offset]);
            row.Value!.AsObject()["UnrecognizedFields"] = Convert.ToBase64String(residual);
            index += rowSize;
        }
        return document with { Write = node =>
        {
            var result = document.Write(node);
            var rowIndex = 0;
            foreach (var row in node.AsObject().OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var residual = Convert.FromBase64String(row.Value!["UnrecognizedFields"]!.GetValue<string>());
                if (residual.Length != rowSize) throw new InvalidDataException("Unrecognized record data has the wrong size.");
                for (var offset = 0; offset < rowSize; offset++)
                {
                    var position = rowIndex * rowSize + offset;
                    result[position] = (byte)((result[position] & ~mask[position]) | (residual[offset] & mask[position]));
                }
                rowIndex++;
            }
            return result;
        } };
    }

    private static void CheckJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException("JSON contains duplicate field names.");
                CheckJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckJson(item);
    }
}
