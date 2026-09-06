// SPDX-License-Identifier: GPL-3.0-only

use std::{ffi::c_void, mem::size_of};

#[repr(C)]
#[derive(Clone, Copy, Default)]
struct AdapterInfo {
    handle: u32,
    luid_low: u32,
    luid_high: i32,
    sources: u32,
    precise_regions: i32,
}

#[repr(C)]
struct EnumerateAdapters {
    count: u32,
    adapters: *mut AdapterInfo,
}

#[repr(C)]
struct QueryAdapter {
    handle: u32,
    kind: u32,
    data: *mut c_void,
    size: u32,
}

#[repr(C)]
struct QueryMemory {
    process: *mut c_void,
    adapter: u32,
    segment: u32,
    budget: u64,
    usage: u64,
    reservation: u64,
    available: u64,
    physical_index: u32,
}

#[link(name = "gdi32")]
extern "system" {
    fn D3DKMTEnumAdapters2(query: *mut EnumerateAdapters) -> i32;
    fn D3DKMTQueryAdapterInfo(query: *mut QueryAdapter) -> i32;
    fn D3DKMTQueryVideoMemoryInfo(query: *mut QueryMemory) -> i32;
    fn D3DKMTCloseAdapter(handle: *const u32) -> i32;
}

#[link(name = "kernel32")]
extern "system" {
    fn GetProcessMitigationPolicy(
        process: *mut c_void,
        policy: u32,
        buffer: *mut c_void,
        size: usize,
    ) -> i32;
}

fn graphics_system_calls_disabled(process: *mut c_void) -> bool {
    const PROCESS_SYSTEM_CALL_DISABLE_POLICY: u32 = 4;
    const DISALLOW_WIN32K_SYSTEM_CALLS: u32 = 1;
    let mut flags = 0u32;
    unsafe {
        GetProcessMitigationPolicy(
            process,
            PROCESS_SYSTEM_CALL_DISABLE_POLICY,
            (&mut flags as *mut u32).cast(),
            size_of::<u32>(),
        ) != 0
            && flags & DISALLOW_WIN32K_SYSTEM_CALLS != 0
    }
}

pub(super) struct Adapters {
    handles: Vec<AdapterInfo>,
    physical: Vec<(u32, u32)>,
}

impl Drop for Adapters {
    fn drop(&mut self) {
        for adapter in &self.handles {
            if adapter.handle != 0 {
                unsafe {
                    D3DKMTCloseAdapter(&adapter.handle);
                }
            }
        }
    }
}

impl Adapters {
    pub(super) fn open() -> Option<Self> {
        let mut result = Self {
            handles: vec![AdapterInfo::default(); 64],
            physical: Vec::new(),
        };
        let mut query = EnumerateAdapters {
            count: result.handles.len() as u32,
            adapters: result.handles.as_mut_ptr(),
        };
        if unsafe { D3DKMTEnumAdapters2(&mut query) } < 0
            || query.count as usize > result.handles.len()
        {
            return None;
        }
        for adapter in &result.handles[..query.count as usize] {
            let query_value = |kind| {
                let mut value = 0u32;
                let mut query = QueryAdapter {
                    handle: adapter.handle,
                    kind,
                    data: (&mut value as *mut u32).cast(),
                    size: size_of::<u32>() as u32,
                };
                (unsafe { D3DKMTQueryAdapterInfo(&mut query) } >= 0).then_some(value)
            };
            let flags = query_value(15)?;
            // Software and display-only adapters do not own hardware-local GPU memory.
            if flags & 4 != 0 || flags & 1 == 0 {
                continue;
            }
            let count = query_value(30)?;
            if count == 0 || count > 16 {
                return None;
            }
            for physical in 0..count {
                result.physical.push((adapter.handle, physical));
            }
        }
        (!result.physical.is_empty()).then_some(result)
    }

    // The caller holds already verified app-tree process handles for the entire sample.
    // Query only each process's local usage, never adapter-wide usage or its budget.
    pub(super) fn usage(&self, process: *mut c_void) -> Option<u64> {
        let mut total = 0u64;
        for &(adapter, physical_index) in &self.physical {
            let mut query = QueryMemory {
                process,
                adapter,
                physical_index,
                segment: 0,
                budget: 0,
                usage: 0,
                reservation: 0,
                available: 0,
            };
            let status = unsafe { D3DKMTQueryVideoMemoryInfo(&mut query) };
            if status < 0 {
                const STATUS_INVALID_PARAMETER: i32 = 0xc000000du32 as i32;
                // Win32k-locked processes cannot own a graphics device. Windows returns
                // INVALID_PARAMETER for their absent graphics accounting context; the
                // separate GPU process owns their graphics allocations instead.
                if status == STATUS_INVALID_PARAMETER && graphics_system_calls_disabled(process) {
                    continue;
                }
                return None;
            }
            total = total.checked_add(query.usage)?;
        }
        Some(total)
    }
}
