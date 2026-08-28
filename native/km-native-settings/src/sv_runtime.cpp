// SPDX-License-Identifier: GPL-3.0-only

#include "km_settings.hpp"
#include "km_sv_runtime.hpp"

namespace {

using LuaState = void;
using LuaCFunction = int (*)(LuaState*);
using LuaKFunction = int (*)(LuaState*, int, intptr_t);

using LuaPushInteger = void (*)(LuaState*, int64_t);
using LuaGetTop = int (*)(LuaState*);
using LuaSetTop = void (*)(LuaState*, int);
using LuaPushCClosure = void (*)(LuaState*, LuaCFunction, int);
using LuaPCallK = int (*)(LuaState*, int, int, int, intptr_t, LuaKFunction);
using LuaLoadBufferX = int (*)(LuaState*, const char*, size_t, const char*, const char*);
using LuaSetGlobal = void (*)(LuaState*, const char*);
using LuaCheckInteger = int64_t (*)(LuaState*, int);

struct LuaApi {
    LuaPushInteger push_integer;
    LuaGetTop get_top;
    LuaSetTop set_top;
    LuaPushCClosure push_c_closure;
    LuaPCallK pcall_k;
    LuaLoadBufferX load_buffer_x;
    LuaSetGlobal set_global;
    LuaCheckInteger check_integer;
};

constexpr uint64_t ScarletTitleId = 0x0100A3D008C5C000;
constexpr uint64_t VioletTitleId = 0x01008F6008C5E000;
constexpr uint32_t InfoProgramId = 18;
constexpr uint32_t MemoryTypeMask = 0xFF;
constexpr uint64_t RequiredPresence = km::PresenceExperienceShare
    | km::PresenceExperienceRate | km::PresenceLevelCap;

constexpr size_t MainTextMinimum = 0x03440000;
constexpr size_t MainDataOffset = 0x04383000;
constexpr size_t MainDataAndBssLength = 0x00451000;
constexpr size_t RegistrationOffset = 0x004C550;
constexpr size_t RegistrationResumeOffset = RegistrationOffset + 0x10;
constexpr size_t ShareDecisionOffset = 0x01141BA0;
constexpr size_t RateRouteOffset = 0x01781E30;
constexpr size_t CapRouteOffset = 0x0178143C;
constexpr size_t ScaffoldOffset = 0x0343FC90;
constexpr size_t RateScaffoldLength = 0x60;
constexpr size_t CapScaffoldOffset = ScaffoldOffset + RateScaffoldLength;
constexpr size_t CapScaffoldLength = 0x120;
constexpr size_t ShareScaffoldOffset = CapScaffoldOffset + CapScaffoldLength;
constexpr size_t ShareScaffoldLength = 0x30;
constexpr size_t ScaffoldLength = RateScaffoldLength + CapScaffoldLength
    + ShareScaffoldLength;
constexpr size_t RuntimeSnapshotOffset = 0x047D3000;
constexpr size_t InitializationWorkerStackLength = 16 * 1024;
constexpr int64_t InitializationRetryNanoseconds = 250'000'000;

constexpr uint32_t ShareRouteInstalled = 0x148BF89C;
constexpr uint32_t RateRouteInstalled = 0x1472F798;
constexpr uint32_t CapRouteInstalled = 0x9472FA2D;
constexpr uint32_t FarLoadX17 = 0x58000051;
constexpr uint32_t FarBranchX17 = 0xD61F0220;

constexpr uint32_t PushIntegerPreimage[] = {
    0xF9400808, 0x52800069, 0xF9000101, 0x39002109,
    0xF9400808, 0x91004108, 0xF9000808, 0xD65F03C0,
};
constexpr uint32_t GetTopPreimage[] = {
    0xF9401009, 0xF9400808, 0xF9400129, 0xCB090108,
    0xD1004108, 0xD344FD00, 0xD65F03C0, 0x00000000,
};
constexpr uint32_t SetTopPreimage[] = {
    0xA9BE7BFD, 0xA9014FF4, 0x910003FD, 0xAA0003F3,
    0x37F80421, 0xF9401268, 0xF9400108, 0xF9400A6A,
};
constexpr uint32_t PushCClosurePreimage[] = {
    0xA9BD7BFD, 0xF9000BF5, 0x910003FD, 0xA9024FF4,
    0xAA0103F5, 0xAA0003F3, 0x34000562, 0xAA1303E0,
};
constexpr uint32_t PCallKPreimage[] = {
    0xD10103FF, 0xA9017BFD, 0x910043FD, 0xF90013F5,
    0xA9034FF4, 0x2A0203F4, 0xAA0003F3, 0x340000E3,
};
constexpr uint32_t LoadBufferXPreimage[] = {
    0xD10083FF, 0xA9017BFD, 0x910043FD, 0xA9000BE1,
    0x90FF8F61, 0x912E4021, 0x910003E2, 0x97C7AC09,
};
constexpr uint32_t SetGlobalPreimage[] = {
    0xF9400C08, 0xAA0103E2, 0xF9402108, 0xF9400908,
    0x91004101, 0x17686E5F, 0xD10083FF, 0xA9017BFD,
};
constexpr uint32_t CheckIntegerPreimage[] = {
    0xA9BD7BFD, 0xF9000BF5, 0x910003FD, 0xA9024FF4,
    0x910073A2, 0x2A0103F4, 0xAA0003F5, 0x942DBAA5,
};
constexpr uint32_t RegistrationPreimage[] = {
    0xA9BE7BFD, 0xF9000BF3, 0x910003FD, 0xD2D00008,
    0xF2E80FE8, 0x9E670100, 0x52801101, 0xAA0003F3,
};
constexpr uint32_t SharePreimage[] = {
    0x148BF89C, 0x54000560, 0xAA1F03FA, 0x92401C1B,
    0x5280003C, 0x3940C288, 0x6B3A011F, 0x54000589,
};
constexpr uint32_t RateRoutePreimage[] = {
    0x35000429, 0xBD402001, 0x1E202020, 0x5400048C,
    0x1472F798, 0x9100C3FF, 0xD65F03C0, 0x0B080508,
};
constexpr uint32_t CapRoutePreimage[] = {
    0xA9411283, 0x940002C9, 0x910003E1, 0xAA1303E0,
    0x9472FA2D, 0xB94003E8, 0x35000208, 0xB94013E8,
};
constexpr uint32_t CapMergePreimage[] = {
    0xB9400028, 0xB9400009, 0x0B080128, 0x39401009,
    0xB9000008, 0x39401028, 0x0B080128, 0x39401409,
};
constexpr uint32_t CurrentExperiencePreimage[] = {
    0xB9405400, 0xD65F03C0, 0x7940B400, 0xD65F03C0,
    0x7940B000, 0xD65F03C0, 0x39418800, 0xD65F03C0,
};
constexpr uint32_t MinimumExperiencePreimage[] = {
    0xD100C3FF, 0xA9017BFD, 0x910043FD, 0xA9024FF4,
    0xF001DA88, 0xF9477108, 0x790013E0, 0x910003E0,
};

// The exact-build immutable normal-battle rate, bounded cap, and EXP Share
// scaffold installed into exefs/main before the title starts. Runtime settings
// are read exclusively from the aligned RW snapshot slot reserved in main BSS.
constexpr uint32_t ScaffoldTemplate[] = {
    0xB9400028, 0x340002A8, 0x90009CA9, 0xC8DFFD29,
    0xB5000069, 0x5284E209, 0x14000002, 0xD349A129,
    0x34000189, 0x9BA97D08, 0x5284E209, 0x9AC90908,
    0xB5000068, 0x52800028, 0x14000007, 0xD360FD0A,
    0xB40000AA, 0x12800008, 0x14000003, 0xD503201F,
    0x2A1F03E8, 0xB9000028, 0xA9427BFD, 0x178D0852,
    0xD101C3FF, 0xA9007BFD, 0xA90153F3, 0xA9025BF5,
    0xA90363F7, 0xA9046BF9, 0xA90573FB, 0xF9400A95,
    0xAA0003F3, 0xAA0103F4, 0x90009CB6, 0xC8DFFED6,
    0xB4000096, 0x36080076, 0xD34222D6, 0x14000002,
    0x52800C96, 0xAA1303E0, 0xAA1403E1, 0x978D05D4,
    0x710192DF, 0x540004E0, 0xB40004D5, 0x3940C2B7,
    0x71001AFF, 0x528000C8, 0x1A8892F7, 0xAA1F03F8,
    0x6B17031F, 0x540003E2, 0xF8787AB9, 0xB4000379,
    0x900085E8, 0x9132C108, 0xF9400329, 0xEB08013F,
    0x540002C1, 0x794E233A, 0x794E333B, 0xAA1903E0,
    0x97E3EFB0, 0x2A0003FC, 0x2A1A03E0, 0x2A1B03E1,
    0x110006C2, 0x975D188C, 0x5100041A, 0x6B1C035A,
    0x1A9F235A, 0x8B181279, 0x8B181289, 0xB9400328,
    0xB940012A, 0x4B0A0108, 0x8B2A4108, 0xEB1A011F,
    0x9A9A9108, 0xB9000328, 0x91000718, 0x17FFFFE1,
    0xA94573FB, 0xA9446BF9, 0xA94363F7, 0xA9425BF5,
    0xA94153F3, 0xA9407BFD, 0x9101C3FF, 0xD65F03C0,
    0xD503201F, 0xD503201F, 0xD503201F, 0xD503201F,
    0xA9BF47F0, 0x90009CB0, 0xC8DFFE10, 0xB4000050,
    0x36000070, 0x72001C1F, 0x14000002, 0x72001FFF,
    0xA8C147F0, 0x1774075C, 0xD503201F, 0xD503201F,
};
static_assert(sizeof(ScaffoldTemplate) == ScaffoldLength);
static_assert(CapScaffoldOffset == 0x0343FCF0);
static_assert(ShareScaffoldOffset == 0x0343FE10);
static_assert((RuntimeSnapshotOffset & (alignof(uint64_t) - 1)) == 0);
static_assert(RuntimeSnapshotOffset + sizeof(uint64_t)
              <= MainDataOffset + MainDataAndBssLength);

constexpr size_t LuaPushIntegerOffset = 0x00B5D960;
constexpr size_t LuaGetTopOffset = 0x00B5D980;
constexpr size_t LuaSetTopOffset = 0x00B5DB00;
constexpr size_t LuaPushCClosureOffset = 0x00B5DBE0;
constexpr size_t LuaPCallKOffset = 0x00B5DDD0;
constexpr size_t LuaLoadBufferXOffset = 0x00E382A0;
constexpr size_t LuaSetGlobalOffset = 0x026073E0;
constexpr size_t LuaCheckIntegerOffset = 0x00024340;
constexpr size_t CapMergeOffset = 0x0178148C;
constexpr size_t CurrentExperienceOffset = 0x02D3BC50;
constexpr size_t MinimumExperienceOffset = 0x00B85FD4;

constexpr char ScarletJournalPath[] =
    "sd:/config/km-editor/gameplay-settings/0100A3D008C5C000/settings.bin";
constexpr char VioletJournalPath[] =
    "sd:/config/km-editor/gameplay-settings/01008F6008C5E000/settings.bin";

// This helper owns no retail save keys. It presents three stock-style rows and
// bridges their menu-domain indexes to KM's SD-backed dual-slot journal.
constexpr char OptionsHelperSource[] = R"lua(
local function km_clone(source)
  if type(source) ~= "table" then return nil end
  local target = {}
  for key, value in pairs(source) do target[key] = value end
  return setmetatable(target, getmetatable(source))
end

local function km_choices(template, prefix, count)
  local choices = km_clone(template)
  if choices == nil then return nil end
  for key in pairs(choices) do
    if type(key) == "number" then choices[key] = nil end
  end
  for index = 0, count - 1 do
    choices[index] = prefix .. string.format("%03d", index)
  end
  choices.length = count
  return choices
end

local function km_row(template, name, info, prefix, count, initial)
  local row = km_clone(template)
  if row == nil then return nil end
  local details = km_choices(template.detailSelect, prefix, count)
  if details == nil then return nil end
  row.numId = initial
  row.startNum = initial
  row.defaultId = initial
  row.menuName = name
  row.infoName = info
  row.selectMax = count
  row.detailSelect = details
  row.buttonType = 0
  return row
end

local function km_rows(self)
  if type(self) ~= "table" or type(self[12]) ~= "table" then return nil end
  return self[12]
end

local function km_construct(self)
  local rows = km_rows(self)
  if rows == nil or type(rows.length) ~= "number" then return end
  if rows.length == 21 then
    if type(rows[18]) == "table" and type(rows[19]) == "table"
        and type(rows[20]) == "table"
        and rows[18].menuName == "km_ui_gameplay_level_cap_name"
        and rows[19].menuName == "km_ui_gameplay_experience_rate_name"
        and rows[20].menuName == "km_ui_gameplay_experience_share_name" then
      return
    end
    return
  end
  if rows.length ~= 18 or type(rows[17]) ~= "table" then return end
  local template = rows[17]
  local cap = km_row(template,
    "km_ui_gameplay_level_cap_name", "km_ui_gameplay_level_cap_info",
    "km_ui_gameplay_level_cap_", 101, 0)
  local rate = km_row(template,
    "km_ui_gameplay_experience_rate_name", "km_ui_gameplay_experience_rate_info",
    "km_ui_gameplay_experience_rate_", 51, 10)
  local share = km_row(template,
    "km_ui_gameplay_experience_share_name", "km_ui_gameplay_experience_share_info",
    "km_ui_gameplay_experience_share_", 2, 0)
  if cap == nil or rate == nil or share == nil then return end
  rows[18] = cap
  rows[19] = rate
  rows[20] = share
  rows.length = 21
end

local function km_indexes(rows)
  if rows == nil or rows.length ~= 21 then return nil end
  local cap = rows[18] and rows[18].numId
  local rate = rows[19] and rows[19].numId
  local share = rows[20] and rows[20].numId
  if type(cap) ~= "number" or cap % 1 ~= 0 or cap < 0 or cap > 100 then return nil end
  if type(rate) ~= "number" or rate % 1 ~= 0 or rate < 0 or rate > 50 then return nil end
  if type(share) ~= "number" or share % 1 ~= 0 or share < 0 or share > 1 then return nil end
  return cap, rate, share
end

local function km_set(rows, cap, rate, share)
  if type(cap) ~= "number" or cap % 1 ~= 0 or cap < 0 or cap > 100 then return end
  if type(rate) ~= "number" or rate % 1 ~= 0 or rate < 0 or rate > 50 then return end
  if type(share) ~= "number" or share % 1 ~= 0 or share < 0 or share > 1 then return end
  rows[18].numId, rows[18].startNum = cap, cap
  rows[19].numId, rows[19].startNum = rate, rate
  rows[20].numId, rows[20].startNum = share, share
end

local function km_load(self)
  local rows = km_rows(self)
  if rows == nil or rows.length ~= 21 then return end
  km_set(rows, KMRuntimeGet())
end

local function km_apply(self)
  local rows = km_rows(self)
  local cap, rate, share = km_indexes(rows)
  if cap == nil then return end
  if KMRuntimeApply(cap, rate, share) ~= 1 then
    km_set(rows, KMRuntimeGet())
  else
    rows[18].startNum = cap
    rows[19].startNum = rate
    rows[20].startNum = share
  end
end

function KMGameplayOptionsConstruct(self) pcall(km_construct, self) end
function KMGameplayOptionsLoad(self) pcall(km_load, self) end
function KMGameplayOptionsApply(self) pcall(km_apply, self) end
)lua";

km::GuestFilesystemApi g_filesystem{};
LuaApi g_lua{};
uintptr_t g_main_base;
uint64_t g_title_id;
const char* g_journal_path;
uint64_t* g_settings_snapshot;
uint32_t g_activation_state;
uint32_t g_settings_lock;
alignas(16) uint8_t g_initialization_worker_stack[InitializationWorkerStackLength];
uint32_t g_initialization_worker_state;

extern "C" int km_sv_registration_trampoline(LuaState* state);
extern "C" int km_sv_registration_hook(LuaState* state);

template <size_t Count>
bool Matches(uintptr_t address, const uint32_t (&expected)[Count]) {
    return km::MemoryCompare(reinterpret_cast<const void*>(address), expected,
                             sizeof(expected)) == 0;
}

void BuildRegistrationStub(uint32_t (&output)[4]) {
    output[0] = FarLoadX17;
    output[1] = FarBranchX17;
    const auto destination = reinterpret_cast<uintptr_t>(&km_sv_registration_hook);
    km::MemoryCopy(output + 2, &destination, sizeof(destination));
}

bool IsRuntimeSnapshotWritable(const km::ModuleRange& module) {
    if (module.data_base != module.base + MainDataOffset) {
        return false;
    }
    const auto snapshot = module.base + RuntimeSnapshotOffset;
    km::MemoryInfo info{};
    uint32_t page_info = 0;
    if (km::km_svc_query_memory(&info, &page_info, snapshot)
            != km::ResultSuccess
        || (info.type & MemoryTypeMask)
            != static_cast<uint32_t>(km::MemoryType::CodeMutable)
        || info.permissions
            != (km::PermissionRead | km::PermissionWrite)
        || info.address > snapshot
        || info.size < sizeof(uint64_t)) {
        return false;
    }
    const auto offset = snapshot - info.address;
    return offset <= info.size - sizeof(uint64_t);
}

bool ValidateProfile(const km::ModuleRange& module) {
    if (module.text_size < MainTextMinimum
        || !IsRuntimeSnapshotWritable(module)
        || !Matches(module.base + LuaPushIntegerOffset, PushIntegerPreimage)
        || !Matches(module.base + LuaGetTopOffset, GetTopPreimage)
        || !Matches(module.base + LuaSetTopOffset, SetTopPreimage)
        || !Matches(module.base + LuaPushCClosureOffset, PushCClosurePreimage)
        || !Matches(module.base + LuaPCallKOffset, PCallKPreimage)
        || !Matches(module.base + LuaLoadBufferXOffset, LoadBufferXPreimage)
        || !Matches(module.base + LuaSetGlobalOffset, SetGlobalPreimage)
        || !Matches(module.base + LuaCheckIntegerOffset, CheckIntegerPreimage)
        || !Matches(module.base + RegistrationOffset, RegistrationPreimage)
        || !Matches(module.base + ShareDecisionOffset, SharePreimage)
        || !Matches(module.base + RateRouteOffset - 0x10, RateRoutePreimage)
        || !Matches(module.base + CapRouteOffset - 0x10, CapRoutePreimage)
        || !Matches(module.base + CapMergeOffset, CapMergePreimage)
        || !Matches(module.base + CurrentExperienceOffset, CurrentExperiencePreimage)
        || !Matches(module.base + MinimumExperienceOffset, MinimumExperiencePreimage)
        || km::MemoryCompare(
               reinterpret_cast<const void*>(module.base + ScaffoldOffset),
               ScaffoldTemplate,
               sizeof(ScaffoldTemplate)) != 0) {
        return false;
    }
    return true;
}

bool ResolveLuaApi(uintptr_t main_base, LuaApi* output) {
    if (output == nullptr) {
        return false;
    }
    *output = LuaApi{
        reinterpret_cast<LuaPushInteger>(main_base + LuaPushIntegerOffset),
        reinterpret_cast<LuaGetTop>(main_base + LuaGetTopOffset),
        reinterpret_cast<LuaSetTop>(main_base + LuaSetTopOffset),
        reinterpret_cast<LuaPushCClosure>(main_base + LuaPushCClosureOffset),
        reinterpret_cast<LuaPCallK>(main_base + LuaPCallKOffset),
        reinterpret_cast<LuaLoadBufferX>(main_base + LuaLoadBufferXOffset),
        reinterpret_cast<LuaSetGlobal>(main_base + LuaSetGlobalOffset),
        reinterpret_cast<LuaCheckInteger>(main_base + LuaCheckIntegerOffset),
    };
    return true;
}

bool ReadSnapshot(km::SettingsValues* values) {
    if (g_settings_snapshot == nullptr) {
        return false;
    }
    const auto packed = __atomic_load_n(g_settings_snapshot, __ATOMIC_ACQUIRE);
    uint64_t presence = 0;
    return km::UnpackSettingsSnapshot(packed, values, &presence)
        && (presence & RequiredPresence) == RequiredPresence;
}

void PublishSnapshot(uint64_t packed) {
    __atomic_store_n(g_settings_snapshot, packed, __ATOMIC_RELEASE);
}

void AcquireSettingsLock() {
    for (;;) {
        uint32_t expected = 0;
        if (__atomic_compare_exchange_n(&g_settings_lock, &expected, 1, false,
                                        __ATOMIC_ACQUIRE, __ATOMIC_RELAXED)) {
            return;
        }
        km::km_svc_sleep_thread(0);
    }
}

void ReleaseSettingsLock() {
    __atomic_store_n(&g_settings_lock, 0, __ATOMIC_RELEASE);
}

bool VerifyInstalled() {
    uint32_t expected_registration[4]{};
    BuildRegistrationStub(expected_registration);
    return *reinterpret_cast<const uint32_t*>(g_main_base + ShareDecisionOffset)
            == ShareRouteInstalled
        && *reinterpret_cast<const uint32_t*>(g_main_base + RateRouteOffset)
            == RateRouteInstalled
        && *reinterpret_cast<const uint32_t*>(g_main_base + CapRouteOffset)
            == CapRouteInstalled
        && km::MemoryCompare(reinterpret_cast<const void*>(g_main_base + ScaffoldOffset),
                             ScaffoldTemplate, sizeof(ScaffoldTemplate)) == 0
        && km::MemoryCompare(reinterpret_cast<const void*>(g_main_base + RegistrationOffset),
                             expected_registration, sizeof(expected_registration)) == 0;
}

void EnterFailClosedState() {
    // A zero snapshot makes every immutable gameplay hook execute its retail
    // fallback. State 4 prevents any further journal writes this process.
    PublishSnapshot(0);
    __atomic_store_n(&g_activation_state, 4, __ATOMIC_RELEASE);
}

bool ApplySettings(const km::SettingsValues& requested) {
    if (__atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE) != 2) {
        // State 1 means the journal is not readable yet and state 3 means the
        // retry worker currently owns initialization. Neither is corruption.
        // Reject this Apply so the Lua helper restores the visible vanilla
        // values, without poisoning a runtime that can still become ready.
        return false;
    }

    // Serialize the complete durable-commit-through-snapshot-publication
    // boundary. JournalLock alone cannot prevent commit A from releasing the
    // journal, commit B publishing, and then A publishing its older snapshot.
    AcquireSettingsLock();
    km::SettingsValues current{};
    if (__atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE) != 2
        || !ReadSnapshot(&current) || !VerifyInstalled()) {
        if (__atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE) == 2
            && g_settings_snapshot != nullptr) {
            EnterFailClosedState();
        }
        ReleaseSettingsLock();
        return false;
    }

    km::SettingsState committed{};
    if (!km::CommitSettingsJournal(g_filesystem, g_journal_path,
                                   km::SettingsFamily::ScarletViolet,
                                   g_title_id, RequiredPresence, requested,
                                   &committed)) {
        // A failed readback can follow a completed write. Reconcile from the
        // journal rather than guessing which slot is authoritative.
        km::SettingsState reconciled{};
        if (km::LoadSettingsJournal(g_filesystem, g_journal_path,
                                    km::SettingsFamily::ScarletViolet,
                                    g_title_id, RequiredPresence, &reconciled)) {
            PublishSnapshot(km::PackSettingsSnapshot(reconciled));
        } else {
            // CommitSettingsJournal can fail after the durable slot changed.
            // If no authenticated winner can be recovered, do not guess: the
            // immutable hooks fall back to retail until the next process boot.
            EnterFailClosedState();
        }
        ReleaseSettingsLock();
        return false;
    }

    PublishSnapshot(km::PackSettingsSnapshot(committed));
    ReleaseSettingsLock();
    return true;
}

bool InitializeGameplayRuntime() {
    if (__atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE) == 2) {
        return true;
    }
    uint32_t expected_state = 1;
    if (!__atomic_compare_exchange_n(&g_activation_state, &expected_state, 3,
                                     false, __ATOMIC_ACQ_REL, __ATOMIC_ACQUIRE)) {
        return expected_state == 2;
    }

    km::SettingsState initial{};
    if (!km::LoadSettingsJournal(g_filesystem, g_journal_path,
                                 km::SettingsFamily::ScarletViolet,
                                 g_title_id, RequiredPresence, &initial)) {
        __atomic_store_n(&g_activation_state, 1, __ATOMIC_RELEASE);
        return false;
    }

    if (!VerifyInstalled()) {
        EnterFailClosedState();
        return false;
    }

    PublishSnapshot(km::PackSettingsSnapshot(initial));
    __atomic_store_n(&g_activation_state, 2, __ATOMIC_RELEASE);
    return true;
}

extern "C" void km_sv_initialization_worker(void*) {
    while (true) {
        const auto state = __atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE);
        if (state != 1 && state != 3) {
            break;
        }
        if (InitializeGameplayRuntime()) {
            break;
        }
        km::km_svc_sleep_thread(InitializationRetryNanoseconds);
    }
    __atomic_store_n(&g_initialization_worker_state, 2, __ATOMIC_RELEASE);
    km::km_svc_exit_thread();
}

void StartInitializationWorker() {
    uint32_t expected_state = 0;
    if (!__atomic_compare_exchange_n(&g_initialization_worker_state,
                                     &expected_state, 1, false,
                                     __ATOMIC_ACQ_REL, __ATOMIC_ACQUIRE)) {
        return;
    }

    km::Handle thread = km::InvalidHandle;
    if (km::km_svc_create_thread(
            &thread,
            km_sv_initialization_worker,
            nullptr,
            g_initialization_worker_stack + InitializationWorkerStackLength,
            0x2C,
            2) != km::ResultSuccess) {
        __atomic_store_n(&g_initialization_worker_state, 0, __ATOMIC_RELEASE);
        return;
    }
    if (km::km_svc_start_thread(thread) != km::ResultSuccess) {
        km::km_svc_close_handle(thread);
        __atomic_store_n(&g_initialization_worker_state, 0, __ATOMIC_RELEASE);
        return;
    }
    // The kernel retains the running thread. Closing this userspace handle
    // avoids leaking a handle while the static worker retries until success.
    km::km_svc_close_handle(thread);
}

void RegisterGlobal(LuaState* state, const char* name, LuaCFunction function) {
    g_lua.push_c_closure(state, function, 0);
    g_lua.set_global(state, name);
}

int NoOpHelper(LuaState*) {
    return 0;
}

int RuntimeGet(LuaState* state) {
    if (__atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE) != 2) {
        InitializeGameplayRuntime();
    }
    km::SettingsValues values{};
    if (__atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE) != 2
        || !ReadSnapshot(&values)) {
        // The Options script always expects three return values. Returning no
        // values during the worker's state-3 window leaves a rejected edit on
        // screen even though it was never committed. Explicit retail values
        // make that rejection visible and deterministic.
        values = km::VanillaSettings;
    }
    const auto cap = values.level_cap_enabled ? values.level_cap : 0;
    const auto rate = values.experience_rate_basis_points / 1000;
    const auto share = values.experience_share ? 0 : 1;
    g_lua.push_integer(state, cap);
    g_lua.push_integer(state, rate);
    g_lua.push_integer(state, share);
    return 3;
}

int RuntimeApply(LuaState* state) {
    const auto cap = g_lua.check_integer(state, 1);
    const auto rate = g_lua.check_integer(state, 2);
    const auto share = g_lua.check_integer(state, 3);
    bool success = false;
    if (cap >= 0 && cap <= 100 && rate >= 0 && rate <= 50
        && share >= 0 && share <= 1) {
        if (__atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE) != 2) {
            InitializeGameplayRuntime();
        }
        const km::SettingsValues requested{
            share == 0,
            static_cast<uint32_t>(rate * 1000),
            cap != 0,
            static_cast<uint8_t>(cap == 0 ? 100 : cap),
        };
        success = ApplySettings(requested);
    }
    g_lua.push_integer(state, success ? 1 : 0);
    return 1;
}

} // namespace

extern "C" uintptr_t g_km_sv_registration_resume;
uintptr_t g_km_sv_registration_resume;

extern "C" int km_sv_registration_hook(LuaState* state) {
    const auto original_results = km_sv_registration_trampoline(state);
    if (__atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE) == 0) {
        return original_results;
    }

    const auto original_top = g_lua.get_top(state);
    // These fallbacks keep the transformed BLUA callable if helper parsing or
    // execution ever fails. The retail Options screen remains usable.
    RegisterGlobal(state, "KMGameplayOptionsConstruct", NoOpHelper);
    RegisterGlobal(state, "KMGameplayOptionsLoad", NoOpHelper);
    RegisterGlobal(state, "KMGameplayOptionsApply", NoOpHelper);

    // This native-binding callback runs during Lua VM construction, after the
    // game's filesystem layer is available and before scripts or gameplay can
    // consume the settings. Applying the journal here does not depend on the
    // player opening Options.
    const auto runtime_ready = InitializeGameplayRuntime();

    RegisterGlobal(state, "KMRuntimeGet", RuntimeGet);
    RegisterGlobal(state, "KMRuntimeApply", RuntimeApply);

    const auto load_status = g_lua.load_buffer_x(
        state, OptionsHelperSource, sizeof(OptionsHelperSource) - 1,
        "@KM/gameplay_options", "t");
    if (load_status == 0) {
        g_lua.pcall_k(state, 0, 0, 0, 0, nullptr);
    }
    g_lua.set_top(state, original_top);
    if (!runtime_ready) {
        // SD mounting can transiently lag Lua VM construction. Retry on a
        // dedicated sleeping worker until the authenticated journal becomes
        // readable; the game thread and Options screen never wait on a timer.
        StartInitializationWorker();
    }
    return original_results;
}

extern "C" bool km_try_activate_sv(const km::ModuleRange* modules,
                                     size_t module_count,
                                     const km::GuestFilesystemApi* filesystem) {
    if (modules == nullptr || module_count == 0 || filesystem == nullptr
        || __atomic_load_n(&g_activation_state, __ATOMIC_ACQUIRE) != 0) {
        return false;
    }

    uint64_t title_id = 0;
    if (km::km_svc_get_info(&title_id, InfoProgramId,
                            km::CurrentProcessPseudoHandle, 0) != km::ResultSuccess
        || (title_id != ScarletTitleId && title_id != VioletTitleId)) {
        return false;
    }

    const km::ModuleRange* main_module = nullptr;
    for (size_t index = 0; index < module_count; ++index) {
        if (!ValidateProfile(modules[index])) {
            continue;
        }
        if (main_module != nullptr) {
            return false;
        }
        main_module = &modules[index];
    }
    if (main_module == nullptr) {
        return false;
    }

    const auto* journal_path = title_id == ScarletTitleId
        ? ScarletJournalPath : VioletJournalPath;
    g_filesystem = *filesystem;
    g_main_base = main_module->base;
    g_title_id = title_id;
    g_journal_path = journal_path;
    g_settings_snapshot = reinterpret_cast<uint64_t*>(
        g_main_base + RuntimeSnapshotOffset);
    // The managed exefs/main derivation reserves this aligned BSS slot. Zero is
    // the immutable hooks' explicit retail fallback and is safe to publish
    // before the authenticated journal becomes readable.
    PublishSnapshot(0);
    ResolveLuaApi(g_main_base, &g_lua);
    g_km_sv_registration_resume = g_main_base + RegistrationResumeOffset;

    uint32_t registration_stub[4]{};
    BuildRegistrationStub(registration_stub);
    const km::ExecutablePatch patches[] = {
        {g_main_base + RegistrationOffset, RegistrationPreimage, registration_stub,
         sizeof(registration_stub)},
    };

    // Arm callbacks before publishing the registration branch, which is the
    // last patch in the transaction. A failed transaction restores everything.
    __atomic_store_n(&g_activation_state, 1, __ATOMIC_RELEASE);
    if (!km::PatchExecutableTransaction(patches, sizeof(patches) / sizeof(patches[0]))) {
        __atomic_store_n(&g_activation_state, 0, __ATOMIC_RELEASE);
        g_settings_snapshot = nullptr;
        return false;
    }
    // Begin an asynchronous, unbounded journal retry immediately after the
    // verified hook is armed. Lua registration performs the same guarded
    // initialization synchronously when it arrives, while this worker covers
    // delayed SD mounting and guarantees that opening Options is not required.
    StartInitializationWorker();
    return true;
}
