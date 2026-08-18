// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Semantics;

namespace KM.Core.Output;

/// <summary>
/// The exact existence, length, and SHA-256 fingerprint of one output file.
/// </summary>
public sealed record OutputFileState
{
    public OutputFileState(bool exists, string? sha256, long lengthBytes)
    {
        if (exists)
        {
            Sha256 = SemanticContractGuards.Sha256Fingerprint(
                sha256 ?? throw new ArgumentNullException(nameof(sha256)),
                nameof(sha256));
            if (lengthBytes < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lengthBytes),
                    lengthBytes,
                    "An existing output file cannot have a negative length.");
            }
        }
        else
        {
            if (sha256 is not null || lengthBytes != 0)
            {
                throw new ArgumentException(
                    "A missing output file cannot declare a fingerprint or length.");
            }

            Sha256 = null;
        }

        Exists = exists;
        LengthBytes = lengthBytes;
    }

    public bool Exists { get; }

    public string? Sha256 { get; }

    public long LengthBytes { get; }

    public static OutputFileState Missing { get; } = new(false, null, 0);

    public static OutputFileState Existing(string sha256, long lengthBytes)
    {
        return new OutputFileState(true, sha256, lengthBytes);
    }
}
