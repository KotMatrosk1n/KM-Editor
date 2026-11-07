// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Audio;
using KM.Formats.SwSh;
using KM.Formats.ZA;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private readonly Dictionary<KM.SwSh.Pokemon.SwShPokemonIdentity, IReadOnlySet<string>> silentVoiceStates = new();

        internal IReadOnlyList<SwShRestorationContribution> Voices(IReadOnlyList<KM.SwSh.Pokemon.SwShPokemonIdentity> identities)
        {
            if (identities.All(i => Profile(i).PlaAssets)) return PlaVoices(identities);
            var audioRoot = Path.Combine(donorRoot, "audio", "wwise", "bin");
            var directories = Directory.EnumerateDirectories(audioRoot).Take(33).ToArray();
            if (directories.Length > 32) throw new InvalidDataException("Donor audio directory count exceeds its budget.");
            var banks = directories.Prepend(audioRoot).Select(path => Path.Combine(path, "PM_VOICE.bnk"))
                .Where(System.IO.File.Exists).ToArray();
            if (banks.Length != 1) throw new InvalidDataException("Donor voice bank is missing or ambiguous.");
            var relative = Path.GetRelativePath(donorRoot, banks[0]).Replace('\\', '/');
            var bytes = archive.Read(relative, 64 * 1024 * 1024);
            Fingerprint("donor", relative, bytes); var source = new SoundBankDocument(bytes);
            var targetBank = new SoundBankDocument(Native(SwShRestorationComposer.VoicesPath[6..]));
            if (source.Version != 145 || targetBank.Version != 128) throw new InvalidDataException("Voice conversion is not defined for these sound bank versions.");
            var directory = Path.GetDirectoryName(banks[0])!;
            ZaRestorationVoiceContext? normalContext = null;
            if (identities.Any(i => (Profile(i).SourceSpecies ?? i.Species) is 669 or 670 || (Profile(i).SourceVoiceForm ?? i.Form) != 0))
            {
                var initPath = Path.GetRelativePath(donorRoot, Path.Combine(directory, "Init.bnk")).Replace('\\', '/');
                var init = archive.Read(initPath, 16 * 1024 * 1024);
                Fingerprint("donor", initPath, init);
                normalContext = ZaRestorationVoiceContext.ReadNormal(init);
            }
            var packages = new Dictionary<ulong, (string Path, SoundPackageMedia Media)>();
            var paths = Directory.EnumerateFiles(directory, "*.pck", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).Take(129).ToArray();
            if (paths.Length > 128) throw new InvalidDataException("Donor audio package count exceeds its budget.");
            foreach (var path in paths)
            {
                using var package = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                foreach (var item in SoundPackageIndex.Read(package, token).Where(m => m.Group == 1 && m.Language == 0))
                    if (!packages.TryAdd(item.Id, (path, item))) throw new InvalidDataException("Donor voice media identity exists in multiple packages.");
            }
            var result = new List<SwShRestorationContribution>();
            foreach (var identity in identities)
            {
                var id = identity.Species;
                IReadOnlySet<uint>? inactiveGroups = null;
                if (id == 491)
                {
                    const uint donorContext = 0xe0515992;
                    var key = BitConverter.GetBytes(donorContext);
                    if (targetBank.Objects.Any(o => o.Payload.AsSpan().IndexOf(key) >= 0))
                        throw new InvalidDataException("Darkrai's donor voice context overlaps a native context.");
                    inactiveGroups = new HashSet<uint> { donorContext };
                }
                var sourceForm = Profile(identity).SourceVoiceForm ?? identity.Form;
                if (id == 26 && identity.Form is 2 or 3)
                {
                    var declared = ZaRestorationVoiceForms.RaichuMega(Donor(ZaRestorationVoiceForms.ParametersPath),
                        Donor(ZaRestorationVoiceForms.CatalogPath), identity.Form,
                        Profile(identity).ResourceStem ?? throw new InvalidDataException("Raichu Mega source model is missing."));
                    if (sourceForm != declared) throw new InvalidDataException("Raichu Mega source voice profile disagrees with its native model parameters.");
                }
                if (id == 801 && identity.Form is 2 or 3)
                {
                    var declared = ZaRestorationVoiceForms.MagearnaMega(Donor(ZaRestorationVoiceForms.ParametersPath),
                        Donor(ZaRestorationVoiceForms.CatalogPath), identity.Form,
                        Profile(identity).ResourceStem ?? throw new InvalidDataException("Magearna Mega source model is missing."));
                    if (sourceForm != declared) throw new InvalidDataException("Magearna Mega voice profile disagrees with its native model parameters.");
                }
                var states = ZaRestorationVoiceGraph.ResolveLayers(source, Profile(identity).SourceSpecies ?? id, sourceForm, inactiveGroups,
                    requireSharedForm: identity.Form != 0 && sourceForm == 0 && !IndependentFloetteColorVoice(identity, Profile(identity)),
                    normalContext: normalContext);
                var media = new Dictionary<string, IReadOnlyList<byte[]>>(StringComparer.Ordinal);
                var playback = new Dictionary<uint, ZaRestorationVoice>();
                foreach (var state in SwShVoiceWriter.States)
                {
                    token.ThrowIfCancellationRequested();
                    var layers = new List<byte[]>();
                    for (var layer = 0; layer < states[state].Count; layer++)
                    {
                        var voice = states[state][layer]; var mediaId = voice.MediaId;
                        byte[] encoded;
                        if (voice.Embedded)
                        {
                            var embedded = source.Media.Where(m => m.Id == mediaId).ToArray();
                            if (embedded.Length != 1 || embedded[0].Bytes.Length > 32 * 1024 * 1024)
                                throw new InvalidDataException("Embedded donor cry is missing or oversized.");
                            encoded = embedded[0].Bytes;
                            Fingerprint("donor-media", relative + "/" + mediaId.ToString(CultureInfo.InvariantCulture), encoded);
                        }
                        else
                        {
                            if (!packages.TryGetValue(mediaId, out var entry) || entry.Media.Length > 32 * 1024 * 1024) throw new InvalidDataException("Donor cry media is unavailable or oversized.");
                            using var input = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read); input.Position = entry.Media.Offset;
                            encoded = new byte[entry.Media.Length]; input.ReadExactly(encoded);
                            Fingerprint("donor-media", Path.GetRelativePath(donorRoot, entry.Path).Replace('\\', '/') + "/" + mediaId.ToString(CultureInfo.InvariantCulture), encoded);
                        }
                        var decoded = PtAdpcmDecoder.Decode(encoded, token);
                        layers.Add(SoundBankDocument.EncodePcm16(decoded.Samples, decoded.Channels, decoded.SampleRate));
                        playback.Add(SwShVoiceWriter.MediaId(id, state, identity.Form, layer), voice);
                    }
                    media.Add(state, layers);
                }
                var silent = states.Where(p => p.Value.Count == 0).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
                silentVoiceStates.Add(identity, silent);
                var contribution = SwShVoiceWriter.CreateLayered(targetBank, id, 7, media, identity.Form, silent);
                result.AddRange(contribution.Objects.Select(o => o.Type == 2
                    ? Gain(o, playback[BinaryPrimitives.ReadUInt32LittleEndian(o.Payload.AsSpan(5))].GainDecibels,
                        playback[BinaryPrimitives.ReadUInt32LittleEndian(o.Payload.AsSpan(5))].VolumeDecibels) : o).Select(o => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath, SwShRestorationWriter.Voices,
                    string.Create(CultureInfo.InvariantCulture, $"{o.Type}:{o.Id}"), null, o.Payload)));
                result.AddRange(contribution.Media.Select(m => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath, SwShRestorationWriter.Voices,
                    "media:" + m.Id.ToString(CultureInfo.InvariantCulture), null, m.Bytes)));
            }
            return result;
        }

        private static bool IndependentFloetteColorVoice(KM.SwSh.Pokemon.SwShPokemonIdentity identity, SpeciesProfile profile)
        {
            if (identity.Species != 670 || identity.Form is < 1 or > 4) return false;
            // These exact color resources use FORM00; FORM01 and FORM02 are
            // independent Eternal Flower and Mega cries in the donor bank.
            if (profile.SourceVoiceForm != 0 || profile.SourceSpecies is not (null or 670)
                || profile.ResourceStem != $"pm0714_{11 + identity.Form:00}_00"
                || profile.OutputStem != $"pm0670_{identity.Form:00}")
                throw new InvalidDataException("Floette color voice source identity is unsupported.");
            return true;
        }

        private static SoundBankObject Gain(SoundBankObject sound, float decibels, float volume = 0)
        {
            if (decibels == 0 && volume == 0) return sound;
            var source = sound.Payload;
            // The selected version 128 PCM template has no local properties.
            // Preserve sample data and carry source makeup gain as a bank property.
            if (!float.IsFinite(decibels) || decibels is < -96 or > 24 || !float.IsFinite(volume) || volume is < -96.3f or > 0 || source.Length < 28
                || source[14] != 0 || source[15] != 0 || source[16] != 0 || source[25] != 0 || source[26] != 0)
                throw new InvalidDataException("Native voice gain requires another target adapter.");
            var properties = volume == 0 ? 1 : 2;
            var bytes = new byte[source.Length + properties * 5]; source.AsSpan(0, 27).CopyTo(bytes);
            bytes[26] = checked((byte)properties); bytes[27] = 6;
            if (properties == 2) bytes[28] = 0;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(27 + properties), decibels);
            if (properties == 2) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(31 + properties), volume);
            source.AsSpan(27).CopyTo(bytes.AsSpan(27 + properties * 5));
            return sound with { Payload = bytes };
        }
    }
}
