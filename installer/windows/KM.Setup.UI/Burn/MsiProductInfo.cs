// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Text;
using WixToolset.BootstrapperApplicationApi;

namespace KM.Setup.UI.Burn;

internal static class MsiProductInfo
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorNoMoreItems = 259;
    private const int ProductCodeBufferLength = 39;
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
        var versionValue = TryGetProductProperty(productCode, "VersionString");
        if (installLocation is null ||
            !int.TryParse(assignmentType, out var assignment) ||
            assignment is < 0 or > 1 ||
            !Version.TryParse(versionValue, out var version))
        {
            return null;
        }

        return new MsiProductRegistration(
            assignment == 1 ? BundleScope.PerMachine : BundleScope.PerUser,
            installLocation,
            NormalizeVersion(version));
    }

    public static bool IsRegistered(string productCode)
    {
        return TryGetProductProperty(productCode, "VersionString") is not null;
    }

    public static IReadOnlyList<string>? TryGetRelatedProductCodes(string upgradeCode)
    {
        var productCodes = new List<string>();
        for (uint index = 0; ; index++)
        {
            var productCode = new StringBuilder(ProductCodeBufferLength);
            var result = MsiEnumRelatedProducts(upgradeCode, 0, index, productCode);
            if (result == ErrorNoMoreItems)
            {
                return productCodes;
            }

            if (result != ErrorSuccess || productCode.Length == 0)
            {
                return null;
            }

            productCodes.Add(productCode.ToString());
        }
    }

    private static string? TryGetProductProperty(string productCode, string property)
    {
        var value = new StringBuilder(MaximumInstallerPropertyLength);
        var length = (uint)value.Capacity;
        var result = MsiGetProductInfo(productCode, property, value, ref length);
        return result == ErrorSuccess ? value.ToString() : null;
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiEnumRelatedProductsW")]
    private static extern uint MsiEnumRelatedProducts(
        string upgradeCode,
        uint reserved,
        uint productIndex,
        StringBuilder productCode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiGetProductInfoW")]
    private static extern uint MsiGetProductInfo(
        string productCode,
        string property,
        StringBuilder value,
        ref uint valueLength);
}

internal sealed record MsiProductRegistration(
    BundleScope Scope,
    string InstallFolder,
    Version Version);
