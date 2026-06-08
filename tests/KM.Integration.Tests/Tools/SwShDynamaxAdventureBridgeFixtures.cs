// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;

namespace KM.Integration.Tests.Tools;

internal static class SwShDynamaxAdventureBridgeFixtures
{
    public static void WriteBaseDynamaxAdventures(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            CreateArchive().Write());
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(133, (25, "Pikachu"), (133, "Eevee")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(85, (1, "Tackle"), (2, "Growl"), (10, "Vine Whip"), (20, "Razor Leaf"), (85, "Thunderbolt")));
    }

    public static SwShDynamaxAdventureArchive CreateArchive()
    {
        return new SwShDynamaxAdventureArchive(
        [
            new SwShDynamaxAdventureRecord(
                0,
                IsSingleCapture: true,
                SingleCaptureFlagBlock: 0x1122334455667788UL,
                Field02: 0,
                Form: 1,
                GigantamaxState: 1,
                BallItemId: 4,
                AdventureIndex: 100,
                Level: 65,
                Species: 133,
                UiMessageId: 0x8877665544332211UL,
                OtGender: 1,
                Version: 1,
                ShinyRoll: 1,
                new SwShDynamaxAdventureIvs(-4, -1, -1, -1, -1, -1),
                Ability: 1,
                IsStoryProgressGated: true,
                Moves: [1, 2, 10, 20]),
            new SwShDynamaxAdventureRecord(
                1,
                IsSingleCapture: false,
                SingleCaptureFlagBlock: 0x0102030405060708UL,
                Field02: 0,
                Form: 0,
                GigantamaxState: 0,
                BallItemId: 4,
                AdventureIndex: 101,
                Level: 60,
                Species: 25,
                UiMessageId: 0x0807060504030201UL,
                OtGender: 1,
                Version: 0,
                ShinyRoll: 1,
                new SwShDynamaxAdventureIvs(-1, 0, 1, 2, 3, 31),
                Ability: 0,
                IsStoryProgressGated: false,
                Moves: [3, 4, 5, 6]),
        ]);
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(index => new SwShGameTextLine($"Value {index}", Flags: 0))
            .ToArray();

        foreach (var (index, value) in entries)
        {
            lines[index] = new SwShGameTextLine(value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }
}
