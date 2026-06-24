// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Projects;

public sealed record ProjectPaths(
    string? BaseRomFsPath,
    string? BaseExeFsPath,
    string? OutputRootPath,
    string? SaveFilePath,
    string? ScarletVioletSupportFolderPath,
    ProjectGame? SelectedGame)
{
    public string? GameTextLanguage { get; init; }

    public ProjectPaths(
        string? BaseRomFsPath,
        string? BaseExeFsPath,
        string? OutputRootPath,
        string? SaveFilePath,
        ProjectGame? SelectedGame)
        : this(BaseRomFsPath, BaseExeFsPath, OutputRootPath, SaveFilePath, ScarletVioletSupportFolderPath: null, SelectedGame)
    {
    }

    public ProjectPaths(
        string? BaseRomFsPath,
        string? BaseExeFsPath,
        string? OutputRootPath,
        string? SaveFilePath)
        : this(BaseRomFsPath, BaseExeFsPath, OutputRootPath, SaveFilePath, SelectedGame: null)
    {
    }

    public ProjectPaths(
        string? BaseRomFsPath,
        string? BaseExeFsPath,
        string? OutputRootPath)
        : this(BaseRomFsPath, BaseExeFsPath, OutputRootPath, SaveFilePath: null, SelectedGame: null)
    {
    }
}

