// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Projects;
using KM.Formats.ZA;

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

    public string? PokemonLegendsZASupportFolderPath { get; private set; }

    public ProjectPathsDto Paths => new(
        BaseRomFsPath,
        BaseExeFsPath,
        OutputRootPath,
        SaveFilePath: null,
        ScarletVioletSupportFolderPath,
        SelectedGame: null,
        PokemonLegendsZASupportFolderPath: PokemonLegendsZASupportFolderPath);

    public static TemporaryBridgeProject Create(string? directoryPrefix = null)
    {
        var directoryName = string.IsNullOrWhiteSpace(directoryPrefix)
            ? Guid.NewGuid().ToString("N")
            : $"{directoryPrefix}-{Guid.NewGuid():N}";
        var rootPath = Path.Combine(Path.GetTempPath(), "km-editor-bridge-tests", directoryName);
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

    public string EnsurePokemonLegendsZASupportFolder()
    {
        if (PokemonLegendsZASupportFolderPath is not null)
        {
            return PokemonLegendsZASupportFolderPath;
        }

        PokemonLegendsZASupportFolderPath = Directory.CreateDirectory(Path.Combine(RootPath, "za-support")).FullName;
        File.WriteAllBytes(Path.Combine(PokemonLegendsZASupportFolderPath, ZaCompressionRuntime.RequiredFileName), []);
        return PokemonLegendsZASupportFolderPath;
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

