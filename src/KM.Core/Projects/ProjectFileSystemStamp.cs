// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;

namespace KM.Core.Projects;

internal static class ProjectFileSystemStamp
{
    private const int MaximumOutputEntries = 20_000;

    // This stamp expires read caches. Apply still validates exact source and output bytes.
    public static string? Capture(ProjectPaths paths)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AddRoot(paths.BaseRomFsPath);
            AddRoot(paths.BaseExeFsPath);
            AddRoot(paths.ScarletVioletSupportFolderPath);
            AddRoot(paths.PokemonLegendsZASupportFolderPath);
            if (!string.IsNullOrWhiteSpace(paths.SaveFilePath)) Add(new FileInfo(paths.SaveFilePath));
            if (!string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            {
                var identity = Path.Combine(paths.BaseExeFsPath, "main.npdm");
                if (File.Exists(identity))
                {
                    using var stream = File.OpenRead(identity);
                    if (stream.Length > 1024 * 1024) return null;
                    hash.AppendData(SHA256.HashData(stream));
                }
            }

            AddRoot(paths.OutputRootPath);
            if (!string.IsNullOrWhiteSpace(paths.OutputRootPath) && Directory.Exists(paths.OutputRootPath))
            {
                var pending = new Stack<string>();
                pending.Push(paths.OutputRootPath);
                var count = 0;
                while (pending.TryPop(out var directory))
                {
                    foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos()
                        .Take(MaximumOutputEntries - count + 1)
                        .OrderBy(entry => entry.Name, StringComparer.Ordinal))
                    {
                        if (++count > MaximumOutputEntries) return null;
                        Add(entry);
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                        if (entry is DirectoryInfo) pending.Push(entry.FullName);
                    }
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset());

            void AddRoot(string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) { Append("unset"); return; }
                if (File.Exists(path)) { Add(new FileInfo(path)); return; }
                var entry = new DirectoryInfo(path);
                Add(entry);
                if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A cache source is not a physical directory.");
            }
            void Add(FileSystemInfo entry)
            {
                entry.Refresh();
                Append(entry.FullName);
                Append(entry.Exists.ToString());
                if (!entry.Exists) return;
                Append(((int)entry.Attributes).ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(entry.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (entry is FileInfo file) Append(file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            void Append(string value)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(value));
                hash.AppendData([0]);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
