// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaMoveStatChanges : IFlatbufferObject
{
    private Struct __p;

    public ByteBuffer ByteBuffer { get { return __p.bb; } }

    public void __init(int _i, ByteBuffer _bb) { __p = new Struct(_i, _bb); }

    public ZaMoveStatChanges __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

    public sbyte Stat1 { get { return __p.bb.GetSbyte(__p.bb_pos + 0); } }

    public sbyte Stat1Stage { get { return __p.bb.GetSbyte(__p.bb_pos + 1); } }

    public byte Stat1Chance { get { return __p.bb.Get(__p.bb_pos + 2); } }

    public sbyte Stat2 { get { return __p.bb.GetSbyte(__p.bb_pos + 3); } }

    public sbyte Stat2Stage { get { return __p.bb.GetSbyte(__p.bb_pos + 4); } }

    public byte Stat2Chance { get { return __p.bb.Get(__p.bb_pos + 5); } }

    public sbyte Stat3 { get { return __p.bb.GetSbyte(__p.bb_pos + 6); } }

    public sbyte Stat3Stage { get { return __p.bb.GetSbyte(__p.bb_pos + 7); } }

    public byte Stat3Chance { get { return __p.bb.Get(__p.bb_pos + 8); } }

    public static Offset<ZaMoveStatChanges> CreateZaMoveStatChanges(
        FlatBufferBuilder builder,
        sbyte Stat1,
        sbyte Stat1Stage,
        byte Stat1Chance,
        sbyte Stat2,
        sbyte Stat2Stage,
        byte Stat2Chance,
        sbyte Stat3,
        sbyte Stat3Stage,
        byte Stat3Chance)
    {
        builder.Prep(1, 9);
        builder.PutByte(Stat3Chance);
        builder.PutSbyte(Stat3Stage);
        builder.PutSbyte(Stat3);
        builder.PutByte(Stat2Chance);
        builder.PutSbyte(Stat2Stage);
        builder.PutSbyte(Stat2);
        builder.PutByte(Stat1Chance);
        builder.PutSbyte(Stat1Stage);
        builder.PutSbyte(Stat1);
        return new Offset<ZaMoveStatChanges>(builder.Offset);
    }
}
