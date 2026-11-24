// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.Restoration;

/// <summary>Preserves both field heaps by splitting the long battle's allocations between them.</summary>
internal static class SwShRestorationBattleHeapPatcher
{
    internal const string Owner = "Restoration Battle Heap";
    internal const int MainRequest = 0x8E3F18;
    internal const uint ExpandedMainRequest = 0x52A20C09;
    private const uint RetailMainRequest = 0x52A1AC09;
    private const int AuxiliaryLoad = 0x8E3F78;
    private const int NestedLoad = 0x961FE0;
    private const int SwordNativeTextLength = 0x1900F30;
    private const int SwordMain = 0x840714;
    private const int SwordAuxiliary = 0x840750;
    private const int ShieldMain = 0x1900FC0;
    private const int ShieldAuxiliary = 0xD36AD4;
    private const int RoAddress = 0x1901000;
    private const uint Return = 0xD65F03C0;
    private static readonly int[] Nested = [0x9505E4, 0x950674, 0x9506E4, 0x9507F4, 0x950BC4, 0x950E64, 0x951184];
    private sealed record Code(int Offset, uint[] Words)
    {
        public int Length => Words.Length * sizeof(uint);
        public int End => Offset + Length;
    }
    private sealed record Layout(Code[] Hooks, Code[] Stubs);
    private static readonly Layout Sword = CreateLayout(SwordMain, SwordAuxiliary);
    private static readonly Layout Shield = CreateLayout(ShieldMain, ShieldAuxiliary);
    private static Layout ForGame(ProjectGame game) => game == ProjectGame.Sword ? Sword : Shield;

    internal static bool IsInstalled(NsoFile main, ProjectGame game)
    {
        var text = main.Text.DecompressedData;
        var layout = ForGame(game);
        return Matches(text, layout.Hooks[0]) && layout.Hooks.Skip(1).All(code => Matches(text, code))
            && layout.Stubs.All(code => Matches(text, code));
    }

    // The Sword v7 stubs reside in padding created by the existing Mega
    // item protection routine. That routine keeps ownership of its full range.
    internal static void NormalizeSwordMegaPadding(Span<byte> routine, int start)
    {
        routine.Slice(SwordMain - start, Sword.Stubs[0].Length).Clear();
        routine.Slice(SwordAuxiliary - start, Sword.Stubs[1].Length).Clear();
    }

    internal static bool ValidateChanges(NsoFile original, NsoFile merged, byte[] mergedBytes, ProjectGame game)
    {
        var before = original.Text.DecompressedData;
        var after = merged.Text.DecompressedData;
        var layout = ForGame(game);
        var independentlyChanged = layout.Hooks.Skip(1).Concat(layout.Stubs.Skip(game == ProjectGame.Sword ? 2 : 0))
            .Any(code => Enumerable.Range(code.Offset, code.Length).Any(offset =>
                (offset < before.Length ? before[offset] : 0) != (offset < after.Length ? after[offset] : 0)));
        if (!independentlyChanged && !Matches(after, layout.Hooks[0]) && !Matches(before, layout.Hooks[0])) return true;
        if (IsInstalled(merged, game))
        {
            ValidateContext(after, game);
            return SwShRestorationModelMemoryPatcher.Read(mergedBytes, game).Installed;
        }
        // Exact removal may leave the model-expanded pair or remove it too.
        return HasOriginalHooks(after) && HasEmptyStubs(after, game, includeSwordMegaPadding: false);
    }

    private static (NsoFile Main, bool Installed, bool Applicable) Read(byte[] baseline, byte[] source, ProjectGame game)
    {
        var original = SwShRestorationModelMemoryPatcher.Read(baseline, game);
        if (original.Installed || original.Main.Text.DecompressedData.Length !=
            (game == ProjectGame.Sword ? SwordNativeTextLength : ShieldMain))
            throw new InvalidDataException("Base ExeFS must contain the original 1.3.2 executable layout and model memory budgets.");
        var current = SwShRestorationModelMemoryPatcher.Read(source, game);
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(original.Main, current.Main, Owner);
        var text = current.Main.Text.DecompressedData;
        var installed = IsInstalled(current.Main, game);
        if (!current.Installed) return (current.Main, false, false);
        ValidateContext(text, game);
        if (!installed && (!HasOriginalHooks(text) || !HasEmptyStubs(text, game)))
            throw new InvalidDataException("The battle heap hooks or executable padding contain incompatible changes.");
        return (current.Main, installed, true);
    }

    internal static bool Check(byte[] baseline, byte[] source, ProjectGame game)
    {
        if (game == ProjectGame.Sword)
            source = SwShRestorationMegaItemProtection.Apply(baseline, source, game);
        return Read(baseline, source, game).Installed;
    }

    internal static byte[] Apply(byte[] baseline, byte[] source, ProjectGame game)
    {
        // On Sword the verified v7 layout uses space freed by the existing
        // Mega Stone protection routine, including on installations without stones.
        if (game == ProjectGame.Sword)
            source = SwShRestorationMegaItemProtection.Apply(baseline, source, game);
        var current = Read(baseline, source, game);
        if (current.Installed) return source;
        if (!current.Applicable)
            throw new InvalidDataException("The battle heap repair requires restoration model memory support.");
        var layout = ForGame(game);
        var text = current.Main.Text.DecompressedData.ToArray();
        Array.Resize(ref text, Math.Max(text.Length, layout.Stubs.Max(code => code.End)));
        foreach (var code in layout.Hooks.Concat(layout.Stubs))
            for (var i = 0; i < code.Words.Length; i++)
                Put(text, code.Offset + i * sizeof(uint), code.Words[i]);
        var output = Write(current.Main, text);
        if (!IsInstalled(NsoFile.Parse(output), game))
            throw new InvalidDataException("The battle heap repair failed output verification.");
        return output;
    }

    internal static byte[] RestoreFromBase(byte[] baseline, byte[] source, ProjectGame game)
    {
        var current = Read(baseline, source, game);
        if (!current.Installed) return source;
        var text = current.Main.Text.DecompressedData.ToArray();
        Put(text, MainRequest, ExpandedMainRequest);
        Put(text, AuxiliaryLoad, Load(24, 26));
        Put(text, NestedLoad, Load(0, 19, 0x2C0));
        foreach (var stub in ForGame(game).Stubs)
            text.AsSpan(stub.Offset, stub.Length).Clear();
        // Shield lends unused space before .rodata. Retain any other code in it.
        if (game == ProjectGame.Shield && !text.AsSpan(ShieldMain).ContainsAnyExcept((byte)0))
            Array.Resize(ref text, ShieldMain);
        return Write(current.Main, text, NsoFile.Parse(baseline));
    }

    private static byte[] Write(NsoFile main, byte[] text, NsoFile? baseline = null)
    {
        var output = main.Write(textDecompressedData: text);
        var after = NsoFile.Parse(output);
        SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline ?? main, after, Owner);
        if (!after.Text.DecompressedData.SequenceEqual(text)
            || !after.Ro.DecompressedData.SequenceEqual(main.Ro.DecompressedData)
            || !after.Data.DecompressedData.SequenceEqual(main.Data.DecompressedData))
            throw new InvalidDataException("The battle heap repair did not preserve the executable payload.");
        return output;
    }

    private static bool HasOriginalHooks(byte[] text) =>
        Word(text, MainRequest) is RetailMainRequest or ExpandedMainRequest
        && Word(text, AuxiliaryLoad) == Load(24, 26) && Word(text, NestedLoad) == Load(0, 19, 0x2C0);

    private static bool HasEmptyStubs(byte[] text, ProjectGame game, bool includeSwordMegaPadding = true) => ForGame(game).Stubs
        .Skip(game == ProjectGame.Sword && !includeSwordMegaPadding ? 2 : 0).All(code =>
        code.Offset <= text.Length
        && code.End <= RoAddress
        && !text.AsSpan(code.Offset, Math.Min(code.Length, text.Length - code.Offset)).ContainsAnyExcept((byte)0));

    private static void ValidateContext(byte[] text, ProjectGame game)
    {
        // Guard the original branch decision, the short path and the field
        // budgets modified by the earlier Wyndon-only workaround.
        var shift = game == ProjectGame.Sword ? 0 : 0x30;
        if (Word(text, MainRequest - 8) != 0x720002DF || Word(text, MainRequest - 4) != MoveSize(8, 182)
            || Word(text, MainRequest + 4) != 0x9A881136 || Word(text, AuxiliaryLoad - 4) != 0xF9002717
            || Word(text, AuxiliaryLoad + 4) != 0x320E03E8 || Word(text, NestedLoad - 4) != 0xF9400319
            || Word(text, NestedLoad + 4) != 0xF9007B20
            || Word(text, 0xD362F4 + shift) != MoveSize(10, 270)
            || Word(text, 0xD36498 + shift) != MoveSize(8, 270)
            || Word(text, 0xD365B4 + shift) != MoveSize(8, 290)
            || Nested.Any(offset => Word(text, offset - 4) != Return)
            || (game == ProjectGame.Sword
                ? Word(text, SwordMain - 4) != Return
                : Word(text, ShieldMain - 4) != 0xD61F0220 || Word(text, ShieldAuxiliary - 4) != Return))
            throw new InvalidDataException("The field battle initialization contains incompatible instructions or budgets.");
    }

    internal static IEnumerable<SwShExeFsReservedRegion> Reservations()
    {
        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
        {
            var layout = ForGame(game);
            // The first hook is the existing model memory budget instruction.
            foreach (var code in layout.Hooks.Skip(1).Concat(layout.Stubs.Skip(game == ProjectGame.Sword ? 2 : 0)))
                yield return new(Owner, $"restoration-battle-{game.ToString().ToLowerInvariant()}-{code.Offset:X}",
                    "exefs/main", "main.text", code.Offset, code.Length, "Field battle heap routing", "payload-only");
        }
    }

    private static Layout CreateLayout(int main, int auxiliary)
    {
        const int managerGot = 0x261F740;
        const int heapsGot = 0x2618AF8;
        var done = main + 56;
        var finish = Nested[6] + 4;
        Code[] hooks =
        [
            new(MainRequest, [Branch(MainRequest, main)]),
            new(AuxiliaryLoad, [Branch(AuxiliaryLoad, auxiliary)]),
            new(NestedLoad, [Branch(NestedLoad, Nested[0], 0x94000000)]),
        ];
        Code[] stubs =
        [
            // Keep the original request and parent unless this is the long path
            // on BG_MAIN. Preserve the original TST flags for the caller's CSEL.
            new(main, [
                MoveSize(9, 262), 0xAA1403FB, // mov x27, x20
                Branch(main + 8, done, 0x36000016, 14, 5), // tbz w22, #0
                Page(10, main + 12, managerGot), Load(10, 10, managerGot & 0xFFF), Load(10, 10),
                Branch(main + 24, done, 0xB400000A, 19, 5), // cbz x10
                Load(11, 10, 0xA8), 0xCA0B028B, // eor x11, x20, x11
                Branch(main + 36, done, 0xB500000B, 19, 5), // cbnz x11
                Load(11, 10, 0x88), Branch(main + 44, done, 0xB400000B, 19, 5),
                0xAA0B03FB, MoveSize(9, 236), Branch(done, MainRequest + 4),
            ]),
            // The original allocator records the real parent for teardown.
            new(auxiliary, [0xAA1B03F4, Load(24, 26), Branch(auxiliary + 8, AuxiliaryLoad + 4)]),
            new(Nested[0], [Load(0, 19, 0x2C0), Page(8, Nested[0] + 4, heapsGot), Branch(Nested[0] + 8, Nested[1])]),
            new(Nested[1], [Load(8, 8, heapsGot & 0xFFF), Load(8, 8), Branch(Nested[1] + 8, Nested[2])]),
            new(Nested[2], [Branch(Nested[2], finish, 0xB4000008, 19, 5), Load(9, 8, 0x48), Branch(Nested[2] + 8, Nested[3])]),
            new(Nested[3], [0xEB09001F, Branch(Nested[3] + 4, finish, 0x54000001, 19, 5), Branch(Nested[3] + 8, Nested[4])]),
            // Move the 27 MiB nested arena only if its parents match the split.
            new(Nested[4], [Load(9, 8, 0x50), Load(8, 8, 0x70), Branch(Nested[4] + 8, Nested[5])]),
            new(Nested[5], [Branch(Nested[5], finish, 0xB4000008, 19, 5), 0xEB09011F, Branch(Nested[5] + 8, Nested[6])]),
            new(Nested[6], [0x9A801100, Return]), // csel x0, x8, x0, ne
        ];
        return new(hooks, stubs);
    }

    private static bool Matches(byte[] text, Code code) => code.End <= text.Length
        && code.Words.Select((word, index) => word == Word(text, code.Offset + index * sizeof(uint))).All(matches => matches);
    private static uint Word(byte[] text, int offset) => offset + sizeof(uint) <= text.Length
        ? BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset, sizeof(uint))) : 0;
    private static void Put(byte[] text, int offset, uint word) =>
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), word);
    private static uint MoveSize(int register, int mib) => 0x52A00000 | (uint)(mib << 9 | register);
    private static uint Load(int destination, int register, int offset = 0) =>
        0xF9400000 | (uint)((offset >> 3) << 10 | register << 5 | destination);
    private static uint Page(int register, int pc, int address)
    {
        var pages = (address >> 12) - (pc >> 12);
        return 0x90000000 | (uint)((pages & 3) << 29 | (pages >> 2 & 0x7FFFF) << 5 | register);
    }
    private static uint Branch(int pc, int address, uint opcode = 0x14000000, int width = 26, int shift = 0)
    {
        var delta = address - pc;
        if (delta % 4 != 0 || delta < -(1 << (width + 1)) || delta >= 1 << (width + 1))
            throw new InvalidDataException("A battle heap branch cannot reach its target.");
        return opcode | (uint)((delta / 4 & ((1 << width) - 1)) << shift);
    }
}
