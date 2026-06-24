// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaItemData : IFlatbufferObject
{
  private Table __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public static void ValidateVersion() { FlatBufferConstants.FLATBUFFERS_25_2_10(); }
  public static ZaItemData GetRootAsZaItemData(ByteBuffer _bb) { return GetRootAsZaItemData(_bb, new ZaItemData()); }
  public static ZaItemData GetRootAsZaItemData(ByteBuffer _bb, ZaItemData obj) { return (obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb)); }
  public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }
  public ZaItemData __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public int Id { get { int o = __p.__offset(4); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int ItemType { get { int o = __p.__offset(6); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public string? InternalName { get { int o = __p.__offset(8); return o != 0 ? __p.__string(o + __p.bb_pos) : null; } }
#if ENABLE_SPAN_T
  public Span<byte> GetInternalNameBytes() { return __p.__vector_as_span<byte>(8, 1); }
#else
  public ArraySegment<byte>? GetInternalNameBytes() { return __p.__vector_as_arraysegment(8); }
#endif
  public byte[] GetInternalNameArray() { return __p.__vector_as_array<byte>(8); }
  public string? IconName { get { int o = __p.__offset(10); return o != 0 ? __p.__string(o + __p.bb_pos) : null; } }
#if ENABLE_SPAN_T
  public Span<byte> GetIconNameBytes() { return __p.__vector_as_span<byte>(10, 1); }
#else
  public ArraySegment<byte>? GetIconNameBytes() { return __p.__vector_as_arraysegment(10); }
#endif
  public byte[] GetIconNameArray() { return __p.__vector_as_array<byte>(10); }
  public int Price { get { int o = __p.__offset(12); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int Pocket { get { int o = __p.__offset(14); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int SlotMaxNum { get { int o = __p.__offset(16); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int SortNum { get { int o = __p.__offset(18); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int PriceMegaShard { get { int o = __p.__offset(20); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int PriceColorfulScrew { get { int o = __p.__offset(22); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public bool CanNotHold { get { int o = __p.__offset(24); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public ushort MachineWaza { get { int o = __p.__offset(26); return o != 0 ? __p.bb.GetUshort(o + __p.bb_pos) : (ushort)0; } }
  public int MachineIndex { get { int o = __p.__offset(28); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public bool WorkRecvSleep { get { int o = __p.__offset(30); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool WorkRecvPoison { get { int o = __p.__offset(32); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool WorkRecvBurn { get { int o = __p.__offset(34); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool WorkRecvFreeze { get { int o = __p.__offset(36); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool WorkRecvParalyze { get { int o = __p.__offset(38); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool WorkRecvConfuse { get { int o = __p.__offset(40); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool WorkRecvMero { get { int o = __p.__offset(42); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public int WorkAttack { get { int o = __p.__offset(44); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkDefense { get { int o = __p.__offset(46); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkSpAttack { get { int o = __p.__offset(48); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkSpDefense { get { int o = __p.__offset(50); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkSpeed { get { int o = __p.__offset(52); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkAccuracy { get { int o = __p.__offset(54); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkCritical { get { int o = __p.__offset(56); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkEffectGuard { get { int o = __p.__offset(58); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int MintNature { get { int o = __p.__offset(60); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkRecvPower { get { int o = __p.__offset(62); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int HealPercentage { get { int o = __p.__offset(64); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkRevival { get { int o = __p.__offset(66); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int RevivePercentage { get { int o = __p.__offset(68); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int ExpPointGain { get { int o = __p.__offset(70); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int MaxUseLevel { get { int o = __p.__offset(72); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkFriendly1 { get { int o = __p.__offset(74); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkFriendly2 { get { int o = __p.__offset(76); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkFriendly3 { get { int o = __p.__offset(78); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public bool WorkEvolutional { get { int o = __p.__offset(80); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool WorkFormChange { get { int o = __p.__offset(82); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public int WorkStatusHp { get { int o = __p.__offset(84); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkStatusAtk { get { int o = __p.__offset(86); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkStatusDef { get { int o = __p.__offset(88); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkStatusSpd { get { int o = __p.__offset(90); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkStatusSAtk { get { int o = __p.__offset(92); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int WorkStatusSDef { get { int o = __p.__offset(94); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int EquipPower { get { int o = __p.__offset(96); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public int AutoHealPriority { get { int o = __p.__offset(98); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }
  public bool CanUseInBattle { get { int o = __p.__offset(100); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public int SwapIntoId { get { int o = __p.__offset(102); return o != 0 ? __p.bb.GetInt(o + __p.bb_pos) : (int)0; } }

  public static void StartZaItemData(FlatBufferBuilder builder) { builder.StartTable(50); }
  public static void AddId(FlatBufferBuilder builder, int id) { builder.AddInt(0, id, 0); }
  public static void AddItemType(FlatBufferBuilder builder, int itemType) { builder.AddInt(1, itemType, 0); }
  public static void AddInternalName(FlatBufferBuilder builder, StringOffset internalNameOffset) { builder.AddOffset(2, internalNameOffset.Value, 0); }
  public static void AddIconName(FlatBufferBuilder builder, StringOffset iconNameOffset) { builder.AddOffset(3, iconNameOffset.Value, 0); }
  public static void AddPrice(FlatBufferBuilder builder, int price) { builder.AddInt(4, price, 0); }
  public static void AddPocket(FlatBufferBuilder builder, int pocket) { builder.AddInt(5, pocket, 0); }
  public static void AddSlotMaxNum(FlatBufferBuilder builder, int slotMaxNum) { builder.AddInt(6, slotMaxNum, 0); }
  public static void AddSortNum(FlatBufferBuilder builder, int sortNum) { builder.AddInt(7, sortNum, 0); }
  public static void AddPriceMegaShard(FlatBufferBuilder builder, int priceMegaShard) { builder.AddInt(8, priceMegaShard, 0); }
  public static void AddPriceColorfulScrew(FlatBufferBuilder builder, int priceColorfulScrew) { builder.AddInt(9, priceColorfulScrew, 0); }
  public static void AddCanNotHold(FlatBufferBuilder builder, bool canNotHold) { builder.AddBool(10, canNotHold, false); }
  public static void AddMachineWaza(FlatBufferBuilder builder, ushort machineWaza) { builder.AddUshort(11, machineWaza, 0); }
  public static void AddMachineIndex(FlatBufferBuilder builder, int machineIndex) { builder.AddInt(12, machineIndex, 0); }
  public static void AddWorkRecvSleep(FlatBufferBuilder builder, bool workRecvSleep) { builder.AddBool(13, workRecvSleep, false); }
  public static void AddWorkRecvPoison(FlatBufferBuilder builder, bool workRecvPoison) { builder.AddBool(14, workRecvPoison, false); }
  public static void AddWorkRecvBurn(FlatBufferBuilder builder, bool workRecvBurn) { builder.AddBool(15, workRecvBurn, false); }
  public static void AddWorkRecvFreeze(FlatBufferBuilder builder, bool workRecvFreeze) { builder.AddBool(16, workRecvFreeze, false); }
  public static void AddWorkRecvParalyze(FlatBufferBuilder builder, bool workRecvParalyze) { builder.AddBool(17, workRecvParalyze, false); }
  public static void AddWorkRecvConfuse(FlatBufferBuilder builder, bool workRecvConfuse) { builder.AddBool(18, workRecvConfuse, false); }
  public static void AddWorkRecvMero(FlatBufferBuilder builder, bool workRecvMero) { builder.AddBool(19, workRecvMero, false); }
  public static void AddWorkAttack(FlatBufferBuilder builder, int workAttack) { builder.AddInt(20, workAttack, 0); }
  public static void AddWorkDefense(FlatBufferBuilder builder, int workDefense) { builder.AddInt(21, workDefense, 0); }
  public static void AddWorkSpAttack(FlatBufferBuilder builder, int workSpAttack) { builder.AddInt(22, workSpAttack, 0); }
  public static void AddWorkSpDefense(FlatBufferBuilder builder, int workSpDefense) { builder.AddInt(23, workSpDefense, 0); }
  public static void AddWorkSpeed(FlatBufferBuilder builder, int workSpeed) { builder.AddInt(24, workSpeed, 0); }
  public static void AddWorkAccuracy(FlatBufferBuilder builder, int workAccuracy) { builder.AddInt(25, workAccuracy, 0); }
  public static void AddWorkCritical(FlatBufferBuilder builder, int workCritical) { builder.AddInt(26, workCritical, 0); }
  public static void AddWorkEffectGuard(FlatBufferBuilder builder, int workEffectGuard) { builder.AddInt(27, workEffectGuard, 0); }
  public static void AddMintNature(FlatBufferBuilder builder, int mintNature) { builder.AddInt(28, mintNature, 0); }
  public static void AddWorkRecvPower(FlatBufferBuilder builder, int workRecvPower) { builder.AddInt(29, workRecvPower, 0); }
  public static void AddHealPercentage(FlatBufferBuilder builder, int healPercentage) { builder.AddInt(30, healPercentage, 0); }
  public static void AddWorkRevival(FlatBufferBuilder builder, int workRevival) { builder.AddInt(31, workRevival, 0); }
  public static void AddRevivePercentage(FlatBufferBuilder builder, int revivePercentage) { builder.AddInt(32, revivePercentage, 0); }
  public static void AddExpPointGain(FlatBufferBuilder builder, int expPointGain) { builder.AddInt(33, expPointGain, 0); }
  public static void AddMaxUseLevel(FlatBufferBuilder builder, int maxUseLevel) { builder.AddInt(34, maxUseLevel, 0); }
  public static void AddWorkFriendly1(FlatBufferBuilder builder, int workFriendly1) { builder.AddInt(35, workFriendly1, 0); }
  public static void AddWorkFriendly2(FlatBufferBuilder builder, int workFriendly2) { builder.AddInt(36, workFriendly2, 0); }
  public static void AddWorkFriendly3(FlatBufferBuilder builder, int workFriendly3) { builder.AddInt(37, workFriendly3, 0); }
  public static void AddWorkEvolutional(FlatBufferBuilder builder, bool workEvolutional) { builder.AddBool(38, workEvolutional, false); }
  public static void AddWorkFormChange(FlatBufferBuilder builder, bool workFormChange) { builder.AddBool(39, workFormChange, false); }
  public static void AddWorkStatusHp(FlatBufferBuilder builder, int workStatusHp) { builder.AddInt(40, workStatusHp, 0); }
  public static void AddWorkStatusAtk(FlatBufferBuilder builder, int workStatusAtk) { builder.AddInt(41, workStatusAtk, 0); }
  public static void AddWorkStatusDef(FlatBufferBuilder builder, int workStatusDef) { builder.AddInt(42, workStatusDef, 0); }
  public static void AddWorkStatusSpd(FlatBufferBuilder builder, int workStatusSpd) { builder.AddInt(43, workStatusSpd, 0); }
  public static void AddWorkStatusSAtk(FlatBufferBuilder builder, int workStatusSAtk) { builder.AddInt(44, workStatusSAtk, 0); }
  public static void AddWorkStatusSDef(FlatBufferBuilder builder, int workStatusSDef) { builder.AddInt(45, workStatusSDef, 0); }
  public static void AddEquipPower(FlatBufferBuilder builder, int equipPower) { builder.AddInt(46, equipPower, 0); }
  public static void AddAutoHealPriority(FlatBufferBuilder builder, int autoHealPriority) { builder.AddInt(47, autoHealPriority, 0); }
  public static void AddCanUseInBattle(FlatBufferBuilder builder, bool canUseInBattle) { builder.AddBool(48, canUseInBattle, false); }
  public static void AddSwapIntoId(FlatBufferBuilder builder, int swapIntoId) { builder.AddInt(49, swapIntoId, 0); }
  public static Offset<ZaItemData> EndZaItemData(FlatBufferBuilder builder) {
    int o = builder.EndTable();
    builder.Required(o, 8); // InternalName
    builder.Required(o, 10); // IconName
    return new Offset<ZaItemData>(o);
  }
}
