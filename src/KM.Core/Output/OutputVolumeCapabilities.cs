// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Text;

namespace KM.Core.Output;

internal static class OutputVolumeCapabilities
{
    private const uint PersistentAcls = 0x00000008;

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
