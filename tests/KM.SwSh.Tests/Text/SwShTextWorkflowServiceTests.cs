// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Tests.Items;
using KM.SwSh.Text;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Text;

public sealed class SwShTextWorkflowServiceTests
{
    [Fact]
    public void LoadReadsTextAndDialogueMapFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/text.dialogue.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "language": "en",
              "entries": [
                {
                  "textId": 10,
                  "label": "Greeting",
                  "value": "Welcome to the lab."
                },
                {
                  "textId": 20,
                  "label": "Farewell",
                  "value": "See you later."
                }
              ],
              "dialogueReferences": [
                {
                  "dialogueId": "intro.lab.greeting",
                  "label": "Lab greeting",
                  "textId": 10,
                  "context": "Intro",
                  "preview": "Welcome to the lab."
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTextWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Entries.Count);
        Assert.Single(workflow.DialogueReferences);
        Assert.Equal("Greeting", workflow.Entries[0].Label);
        Assert.Equal("en", workflow.Entries[0].Language);
        Assert.Equal(ProjectFileLayer.Base, workflow.Entries[0].Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, workflow.Entries[0].Provenance.FileState);
        Assert.Equal(2, workflow.Stats.TotalTextEntryCount);
        Assert.Equal(1, workflow.Stats.DialogueReferenceCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/message.dat", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTextWorkflowService().Load(project);

        Assert.Empty(workflow.Entries);
        Assert.Empty(workflow.DialogueReferences);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.text");
    }

    [Fact]
    public void LoadWarnsWhenDialogueReferenceTargetsMissingTextEntry()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/text.dialogue.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "language": "en",
              "entries": [],
              "dialogueReferences": [
                {
                  "dialogueId": "intro.missing",
                  "label": "Missing line",
                  "textId": 999,
                  "context": "Intro",
                  "preview": ""
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTextWorkflowService().Load(project);

        Assert.Single(workflow.DialogueReferences);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.text");
    }
}
