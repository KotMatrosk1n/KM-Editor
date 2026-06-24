// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaShopData : IFlatbufferObject
{
  private Table __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public static void ValidateVersion() { FlatBufferConstants.FLATBUFFERS_25_2_10(); }
  public static ZaShopData GetRootAsZaShopData(ByteBuffer _bb) { return GetRootAsZaShopData(_bb, new ZaShopData()); }
  public static ZaShopData GetRootAsZaShopData(ByteBuffer _bb, ZaShopData obj) { return (obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb)); }
  public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }
  public ZaShopData __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public string? ShopId { get { int o = __p.__offset(4); return o != 0 ? __p.__string(o + __p.bb_pos) : null; } }
#if ENABLE_SPAN_T
  public Span<byte> GetShopIdBytes() { return __p.__vector_as_span<byte>(4, 1); }
#else
  public ArraySegment<byte>? GetShopIdBytes() { return __p.__vector_as_arraysegment(4); }
#endif
  public byte[] GetShopIdArray() { return __p.__vector_as_array<byte>(4); }
  public string? LineupId { get { int o = __p.__offset(6); return o != 0 ? __p.__string(o + __p.bb_pos) : null; } }
#if ENABLE_SPAN_T
  public Span<byte> GetLineupIdBytes() { return __p.__vector_as_span<byte>(6, 1); }
#else
  public ArraySegment<byte>? GetLineupIdBytes() { return __p.__vector_as_arraysegment(6); }
#endif
  public byte[] GetLineupIdArray() { return __p.__vector_as_array<byte>(6); }
  public string? ResourceLabel { get { int o = __p.__offset(8); return o != 0 ? __p.__string(o + __p.bb_pos) : null; } }
#if ENABLE_SPAN_T
  public Span<byte> GetResourceLabelBytes() { return __p.__vector_as_span<byte>(8, 1); }
#else
  public ArraySegment<byte>? GetResourceLabelBytes() { return __p.__vector_as_arraysegment(8); }
#endif
  public byte[] GetResourceLabelArray() { return __p.__vector_as_array<byte>(8); }
  public string? MessageLabel { get { int o = __p.__offset(10); return o != 0 ? __p.__string(o + __p.bb_pos) : null; } }
#if ENABLE_SPAN_T
  public Span<byte> GetMessageLabelBytes() { return __p.__vector_as_span<byte>(10, 1); }
#else
  public ArraySegment<byte>? GetMessageLabelBytes() { return __p.__vector_as_arraysegment(10); }
#endif
  public byte[] GetMessageLabelArray() { return __p.__vector_as_array<byte>(10); }
  public int ShopKind { get { int o = __p.__offset(12); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int Condition { get { int o = __p.__offset(14); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }

  public static void StartZaShopData(FlatBufferBuilder builder) { builder.StartTable(6); }
  public static void AddShopId(FlatBufferBuilder builder, StringOffset shopIdOffset) { builder.AddOffset(0, shopIdOffset.Value, 0); }
  public static void AddLineupId(FlatBufferBuilder builder, StringOffset lineupIdOffset) { builder.AddOffset(1, lineupIdOffset.Value, 0); }
  public static void AddResourceLabel(FlatBufferBuilder builder, StringOffset resourceLabelOffset) { builder.AddOffset(2, resourceLabelOffset.Value, 0); }
  public static void AddMessageLabel(FlatBufferBuilder builder, StringOffset messageLabelOffset) { builder.AddOffset(3, messageLabelOffset.Value, 0); }
  public static void AddShopKind(FlatBufferBuilder builder, int shopKind) { builder.AddInt(4, shopKind, 0); }
  public static void AddCondition(FlatBufferBuilder builder, int condition) { builder.AddInt(5, condition, 0); }
  public static Offset<ZaShopData> EndZaShopData(FlatBufferBuilder builder) {
    int o = builder.EndTable();
    builder.Required(o, 4); // ShopId
    builder.Required(o, 6); // LineupId
    builder.Required(o, 8); // ResourceLabel
    builder.Required(o, 10); // MessageLabel
    return new Offset<ZaShopData>(o);
  }
}
