// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats.SV;

public static class SvBundledOodle
{
    public const string EnvironmentVariableName = "KM_EDITOR_BUNDLED_OODLE_PATH";
    public const string FileName = "oo2core_8_win64.dll";

    public static string ResolveRequiredPath()
    {
        var environmentPath = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
        {
            return environmentPath;
        }

        var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, FileName);
        if (File.Exists(baseDirectoryPath))
        {
            return baseDirectoryPath;
        }

        var repoResourcePath = FindRepoResourcePath();
        if (repoResourcePath is not null)
        {
            return repoResourcePath;
        }

        throw new FileNotFoundException(
            $"Bundled Oodle 8 DLL was not found. Expected {FileName} in the packaged app resources.",
            FileName);
    }

    private static string? FindRepoResourcePath()
    {
        var searchRoots = new[]
            {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var searchRoot in searchRoots)
        {
            var current = new DirectoryInfo(searchRoot);
            while (current is not null)
            {
                var candidate = Path.Combine(
                    current.FullName,
                    "apps",
                    "desktop",
                    "src-tauri",
                    "resources",
                    "oodle",
                    "win-x64",
                    FileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        return null;
    }
}
