// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Editing;

public sealed class SwShOutputRollbackScopeTests
{
    [Fact]
    public void CaptureRejectsSymbolicLinkBelowOutputRoot()
    {
        using var project = TemporarySwShProject.Create();
        var externalRoot = Directory.CreateDirectory(Path.Combine(project.RootPath, "external-output")).FullName;
        var externalFile = Path.Combine(externalRoot, "main");
        File.WriteAllBytes(externalFile, [7]);
        var linkPath = Path.Combine(project.OutputRootPath, "exefs");
        if (!TryCreateDirectorySymbolicLink(linkPath, externalRoot))
        {
            return;
        }

        try
        {
            Assert.False(SwShOutputRollbackScope.TryCapture(
                project.Paths,
                ["exefs/main"],
                out var scope,
                out var failure));
            Assert.Null(scope);
            Assert.Equal("exefs/main", failure?.RelativePath);
            Assert.Equal([7], File.ReadAllBytes(externalFile));
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }
        }
    }

    [Fact]
    public void RollbackDoesNotTraverseLinkAddedAfterCapture()
    {
        using var project = TemporarySwShProject.Create();
        Assert.True(SwShOutputRollbackScope.TryCapture(
            project.Paths,
            ["romfs/data.bin"],
            out var capturedScope,
            out _));
        using var scope = capturedScope!;
        var externalRoot = Directory.CreateDirectory(Path.Combine(project.RootPath, "external-output")).FullName;
        var externalFile = Path.Combine(externalRoot, "data.bin");
        File.WriteAllBytes(externalFile, [9]);
        var linkPath = Path.Combine(project.OutputRootPath, "romfs");
        if (!TryCreateDirectorySymbolicLink(linkPath, externalRoot))
        {
            scope.Commit();
            return;
        }

        try
        {
            var failures = scope.Rollback();

            Assert.Contains(failures, failure => failure.RelativePath == "romfs/data.bin");
            Assert.Equal([9], File.ReadAllBytes(externalFile));
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }
        }
    }

    [Fact]
    public void StableOutputPathsKeepUsingOriginalRootAfterConfiguredLinkRetarget()
    {
        using var project = TemporarySwShProject.Create();
        var firstRoot = Directory.CreateDirectory(Path.Combine(project.RootPath, "linked-output-a")).FullName;
        var secondRoot = Directory.CreateDirectory(Path.Combine(project.RootPath, "linked-output-b")).FullName;
        var rootLink = Path.Combine(project.RootPath, "output-link");
        if (!TryCreateDirectorySymbolicLink(rootLink, firstRoot))
        {
            return;
        }

        try
        {
            Assert.True(SwShOutputRollbackScope.TryResolveStableOutputPaths(
                project.Paths with { OutputRootPath = rootLink },
                out var stablePaths,
                out _));

            Directory.Delete(rootLink);
            Assert.True(TryCreateDirectorySymbolicLink(rootLink, secondRoot));

            var resolved = SwShOutputRollbackScope.ResolvePhysicalContainedPath(
                stablePaths.OutputRootPath,
                "romfs/data.bin");

            Assert.Equal(Path.Combine(firstRoot, "romfs", "data.bin"), resolved);
            Assert.NotEqual(Path.Combine(secondRoot, "romfs", "data.bin"), resolved);
        }
        finally
        {
            if (Directory.Exists(rootLink))
            {
                Directory.Delete(rootLink);
            }
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
