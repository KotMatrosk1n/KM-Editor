// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaShopInventoryCondition : IFlatbufferObject
{
  private Table __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public static void ValidateVersion() { FlatBufferConstants.FLATBUFFERS_25_2_10(); }
  public static ZaShopInventoryCondition GetRootAsZaShopInventoryCondition(ByteBuffer _bb) { return GetRootAsZaShopInventoryCondition(_bb, new ZaShopInventoryCondition()); }
  public static ZaShopInventoryCondition GetRootAsZaShopInventoryCondition(ByteBuffer _bb, ZaShopInventoryCondition obj) { return (obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb)); }
  public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }
  public ZaShopInventoryCondition __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public ZaShopInventoryConditionHolder? Values(int j) { int o = __p.__offset(4); return o != 0 ? (ZaShopInventoryConditionHolder?)(new ZaShopInventoryConditionHolder()).__assign(__p.__indirect(__p.__vector(o) + j * 4), __p.bb) : null; }
  public int ValuesLength { get { int o = __p.__offset(4); return o != 0 ? __p.__vector_len(o) : 0; } }

  public static void StartZaShopInventoryCondition(FlatBufferBuilder builder) { builder.StartTable(1); }
  public static void AddValues(FlatBufferBuilder builder, VectorOffset valuesOffset) { builder.AddOffset(0, valuesOffset.Value, 0); }
  public static VectorOffset CreateValuesVector(FlatBufferBuilder builder, Offset<ZaShopInventoryConditionHolder>[] data) { builder.StartVector(4, data.Length, 4); for (int i = data.Length - 1; i >= 0; i--) builder.AddOffset(data[i].Value); return builder.EndVector(); }
  public static VectorOffset CreateValuesVectorBlock(FlatBufferBuilder builder, Offset<ZaShopInventoryConditionHolder>[] data) { builder.StartVector(4, data.Length, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateValuesVectorBlock(FlatBufferBuilder builder, ArraySegment<Offset<ZaShopInventoryConditionHolder>> data) { builder.StartVector(4, data.Count, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateValuesVectorBlock(FlatBufferBuilder builder, IntPtr dataPtr, int sizeInBytes) { builder.StartVector(1, sizeInBytes, 1); builder.Add<Offset<ZaShopInventoryConditionHolder>>(dataPtr, sizeInBytes); return builder.EndVector(); }
  public static void StartValuesVector(FlatBufferBuilder builder, int numElems) { builder.StartVector(4, numElems, 4); }
  public static Offset<ZaShopInventoryCondition> EndZaShopInventoryCondition(FlatBufferBuilder builder) {
    int o = builder.EndTable();
    builder.Required(o, 4); // Values
    return new Offset<ZaShopInventoryCondition>(o);
  }
}
