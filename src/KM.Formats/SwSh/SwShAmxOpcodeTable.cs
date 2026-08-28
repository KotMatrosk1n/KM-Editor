// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats.SwSh;

internal enum SwShAmxOpcodeEncoding
{
    Fixed,
    Packed,
    Relative,
    SwitchTable,
    IndirectSwitchTable,
}

internal sealed record SwShAmxOpcodeDefinition(
    int Opcode,
    string Mnemonic,
    SwShAmxOpcodeEncoding Encoding,
    int OperandCount);

internal static class SwShAmxOpcodeTable
{
    private static readonly string[] Names =
    [
        "invalid",
        "load.pri", "load.alt", "load.s.pri", "load.s.alt", "lref.pri", "lref.alt", "lref.s.pri", "lref.s.alt",
        "load.i", "lodb.i", "const.pri", "const.alt", "addr.pri", "addr.alt", "stor.pri", "stor.alt", "stor.s.pri",
        "stor.s.alt", "sref.pri", "sref.alt", "sref.s.pri", "sref.s.alt", "stor.i", "strb.i", "lidx", "lidx.b",
        "idxaddr", "idxaddr.b", "align.pri", "align.alt", "lctrl", "sctrl", "move.pri", "move.alt", "xchg", "push.pri",
        "push.alt", "pick", "push.c", "push", "push.s", "pop.pri", "pop.alt", "stack", "heap", "proc", "ret", "retn",
        "call", "call.pri", "jump", "jrel", "jzer", "jnz", "jeq", "jneq", "jless", "jleq", "jgrtr", "jgeq",
        "jsless", "jsleq", "jsgrtr", "jsgeq", "shl", "shr", "sshr", "shl.c.pri", "shl.c.alt", "shr.c.pri", "shr.c.alt",
        "smul", "sdiv", "sdiv.alt", "umul", "udiv", "udiv.alt", "add", "sub", "sub.alt", "and", "or", "xor", "not",
        "neg", "invert", "add.c", "smul.c", "zero.pri", "zero.alt", "zero", "zero.s", "sign.pri", "sign.alt", "eq",
        "neq", "less", "leq", "grtr", "geq", "sless", "sleq", "sgrtr", "sgeq", "eq.c.pri", "eq.c.alt", "inc.pri",
        "inc.alt", "inc", "inc.s", "inc.i", "dec.pri", "dec.alt", "dec", "dec.s", "dec.i", "movs", "cmps", "fill",
        "halt", "bounds", "sysreq.pri", "sysreq.c", "pushr.pri", "pushr.c", "pushr.s", "pushr.adr", "jump.pri", "switch",
        "casetbl", "swap.pri", "swap.alt", "push.adr", "nop", "sysreq.n", "symtag", "break", "push2.c", "push2",
        "push2.s", "push2.adr", "push3.c", "push3", "push3.s", "push3.adr", "push4.c", "push4", "push4.s", "push4.adr",
        "push5.c", "push5", "push5.s", "push5.adr", "load.both", "load.s.both", "const", "const.s", "icall", "iretn",
        "iswitch", "icasetbl", "load.p.pri", "load.p.alt", "load.p.s.pri", "load.p.s.alt", "lref.p.pri", "lref.p.alt",
        "lref.p.s.pri", "lref.p.s.alt", "lodb.p.i", "const.p.pri", "const.p.alt", "addr.p.pri", "addr.p.alt", "stor.p.pri",
        "stor.p.alt", "stor.p.s.pri", "stor.p.s.alt", "sref.p.pri", "sref.p.alt", "sref.p.s.pri", "sref.p.s.alt", "strb.p.i",
        "lidx.p.b", "idxaddr.p.b", "align.p.pri", "align.p.alt", "push.p.c", "push.p", "push.p.s", "stack.p", "heap.p",
        "shl.p.c.pri", "shl.p.c.alt", "shr.p.c.pri", "shr.p.c.alt", "add.p.c", "smul.p.c", "zero.p", "zero.p.s",
        "eq.p.c.pri", "eq.p.c.alt", "inc.p", "inc.p.s", "dec.p", "dec.p.s", "movs.p", "cmps.p", "fill.p", "halt.p",
        "bounds.p", "push.p.adr", "pushr.p.c", "pushr.p.s", "pushr.p.adr",
    ];

    public static SwShAmxOpcodeDefinition Get(int opcode)
    {
        if ((uint)opcode >= (uint)Names.Length || opcode == 0)
        {
            throw new InvalidDataException($"Unknown Sword/Shield AMX opcode {opcode}.");
        }

        if (opcode is 130 or 161)
        {
            return new SwShAmxOpcodeDefinition(
                opcode,
                Names[opcode],
                opcode == 130 ? SwShAmxOpcodeEncoding.SwitchTable : SwShAmxOpcodeEncoding.IndirectSwitchTable,
                -1);
        }

        if (opcode is >= 162 and <= 215)
        {
            return new SwShAmxOpcodeDefinition(opcode, Names[opcode], SwShAmxOpcodeEncoding.Packed, 1);
        }

        if (opcode is 49 or 51 or 52 or >= 53 and <= 64 or 129 or 160)
        {
            return new SwShAmxOpcodeDefinition(opcode, Names[opcode], SwShAmxOpcodeEncoding.Relative, 1);
        }

        var operandCount = opcode switch
        {
            >= 150 and <= 153 => 5,
            >= 146 and <= 149 => 4,
            >= 142 and <= 145 => 3,
            135 or >= 138 and <= 141 or >= 154 and <= 157 => 2,
            9 or 23 or 25 or 27
                or >= 33 and <= 37
                or 42 or 43 or >= 46 and <= 48 or 50
                or >= 65 and <= 67 or >= 72 and <= 86
                or 89 or 90 or >= 93 and <= 104
                or 107 or 108 or 111 or 112 or 113 or 116
                or 122 or 124 or 128 or 131 or 132 or 134 or 137 or 159 => 0,
            _ => 1,
        };

        return new SwShAmxOpcodeDefinition(opcode, Names[opcode], SwShAmxOpcodeEncoding.Fixed, operandCount);
    }
}
