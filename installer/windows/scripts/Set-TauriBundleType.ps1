# SPDX-License-Identifier: GPL-3.0-only

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

$ErrorActionPreference = 'Stop'

$resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
if ($null -eq $resolvedPath) {
    throw 'The Tauri executable to patch does not exist.'
}

$file = Get-Item -LiteralPath $resolvedPath.Path
if ($file.PSIsContainer -or $file.Length -eq 0) {
    throw 'The Tauri executable must be a nonempty file.'
}

$header = [byte[]]::new(2)
$headerStream = [System.IO.File]::OpenRead($file.FullName)
try {
    $headerCount = $headerStream.Read($header, 0, $header.Length)
} finally {
    $headerStream.Dispose()
}

if ($headerCount -ne 2 -or $header[0] -ne 0x4d -or $header[1] -ne 0x5a) {
    throw 'The Tauri executable is not a Windows PE file.'
}

$signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
    throw 'The Tauri executable already contains Authenticode signature state. Patch its bundle marker before signing it.'
}

if (-not ('Km.Setup.BinaryMarker' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;

namespace Km.Setup
{
    public static class BinaryMarker
    {
        public static long ReplaceExactlyOnce(string path, byte[] original, byte[] replacement)
        {
            if (original == null || replacement == null || original.Length == 0 ||
                original.Length != replacement.Length)
            {
                throw new ArgumentException("The binary markers must have the same nonzero length.");
            }

            byte[] bytes = File.ReadAllBytes(path);
            long match = -1;

            for (int candidate = 0; candidate <= bytes.Length - original.Length; candidate++)
            {
                if (bytes[candidate] != original[0])
                {
                    continue;
                }

                int markerIndex = 1;
                while (markerIndex < original.Length &&
                       bytes[candidate + markerIndex] == original[markerIndex])
                {
                    markerIndex++;
                }

                if (markerIndex != original.Length)
                {
                    continue;
                }

                if (match >= 0)
                {
                    throw new InvalidDataException("The Tauri bundle marker occurs more than once.");
                }

                match = candidate;
            }

            if (match < 0)
            {
                throw new InvalidDataException("The unpatched Tauri bundle marker was not found.");
            }

            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Position = match;
                stream.Write(replacement, 0, replacement.Length);
                stream.Flush(true);

                stream.Position = match;
                byte[] verification = new byte[replacement.Length];
                int read = stream.Read(verification, 0, verification.Length);
                if (read != verification.Length)
                {
                    throw new IOException("The patched Tauri bundle marker could not be read back.");
                }

                for (int index = 0; index < replacement.Length; index++)
                {
                    if (verification[index] != replacement[index])
                    {
                        throw new IOException("The patched Tauri bundle marker failed verification.");
                    }
                }
            }

            return match;
        }
    }
}
'@
}

$encoding = [System.Text.Encoding]::ASCII
$unknownMarker = $encoding.GetBytes('__TAURI_BUNDLE_TYPE_VAR_UNK')
$nsisMarker = $encoding.GetBytes('__TAURI_BUNDLE_TYPE_VAR_NSS')

$offset = [Km.Setup.BinaryMarker]::ReplaceExactlyOnce(
    $file.FullName,
    $unknownMarker,
    $nsisMarker
)

Write-Output "Patched the staged Tauri updater-family marker to NSIS compatibility at byte offset $offset."
