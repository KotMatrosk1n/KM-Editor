// SPDX-License-Identifier: GPL-3.0-only

#include "km_swsh_native_settings.hpp"

extern "C" {
__attribute__((visibility("hidden"))) uintptr_t km_swsh_registration_continue;
__attribute__((visibility("hidden"))) uintptr_t km_swsh_level_cap_continue;
__attribute__((visibility("hidden"))) volatile uint64_t km_swsh_effective_snapshot;
}

namespace {

using km::GuestFilesystemApi;
using km::ModuleRange;
using km::SettingsFamily;
using km::SettingsState;
using km::SettingsValues;
using km::SwShNativeSettingsEdition;
using km::SwShNativeSettingsProfileView;

constexpr uint64_t SwordTitleId = 0x0100ABF008968000ULL;
constexpr uint64_t ShieldTitleId = 0x01008DB008C2C000ULL;
constexpr uint64_t RequiredPresence =
    km::PresenceExperienceShare | km::PresenceExperienceRate
    | km::PresenceLevelCap;
constexpr uintptr_t ExpectedRoOffset = 0x01901000;
constexpr uintptr_t ExpectedDataOffset = 0x024DB000;
constexpr uintptr_t NativeArrayRegistrarOffset = 0x0066CBA0;
constexpr uintptr_t NativeLowLevelRegistrarOffset = 0x0066D970;
constexpr uintptr_t ExperienceAdditiveTransitionOffset = 0x007E4EA0;
constexpr uintptr_t LevelCapGrowthThresholdOffset = 0x007EC4A0;
constexpr uintptr_t ExperienceShareHookOffset = 0x007FB2C0;
constexpr uintptr_t LevelCapHookOffset = 0x0083A3E0;
constexpr uintptr_t ExperienceRateHookOffset = 0x008A564C;
constexpr uintptr_t ExperienceRateCalculatorOffset = 0x008A5A00;

// Exact base-zero caves reserved as a unique three-NOP scaffold by
// SwShStaticGameplaySettingsMainPatcher.BuildRuntimeManaged.
constexpr uintptr_t ShareBridgeOffset = 0x008A0294;
constexpr uintptr_t ShareTargetSlotOffset = 0x008A0FD4;
constexpr uintptr_t RateBridgeOffset = 0x008A1314;
constexpr uintptr_t RateTargetSlotOffset = 0x008A1534;
constexpr uintptr_t LevelCapBridgeOffset = 0x008A1544;
constexpr uintptr_t LevelCapTargetSlotOffset = 0x008A1B34;

constexpr uint32_t ShareRetailInstruction = 0x320003E0;
constexpr uint32_t RateRetailInstruction = 0xB94003E8;
constexpr uint32_t LevelCapRetailInstruction = 0xF81D0FF5;
constexpr uint32_t NopInstruction = 0xD503201F;
constexpr uint32_t BranchRegisterX17 = 0xD61F0220;
constexpr size_t JournalWorkerStackLength = 0x4000;
constexpr int64_t JournalRetryNanoseconds = 1'000'000'000LL;

constexpr uint8_t RegistrationHookPreimage[] = {
    0xF3, 0x0F, 0x1E, 0xF8, 0xFD, 0x7B, 0x01, 0xA9,
    0xFD, 0x43, 0x00, 0x91, 0xF3, 0x03, 0x00, 0xAA,
    0x88, 0x31, 0x00, 0x94,
};
constexpr uint8_t NativeArrayRegistrarPreimage[] = {
    0x00, 0x80, 0x01, 0x91, 0x02, 0x00, 0x80, 0x12,
    0x72, 0x03, 0x00, 0x14,
};
constexpr uint8_t NativeLowLevelRegistrarPreimage[] = {
    0xF8, 0x5F, 0xBC, 0xA9, 0xF6, 0x57, 0x01, 0xA9,
    0xF4, 0x4F, 0x02, 0xA9, 0xFD, 0x7B, 0x03, 0xA9,
};
constexpr uint8_t ExperienceAdditiveTransitionPreimage[] = {
    0xF8, 0x5F, 0xBC, 0xA9, 0xF6, 0x57, 0x01, 0xA9,
    0xF4, 0x4F, 0x02, 0xA9, 0xFD, 0x7B, 0x03, 0xA9,
};
constexpr uint8_t LevelCapGrowthThresholdPreimage[] = {
    0xF3, 0x0F, 0x1E, 0xF8, 0xFD, 0x7B, 0x01, 0xA9,
    0xFD, 0x43, 0x00, 0x91, 0xF3, 0x03, 0x02, 0x2A,
    0x98, 0xE1, 0xFD, 0x97, 0xFD, 0x7B, 0x41, 0xA9,
    0xE0, 0x03, 0x13, 0x2A, 0xF3, 0x07, 0x42, 0xF8,
    0xB8, 0xE1, 0xFD, 0x17,
};
constexpr uint8_t ExperienceRateCalculatorPreimage[] = {
    0xEA, 0x0F, 0x1C, 0xFC, 0xE9, 0xA3, 0x00, 0x6D,
    0xF5, 0x0F, 0x00, 0xF9, 0xF4, 0x4F, 0x02, 0xA9,
};
constexpr uint32_t BridgeScaffold[] = {
    NopInstruction, NopInstruction, NopInstruction,
};
constexpr uint32_t TargetSlotScaffold[] = {
    NopInstruction, NopInstruction,
};

constexpr SwShNativeSettingsProfileView SwordProfile{
    SwShNativeSettingsEdition::Sword,
    SwordTitleId,
    "1.3.2",
    "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000",
    0x01901000,
    0x01464FC0,
    NativeArrayRegistrarOffset,
    "sd:/config/km-editor/gameplay-settings/0100ABF008968000/settings.bin",
};
constexpr SwShNativeSettingsProfileView ShieldProfile{
    SwShNativeSettingsEdition::Shield,
    ShieldTitleId,
    "1.3.2",
    "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000",
    0x01901000,
    0x01464FF0,
    NativeArrayRegistrarOffset,
    "sd:/config/km-editor/gameplay-settings/01008DB008C2C000/settings.bin",
};

const SwShNativeSettingsProfileView* ProfileForEdition(
    SwShNativeSettingsEdition edition) {
    switch (edition) {
    case SwShNativeSettingsEdition::Sword:
        return &SwordProfile;
    case SwShNativeSettingsEdition::Shield:
        return &ShieldProfile;
    default:
        return nullptr;
    }
}

struct AmxNativeSymbol {
    const char* name;
    uint64_t (*function)(void*, uint64_t*);
};
static_assert(sizeof(AmxNativeSymbol) == 0x10);

struct FarBranch {
    uint32_t load_target;
    uint32_t branch_target;
    uintptr_t target;
};
static_assert(sizeof(FarBranch) == 0x10);

struct LiteralBridge {
    uint32_t load_target;
    uint32_t branch_target;
    uint32_t padding;
};
static_assert(sizeof(LiteralBridge) == 0x0C);

using NativeArrayRegistrar = void (*)(uint64_t, const AmxNativeSymbol*);
using RegistrationOriginal = void (*)(uint64_t);

enum ActivationState : uint32_t {
    ActivationIdle = 0,
    ActivationInstalling = 1,
    ActivationActive = 2,
    ActivationRejected = 3,
};

const SwShNativeSettingsProfileView* g_profile;
uintptr_t g_main_base;
GuestFilesystemApi g_filesystem{};
SettingsState g_persisted{};
volatile uint32_t g_activation_state;
volatile uint32_t g_settings_lock;
volatile uint32_t g_journal_ready;
volatile uint32_t g_journal_worker_state;
alignas(km::PageSize) uint8_t g_journal_worker_stack[JournalWorkerStackLength]{};

extern "C" uint64_t km_swsh_settings_read(void*, uint64_t* parameters);
extern "C" uint64_t km_swsh_settings_write(void*, uint64_t* parameters);
extern "C" void km_swsh_registration_callback(uint64_t owner);
extern "C" void km_swsh_registration_original(uint64_t owner);
extern "C" uint32_t km_swsh_share_callback();
extern "C" void km_swsh_rate_bridge();
extern "C" void km_swsh_level_cap_bridge();

constexpr AmxNativeSymbol NativeSymbols[] = {
    {"KmSettingsRead_", km_swsh_settings_read},
    {"KmSettingsWrite_", km_swsh_settings_write},
    {nullptr, nullptr},
};

bool IsInsideText(const ModuleRange& module, uintptr_t offset, size_t length) {
    return module.base != 0 && offset <= module.text_size
        && length <= module.text_size - offset;
}

bool Matches(const ModuleRange& module, uintptr_t offset,
             const void* expected, size_t length) {
    return expected != nullptr && IsInsideText(module, offset, length)
        && km::MemoryCompare(reinterpret_cast<const void*>(module.base + offset),
                             expected, length) == 0;
}

bool MatchesWord(const ModuleRange& module, uintptr_t offset, uint32_t expected) {
    return Matches(module, offset, &expected, sizeof(expected));
}

bool MatchesProfile(const ModuleRange& module,
                    const SwShNativeSettingsProfileView& profile) {
    if (module.text_size != profile.text_size
        || module.ro_base != module.base + ExpectedRoOffset
        || module.data_base != module.base + ExpectedDataOffset
        || !Matches(module, profile.registration_hook_offset,
                    RegistrationHookPreimage, sizeof(RegistrationHookPreimage))
        || !Matches(module, profile.native_array_registrar_offset,
                    NativeArrayRegistrarPreimage, sizeof(NativeArrayRegistrarPreimage))
        || !Matches(module, NativeLowLevelRegistrarOffset,
                    NativeLowLevelRegistrarPreimage, sizeof(NativeLowLevelRegistrarPreimage))
        || !Matches(module, ExperienceAdditiveTransitionOffset,
                    ExperienceAdditiveTransitionPreimage,
                    sizeof(ExperienceAdditiveTransitionPreimage))
        || !Matches(module, LevelCapGrowthThresholdOffset,
                    LevelCapGrowthThresholdPreimage,
                    sizeof(LevelCapGrowthThresholdPreimage))
        || !Matches(module, ExperienceRateCalculatorOffset,
                    ExperienceRateCalculatorPreimage,
                    sizeof(ExperienceRateCalculatorPreimage))
        || !MatchesWord(module, ExperienceShareHookOffset, ShareRetailInstruction)
        || !MatchesWord(module, ExperienceRateHookOffset, RateRetailInstruction)
        || !MatchesWord(module, LevelCapHookOffset, LevelCapRetailInstruction)) {
        return false;
    }

    constexpr uintptr_t scaffold_offsets[] = {
        ShareBridgeOffset,
        ShareTargetSlotOffset,
        RateBridgeOffset,
        RateTargetSlotOffset,
        LevelCapBridgeOffset,
        LevelCapTargetSlotOffset,
    };
    for (const auto offset : scaffold_offsets) {
        if (!Matches(module, offset, BridgeScaffold, sizeof(BridgeScaffold))) {
            return false;
        }
    }
    return true;
}

const ModuleRange* FindExactMain(const ModuleRange* modules, size_t module_count,
                                 const SwShNativeSettingsProfileView& profile) {
    const ModuleRange* match = nullptr;
    for (size_t index = 0; index < module_count; ++index) {
        if (!MatchesProfile(modules[index], profile)) {
            continue;
        }
        if (match != nullptr) {
            return nullptr;
        }
        match = &modules[index];
    }
    return match;
}

bool IsActive() {
    return __atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE)
        == ActivationActive;
}

bool IsRepresentable(const SettingsState& state) {
    const auto& values = state.values;
    return state.family == SettingsFamily::SwordShield
        && g_profile != nullptr
        && state.title_id == g_profile->title_id
        && (state.presence & RequiredPresence) == RequiredPresence
        && values.experience_rate_basis_points <= 50000
        && values.experience_rate_basis_points % 1000 == 0
        && ((!values.level_cap_enabled && values.level_cap == 100)
            || (values.level_cap_enabled && values.level_cap >= 1
                && values.level_cap <= 100));
}

void AcquireSettingsLock() {
    for (;;) {
        uint32_t expected = 0;
        if (__atomic_compare_exchange_n(
                &g_settings_lock, &expected, 1, false,
                __ATOMIC_ACQUIRE, __ATOMIC_RELAXED)) {
            return;
        }
        // A journal load or commit owns this lock until its authenticated state
        // and the corresponding packed snapshot have been published together.
        // Yield instead of dropping a one-shot menu selection.
        km::km_svc_sleep_thread(0);
    }
}

void ReleaseSettingsLock() {
    __atomic_store_n(&g_settings_lock, 0, __ATOMIC_RELEASE);
}

uint32_t EncodeRelativeBranch(uintptr_t source, uintptr_t target, bool link) {
    const auto delta = static_cast<int64_t>(target) - static_cast<int64_t>(source);
    if ((delta & 3) != 0 || delta < -(int64_t{1} << 27)
        || delta >= (int64_t{1} << 27)) {
        return 0;
    }
    return (link ? 0x94000000U : 0x14000000U)
        | (static_cast<uint32_t>(delta / 4) & 0x03FFFFFFU);
}

uint32_t EncodeLiteralLoadX17(uintptr_t source, uintptr_t literal) {
    const auto delta = static_cast<int64_t>(literal) - static_cast<int64_t>(source);
    if ((delta & 3) != 0 || delta < -(int64_t{1} << 20)
        || delta >= (int64_t{1} << 20)) {
        return 0;
    }
    return 0x58000011U
        | ((static_cast<uint32_t>(delta / 4) & 0x7FFFFU) << 5);
}

FarBranch MakeFarBranch(const void* target) {
    return FarBranch{
        0x58000051U, // LDR X17, #8
        BranchRegisterX17,
        reinterpret_cast<uintptr_t>(target),
    };
}

LiteralBridge MakeLiteralBridge(uintptr_t bridge_offset,
                                uintptr_t target_slot_offset) {
    return LiteralBridge{
        EncodeLiteralLoadX17(bridge_offset, target_slot_offset),
        BranchRegisterX17,
        NopInstruction,
    };
}

bool InstallImmutableHooks(const ModuleRange& main,
                           const SwShNativeSettingsProfileView& profile) {
    const auto registration = MakeFarBranch(
        reinterpret_cast<const void*>(km_swsh_registration_callback));
    const auto share_bridge = MakeLiteralBridge(
        ShareBridgeOffset, ShareTargetSlotOffset);
    const auto rate_bridge = MakeLiteralBridge(
        RateBridgeOffset, RateTargetSlotOffset);
    const auto cap_bridge = MakeLiteralBridge(
        LevelCapBridgeOffset, LevelCapTargetSlotOffset);
    const auto share_hook = EncodeRelativeBranch(
        ExperienceShareHookOffset, ShareBridgeOffset, false);
    const auto rate_hook = EncodeRelativeBranch(
        ExperienceRateHookOffset, RateBridgeOffset, true);
    const auto cap_hook = EncodeRelativeBranch(
        LevelCapHookOffset, LevelCapBridgeOffset, false);
    const uintptr_t share_target =
        reinterpret_cast<uintptr_t>(km_swsh_share_callback);
    const uintptr_t rate_target =
        reinterpret_cast<uintptr_t>(km_swsh_rate_bridge);
    const uintptr_t cap_target =
        reinterpret_cast<uintptr_t>(km_swsh_level_cap_bridge);
    if (share_bridge.load_target == 0 || rate_bridge.load_target == 0
        || cap_bridge.load_target == 0 || share_hook == 0
        || rate_hook == 0 || cap_hook == 0) {
        return false;
    }

    km_swsh_registration_continue =
        main.base + profile.registration_hook_offset + sizeof(FarBranch);
    km_swsh_level_cap_continue = main.base + LevelCapHookOffset + sizeof(uint32_t);

    // One transaction is the entire lifetime executable mutation surface for
    // Sword/Shield. Target words and bridge bodies precede all entry words.
    const km::ExecutablePatch patches[] = {
        {main.base + ShareTargetSlotOffset, TargetSlotScaffold,
         &share_target, sizeof(share_target)},
        {main.base + ShareBridgeOffset, BridgeScaffold,
         &share_bridge, sizeof(share_bridge)},
        {main.base + RateTargetSlotOffset, TargetSlotScaffold,
         &rate_target, sizeof(rate_target)},
        {main.base + RateBridgeOffset, BridgeScaffold,
         &rate_bridge, sizeof(rate_bridge)},
        {main.base + LevelCapTargetSlotOffset, TargetSlotScaffold,
         &cap_target, sizeof(cap_target)},
        {main.base + LevelCapBridgeOffset, BridgeScaffold,
         &cap_bridge, sizeof(cap_bridge)},
        {main.base + ExperienceShareHookOffset, &ShareRetailInstruction,
         &share_hook, sizeof(share_hook)},
        {main.base + ExperienceRateHookOffset, &RateRetailInstruction,
         &rate_hook, sizeof(rate_hook)},
        {main.base + LevelCapHookOffset, &LevelCapRetailInstruction,
         &cap_hook, sizeof(cap_hook)},
        {main.base + profile.registration_hook_offset,
         RegistrationHookPreimage, &registration, sizeof(registration)},
    };
    return km::PatchExecutableTransaction(
        patches, sizeof(patches) / sizeof(patches[0]));
}

bool TryLoadAndPublishJournal() {
    if (!IsActive() || g_profile == nullptr) {
        return false;
    }

    // Take the title-local lock before reading. Otherwise a retry worker can
    // authenticate an older slot, race a newer menu commit, and publish the
    // stale state after that commit has already become durable.
    AcquireSettingsLock();
    if (!IsActive() || g_profile == nullptr) {
        ReleaseSettingsLock();
        return false;
    }
    if (__atomic_load_n(&g_journal_ready, __ATOMIC_ACQUIRE) != 0) {
        ReleaseSettingsLock();
        return true;
    }
    SettingsState loaded{};
    const auto loaded_ok = km::LoadSettingsJournal(
            g_filesystem,
            g_profile->settings_journal_path,
            SettingsFamily::SwordShield,
            g_profile->title_id,
            RequiredPresence,
            &loaded)
        && IsRepresentable(loaded);
    if (loaded_ok) {
        g_persisted = loaded;
        __atomic_store_n(
            &km_swsh_effective_snapshot,
            km::PackSettingsSnapshot(loaded),
            __ATOMIC_RELEASE);
        __atomic_store_n(&g_journal_ready, 1, __ATOMIC_RELEASE);
    }
    ReleaseSettingsLock();
    return loaded_ok;
}

void JournalRetryWorker(void*) {
    for (;;) {
        if (TryLoadAndPublishJournal()) {
            __atomic_store_n(&g_journal_worker_state, 2, __ATOMIC_RELEASE);
            km::km_svc_exit_thread();
        }
        // This retry is data-only. The complete executable hook transaction
        // already committed synchronously during subsdk startup.
        km::km_svc_sleep_thread(JournalRetryNanoseconds);
    }
}

void StartJournalRetryWorker() {
    uint32_t expected = 0;
    if (!__atomic_compare_exchange_n(
            &g_journal_worker_state, &expected, 1, false,
            __ATOMIC_ACQ_REL, __ATOMIC_ACQUIRE)) {
        return;
    }

    km::Handle thread = km::InvalidHandle;
    if (km::km_svc_create_thread(
            &thread,
            JournalRetryWorker,
            nullptr,
            g_journal_worker_stack + JournalWorkerStackLength,
            0x2C,
            2) != km::ResultSuccess) {
        __atomic_store_n(&g_journal_worker_state, 0, __ATOMIC_RELEASE);
        return;
    }
    if (km::km_svc_start_thread(thread) != km::ResultSuccess) {
        km::km_svc_close_handle(thread);
        __atomic_store_n(&g_journal_worker_state, 0, __ATOMIC_RELEASE);
        return;
    }
    km::km_svc_close_handle(thread);
}

void EnsureJournalReadiness() {
    if (!IsActive()
        || __atomic_load_n(&g_journal_ready, __ATOMIC_ACQUIRE) != 0) {
        return;
    }

    // Thread creation can fail transiently during the loader-serialized entry.
    // A later VM-registration or native callback gets one synchronous data-only
    // adoption attempt, even while the worker is between attempts, so a menu
    // selection cannot be discarded merely because the worker is sleeping.
    // Rearm the background retry if the journal is still unavailable.
    // Executable hooks are never revisited here.
    if (!TryLoadAndPublishJournal()) {
        StartJournalRetryWorker();
    }
}

bool CommitAndPublishSnapshot(uint64_t packed_snapshot) {
    if (!IsActive() || g_profile == nullptr
        || __atomic_load_n(&g_journal_ready, __ATOMIC_ACQUIRE) == 0) {
        return false;
    }

    AcquireSettingsLock();
    if (!IsActive() || g_profile == nullptr
        || __atomic_load_n(&g_journal_ready, __ATOMIC_ACQUIRE) == 0) {
        ReleaseSettingsLock();
        return false;
    }

    SettingsValues requested{};
    uint64_t presence = 0;
    const auto valid = km::UnpackSettingsSnapshot(
        packed_snapshot, &requested, &presence);
    if (!valid || presence != g_persisted.presence
        || (presence & RequiredPresence) != RequiredPresence) {
        ReleaseSettingsLock();
        return false;
    }

    const auto& current = g_persisted.values;
    if (current.experience_share == requested.experience_share
        && current.experience_rate_basis_points
            == requested.experience_rate_basis_points
        && current.level_cap_enabled == requested.level_cap_enabled
        && current.level_cap == requested.level_cap) {
        ReleaseSettingsLock();
        return true;
    }

    SettingsState committed{};
    const auto committed_ok = km::CommitSettingsJournal(
        g_filesystem,
        g_profile->settings_journal_path,
        SettingsFamily::SwordShield,
        g_profile->title_id,
        RequiredPresence,
        requested,
        &committed);
    if (!committed_ok) {
        // A failed readback can follow a durable inactive-slot write. Resolve
        // the authenticated journal again so this process and the next launch
        // cannot disagree about which settings won.
        SettingsState reconciled{};
        if (km::LoadSettingsJournal(
                g_filesystem,
                g_profile->settings_journal_path,
                SettingsFamily::SwordShield,
                g_profile->title_id,
                RequiredPresence,
                &reconciled)
            && IsRepresentable(reconciled)) {
            g_persisted = reconciled;
            __atomic_store_n(&g_journal_ready, 1, __ATOMIC_RELEASE);
            __atomic_store_n(
                &km_swsh_effective_snapshot,
                km::PackSettingsSnapshot(reconciled),
                __ATOMIC_RELEASE);
        } else {
            // No authenticated durable winner can be established. Keep the
            // immutable hooks installed, publish the zero retail-fallback
            // snapshot, and reject all further menu reads and writes until a
            // cold launch can authenticate the journal from scratch.
            MemorySet(&g_persisted, 0, sizeof(g_persisted));
            __atomic_store_n(&g_journal_ready, 0, __ATOMIC_RELEASE);
            __atomic_store_n(
                &km_swsh_effective_snapshot, 0, __ATOMIC_RELEASE);
            __atomic_store_n(
                &g_activation_state, ActivationRejected, __ATOMIC_RELEASE);
        }
        ReleaseSettingsLock();
        return false;
    }

    g_persisted = committed;
    __atomic_store_n(
        &km_swsh_effective_snapshot,
        km::PackSettingsSnapshot(committed),
        __ATOMIC_RELEASE);
    ReleaseSettingsLock();
    return true;
}

} // namespace

extern "C" uint64_t km_swsh_settings_read(void*, uint64_t* parameters) {
    if (!IsActive() || parameters == nullptr || parameters[0] != 0) {
        return 0;
    }
    EnsureJournalReadiness();
    const auto snapshot = __atomic_load_n(
        &km_swsh_effective_snapshot, __ATOMIC_ACQUIRE);
    if (snapshot != 0) {
        return snapshot;
    }

    // Zero is reserved for exact-retail gameplay fallback in the immutable
    // hooks. The menu must not interpret it as Share Off and a 0% rate while
    // storage is temporarily unavailable, so expose a canonical packed retail
    // view without making that view writable or claiming journal readiness.
    const SettingsState retail_view{
        SettingsFamily::SwordShield,
        g_profile == nullptr ? 0 : g_profile->title_id,
        0,
        RequiredPresence,
        km::VanillaSettings,
        -1,
        false,
    };
    return km::PackSettingsSnapshot(retail_view);
}

extern "C" uint64_t km_swsh_settings_write(void*, uint64_t* parameters) {
    if (parameters == nullptr || parameters[0] != sizeof(uint64_t)) {
        return 0;
    }
    EnsureJournalReadiness();
    return CommitAndPublishSnapshot(parameters[1]) ? 1 : 0;
}

extern "C" void km_swsh_registration_callback(uint64_t owner) {
    reinterpret_cast<RegistrationOriginal>(km_swsh_registration_original)(owner);
    if (!IsActive() || g_profile == nullptr) {
        return;
    }
    EnsureJournalReadiness();
    const auto registrar = reinterpret_cast<NativeArrayRegistrar>(
        g_main_base + g_profile->native_array_registrar_offset);
    registrar(owner, NativeSymbols);
}

extern "C" uint32_t km_swsh_share_callback() {
    const auto snapshot = __atomic_load_n(
        &km_swsh_effective_snapshot, __ATOMIC_ACQUIRE);
    // The exact retail function always returns true. Zero is the only
    // unpublished value, so hook readiness and setting value are one load.
    return snapshot == 0 ? 1U : static_cast<uint32_t>(snapshot & 1U);
}

extern "C" uint32_t km_swsh_apply_level_cap(
    uintptr_t owner, uint32_t party_index, uint32_t award) {
    const auto snapshot = __atomic_load_n(
        &km_swsh_effective_snapshot, __ATOMIC_ACQUIRE);
    SettingsValues values{};
    if (snapshot == 0 || !km::UnpackSettingsSnapshot(snapshot, &values)
        || !values.level_cap_enabled || values.level_cap >= 100
        || owner == 0 || g_main_base == 0) {
        return award;
    }

    const auto party = *reinterpret_cast<const uintptr_t*>(owner + 0x20);
    if (party == 0) {
        return award;
    }
    const auto slots = *reinterpret_cast<const uintptr_t*>(party + 0x10);
    if (slots == 0) {
        return award;
    }
    const auto entries = *reinterpret_cast<const uintptr_t*>(slots + 0x08);
    if (entries == 0) {
        return award;
    }
    const auto entry = entries
        + static_cast<uintptr_t>(static_cast<uint8_t>(party_index)) * 8U;
    const auto creature = *reinterpret_cast<const uintptr_t*>(entry + 0x120);
    if (creature == 0) {
        return award;
    }

    const auto level = *reinterpret_cast<const uint16_t*>(creature + 0x70);
    const auto growth = *reinterpret_cast<const uint8_t*>(creature + 0x33D);
    using GrowthThreshold = uint32_t (*)(uint32_t, uint32_t, uint32_t);
    const auto threshold = reinterpret_cast<GrowthThreshold>(
        g_main_base + LevelCapGrowthThresholdOffset)(
            level, growth, static_cast<uint32_t>(values.level_cap) + 1U);
    if (threshold == 0) {
        return award;
    }
    const auto maximum = threshold - 1U;
    const auto current = *reinterpret_cast<const uint32_t*>(creature + 0x6C);
    const auto remaining = current >= maximum ? 0U : maximum - current;
    return award > remaining ? remaining : award;
}

namespace km {

const SwShNativeSettingsProfileView* GetSwShNativeSettingsProfile(
    SwShNativeSettingsEdition edition) {
    return ProfileForEdition(edition);
}

bool TryInstallSwShNativeSettings(
    SwShNativeSettingsEdition edition,
    const ModuleRange* modules,
    size_t module_count,
    const GuestFilesystemApi& filesystem) {
    const auto* profile = ProfileForEdition(edition);
    if (profile == nullptr) {
        return false;
    }

    auto state = __atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE);
    if (state == ActivationActive) {
        return g_profile != nullptr && g_profile->edition == edition;
    }
    if (state != ActivationIdle || modules == nullptr || module_count == 0
        || IsExecutablePatchingFaulted()) {
        return false;
    }
    uint32_t expected = ActivationIdle;
    if (!__atomic_compare_exchange_n(
            &g_activation_state, &expected, ActivationInstalling, false,
            __ATOMIC_ACQ_REL, __ATOMIC_ACQUIRE)) {
        return expected == ActivationActive && g_profile != nullptr
            && g_profile->edition == edition;
    }

    const auto* main = FindExactMain(modules, module_count, *profile);
    if (main == nullptr) {
        __atomic_store_n(
            &g_activation_state, ActivationRejected, __ATOMIC_RELEASE);
        return false;
    }

    g_profile = profile;
    g_main_base = main->base;
    g_filesystem = filesystem;
    __atomic_store_n(&km_swsh_effective_snapshot, 0, __ATOMIC_RELEASE);
    if (!InstallImmutableHooks(*main, *profile)) {
        g_profile = nullptr;
        g_main_base = 0;
        MemorySet(&g_filesystem, 0, sizeof(g_filesystem));
        __atomic_store_n(
            &g_activation_state, ActivationRejected, __ATOMIC_RELEASE);
        return false;
    }

    // Hook publication is complete and immutable before any journal I/O.
    // Until a verified journal is adopted, every gameplay hook sees zero and
    // executes the exact retail behavior.
    __atomic_store_n(&g_activation_state, ActivationActive, __ATOMIC_RELEASE);
    if (!TryLoadAndPublishJournal()) {
        StartJournalRetryWorker();
    }
    return true;
}

} // namespace km
