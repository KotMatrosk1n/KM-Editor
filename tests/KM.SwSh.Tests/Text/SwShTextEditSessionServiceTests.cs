// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;
using KM.SwSh.Text;
using Xunit;

namespace KM.SwSh.Tests.Text;

public sealed class SwShTextEditSessionServiceTests
{
    [Fact]
    public void UpdateEntryCreatesPendingTextEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();

        var result = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Hello there.");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.text", edit.Domain);
        Assert.Equal("value", edit.Field);
        Assert.Equal("romfs/bin/message/English/common/story.dat#0", edit.RecordId);
        Assert.Equal("Hello there.", edit.NewValue);
        Assert.Equal("Hello there.", result.Workflow.Entries[0].Value);
        Assert.Equal("Hello there.", result.Workflow.DialogueReferences[0].Preview);
    }

    [Fact]
    public void UpdateEntryReplacesPendingEditForSameTextLine()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();
        var first = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Hello there.");

        var second = service.UpdateEntry(
            temp.Paths,
            first.Session,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Updated line.");

        var edit = Assert.Single(second.Session.PendingEdits);
        Assert.Equal("Updated line.", edit.NewValue);
        Assert.Equal("Updated line.", second.Workflow.Entries[0].Value);
    }

    [Fact]
    public void ValidateAndCreateChangePlanUseMessageTableTarget()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();
        var update = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Hello there.");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.True(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Info);
        Assert.True(plan.CanApply);
        var write = Assert.Single(plan.Writes);
        Assert.Equal("romfs/bin/message/English/common/story.dat", write.TargetRelativePath);
        Assert.Equal("romfs/bin/message/English/common/story.dat", Assert.Single(write.Sources).RelativePath);
        Assert.False(write.ReplacesExistingOutput);
    }

    [Fact]
    public void ApplyChangePlanWritesEditedTextTableToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();
        var update = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Hello there.");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("romfs/bin/message/English/common/story.dat", Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "common",
            "story.dat");
        var output = SwShGameTextFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal("Hello there.", output.Lines[0].Text);
        Assert.Equal("Second line.", output.Lines[1].Text);
    }

    [Fact]
    public void ApplyChangePlanRejectsStaleReviewedPlan()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();
        var update = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Hello there.");
        var stalePlan = new ChangePlan(update.Session.Id, Array.Empty<PlannedFileWrite>(), Array.Empty<ValidationDiagnostic>());

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, stalePlan);

        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateEntryRejectsVariablePlaceholderLines()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("[VAR 0100]"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShTextEditSessionService();

        var result = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Replacement");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateEntryRequiresEditableProjectPaths()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();

        var result = service.UpdateEntry(
            temp.Paths with { OutputRootPath = null },
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Hello there.");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("Welcome to the lab.", "Second line."));
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }

    private static byte[] CreateTextTable(params string[] lines)
    {
        return SwShGameTextFile.Write(lines.Select(line => new SwShGameTextLine(line, Flags: 0)).ToArray());
    }
}
