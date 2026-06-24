// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Core.Tests;
using Xunit;

namespace KM.Core.Tests.Projects;

public sealed class ProjectValidatorTests
{
    private const int NpdmTitleIdOffset = 0x290;
    private const ulong SwordTitleId = 0x0100ABF008968000;
    private const ulong ShieldTitleId = 0x01008DB008C2C000;
    private const ulong ScarletTitleId = 0x0100A3D008C5C000;
    private const ulong PokemonLegendsZATitleId = 0x0100F43008C44000;

    [Fact]
    public void ValidateReturnsEditableReadyWhenBaseAndOutputPathsAreSafe()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteOutputFile("romfs/data/items.bin", "layered-items");

        var health = new ProjectValidator().Validate(temp.Paths);

        Assert.Equal(ProjectHealthState.EditableReady, health.State);
        Assert.True(health.CanOpenReadOnlyWorkflows);
        Assert.True(health.CanOpenEditableWorkflows);
        Assert.Equal(2, health.FileGraph.BaseFileCount);
        Assert.Equal(1, health.FileGraph.LayeredFileCount);
        Assert.Equal(1, health.FileGraph.OverrideCount);
    }

    [Fact]
    public void ValidateReturnsReadOnlyReadyWhenOutputRootIsNotConfigured()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");

        var health = new ProjectValidator().Validate(temp.Paths with { OutputRootPath = null });

        Assert.Equal(ProjectHealthState.ReadOnlyReady, health.State);
        Assert.True(health.CanOpenReadOnlyWorkflows);
        Assert.False(health.CanOpenEditableWorkflows);
        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.OutputRoot && path.Status == ProjectPathStatus.NotSet);
        Assert.Contains(health.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ValidateAcceptsOptionalSaveFileWithoutAddingItToFileGraph()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        var saveFilePath = Path.Combine(temp.RootPath, "main");
        File.WriteAllBytes(saveFilePath, [0x01, 0x02, 0x03]);

        var health = new ProjectValidator().Validate(temp.Paths with { SaveFilePath = saveFilePath });

        Assert.Equal(ProjectHealthState.EditableReady, health.State);
        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.SaveFile && path.Status == ProjectPathStatus.Valid);
        Assert.Equal(2, health.FileGraph.BaseFileCount);
    }

    [Fact]
    public void ValidateWarnsForDirectoryUsedAsSaveFileWithoutBlockingProject()
    {
        using var temp = TemporaryProjectFolders.Create();
        var health = new ProjectValidator().Validate(temp.Paths with { SaveFilePath = temp.OutputRootPath });

        Assert.Equal(ProjectHealthState.EditableReady, health.State);
        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.SaveFile && path.Status == ProjectPathStatus.WrongKind);
        Assert.Contains(
            health.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("must be a file", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateKeepsScarletVioletSupportFolderOptional()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("arc/data.trpfd", "descriptor");
        temp.WriteBaseRomFsFile("arc/data.trpfs", "storage");
        WriteBaseExeFsBytes(temp, "main.npdm", CreateNpdm(ScarletTitleId));

        var health = new ProjectValidator().Validate(temp.Paths with { SelectedGame = ProjectGame.Scarlet });

        Assert.Equal(ProjectHealthState.EditableReady, health.State);
        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.ScarletVioletSupportFolder
                && path.Status == ProjectPathStatus.NotSet
                && !path.IsRequired);
    }

    [Fact]
    public void ValidateAcceptsScarletVioletSupportFolderWhenRequiredFileIsPresent()
    {
        using var temp = TemporaryProjectFolders.Create();
        var supportFolder = Directory.CreateDirectory(Path.Combine(temp.RootPath, "sv-support")).FullName;
        File.WriteAllBytes(Path.Combine(supportFolder, string.Concat("oo2", "core", "_8_", "win", "64", ".dll")), []);

        var health = new ProjectValidator().Validate(
            temp.Paths with
            {
                ScarletVioletSupportFolderPath = supportFolder,
                SelectedGame = ProjectGame.Scarlet,
            });

        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.ScarletVioletSupportFolder
                && path.Status == ProjectPathStatus.Valid);
    }

    [Fact]
    public void ValidateAcceptsPokemonLegendsZAWhenTitleIdUsesAci0Offset()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("arc/data.trpfd", "descriptor");
        temp.WriteBaseRomFsFile("arc/data.trpfs", "storage");
        temp.WriteBaseExeFsFile("main", "base-main");
        WriteBaseExeFsBytes(temp, "main.npdm", CreateNpdmWithTitleAtOffset(PokemonLegendsZATitleId, 0x480));

        var health = new ProjectValidator().Validate(temp.Paths with { SelectedGame = ProjectGame.ZA });

        Assert.Equal(ProjectHealthState.EditableReady, health.State);
        Assert.Contains(
            health.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("matches selected Pokemon Legends Z-A", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAcceptsPokemonLegendsZASupportFolderWhenRequiredFileIsPresent()
    {
        using var temp = TemporaryProjectFolders.Create();
        var supportFolder = Directory.CreateDirectory(Path.Combine(temp.RootPath, "za-support")).FullName;
        File.WriteAllBytes(Path.Combine(supportFolder, string.Concat("oo2", "core", "_8_", "win", "64", ".dll")), []);

        var health = new ProjectValidator().Validate(
            temp.Paths with
            {
                PokemonLegendsZASupportFolderPath = supportFolder,
                SelectedGame = ProjectGame.ZA,
            });

        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.PokemonLegendsZASupportFolder
                && path.Status == ProjectPathStatus.Valid);
    }

    [Fact]
    public void ValidateReturnsNeedsPathsWhenRequiredBasePathIsMissing()
    {
        using var temp = TemporaryProjectFolders.Create();
        var missingRomFs = Path.Combine(temp.RootPath, "missing-romfs");

        var health = new ProjectValidator().Validate(temp.Paths with { BaseRomFsPath = missingRomFs });

        Assert.Equal(ProjectHealthState.NeedsPaths, health.State);
        Assert.False(health.CanOpenReadOnlyWorkflows);
        Assert.False(health.CanOpenEditableWorkflows);
        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.BaseRomFs && path.Status == ProjectPathStatus.Missing);
    }

    [Fact]
    public void ValidateBlocksOutputRootThatOverlapsBaseData()
    {
        using var temp = TemporaryProjectFolders.Create();

        var health = new ProjectValidator().Validate(temp.Paths with { OutputRootPath = temp.BaseRomFsPath });

        Assert.Equal(ProjectHealthState.Blocked, health.State);
        Assert.False(health.CanOpenEditableWorkflows);
        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.OutputRoot && path.Status == ProjectPathStatus.Unsafe);
        Assert.Contains(
            health.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("must not overlap", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAcceptsSelectedShieldGameWhenBaseExeFsTitleMatches()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        WriteBaseExeFsBytes(temp, "main.npdm", CreateNpdm(ShieldTitleId));

        var health = new ProjectValidator().Validate(temp.Paths with { SelectedGame = ProjectGame.Shield });

        Assert.Equal(ProjectHealthState.EditableReady, health.State);
        Assert.True(health.CanOpenEditableWorkflows);
        Assert.Contains(
            health.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("matches selected Pokemon Shield", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAcceptsSelectedScarletGameWhenBaseExeFsAndTrinityArchiveMatch()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("arc/data.trpfd", "descriptor");
        temp.WriteBaseRomFsFile("arc/data.trpfs", "storage");
        temp.WriteBaseExeFsFile("main", "base-main");
        WriteBaseExeFsBytes(temp, "main.npdm", CreateNpdm(ScarletTitleId));

        var health = new ProjectValidator().Validate(temp.Paths with { SelectedGame = ProjectGame.Scarlet });

        Assert.Equal(ProjectHealthState.EditableReady, health.State);
        Assert.Contains(
            health.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("Trinity archive required for Pokemon Scarlet", StringComparison.Ordinal));
        Assert.Contains(
            health.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("matches selected Pokemon Scarlet", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateBlocksSelectedScarletGameWhenTrinityArchiveIsMissing()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        WriteBaseExeFsBytes(temp, "main.npdm", CreateNpdm(ScarletTitleId));

        var health = new ProjectValidator().Validate(temp.Paths with { SelectedGame = ProjectGame.Scarlet });

        Assert.Equal(ProjectHealthState.Blocked, health.State);
        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.BaseRomFs && path.Status == ProjectPathStatus.Unsafe);
        Assert.Contains(
            health.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("does not contain the Trinity archive", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateBlocksSelectedGameWhenBaseExeFsTitleIsForOtherGame()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        WriteBaseExeFsBytes(temp, "main.npdm", CreateNpdm(SwordTitleId));

        var health = new ProjectValidator().Validate(temp.Paths with { SelectedGame = ProjectGame.Shield });

        Assert.Equal(ProjectHealthState.Blocked, health.State);
        Assert.False(health.CanOpenReadOnlyWorkflows);
        Assert.False(health.CanOpenEditableWorkflows);
        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.BaseExeFs && path.Status == ProjectPathStatus.Unsafe);
        Assert.Contains(
            health.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Selected Pokemon Shield", StringComparison.Ordinal)
                && diagnostic.Message.Contains("Pokemon Sword", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateBlocksSelectedGameWhenOutputRootUsesOtherGameTitleId()
    {
        using var temp = TemporaryProjectFolders.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        WriteBaseExeFsBytes(temp, "main.npdm", CreateNpdm(ShieldTitleId));
        var swordOutputRoot = Directory.CreateDirectory(
            Path.Combine(temp.RootPath, SwordTitleId.ToString("X16"))).FullName;

        var health = new ProjectValidator().Validate(
            temp.Paths with
            {
                OutputRootPath = swordOutputRoot,
                SelectedGame = ProjectGame.Shield,
            });

        Assert.Equal(ProjectHealthState.Blocked, health.State);
        Assert.False(health.CanOpenEditableWorkflows);
        Assert.Contains(
            health.Paths,
            path => path.Role == ProjectPathRole.OutputRoot && path.Status == ProjectPathStatus.Unsafe);
        Assert.Contains(
            health.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Output Root folder is the Pokemon Sword title id", StringComparison.Ordinal));
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var npdm = new byte[NpdmTitleIdOffset + sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(npdm.AsSpan(NpdmTitleIdOffset), titleId);
        return npdm;
    }

    private static byte[] CreateNpdmWithTitleAtOffset(ulong titleId, int titleIdOffset)
    {
        var npdm = new byte[titleIdOffset + sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(npdm.AsSpan(titleIdOffset), titleId);
        return npdm;
    }

    private static void WriteBaseExeFsBytes(
        TemporaryProjectFolders temp,
        string relativePath,
        byte[] contents)
    {
        var filePath = Path.Combine(temp.BaseExeFsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, contents);
    }
}

