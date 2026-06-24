// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaShopLineupArray : IFlatbufferObject
{
  private Table __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public static void ValidateVersion() { FlatBufferConstants.FLATBUFFERS_25_2_10(); }
  public static ZaShopLineupArray GetRootAsZaShopLineupArray(ByteBuffer _bb) { return GetRootAsZaShopLineupArray(_bb, new ZaShopLineupArray()); }
  public static ZaShopLineupArray GetRootAsZaShopLineupArray(ByteBuffer _bb, ZaShopLineupArray obj) { return (obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb)); }
  public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }
  public ZaShopLineupArray __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public ZaShopLineup? Values(int j) { int o = __p.__offset(4); return o != 0 ? (ZaShopLineup?)(new ZaShopLineup()).__assign(__p.__indirect(__p.__vector(o) + j * 4), __p.bb) : null; }
  public int ValuesLength { get { int o = __p.__offset(4); return o != 0 ? __p.__vector_len(o) : 0; } }

  public static Offset<ZaShopLineupArray> CreateZaShopLineupArray(FlatBufferBuilder builder,
      VectorOffset valuesOffset = default(VectorOffset)) {
    builder.StartTable(1);
    ZaShopLineupArray.AddValues(builder, valuesOffset);
    return ZaShopLineupArray.EndZaShopLineupArray(builder);
  }

  public static void StartZaShopLineupArray(FlatBufferBuilder builder) { builder.StartTable(1); }
  public static void AddValues(FlatBufferBuilder builder, VectorOffset valuesOffset) { builder.AddOffset(0, valuesOffset.Value, 0); }
  public static VectorOffset CreateValuesVector(FlatBufferBuilder builder, Offset<ZaShopLineup>[] data) { builder.StartVector(4, data.Length, 4); for (int i = data.Length - 1; i >= 0; i--) builder.AddOffset(data[i].Value); return builder.EndVector(); }
  public static VectorOffset CreateValuesVectorBlock(FlatBufferBuilder builder, Offset<ZaShopLineup>[] data) { builder.StartVector(4, data.Length, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateValuesVectorBlock(FlatBufferBuilder builder, ArraySegment<Offset<ZaShopLineup>> data) { builder.StartVector(4, data.Count, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateValuesVectorBlock(FlatBufferBuilder builder, IntPtr dataPtr, int sizeInBytes) { builder.StartVector(1, sizeInBytes, 1); builder.Add<Offset<ZaShopLineup>>(dataPtr, sizeInBytes); return builder.EndVector(); }
  public static void StartValuesVector(FlatBufferBuilder builder, int numElems) { builder.StartVector(4, numElems, 4); }
  public static Offset<ZaShopLineupArray> EndZaShopLineupArray(FlatBufferBuilder builder) {
    int o = builder.EndTable();
    builder.Required(o, 4); // Values
    return new Offset<ZaShopLineupArray>(o);
  }
  public static void FinishZaShopLineupArrayBuffer(FlatBufferBuilder builder, Offset<ZaShopLineupArray> offset) { builder.Finish(offset.Value); }
  public static void FinishSizePrefixedZaShopLineupArrayBuffer(FlatBufferBuilder builder, Offset<ZaShopLineupArray> offset) { builder.FinishSizePrefixed(offset.Value); }
}
