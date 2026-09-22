// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.ModMerger;
using KM.Formats.SV;
using KM.Formats.ZA;

namespace KM.Tools.ModMerging;

internal sealed class MergeVanilla(MergeWorkspaceRequest request, string game) : IDisposable
{
    private SvTrinityArchive? sv;
    private ZaTrinityArchive? za;
    private long observedBytes;
    public Dictionary<string, byte[]> Observed { get; } = new(StringComparer.OrdinalIgnoreCase);

    public byte[]? Read(string path)
    {
        try { return ReadCore(path); }
        catch (FileNotFoundException exception) when (exception.FileName?.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == true)
        { throw new MergeInputException(MergeWorkspaceErrorCodes.SupportRequired, "Select the compression support folder to read the original compressed data."); }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        { throw new MergeInputException(MergeWorkspaceErrorCodes.SupportRequired, "The compression support library could not be loaded."); }
    }

    private byte[]? ReadCore(string path)
    {
        if (request.Mode != "advanced") return null;
        if (Observed.TryGetValue(path, out var cached)) return cached;
        var root = path.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase) ? request.BaseRomFs
            : path.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase) ? ResolveExeFsRoot(request) : null;
        if (string.IsNullOrWhiteSpace(root)) return null;
        var relative = path[(path.IndexOf('/') + 1)..];
        var physical = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        MergeInputs.RejectLinks(physical);
        byte[]? bytes = null;
        if (File.Exists(physical))
        {
            if (new FileInfo(physical).Length > 256 * 1024 * 1024)
                throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "An original file exceeds the supported merge size.");
            bytes = File.ReadAllBytes(physical);
        }
        else if (path.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
                 && File.Exists(Path.Combine(root, "arc", "data.trpfd")))
        {
            if (MergeInputs.Family(game) == "sv")
            {
                sv ??= SvTrinityArchive.Open(root, request.SupportFolder, maximumIndexBytes: 64 * 1024 * 1024, maximumPackBytes: 512 * 1024 * 1024);
                if (sv.TryReadFile(relative, 256 * 1024 * 1024, out var result)) bytes = result;
            }
            else if (MergeInputs.Family(game) == "za")
            {
                za ??= ZaTrinityArchive.Open(root, request.SupportFolder, maximumIndexBytes: 64 * 1024 * 1024, maximumPackBytes: 512 * 1024 * 1024);
                if (za.TryReadFile(relative, 256 * 1024 * 1024, out var result)) bytes = result;
            }
        }
        if (bytes is not null)
        {
            observedBytes += bytes.Length;
            if (observedBytes > MergeInputs.MaximumBytes) throw new MergeInputException(MergeWorkspaceErrorCodes.LimitExceeded, "The original comparison exceeds the supported memory size.");
            Observed[path] = bytes;
        }
        return bytes;
    }

    public void Dispose() { sv?.Dispose(); za?.Dispose(); }

    internal static string? ResolveExeFsRoot(MergeWorkspaceRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.BaseExeFs)) return request.BaseExeFs;
        if (request.Mode != "advanced" || string.IsNullOrWhiteSpace(request.BaseRomFs)) return null;
        var romFs = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.BaseRomFs));
        var parent = Directory.GetParent(romFs);
        return parent is null ? null : Path.Combine(parent.FullName, "exefs");
    }
}
