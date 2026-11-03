// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Audio;

namespace KM.Formats.SwSh;

public sealed record SwShVoiceContribution(SoundBankObject[] Objects, SoundBankMedia[] Media);

/// <summary>Creates native voice actor, action, and event graphs from locally converted cries.</summary>
public static class SwShVoiceWriter
{
    public static readonly IReadOnlyList<string> States = Array.AsReadOnly(new[]
        { "INDEX", "GLAD1", "GLAD2", "GLAD3", "ANGER", "SAD", "ATK", "SPATK", "EX1", "EX2", "EX3", "EX4", "ENV" });
    private static readonly string[] NativeStates = ["INDEX", "GLAD1", "ANGER", "SAD", "ATK"];

    public static IReadOnlySet<uint> ObjectIds(int species, int form = 0, int layers = 1, IReadOnlySet<string>? silentStates = null)
    {
        if (form is < 0 or > 255 || species is < 1 or > ushort.MaxValue || layers is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(species));
        ValidateSilentStates(silentStates);
        var result = new HashSet<uint>();
        uint Owned(string suffix) => Hash(OwnedName(species, form, suffix));
        foreach (var family in new[] { "", "EV_", "Btl_" })
        {
            var label = family.TrimEnd('_');
            result.Add(Owned(family.Length == 0 ? "parent" : label + "_parent"));
            foreach (var state in family.Length == 0 ? States : NativeStates)
            {
                var prefix = family.Length == 0 ? state : label + "_" + state;
                for (var layer = 0; layer < (silentStates?.Contains(state) == true ? 0 : layers); layer++)
                {
                    var suffix = LayerSuffix(layer);
                    result.Add(Owned(prefix + suffix + "_sound")); result.Add(Owned(prefix + suffix + "_action"));
                }
                var variant = Array.IndexOf(NativeStates, state);
                result.Add(Hash(variant >= 0 ? string.Create(CultureInfo.InvariantCulture, $"Play_PV_{family}{species:000}_{SwShVoiceRouting.EventForm(species, form):00}_{variant:00}")
                    : ExtraEvent(species, form, state)));
            }
        }
        return result;
    }

    public static uint MediaId(int species, string state, int form = 0, int layer = 0)
    {
        if (form is < 0 or > 255 || species is < 1 or > ushort.MaxValue || layer is < 0 or > 1 || !States.Contains(state))
            throw new ArgumentOutOfRangeException(nameof(species), "Choose a supported species and cry state.");
        return Hash(OwnedName(species, form, state + LayerSuffix(layer) + "_media")) & 0x3fffffff;
    }

    private static string LayerSuffix(int layer) => layer == 0 ? "" : "_layer1";

    private static string OwnedName(int species, int form, string suffix) => form == 0
        ? string.Create(CultureInfo.InvariantCulture, $"KM_Restoration_v1_{species}_{suffix}")
        : string.Create(CultureInfo.InvariantCulture, $"KM_Restoration_v1_{species}_form{form}_{suffix}");
    private static string ExtraEvent(int species, int form, string state) => form == 0
        ? string.Create(CultureInfo.InvariantCulture, $"Play_KM_PV_{species:000}_{state}")
        : string.Create(CultureInfo.InvariantCulture, $"Play_KM_PV_{species:000}_{form:00}_{state}");

    public static uint Hash(string name)
    {
        uint hash = 2166136261;
        foreach (var character in System.Text.Encoding.UTF8.GetBytes(name.ToLowerInvariant())) hash = unchecked(hash * 16777619) ^ character;
        return hash;
    }
    public static SwShVoiceContribution Create(SoundBankDocument native, int species, int donor, IReadOnlyDictionary<string, byte[]> pcmMedia, int form = 0)
        => CreateCore(native, species, donor, SingleLayers(pcmMedia), form, false);

    public static SwShVoiceContribution CreateVorbis(SoundBankDocument native, int species, int donor, IReadOnlyDictionary<string, byte[]> media, int form = 0)
        => CreateCore(native, species, donor, SingleLayers(media), form, true);

    public static SwShVoiceContribution CreateLayered(SoundBankDocument native, int species, int donor,
        IReadOnlyDictionary<string, IReadOnlyList<byte[]>> pcmMedia, int form = 0, IReadOnlySet<string>? silentStates = null)
        => CreateCore(native, species, donor, pcmMedia, form, false, silentStates);

    private static void ValidateSilentStates(IReadOnlySet<string>? states)
    {
        if (states is not null && (states.Count == States.Count || states.Any(s => !States.Contains(s))))
            throw new InvalidDataException("Explicit silent cry states are invalid.");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<byte[]>> SingleLayers(IReadOnlyDictionary<string, byte[]> media)
        => media.ToDictionary(p => p.Key, p => (IReadOnlyList<byte[]>)new[] { p.Value }, StringComparer.Ordinal);

    private static SwShVoiceContribution CreateCore(SoundBankDocument native, int species, int donor, IReadOnlyDictionary<string, IReadOnlyList<byte[]>> pcmMedia, int form, bool vorbis, IReadOnlySet<string>? silentStates = null)
    {
        if (form is < 0 or > 255 || species is < 1 or > ushort.MaxValue || donor is < 1 or > 898 || pcmMedia.Count != States.Count || States.Any(s => !pcmMedia.ContainsKey(s)))
            throw new InvalidDataException("Voice restoration requires all declared cry states.");
        ValidateSilentStates(silentStates);
        if (pcmMedia.Any(p => p.Value.Count > 2 || (p.Value.Count == 0) != (silentStates?.Contains(p.Key) == true)))
            throw new InvalidDataException("Cry layer count differs from its declared playback.");
        var objects = new List<SoundBankObject>(); var media = new List<SoundBankMedia>();
        var used = native.Objects.Select(o => o.Id).Concat(native.Media.Select(m => m.Id)).ToHashSet();
        uint Identity(string name, bool mediaId = false)
        {
            var id = Hash(name); if (mediaId) id &= 0x3fffffff;
            if (!used.Add(id)) throw new InvalidDataException("Restored voice identity collides with existing audio.");
            return id;
        }
        uint New(string suffix) => Identity(OwnedName(species, form, suffix));
        SoundBankObject Find(byte type, uint id)
        {
            var found = native.Objects.Where(o => o.Type == type && o.Id == id).ToArray();
            return found.Length == 1 ? found[0] : throw new InvalidDataException("Native voice graph is ambiguous or incomplete.");
        }
        (SoundBankObject Sound, SoundBankObject Action) Donor(string family, int variant)
        {
            var eventName = string.Create(CultureInfo.InvariantCulture, $"Play_PV_{family}{donor:000}_00_{variant:00}");
            var evt = Find(4, Hash(eventName));
            if (evt.Payload.Length != 5 || evt.Payload[0] != 1) throw new InvalidDataException("Native voice event layout is unsupported.");
            var action = Find(3, Read(evt.Payload, 1)); var sound = Find(2, Read(action.Payload, 2));
            if (sound.Payload.Length < 25) throw new InvalidDataException("Native voice sound is truncated.");
            return (sound, action);
        }
        var clips = new Dictionary<string, SoundBankMedia[]>(StringComparer.Ordinal);
        foreach (var state in States)
        {
            var stateClips = new List<SoundBankMedia>();
            for (var layer = 0; layer < pcmMedia[state].Count; layer++)
            {
                var bytes = pcmMedia[state][layer];
                if (vorbis) SwShVorbisVoice.Validate(bytes);
                else if (bytes.Length < 68 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20)) != 0xfffe)
                    throw new InvalidDataException("Restored cry must use native extensible PCM.");
                stateClips.Add(new(Identity(OwnedName(species, form, state + LayerSuffix(layer) + "_media"), true), bytes));
            }
            clips.Add(state, stateClips.ToArray()); media.AddRange(stateClips);
        }
        foreach (var family in new[] { "", "EV_", "Btl_" })
        {
            var familyLabel = family.TrimEnd('_');
            var parent = New(family.Length == 0 ? "parent" : familyLabel + "_parent"); var children = new List<uint>();
            SoundBankObject? originalParent = null;
            var states = family.Length == 0 ? States : NativeStates;
            foreach (var state in states)
            {
                var variant = Array.IndexOf(NativeStates, state); var donorGraph = Donor(family, family.Length == 0 ? 0 : variant);
                var parentId = Read(donorGraph.Sound.Payload, 21);
                var candidates = native.Objects.Where(o => o.Id == parentId && o.Type != 2 && o.Type != 3 && o.Type != 4).ToArray();
                if (candidates.Length != 1 || (originalParent is not null && originalParent.Id != parentId))
                    throw new InvalidDataException("Native voice mixer could not be resolved uniquely.");
                originalParent = candidates[0];
                var prefix = family.Length == 0 ? state : familyLabel + "_" + state;
                var actions = new List<uint>();
                for (var layer = 0; layer < clips[state].Length; layer++)
                {
                    var suffix = LayerSuffix(layer);
                    var sound = New(prefix + suffix + "_sound"); children.Add(sound);
                    var payload = donorGraph.Sound.Payload.ToArray(); var clip = clips[state][layer];
                    Write(payload, 0, vorbis ? 0x00040001U : 0x00010001U); payload[4] = 0; Write(payload, 5, clip.Id); Write(payload, 9, checked((uint)clip.Bytes.Length)); Write(payload, 21, parent);
                    objects.Add(new(2, sound, payload));
                    var action = New(prefix + suffix + "_action"); var actionBytes = donorGraph.Action.Payload.ToArray(); Write(actionBytes, 2, sound);
                    objects.Add(new(3, action, actionBytes)); actions.Add(action);
                }
                var eventName = variant >= 0 ? string.Create(CultureInfo.InvariantCulture, $"Play_PV_{family}{species:000}_{SwShVoiceRouting.EventForm(species, form):00}_{variant:00}")
                    : ExtraEvent(species, form, state);
                var eventBytes = new byte[1 + actions.Count * 4]; eventBytes[0] = checked((byte)actions.Count);
                for (var layer = 0; layer < actions.Count; layer++) Write(eventBytes, 1 + layer * 4, actions[layer]);
                objects.Add(new(4, Identity(eventName), eventBytes));
            }
            var mixer = originalParent!.Payload;
            if (mixer.Length < 24 || Read(mixer, mixer.Length - 24) != 5)
                throw new InvalidDataException("Native voice mixer child layout is unsupported.");
            var result = new byte[mixer.Length - 24 + 4 + children.Count * 4]; mixer.AsSpan(0, mixer.Length - 24).CopyTo(result);
            Write(result, mixer.Length - 24, checked((uint)children.Count));
            for (var i = 0; i < children.Count; i++) Write(result, mixer.Length - 20 + i * 4, children[i]);
            objects.Add(new(originalParent.Type, parent, result));
        }
        return new(objects.ToArray(), media.ToArray());
    }
    private static uint Read(byte[] data, int at)
    {
        if (at < 0 || at > data.Length - 4) throw new InvalidDataException("Voice graph reference is truncated.");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
    }
    private static void Write(byte[] data, int at, uint value)
    {
        if (at < 0 || at > data.Length - 4) throw new InvalidDataException("Voice graph patch is out of bounds.");
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
    }
}
