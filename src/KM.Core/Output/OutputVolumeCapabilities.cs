// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KM.Core.Output;

internal static class OutputVolumeCapabilities
{
    private const uint PersistentAcls = 0x00000008;

    internal static bool? ReadElevation(SafeAccessTokenHandle token)
    {
        return GetTokenInformation(token, 20, out var value, sizeof(int), out _) ? value != 0 : null;
    }

    internal static OutputStorageDiagnostic Describe(string path, string role)
    {
        if (!OperatingSystem.IsWindows()) return new(role, "unsupported", "unknown", null, null, null);
        if (!SetThreadErrorMode(GetThreadErrorMode() | 1, out var previous))
            return new(role, "unavailable", "unknown", null, null, Marshal.GetLastWin32Error());
        try
        {
            var volume = new StringBuilder(32_768);
            var format = new StringBuilder(261);
            if (!GetVolumePathName(path, volume, volume.Capacity)
                || !GetVolumeInformation(volume.ToString(), null, 0, out _, out _, out var flags, format, format.Capacity))
                return new(role, "unavailable", "unknown", null, null, Marshal.GetLastWin32Error());
            var fileSystem = format.ToString().ToUpperInvariant() switch
            {
                "NTFS" => "ntfs", "REFS" => "refs", "FAT" => "fat", "FAT32" => "fat32", "EXFAT" => "exfat", _ => "other",
            };
            var freeOk = GetDiskFreeSpaceEx(volume.ToString(), out var available, out _, out _);
            return new(role, freeOk ? "available" : "partial", fileSystem, (flags & PersistentAcls) != 0,
                freeOk ? (long)Math.Min(available, long.MaxValue) : null, freeOk ? null : Marshal.GetLastWin32Error());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(role, "unavailable", "unknown", null, null, null); }
        finally { SetThreadErrorMode(previous, out _); }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        out int information, int length, out int returnLength);

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string path, out ulong available, out ulong total, out ulong free);

    public static bool RequiresPrivateWorkspaceJournal(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        // Missing removable media must return an error, not open a system dialog.
        if (!SetThreadErrorMode(GetThreadErrorMode() | 0x00000001, out var previousMode))
            throw new OutputPathSecurityException();
        try
        {
            var existing = Path.GetFullPath(path);
            while (!Directory.Exists(existing) && !File.Exists(existing))
                existing = Path.GetDirectoryName(existing) ?? throw new OutputPathSecurityException();
            var volume = new StringBuilder(32_768);
            var format = new StringBuilder(261);
            if (!GetVolumePathName(existing, volume, volume.Capacity)
                || !GetVolumeInformation(volume.ToString(), null, 0, out _, out _, out var flags, format, format.Capacity))
                return false; // Unclassified storage must still pass the existing owner and ACL checks.
            return RequiresPrivateWorkspaceJournal(format.ToString(), flags);
        }
        finally { SetThreadErrorMode(previousMode, out _); }
    }

    private static bool RequiresPrivateWorkspaceJournal(string format, uint flags)
    {
        if ((flags & PersistentAcls) != 0) return false;
        if (format.Equals("FAT", StringComparison.OrdinalIgnoreCase)
            || format.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
            || format.Equals("exFAT", StringComparison.OrdinalIgnoreCase)) return true;
        throw new OutputPathSecurityException();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint newMode, out uint oldMode);

    [DllImport("kernel32.dll")]
    private static extern uint GetThreadErrorMode();

    [DllImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string fileName, StringBuilder volumePathName, int bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(string rootPathName, StringBuilder? volumeName, int volumeNameSize,
        out uint serialNumber, out uint maximumComponentLength, out uint fileSystemFlags,
        StringBuilder fileSystemName, int fileSystemNameSize);
}
