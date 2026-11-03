// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Audio;

namespace KM.Formats.ZA;

public static partial class ZaRestorationVoiceGraph
{
    // Independently parsed v145 switch: these default transition records name no
    // bank objects and are unreachable. EX1/2/3 are explicitly empty states.
    private static readonly uint[] StaraptorOrphanedVoiceConfigurations = [1067512234, 359177528, 19807028];

    private static bool QualifiedStaraptorMegaSwitch(byte[] bytes, SoundBankDocument bank)
        => bytes.Length == 486
            && Convert.ToHexString(SHA256.HashData(bytes)) == "A3E5A4BE35FFA6154572E78A2A354623470B8B50E8C505CEFDC64EE01F1AAB01"
            && !bank.Objects.Any(o => StaraptorOrphanedVoiceConfigurations.Contains(o.Id));
}
