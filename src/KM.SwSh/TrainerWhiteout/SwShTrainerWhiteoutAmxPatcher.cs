// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Collections.Concurrent;
using KM.Formats.SwSh;
using KM.SwSh.Scripts;
using I = KM.Formats.SwSh.SwShAmxInstruction;

namespace KM.SwSh.TrainerWhiteout;

internal static class SwShTrainerWhiteoutAmxPatcher
{
    private const long Marker = 0x4B4D5457414D5831;
    private const long EndMarker = 0x4B4D5457454E4431;
    private const int FormatVersion = 1;
    private const int AppendedDataCells = 446;
    private const int SettingsOffset = 3;
    public const int TrainerCount = 437;
    private static readonly ConcurrentDictionary<string, SwShAmxDocument> Baselines = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SwShAmxDocument> HelperLayouts = new(StringComparer.Ordinal);
    public static IReadOnlyList<byte> ReadSettings(byte[] vanilla, byte[] source, string fileName)
    {
        return Inspect(vanilla, source, fileName).Settings;
    }

    public static byte[] ApplySettings(byte[] vanilla, byte[] source, string fileName, IReadOnlyDictionary<int, bool?> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var id in changes.Keys)
            if (id is < 1 or >= TrainerCount)
                throw new ArgumentOutOfRangeException(nameof(changes), "The trainer ID is outside the roster.");
        var current = Inspect(vanilla, source, fileName);
        var updates = changes.Where(change => current.Settings[change.Key] != Encode(change.Value)).ToArray();
        if (updates.Length == 0)
            return source.ToArray();
        var output = current.Installed ? source.ToArray() : Build(SwShAmxDocument.Parse(source), fileName);
        var dataOffset = current.Installed ? current.OriginalDataCells : SwShAmxDocument.Parse(source).DataCells.Count;
        output = SwShAmxCellPatcher.ApplyDataCellPatches(output, updates.Select(change => new SwShAmxDataCellPatch(dataOffset + SettingsOffset + change.Key, Encode(change.Value))).ToArray());
        var actual = Inspect(vanilla, output, fileName).Settings;
        for (var id = 0; id < TrainerCount; id++)
        {
            var expected = changes.TryGetValue(id, out var value) ? Encode(value) : current.Settings[id];
            if (actual[id] != expected)
                throw new InvalidDataException("Trainer Whiteout output did not preserve the requested settings.");
        }

        return output;
    }

    private static byte Encode(bool? enabled) => enabled is null ? (byte)0 : enabled.Value ? (byte)2 : (byte)1;
    private static State Inspect(byte[] vanilla, byte[] source, string fileName)
    {
        ArgumentNullException.ThrowIfNull(vanilla);
        ArgumentNullException.ThrowIfNull(source);
        if (!SwShTrainerWhiteoutCatalog.ScriptHashes.TryGetValue(fileName, out var hash) || Convert.ToHexString(SHA256.HashData(vanilla)) != hash)
            throw new InvalidDataException("Trainer Whiteout requires a supported vanilla trainer script.");
        var baseline = Baselines.GetOrAdd(hash, _ => SwShAmxDocument.Parse(vanilla));
        var current = SwShAmxDocument.Parse(source);
        ValidateBorrowedHelpers(baseline, current, fileName);
        if (current.DataCells.Count < 6 || current.DataCells[^1] != EndMarker || current.DataCells[^6] != Marker)
        {
            ValidateOriginal(baseline, current, fileName);
            return new(false, current.DataCells.Count, new byte[TrainerCount]);
        }

        if (current.DataCells[^5] != FormatVersion)
            throw new InvalidDataException("Trainer Whiteout script version is unsupported.");
        var originalCode = checked((int)current.DataCells[^4]);
        var originalData = checked((int)current.DataCells[^3]);
        var originalNatives = checked((int)current.DataCells[^2]);
        if (originalCode < CodeCells(baseline) || originalData < baseline.DataCells.Count || originalNatives < baseline.NativeHashes.Count || originalNatives > current.NativeHashes.Count || originalData != current.DataCells.Count - AppendedDataCells || originalCode >= CodeCells(current))
            throw new InvalidDataException("Trainer Whiteout script metadata is inconsistent.");
        if (current.DataCells.Skip(originalData).Take(3).Any(value => value != 0))
            throw new InvalidDataException("Trainer Whiteout script contains a persisted runtime result.");
        var settings = current.DataCells.Skip(originalData + SettingsOffset).Take(TrainerCount).ToArray();
        if (settings[0] != 0 || settings.Any(value => value is < 0 or > 2))
            throw new InvalidDataException("Trainer Whiteout script settings are invalid.");
        // Rebuild the owned helper layout. Existing code and data before the appended region are retained.
        if (!current.NativeHashes.Take(baseline.NativeHashes.Count).SequenceEqual(baseline.NativeHashes))
            throw new InvalidDataException("Trainer Whiteout requires the original script native bindings.");
        var layoutKey = $"{hash}:{originalCode}:{originalData}:{string.Join(',', current.NativeHashes.Take(originalNatives))}";
        // Cache only derived, immutable validation layouts. Live output is always parsed and compared.
        if (HelperLayouts.Count >= 256)
            HelperLayouts.Clear();
        var expected = HelperLayouts.GetOrAdd(layoutKey, _ =>
        {
            var skeleton = baseline.CreateAssembler();
            skeleton.AppendDataCells(new long[originalData - baseline.DataCells.Count]);
            foreach (var native in current.NativeHashes.Skip(baseline.NativeHashes.Count).Take(originalNatives - baseline.NativeHashes.Count))
                skeleton.GetOrAddNative(native);
            if (skeleton.NativeHashes.Count != originalNatives)
                throw new InvalidDataException("Trainer Whiteout cannot identify the original native table.");
            var extraCode = originalCode - CodeCells(baseline);
            if (extraCode > 0)
                skeleton.InsertAfter(skeleton.Instructions.Last(), Enumerable.Range(0, extraCode).Select(_ => I.CreateLiteral(Op.Nop)).ToArray());
            return SwShAmxDocument.Parse(Build(SwShAmxDocument.Parse(skeleton.Assemble()), fileName));
        });
        if (!expected.NativeHashes.SequenceEqual(current.NativeHashes) || CodeCells(expected) != CodeCells(current) || !expected.Instructions.Where(instruction => instruction.OriginalCell >= originalCode).Select(Signature).SequenceEqual(current.Instructions.Where(instruction => instruction.OriginalCell >= originalCode).Select(Signature)))
            throw new InvalidDataException("Trainer Whiteout helper code contains incompatible changes.");
        var owned = OwnedCells(baseline, fileName);
        var actualByCell = current.Instructions.ToDictionary(instruction => instruction.OriginalCell!.Value);
        foreach (var cell in owned)
        {
            var anchor = expected.Instructions.Single(instruction => instruction.OriginalCell == cell);
            if (!actualByCell.TryGetValue(cell, out var actual) || Signature(anchor) != Signature(actual))
                throw new InvalidDataException("A Trainer Whiteout script hook contains incompatible changes.");
            if (anchor.Mnemonic == "call" && anchor.EncodedCellCount == 2 && baseline.Instructions.Single(instruction => instruction.OriginalCell == cell).Mnemonic == "sysreq.n")
            {
                var cleanup = expected.Instructions.Single(instruction => instruction.OriginalCell == cell + 2);
                if (!actualByCell.TryGetValue(cell + 2, out var found) || Signature(cleanup) != Signature(found))
                    throw new InvalidDataException("A Trainer Whiteout script hook has an invalid stack cleanup.");
            }
        }

        return new(true, originalData, settings.Select(value => (byte)value).ToArray());
    }

    private static readonly string[] OwnedNativeNames = ["CallTrainerBattleCore", "StartLoadTrainerBattleSeamless_", "StartLoadTrainerBattleSeamlessDebugAging_", "StartLoadTornamentTrainerBattleSeamless_", "StartLoadKumiteBattleSeamless_", "GetTrainerBattleResult_", "CallBattleLose_", "EndKumiteWork", "StartKumiteWork", "CallRaidBattleMatchingEvent_"];
    private static readonly HashSet<uint> OwnedNativeHashes = OwnedNativeNames.Select(name => SwShAmxNativeNameHash.Compute(name)).ToHashSet();
    private static HashSet<int> OwnedCells(SwShAmxDocument baseline, string fileName)
    {
        var cells = baseline.Instructions.Where(instruction => instruction.Mnemonic == "sysreq.n" && OwnedNativeHashes.Contains(baseline.NativeHashes[(int)instruction.Operands[0].LiteralValue])).Select(instruction => instruction.OriginalCell!.Value).ToHashSet();
        if (fileName == "rigel1_sub_event_006_bt_da.amx")
            cells.UnionWith([7580, 7637]);
        return cells;
    }

    private static void ValidateOriginal(SwShAmxDocument baseline, SwShAmxDocument current, string fileName)
    {
        if (CodeCells(current) < CodeCells(baseline) || current.DataCells.Count < baseline.DataCells.Count || !current.NativeHashes.Take(baseline.NativeHashes.Count).SequenceEqual(baseline.NativeHashes))
            throw new InvalidDataException("The trainer script layout or native bindings are incompatible.");
        var owned = OwnedCells(baseline, fileName);
        var currentOwned = OwnedCells(current, fileName);
        if (!owned.SetEquals(currentOwned))
            throw new InvalidDataException("The trainer script launch or result callsites have changed.");
        var actual = current.Instructions.ToDictionary(instruction => instruction.OriginalCell!.Value);
        foreach (var cell in owned)
            if (!actual.TryGetValue(cell, out var found) || Signature(found) != Signature(baseline.Instructions.Single(instruction => instruction.OriginalCell == cell)))
                throw new InvalidDataException("A trainer script launch or result anchor contains incompatible changes.");
    }

    private static int CodeCells(SwShAmxDocument document) => (document.Header.DataOffset - document.Header.CodeOffset) / 8;
    private static void ValidateBorrowedHelpers(SwShAmxDocument baseline, SwShAmxDocument current, string fileName)
    {
        if (fileName != "rigel1_sub_event_006_bt_da.amx")
            return;
        // The tower continuation calls these existing fade and yield helpers by address.
        bool Borrowed(I instruction) => instruction.OriginalCell is >= 1 and < 18 or >= 91 and < 103 or >= 117 and < 135;
        if (!baseline.Instructions.Where(Borrowed).Select(Signature).SequenceEqual(current.Instructions.Where(Borrowed).Select(Signature)))
            throw new InvalidDataException("The tower fade or yield helpers contain incompatible changes.");
        var first = 37720 / 8;
        var length = baseline.DataCells.Skip(first).TakeWhile(value => value != 0).Count() + 1;
        if (!baseline.DataCells.Skip(first).Take(length).SequenceEqual(current.DataCells.Skip(first).Take(length)))
            throw new InvalidDataException("The tower fade target contains incompatible changes.");
    }

    private static string Signature(I instruction) => $"{instruction.OriginalCell}:{instruction.Opcode}:{string.Join(',', instruction.Operands.Select(operand => operand.Kind == SwShAmxOperandKind.Literal ? $"v{operand.LiteralValue}" : $"t{operand.Target.OriginalCell}"))}";
    private sealed record State(bool Installed, int OriginalDataCells, IReadOnlyList<byte> Settings);
    private static byte[] Build(SwShAmxDocument doc, string fileName)
    {
        var names = new[]
        {
            "CallTrainerBattleCore",
            "StartLoadTrainerBattleSeamless_",
            "StartLoadTrainerBattleSeamlessDebugAging_",
            "StartLoadTornamentTrainerBattleSeamless_",
            "StartLoadKumiteBattleSeamless_"
        }.ToDictionary(n => SwShAmxNativeNameHash.Compute(n));
        var sites = doc.Instructions.Where(i => i.Mnemonic == "sysreq.n" && names.ContainsKey(doc.NativeHashes[(int)i.Operands[0].LiteralValue])).ToArray();
        if (sites.Length == 0)
            throw new InvalidDataException("The script has no supported trainer launch.");
        var a = doc.CreateAssembler();
        int state = a.AppendDataCells(0), vanillaFlags = a.AppendDataCells(0), deferred = a.AppendDataCells(0), table = a.AppendDataCells(new long[437]);
        // Resolve raw opponent IDs before battle teardown clears the native trainer models.
        var lookup = I.CreateLiteral(Op.Proc);
        var bad = I.CreateLiteral(Op.ZeroPri);
        var code = new List<I>
        {
            lookup,
            I.CreateLiteral(Op.LoadSPri, 24),
            I.CreateLiteral(Op.ConstAlt, 1),
            I.CreateBranch(Op.Jsless, bad),
            I.CreateLiteral(Op.ConstAlt, 436),
            I.CreateBranch(Op.Jsgrtr, bad),
            I.CreateLiteral(Op.ShlCPri, 3),
            I.CreateLiteral(Op.AddC, table),
            I.CreateLiteral(Op.LoadI),
            I.CreateLiteral(Op.ConstAlt, 2),
            I.CreateBranch(Op.Jgrtr, bad),
            I.CreateLiteral(Op.Retn),
            bad,
            I.CreateLiteral(Op.Retn)
        };
        a.InsertAfter(a.Instructions.Last(), code.ToArray());
        // Explicit No takes priority; an untouched second opponent follows this battle's flags.
        var resolve = I.CreateLiteral(Op.Proc);
        var no = I.CreateLiteral(Op.ConstPri, 1);
        var yes = I.CreateLiteral(Op.ConstPri, 2);
        var unchanged = I.CreateLiteral(Op.ZeroPri);
        var firstYes = I.CreateLiteral(Op.LoadSPri, -16);
        var otherDefault = I.CreateLiteral(Op.LoadSPri, 40);
        var resolveDone = I.CreateLiteral(Op.Stack, 16);
        code = [
            resolve,
            I.CreateLiteral(Op.Stack, -16),
            I.CreateLiteral(Op.PushS, 24),
            I.CreateLiteral(Op.PushC, 8),
            I.CreateBranch(Op.Call, lookup),
            I.CreateLiteral(Op.StorSPri, -8),
            I.CreateLiteral(Op.PushS, 32),
            I.CreateLiteral(Op.PushC, 8),
            I.CreateBranch(Op.Call, lookup),
            I.CreateLiteral(Op.StorSPri, -16),
            I.CreateLiteral(Op.EqCPri, 1),
            I.CreateBranch(Op.Jnz, no),
            I.CreateLiteral(Op.LoadSPri, -8),
            I.CreateLiteral(Op.EqCPri, 1),
            I.CreateBranch(Op.Jnz, no),
            I.CreateLiteral(Op.LoadSPri, -8),
            I.CreateLiteral(Op.EqCPri, 2),
            I.CreateBranch(Op.Jnz, firstYes),
            I.CreateLiteral(Op.LoadSPri, -16),
            I.CreateLiteral(Op.EqCPri, 2),
            I.CreateBranch(Op.Jzer, unchanged),
            I.CreateLiteral(Op.LoadSPri, 24),
            I.CreateLiteral(Op.ConstAlt, 1),
            I.CreateBranch(Op.Jsless, yes),
            I.CreateLiteral(Op.ConstAlt, 436),
            I.CreateBranch(Op.Jsgrtr, yes),
            I.CreateBranch(Op.Jump, otherDefault),
            firstYes,
            I.CreateLiteral(Op.EqCPri, 2),
            I.CreateBranch(Op.Jnz, yes),
            I.CreateLiteral(Op.LoadSPri, 32),
            I.CreateLiteral(Op.ConstAlt, 1),
            I.CreateBranch(Op.Jsless, yes),
            I.CreateLiteral(Op.ConstAlt, 436),
            I.CreateBranch(Op.Jsgrtr, yes),
            otherDefault,
            I.CreateLiteral(Op.ConstAlt, 4),
            I.CreateLiteral(Op.And),
            I.CreateBranch(Op.Jnz, no),
            yes,
            I.CreateBranch(Op.Jump, resolveDone),
            no,
            I.CreateBranch(Op.Jump, resolveDone),
            unchanged,
            resolveDone,
            I.CreateLiteral(Op.Retn)
        ];
        a.InsertAfter(a.Instructions.Last(), code.ToArray());
        // Native wrappers use proc/ret with argument zero at frame +16. Nested AMX calls use +24.
        foreach (var site in sites)
        {
            var name = names[doc.NativeHashes[(int)site.Operands[0].LiteralValue]];
            var core = name == "CallTrainerBattleCore";
            var tournament = name == "StartLoadTornamentTrainerBattleSeamless_";
            var kumite = name == "StartLoadKumiteBattleSeamless_";
            int count = core ? 56 : tournament ? 16 : 48, flagArgument = core ? 4 : 3;
            if (site.Operands[1].LiteralValue != count)
                throw new InvalidDataException("Unexpected native argument shape");
            var entry = I.CreateLiteral(Op.Proc);
            var flagsNo = I.CreateLiteral(Op.ConstPri, 4);
            var flagsYes = I.CreateLiteral(Op.ConstPri, ~4L);
            var flagsDone = I.CreateLiteral(Op.PushPri);
            var nativeIndex = site.Operands[0].LiteralValue;
            code = [
                entry,
                I.CreateLiteral(Op.ZeroPri),
                I.CreateLiteral(Op.StorPri, deferred),
                tournament || kumite ? I.CreateLiteral(Op.ConstPri, kumite ? 4 : 0) : I.CreateLiteral(Op.LoadSPri, 16 + 8 * flagArgument),
                I.CreateLiteral(Op.StorPri, vanillaFlags),
                I.CreateLiteral(Op.PushPri),
                tournament ? I.CreateLiteral(Op.PushC, 0) : I.CreateLiteral(Op.PushS, 32),
                I.CreateLiteral(Op.PushS, tournament ? 16 : 24),
                I.CreateLiteral(Op.PushC, 24),
                I.CreateBranch(Op.Call, resolve),
                I.CreateLiteral(Op.StorPri, state)
            ];
            if (tournament || kumite)
                code.AddRange([I.CreateLiteral(Op.PushPri), I.CreateLiteral(Op.PushC, 0x4B4D5457)]);
            for (var argument = count / 8 - 1; argument >= 0; argument--)
            {
                if (argument != flagArgument || tournament || kumite)
                {
                    code.Add(I.CreateLiteral(Op.PushS, 16 + 8 * argument));
                    continue;
                }

                code.AddRange([
                    I.CreateLiteral(Op.LoadPri, state),
                    I.CreateLiteral(Op.EqCPri, 1),
                    I.CreateBranch(Op.Jnz, flagsNo),
                    I.CreateLiteral(Op.LoadPri, state),
                    I.CreateLiteral(Op.EqCPri, 2),
                    I.CreateBranch(Op.Jnz, flagsYes),
                    I.CreateLiteral(Op.LoadSPri, 16 + 8 * flagArgument),
                    I.CreateBranch(Op.Jump, flagsDone),
                    flagsNo,
                    I.CreateLiteral(Op.LoadSAlt, 16 + 8 * flagArgument),
                    I.CreateLiteral(Op.Or),
                    I.CreateBranch(Op.Jump, flagsDone),
                    flagsYes,
                    I.CreateLiteral(Op.LoadSAlt, 16 + 8 * flagArgument),
                    I.CreateLiteral(Op.And),
                    flagsDone
                ]);
            }

            code.AddRange([I.CreateLiteral(Op.SysreqN, nativeIndex, count + (tournament || kumite ? 16 : 0)), I.CreateLiteral(Op.Ret)]);
            a.InsertAfter(a.Instructions.Last(), code.ToArray());
            a.ReplaceWith(a.GetInstructionAtOriginalCell(site.OriginalCell!.Value), I.CreateBranch(Op.Call, entry), I.CreateLiteral(Op.StackP, count));
        }

        var lossNative = a.GetOrAddNative("CallBattleLose_");
        var suspendNative = a.GetOrAddNative("_Suspend");
        var partyNative = a.GetOrAddNative("PokePartyGetCount");
        foreach (var site in doc.Instructions.Where(i => i.Mnemonic == "sysreq.n"))
        {
            var hash = doc.NativeHashes[(int)site.Operands[0].LiteralValue];
            string? kind = hash == SwShAmxNativeNameHash.Compute("GetTrainerBattleResult_") ? "result" : hash == SwShAmxNativeNameHash.Compute("CallBattleLose_") ? "loss" : hash == SwShAmxNativeNameHash.Compute("EndKumiteWork") ? "endKumite" : hash == SwShAmxNativeNameHash.Compute("StartKumiteWork") || hash == SwShAmxNativeNameHash.Compute("CallRaidBattleMatchingEvent_") ? "reset" : null;
            if (kind is null)
                continue;
            var entry = I.CreateLiteral(Op.Proc);
            var done = I.CreateLiteral(Op.LoadSPri, -8);
            var nativeIndex = site.Operands[0].LiteralValue;
            var count = site.Operands[1].LiteralValue;
            code = [entry, I.CreateLiteral(Op.Stack, -8)];
            if (kind == "reset")
                code.AddRange([
                    I.CreateLiteral(Op.ZeroPri),
                    I.CreateLiteral(Op.StorPri, state),
                    I.CreateLiteral(Op.StorPri, vanillaFlags),
                    I.CreateLiteral(Op.StorPri, deferred)
                ]);
            if (kind == "loss")
            {
                code.AddRange([
                    I.CreateLiteral(Op.ZeroPri),
                    I.CreateLiteral(Op.StorSPri, -8),
                    I.CreateLiteral(Op.LoadPri, state),
                    I.CreateLiteral(Op.EqCPri, 1),
                    I.CreateBranch(Op.Jnz, done)
                ]);
            }

            for (int argument = (int)count / 8 - 1; argument >= 0; argument--)
                code.Add(I.CreateLiteral(Op.PushS, 16 + 8 * argument));
            code.AddRange([I.CreateLiteral(Op.SysreqN, nativeIndex, count), I.CreateLiteral(Op.StorSPri, -8)]);
            if (kind == "result")
            {
                var defeated = I.CreateLiteral(Op.ConstPri, 2);
                code.AddRange([
                    I.CreateLiteral(Op.ZeroPri),
                    I.CreateLiteral(Op.StorPri, deferred),
                    I.CreateLiteral(Op.LoadPri, state),
                    I.CreateLiteral(Op.EqCPri, 2),
                    I.CreateBranch(Op.Jzer, done),
                    I.CreateLiteral(Op.LoadPri, vanillaFlags),
                    I.CreateLiteral(Op.ConstAlt, 4),
                    I.CreateLiteral(Op.And),
                    I.CreateBranch(Op.Jzer, done),
                    I.CreateLiteral(Op.LoadSPri, 16),
                    I.CreateLiteral(Op.LoadI),
                    I.CreateBranch(Op.Jzer, defeated),
                    I.CreateLiteral(Op.EqCPri, 3),
                    I.CreateBranch(Op.Jnz, defeated),
                    I.CreateLiteral(Op.LoadSPri, 16),
                    I.CreateLiteral(Op.LoadI),
                    I.CreateLiteral(Op.EqCPri, 2),
                    I.CreateBranch(Op.Jzer, done),
                    I.CreateLiteral(Op.PushC, 0),
                    I.CreateLiteral(Op.PushC, 1),
                    I.CreateLiteral(Op.SysreqN, partyNative, 16),
                    I.CreateBranch(Op.Jnz, done),
                    defeated
                ]);
                if (fileName == "shibari_dojo.amx")
                    code.Add(I.CreateLiteral(Op.StorPri, deferred));
                else
                    code.AddRange([
                        I.CreateLiteral(Op.SysreqN, lossNative, 0),
                        I.CreateLiteral(Op.PushC, 1),
                        I.CreateLiteral(Op.SysreqN, suspendNative, 8)
                    ]);
            }

            if (kind == "endKumite")
            {
                code.AddRange([
                    I.CreateLiteral(Op.LoadPri, deferred),
                    I.CreateLiteral(Op.ConstAlt, 0),
                    I.CreateLiteral(Op.StorAlt, deferred),
                    I.CreateLiteral(Op.EqCPri, 2),
                    I.CreateBranch(Op.Jzer, done),
                    I.CreateLiteral(Op.SysreqN, lossNative, 0),
                    I.CreateLiteral(Op.PushC, 1),
                    I.CreateLiteral(Op.SysreqN, suspendNative, 8)
                ]);
            }

            code.AddRange([done, I.CreateLiteral(Op.Stack, 8), I.CreateLiteral(Op.Ret)]);
            a.InsertAfter(a.Instructions.Last(), code.ToArray());
            a.ReplaceWith(a.GetInstructionAtOriginalCell(site.OriginalCell!.Value), I.CreateBranch(Op.Call, entry), I.CreateLiteral(Op.StackP, count));
        }

        if (fileName == "rigel1_sub_event_006_bt_da.amx")
        {
            // Preserve the original retry flags and fade helpers without running the relocation scene.
            var entry = I.CreateLiteral(Op.Proc);
            var passthrough = I.CreateLiteral(Op.PushS, 24);
            var flag = a.GetOrAddNative("FlagSet");
            code = [
                entry,
                I.CreateLiteral(Op.LoadPri, state),
                I.CreateLiteral(Op.EqCPri, 1),
                I.CreateBranch(Op.Jzer, passthrough),
                I.CreateLiteral(Op.PushC, 4162895398522194447),
                I.CreateLiteral(Op.SysreqN, flag, 8),
                I.CreateLiteral(Op.PushC, 3673679524964061099),
                I.CreateLiteral(Op.SysreqN, flag, 8),
                I.CreateLiteral(Op.PushC, 37720),
                I.CreateLiteral(Op.PushC, 8),
                I.CreateLiteral(Op.PushC, 16),
                I.CreateBranch(Op.Call, a.GetInstructionAtOriginalCell(91)),
                I.CreateLiteral(Op.PushC, 0),
                I.CreateBranch(Op.Call, a.GetInstructionAtOriginalCell(117)),
                I.CreateLiteral(Op.ZeroPri),
                I.CreateLiteral(Op.Retn),
                passthrough,
                I.CreateLiteral(Op.PushC, 8),
                I.CreateBranch(Op.Call, a.GetInstructionAtOriginalCell(11105)),
                I.CreateLiteral(Op.Retn)
            ];
            a.InsertAfter(a.Instructions.Last(), code.ToArray());
            foreach (var cell in new[] { 7580, 7637 })
            {
                var site = doc.Instructions.Single(i => i.OriginalCell == cell);
                if (site.Mnemonic != "call" || site.Operands[0].Target.OriginalCell != 11105)
                    throw new InvalidDataException("Mustard loss shape changed");
                a.ReplaceWith(a.GetInstructionAtOriginalCell(cell), I.CreateBranch(Op.Call, entry));
            }
        }

        a.AppendDataCells(Marker, FormatVersion, CodeCells(doc), doc.DataCells.Count, doc.NativeHashes.Count, EndMarker);
        return a.Assemble();
    }

    private static class Op
    {
        public const int LoadPri = 1;
        public const int LoadSPri = 3;
        public const int LoadSAlt = 4;
        public const int LoadI = 9;
        public const int ConstPri = 11;
        public const int ConstAlt = 12;
        public const int StorPri = 15;
        public const int StorAlt = 16;
        public const int StorSPri = 17;
        public const int PushPri = 36;
        public const int PushC = 39;
        public const int PushS = 41;
        public const int Stack = 44;
        public const int Proc = 46;
        public const int Ret = 47;
        public const int Retn = 48;
        public const int Call = 49;
        public const int Jump = 51;
        public const int Jzer = 53;
        public const int Jnz = 54;
        public const int Jgrtr = 59;
        public const int Jsless = 61;
        public const int Jsgrtr = 63;
        public const int ShlCPri = 68;
        public const int And = 81;
        public const int Or = 82;
        public const int AddC = 87;
        public const int ZeroPri = 89;
        public const int EqCPri = 105;
        public const int Nop = 134;
        public const int SysreqN = 135;
        public const int StackP = 191;
    }
}
