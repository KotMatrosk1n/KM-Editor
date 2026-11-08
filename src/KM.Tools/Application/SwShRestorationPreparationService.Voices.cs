// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Audio;
using KM.Formats.SwSh;
using KM.Formats.SV;
using KM.SwSh.Restoration;
using KM.SwSh.Pokemon;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal IReadOnlyList<SwShRestorationContribution> Voices(IReadOnlyList<SwShPokemonIdentity> identities)
        {
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
            if (source.Version != 140 || targetBank.Version != 128) throw new InvalidDataException("Voice conversion is not defined for these sound bank versions.");
            var directory = Path.GetDirectoryName(banks[0])!;
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
                var sourceForm = Profile(identity).SourceVoiceForm ?? identity.Form;
                var states = SvRestorationVoiceGraph.ResolvePlayback(source, Profile(identity).SourceSpecies ?? identity.Species, sourceForm,
                    requireSharedForm: identity.Form != 0 && sourceForm == 0);
                var media = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var state in SwShVoiceWriter.States)
                {
                    token.ThrowIfCancellationRequested();
                    var mediaId = states[state].MediaId;
                    if (!packages.TryGetValue(mediaId, out var entry) || entry.Media.Length > 32 * 1024 * 1024) throw new InvalidDataException("Donor cry media is unavailable or oversized.");
                    ReportResource(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(entry.Path)}/{mediaId}.wem"));
                    using var input = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read); input.Position = entry.Media.Offset;
                    var encoded = new byte[entry.Media.Length]; input.ReadExactly(encoded);
                    Fingerprint("donor-media", Path.GetRelativePath(donorRoot, entry.Path).Replace('\\', '/') + "/" + mediaId.ToString(CultureInfo.InvariantCulture), encoded);
                    var decoded = PtAdpcmDecoder.Decode(encoded, token); media.Add(state, SoundBankDocument.EncodePcm16(decoded.Samples, decoded.Channels, decoded.SampleRate));
                }
                var contribution = SwShVoiceWriter.Create(targetBank, identity.Species, 7, media, identity.Form);
                var playback = states.ToDictionary(p => SwShVoiceWriter.MediaId(identity.Species, p.Key, identity.Form), p => p.Value);
                result.AddRange(contribution.Objects.Select(o => o.Type == 2 ? Playback(o, playback[BinaryPrimitives.ReadUInt32LittleEndian(o.Payload.AsSpan(5))]) : o).Select(o => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath, SwShRestorationWriter.Voices,
                    string.Create(CultureInfo.InvariantCulture, $"{o.Type}:{o.Id}"), null, o.Payload)));
                result.AddRange(contribution.Media.Select(m => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath, SwShRestorationWriter.Voices,
                    "media:" + m.Id.ToString(CultureInfo.InvariantCulture), null, m.Bytes)));
            }
            return result;
        }

        private static SoundBankObject Playback(SoundBankObject sound, SvRestorationVoice playback)
        {
            sound = Gain(sound, playback.GainDecibels);
            if (playback.PitchMinimum == 0 && playback.PitchMaximum == 0) return sound;
            var source = sound.Payload;
            if (source.Length < 28 || source[26] > 1) throw new InvalidDataException("Native cry playback properties are unsupported.");
            var at = 27 + source[26] * 5;
            if (at >= source.Length || source[at] != 0) throw new InvalidDataException("Native cry already contains random properties.");
            var bytes = new byte[source.Length + 9]; source.AsSpan(0, at).CopyTo(bytes);
            bytes[at] = 1; bytes[at + 1] = 2;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 2), playback.PitchMinimum);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 6), playback.PitchMaximum);
            source.AsSpan(at + 1).CopyTo(bytes.AsSpan(at + 10));
            return sound with { Payload = bytes };
        }

        private static SoundBankObject Gain(SoundBankObject sound, float decibels)
        {
            if (decibels == 0) return sound;
            var source = sound.Payload;
            // The selected version 128 PCM template has no local properties.
            // Preserve sample data and carry source makeup gain as a bank property.
            if (!float.IsFinite(decibels) || decibels is < -96 or > 24 || source.Length < 28
                || source[14] != 0 || source[15] != 0 || source[16] != 0 || source[25] != 0 || source[26] != 0)
                throw new InvalidDataException("Native voice gain requires another target adapter.");
            var bytes = new byte[source.Length + 5]; source.AsSpan(0, 27).CopyTo(bytes);
            bytes[26] = 1; bytes[27] = 6; BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(28), decibels);
            source.AsSpan(27).CopyTo(bytes.AsSpan(32));
            return sound with { Payload = bytes };
        }
    }
}
