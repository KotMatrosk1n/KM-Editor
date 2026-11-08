// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Formats.Audio;
using KM.Formats.PLA;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        private IReadOnlyList<SwShRestorationContribution> PlaVoices(IReadOnlyList<SwShPokemonIdentity> identities)
        {
            const string folder = "bin/sound/NX64/wwise/bank/";
            var source = new SoundBankDocument(Donor(folder + "PM_VOICE.bnk"));
            var native = new SoundBankDocument(Native(SwShRestorationComposer.VoicesPath[6..]));
            if (native.Version != 128) throw new InvalidDataException("The native cry bank version is unsupported.");
            var packages = new Dictionary<ulong, (string Path, SoundPackageMedia Media, long Length, long Ticks)>();
            var paths = Directory.EnumerateFiles(Path.Combine(donorRoot, folder), "*.pck").Order(StringComparer.Ordinal).Take(129).ToArray();
            if (paths.Length > 128) throw new InvalidDataException("PLA audio packages exceed their budget.");
            foreach (var path in paths)
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var ticks = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                foreach (var item in SoundPackageIndex.Read(input, token).Where(m => m.Group == 1 && m.Language == 0))
                    if (!packages.TryAdd(item.Id, (path, item, input.Length, ticks)))
                        throw new InvalidDataException("PLA cry media has an ambiguous identity.");
            }
            var result = new List<SwShRestorationContribution>();
            foreach (var identity in identities)
            {
                var states = PlaRestorationVoiceGraph.Resolve(source, identity.Species);
                var decoded = new Dictionary<uint, byte[]>(); var media = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var state in SwShVoiceWriter.States)
                {
                    token.ThrowIfCancellationRequested(); var id = states[state];
                    if (!decoded.TryGetValue(id, out var pcm))
                    {
                        if (!packages.TryGetValue(id, out var entry) || entry.Media.Length > 32 * 1024 * 1024)
                            throw new InvalidDataException("PLA cry media is unavailable or oversized.");
                        ReportResource(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(entry.Path)}/{id}.wem"));
                        using var input = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (input.Length != entry.Length || System.IO.File.GetLastWriteTimeUtc(entry.Path).Ticks != entry.Ticks)
                            throw new InvalidDataException("PLA cry package changed during preparation.");
                        input.Position = entry.Media.Offset; var encoded = new byte[entry.Media.Length]; input.ReadExactly(encoded);
                        Fingerprint("donor-media", Path.GetRelativePath(donorRoot, entry.Path).Replace('\\', '/') + "/" + id.ToString(CultureInfo.InvariantCulture), encoded);
                        // PLA's packed Vorbis matches the native SwSh WEM contract.
                        // Preserve the authored media without a lossy re-encode.
                        SwShVorbisVoice.Validate(encoded);
                        pcm = encoded; decoded.Add(id, pcm);
                    }
                    media.Add(state, pcm);
                }
                var contribution = SwShVoiceWriter.CreateVorbis(native, identity.Species, 7, media, identity.Form);
                result.AddRange(contribution.Objects.Select(o => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath,
                    SwShRestorationWriter.Voices, $"{o.Type}:{o.Id}", null, o.Payload)));
                result.AddRange(contribution.Media.Select(m => new SwShRestorationContribution(SwShRestorationComposer.VoicesPath,
                    SwShRestorationWriter.Voices, "media:" + m.Id.ToString(CultureInfo.InvariantCulture), null, m.Bytes)));
            }
            return result;
        }
    }
}
