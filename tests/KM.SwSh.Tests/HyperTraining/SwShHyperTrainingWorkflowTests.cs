// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using KM.SwSh.HyperTraining;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.HyperTraining;

public sealed class SwShHyperTrainingWorkflowTests
{
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    [Fact]
    public void ApplyMinimumLevelPatchesThresholdCellOnly()
    {
        var amx = CreateSyntheticHyperTrainingAmx(100);

        var patched = SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(amx, 42);

        Assert.Equal(42, SwShHyperTrainingAmxPatcher.ReadMinimumLevel(patched));
        Assert.Equal(PackInstruction(188, 100), ReadCodeCell(patched, 2300));
    }

    [Fact]
    public void ApplyMinimumLevelSupportsCompactAmx()
    {
        var amx = CreateSyntheticCompactHyperTrainingAmx(100);

        var patched = SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(amx, 25);

        Assert.Equal(25, SwShHyperTrainingAmxPatcher.ReadMinimumLevel(patched));
        Assert.True((BinaryPrimitives.ReadInt16LittleEndian(patched.AsSpan(0x08)) & 0x0004) != 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ApplyMinimumLevelRejectsOutOfRangeValues(int minimumLevel)
    {
        var amx = CreateSyntheticHyperTrainingAmx(100);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SwShHyperTrainingAmxPatcher.ApplyMinimumLevel(amx, minimumLevel));
    }

    [Fact]
    public void AnalyzeReportsConflictForUnexpectedLevelCheckShape()
    {
        var amx = CreateSyntheticHyperTrainingAmx(100);
        WriteCodeCell(amx, 2295, 0);

        var analysis = SwShHyperTrainingAmxPatcher.Analyze(amx);

        Assert.Equal(SwShHyperTrainingScriptKind.Conflict, analysis.Kind);
        Assert.Contains("level comparison", analysis.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DialoguePatcherUpdatesLevelLinesAndKeepsVariableToken()
    {
        var table = CreateDialogueTable();

        var patched = SwShHyperTrainingDialoguePatcher.ApplyMinimumLevel(table, 42);
        var parsed = SwShGameTextFile.Parse(patched);

        Assert.Contains("Lv. 42", parsed.Lines[0].Text, StringComparison.Ordinal);
        Assert.Contains("[VAR BE00]", parsed.Lines[0].Text, StringComparison.Ordinal);
        Assert.Contains("Lv. 42", parsed.Lines[3].Text, StringComparison.Ordinal);
        Assert.Equal("Untouched \\[VAR 0102(0001)] line.", parsed.Lines[4].Text);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void ApplyMainMinimumLevelPatchesPickerCutoffAndBranchConditions(ProjectGame game)
    {
        var main = CreateSyntheticHyperTrainingMain(game);
        var offsets = HyperTrainingOffsets(game);

        var patched = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(main, 50, game);
        var patchedText = SwShNsoFile.Parse(patched).Text.DecompressedData;
        var analysis = SwShHyperTrainingMainPatcher.Analyze(patched, game);

        Assert.Equal(SwShHyperTrainingMainKind.CustomMinimumLevel, analysis.Kind);
        Assert.Equal(50, analysis.MinimumLevel);
        Assert.Equal(EncodeCmpW0Immediate(50), ReadInstruction(patchedText, offsets.PreflightCompareOffset));
        Assert.Equal(EncodeCmpW0Immediate(50), ReadInstruction(patchedText, offsets.EligibilityCompareOffset));
        Assert.Equal(0x54000063u, ReadInstruction(patchedText, offsets.EligibilityBranchOffset));
        Assert.Equal(EncodeCmpW0Immediate(50), ReadInstruction(patchedText, offsets.GrayOutCompareOffset));
        Assert.Equal(0x540000A3u, ReadInstruction(patchedText, offsets.GrayOutBranchOffset));
        Assert.Equal(EncodeCmpW0Immediate(50), ReadInstruction(patchedText, offsets.DetailCompareOffset));
        Assert.Equal(0x540002C3u, ReadInstruction(patchedText, offsets.DetailBranchOffset));
    }

    [Fact]
    public void ApplyMainMinimumLevelRestoresVanillaBranchShapeAtLevel100()
    {
        var patched = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(
            CreateSyntheticHyperTrainingMain(ProjectGame.Sword),
            50,
            ProjectGame.Sword);

        var restored = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(patched, 100, ProjectGame.Sword);
        var restoredText = SwShNsoFile.Parse(restored).Text.DecompressedData;
        var analysis = SwShHyperTrainingMainPatcher.Analyze(restored, ProjectGame.Sword);

        Assert.Equal(SwShHyperTrainingMainKind.NotInstalled, analysis.Kind);
        Assert.Equal(100, analysis.MinimumLevel);
        Assert.Equal(0x54000061u, ReadInstruction(restoredText, SwShHyperTrainingMainPatcher.SwordEligibilityBranchOffset));
        Assert.Equal(0x540000A1u, ReadInstruction(restoredText, SwShHyperTrainingMainPatcher.SwordGrayOutBranchOffset));
        Assert.Equal(0x540002C1u, ReadInstruction(restoredText, SwShHyperTrainingMainPatcher.SwordDetailBranchOffset));
    }

    [Fact]
    public void ApplyMainMinimumLevelChangesOnlyHyperTrainingReservedTextBytes()
    {
        var main = CreateSyntheticHyperTrainingMain(ProjectGame.Sword, extraTextSetup: text =>
        {
            WriteInstruction(text, 0x00747988, 0xDEADBEEF);
            WriteInstruction(text, 0x013AE3AC, 0xFEEDFACE);
        });
        var baseText = SwShNsoFile.Parse(main).Text.DecompressedData;

        var patched = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(main, 50, ProjectGame.Sword);
        var patchedText = SwShNsoFile.Parse(patched).Text.DecompressedData;

        Assert.Equal(0xDEADBEEFu, ReadInstruction(patchedText, 0x00747988));
        Assert.Equal(0xFEEDFACEu, ReadInstruction(patchedText, 0x013AE3AC));
        Assert.All(
            ChangedTextOffsets(baseText, patchedText),
            changedOffset => Assert.Contains(
                SwShHyperTrainingMainPatcher.ReservedMainTextRegions(),
                region => SwShExeFsReservedRegionLedger.Overlaps(region, changedOffset, 1)));
    }

    [Fact]
    public void StageAndApplyMinimumLevelWritesScriptDialogueAndMainOutputs()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/script/amx/hyper_training.amx",
            CreateSyntheticHyperTrainingAmx(100));
        temp.WriteBaseRomFsFile(
            "bin/message/English/script/sub_event_007.dat",
            CreateDialogueTable());
        temp.WriteBaseExeFsFile("main", CreateSyntheticHyperTrainingMain(ProjectGame.Sword));
        var service = new SwShHyperTrainingEditSessionService();

        var staged = service.StageMinimumLevel(temp.Paths, 50, session: null);
        var plan = service.CreateChangePlan(temp.Paths, staged.Session);
        var apply = service.ApplyChangePlan(temp.Paths, staged.Session, plan);

        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(3, plan.Writes.Count);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShHyperTrainingWorkflowService.ScriptPath);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShHyperTrainingWorkflowService.ExeFsMainPath);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShHyperTrainingWorkflowService.EnglishDialoguePath);
        Assert.Equal(
            50,
            SwShHyperTrainingAmxPatcher.ReadMinimumLevel(File.ReadAllBytes(Path.Combine(
                temp.OutputRootPath,
                "romfs",
                "bin",
                "script",
                "amx",
                "hyper_training.amx"))));
        var mainAnalysis = SwShHyperTrainingMainPatcher.Analyze(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "exefs",
            "main")));
        Assert.Equal(SwShHyperTrainingMainKind.CustomMinimumLevel, mainAnalysis.Kind);
        Assert.Equal(50, mainAnalysis.MinimumLevel);

        var dialogue = SwShGameTextFile.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "script",
            "sub_event_007.dat")));
        Assert.Contains("Lv. 50", dialogue.Lines[3].Text, StringComparison.Ordinal);
    }

    private static byte[] CreateSyntheticHyperTrainingMain(
        ProjectGame game,
        int minimumLevel = 100,
        Action<byte[]>? extraTextSetup = null)
    {
        var text = new byte[0x0157D000];
        var offsets = HyperTrainingOffsets(game);
        WriteInstruction(text, offsets.PreflightCompareOffset, EncodeCmpW0Immediate(minimumLevel));
        WriteInstruction(text, offsets.EligibilityCompareOffset, EncodeCmpW0Immediate(minimumLevel));
        WriteInstruction(text, offsets.EligibilityBranchOffset, minimumLevel == 100 ? 0x54000061u : 0x54000063u);
        WriteInstruction(text, offsets.GrayOutCompareOffset, EncodeCmpW0Immediate(minimumLevel));
        WriteInstruction(text, offsets.GrayOutBranchOffset, minimumLevel == 100 ? 0x540000A1u : 0x540000A3u);
        WriteInstruction(text, offsets.DetailCompareOffset, EncodeCmpW0Immediate(minimumLevel));
        WriteInstruction(text, offsets.DetailBranchOffset, minimumLevel == 100 ? 0x540002C1u : 0x540002C3u);
        extraTextSetup?.Invoke(text);
        return CreateNso(text, [0x10], [0x20], BuildIdForGame(game));
    }

    private static HyperTrainingOffsetSet HyperTrainingOffsets(ProjectGame game)
    {
        var shift = game == ProjectGame.Shield ? 0x30 : 0;
        return new HyperTrainingOffsetSet(
            SwShHyperTrainingMainPatcher.SwordPreflightCompareOffset + shift,
            SwShHyperTrainingMainPatcher.SwordEligibilityCompareOffset + shift,
            SwShHyperTrainingMainPatcher.SwordEligibilityBranchOffset + shift,
            SwShHyperTrainingMainPatcher.SwordGrayOutCompareOffset + shift,
            SwShHyperTrainingMainPatcher.SwordGrayOutBranchOffset + shift,
            SwShHyperTrainingMainPatcher.SwordDetailCompareOffset + shift,
            SwShHyperTrainingMainPatcher.SwordDetailBranchOffset + shift);
    }

    private static uint EncodeCmpW0Immediate(int immediate)
    {
        return 0x7100001Fu | (uint)(immediate << 10);
    }

    private static uint ReadInstruction(byte[] text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)));
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static int[] ChangedTextOffsets(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        return Enumerable.Range(0, before.Length)
            .Where(index => before[index] != after[index])
            .ToArray();
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[] buildId)
    {
        var textOffset = SwShNsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), SwShNsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        buildId.CopyTo(output.AsSpan(0x40, 0x20));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        SwShNsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        SwShNsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        SwShNsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        return output;
    }

    private static void WriteSegmentHeader(byte[] output, int offset, int fileOffset, int memoryOffset, int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static byte[] BuildIdForGame(ProjectGame game)
    {
        return Convert.FromHexString(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

    private static byte[] CreateSyntheticHyperTrainingAmx(int minimumLevel)
    {
        const int headerSize = 0x38;
        const int cellSize = 8;
        const int codeCellCount = 2301;
        var cod = headerSize;
        var dat = cod + codeCellCount * cellSize;
        var data = new byte[dat];

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x00), data.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), 0xF1E1);
        data[0x06] = 0x0A;
        data[0x07] = 0x0A;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x08), 0);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x0A), 12);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x1C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x20), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x24), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x28), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x2C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x34), cod);

        WriteCodeCell(data, 2294, PackInstruction(172, minimumLevel));
        WriteCodeCell(data, 2295, 64);
        WriteCodeCell(data, 2296, 176);
        WriteCodeCell(data, 2300, PackInstruction(188, 100));
        return data;
    }

    private static byte[] CreateSyntheticCompactHyperTrainingAmx(int minimumLevel)
    {
        var expanded = CreateSyntheticHyperTrainingAmx(minimumLevel);
        const int cod = 0x38;
        const int cellSize = 8;
        var prefix = expanded[..cod].ToArray();
        BinaryPrimitives.WriteInt16LittleEndian(prefix.AsSpan(0x08), 0x0004);
        var compactBody = CompactAmxMemory(expanded, cod, expanded.Length, cellSize);
        var compact = new byte[cod + compactBody.Length];
        prefix.CopyTo(compact, 0);
        compactBody.CopyTo(compact.AsSpan(cod));
        BinaryPrimitives.WriteInt32LittleEndian(compact.AsSpan(0x00), compact.Length);
        return compact;
    }

    private static byte[] CreateDialogueTable()
    {
        return SwShGameTextFile.Write(
        [
            new SwShGameTextLine("Intro.", 4),
            new SwShGameTextLine("No caps.", 0),
            new SwShGameTextLine("Choose Pokemon.", 0),
            new SwShGameTextLine("If it isn't Lv. 100, it is not hype enough.", 4),
            new SwShGameTextLine("Untouched \\[VAR 0102(0001)] line.", 0),
        ]);
    }

    private static ulong ReadCodeCell(byte[] amx, int cell)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(amx.AsSpan(0x38 + cell * 8));
    }

    private static void WriteCodeCell(byte[] amx, int cell, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(amx.AsSpan(0x38 + cell * 8), value);
    }

    private static ulong PackInstruction(int opcode, int operand)
    {
        return ((ulong)(uint)operand << 32) | (uint)opcode;
    }

    private static byte[] CompactAmxMemory(byte[] expanded, int cod, int hea, int cellSize)
    {
        var compact = new List<byte>();
        for (var offset = cod; offset < hea; offset += cellSize)
        {
            var signed = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(expanded.AsSpan(offset)));
            var chunks = new List<byte>();
            var value = signed;
            while (true)
            {
                var payload = (byte)(value & 0x7F);
                chunks.Add(payload);
                value >>= 7;
                var signBitSet = (payload & 0x40) != 0;
                if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                {
                    break;
                }
            }

            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                var current = chunks[i];
                if (i != 0)
                {
                    current |= 0x80;
                }

                compact.Add(current);
            }
        }

        return compact.ToArray();
    }

    private sealed record HyperTrainingOffsetSet(
        int PreflightCompareOffset,
        int EligibilityCompareOffset,
        int EligibilityBranchOffset,
        int GrayOutCompareOffset,
        int GrayOutBranchOffset,
        int DetailCompareOffset,
        int DetailBranchOffset);
}
