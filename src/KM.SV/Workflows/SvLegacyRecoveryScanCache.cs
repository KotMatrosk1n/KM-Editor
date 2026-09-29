// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Output;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Trainers;
using System.Text;

namespace KM.SV.Workflows;

// Only clean scan results are cached. Actual repairs always read and authenticate
// their sources afresh at Review and Apply. No editor load invokes this scan.
internal sealed class SvLegacyRecoveryScanCache
{
    private readonly object sync = new();
    private ProjectPaths? cleanProject;
    private string? cleanStamp;

    internal bool IsClean(ProjectPaths paths, string? stamp)
    {
        lock (sync) return stamp is not null && paths == cleanProject && stamp == cleanStamp;
    }

    internal void RememberClean(ProjectPaths paths, string? stamp)
    {
        lock (sync) { cleanProject = paths; cleanStamp = stamp; }
    }

    internal static string? Capture(ProjectPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath)) return null;
        try
        {
            var stamp = new StringBuilder();
            var metadata = new OutputWorkspaceStorage(paths).MetadataRoot;
            foreach (var name in new[] { "ownership.json", "history.json" })
            {
                Append(Path.Combine(metadata, name));
                Append(Path.Combine(paths.OutputRootPath, ".km", name));
            }
            foreach (var path in SvTrainersEditSessionService.LegacyPartnerScenePaths()
                .Concat([SvDataPaths.TrainerDataArray, SvDataPaths.EventBattlePokemonArray,
                    "script/lua/bin/release/main/main.blua", "arc/data.trpfd", "arc/data.trpfs"]))
            {
                Append(Path.Combine(paths.OutputRootPath, "romfs", path));
                Append(Path.Combine(paths.OutputRootPath, path));
                if (!string.IsNullOrWhiteSpace(paths.BaseRomFsPath)) Append(Path.Combine(paths.BaseRomFsPath, path));
            }
            return stamp.ToString();

            void Append(string path)
            {
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                        throw new IOException("An output scan dependency is not a regular file.");
                    var info = new FileInfo(path);
                    stamp.Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append(';');
                }
                catch (FileNotFoundException) { stamp.Append("missing;"); }
                catch (DirectoryNotFoundException) { stamp.Append("missing;"); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }
}
