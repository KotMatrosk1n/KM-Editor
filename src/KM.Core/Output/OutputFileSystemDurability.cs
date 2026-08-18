// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KM.Core.Output;

internal static class OutputFileSystemDurability
{
    private const uint MoveFileReplaceExisting = 0x00000001;
    private const uint MoveFileWriteThrough = 0x00000008;
    private const int AtCurrentWorkingDirectory = -100;
    private const uint LinuxRenameNoReplace = 0x00000001;
    private const uint MacOsRenameExclusive = 0x00000004;

    public static void Move(string source, string destination, bool overwrite)
    {
        if (OperatingSystem.IsWindows())
        {
            var flags = MoveFileWriteThrough
                        | (overwrite ? MoveFileReplaceExisting : 0);
            if (!MoveFileEx(ToNativeWindowsPath(source), ToNativeWindowsPath(destination), flags))
            {
                throw new IOException(
                    "A durable output file move failed.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            return;
        }

        MoveUnix(source, destination, overwrite, "file");
        FlushMoveParents(source, destination);
    }

    public static void MoveDirectory(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(
                    ToNativeWindowsPath(source),
                    ToNativeWindowsPath(destination),
                    MoveFileWriteThrough))
            {
                throw new IOException(
                    "A durable output directory move failed.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            return;
        }

        MoveUnix(source, destination, overwrite: false, "directory");
        FlushMoveParents(source, destination);
    }

    public static void FlushParent(string path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            FlushDirectory(parent);
        }
    }

    public static void FlushDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            // File streams are flushed with FlushFileBuffers, while namespace
            // publication uses MoveFileEx with MOVEFILE_WRITE_THROUGH.
            return;
        }

        using var handle = File.OpenHandle(
            directory,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileOptions.None);
        RandomAccess.FlushToDisk(handle);
    }

    private static void FlushMoveParents(string source, string destination)
    {
        var sourceParent = Path.GetDirectoryName(Path.GetFullPath(source));
        var destinationParent = Path.GetDirectoryName(Path.GetFullPath(destination));
        // Persist the destination name first. If a cross-directory rename is
        // interrupted between barriers, retaining both names is recoverable;
        // persisting removal first could lose the only durable name.
        if (!string.IsNullOrWhiteSpace(destinationParent))
        {
            FlushDirectory(destinationParent);
        }

        if (!string.IsNullOrWhiteSpace(sourceParent)
            && !string.Equals(sourceParent, destinationParent, StringComparison.Ordinal))
        {
            FlushDirectory(sourceParent);
        }
    }

    private static void MoveUnix(
        string source,
        string destination,
        bool overwrite,
        string entryKind)
    {
        int result;
        if (overwrite)
        {
            result = Rename(source, destination);
        }
        else if (OperatingSystem.IsLinux())
        {
            result = RenameAt2(
                AtCurrentWorkingDirectory,
                source,
                AtCurrentWorkingDirectory,
                destination,
                LinuxRenameNoReplace);
        }
        else if (OperatingSystem.IsMacOS())
        {
            result = RenameExclusive(source, destination, MacOsRenameExclusive);
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Durable no-replace output moves require a supported local filesystem platform.");
        }

        if (result != 0)
        {
            throw new IOException(
                $"A durable output {entryKind} move failed.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    private static string ToNativeWindowsPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            return fullPath;
        }

        return fullPath.StartsWith("\\\\", StringComparison.Ordinal)
            ? "\\\\?\\UNC\\" + fullPath[2..]
            : "\\\\?\\" + fullPath;
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string existingFileName,
        string newFileName,
        uint flags);

    [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
    private static extern int Rename(string existingPath, string newPath);

    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameAt2(
        int existingDirectory,
        string existingPath,
        int newDirectory,
        string newPath,
        uint flags);

    [DllImport("libc", EntryPoint = "renamex_np", SetLastError = true)]
    private static extern int RenameExclusive(
        string existingPath,
        string newPath,
        uint flags);
}
