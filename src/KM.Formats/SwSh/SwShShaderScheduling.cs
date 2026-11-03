// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Conservative serial controls for instructions inserted into the pinned Maxwell programs.</summary>
internal static class SwShShaderScheduling
{
    internal static void Original(List<(ulong Instruction, ulong Control)> code, ref int boundary, ulong instruction, ulong control)
    {
        // An original instruction keeps its native controls, so drain a final inserted
        // variable operation explicitly before handing execution back to that instruction.
        if (code.Count > boundary && ((code[^1].Control >> 5 & 7) != 7 || (code[^1].Control >> 8 & 7) != 7))
            code.Add((0x50b0000000070f00UL, Serial(0x50b0000000070f00UL)));
        code.Add((instruction, control));
        boundary = code.Count;
    }

    internal static ulong Serial(ulong instruction)
    {
        // Maxwell controls: write dependency bits5..7, read dependency bits8..10,
        // wait mask bits11..16. See MaxAS Control Codes and the preserved native programs.
        // Stalls cannot replace completion barriers for loads, interpolation, SFU or conversions.
        var operation = instruction & 0xfff8000000000000UL;
        var immediate = instruction & 0xfef8000000000000UL;
        var registerResult = operation is 0xefd8000000000000UL or 0xef90000000000000UL
            or 0x5080000000000000UL or 0x5cb8000000000000UL or 0x5cb0000000000000UL or 0x5ca8000000000000UL
            or 0x4cb8000000000000UL or 0x4cb0000000000000UL or 0x4ca8000000000000UL
            || immediate is 0x38b8000000000000UL or 0x38b0000000000000UL or 0x38a8000000000000UL
            || (instruction & 0xff00000000000000UL) is 0xe000000000000000UL or 0xdc00000000000000UL
            || (instruction & 0xfe00000000000000UL) is 0xd800000000000000UL or 0xda00000000000000UL
            || (instruction & 0xf800000000000000UL) == 0xc000000000000000UL;
        var attributeStore = operation == 0xeff0000000000000UL;
        // Every inserted consumer waits before reading or reusing registers. A result barrier
        // also covers delayed texture operands; stores instead protect their input registers.
        return 15UL | (registerResult ? 5UL : 7UL) << 5 | (attributeStore ? 5UL : 7UL) << 8 | 63UL << 11;
    }
}
