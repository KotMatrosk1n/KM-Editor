// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.FairyGymBoosts;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using System.Text;

namespace KM.SwSh.Tests.FairyGymBoosts;

internal static class FairyGymBoostTestFixtures
{
    private const ulong FillerCommandHash = 0x1020304050607080;
    private const int FillerPayloadLength = 0x14F8;

    public static IReadOnlyList<string> RelativePaths { get; } =
    [
        SwShFairyGymBoostsWorkflowService.AnnetteSequencePath,
        SwShFairyGymBoostsWorkflowService.TeresaSequencePath,
        SwShFairyGymBoostsWorkflowService.TheodoraSequencePath,
        SwShFairyGymBoostsWorkflowService.OpalNicknameSequencePath,
        SwShFairyGymBoostsWorkflowService.OpalColorSequencePath,
        SwShFairyGymBoostsWorkflowService.OpalAgeSequencePath,
    ];

    public static TemporarySwShProject CreateProject(ProjectGame game)
    {
        var project = TemporarySwShProject.Create();
        project.WriteBaseExeFsFile("main", "base-main");
        var npdm = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(
            npdm.AsSpan(0x290, sizeof(ulong)),
            game == ProjectGame.Shield
                ? 0x01008DB008C2C000UL
                : 0x0100ABF008968000UL);
        project.WriteBaseExeFsFile("main.npdm", npdm);
        foreach (var relativePath in RelativePaths)
        {
            project.WriteBaseRomFsFile(
                StripRomFsPrefix(relativePath),
                CreateVanillaBseq(relativePath));
        }

        return project;
    }

    public static ProjectPaths GetPaths(TemporarySwShProject project, ProjectGame game)
    {
        return project.Paths with { SelectedGame = game };
    }

    public static byte[] CreateVanillaBseq(
        string relativePath,
        int slotThreeEffect = unchecked((int)0x11223344),
        int slotThreeResult = unchecked((int)0x55667788),
        bool addAmbiguousCommand = false)
    {
        var slots = GetVanillaSlots(relativePath);
        return CreateBseq(
            slots[0].EffectId,
            slots[0].ResultValue,
            slots[1].EffectId,
            slots[1].ResultValue,
            slotThreeEffect,
            slotThreeResult,
            addAmbiguousCommand);
    }

    public static byte[] CreateBseq(
        int effectOne,
        int resultOne,
        int effectTwo,
        int resultTwo,
        int slotThreeEffect = unchecked((int)0x11223344),
        int slotThreeResult = unchecked((int)0x55667788),
        bool addAmbiguousCommand = false)
    {
        var data = new byte[SwShFairyGymBoostsBseqPatcher.FileLength];
        Encoding.ASCII.GetBytes("SESD").CopyTo(data, 0x00);
        WriteU32(data, 0x04, SwShBseqFile.ExpectedVersion);
        WriteU32(data, 0x0C, 1);
        WriteU32(data, 0x10, 0);
        WriteU32(data, 0x14, 2);
        WriteU64(data, 0x18, FillerCommandHash);
        WriteU32(data, 0x20, FillerPayloadLength);
        WriteU64(data, 0x24, SwShBseqKnownCommands.SpecialQuizResult);
        WriteU32(data, 0x2C, SwShBseqKnownCommands.SpecialQuizResultPayloadLength);

        WriteU64(data, 0x3C, FillerCommandHash);
        var specialCommandOffset = 0x44 + FillerPayloadLength;
        WriteU64(data, specialCommandOffset + 0x0C, SwShBseqKnownCommands.SpecialQuizResult);
        WriteSlot(data, answerChoice: 1, effectOne, resultOne);
        WriteSlot(data, answerChoice: 2, effectTwo, resultTwo);
        WriteSlot(data, answerChoice: 3, slotThreeEffect, slotThreeResult);

        var terminatorOffset = SwShFairyGymBoostsBseqPatcher.PayloadOffset
            + SwShBseqKnownCommands.SpecialQuizResultPayloadLength;
        if (addAmbiguousCommand)
        {
            WriteU64(data, terminatorOffset + 0x0C, SwShBseqKnownCommands.SpecialQuizResult);
            WriteU32(
                data,
                terminatorOffset + 0x14 + SwShBseqKnownCommands.SpecialQuizResultPayloadLength,
                0xFFFFFFFF);
        }
        else
        {
            WriteU32(data, terminatorOffset, 0xFFFFFFFF);
        }

        data[0x3000] = 0xA5;
        return data;
    }

    public static IReadOnlyList<SwShFairyGymBoostAnswerSlot> GetVanillaSlots(string relativePath)
    {
        return relativePath switch
        {
            SwShFairyGymBoostsWorkflowService.AnnetteSequencePath =>
                [new(1, 1), new(1, 1)],
            SwShFairyGymBoostsWorkflowService.TeresaSequencePath =>
                [new(5, 2), new(5, 1)],
            SwShFairyGymBoostsWorkflowService.TheodoraSequencePath =>
                [new(3, 2), new(3, 1)],
            SwShFairyGymBoostsWorkflowService.OpalNicknameSequencePath =>
                [new(6, 2), new(6, 1)],
            SwShFairyGymBoostsWorkflowService.OpalColorSequencePath =>
                [new(4, 2), new(4, 1)],
            SwShFairyGymBoostsWorkflowService.OpalAgeSequencePath =>
                [new(2, 1), new(2, 2)],
            _ => throw new ArgumentOutOfRangeException(nameof(relativePath)),
        };
    }

    public static string StripRomFsPrefix(string relativePath)
    {
        const string prefix = "romfs/";
        return relativePath.StartsWith(prefix, StringComparison.Ordinal)
            ? relativePath[prefix.Length..]
            : relativePath;
    }

    public static string GetBasePath(TemporarySwShProject project, string relativePath)
    {
        return Path.Combine(
            project.BaseRomFsPath,
            StripRomFsPrefix(relativePath).Replace('/', Path.DirectorySeparatorChar));
    }

    public static string GetOutputPath(TemporarySwShProject project, string relativePath)
    {
        return Path.Combine(
            project.OutputRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static (int EffectId, int ResultValue) ReadSlot(byte[] data, int answerChoice)
    {
        var offset = SwShFairyGymBoostsBseqPatcher.PayloadOffset
            + ((answerChoice - 1) * SwShFairyGymBoostsBseqPatcher.SlotSize);
        return (
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int))),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + sizeof(int), sizeof(int))));
    }

    private static void WriteSlot(
        byte[] data,
        int answerChoice,
        int effectId,
        int resultValue)
    {
        var offset = SwShFairyGymBoostsBseqPatcher.PayloadOffset
            + ((answerChoice - 1) * SwShFairyGymBoostsBseqPatcher.SlotSize);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), effectId);
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(offset + sizeof(int), sizeof(int)),
            resultValue);
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private static void WriteU64(byte[] data, int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, sizeof(ulong)), value);
    }
}
