// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.ZA.Data;
using KM.ZA.Workflows;
using Xunit;

namespace KM.Integration.Tests.ZA;

public sealed class ZaMissionTitleLookupTests
{
    [Fact]
    public void MissionTitlePathsUseTheProvenMessageFolders()
    {
        Assert.Equal(
            "ik_message/dat/French/common/questlist_main.dat",
            ZaDataPaths.MainMissionTitles("French"));
        Assert.Equal(
            "ik_message/dat/French/common/questlist_sub.dat",
            ZaDataPaths.SideMissionTitles("French"));
        Assert.Equal(
            "ik_message/dat/French/sk/questlist_dlc.dat",
            ZaDataPaths.HyperspaceMissionTitles("French"));
    }

    [Fact]
    public void LookupLoadsLocalizedTitlesFromAllThreeMissionTables()
    {
        using var temp = TemporaryProject.Create("fr");
        WriteTextTable(
            temp.BaseRomFsPath,
            ZaDataPaths.MainMissionTitles("French"),
            47,
            (21, "Atteindre le rang D"));
        WriteTextTable(
            temp.BaseRomFsPath,
            ZaDataPaths.HyperspaceMissionTitles("French"),
            14,
            (7, "Naveen ne va pas bien"));
        WriteTextTable(
            temp.BaseRomFsPath,
            ZaDataPaths.SideMissionTitles("French"),
            204,
            (85, "Menu de combats haut de gamme"),
            (148, "Esquive sans défense"));

        var diagnostics = new List<ValidationDiagnostic>();
        var labels = ZaTextLabelLookup.Load(
            temp.Project,
            new ZaWorkflowFileSource(),
            diagnostics,
            temp.Project.Paths);

        Assert.True(ZaMissionCatalog.TryGetMainMission(19, out var main));
        Assert.True(ZaMissionCatalog.TryGetHyperspaceMission(7, out var hyperspace));
        Assert.True(ZaMissionCatalog.TryGetNumberedSideMission(73, out var restaurant));
        Assert.Equal("Atteindre le rang D", labels.MissionTitle(main));
        Assert.Equal("Naveen ne va pas bien", labels.MissionTitle(hyperspace));
        Assert.Equal("Menu de combats haut de gamme", labels.MissionTitle(restaurant));
        Assert.Equal("Esquive sans défense", labels.SideMissionTitleByInternalId(147));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MissingLocalizedMissionTableFallsBackToEnglishTable()
    {
        using var temp = TemporaryProject.Create("fr");
        WriteTextTable(
            temp.BaseRomFsPath,
            ZaDataPaths.SideMissionTitles("English"),
            148,
            (148, "English title from game text"));

        var labels = ZaTextLabelLookup.Load(
            temp.Project,
            new ZaWorkflowFileSource(),
            [],
            temp.Project.Paths);

        Assert.Equal("English title from game text", labels.SideMissionTitleByInternalId(147));
    }

    [Fact]
    public void MissingOrPlaceholderGameTextFallsBackToCatalogEnglish()
    {
        using var temp = TemporaryProject.Create("fr");
        WriteTextTable(
            temp.BaseRomFsPath,
            ZaDataPaths.SideMissionTitles("French"),
            148,
            (148, "[VAR BDFF(0094)]"));

        var labels = ZaTextLabelLookup.Load(
            temp.Project,
            new ZaWorkflowFileSource(),
            [],
            temp.Project.Paths);

        Assert.Equal("Be a Defenseless Dodger!", labels.SideMissionTitleByInternalId(147));
        Assert.Equal("Full Course of Battles: High Rolling", labels.SideMissionTitleByInternalId(84));
        Assert.Null(labels.SideMissionTitleByInternalId(0));
        Assert.Null(labels.SideMissionTitleByInternalId(204));
    }

    private static void WriteTextTable(
        string romFsRoot,
        string virtualPath,
        int maximumIndex,
        params (int Index, string Text)[] entries)
    {
        var values = Enumerable.Repeat(string.Empty, maximumIndex + 1).ToArray();
        foreach (var entry in entries)
        {
            values[entry.Index] = entry.Text;
        }

        var bytes = SwShGameTextFile.Write(values
            .Select(value => new SwShGameTextLine(value, Flags: 0))
            .ToArray());
        var path = Path.Combine(romFsRoot, virtualPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private sealed class TemporaryProject : IDisposable
    {
        private TemporaryProject(string rootPath, ProjectPaths paths, OpenedProject project)
        {
            RootPath = rootPath;
            Project = project;
            BaseRomFsPath = paths.BaseRomFsPath!;
        }

        public string BaseRomFsPath { get; }

        public OpenedProject Project { get; }

        private string RootPath { get; }

        public static TemporaryProject Create(string language)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "km-za-mission-title-tests", Guid.NewGuid().ToString("N"));
            var paths = new ProjectPaths(
                Directory.CreateDirectory(Path.Combine(rootPath, "romfs")).FullName,
                Directory.CreateDirectory(Path.Combine(rootPath, "exefs")).FullName,
                Directory.CreateDirectory(Path.Combine(rootPath, "output")).FullName,
                SaveFilePath: null,
                SelectedGame: ProjectGame.ZA)
            {
                GameTextLanguage = language,
            };
            var graph = new ProjectFileGraph([]);
            var health = new ProjectHealth(
                ProjectHealthState.EditableReady,
                [],
                graph.ToSummary(),
                []);
            var project = new OpenedProject(
                ProjectId.New(),
                paths,
                health,
                graph,
                DateTimeOffset.UtcNow);
            return new TemporaryProject(rootPath, paths, project);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
