// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Text;
using WixToolset.BootstrapperApplicationApi;

namespace KM.Setup.UI.Burn;

internal static class MsiProductInfo
{
    private const uint ErrorSuccess = 0;
    private const int MaximumInstallerPropertyLength = 32_767;

    public static string? TryGetInstallLocation(string productCode, bool mustExist = true)
    {
        var value = TryGetProductProperty(productCode, "InstallLocation");
        return InstallPathPolicy.TryNormalizeLocalFolder(value, mustExist);
    }

    public static MsiProductRegistration? TryGetRegistration(string productCode)
    {
        // An exact registered product remains authoritative even when its files
        // were manually removed. Repair must be able to recreate the folder and
        // uninstall must still be able to retire the registration.
        var installLocation = TryGetInstallLocation(productCode, mustExist: false);
        var assignmentType = TryGetProductProperty(productCode, "AssignmentType");
        if (installLocation is null || !int.TryParse(assignmentType, out var assignment) || assignment is < 0 or > 1)
        {
            return null;
        }

        return new MsiProductRegistration(
            assignment == 1 ? BundleScope.PerMachine : BundleScope.PerUser,
            installLocation);
    }

    public static bool IsRegistered(string productCode)
    {
        return TryGetProductProperty(productCode, "VersionString") is not null;
    }

    private static string? TryGetProductProperty(string productCode, string property)
    {
        var value = new StringBuilder(MaximumInstallerPropertyLength);
        var length = (uint)value.Capacity;
        var result = MsiGetProductInfo(productCode, property, value, ref length);
        return result == ErrorSuccess ? value.ToString() : null;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiGetProductInfoW")]
    private static extern uint MsiGetProductInfo(
        string productCode,
        string property,
        StringBuilder value,
        ref uint valueLength);
}

internal sealed record MsiProductRegistration(BundleScope Scope, string InstallFolder);
