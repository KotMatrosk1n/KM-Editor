// SPDX-License-Identifier: GPL-3.0-only
#include "km_runtime.hpp"
#include "profiles.hpp"

extern "C" void km_runtime_initialize();
extern "C" {
alignas(16) __attribute__((used, visibility("hidden"))) uint8_t km_gift_catalog[262144] = {'K','M','G','I','F','T','0','0'};
}
namespace {
constexpr size_t PayloadSize = 262144;
constexpr size_t EntrySize = 768;
constexpr size_t MaximumEntries = 128;
auto& catalog = km_gift_catalog;
void (*volatile initialization_anchor)() = &km_runtime_initialize;
uintptr_t main_base;
int edition_delta;
bool entered_receiver;
using RandomBytes = void (*)(void*, size_t);
RandomBytes random_bytes;
uint8_t working_card[720];
uint8_t royal_cards[9][720];
bool royal_ready[9];

template<class T> T& Field(uintptr_t address, size_t offset = 0) {
    return *reinterpret_cast<T*>(address + offset);
}
template<class Result, class... Arguments> Result Native(uintptr_t address, Arguments... args) {
    return reinterpret_cast<Result (*)(Arguments...)>(main_base + address + edition_delta)(args...);
}
uint32_t Read32(const uint8_t* bytes) {
    return uint32_t(bytes[0]) | uint32_t(bytes[1]) << 8 | uint32_t(bytes[2]) << 16 | uint32_t(bytes[3]) << 24;
}
uint64_t Digest(const uint8_t* bytes, size_t size, bool payload = false) {
    uint64_t hash = 14695981039346656037ULL;
    for (size_t index = 0; index < size; ++index)
        hash = (hash ^ (payload && index >= 32 && index < 40 ? uint8_t{0} : bytes[index])) * 1099511628211ULL;
    return hash;
}
uint32_t Roll(uint32_t limit) {
    uint32_t value;
    const uint32_t threshold = uint32_t(-limit) % limit;
    do { random_bytes(&value, sizeof(value)); } while (value < threshold);
    return value % limit;
}
uint32_t RandomIdentity() {
    uint32_t value;
    do { random_bytes(&value, sizeof(value)); } while (value == 0);
    return value;
}
uintptr_t SaveRoot() {
    return Field<uintptr_t>(Field<uintptr_t>(main_base, 0x2610798));
}
uintptr_t Request(uintptr_t state, size_t offset) {
    const auto reference = Field<uintptr_t>(state, offset);
    if (!reference || Field<uint32_t>(reference, 0x54) == 0) return 0;
    const auto value = Field<uintptr_t>(reference, 0x60);
    return value ? value - 0x50 : 0;
}
void SetPhase(uintptr_t state, uint32_t phase) {
    Field<uint32_t>(state, 0x2AC) = phase;
    Field<uint8_t>(state, 0x2A8) = 1;
}
void FreezeCard(const uint8_t* entry) {
    for (size_t index = 0; index < sizeof(working_card); ++index) working_card[index] = entry[32 + index];
    auto card = reinterpret_cast<uintptr_t>(working_card);
    Native<bool>(0x1449CA0, card);
    Field<uint32_t>(card, 0x28) = RandomIdentity();
    auto pid = RandomIdentity();
    const auto trainer = Native<uint32_t>(0x1424D30, Field<uintptr_t>(SaveRoot(), 0x1E8));
    const auto xor_value = uint16_t(trainer) ^ uint16_t(trainer >> 16) ^ uint16_t(pid) ^ uint16_t(pid >> 16);
    if (working_card[0x248] == 0 && xor_value < 16) pid ^= 0x10000000U;
    if (working_card[0x248] == 2)
        pid = (pid & 65535U) | uint32_t(uint16_t(pid) ^ uint16_t(trainer) ^ uint16_t(trainer >> 16) ^ 1U) << 16;
    Field<uint32_t>(card, 0x2C) = pid;
    working_card[0x248] = 4;
    if (working_card[0x246] == 255) working_card[0x246] = uint8_t(Roll(25));
    if (working_card[0x247] == 3) working_card[0x247] = (entry[19] & 2) ? uint8_t(Roll(2)) : 0;
    if (working_card[0x243] == 3) {
        const auto ratio = entry[18];
        working_card[0x243] = ratio == 255 ? 2 : ratio == 254 ? 1 : ratio == 0 ? 0 : uint8_t(Roll(256) < ratio);
    }
    unsigned flawless = 0;
    for (size_t index = 0; index < 6; ++index) {
        const auto value = working_card[0x26C + index];
        if (value >= 252 && value <= 254) flawless = value - 251;
    }
    for (size_t index = 0; index < 6; ++index)
        if (flawless || working_card[0x26C + index] > 31) working_card[0x26C + index] = uint8_t(Roll(32));
    bool chosen[6]{};
    while (flawless) {
        const auto index = Roll(6);
        if (chosen[index]) continue;
        chosen[index] = true;
        working_card[0x26C + index] = 31;
        --flawless;
    }
    uint16_t crc = 65535;
    for (size_t index = 0; index < sizeof(working_card); ++index) {
        crc ^= uint16_t(index == 0x2CC || index == 0x2CD ? 0 : working_card[index]) << 8;
        for (unsigned bit = 0; bit < 8; ++bit) crc = uint16_t((crc << 1) ^ ((crc & 0x8000) ? 0x1021 : 0));
    }
    Field<uint16_t>(card, 0x2CC) = crc;
}
const uint8_t* ChooseEntry(uintptr_t state) {
    const auto count = Read32(catalog + 20);
    if (Read32(catalog + 16) == 2) {
        const auto badges = Native<uint32_t>(0x1438FB0, Field<uintptr_t>(SaveRoot(), 0x208));
        if (badges > 8) return nullptr;
        if (royal_ready[badges]) {
            for (size_t index = 0; index < sizeof(working_card); ++index) working_card[index] = royal_cards[badges][index];
            return working_card;
        }
        uint32_t matches = 0;
        uint32_t total = 0;
        for (uint32_t index = 0; index < count; ++index) {
            const auto entry = catalog + 64 + EntrySize * index;
            if (entry[17] != badges) continue;
            if (++matches > 14 || Read32(entry + 20) > 10000) return nullptr;
            total += Read32(entry + 20);
        }
        if (!matches) return nullptr;
        if (total != 10000) return nullptr;
        auto selection = Roll(total);
        for (uint32_t index = 0; index < count; ++index) {
            const auto entry = catalog + 64 + EntrySize * index;
            if (entry[17] != badges) continue;
            const auto weight = Read32(entry + 20);
            if (selection >= weight) { selection -= weight; continue; }
            FreezeCard(entry);
            for (size_t part = 0; part < sizeof(working_card); ++part) royal_cards[badges][part] = working_card[part];
            royal_ready[badges] = true;
            return working_card;
        }
        return nullptr;
    }
    const auto context = Field<uintptr_t>(Field<uintptr_t>(state, 0x68), 0x60);
    const auto app_reference = Field<uintptr_t>(context, 0x20);
    if (!app_reference) return nullptr;
    const auto app_value = Field<uintptr_t>(app_reference, 0x60);
    if (!app_value) return nullptr;
    const auto code = reinterpret_cast<const uint8_t*>(app_value - 0x50 + 0x79);
    for (uint32_t index = 0; index < count; ++index) {
        const auto entry = catalog + 64 + EntrySize * index;
        bool equal = true;
        for (size_t part = 0; part < 17; ++part) {
            auto value = code[part];
            if (value >= 'a' && value <= 'z') value -= 'a' - 'A';
            if (value != entry[part]) { equal = false; break; }
            if (value == 0) break;
        }
        if (equal) { FreezeCard(entry); return working_card; }
    }
    return nullptr;
}
void Change(uintptr_t state, uint32_t next) {
    if (next == 2) {
        const bool royal = Read32(catalog + 16) == 2;
        Field<uint32_t>(state, 0x78) = royal ? 5 : 4;
        SetPhase(state, royal ? 8 : 4);
        return;
    }
    Native<void>(0x1008540, state, next);
}
void ShowFailure(uintptr_t state, uint32_t message) {
    // The native dialog copies this small callback and returns through the ordinary receiver exit.
    uintptr_t callback[] = {
        main_base + 0x100A640 + edition_delta, main_base + 0x100A650 + edition_delta,
        main_base + 0x100A660 + edition_delta, main_base + 0x100A670 + edition_delta, state,
    };
    Native<void>(0x1007F40, state + 0x1D0, message, callback);
    SetPhase(state, 19);
}
void Receive(uintptr_t state) {
    const auto phase = Field<uint32_t>(state, 0x2AC);
    if (Field<uint8_t>(state, 0x2A8) && phase == 8) {
        const auto card = ChooseEntry(state);
        const auto request = Request(state, 0x98);
        if (!request) return;
        const int result = card ? Native<int>(0xFF0E00, request, 0, card, uint64_t{720}) : 12;
        Field<uint32_t>(state, 0x3BC) = uint32_t(result);
        if (result == 0) {
            Native<void>(0x1007880, state);
            Native<void>(0x10078B0, state);
            SetPhase(state, 13);
        } else ShowFailure(state, card ? 4 : 5);
        return;
    }
    if (Field<uint8_t>(state, 0x2A8) && phase == 13 && Field<uint32_t>(state, 0x3BC) == 0) {
        if (Native<int>(0x1007900, state)) {
            const auto request = Request(state, 0x98);
            if (request) { Native<void>(0xFF0DE0, request, 7); Field<uint32_t>(state, 0x78) = 10; }
        }
        return;
    }
    Native<void>(0x1009000, state);
}
int Menu(uintptr_t state) {
    const int result = Native<int>(0xFFCB10, state);
    if (Field<uint32_t>(state, 0x80) == 1) {
        const auto request = Request(state, 0x88);
        if (request) {
            Native<void>(0xFF0DE0, request, entered_receiver ? 0 : 4);
            entered_receiver = !entered_receiver;
            return 1;
        }
    }
    return result;
}
}

extern "C" void km_runtime_initialize() {
    if (initialization_anchor != &km_runtime_initialize || Read32(catalog) != 0x49474D4B || Read32(catalog + 4) != 0x31305446 || Read32(catalog + 8) != 1
        || Read32(catalog + 16) < 1 || Read32(catalog + 16) > 2 || Read32(catalog + 20) > MaximumEntries
        || Field<uint64_t>(reinterpret_cast<uintptr_t>(catalog), 32) != Digest(catalog, PayloadSize, true)) return;
    km::ModuleRange modules[16]{};
    size_t count = 0;
    if (!km::FindMappedModules(modules, 16, &count)) return;
    random_bytes = reinterpret_cast<RandomBytes>(km::ResolveGuestExport(modules, count, "_ZN2nn6crypto36GenerateCryptographicallyRandomBytesEPvm"));
    if (!random_bytes) return;
    for (size_t index = 0; index < count; ++index) {
        const auto& module = modules[index];
        if (module.ro_base - module.base != 0x1901000 || module.data_base - module.base != 0x24DB000) continue;
        const auto edition = Read32(catalog + 12);
        if (edition != 1 && edition != 2) return;
        const auto& profile = Profiles[edition - 1];
        bool matches = true;
        for (const auto& guard : profile.guards)
            if (Digest(reinterpret_cast<const uint8_t*>(module.base + guard.offset), guard.size) != guard.hash) matches = false;
        if (!matches) return;
        main_base = module.base;
        edition_delta = edition == 1 ? -0x30 : 0;
        const auto serial_table = Field<uintptr_t>(main_base, 0x2624960);
        const auto menu_table = Field<uintptr_t>(main_base, 0x26248B8);
        if (serial_table != main_base + 0x2575230 || menu_table != main_base + 0x2574790
            || serial_table < module.data_base || serial_table + 0xA0 > module.data_base + module.data_size
            || menu_table < module.data_base || menu_table + 0x58 > module.data_base + module.data_size
            || Field<uintptr_t>(serial_table, 0x98) != main_base + 0x1008540 + edition_delta
            || Field<uintptr_t>(serial_table, 0x90) != main_base + 0x1009000 + edition_delta
            || Field<uintptr_t>(menu_table, 0x50) != main_base + 0xFFCB10 + edition_delta) return;
        Field<uintptr_t>(serial_table, 0x98) = reinterpret_cast<uintptr_t>(&Change);
        Field<uintptr_t>(serial_table, 0x90) = reinterpret_cast<uintptr_t>(&Receive);
        Field<uintptr_t>(menu_table, 0x50) = reinterpret_cast<uintptr_t>(&Menu);
        return;
    }
}
