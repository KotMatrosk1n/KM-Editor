// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaMoveInflict : IFlatbufferObject
{
  private Struct __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public void __init(int _i, ByteBuffer _bb) { __p = new Struct(_i, _bb); }
  public ZaMoveInflict __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public ushort Condition { get { return __p.bb.GetUshort(__p.bb_pos + 0); } }
  public byte Chance { get { return __p.bb.Get(__p.bb_pos + 2); } }
  public byte TurnMode { get { return __p.bb.Get(__p.bb_pos + 3); } }
  public byte TurnMin { get { return __p.bb.Get(__p.bb_pos + 4); } }
  public byte TurnMax { get { return __p.bb.Get(__p.bb_pos + 5); } }

  public static Offset<ZaMoveInflict> CreateZaMoveInflict(FlatBufferBuilder builder, ushort Condition, byte Chance, byte TurnMode, byte TurnMin, byte TurnMax) {
    builder.Prep(2, 6);
    builder.PutByte(TurnMax);
    builder.PutByte(TurnMin);
    builder.PutByte(TurnMode);
    builder.PutByte(Chance);
    builder.PutUshort(Condition);
    return new Offset<ZaMoveInflict>(builder.Offset);
  }
}
