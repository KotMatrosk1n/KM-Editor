// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;
using KM.SwSh.Text;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Text;

public sealed class SwShTextWorkflowServiceTests
{
    [Fact]
    public void LoadReadsRealEnglishMessageTables()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("Welcome to the lab.", "See you later."));
        temp.WriteBaseRomFsFile(
            "bin/message/French/common/story.dat",
            CreateTextTable("Bonjour."));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTextWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Entries.Count);
        Assert.Equal(2, workflow.DialogueReferences.Count);
        var entry = workflow.Entries[0];
        Assert.Equal(0, entry.TextId);
        Assert.Equal("romfs/bin/message/English/common/story.dat#0", entry.TextKey);
        Assert.Equal("story #0", entry.Label);
        Assert.Equal("English", entry.Language);
        Assert.Equal("romfs/bin/message/English/common/story.dat", entry.SourceFile);
        Assert.Equal(0, entry.LineIndex);
        Assert.Equal("Welcome to the lab.", entry.Value);
        Assert.True(entry.CanEdit);
        Assert.Null(entry.EditBlockedReason);
        Assert.Equal(ProjectFileLayer.Base, entry.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, entry.Provenance.FileState);
        Assert.Equal("common/story:0", workflow.DialogueReferences[0].DialogueId);
        Assert.Equal(2, workflow.Stats.TotalTextEntryCount);
        Assert.Equal(2, workflow.Stats.DialogueReferenceCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        var editableField = Assert.Single(workflow.EditableFields);
        Assert.Equal("value", editableField.Field);
        Assert.Equal(4096, editableField.MaximumLength);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadUsesLayeredMessageOverrideWhenAvailable()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("Base line."));
        temp.WriteOutputFile(
            "romfs/bin/message/English/common/story.dat",
            CreateTextTable("Layered line."));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShTextWorkflowService().Load(project);

        var entry = Assert.Single(workflow.Entries);
        Assert.Equal("Layered line.", entry.Value);
        Assert.Equal(ProjectFileLayer.Layered, entry.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, entry.Provenance.FileState);
    }

    [Fact]
    public void LoadReadsLegacyAlignedLayeredMessageTables()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/pw.dat",
            CreateTextTable("Base header.", "Base posting."));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/trname.dat",
            CreateTextTable("Base header.", "Base trainer."));
        var layeredPostings = CreateTextTable("Header", "Victor", "Completed");
        var layeredTrainerNames = CreateTextTable("Header", "Lauren", "Benjamin");
        IncludeRawZeroAlignmentInDeclaredLineLength(layeredPostings, lineIndex: 1);
        IncludeRawZeroAlignmentInDeclaredLineLength(layeredTrainerNames, lineIndex: 1);
        temp.WriteOutputFile("romfs/bin/message/English/common/pw.dat", layeredPostings);
        temp.WriteOutputFile("romfs/bin/message/English/common/trname.dat", layeredTrainerNames);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShTextWorkflowService().Load(project);

        var posting = Assert.Single(workflow.Entries, entry =>
            entry.SourceFile == "romfs/bin/message/English/common/pw.dat"
            && entry.LineIndex == 1);
        Assert.Equal("Victor", posting.Value);
        Assert.Equal(ProjectFileLayer.Layered, posting.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, posting.Provenance.FileState);
        var trainerName = Assert.Single(workflow.Entries, entry =>
            entry.SourceFile == "romfs/bin/message/English/common/trname.dat"
            && entry.LineIndex == 1);
        Assert.Equal("Lauren", trainerName.Value);
        Assert.Equal(ProjectFileLayer.Layered, trainerName.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, trainerName.Provenance.FileState);
        Assert.Equal(6, workflow.Entries.Count);
        Assert.Equal(2, workflow.Stats.SourceFileCount);
        Assert.DoesNotContain(
            workflow.Diagnostics,
            diagnostic => diagnostic.Message.Contains("could not be decoded", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fr", "French", "Ligne francaise.")]
    [InlineData("zh", "Simp_Chinese", "中文行。")]
    [InlineData("ja-kanji", "JPN_KANJI", "漢字行。")]
    [InlineData("japanese-kanji", "JPN_KANJI", "漢字行。")]
    public void LoadUsesSelectedLanguageWhenAvailable(
        string languageCode,
        string messageFolder,
        string expectedValue)
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("English line."));
        temp.WriteBaseRomFsFile(
            $"bin/message/{messageFolder}/common/story.dat",
            CreateTextTable(expectedValue));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with
        {
            GameTextLanguage = languageCode,
            OutputRootPath = null,
        });

        var workflow = new SwShTextWorkflowService().Load(project);

        var entry = Assert.Single(workflow.Entries);
        Assert.Equal(messageFolder, entry.Language);
        Assert.Equal(expectedValue, entry.Value);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadFallsBackToFirstAvailableLanguageWhenEnglishIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/French/common/story.dat",
            CreateTextTable("Bonjour."));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTextWorkflowService().Load(project);

        var entry = Assert.Single(workflow.Entries);
        Assert.Equal("French", entry.Language);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.text"
                && diagnostic.Message.Contains("English message tables were not found", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadWarnsWhenSelectedLanguageFallsBackToEnglish()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("English line."));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with
        {
            GameTextLanguage = "fr",
            OutputRootPath = null,
        });

        var workflow = new SwShTextWorkflowService().Load(project);

        var entry = Assert.Single(workflow.Entries);
        Assert.Equal("English", entry.Language);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.text"
                && diagnostic.Message.Contains("French", StringComparison.Ordinal)
                && diagnostic.Message.Contains("English", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadMarksVariablePlaceholderLinesEditable()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("[VAR 0100]"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTextWorkflowService().Load(project);

        var entry = Assert.Single(workflow.Entries);
        Assert.True(entry.CanEdit);
        Assert.Null(entry.EditBlockedReason);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenMessageTablesAreMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/message-placeholder.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTextWorkflowService().Load(project);

        Assert.Empty(workflow.Entries);
        Assert.Empty(workflow.DialogueReferences);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.text");
    }

    private static byte[] CreateTextTable(params string[] lines)
    {
        return SwShGameTextFile.Write(lines.Select(line => new SwShGameTextLine(line, Flags: 0)).ToArray());
    }

    private static void IncludeRawZeroAlignmentInDeclaredLineLength(byte[] data, int lineIndex)
    {
        var sectionStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x0C));
        var sectionLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(sectionStart));
        var lineCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x02));
        Assert.InRange(lineIndex, 0, lineCount - 1);

        var entryOffset = sectionStart + sizeof(uint) + (lineIndex * 0x08);
        var textOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(entryOffset));
        var textLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entryOffset + 0x04));
        var nextTextOffset = lineIndex + 1 < lineCount
            ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(entryOffset + 0x08))
            : sectionLength;
        var rawAlignmentOffset = checked(sectionStart + textOffset + (textLength * sizeof(ushort)));

        Assert.True(textOffset + ((textLength + 1) * sizeof(ushort)) <= nextTextOffset);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rawAlignmentOffset)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(entryOffset + 0x04),
            checked((ushort)(textLength + 1)));
    }
}
