// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.Core.Tests;

internal sealed class TemporaryProjectFolders : IDisposable
{
    private TemporaryProjectFolders(string rootPath)
    {
        RootPath = rootPath;
        BaseRomFsPath = Directory.CreateDirectory(Path.Combine(rootPath, "base-romfs")).FullName;
        BaseExeFsPath = Directory.CreateDirectory(Path.Combine(rootPath, "base-exefs")).FullName;
        OutputRootPath = Directory.CreateDirectory(Path.Combine(rootPath, "output")).FullName;
    }

    public string RootPath { get; }

    public string BaseRomFsPath { get; }

    public string BaseExeFsPath { get; }

    public string OutputRootPath { get; }

    public ProjectPaths Paths => new(BaseRomFsPath, BaseExeFsPath, OutputRootPath);

    public static TemporaryProjectFolders Create()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "km-editor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        return new TemporaryProjectFolders(rootPath);
    }

    public void WriteBaseRomFsFile(string relativePath, string contents)
    {
        WriteFile(BaseRomFsPath, relativePath, contents);
    }

    public void WriteBaseExeFsFile(string relativePath, string contents)
    {
        WriteFile(BaseExeFsPath, relativePath, contents);
    }

    public void WriteOutputFile(string relativePath, string contents)
    {
        WriteFile(OutputRootPath, relativePath, contents);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private static void WriteFile(string rootPath, string relativePath, string contents)
    {
        var filePath = Path.Combine(rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, contents);
    }
}

