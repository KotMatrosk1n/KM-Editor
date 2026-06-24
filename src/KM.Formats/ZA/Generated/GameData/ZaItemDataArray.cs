// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaItemDataArray : IFlatbufferObject
{
  private Table __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public static void ValidateVersion() { FlatBufferConstants.FLATBUFFERS_25_2_10(); }
  public static ZaItemDataArray GetRootAsZaItemDataArray(ByteBuffer _bb) { return GetRootAsZaItemDataArray(_bb, new ZaItemDataArray()); }
  public static ZaItemDataArray GetRootAsZaItemDataArray(ByteBuffer _bb, ZaItemDataArray obj) { return (obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb)); }
  public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }
  public ZaItemDataArray __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public ZaItemData? Values(int j) { int o = __p.__offset(4); return o != 0 ? (ZaItemData?)(new ZaItemData()).__assign(__p.__indirect(__p.__vector(o) + j * 4), __p.bb) : null; }
  public int ValuesLength { get { int o = __p.__offset(4); return o != 0 ? __p.__vector_len(o) : 0; } }

  public static Offset<ZaItemDataArray> CreateZaItemDataArray(FlatBufferBuilder builder,
      VectorOffset valuesOffset = default(VectorOffset)) {
    builder.StartTable(1);
    ZaItemDataArray.AddValues(builder, valuesOffset);
    return ZaItemDataArray.EndZaItemDataArray(builder);
  }

  public static void StartZaItemDataArray(FlatBufferBuilder builder) { builder.StartTable(1); }
  public static void AddValues(FlatBufferBuilder builder, VectorOffset valuesOffset) { builder.AddOffset(0, valuesOffset.Value, 0); }
  public static VectorOffset CreateValuesVector(FlatBufferBuilder builder, Offset<ZaItemData>[] data) { builder.StartVector(4, data.Length, 4); for (int i = data.Length - 1; i >= 0; i--) builder.AddOffset(data[i].Value); return builder.EndVector(); }
  public static VectorOffset CreateValuesVectorBlock(FlatBufferBuilder builder, Offset<ZaItemData>[] data) { builder.StartVector(4, data.Length, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateValuesVectorBlock(FlatBufferBuilder builder, ArraySegment<Offset<ZaItemData>> data) { builder.StartVector(4, data.Count, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateValuesVectorBlock(FlatBufferBuilder builder, IntPtr dataPtr, int sizeInBytes) { builder.StartVector(1, sizeInBytes, 1); builder.Add<Offset<ZaItemData>>(dataPtr, sizeInBytes); return builder.EndVector(); }
  public static void StartValuesVector(FlatBufferBuilder builder, int numElems) { builder.StartVector(4, numElems, 4); }
  public static Offset<ZaItemDataArray> EndZaItemDataArray(FlatBufferBuilder builder) {
    int o = builder.EndTable();
    builder.Required(o, 4); // Values
    return new Offset<ZaItemDataArray>(o);
  }
  public static void FinishZaItemDataArrayBuffer(FlatBufferBuilder builder, Offset<ZaItemDataArray> offset) { builder.Finish(offset.Value); }
  public static void FinishSizePrefixedZaItemDataArrayBuffer(FlatBufferBuilder builder, Offset<ZaItemDataArray> offset) { builder.FinishSizePrefixed(offset.Value); }
}
