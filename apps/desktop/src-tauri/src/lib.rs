// SPDX-License-Identifier: GPL-3.0-only

use std::collections::VecDeque;
use std::future::Future;
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::{mpsc, Arc, Condvar, Mutex, Weak};
use std::thread::JoinHandle;
use std::time::{Duration, Instant, SystemTime};

#[cfg(windows)]
use std::ffi::c_void;
#[cfg(unix)]
use std::os::fd::AsRawFd;
#[cfg(windows)]
use std::os::windows::io::AsRawHandle;
#[cfg(windows)]
use std::os::windows::process::CommandExt;
use tauri::{Emitter, Manager};
use tauri_plugin_shell::ShellExt;

#[cfg(windows)]
mod windows_app_identity;

const BRIDGE_SIDECAR_NAME: &str = "km-tools-bridge";
// The bridge wire protocol is stateful and line-oriented. Keep mutations on the owning
// sidecar while proven immutable reads use a small, resource-bounded pool of long-lived
// sidecars with stable command affinity.
const MAX_PROJECT_BRIDGE_PARALLEL_READ_WORKERS: usize = 4;
const PROJECT_BRIDGE_ESTIMATED_READ_WORKER_BYTES: u64 = 768 * 1024 * 1024;
const PROJECT_BRIDGE_READ_WORKER_MEMORY_BUDGET_DIVISOR: u64 = 4;
const PROJECT_BRIDGE_READ_WORKER_ENVIRONMENT_VARIABLE: &str = "KM_MANAGED_READ_WORKER";
const PROJECT_BRIDGE_CPU_LIMIT_ENVIRONMENT_VARIABLE: &str = "KM_MANAGED_CONCURRENCY_CPU_LIMIT";
const PROJECT_BRIDGE_MEMORY_LIMIT_ENVIRONMENT_VARIABLE: &str =
    "KM_MANAGED_CONCURRENCY_MEMORY_BYTES";
const MAX_PROJECT_BRIDGE_PENDING_REQUESTS: usize = 64;
const MAX_PROJECT_BRIDGE_PENDING_REQUEST_BYTES: usize = 256 * 1024 * 1024;
const PROJECT_BRIDGE_LIMIT_PROVISION_MULTIPLIER: usize = 4;
const PROJECT_BRIDGE_LIMIT_HARD_CEILING_MULTIPLIER: usize = 2;
const PROJECT_BRIDGE_EXPECTED_REQUEST_BYTES: usize = 16 * 1024 * 1024;
const PROJECT_BRIDGE_PROVISIONED_REQUEST_BYTES: usize = checked_project_bridge_limit(
    PROJECT_BRIDGE_EXPECTED_REQUEST_BYTES,
    PROJECT_BRIDGE_LIMIT_PROVISION_MULTIPLIER,
);
const MAX_PROJECT_BRIDGE_REQUEST_BYTES: usize = checked_project_bridge_limit(
    PROJECT_BRIDGE_PROVISIONED_REQUEST_BYTES,
    PROJECT_BRIDGE_LIMIT_HARD_CEILING_MULTIPLIER,
);
// Tauri JSON framing can approximately double an inner response containing quotes or
// backslashes. The current x86_64 desktop build therefore keeps the decoded inner
// ceiling below half of V8's maximum string length with additional margin.
const PROJECT_BRIDGE_EXPECTED_RESPONSE_BYTES: usize = 30 * 1024 * 1024;
const PROJECT_BRIDGE_PROVISIONED_RESPONSE_BYTES: usize = checked_project_bridge_limit(
    PROJECT_BRIDGE_EXPECTED_RESPONSE_BYTES,
    PROJECT_BRIDGE_LIMIT_PROVISION_MULTIPLIER,
);
const MAX_PROJECT_BRIDGE_RESPONSE_BYTES: usize = checked_project_bridge_limit(
    PROJECT_BRIDGE_PROVISIONED_RESPONSE_BYTES,
    PROJECT_BRIDGE_LIMIT_HARD_CEILING_MULTIPLIER,
);
const MAX_PROJECT_BRIDGE_FRAMED_RESPONSE_BYTES: usize =
    match MAX_PROJECT_BRIDGE_RESPONSE_BYTES.checked_add(2) {
        Some(limit) => limit,
        None => panic!("project bridge framed response limit overflow"),
    };
const PROJECT_BRIDGE_RECYCLED_ERROR: &str =
    "Project bridge request was canceled because the bridge was recycled.";
const PROJECT_BRIDGE_READ_PREEMPTED_ERROR: &str =
    "Read-only project preparation was restarted behind a pending output operation.";
const PROJECT_BRIDGE_PROJECT_READ_TIMEOUT: Duration = Duration::from_secs(30);
const PROJECT_BRIDGE_OUTPUT_READ_TIMEOUT: Duration = Duration::from_secs(45);
const PROJECT_BRIDGE_WORKFLOW_EXPECTED_TIMEOUT_SECONDS: u64 = 75;
const PROJECT_BRIDGE_WORKFLOW_PROVISION_MULTIPLIER: u64 = 4;
const PROJECT_BRIDGE_WORKFLOW_CEILING_MULTIPLIER: u64 = 2;
const PROJECT_BRIDGE_EDITOR_OPERATION_TIMEOUT: Duration = Duration::from_secs(
    PROJECT_BRIDGE_WORKFLOW_EXPECTED_TIMEOUT_SECONDS * PROJECT_BRIDGE_WORKFLOW_PROVISION_MULTIPLIER,
);
const PROJECT_BRIDGE_TERMINATION_WAIT_TIMEOUT: Duration = Duration::from_secs(2);
const PROJECT_BRIDGE_WORKFLOW_LOAD_TIMEOUT: Duration = Duration::from_secs(
    PROJECT_BRIDGE_WORKFLOW_EXPECTED_TIMEOUT_SECONDS
        * PROJECT_BRIDGE_WORKFLOW_PROVISION_MULTIPLIER
        * PROJECT_BRIDGE_WORKFLOW_CEILING_MULTIPLIER,
);
// New commands must never become unbounded by omission. They receive this generous
// ceiling without replay until their retry semantics are explicitly reviewed.
const PROJECT_BRIDGE_DEFAULT_OPERATION_TIMEOUT: Duration = PROJECT_BRIDGE_WORKFLOW_LOAD_TIMEOUT;
const PROJECT_BRIDGE_IO_POLL_INTERVAL: Duration = Duration::from_millis(10);
const PROJECT_BRIDGE_TERMINATION_POLL_INTERVAL: Duration = Duration::from_millis(10);
const PROJECT_BRIDGE_READ_CHUNK_BYTES: usize = 64 * 1024;
const PROJECT_BRIDGE_INITIAL_RESPONSE_BUFFER_BYTES: usize = 8 * 1024;
const PROJECT_BRIDGE_REQUEST_RUNNING: usize = 0;
const PROJECT_BRIDGE_REQUEST_COMPLETED: usize = 1;
const PROJECT_BRIDGE_REQUEST_TIMED_OUT: usize = 2;
const PROJECT_BRIDGE_NO_ACTIVE_REQUEST_TOKEN: usize = 0;
const SUPPORT_SEARCH_CANCELED_ERROR: &str = "Support file search was canceled.";
const WINDOW_CLOSE_REQUESTED_EVENT: &str = "km-editor://window-close-requested";
const SUPPORT_SEARCH_PROGRESS_EVENT: &str = "km-editor://support-file-search-progress";
const UPDATER_TEMP_DIRECTORY_PREFIX: &str = "KM Editor-";
const UPDATER_TEMP_DIRECTORY_MARKER: &str = "-updater-";
const STALE_UPDATER_TEMP_DIRECTORY_AGE: Duration = Duration::from_secs(24 * 60 * 60);
#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

// This sorted catalog mirrors every command that the managed dispatcher actually routes. Unknown
// commands never enter a read worker: they fail closed onto the exclusive ordered lane. The Rust
// contract test below compares this catalog to the C# dispatcher so a new route cannot silently
// inherit concurrency or replay behavior.
const ROUTED_PROJECT_BRIDGE_COMMANDS: &[&str] = &[
    "angeFight.load",
    "angeFight.stage",
    "angeFight.uninstall.stage",
    "bagHook.install.stage",
    "bagHook.load",
    "bagHook.uninstall.stage",
    "battleCafeRewards.load",
    "battleCafeRewards.rows.stage",
    "behavior.entry.update",
    "behavior.fields.update",
    "behavior.load",
    "catchCap.load",
    "catchCap.stage",
    "catchCap.uninstall.stage",
    "changePlan.apply",
    "changePlan.create",
    "changeSets.captureSession",
    "changeSets.export",
    "changeSets.import",
    "changeSets.materialize",
    "changeSets.mutate",
    "changeSets.read",
    "dynamaxAdventures.defaults.preview",
    "dynamaxAdventures.field.update",
    "dynamaxAdventures.fields.update",
    "dynamaxAdventures.load",
    "dynamaxAdventures.repair.stage",
    "dynamaxAdventures.restore.stage",
    "dynamaxAdventures.seed.plan",
    "dynamaxAdventures.seed.save.set",
    "dynamaxAdventures.seed.search",
    "editSession.start",
    "editSession.validate",
    "encounters.load",
    "encounters.slot.update",
    "encounters.slot.vanilla.stage",
    "encounters.slots.update",
    "exefsPatches.load",
    "exefsPatches.patch.stage",
    "fairyGymBoosts.load",
    "fairyGymBoosts.stage",
    "fashionCatalog.field.stage",
    "fashionCatalog.load",
    "fashionUnlock.install.stage",
    "fashionUnlock.load",
    "fashionUnlock.uninstall.stage",
    "flagworkSave.load",
    "fpsPatch.apply",
    "fpsPatch.load",
    "fpsPatch.restore",
    "gameDump.load",
    "gameDump.run",
    "gameModules.capabilities",
    "gameModules.query",
    "gameplaySettings.get",
    "gameplaySettings.update.apply",
    "gameplaySettings.update.preview",
    "giftPokemon.field.update",
    "giftPokemon.fields.update",
    "giftPokemon.gift.vanilla.stage",
    "giftPokemon.load",
    "guidedDesign.capabilities",
    "guidedDesign.import",
    "guidedDesign.preview",
    "gymUniformRemoval.install.stage",
    "gymUniformRemoval.load",
    "gymUniformRemoval.uninstall.stage",
    "habitatCoordinates.coordinate.stage",
    "habitatCoordinates.load",
    "hyperTraining.load",
    "hyperTraining.stage",
    "hyperspaceBypass.install.stage",
    "hyperspaceBypass.load",
    "hyperspaceBypass.uninstall.stage",
    "inGameSettingsPackage.apply",
    "inGameSettingsPackage.inspect",
    "inGameSettingsPackage.preview",
    "items.field.update",
    "items.fields.update",
    "items.item.vanilla.stage",
    "items.load",
    "ivScreen.install.stage",
    "ivScreen.load",
    "ivScreen.uninstall.stage",
    "modMerger.apply",
    "modMerger.load",
    "modMerger.stage",
    "moves.field.update",
    "moves.fields.update",
    "moves.load",
    "moves.move.vanilla.stage",
    "npcItemGift.load",
    "npcItemGift.stage",
    "output.checkpoint.create",
    "output.checkpoint.delete",
    "output.checkpoint.list",
    "output.checkpoint.restore",
    "output.checkpoint.restore.preview",
    "output.cleanup.apply",
    "output.cleanup.preview",
    "output.history.list",
    "output.integrity.scan",
    "output.recovery.reconcile",
    "output.recovery.status",
    "placement.catalog.open",
    "placement.catalog.query",
    "placement.load",
    "placement.object.load",
    "placement.object.update",
    "placement.objects.update",
    "pokemon.composite.update",
    "pokemon.dex.megas.sync.stage",
    "pokemon.dex.move",
    "pokemon.dex.resize",
    "pokemon.dex.swap",
    "pokemon.dex.vanilla.stage",
    "pokemon.evolution.update",
    "pokemon.field.update",
    "pokemon.fields.update",
    "pokemon.learnset.update",
    "pokemon.load",
    "profanityFilter.apply",
    "profanityFilter.load",
    "profanityFilter.restore",
    "project.fileGraph.refresh",
    "project.open",
    "project.relocation.apply",
    "project.relocation.preview",
    "project.sourceRevision.read",
    "project.validate",
    "raidBattles.load",
    "raidBattles.slot.update",
    "raidBattles.slots.update",
    "raidBonusRewards.load",
    "raidBonusRewards.reward.update",
    "raidBonusRewards.rewards.update",
    "raidRewards.load",
    "raidRewards.reward.update",
    "raidRewards.rewards.update",
    "randomizer.apply",
    "randomizer.restore",
    "randomizer.seed.import",
    "recipes.export",
    "recipes.import",
    "recipes.preview",
    "recipes.validate",
    "rentalPokemon.field.update",
    "rentalPokemon.fields.update",
    "rentalPokemon.load",
    "researchLab.annotations.mutate",
    "researchLab.annotations.read",
    "researchLab.byteWindow",
    "researchLab.capabilities",
    "researchLab.compare",
    "researchLab.source.close",
    "researchLab.source.open",
    "rowClipboard.authorizations.clear",
    "rowClipboard.copy.prepare",
    "rowClipboard.paste.preview",
    "rowClipboard.paste.stage",
    "royalCandy.load",
    "royalCandy.workflow.stage",
    "semantic.balance-lab",
    "semantic.capabilities",
    "semantic.changes",
    "semantic.compare",
    "semantic.entity",
    "semantic.external.compare",
    "semantic.impact",
    "semantic.ownership",
    "semantic.references",
    "semantic.search",
    "semanticMerge.capabilities",
    "semanticMerge.import",
    "semanticMerge.preview",
    "semanticMerge.source.open",
    "shinyRate.load",
    "shinyRate.stage",
    "shops.inventory.items.update",
    "shops.inventory.update",
    "shops.load",
    "spreadsheetImport.load",
    "spreadsheetImport.preview",
    "startingItems.load",
    "startingItems.stage",
    "staticEncounters.field.update",
    "staticEncounters.fields.update",
    "staticEncounters.load",
    "support.report.build",
    "svCache.clear",
    "svCache.settings.update",
    "svCache.status",
    "svCache.warmup.step",
    "svModMerger.apply",
    "svModMerger.load",
    "svModMerger.stage",
    "swshCache.clear",
    "swshCache.settings.update",
    "swshCache.status",
    "swshCache.warmup.step",
    "teraRaids.field.update",
    "teraRaids.fields.update",
    "teraRaids.load",
    "text.entry.update",
    "text.load",
    "tmMachineControls.load",
    "tmMachineControls.materialVisibility.stage",
    "tmMachineControls.recipeAvailability.stage",
    "tradePokemon.field.update",
    "tradePokemon.fields.update",
    "tradePokemon.load",
    "trainerPools.fixedCountSwap.stage",
    "trainerPools.load",
    "trainers.field.update",
    "trainers.fields.update",
    "trainers.load",
    "typeChart.load",
    "typeChart.stage",
    "typeChart.uninstall.stage",
    "workflow.list",
    "workspace.applicationState.read",
    "workspace.applicationState.write",
    "workspace.drafts.delete",
    "workspace.drafts.read",
    "workspace.drafts.write",
    "workspace.projectState.delete",
    "workspace.projectState.read",
    "workspace.projectState.write",
    "zaCache.clear",
    "zaCache.settings.update",
    "zaCache.status",
    "zaCache.warmup.step",
    "zaModMerger.apply",
    "zaModMerger.load",
    "zaModMerger.stage",
];

const fn checked_project_bridge_limit(value: usize, multiplier: usize) -> usize {
    match value.checked_mul(multiplier) {
        Some(limit) => limit,
        None => panic!("project bridge size limit overflow"),
    }
}

fn project_bridge_parallel_read_worker_limit() -> usize {
    let cpu_limit = std::thread::available_parallelism()
        .map(|parallelism| parallelism.get().saturating_sub(1).max(1))
        .unwrap_or(1)
        .min(MAX_PROJECT_BRIDGE_PARALLEL_READ_WORKERS);
    let memory_limit = available_project_bridge_memory_bytes()
        .map(|available_bytes| {
            (available_bytes
                / PROJECT_BRIDGE_READ_WORKER_MEMORY_BUDGET_DIVISOR
                / PROJECT_BRIDGE_ESTIMATED_READ_WORKER_BYTES)
                .clamp(1, MAX_PROJECT_BRIDGE_PARALLEL_READ_WORKERS as u64) as usize
        })
        .unwrap_or(1);

    cpu_limit.min(memory_limit).max(1)
}

fn project_bridge_worker_host_budgets(worker_count: usize) -> (usize, u64) {
    let worker_count = worker_count.max(1);
    let cpu_budget = std::thread::available_parallelism()
        .map(|parallelism| parallelism.get())
        .unwrap_or(1)
        .div_ceil(worker_count)
        .clamp(1, 64);
    let memory_budget = available_project_bridge_memory_bytes()
        .map(|available_bytes| {
            available_bytes / PROJECT_BRIDGE_READ_WORKER_MEMORY_BUDGET_DIVISOR / worker_count as u64
        })
        .unwrap_or(PROJECT_BRIDGE_ESTIMATED_READ_WORKER_BYTES)
        .max(64 * 1024 * 1024);
    (cpu_budget, memory_budget)
}

#[cfg(windows)]
fn available_project_bridge_memory_bytes() -> Option<u64> {
    let mut status = ProjectBridgeMemoryStatus {
        length: std::mem::size_of::<ProjectBridgeMemoryStatus>() as u32,
        memory_load: 0,
        total_physical: 0,
        available_physical: 0,
        total_page_file: 0,
        available_page_file: 0,
        total_virtual: 0,
        available_virtual: 0,
        available_extended_virtual: 0,
    };
    let succeeded = unsafe { global_memory_status_ex(&mut status) };
    (succeeded != 0 && status.available_physical > 0).then_some(status.available_physical)
}

#[cfg(target_os = "linux")]
fn available_project_bridge_memory_bytes() -> Option<u64> {
    let contents = std::fs::read_to_string("/proc/meminfo").ok()?;
    let kibibytes = contents.lines().find_map(|line| {
        let value = line.strip_prefix("MemAvailable:")?.trim();
        value.strip_suffix("kB")?.trim().parse::<u64>().ok()
    })?;
    kibibytes.checked_mul(1024)
}

#[cfg(not(any(windows, target_os = "linux")))]
fn available_project_bridge_memory_bytes() -> Option<u64> {
    None
}

struct CloseGuardState {
    is_guarded: AtomicBool,
    shutdown_started: AtomicBool,
}

#[derive(Clone, Default)]
struct SupportSearchState {
    generation: Arc<AtomicUsize>,
}

impl SupportSearchState {
    fn begin_search(&self) -> usize {
        self.generation
            .fetch_add(1, Ordering::AcqRel)
            .wrapping_add(1)
    }

    fn cancel(&self) {
        self.generation.fetch_add(1, Ordering::AcqRel);
    }

    fn is_current(&self, generation: usize) -> bool {
        self.generation.load(Ordering::Acquire) == generation
    }
}

#[derive(Clone)]
struct ProjectBridgeState {
    process: Arc<Mutex<Option<Arc<ProjectBridgeProcess>>>>,
    read_processes: Arc<Vec<Mutex<Option<Arc<ProjectBridgeProcess>>>>>,
    process_lifecycle: Arc<Mutex<()>>,
    generation: Arc<AtomicUsize>,
    read_generation: Arc<AtomicUsize>,
    read_worker_requests: Arc<ProjectBridgeReadWorkerRequestState>,
    execution_gate: Arc<tokio::sync::RwLock<()>>,
    pending_requests: Arc<Mutex<ProjectBridgePendingRequestState>>,
}

impl Default for ProjectBridgeState {
    fn default() -> Self {
        Self::with_parallel_read_limit(project_bridge_parallel_read_worker_limit())
    }
}

impl ProjectBridgeState {
    fn with_parallel_read_limit(maximum_parallel_reads: usize) -> Self {
        assert!(maximum_parallel_reads > 0);
        let maximum_parallel_reads = u32::try_from(maximum_parallel_reads)
            .expect("project bridge parallel read limit must fit in u32");
        Self {
            process: Arc::new(Mutex::new(None)),
            read_processes: Arc::new(
                (0..maximum_parallel_reads)
                    .map(|_| Mutex::new(None))
                    .collect(),
            ),
            process_lifecycle: Arc::new(Mutex::new(())),
            generation: Arc::new(AtomicUsize::new(0)),
            read_generation: Arc::new(AtomicUsize::new(0)),
            read_worker_requests: Arc::new(ProjectBridgeReadWorkerRequestState::default()),
            execution_gate: Arc::new(tokio::sync::RwLock::with_max_readers(
                (),
                maximum_parallel_reads,
            )),
            pending_requests: Arc::new(Mutex::new(ProjectBridgePendingRequestState::default())),
        }
    }

    fn reserve_pending_request(
        &self,
        request_bytes: usize,
    ) -> Result<ProjectBridgePendingRequestGuard, String> {
        let mut pending = self
            .pending_requests
            .lock()
            .map_err(|_| "Project bridge request budget lock was poisoned.".to_owned())?;
        if pending.count >= MAX_PROJECT_BRIDGE_PENDING_REQUESTS {
            return Err(
                "Too many project operations are already queued. Wait for the current operations to finish and retry."
                    .to_owned(),
            );
        }
        if request_bytes > MAX_PROJECT_BRIDGE_PENDING_REQUEST_BYTES - pending.bytes {
            return Err(
                "Queued project operations exceeded the supported memory budget. Wait for the current operations to finish and retry."
                    .to_owned(),
            );
        }

        pending.count += 1;
        pending.bytes += request_bytes;
        drop(pending);
        Ok(ProjectBridgePendingRequestGuard {
            pending_requests: self.pending_requests.clone(),
            request_bytes,
        })
    }

    async fn acquire_execution_permit_for_generation(
        &self,
        request_generation: usize,
        concurrency: ProjectBridgeCommandConcurrency,
    ) -> Result<ProjectBridgeExecutionPermit, String> {
        let permit = match concurrency {
            ProjectBridgeCommandConcurrency::IndependentRead(_) => {
                let guard = Arc::new(Mutex::new(Some(
                    self.execution_gate.clone().read_owned().await,
                )));
                let request = ProjectBridgeReadWorkerRequest::begin(
                    self.read_worker_requests.clone(),
                    ProjectBridgeReadWorkerRequestKind::Disposable,
                    Some(&guard),
                )?;
                ProjectBridgeExecutionPermit::IndependentRead {
                    _guard: None,
                    _revocable_guard: Some(guard),
                    _read_worker_request: Some(request),
                }
            }
            ProjectBridgeCommandConcurrency::OwnerOrdered => {
                ProjectBridgeExecutionPermit::IndependentRead {
                    _guard: Some(self.execution_gate.clone().read_owned().await),
                    _revocable_guard: None,
                    _read_worker_request: None,
                }
            }
            ProjectBridgeCommandConcurrency::AffinityOrdered(_) => {
                let guard = self.execution_gate.clone().read_owned().await;
                let protected_request = ProjectBridgeReadWorkerRequest::begin(
                    self.read_worker_requests.clone(),
                    ProjectBridgeReadWorkerRequestKind::Protected,
                    None,
                );
                ProjectBridgeExecutionPermit::IndependentRead {
                    _guard: Some(guard),
                    _revocable_guard: None,
                    _read_worker_request: Some(protected_request?),
                }
            }
            ProjectBridgeCommandConcurrency::AffinityExclusive(_) => {
                let guard = self.execution_gate.clone().write_owned().await;
                let protected_request = ProjectBridgeReadWorkerRequest::begin(
                    self.read_worker_requests.clone(),
                    ProjectBridgeReadWorkerRequestKind::Protected,
                    None,
                );
                ProjectBridgeExecutionPermit::Ordered {
                    _guard: guard,
                    _read_worker_request: Some(protected_request?),
                }
            }
            ProjectBridgeCommandConcurrency::Exclusive => ProjectBridgeExecutionPermit::Ordered {
                _guard: self.execution_gate.clone().write_owned().await,
                _read_worker_request: None,
            },
        };
        ensure_project_bridge_request_is_current(self, request_generation)?;
        Ok(permit)
    }

    async fn acquire_execution_permit_for_request(
        &self,
        request_generation: usize,
        policy: ProjectBridgeRequestPolicy,
    ) -> Result<ProjectBridgeExecutionPermit, String> {
        if !policy.writer_priority_before_execution
            || !matches!(
                policy.concurrency,
                ProjectBridgeCommandConcurrency::Exclusive
                    | ProjectBridgeCommandConcurrency::OwnerOrdered
            )
        {
            return self
                .acquire_execution_permit_for_generation(request_generation, policy.concurrency)
                .await;
        }

        // Register the writer with Tokio's fair RwLock before canceling disposable readers. Once
        // this future has been polled, later reads queue behind it instead of replacing the work we
        // just stopped. The owning sidecar is never part of this preemption path.
        let gate = self.execution_gate.clone();
        let (queued_sender, queued_receiver) = tokio::sync::oneshot::channel();
        let writer_task = tauri::async_runtime::spawn(async move {
            let mut writer = Box::pin(gate.write_owned());
            let mut queued_sender = Some(queued_sender);
            std::future::poll_fn(|context| {
                let result = writer.as_mut().poll(context);
                if let Some(sender) = queued_sender.take() {
                    let _ = sender.send(());
                }
                result
            })
            .await
        });
        queued_receiver
            .await
            .map_err(|_| "Project bridge writer admission stopped unexpectedly.".to_owned())?;

        let preemption_state = self.clone();
        let writer_cleanup = tauri::async_runtime::spawn_blocking(move || {
            preempt_project_bridge_read_workers(&preemption_state)
        })
        .await
        .map_err(|error| format!("Project bridge reader preemption task failed: {error}"))??;

        let guard = writer_task
            .await
            .map_err(|error| format!("Project bridge writer admission task failed: {error}"))?;
        // A reader may already own a read guard while being descheduled immediately before it
        // registers in read_worker_requests. Keep the cleanup marker until this write guard is
        // actually owned so that such a reader resumes into PREEMPTED and releases its guard.
        drop(writer_cleanup);
        ensure_project_bridge_request_is_current(self, request_generation)?;
        Ok(ProjectBridgeExecutionPermit::Ordered {
            _guard: guard,
            _read_worker_request: None,
        })
    }
}

#[derive(Default)]
struct ProjectBridgePendingRequestState {
    count: usize,
    bytes: usize,
}

struct ProjectBridgePendingRequestGuard {
    pending_requests: Arc<Mutex<ProjectBridgePendingRequestState>>,
    request_bytes: usize,
}

#[derive(Debug)]
enum ProjectBridgeExecutionPermit {
    IndependentRead {
        _guard: Option<tokio::sync::OwnedRwLockReadGuard<()>>,
        _revocable_guard: Option<ProjectBridgeRevocableReadGuard>,
        _read_worker_request: Option<ProjectBridgeReadWorkerRequest>,
    },
    Ordered {
        _guard: tokio::sync::OwnedRwLockWriteGuard<()>,
        _read_worker_request: Option<ProjectBridgeReadWorkerRequest>,
    },
}

type ProjectBridgeRevocableReadGuard = Arc<Mutex<Option<tokio::sync::OwnedRwLockReadGuard<()>>>>;
type ProjectBridgeWeakRevocableReadGuard =
    Weak<Mutex<Option<tokio::sync::OwnedRwLockReadGuard<()>>>>;

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum ProjectBridgeReadWorkerRequestKind {
    Disposable,
    Protected,
}

#[derive(Debug, Default)]
struct ProjectBridgeReadWorkerRequestState {
    state: Mutex<ProjectBridgeReadWorkerRequestStateInner>,
    changed: Condvar,
}

#[derive(Debug, Default)]
struct ProjectBridgeReadWorkerRequestStateInner {
    active_disposable_requests: usize,
    active_protected_requests: usize,
    writer_cleanup_waiters: usize,
    owner_cleanup_waiters: usize,
    disposable_execution_guards: Vec<ProjectBridgeWeakRevocableReadGuard>,
}

impl ProjectBridgeReadWorkerRequestState {
    fn revoke_disposable_execution_guards(&self) -> Result<(), String> {
        let guards = {
            let mut state = self
                .state
                .lock()
                .map_err(|_| "Project bridge read-worker request lock was poisoned.".to_owned())?;
            state
                .disposable_execution_guards
                .retain(|guard| guard.strong_count() != 0);
            state
                .disposable_execution_guards
                .iter()
                .filter_map(Weak::upgrade)
                .collect::<Vec<_>>()
        };
        for guard in guards {
            let execution_guard = match guard.lock() {
                Ok(mut execution_guard) => execution_guard.take(),
                Err(poisoned) => poisoned.into_inner().take(),
            };
            drop(execution_guard);
        }
        Ok(())
    }
}

#[cfg(test)]
impl ProjectBridgeReadWorkerRequestState {
    fn active_protected_requests(&self) -> usize {
        self.state
            .lock()
            .expect("inspect protected read-worker requests")
            .active_protected_requests
    }
}

#[derive(Debug)]
struct ProjectBridgeReadWorkerRequest {
    requests: Arc<ProjectBridgeReadWorkerRequestState>,
    kind: ProjectBridgeReadWorkerRequestKind,
    disposable_execution_guard: Option<ProjectBridgeWeakRevocableReadGuard>,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum ProjectBridgeReadWorkerCleanupKind {
    WriterPreemption,
    OwnerTimeout,
}

struct ProjectBridgeReadWorkerCleanup {
    requests: Arc<ProjectBridgeReadWorkerRequestState>,
    kind: ProjectBridgeReadWorkerCleanupKind,
}

impl ProjectBridgeReadWorkerRequest {
    fn begin(
        requests: Arc<ProjectBridgeReadWorkerRequestState>,
        kind: ProjectBridgeReadWorkerRequestKind,
        disposable_execution_guard: Option<&ProjectBridgeRevocableReadGuard>,
    ) -> Result<Self, String> {
        let mut state = requests
            .state
            .lock()
            .map_err(|_| "Project bridge read-worker request lock was poisoned.".to_owned())?;
        let blocked = match kind {
            ProjectBridgeReadWorkerRequestKind::Disposable => state.writer_cleanup_waiters != 0,
            ProjectBridgeReadWorkerRequestKind::Protected => {
                state.writer_cleanup_waiters != 0 || state.owner_cleanup_waiters != 0
            }
        };
        if blocked {
            return Err(match kind {
                ProjectBridgeReadWorkerRequestKind::Disposable => {
                    PROJECT_BRIDGE_READ_PREEMPTED_ERROR.to_owned()
                }
                ProjectBridgeReadWorkerRequestKind::Protected => {
                    PROJECT_BRIDGE_RECYCLED_ERROR.to_owned()
                }
            });
        }

        let disposable_execution_guard = match kind {
            ProjectBridgeReadWorkerRequestKind::Disposable => {
                let guard = disposable_execution_guard.ok_or_else(|| {
                    "Disposable project bridge reads require a revocable execution guard."
                        .to_owned()
                })?;
                Some(Arc::downgrade(guard))
            }
            ProjectBridgeReadWorkerRequestKind::Protected => {
                if disposable_execution_guard.is_some() {
                    return Err(
                        "Protected project bridge work cannot use a revocable execution guard."
                            .to_owned(),
                    );
                }
                None
            }
        };

        let active_requests = match kind {
            ProjectBridgeReadWorkerRequestKind::Disposable => &mut state.active_disposable_requests,
            ProjectBridgeReadWorkerRequestKind::Protected => &mut state.active_protected_requests,
        };
        *active_requests = active_requests
            .checked_add(1)
            .ok_or_else(|| "Project bridge read-worker request count overflowed.".to_owned())?;
        if let Some(guard) = disposable_execution_guard.as_ref() {
            state.disposable_execution_guards.push(guard.clone());
        }
        drop(state);
        Ok(Self {
            requests,
            kind,
            disposable_execution_guard,
        })
    }
}

impl Drop for ProjectBridgeReadWorkerRequest {
    fn drop(&mut self) {
        let mut state = match self.requests.state.lock() {
            Ok(state) => state,
            Err(poisoned) => poisoned.into_inner(),
        };
        let active_requests = match self.kind {
            ProjectBridgeReadWorkerRequestKind::Disposable => &mut state.active_disposable_requests,
            ProjectBridgeReadWorkerRequestKind::Protected => &mut state.active_protected_requests,
        };
        *active_requests = active_requests.saturating_sub(1);
        if let Some(guard) = self.disposable_execution_guard.as_ref() {
            state
                .disposable_execution_guards
                .retain(|candidate| !Weak::ptr_eq(candidate, guard));
        }
        self.requests.changed.notify_all();
    }
}

impl ProjectBridgeReadWorkerCleanup {
    fn begin(
        requests: Arc<ProjectBridgeReadWorkerRequestState>,
        kind: ProjectBridgeReadWorkerCleanupKind,
    ) -> Result<Self, String> {
        let mut state = requests
            .state
            .lock()
            .map_err(|_| "Project bridge read-worker request lock was poisoned.".to_owned())?;
        let cleanup_waiters = match kind {
            ProjectBridgeReadWorkerCleanupKind::WriterPreemption => {
                &mut state.writer_cleanup_waiters
            }
            ProjectBridgeReadWorkerCleanupKind::OwnerTimeout => &mut state.owner_cleanup_waiters,
        };
        *cleanup_waiters = cleanup_waiters
            .checked_add(1)
            .ok_or_else(|| "Project bridge read-worker cleanup count overflowed.".to_owned())?;
        drop(state);
        Ok(Self { requests, kind })
    }

    fn wait_for_protected_response_boundary(&self) -> Result<(), String> {
        let mut state = self
            .requests
            .state
            .lock()
            .map_err(|_| "Project bridge read-worker request lock was poisoned.".to_owned())?;
        while state.active_protected_requests != 0 {
            state =
                self.requests.changed.wait(state).map_err(|_| {
                    "Project bridge read-worker request lock was poisoned.".to_owned()
                })?;
        }
        Ok(())
    }

    fn has_disposable_requests(&self) -> Result<bool, String> {
        self.requests
            .state
            .lock()
            .map(|state| state.active_disposable_requests != 0)
            .map_err(|_| "Project bridge read-worker request lock was poisoned.".to_owned())
    }
}

impl Drop for ProjectBridgeReadWorkerCleanup {
    fn drop(&mut self) {
        let mut state = match self.requests.state.lock() {
            Ok(state) => state,
            Err(poisoned) => poisoned.into_inner(),
        };
        let cleanup_waiters = match self.kind {
            ProjectBridgeReadWorkerCleanupKind::WriterPreemption => {
                &mut state.writer_cleanup_waiters
            }
            ProjectBridgeReadWorkerCleanupKind::OwnerTimeout => &mut state.owner_cleanup_waiters,
        };
        *cleanup_waiters = cleanup_waiters.saturating_sub(1);
        self.requests.changed.notify_all();
    }
}

impl Drop for ProjectBridgePendingRequestGuard {
    fn drop(&mut self) {
        if let Ok(mut pending) = self.pending_requests.lock() {
            pending.count = pending.count.saturating_sub(1);
            pending.bytes = pending.bytes.saturating_sub(self.request_bytes);
        }
    }
}

struct ProjectBridgeProcess {
    active_request_token: AtomicUsize,
    active_preemptible_request: AtomicBool,
    next_request_token: AtomicUsize,
    child: Mutex<Option<Child>>,
    admission: ProjectBridgeAdmission,
    io: Mutex<ProjectBridgeIo>,
}

#[derive(Default)]
struct ProjectBridgeAdmission {
    state: Mutex<ProjectBridgeAdmissionState>,
    changed: Condvar,
}

#[derive(Default)]
struct ProjectBridgeAdmissionState {
    active: bool,
    waiting_request_tokens: VecDeque<usize>,
}

struct ProjectBridgeAdmissionGuard<'a> {
    admission: &'a ProjectBridgeAdmission,
}

struct ProjectBridgeIo {
    stdin: Option<ChildStdin>,
    stdout: ChildStdout,
    buffered_stdout: Vec<u8>,
}

#[derive(Clone)]
struct ProjectBridgeRequestCancellation {
    request_state: Option<Arc<AtomicUsize>>,
    generation: Arc<AtomicUsize>,
    request_generation: usize,
    read_generation: Option<Arc<AtomicUsize>>,
    request_read_generation: Option<usize>,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum ProjectBridgeStdoutReadiness {
    Pending,
    Ready(usize),
    Closed,
}

#[derive(Debug)]
enum ProjectBridgeRequestFailure {
    Retryable(String),
    NonRetryable(String),
    TimedOut(String),
}

#[derive(Debug)]
enum ProjectBridgeAdmissionFailure {
    Poisoned,
}

#[derive(Clone, Copy)]
struct ProjectBridgeRequestPolicy {
    execution_timeout: Option<Duration>,
    retry_after_transport_failure: bool,
    concurrency: ProjectBridgeCommandConcurrency,
    writer_priority_before_execution: bool,
    recycle_read_workers_before_execution: bool,
    recycle_read_workers_after_execution: bool,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum ProjectBridgeCommandConcurrency {
    IndependentRead(ProjectBridgeReadAffinity),
    AffinityOrdered(ProjectBridgeReadAffinity),
    AffinityExclusive(ProjectBridgeReadAffinity),
    OwnerOrdered,
    Exclusive,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum ProjectBridgeReadAffinity {
    SemanticExplore,
    BalanceLab,
    GameModules,
    GuidedDesign,
    SemanticMerge,
    ResearchLab,
    Workflow(u64),
    GeneralProject,
}

impl ProjectBridgeReadAffinity {
    fn stable_index(self) -> usize {
        match self {
            Self::SemanticExplore => 0,
            Self::BalanceLab => 1,
            Self::GameModules => 2,
            Self::GuidedDesign => 3,
            Self::SemanticMerge => 4,
            Self::ResearchLab => 5,
            Self::Workflow(hash) => 6_usize.wrapping_add(hash as usize),
            Self::GeneralProject => 7,
        }
    }
}

struct ProjectBridgeRequestWatchdog {
    cancellation_sender: mpsc::Sender<()>,
    request_state: Arc<AtomicUsize>,
    thread: Option<JoinHandle<()>>,
}

struct ProjectBridgeActiveRequest<'a> {
    active_request_token: &'a AtomicUsize,
    active_preemptible_request: &'a AtomicBool,
    request_token: usize,
}

impl Drop for ProjectBridgeProcess {
    fn drop(&mut self) {
        let child = match self.child.get_mut() {
            Ok(child) => child,
            Err(poisoned) => poisoned.into_inner(),
        };
        let _ = terminate_project_bridge_child(child);
    }
}

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct SupportSearchProgress {
    current_root: String,
    current_path: String,
    searched_directories: u64,
    searched_files: u64,
}

#[tauri::command(rename_all = "camelCase")]
async fn project_bridge(
    app_handle: tauri::AppHandle,
    bridge_state: tauri::State<'_, ProjectBridgeState>,
    request_json: String,
) -> Result<String, String> {
    validate_project_bridge_request_transport(&request_json)?;

    let bridge_state = bridge_state.inner().clone();
    let _pending_request = bridge_state.reserve_pending_request(request_json.len())?;
    let request_policy = resolved_project_bridge_request_policy(&request_json);
    loop {
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let request_read_generation = bridge_state.read_generation.load(Ordering::Acquire);
        let execution_permit = match bridge_state
            .acquire_execution_permit_for_request(request_generation, request_policy)
            .await
        {
            Ok(permit) => permit,
            Err(error)
                if project_bridge_request_should_restart_after_preemption(
                    request_policy,
                    &error,
                ) =>
            {
                continue;
            }
            Err(error) => return Err(error),
        };
        let request_bridge_state = bridge_state.clone();
        let request_app_handle = app_handle.clone();
        let request_json_for_attempt = request_json.clone();
        let mut response = tauri::async_runtime::spawn_blocking(move || {
            let _execution_permit = execution_permit;
            if request_policy.recycle_read_workers_before_execution {
                recycle_project_bridge_read_workers(&request_bridge_state)?;
            }

            let response = match request_policy.concurrency {
                ProjectBridgeCommandConcurrency::IndependentRead(affinity) => {
                    run_project_bridge_read_request(
                        &request_app_handle,
                        &request_bridge_state,
                        request_generation,
                        Some(request_read_generation),
                        request_json_for_attempt,
                        request_policy,
                        affinity,
                    )
                }
                ProjectBridgeCommandConcurrency::AffinityOrdered(affinity)
                | ProjectBridgeCommandConcurrency::AffinityExclusive(affinity) => {
                    run_project_bridge_read_request(
                        &request_app_handle,
                        &request_bridge_state,
                        request_generation,
                        None,
                        request_json_for_attempt,
                        request_policy,
                        affinity,
                    )
                }
                ProjectBridgeCommandConcurrency::OwnerOrdered
                | ProjectBridgeCommandConcurrency::Exclusive => run_project_bridge_request(
                    &request_app_handle,
                    &request_bridge_state,
                    request_generation,
                    request_json_for_attempt,
                    request_policy,
                ),
            };
            if request_policy.recycle_read_workers_after_execution {
                let recycle_result = recycle_project_bridge_read_workers(&request_bridge_state);
                if response.is_ok() {
                    recycle_result?;
                }
            }
            response
        })
        .await
        .map_err(|error| format!("Project bridge request task failed: {error}"))?;

        response = accept_project_bridge_response_for_epoch(
            &bridge_state,
            request_generation,
            request_read_generation,
            request_policy.concurrency,
            response,
        );

        if response.as_ref().is_err_and(|error| {
            project_bridge_request_should_restart_after_preemption(request_policy, error)
        }) {
            // The old read permit has been dropped by the blocking task. Re-admit the same
            // replay-safe read behind the queued writer using the new read generation so
            // background preparation recovers without surfacing a false editor error.
            continue;
        }
        return response;
    }
}

fn project_bridge_concurrency_is_preemptible_read(
    concurrency: ProjectBridgeCommandConcurrency,
) -> bool {
    matches!(
        concurrency,
        ProjectBridgeCommandConcurrency::IndependentRead(_)
    )
}

fn project_bridge_request_should_restart_after_preemption(
    policy: ProjectBridgeRequestPolicy,
    error: &str,
) -> bool {
    error == PROJECT_BRIDGE_READ_PREEMPTED_ERROR
        && policy.retry_after_transport_failure
        && project_bridge_concurrency_is_preemptible_read(policy.concurrency)
}

fn accept_project_bridge_response_for_epoch(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    request_read_generation: usize,
    concurrency: ProjectBridgeCommandConcurrency,
    response: Result<String, String>,
) -> Result<String, String> {
    let request_read_generation = project_bridge_concurrency_is_preemptible_read(concurrency)
        .then_some(request_read_generation);
    ensure_project_bridge_request_epoch_is_current(
        bridge_state,
        request_generation,
        request_read_generation,
    )?;
    response
}

fn validate_project_bridge_request_transport(request_json: &str) -> Result<(), String> {
    if request_json.len() > MAX_PROJECT_BRIDGE_REQUEST_BYTES {
        return Err("Project bridge request exceeded the supported size limit.".to_owned());
    }

    if request_json
        .as_bytes()
        .iter()
        .any(|byte| matches!(byte, b'\r' | b'\n'))
    {
        return Err("Project bridge requests must contain exactly one JSON line.".to_owned());
    }

    Ok(())
}

#[tauri::command]
async fn recycle_project_bridge(
    bridge_state: tauri::State<'_, ProjectBridgeState>,
) -> Result<(), String> {
    recycle_project_bridge_after_active_requests(bridge_state.inner().clone()).await
}

async fn recycle_project_bridge_after_active_requests(
    bridge_state: ProjectBridgeState,
) -> Result<(), String> {
    // Recycling is a lifecycle operation, not a request in the current generation. It must wait
    // behind the same exclusive barrier as durable work, then hold that barrier until every
    // process has been detached and reaped. Otherwise a manual recycle or shutdown can kill the
    // sidecar between journal publication and its cleanup/recovery boundary.
    let execution_permit = bridge_state.execution_gate.clone().write_owned().await;
    tauri::async_runtime::spawn_blocking(move || {
        let _execution_permit = execution_permit;
        recycle_project_bridge_process(&bridge_state)
    })
    .await
    .map_err(|error| format!("Project bridge recycle task failed: {error}"))?
}

fn run_project_bridge_request(
    app_handle: &tauri::AppHandle,
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    request_json: String,
    request_policy: ProjectBridgeRequestPolicy,
) -> Result<String, String> {
    run_project_bridge_request_with(
        bridge_state,
        request_generation,
        &request_json,
        request_policy,
        None,
        &bridge_state.process,
        || start_project_bridge_process(app_handle, None),
    )
}

fn run_project_bridge_read_request(
    app_handle: &tauri::AppHandle,
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    request_read_generation: Option<usize>,
    request_json: String,
    request_policy: ProjectBridgeRequestPolicy,
    affinity: ProjectBridgeReadAffinity,
) -> Result<String, String> {
    let process_index = affinity.stable_index() % bridge_state.read_processes.len();
    let process_slot = &bridge_state.read_processes[process_index];
    run_project_bridge_request_with(
        bridge_state,
        request_generation,
        &request_json,
        request_policy,
        request_read_generation,
        process_slot,
        || start_project_bridge_process(app_handle, Some(bridge_state.read_processes.len())),
    )
}

fn run_project_bridge_request_with<F>(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    request_json: &str,
    request_policy: ProjectBridgeRequestPolicy,
    request_read_generation: Option<usize>,
    process_slot: &Mutex<Option<Arc<ProjectBridgeProcess>>>,
    mut start_process: F,
) -> Result<String, String>
where
    F: FnMut() -> Result<ProjectBridgeProcess, String>,
{
    let may_retry = request_policy.retry_after_transport_failure;

    for attempt in 0..2 {
        ensure_project_bridge_request_epoch_is_current(
            bridge_state,
            request_generation,
            request_read_generation,
        )?;
        let process = get_or_start_project_bridge_process(
            bridge_state,
            request_generation,
            process_slot,
            &mut start_process,
        )?;
        let request_result = process.request(
            bridge_state,
            request_generation,
            request_json,
            request_policy.execution_timeout,
            request_read_generation,
        );
        match request_result {
            Ok(response) => {
                ensure_project_bridge_request_epoch_is_current(
                    bridge_state,
                    request_generation,
                    request_read_generation,
                )?;
                return Ok(response);
            }
            Err(ProjectBridgeRequestFailure::TimedOut(error)) => return Err(error),
            Err(ProjectBridgeRequestFailure::NonRetryable(error)) => {
                remove_failed_project_bridge_process(bridge_state, process_slot, &process)?;
                return Err(error);
            }
            Err(ProjectBridgeRequestFailure::Retryable(error)) => {
                remove_failed_project_bridge_process(bridge_state, process_slot, &process)?;
                ensure_project_bridge_request_epoch_is_current(
                    bridge_state,
                    request_generation,
                    request_read_generation,
                )?;
                if !may_retry || attempt > 0 {
                    return Err(error);
                }
            }
        }
    }

    Err("Project bridge request could not be completed.".to_owned())
}

fn get_or_start_project_bridge_process(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    process_slot: &Mutex<Option<Arc<ProjectBridgeProcess>>>,
    start_process: &mut impl FnMut() -> Result<ProjectBridgeProcess, String>,
) -> Result<Arc<ProjectBridgeProcess>, String> {
    let _lifecycle = bridge_state
        .process_lifecycle
        .lock()
        .map_err(|_| "Project bridge process lifecycle lock was poisoned.".to_owned())?;
    let mut process = process_slot
        .lock()
        .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;
    ensure_project_bridge_request_is_current(bridge_state, request_generation)?;

    if let Some(process) = process.as_ref() {
        return Ok(process.clone());
    }

    let started = Arc::new(start_process()?);
    *process = Some(started.clone());
    Ok(started)
}

fn remove_failed_project_bridge_process(
    bridge_state: &ProjectBridgeState,
    process_slot: &Mutex<Option<Arc<ProjectBridgeProcess>>>,
    failed_process: &Arc<ProjectBridgeProcess>,
) -> Result<(), String> {
    let _lifecycle = bridge_state
        .process_lifecycle
        .lock()
        .map_err(|_| "Project bridge process lifecycle lock was poisoned.".to_owned())?;
    let removed = {
        let mut current = process_slot
            .lock()
            .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;
        if current
            .as_ref()
            .is_some_and(|process| Arc::ptr_eq(process, failed_process))
        {
            current.take()
        } else {
            None
        }
    };

    if let Some(process) = removed {
        process.terminate()?;
    }
    Ok(())
}

impl ProjectBridgeProcess {
    fn new(child: Child, stdin: ChildStdin, stdout: ChildStdout) -> Self {
        Self {
            active_request_token: AtomicUsize::new(PROJECT_BRIDGE_NO_ACTIVE_REQUEST_TOKEN),
            active_preemptible_request: AtomicBool::new(false),
            next_request_token: AtomicUsize::new(1),
            child: Mutex::new(Some(child)),
            admission: ProjectBridgeAdmission::default(),
            io: Mutex::new(ProjectBridgeIo {
                stdin: Some(stdin),
                stdout,
                buffered_stdout: Vec::with_capacity(PROJECT_BRIDGE_INITIAL_RESPONSE_BUFFER_BYTES),
            }),
        }
    }

    fn request(
        self: &Arc<Self>,
        bridge_state: &ProjectBridgeState,
        request_generation: usize,
        request_json: &str,
        execution_timeout: Option<Duration>,
        request_read_generation: Option<usize>,
    ) -> Result<String, ProjectBridgeRequestFailure> {
        let request_token = self.allocate_request_token();
        let _admission = match self.admission.acquire(request_token) {
            Ok(admission) => admission,
            Err(ProjectBridgeAdmissionFailure::Poisoned) => {
                return Err(ProjectBridgeRequestFailure::Retryable(
                    "Project bridge admission queue was poisoned.".to_owned(),
                ));
            }
        };
        let mut io = self.io.lock().map_err(|_| {
            ProjectBridgeRequestFailure::Retryable(
                "Project bridge I/O lock was poisoned.".to_owned(),
            )
        })?;
        let _active_request = ProjectBridgeActiveRequest::begin(
            &self.active_request_token,
            &self.active_preemptible_request,
            request_token,
            request_read_generation.is_some(),
        );
        let watchdog = execution_timeout.map(|timeout| {
            ProjectBridgeRequestWatchdog::start(
                bridge_state.clone(),
                request_generation,
                self.clone(),
                request_token,
                timeout,
            )
        });
        let request_cancellation = ProjectBridgeRequestCancellation {
            request_state: watchdog
                .as_ref()
                .map(ProjectBridgeRequestWatchdog::request_state),
            generation: bridge_state.generation.clone(),
            request_generation,
            read_generation: request_read_generation.map(|_| bridge_state.read_generation.clone()),
            request_read_generation,
        };

        let request_result = (|| -> Result<String, ProjectBridgeRequestFailure> {
            ensure_project_bridge_request_is_current(bridge_state, request_generation)
                .map_err(ProjectBridgeRequestFailure::Retryable)?;
            write_project_bridge_request_with_cancellation(
                &mut io.stdin,
                request_json,
                &request_cancellation,
            )?;

            let ProjectBridgeIo {
                stdout,
                buffered_stdout,
                ..
            } = &mut *io;
            let mut response = read_bounded_project_bridge_response(
                stdout,
                buffered_stdout,
                &request_cancellation,
            )?;

            while response.ends_with(['\r', '\n']) {
                response.pop();
            }

            ensure_project_bridge_request_epoch_is_current(
                bridge_state,
                request_generation,
                request_read_generation,
            )
            .map_err(ProjectBridgeRequestFailure::Retryable)?;
            Ok(response)
        })();
        let timed_out = watchdog.is_some_and(ProjectBridgeRequestWatchdog::finish);
        if timed_out {
            return Err(ProjectBridgeRequestFailure::TimedOut(
                create_project_bridge_timeout_error(
                    execution_timeout.unwrap_or(PROJECT_BRIDGE_PROJECT_READ_TIMEOUT),
                ),
            ));
        }

        request_result
    }

    fn allocate_request_token(&self) -> usize {
        loop {
            let request_token = self.next_request_token.fetch_add(1, Ordering::Relaxed);
            if request_token != PROJECT_BRIDGE_NO_ACTIVE_REQUEST_TOKEN {
                return request_token;
            }
        }
    }

    fn terminate(&self) -> Result<(), String> {
        let mut child = self
            .child
            .lock()
            .map_err(|_| "Project bridge child lock was poisoned.".to_owned())?;
        terminate_project_bridge_child(&mut child)
    }
}

impl ProjectBridgeAdmission {
    fn acquire(
        &self,
        request_token: usize,
    ) -> Result<ProjectBridgeAdmissionGuard<'_>, ProjectBridgeAdmissionFailure> {
        let mut state = self
            .state
            .lock()
            .map_err(|_| ProjectBridgeAdmissionFailure::Poisoned)?;
        state.waiting_request_tokens.push_back(request_token);

        loop {
            if !state.active && state.waiting_request_tokens.front().copied() == Some(request_token)
            {
                state.waiting_request_tokens.pop_front();
                state.active = true;
                return Ok(ProjectBridgeAdmissionGuard { admission: self });
            }

            state = self
                .changed
                .wait(state)
                .map_err(|_| ProjectBridgeAdmissionFailure::Poisoned)?;
        }
    }
}

impl Drop for ProjectBridgeAdmissionGuard<'_> {
    fn drop(&mut self) {
        let mut state = match self.admission.state.lock() {
            Ok(state) => state,
            Err(poisoned) => poisoned.into_inner(),
        };
        state.active = false;
        self.admission.changed.notify_all();
    }
}

fn write_project_bridge_request_with_cancellation(
    stdin: &mut Option<ChildStdin>,
    request_json: &str,
    cancellation: &ProjectBridgeRequestCancellation,
) -> Result<(), ProjectBridgeRequestFailure> {
    cancellation.ensure_active()?;
    let mut request_stdin = stdin.take().ok_or_else(|| {
        ProjectBridgeRequestFailure::Retryable(
            "Project bridge request pipe was unavailable.".to_owned(),
        )
    })?;
    let mut request = Vec::with_capacity(request_json.len().saturating_add(1));
    request.extend_from_slice(request_json.as_bytes());
    request.push(b'\n');
    let (completion_sender, completion_receiver) = mpsc::channel();
    std::thread::Builder::new()
        .name("km-project-bridge-write".to_owned())
        .spawn(move || {
            let result = request_stdin
                .write_all(&request)
                .and_then(|_| request_stdin.flush());
            let _ = completion_sender.send((request_stdin, result));
        })
        .map_err(|error| {
            ProjectBridgeRequestFailure::Retryable(format!(
                "Could not start the project bridge request writer: {error}"
            ))
        })?;

    loop {
        cancellation.ensure_active()?;
        match completion_receiver.recv_timeout(PROJECT_BRIDGE_IO_POLL_INTERVAL) {
            Ok((returned_stdin, result)) => {
                *stdin = Some(returned_stdin);
                return result.map_err(|error| {
                    ProjectBridgeRequestFailure::Retryable(format!(
                        "Could not send the project bridge request: {error}"
                    ))
                });
            }
            Err(mpsc::RecvTimeoutError::Timeout) => continue,
            Err(mpsc::RecvTimeoutError::Disconnected) => {
                return Err(ProjectBridgeRequestFailure::Retryable(
                    "Project bridge request writer stopped unexpectedly.".to_owned(),
                ));
            }
        }
    }
}

fn read_bounded_project_bridge_response(
    stdout: &mut ChildStdout,
    buffered_stdout: &mut Vec<u8>,
    cancellation: &ProjectBridgeRequestCancellation,
) -> Result<String, ProjectBridgeRequestFailure> {
    let mut response_scan_offset = 0;
    loop {
        cancellation.ensure_active()?;
        if let Some(response) =
            take_project_bridge_response_frame(buffered_stdout, &mut response_scan_offset)?
        {
            cancellation.ensure_active()?;
            return decode_project_bridge_response(response);
        }

        let readiness = wait_for_project_bridge_stdout_with_cancellation(cancellation, || {
            wait_for_project_bridge_stdout(stdout, PROJECT_BRIDGE_IO_POLL_INTERVAL)
        })?;
        match readiness {
            ProjectBridgeStdoutReadiness::Pending => continue,
            ProjectBridgeStdoutReadiness::Closed => {
                reset_project_bridge_response_buffer(buffered_stdout);
                return Err(project_bridge_incomplete_response_failure());
            }
            ProjectBridgeStdoutReadiness::Ready(available_bytes) => {
                let read_bytes = available_bytes.clamp(1, PROJECT_BRIDGE_READ_CHUNK_BYTES);
                let mut chunk = vec![0_u8; read_bytes];
                match stdout.read(&mut chunk) {
                    Ok(0) => {
                        reset_project_bridge_response_buffer(buffered_stdout);
                        return Err(project_bridge_incomplete_response_failure());
                    }
                    Ok(count) => buffered_stdout.extend_from_slice(&chunk[..count]),
                    Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
                    Err(error) if error.kind() == io::ErrorKind::WouldBlock => continue,
                    Err(error) => {
                        return Err(ProjectBridgeRequestFailure::Retryable(format!(
                            "Could not read the project bridge response: {error}"
                        )));
                    }
                }
            }
        }
    }
}

fn take_project_bridge_response_frame(
    buffered_stdout: &mut Vec<u8>,
    response_scan_offset: &mut usize,
) -> Result<Option<Vec<u8>>, ProjectBridgeRequestFailure> {
    take_project_bridge_response_frame_with_limit(
        buffered_stdout,
        response_scan_offset,
        MAX_PROJECT_BRIDGE_FRAMED_RESPONSE_BYTES,
    )
}

fn take_project_bridge_response_frame_with_limit(
    buffered_stdout: &mut Vec<u8>,
    response_scan_offset: &mut usize,
    maximum_framed_response_bytes: usize,
) -> Result<Option<Vec<u8>>, ProjectBridgeRequestFailure> {
    let search_start = (*response_scan_offset).min(buffered_stdout.len());
    let newline = buffered_stdout[search_start..]
        .iter()
        .position(|byte| *byte == b'\n');
    if let Some(relative_index) = newline {
        let index = search_start + relative_index;
        let frame_length = index + 1;
        if frame_length > maximum_framed_response_bytes {
            reset_project_bridge_response_buffer(buffered_stdout);
            *response_scan_offset = 0;
            return Err(project_bridge_response_size_failure());
        }

        let remainder = buffered_stdout.split_off(frame_length);
        let response = std::mem::replace(buffered_stdout, remainder);
        *response_scan_offset = 0;
        return Ok(Some(response));
    }

    if buffered_stdout.len() > maximum_framed_response_bytes {
        reset_project_bridge_response_buffer(buffered_stdout);
        *response_scan_offset = 0;
        return Err(project_bridge_response_size_failure());
    }

    *response_scan_offset = buffered_stdout.len();
    Ok(None)
}

fn decode_project_bridge_response(
    mut response: Vec<u8>,
) -> Result<String, ProjectBridgeRequestFailure> {
    if response.is_empty() {
        return Err(ProjectBridgeRequestFailure::Retryable(
            "Project bridge runner returned an empty response.".to_owned(),
        ));
    }

    let had_line_feed = response.last() == Some(&b'\n');
    if had_line_feed {
        response.pop();
        if response.last() == Some(&b'\r') {
            response.pop();
        }
    }

    if response.len() > MAX_PROJECT_BRIDGE_RESPONSE_BYTES {
        return Err(project_bridge_response_size_failure());
    }

    String::from_utf8(response).map_err(|_| {
        ProjectBridgeRequestFailure::Retryable(
            "Project bridge runner returned a response that was not valid UTF-8.".to_owned(),
        )
    })
}

fn project_bridge_response_size_failure() -> ProjectBridgeRequestFailure {
    ProjectBridgeRequestFailure::NonRetryable(
        "Project bridge response exceeded the supported size limit.".to_owned(),
    )
}

fn project_bridge_incomplete_response_failure() -> ProjectBridgeRequestFailure {
    ProjectBridgeRequestFailure::Retryable(
        "Project bridge runner closed before completing its newline-delimited response.".to_owned(),
    )
}

fn reset_project_bridge_response_buffer(buffered_stdout: &mut Vec<u8>) {
    *buffered_stdout = Vec::with_capacity(PROJECT_BRIDGE_INITIAL_RESPONSE_BUFFER_BYTES);
}

impl ProjectBridgeRequestCancellation {
    fn ensure_active(&self) -> Result<(), ProjectBridgeRequestFailure> {
        if self
            .request_state
            .as_ref()
            .is_some_and(|state| state.load(Ordering::Acquire) != PROJECT_BRIDGE_REQUEST_RUNNING)
        {
            return Err(ProjectBridgeRequestFailure::Retryable(
                "Project bridge response read was canceled by the active request watchdog."
                    .to_owned(),
            ));
        }

        if self.generation.load(Ordering::Acquire) != self.request_generation {
            return Err(ProjectBridgeRequestFailure::Retryable(
                PROJECT_BRIDGE_RECYCLED_ERROR.to_owned(),
            ));
        }
        if let (Some(read_generation), Some(request_read_generation)) =
            (&self.read_generation, self.request_read_generation)
        {
            if read_generation.load(Ordering::Acquire) != request_read_generation {
                return Err(ProjectBridgeRequestFailure::Retryable(
                    PROJECT_BRIDGE_READ_PREEMPTED_ERROR.to_owned(),
                ));
            }
        }

        Ok(())
    }
}

fn wait_for_project_bridge_stdout_with_cancellation(
    cancellation: &ProjectBridgeRequestCancellation,
    wait: impl FnOnce() -> io::Result<ProjectBridgeStdoutReadiness>,
) -> Result<ProjectBridgeStdoutReadiness, ProjectBridgeRequestFailure> {
    cancellation.ensure_active()?;
    let readiness = wait().map_err(|error| {
        ProjectBridgeRequestFailure::Retryable(format!(
            "Could not inspect the project bridge response pipe: {error}"
        ))
    })?;
    cancellation.ensure_active()?;
    Ok(readiness)
}

#[cfg(windows)]
fn wait_for_project_bridge_stdout(
    stdout: &ChildStdout,
    poll_interval: Duration,
) -> io::Result<ProjectBridgeStdoutReadiness> {
    let mut available_bytes = 0_u32;
    let succeeded = unsafe {
        peek_named_pipe(
            stdout.as_raw_handle(),
            std::ptr::null_mut(),
            0,
            std::ptr::null_mut(),
            &mut available_bytes,
            std::ptr::null_mut(),
        )
    };
    if succeeded == 0 {
        let error = io::Error::last_os_error();
        return match error.raw_os_error() {
            Some(109 | 232) => Ok(ProjectBridgeStdoutReadiness::Closed),
            _ => Err(error),
        };
    }

    if available_bytes == 0 {
        std::thread::sleep(poll_interval);
        return Ok(ProjectBridgeStdoutReadiness::Pending);
    }

    Ok(ProjectBridgeStdoutReadiness::Ready(
        available_bytes as usize,
    ))
}

#[cfg(unix)]
fn wait_for_project_bridge_stdout(
    stdout: &ChildStdout,
    poll_interval: Duration,
) -> io::Result<ProjectBridgeStdoutReadiness> {
    const POLLIN: i16 = 0x0001;
    const POLLERR: i16 = 0x0008;
    const POLLHUP: i16 = 0x0010;
    const POLLNVAL: i16 = 0x0020;

    let mut descriptor = ProjectBridgePollFd {
        fd: stdout.as_raw_fd(),
        events: POLLIN,
        revents: 0,
    };
    let timeout_milliseconds = poll_interval.as_millis().min(i32::MAX as u128) as i32;
    let result = unsafe { project_bridge_poll(&mut descriptor, 1, timeout_milliseconds) };
    if result < 0 {
        let error = io::Error::last_os_error();
        return if error.kind() == io::ErrorKind::Interrupted {
            Ok(ProjectBridgeStdoutReadiness::Pending)
        } else {
            Err(error)
        };
    }
    if result == 0 {
        return Ok(ProjectBridgeStdoutReadiness::Pending);
    }
    if descriptor.revents & POLLNVAL != 0 {
        return Err(io::Error::from_raw_os_error(9));
    }
    if descriptor.revents & (POLLIN | POLLERR | POLLHUP) != 0 {
        return Ok(ProjectBridgeStdoutReadiness::Ready(
            PROJECT_BRIDGE_READ_CHUNK_BYTES,
        ));
    }

    Ok(ProjectBridgeStdoutReadiness::Pending)
}

#[cfg(not(any(unix, windows)))]
fn wait_for_project_bridge_stdout(
    _stdout: &ChildStdout,
    poll_interval: Duration,
) -> io::Result<ProjectBridgeStdoutReadiness> {
    std::thread::sleep(poll_interval);
    Ok(ProjectBridgeStdoutReadiness::Ready(1))
}

#[cfg(windows)]
#[link(name = "kernel32")]
unsafe extern "system" {
    #[link_name = "PeekNamedPipe"]
    fn peek_named_pipe(
        pipe: *mut c_void,
        buffer: *mut c_void,
        buffer_size: u32,
        bytes_read: *mut u32,
        total_bytes_available: *mut u32,
        bytes_left_this_message: *mut u32,
    ) -> i32;

    #[link_name = "GlobalMemoryStatusEx"]
    fn global_memory_status_ex(status: *mut ProjectBridgeMemoryStatus) -> i32;
}

#[cfg(windows)]
#[repr(C)]
struct ProjectBridgeMemoryStatus {
    length: u32,
    memory_load: u32,
    total_physical: u64,
    available_physical: u64,
    total_page_file: u64,
    available_page_file: u64,
    total_virtual: u64,
    available_virtual: u64,
    available_extended_virtual: u64,
}

#[cfg(unix)]
#[repr(C)]
struct ProjectBridgePollFd {
    fd: i32,
    events: i16,
    revents: i16,
}

#[cfg(target_os = "linux")]
type ProjectBridgePollCount = usize;
#[cfg(all(unix, not(target_os = "linux")))]
type ProjectBridgePollCount = u32;

#[cfg(unix)]
unsafe extern "C" {
    #[link_name = "poll"]
    fn project_bridge_poll(
        descriptors: *mut ProjectBridgePollFd,
        descriptor_count: ProjectBridgePollCount,
        timeout_milliseconds: i32,
    ) -> i32;
}

impl<'a> ProjectBridgeActiveRequest<'a> {
    fn begin(
        active_request_token: &'a AtomicUsize,
        active_preemptible_request: &'a AtomicBool,
        request_token: usize,
        is_preemptible: bool,
    ) -> Self {
        debug_assert_ne!(request_token, PROJECT_BRIDGE_NO_ACTIVE_REQUEST_TOKEN);
        active_preemptible_request.store(is_preemptible, Ordering::Release);
        active_request_token.store(request_token, Ordering::Release);
        Self {
            active_request_token,
            active_preemptible_request,
            request_token,
        }
    }
}

impl Drop for ProjectBridgeActiveRequest<'_> {
    fn drop(&mut self) {
        self.active_preemptible_request
            .store(false, Ordering::Release);
        let _ = self.active_request_token.compare_exchange(
            self.request_token,
            PROJECT_BRIDGE_NO_ACTIVE_REQUEST_TOKEN,
            Ordering::AcqRel,
            Ordering::Acquire,
        );
    }
}

impl ProjectBridgeRequestWatchdog {
    fn start(
        bridge_state: ProjectBridgeState,
        request_generation: usize,
        process: Arc<ProjectBridgeProcess>,
        request_token: usize,
        timeout: Duration,
    ) -> Self {
        let request_state = Arc::new(AtomicUsize::new(PROJECT_BRIDGE_REQUEST_RUNNING));
        let watchdog_request_state = request_state.clone();
        let (cancellation_sender, cancellation_receiver) = mpsc::channel();
        let thread = std::thread::spawn(move || {
            if cancellation_receiver.recv_timeout(timeout) != Err(mpsc::RecvTimeoutError::Timeout) {
                return;
            }

            if watchdog_request_state
                .compare_exchange(
                    PROJECT_BRIDGE_REQUEST_RUNNING,
                    PROJECT_BRIDGE_REQUEST_TIMED_OUT,
                    Ordering::AcqRel,
                    Ordering::Acquire,
                )
                .is_ok()
                && active_project_bridge_request_matches(
                    &process.active_request_token,
                    request_token,
                )
            {
                let _ = timeout_project_bridge_process(&bridge_state, request_generation, &process);
            }
        });

        Self {
            cancellation_sender,
            request_state,
            thread: Some(thread),
        }
    }

    fn request_state(&self) -> Arc<AtomicUsize> {
        self.request_state.clone()
    }

    fn finish(mut self) -> bool {
        let _ = self.request_state.compare_exchange(
            PROJECT_BRIDGE_REQUEST_RUNNING,
            PROJECT_BRIDGE_REQUEST_COMPLETED,
            Ordering::AcqRel,
            Ordering::Acquire,
        );
        let _ = self.cancellation_sender.send(());
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }

        self.request_state.load(Ordering::Acquire) == PROJECT_BRIDGE_REQUEST_TIMED_OUT
    }
}

fn active_project_bridge_request_matches(
    active_request_token: &AtomicUsize,
    request_token: usize,
) -> bool {
    request_token != PROJECT_BRIDGE_NO_ACTIVE_REQUEST_TOKEN
        && active_request_token.load(Ordering::Acquire) == request_token
}

fn project_bridge_request_policy(request_json: &str) -> Option<ProjectBridgeRequestPolicy> {
    let request: serde_json::Value = serde_json::from_str(request_json).ok()?;
    let command = request.get("command")?.as_str()?;
    let concurrency = project_bridge_command_concurrency(command)?;
    let recycles_read_workers = project_bridge_command_recycles_read_workers(command);
    let recycle_read_workers_before_execution = recycles_read_workers
        && matches!(
            concurrency,
            ProjectBridgeCommandConcurrency::Exclusive
                | ProjectBridgeCommandConcurrency::OwnerOrdered
        );
    let writer_priority_before_execution =
        recycle_read_workers_before_execution || command == "changePlan.create";
    let recycle_read_workers_after_execution = recycles_read_workers
        && matches!(
            concurrency,
            ProjectBridgeCommandConcurrency::AffinityExclusive(_)
        );

    let (bounded_execution_timeout, bounded_retry_after_transport_failure) =
        if is_replay_safe_edit_session_command(command) {
            (PROJECT_BRIDGE_EDITOR_OPERATION_TIMEOUT, true)
        } else if matches!(
            command,
            "changeSets.captureSession"
                | "changePlan.apply"
                | "dynamaxAdventures.seed.save.set"
                | "gameplaySettings.update.preview"
                | "gameplaySettings.update.apply"
        ) {
            (PROJECT_BRIDGE_EDITOR_OPERATION_TIMEOUT, false)
        } else if matches!(
            command,
            "project.open"
                | "project.validate"
                | "project.fileGraph.refresh"
                | "randomizer.seed.import"
                | "workflow.list"
                | "workspace.drafts.read"
                | "workspace.applicationState.read"
                | "workspace.projectState.read"
        ) {
            (PROJECT_BRIDGE_PROJECT_READ_TIMEOUT, true)
        } else if command == "output.cleanup.preview" {
            (PROJECT_BRIDGE_OUTPUT_READ_TIMEOUT, false)
        } else if command == "output.history.list" {
            (PROJECT_BRIDGE_OUTPUT_READ_TIMEOUT, true)
        } else if is_project_bridge_workflow_load_command(command)
            || matches!(
                command,
                "placement.catalog.open"
                    | "placement.catalog.query"
                    | "svCache.status"
                    | "svCache.settings.update"
                    | "svCache.clear"
                    | "svCache.warmup.step"
                    | "zaCache.status"
                    | "zaCache.settings.update"
                    | "zaCache.clear"
                    | "zaCache.warmup.step"
                    | "swshCache.status"
                    | "swshCache.settings.update"
                    | "swshCache.clear"
                    | "swshCache.warmup.step"
                    | "output.recovery.status"
                    | "output.integrity.scan"
                    | "output.checkpoint.list"
                    | "output.checkpoint.restore.preview"
                    | "project.relocation.preview"
                    | "changeSets.read"
                    | "changeSets.materialize"
                    | "changeSets.export"
                    | "project.sourceRevision.read"
                    | "semantic.capabilities"
                    | "semantic.search"
                    | "semantic.entity"
                    | "semantic.compare"
                    | "semantic.references"
                    | "semantic.impact"
                    | "semantic.ownership"
                    | "semantic.external.compare"
                    | "semantic.changes"
                    | "semantic.balance-lab"
                    | "guidedDesign.capabilities"
                    | "guidedDesign.preview"
                    | "gameplaySettings.get"
                    | "semanticMerge.capabilities"
                    | "semanticMerge.source.open"
                    | "gameModules.capabilities"
                    | "gameModules.query"
                    | "modMerger.stage"
                    | "svModMerger.stage"
                    | "zaModMerger.stage"
                    | "researchLab.capabilities"
                    | "researchLab.annotations.read"
                    | "recipes.export"
                    | "recipes.validate"
                    | "support.report.build"
            )
        {
            (PROJECT_BRIDGE_WORKFLOW_LOAD_TIMEOUT, true)
        } else {
            // Every routed command reaches this explicit conservative policy if it has not received a
            // narrower reviewed timeout/replay contract above.
            (PROJECT_BRIDGE_DEFAULT_OPERATION_TIMEOUT, false)
        };

    // A watchdog may terminate only work whose effects are stateless or explicitly recoverable.
    // Durable exclusive commands keep their barrier until the managed command returns, which is
    // also the point where output transactions have either finalized or left a durable recovery
    // journal. Transport replay is disabled for the same commands because a disconnected caller
    // cannot know whether publication already completed.
    let (execution_timeout, retry_after_transport_failure) =
        if project_bridge_command_requires_uninterrupted_completion(command, concurrency) {
            (None, false)
        } else {
            (
                Some(bounded_execution_timeout),
                bounded_retry_after_transport_failure,
            )
        };

    Some(ProjectBridgeRequestPolicy {
        execution_timeout,
        retry_after_transport_failure,
        concurrency,
        writer_priority_before_execution,
        recycle_read_workers_before_execution,
        recycle_read_workers_after_execution,
    })
}

fn resolved_project_bridge_request_policy(request_json: &str) -> ProjectBridgeRequestPolicy {
    project_bridge_request_policy(request_json).unwrap_or(ProjectBridgeRequestPolicy {
        execution_timeout: Some(PROJECT_BRIDGE_DEFAULT_OPERATION_TIMEOUT),
        retry_after_transport_failure: false,
        concurrency: ProjectBridgeCommandConcurrency::Exclusive,
        writer_priority_before_execution: false,
        recycle_read_workers_before_execution: false,
        recycle_read_workers_after_execution: false,
    })
}

fn project_bridge_command_requires_uninterrupted_completion(
    command: &str,
    concurrency: ProjectBridgeCommandConcurrency,
) -> bool {
    matches!(
        concurrency,
        ProjectBridgeCommandConcurrency::AffinityExclusive(_)
            | ProjectBridgeCommandConcurrency::Exclusive
    ) && !project_bridge_command_allows_bounded_recovery(command)
}

fn project_bridge_command_allows_bounded_recovery(command: &str) -> bool {
    // Cache payloads are derived, disposable, and atomically published. These maintenance calls
    // can therefore retain watchdog recovery without risking project or output corruption.
    matches!(
        command,
        "svCache.clear"
            | "svCache.settings.update"
            | "svCache.status"
            | "svCache.warmup.step"
            | "swshCache.clear"
            | "swshCache.settings.update"
            | "swshCache.status"
            | "swshCache.warmup.step"
            | "zaCache.clear"
            | "zaCache.settings.update"
            | "zaCache.status"
            | "zaCache.warmup.step"
    )
}

fn project_bridge_command_concurrency(command: &str) -> Option<ProjectBridgeCommandConcurrency> {
    if ROUTED_PROJECT_BRIDGE_COMMANDS
        .binary_search(&command)
        .is_err()
    {
        return None;
    }

    if matches!(command, "semanticMerge.import" | "recipes.import") {
        return Some(ProjectBridgeCommandConcurrency::AffinityExclusive(
            ProjectBridgeReadAffinity::SemanticMerge,
        ));
    }
    if matches!(
        command,
        "recipes.export" | "recipes.preview" | "semanticMerge.preview"
    ) {
        return Some(ProjectBridgeCommandConcurrency::AffinityOrdered(
            ProjectBridgeReadAffinity::SemanticMerge,
        ));
    }
    if command == "researchLab.annotations.mutate" {
        return Some(ProjectBridgeCommandConcurrency::AffinityExclusive(
            ProjectBridgeReadAffinity::ResearchLab,
        ));
    }
    if matches!(
        command,
        "researchLab.byteWindow"
            | "researchLab.compare"
            | "researchLab.source.close"
            | "researchLab.source.open"
    ) {
        return Some(ProjectBridgeCommandConcurrency::AffinityOrdered(
            ProjectBridgeReadAffinity::ResearchLab,
        ));
    }

    let affinity = if matches!(
        command,
        "semantic.capabilities"
            | "project.sourceRevision.read"
            | "semantic.search"
            | "semantic.entity"
            | "semantic.compare"
            | "semantic.references"
            | "semantic.impact"
            | "semantic.ownership"
            | "semantic.external.compare"
            | "semantic.changes"
    ) {
        Some(ProjectBridgeReadAffinity::SemanticExplore)
    } else if command == "semantic.balance-lab" {
        Some(ProjectBridgeReadAffinity::BalanceLab)
    } else if matches!(command, "gameModules.capabilities" | "gameModules.query") {
        Some(ProjectBridgeReadAffinity::GameModules)
    } else if matches!(
        command,
        "guidedDesign.capabilities" | "guidedDesign.preview"
    ) {
        Some(ProjectBridgeReadAffinity::GuidedDesign)
    } else if matches!(
        command,
        "semanticMerge.capabilities"
            | "semanticMerge.source.open"
            | "semanticMerge.preview"
            | "recipes.validate"
            | "recipes.preview"
    ) {
        Some(ProjectBridgeReadAffinity::SemanticMerge)
    } else if matches!(
        command,
        "researchLab.capabilities"
            | "researchLab.source.open"
            | "researchLab.compare"
            | "researchLab.byteWindow"
            | "researchLab.annotations.read"
    ) {
        Some(ProjectBridgeReadAffinity::ResearchLab)
    } else if is_project_bridge_workflow_read_command(command) {
        Some(ProjectBridgeReadAffinity::Workflow(
            stable_project_bridge_affinity_hash(command),
        ))
    } else if matches!(
        command,
        "editSession.start"
            | "gameplaySettings.get"
            | "inGameSettingsPackage.inspect"
            | "output.recovery.status"
            | "output.history.list"
            | "output.checkpoint.list"
            | "project.relocation.preview"
            | "randomizer.seed.import"
            | "support.report.build"
            | "workflow.list"
            | "workspace.drafts.read"
            | "workspace.applicationState.read"
            | "workspace.projectState.read"
    ) {
        Some(ProjectBridgeReadAffinity::GeneralProject)
    } else {
        None
    };

    Some(match affinity {
        Some(affinity) => ProjectBridgeCommandConcurrency::IndependentRead(affinity),
        None if project_bridge_command_requires_exclusive_barrier(command) => {
            ProjectBridgeCommandConcurrency::Exclusive
        }
        None => ProjectBridgeCommandConcurrency::OwnerOrdered,
    })
}

fn stable_project_bridge_affinity_hash(command: &str) -> u64 {
    let family = command
        .split_once('.')
        .map_or(command, |(family, _)| family);
    family
        .as_bytes()
        .iter()
        .fold(0xcbf2_9ce4_8422_2325, |hash, byte| {
            (hash ^ u64::from(*byte)).wrapping_mul(0x0000_0100_0000_01b3)
        })
}

fn is_project_bridge_workflow_read_command(command: &str) -> bool {
    is_project_bridge_workflow_load_command(command)
        || matches!(
            command,
            "dynamaxAdventures.defaults.preview"
                | "dynamaxAdventures.seed.plan"
                | "dynamaxAdventures.seed.search"
                | "modMerger.stage"
                | "placement.catalog.open"
                | "placement.catalog.query"
                | "spreadsheetImport.preview"
                | "svModMerger.stage"
                | "zaModMerger.stage"
        )
}

fn is_project_bridge_workflow_load_command(command: &str) -> bool {
    matches!(
        command,
        "angeFight.load"
            | "bagHook.load"
            | "battleCafeRewards.load"
            | "behavior.load"
            | "catchCap.load"
            | "dynamaxAdventures.load"
            | "encounters.load"
            | "exefsPatches.load"
            | "fairyGymBoosts.load"
            | "fashionCatalog.load"
            | "fashionUnlock.load"
            | "flagworkSave.load"
            | "fpsPatch.load"
            | "gameDump.load"
            | "giftPokemon.load"
            | "gymUniformRemoval.load"
            | "habitatCoordinates.load"
            | "hyperspaceBypass.load"
            | "hyperTraining.load"
            | "items.load"
            | "ivScreen.load"
            | "modMerger.load"
            | "moves.load"
            | "npcItemGift.load"
            | "placement.load"
            | "placement.object.load"
            | "pokemon.load"
            | "profanityFilter.load"
            | "raidBattles.load"
            | "raidBonusRewards.load"
            | "raidRewards.load"
            | "rentalPokemon.load"
            | "royalCandy.load"
            | "shinyRate.load"
            | "shops.load"
            | "spreadsheetImport.load"
            | "startingItems.load"
            | "staticEncounters.load"
            | "svModMerger.load"
            | "teraRaids.load"
            | "text.load"
            | "tmMachineControls.load"
            | "tradePokemon.load"
            | "trainerPools.load"
            | "trainers.load"
            | "typeChart.load"
            | "zaModMerger.load"
    )
}

fn project_bridge_command_requires_exclusive_barrier(command: &str) -> bool {
    matches!(
        command,
        "changePlan.apply"
            | "changeSets.captureSession"
            | "changeSets.import"
            | "changeSets.mutate"
            | "dynamaxAdventures.seed.save.set"
            | "fpsPatch.apply"
            | "fpsPatch.restore"
            | "gameDump.run"
            | "gameplaySettings.update.apply"
            | "guidedDesign.import"
            | "inGameSettingsPackage.apply"
            | "modMerger.apply"
            | "output.checkpoint.create"
            | "output.checkpoint.delete"
            | "output.checkpoint.restore"
            | "output.cleanup.apply"
            | "output.recovery.reconcile"
            | "profanityFilter.apply"
            | "profanityFilter.restore"
            | "project.fileGraph.refresh"
            | "project.open"
            | "project.relocation.apply"
            | "project.validate"
            | "randomizer.apply"
            | "randomizer.restore"
            | "svCache.clear"
            | "svCache.settings.update"
            | "svCache.status"
            | "svCache.warmup.step"
            | "svModMerger.apply"
            | "swshCache.clear"
            | "swshCache.settings.update"
            | "swshCache.status"
            | "swshCache.warmup.step"
            | "workspace.applicationState.write"
            | "workspace.drafts.delete"
            | "workspace.drafts.write"
            | "workspace.projectState.delete"
            | "workspace.projectState.write"
            | "zaCache.clear"
            | "zaCache.settings.update"
            | "zaCache.status"
            | "zaCache.warmup.step"
            | "zaModMerger.apply"
    )
}

fn project_bridge_command_recycles_read_workers(command: &str) -> bool {
    matches!(
        command,
        "changePlan.apply"
            | "dynamaxAdventures.seed.save.set"
            | "fpsPatch.apply"
            | "fpsPatch.restore"
            | "gameDump.run"
            | "gameplaySettings.update.apply"
            | "inGameSettingsPackage.apply"
            | "modMerger.apply"
            | "output.checkpoint.create"
            | "output.checkpoint.delete"
            | "output.checkpoint.restore"
            | "output.cleanup.apply"
            | "output.recovery.reconcile"
            | "profanityFilter.apply"
            | "profanityFilter.restore"
            | "project.fileGraph.refresh"
            | "project.open"
            | "project.relocation.apply"
            | "project.validate"
            | "randomizer.apply"
            | "randomizer.restore"
            | "recipes.import"
            | "researchLab.annotations.mutate"
            | "semanticMerge.import"
            | "svCache.clear"
            | "svCache.settings.update"
            | "svModMerger.apply"
            | "swshCache.clear"
            | "swshCache.settings.update"
            | "zaCache.clear"
            | "zaCache.settings.update"
            | "zaModMerger.apply"
    )
}

fn is_replay_safe_edit_session_command(command: &str) -> bool {
    matches!(
        command,
        "angeFight.stage"
            | "angeFight.uninstall.stage"
            | "bagHook.install.stage"
            | "bagHook.uninstall.stage"
            | "battleCafeRewards.rows.stage"
            | "behavior.entry.update"
            | "behavior.fields.update"
            | "catchCap.stage"
            | "catchCap.uninstall.stage"
            | "changePlan.create"
            | "dynamaxAdventures.defaults.preview"
            | "dynamaxAdventures.field.update"
            | "dynamaxAdventures.fields.update"
            | "dynamaxAdventures.repair.stage"
            | "dynamaxAdventures.restore.stage"
            | "editSession.start"
            | "editSession.validate"
            | "encounters.slot.update"
            | "encounters.slot.vanilla.stage"
            | "encounters.slots.update"
            | "exefsPatches.patch.stage"
            | "fairyGymBoosts.stage"
            | "fashionCatalog.field.stage"
            | "fashionUnlock.install.stage"
            | "fashionUnlock.uninstall.stage"
            | "giftPokemon.field.update"
            | "giftPokemon.fields.update"
            | "giftPokemon.gift.vanilla.stage"
            | "gymUniformRemoval.install.stage"
            | "gymUniformRemoval.uninstall.stage"
            | "habitatCoordinates.coordinate.stage"
            | "hyperTraining.stage"
            | "hyperspaceBypass.install.stage"
            | "hyperspaceBypass.uninstall.stage"
            | "items.field.update"
            | "items.fields.update"
            | "items.item.vanilla.stage"
            | "ivScreen.install.stage"
            | "ivScreen.uninstall.stage"
            | "moves.field.update"
            | "moves.fields.update"
            | "moves.move.vanilla.stage"
            | "npcItemGift.stage"
            | "placement.object.update"
            | "placement.objects.update"
            | "pokemon.composite.update"
            | "pokemon.dex.megas.sync.stage"
            | "pokemon.dex.move"
            | "pokemon.dex.resize"
            | "pokemon.dex.swap"
            | "pokemon.dex.vanilla.stage"
            | "pokemon.evolution.update"
            | "pokemon.field.update"
            | "pokemon.fields.update"
            | "pokemon.learnset.update"
            | "raidBattles.slot.update"
            | "raidBattles.slots.update"
            | "raidBonusRewards.reward.update"
            | "raidBonusRewards.rewards.update"
            | "raidRewards.reward.update"
            | "raidRewards.rewards.update"
            | "rentalPokemon.field.update"
            | "rentalPokemon.fields.update"
            | "royalCandy.workflow.stage"
            | "shinyRate.stage"
            | "shops.inventory.items.update"
            | "shops.inventory.update"
            | "spreadsheetImport.preview"
            | "startingItems.stage"
            | "staticEncounters.field.update"
            | "staticEncounters.fields.update"
            | "teraRaids.field.update"
            | "teraRaids.fields.update"
            | "text.entry.update"
            | "tmMachineControls.materialVisibility.stage"
            | "tmMachineControls.recipeAvailability.stage"
            | "tradePokemon.field.update"
            | "tradePokemon.fields.update"
            | "trainerPools.fixedCountSwap.stage"
            | "trainers.field.update"
            | "trainers.fields.update"
            | "typeChart.stage"
            | "typeChart.uninstall.stage"
    )
}

fn timeout_project_bridge_process(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    timed_out_process: &Arc<ProjectBridgeProcess>,
) -> Result<(), String> {
    // An owner timeout invalidates every sidecar generation, but affinity-ordered work may be
    // finishing a process-held or file-producing response on a read worker at the same time. Mark
    // the lifecycle transition before waiting so no new protected request can enter, then wait
    // without holding the process-lifecycle lock that a protected request may need for recovery.
    let timed_out_owner = bridge_state
        .process
        .lock()
        .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?
        .as_ref()
        .is_some_and(|process| Arc::ptr_eq(process, timed_out_process));
    let protected_boundary = if timed_out_owner {
        let boundary = ProjectBridgeReadWorkerCleanup::begin(
            bridge_state.read_worker_requests.clone(),
            ProjectBridgeReadWorkerCleanupKind::OwnerTimeout,
        )?;
        boundary.wait_for_protected_response_boundary()?;
        Some(boundary)
    } else {
        None
    };

    let _lifecycle = bridge_state
        .process_lifecycle
        .lock()
        .map_err(|_| "Project bridge process lifecycle lock was poisoned.".to_owned())?;
    let removed = {
        if bridge_state.generation.load(Ordering::Acquire) != request_generation {
            return Ok(());
        }

        let mut current = bridge_state
            .process
            .lock()
            .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;
        if current
            .as_ref()
            .is_some_and(|process| Arc::ptr_eq(process, timed_out_process))
        {
            bridge_state.generation.fetch_add(1, Ordering::AcqRel);
            let mut removed = current.take().into_iter().collect::<Vec<_>>();
            drop(current);
            removed.extend(take_project_bridge_read_workers_unlocked(bridge_state)?);
            removed
        } else {
            drop(current);
            let mut removed = Vec::with_capacity(1);
            for process_slot in bridge_state.read_processes.iter() {
                let mut current = process_slot
                    .lock()
                    .map_err(|_| "Project bridge read-worker lock was poisoned.".to_owned())?;
                if current
                    .as_ref()
                    .is_some_and(|process| Arc::ptr_eq(process, timed_out_process))
                {
                    if let Some(process) = current.take() {
                        removed.push(process);
                    }
                    break;
                }
            }
            removed
        }
    };
    let result = terminate_project_bridge_processes(removed);
    drop(protected_boundary);
    result
}

fn create_project_bridge_timeout_error(timeout: Duration) -> String {
    format!(
        "The project request did not return within {} seconds. KM Editor stopped the stalled bridge so the interface can recover. No response was accepted. Refresh the current editor state before retrying because a durable request may have finished before the connection stopped.",
        timeout.as_secs()
    )
}

fn ensure_project_bridge_request_is_current(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
) -> Result<(), String> {
    if bridge_state.generation.load(Ordering::Acquire) == request_generation {
        Ok(())
    } else {
        Err(PROJECT_BRIDGE_RECYCLED_ERROR.to_owned())
    }
}

fn ensure_project_bridge_request_epoch_is_current(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    request_read_generation: Option<usize>,
) -> Result<(), String> {
    ensure_project_bridge_request_is_current(bridge_state, request_generation)?;
    if request_read_generation.is_some_and(|request_read_generation| {
        bridge_state.read_generation.load(Ordering::Acquire) != request_read_generation
    }) {
        Err(PROJECT_BRIDGE_READ_PREEMPTED_ERROR.to_owned())
    } else {
        Ok(())
    }
}

fn terminate_project_bridge_child(child: &mut Option<Child>) -> Result<(), String> {
    let Some(mut child) = child.take() else {
        return Ok(());
    };

    if let Ok(Some(_)) = child.try_wait() {
        return Ok(());
    }

    if let Err(kill_error) = child.kill() {
        match child.try_wait() {
            Ok(Some(_)) => return Ok(()),
            Ok(None) => {
                reap_project_bridge_child_in_background(child, true);
                return Err(format!(
                    "Could not terminate the project bridge runner: {kill_error}"
                ));
            }
            Err(wait_error) => {
                reap_project_bridge_child_in_background(child, true);
                return Err(format!(
                    "Could not terminate or inspect the project bridge runner: {kill_error}; {wait_error}"
                ));
            }
        }
    }

    let deadline = Instant::now() + PROJECT_BRIDGE_TERMINATION_WAIT_TIMEOUT;
    while Instant::now() < deadline {
        match child.try_wait() {
            Ok(Some(_)) => return Ok(()),
            Ok(None) => std::thread::sleep(PROJECT_BRIDGE_TERMINATION_POLL_INTERVAL),
            Err(error) => {
                reap_project_bridge_child_in_background(child, false);
                return Err(format!(
                    "Could not confirm that the project bridge runner stopped: {error}"
                ));
            }
        }
    }

    reap_project_bridge_child_in_background(child, false);
    Ok(())
}

fn reap_project_bridge_child_in_background(mut child: Child, retry_termination: bool) {
    std::thread::spawn(move || {
        if retry_termination {
            let _ = child.kill();
        }
        let _ = child.wait();
    });
}

fn start_project_bridge_process(
    app_handle: &tauri::AppHandle,
    read_worker_pool_size: Option<usize>,
) -> Result<ProjectBridgeProcess, String> {
    let mut command = resolve_project_bridge_command(app_handle, "bridge")?;
    configure_project_bridge_process_environment(&mut command, read_worker_pool_size);
    #[cfg(windows)]
    command.creation_flags(CREATE_NO_WINDOW);
    let mut child = command
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|error| format!("Could not start the project bridge runner: {error}"))?;
    let Some(stdin) = child.stdin.take() else {
        let _ = child.kill();
        let _ = child.wait();
        return Err("Project bridge runner did not expose stdin.".to_owned());
    };
    let Some(stdout) = child.stdout.take() else {
        let _ = child.kill();
        let _ = child.wait();
        return Err("Project bridge runner did not expose stdout.".to_owned());
    };

    Ok(ProjectBridgeProcess::new(child, stdin, stdout))
}

fn configure_project_bridge_process_environment(
    command: &mut Command,
    read_worker_pool_size: Option<usize>,
) {
    if let Some(worker_count) = read_worker_pool_size {
        let (cpu_budget, memory_budget) = project_bridge_worker_host_budgets(worker_count);
        command.env(PROJECT_BRIDGE_READ_WORKER_ENVIRONMENT_VARIABLE, "1");
        command.env(
            PROJECT_BRIDGE_CPU_LIMIT_ENVIRONMENT_VARIABLE,
            cpu_budget.to_string(),
        );
        command.env(
            PROJECT_BRIDGE_MEMORY_LIMIT_ENVIRONMENT_VARIABLE,
            memory_budget.to_string(),
        );
    } else {
        // The desktop process may itself have inherited this marker. The owning bridge must
        // always retain cache publication authority; only explicitly created read workers lose it.
        command.env_remove(PROJECT_BRIDGE_READ_WORKER_ENVIRONMENT_VARIABLE);
    }
}

fn resolve_project_bridge_command(
    app_handle: &tauri::AppHandle,
    bridge_mode: &str,
) -> Result<Command, String> {
    if let Some(command) = resolve_bundled_bridge_command(app_handle, bridge_mode)? {
        return Ok(command);
    }

    resolve_dev_bridge_command(bridge_mode)
}

fn resolve_bundled_bridge_command(
    app_handle: &tauri::AppHandle,
    bridge_mode: &str,
) -> Result<Option<Command>, String> {
    let sidecar_command = app_handle
        .shell()
        .sidecar(BRIDGE_SIDECAR_NAME)
        .map_err(|error| format!("Could not resolve the bundled project bridge sidecar: {error}"))?
        .arg(bridge_mode);
    let command: Command = sidecar_command.into();
    let program_path = Path::new(command.get_program());

    if program_path.is_file() {
        Ok(Some(command))
    } else {
        Ok(None)
    }
}

fn resolve_dev_bridge_command(bridge_mode: &str) -> Result<Command, String> {
    let repo_root = resolve_repo_root()?;
    let mut command = Command::new("dotnet");
    command
        .args([
            "run",
            "--project",
            "src/KM.Tools",
            "--no-restore",
            "--",
            bridge_mode,
        ])
        .current_dir(repo_root);

    Ok(command)
}

#[tauri::command(rename_all = "camelCase")]
fn open_path(path: String) -> Result<(), String> {
    let trimmed_path = path.trim();

    if trimmed_path.is_empty() {
        return Err("No folder path was provided.".to_owned());
    }

    let path = PathBuf::from(trimmed_path);

    if !path.is_dir() {
        return Err("The folder does not exist.".to_owned());
    }

    let mut command = create_open_path_command(&path);
    let child = command
        .spawn()
        .map_err(|error| format!("Could not open the folder: {error}"))?;
    reap_open_path_child_in_background(child);
    Ok(())
}

fn reap_open_path_child_in_background(mut child: Child) {
    std::thread::spawn(move || {
        let _ = child.wait();
    });
}

#[tauri::command(rename_all = "camelCase")]
fn create_directory(path: String) -> Result<(), String> {
    let trimmed_path = path.trim();

    if trimmed_path.is_empty() {
        return Err("No folder path was provided.".to_owned());
    }

    let path = PathBuf::from(trimmed_path);

    if path.exists() {
        return if path.is_dir() {
            Err("The output root folder already exists.".to_owned())
        } else {
            Err("A file already exists at the output root path.".to_owned())
        };
    }

    std::fs::create_dir(&path).map_err(|error| format!("Could not create the folder: {error}"))
}

#[tauri::command(rename_all = "camelCase")]
async fn find_support_file_folder(
    app_handle: tauri::AppHandle,
    search_state: tauri::State<'_, SupportSearchState>,
) -> Result<Option<String>, String> {
    let search_state = search_state.inner().clone();
    let generation = search_state.begin_search();
    tauri::async_runtime::spawn_blocking(move || {
        find_support_file_blocking(&app_handle, &search_state, generation)
    })
    .await
    .map_err(|error| format!("S/V support file search task failed: {error}"))?
}

#[tauri::command]
fn cancel_support_file_search(search_state: tauri::State<'_, SupportSearchState>) {
    search_state.cancel();
}

fn find_support_file_blocking(
    app_handle: &tauri::AppHandle,
    search_state: &SupportSearchState,
    generation: usize,
) -> Result<Option<String>, String> {
    let mut searched_directories = 0_u64;
    let mut searched_files = 0_u64;
    let mut last_emit = Instant::now()
        .checked_sub(Duration::from_secs(1))
        .unwrap_or_else(Instant::now);

    for root in enumerate_filesystem_roots() {
        ensure_support_search_is_current(search_state, generation)?;
        let root_label = root.display().to_string();
        let mut stack = vec![root.clone()];

        while let Some(directory) = stack.pop() {
            ensure_support_search_is_current(search_state, generation)?;
            searched_directories = searched_directories.saturating_add(1);
            if last_emit.elapsed() >= Duration::from_millis(200) {
                emit_support_search_progress(
                    app_handle,
                    &root_label,
                    &directory,
                    searched_directories,
                    searched_files,
                );
                last_emit = Instant::now();
            }

            let Ok(entries) = std::fs::read_dir(&directory) else {
                continue;
            };

            for entry in entries.flatten() {
                ensure_support_search_is_current(search_state, generation)?;
                let path = entry.path();
                let Ok(file_type) = entry.file_type() else {
                    continue;
                };

                if file_type.is_dir() {
                    stack.push(path);
                    continue;
                }

                if !file_type.is_file() {
                    continue;
                }

                searched_files = searched_files.saturating_add(1);
                if is_required_support_file(&entry.file_name().to_string_lossy()) {
                    emit_support_search_progress(
                        app_handle,
                        &root_label,
                        &path,
                        searched_directories,
                        searched_files,
                    );
                    return Ok(path.parent().map(|parent| parent.display().to_string()));
                }
            }
        }
    }

    Ok(None)
}

fn ensure_support_search_is_current(
    search_state: &SupportSearchState,
    generation: usize,
) -> Result<(), String> {
    search_state
        .is_current(generation)
        .then_some(())
        .ok_or_else(|| SUPPORT_SEARCH_CANCELED_ERROR.to_owned())
}

fn emit_support_search_progress(
    app_handle: &tauri::AppHandle,
    current_root: &str,
    current_path: &Path,
    searched_directories: u64,
    searched_files: u64,
) {
    let _ = app_handle.emit(
        SUPPORT_SEARCH_PROGRESS_EVENT,
        SupportSearchProgress {
            current_root: current_root.to_owned(),
            current_path: current_path.display().to_string(),
            searched_directories,
            searched_files,
        },
    );
}

fn is_required_support_file(file_name: &str) -> bool {
    file_name.eq_ignore_ascii_case(&required_support_file_name())
}

fn required_support_file_name() -> String {
    ["oo2", "core", "_8_", "win", "64", ".dll"].concat()
}

fn cleanup_stale_updater_temp_directories() {
    let _ = cleanup_stale_updater_temp_directories_in(
        &std::env::temp_dir(),
        SystemTime::now(),
        STALE_UPDATER_TEMP_DIRECTORY_AGE,
    );
}

fn cleanup_stale_updater_temp_directories_in(
    temp_root: &Path,
    now: SystemTime,
    minimum_age: Duration,
) -> std::io::Result<usize> {
    let mut removed = 0;

    for entry in std::fs::read_dir(temp_root)?.flatten() {
        let Some(name) = entry.file_name().to_str().map(str::to_owned) else {
            continue;
        };
        if !is_tauri_updater_temp_directory_name(&name) {
            continue;
        }

        let Ok(file_type) = entry.file_type() else {
            continue;
        };
        if !file_type.is_dir() {
            continue;
        }

        let Ok(metadata) = entry.metadata() else {
            continue;
        };
        let Ok(modified) = metadata.modified() else {
            continue;
        };
        let Ok(age) = now.duration_since(modified) else {
            continue;
        };
        if age < minimum_age {
            continue;
        }

        if std::fs::remove_dir_all(entry.path()).is_ok() {
            removed += 1;
        }
    }

    Ok(removed)
}

fn is_tauri_updater_temp_directory_name(name: &str) -> bool {
    let Some(remainder) = name.strip_prefix(UPDATER_TEMP_DIRECTORY_PREFIX) else {
        return false;
    };
    let Some((version, random_suffix)) = remainder.split_once(UPDATER_TEMP_DIRECTORY_MARKER) else {
        return false;
    };
    !version.is_empty() && !random_suffix.is_empty()
}

#[cfg(windows)]
fn enumerate_filesystem_roots() -> Vec<PathBuf> {
    ('A'..='Z')
        .map(|drive| PathBuf::from(format!("{drive}:\\")))
        .filter(|path| path.exists())
        .collect()
}

#[cfg(not(windows))]
fn enumerate_filesystem_roots() -> Vec<PathBuf> {
    vec![PathBuf::from("/")]
}

#[tauri::command]
fn set_close_guard_enabled(state: tauri::State<'_, CloseGuardState>, enabled: bool) {
    state.is_guarded.store(enabled, Ordering::SeqCst);
}

#[tauri::command]
async fn exit_app(
    app_handle: tauri::AppHandle,
    bridge_state: tauri::State<'_, ProjectBridgeState>,
) -> Result<(), String> {
    app_handle.state::<SupportSearchState>().cancel();
    recycle_project_bridge_after_active_requests(bridge_state.inner().clone()).await?;
    app_handle.exit(0);
    Ok(())
}

fn recycle_project_bridge_process(bridge_state: &ProjectBridgeState) -> Result<(), String> {
    let _lifecycle = bridge_state
        .process_lifecycle
        .lock()
        .map_err(|_| "Project bridge process lifecycle lock was poisoned.".to_owned())?;
    bridge_state.generation.fetch_add(1, Ordering::AcqRel);
    let mut processes = Vec::with_capacity(bridge_state.read_processes.len() + 1);
    if let Some(process) = bridge_state
        .process
        .lock()
        .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?
        .take()
    {
        processes.push(process);
    }
    processes.extend(take_project_bridge_read_workers_unlocked(bridge_state)?);
    terminate_project_bridge_processes(processes)
}

fn recycle_project_bridge_read_workers(bridge_state: &ProjectBridgeState) -> Result<(), String> {
    let _lifecycle = bridge_state
        .process_lifecycle
        .lock()
        .map_err(|_| "Project bridge process lifecycle lock was poisoned.".to_owned())?;
    terminate_project_bridge_processes(take_project_bridge_read_workers_unlocked(bridge_state)?)
}

fn preempt_project_bridge_read_workers(
    bridge_state: &ProjectBridgeState,
) -> Result<ProjectBridgeReadWorkerCleanup, String> {
    // Register a cleanup boundary before waiting. New disposable readers are rejected behind the
    // already-queued writer, while protected requests that crossed admission first are allowed to
    // finish. The condition variable releases its state lock while waiting, and the process
    // lifecycle lock is acquired only after the protected response boundary, avoiding inversion
    // with protected transport recovery.
    let protected_boundary = ProjectBridgeReadWorkerCleanup::begin(
        bridge_state.read_worker_requests.clone(),
        ProjectBridgeReadWorkerCleanupKind::WriterPreemption,
    )?;
    protected_boundary.wait_for_protected_response_boundary()?;
    let has_disposable_requests = protected_boundary.has_disposable_requests()?;

    let _lifecycle = bridge_state
        .process_lifecycle
        .lock()
        .map_err(|_| "Project bridge process lifecycle lock was poisoned.".to_owned())?;
    // Detach active disposable workers before changing the read generation so none can race into
    // a new process slot. Every writer boundary also invalidates responses that completed their
    // worker section but have not yet crossed the async command's final acceptance check.
    let processes = if has_disposable_requests {
        take_active_preemptible_project_bridge_read_workers_unlocked(bridge_state)?
    } else {
        Vec::new()
    };
    bridge_state.read_generation.fetch_add(1, Ordering::AcqRel);
    terminate_project_bridge_processes(processes)?;
    if has_disposable_requests {
        // A canceled or otherwise blocked spawn_blocking task may retain its permit after its
        // sidecar is gone. Revoking the registered read guards lets the queued writer proceed while
        // stale cleanup remains fenced by the old read generation.
        bridge_state
            .read_worker_requests
            .revoke_disposable_execution_guards()?;
    }
    Ok(protected_boundary)
}

fn take_active_preemptible_project_bridge_read_workers_unlocked(
    bridge_state: &ProjectBridgeState,
) -> Result<Vec<Arc<ProjectBridgeProcess>>, String> {
    let mut processes = Vec::with_capacity(bridge_state.read_processes.len());
    for process_slot in bridge_state.read_processes.iter() {
        let mut current = process_slot
            .lock()
            .map_err(|_| "Project bridge read-worker lock was poisoned.".to_owned())?;
        if current
            .as_ref()
            .is_some_and(|process| process.active_preemptible_request.load(Ordering::Acquire))
        {
            if let Some(process) = current.take() {
                processes.push(process);
            }
        }
    }
    Ok(processes)
}

fn take_project_bridge_read_workers_unlocked(
    bridge_state: &ProjectBridgeState,
) -> Result<Vec<Arc<ProjectBridgeProcess>>, String> {
    let mut processes = Vec::with_capacity(bridge_state.read_processes.len());
    for process_slot in bridge_state.read_processes.iter() {
        if let Some(process) = process_slot
            .lock()
            .map_err(|_| "Project bridge read-worker lock was poisoned.".to_owned())?
            .take()
        {
            processes.push(process);
        }
    }
    Ok(processes)
}

fn terminate_project_bridge_processes(
    processes: Vec<Arc<ProjectBridgeProcess>>,
) -> Result<(), String> {
    let mut first_error = None;
    for process in processes {
        if let Err(error) = process.terminate() {
            first_error.get_or_insert(error);
        }
    }
    first_error.map_or(Ok(()), Err)
}

#[cfg(windows)]
fn create_open_path_command(path: &Path) -> Command {
    let mut command = Command::new("explorer.exe");
    command.arg(path);
    command
}

#[cfg(target_os = "macos")]
fn create_open_path_command(path: &Path) -> Command {
    let mut command = Command::new("open");
    command.arg(path);
    command
}

#[cfg(all(unix, not(target_os = "macos")))]
fn create_open_path_command(path: &Path) -> Command {
    let mut command = Command::new("xdg-open");
    command.arg(path);
    command
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let context = tauri::generate_context!();
    #[cfg(windows)]
    windows_app_identity::set_current_process(&context.config().identifier)
        .expect("KM Editor could not establish its stable Windows taskbar identity");

    tauri::Builder::default()
        .manage(CloseGuardState {
            is_guarded: AtomicBool::new(false),
            shutdown_started: AtomicBool::new(false),
        })
        .manage(SupportSearchState::default())
        .manage(ProjectBridgeState::default())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_process::init())
        .setup(|app| {
            cleanup_stale_updater_temp_directories();

            #[cfg(desktop)]
            app.handle()
                .plugin(tauri_plugin_updater::Builder::new().build())?;

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            project_bridge,
            recycle_project_bridge,
            create_directory,
            find_support_file_folder,
            cancel_support_file_search,
            open_path,
            set_close_guard_enabled,
            exit_app
        ])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                let app_handle = window.app_handle();
                let close_guard = app_handle.state::<CloseGuardState>();

                if close_guard.is_guarded.load(Ordering::SeqCst) {
                    api.prevent_close();
                    let _ = window.emit(WINDOW_CLOSE_REQUESTED_EVENT, ());
                } else {
                    // Native window close can race the frontend guard. Always serialize shutdown
                    // behind active bridge work instead of synchronously killing its process.
                    api.prevent_close();
                    app_handle.state::<SupportSearchState>().cancel();
                    if close_guard
                        .shutdown_started
                        .compare_exchange(false, true, Ordering::AcqRel, Ordering::Acquire)
                        .is_ok()
                    {
                        let app_handle = app_handle.clone();
                        let bridge_state = app_handle.state::<ProjectBridgeState>().inner().clone();
                        tauri::async_runtime::spawn(async move {
                            if recycle_project_bridge_after_active_requests(bridge_state)
                                .await
                                .is_ok()
                            {
                                app_handle.exit(0);
                            } else {
                                app_handle
                                    .state::<CloseGuardState>()
                                    .shutdown_started
                                    .store(false, Ordering::Release);
                            }
                        });
                    }
                }
            }
        })
        .run(context)
        .expect("error while running tauri application");
}

fn resolve_repo_root() -> Result<PathBuf, String> {
    #[cfg(debug_assertions)]
    let manifest_dir = Some(PathBuf::from(env!("CARGO_MANIFEST_DIR")));
    #[cfg(not(debug_assertions))]
    let manifest_dir: Option<PathBuf> = None;
    let current_dir = std::env::current_dir()
        .map_err(|error| format!("Could not inspect current directory: {error}"))?;
    let current_exe = std::env::current_exe()
        .ok()
        .and_then(|path| path.parent().map(Path::to_path_buf));

    // Tauri can launch from different working directories in dev/build flows; walk known anchors.
    [manifest_dir, Some(current_dir), current_exe]
        .into_iter()
        .flatten()
        .find_map(find_repo_root)
        .ok_or_else(|| {
            "Could not locate the repository root for the project bridge runner.".to_owned()
        })
}

fn find_repo_root(start_path: PathBuf) -> Option<PathBuf> {
    start_path
        .ancestors()
        .find(|path| path.join("KM.Editor.slnx").is_file())
        .map(Path::to_path_buf)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::{BTreeMap, BTreeSet};
    use std::ffi::OsStr;
    use std::future::Future;
    use std::sync::Barrier;

    const CSHARP_COMMAND_NAMES_SOURCE: &str =
        include_str!("../../../../src/KM.Api/Bridge/KmCommandNames.cs");
    const CSHARP_DISPATCHER_SOURCE: &str =
        include_str!("../../../../src/KM.Tools/Bridge/ProjectBridgeDispatcher.cs");
    const TYPESCRIPT_COMMAND_CONTRACT_SOURCE: &str = include_str!("../../src/bridge/contracts.ts");

    #[test]
    fn project_source_revision_is_an_explicit_replay_safe_read() {
        let policy = project_bridge_request_policy(
            r#"{"command":"project.sourceRevision.read","requestId":"test","payload":{}}"#,
        )
        .expect("source revision requests must have an explicit policy");

        assert_eq!(
            policy.execution_timeout,
            Some(PROJECT_BRIDGE_WORKFLOW_LOAD_TIMEOUT)
        );
        assert!(policy.retry_after_transport_failure);
        assert_eq!(
            policy.concurrency,
            ProjectBridgeCommandConcurrency::IndependentRead(
                ProjectBridgeReadAffinity::SemanticExplore
            )
        );
    }

    #[test]
    fn routed_commands_have_an_exhaustive_cross_layer_policy_contract() {
        assert!(ROUTED_PROJECT_BRIDGE_COMMANDS
            .windows(2)
            .all(|pair| pair[0] < pair[1]));

        let declarations = parse_csharp_command_declarations(CSHARP_COMMAND_NAMES_SOURCE);
        let dispatched = parse_csharp_dispatcher_commands(CSHARP_DISPATCHER_SOURCE, &declarations);
        let rust = ROUTED_PROJECT_BRIDGE_COMMANDS
            .iter()
            .copied()
            .map(str::to_owned)
            .collect::<BTreeSet<_>>();
        let typescript = parse_typescript_command_values(TYPESCRIPT_COMMAND_CONTRACT_SOURCE);

        assert_eq!(
            dispatched, rust,
            "Rust must classify every routed C# command"
        );
        assert_eq!(
            typescript, rust,
            "TypeScript must expose every routed C# command"
        );
        assert_eq!(
            declarations.values().cloned().collect::<BTreeSet<_>>(),
            rust,
            "C# command declarations and dispatcher routes must stay one-to-one"
        );
        for command in ROUTED_PROJECT_BRIDGE_COMMANDS {
            let concurrency = project_bridge_command_concurrency(command).unwrap_or_else(|| {
                panic!("{command} must have an explicit concurrency classification")
            });
            let request = format!(r#"{{"command":"{command}"}}"#);
            assert!(
                project_bridge_request_policy(&request).is_some(),
                "{command} must have an explicit timeout/replay policy"
            );
            if project_bridge_command_recycles_read_workers(command) {
                assert!(
                    matches!(
                        concurrency,
                        ProjectBridgeCommandConcurrency::Exclusive
                            | ProjectBridgeCommandConcurrency::AffinityExclusive(_)
                    ),
                    "{command} cannot recycle read workers without owning the exclusive barrier"
                );
            }

            let policy = project_bridge_request_policy(&request)
                .unwrap_or_else(|| panic!("{command} must have an explicit request policy"));
            if project_bridge_command_requires_uninterrupted_completion(command, concurrency) {
                assert_eq!(
                    policy.execution_timeout, None,
                    "{command} must not arm a process-killing watchdog during durable work"
                );
                assert!(
                    !policy.retry_after_transport_failure,
                    "{command} must not replay after an indeterminate durable completion"
                );
            } else {
                assert!(
                    policy.execution_timeout.is_some(),
                    "{command} must retain bounded recovery unless it owns durable work"
                );
            }
        }
    }

    #[test]
    fn unknown_commands_fail_closed_onto_the_ordered_lane() {
        let command = "future.unclassified.command";
        assert_eq!(project_bridge_command_concurrency(command), None);
        let policy =
            resolved_project_bridge_request_policy(&format!(r#"{{"command":"{command}"}}"#));
        assert_eq!(
            policy.concurrency,
            ProjectBridgeCommandConcurrency::Exclusive
        );
        assert_eq!(
            policy.execution_timeout,
            Some(PROJECT_BRIDGE_DEFAULT_OPERATION_TIMEOUT)
        );
        assert!(!policy.retry_after_transport_failure);
        assert!(!policy.writer_priority_before_execution);
        assert!(!policy.recycle_read_workers_before_execution);
        assert!(!policy.recycle_read_workers_after_execution);
    }

    #[test]
    fn stateful_handle_and_review_families_never_cross_processes() {
        for command in [
            "semanticMerge.capabilities",
            "semanticMerge.source.open",
            "semanticMerge.preview",
            "semanticMerge.import",
            "recipes.export",
            "recipes.validate",
            "recipes.preview",
            "recipes.import",
        ] {
            assert_eq!(
                command_affinity(command),
                Some(ProjectBridgeReadAffinity::SemanticMerge),
                "{command} must stay on the semantic-merge worker"
            );
        }
        for command in [
            "researchLab.capabilities",
            "researchLab.source.open",
            "researchLab.source.close",
            "researchLab.compare",
            "researchLab.byteWindow",
            "researchLab.annotations.read",
            "researchLab.annotations.mutate",
        ] {
            assert_eq!(
                command_affinity(command),
                Some(ProjectBridgeReadAffinity::ResearchLab),
                "{command} must stay on the research worker"
            );
        }
        for command in [
            "gameplaySettings.update.preview",
            "gameplaySettings.update.apply",
            "inGameSettingsPackage.preview",
            "inGameSettingsPackage.apply",
            "output.cleanup.preview",
            "output.cleanup.apply",
            "output.checkpoint.restore.preview",
            "output.checkpoint.restore",
            "rowClipboard.copy.prepare",
            "rowClipboard.paste.preview",
            "rowClipboard.paste.stage",
        ] {
            assert!(
                matches!(
                    project_bridge_command_concurrency(command),
                    Some(
                        ProjectBridgeCommandConcurrency::OwnerOrdered
                            | ProjectBridgeCommandConcurrency::Exclusive
                    )
                ),
                "{command} must stay on the stateful owner process"
            );
        }
    }

    #[test]
    fn process_held_handles_are_not_replayed_after_their_worker_is_lost() {
        for command in [
            "semanticMerge.preview",
            "semanticMerge.import",
            "recipes.preview",
            "recipes.import",
            "researchLab.source.open",
            "researchLab.source.close",
            "researchLab.compare",
            "researchLab.byteWindow",
            "researchLab.annotations.mutate",
            "rowClipboard.paste.preview",
            "rowClipboard.paste.stage",
            "output.cleanup.preview",
        ] {
            let policy = project_bridge_request_policy(&format!(r#"{{"command":"{command}"}}"#))
                .unwrap_or_else(|| panic!("missing request policy for {command}"));
            assert!(
                !policy.retry_after_transport_failure,
                "{command} cannot be replayed after process-local authorization or handle state is lost"
            );
        }

        for command in [
            "semanticMerge.capabilities",
            "semanticMerge.source.open",
            "recipes.export",
            "recipes.validate",
            "researchLab.capabilities",
            "researchLab.annotations.read",
            "placement.catalog.open",
            "placement.catalog.query",
            "placement.object.load",
        ] {
            let policy = project_bridge_request_policy(&format!(r#"{{"command":"{command}"}}"#))
                .unwrap_or_else(|| panic!("missing request policy for {command}"));
            assert!(
                policy.retry_after_transport_failure,
                "{command} is stateless or recreates its exact immutable revision and should remain replayable"
            );
        }
    }

    #[test]
    fn process_held_read_commands_are_protected_and_preemption_obeys_replay_policy() {
        for command in [
            "semanticMerge.preview",
            "recipes.preview",
            "researchLab.source.open",
            "researchLab.compare",
            "researchLab.byteWindow",
        ] {
            let policy = project_bridge_request_policy(&format!(r#"{{"command":"{command}"}}"#))
                .unwrap_or_else(|| panic!("missing request policy for {command}"));
            assert!(matches!(
                policy.concurrency,
                ProjectBridgeCommandConcurrency::AffinityOrdered(_)
            ));
            assert!(!policy.retry_after_transport_failure);
            assert!(
                !project_bridge_request_should_restart_after_preemption(
                    policy,
                    PROJECT_BRIDGE_READ_PREEMPTED_ERROR,
                ),
                "{command} must not replay after losing process-held state"
            );
        }

        let replayable = project_bridge_request_policy(
            r#"{"command":"project.sourceRevision.read","requestId":"read"}"#,
        )
        .expect("replay-safe read policy");
        assert!(project_bridge_request_should_restart_after_preemption(
            replayable,
            PROJECT_BRIDGE_READ_PREEMPTED_ERROR,
        ));

        let non_replayable_read = ProjectBridgeRequestPolicy {
            execution_timeout: Some(PROJECT_BRIDGE_PROJECT_READ_TIMEOUT),
            retry_after_transport_failure: false,
            concurrency: ProjectBridgeCommandConcurrency::IndependentRead(
                ProjectBridgeReadAffinity::GeneralProject,
            ),
            writer_priority_before_execution: false,
            recycle_read_workers_before_execution: false,
            recycle_read_workers_after_execution: false,
        };
        assert!(!project_bridge_request_should_restart_after_preemption(
            non_replayable_read,
            PROJECT_BRIDGE_READ_PREEMPTED_ERROR,
        ));
    }

    #[test]
    fn change_plan_creation_has_writer_priority_without_leaving_the_owner_process() {
        let policy =
            project_bridge_request_policy(r#"{"command":"changePlan.create","requestId":"plan"}"#)
                .expect("change-plan creation policy");
        assert_eq!(
            policy.concurrency,
            ProjectBridgeCommandConcurrency::OwnerOrdered
        );
        assert!(policy.writer_priority_before_execution);
        assert!(!policy.recycle_read_workers_before_execution);
        assert!(!policy.recycle_read_workers_after_execution);
        assert!(policy.retry_after_transport_failure);
        assert_eq!(
            policy.execution_timeout,
            Some(PROJECT_BRIDGE_EDITOR_OPERATION_TIMEOUT)
        );
    }

    #[test]
    fn owner_process_cannot_inherit_the_read_worker_marker() {
        let mut owner = Command::new("unused-owner");
        owner.env(
            PROJECT_BRIDGE_READ_WORKER_ENVIRONMENT_VARIABLE,
            "unexpected",
        );
        configure_project_bridge_process_environment(&mut owner, None);
        assert_eq!(
            project_bridge_command_environment(
                &owner,
                PROJECT_BRIDGE_READ_WORKER_ENVIRONMENT_VARIABLE
            ),
            Some(None),
            "the owning sidecar must explicitly remove an inherited read-worker marker"
        );

        let mut worker = Command::new("unused-worker");
        configure_project_bridge_process_environment(&mut worker, Some(2));
        assert_eq!(
            project_bridge_command_environment(
                &worker,
                PROJECT_BRIDGE_READ_WORKER_ENVIRONMENT_VARIABLE
            ),
            Some(Some(OsStr::new("1")))
        );
        assert!(matches!(
            project_bridge_command_environment(
                &worker,
                PROJECT_BRIDGE_CPU_LIMIT_ENVIRONMENT_VARIABLE
            ),
            Some(Some(value)) if value.to_string_lossy().parse::<usize>().is_ok_and(|value| (1..=64).contains(&value))
        ));
        assert!(matches!(
            project_bridge_command_environment(
                &worker,
                PROJECT_BRIDGE_MEMORY_LIMIT_ENVIRONMENT_VARIABLE
            ),
            Some(Some(value)) if value.to_string_lossy().parse::<u64>().is_ok_and(|value| value >= 64 * 1024 * 1024)
        ));
    }

    #[test]
    fn cache_commands_are_exclusive_and_only_destructive_controls_recycle_workers() {
        for family in ["svCache", "zaCache", "swshCache"] {
            for operation in ["status", "warmup.step"] {
                let command = format!("{family}.{operation}");
                let policy =
                    project_bridge_request_policy(&format!(r#"{{"command":"{command}"}}"#))
                        .expect("cache command policy");
                assert_eq!(
                    policy.concurrency,
                    ProjectBridgeCommandConcurrency::Exclusive
                );
                assert!(!policy.recycle_read_workers_before_execution);
                assert!(!policy.recycle_read_workers_after_execution);
            }
            for operation in ["settings.update", "clear"] {
                let command = format!("{family}.{operation}");
                let policy =
                    project_bridge_request_policy(&format!(r#"{{"command":"{command}"}}"#))
                        .expect("cache command policy");
                assert_eq!(
                    policy.concurrency,
                    ProjectBridgeCommandConcurrency::Exclusive
                );
                assert!(policy.recycle_read_workers_before_execution);
                assert!(!policy.recycle_read_workers_after_execution);
            }
        }
    }

    #[test]
    fn shared_persistence_mutations_take_the_exclusive_barrier() {
        for command in [
            "changeSets.captureSession",
            "changeSets.import",
            "changeSets.mutate",
            "guidedDesign.import",
            "workspace.applicationState.write",
            "workspace.drafts.delete",
            "workspace.drafts.write",
            "workspace.projectState.delete",
            "workspace.projectState.write",
        ] {
            assert_eq!(
                project_bridge_command_concurrency(command),
                Some(ProjectBridgeCommandConcurrency::Exclusive),
                "{command} must not race a read worker observing the same durable state"
            );
        }
    }

    #[test]
    fn read_workers_overlap_with_a_bounded_exclusive_writer_barrier() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(2);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let first = tauri::async_runtime::block_on(
            bridge_state.acquire_execution_permit_for_generation(
                request_generation,
                project_bridge_command_concurrency("project.sourceRevision.read")
                    .expect("source revision classification"),
            ),
        )
        .expect("source revision read permit");
        let second = tauri::async_runtime::block_on(
            bridge_state.acquire_execution_permit_for_generation(
                request_generation,
                project_bridge_command_concurrency("gameModules.capabilities")
                    .expect("game-module classification"),
            ),
        )
        .expect("independent analysis reads must overlap");

        assert!(bridge_state
            .execution_gate
            .clone()
            .try_read_owned()
            .is_err());
        assert!(bridge_state
            .execution_gate
            .clone()
            .try_write_owned()
            .is_err());
        drop(first);
        drop(second);
        let _writer = bridge_state
            .execution_gate
            .clone()
            .try_write_owned()
            .expect("exclusive execution must resume after every reader exits");
    }

    #[test]
    fn malformed_project_bridge_requests_keep_the_default_watchdog() {
        for request_json in ["not-json", r#"{"requestId":"missing-command"}"#] {
            let policy = resolved_project_bridge_request_policy(request_json);
            assert_eq!(
                policy.execution_timeout,
                Some(PROJECT_BRIDGE_DEFAULT_OPERATION_TIMEOUT)
            );
            assert!(!policy.retry_after_transport_failure);
            assert_eq!(
                policy.concurrency,
                ProjectBridgeCommandConcurrency::Exclusive
            );
        }
    }

    #[test]
    fn project_bridge_transport_rejects_line_delimiter_injection() {
        for request_json in [
            "{}\n{}",
            "{}\r{}",
            "{}\r\n{}",
            "\n{\"command\":\"project.list\"}",
        ] {
            assert!(
                validate_project_bridge_request_transport(request_json).is_err(),
                "raw request delimiters must be rejected before bridge admission"
            );
        }

        assert!(validate_project_bridge_request_transport(
            r#"{"command":"project.list","value":"escaped\ntext"}"#
        )
        .is_ok());
    }

    #[test]
    fn queued_request_permit_rejects_a_recycled_generation() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let held_permit =
            tauri::async_runtime::block_on(bridge_state.acquire_execution_permit_for_generation(
                request_generation,
                ProjectBridgeCommandConcurrency::IndependentRead(
                    ProjectBridgeReadAffinity::SemanticExplore,
                ),
            ))
            .expect("the first request should acquire the only read permit");
        let waiting_state = bridge_state.clone();
        let waiting = std::thread::spawn(move || {
            tauri::async_runtime::block_on(waiting_state.acquire_execution_permit_for_generation(
                request_generation,
                ProjectBridgeCommandConcurrency::Exclusive,
            ))
        });

        bridge_state.generation.fetch_add(1, Ordering::AcqRel);
        drop(held_permit);

        let error = waiting
            .join()
            .expect("queued permit thread")
            .expect_err("the stale queued request must not acquire the recycled generation");
        assert_eq!(error, PROJECT_BRIDGE_RECYCLED_ERROR);
    }

    #[test]
    fn canceled_response_read_releases_the_sole_request_permit() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let permit =
            tauri::async_runtime::block_on(bridge_state.acquire_execution_permit_for_generation(
                request_generation,
                ProjectBridgeCommandConcurrency::Exclusive,
            ))
            .expect("the active request should acquire exclusive execution");
        let request_state = Arc::new(AtomicUsize::new(PROJECT_BRIDGE_REQUEST_TIMED_OUT));
        let cancellation = ProjectBridgeRequestCancellation {
            request_state: Some(request_state),
            generation: bridge_state.generation.clone(),
            request_generation: bridge_state.generation.load(Ordering::Acquire),
            read_generation: None,
            request_read_generation: None,
        };
        let wait_was_called = Arc::new(AtomicBool::new(false));
        let wait_was_called_by_closure = wait_was_called.clone();

        let result = wait_for_project_bridge_stdout_with_cancellation(&cancellation, move || {
            wait_was_called_by_closure.store(true, Ordering::Release);
            Ok(ProjectBridgeStdoutReadiness::Pending)
        });
        assert!(matches!(
            result,
            Err(ProjectBridgeRequestFailure::Retryable(_))
        ));
        assert!(
            !wait_was_called.load(Ordering::Acquire),
            "a canceled response read must not enter the pipe wait"
        );

        drop(permit);
        let _replacement_permit = bridge_state
            .execution_gate
            .clone()
            .try_write_owned()
            .expect("canceling the active read must release the sole request permit");
    }

    #[test]
    fn recycled_generation_cancels_response_read_before_pipe_wait() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let cancellation = ProjectBridgeRequestCancellation {
            request_state: None,
            generation: bridge_state.generation.clone(),
            request_generation,
            read_generation: None,
            request_read_generation: None,
        };
        bridge_state.generation.fetch_add(1, Ordering::AcqRel);

        let result = wait_for_project_bridge_stdout_with_cancellation(&cancellation, || {
            panic!("a recycled response read must not enter the pipe wait")
        });
        match result {
            Err(ProjectBridgeRequestFailure::Retryable(error)) => {
                assert_eq!(error, PROJECT_BRIDGE_RECYCLED_ERROR)
            }
            _ => panic!("a recycled response read must return the recycled request failure"),
        }
    }

    #[test]
    fn preempted_read_generation_rejects_an_already_buffered_response() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let request_read_generation = bridge_state.read_generation.load(Ordering::Acquire);
        let cancellation = ProjectBridgeRequestCancellation {
            request_state: None,
            generation: bridge_state.generation.clone(),
            request_generation,
            read_generation: Some(bridge_state.read_generation.clone()),
            request_read_generation: Some(request_read_generation),
        };
        bridge_state.read_generation.fetch_add(1, Ordering::AcqRel);
        let mut child = spawn_partial_response_fixture();
        let mut stdout = child
            .stdout
            .take()
            .expect("buffered-response fixture stdout");
        let mut buffered = b"stale-response\n".to_vec();

        let result =
            read_bounded_project_bridge_response(&mut stdout, &mut buffered, &cancellation);
        assert!(matches!(
            result,
            Err(ProjectBridgeRequestFailure::Retryable(error))
                if error == PROJECT_BRIDGE_READ_PREEMPTED_ERROR
        ));
        assert_eq!(
            buffered, b"stale-response\n",
            "cancellation must win before a buffered stale frame is extracted"
        );
        child.wait().expect("reap buffered-response fixture");
    }

    #[test]
    fn response_framing_retains_bytes_for_the_next_ordered_request() {
        let mut buffered = b"{\"first\":true}\r\n{\"second\":true}\n".to_vec();
        let mut response_scan_offset = 0;
        let first = take_project_bridge_response_frame(&mut buffered, &mut response_scan_offset)
            .expect("the first frame should be valid")
            .expect("the first frame should be complete");
        assert_eq!(
            decode_project_bridge_response(first).expect("decode the first response"),
            r#"{"first":true}"#
        );
        let second = take_project_bridge_response_frame(&mut buffered, &mut response_scan_offset)
            .expect("the second frame should be valid")
            .expect("the second frame should be complete");
        assert_eq!(
            decode_project_bridge_response(second).expect("decode the second response"),
            r#"{"second":true}"#
        );
        assert!(buffered.is_empty());
        assert_eq!(response_scan_offset, 0);
    }

    #[test]
    fn response_framing_scans_large_frames_incrementally() {
        let mut buffered = Vec::with_capacity(PROJECT_BRIDGE_EXPECTED_RESPONSE_BYTES + 1);
        let mut response_scan_offset = 0;

        while buffered.len() < PROJECT_BRIDGE_EXPECTED_RESPONSE_BYTES {
            let next_length = buffered
                .len()
                .saturating_add(PROJECT_BRIDGE_READ_CHUNK_BYTES)
                .min(PROJECT_BRIDGE_EXPECTED_RESPONSE_BYTES);
            buffered.resize(next_length, b'x');
            assert!(
                take_project_bridge_response_frame(&mut buffered, &mut response_scan_offset)
                    .expect("a bounded partial frame should remain valid")
                    .is_none()
            );
            assert_eq!(
                response_scan_offset,
                buffered.len(),
                "the next scan must start after every byte already inspected"
            );
        }

        buffered.push(b'\n');
        let response = take_project_bridge_response_frame(&mut buffered, &mut response_scan_offset)
            .expect("the expected large frame should be valid")
            .expect("the expected large frame should be complete");
        assert_eq!(response.len(), PROJECT_BRIDGE_EXPECTED_RESPONSE_BYTES + 1);
        assert!(buffered.is_empty());
        assert_eq!(response_scan_offset, 0);
    }

    #[test]
    fn response_framing_enforces_the_exact_frame_limit() {
        const TEST_FRAME_LIMIT: usize = 8;
        let mut exact = b"1234567\n".to_vec();
        let mut exact_scan_offset = 0;
        let response = take_project_bridge_response_frame_with_limit(
            &mut exact,
            &mut exact_scan_offset,
            TEST_FRAME_LIMIT,
        )
        .expect("an exact-limit frame should be valid")
        .expect("an exact-limit frame should be complete");
        assert_eq!(response, b"1234567\n");
        assert!(exact.is_empty());
        assert_eq!(exact_scan_offset, 0);

        let mut oversized = b"12345678\n".to_vec();
        let mut oversized_scan_offset = 0;
        let result = take_project_bridge_response_frame_with_limit(
            &mut oversized,
            &mut oversized_scan_offset,
            TEST_FRAME_LIMIT,
        );
        assert!(matches!(
            result,
            Err(ProjectBridgeRequestFailure::NonRetryable(error))
                if error.contains("exceeded the supported size limit")
        ));
        assert!(oversized.is_empty());
        assert_eq!(oversized_scan_offset, 0);
    }

    #[test]
    fn closed_stdout_never_accepts_a_partial_response_frame() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let cancellation = ProjectBridgeRequestCancellation {
            request_state: None,
            generation: bridge_state.generation.clone(),
            request_generation,
            read_generation: None,
            request_read_generation: None,
        };
        let mut child = spawn_partial_response_fixture();
        let mut stdout = child.stdout.take().expect("partial fixture stdout");
        let mut buffered = Vec::with_capacity(PROJECT_BRIDGE_INITIAL_RESPONSE_BUFFER_BYTES);

        let result =
            read_bounded_project_bridge_response(&mut stdout, &mut buffered, &cancellation);
        assert!(matches!(
            result,
            Err(ProjectBridgeRequestFailure::Retryable(error))
                if error.contains("closed before completing")
        ));
        assert!(buffered.is_empty());
        assert_eq!(
            buffered.capacity(),
            PROJECT_BRIDGE_INITIAL_RESPONSE_BUFFER_BYTES,
            "discarding an invalid frame must release any enlarged response allocation"
        );
        child.wait().expect("reap partial response fixture");
    }

    #[test]
    fn process_response_ownership_survives_heavy_contention() {
        const REQUEST_COUNT: usize = 64;
        let process = Arc::new(spawn_echo_project_bridge_fixture());
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(4);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let start = Arc::new(Barrier::new(REQUEST_COUNT + 1));
        let threads = (0..REQUEST_COUNT)
            .map(|request_index| {
                let process = process.clone();
                let bridge_state = bridge_state.clone();
                let start = start.clone();
                std::thread::spawn(move || {
                    let request = format!(r#"{{"requestIndex":{request_index}}}"#);
                    start.wait();
                    let response = process
                        .request(
                            &bridge_state,
                            request_generation,
                            &request,
                            Some(Duration::from_secs(5)),
                            None,
                        )
                        .unwrap_or_else(|error| {
                            panic!("request {request_index} failed: {error:?}")
                        });
                    assert_eq!(response, request, "response ownership crossed requests");
                })
            })
            .collect::<Vec<_>>();
        start.wait();
        for thread in threads {
            thread.join().expect("contention request thread");
        }
        process.terminate().expect("terminate echo fixture");
    }

    #[test]
    fn retryable_transport_crash_restarts_once_without_reusing_the_dead_process() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let request = r#"{"request":"recover-after-crash"}"#;
        let policy = ProjectBridgeRequestPolicy {
            execution_timeout: Some(Duration::from_secs(5)),
            retry_after_transport_failure: true,
            concurrency: ProjectBridgeCommandConcurrency::IndependentRead(
                ProjectBridgeReadAffinity::GeneralProject,
            ),
            writer_priority_before_execution: false,
            recycle_read_workers_before_execution: false,
            recycle_read_workers_after_execution: false,
        };
        let mut starts = 0;
        let response = run_project_bridge_request_with(
            &bridge_state,
            request_generation,
            request,
            policy,
            None,
            &bridge_state.process,
            || {
                starts += 1;
                Ok(if starts == 1 {
                    spawn_crashing_project_bridge_fixture()
                } else {
                    spawn_echo_project_bridge_fixture()
                })
            },
        )
        .expect("the replay-safe request should recover on one fresh process");
        assert_eq!(response, request);
        assert_eq!(starts, 2, "transport recovery must retry at most once");
        recycle_project_bridge_process(&bridge_state).expect("reap recovery fixture");
    }

    #[test]
    fn durable_request_reaches_its_response_boundary_before_recycle_or_shutdown() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let request = r#"{"command":"changePlan.apply","requestId":"durable"}"#;
        let policy = project_bridge_request_policy(request).expect("durable request policy");
        assert_eq!(policy.execution_timeout, None);
        assert!(!policy.retry_after_transport_failure);

        let request_state = bridge_state.clone();
        let (admitted_sender, admitted_receiver) = mpsc::channel();
        let request_thread = std::thread::spawn(move || {
            let execution_permit = tauri::async_runtime::block_on(
                request_state.acquire_execution_permit_for_generation(
                    request_generation,
                    policy.concurrency,
                ),
            )
            .expect("admit durable request");
            admitted_sender
                .send(())
                .expect("signal durable request admission");
            let response = run_project_bridge_request_with(
                &request_state,
                request_generation,
                request,
                policy,
                None,
                &request_state.process,
                || Ok(spawn_delayed_echo_project_bridge_fixture()),
            );
            drop(execution_permit);
            response
        });
        admitted_receiver
            .recv_timeout(Duration::from_secs(5))
            .expect("durable request admitted");

        let recycle_state = bridge_state.clone();
        let (recycled_sender, recycled_receiver) = mpsc::channel();
        let recycle_thread = std::thread::spawn(move || {
            let result = tauri::async_runtime::block_on(
                recycle_project_bridge_after_active_requests(recycle_state),
            );
            recycled_sender.send(result).expect("send recycle result");
        });
        assert!(
            recycled_receiver
                .recv_timeout(Duration::from_millis(50))
                .is_err(),
            "manual recycle and native shutdown must wait behind admitted durable work"
        );

        let response = request_thread
            .join()
            .expect("durable request thread")
            .expect("delayed durable request must complete without a watchdog termination");
        assert_eq!(response, request);
        recycled_receiver
            .recv_timeout(Duration::from_secs(5))
            .expect("queued recycle must complete after durable response")
            .expect("queued recycle result");
        recycle_thread.join().expect("recycle thread");
        assert!(
            bridge_state
                .process
                .lock()
                .expect("owner process slot")
                .is_none(),
            "recycle must detach the completed durable request process"
        );
        assert_ne!(
            bridge_state.generation.load(Ordering::Acquire),
            request_generation,
            "recycle must advance the generation after the durable boundary"
        );
    }

    #[test]
    fn watchdog_cancels_a_request_blocked_while_filling_stdin() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let process = Arc::new(spawn_silent_project_bridge_fixture());
        *bridge_state.process.lock().expect("owner process slot") = Some(process.clone());
        let request = "x".repeat(4 * 1024 * 1024);
        let started = Instant::now();

        let result = process.request(
            &bridge_state,
            request_generation,
            &request,
            Some(Duration::from_millis(50)),
            None,
        );
        assert!(matches!(
            result,
            Err(ProjectBridgeRequestFailure::TimedOut(_))
        ));
        assert!(
            started.elapsed() < Duration::from_secs(5),
            "a blocked stdin write must release admission after the active watchdog fires"
        );
        assert!(bridge_state
            .process
            .lock()
            .expect("owner process slot")
            .is_none());
        assert_ne!(
            bridge_state.generation.load(Ordering::Acquire),
            request_generation,
            "an owner timeout must invalidate requests bound to every old sidecar"
        );
        process
            .terminate()
            .expect("timed-out process is already detached");
    }

    #[test]
    fn process_lifecycle_prevents_a_new_generation_from_binding_a_detached_process() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
        let old_process = Arc::new(spawn_echo_project_bridge_fixture());
        *bridge_state.process.lock().expect("owner process slot") = Some(old_process.clone());
        let lifecycle = bridge_state
            .process_lifecycle
            .lock()
            .expect("process lifecycle lock");
        let new_generation = bridge_state
            .generation
            .fetch_add(1, Ordering::AcqRel)
            .wrapping_add(1);
        let waiting_state = bridge_state.clone();
        let (started_sender, started_receiver) = mpsc::channel();
        let (finished_sender, finished_receiver) = mpsc::channel();
        let waiting = std::thread::spawn(move || {
            started_sender.send(()).expect("signal lifecycle waiter");
            let process = get_or_start_project_bridge_process(
                &waiting_state,
                new_generation,
                &waiting_state.process,
                &mut || Ok(spawn_echo_project_bridge_fixture()),
            )
            .expect("start replacement process");
            finished_sender
                .send(process)
                .expect("return replacement process");
        });
        started_receiver
            .recv_timeout(Duration::from_secs(5))
            .expect("lifecycle waiter started");
        assert!(
            finished_receiver
                .recv_timeout(Duration::from_millis(50))
                .is_err(),
            "process lookup must wait while a generation transition detaches process slots"
        );
        let detached = bridge_state
            .process
            .lock()
            .expect("detach owner fixture")
            .take()
            .expect("old owner fixture");
        drop(lifecycle);

        let replacement = finished_receiver
            .recv_timeout(Duration::from_secs(5))
            .expect("replacement process result");
        assert!(!Arc::ptr_eq(&replacement, &old_process));
        waiting.join().expect("lifecycle waiter thread");
        detached.terminate().expect("terminate detached fixture");
        recycle_project_bridge_process(&bridge_state).expect("terminate replacement fixture");
    }

    #[test]
    fn process_pool_reuses_its_bounded_slots_and_recycles_every_child() {
        const READ_WORKER_LIMIT: usize = 4;
        assert_eq!(
            MAX_PROJECT_BRIDGE_PARALLEL_READ_WORKERS, READ_WORKER_LIMIT,
            "raising the process ceiling requires an explicit memory and lifecycle review"
        );
        assert!((1..=READ_WORKER_LIMIT).contains(&project_bridge_parallel_read_worker_limit()));
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(READ_WORKER_LIMIT);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let affinities = [
            ProjectBridgeReadAffinity::SemanticExplore,
            ProjectBridgeReadAffinity::BalanceLab,
            ProjectBridgeReadAffinity::GameModules,
            ProjectBridgeReadAffinity::GuidedDesign,
            ProjectBridgeReadAffinity::SemanticMerge,
            ProjectBridgeReadAffinity::ResearchLab,
            ProjectBridgeReadAffinity::Workflow(0),
            ProjectBridgeReadAffinity::Workflow(1),
            ProjectBridgeReadAffinity::GeneralProject,
        ];
        let mut owner_starts = 0;
        let mut read_worker_starts = 0;

        for _ in 0..3 {
            get_or_start_project_bridge_process(
                &bridge_state,
                request_generation,
                &bridge_state.process,
                &mut || {
                    owner_starts += 1;
                    Ok(spawn_echo_project_bridge_fixture())
                },
            )
            .expect("reuse the owning bridge process");

            for affinity in affinities {
                let process_index = affinity.stable_index() % bridge_state.read_processes.len();
                get_or_start_project_bridge_process(
                    &bridge_state,
                    request_generation,
                    &bridge_state.read_processes[process_index],
                    &mut || {
                        read_worker_starts += 1;
                        Ok(spawn_echo_project_bridge_fixture())
                    },
                )
                .expect("reuse the bounded read-worker slot");
            }
        }

        assert_eq!(owner_starts, 1, "the owner process must be long-lived");
        assert_eq!(
            read_worker_starts, READ_WORKER_LIMIT,
            "affinities must reuse the fixed read-worker slots"
        );
        assert_eq!(bridge_state.read_processes.len(), READ_WORKER_LIMIT);
        assert!(bridge_state
            .process
            .lock()
            .expect("owner process slot")
            .is_some());
        assert_eq!(
            bridge_state
                .read_processes
                .iter()
                .filter(|slot| slot.lock().expect("read-worker slot").is_some())
                .count(),
            READ_WORKER_LIMIT
        );

        recycle_project_bridge_process(&bridge_state).expect("recycle the complete bridge pool");
        assert!(bridge_state
            .process
            .lock()
            .expect("owner process slot")
            .is_none());
        assert!(bridge_state
            .read_processes
            .iter()
            .all(|slot| slot.lock().expect("read-worker slot").is_none()));
    }

    #[test]
    fn durable_writer_preempts_only_read_work_and_the_read_restarts_after_output() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let request_read_generation = bridge_state.read_generation.load(Ordering::Acquire);
            let read_request = r#"{"command":"project.sourceRevision.read","requestId":"read"}"#;
            let read_policy = project_bridge_request_policy(read_request).expect("read policy");
            assert!(project_bridge_concurrency_is_preemptible_read(
                read_policy.concurrency
            ));

            let owner = Arc::new(spawn_echo_project_bridge_fixture());
            *bridge_state.process.lock().expect("owner process slot") = Some(owner.clone());
            let read_process = Arc::new(spawn_silent_project_bridge_fixture());
            *bridge_state.read_processes[0]
                .lock()
                .expect("read process slot") = Some(read_process.clone());

            let read_state = bridge_state.clone();
            let (read_result_sender, read_result_receiver) = mpsc::channel();
            let read_thread = std::thread::spawn(move || {
                let permit = tauri::async_runtime::block_on(
                    read_state.acquire_execution_permit_for_generation(
                        request_generation,
                        read_policy.concurrency,
                    ),
                )
                .expect("admit long read");
                let result = run_project_bridge_request_with(
                    &read_state,
                    request_generation,
                    read_request,
                    read_policy,
                    Some(request_read_generation),
                    &read_state.read_processes[0],
                    || panic!("the preempted generation must not start another worker"),
                );
                drop(permit);
                read_result_sender.send(result).expect("send read result");
            });
            wait_for_active_project_bridge_request(&read_process);

            let write_request = r#"{"command":"changePlan.apply","requestId":"write"}"#;
            let write_policy = project_bridge_request_policy(write_request).expect("write policy");
            assert!(write_policy.recycle_read_workers_before_execution);
            let started = Instant::now();
            let write_permit = bridge_state
                .acquire_execution_permit_for_request(request_generation, write_policy)
                .await
                .expect("durable writer must acquire after canceling the disposable read");
            assert!(
                started.elapsed() < Duration::from_secs(5),
                "a durable writer must not inherit the read operation's multi-minute timeout"
            );
            let read_error = read_result_receiver
                .recv_timeout(Duration::from_secs(5))
                .expect("preempted read must release its permit")
                .expect_err("the old read generation must not publish a response");
            assert_eq!(read_error, PROJECT_BRIDGE_READ_PREEMPTED_ERROR);
            read_thread.join().expect("preempted read thread");
            assert!(bridge_state.read_processes[0]
                .lock()
                .expect("read process slot")
                .is_none());
            assert!(bridge_state
                .process
                .lock()
                .expect("owner process slot")
                .as_ref()
                .is_some_and(|current| Arc::ptr_eq(current, &owner)));

            drop(write_permit);
            let restarted_read_generation = bridge_state.read_generation.load(Ordering::Acquire);
            assert_ne!(restarted_read_generation, request_read_generation);
            let restarted_permit = bridge_state
                .acquire_execution_permit_for_generation(
                    request_generation,
                    read_policy.concurrency,
                )
                .await
                .expect("read must restart after output");
            let restarted_response = run_project_bridge_request_with(
                &bridge_state,
                request_generation,
                read_request,
                read_policy,
                Some(restarted_read_generation),
                &bridge_state.read_processes[0],
                || Ok(spawn_echo_project_bridge_fixture()),
            )
            .expect("fresh read generation");
            assert_eq!(restarted_response, read_request);
            drop(restarted_permit);

            let owner_probe = r#"{"requestId":"owner-still-alive"}"#;
            assert_eq!(
                owner
                    .request(
                        &bridge_state,
                        request_generation,
                        owner_probe,
                        Some(Duration::from_secs(5)),
                        None,
                    )
                    .expect("writer preemption must never terminate the owner"),
                owner_probe
            );
            recycle_project_bridge_process(&bridge_state).expect("reap bridge fixtures");
        });
    }

    #[test]
    fn durable_writer_revokes_a_detached_blocking_reads_execution_guard() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let read_policy = project_bridge_request_policy(
                r#"{"command":"project.sourceRevision.read","requestId":"read"}"#,
            )
            .expect("read policy");
            let read_permit = bridge_state
                .acquire_execution_permit_for_generation(
                    request_generation,
                    read_policy.concurrency,
                )
                .await
                .expect("admit detached blocking read");
            let (started_sender, started_receiver) = mpsc::channel();
            let (release_sender, release_receiver) = mpsc::channel();
            let (finished_sender, finished_receiver) = mpsc::channel();
            let cleanup_finished = Arc::new(AtomicBool::new(false));
            let cleanup_finished_task = cleanup_finished.clone();
            let read_task = tauri::async_runtime::spawn_blocking(move || {
                started_sender.send(()).expect("signal blocking read");
                release_receiver.recv().expect("release blocking read");
                drop(read_permit);
                cleanup_finished_task.store(true, Ordering::Release);
                finished_sender.send(()).expect("signal read cleanup");
            });
            started_receiver
                .recv_timeout(Duration::from_secs(5))
                .expect("blocking read starts");
            drop(read_task);

            let write_policy = project_bridge_request_policy(
                r#"{"command":"changePlan.apply","requestId":"write"}"#,
            )
            .expect("writer policy");
            let write_permit = tokio::time::timeout(
                Duration::from_secs(5),
                bridge_state.acquire_execution_permit_for_request(request_generation, write_policy),
            )
            .await
            .expect("writer admission must not inherit blocking cleanup")
            .expect("writer must revoke the disposable read guard");
            assert!(
                !cleanup_finished.load(Ordering::Acquire),
                "writer must acquire while stale blocking cleanup remains detached"
            );
            {
                let state = bridge_state
                    .read_worker_requests
                    .state
                    .lock()
                    .expect("inspect revoked disposable read");
                assert_eq!(state.active_disposable_requests, 1);
                assert_eq!(state.writer_cleanup_waiters, 0);
                assert_eq!(state.disposable_execution_guards.len(), 1);
            }

            drop(write_permit);
            release_sender.send(()).expect("release stale cleanup");
            finished_receiver
                .recv_timeout(Duration::from_secs(5))
                .expect("stale cleanup finishes");
            assert!(cleanup_finished.load(Ordering::Acquire));
            let state = bridge_state
                .read_worker_requests
                .state
                .lock()
                .expect("inspect completed disposable cleanup");
            assert_eq!(state.active_disposable_requests, 0);
            assert!(state.disposable_execution_guards.is_empty());
        });
    }

    #[test]
    fn change_plan_creation_preempts_disposable_analysis_and_preserves_the_owner() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let request_read_generation = bridge_state.read_generation.load(Ordering::Acquire);
            let read_request = r#"{"command":"project.sourceRevision.read","requestId":"read"}"#;
            let read_policy = project_bridge_request_policy(read_request).expect("read policy");

            let owner = Arc::new(spawn_echo_project_bridge_fixture());
            *bridge_state.process.lock().expect("owner process slot") = Some(owner.clone());
            let read_process = Arc::new(spawn_silent_project_bridge_fixture());
            *bridge_state.read_processes[0]
                .lock()
                .expect("read process slot") = Some(read_process.clone());

            let read_state = bridge_state.clone();
            let (read_result_sender, read_result_receiver) = mpsc::channel();
            let read_thread = std::thread::spawn(move || {
                let permit = tauri::async_runtime::block_on(
                    read_state.acquire_execution_permit_for_generation(
                        request_generation,
                        read_policy.concurrency,
                    ),
                )
                .expect("admit disposable analysis");
                let result = run_project_bridge_request_with(
                    &read_state,
                    request_generation,
                    read_request,
                    read_policy,
                    Some(request_read_generation),
                    &read_state.read_processes[0],
                    || panic!("the preempted generation must not start another worker"),
                );
                drop(permit);
                read_result_sender.send(result).expect("send read result");
            });
            wait_for_active_project_bridge_request(&read_process);

            let create_policy = project_bridge_request_policy(
                r#"{"command":"changePlan.create","requestId":"plan"}"#,
            )
            .expect("change-plan creation policy");
            let started = Instant::now();
            let create_permit = bridge_state
                .acquire_execution_permit_for_request(request_generation, create_policy)
                .await
                .expect("change-plan creation must preempt disposable analysis");
            assert!(
                started.elapsed() < Duration::from_secs(5),
                "change-plan creation must not inherit the analysis timeout"
            );
            let read_error = read_result_receiver
                .recv_timeout(Duration::from_secs(5))
                .expect("preempted analysis result")
                .expect_err("the stale analysis generation must not publish");
            assert_eq!(read_error, PROJECT_BRIDGE_READ_PREEMPTED_ERROR);
            read_thread.join().expect("analysis thread");
            assert!(bridge_state.read_processes[0]
                .lock()
                .expect("read process slot")
                .is_none());
            assert!(bridge_state
                .process
                .lock()
                .expect("owner process slot")
                .as_ref()
                .is_some_and(|current| Arc::ptr_eq(current, &owner)));

            drop(create_permit);
            recycle_project_bridge_process(&bridge_state).expect("reap bridge fixtures");
        });
    }

    #[test]
    fn change_plan_creation_priority_preserves_an_idle_affinity_handle_worker() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(2);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let create_policy = project_bridge_request_policy(
                r#"{"command":"changePlan.create","requestId":"plan"}"#,
            )
            .expect("change-plan creation policy");
            assert!(create_policy.writer_priority_before_execution);
            assert!(
                !create_policy.recycle_read_workers_before_execution,
                "plan creation does not mutate the source revision"
            );

            let semantic_merge_process = Arc::new(spawn_echo_project_bridge_fixture());
            let semantic_merge_slot = ProjectBridgeReadAffinity::SemanticMerge.stable_index()
                % bridge_state.read_processes.len();
            *bridge_state.read_processes[semantic_merge_slot]
                .lock()
                .expect("semantic-merge worker slot") = Some(semantic_merge_process.clone());

            let create_permit = bridge_state
                .acquire_execution_permit_for_request(request_generation, create_policy)
                .await
                .expect("admit prioritized plan creation");
            assert!(bridge_state.read_processes[semantic_merge_slot]
                .lock()
                .expect("semantic-merge worker slot")
                .as_ref()
                .is_some_and(|current| Arc::ptr_eq(current, &semantic_merge_process)));

            drop(create_permit);
            recycle_project_bridge_process(&bridge_state).expect("reap affinity fixture");
        });
    }

    #[test]
    fn writer_boundary_invalidates_a_completed_read_before_outer_delivery() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let completed_read_generation = bridge_state.read_generation.load(Ordering::Acquire);
            let read_policy = project_bridge_request_policy(
                r#"{"command":"project.sourceRevision.read","requestId":"read"}"#,
            )
            .expect("read policy");

            // Model the gap after the blocking worker has produced its response and released its
            // execution permit, but before project_bridge performs its final async acceptance.
            let completed_read_permit = bridge_state
                .acquire_execution_permit_for_generation(
                    request_generation,
                    read_policy.concurrency,
                )
                .await
                .expect("admit completed read");
            drop(completed_read_permit);
            assert_eq!(
                bridge_state
                    .read_worker_requests
                    .state
                    .lock()
                    .expect("inspect read-worker requests")
                    .active_disposable_requests,
                0
            );

            let write_policy = project_bridge_request_policy(
                r#"{"command":"changePlan.apply","requestId":"write"}"#,
            )
            .expect("writer policy");
            let write_permit = bridge_state
                .acquire_execution_permit_for_request(request_generation, write_policy)
                .await
                .expect("admit writer boundary");

            assert_ne!(
                bridge_state.read_generation.load(Ordering::Acquire),
                completed_read_generation,
                "a writer must invalidate even a response no longer counted as active"
            );
            assert_eq!(
                ensure_project_bridge_request_epoch_is_current(
                    &bridge_state,
                    request_generation,
                    Some(completed_read_generation),
                )
                .expect_err("the completed response must fail final acceptance"),
                PROJECT_BRIDGE_READ_PREEMPTED_ERROR
            );
            drop(write_permit);
        });
    }

    #[test]
    fn global_recycle_rejects_a_completed_owner_response_before_outer_delivery() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let request_read_generation = bridge_state.read_generation.load(Ordering::Acquire);
            let owner_policy = project_bridge_request_policy(
                r#"{"command":"changePlan.create","requestId":"plan"}"#,
            )
            .expect("owner policy");

            // Model the gap after the blocking owner request has produced its response and
            // released its permit, but before project_bridge accepts the async task result.
            let completed_owner_permit = bridge_state
                .acquire_execution_permit_for_generation(
                    request_generation,
                    owner_policy.concurrency,
                )
                .await
                .expect("admit completed owner request");
            let completed_response = Ok("completed-owner-response".to_owned());
            drop(completed_owner_permit);
            bridge_state.generation.fetch_add(1, Ordering::AcqRel);

            let error = accept_project_bridge_response_for_epoch(
                &bridge_state,
                request_generation,
                request_read_generation,
                owner_policy.concurrency,
                completed_response,
            )
            .expect_err("the recycled owner response must fail outer acceptance");
            assert_eq!(error, PROJECT_BRIDGE_RECYCLED_ERROR);
            assert!(
                !project_bridge_request_should_restart_after_preemption(owner_policy, &error),
                "a global recycle must never transparently replay an owner command"
            );
        });
    }

    #[test]
    fn writer_cleanup_covers_the_read_guard_to_registration_gap() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let write_policy = project_bridge_request_policy(
                r#"{"command":"changePlan.apply","requestId":"write"}"#,
            )
            .expect("writer policy");

            // Model an IndependentRead that acquired its fair-gate guard and was descheduled
            // immediately before registering as disposable.
            let unregistered_read_guard = bridge_state.execution_gate.clone().read_owned().await;
            let writer_state = bridge_state.clone();
            let (writer_result_sender, writer_result_receiver) = mpsc::channel();
            let writer_thread = std::thread::spawn(move || {
                let result = tauri::async_runtime::block_on(
                    writer_state
                        .acquire_execution_permit_for_request(request_generation, write_policy),
                );
                writer_result_sender
                    .send(result)
                    .expect("send writer admission result");
            });

            let cleanup_deadline = Instant::now() + Duration::from_secs(5);
            loop {
                let cleanup_waiters = bridge_state
                    .read_worker_requests
                    .state
                    .lock()
                    .expect("inspect writer cleanup")
                    .writer_cleanup_waiters;
                if cleanup_waiters == 1 {
                    break;
                }
                assert!(
                    Instant::now() < cleanup_deadline,
                    "writer did not establish its cleanup boundary"
                );
                std::thread::yield_now();
            }

            let registration_error = ProjectBridgeReadWorkerRequest::begin(
                bridge_state.read_worker_requests.clone(),
                ProjectBridgeReadWorkerRequestKind::Disposable,
                None,
            )
            .expect_err("the admission-gap read must be preempted");
            assert_eq!(registration_error, PROJECT_BRIDGE_READ_PREEMPTED_ERROR);
            assert_eq!(
                bridge_state
                    .read_worker_requests
                    .state
                    .lock()
                    .expect("inspect retained writer cleanup")
                    .writer_cleanup_waiters,
                1,
                "cleanup must remain registered until the writer owns the gate"
            );

            drop(unregistered_read_guard);
            let writer_permit = writer_result_receiver
                .recv_timeout(Duration::from_secs(5))
                .expect("writer admission result")
                .expect("writer acquires after the admission-gap guard drops");
            assert_eq!(
                bridge_state
                    .read_worker_requests
                    .state
                    .lock()
                    .expect("inspect completed writer cleanup")
                    .writer_cleanup_waiters,
                0
            );
            drop(writer_permit);
            writer_thread.join().expect("writer admission thread");
        });
    }

    #[test]
    fn durable_writer_waits_for_protected_work_then_preempts_disposable_analysis() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(2);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let request_read_generation = bridge_state.read_generation.load(Ordering::Acquire);
            let read_request = r#"{"command":"project.sourceRevision.read","requestId":"read"}"#;
            let read_policy = project_bridge_request_policy(read_request).expect("read policy");
            let protected_request =
                r#"{"command":"researchLab.source.close","requestId":"protected"}"#;
            let protected_policy =
                project_bridge_request_policy(protected_request).expect("protected policy");
            assert_eq!(
                read_policy.concurrency,
                ProjectBridgeCommandConcurrency::IndependentRead(
                    ProjectBridgeReadAffinity::SemanticExplore
                )
            );
            assert_eq!(
                protected_policy.concurrency,
                ProjectBridgeCommandConcurrency::AffinityOrdered(
                    ProjectBridgeReadAffinity::ResearchLab
                )
            );

            let read_process = Arc::new(spawn_silent_project_bridge_fixture());
            *bridge_state.read_processes[0]
                .lock()
                .expect("read process slot") = Some(read_process.clone());
            let protected_process = Arc::new(spawn_delayed_echo_project_bridge_fixture());
            *bridge_state.read_processes[1]
                .lock()
                .expect("protected process slot") = Some(protected_process.clone());

            let read_state = bridge_state.clone();
            let (read_result_sender, read_result_receiver) = mpsc::channel();
            let read_thread = std::thread::spawn(move || {
                let permit = tauri::async_runtime::block_on(
                    read_state.acquire_execution_permit_for_generation(
                        request_generation,
                        read_policy.concurrency,
                    ),
                )
                .expect("admit disposable analysis");
                let result = run_project_bridge_request_with(
                    &read_state,
                    request_generation,
                    read_request,
                    read_policy,
                    Some(request_read_generation),
                    &read_state.read_processes[0],
                    || panic!("the preempted generation must not start another worker"),
                );
                drop(permit);
                read_result_sender.send(result).expect("send read result");
            });
            wait_for_active_project_bridge_request(&read_process);

            let protected_state = bridge_state.clone();
            let (protected_result_sender, protected_result_receiver) = mpsc::channel();
            let protected_thread = std::thread::spawn(move || {
                let permit = tauri::async_runtime::block_on(
                    protected_state.acquire_execution_permit_for_generation(
                        request_generation,
                        protected_policy.concurrency,
                    ),
                )
                .expect("admit protected request");
                let result = run_project_bridge_request_with(
                    &protected_state,
                    request_generation,
                    protected_request,
                    protected_policy,
                    None,
                    &protected_state.read_processes[1],
                    || panic!("the protected process already exists"),
                );
                drop(permit);
                protected_result_sender
                    .send(result)
                    .expect("send protected result");
            });
            wait_for_active_project_bridge_request(&protected_process);

            let write_policy = project_bridge_request_policy(
                r#"{"command":"changePlan.apply","requestId":"write"}"#,
            )
            .expect("writer policy");
            let started = Instant::now();
            let write_permit = bridge_state
                .acquire_execution_permit_for_request(request_generation, write_policy)
                .await
                .expect("writer waits for protected work then preempts analysis");
            assert!(
                started.elapsed() < Duration::from_secs(5),
                "writer must not inherit the disposable analysis timeout"
            );
            assert_eq!(
                protected_result_receiver
                    .recv_timeout(Duration::from_secs(5))
                    .expect("protected result")
                    .expect("protected request must finish before preemption"),
                protected_request
            );
            let read_error = read_result_receiver
                .recv_timeout(Duration::from_secs(5))
                .expect("preempted analysis result")
                .expect_err("disposable analysis must be preempted");
            assert_eq!(read_error, PROJECT_BRIDGE_READ_PREEMPTED_ERROR);
            protected_thread.join().expect("protected request thread");
            read_thread.join().expect("analysis thread");
            assert!(bridge_state.read_processes[0]
                .lock()
                .expect("read process slot")
                .is_none());
            assert!(bridge_state.read_processes[1]
                .lock()
                .expect("protected process slot")
                .as_ref()
                .is_some_and(|current| Arc::ptr_eq(current, &protected_process)));

            drop(write_permit);
            recycle_project_bridge_process(&bridge_state).expect("reap bridge fixtures");
        });
    }

    #[test]
    fn owner_timeout_waits_for_the_protected_read_worker_response_boundary() {
        let bridge_state = ProjectBridgeState::with_parallel_read_limit(2);
        let request_generation = bridge_state.generation.load(Ordering::Acquire);
        let owner = Arc::new(spawn_echo_project_bridge_fixture());
        *bridge_state.process.lock().expect("owner process slot") = Some(owner.clone());

        let protected_request = r#"{"command":"researchLab.source.close","requestId":"protected"}"#;
        let protected_policy =
            project_bridge_request_policy(protected_request).expect("protected policy");
        let protected_process = Arc::new(spawn_delayed_echo_project_bridge_fixture());
        *bridge_state.read_processes[1]
            .lock()
            .expect("protected process slot") = Some(protected_process.clone());

        let protected_state = bridge_state.clone();
        let (protected_result_sender, protected_result_receiver) = mpsc::channel();
        let protected_thread = std::thread::spawn(move || {
            let permit = tauri::async_runtime::block_on(
                protected_state.acquire_execution_permit_for_generation(
                    request_generation,
                    protected_policy.concurrency,
                ),
            )
            .expect("admit protected request");
            let result = run_project_bridge_request_with(
                &protected_state,
                request_generation,
                protected_request,
                protected_policy,
                None,
                &protected_state.read_processes[1],
                || panic!("the protected process already exists"),
            );
            drop(permit);
            protected_result_sender
                .send(result)
                .expect("send protected result");
        });
        wait_for_active_project_bridge_request(&protected_process);
        assert_eq!(
            bridge_state
                .read_worker_requests
                .active_protected_requests(),
            1
        );

        let timeout_state = bridge_state.clone();
        let timeout_owner = owner.clone();
        let (timeout_started_sender, timeout_started_receiver) = mpsc::channel();
        let (timeout_result_sender, timeout_result_receiver) = mpsc::channel();
        let timeout_thread = std::thread::spawn(move || {
            timeout_started_sender
                .send(())
                .expect("signal timeout start");
            let result =
                timeout_project_bridge_process(&timeout_state, request_generation, &timeout_owner);
            timeout_result_sender
                .send(result)
                .expect("send timeout result");
        });
        timeout_started_receiver
            .recv_timeout(Duration::from_secs(5))
            .expect("timeout thread started");
        let cleanup_deadline = Instant::now() + Duration::from_secs(5);
        loop {
            let cleanup_waiters = bridge_state
                .read_worker_requests
                .state
                .lock()
                .expect("inspect read-worker cleanup")
                .owner_cleanup_waiters;
            if cleanup_waiters == 1 {
                break;
            }
            assert!(
                Instant::now() < cleanup_deadline,
                "owner timeout did not enter the protected response boundary"
            );
            std::thread::yield_now();
        }
        assert!(
            timeout_result_receiver
                .recv_timeout(Duration::from_millis(50))
                .is_err(),
            "owner timeout must remain blocked while protected work is active"
        );
        assert_eq!(
            protected_result_receiver
                .recv_timeout(Duration::from_secs(5))
                .expect("protected result")
                .expect("owner timeout must not interrupt the protected response"),
            protected_request
        );
        timeout_result_receiver
            .recv_timeout(Duration::from_secs(5))
            .expect("owner timeout result")
            .expect("owner timeout cleanup");
        protected_thread.join().expect("protected request thread");
        timeout_thread.join().expect("owner timeout thread");
        assert_ne!(
            bridge_state.generation.load(Ordering::Acquire),
            request_generation
        );
        assert!(bridge_state
            .process
            .lock()
            .expect("owner process slot")
            .is_none());
        assert!(bridge_state
            .read_processes
            .iter()
            .all(|slot| slot.lock().expect("read-worker slot").is_none()));
    }

    #[test]
    fn durable_writer_waits_for_a_stateful_read_worker_writer_boundary() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let protected_request = r#"{"command":"semanticMerge.import","requestId":"protected"}"#;
            let protected_policy =
                project_bridge_request_policy(protected_request).expect("protected policy");
            assert!(matches!(
                protected_policy.concurrency,
                ProjectBridgeCommandConcurrency::AffinityExclusive(_)
            ));
            let protected_process = Arc::new(spawn_delayed_echo_project_bridge_fixture());
            *bridge_state.read_processes[0]
                .lock()
                .expect("protected process slot") = Some(protected_process.clone());

            let protected_state = bridge_state.clone();
            let (protected_result_sender, protected_result_receiver) = mpsc::channel();
            let protected_thread = std::thread::spawn(move || {
                let permit = tauri::async_runtime::block_on(
                    protected_state.acquire_execution_permit_for_generation(
                        request_generation,
                        protected_policy.concurrency,
                    ),
                )
                .expect("admit protected writer");
                let result = run_project_bridge_request_with(
                    &protected_state,
                    request_generation,
                    protected_request,
                    protected_policy,
                    None,
                    &protected_state.read_processes[0],
                    || panic!("protected process already exists"),
                );
                drop(permit);
                protected_result_sender
                    .send(result)
                    .expect("send protected result");
            });
            wait_for_active_project_bridge_request(&protected_process);
            assert_eq!(
                bridge_state
                    .read_worker_requests
                    .active_protected_requests(),
                1
            );

            let write_policy = project_bridge_request_policy(
                r#"{"command":"changePlan.apply","requestId":"write"}"#,
            )
            .expect("write policy");
            let write_permit = bridge_state
                .acquire_execution_permit_for_request(request_generation, write_policy)
                .await
                .expect("writer waits for the protected response boundary");
            assert_eq!(
                protected_result_receiver
                    .recv_timeout(Duration::from_secs(5))
                    .expect("protected request result")
                    .expect("protected request must not be killed"),
                protected_request
            );
            protected_thread.join().expect("protected request thread");
            assert!(bridge_state.read_processes[0]
                .lock()
                .expect("protected process slot")
                .as_ref()
                .is_some_and(|current| Arc::ptr_eq(current, &protected_process)));
            drop(write_permit);
            recycle_project_bridge_process(&bridge_state).expect("reap protected fixture");
        });
    }

    #[test]
    fn durable_writer_does_not_preempt_an_affinity_ordered_file_operation() {
        tauri::async_runtime::block_on(async {
            let bridge_state = ProjectBridgeState::with_parallel_read_limit(1);
            let request_generation = bridge_state.generation.load(Ordering::Acquire);
            let request_read_generation = bridge_state.read_generation.load(Ordering::Acquire);
            let protected_request = r#"{"command":"recipes.export","requestId":"export"}"#;
            let protected_policy =
                project_bridge_request_policy(protected_request).expect("export policy");
            assert!(matches!(
                protected_policy.concurrency,
                ProjectBridgeCommandConcurrency::AffinityOrdered(_)
            ));
            assert!(!project_bridge_concurrency_is_preemptible_read(
                protected_policy.concurrency
            ));
            let protected_process = Arc::new(spawn_delayed_echo_project_bridge_fixture());
            *bridge_state.read_processes[0]
                .lock()
                .expect("export process slot") = Some(protected_process.clone());

            let protected_state = bridge_state.clone();
            let (protected_result_sender, protected_result_receiver) = mpsc::channel();
            let protected_thread = std::thread::spawn(move || {
                let permit = tauri::async_runtime::block_on(
                    protected_state.acquire_execution_permit_for_generation(
                        request_generation,
                        protected_policy.concurrency,
                    ),
                )
                .expect("admit export");
                let result = run_project_bridge_request_with(
                    &protected_state,
                    request_generation,
                    protected_request,
                    protected_policy,
                    None,
                    &protected_state.read_processes[0],
                    || panic!("export process already exists"),
                );
                drop(permit);
                protected_result_sender
                    .send(result)
                    .expect("send export result");
            });
            wait_for_active_project_bridge_request(&protected_process);
            assert_eq!(
                bridge_state
                    .read_worker_requests
                    .active_protected_requests(),
                1
            );

            let write_policy = project_bridge_request_policy(
                r#"{"command":"changePlan.apply","requestId":"write"}"#,
            )
            .expect("write policy");
            let write_permit = bridge_state
                .acquire_execution_permit_for_request(request_generation, write_policy)
                .await
                .expect("writer waits for export response boundary");
            assert_eq!(
                protected_result_receiver
                    .recv_timeout(Duration::from_secs(5))
                    .expect("export result")
                    .expect("export must not be killed"),
                protected_request
            );
            protected_thread.join().expect("export thread");
            assert_ne!(
                bridge_state.read_generation.load(Ordering::Acquire),
                request_read_generation,
                "every writer boundary must invalidate undelivered disposable responses"
            );
            assert!(bridge_state.read_processes[0]
                .lock()
                .expect("export process slot")
                .as_ref()
                .is_some_and(|current| Arc::ptr_eq(current, &protected_process)));
            drop(write_permit);
            recycle_project_bridge_process(&bridge_state).expect("reap export fixture");
        });
    }

    #[test]
    fn exclusive_writer_is_not_starved_by_a_reader_flood() {
        tauri::async_runtime::block_on(async {
            const READER_COUNT: usize = 64;
            let gate = Arc::new(tokio::sync::RwLock::with_max_readers((), 4));
            let initial_reader = gate.clone().read_owned().await;
            let acquisition_order = Arc::new(Mutex::new(Vec::with_capacity(READER_COUNT + 1)));
            let (writer_queued_sender, writer_queued_receiver) = tokio::sync::oneshot::channel();
            let writer_gate = gate.clone();
            let writer_order = acquisition_order.clone();
            let writer = tauri::async_runtime::spawn(async move {
                let mut write_permit = Box::pin(writer_gate.write_owned());
                let mut queued_sender = Some(writer_queued_sender);
                let _permit = std::future::poll_fn(|context| {
                    let result = write_permit.as_mut().poll(context);
                    if result.is_pending() {
                        if let Some(sender) = queued_sender.take() {
                            sender.send(()).expect("queue writer signal");
                        }
                    }
                    result
                })
                .await;
                writer_order.lock().expect("writer order").push(0_usize);
            });
            writer_queued_receiver.await.expect("writer queued");

            let readers = (1..=READER_COUNT)
                .map(|reader_index| {
                    let reader_gate = gate.clone();
                    let reader_order = acquisition_order.clone();
                    tauri::async_runtime::spawn(async move {
                        let _permit = reader_gate.read_owned().await;
                        reader_order
                            .lock()
                            .expect("reader order")
                            .push(reader_index);
                    })
                })
                .collect::<Vec<_>>();
            drop(initial_reader);
            writer.await.expect("writer task");
            for reader in readers {
                reader.await.expect("reader task");
            }
            let order = acquisition_order.lock().expect("acquisition order");
            assert_eq!(order.len(), READER_COUNT + 1);
            assert_eq!(order[0], 0, "queued writer must precede later readers");
        });
    }

    #[test]
    fn pending_project_bridge_requests_are_bounded_by_count_and_bytes() {
        let byte_limited_state = ProjectBridgeState::with_parallel_read_limit(1);
        let first = byte_limited_state
            .reserve_pending_request(MAX_PROJECT_BRIDGE_PENDING_REQUEST_BYTES / 2)
            .expect("the first request should fit the byte budget");
        let second = byte_limited_state
            .reserve_pending_request(MAX_PROJECT_BRIDGE_PENDING_REQUEST_BYTES / 2)
            .expect("the second request should fill the byte budget");
        assert!(byte_limited_state.reserve_pending_request(1).is_err());
        drop(first);
        byte_limited_state
            .reserve_pending_request(1)
            .expect("dropping a request should release its byte budget");
        drop(second);

        let count_limited_state = ProjectBridgeState::with_parallel_read_limit(1);
        let requests = (0..MAX_PROJECT_BRIDGE_PENDING_REQUESTS)
            .map(|_| {
                count_limited_state
                    .reserve_pending_request(1)
                    .expect("request should fit the count budget")
            })
            .collect::<Vec<_>>();
        assert!(count_limited_state.reserve_pending_request(1).is_err());
        drop(requests);
        count_limited_state
            .reserve_pending_request(1)
            .expect("dropping requests should release the count budget");
    }

    #[test]
    fn project_bridge_admission_is_fifo() {
        let admission = Arc::new(ProjectBridgeAdmission::default());
        let active = admission
            .acquire(1)
            .expect("the first request should acquire admission");
        let (acquired_sender, acquired_receiver) = mpsc::channel();

        let second_admission = admission.clone();
        let second_sender = acquired_sender.clone();
        let second = std::thread::spawn(move || {
            let _guard = second_admission
                .acquire(2)
                .expect("the second request should acquire admission");
            second_sender.send(2).expect("send second acquisition");
        });
        wait_for_admission_queue_length(&admission, 1);

        let third_admission = admission.clone();
        let third = std::thread::spawn(move || {
            let _guard = third_admission
                .acquire(3)
                .expect("the third request should acquire admission");
            acquired_sender.send(3).expect("send third acquisition");
        });
        wait_for_admission_queue_length(&admission, 2);

        drop(active);
        assert_eq!(
            acquired_receiver
                .recv_timeout(Duration::from_secs(5))
                .expect("the second request should be admitted first"),
            2
        );
        assert_eq!(
            acquired_receiver
                .recv_timeout(Duration::from_secs(5))
                .expect("the third request should be admitted second"),
            3
        );

        second.join().expect("second admission thread");
        third.join().expect("third admission thread");
    }

    #[test]
    fn queued_project_bridge_admission_waits_until_the_active_request_finishes() {
        let admission = Arc::new(ProjectBridgeAdmission::default());
        let active = admission
            .acquire(1)
            .expect("the first request should acquire admission");
        let (acquired_sender, acquired_receiver) = mpsc::channel();

        let queued_admission = admission.clone();
        let queued = std::thread::spawn(move || {
            let _guard = queued_admission
                .acquire(2)
                .expect("the queued request should acquire admission");
            acquired_sender.send(()).expect("send queued acquisition");
        });
        wait_for_admission_queue_length(&admission, 1);
        assert!(
            acquired_receiver
                .recv_timeout(Duration::from_millis(50))
                .is_err(),
            "the queued request must not bypass the active request"
        );

        drop(active);
        acquired_receiver
            .recv_timeout(Duration::from_secs(5))
            .expect("the queued request should remain admitted instead of expiring");
        queued.join().expect("queued admission thread");
    }

    fn command_affinity(command: &str) -> Option<ProjectBridgeReadAffinity> {
        match project_bridge_command_concurrency(command)? {
            ProjectBridgeCommandConcurrency::IndependentRead(affinity)
            | ProjectBridgeCommandConcurrency::AffinityOrdered(affinity)
            | ProjectBridgeCommandConcurrency::AffinityExclusive(affinity) => Some(affinity),
            ProjectBridgeCommandConcurrency::OwnerOrdered
            | ProjectBridgeCommandConcurrency::Exclusive => None,
        }
    }

    fn project_bridge_command_environment<'a>(
        command: &'a Command,
        name: &str,
    ) -> Option<Option<&'a OsStr>> {
        command
            .get_envs()
            .find(|(key, _)| *key == OsStr::new(name))
            .map(|(_, value)| value)
    }

    fn parse_csharp_command_declarations(source: &str) -> BTreeMap<String, String> {
        source
            .lines()
            .filter_map(|line| {
                let declaration = line.trim().strip_prefix("public const string ")?;
                let (name, value) = declaration.split_once(" = ")?;
                Some((
                    name.to_owned(),
                    value.trim_end_matches(';').trim_matches('"').to_owned(),
                ))
            })
            .collect()
    }

    fn parse_csharp_dispatcher_commands(
        source: &str,
        declarations: &BTreeMap<String, String>,
    ) -> BTreeSet<String> {
        source
            .lines()
            .filter(|line| line.contains("=>"))
            .filter_map(|line| {
                let suffix = line.split_once("KmCommandNames.")?.1;
                let name = suffix
                    .split(|character: char| !character.is_ascii_alphanumeric() && character != '_')
                    .next()?;
                declarations.get(name).cloned()
            })
            .collect()
    }

    fn parse_typescript_command_values(source: &str) -> BTreeSet<String> {
        let mut in_values = false;
        let mut values = BTreeSet::new();
        for line in source.lines() {
            let line = line.trim();
            if line == "export const kmCommandNameValues = [" {
                in_values = true;
                continue;
            }
            if !in_values {
                continue;
            }
            if line == "] as const;" {
                break;
            }
            if let Some(value) = line
                .strip_prefix('"')
                .and_then(|value| value.strip_suffix("\","))
            {
                values.insert(value.to_owned());
            }
        }
        values
    }

    fn wait_for_admission_queue_length(admission: &ProjectBridgeAdmission, expected: usize) {
        let deadline = Instant::now() + Duration::from_secs(5);
        loop {
            let queued = admission
                .state
                .lock()
                .expect("inspect admission state")
                .waiting_request_tokens
                .len();
            if queued == expected {
                return;
            }
            assert!(
                Instant::now() < deadline,
                "the request did not enter the admission queue"
            );
            std::thread::yield_now();
        }
    }

    fn wait_for_active_project_bridge_request(process: &ProjectBridgeProcess) {
        let deadline = Instant::now() + Duration::from_secs(5);
        while process.active_request_token.load(Ordering::Acquire)
            == PROJECT_BRIDGE_NO_ACTIVE_REQUEST_TOKEN
        {
            assert!(
                Instant::now() < deadline,
                "the fixture did not begin its bridge request"
            );
            std::thread::yield_now();
        }
    }

    fn spawn_echo_project_bridge_fixture() -> ProjectBridgeProcess {
        #[cfg(windows)]
        let mut command = {
            let mut command = Command::new("powershell.exe");
            command.args([
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "$line = [Console]::In.ReadLine(); while ($null -ne $line) { [Console]::Out.WriteLine($line); [Console]::Out.Flush(); $line = [Console]::In.ReadLine() }",
            ]);
            command
        };
        #[cfg(unix)]
        let mut command = {
            let mut command = Command::new("sh");
            command.args([
                "-c",
                "while IFS= read -r line; do printf '%s\\n' \"$line\"; done",
            ]);
            command
        };
        spawn_project_bridge_fixture(&mut command)
    }

    fn spawn_delayed_echo_project_bridge_fixture() -> ProjectBridgeProcess {
        #[cfg(windows)]
        let mut command = {
            let mut command = Command::new("powershell.exe");
            command.args([
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "$line = [Console]::In.ReadLine(); if ($null -ne $line) { Start-Sleep -Milliseconds 250; [Console]::Out.WriteLine($line); [Console]::Out.Flush() }; $line = [Console]::In.ReadLine(); while ($null -ne $line) { [Console]::Out.WriteLine($line); [Console]::Out.Flush(); $line = [Console]::In.ReadLine() }",
            ]);
            command
        };
        #[cfg(unix)]
        let mut command = {
            let mut command = Command::new("sh");
            command.args([
                "-c",
                "first=1; while IFS= read -r line; do if [ \"$first\" = 1 ]; then sleep 0.25; first=0; fi; printf '%s\\n' \"$line\"; done",
            ]);
            command
        };
        spawn_project_bridge_fixture(&mut command)
    }

    fn spawn_crashing_project_bridge_fixture() -> ProjectBridgeProcess {
        #[cfg(windows)]
        let mut command = {
            let mut command = Command::new("cmd.exe");
            command.args(["/d", "/q", "/c", "exit", "/b", "7"]);
            command
        };
        #[cfg(unix)]
        let mut command = {
            let mut command = Command::new("sh");
            command.args(["-c", "exit 7"]);
            command
        };
        spawn_project_bridge_fixture(&mut command)
    }

    fn spawn_silent_project_bridge_fixture() -> ProjectBridgeProcess {
        #[cfg(windows)]
        let mut command = {
            let mut command = Command::new("powershell.exe");
            command.args([
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "Start-Sleep -Seconds 30",
            ]);
            command
        };
        #[cfg(unix)]
        let mut command = {
            let mut command = Command::new("sh");
            command.args(["-c", "sleep 30"]);
            command
        };
        spawn_project_bridge_fixture(&mut command)
    }

    fn spawn_partial_response_fixture() -> Child {
        #[cfg(windows)]
        let mut command = {
            let mut command = Command::new("powershell.exe");
            command.args([
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "[Console]::Out.Write('partial')",
            ]);
            command
        };
        #[cfg(unix)]
        let mut command = {
            let mut command = Command::new("sh");
            command.args(["-c", "printf partial"]);
            command
        };
        command
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .expect("spawn partial response fixture")
    }

    fn spawn_project_bridge_fixture(command: &mut Command) -> ProjectBridgeProcess {
        let mut child = command
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .expect("spawn project bridge fixture");
        let stdin = child.stdin.take().expect("fixture stdin");
        let stdout = child.stdout.take().expect("fixture stdout");
        ProjectBridgeProcess::new(child, stdin, stdout)
    }
}
