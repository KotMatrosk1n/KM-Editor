// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Audio;

namespace KM.Formats.ZA;

public static partial class ZaRestorationVoiceGraph
{
    // Independently parsed v145 switch: these default transition records name no
    // bank objects and are unreachable. EX1/2/3 are explicitly empty states.
    private static readonly uint[] StaraptorOrphanedVoiceConfigurations = [1067512234, 359177528, 19807028];
    private static readonly uint[] ZeraoraOrphanedVoiceConfigurations = [628673065];

    // Mega Zeraora declares thirteen children and thirteen populated states,
    // plus one neutral transition for an identity absent from the entire bank.
    private static bool QualifiedZeraoraMegaSwitch(byte[] bytes, SoundBankDocument bank)
        => bytes.Length == 458
            && Convert.ToHexString(SHA256.HashData(bytes)) == "D568F3A8100CBF1C1262F8459627C4F10EAF788BB25E6C7013AF6DC14CBEC032"
            && !bank.Objects.Any(o => ZeraoraOrphanedVoiceConfigurations.Contains(o.Id));

    private static bool QualifiedStaraptorMegaSwitch(byte[] bytes, SoundBankDocument bank)
        => bytes.Length == 486
            && Convert.ToHexString(SHA256.HashData(bytes)) == "A3E5A4BE35FFA6154572E78A2A354623470B8B50E8C505CEFDC64EE01F1AAB01"
            && !bank.Objects.Any(o => StaraptorOrphanedVoiceConfigurations.Contains(o.Id));
}
