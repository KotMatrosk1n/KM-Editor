// SPDX-License-Identifier: GPL-3.0-only
#pragma once

#include <stddef.h>
#include <stdint.h>

namespace km {

using Result = uint32_t;
using Handle = uint32_t;

constexpr Result ResultSuccess = 0;
constexpr Handle InvalidHandle = 0;
constexpr Handle CurrentProcessPseudoHandle = 0xFFFF8001U;
constexpr size_t PageSize = 0x1000;

struct MemoryInfo {
    uint64_t address;
    uint64_t size;
    uint32_t type;
    uint32_t attributes;
    uint32_t permissions;
    uint32_t device_refcount;
    uint32_t ipc_refcount;
    uint32_t padding;
};
static_assert(sizeof(MemoryInfo) == 0x28);

enum class MemoryType : uint32_t {
    Unmapped = 0,
    CodeStatic = 3,
    CodeMutable = 4,
};

enum MemoryPermission : uint32_t {
    PermissionRead = 1,
    PermissionWrite = 2,
    PermissionExecute = 4,
};

struct ModuleRange {
    uintptr_t base;
    size_t text_size;
    uintptr_t ro_base;
    size_t ro_size;
    uintptr_t data_base;
    size_t data_size;
};

struct FileHandle {
    uint64_t value;
};

struct WriteOption {
    int32_t flags;
};

struct GuestFilesystemApi {
    Result (*mount_sd)(const char* mount_name);
    Result (*open_file)(FileHandle* out, const char* path, int32_t mode);
    Result (*read_file)(uint64_t* out_read, FileHandle handle, int64_t offset,
                        void* destination, uint64_t size);
    Result (*write_file)(FileHandle handle, int64_t offset, const void* source,
                         uint64_t size, const WriteOption& option);
    Result (*get_file_size)(int64_t* out_size, FileHandle handle);
    Result (*flush_file)(FileHandle handle);
    void (*close_file)(FileHandle handle);
};

struct ExecutablePatch {
    uintptr_t address;
    const void* expected;
    const void* replacement;
    size_t length;
};

enum class ExecutablePatchResult : uint32_t {
    Rejected = 0,
    Committed = 1,
    RecoveryRequired = 2,
};

extern "C" Result km_svc_query_memory(MemoryInfo* out_info, uint32_t* out_page_info,
                                       uint64_t address);
extern "C" Result km_svc_get_info(uint64_t* out, uint32_t type, Handle handle,
                                  uint64_t sub_id);
extern "C" Result km_svc_map_process_memory(void* destination, Handle process,
                                             uint64_t source, uint64_t size);
extern "C" Result km_svc_unmap_process_memory(void* destination, Handle process,
                                               uint64_t source, uint64_t size);
extern "C" Result km_svc_create_thread(Handle* out_thread, void (*entry)(void*),
                                        void* argument, void* stack_top,
                                        int32_t priority, int32_t processor_id);
extern "C" Result km_svc_start_thread(Handle thread);
extern "C" [[noreturn]] void km_svc_exit_thread();
extern "C" Result km_svc_sleep_thread(int64_t nanoseconds);
extern "C" Result km_svc_close_handle(Handle handle);
extern "C" Result km_svc_wait_synchronization(int32_t* out_index,
                                               const Handle* handles,
                                               int32_t handle_count,
                                               int64_t timeout);
extern "C" Result km_svc_send_sync_request(Handle session);
extern "C" Result km_svc_create_session(Handle* out_server, Handle* out_client,
                                         bool light);
extern "C" Result km_svc_reply_and_receive(int32_t* out_index,
                                            const Handle* handles,
                                            int32_t handle_count,
                                            Handle reply_target,
                                            int64_t timeout);
extern "C" void* km_get_thread_local_region();
extern "C" void km_flush_data_cache(void* address, uint64_t size);
extern "C" void km_invalidate_instruction_cache(void* address, uint64_t size);

void* MemoryCopy(void* destination, const void* source, size_t length);
void* MemorySet(void* destination, int value, size_t length);
int MemoryCompare(const void* left, const void* right, size_t length);
size_t StringLength(const char* value);
bool StringEquals(const char* left, const char* right, size_t maximum);

bool FindMappedModules(ModuleRange* output, size_t capacity, size_t* out_count);
void* ResolveGuestExport(const ModuleRange* modules, size_t module_count,
                         const char* symbol_name);
bool ResolveGuestFilesystem(const ModuleRange* modules, size_t module_count,
                            GuestFilesystemApi* output);

Handle GetCurrentProcessHandle();
bool PatchExecutableTransaction(const ExecutablePatch* patches, size_t count);
ExecutablePatchResult PatchExecutableTransactionDetailed(
    const ExecutablePatch* patches, size_t count);
bool IsExecutablePatchingFaulted();
bool ReadExecutableBytes(uintptr_t address, void* destination, size_t length);

} // namespace km

extern "C" void km_runtime_start(void* loader_context, uint64_t main_thread_handle);
extern "C" void km_relocate_self(uintptr_t module_base, const void* dynamic);
