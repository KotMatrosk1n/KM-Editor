// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.SwSh.Models;

internal sealed class SwShModelSource(OpenedProject project, long maximumBytes = 128L * 1024 * 1024, int maximumReads = 1024)
{
    private readonly Dictionary<string, byte[]?> observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SwShGfPackFile> packs = new(StringComparer.Ordinal);
    private long bytesRead;
    private int reads;

    internal byte[] Read(string relative, string? archive = null)
    {
        relative = Canonical(relative);
        if (project.Paths.OutputRootPath is { Length: > 0 } output && Physical(output, "romfs/" + relative) is { } layered) return layered;
        if (project.Paths.BaseRomFsPath is { Length: > 0 } root && Physical(root, relative) is { } loose) return loose;
        if (archive is null) throw new FileNotFoundException("Model resource is unavailable.");
        archive = Canonical(archive);
        if (!packs.TryGetValue(archive, out var pack))
        {
            pack = SwShGfPackFile.ParseBoundedReadOnly(Read(archive), 4096, 128L * 1024 * 1024);
            packs.Add(archive, pack);
        }
        if (!pack.TryGetFileByName(Path.GetFileName(relative), 32 * 1024 * 1024, out var data))
            throw new FileNotFoundException("Model package dependency is unavailable.");
        Admit(data.Length);
        return data;
    }

    internal void Verify()
    {
        foreach (var (path, before) in observed)
        {
            var bytes = ReadPhysical(path);
            if (before is null ? bytes is not null : bytes is null || !SHA256.HashData(bytes).AsSpan().SequenceEqual(before))
                throw new InvalidDataException("Model sources changed while loading. Reload the model.");
        }
    }

    private byte[]? Physical(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var bytes = ReadPhysical(path);
        var hash = bytes is null ? null : SHA256.HashData(bytes);
        if (observed.TryGetValue(path, out var prior) && (prior is null ? hash is not null : hash is null || !prior.AsSpan().SequenceEqual(hash)))
            throw new InvalidDataException("Model sources changed while loading. Reload the model.");
        observed[path] = hash;
        return bytes;
    }

    private byte[]? ReadPhysical(string path)
    {
        if (++reads > maximumReads) throw new InvalidDataException("Model dependency count exceeds the preview budget.");
        FileSystemInfo? current = new FileInfo(path);
        while (current is not null)
        {
            if (!string.IsNullOrEmpty(current.LinkTarget)) throw new InvalidDataException("Linked model resources are unsupported.");
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
        }
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        using (stream)
        {
            if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("Model resource exceeds the preview budget.");
            Admit(stream.Length);
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new InvalidDataException("Model resource changed while reading.");
            return bytes;
        }
    }

    private void Admit(long size)
    {
        bytesRead = checked(bytesRead + size);
        if (bytesRead > maximumBytes) throw new InvalidDataException("Model resources exceed the preview budget.");
    }

    internal static string Canonical(string relative)
    {
        var result = TrinityPreviewReader.Resolve("catalog", relative);
        if (result != relative || !result.StartsWith("bin/", StringComparison.Ordinal)
            || result.Split('/').Any(part => part.EndsWith('.') || part.EndsWith(' ')))
            throw new InvalidDataException("Model resource path is unsupported.");
        return result;
    }
}
