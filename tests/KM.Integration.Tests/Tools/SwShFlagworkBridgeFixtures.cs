// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShFlagworkBridgeFixtures
{
    public static void WriteBaseFlagwork(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/flagwork/system_flags.tbl",
            new SwShAhtbFile(
            [
                new SwShAhtbEntry(0x1122334455667788, "FE_TEST_FLAG"),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/flagwork/scene_work.tbl",
            new SwShAhtbFile(
            [
                new SwShAhtbEntry(0x99AABBCCDDEEFF00, "WK_SCENE_MAIN"),
            ]).Write());
    }
}
