// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaShopInventoryAppearCondition : IFlatbufferObject
{
  private Table __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public static void ValidateVersion() { FlatBufferConstants.FLATBUFFERS_25_2_10(); }
  public static ZaShopInventoryAppearCondition GetRootAsZaShopInventoryAppearCondition(ByteBuffer _bb) { return GetRootAsZaShopInventoryAppearCondition(_bb, new ZaShopInventoryAppearCondition()); }
  public static ZaShopInventoryAppearCondition GetRootAsZaShopInventoryAppearCondition(ByteBuffer _bb, ZaShopInventoryAppearCondition obj) { return (obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb)); }
  public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }
  public ZaShopInventoryAppearCondition __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public string? Condition { get { int o = __p.__offset(4); return o != 0 ? __p.__string(o + __p.bb_pos) : null; } }
#if ENABLE_SPAN_T
  public Span<byte> GetConditionBytes() { return __p.__vector_as_span<byte>(4, 1); }
#else
  public ArraySegment<byte>? GetConditionBytes() { return __p.__vector_as_arraysegment(4); }
#endif
  public byte[] GetConditionArray() { return __p.__vector_as_array<byte>(4); }
  public uint Comparison { get { int o = __p.__offset(6); return o != 0 ? __p.bb.GetUint(o + __p.bb_pos) : (uint)0; } }
  public string? Arguments(int j) { int o = __p.__offset(8); return o != 0 ? __p.__string(__p.__vector(o) + j * 4) : null; }
  public int ArgumentsLength { get { int o = __p.__offset(8); return o != 0 ? __p.__vector_len(o) : 0; } }

  public static void StartZaShopInventoryAppearCondition(FlatBufferBuilder builder) { builder.StartTable(3); }
  public static void AddCondition(FlatBufferBuilder builder, StringOffset conditionOffset) { builder.AddOffset(0, conditionOffset.Value, 0); }
  public static void AddComparison(FlatBufferBuilder builder, uint comparison) { builder.AddUint(1, comparison, 0); }
  public static void AddArguments(FlatBufferBuilder builder, VectorOffset argumentsOffset) { builder.AddOffset(2, argumentsOffset.Value, 0); }
  public static VectorOffset CreateArgumentsVector(FlatBufferBuilder builder, StringOffset[] data) { builder.StartVector(4, data.Length, 4); for (int i = data.Length - 1; i >= 0; i--) builder.AddOffset(data[i].Value); return builder.EndVector(); }
  public static VectorOffset CreateArgumentsVectorBlock(FlatBufferBuilder builder, StringOffset[] data) { builder.StartVector(4, data.Length, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateArgumentsVectorBlock(FlatBufferBuilder builder, ArraySegment<StringOffset> data) { builder.StartVector(4, data.Count, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateArgumentsVectorBlock(FlatBufferBuilder builder, IntPtr dataPtr, int sizeInBytes) { builder.StartVector(1, sizeInBytes, 1); builder.Add<StringOffset>(dataPtr, sizeInBytes); return builder.EndVector(); }
  public static void StartArgumentsVector(FlatBufferBuilder builder, int numElems) { builder.StartVector(4, numElems, 4); }
  public static Offset<ZaShopInventoryAppearCondition> EndZaShopInventoryAppearCondition(FlatBufferBuilder builder) {
    int o = builder.EndTable();
    return new Offset<ZaShopInventoryAppearCondition>(o);
  }
}
