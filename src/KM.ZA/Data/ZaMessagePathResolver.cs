// SPDX-License-Identifier: GPL-3.0-only

namespace KM.ZA.Data;

internal static class ZaMessagePathResolver
{
    public const string MessageRootPath = "ik_message/dat";
    private const string DefaultMessageExtension = ".dat";

    public static string? TryCreateMessageDatPathFromPackName(string packName, string language)
    {
        const string packSuffix = ".trpak";

        if (string.IsNullOrWhiteSpace(packName))
        {
            return null;
        }

        var normalized = packName.Replace('\\', '/').TrimStart('/');
        var prefix = $"arc/ik_messagedat{language}";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(packSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tail = normalized[prefix.Length..^packSuffix.Length].TrimStart('/', '\\');
        var folder = string.Empty;
        var fileName = string.Empty;
        if (tail.StartsWith("common", StringComparison.OrdinalIgnoreCase))
        {
            folder = "common";
            fileName = tail["common".Length..];
        }
        else if (tail.StartsWith("script", StringComparison.OrdinalIgnoreCase))
        {
            folder = "script";
            fileName = tail["script".Length..];
        }
        else if (tail.StartsWith("sk", StringComparison.OrdinalIgnoreCase))
        {
            folder = "sk";
            fileName = tail["sk".Length..];
        }

        fileName = fileName.TrimStart('/', '\\').Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var extension = Path.GetExtension(fileName);
        if (string.Equals(extension, ".dat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".tbl", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.ChangeExtension(fileName, null);
        }
        else
        {
            extension = DefaultMessageExtension;
        }

        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : $"{MessageRootPath}/{language}/{folder}/{fileName}{extension}";
    }
}
