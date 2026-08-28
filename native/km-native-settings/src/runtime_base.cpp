// SPDX-License-Identifier: GPL-3.0-only

#include "km_runtime.hpp"

namespace {

using km::Handle;
using km::MemoryInfo;
using km::ModuleRange;
using km::Result;

constexpr uint32_t MemoryTypeMask = 0xFF;
constexpr uint32_t InfoAliasRegionAddress = 2;
constexpr uint32_t InfoAliasRegionSize = 3;
constexpr uint32_t InfoMesosphereCurrentProcess = 65001;
constexpr uint64_t InfiniteTimeout = ~uint64_t{0};
constexpr size_t MaximumDynamicEntries = 4096;
constexpr size_t MaximumDynamicSymbols = 1'000'000;
constexpr size_t MaximumPatchBytes = 0x10000;
constexpr size_t MaximumTransactionPatches = 64;

constexpr int64_t DynamicNull = 0;
constexpr int64_t DynamicHash = 4;
constexpr int64_t DynamicStringTable = 5;
constexpr int64_t DynamicSymbolTable = 6;
constexpr int64_t DynamicRela = 7;
constexpr int64_t DynamicRelaSize = 8;
constexpr int64_t DynamicRelaEntry = 9;
constexpr int64_t DynamicStringSize = 10;
constexpr uint32_t Aarch64RelativeRelocation = 1027;

struct DynamicEntry {
    int64_t tag;
    uint64_t value;
};

struct RelaEntry {
    uint64_t offset;
    uint64_t info;
    int64_t addend;
};

struct DynamicSymbol {
    uint32_t name;
    uint8_t info;
    uint8_t other;
    uint16_t section;
    uint64_t value;
    uint64_t size;
};
static_assert(sizeof(DynamicSymbol) == 24);

struct DynamicView {
    const uint32_t* hash;
    const char* strings;
    const DynamicSymbol* symbols;
    size_t string_size;
};

alignas(0x1000) uint8_t g_process_handle_stack[0x1000];
volatile Handle g_received_process_handle;
volatile Handle g_current_process_handle;
volatile uint32_t g_current_process_handle_state;
volatile uint32_t g_patch_transaction_lock;
volatile uint32_t g_executable_patching_faulted;

extern "C" {
__attribute__((section(".bss"))) uint8_t __nx_module_runtime[0xD0];
}

constexpr uintptr_t AlignDown(uintptr_t value, size_t alignment) {
    return value & ~(static_cast<uintptr_t>(alignment) - 1);
}

constexpr uintptr_t AlignUp(uintptr_t value, size_t alignment) {
    return (value + alignment - 1) & ~(static_cast<uintptr_t>(alignment) - 1);
}

bool AddWithoutOverflow(uintptr_t left, size_t right, uintptr_t* output) {
    const auto result = left + right;
    if (result < left) {
        return false;
    }
    *output = result;
    return true;
}

bool IsInsideRange(uintptr_t start, size_t length,
                   uintptr_t address, size_t size) {
    uintptr_t range_end = 0;
    uintptr_t address_end = 0;
    return AddWithoutOverflow(start, length, &range_end)
        && AddWithoutOverflow(address, size, &address_end)
        && address >= start
        && address_end <= range_end;
}

bool IsInsideModule(const ModuleRange& module, uintptr_t address, size_t size) {
    // NSO segments can have unmapped alignment gaps. Treating the entire span
    // from .text through .data as readable would let a malformed MOD0 point
    // export parsing into an unmapped hole and fault before profile rejection.
    return IsInsideRange(module.base, module.text_size, address, size)
        || IsInsideRange(module.ro_base, module.ro_size, address, size)
        || IsInsideRange(module.data_base, module.data_size, address, size);
}

bool ReadDynamicView(const ModuleRange& module, DynamicView* output) {
    if (module.text_size < 8) {
        return false;
    }

    const auto mod0_relative = *reinterpret_cast<const int32_t*>(module.base + 4);
    const auto mod0 = static_cast<uintptr_t>(
        static_cast<intptr_t>(module.base) + static_cast<intptr_t>(mod0_relative));
    if (!IsInsideModule(module, mod0, 8)
        || *reinterpret_cast<const uint32_t*>(mod0) != 0x30444F4D) {
        return false;
    }

    const auto dynamic_relative = *reinterpret_cast<const int32_t*>(mod0 + 4);
    const auto dynamic_address = static_cast<uintptr_t>(
        static_cast<intptr_t>(mod0) + static_cast<intptr_t>(dynamic_relative));
    if (!IsInsideModule(module, dynamic_address, sizeof(DynamicEntry))) {
        return false;
    }

    DynamicView view{};
    const auto dynamic = reinterpret_cast<const DynamicEntry*>(dynamic_address);
    bool terminated = false;
    for (size_t index = 0; index < MaximumDynamicEntries; ++index) {
        const auto entry_address = dynamic_address + index * sizeof(DynamicEntry);
        if (!IsInsideModule(module, entry_address, sizeof(DynamicEntry))) {
            return false;
        }
        const auto& entry = dynamic[index];
        if (entry.tag == DynamicNull) {
            terminated = true;
            break;
        }
        const auto value_address = module.base + entry.value;
        switch (entry.tag) {
        case DynamicHash:
            view.hash = reinterpret_cast<const uint32_t*>(value_address);
            break;
        case DynamicStringTable:
            view.strings = reinterpret_cast<const char*>(value_address);
            break;
        case DynamicSymbolTable:
            view.symbols = reinterpret_cast<const DynamicSymbol*>(value_address);
            break;
        case DynamicStringSize:
            view.string_size = static_cast<size_t>(entry.value);
            break;
        default:
            break;
        }
    }

    if (!terminated || view.hash == nullptr || view.strings == nullptr
        || view.symbols == nullptr || view.string_size == 0
        || !IsInsideModule(module, reinterpret_cast<uintptr_t>(view.hash), 8)
        || !IsInsideModule(module, reinterpret_cast<uintptr_t>(view.strings), view.string_size)) {
        return false;
    }

    const auto symbol_count = view.hash[1];
    if (symbol_count == 0 || symbol_count > MaximumDynamicSymbols
        || !IsInsideModule(module, reinterpret_cast<uintptr_t>(view.symbols),
                          static_cast<size_t>(symbol_count) * sizeof(DynamicSymbol))) {
        return false;
    }

    *output = view;
    return true;
}

void* FindSymbol(const ModuleRange& module, const char* requested) {
    DynamicView view{};
    if (!ReadDynamicView(module, &view)) {
        return nullptr;
    }

    const auto symbol_count = view.hash[1];
    for (uint32_t index = 1; index < symbol_count; ++index) {
        const auto& symbol = view.symbols[index];
        if (symbol.section == 0 || symbol.name >= view.string_size
            || (symbol.info >> 4) == 0) {
            continue;
        }
        const auto maximum = view.string_size - symbol.name;
        if (!km::StringEquals(view.strings + symbol.name, requested, maximum)) {
            continue;
        }
        const auto address = module.base + symbol.value;
        if (!IsInsideModule(module, address, 1)) {
            continue;
        }
        return reinterpret_cast<void*>(address);
    }
    return nullptr;
}

extern "C" void ReceiveProcessHandle(void* argument) {
    const auto session = static_cast<Handle>(reinterpret_cast<uintptr_t>(argument));
    auto* tls = static_cast<uint32_t*>(km::km_get_thread_local_region());
    km::MemorySet(tls, 0, 0x10);
    int32_t index = -1;
    if (km::km_svc_reply_and_receive(&index, &session, 1, km::InvalidHandle,
                                 static_cast<int64_t>(InfiniteTimeout)) == km::ResultSuccess) {
        __atomic_store_n(&g_received_process_handle, tls[3], __ATOMIC_RELEASE);
    }
    km::km_svc_close_handle(session);
    km::km_svc_exit_thread();
}

Handle GetCurrentProcessHandleViaIpc() {
    Handle server = km::InvalidHandle;
    Handle client = km::InvalidHandle;
    Handle thread = km::InvalidHandle;
    __atomic_store_n(&g_received_process_handle, km::InvalidHandle, __ATOMIC_RELEASE);
    if (km::km_svc_create_session(&server, &client, false) != km::ResultSuccess) {
        return km::InvalidHandle;
    }
    if (km::km_svc_create_thread(&thread, ReceiveProcessHandle,
                             reinterpret_cast<void*>(static_cast<uintptr_t>(server)),
                             g_process_handle_stack + sizeof(g_process_handle_stack),
                             0x20, 2) != km::ResultSuccess) {
        km::km_svc_close_handle(server);
        km::km_svc_close_handle(client);
        return km::InvalidHandle;
    }
    if (km::km_svc_start_thread(thread) != km::ResultSuccess) {
        km::km_svc_close_handle(thread);
        km::km_svc_close_handle(server);
        km::km_svc_close_handle(client);
        return km::InvalidHandle;
    }

    const uint32_t request[4] = {0, 0x80000000U, 2, km::CurrentProcessPseudoHandle};
    km::MemoryCopy(km::km_get_thread_local_region(), request, sizeof(request));
    // Closing the client completes this one-way self-transfer and can make the
    // synchronous send report session closure even after the server received
    // the process handle. The receiver and transferred nonzero handle are the
    // authoritative outcome, not the client-side send result.
    (void)km::km_svc_send_sync_request(client);
    km::km_svc_close_handle(client);
    int32_t index = -1;
    const auto wait_result = km::km_svc_wait_synchronization(
        &index, &thread, 1, static_cast<int64_t>(InfiniteTimeout));
    km::km_svc_close_handle(thread);
    if (wait_result != km::ResultSuccess) {
        return km::InvalidHandle;
    }
    const auto received = __atomic_load_n(
        &g_received_process_handle, __ATOMIC_ACQUIRE);
    return received == km::InvalidHandle ? km::InvalidHandle : received;
}

bool FindAliasDestination(size_t size, uintptr_t* output) {
    uint64_t alias_address = 0;
    uint64_t alias_size = 0;
    if (km::km_svc_get_info(&alias_address, InfoAliasRegionAddress,
                        km::CurrentProcessPseudoHandle, 0) != km::ResultSuccess
        || km::km_svc_get_info(&alias_size, InfoAliasRegionSize,
                           km::CurrentProcessPseudoHandle, 0) != km::ResultSuccess
        || alias_size < size || alias_address + alias_size < alias_address) {
        return false;
    }

    const auto alias_end = alias_address + alias_size;
    auto cursor = alias_address;
    for (size_t iteration = 0; iteration < 16384 && cursor < alias_end; ++iteration) {
        MemoryInfo info{};
        uint32_t page_info = 0;
        if (km_svc_query_memory(&info, &page_info, cursor) != km::ResultSuccess
            || info.size == 0 || info.address + info.size <= cursor) {
            return false;
        }
        const auto candidate = AlignUp(
            info.address < alias_address ? alias_address : info.address,
            km::PageSize);
        uintptr_t candidate_end = 0;
        if ((info.type & MemoryTypeMask) == static_cast<uint32_t>(km::MemoryType::Unmapped)
            && AddWithoutOverflow(candidate, size, &candidate_end)
            && candidate_end <= info.address + info.size
            && candidate_end <= alias_end) {
            *output = candidate;
            return true;
        }
        cursor = info.address + info.size;
    }
    return false;
}

class PatchTransactionLock {
public:
    PatchTransactionLock()
        : held_(__atomic_exchange_n(
              &g_patch_transaction_lock, 1, __ATOMIC_ACQUIRE) == 0) {}

    ~PatchTransactionLock() {
        if (held_) {
            __atomic_store_n(&g_patch_transaction_lock, 0, __ATOMIC_RELEASE);
        }
    }

    bool held() const { return held_; }

private:
    bool held_;
};

void StoreExecutableWords(uintptr_t destination, const void* source, size_t length,
                          bool include_first_word) {
    const auto* bytes = static_cast<const uint8_t*>(source);
    const auto word_count = length / sizeof(uint32_t);
    const auto first = include_first_word ? size_t{0} : size_t{1};
    for (size_t index = word_count; index > first; --index) {
        uint32_t word = 0;
        km::MemoryCopy(&word, bytes + (index - 1) * sizeof(uint32_t), sizeof(word));
        __atomic_store_n(
            reinterpret_cast<uint32_t*>(destination) + index - 1,
            word,
            __ATOMIC_RELAXED);
    }
}

} // namespace

extern "C" void km_relocate_self(uintptr_t module_base, const void* dynamic_pointer) {
    const auto* dynamic = static_cast<const DynamicEntry*>(dynamic_pointer);
    const RelaEntry* relocations = nullptr;
    size_t relocation_size = 0;
    size_t relocation_entry_size = sizeof(RelaEntry);
    for (size_t index = 0; index < MaximumDynamicEntries; ++index) {
        const auto& entry = dynamic[index];
        if (entry.tag == DynamicNull) {
            break;
        }
        if (entry.tag == DynamicRela) {
            relocations = reinterpret_cast<const RelaEntry*>(module_base + entry.value);
        } else if (entry.tag == DynamicRelaSize) {
            relocation_size = static_cast<size_t>(entry.value);
        } else if (entry.tag == DynamicRelaEntry) {
            relocation_entry_size = static_cast<size_t>(entry.value);
        }
    }
    if (relocations == nullptr || relocation_entry_size != sizeof(RelaEntry)
        || relocation_size % sizeof(RelaEntry) != 0) {
        return;
    }
    const auto count = relocation_size / sizeof(RelaEntry);
    for (size_t index = 0; index < count; ++index) {
        const auto& relocation = relocations[index];
        if (static_cast<uint32_t>(relocation.info) == Aarch64RelativeRelocation) {
            *reinterpret_cast<uintptr_t*>(module_base + relocation.offset) =
                module_base + static_cast<uintptr_t>(relocation.addend);
        }
    }
}

namespace km {

void* MemoryCopy(void* destination, const void* source, size_t length) {
    auto* out = static_cast<uint8_t*>(destination);
    const auto* in = static_cast<const uint8_t*>(source);
    for (size_t index = 0; index < length; ++index) {
        out[index] = in[index];
    }
    return destination;
}

void* MemorySet(void* destination, int value, size_t length) {
    auto* out = static_cast<uint8_t*>(destination);
    for (size_t index = 0; index < length; ++index) {
        out[index] = static_cast<uint8_t>(value);
    }
    return destination;
}

int MemoryCompare(const void* left, const void* right, size_t length) {
    const auto* lhs = static_cast<const uint8_t*>(left);
    const auto* rhs = static_cast<const uint8_t*>(right);
    for (size_t index = 0; index < length; ++index) {
        if (lhs[index] != rhs[index]) {
            return lhs[index] < rhs[index] ? -1 : 1;
        }
    }
    return 0;
}

size_t StringLength(const char* value) {
    size_t length = 0;
    while (value[length] != '\0') {
        ++length;
    }
    return length;
}

bool StringEquals(const char* left, const char* right, size_t maximum) {
    for (size_t index = 0; index < maximum; ++index) {
        if (left[index] != right[index]) {
            return false;
        }
        if (left[index] == '\0') {
            return true;
        }
    }
    return false;
}

bool FindMappedModules(ModuleRange* output, size_t capacity, size_t* out_count) {
    if (output == nullptr || out_count == nullptr || capacity == 0) {
        return false;
    }
    *out_count = 0;
    enum class State { Text, Rodata, Data } state = State::Text;
    ModuleRange candidate{};
    uint64_t cursor = 0;
    for (size_t iteration = 0; iteration < 65536; ++iteration) {
        MemoryInfo info{};
        uint32_t page_info = 0;
        if (km_svc_query_memory(&info, &page_info, cursor) != ResultSuccess
            || info.size == 0) {
            return false;
        }
        const auto type = info.type & MemoryTypeMask;
        const auto is_text = type == static_cast<uint32_t>(MemoryType::CodeStatic)
            && info.permissions == (PermissionRead | PermissionExecute);
        const auto is_rodata = type == static_cast<uint32_t>(MemoryType::CodeStatic)
            && info.permissions == PermissionRead;
        const auto is_data = type == static_cast<uint32_t>(MemoryType::CodeMutable)
            && info.permissions == (PermissionRead | PermissionWrite);
        switch (state) {
        case State::Text:
            if (is_text) {
                candidate = ModuleRange{static_cast<uintptr_t>(info.address),
                                        static_cast<size_t>(info.size), 0, 0, 0, 0};
                state = State::Rodata;
            }
            break;
        case State::Rodata:
            if (is_rodata) {
                candidate.ro_base = static_cast<uintptr_t>(info.address);
                candidate.ro_size = static_cast<size_t>(info.size);
                state = State::Data;
            } else {
                state = State::Text;
                if (is_text) {
                    candidate = ModuleRange{static_cast<uintptr_t>(info.address),
                                            static_cast<size_t>(info.size), 0, 0, 0, 0};
                    state = State::Rodata;
                }
            }
            break;
        case State::Data:
            if (is_data) {
                candidate.data_base = static_cast<uintptr_t>(info.address);
                candidate.data_size = static_cast<size_t>(info.size);
                if (*out_count == capacity) {
                    return false;
                }
                output[(*out_count)++] = candidate;
            }
            state = State::Text;
            if (!is_data && is_text) {
                candidate = ModuleRange{static_cast<uintptr_t>(info.address),
                                        static_cast<size_t>(info.size), 0, 0, 0, 0};
                state = State::Rodata;
            }
            break;
        }

        const auto next = info.address + info.size;
        if (next <= cursor) {
            return *out_count != 0;
        }
        cursor = next;
    }
    return false;
}

void* ResolveGuestExport(const ModuleRange* modules, size_t module_count,
                         const char* symbol_name) {
    if (modules == nullptr || symbol_name == nullptr) {
        return nullptr;
    }
    for (size_t index = 0; index < module_count; ++index) {
        if (auto* address = FindSymbol(modules[index], symbol_name)) {
            return address;
        }
    }
    return nullptr;
}

bool ResolveGuestFilesystem(const ModuleRange* modules, size_t module_count,
                            GuestFilesystemApi* output) {
    if (output == nullptr) {
        return false;
    }
    GuestFilesystemApi api{};
    api.mount_sd = reinterpret_cast<decltype(api.mount_sd)>(ResolveGuestExport(
        modules, module_count, "_ZN2nn2fs19MountSdCardForDebugEPKc"));
    api.open_file = reinterpret_cast<decltype(api.open_file)>(ResolveGuestExport(
        modules, module_count, "_ZN2nn2fs8OpenFileEPNS0_10FileHandleEPKci"));
    api.read_file = reinterpret_cast<decltype(api.read_file)>(ResolveGuestExport(
        modules, module_count, "_ZN2nn2fs8ReadFileEPmNS0_10FileHandleElPvm"));
    api.write_file = reinterpret_cast<decltype(api.write_file)>(ResolveGuestExport(
        modules, module_count,
        "_ZN2nn2fs9WriteFileENS0_10FileHandleElPKvmRKNS0_11WriteOptionE"));
    api.get_file_size = reinterpret_cast<decltype(api.get_file_size)>(ResolveGuestExport(
        modules, module_count, "_ZN2nn2fs11GetFileSizeEPlNS0_10FileHandleE"));
    api.flush_file = reinterpret_cast<decltype(api.flush_file)>(ResolveGuestExport(
        modules, module_count, "_ZN2nn2fs9FlushFileENS0_10FileHandleE"));
    api.close_file = reinterpret_cast<decltype(api.close_file)>(ResolveGuestExport(
        modules, module_count, "_ZN2nn2fs9CloseFileENS0_10FileHandleE"));
    if (api.mount_sd == nullptr || api.open_file == nullptr || api.read_file == nullptr
        || api.write_file == nullptr || api.get_file_size == nullptr
        || api.flush_file == nullptr || api.close_file == nullptr) {
        return false;
    }
    *output = api;
    return true;
}

Handle GetCurrentProcessHandle() {
    for (;;) {
        const auto state = __atomic_load_n(
            &g_current_process_handle_state, __ATOMIC_ACQUIRE);
        if (state == 2) {
            return __atomic_load_n(&g_current_process_handle, __ATOMIC_ACQUIRE);
        }
        if (state == 1) {
            km_svc_sleep_thread(0);
            continue;
        }

        uint32_t expected = 0;
        if (!__atomic_compare_exchange_n(
                &g_current_process_handle_state,
                &expected,
                1,
                false,
                __ATOMIC_ACQ_REL,
                __ATOMIC_ACQUIRE)) {
            continue;
        }

        Handle resolved = InvalidHandle;
        uint64_t value = 0;
        if (km_svc_get_info(&value, InfoMesosphereCurrentProcess, InvalidHandle, 0)
                == ResultSuccess
            && value != 0 && value <= UINT32_MAX) {
            resolved = static_cast<Handle>(value);
        } else {
            resolved = GetCurrentProcessHandleViaIpc();
        }

        if (resolved == InvalidHandle) {
            // Handle acquisition can fail transiently while the process is still
            // starting. Permit a later transaction to retry; every auxiliary
            // handle created by the failed IPC path has already been closed.
            __atomic_store_n(&g_current_process_handle_state, 0, __ATOMIC_RELEASE);
            return InvalidHandle;
        }
        __atomic_store_n(&g_current_process_handle, resolved, __ATOMIC_RELEASE);
        __atomic_store_n(&g_current_process_handle_state, 2, __ATOMIC_RELEASE);
        return resolved;
    }
}

bool ReadExecutableBytes(uintptr_t address, void* destination, size_t length) {
    if (address == 0 || destination == nullptr || length == 0
        || length > MaximumPatchBytes) {
        return false;
    }
    MemoryCopy(destination, reinterpret_cast<const void*>(address), length);
    return true;
}

bool PatchExecutableTransaction(const ExecutablePatch* patches, size_t count) {
    return PatchExecutableTransactionDetailed(patches, count)
        == ExecutablePatchResult::Committed;
}

ExecutablePatchResult PatchExecutableTransactionDetailed(
    const ExecutablePatch* patches, size_t count) {
    struct Mapping {
        uintptr_t source;
        uintptr_t alias;
        size_t length;
    };
    struct PageRange {
        uintptr_t start;
        uintptr_t end;
    };
    Mapping mappings[MaximumTransactionPatches]{};
    PageRange ranges[MaximumTransactionPatches]{};
    size_t patch_mapping[MaximumTransactionPatches]{};
    size_t patch_offset[MaximumTransactionPatches]{};

    PatchTransactionLock transaction_lock;
    if (!transaction_lock.held()) {
        return ExecutablePatchResult::Rejected;
    }
    if (__atomic_load_n(&g_executable_patching_faulted, __ATOMIC_ACQUIRE) != 0) {
        return ExecutablePatchResult::RecoveryRequired;
    }
    if (patches == nullptr || count == 0 || count > MaximumTransactionPatches) {
        return ExecutablePatchResult::Rejected;
    }
    for (size_t index = 0; index < count; ++index) {
        const auto& patch = patches[index];
        uintptr_t patch_end = 0;
        if (patch.address == 0 || patch.expected == nullptr || patch.replacement == nullptr
            || patch.length == 0 || patch.length > MaximumPatchBytes
            || (patch.address & (sizeof(uint32_t) - 1)) != 0
            || (patch.length & (sizeof(uint32_t) - 1)) != 0
            || !AddWithoutOverflow(patch.address, patch.length, &patch_end)
            || MemoryCompare(reinterpret_cast<const void*>(patch.address),
                             patch.expected, patch.length) != 0) {
            return ExecutablePatchResult::Rejected;
        }
        for (size_t previous = 0; previous < index; ++previous) {
            uintptr_t previous_end = 0;
            if (!AddWithoutOverflow(patches[previous].address,
                                    patches[previous].length, &previous_end)
                || (patch.address < previous_end
                    && patches[previous].address < patch_end)) {
                return ExecutablePatchResult::Rejected;
            }
        }

        if (patch_end > UINTPTR_MAX - (PageSize - 1)) {
            return ExecutablePatchResult::Rejected;
        }
        ranges[index] = PageRange{
            AlignDown(patch.address, PageSize),
            AlignUp(patch_end, PageSize),
        };
    }

    // Sort and merge page ranges before mapping them. Multiple patch sites in
    // one code page must share one writable alias; duplicate aliases to the same
    // physical page introduce avoidable cache-alias and cleanup ambiguity.
    for (size_t index = 1; index < count; ++index) {
        const auto value = ranges[index];
        auto insertion = index;
        while (insertion != 0 && ranges[insertion - 1].start > value.start) {
            ranges[insertion] = ranges[insertion - 1];
            --insertion;
        }
        ranges[insertion] = value;
    }
    size_t mapping_count = 0;
    for (size_t index = 0; index < count; ++index) {
        if (mapping_count != 0
            && ranges[index].start <= mappings[mapping_count - 1].source
                    + mappings[mapping_count - 1].length) {
            const auto current_end = mappings[mapping_count - 1].source
                + mappings[mapping_count - 1].length;
            if (ranges[index].end > current_end) {
                mappings[mapping_count - 1].length = static_cast<size_t>(
                    ranges[index].end - mappings[mapping_count - 1].source);
            }
            continue;
        }
        mappings[mapping_count++] = Mapping{
            ranges[index].start,
            0,
            static_cast<size_t>(ranges[index].end - ranges[index].start),
        };
    }

    const auto process = GetCurrentProcessHandle();
    if (process == InvalidHandle) {
        return ExecutablePatchResult::Rejected;
    }

    size_t mapped = 0;
    for (size_t index = 0; index < mapping_count; ++index) {
        uintptr_t alias = 0;
        if (!FindAliasDestination(mappings[index].length, &alias)
            || km_svc_map_process_memory(reinterpret_cast<void*>(alias), process,
                                         mappings[index].source,
                                         mappings[index].length) != ResultSuccess) {
            bool cleaned = true;
            for (size_t rollback = 0; rollback < mapped; ++rollback) {
                if (km_svc_unmap_process_memory(
                        reinterpret_cast<void*>(mappings[rollback].alias), process,
                        mappings[rollback].source, mappings[rollback].length)
                    != ResultSuccess) {
                    cleaned = false;
                }
            }
            if (!cleaned) {
                __atomic_store_n(
                    &g_executable_patching_faulted, 1, __ATOMIC_RELEASE);
                return ExecutablePatchResult::RecoveryRequired;
            }
            return ExecutablePatchResult::Rejected;
        }
        mappings[index].alias = alias;
        ++mapped;
    }

    for (size_t index = 0; index < count; ++index) {
        uintptr_t patch_end = 0;
        AddWithoutOverflow(patches[index].address, patches[index].length, &patch_end);
        bool found = false;
        for (size_t mapping = 0; mapping < mapping_count; ++mapping) {
            const auto mapping_end = mappings[mapping].source + mappings[mapping].length;
            if (patches[index].address >= mappings[mapping].source
                && patch_end <= mapping_end) {
                patch_mapping[index] = mapping;
                patch_offset[index] = static_cast<size_t>(
                    patches[index].address - mappings[mapping].source);
                found = true;
                break;
            }
        }
        if (!found) {
            bool cleaned = true;
            for (size_t cleanup = 0; cleanup < mapped; ++cleanup) {
                if (km_svc_unmap_process_memory(
                        reinterpret_cast<void*>(mappings[cleanup].alias), process,
                        mappings[cleanup].source, mappings[cleanup].length)
                    != ResultSuccess) {
                    cleaned = false;
                }
            }
            __atomic_store_n(&g_executable_patching_faulted, 1, __ATOMIC_RELEASE);
            (void)cleaned;
            return ExecutablePatchResult::RecoveryRequired;
        }
    }

    // Revalidate after every writable alias exists. No executable byte has been
    // changed yet, so any concurrent or unexpected mutation still fails closed.
    for (size_t index = 0; index < count; ++index) {
        const auto& patch = patches[index];
        const auto* alias_bytes = reinterpret_cast<const void*>(
            mappings[patch_mapping[index]].alias + patch_offset[index]);
        if (MemoryCompare(reinterpret_cast<const void*>(patch.address),
                          patch.expected, patch.length) != 0
            || MemoryCompare(alias_bytes, patch.expected, patch.length) != 0) {
            bool cleaned = true;
            for (size_t cleanup = 0; cleanup < mapped; ++cleanup) {
                if (km_svc_unmap_process_memory(
                        reinterpret_cast<void*>(mappings[cleanup].alias), process,
                        mappings[cleanup].source, mappings[cleanup].length)
                    != ResultSuccess) {
                    cleaned = false;
                }
            }
            if (!cleaned) {
                __atomic_store_n(
                    &g_executable_patching_faulted, 1, __ATOMIC_RELEASE);
                return ExecutablePatchResult::RecoveryRequired;
            }
            return ExecutablePatchResult::Rejected;
        }
    }

    // Every instruction is published with one aligned 32-bit store. For a
    // multi-instruction entry hook, populate its continuation words first and
    // publish its entry word last. Cache maintenance is deferred until every
    // patch has been written, so no byte-at-a-time instruction stream is ever
    // made executable.
    for (size_t index = 0; index < count; ++index) {
        const auto& patch = patches[index];
        const auto writable = mappings[patch_mapping[index]].alias + patch_offset[index];
        StoreExecutableWords(writable, patch.replacement, patch.length, false);
    }
    for (size_t index = 0; index < count; ++index) {
        const auto& patch = patches[index];
        const auto writable = mappings[patch_mapping[index]].alias + patch_offset[index];
        StoreExecutableWords(writable, patch.replacement, sizeof(uint32_t), true);
    }
    for (size_t index = 0; index < count; ++index) {
        const auto writable = mappings[patch_mapping[index]].alias + patch_offset[index];
        km_flush_data_cache(reinterpret_cast<void*>(writable), patches[index].length);
    }
    for (size_t index = 0; index < count; ++index) {
        km_invalidate_instruction_cache(
            reinterpret_cast<void*>(patches[index].address), patches[index].length);
    }

    bool verified = true;
    for (size_t index = 0; index < count; ++index) {
        if (MemoryCompare(reinterpret_cast<const void*>(patches[index].address),
                          patches[index].replacement, patches[index].length) != 0) {
            verified = false;
            break;
        }
    }
    if (!verified) {
        for (size_t index = 0; index < count; ++index) {
            const auto writable = mappings[patch_mapping[index]].alias + patch_offset[index];
            StoreExecutableWords(writable, patches[index].expected,
                                 patches[index].length, false);
        }
        // Withdraw entry words in reverse dependency order. Callers publish
        // downstream targets before source hooks, so rollback must restore the
        // source hooks first and cannot expose a source that still branches to
        // a target whose original bytes have already been restored.
        for (size_t reverse = count; reverse != 0; --reverse) {
            const auto index = reverse - 1;
            const auto writable = mappings[patch_mapping[index]].alias + patch_offset[index];
            StoreExecutableWords(writable, patches[index].expected,
                                 sizeof(uint32_t), true);
        }
        for (size_t index = 0; index < count; ++index) {
            const auto writable = mappings[patch_mapping[index]].alias + patch_offset[index];
            km_flush_data_cache(
                reinterpret_cast<void*>(writable), patches[index].length);
        }
        for (size_t index = 0; index < count; ++index) {
            km_invalidate_instruction_cache(
                reinterpret_cast<void*>(patches[index].address), patches[index].length);
        }
    }

    bool restored = verified;
    if (!verified) {
        restored = true;
        for (size_t index = 0; index < count; ++index) {
            const auto alias_bytes = reinterpret_cast<const void*>(
                mappings[patch_mapping[index]].alias + patch_offset[index]);
            if (MemoryCompare(reinterpret_cast<const void*>(patches[index].address),
                              patches[index].expected, patches[index].length) != 0
                || MemoryCompare(alias_bytes, patches[index].expected,
                                 patches[index].length) != 0) {
                restored = false;
                break;
            }
        }
    }

    bool unmapped = true;
    for (size_t index = 0; index < mapped; ++index) {
        if (km_svc_unmap_process_memory(reinterpret_cast<void*>(mappings[index].alias),
                                        process, mappings[index].source,
                                        mappings[index].length) != ResultSuccess) {
            unmapped = false;
        }
    }
    if (verified) {
        if (!unmapped) {
            // The executable bytes are already committed and verified. Do not
            // report a false rejection that could make callers apply them twice;
            // quarantine the patcher so the leaked alias cannot compound.
            __atomic_store_n(&g_executable_patching_faulted, 1, __ATOMIC_RELEASE);
        }
        return ExecutablePatchResult::Committed;
    }
    if (!restored || !unmapped) {
        __atomic_store_n(&g_executable_patching_faulted, 1, __ATOMIC_RELEASE);
        return ExecutablePatchResult::RecoveryRequired;
    }
    return ExecutablePatchResult::Rejected;
}

bool IsExecutablePatchingFaulted() {
    return __atomic_load_n(&g_executable_patching_faulted, __ATOMIC_ACQUIRE) != 0;
}

} // namespace km

extern "C" void* memcpy(void* destination, const void* source, size_t length) {
    return km::MemoryCopy(destination, source, length);
}

extern "C" void* memset(void* destination, int value, size_t length) {
    return km::MemorySet(destination, value, length);
}

extern "C" int memcmp(const void* left, const void* right, size_t length) {
    return km::MemoryCompare(left, right, length);
}

extern "C" size_t strlen(const char* value) {
    return km::StringLength(value);
}
