// SPDX-License-Identifier: GPL-3.0-only

#include "km_settings.hpp"

namespace {

constexpr size_t RecordSize = 0x100;
constexpr size_t SlotSize = 0x1000;
constexpr size_t JournalSize = SlotSize * 2;
constexpr uint16_t HeaderSize = 0x50;
constexpr uint16_t SupportedSchema = 1;
constexpr uint64_t KnownPresence =
    km::PresenceExperienceShare | km::PresenceExperienceRate | km::PresenceLevelCap;
constexpr uint64_t SerialHalfRange = uint64_t{1} << 63;
constexpr uint8_t Magic[8] = {'K', 'M', 'G', 'S', 'E', 'T', '0', '1'};
constexpr uint8_t Owner[8] = {'K', 'M', 'E', 'D', 'I', 'T', 'O', 'R'};

alignas(16) uint8_t g_journal_buffer[JournalSize];
alignas(16) uint8_t g_verify_buffer[JournalSize];
volatile uint32_t g_journal_lock;

uint16_t ReadU16(const uint8_t* bytes) {
    return static_cast<uint16_t>(bytes[0])
        | static_cast<uint16_t>(bytes[1]) << 8;
}

uint32_t ReadU32(const uint8_t* bytes) {
    return static_cast<uint32_t>(bytes[0])
        | static_cast<uint32_t>(bytes[1]) << 8
        | static_cast<uint32_t>(bytes[2]) << 16
        | static_cast<uint32_t>(bytes[3]) << 24;
}

uint64_t ReadU64(const uint8_t* bytes) {
    return static_cast<uint64_t>(ReadU32(bytes))
        | static_cast<uint64_t>(ReadU32(bytes + 4)) << 32;
}

void WriteU16(uint8_t* bytes, uint16_t value) {
    bytes[0] = static_cast<uint8_t>(value);
    bytes[1] = static_cast<uint8_t>(value >> 8);
}

void WriteU32(uint8_t* bytes, uint32_t value) {
    bytes[0] = static_cast<uint8_t>(value);
    bytes[1] = static_cast<uint8_t>(value >> 8);
    bytes[2] = static_cast<uint8_t>(value >> 16);
    bytes[3] = static_cast<uint8_t>(value >> 24);
}

void WriteU64(uint8_t* bytes, uint64_t value) {
    WriteU32(bytes, static_cast<uint32_t>(value));
    WriteU32(bytes + 4, static_cast<uint32_t>(value >> 32));
}

bool IsZero(const uint8_t* bytes, size_t length) {
    for (size_t index = 0; index < length; ++index) {
        if (bytes[index] != 0) {
            return false;
        }
    }
    return true;
}

uint32_t ComputeCrc32C(const uint8_t* bytes, size_t length) {
    uint32_t crc = UINT32_MAX;
    for (size_t index = 0; index < length; ++index) {
        const auto value = index >= 0x48 && index < 0x4C ? uint8_t{0} : bytes[index];
        crc ^= value;
        for (int bit = 0; bit < 8; ++bit) {
            crc = (crc >> 1) ^ (0x82F63B78U & static_cast<uint32_t>(
                -static_cast<int32_t>(crc & 1)));
        }
    }
    return ~crc;
}

bool IsCanonical(uint64_t presence, const km::SettingsValues& values) {
    if ((presence & ~KnownPresence) != 0
        || values.experience_rate_basis_points > 50000
        || values.experience_rate_basis_points % 1000 != 0
        || values.level_cap < 1 || values.level_cap > 100) {
        return false;
    }
    if ((presence & km::PresenceExperienceShare) == 0 && !values.experience_share) {
        return false;
    }
    if ((presence & km::PresenceExperienceRate) == 0
        && values.experience_rate_basis_points != 10000) {
        return false;
    }
    if ((presence & km::PresenceLevelCap) == 0
        && (values.level_cap_enabled || values.level_cap != 100)) {
        return false;
    }
    if (!values.level_cap_enabled && values.level_cap != 100) {
        return false;
    }
    return true;
}

enum class SlotKind {
    Empty,
    Valid,
    Newer,
    OwnedCorrupt,
    Foreign,
};

struct SlotView {
    SlotKind kind;
    uint64_t generation;
    uint64_t presence;
    km::SettingsValues values;
};

bool ValuesEqual(const km::SettingsValues& left,
                 const km::SettingsValues& right) {
    return left.experience_share == right.experience_share
        && left.experience_rate_basis_points == right.experience_rate_basis_points
        && left.level_cap_enabled == right.level_cap_enabled
        && left.level_cap == right.level_cap;
}

SlotView InspectSlot(const uint8_t* slot, km::SettingsFamily family,
                     uint64_t title_id) {
    if (IsZero(slot, SlotSize)) {
        return SlotView{SlotKind::Empty, 0, 0, km::VanillaSettings};
    }
    if (km::MemoryCompare(slot, Magic, sizeof(Magic)) != 0
        || km::MemoryCompare(slot + 8, Owner, sizeof(Owner)) != 0) {
        return SlotView{SlotKind::Foreign, 0, 0, km::VanillaSettings};
    }
    const auto header_size = ReadU16(slot + 0x10);
    const auto record_size = ReadU16(slot + 0x12);
    const auto schema = ReadU16(slot + 0x14);
    const auto stored_family = ReadU16(slot + 0x16);
    const auto stored_title = ReadU64(slot + 0x18);
    const auto identity_matches = stored_family == static_cast<uint16_t>(family)
        && stored_title == title_id;
    if (header_size < HeaderSize || record_size < HeaderSize || record_size > SlotSize
        || header_size > record_size || !IsZero(slot + record_size, SlotSize - record_size)
        || ComputeCrc32C(slot, record_size) != ReadU32(slot + 0x48)) {
        return SlotView{identity_matches ? SlotKind::OwnedCorrupt : SlotKind::Foreign,
                        0, 0, km::VanillaSettings};
    }
    if (!identity_matches) {
        return SlotView{SlotKind::Foreign, 0, 0, km::VanillaSettings};
    }
    if (schema > SupportedSchema) {
        return SlotView{SlotKind::Newer, 0, 0, km::VanillaSettings};
    }
    const auto presence = ReadU64(slot + 0x30);
    const km::SettingsValues values{
        slot[0x3C] != 0,
        ReadU32(slot + 0x38),
        slot[0x3D] != 0,
        slot[0x3E],
    };
    if (schema != SupportedSchema || header_size != HeaderSize || record_size != RecordSize
        || ReadU16(slot + 0x2E) != 0 || ReadU64(slot + 0x40) != 0
        || slot[0x3C] > 1 || slot[0x3D] > 1 || slot[0x3F] != 0
        || !IsZero(slot + 0x4C, 4) || !IsZero(slot + 0x50, RecordSize - 0x50)
        || !IsCanonical(presence, values)) {
        return SlotView{SlotKind::OwnedCorrupt, 0, 0, km::VanillaSettings};
    }
    return SlotView{SlotKind::Valid, ReadU64(slot + 0x20), presence, values};
}

bool ResolveJournal(const uint8_t* journal, km::SettingsFamily family,
                    uint64_t title_id, uint64_t required_presence,
                    km::SettingsState* output) {
    const auto first = InspectSlot(journal, family, title_id);
    const auto second = InspectSlot(journal + SlotSize, family, title_id);
    if (first.kind == SlotKind::Foreign || second.kind == SlotKind::Foreign
        || first.kind == SlotKind::Newer || second.kind == SlotKind::Newer) {
        return false;
    }
    const auto first_valid = first.kind == SlotKind::Valid;
    const auto second_valid = second.kind == SlotKind::Valid;
    if (!first_valid && !second_valid) {
        return false;
    }
    int active = 0;
    const SlotView* selected = &first;
    if (!first_valid) {
        active = 1;
        selected = &second;
    } else if (second_valid) {
        const auto difference = first.generation - second.generation;
        if (difference == 0) {
            if (first.presence != second.presence
                || !ValuesEqual(first.values, second.values)) {
                return false;
            }
        } else if (difference == SerialHalfRange) {
            return false;
        } else if (difference >= SerialHalfRange) {
            active = 1;
            selected = &second;
        }
    }
    if ((selected->presence & required_presence) != required_presence) {
        return false;
    }
    *output = km::SettingsState{family, title_id, selected->generation,
                                selected->presence, selected->values, active, true};
    return true;
}

bool OpenJournal(const km::GuestFilesystemApi& fs, const char* path, int32_t mode,
                 km::FileHandle* output) {
    if (fs.open_file(output, path, mode) == km::ResultSuccess) {
        return true;
    }
    fs.mount_sd("sd");
    return fs.open_file(output, path, mode) == km::ResultSuccess;
}

bool ReadJournal(const km::GuestFilesystemApi& fs, const char* path, int32_t mode,
                 uint8_t* destination, km::FileHandle* open_handle = nullptr) {
    km::FileHandle handle{};
    if (!OpenJournal(fs, path, mode, &handle)) {
        return false;
    }
    int64_t size = 0;
    uint64_t read = 0;
    const auto valid = fs.get_file_size(&size, handle) == km::ResultSuccess
        && size == static_cast<int64_t>(JournalSize)
        && fs.read_file(&read, handle, 0, destination, JournalSize) == km::ResultSuccess
        && read == JournalSize;
    if (!valid || open_handle == nullptr) {
        fs.close_file(handle);
    } else {
        *open_handle = handle;
    }
    return valid;
}

void SerializeSlot(uint8_t* slot, const km::SettingsState& current,
                   const km::SettingsValues& values) {
    km::MemorySet(slot, 0, SlotSize);
    km::MemoryCopy(slot, Magic, sizeof(Magic));
    km::MemoryCopy(slot + 8, Owner, sizeof(Owner));
    WriteU16(slot + 0x10, HeaderSize);
    WriteU16(slot + 0x12, RecordSize);
    WriteU16(slot + 0x14, SupportedSchema);
    WriteU16(slot + 0x16, static_cast<uint16_t>(current.family));
    WriteU64(slot + 0x18, current.title_id);
    WriteU64(slot + 0x20, current.generation + 1);
    WriteU16(slot + 0x28, 2);
    WriteU16(slot + 0x2A, 5);
    WriteU16(slot + 0x2C, 0);
    WriteU64(slot + 0x30, current.presence);
    WriteU32(slot + 0x38, values.experience_rate_basis_points);
    slot[0x3C] = values.experience_share ? 1 : 0;
    slot[0x3D] = values.level_cap_enabled ? 1 : 0;
    slot[0x3E] = values.level_cap;
    WriteU32(slot + 0x48, ComputeCrc32C(slot, RecordSize));
}

class JournalLock {
public:
    JournalLock() : held_(false) {
        for (;;) {
            uint32_t expected = 0;
            if (__atomic_compare_exchange_n(
                    &g_journal_lock,
                    &expected,
                    1,
                    false,
                    __ATOMIC_ACQUIRE,
                    __ATOMIC_RELAXED)) {
                held_ = true;
                return;
            }
            // Journal reads and one-slot commits are bounded critical sections.
            // Yield instead of reporting a false I/O failure when a title's
            // startup retry worker briefly overlaps a menu commit.
            km::km_svc_sleep_thread(0);
        }
    }
    ~JournalLock() {
        if (held_) {
            __atomic_store_n(&g_journal_lock, 0, __ATOMIC_RELEASE);
        }
    }
    bool held() const { return held_; }
private:
    bool held_;
};

} // namespace

namespace km {

bool LoadSettingsJournal(const GuestFilesystemApi& filesystem, const char* path,
                         SettingsFamily family, uint64_t title_id,
                         uint64_t required_presence, SettingsState* output) {
    JournalLock lock;
    if (!lock.held() || output == nullptr
        || !ReadJournal(filesystem, path, 1, g_journal_buffer)) {
        return false;
    }
    return ResolveJournal(g_journal_buffer, family, title_id, required_presence, output);
}

bool CommitSettingsJournal(const GuestFilesystemApi& filesystem, const char* path,
                           SettingsFamily family, uint64_t title_id,
                           uint64_t required_presence, const SettingsValues& values,
                           SettingsState* output) {
    JournalLock lock;
    if (!lock.held() || output == nullptr || !IsCanonical(required_presence, values)) {
        return false;
    }
    FileHandle handle{};
    if (!ReadJournal(filesystem, path, 3, g_journal_buffer, &handle)) {
        return false;
    }
    SettingsState current{};
    if (!ResolveJournal(g_journal_buffer, family, title_id, required_presence, &current)
        || !IsCanonical(current.presence, values)) {
        filesystem.close_file(handle);
        return false;
    }
    const auto target = current.active_slot == 0 ? 1 : 0;
    auto* target_bytes = g_journal_buffer + target * SlotSize;
    SerializeSlot(target_bytes, current, values);
    const WriteOption flush{1};
    const auto write_result = filesystem.write_file(
        handle, static_cast<int64_t>(target * SlotSize), target_bytes, SlotSize, flush);
    const auto flush_result = write_result == ResultSuccess
        ? filesystem.flush_file(handle) : write_result;
    filesystem.close_file(handle);
    if (write_result != ResultSuccess || flush_result != ResultSuccess
        || !ReadJournal(filesystem, path, 1, g_verify_buffer)) {
        return false;
    }
    SettingsState verified{};
    if (!ResolveJournal(g_verify_buffer, family, title_id, required_presence, &verified)
        || verified.active_slot != target || verified.generation != current.generation + 1
        || verified.presence != current.presence
        || !ValuesEqual(verified.values, values)) {
        return false;
    }
    *output = verified;
    return true;
}

uint64_t PackSettingsSnapshot(const SettingsState& state) {
    uint64_t packed = state.values.experience_share ? 1 : 0;
    if (state.values.level_cap_enabled) {
        packed |= uint64_t{1} << 1;
    }
    packed |= static_cast<uint64_t>(state.values.level_cap) << 2;
    packed |= static_cast<uint64_t>(state.values.experience_rate_basis_points) << 9;
    packed |= (state.presence & KnownPresence) << 41;
    packed |= static_cast<uint64_t>(SupportedSchema) << 44;
    return packed;
}

bool UnpackSettingsSnapshot(uint64_t packed, SettingsValues* values,
                            uint64_t* presence) {
    if (values == nullptr || packed >> 48 != 0
        || ((packed >> 44) & 0xF) != SupportedSchema) {
        return false;
    }
    const auto unpacked_presence = (packed >> 41) & KnownPresence;
    const SettingsValues unpacked{
        (packed & 1) != 0,
        static_cast<uint32_t>((packed >> 9) & UINT32_MAX),
        (packed & (uint64_t{1} << 1)) != 0,
        static_cast<uint8_t>((packed >> 2) & 0x7F),
    };
    if (!IsCanonical(unpacked_presence, unpacked)) {
        return false;
    }
    *values = unpacked;
    if (presence != nullptr) {
        *presence = unpacked_presence;
    }
    return true;
}

} // namespace km
