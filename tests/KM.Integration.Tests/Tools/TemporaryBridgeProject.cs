// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Projects;

namespace KM.Integration.Tests.Tools;

internal sealed class TemporaryBridgeProject : IDisposable
{
    private TemporaryBridgeProject(string rootPath)
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

    public string? ScarletVioletSupportFolderPath { get; private set; }

    public ProjectPathsDto Paths => new(
        BaseRomFsPath,
        BaseExeFsPath,
        OutputRootPath,
        SaveFilePath: null,
        ScarletVioletSupportFolderPath,
        SelectedGame: null);

    public static TemporaryBridgeProject Create()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "km-editor-bridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        return new TemporaryBridgeProject(rootPath);
    }

    public void WriteBaseRomFsFile(string relativePath, string contents)
    {
        WriteFile(BaseRomFsPath, relativePath, contents);
    }

    public void WriteBaseRomFsFile(string relativePath, byte[] contents)
    {
        WriteFile(BaseRomFsPath, relativePath, contents);
    }

    public void WriteBaseExeFsFile(string relativePath, string contents)
    {
        WriteFile(BaseExeFsPath, relativePath, contents);
    }

    public void WriteBaseExeFsFile(string relativePath, byte[] contents)
    {
        WriteFile(BaseExeFsPath, relativePath, contents);
    }

    public void WriteOutputFile(string relativePath, string contents)
    {
        WriteFile(OutputRootPath, relativePath, contents);
    }

    public void WriteOutputFile(string relativePath, byte[] contents)
    {
        WriteFile(OutputRootPath, relativePath, contents);
    }

    public string EnsureScarletVioletSupportFolder()
    {
        if (ScarletVioletSupportFolderPath is not null)
        {
            return ScarletVioletSupportFolderPath;
        }

        ScarletVioletSupportFolderPath = Directory.CreateDirectory(Path.Combine(RootPath, "sv-support")).FullName;
        File.WriteAllBytes(Path.Combine(ScarletVioletSupportFolderPath, string.Concat("oo2", "core", "_8_", "win", "64", ".dll")), []);
        return ScarletVioletSupportFolderPath;
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

    private static void WriteFile(string rootPath, string relativePath, byte[] contents)
    {
        var filePath = Path.Combine(rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, contents);
    }
}

