// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaShopInventory : IFlatbufferObject
{
  private Table __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public static void ValidateVersion() { FlatBufferConstants.FLATBUFFERS_25_2_10(); }
  public static ZaShopInventory GetRootAsZaShopInventory(ByteBuffer _bb) { return GetRootAsZaShopInventory(_bb, new ZaShopInventory()); }
  public static ZaShopInventory GetRootAsZaShopInventory(ByteBuffer _bb, ZaShopInventory obj) { return (obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb)); }
  public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }
  public ZaShopInventory __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public uint Item { get { int o = __p.__offset(4); return o != 0 ? __p.bb.GetUint(o + __p.bb_pos) : (uint)0; } }
  public uint DisplayIndex { get { int o = __p.__offset(6); return o != 0 ? __p.bb.GetUint(o + __p.bb_pos) : (uint)0; } }
  public ZaShopInventoryCondition? Conditions(int j) { int o = __p.__offset(8); return o != 0 ? (ZaShopInventoryCondition?)(new ZaShopInventoryCondition()).__assign(__p.__indirect(__p.__vector(o) + j * 4), __p.bb) : null; }
  public int ConditionsLength { get { int o = __p.__offset(8); return o != 0 ? __p.__vector_len(o) : 0; } }

  public static void StartZaShopInventory(FlatBufferBuilder builder) { builder.StartTable(3); }
  public static void AddItem(FlatBufferBuilder builder, uint item) { builder.AddUint(0, item, 0); }
  public static void AddDisplayIndex(FlatBufferBuilder builder, uint displayIndex) { builder.AddUint(1, displayIndex, 0); }
  public static void AddConditions(FlatBufferBuilder builder, VectorOffset conditionsOffset) { builder.AddOffset(2, conditionsOffset.Value, 0); }
  public static VectorOffset CreateConditionsVector(FlatBufferBuilder builder, Offset<ZaShopInventoryCondition>[] data) { builder.StartVector(4, data.Length, 4); for (int i = data.Length - 1; i >= 0; i--) builder.AddOffset(data[i].Value); return builder.EndVector(); }
  public static VectorOffset CreateConditionsVectorBlock(FlatBufferBuilder builder, Offset<ZaShopInventoryCondition>[] data) { builder.StartVector(4, data.Length, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateConditionsVectorBlock(FlatBufferBuilder builder, ArraySegment<Offset<ZaShopInventoryCondition>> data) { builder.StartVector(4, data.Count, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateConditionsVectorBlock(FlatBufferBuilder builder, IntPtr dataPtr, int sizeInBytes) { builder.StartVector(1, sizeInBytes, 1); builder.Add<Offset<ZaShopInventoryCondition>>(dataPtr, sizeInBytes); return builder.EndVector(); }
  public static void StartConditionsVector(FlatBufferBuilder builder, int numElems) { builder.StartVector(4, numElems, 4); }
  public static Offset<ZaShopInventory> EndZaShopInventory(FlatBufferBuilder builder) {
    int o = builder.EndTable();
    builder.Required(o, 8); // Conditions
    return new Offset<ZaShopInventory>(o);
  }
}
