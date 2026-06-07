// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShTextBridgeFixtures
{
    public const string StoryTextRelativePath = "romfs/bin/message/English/common/story.dat";

    public static void WriteBaseText(TemporaryBridgeProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        project.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("Welcome to the lab.", "Second line."));
    }

    public static byte[] CreateTextTable(params string[] lines)
    {
        return SwShGameTextFile.Write(lines.Select(line => new SwShGameTextLine(line, Flags: 0)).ToArray());
    }
}
