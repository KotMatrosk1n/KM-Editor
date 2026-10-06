// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.SwSh;

namespace KM.SwSh.Gifts;

internal sealed record SwShStarterIdentity(int Species, int Form);

/// <summary>Redirects presentation calls without moving existing script instructions or data.</summary>
internal static class SwShStarterPresentationScript
{
    internal static readonly SwShStarterIdentity[] Original = [new(810, 0), new(813, 0), new(816, 0)];
    internal static readonly string[] Names = ["main_event_0080", "main_event_0082", "main_event_0180"];
    private sealed record Layout(int NameCell, int[] VoiceCells);
    private static readonly Layout[] Layouts = [new(1040, [3990, 4062]), new(1153, [4657, 5594, 5691, 6108]), new(928, [4360])];

    internal static SwShStarterIdentity[] Read(byte[] bytes, int script)
    {
        var document = SwShAmxDocument.Parse(bytes);
        SwShStarterIdentity[]? result = null;
        foreach (var (cell, name) in Sites(Layouts[script]))
        {
            var pair = Inspect(document, cell, name);
            if (result is not null && (name ? !result.Select(v => v.Species).SequenceEqual(pair.Values.Select(v => v.Species)) : !result.SequenceEqual(pair.Values)))
                throw new InvalidDataException("Starter presentation calls have inconsistent mappings. Restore a consistent script before synchronizing gifts.");
            if (!name) result = pair.Values;
        }
        return result!;
    }

    internal static byte[] Write(byte[] bytes, int script, SwShStarterIdentity[] values)
    {
        if (values.Length != 3 || values.Any(v => v.Species is < 1 or > ushort.MaxValue || v.Form is < 0 or > byte.MaxValue))
            throw new InvalidDataException("Starter presentation requires three valid species and form identities.");
        var document = SwShAmxDocument.Parse(bytes);
        var previous = Read(bytes, script);
        if (previous.SequenceEqual(values)) return bytes;
        var assembler = document.CreateAssembler();
        var helpers = new Dictionary<bool, SwShAmxInstruction>();
        var edited = new HashSet<int>();
        foreach (var (cell, name) in Sites(Layouts[script]))
        {
            var existing = Inspect(document, cell, name);
            if (existing.Start is { } start)
            {
                if (!edited.Add(start)) continue;
                var block = document.Instructions.SkipWhile(i => i.OriginalCell != start).ToArray();
                for (var slot = 0; slot < 3; slot++)
                {
                    var index = (name ? 4 : 1) + slot * (name ? 6 : 8);
                    foreach (var (offset, value) in (name ? new[] { (3, values[slot].Species) } : new[] { (3, values[slot].Species), (5, values[slot].Form) }))
                    {
                        var instruction = assembler.GetInstructionAtOriginalCell(block[index + offset].OriginalCell!.Value);
                        assembler.ReplaceLiteralOperand(instruction, 0, instruction.Operands[0].LiteralValue, value);
                    }
                }
                continue;
            }
            if (!helpers.TryGetValue(name, out var helper))
            {
                var block = Build(values, name, existing.Native);
                assembler.InsertAfter(assembler.Instructions[^1], block);
                helpers.Add(name, helper = block[0]);
            }
            assembler.ReplaceWith(assembler.GetInstructionAtOriginalCell(cell), I(188, 32), SwShAmxInstruction.CreateBranch(49, helper));
        }
        var output = assembler.Assemble();
        var verified = SwShAmxDocument.Parse(output);
        if (!Read(output, script).SequenceEqual(values)) throw new InvalidDataException("Starter script verification failed.");
        // Each redirected native occupies exactly the same three cells as before.
        var owned = Sites(Layouts[script]).Select(s => s.Cell).ToHashSet();
        foreach (var instruction in document.Instructions.Where(i => !owned.Contains(i.OriginalCell!.Value)))
        {
            if (!verified.Instructions.Any(i => i.OriginalCell == instruction.OriginalCell))
                throw new InvalidDataException("Starter synchronization moved an existing script instruction.");
        }
        return output;
    }

    private static IEnumerable<(int Cell, bool Name)> Sites(Layout layout) =>
        layout.VoiceCells.Select(cell => (cell, false)).Append((layout.NameCell, true));

    private static (SwShStarterIdentity[] Values, int? Start, int Native) Inspect(SwShAmxDocument document, int cell, bool name)
    {
        var native = document.NativeHashes.ToList().IndexOf(SwShAmxNativeNameHash.Compute(name ? "PG_WordSetRegister" : "SoundPlayPokeVoice"));
        if (native < 0) throw new InvalidDataException("Starter presentation native is missing.");
        var at = document.Instructions.ToList().FindIndex(i => i.OriginalCell == cell);
        if (at < 0) throw new InvalidDataException("Starter presentation instruction layout is unsupported.");
        var instruction = document.Instructions[at];
        if (instruction.Mnemonic == "sysreq.n" && instruction.Operands[0].LiteralValue == native && instruction.Operands[1].LiteralValue == 32)
            return (Original.ToArray(), null, native);
        if (at + 1 >= document.Instructions.Count || instruction.Mnemonic != "push.p.c" || instruction.Operands[0].LiteralValue != 32
            || document.Instructions[at + 1].Mnemonic != "call")
            throw new InvalidDataException("A starter presentation call was changed by another script edit.");
        var start = document.Instructions[at + 1].Operands[0].Target.OriginalCell!.Value;
        var block = document.Instructions.SkipWhile(i => i.OriginalCell != start).ToArray();
        var prefix = name ? 4 : 1;
        if (block.Length < prefix + (name ? 18 : 24) + 6) throw new InvalidDataException("Starter presentation helper is incomplete.");
        var values = Enumerable.Range(0, 3).Select(slot =>
        {
            var index = prefix + slot * (name ? 6 : 8);
            if (block[index + 3].Mnemonic != "const.p.pri" || (!name && block[index + 5].Mnemonic != "const.p.pri"))
                throw new InvalidDataException("Starter presentation helper has an unsupported layout.");
            return new SwShStarterIdentity(checked((int)block[index + 3].Operands[0].LiteralValue), name ? 0 : checked((int)block[index + 5].Operands[0].LiteralValue));
        }).ToArray();
        var expected = Build(values, name, native);
        var positions = new Dictionary<SwShAmxInstruction, int>();
        var position = 0;
        foreach (var item in expected) { positions.Add(item, position); position += item.EncodedCellCount; }
        for (var i = 0; i < expected.Length; i++)
        {
            var actual = block[i]; var wanted = expected[i];
            if (actual.Mnemonic != wanted.Mnemonic || actual.Operands.Count != wanted.Operands.Count
                || actual.Operands.Where((operand, j) => operand.Kind != wanted.Operands[j].Kind
                    || (operand.Kind == SwShAmxOperandKind.Literal ? operand.LiteralValue != wanted.Operands[j].LiteralValue
                        : operand.Target.OriginalCell - start != positions[wanted.Operands[j].Target])).Any())
                throw new InvalidDataException("Starter presentation helper changed outside Gift Pokemon.");
        }
        return (values, start, native);
    }

    private static SwShAmxInstruction[] Build(SwShStarterIdentity[] values, bool name, int native)
    {
        var species = name ? 40 : 24; var form = name ? 48 : 32;
        var dispatch = I(190, 48);
        var code = new List<SwShAmxInstruction> { I(46) };
        if (name) code.AddRange([I(164, 24), I(201, 3), SwShAmxInstruction.CreateBranch(53, dispatch)]);
        var cases = Original.Select(_ => I(164, species)).ToArray();
        for (var i = 0; i < 3; i++)
        {
            code.AddRange([cases[i], I(201, Original[i].Species), SwShAmxInstruction.CreateBranch(53, i < 2 ? cases[i + 1] : dispatch),
                I(171, values[i].Species), I(177, species)]);
            if (!name) code.AddRange([I(171, values[i].Form), I(177, form)]);
            code.Add(SwShAmxInstruction.CreateBranch(51, dispatch));
        }
        code.AddRange([dispatch, I(190, 40), I(190, 32), I(190, 24), I(135, native, 32), I(48)]);
        return code.ToArray();
    }

    private static SwShAmxInstruction I(int opcode, params long[] values) => SwShAmxInstruction.CreateLiteral(opcode, values);
}
