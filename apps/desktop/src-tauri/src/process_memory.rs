// SPDX-License-Identifier: GPL-3.0-only

use serde::Serialize;

#[derive(Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MemoryGroup {
    process_count: u32,
    unreadable_count: u32,
    private_ram_bytes: Option<u64>,
    committed_bytes: Option<u64>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MemorySnapshot {
    desktop: MemoryGroup,
    workers: MemoryGroup,
    web_view: MemoryGroup,
    total: MemoryGroup,
}

impl MemoryGroup {
    fn empty() -> Self {
        Self {
            private_ram_bytes: Some(0),
            committed_bytes: Some(0),
            ..Self::default()
        }
    }

    fn add(&mut self, ram: Option<u64>, committed: Option<u64>) {
        self.process_count += 1;
        self.unreadable_count += u32::from(committed.is_none());
        self.private_ram_bytes = self
            .private_ram_bytes
            .zip(ram)
            .and_then(|(a, b)| a.checked_add(b));
        self.committed_bytes = self
            .committed_bytes
            .zip(committed)
            .and_then(|(a, b)| a.checked_add(b));
    }
}

#[cfg(not(windows))]
pub fn collect() -> Result<MemorySnapshot, String> {
    Err("unsupported".into())
}

#[cfg(windows)]
pub fn collect() -> Result<MemorySnapshot, String> {
    windows::collect()
}

#[cfg(windows)]
mod windows {
    use super::{MemoryGroup, MemorySnapshot};
    use std::{collections::HashMap, ffi::c_void, mem::size_of};

    const MAX_PROCESSES: usize = 32_768;
    const MAX_OWNED_PROCESSES: usize = 256;
    const MAX_DEPTH: usize = 16;

    #[derive(Clone, Copy, PartialEq)]
    enum Role {
        Desktop,
        Worker,
        WebView,
    }

    struct Entry {
        pid: u32,
        parent: u32,
        name: String,
    }
    struct Owned {
        handle: Handle,
        created: u64,
        role: Role,
    }
    struct Handle(*mut c_void);
    impl Drop for Handle {
        fn drop(&mut self) {
            unsafe {
                CloseHandle(self.0);
            }
        }
    }

    #[repr(C)]
    struct ProcessEntry {
        size: u32,
        usage: u32,
        pid: u32,
        heap: usize,
        module: u32,
        threads: u32,
        parent: u32,
        priority: i32,
        flags: u32,
        name: [u16; 260],
    }
    #[repr(C)]
    #[derive(Default)]
    struct FileTime {
        low: u32,
        high: u32,
    }
    #[repr(C)]
    #[derive(Default)]
    struct Counters {
        size: u32,
        faults: u32,
        peak_working: usize,
        working: usize,
        peak_paged: usize,
        paged: usize,
        peak_nonpaged: usize,
        nonpaged: usize,
        pagefile: usize,
        peak_pagefile: usize,
        private_usage: usize,
        private_working: usize,
        shared_commit: u64,
    }

    #[link(name = "kernel32")]
    extern "system" {
        fn CreateToolhelp32Snapshot(flags: u32, pid: u32) -> *mut c_void;
        fn Process32FirstW(snapshot: *mut c_void, entry: *mut ProcessEntry) -> i32;
        fn Process32NextW(snapshot: *mut c_void, entry: *mut ProcessEntry) -> i32;
        fn OpenProcess(access: u32, inherit: i32, pid: u32) -> *mut c_void;
        fn CloseHandle(handle: *mut c_void) -> i32;
        fn GetLastError() -> u32;
        fn GetProcessTimes(
            handle: *mut c_void,
            created: *mut FileTime,
            exited: *mut FileTime,
            kernel: *mut FileTime,
            user: *mut FileTime,
        ) -> i32;
        fn K32GetProcessMemoryInfo(handle: *mut c_void, counters: *mut Counters, size: u32) -> i32;
    }

    fn open(pid: u32) -> Option<(Handle, u64)> {
        // Query-only access; no process memory reads, writes or lifecycle operations.
        let handle = unsafe { OpenProcess(0x1000, 0, pid) };
        if handle.is_null() {
            return None;
        }
        let handle = Handle(handle);
        let (mut created, mut exited, mut kernel, mut user) = (
            FileTime::default(),
            FileTime::default(),
            FileTime::default(),
            FileTime::default(),
        );
        if unsafe { GetProcessTimes(handle.0, &mut created, &mut exited, &mut kernel, &mut user) }
            == 0
        {
            return None;
        }
        Some((
            handle,
            (u64::from(created.high) << 32) | u64::from(created.low),
        ))
    }

    fn role(parent: Role, name: &str) -> Option<Role> {
        if (parent == Role::Desktop || parent == Role::WebView) && name == "msedgewebview2.exe" {
            return Some(Role::WebView);
        }
        if parent == Role::Desktop
            && (name == "km-tools-bridge.exe"
                || name == "km.tools.exe"
                || name == "dotnet.exe"
                || (name.starts_with("km-tools-bridge-") && name.ends_with(".exe")))
        {
            return Some(Role::Worker);
        }
        None
    }

    fn memory(handle: &Handle) -> (Option<u64>, Option<u64>) {
        let mut counters = Counters {
            size: size_of::<Counters>() as u32,
            private_working: usize::MAX,
            ..Counters::default()
        };
        let result = unsafe {
            K32GetProcessMemoryInfo(handle.0, &mut counters, size_of::<Counters>() as u32)
        };
        if result != 0 {
            let ram =
                (counters.private_working != usize::MAX).then_some(counters.private_working as u64);
            return (ram, Some(counters.private_usage as u64));
        }
        // Older Windows supports committed memory but not the EX2 private-RAM counter.
        let legacy_size = std::mem::offset_of!(Counters, private_working) as u32;
        counters.size = legacy_size;
        if unsafe { K32GetProcessMemoryInfo(handle.0, &mut counters, legacy_size) } != 0 {
            (None, Some(counters.private_usage as u64))
        } else {
            (None, None)
        }
    }

    pub(super) fn collect() -> Result<MemorySnapshot, String> {
        let snapshot = unsafe { CreateToolhelp32Snapshot(0x00000002, 0) };
        if snapshot == -1isize as *mut c_void {
            return Err("unavailable".into());
        }
        let snapshot = Handle(snapshot);
        let mut entry: ProcessEntry = unsafe { std::mem::zeroed() };
        entry.size = size_of::<ProcessEntry>() as u32;
        let mut entries = Vec::new();
        let mut found = unsafe { Process32FirstW(snapshot.0, &mut entry) };
        while found != 0 {
            if entries.len() == MAX_PROCESSES {
                return Err("unavailable".into());
            }
            let end = entry
                .name
                .iter()
                .position(|c| *c == 0)
                .unwrap_or(entry.name.len());
            entries.push(Entry {
                pid: entry.pid,
                parent: entry.parent,
                name: String::from_utf16_lossy(&entry.name[..end]).to_ascii_lowercase(),
            });
            found = unsafe { Process32NextW(snapshot.0, &mut entry) };
        }
        if unsafe { GetLastError() } != 18 {
            return Err("unavailable".into());
        }
        let root = std::process::id();
        let (handle, created) = open(root).ok_or("unavailable")?;
        let mut owned = HashMap::from([(
            root,
            Owned {
                handle,
                created,
                role: Role::Desktop,
            },
        )]);
        let mut result = MemorySnapshot {
            desktop: MemoryGroup::empty(),
            workers: MemoryGroup::empty(),
            web_view: MemoryGroup::empty(),
            total: MemoryGroup::empty(),
        };
        for _ in 0..MAX_DEPTH {
            let mut changed = false;
            entries.retain(|entry| {
                if entry.pid == root {
                    return false;
                }
                let Some(parent) = owned.get(&entry.parent) else {
                    return true;
                };
                let Some(role) = role(parent.role, &entry.name) else {
                    return false;
                };
                let group = match role {
                    Role::Worker => &mut result.workers,
                    Role::WebView => &mut result.web_view,
                    Role::Desktop => &mut result.desktop,
                };
                match open(entry.pid) {
                    // A recycled parent PID must never pull another app into this instance.
                    Some((handle, created))
                        if created >= parent.created && owned.len() < MAX_OWNED_PROCESSES =>
                    {
                        owned.insert(
                            entry.pid,
                            Owned {
                                handle,
                                created,
                                role,
                            },
                        );
                        changed = true;
                    }
                    Some(_) => {
                        group.add(None, None);
                        result.total.add(None, None);
                    }
                    None => {
                        group.add(None, None);
                        result.total.add(None, None);
                    }
                }
                false
            });
            if !changed {
                break;
            }
        }
        if entries.iter().any(|entry| {
            owned
                .get(&entry.parent)
                .is_some_and(|parent| role(parent.role, &entry.name).is_some())
        }) {
            return Err("unavailable".into());
        }
        // Keep parent handles alive through collection to prevent PID reuse during sampling.
        for process in owned.values() {
            let (ram, committed) = memory(&process.handle);
            let group = match process.role {
                Role::Desktop => &mut result.desktop,
                Role::Worker => &mut result.workers,
                Role::WebView => &mut result.web_view,
            };
            group.add(ram, committed);
            result.total.add(ram, committed);
        }
        Ok(result)
    }
}
