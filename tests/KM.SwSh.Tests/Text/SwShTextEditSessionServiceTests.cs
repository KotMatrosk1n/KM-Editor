// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
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
    public void UpdateEntryPreservesForeignDomainAndRemovesOnlyRevertedTextEdit()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();
        const string textKey = "romfs/bin/message/English/common/story.dat#0";
        var foreignEdit = new PendingEdit(
            "workflow.items",
            "Unrelated edit with the same record identity.",
            [],
            RecordId: textKey,
            Field: "value",
            NewValue: "Foreign value");
        var foreignSession = EditSession.Start().WithPendingEdit(foreignEdit);

        var staged = service.UpdateEntry(temp.Paths, foreignSession, textKey, "Pending text value.");
        var validation = service.Validate(temp.Paths, staged.Session);
        var plan = service.CreateChangePlan(temp.Paths, staged.Session);

        Assert.Equal(2, staged.Session.PendingEdits.Count);
        Assert.Contains(foreignEdit, staged.Session.PendingEdits);
        Assert.Equal("Pending text value.", staged.Workflow.Entries[0].Value);
        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Single(plan.Writes);

        var reverted = service.UpdateEntry(
            temp.Paths,
            staged.Session,
            textKey,
            "Welcome to the lab.");

        Assert.Equal(foreignEdit, Assert.Single(reverted.Session.PendingEdits));
        Assert.Equal("Welcome to the lab.", reverted.Workflow.Entries[0].Value);
    }

    [Theory]
    [InlineData("Replacement [VAR 0100]")]
    [InlineData("Pause [WAIT 60]")]
    [InlineData("Null line [~ 0]")]
    [InlineData("Reading {漢字|かんじ}")]
    [InlineData("Literal \\[bracket] and \\{brace}")]
    public void UpdateEntryAllowsSupportedControlSyntax(string value)
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();

        var result = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(value, Assert.Single(result.Session.PendingEdits).NewValue);
        Assert.Equal(value, result.Workflow.Entries[0].Value);
    }

    [Fact]
    public void UpdateEntryRejectsInvalidTextAndPreservesExistingOverlay()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();
        var staged = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Pending text value.");
        string[] invalidValues =
        [
            "Bad\0value",
            "Bad\u0010value",
            "Bad\uD800value",
            "Bad \\q escape",
            "Bad trailing escape\\",
            "Bad [VAR 0102(ZZZZ)]",
            "Bad [VAR 0102(0001)",
            "Bad [WAIT -1]",
            "Bad [~ ]",
            "Bad {base}",
            "Bad [literal]",
        ];

        foreach (var invalidValue in invalidValues)
        {
            var result = service.UpdateEntry(
                temp.Paths,
                staged.Session,
                textKey: "romfs/bin/message/English/common/story.dat#1",
                invalidValue);

            Assert.Equal(staged.Session, result.Session);
            Assert.Equal("Pending text value.", result.Workflow.Entries[0].Value);
            Assert.Equal("Second line.", result.Workflow.Entries[1].Value);
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                    && diagnostic.Field == SwShTextEditSessionService.TextValueField);
        }
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
        Assert.False(string.IsNullOrWhiteSpace(write.SourceFingerprint));
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
    public void ApplyChangePlanRejectsReviewedPlanAfterSourceContentChanges()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();
        var update = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "Hello there.");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(plan.Writes).SourceFingerprint));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/story.dat",
            CreateTextTable("Externally changed line.", "Second line."));

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && (diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase)
                    || diagnostic.Message.Contains("changed", StringComparison.OrdinalIgnoreCase)));
        Assert.False(File.Exists(GetStoryOutputPath(temp)));
    }

    [Fact]
    public void ApplyChangePlanRejectsReviewedPlanWhenPendingValueChangesWithoutSummaryChange()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();
        var sharedPreview = new string('A', 72);
        var update = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: $"{sharedPreview} reviewed suffix");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var originalEdit = Assert.Single(update.Session.PendingEdits);
        var changedSession = update.Session with
        {
            PendingEdits = [originalEdit with { NewValue = $"{sharedPreview} unreviewed suffix" }],
        };

        var apply = service.ApplyChangePlan(temp.Paths, changedSession, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetStoryOutputPath(temp)));
    }

    [Fact]
    public void ApplyChangePlanRefreshesLayeredSourceBeforeASecondEdit()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTextEditSessionService();
        var firstUpdate = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#0",
            value: "First applied line.");
        var firstPlan = service.CreateChangePlan(temp.Paths, firstUpdate.Session);
        var firstApply = service.ApplyChangePlan(temp.Paths, firstUpdate.Session, firstPlan);
        Assert.DoesNotContain(firstApply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var secondUpdate = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/story.dat#1",
            value: "Second applied line.");
        var secondPlan = service.CreateChangePlan(temp.Paths, secondUpdate.Session);
        var secondWrite = Assert.Single(secondPlan.Writes);

        Assert.Equal("First applied line.", secondUpdate.Workflow.Entries[0].Value);
        Assert.True(secondWrite.ReplacesExistingOutput);
        Assert.Equal(ProjectFileLayer.Layered, Assert.Single(secondWrite.Sources).Layer);
        Assert.False(string.IsNullOrWhiteSpace(secondWrite.SourceFingerprint));

        var secondApply = service.ApplyChangePlan(temp.Paths, secondUpdate.Session, secondPlan);
        var output = SwShGameTextFile.Parse(File.ReadAllBytes(GetStoryOutputPath(temp)));

        Assert.DoesNotContain(secondApply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("First applied line.", output.Lines[0].Text);
        Assert.Equal("Second applied line.", output.Lines[1].Text);
    }

    [Fact]
    public void ApplyChangePlanRollsBackEarlierTextFileWhenLaterTargetCannotBeWritten()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/a.dat",
            CreateTextTable("A base line."));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/z.dat",
            CreateTextTable("Z base line."));
        temp.WriteBaseExeFsFile("main", "base-main");
        var aOutputPath = GetTextOutputPath(temp, "a.dat");
        var originalAOutput = CreateTextTable("A existing layered line.");
        Directory.CreateDirectory(Path.GetDirectoryName(aOutputPath)!);
        File.WriteAllBytes(aOutputPath, originalAOutput);
        var service = new SwShTextEditSessionService();
        var first = service.UpdateEntry(
            temp.Paths,
            session: null,
            textKey: "romfs/bin/message/English/common/a.dat#0",
            value: "A changed line.");
        var second = service.UpdateEntry(
            temp.Paths,
            first.Session,
            textKey: "romfs/bin/message/English/common/z.dat#0",
            value: "Z changed line.");
        var zOutputPath = GetTextOutputPath(temp, "z.dat");
        Directory.CreateDirectory(zOutputPath);
        var plan = service.CreateChangePlan(temp.Paths, second.Session);

        var apply = service.ApplyChangePlan(temp.Paths, second.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Message.Contains("rolled back", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(originalAOutput, File.ReadAllBytes(aOutputPath));
        Assert.True(Directory.Exists(zOutputPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(zOutputPath)!, "*.tmp"));
    }

    [Fact]
    public void UpdateEntryAllowsVariablePlaceholderLines()
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
            value: "Replacement [VAR 0100]");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        Assert.Equal("Replacement [VAR 0100]", Assert.Single(result.Session.PendingEdits).NewValue);
        Assert.Equal("Replacement [VAR 0100]", result.Workflow.Entries[0].Value);
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

    private static string GetStoryOutputPath(TemporarySwShProject temp)
    {
        return GetTextOutputPath(temp, "story.dat");
    }

    private static string GetTextOutputPath(TemporarySwShProject temp, string fileName)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "message",
            "English",
            "common",
            fileName);
    }
}
