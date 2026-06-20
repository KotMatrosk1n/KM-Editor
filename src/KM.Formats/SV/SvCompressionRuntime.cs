// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats.SV;

public static class SvCompressionRuntime
{
    public static string RequiredFileName => string.Concat("oo2", "core", "_8_", "win", "64", ".dll");

    public static bool IsConfigured(string? supportFolderPath)
    {
        return TryResolveRequiredFilePath(supportFolderPath, out _);
    }

    public static bool TryResolveRequiredFilePath(string? supportFolderPath, out string filePath)
    {
        filePath = string.Empty;
        if (string.IsNullOrWhiteSpace(supportFolderPath))
        {
            return false;
        }

        if (!Directory.Exists(supportFolderPath))
        {
            return false;
        }

        var candidatePath = Path.Combine(supportFolderPath, RequiredFileName);
        if (!File.Exists(candidatePath))
        {
            return false;
        }

        filePath = candidatePath;
        return true;
    }

    public static string ResolveRequiredFilePath(string? supportFolderPath)
    {
        if (TryResolveRequiredFilePath(supportFolderPath, out var filePath))
        {
            return filePath;
        }

        throw new FileNotFoundException(
            "Scarlet/Violet compression support is not configured. Set the oo2core_8_win64.dll folder in Project Setup before opening compressed data.",
            RequiredFileName);
    }
}
