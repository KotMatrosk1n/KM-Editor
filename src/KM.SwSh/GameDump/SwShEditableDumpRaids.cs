// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.GameDump;
using KM.SwSh.Raids;
using static KM.Core.GameDump.DumpSchemaBuilder;

namespace KM.SwSh.GameDump;

public sealed partial class SwShEditableDumpProvider
{
    private static void BuildRaids(SwShRaidBattlesWorkflow data, List<DumpRecord> records)
    {
        var fields = Definitions(data.EditableFields);
        var map = new Dictionary<string, string> { ["species"] = "speciesId" };
        for (var i = 0; i < 5; i++) map[$"star{i + 1}Probability"] = $"probabilities.{i}";
        foreach (var table in data.Tables)
        {
            var record = new DumpRecord(table.TableId, table.DisplayName, "raidBattles", table.TableId);
            record.Children["slots"] = table.Slots.Select(slot => Record(slot, fields, "slot", "species", "raidBattles", map, table.TableId, slot.Slot)).ToList();
            records.Add(record);
        }
    }

    private static void BuildRewards(SwShRaidRewardsWorkflow data, List<DumpRecord> records, string category)
    {
        var fields = Definitions(data.EditableFields);
        var map = new Dictionary<string, string>();
        for (var i = 0; i < 5; i++) map[$"star{i + 1}Value"] = $"values.{i}";
        foreach (var table in data.Tables)
        {
            var record = new DumpRecord(table.TableId, table.DisplayName, category, table.TableId);
            record.Children["rewards"] = table.Rewards.Select(reward => Record(reward, fields, "slot", "itemName", category, map, table.TableId, reward.Slot)).ToList();
            records.Add(record);
        }
    }
}
