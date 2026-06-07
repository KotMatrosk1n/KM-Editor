// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Core.Tests;
using Xunit;

namespace KM.Core.Tests.Projects;

public sealed class ProjectValidatorTests
{
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
}

