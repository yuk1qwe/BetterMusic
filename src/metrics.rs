//! Снимок рабочего набора дерева процессов; опрос выполняется только по запросу.
use std::collections::HashSet;
use windows_sys::Win32::{
    Foundation::{CloseHandle, INVALID_HANDLE_VALUE},
    System::{
        Diagnostics::ToolHelp::{
            CreateToolhelp32Snapshot, PROCESSENTRY32W, Process32FirstW, Process32NextW,
            TH32CS_SNAPPROCESS,
        },
        ProcessStatus::{K32GetProcessMemoryInfo, PROCESS_MEMORY_COUNTERS},
        Threading::{OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_VM_READ},
    },
};

pub struct Snapshot {
    pub bytes: u64,
    pub measured: usize,
    pub total: usize,
}
pub fn memory_snapshot() -> Option<Snapshot> {
    unsafe {
        let snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if snapshot == INVALID_HANDLE_VALUE {
            return None;
        }
        let mut entry = PROCESSENTRY32W {
            dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
            ..Default::default()
        };
        let mut edges = Vec::new();
        let mut ok = Process32FirstW(snapshot, &mut entry);
        while ok != 0 {
            edges.push((entry.th32ProcessID, entry.th32ParentProcessID));
            ok = Process32NextW(snapshot, &mut entry);
        }
        CloseHandle(snapshot);
        let mut descendants = HashSet::from([std::process::id()]);
        loop {
            let before = descendants.len();
            for &(pid, parent) in &edges {
                if descendants.contains(&parent) {
                    descendants.insert(pid);
                }
            }
            if descendants.len() == before {
                break;
            }
        }
        let mut result = Snapshot {
            bytes: 0,
            measured: 0,
            total: descendants.len(),
        };
        for pid in descendants {
            let process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, 0, pid);
            if process.is_null() {
                continue;
            }
            let mut counters = PROCESS_MEMORY_COUNTERS {
                cb: std::mem::size_of::<PROCESS_MEMORY_COUNTERS>() as u32,
                ..Default::default()
            };
            if K32GetProcessMemoryInfo(process, &mut counters, counters.cb) != 0 {
                result.bytes += counters.WorkingSetSize as u64;
                result.measured += 1;
            }
            CloseHandle(process);
        }
        (result.measured > 0).then_some(result)
    }
}
