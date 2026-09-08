// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_options.hpp"

extern "C" {
uintptr_t km_swsh_options_ctor_continue;
uintptr_t km_swsh_options_render_continue;
uintptr_t km_swsh_options_input_continue;
uintptr_t km_swsh_options_activate_continue;
uintptr_t km_swsh_options_number_continue;
uintptr_t km_swsh_options_apply_continue[3];
uintptr_t km_swsh_options_apply_failure[3];
void km_swsh_options_ctor_original(uintptr_t, uintptr_t, uintptr_t, uintptr_t);
void km_swsh_options_render_original(uintptr_t, int32_t, uint32_t);
void km_swsh_options_input_original(uintptr_t*, const int32_t*);
void km_swsh_options_activate_original(uintptr_t);
void km_swsh_options_number_original(uintptr_t, uint32_t, int32_t, int32_t, int32_t);
void km_swsh_options_apply_first();
void km_swsh_options_apply_second();
void km_swsh_options_reset_gate();
uint64_t km_swsh_read_settings();
bool km_swsh_write_settings(uint64_t);
}

namespace {
constexpr uintptr_t OptionsStart = 0x014D7BD0;
constexpr size_t OptionsLength = 0x014DEDD0 - OptionsStart;
constexpr uintptr_t DescriptorsOffset = 0xC50;
constexpr uintptr_t CountOffset = 0x5D4;
constexpr uintptr_t MapOffset = 0x5D8;
constexpr uint32_t StockCount = 16;
constexpr uint32_t TotalCount = 19;
constexpr uintptr_t RetryFlagOffset = 0x628;
constexpr uintptr_t RetrySelectionOffset = 0x62C;
constexpr uintptr_t RetryValuesOffset = 0x630;
constexpr uint32_t RetryDraftTag = 0x4B4D4452;
constexpr uint64_t Presence = km::PresenceExperienceShare
    | km::PresenceExperienceRate | km::PresenceLevelCap;

struct Descriptor {
    uint32_t kind;
    uint32_t id;
    uint64_t heading;
    uint64_t choices[3];
    uint64_t description;
    uint32_t value;
    uint32_t original;
};
static_assert(sizeof(Descriptor) == 0x38);
static_assert(DescriptorsOffset + TotalCount * sizeof(Descriptor) <= 0x1080);
static_assert(MapOffset + TotalCount * sizeof(uint32_t) < 0x950);
static_assert(RetryValuesOffset + TotalCount * 8 < 0x950);

struct OptionsWord { uintptr_t offset; uint32_t expected; uint32_t replacement; };
#include "km_swsh_options_words.inc"

alignas(8) uint8_t g_options_before[OptionsLength];
alignas(8) uint8_t g_options_after[OptionsLength];
uint32_t g_number_before[4];
uint32_t g_number_after[4];
uintptr_t g_main;
uintptr_t g_delta;
const Descriptor* g_render_descriptor;
uint32_t g_render_value;
uintptr_t g_render_bank;
void* g_render_thread;

constexpr uint64_t Hash(const char* text) {
    uint64_t value = 0xCBF29CE484222645ULL;
    while (*text) value = (value ^ static_cast<uint8_t>(*text++)) * 0x100000001B3ULL;
    return value;
}

template <typename T> T& Field(uintptr_t owner, uintptr_t offset) {
    return *reinterpret_cast<T*>(owner + offset);
}
Descriptor* Rows(uintptr_t view) {
    return reinterpret_cast<Descriptor*>(view + DescriptorsOffset);
}
bool HasKmRows(uintptr_t view) {
    const auto count = Field<uint32_t>(view, CountOffset);
    if (count < 3 || count > TotalCount) return false;
    const auto* map = reinterpret_cast<const uint32_t*>(view + MapOffset);
    return map[count - 3] == 16 && map[count - 2] == 17 && map[count - 1] == 18;
}
void LoadDraft(uintptr_t view) {
    km::SettingsValues values = km::VanillaSettings;
    km::UnpackSettingsSnapshot(km_swsh_read_settings(), &values);
    auto* rows = Rows(view);
    rows[16] = {0, 16, Hash("km_exp_share"),
        {Hash("km_off"), Hash("km_on"), Hash("")}, Hash("km_exp_share_help"),
        values.experience_share ? 1U : 0U, values.experience_share ? 1U : 0U};
    const uint32_t rate = values.experience_rate_basis_points / 1000;
    rows[17] = {2, 17, Hash("km_exp_rate"), {Hash(""), Hash(""), Hash("")},
        Hash("km_exp_rate_help"), rate, rate};
    const uint32_t cap = values.level_cap_enabled ? values.level_cap : 0;
    rows[18] = {2, 18, Hash("km_level_cap"), {Hash(""), Hash(""), Hash("")},
        Hash("km_level_cap_help"), cap, cap};
}
void Refresh(uintptr_t view) {
    reinterpret_cast<void (*)(uintptr_t)>(g_main + 0x014D9C90 + g_delta)(view);
}
void PreserveFailedDraft(uintptr_t view) {
    const auto* rows = Rows(view);
    for (uint32_t index = 0; index < TotalCount; ++index) {
        Field<uint32_t>(view, RetryValuesOffset + index * 8) = rows[index].value;
        Field<uint32_t>(view, RetryValuesOffset + index * 8 + 4) = rows[index].original;
    }
    Field<uint32_t>(view, RetrySelectionOffset) = Field<uint32_t>(view, 0x968);
    Field<uint32_t>(view, RetryFlagOffset) = RetryDraftTag;
    for (uint32_t index = StockCount; index < TotalCount; ++index)
        Rows(view)[index].description = Hash("km_save_failed");
}
bool PutBranch(uintptr_t offset, const void* target, const uint32_t (&expected)[4]) {
    const auto position = offset - OptionsStart;
    if (position > OptionsLength - 16
        || km::MemoryCompare(g_options_before + position, expected, 16) != 0) return false;
    const uint32_t branch[] = {0x58000051, 0xD61F0220};
    km::MemoryCopy(g_options_after + position, branch, 8);
    const auto address = reinterpret_cast<uintptr_t>(target);
    km::MemoryCopy(g_options_after + position + 8, &address, 8);
    return true;
}
}

extern "C" void km_swsh_options_ctor(uintptr_t view, uintptr_t parent,
                                      uintptr_t argument, uintptr_t context) {
    km_swsh_options_ctor_original(view, parent, argument, context);
    Field<uint32_t>(view, RetryFlagOffset) = 0;
    auto& count = Field<uint32_t>(view, CountOffset);
    if (count > StockCount) return;
    LoadDraft(view);
    auto* map = reinterpret_cast<uint32_t*>(view + MapOffset);
    for (uint32_t index = StockCount; index < TotalCount; ++index) map[count++] = index;
}

extern "C" void km_swsh_options_activate(uintptr_t owner) {
    km_swsh_options_activate_original(owner);
    const auto view = Field<uintptr_t>(owner, 0x78);
    if (!HasKmRows(view)) return;
    // The stock Top controller reloads values on return from confirmation.
    // Ordinary cancellation discards the draft. A failed write restores both
    // stock and KM drafts across that same controller transition.
    LoadDraft(view);
    if (Field<uint32_t>(view, RetryFlagOffset) == RetryDraftTag) {
        for (uint32_t index = 0; index < TotalCount; ++index) {
            Rows(view)[index].value = Field<uint32_t>(view, RetryValuesOffset + index * 8);
            Rows(view)[index].original = Field<uint32_t>(view, RetryValuesOffset + index * 8 + 4);
        }
        Field<uint32_t>(view, 0x968) = Field<uint32_t>(view, RetrySelectionOffset);
        Field<uint32_t>(view, RetryFlagOffset) = 0;
        for (uint32_t index = StockCount; index < TotalCount; ++index)
            Rows(view)[index].description = Hash("km_save_failed");
    }
    Refresh(view);
}

extern "C" void km_swsh_options_input(uintptr_t* callback, const int32_t* direction) {
    const auto view = Field<uintptr_t>(*callback, 0x78);
    const auto selected = Field<int32_t>(view, 0x968);
    const auto count = Field<uint32_t>(view, CountOffset);
    if (selected < 0 || static_cast<uint32_t>(selected) >= count || count > TotalCount) return;
    const auto index = Field<uint32_t>(view, MapOffset + selected * 4);
    if (index < StockCount) { km_swsh_options_input_original(callback, direction); return; }
    if (index >= TotalCount || !HasKmRows(view)) return;
    auto& row = Rows(view)[index];
    const int32_t maximum = index == 16 ? 1 : index == 17 ? 50 : 100;
    auto value = static_cast<int32_t>(row.value) + (*direction == 0 ? 1 : -1);
    if (value < 0) value = 0;
    if (value > maximum) value = maximum;
    if (static_cast<uint32_t>(value) == row.value) return;
    reinterpret_cast<void (*)(uintptr_t, uint32_t, uint32_t)>(
        g_main + 0x014D9A40 + g_delta)(view, static_cast<uint32_t>(value), index);
}

extern "C" void km_swsh_options_render(uintptr_t view, int32_t visual, uint32_t pane) {
    const auto count = Field<uint32_t>(view, CountOffset);
    if (visual < 0 || count > TotalCount || static_cast<uint32_t>(visual) >= count) return;
    const auto index = Field<uint32_t>(view, MapOffset + visual * 4);
    if (index >= TotalCount) return;
    auto& row = Rows(view)[index];
    const auto saved_descriptor = g_render_descriptor;
    const auto saved_value = g_render_value;
    const auto saved_bank = g_render_bank;
    const auto saved_thread = __atomic_load_n(&g_render_thread, __ATOMIC_ACQUIRE);
    __atomic_store_n(&g_render_thread, nullptr, __ATOMIC_RELEASE);
    const auto value = row.value;
    if (index == 17 || index == 18) {
        g_render_descriptor = &row;
        g_render_value = value;
        const auto manager = Field<uintptr_t>(view, 0x2B0);
        g_render_bank = manager == 0 ? 0 : Field<uintptr_t>(manager, 0x2E8);
        __atomic_store_n(&g_render_thread, km::km_get_thread_local_region(), __ATOMIC_RELEASE);
        // Only the stock slider animation receives a value in its 0..10 range.
        row.value = index == 17 ? value / 5 : (value + 9) / 10;
    } else {
        g_render_descriptor = nullptr;
    }
    km_swsh_options_render_original(view, visual, pane);
    row.value = value;
    g_render_descriptor = saved_descriptor;
    g_render_value = saved_value;
    g_render_bank = saved_bank;
    __atomic_store_n(&g_render_thread, saved_thread, __ATOMIC_RELEASE);
}

extern "C" void km_swsh_options_number(uintptr_t buffer, uint32_t value,
                                        int32_t digits, int32_t padding, int32_t latin) {
    if (__atomic_load_n(&g_render_thread, __ATOMIC_ACQUIRE) != km::km_get_thread_local_region()
        || g_render_descriptor == nullptr) {
        km_swsh_options_number_original(buffer, value, digits, padding, latin);
        return;
    }
    const auto append = reinterpret_cast<void (*)(uintptr_t, uint32_t)>(g_main + 0x0067D5C0);
    if (g_render_descriptor->id == 18 && g_render_value == 0) {
        const auto key = Hash("km_off");
        // Resolve through the same live Options message bank as stock labels.
        reinterpret_cast<void (*)(uintptr_t, const uint64_t*, uintptr_t)>(
            g_main + 0x0067EB10)(buffer, &key, g_render_bank);
        return;
    }
    const bool rate = g_render_descriptor->id == 17;
    km_swsh_options_number_original(buffer, rate ? g_render_value * 10 : g_render_value, 3, 0, 1);
    if (rate) append(buffer, '%');
}

extern "C" bool km_swsh_options_apply(uintptr_t owner) {
    const auto view = Field<uintptr_t>(owner, 0x78);
    auto& count = Field<uint32_t>(view, CountOffset);
    if (!HasKmRows(view)) return false;
    auto* rows = Rows(view);
    if (rows[16].value > 1 || rows[17].value > 50 || rows[18].value > 100) return false;
    km::SettingsState requested{km::SettingsFamily::SwordShield, 0, 0, Presence,
        {rows[16].value != 0, rows[17].value * 1000, rows[18].value != 0,
         static_cast<uint8_t>(rows[18].value == 0 ? 100 : rows[18].value)}, -1, false};
    const bool changed = rows[16].value != rows[16].original
        || rows[17].value != rows[17].original || rows[18].value != rows[18].original;
    if (changed && !km_swsh_write_settings(km::PackSettingsSnapshot(requested))) {
        PreserveFailedDraft(view);
        // Follow the stock confirmation's return to editing transition. The
        // assembly gates skip the stock success message and close animation.
        Field<uint32_t>(owner, 0x88) = 2;
        Refresh(view);
        return false;
    }
    const auto visible = count;
    count -= 3;
    reinterpret_cast<void (*)(uintptr_t)>(g_main + 0x014DD330 + g_delta)(owner);
    count = visible;
    for (uint32_t index = StockCount; index < TotalCount; ++index) rows[index].original = rows[index].value;
    return true;
}

extern "C" bool km_swsh_options_reset_apply(uintptr_t owner) {
    const auto view = Field<uintptr_t>(owner, 0x78);
    if (!HasKmRows(view)) return false;
    const km::SettingsState vanilla{km::SettingsFamily::SwordShield, 0, 0, Presence,
        km::VanillaSettings, -1, false};
    if (!km_swsh_write_settings(km::PackSettingsSnapshot(vanilla))) {
        PreserveFailedDraft(view);
        return false;
    }
    // Stock Restore Defaults persists its defaults immediately. Match that
    // behavior, but only after the KM journal has committed successfully.
    reinterpret_cast<void (*)(uintptr_t)>(g_main + 0x014DC340 + g_delta)(owner);
    LoadDraft(view);
    Field<uint32_t>(view, RetryFlagOffset) = 0;
    Refresh(view);
    return true;
}

namespace km {
bool PrepareSwShOptions(const ModuleRange& main, SwShNativeSettingsEdition edition,
                        ExecutablePatch (&patches)[2]) {
    g_main = main.base;
    g_delta = edition == SwShNativeSettingsEdition::Shield ? 0x30 : 0;
    if (main.text_size < OptionsStart + g_delta + OptionsLength) return false;
    MemoryCopy(g_options_before, reinterpret_cast<const void*>(main.base + OptionsStart + g_delta), OptionsLength);
    MemoryCopy(g_options_after, g_options_before, OptionsLength);
    for (const auto& word : OptionsWords) {
        const auto offset = word.offset - OptionsStart;
        if (offset > OptionsLength - 4 || MemoryCompare(g_options_before + offset, &word.expected, 4) != 0) return false;
        MemoryCopy(g_options_after + offset, &word.replacement, 4);
    }
    constexpr uint32_t ctor[] = {0xD10183FF, 0xF90013F7, 0xA90357F6, 0xA9044FF4};
    constexpr uint32_t render[] = {0xD10383FF, 0xFD0033EA, 0x6D0723E9, 0xA9086FFC};
    constexpr uint32_t input[] = {0xD10283FF, 0xF90023FB, 0xA90567FA, 0xA9065FF8};
    constexpr uint32_t activate[] = {0xD10183FF, 0xF90013F7, 0xA90357F6, 0xA9044FF4};
    constexpr uint32_t first[] = {0x94000061, 0x52802028, 0x790053E8, 0xD2871E68};
    constexpr uint32_t second[] = {0x97FFFFA6, 0x52802028, 0xA902FFFF, 0x790073E8};
    constexpr uint32_t reset[] = {0x94000039, 0x52802028, 0xA902FFFF, 0x790073E8};
    if (!PutBranch(0x014D8570, reinterpret_cast<const void*>(km_swsh_options_ctor), ctor)
        || !PutBranch(0x014D9CD0, reinterpret_cast<const void*>(km_swsh_options_render), render)
        || !PutBranch(0x014DE2C0, reinterpret_cast<const void*>(km_swsh_options_input), input)
        || !PutBranch(0x014DB690, reinterpret_cast<const void*>(km_swsh_options_activate), activate)
        || !PutBranch(0x014DC25C, reinterpret_cast<const void*>(km_swsh_options_reset_gate), reset)
        || !PutBranch(0x014DD1AC, reinterpret_cast<const void*>(km_swsh_options_apply_first), first)
        || !PutBranch(0x014DD498, reinterpret_cast<const void*>(km_swsh_options_apply_second), second)) return false;
    km_swsh_options_ctor_continue = main.base + 0x014D8580 + g_delta;
    km_swsh_options_render_continue = main.base + 0x014D9CE0 + g_delta;
    km_swsh_options_input_continue = main.base + 0x014DE2D0 + g_delta;
    km_swsh_options_activate_continue = main.base + 0x014DB6A0 + g_delta;
    km_swsh_options_number_continue = main.base + 0x013BF2B0 + g_delta;
    km_swsh_options_apply_continue[0] = main.base + 0x014DD1BC + g_delta;
    km_swsh_options_apply_continue[1] = main.base + 0x014DD4A8 + g_delta;
    km_swsh_options_apply_failure[0] = main.base + 0x014DD23C + g_delta;
    km_swsh_options_apply_failure[1] = main.base + 0x014DD564 + g_delta;
    km_swsh_options_apply_continue[2] = main.base + 0x014DC26C + g_delta;
    km_swsh_options_apply_failure[2] = main.base + 0x014DC324 + g_delta;
    constexpr uint32_t number[] = {0xA9BB67FA, 0xA9015FF8, 0xA90257F6, 0xA9034FF4};
    const auto number_address = main.base + 0x013BF2A0 + g_delta;
    if (MemoryCompare(reinterpret_cast<const void*>(number_address), number, 16) != 0) return false;
    MemoryCopy(g_number_before, number, 16);
    g_number_after[0] = 0x58000051;
    g_number_after[1] = 0xD61F0220;
    const auto target = reinterpret_cast<uintptr_t>(km_swsh_options_number);
    MemoryCopy(g_number_after + 2, &target, 8);
    patches[0] = {main.base + OptionsStart + g_delta, g_options_before, g_options_after, OptionsLength};
    patches[1] = {number_address, g_number_before, g_number_after, 16};
    return true;
}
}
