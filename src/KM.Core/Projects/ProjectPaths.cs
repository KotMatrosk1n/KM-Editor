// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Projects;

public sealed record ProjectPaths(
    string? BaseRomFsPath,
    string? BaseExeFsPath,
    string? OutputRootPath,
    string? SaveFilePath)
{
    public ProjectPaths(
        string? BaseRomFsPath,
        string? BaseExeFsPath,
        string? OutputRootPath)
        : this(BaseRomFsPath, BaseExeFsPath, OutputRootPath, SaveFilePath: null)
    {
    }
}

