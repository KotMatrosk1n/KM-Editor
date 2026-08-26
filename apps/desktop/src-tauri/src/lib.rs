// SPDX-License-Identifier: GPL-3.0-only

use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::{mpsc, Arc, Mutex, MutexGuard, TryLockError};
use std::thread::JoinHandle;
use std::time::{Duration, Instant, SystemTime};

#[cfg(windows)]
use std::os::windows::process::CommandExt;
use tauri::{Emitter, Manager};
use tauri_plugin_shell::ShellExt;

#[cfg(windows)]
mod windows_app_identity;

const BRIDGE_SIDECAR_NAME: &str = "km-tools-bridge";
const MAX_PROJECT_BRIDGE_IN_FLIGHT_REQUESTS: usize = 8;
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
const PROJECT_BRIDGE_PROJECT_READ_TIMEOUT: Duration = Duration::from_secs(30);
const PROJECT_BRIDGE_OUTPUT_READ_TIMEOUT: Duration = Duration::from_secs(45);
const PROJECT_BRIDGE_WORKFLOW_EXPECTED_TIMEOUT_SECONDS: u64 = 75;
const PROJECT_BRIDGE_WORKFLOW_PROVISION_MULTIPLIER: u64 = 4;
const PROJECT_BRIDGE_WORKFLOW_CEILING_MULTIPLIER: u64 = 2;
const PROJECT_BRIDGE_EDITOR_OPERATION_TIMEOUT: Duration = Duration::from_secs(
    PROJECT_BRIDGE_WORKFLOW_EXPECTED_TIMEOUT_SECONDS * PROJECT_BRIDGE_WORKFLOW_PROVISION_MULTIPLIER,
);
const PROJECT_BRIDGE_QUEUE_WAIT_TIMEOUT: Duration = Duration::from_secs(60);
const PROJECT_BRIDGE_TERMINATION_WAIT_TIMEOUT: Duration = Duration::from_secs(2);
const PROJECT_BRIDGE_OUTER_TIMEOUT_MARGIN: Duration = Duration::from_secs(5);
const PROJECT_BRIDGE_WORKFLOW_LOAD_TIMEOUT: Duration = Duration::from_secs(
    PROJECT_BRIDGE_WORKFLOW_EXPECTED_TIMEOUT_SECONDS
        * PROJECT_BRIDGE_WORKFLOW_PROVISION_MULTIPLIER
        * PROJECT_BRIDGE_WORKFLOW_CEILING_MULTIPLIER,
);
// New commands must never become unbounded by omission. They receive this generous
// ceiling without replay until their retry semantics are explicitly reviewed.
const PROJECT_BRIDGE_DEFAULT_OPERATION_TIMEOUT: Duration = PROJECT_BRIDGE_WORKFLOW_LOAD_TIMEOUT;
const PROJECT_BRIDGE_QUEUE_WAIT_POLL_INTERVAL: Duration = Duration::from_millis(10);
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

const fn checked_project_bridge_limit(value: usize, multiplier: usize) -> usize {
    match value.checked_mul(multiplier) {
        Some(limit) => limit,
        None => panic!("project bridge size limit overflow"),
    }
}

struct CloseGuardState {
    is_guarded: AtomicBool,
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
    generation: Arc<AtomicUsize>,
    in_flight_requests: Arc<AtomicUsize>,
    maximum_in_flight_requests: usize,
}

impl Default for ProjectBridgeState {
    fn default() -> Self {
        Self::with_request_limit(MAX_PROJECT_BRIDGE_IN_FLIGHT_REQUESTS)
    }
}

impl ProjectBridgeState {
    fn with_request_limit(maximum_in_flight_requests: usize) -> Self {
        assert!(maximum_in_flight_requests > 0);
        Self {
            process: Arc::new(Mutex::new(None)),
            generation: Arc::new(AtomicUsize::new(0)),
            in_flight_requests: Arc::new(AtomicUsize::new(0)),
            maximum_in_flight_requests,
        }
    }

    fn try_acquire_request_permit(&self) -> Result<ProjectBridgeRequestPermit, String> {
        self.in_flight_requests
            .fetch_update(Ordering::AcqRel, Ordering::Acquire, |current| {
                (current < self.maximum_in_flight_requests).then_some(current + 1)
            })
            .map_err(|_| {
                "Project bridge request capacity is full. Wait for the current editor operation to finish and retry."
                    .to_owned()
            })?;
        Ok(ProjectBridgeRequestPermit {
            in_flight_requests: self.in_flight_requests.clone(),
        })
    }
}

struct ProjectBridgeRequestPermit {
    in_flight_requests: Arc<AtomicUsize>,
}

impl Drop for ProjectBridgeRequestPermit {
    fn drop(&mut self) {
        self.in_flight_requests.fetch_sub(1, Ordering::AcqRel);
    }
}

struct ProjectBridgeProcess {
    active_request_token: AtomicUsize,
    next_request_token: AtomicUsize,
    child: Mutex<Option<Child>>,
    io: Mutex<ProjectBridgeIo>,
}

struct ProjectBridgeIo {
    stdin: ChildStdin,
    stdout: BufReader<ChildStdout>,
}

enum ProjectBridgeRequestFailure {
    Retryable(String),
    NonRetryable(String),
    TimedOut(String),
}

enum ProjectBridgeIoLockFailure {
    Poisoned,
    TimedOut,
}

#[derive(Clone, Copy)]
struct ProjectBridgeRequestPolicy {
    execution_timeout: Duration,
    retry_after_transport_failure: bool,
}

struct ProjectBridgeRequestWatchdog {
    cancellation_sender: mpsc::Sender<()>,
    request_state: Arc<AtomicUsize>,
    thread: Option<JoinHandle<()>>,
}

struct ProjectBridgeActiveRequest<'a> {
    active_request_token: &'a AtomicUsize,
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
    if request_json.len() > MAX_PROJECT_BRIDGE_REQUEST_BYTES {
        return Err("Project bridge request exceeded the supported size limit.".to_owned());
    }

    let bridge_state = bridge_state.inner().clone();
    let request_permit = bridge_state.try_acquire_request_permit()?;
    let request_generation = bridge_state.generation.load(Ordering::Acquire);
    let outer_timeout = project_bridge_outer_timeout(&request_json);
    let request_bridge_state = bridge_state.clone();
    let mut request_task = tauri::async_runtime::spawn_blocking(move || {
        let _request_permit = request_permit;
        run_project_bridge_request(
            &app_handle,
            &request_bridge_state,
            request_generation,
            request_json,
        )
    });

    match tokio::time::timeout(outer_timeout, &mut request_task).await {
        Ok(request_result) => request_result
            .map_err(|error| format!("Project bridge request task failed: {error}"))?,
        Err(_) => {
            request_task.abort();
            let recovery_bridge_state = bridge_state.clone();
            tauri::async_runtime::spawn_blocking(move || {
                let _ = recycle_project_bridge_request_if_current(
                    &recovery_bridge_state,
                    request_generation,
                );
            });
            Err(create_project_bridge_outer_timeout_error(outer_timeout))
        }
    }
}

#[tauri::command]
async fn recycle_project_bridge(
    bridge_state: tauri::State<'_, ProjectBridgeState>,
) -> Result<(), String> {
    let bridge_state = bridge_state.inner().clone();
    tauri::async_runtime::spawn_blocking(move || recycle_project_bridge_process(&bridge_state))
        .await
        .map_err(|error| format!("Project bridge recycle task failed: {error}"))?
}

fn run_project_bridge_request(
    app_handle: &tauri::AppHandle,
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    request_json: String,
) -> Result<String, String> {
    run_project_bridge_request_with(bridge_state, request_generation, &request_json, || {
        start_project_bridge_process(app_handle)
    })
}

fn run_project_bridge_request_with<F>(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
    request_json: &str,
    mut start_process: F,
) -> Result<String, String>
where
    F: FnMut() -> Result<ProjectBridgeProcess, String>,
{
    let request_policy = project_bridge_request_policy(request_json);
    let may_retry = request_policy.is_some_and(|policy| policy.retry_after_transport_failure);

    for attempt in 0..2 {
        let process = get_or_start_project_bridge_process(
            bridge_state,
            request_generation,
            &mut start_process,
        )?;
        let request_result = process.request(
            bridge_state,
            request_generation,
            request_json,
            request_policy.map(|policy| policy.execution_timeout),
        );
        match request_result {
            Ok(response) => return Ok(response),
            Err(ProjectBridgeRequestFailure::TimedOut(error)) => return Err(error),
            Err(ProjectBridgeRequestFailure::NonRetryable(error)) => {
                remove_failed_project_bridge_process(bridge_state, &process)?;
                return Err(error);
            }
            Err(ProjectBridgeRequestFailure::Retryable(error)) => {
                remove_failed_project_bridge_process(bridge_state, &process)?;
                ensure_project_bridge_request_is_current(bridge_state, request_generation)?;
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
    start_process: &mut impl FnMut() -> Result<ProjectBridgeProcess, String>,
) -> Result<Arc<ProjectBridgeProcess>, String> {
    let mut process = bridge_state
        .process
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
    failed_process: &Arc<ProjectBridgeProcess>,
) -> Result<(), String> {
    let removed = {
        let mut current = bridge_state
            .process
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
            next_request_token: AtomicUsize::new(1),
            child: Mutex::new(Some(child)),
            io: Mutex::new(ProjectBridgeIo {
                stdin,
                stdout: BufReader::new(stdout),
            }),
        }
    }

    fn request(
        self: &Arc<Self>,
        bridge_state: &ProjectBridgeState,
        request_generation: usize,
        request_json: &str,
        execution_timeout: Option<Duration>,
    ) -> Result<String, ProjectBridgeRequestFailure> {
        let request_token = self.allocate_request_token();
        let mut io =
            match lock_project_bridge_request_io(&self.io, Some(PROJECT_BRIDGE_QUEUE_WAIT_TIMEOUT))
            {
                Ok(io) => io,
                Err(ProjectBridgeIoLockFailure::TimedOut) => {
                    return Err(ProjectBridgeRequestFailure::TimedOut(
                        create_project_bridge_queue_timeout_error(
                            PROJECT_BRIDGE_QUEUE_WAIT_TIMEOUT,
                        ),
                    ));
                }
                Err(ProjectBridgeIoLockFailure::Poisoned) => {
                    return Err(ProjectBridgeRequestFailure::Retryable(
                        "Project bridge I/O lock was poisoned.".to_owned(),
                    ));
                }
            };
        let _active_request =
            ProjectBridgeActiveRequest::begin(&self.active_request_token, request_token);
        let watchdog = execution_timeout.map(|timeout| {
            ProjectBridgeRequestWatchdog::start(
                bridge_state.clone(),
                request_generation,
                self.clone(),
                request_token,
                timeout,
            )
        });

        let request_result = (|| -> Result<String, ProjectBridgeRequestFailure> {
            ensure_project_bridge_request_is_current(bridge_state, request_generation)
                .map_err(ProjectBridgeRequestFailure::Retryable)?;
            io.stdin
                .write_all(request_json.as_bytes())
                .map_err(|error| {
                    ProjectBridgeRequestFailure::Retryable(format!(
                        "Could not send the project bridge request: {error}"
                    ))
                })?;
            io.stdin
                .write_all(b"\n")
                .and_then(|_| io.stdin.flush())
                .map_err(|error| {
                    ProjectBridgeRequestFailure::Retryable(format!(
                        "Could not send the project bridge request: {error}"
                    ))
                })?;

            let mut response = read_bounded_project_bridge_response(&mut io.stdout)?;

            while response.ends_with(['\r', '\n']) {
                response.pop();
            }

            ensure_project_bridge_request_is_current(bridge_state, request_generation)
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

fn read_bounded_project_bridge_response(
    reader: &mut impl BufRead,
) -> Result<String, ProjectBridgeRequestFailure> {
    let mut response = Vec::with_capacity(8 * 1024);
    loop {
        let available = reader.fill_buf().map_err(|error| {
            ProjectBridgeRequestFailure::Retryable(format!(
                "Could not read the project bridge response: {error}"
            ))
        })?;
        if available.is_empty() {
            break;
        }

        let newline = available.iter().position(|byte| *byte == b'\n');
        let chunk_length = newline.map_or(available.len(), |index| index + 1);
        let next_framed_length = response.len().checked_add(chunk_length).ok_or_else(|| {
            ProjectBridgeRequestFailure::NonRetryable(
                "Project bridge response exceeded the supported size limit.".to_owned(),
            )
        })?;
        if next_framed_length > MAX_PROJECT_BRIDGE_FRAMED_RESPONSE_BYTES {
            return Err(ProjectBridgeRequestFailure::NonRetryable(
                "Project bridge response exceeded the supported size limit.".to_owned(),
            ));
        }

        response.extend_from_slice(&available[..chunk_length]);
        reader.consume(chunk_length);
        if newline.is_some() {
            break;
        }
    }

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
        return Err(ProjectBridgeRequestFailure::NonRetryable(
            "Project bridge response exceeded the supported size limit.".to_owned(),
        ));
    }

    String::from_utf8(response).map_err(|_| {
        ProjectBridgeRequestFailure::Retryable(
            "Project bridge runner returned a response that was not valid UTF-8.".to_owned(),
        )
    })
}

impl<'a> ProjectBridgeActiveRequest<'a> {
    fn begin(active_request_token: &'a AtomicUsize, request_token: usize) -> Self {
        debug_assert_ne!(request_token, PROJECT_BRIDGE_NO_ACTIVE_REQUEST_TOKEN);
        active_request_token.store(request_token, Ordering::Release);
        Self {
            active_request_token,
            request_token,
        }
    }
}

impl Drop for ProjectBridgeActiveRequest<'_> {
    fn drop(&mut self) {
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

fn lock_project_bridge_request_io<'a, T>(
    io: &'a Mutex<T>,
    timeout: Option<Duration>,
) -> Result<MutexGuard<'a, T>, ProjectBridgeIoLockFailure> {
    let Some(timeout) = timeout else {
        return io.lock().map_err(|_| ProjectBridgeIoLockFailure::Poisoned);
    };
    let started_at = Instant::now();

    loop {
        match io.try_lock() {
            Ok(io) => return Ok(io),
            Err(TryLockError::Poisoned(_)) => return Err(ProjectBridgeIoLockFailure::Poisoned),
            Err(TryLockError::WouldBlock) if started_at.elapsed() >= timeout => {
                return Err(ProjectBridgeIoLockFailure::TimedOut)
            }
            Err(TryLockError::WouldBlock) => {
                std::thread::sleep(PROJECT_BRIDGE_QUEUE_WAIT_POLL_INTERVAL)
            }
        }
    }
}

fn project_bridge_request_policy(request_json: &str) -> Option<ProjectBridgeRequestPolicy> {
    let request: serde_json::Value = serde_json::from_str(request_json).ok()?;
    let command = request.get("command")?.as_str()?;

    if is_replay_safe_edit_session_command(command) {
        return Some(ProjectBridgeRequestPolicy {
            execution_timeout: PROJECT_BRIDGE_EDITOR_OPERATION_TIMEOUT,
            retry_after_transport_failure: true,
        });
    }

    if command == "changeSets.captureSession" {
        return Some(ProjectBridgeRequestPolicy {
            execution_timeout: PROJECT_BRIDGE_EDITOR_OPERATION_TIMEOUT,
            retry_after_transport_failure: false,
        });
    }

    if matches!(
        command,
        "changePlan.apply"
            | "dynamaxAdventures.seed.save.set"
            | "gameplaySettings.update.preview"
            | "gameplaySettings.update.apply"
    ) {
        return Some(ProjectBridgeRequestPolicy {
            execution_timeout: PROJECT_BRIDGE_EDITOR_OPERATION_TIMEOUT,
            retry_after_transport_failure: false,
        });
    }

    if matches!(
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
        return Some(ProjectBridgeRequestPolicy {
            execution_timeout: PROJECT_BRIDGE_PROJECT_READ_TIMEOUT,
            retry_after_transport_failure: true,
        });
    }

    if matches!(command, "output.cleanup.preview" | "output.history.list") {
        return Some(ProjectBridgeRequestPolicy {
            execution_timeout: PROJECT_BRIDGE_OUTPUT_READ_TIMEOUT,
            retry_after_transport_failure: true,
        });
    }

    if matches!(
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
            | "semanticMerge.preview"
            | "gameModules.capabilities"
            | "gameModules.query"
            | "modMerger.stage"
            | "svModMerger.stage"
            | "zaModMerger.stage"
            | "researchLab.capabilities"
            | "researchLab.source.open"
            | "researchLab.source.close"
            | "researchLab.compare"
            | "researchLab.byteWindow"
            | "researchLab.annotations.read"
            | "recipes.export"
            | "recipes.validate"
            | "recipes.preview"
            | "support.report.build"
    ) {
        return Some(ProjectBridgeRequestPolicy {
            execution_timeout: PROJECT_BRIDGE_WORKFLOW_LOAD_TIMEOUT,
            retry_after_transport_failure: true,
        });
    }

    if command.ends_with(".load") {
        return Some(ProjectBridgeRequestPolicy {
            execution_timeout: PROJECT_BRIDGE_WORKFLOW_LOAD_TIMEOUT,
            retry_after_transport_failure: true,
        });
    }

    // Fail closed for replay safety while still guaranteeing shared-bridge recovery.
    Some(ProjectBridgeRequestPolicy {
        execution_timeout: PROJECT_BRIDGE_DEFAULT_OPERATION_TIMEOUT,
        retry_after_transport_failure: false,
    })
}

fn project_bridge_outer_timeout(request_json: &str) -> Duration {
    let policy =
        project_bridge_request_policy(request_json).unwrap_or(ProjectBridgeRequestPolicy {
            execution_timeout: PROJECT_BRIDGE_DEFAULT_OPERATION_TIMEOUT,
            retry_after_transport_failure: false,
        });
    let attempt_budget = PROJECT_BRIDGE_QUEUE_WAIT_TIMEOUT
        .saturating_add(policy.execution_timeout)
        .saturating_add(PROJECT_BRIDGE_TERMINATION_WAIT_TIMEOUT);
    let maximum_attempts = if policy.retry_after_transport_failure {
        2
    } else {
        1
    };

    attempt_budget
        .saturating_mul(maximum_attempts)
        .saturating_add(PROJECT_BRIDGE_OUTER_TIMEOUT_MARGIN)
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
            | "rowClipboard.paste.preview"
            | "rowClipboard.paste.stage"
            | "royalCandy.workflow.stage"
            | "shinyRate.stage"
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
    let removed = {
        let mut current = bridge_state
            .process
            .lock()
            .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;
        if bridge_state.generation.load(Ordering::Acquire) == request_generation
            && current
                .as_ref()
                .is_some_and(|process| Arc::ptr_eq(process, timed_out_process))
        {
            bridge_state.generation.fetch_add(1, Ordering::AcqRel);
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

fn recycle_project_bridge_request_if_current(
    bridge_state: &ProjectBridgeState,
    request_generation: usize,
) -> Result<(), String> {
    let removed = {
        let mut current = bridge_state
            .process
            .lock()
            .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;
        if bridge_state.generation.load(Ordering::Acquire) == request_generation {
            bridge_state.generation.fetch_add(1, Ordering::AcqRel);
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

fn create_project_bridge_timeout_error(timeout: Duration) -> String {
    format!(
        "The project request did not return within {} seconds. KM Editor stopped the stalled bridge so the interface can recover. No response was accepted. Refresh the current editor state before retrying because a durable request may have finished before the connection stopped.",
        timeout.as_secs()
    )
}

fn create_project_bridge_outer_timeout_error(timeout: Duration) -> String {
    format!(
        "The project request did not return after {} seconds, including bridge recovery time. KM Editor released the interface and discarded any late response. Refresh the current editor state before retrying because a durable request may have finished before the connection stopped.",
        timeout.as_secs()
    )
}

fn create_project_bridge_queue_timeout_error(timeout: Duration) -> String {
    format!(
        "The project bridge remained busy for {} seconds. This request was not sent. Wait for the current operation to finish, then retry.",
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

fn terminate_project_bridge_child(child: &mut Option<Child>) -> Result<(), String> {
    let Some(mut child) = child.take() else {
        return Ok(());
    };

    match child.try_wait() {
        Ok(Some(_)) => return Ok(()),
        Ok(None) | Err(_) => {}
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
            Ok(None) => std::thread::sleep(PROJECT_BRIDGE_QUEUE_WAIT_POLL_INTERVAL),
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
) -> Result<ProjectBridgeProcess, String> {
    let mut command = resolve_project_bridge_command(app_handle, "bridge")?;
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
    command
        .spawn()
        .map(|_| ())
        .map_err(|error| format!("Could not open the folder: {error}"))
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
fn exit_app(app_handle: tauri::AppHandle) {
    app_handle.state::<SupportSearchState>().cancel();
    shutdown_project_bridge(&app_handle);
    app_handle.exit(0);
}

fn shutdown_project_bridge(app_handle: &tauri::AppHandle) {
    let bridge_state = app_handle.state::<ProjectBridgeState>();
    if let Ok(Some(process)) = detach_project_bridge_process(&bridge_state) {
        let _ = process.terminate();
    }
}

fn recycle_project_bridge_process(bridge_state: &ProjectBridgeState) -> Result<(), String> {
    if let Some(process) = detach_project_bridge_process(bridge_state)? {
        process.terminate()?;
    }
    Ok(())
}

fn detach_project_bridge_process(
    bridge_state: &ProjectBridgeState,
) -> Result<Option<Arc<ProjectBridgeProcess>>, String> {
    let mut process = bridge_state
        .process
        .lock()
        .map_err(|_| "Project bridge process lock was poisoned.".to_owned())?;
    bridge_state.generation.fetch_add(1, Ordering::AcqRel);
    Ok(process.take())
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
        })
        .manage(SupportSearchState::default())
        .manage(ProjectBridgeState::default())
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
                    app_handle.state::<SupportSearchState>().cancel();
                    shutdown_project_bridge(&app_handle);
                    app_handle.exit(0);
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
