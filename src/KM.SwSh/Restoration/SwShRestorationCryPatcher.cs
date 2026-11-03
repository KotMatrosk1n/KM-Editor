// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

/// <summary>Routes selected added forms to independent events while retaining the native cry bank.</summary>
public static class SwShRestorationCryPatcher
{
    internal const string Owner = "Restoration Cry Routing";
    // These are twelve-byte alignment gaps following RET, with no native direct
    // branch, ADR, literal-load or absolute RO/DATA pointer references. Keeping
    // the dispatch in these gaps preserves the text tail used by other features.
    private static readonly int[] Padding =
        [0x7CA4, 0x7CC4, 0x7D34, 0x7D44, 0x7DB4, 0x7DC4, 0x8254,
         0x8274, 0x8734, 0x87A4, 0x87B4, 0x8824, 0x8C54, 0x8D54];
    private const string PaddingHash = "E7B21317B018A7524666849087784AC2069D47B10F5A361B85147091FD9B3E5E";
    private sealed record Site(int Offset, string Hash);
    private static Site[] Sites(ProjectGame game) => game == ProjectGame.Sword ?
    [
        new(0x9D8B08,"6535113A0A7453FC268305E866D00A058C3E08CD4532B4786D24029AECF4A2EB"),
        new(0x9D8B90,"D26639492FA262FAE57A0839AFAFD61EB25F4DECB0CD8120FD0E7BC322E1149F"),
        new(0xA70104,"9E954BEBDE8A79C8B8EA08DA5286D14B1AE6037DC8708D9C37CE8C1310019582"),
        new(0xA704D4,"856410930DDA9370F06B28C99DFB6AE6D14DAAE83E51800897E6D70ACDA509EE"),
        new(0x11D7530,"877829DD75A645B2F1FF92E442CD56D0FDF76CE05C537281E48A1E815D6CB78C"),
        new(0x13B3494,"7F078A757C804141FBAA0C93F37946F1D7F67D55E8C2B40E2969E76D71A76A3C"),
        new(0x13B375C,"4C35FB175BFC4B68532084EA8F7B5658DBE82F2FF1C9C0263F8EC81DD61C5C55"),
    ] :
    [
        new(0x9D8B08,"0DD1E0EF3ED57CCF417438C8EC030132F23D440E5E9F8F352F422591F1D81D92"),
        new(0x9D8B90,"2DF8CAF44019929FB3BAB98C84EAAB08B4FD1DDC9983899447999F4598ABC63E"),
        new(0xA70104,"F3E4A77BD68F1E66889072B72BDADE6C79AEE3559A411E9107E61AC546BAA245"),
        new(0xA704D4,"E378BF0091751F75421590374ABC0AF7D405CD555D5DDFC38DBD8943846C4994"),
        new(0x11D7560,"EF68A4692AC145350895DDFE26A99C118F9EC70D82D6397EAC7E5932D3229169"),
        new(0x13B34C4,"769CFB0D91CA36AA5869D3CCD687A7124188B40A3E071F7A56AFD4165AE22B45"),
        new(0x13B378C,"E53C45AC5FC18FA3A8B4673FCF172F90A866B7816BC2E545C8F0C6EC62D79DB7"),
    ];
    private static int NativeLength(ProjectGame game) => game == ProjectGame.Sword ? 0x1900F30 : 0x1900FC0;
    private static int Formatter(ProjectGame game) => game == ProjectGame.Sword ? 0x18FE230 : 0x18FE2C0;
    private static uint Branch(int from, int to, bool link) => (link ? 0x94000000U : 0x14000000U) | (uint)((to - from) / 4 & 0x3FFFFFF);
    private static void Put(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);

    internal static (NsoFile Main, int[] Species) Read(byte[] bytes, ProjectGame game)
    {
        var main = NsoFile.Parse(bytes); var text = main.Text.DecompressedData;
        if (game is not (ProjectGame.Sword or ProjectGame.Shield)
            || SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(main.BuildId)) != game
            || main.Text.Header.MemoryOffset != 0 || text.Length < NativeLength(game)
            || text.Length > 0x1901000 || main.Ro.Header.MemoryOffset != 0x1901000)
            throw new InvalidDataException("Independent form cries require the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (main.Text,NsoFlags.CheckHashText),(main.Ro,NsoFlags.CheckHashRo),(main.Data,NsoFlags.CheckHashData) })
            if (main.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The cry routing executable has an invalid segment checksum.");
        var installed = 0;
        foreach (var site in Sites(game))
        {
            var word = BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(site.Offset));
            if (word == Branch(site.Offset,Padding[0],true)) installed++;
            else if (word != Branch(site.Offset,Formatter(game),true))
                throw new InvalidDataException("A native cry formatter call contains incompatible changes.");
            var context = text.AsSpan(site.Offset-32,64).ToArray(); Put(context,32,Branch(site.Offset,Formatter(game),true));
            if (Convert.ToHexString(SHA256.HashData(context)) != site.Hash)
                throw new InvalidDataException("The native cry formatter arguments contain incompatible changes.");
        }
        var normalized = Padding.SelectMany(at =>
        {
            var context = text.AsSpan(at - 4, 20).ToArray();
            context.AsSpan(4, 12).Clear();
            return context;
        }).ToArray();
        if (Convert.ToHexString(SHA256.HashData(normalized)) != PaddingHash)
            throw new InvalidDataException("The cry routing alignment boundaries contain incompatible changes.");
        if (installed == 0)
        {
            if (Padding.Any(at => text.AsSpan(at, 12).IndexOfAnyExcept((byte)0) >= 0))
                throw new InvalidDataException("The cry routing alignment gaps are occupied.");
            return (main, []);
        }
        if (installed != Sites(game).Length)
            throw new InvalidDataException("The cry routing hook is incomplete.");
        var species = new List<int>();
        for (var i = 0; i < SwShVoiceRouting.FormOneSpecies.Count; i++)
        {
            var value = SwShVoiceRouting.FormOneSpecies[i];
            if (BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(Padding[i + 1])) == CompareSpecies(value)) species.Add(value);
        }
        var payloads = Payloads(game, species);
        if (species.Count == 0 || Padding.Where((at, i) => !text.AsSpan(at, 12).SequenceEqual(payloads[i])).Any())
            throw new InvalidDataException("The cry routing payload contains incompatible changes.");
        return (main, species.ToArray());
    }

    public static byte[] Apply(byte[] baseline, byte[] source, ProjectGame game, IReadOnlyCollection<SwShPokemonIdentity> identities)
    {
        var original = Read(baseline,game); var current = Read(source,game);
        if (original.Species.Length!=0 || original.Main.Text.DecompressedData.Length!=NativeLength(game))
            throw new InvalidDataException("Base ExeFS must contain the original cry routing.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(original.Main,current.Main,Owner);
        var selected=identities.Where(i=>SwShVoiceRouting.EventForm(i.Species,i.Form)!=i.Form).Select(i=>i.Species).Distinct().Order().ToArray();
        if (selected.SequenceEqual(current.Species)) return source;
        var regions=Reservations();
        var overlap = SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game,Owner).FirstOrDefault(other=>
            regions.Any(own=>SwShExeFsReservedRegionLedger.Overlaps(other,own.StartOffset!.Value,own.Length!.Value)));
        if (overlap is not null)
            throw new InvalidDataException($"Cry routing overlaps {overlap.Owner} ({overlap.FeatureId}).");
        var text=current.Main.Text.DecompressedData.ToArray();
        foreach(var site in Sites(game)) Put(text,site.Offset,Branch(site.Offset,selected.Length==0?Formatter(game):Padding[0],true));
        var payloads = Payloads(game, selected);
        for (var i = 0; i < Padding.Length; i++) payloads[i].CopyTo(text, Padding[i]);
        var output=current.Main.Write(textDecompressedData:text); var after=Read(output,game);
        if (!after.Species.SequenceEqual(selected) || !after.Main.Text.DecompressedData.SequenceEqual(text)
            || !after.Main.Ro.DecompressedData.SequenceEqual(current.Main.Ro.DecompressedData)
            || !after.Main.Data.DecompressedData.SequenceEqual(current.Main.Data.DecompressedData))
            throw new InvalidDataException("Cry routing output failed preservation verification.");
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(original.Main,after.Main,Owner);
        return output;
    }

    private static uint CompareSpecies(int species) => 0x7100007FU | checked((uint)species << 10);
    private static uint BranchEqual(int from, int to) => 0x54000000U | (uint)((to - from) / 4 & 0x7FFFF) << 5;

    private static byte[][] Payloads(ProjectGame game, IReadOnlyCollection<int> species)
    {
        var result = Padding.Select(_ => new byte[12]).ToArray();
        if (species.Count == 0) return result;
        void Words(int i, uint a, uint b, uint c)
        {
            Put(result[i], 0, a); Put(result[i], 4, b); Put(result[i], 8, c);
        }
        var formatter = Formatter(game);
        // Only form one enters the species dispatch. BL already set the native
        // return address; every exit tail-branches to the original formatter.
        Words(0, 0x7100049F, BranchEqual(Padding[0] + 4, Padding[1]), Branch(Padding[0] + 8, formatter, false));
        for (var i = 0; i < SwShVoiceRouting.FormOneSpecies.Count; i++)
        {
            var at = Padding[i + 1];
            var next = i + 1 == SwShVoiceRouting.FormOneSpecies.Count ? formatter : Padding[i + 2];
            if (species.Contains(SwShVoiceRouting.FormOneSpecies[i]))
                Words(i + 1, CompareSpecies(SwShVoiceRouting.FormOneSpecies[i]), BranchEqual(at + 4, Padding[^1]), Branch(at + 8, next, false));
            else Words(i + 1, Branch(at, next, false), 0xD503201F, 0xD503201F);
        }
        Words(Padding.Length - 1, 0x52801024, Branch(Padding[^1] + 4, formatter, false), 0xD503201F);
        return result;
    }

    public static bool HasInstalledPatch(byte[] bytes)
    {
        try { return SwShExecutableMergeSupport.GameForBuild(Convert.ToHexString(NsoFile.Parse(bytes).BuildId)) is { } game && Read(bytes,game).Species.Length!=0; }
        catch(InvalidDataException) { return false; }
    }
    internal static IReadOnlyList<SwShExeFsReservedRegion> Reservations() => Sites(ProjectGame.Sword).Concat(Sites(ProjectGame.Shield)).Select(s=>s.Offset).Distinct()
        .Select(at=>new SwShExeFsReservedRegion(Owner,$"restoration-cry-call-{at:X}","exefs/main","main.text",at,4,"Independent restored form cry call","payload-only"))
        .Concat(Padding.Select(at => new SwShExeFsReservedRegion(Owner,$"restoration-cry-padding-{at:X}","exefs/main","main.text",at,12,"Independent restored form cry dispatch","payload-only"))).ToArray();
}
