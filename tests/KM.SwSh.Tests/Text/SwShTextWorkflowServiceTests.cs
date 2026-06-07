// SPDX-License-Identifier: GPL-3.0-only

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
    public void LoadMarksVariablePlaceholderLinesReadOnly()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("[VAR 0100]"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTextWorkflowService().Load(project);

        var entry = Assert.Single(workflow.Entries);
        Assert.False(entry.CanEdit);
        Assert.Contains("Variable placeholders", entry.EditBlockedReason, StringComparison.Ordinal);
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
}
