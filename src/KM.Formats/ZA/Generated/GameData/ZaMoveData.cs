// SPDX-License-Identifier: GPL-3.0-only

using global::System;
using Google.FlatBuffers;

namespace KM.Formats.ZA.Generated.GameData;

public struct ZaMoveData : IFlatbufferObject
{
  private Table __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public static void ValidateVersion() { FlatBufferConstants.FLATBUFFERS_25_2_10(); }
  public static ZaMoveData GetRootAsZaMoveData(ByteBuffer _bb) { return GetRootAsZaMoveData(_bb, new ZaMoveData()); }
  public static ZaMoveData GetRootAsZaMoveData(ByteBuffer _bb, ZaMoveData obj) { return (obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb)); }
  public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }
  public ZaMoveData __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public ushort MoveId { get { int o = __p.__offset(4); return o != 0 ? __p.bb.GetUshort(o + __p.bb_pos) : (ushort)0; } }
  public bool CanUseMove { get { int o = __p.__offset(6); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public byte Type { get { int o = __p.__offset(8); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public byte Quality { get { int o = __p.__offset(10); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public byte Category { get { int o = __p.__offset(12); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public byte Power { get { int o = __p.__offset(14); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public byte Accuracy { get { int o = __p.__offset(16); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public byte Pp { get { int o = __p.__offset(18); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public sbyte Priority { get { int o = __p.__offset(20); return o != 0 ? __p.bb.GetSbyte(o + __p.bb_pos) : (sbyte)0; } }
  public byte HitMax { get { int o = __p.__offset(22); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public byte HitMin { get { int o = __p.__offset(24); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public ZaMoveInflict? Inflict { get { int o = __p.__offset(26); return o != 0 ? (ZaMoveInflict?)(new ZaMoveInflict()).__assign(o + __p.bb_pos, __p.bb) : null; } }
  public byte CritStage { get { int o = __p.__offset(28); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public byte Flinch { get { int o = __p.__offset(30); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public ushort EffectSequence { get { int o = __p.__offset(32); return o != 0 ? __p.bb.GetUshort(o + __p.bb_pos) : (ushort)0; } }
  public sbyte Recoil { get { int o = __p.__offset(34); return o != 0 ? __p.bb.GetSbyte(o + __p.bb_pos) : (sbyte)0; } }
  public sbyte RawHealing { get { int o = __p.__offset(36); return o != 0 ? __p.bb.GetSbyte(o + __p.bb_pos) : (sbyte)0; } }
  public byte RawTarget { get { int o = __p.__offset(38); return o != 0 ? __p.bb.Get(o + __p.bb_pos) : (byte)0; } }
  public ZaMoveStatChanges? StatChanges { get { int o = __p.__offset(40); return o != 0 ? (ZaMoveStatChanges?)(new ZaMoveStatChanges()).__assign(o + __p.bb_pos, __p.bb) : null; } }
  public sbyte Affinity { get { int o = __p.__offset(42); return o != 0 ? __p.bb.GetSbyte(o + __p.bb_pos) : (sbyte)0; } }
  public bool FlagMakesContact { get { int o = __p.__offset(44); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagCharge { get { int o = __p.__offset(46); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagRecharge { get { int o = __p.__offset(48); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagProtect { get { int o = __p.__offset(50); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagReflectable { get { int o = __p.__offset(52); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagSnatch { get { int o = __p.__offset(54); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagMirror { get { int o = __p.__offset(56); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagPunch { get { int o = __p.__offset(58); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagSound { get { int o = __p.__offset(60); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagDance { get { int o = __p.__offset(62); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagGravity { get { int o = __p.__offset(64); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagDefrost { get { int o = __p.__offset(66); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagDistanceTriple { get { int o = __p.__offset(68); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagHeal { get { int o = __p.__offset(70); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagIgnoreSubstitute { get { int o = __p.__offset(72); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagFailSkyBattle { get { int o = __p.__offset(74); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagAnimateAlly { get { int o = __p.__offset(76); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagMetronome { get { int o = __p.__offset(78); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagFailEncore { get { int o = __p.__offset(80); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagFailMeFirst { get { int o = __p.__offset(82); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagFutureAttack { get { int o = __p.__offset(84); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagPressure { get { int o = __p.__offset(86); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagCombo { get { int o = __p.__offset(88); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagNoSleepTalk { get { int o = __p.__offset(90); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagNoAssist { get { int o = __p.__offset(92); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagFailCopycat { get { int o = __p.__offset(94); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagFailMimic { get { int o = __p.__offset(96); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagFailInstruct { get { int o = __p.__offset(98); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagPowder { get { int o = __p.__offset(100); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagBite { get { int o = __p.__offset(102); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagBullet { get { int o = __p.__offset(104); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagNoMultiHit { get { int o = __p.__offset(106); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagNoEffectiveness { get { int o = __p.__offset(108); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagSheerForce { get { int o = __p.__offset(110); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagSlicing { get { int o = __p.__offset(112); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagWind { get { int o = __p.__offset(114); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unknown56 { get { int o = __p.__offset(116); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unknown57 { get { int o = __p.__offset(118); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unknown58 { get { int o = __p.__offset(120); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unknown59 { get { int o = __p.__offset(122); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unknown60 { get { int o = __p.__offset(124); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused61 { get { int o = __p.__offset(126); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused62 { get { int o = __p.__offset(128); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused63 { get { int o = __p.__offset(130); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused64 { get { int o = __p.__offset(132); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused65 { get { int o = __p.__offset(134); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused66 { get { int o = __p.__offset(136); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused67 { get { int o = __p.__offset(138); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused68 { get { int o = __p.__offset(140); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused69 { get { int o = __p.__offset(142); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool Unused70 { get { int o = __p.__offset(144); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }
  public bool FlagCantUseTwice { get { int o = __p.__offset(146); return o != 0 ? 0!=__p.bb.Get(o + __p.bb_pos) : (bool)false; } }

  public static void StartZaMoveData(FlatBufferBuilder builder) { builder.StartTable(72); }
  public static void AddMoveId(FlatBufferBuilder builder, ushort moveId) { builder.AddUshort(0, moveId, 0); }
  public static void AddCanUseMove(FlatBufferBuilder builder, bool canUseMove) { builder.AddBool(1, canUseMove, false); }
  public static void AddType(FlatBufferBuilder builder, byte type) { builder.AddByte(2, type, 0); }
  public static void AddQuality(FlatBufferBuilder builder, byte quality) { builder.AddByte(3, quality, 0); }
  public static void AddCategory(FlatBufferBuilder builder, byte category) { builder.AddByte(4, category, 0); }
  public static void AddPower(FlatBufferBuilder builder, byte power) { builder.AddByte(5, power, 0); }
  public static void AddAccuracy(FlatBufferBuilder builder, byte accuracy) { builder.AddByte(6, accuracy, 0); }
  public static void AddPp(FlatBufferBuilder builder, byte pp) { builder.AddByte(7, pp, 0); }
  public static void AddPriority(FlatBufferBuilder builder, sbyte priority) { builder.AddSbyte(8, priority, 0); }
  public static void AddHitMax(FlatBufferBuilder builder, byte hitMax) { builder.AddByte(9, hitMax, 0); }
  public static void AddHitMin(FlatBufferBuilder builder, byte hitMin) { builder.AddByte(10, hitMin, 0); }
  public static void AddInflict(FlatBufferBuilder builder, Offset<ZaMoveInflict> inflictOffset) { builder.AddStruct(11, inflictOffset.Value, 0); }
  public static void AddCritStage(FlatBufferBuilder builder, byte critStage) { builder.AddByte(12, critStage, 0); }
  public static void AddFlinch(FlatBufferBuilder builder, byte flinch) { builder.AddByte(13, flinch, 0); }
  public static void AddEffectSequence(FlatBufferBuilder builder, ushort effectSequence) { builder.AddUshort(14, effectSequence, 0); }
  public static void AddRecoil(FlatBufferBuilder builder, sbyte recoil) { builder.AddSbyte(15, recoil, 0); }
  public static void AddRawHealing(FlatBufferBuilder builder, sbyte rawHealing) { builder.AddSbyte(16, rawHealing, 0); }
  public static void AddRawTarget(FlatBufferBuilder builder, byte rawTarget) { builder.AddByte(17, rawTarget, 0); }
  public static void AddStatChanges(FlatBufferBuilder builder, Offset<ZaMoveStatChanges> statChangesOffset) { builder.AddStruct(18, statChangesOffset.Value, 0); }
  public static void AddAffinity(FlatBufferBuilder builder, sbyte affinity) { builder.AddSbyte(19, affinity, 0); }
  public static void AddFlagMakesContact(FlatBufferBuilder builder, bool flagMakesContact) { builder.AddBool(20, flagMakesContact, false); }
  public static void AddFlagCharge(FlatBufferBuilder builder, bool flagCharge) { builder.AddBool(21, flagCharge, false); }
  public static void AddFlagRecharge(FlatBufferBuilder builder, bool flagRecharge) { builder.AddBool(22, flagRecharge, false); }
  public static void AddFlagProtect(FlatBufferBuilder builder, bool flagProtect) { builder.AddBool(23, flagProtect, false); }
  public static void AddFlagReflectable(FlatBufferBuilder builder, bool flagReflectable) { builder.AddBool(24, flagReflectable, false); }
  public static void AddFlagSnatch(FlatBufferBuilder builder, bool flagSnatch) { builder.AddBool(25, flagSnatch, false); }
  public static void AddFlagMirror(FlatBufferBuilder builder, bool flagMirror) { builder.AddBool(26, flagMirror, false); }
  public static void AddFlagPunch(FlatBufferBuilder builder, bool flagPunch) { builder.AddBool(27, flagPunch, false); }
  public static void AddFlagSound(FlatBufferBuilder builder, bool flagSound) { builder.AddBool(28, flagSound, false); }
  public static void AddFlagDance(FlatBufferBuilder builder, bool flagDance) { builder.AddBool(29, flagDance, false); }
  public static void AddFlagGravity(FlatBufferBuilder builder, bool flagGravity) { builder.AddBool(30, flagGravity, false); }
  public static void AddFlagDefrost(FlatBufferBuilder builder, bool flagDefrost) { builder.AddBool(31, flagDefrost, false); }
  public static void AddFlagDistanceTriple(FlatBufferBuilder builder, bool flagDistanceTriple) { builder.AddBool(32, flagDistanceTriple, false); }
  public static void AddFlagHeal(FlatBufferBuilder builder, bool flagHeal) { builder.AddBool(33, flagHeal, false); }
  public static void AddFlagIgnoreSubstitute(FlatBufferBuilder builder, bool flagIgnoreSubstitute) { builder.AddBool(34, flagIgnoreSubstitute, false); }
  public static void AddFlagFailSkyBattle(FlatBufferBuilder builder, bool flagFailSkyBattle) { builder.AddBool(35, flagFailSkyBattle, false); }
  public static void AddFlagAnimateAlly(FlatBufferBuilder builder, bool flagAnimateAlly) { builder.AddBool(36, flagAnimateAlly, false); }
  public static void AddFlagMetronome(FlatBufferBuilder builder, bool flagMetronome) { builder.AddBool(37, flagMetronome, false); }
  public static void AddFlagFailEncore(FlatBufferBuilder builder, bool flagFailEncore) { builder.AddBool(38, flagFailEncore, false); }
  public static void AddFlagFailMeFirst(FlatBufferBuilder builder, bool flagFailMeFirst) { builder.AddBool(39, flagFailMeFirst, false); }
  public static void AddFlagFutureAttack(FlatBufferBuilder builder, bool flagFutureAttack) { builder.AddBool(40, flagFutureAttack, false); }
  public static void AddFlagPressure(FlatBufferBuilder builder, bool flagPressure) { builder.AddBool(41, flagPressure, false); }
  public static void AddFlagCombo(FlatBufferBuilder builder, bool flagCombo) { builder.AddBool(42, flagCombo, false); }
  public static void AddFlagNoSleepTalk(FlatBufferBuilder builder, bool flagNoSleepTalk) { builder.AddBool(43, flagNoSleepTalk, false); }
  public static void AddFlagNoAssist(FlatBufferBuilder builder, bool flagNoAssist) { builder.AddBool(44, flagNoAssist, false); }
  public static void AddFlagFailCopycat(FlatBufferBuilder builder, bool flagFailCopycat) { builder.AddBool(45, flagFailCopycat, false); }
  public static void AddFlagFailMimic(FlatBufferBuilder builder, bool flagFailMimic) { builder.AddBool(46, flagFailMimic, false); }
  public static void AddFlagFailInstruct(FlatBufferBuilder builder, bool flagFailInstruct) { builder.AddBool(47, flagFailInstruct, false); }
  public static void AddFlagPowder(FlatBufferBuilder builder, bool flagPowder) { builder.AddBool(48, flagPowder, false); }
  public static void AddFlagBite(FlatBufferBuilder builder, bool flagBite) { builder.AddBool(49, flagBite, false); }
  public static void AddFlagBullet(FlatBufferBuilder builder, bool flagBullet) { builder.AddBool(50, flagBullet, false); }
  public static void AddFlagNoMultiHit(FlatBufferBuilder builder, bool flagNoMultiHit) { builder.AddBool(51, flagNoMultiHit, false); }
  public static void AddFlagNoEffectiveness(FlatBufferBuilder builder, bool flagNoEffectiveness) { builder.AddBool(52, flagNoEffectiveness, false); }
  public static void AddFlagSheerForce(FlatBufferBuilder builder, bool flagSheerForce) { builder.AddBool(53, flagSheerForce, false); }
  public static void AddFlagSlicing(FlatBufferBuilder builder, bool flagSlicing) { builder.AddBool(54, flagSlicing, false); }
  public static void AddFlagWind(FlatBufferBuilder builder, bool flagWind) { builder.AddBool(55, flagWind, false); }
  public static void AddUnknown56(FlatBufferBuilder builder, bool unknown56) { builder.AddBool(56, unknown56, false); }
  public static void AddUnknown57(FlatBufferBuilder builder, bool unknown57) { builder.AddBool(57, unknown57, false); }
  public static void AddUnknown58(FlatBufferBuilder builder, bool unknown58) { builder.AddBool(58, unknown58, false); }
  public static void AddUnknown59(FlatBufferBuilder builder, bool unknown59) { builder.AddBool(59, unknown59, false); }
  public static void AddUnknown60(FlatBufferBuilder builder, bool unknown60) { builder.AddBool(60, unknown60, false); }
  public static void AddUnused61(FlatBufferBuilder builder, bool unused61) { builder.AddBool(61, unused61, false); }
  public static void AddUnused62(FlatBufferBuilder builder, bool unused62) { builder.AddBool(62, unused62, false); }
  public static void AddUnused63(FlatBufferBuilder builder, bool unused63) { builder.AddBool(63, unused63, false); }
  public static void AddUnused64(FlatBufferBuilder builder, bool unused64) { builder.AddBool(64, unused64, false); }
  public static void AddUnused65(FlatBufferBuilder builder, bool unused65) { builder.AddBool(65, unused65, false); }
  public static void AddUnused66(FlatBufferBuilder builder, bool unused66) { builder.AddBool(66, unused66, false); }
  public static void AddUnused67(FlatBufferBuilder builder, bool unused67) { builder.AddBool(67, unused67, false); }
  public static void AddUnused68(FlatBufferBuilder builder, bool unused68) { builder.AddBool(68, unused68, false); }
  public static void AddUnused69(FlatBufferBuilder builder, bool unused69) { builder.AddBool(69, unused69, false); }
  public static void AddUnused70(FlatBufferBuilder builder, bool unused70) { builder.AddBool(70, unused70, false); }
  public static void AddFlagCantUseTwice(FlatBufferBuilder builder, bool flagCantUseTwice) { builder.AddBool(71, flagCantUseTwice, false); }
  public static Offset<ZaMoveData> EndZaMoveData(FlatBufferBuilder builder) {
    int o = builder.EndTable();
    return new Offset<ZaMoveData>(o);
  }
}


static public class ZaMoveDataVerify
{
  static public bool Verify(Google.FlatBuffers.Verifier verifier, uint tablePos)
  {
    return verifier.VerifyTableStart(tablePos)
      && verifier.VerifyField(tablePos, 4 /*MoveId*/, 2 /*ushort*/, 2, false)
      && verifier.VerifyField(tablePos, 6 /*CanUseMove*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 8 /*Type*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 10 /*Quality*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 12 /*Category*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 14 /*Power*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 16 /*Accuracy*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 18 /*Pp*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 20 /*Priority*/, 1 /*sbyte*/, 1, false)
      && verifier.VerifyField(tablePos, 22 /*HitMax*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 24 /*HitMin*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 26 /*Inflict*/, 6 /*ZaMoveInflict*/, 2, false)
      && verifier.VerifyField(tablePos, 28 /*CritStage*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 30 /*Flinch*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 32 /*EffectSequence*/, 2 /*ushort*/, 2, false)
      && verifier.VerifyField(tablePos, 34 /*Recoil*/, 1 /*sbyte*/, 1, false)
      && verifier.VerifyField(tablePos, 36 /*RawHealing*/, 1 /*sbyte*/, 1, false)
      && verifier.VerifyField(tablePos, 38 /*RawTarget*/, 1 /*byte*/, 1, false)
      && verifier.VerifyField(tablePos, 40 /*StatChanges*/, 9 /*ZaMoveStatChanges*/, 1, false)
      && verifier.VerifyField(tablePos, 42 /*Affinity*/, 1 /*sbyte*/, 1, false)
      && verifier.VerifyField(tablePos, 44 /*FlagMakesContact*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 46 /*FlagCharge*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 48 /*FlagRecharge*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 50 /*FlagProtect*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 52 /*FlagReflectable*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 54 /*FlagSnatch*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 56 /*FlagMirror*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 58 /*FlagPunch*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 60 /*FlagSound*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 62 /*FlagDance*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 64 /*FlagGravity*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 66 /*FlagDefrost*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 68 /*FlagDistanceTriple*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 70 /*FlagHeal*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 72 /*FlagIgnoreSubstitute*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 74 /*FlagFailSkyBattle*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 76 /*FlagAnimateAlly*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 78 /*FlagMetronome*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 80 /*FlagFailEncore*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 82 /*FlagFailMeFirst*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 84 /*FlagFutureAttack*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 86 /*FlagPressure*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 88 /*FlagCombo*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 90 /*FlagNoSleepTalk*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 92 /*FlagNoAssist*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 94 /*FlagFailCopycat*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 96 /*FlagFailMimic*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 98 /*FlagFailInstruct*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 100 /*FlagPowder*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 102 /*FlagBite*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 104 /*FlagBullet*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 106 /*FlagNoMultiHit*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 108 /*FlagNoEffectiveness*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 110 /*FlagSheerForce*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 112 /*FlagSlicing*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 114 /*FlagWind*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 116 /*Unknown56*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 118 /*Unknown57*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 120 /*Unknown58*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 122 /*Unknown59*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 124 /*Unknown60*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 126 /*Unused61*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 128 /*Unused62*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 130 /*Unused63*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 132 /*Unused64*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 134 /*Unused65*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 136 /*Unused66*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 138 /*Unused67*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 140 /*Unused68*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 142 /*Unused69*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 144 /*Unused70*/, 1 /*bool*/, 1, false)
      && verifier.VerifyField(tablePos, 146 /*FlagCantUseTwice*/, 1 /*bool*/, 1, false)
      && verifier.VerifyTableEnd(tablePos);
  }
}
