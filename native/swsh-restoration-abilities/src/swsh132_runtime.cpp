// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_restoration_runtime.hpp"
#include "km_swsh_restoration_abilities.hpp"
#include "km_swsh_reactive_abilities.hpp"
#include "km_swsh_support_abilities.hpp"
#include "km_swsh_field_abilities.hpp"
#include "km_swsh_ruin_abilities.hpp"
#include "km_swsh_copy_abilities.hpp"
#include "km_swsh_conversion_abilities.hpp"
#include "km_swsh_wind_abilities.hpp"
#include "swsh132_profile.hpp"
#include "km_swsh_effectiveness_abilities.hpp"
#include "km_swsh_battle_state_abilities.hpp"
#include "km_swsh_form_abilities.hpp"
#include "km_swsh_effect_source_abilities.hpp"
#include "km_swsh_paradox_abilities.hpp"
#include "km_swsh_berry_abilities.hpp"
#include "km_swsh_protection_abilities.hpp"
#include "km_swsh_commander_ability.hpp"
#include "km_swsh_party_cure_ability.hpp"

extern "C" {
uintptr_t km_dispatch_resume{}, km_invulnerability_resume{}, km_switch_resume{};
uintptr_t km_automatic_command_resume{};
bool km_original_automatic_command(void*, void*, uint64_t*);
void km_original_dispatch(void*, uint32_t, uint32_t, bool);
bool km_original_invulnerability(void*, void*, void*, uint32_t, bool*);
void km_original_switch(void*, uint8_t*, const void*);
uintptr_t km_effectiveness_resume{};
uint32_t km_original_effectiveness(void*, void*, void*, uint32_t, uint32_t);
uintptr_t km_copy_property_resume{}, km_suppress_property_resume{}, km_swap_property_resume{}, km_transform_property_resume{};
bool km_original_copy_property(uint32_t);
bool km_original_suppress_property(uint32_t);
bool km_original_swap_property(uint32_t);
bool km_original_transform_property(uint32_t);
uintptr_t km_damage_weather_resume{};
uint32_t km_original_damage_weather(void*, void*, void*, const void*, uint32_t, uint32_t, uint32_t, uint32_t, uint32_t, uint32_t, void*);
uintptr_t km_status_submission_resume{}, km_rank_submission_resume{};
bool km_original_status_submission(void*, const km::restoration::NativeStatusRequest*);
bool km_original_rank_submission(void*, const km::restoration::NativeRankRequest*);
}

namespace km::restoration {
namespace {

struct AbilityEvent {
    uint32_t event;
    uint32_t reserved;
    void (*callback)(void*, uint32_t);
};
struct AbilityRow {
    uint32_t ability;
    uint32_t reserved;
    const AbilityEvent* (*factory)(uint32_t*);
};
static_assert(sizeof(AbilityEvent) == 16);
static_assert(sizeof(AbilityRow) == 16);
constexpr size_t OriginalCount = sizeof(swsh132::OriginalAbilities)
    / sizeof(swsh132::OriginalAbilities[0]);
constexpr size_t HandlerCount = 45;
// The native dispatcher reads pairs. An unreachable sentinel fills an odd tail.
constexpr size_t ExtendedCount = OriginalCount + ((HandlerCount + 1) & ~size_t{1});
static_assert(OriginalCount == 260 && ExtendedCount % 2 == 0);

AbilityRow g_table[ExtendedCount]{};
uint16_t g_breakable[82]{};
AbilityEvent g_thermal_events[6]{};
AbilityEvent g_salt_events[4]{};
AbilityEvent g_armor_tail_events[1]{};
AbilityEvent g_minds_eye_events[7]{};
AbilityEvent g_eelevate_events[4]{};
AbilityEvent g_charge_events[6]{};
NativeAbilityApi g_api{};
NativeDamageAbilityApi g_damage_api{};
NativeReactiveAbilityApi g_reactive_api{};
NativePartyCureApi g_cure_api{};
void* (*g_registry)(void*){};
void* (*g_find_node)(void*, uint32_t, uint32_t){};
uint32_t (*g_node_ability)(void*){};
uint32_t (*g_node_owner)(void*){};
uint32_t (*g_node_kind)(void*){};
bool (*g_filtered)(void*, void*, uint32_t){};

NativeSupportAbilityApi g_support_api{};
NativeFieldAbilityApi g_field_api{};
NativeGuardDogApi g_guard_api{};
NativeRuinAbilityApi g_ruin_api{};
NativeCopyAbilityApi g_copy_api{};
NativeConversionAbilityApi g_conversion_api{};
NativeWindAbilityApi g_wind_api{};
NativePersistentChargeApi g_charge_api{};
NativeEffectivenessAbilityApi g_effectiveness_api{};
NativeBattleStateAbilityApi g_battle_state_api{};
NativeFormAbilityApi g_form_api{};
NativeEffectSourceAbilityApi g_effect_source_api{};
NativeParadoxAbilityApi g_paradox_api{};
NativeBerryAbilityApi g_berry_api{};
NativeProtectionAbilityApi g_protection_api{};
NativeCommanderApi g_commander_api{};
AbilityEvent g_drill_events[5]{};
bool (*g_bound_event)(void*, uint32_t, uint32_t*){};
uint32_t (*g_owner_weather)(void*, uint32_t){};
void (*g_residual_event)(void*, void*, const uint16_t*){};
uint32_t (*g_move_category)(uint32_t){};
void (*g_stall_order)(void*, uint32_t){};
void (*g_bypass_begin)(void*, uint32_t){};
void (*g_bypass_end)(void*, uint32_t){};
void (*g_bypass_reset)(void*, uint32_t){};
void (*g_status_operation)(void*, void*, const NativeStatusRequest*){};
void* (*g_operation_battle)(void*){};
uintptr_t (*g_append_party)(void*, void*){};
void (*g_reset_form)(void*, uint32_t, uint32_t){};
void (*g_set_raw_ability)(void*, uint32_t){};
uintptr_t (*g_manager_construct)(void*, void*){};
uintptr_t (*g_manager_destroy)(void*){};
void (*g_push_event)(void*){};
void (*g_pop_event)(void*){};
void (*g_event_readonly)(void*, uint32_t, uint32_t){};
void (*g_event_writable)(void*, uint32_t, uint32_t){};
void (*g_dispatch_event)(void*, uint32_t){};
uint32_t (*g_read_event)(void*, uint32_t){};
uint32_t (*g_battler_id)(void*){};
void* (*g_side_condition)(void*, uint32_t, uint32_t){};
bool (*g_condition_active)(void*){};
uintptr_t g_installed_base{};
bool g_attempted{};

void ToxicChainCallback(void* context, uint32_t owner) {
    (void)ToxicChain(context, owner, g_api);
}
const AbilityEvent ToxicChainEvents[] = {{98, 0, ToxicChainCallback}};
const AbilityEvent* ToxicChainFactory(uint32_t* count) {
    *count = 0x60001; // Native damaging-hit priority 6, one callback.
    return ToxicChainEvents;
}
const AbilityEvent* MindsEyeFactory(uint32_t* count) {
    // Accuracy protection/evasion handling plus Ghost immunity handling. The
    // native Scrappy Intimidate-immunity callbacks are not part of Mind's Eye.
    *count = 7;
    return g_minds_eye_events;
}

void SharpnessCallback(void* context, uint32_t owner) {
    (void)Sharpness(context, owner, g_damage_api);
}
void RockyPayloadCallback(void* context, uint32_t owner) {
    (void)RockyPayload(context, owner, g_damage_api);
}
const AbilityEvent SharpnessEvents[] = {{72, 0, SharpnessCallback}};
const AbilityEvent RockyPayloadEvents[] = {{76, 0, RockyPayloadCallback}};
const AbilityEvent* SharpnessFactory(uint32_t* count) {
    *count = 1;
    return SharpnessEvents;
}
const AbilityEvent* RockyPayloadFactory(uint32_t* count) {
    *count = 1;
    return RockyPayloadEvents;
}

void SeedSowerCallback(void* context, uint32_t owner) {
    (void)SeedSower(context, owner, g_reactive_api);
}
const AbilityEvent SeedSowerEvents[] = {{98, 0, SeedSowerCallback}};
const AbilityEvent* SeedSowerFactory(uint32_t* count) {
    *count = 1;
    return SeedSowerEvents;
}
void WellBakedBodyCallback(void* context, uint32_t owner) {
    (void)WellBakedBody(context, owner, g_reactive_api);
}
const AbilityEvent WellBakedBodyEvents[] = {{58, 0, WellBakedBodyCallback}};
const AbilityEvent* WellBakedBodyFactory(uint32_t* count) {
    *count = 1;
    return WellBakedBodyEvents;
}
void EarthEaterCallback(void* context, uint32_t owner) {
    (void)EarthEater(context, owner, g_reactive_api);
}
const AbilityEvent EarthEaterEvents[] = {{58, 0, EarthEaterCallback}};
const AbilityEvent* EarthEaterFactory(uint32_t* count) {
    *count = 1;
    return EarthEaterEvents;
}
void GoodAsGoldCallback(void* context, uint32_t owner) {
    (void)GoodAsGold(context, owner, g_reactive_api);
}
const AbilityEvent GoodAsGoldEvents[] = {{58, 0, GoodAsGoldCallback}};
const AbilityEvent* GoodAsGoldFactory(uint32_t* count) {
    *count = 1;
    return GoodAsGoldEvents;
}
void ThermalExchangeHitCallback(void* context, uint32_t owner) {
    (void)ThermalExchangeHit(context, owner, g_reactive_api);
}
void PurifyingSaltStatusCallback(void* context, uint32_t owner) {
    (void)PurifyingSaltStatus(context, owner, g_reactive_api);
}
void PurifyingSaltDamageCallback(void* context, uint32_t owner) {
    (void)PurifyingSaltDamage(context, owner, g_reactive_api);
}
const AbilityEvent* ThermalExchangeFactory(uint32_t* count) {
    *count = 6;
    return g_thermal_events;
}
const AbilityEvent* PurifyingSaltFactory(uint32_t* count) {
    *count = 4;
    return g_salt_events;
}
const AbilityEvent* ArmorTailFactory(uint32_t* count) {
    *count = 1;
    return g_armor_tail_events;
}

void LingeringAromaCallback(void* context, uint32_t owner) {
    (void)LingeringAroma(context, owner, g_support_api);
}
const AbilityEvent LingeringAromaEvents[] = {{98, 0, LingeringAromaCallback}};
const AbilityEvent* LingeringAromaFactory(uint32_t* count) {
    *count = 1;
    return LingeringAromaEvents;
}
void ToxicDebrisCallback(void* context, uint32_t owner) {
    (void)ToxicDebris(context, owner, g_support_api);
}
const AbilityEvent ToxicDebrisEvents[] = {{98, 0, ToxicDebrisCallback}};
const AbilityEvent* ToxicDebrisFactory(uint32_t* count) {
    *count = 1;
    return ToxicDebrisEvents;
}
void HospitalityCallback(void* context, uint32_t owner) {
    (void)Hospitality(context, owner, g_support_api);
}
const AbilityEvent HospitalityEvents[] = {{111, 0, HospitalityCallback}, {175, 0, HospitalityCallback}};
const AbilityEvent* HospitalityFactory(uint32_t* count) {
    *count = 2;
    return HospitalityEvents;
}

void AngerShellBeforeMoveCallback(void* context, uint32_t owner) {
    (void)AngerShellBeforeMove(context, owner, g_field_api);
}
void AngerShellBeforeDamageCallback(void* context, uint32_t owner) {
    (void)AngerShellBeforeDamage(context, owner, g_field_api);
}
void AngerShellAfterMoveCallback(void* context, uint32_t owner) {
    (void)AngerShellAfterMove(context, owner, g_field_api);
}
const AbilityEvent AngerShellEvents[] = {{163, 0, AngerShellBeforeMoveCallback}, {140, 0, AngerShellBeforeDamageCallback}, {165, 0, AngerShellAfterMoveCallback}};
const AbilityEvent* AngerShellFactory(uint32_t* count) {
    *count = 3;
    return AngerShellEvents;
}
void OrichalcumPulseEntryCallback(void* context, uint32_t owner) {
    (void)OrichalcumPulseEntry(context, owner, g_field_api);
}
void OrichalcumPulseDamageCallback(void* context, uint32_t owner) {
    (void)OrichalcumPulseDamage(context, owner, g_field_api);
}
const AbilityEvent OrichalcumPulseEvents[] = {{111, 0, OrichalcumPulseEntryCallback}, {175, 0, OrichalcumPulseEntryCallback}, {76, 0, OrichalcumPulseDamageCallback}};
const AbilityEvent* OrichalcumPulseFactory(uint32_t* count) {
    *count = 3;
    return OrichalcumPulseEvents;
}
void HadronEngineEntryCallback(void* context, uint32_t owner) {
    (void)HadronEngineEntry(context, owner, g_field_api);
}
void HadronEngineDamageCallback(void* context, uint32_t owner) {
    (void)HadronEngineDamage(context, owner, g_field_api);
}
const AbilityEvent HadronEngineEvents[] = {{111, 0, HadronEngineEntryCallback}, {175, 0, HadronEngineEntryCallback}, {76, 0, HadronEngineDamageCallback}};
const AbilityEvent* HadronEngineFactory(uint32_t* count) {
    *count = 3;
    return HadronEngineEvents;
}

bool GuardDogIntimidateCallback(void* context, const NativeRankRequest* request) {
    return GuardDogIntimidate(context, request, g_guard_api);
}
void GuardDogForcedSwitchCallback(void* context, uint32_t owner) {
    (void)GuardDogForcedSwitch(context, owner, g_guard_api);
}
const AbilityEvent GuardDogEvents[] = {{176, 0, GuardDogForcedSwitchCallback}};
const AbilityEvent* GuardDogFactory(uint32_t* count) {
    *count = 1;
    return GuardDogEvents;
}

void RuinEntryCallback(void* context, uint32_t owner) {
    RuinEntry(context, owner, g_ruin_api);
}
void VesselOfRuinCallback(void* context, uint32_t owner) {
    (void)RuinStat(context, owner, 284, false, 2, g_ruin_api);
}
const AbilityEvent VesselOfRuinEvents[] = {{111, 0, RuinEntryCallback}, {175, 0, RuinEntryCallback}, {76, 0, VesselOfRuinCallback}};
const AbilityEvent* VesselOfRuinFactory(uint32_t* count) {
    *count = 3;
    return VesselOfRuinEvents;
}
void SwordOfRuinCallback(void* context, uint32_t owner) {
    (void)RuinStat(context, owner, 285, true, 1, g_ruin_api);
}
const AbilityEvent SwordOfRuinEvents[] = {{111, 0, RuinEntryCallback}, {175, 0, RuinEntryCallback}, {77, 0, SwordOfRuinCallback}};
const AbilityEvent* SwordOfRuinFactory(uint32_t* count) {
    *count = 3;
    return SwordOfRuinEvents;
}
void TabletsOfRuinCallback(void* context, uint32_t owner) {
    (void)RuinStat(context, owner, 286, false, 1, g_ruin_api);
}
const AbilityEvent TabletsOfRuinEvents[] = {{111, 0, RuinEntryCallback}, {175, 0, RuinEntryCallback}, {76, 0, TabletsOfRuinCallback}};
const AbilityEvent* TabletsOfRuinFactory(uint32_t* count) {
    *count = 3;
    return TabletsOfRuinEvents;
}
void BeadsOfRuinCallback(void* context, uint32_t owner) {
    (void)RuinStat(context, owner, 287, true, 2, g_ruin_api);
}
const AbilityEvent BeadsOfRuinEvents[] = {{111, 0, RuinEntryCallback}, {175, 0, RuinEntryCallback}, {77, 0, BeadsOfRuinCallback}};
const AbilityEvent* BeadsOfRuinFactory(uint32_t* count) {
    *count = 3;
    return BeadsOfRuinEvents;
}

void CostarCallback(void* context, uint32_t owner) {
    (void)Costar(context, owner, g_copy_api);
}
const AbilityEvent CostarEvents[] = {{111, 0, CostarCallback}, {175, 0, CostarCallback}};
const AbilityEvent* CostarFactory(uint32_t* count) {
    *count = 0x90002; // Run after normal priority-7 entry effects.
    return CostarEvents;
}
void FireManeCallback(void* context, uint32_t owner) {
    (void)FireMane(context, owner, g_damage_api);
}
const AbilityEvent FireManeEvents[] = {{76, 0, FireManeCallback}};
const AbilityEvent* FireManeFactory(uint32_t* count) {
    *count = 1;
    return FireManeEvents;
}

void DragonizeTypeCallback(void* context, uint32_t owner) { (void)DragonizeType(context, owner, g_conversion_api); }
void DragonizePowerCallback(void* context, uint32_t owner) { (void)DragonizePower(context, owner, g_conversion_api); }
void DragonizeResetCallback(void* context, uint32_t) { DragonizeReset(context, g_conversion_api); }
void DragonizeMaxCallback(void* context, uint32_t owner) { (void)DragonizeMaxMove(context, owner, g_conversion_api); }
const AbilityEvent DragonizeEvents[] = {{42, 0, DragonizeTypeCallback}, {72, 0, DragonizePowerCallback},
    {171, 0, DragonizeResetCallback}, {228, 0, DragonizeMaxCallback}};
const AbilityEvent* DragonizeFactory(uint32_t* count) { *count = 4; return DragonizeEvents; }
void SpicySprayCallback(void* context, uint32_t owner) { (void)SpicySpray(context, owner, g_api); }
const AbilityEvent SpicySprayEvents[] = {{98, 0, SpicySprayCallback}};
const AbilityEvent* SpicySprayFactory(uint32_t* count) { *count = 1; return SpicySprayEvents; }

void AuraGuardCallback(void* context, uint32_t owner) { (void)AuraGuard(context, owner, g_damage_api); }
const AbilityEvent AuraGuardEvents[] = {{92, 0, AuraGuardCallback}};
const AbilityEvent* AuraGuardFactory(uint32_t* count) { *count = 1; return AuraGuardEvents; }
const AbilityEvent* EelevateFactory(uint32_t* count) { *count = 4; return g_eelevate_events; }
uint32_t GroundingAbilityCallback(void* battler, uint32_t field) {
    // Only the native groundedness check uses this adapter. Everywhere else
    // Eelevate retains its own identity, including suppression and ability text.
    const auto ability = g_guard_api.battler_value(battler, field);
    return field == 19 && ability == 313 ? 26 : ability;
}

const AbilityEvent* EmptyFactory(uint32_t* count) { *count = 0; return nullptr; }
void WindRiderEntryCallback(void* context, uint32_t owner) { (void)WindRiderEntry(context, owner, g_wind_api); }
void WindRiderImmunityCallback(void* context, uint32_t owner) { (void)WindRiderImmunity(context, owner, g_wind_api); }
void WindPowerCallback(void* context, uint32_t owner) { (void)WindChargeHit(context, owner, true, g_wind_api); }
void ElectromorphosisCallback(void* context, uint32_t owner) { (void)WindChargeHit(context, owner, false, g_wind_api); }
const AbilityEvent WindRiderEvents[] = {{111, 0, WindRiderEntryCallback}, {175, 0, WindRiderEntryCallback}, {58, 0, WindRiderImmunityCallback}};
const AbilityEvent WindPowerEvents[] = {{98, 0, WindPowerCallback}};
const AbilityEvent ElectromorphosisEvents[] = {{98, 0, ElectromorphosisCallback}};
const AbilityEvent* WindRiderFactory(uint32_t* count) { *count = 3; return WindRiderEvents; }
const AbilityEvent* WindPowerFactory(uint32_t* count) { *count = 1; return WindPowerEvents; }
const AbilityEvent* ElectromorphosisFactory(uint32_t* count) { *count = 1; return ElectromorphosisEvents; }
const AbilityEvent* PersistentChargeFactory(uint32_t* count) { *count = 6; return g_charge_events; }
void PersistentChargeBeforeMoveCallback(void* context, uint32_t owner) { PersistentChargeBeforeMove(context, owner, g_charge_api); }
void PersistentChargeResolvedTypeCallback(void* context, uint32_t owner) { PersistentChargeResolvedType(context, owner, g_charge_api); }
bool GrantChargeCallback(void* context, uint32_t owner) { return GrantPersistentCharge(context, owner, g_charge_api); }
bool TailwindActiveCallback(void* context, uint32_t owner) {
    auto* condition = g_side_condition(reinterpret_cast<void**>(context)[1], g_wind_api.side(context, owner), 4);
    return condition && g_condition_active(condition);
}
bool TailwindSubmissionCallback(void* context, const NativeSideConditionRequest* request) {
    const bool accepted = g_support_api.submit_side_condition(context, request);
    if (accepted) WindTailwindStarted(context, request->side, g_wind_api);
    return accepted;
}

void TeraShellCallback(void* context, uint32_t owner) { (void)TeraShell(context, owner, g_effectiveness_api); }
const AbilityEvent TeraShellEvents[] = {{FinalEffectivenessEvent, 0, TeraShellCallback}};
const AbilityEvent* TeraShellFactory(uint32_t* count) { *count = 1; return TeraShellEvents; }
uint32_t FinalEffectivenessCallback(void* battle, void* attacker, void* defender, uint32_t type, uint32_t preview) {
    const auto original = km_original_effectiveness(battle, attacker, defender, type, preview);
    if (!battle || !attacker || !defender || type >= 18 || !original) return original;
    auto* manager = reinterpret_cast<void**>(battle)[2];
    g_push_event(manager);
    g_event_readonly(manager, 3, g_battler_id(attacker));
    g_event_readonly(manager, 4, g_battler_id(defender));
    g_event_readonly(manager, 22, type);
    g_event_writable(manager, 61, original);
    g_dispatch_event(manager, FinalEffectivenessEvent);
    const auto result = g_read_event(manager, 61);
    g_pop_event(manager);
    return result;
}

void CommanderEntryCallback(void* context, uint32_t owner) { (void)CommanderEntry(context, owner, g_commander_api); }
const AbilityEvent CommanderEvents[] = {{111, 0, CommanderEntryCallback}, {175, 0, CommanderEntryCallback}, {2, 0, CommanderEntryCallback}};
const AbilityEvent* CommanderFactory(uint32_t* count) { *count = 0x90003; return CommanderEvents; }
void CommanderDispatch(void* manager, uint32_t event, uint32_t kind, bool check_suppression) {
    km_original_dispatch(manager, event, kind, check_suppression);
    if (event != 12 && event != 32 && event != 176 && event != 192 && event != 107 && event != 217) return;
    void* context[9]{};
    for (uint32_t i = 0; i < 6; ++i) context[i] = reinterpret_cast<void**>(manager)[i];
    context[6] = manager;
    context[7] = reinterpret_cast<void**>(manager)[14];
    CommanderEvent(context, event, g_commander_api);
}
bool CommanderInvulnerability(void* battle, void* attacker, void* defender, uint32_t move, bool* show_message) {
    if (battle && defender && CommanderHidden(reinterpret_cast<void**>(battle)[2], g_battler_id(defender))) {
        if (show_message) *show_message = true;
        return true;
    }
    return km_original_invulnerability(battle, attacker, defender, move, show_message);
}
void CommanderSwitch(void* operation, uint8_t* result, const void* request) {
    auto* battler = request ? *reinterpret_cast<void* const*>(request) : nullptr;
    auto* battle = operation ? g_operation_battle(operation) : nullptr;
    if (battle && battler && g_support_api.battler_value(battler, 13)
        && CommanderLocked(reinterpret_cast<void**>(battle)[2], g_battler_id(battler))) {
        if (result) *result = 0;
        return;
    }
    km_original_switch(operation, result, request);
}

bool CommanderAutomaticCommand(void* controller, void* battler, uint64_t* command) {
    if (CommanderHiddenBattler(battler)) {
        // Native empty command and true mean skip selection, including the
        // null-output probe used when advancing between doubles partners.
        if (command) *command = 0;
        return true;
    }
    return km_original_automatic_command(controller, battler, command);
}

uintptr_t ManagerConstructCallback(void* manager, void* init) {
    const auto result = g_manager_construct(manager, init);
    BeginAbilityBattle(manager);
    return result;
}
uintptr_t ManagerDestroyCallback(void* manager) {
    EndAbilityBattle(manager);
    return g_manager_destroy(manager);
}
void FaintDispatchCallback(void* manager, uint32_t event) {
    if (event == 217) RecordAbilityFaint(manager, g_read_event(manager, 2));
    g_dispatch_event(manager, event);
}
void SupremeOverlordEntryCallback(void* context, uint32_t owner) { (void)SupremeOverlordEntry(context, owner, g_battle_state_api); }
void SupremeOverlordPowerCallback(void* context, uint32_t owner) { (void)SupremeOverlordPower(context, owner, g_battle_state_api); }
void SupersweetSyrupCallback(void* context, uint32_t owner) { (void)SupersweetSyrup(context, owner, g_battle_state_api); }
const AbilityEvent SupremeOverlordEvents[] = {{111, 0, SupremeOverlordEntryCallback}, {175, 0, SupremeOverlordEntryCallback}, {72, 0, SupremeOverlordPowerCallback}};
const AbilityEvent SupersweetSyrupEvents[] = {{111, 0, SupersweetSyrupCallback}, {175, 0, SupersweetSyrupCallback}};
const AbilityEvent* SupremeOverlordFactory(uint32_t* count) { *count = 3; return SupremeOverlordEvents; }
const AbilityEvent* SupersweetSyrupFactory(uint32_t* count) { *count = 2; return SupersweetSyrupEvents; }

void ZeroToHeroWithdrawCallback(void* context, uint32_t owner) { (void)ZeroToHeroWithdraw(context, owner, g_form_api); }
void ZeroToHeroEntryCallback(void* context, uint32_t owner) { (void)ZeroToHeroEntry(context, owner, g_form_api); }
void TeraShiftCallback(void* context, uint32_t owner) { (void)TeraShift(context, owner, g_form_api); }
const AbilityEvent ZeroToHeroEvents[] = {{107, 0, ZeroToHeroWithdrawCallback}, {111, 0, ZeroToHeroEntryCallback}, {175, 0, ZeroToHeroEntryCallback}};
const AbilityEvent TeraShiftEvents[] = {{111, 0, TeraShiftCallback}, {175, 0, TeraShiftCallback}};
const AbilityEvent* ZeroToHeroFactory(uint32_t* count) { *count = 3; return ZeroToHeroEvents; }
const AbilityEvent* TeraShiftFactory(uint32_t* count) { *count = 0x50002; return TeraShiftEvents; }
void PoisonPuppeteerCallback(void* context, uint32_t owner) { (void)PoisonPuppeteer(context, owner, g_effect_source_api); }
void OpportunistRememberCallback(void* context, uint32_t owner) { (void)OpportunistRemember(context, owner, g_effect_source_api); }
void OpportunistApplyCallback(void* context, uint32_t owner) { (void)OpportunistApply(context, owner, g_effect_source_api); }
const AbilityEvent PoisonPuppeteerEvents[] = {{132, 0, PoisonPuppeteerCallback}};
const AbilityEvent OpportunistEvents[] = {{120, 0, OpportunistRememberCallback}, {111, 0, OpportunistApplyCallback},
    {175, 0, OpportunistApplyCallback}, {4, 0, OpportunistApplyCallback}, {149, 0, OpportunistApplyCallback}};
const AbilityEvent* PoisonPuppeteerFactory(uint32_t* count) { *count = 1; return PoisonPuppeteerEvents; }
const AbilityEvent* OpportunistFactory(uint32_t* count) { *count = 0x90005; return OpportunistEvents; }
bool StatusSubmissionCallback(void* context, const NativeStatusRequest* request) {
    const auto* work = static_cast<const uint8_t*>(reinterpret_cast<void**>(context)[8]);
    const auto kind = work ? work[0x30] : 0xff;
    const auto identity = work ? *reinterpret_cast<const uint16_t*>(work + 0x38) : 0xffff;
    AbilityEffectSource effect(reinterpret_cast<void**>(context)[6], kind, identity);
    return km_original_status_submission(context, request);
}
bool RankSubmissionCallback(void* context, const NativeRankRequest* request) {
    const auto* work = static_cast<const uint8_t*>(reinterpret_cast<void**>(context)[8]);
    const auto kind = work ? work[0x30] : 0xff;
    const auto identity = work ? *reinterpret_cast<const uint16_t*>(work + 0x38) : 0xffff;
    AbilityEffectSource effect(reinterpret_cast<void**>(context)[6], kind, identity);
    return km_original_rank_submission(context, request);
}
void MoveStatusOperationCallback(void* operation, void* result, const NativeStatusRequest* request) {
    auto* battle = g_operation_battle(operation);
    AbilityEffectSource effect(battle ? reinterpret_cast<void**>(battle)[2] : nullptr, 0, 0);
    g_status_operation(operation, result, request);
}
bool CopyPropertyCallback(uint32_t ability) {
    const auto id = static_cast<uint16_t>(ability);
    return id == 279 || id == 281 || id == 282 || id == 278 || id == 307 || id == 308 || id == 310 || km_original_copy_property(ability);
}
bool SuppressPropertyCallback(uint32_t ability) {
    const auto id = static_cast<uint16_t>(ability);
    return id == 278 || id == 307 || km_original_suppress_property(ability);
}
bool SwapPropertyCallback(uint32_t ability) {
    const auto id = static_cast<uint16_t>(ability);
    return id == 279 || id == 281 || id == 282 || id == 278 || id == 307 || id == 308 || id == 310 || km_original_swap_property(ability);
}
bool TransformPropertyCallback(uint32_t ability) {
    const auto id = static_cast<uint16_t>(ability);
    return id == 281 || id == 282 || id == 278 || id == 307 || km_original_transform_property(ability);
}
bool OverwritePropertyCallback(uint32_t ability) {
    const auto id = static_cast<uint16_t>(ability);
    return id == 308 || id == 310 || SuppressPropertyCallback(ability);
}
uintptr_t FinalPartyAppendCallback(void* party, void* battler) {
    if (battler && !g_form_api.transformed(battler) && static_cast<uint8_t*>(battler)[0x33d] == 1) {
        const auto species = g_form_api.species(battler);
        if (species == 964 || species == 1024) {
            g_reset_form(battler, 0, 0);
            g_set_raw_ability(battler, species == 964 ? 278 : 307);
        }
    }
    return g_append_party(party, battler);
}

void* EffectiveAbilityNode(void* manager, uint32_t owner, uint32_t ability, uint32_t event) {
    if (!manager || owner >= 31) return nullptr;
    auto* registry = g_registry(manager);
    if (!registry) return nullptr;
    auto* node = g_find_node(registry, 4, owner);
    return node && g_node_ability(node) == ability && g_node_owner(node) == owner
        && !g_filtered(manager, node, event) ? node : nullptr;
}
bool ProtectedByGold(void* context, uint32_t owner) {
    auto* manager = *reinterpret_cast<void**>(static_cast<uint8_t*>(context) + 0x30);
    // Switched-out/removed listeners do not protect benched party members.
    // Retail filters own Gastro Acid, Neutralizing Gas and Mold Breaker.
    return EffectiveAbilityNode(manager, owner, 283, 58) != nullptr;
}
uint32_t CureEventValue(void* context, uint32_t variable) {
    if (variable == 18) {
        // Party curing runs inside a move listener, outside the ordinary
        // immunity frame. Obtain its actual bound move identity from that node.
        auto* node = *reinterpret_cast<void**>(static_cast<uint8_t*>(context) + 0x40);
        return node && g_node_kind(node) == 2 ? g_node_ability(node) : 0;
    }
    return g_api.event_value(context, variable);
}
void AnnounceGold(void* context, uint32_t owner) {
    (void)g_reactive_api.begin_ability(context, owner);
    NativeMessageRequest message{}; message.source = static_cast<uint8_t>(owner);
    g_reactive_api.initialize_message(message.message);
    g_reactive_api.set_message(message.message, 2, 288);
    g_reactive_api.add_message_battler(message.message, owner);
    g_reactive_api.submit_message(context, &message);
    (void)g_reactive_api.end_ability(context, owner);
}
bool PartyCureCallback(void* context, const NativePartyCureRequest* request) {
    return GoodAsGoldPartyCure(context, request, g_cure_api);
}

void MyceliumOrderCallback(void* context, uint32_t owner) {
    const auto move = g_api.event_value(context, 18);
    if (move && move <= 65535 && g_move_category(move) == 0)
        g_stall_order(context, owner);
}
void MyceliumBypassCallback(void* context, uint32_t owner) {
    const auto move = g_api.event_value(context, 18);
    if (move && move <= 65535 && g_move_category(move) == 0)
        g_bypass_begin(context, owner);
}
void MyceliumEndCallback(void* context, uint32_t owner) { g_bypass_end(context, owner); }
void MyceliumResetCallback(void* context, uint32_t owner) { g_bypass_reset(context, owner); }
const AbilityEvent MyceliumMightEvents[] = {{15, 0, MyceliumOrderCallback}, {3, 0, MyceliumBypassCallback},
    {4, 0, MyceliumEndCallback}, {134, 0, MyceliumResetCallback}};
const AbilityEvent* MyceliumMightFactory(uint32_t* count) { *count = 0x90004; return MyceliumMightEvents; }

void ProtosynthesisRefresh(void* c, uint32_t o) { ParadoxRefresh(c, o, false, g_paradox_api); }
void QuarkDriveRefresh(void* c, uint32_t o) { ParadoxRefresh(c, o, true, g_paradox_api); }
void ProtosynthesisOwnerRefresh(void* c, uint32_t o) {
    if (g_paradox_api.event_value(c, 2) == o) ProtosynthesisRefresh(c, o);
}
void QuarkDriveOwnerRefresh(void* c, uint32_t o) {
    if (g_paradox_api.event_value(c, 2) == o) QuarkDriveRefresh(c, o);
}
void ParadoxResetCallback(void* c, uint32_t o) { ParadoxReset(c, o, g_paradox_api); }
void ParadoxAttackCallback(void* c, uint32_t o) { ParadoxStat(c, o, 76, g_paradox_api); }
void ParadoxDefenseCallback(void* c, uint32_t o) { ParadoxStat(c, o, 77, g_paradox_api); }
void ParadoxSpeedCallback(void* c, uint32_t o) { ParadoxStat(c, o, 19, g_paradox_api); }
const AbilityEvent ProtosynthesisEvents[] = {{111, 0, ProtosynthesisOwnerRefresh}, {175, 0, ProtosynthesisOwnerRefresh},
    {2, 0, ProtosynthesisOwnerRefresh}, {196, 0, ProtosynthesisOwnerRefresh},
    {150, 0, ProtosynthesisRefresh}, {151, 0, ProtosynthesisRefresh},
    {134, 0, ParadoxResetCallback}, {76, 0, ParadoxAttackCallback},
    {77, 0, ParadoxDefenseCallback}, {19, 0, ParadoxSpeedCallback}};
const AbilityEvent QuarkDriveEvents[] = {{111, 0, QuarkDriveOwnerRefresh}, {175, 0, QuarkDriveOwnerRefresh},
    {2, 0, QuarkDriveOwnerRefresh}, {196, 0, QuarkDriveOwnerRefresh},
    {150, 0, QuarkDriveRefresh}, {157, 0, QuarkDriveRefresh},
    {134, 0, ParadoxResetCallback}, {76, 0, ParadoxAttackCallback},
    {77, 0, ParadoxDefenseCallback}, {19, 0, ParadoxSpeedCallback}};
const AbilityEvent* ProtosynthesisFactory(uint32_t* count) { *count = 0x9000a; return ProtosynthesisEvents; }
const AbilityEvent* QuarkDriveFactory(uint32_t* count) { *count = 0x9000a; return QuarkDriveEvents; }

void CudChewRememberCallback(void* c, uint32_t o) { CudChewRemember(c, o, g_berry_api); }
void CudChewRepeatCallback(void* c, uint32_t o) { CudChewRepeat(c, o, g_berry_api); }
void CudChewResetCallback(void* c, uint32_t o) { CudChewReset(c, o, g_berry_api); }
const AbilityEvent CudChewEvents[] = {{182, 0, CudChewRememberCallback}, {149, 0, CudChewRepeatCallback}, {134, 0, CudChewResetCallback}};
const AbilityEvent* CudChewFactory(uint32_t* count) { *count = 0x90003; return CudChewEvents; }
bool StolenBerryCallback(void* context, const NativeForceEatRequest* request) {
    // Bug Bite and Pluck share this retail submission site.
    AbilityEffectSource effect(reinterpret_cast<void**>(context)[6], 0, 450);
    return g_berry_api.force_eat(context, request);
}
void CompleteResidualCallback(void* operation, void* result, const uint16_t* request) {
    g_residual_event(operation, result, request);
    auto* battle = g_operation_battle(operation);
    if (battle) CompleteAbilityTurn(reinterpret_cast<void**>(battle)[2]);
}

// Mega Sol changes the weather seen by the holder's moves, never the field.
// These entry points are limited to move callbacks and the damage calculator;
// weather abilities, residuals, opposing moves and field messages stay native.
uint32_t MegaSolMoveWeather(void* context, uint32_t queried_owner) {
    auto* manager = reinterpret_cast<void**>(context)[6];
    uint32_t actor = 31;
    if (!g_bound_event(manager, 3, &actor)) g_bound_event(manager, 2, &actor);
    auto* attacker = actor < 31 ? g_support_api.battler(context, actor) : nullptr;
    return attacker && g_support_api.battler_value(attacker, 19) == 315
        ? 1 : g_owner_weather(context, queried_owner);
}
uint32_t MegaSolDamageWeather(void* battle, void* attacker, void* defender, const void* move,
    uint32_t effectiveness, uint32_t critical, uint32_t flag6, uint32_t flag7,
    uint32_t flag8, uint32_t weather, void* output) {
    if (attacker && g_support_api.battler_value(attacker, 19) == 315) weather = 1;
    return km_original_damage_weather(battle, attacker, defender, move, effectiveness,
        critical, flag6, flag7, flag8, weather, output);
}
const AbilityEvent* MegaSolFactory(uint32_t* count) {
    // This passive ability is implemented at the guarded native move-weather
    // calls, including AI damage calculation. It has no battle-event callbacks.
    *count = 0; return nullptr;
}

bool ProtectionSideActive(void* context, uint32_t side, uint32_t condition) {
    auto* state = g_side_condition(reinterpret_cast<void**>(context)[1], side, condition);
    return state && g_condition_active(state);
}
void PiercingDrillBeginCallback(void* c, uint32_t o) { PiercingDrillBegin(c, o, g_protection_api); }
void PiercingDrillEndCallback(void* c, uint32_t o) { PiercingDrillEnd(c, o, g_protection_api); }
void PiercingDrillDamageCallback(void* c, uint32_t o) { PiercingDrillDamage(c, o, false, g_protection_api); }
void PiercingDrillFixedCallback(void* c, uint32_t o) { PiercingDrillDamage(c, o, true, g_protection_api); }
const AbilityEvent* PiercingDrillFactory(uint32_t* count) { *count = 0x60005; return g_drill_events; }

bool Inside(uintptr_t at, size_t length, uintptr_t base, size_t size) {
    return at >= base && length <= size && at - base <= size - length;
}
template<class T> bool Read(uintptr_t at, T* value) {
    return ReadExecutableBytes(at, value, sizeof(T));
}
bool EqualWords(uintptr_t at, const uint32_t* expected, size_t count) {
    for (size_t i = 0; i < count; ++i) {
        uint32_t actual{};
        if (!Read(at + i * 4, &actual) || actual != expected[i]) return false;
    }
    return true;
}

bool AddressPair(uintptr_t instruction, uintptr_t target, uint32_t* words) {
    const auto pages = (static_cast<int64_t>(target & ~uintptr_t{0xfff})
        - static_cast<int64_t>(instruction & ~uintptr_t{0xfff})) / 4096;
    if (pages < -(int64_t{1} << 20) || pages >= (int64_t{1} << 20)) return false;
    const auto immediate = static_cast<uint32_t>(pages) & 0x1fffff;
    words[0] = 0x90000009 | ((immediate & 3) << 29)
        | ((immediate >> 2) << 5); // ADRP x9
    words[1] = 0x91000129 | ((static_cast<uint32_t>(target) & 0xfff) << 10);
    return true; // ADD x9,x9,#lo12
}

template<class T> T Native(uintptr_t base, uintptr_t offset) {
    return reinterpret_cast<T>(base + offset);
}
} // namespace

bool TryInstallSwSh132(const ModuleRange& main) noexcept {
    if (g_attempted) return g_installed_base == main.base && g_installed_base != 0;
    if (!main.base || !Inside(main.base + swsh132::AbilityTableOffset,
            OriginalCount * sizeof(AbilityRow), main.data_base, main.data_size)) return false;

    for (const auto& site : swsh132::AbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::MindsEyeAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::PartyCureAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::ReactiveAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::SupportAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::FieldAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::CopyAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::ElevationAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::WindAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::EffectivenessAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::BattleStateAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::CommanderAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::ProtectionAbiSites) {
        if (!Inside(main.base + site.offset, 16, main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::MegaSolAbiSites) {
        if (!Inside(main.base + site.offset, 16, main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::BerryAbiSites) {
        if (!Inside(main.base + site.offset, 16, main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::ParadoxAbiSites) {
        if (!Inside(main.base + site.offset, 16, main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::MyceliumAbiSites) {
        if (!Inside(main.base + site.offset, 16, main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::EffectSourceAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    for (const auto& site : swsh132::FormAbiSites) {
        if (!Inside(main.base + site.offset, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(main.base + site.offset, site.expected, 4)) return false;
    }
    if (!Inside(main.base + swsh132::BreakableTableOffset,
            sizeof(swsh132::OriginalBreakableAbilities), main.ro_base, main.ro_size)) return false;
    for (size_t i = 0; i < 70; ++i) {
        uint16_t actual{};
        if (!Read(main.base + swsh132::BreakableTableOffset + i * 2, &actual)
            || actual != swsh132::OriginalBreakableAbilities[i]) return false;
    }
    AbilityRow original[OriginalCount]{};
    for (size_t i = 0; i < OriginalCount; ++i) {
        const auto& identity = swsh132::OriginalAbilities[i];
        auto& row = original[i];
        if (!Read(main.base + swsh132::AbilityTableOffset + i * sizeof(row), &row)
            || row.ability != identity.ability || row.reserved != 0
            || reinterpret_cast<uintptr_t>(row.factory) != main.base + identity.factory_offset
            || !Inside(main.base + identity.factory_offset, 4, main.base, main.text_size)) return false;
    }

    uint32_t pointer_words[6][2]{};
    uint32_t count_words[3]{};
    ExecutablePatch patches[48]{};
    size_t n = 0;
    for (const auto& site : swsh132::PointerSites) {
        const auto at = main.base + site.offset;
        if (!Inside(at, sizeof(site.expected), main.base, main.text_size)
            || !EqualWords(at, site.expected, 2)
            || !AddressPair(at, reinterpret_cast<uintptr_t>(g_table) + site.table_offset,
                pointer_words[n])) return false;
        patches[n] = {at, site.expected, pointer_words[n], sizeof(site.expected)};
        ++n;
    }
    for (size_t i = 0; i < 3; ++i) {
        const auto& site = swsh132::CountSites[i];
        const auto at = main.base + site.offset;
        if (!Inside(at, 4, main.base, main.text_size)
            || !EqualWords(at, &site.expected, 1)) return false;
        count_words[i] = (site.expected & ~(0xfffU << 10))
            | (static_cast<uint32_t>(ExtendedCount) << 10);
        patches[n++] = {at, &site.expected, &count_words[i], 4};
    }
    uint32_t breakable_pointer[2]{};
    const auto& pointer_site = swsh132::BreakablePointer;
    const auto& count_site = swsh132::BreakableCount;
    const auto pointer_at = main.base + pointer_site.offset;
    const auto count_at = main.base + count_site.offset;
    if (!Inside(pointer_at, 8, main.base, main.text_size)
        || !Inside(count_at, 4, main.base, main.text_size)
        || !EqualWords(pointer_at, pointer_site.expected, 2)
        || !EqualWords(count_at, &count_site.expected, 1)
        || !AddressPair(pointer_at, reinterpret_cast<uintptr_t>(g_breakable), breakable_pointer)) return false;
    const uint32_t breakable_count = (count_site.expected & ~(0xfffU << 10)) | (82U << 10);
    patches[n++] = {pointer_at, pointer_site.expected, breakable_pointer, 8};
    patches[n++] = {count_at, &count_site.expected, &breakable_count, 4};

    const auto& intimidate_site = swsh132::IntimidateSubmission;
    const auto& cure_site = swsh132::PartyCureSite;
    const auto cure_at = main.base + cure_site.offset;
    const auto cure_delta = static_cast<int64_t>(reinterpret_cast<uintptr_t>(PartyCureCallback)) - static_cast<int64_t>(cure_at);
    if (!Inside(cure_at, 4, main.base, main.text_size) || !EqualWords(cure_at, &cure_site.expected, 1)
        || (cure_delta & 3) || cure_delta < -(int64_t{1} << 27) || cure_delta >= (int64_t{1} << 27)) return false;
    const uint32_t cure_call = 0x94000000 | (static_cast<uint32_t>(cure_delta / 4) & 0x3ffffff);
    patches[n++] = {cure_at, &cure_site.expected, &cure_call, 4};
    const auto intimidate_at = main.base + intimidate_site.offset;
    const auto delta = static_cast<int64_t>(reinterpret_cast<uintptr_t>(GuardDogIntimidateCallback))
        - static_cast<int64_t>(intimidate_at);
    if (!Inside(intimidate_at, 4, main.base, main.text_size)
        || !EqualWords(intimidate_at, &intimidate_site.expected, 1)
        || (delta & 3) || delta < -(int64_t{1} << 27) || delta >= (int64_t{1} << 27)) return false;
    const uint32_t intimidate_call = 0x94000000 | (static_cast<uint32_t>(delta / 4) & 0x3ffffff);
    patches[n++] = {intimidate_at, &intimidate_site.expected, &intimidate_call, 4};

    const auto& grounding_site = swsh132::GroundingAbility;
    const auto grounding_at = main.base + grounding_site.offset;
    const auto grounding_delta = static_cast<int64_t>(reinterpret_cast<uintptr_t>(GroundingAbilityCallback))
        - static_cast<int64_t>(grounding_at);
    if (!Inside(grounding_at, 4, main.base, main.text_size)
        || !EqualWords(grounding_at, &grounding_site.expected, 1)
        || (grounding_delta & 3) || grounding_delta < -(int64_t{1} << 27)
        || grounding_delta >= (int64_t{1} << 27)) return false;
    const uint32_t grounding_call = 0x94000000 | (static_cast<uint32_t>(grounding_delta / 4) & 0x3ffffff);
    patches[n++] = {grounding_at, &grounding_site.expected, &grounding_call, 4};

    const swsh132::WordSite wind_sites[] = {swsh132::ChargeFactory, swsh132::TailwindSubmission};
    const uintptr_t wind_targets[] = {reinterpret_cast<uintptr_t>(PersistentChargeFactory), reinterpret_cast<uintptr_t>(TailwindSubmissionCallback)};
    uint32_t wind_branches[2]{};
    for (size_t i = 0; i < 2; ++i) {
        const auto& site = wind_sites[i];
        const auto at = main.base + site.offset;
        const auto displacement = static_cast<int64_t>(wind_targets[i]) - static_cast<int64_t>(at);
        if (!Inside(at, 4, main.base, main.text_size) || !EqualWords(at, &site.expected, 1)
            || (displacement & 3) || displacement < -(int64_t{1} << 27)
            || displacement >= (int64_t{1} << 27)) return false;
        wind_branches[i] = (i == 0 ? 0x14000000 : 0x94000000)
            | (static_cast<uint32_t>(displacement / 4) & 0x3ffffff);
        patches[n++] = {at, &site.expected, &wind_branches[i], 4};
    }

    const auto& effectiveness_site = swsh132::FinalEffectiveness;
    const auto effectiveness_at = main.base + effectiveness_site.offset;
    const auto effectiveness_delta = static_cast<int64_t>(reinterpret_cast<uintptr_t>(FinalEffectivenessCallback))
        - static_cast<int64_t>(effectiveness_at);
    if ((effectiveness_delta & 3) || effectiveness_delta < -(int64_t{1} << 27)
        || effectiveness_delta >= (int64_t{1} << 27)) return false;
    const uint32_t effectiveness_branch = 0x14000000
        | (static_cast<uint32_t>(effectiveness_delta / 4) & 0x3ffffff);
    patches[n++] = {effectiveness_at, &effectiveness_site.expected, &effectiveness_branch, 4};
    km_effectiveness_resume = effectiveness_at + 4;
    g_push_event = Native<decltype(g_push_event)>(main.base, 0x7db2b0);
    g_pop_event = Native<decltype(g_pop_event)>(main.base, 0x7db2f0);
    g_event_readonly = Native<decltype(g_event_readonly)>(main.base, 0x7db3f0);
    g_event_writable = Native<decltype(g_event_writable)>(main.base, 0x7db460);
    g_dispatch_event = Native<decltype(g_dispatch_event)>(main.base, 0x7dad40);
    g_read_event = Native<decltype(g_read_event)>(main.base, 0x7db610);
    g_battler_id = Native<decltype(g_battler_id)>(main.base, 0x7e0a00);
    g_effectiveness_api = {
        Native<decltype(g_effectiveness_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_effectiveness_api.set_event_value)>(main.base, 0x83ebd0),
        Native<decltype(g_effectiveness_api.battler)>(main.base, 0x83ec60),
        Native<decltype(g_effectiveness_api.full_hp)>(main.base, 0x7e1890),
    };
    g_api = {
        Native<decltype(g_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_api.percent_chance)>(main.base, 0x7ec900),
        PoisonParameter,
        Native<decltype(g_api.initialize_message)>(main.base, 0x843e60),
        Native<decltype(g_api.set_message)>(main.base, 0x843f20),
        Native<decltype(g_api.add_message_battler)>(main.base, 0x843f60),
        Native<decltype(g_api.submit_status)>(main.base, 0x83f310),
    };
    g_damage_api = {
        Native<decltype(g_damage_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_damage_api.chain_modifier)>(main.base, 0x83ebf0),
    };
    g_reactive_api = {
        Native<decltype(g_reactive_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_reactive_api.set_event_value)>(main.base, 0x83ebd0),
        Native<decltype(g_reactive_api.set_work_value)>(main.base, 0x83ec40),
        Native<decltype(g_reactive_api.chain_modifier)>(main.base, 0x83ebf0),
        Native<decltype(g_reactive_api.move_category)>(main.base, 0x780b10),
        Native<decltype(g_reactive_api.move_has_only_field_effects)>(main.base, 0x8412d0),
        Native<decltype(g_reactive_api.absorb_heal)>(main.base, 0x88f140),
        Native<decltype(g_reactive_api.absorb_rank)>(main.base, 0x88f270),
        Native<decltype(g_reactive_api.initialize_message)>(main.base, 0x843e60),
        Native<decltype(g_reactive_api.set_message)>(main.base, 0x843f20),
        Native<decltype(g_reactive_api.add_message_battler)>(main.base, 0x843f60),
        Native<decltype(g_reactive_api.submit_message)>(main.base, 0x83f640),
        Native<decltype(g_reactive_api.begin_ability)>(main.base, 0x83f010),
        Native<decltype(g_reactive_api.end_ability)>(main.base, 0x83f090),
        Native<decltype(g_reactive_api.submit_rank)>(main.base, 0x83f400),
        Native<decltype(g_reactive_api.terrain)>(main.base, 0x83eda0),
        Native<decltype(g_reactive_api.submit_terrain)>(main.base, 0x83f970),
    };
    g_registry = Native<decltype(g_registry)>(main.base, 0x7daa40);
    g_find_node = Native<decltype(g_find_node)>(main.base, 0x7dd0a0);
    g_node_ability = Native<decltype(g_node_ability)>(main.base, 0x7dba00);
    g_node_owner = Native<decltype(g_node_owner)>(main.base, 0x7db8c0);
    g_node_kind = Native<decltype(g_node_kind)>(main.base, 0x7db9d0);
    g_filtered = Native<decltype(g_filtered)>(main.base, 0x7db080);
    g_cure_api = {CureEventValue, ProtectedByGold, AnnounceGold,
        Native<decltype(g_cure_api.submit_cure)>(main.base, 0x83f290)};
    g_support_api = {
        Native<decltype(g_support_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_support_api.battler)>(main.base, 0x83ec60),
        Native<decltype(g_support_api.battler_value)>(main.base, 0x7e12a0),
        Native<decltype(g_support_api.contact_effect_blocked)>(main.base, 0x8415b0),
        Native<decltype(g_support_api.cannot_replace_ability)>(main.base, 0x7dd580),
        Native<decltype(g_support_api.same_side)>(main.base, 0x83ed80),
        Native<decltype(g_support_api.opposing_side)>(main.base, 0x83ed70),
        Native<decltype(g_support_api.position)>(main.base, 0x83ee50),
        Native<decltype(g_support_api.select_battlers)>(main.base, 0x841920),
        Native<decltype(g_support_api.full_hp)>(main.base, 0x7e1890),
        Native<decltype(g_support_api.hp_fraction)>(main.base, 0x7ec2f0),
        Native<decltype(g_support_api.move_category)>(main.base, 0x780b10),
        Native<decltype(g_support_api.initialize_message)>(main.base, 0x843e60),
        Native<decltype(g_support_api.set_message)>(main.base, 0x843f20),
        Native<decltype(g_support_api.add_message_argument)>(main.base, 0x843f60),
        Native<decltype(g_support_api.submit_ability)>(main.base, 0x83fb80),
        Native<decltype(g_support_api.submit_side_condition)>(main.base, 0x83f7a0),
        Native<decltype(g_support_api.submit_heal)>(main.base, 0x83f110),
    };
    g_guard_api = {
        Native<decltype(g_guard_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_guard_api.set_event_value)>(main.base, 0x83ebd0),
        Native<decltype(g_guard_api.initialize_message)>(main.base, 0x843e60),
        Native<decltype(g_guard_api.submit_rank)>(main.base, 0x83f400),
        Native<decltype(g_guard_api.begin_ability)>(main.base, 0x83f010),
        Native<decltype(g_guard_api.end_ability)>(main.base, 0x83f090),
        Native<decltype(g_guard_api.battler)>(main.base, 0x83ec60),
        Native<decltype(g_guard_api.battler_value)>(main.base, 0x7e12a0),
    };
    g_ruin_api = {
        Native<decltype(g_ruin_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_ruin_api.battler)>(main.base, 0x83ec60),
        Native<decltype(g_ruin_api.battler_value)>(main.base, 0x7e12a0),
        Native<decltype(g_ruin_api.select_battlers)>(main.base, 0x841920),
        Native<decltype(g_ruin_api.chain_modifier)>(main.base, 0x83ebf0),
        Native<decltype(g_ruin_api.begin_ability)>(main.base, 0x83f010),
        Native<decltype(g_ruin_api.end_ability)>(main.base, 0x83f090),
    };
    g_field_api = {
        Native<decltype(g_field_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_field_api.work_value)>(main.base, 0x83ec30),
        Native<decltype(g_field_api.set_work_value)>(main.base, 0x83ec40),
        Native<decltype(g_field_api.battler)>(main.base, 0x83ec60),
        Native<decltype(g_field_api.battler_value)>(main.base, 0x7e12a0),
        Native<decltype(g_field_api.hp_fraction)>(main.base, 0x7ec2a0),
        Native<decltype(g_field_api.secondary_effect_blocked)>(main.base, 0x8415b0),
        Native<decltype(g_field_api.initialize_message)>(main.base, 0x843e60),
        Native<decltype(g_field_api.begin_ability)>(main.base, 0x83f010),
        Native<decltype(g_field_api.end_ability)>(main.base, 0x83f090),
        Native<decltype(g_field_api.submit_rank)>(main.base, 0x83f400),
        Native<decltype(g_field_api.weather)>(main.base, 0x83fa30),
        Native<decltype(g_field_api.terrain)>(main.base, 0x83eda0),
        Native<decltype(g_field_api.chain_modifier)>(main.base, 0x83ebf0),
        Native<decltype(g_field_api.sun_entry)>(main.base, 0x88c0b0),
        Native<decltype(g_field_api.electric_entry)>(main.base, 0x8999b0),
    };
    g_copy_api = {
        Native<decltype(g_copy_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_copy_api.battler)>(main.base, 0x83ec60),
        Native<decltype(g_copy_api.battler_value)>(main.base, 0x7e12a0),
        Native<decltype(g_copy_api.position)>(main.base, 0x83ee50),
        Native<decltype(g_copy_api.select_battlers)>(main.base, 0x841920),
        Native<decltype(g_copy_api.critical_stage)>(main.base, 0x7e3740),
        Native<decltype(g_copy_api.condition)>(main.base, 0x7e28f0),
        Native<decltype(g_copy_api.status)>(main.base, 0x7e1810),
        Native<decltype(g_copy_api.status_parameter)>(main.base, 0x7e1aa0),
        Native<decltype(g_copy_api.submit_copy_rank)>(main.base, 0x83f440),
        Native<decltype(g_copy_api.add_condition)>(main.base, 0x83f6f0),
        Native<decltype(g_copy_api.remove_condition)>(main.base, 0x83f730),
        Native<decltype(g_copy_api.submit_cure)>(main.base, 0x83f290),
        Native<decltype(g_copy_api.submit_status)>(main.base, 0x83f310),
        Native<decltype(g_copy_api.initialize_message)>(main.base, 0x843e60),
        Native<decltype(g_copy_api.set_message)>(main.base, 0x843f20),
        Native<decltype(g_copy_api.add_message_argument)>(main.base, 0x843f60),
        Native<decltype(g_copy_api.submit_message)>(main.base, 0x83f640),
        Native<decltype(g_copy_api.begin_ability)>(main.base, 0x83f010),
        Native<decltype(g_copy_api.end_ability)>(main.base, 0x83f090),
    };
    g_conversion_api = {
        Native<decltype(g_conversion_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_conversion_api.set_event_value)>(main.base, 0x83ebd0),
        Native<decltype(g_conversion_api.work_value)>(main.base, 0x83ec30),
        Native<decltype(g_conversion_api.set_work_value)>(main.base, 0x83ec40),
        Native<decltype(g_conversion_api.chain_modifier)>(main.base, 0x83ebf0),
    };
    g_side_condition = Native<decltype(g_side_condition)>(main.base, 0x831960);
    g_condition_active = Native<decltype(g_condition_active)>(main.base, 0x835550);
    g_wind_api = {
        g_reactive_api.event_value, g_reactive_api.set_event_value,
        g_support_api.battler, g_support_api.battler_value,
        Native<decltype(g_wind_api.side)>(main.base, 0x83ed30), TailwindActiveCallback,
        g_reactive_api.move_has_only_field_effects, g_reactive_api.absorb_rank, GrantChargeCallback,
        g_support_api.select_battlers, g_reactive_api.initialize_message,
        g_reactive_api.submit_rank, g_reactive_api.begin_ability, g_reactive_api.end_ability,
    };
    g_charge_api = {
        Native<decltype(g_charge_api.work_list)>(main.base, 0x7daa40),
        Native<decltype(g_charge_api.first_work)>(main.base, 0x7dd0a0),
        Native<decltype(g_charge_api.next_work)>(main.base, 0x7dd130),
        Native<decltype(g_charge_api.work_identity)>(main.base, 0x7dba00),
        Native<decltype(g_charge_api.register_move)>(main.base, 0x870880),
        Native<decltype(g_charge_api.set_work)>(main.base, 0x7db870),
        Native<decltype(g_charge_api.work_value)>(main.base, 0x7db860),
        g_reactive_api.event_value,
        Native<decltype(g_charge_api.set_event_work)>(main.base, 0x83ec40),
        Native<decltype(g_charge_api.event_work)>(main.base, 0x83ec30),
        g_support_api.battler, g_copy_api.add_condition,
        g_reactive_api.initialize_message, g_reactive_api.set_message,
        g_reactive_api.add_message_battler, g_reactive_api.submit_message,
        g_reactive_api.begin_ability, g_reactive_api.end_ability,
        Native<decltype(g_charge_api.original_before_move)>(main.base, 0x874690),
    };
    const swsh132::WordSite state_sites[] = {swsh132::ManagerConstruct, swsh132::ManagerDestroy, swsh132::ConfirmedFaint};
    const uintptr_t state_targets[] = {reinterpret_cast<uintptr_t>(ManagerConstructCallback),
        reinterpret_cast<uintptr_t>(ManagerDestroyCallback), reinterpret_cast<uintptr_t>(FaintDispatchCallback)};
    uint32_t state_branches[3]{};
    for (size_t i = 0; i < 3; ++i) {
        const auto& site = state_sites[i];
        const auto at = main.base + site.offset;
        const auto displacement = static_cast<int64_t>(state_targets[i]) - static_cast<int64_t>(at);
        if (!Inside(at, 4, main.base, main.text_size) || !EqualWords(at, &site.expected, 1)
            || (displacement & 3) || displacement < -(int64_t{1} << 27)
            || displacement >= (int64_t{1} << 27)) return false;
        state_branches[i] = 0x94000000 | (static_cast<uint32_t>(displacement / 4) & 0x3ffffff);
        patches[n++] = {at, &site.expected, &state_branches[i], 4};
    }
    g_manager_construct = Native<decltype(g_manager_construct)>(main.base, 0x7da800);
    g_manager_destroy = Native<decltype(g_manager_destroy)>(main.base, 0x7daa10);
    g_battle_state_api = {
        g_reactive_api.event_value, g_field_api.work_value, g_field_api.set_work_value,
        g_support_api.same_side, g_damage_api.chain_modifier,
        Native<decltype(g_battle_state_api.position)>(main.base, 0x8416b0),
        g_support_api.select_battlers, g_support_api.battler,
        Native<decltype(g_battle_state_api.has_substitute)>(main.base, 0x7e55a0),
        g_reactive_api.initialize_message, g_reactive_api.submit_rank,
        g_reactive_api.begin_ability, g_reactive_api.end_ability,
    };
    g_form_api = {
        g_reactive_api.event_value, g_support_api.battler,
        Native<decltype(g_form_api.species)>(main.base, 0x7e1290),
        Native<decltype(g_form_api.transformed)>(main.base, 0x7e1570),
        Native<decltype(g_form_api.change_form)>(main.base, 0x840150),
        Native<decltype(g_form_api.remove_ability)>(main.base, 0x83a6f0),
        Native<decltype(g_form_api.set_ability)>(main.base, 0x838590),
        Native<decltype(g_form_api.register_ability)>(main.base, 0x83a6a0),
        g_reactive_api.initialize_message, g_reactive_api.set_message,
        g_reactive_api.add_message_battler, g_reactive_api.begin_ability, g_reactive_api.end_ability,
    };
    g_append_party = Native<decltype(g_append_party)>(main.base, 0x8140f0);
    g_move_category = Native<decltype(g_move_category)>(main.base, 0x780b10);
    g_stall_order = Native<decltype(g_stall_order)>(main.base, 0x8894c0);
    g_bypass_begin = Native<decltype(g_bypass_begin)>(main.base, 0x890930);
    g_bypass_end = Native<decltype(g_bypass_end)>(main.base, 0x8909a0);
    g_bypass_reset = Native<decltype(g_bypass_reset)>(main.base, 0x890a00);
    g_paradox_api = {
        Native<decltype(g_paradox_api.event_value)>(main.base, 0x83eb90),
        Native<decltype(g_paradox_api.work_value)>(main.base, 0x83ec30),
        Native<decltype(g_paradox_api.set_work_value)>(main.base, 0x83ec40),
        Native<decltype(g_paradox_api.battler)>(main.base, 0x83ec60),
        Native<decltype(g_paradox_api.battler_value)>(main.base, 0x7e12a0),
        Native<decltype(g_paradox_api.field_condition)>(main.base, 0x832ae0),
        Native<decltype(g_paradox_api.weather)>(main.base, 0x83f9f0),
        Native<decltype(g_paradox_api.terrain)>(main.base, 0x83eda0),
        Native<decltype(g_paradox_api.held_item)>(main.base, 0x7e2d10),
        Native<decltype(g_paradox_api.item_usable)>(main.base, 0x841730),
        Native<decltype(g_paradox_api.consume_item)>(main.base, 0x83fd20),
        Native<decltype(g_paradox_api.initialize_message)>(main.base, 0x843e60),
        Native<decltype(g_paradox_api.begin_ability)>(main.base, 0x83f010),
        Native<decltype(g_paradox_api.end_ability)>(main.base, 0x83f090),
        Native<decltype(g_paradox_api.chain_modifier)>(main.base, 0x83ebf0),
    };
    g_bound_event = Native<decltype(g_bound_event)>(main.base, 0x7db640);
    g_owner_weather = Native<decltype(g_owner_weather)>(main.base, 0x83fa30);
    uint32_t sol_branches[12]{};
    for (size_t i = 0; i < 12; ++i) {
        const auto& site = swsh132::MegaSolHookSites[i];
        const auto at = main.base + site.offset;
        const auto target = i == 11 ? reinterpret_cast<uintptr_t>(MegaSolDamageWeather)
            : reinterpret_cast<uintptr_t>(MegaSolMoveWeather);
        const auto displacement = static_cast<int64_t>(target) - static_cast<int64_t>(at);
        if (!Inside(at, 4, main.base, main.text_size) || !EqualWords(at, &site.expected, 1)
            || (displacement & 3) || displacement < -(int64_t{1} << 27)
            || displacement >= (int64_t{1} << 27)) return false;
        sol_branches[i] = (i == 11 ? 0x14000000 : 0x94000000)
            | (static_cast<uint32_t>(displacement / 4) & 0x3ffffff);
        patches[n++] = {at, &site.expected, &sol_branches[i], 4};
        if (i == 11) km_damage_weather_resume = at + 4;
    }
    g_protection_api = {g_charge_api, g_reactive_api.event_value,
        g_field_api.set_work_value, g_field_api.work_value, g_support_api.battler,
        Native<decltype(g_protection_api.turn_flag)>(main.base, 0x7e2df0),
        Native<decltype(g_protection_api.move_flag)>(main.base, 0x780a00),
        Native<decltype(g_protection_api.damaging_move)>(main.base, 0x780c60),
        g_wind_api.side, ProtectionSideActive,
        Native<decltype(g_protection_api.bypass_begin)>(main.base, 0x89b410),
        Native<decltype(g_protection_api.bypass_end)>(main.base, 0x89b470),
        g_damage_api.chain_modifier, g_reactive_api.set_event_value};
    g_drill_events[0] = {60, 0, Native<decltype(AbilityEvent::callback)>(main.base, 0x89b3b0)};
    g_drill_events[1] = {51, 0, PiercingDrillBeginCallback};
    g_drill_events[2] = {53, 0, PiercingDrillEndCallback};
    g_drill_events[3] = {92, 0, PiercingDrillDamageCallback};
    g_drill_events[4] = {94, 0, PiercingDrillFixedCallback};
    g_berry_api = {g_battle_state_api, g_support_api.battler_value,
        Native<decltype(g_berry_api.is_berry)>(main.base, 0x788fc0),
        Native<decltype(g_berry_api.force_eat)>(main.base, 0x83fce0)};
    g_residual_event = Native<decltype(g_residual_event)>(main.base, 0x8a99f0);
    const uintptr_t berry_targets[] = {reinterpret_cast<uintptr_t>(StolenBerryCallback), reinterpret_cast<uintptr_t>(CompleteResidualCallback)};
    uint32_t berry_branches[2]{};
    for (size_t i = 0; i < 2; ++i) {
        const auto& site = swsh132::BerryHookSites[i];
        const auto at = main.base + site.offset;
        const auto displacement = static_cast<int64_t>(berry_targets[i]) - static_cast<int64_t>(at);
        if (!Inside(at, 4, main.base, main.text_size) || !EqualWords(at, &site.expected, 1)
            || (displacement & 3) || displacement < -(int64_t{1} << 27)
            || displacement >= (int64_t{1} << 27)) return false;
        berry_branches[i] = 0x94000000 | (static_cast<uint32_t>(displacement / 4) & 0x3ffffff);
        patches[n++] = {at, &site.expected, &berry_branches[i], 4};
    }
    g_commander_api = {g_battle_state_api, g_form_api.species, g_form_api.transformed,
        g_reactive_api.set_event_value, Native<decltype(g_commander_api.visibility)>(main.base, 0x83a080)};
    const uintptr_t commander_targets[] = {reinterpret_cast<uintptr_t>(CommanderDispatch),
        reinterpret_cast<uintptr_t>(CommanderInvulnerability), reinterpret_cast<uintptr_t>(CommanderSwitch),
        reinterpret_cast<uintptr_t>(CommanderAutomaticCommand)};
    uintptr_t* const commander_resumes[] = {&km_dispatch_resume, &km_invulnerability_resume, &km_switch_resume,
        &km_automatic_command_resume};
    uint32_t commander_branches[4]{};
    for (size_t i = 0; i < 4; ++i) {
        const auto& site = swsh132::CommanderHookSites[i];
        const auto at = main.base + site.offset;
        const auto displacement = static_cast<int64_t>(commander_targets[i]) - static_cast<int64_t>(at);
        if (!Inside(at, 4, main.base, main.text_size) || !EqualWords(at, &site.expected, 1)
            || (displacement & 3) || displacement < -(int64_t{1} << 27)
            || displacement >= (int64_t{1} << 27)) return false;
        commander_branches[i] = 0x14000000 | (static_cast<uint32_t>(displacement / 4) & 0x3ffffff);
        patches[n++] = {at, &site.expected, &commander_branches[i], 4};
        *commander_resumes[i] = at + 4;
    }
    g_effect_source_api = {g_battle_state_api, g_support_api.battler_value, g_form_api.species,
        Native<decltype(g_effect_source_api.status_parameter)>(main.base, 0x7ec080), g_api.submit_status};
    g_status_operation = Native<decltype(g_status_operation)>(main.base, 0x8440e0);
    g_operation_battle = Native<decltype(g_operation_battle)>(main.base, 0x8423e0);
    const uintptr_t source_targets[] = {reinterpret_cast<uintptr_t>(StatusSubmissionCallback),
        reinterpret_cast<uintptr_t>(RankSubmissionCallback), reinterpret_cast<uintptr_t>(MoveStatusOperationCallback)};
    uint32_t source_branches[3]{};
    for (size_t i = 0; i < 3; ++i) {
        const auto& site = swsh132::EffectSourceHookSites[i];
        const auto at = main.base + site.offset;
        const auto displacement = static_cast<int64_t>(source_targets[i]) - static_cast<int64_t>(at);
        if (!Inside(at, 4, main.base, main.text_size) || !EqualWords(at, &site.expected, 1)
            || (displacement & 3) || displacement < -(int64_t{1} << 27)
            || displacement >= (int64_t{1} << 27)) return false;
        source_branches[i] = (i < 2 ? 0x14000000 : 0x94000000)
            | (static_cast<uint32_t>(displacement / 4) & 0x3ffffff);
        patches[n++] = {at, &site.expected, &source_branches[i], 4};
    }
    km_status_submission_resume = main.base + 0x83f314;
    km_rank_submission_resume = main.base + 0x83f404;
    g_reset_form = Native<decltype(g_reset_form)>(main.base, 0x7e4ab0);
    g_set_raw_ability = Native<decltype(g_set_raw_ability)>(main.base, 0x7e4aa0);
    const uintptr_t form_targets[] = {reinterpret_cast<uintptr_t>(CopyPropertyCallback),
        reinterpret_cast<uintptr_t>(SuppressPropertyCallback), reinterpret_cast<uintptr_t>(SwapPropertyCallback),
        reinterpret_cast<uintptr_t>(TransformPropertyCallback), reinterpret_cast<uintptr_t>(OverwritePropertyCallback),
        reinterpret_cast<uintptr_t>(FinalPartyAppendCallback), reinterpret_cast<uintptr_t>(FinalPartyAppendCallback)};
    uintptr_t* const property_resumes[] = {&km_copy_property_resume, &km_suppress_property_resume,
        &km_swap_property_resume, &km_transform_property_resume};
    uint32_t form_branches[7]{};
    for (size_t i = 0; i < 7; ++i) {
        const auto& site = swsh132::FormHookSites[i];
        const auto at = main.base + site.offset;
        const auto displacement = static_cast<int64_t>(form_targets[i]) - static_cast<int64_t>(at);
        if (!Inside(at, 4, main.base, main.text_size) || !EqualWords(at, &site.expected, 1)
            || (displacement & 3) || displacement < -(int64_t{1} << 27)
            || displacement >= (int64_t{1} << 27)) return false;
        form_branches[i] = (i < 4 ? 0x14000000 : 0x94000000)
            | (static_cast<uint32_t>(displacement / 4) & 0x3ffffff);
        patches[n++] = {at, &site.expected, &form_branches[i], 4};
        if (i < 4) *property_resumes[i] = at + 4;
    }
    g_charge_events[0] = {38, 0, Native<decltype(AbilityEvent::callback)>(main.base, 0x874590)};
    g_charge_events[1] = {3, 0, PersistentChargeBeforeMoveCallback};
    g_charge_events[2] = {78, 0, Native<decltype(AbilityEvent::callback)>(main.base, 0x874700)};
    g_charge_events[3] = {72, 0, Native<decltype(AbilityEvent::callback)>(main.base, 0x874770)};
    g_charge_events[4] = {4, 0, Native<decltype(AbilityEvent::callback)>(main.base, 0x8747e0)};
    g_charge_events[5] = {43, 0, PersistentChargeResolvedTypeCallback};
    for (size_t i = 0; i < OriginalCount; ++i) g_table[i] = original[i];
    g_table[ExtendedCount - 1] = {0xffffffff, 0, EmptyFactory};
    g_table[OriginalCount] = {305, 0, ToxicChainFactory};
    g_table[OriginalCount + 1] = {300, 0, MindsEyeFactory};
    g_table[OriginalCount + 2] = {276, 0, RockyPayloadFactory};
    g_table[OriginalCount + 3] = {292, 0, SharpnessFactory};
    g_table[OriginalCount + 4] = {269, 0, SeedSowerFactory};
    g_table[OriginalCount + 5] = {270, 0, ThermalExchangeFactory};
    g_table[OriginalCount + 6] = {272, 0, PurifyingSaltFactory};
    g_table[OriginalCount + 7] = {273, 0, WellBakedBodyFactory};
    g_table[OriginalCount + 8] = {283, 0, GoodAsGoldFactory};
    g_table[OriginalCount + 9] = {296, 0, ArmorTailFactory};
    g_table[OriginalCount + 10] = {297, 0, EarthEaterFactory};
    g_table[OriginalCount + 11] = {268, 0, LingeringAromaFactory};
    g_table[OriginalCount + 12] = {295, 0, ToxicDebrisFactory};
    g_table[OriginalCount + 13] = {299, 0, HospitalityFactory};
    g_table[OriginalCount + 14] = {271, 0, AngerShellFactory};
    g_table[OriginalCount + 15] = {288, 0, OrichalcumPulseFactory};
    g_table[OriginalCount + 16] = {289, 0, HadronEngineFactory};
    g_table[OriginalCount + 17] = {275, 0, GuardDogFactory};
    g_table[OriginalCount + 18] = {284, 0, VesselOfRuinFactory};
    g_table[OriginalCount + 19] = {285, 0, SwordOfRuinFactory};
    g_table[OriginalCount + 20] = {286, 0, TabletsOfRuinFactory};
    g_table[OriginalCount + 21] = {287, 0, BeadsOfRuinFactory};
    g_table[OriginalCount + 22] = {294, 0, CostarFactory};
    g_table[OriginalCount + 23] = {316, 0, FireManeFactory};
    g_table[OriginalCount + 24] = {312, 0, DragonizeFactory};
    g_table[OriginalCount + 25] = {318, 0, SpicySprayFactory};
    g_table[OriginalCount + 26] = {313, 0, EelevateFactory};
    g_table[OriginalCount + 27] = {319, 0, AuraGuardFactory};
    g_table[OriginalCount + 28] = {274, 0, WindRiderFactory};
    g_table[OriginalCount + 29] = {277, 0, WindPowerFactory};
    g_table[OriginalCount + 30] = {280, 0, ElectromorphosisFactory};
    g_table[OriginalCount + 31] = {308, 0, TeraShellFactory};
    g_table[OriginalCount + 32] = {293, 0, SupremeOverlordFactory};
    g_table[OriginalCount + 33] = {306, 0, SupersweetSyrupFactory};
    g_table[OriginalCount + 34] = {278, 0, ZeroToHeroFactory};
    g_table[OriginalCount + 35] = {307, 0, TeraShiftFactory};
    g_table[OriginalCount + 36] = {310, 0, PoisonPuppeteerFactory};
    g_table[OriginalCount + 37] = {290, 0, OpportunistFactory};
    g_table[OriginalCount + 38] = {298, 0, MyceliumMightFactory};
    g_table[OriginalCount + 39] = {281, 0, ProtosynthesisFactory};
    g_table[OriginalCount + 40] = {282, 0, QuarkDriveFactory};
    g_table[OriginalCount + 41] = {291, 0, CudChewFactory};
    g_table[OriginalCount + 42] = {315, 0, MegaSolFactory};
    g_table[OriginalCount + 43] = {311, 0, PiercingDrillFactory};
    g_table[OriginalCount + 44] = {279, 0, CommanderFactory};
    const uint32_t eelevate[][2] = {{18, 0x88fc20}, {29, 0x88fc90}, {148, 0x88fd50}, {165, 0x8943c0}};
    for (size_t i = 0; i < 4; ++i)
        g_eelevate_events[i] = {eelevate[i][0], 0,
            Native<decltype(AbilityEvent::callback)>(main.base, eelevate[i][1])};
    const uint32_t thermal[][2] = {{129, 0x88b6e0}, {131, 0x88b1c0},
        {175, 0x88b710}, {111, 0x88b710}, {2, 0x88b760}};
    for (size_t i = 0; i < 5; ++i)
        g_thermal_events[i] = {thermal[i][0], 0,
            Native<decltype(AbilityEvent::callback)>(main.base, thermal[i][1])};
    g_thermal_events[5] = {98, 0, ThermalExchangeHitCallback};
    g_salt_events[0] = {129, 0, PurifyingSaltStatusCallback};
    g_salt_events[1] = {131, 0, Native<decltype(AbilityEvent::callback)>(main.base, 0x88b1c0)};
    g_salt_events[2] = {14, 0, Native<decltype(AbilityEvent::callback)>(main.base, 0x88b480)};
    g_salt_events[3] = {76, 0, PurifyingSaltDamageCallback};
    g_armor_tail_events[0] = {34, 0, Native<decltype(AbilityEvent::callback)>(main.base, 0x898350)};
    for (size_t i = 0; i < 7; ++i) {
        g_minds_eye_events[i] = {swsh132::MindsEyeCallbacks[i][0], 0,
            Native<decltype(AbilityEvent::callback)>(main.base, swsh132::MindsEyeCallbacks[i][1])};
    }
    for (size_t i = 0; i < 70; ++i) g_breakable[i] = swsh132::OriginalBreakableAbilities[i];
    // The native property lookup reads pairs; keep an even number of ids.
    const uint16_t extra_breakable[] = {270, 272, 273, 275, 283, 296, 297, 300, 313, 319, 274, 308};
    for (size_t i = 0; i < 12; ++i) g_breakable[70 + i] = extra_breakable[i];
    g_attempted = true;
    // Keep the published table/API alive even if recovery is required. The
    // shared patcher owns rollback; no partially published pointer is cleared.
    if (PatchExecutableTransactionDetailed(patches, n) != ExecutablePatchResult::Committed)
        return false;
    g_installed_base = main.base;
    return true;
}

} // namespace km::restoration
