// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.GameDump;
using KM.Core.Projects;
using KM.Core.Tests;
using Xunit;

namespace KM.Core.Tests.GameDump;

public sealed class GameDumpWriterTests
{
    [Fact]
    public void WriteRowsOverwritesOnlyTheSelectedCategoryFolder()
    {
        using var temp = TemporaryProjectFolders.Create();
        var destination = Path.Combine(temp.RootPath, "dump");
        var itemsFolder = Path.Combine(destination, "Items");
        var pokemonFolder = Path.Combine(destination, "Pokemon");
        Directory.CreateDirectory(itemsFolder);
        Directory.CreateDirectory(pokemonFolder);
        File.WriteAllText(Path.Combine(itemsFolder, "old.txt"), "old");
        File.WriteAllText(Path.Combine(pokemonFolder, "keep.txt"), "keep");

        var writtenFiles = GameDumpWriter.WriteRows(
            destination,
            "items",
            "Items",
            new[]
            {
                new TestDumpRow(1, "Potion", ["Medicine", "Healing"]),
            },
            GameDumpFormat.TsvAndJson);

        Assert.Equal(2, writtenFiles.Count);
        Assert.False(File.Exists(Path.Combine(itemsFolder, "old.txt")));
        Assert.True(File.Exists(Path.Combine(itemsFolder, "items.tsv")));
        Assert.True(File.Exists(Path.Combine(itemsFolder, "items.json")));
        Assert.True(File.Exists(Path.Combine(pokemonFolder, "keep.txt")));
        Assert.Contains("Potion", File.ReadAllText(Path.Combine(itemsFolder, "items.tsv")));
    }

    [Fact]
    public void ValidateDestinationRejectsProjectPathOverlap()
    {
        using var temp = TemporaryProjectFolders.Create();
        var paths = new ProjectPaths(
            temp.BaseRomFsPath,
            temp.BaseExeFsPath,
            temp.OutputRootPath,
            SaveFilePath: null,
            ProjectGame.Sword);

        var diagnostics = GameDumpWriter.ValidateDestination(
            paths,
            Path.Combine(temp.BaseRomFsPath, "dump"));

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Base RomFS", StringComparison.Ordinal));
    }

    private sealed record TestDumpRow(
        int Id,
        string Name,
        IReadOnlyList<string> Tags);
}
