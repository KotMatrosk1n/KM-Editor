// SPDX-License-Identifier: GPL-3.0-only
#[cfg(windows)]
mod animation;
#[cfg(windows)]
mod background;
#[cfg(windows)]
mod gpu;
#[cfg(windows)]
mod scene;
#[cfg(windows)]
mod window;
use std::sync::{
    atomic::{AtomicBool, AtomicU64, Ordering},
    Arc, Mutex,
};
use tauri::Manager;

#[derive(Default)]
pub struct PreviewState {
    session: Mutex<Option<String>>,
    generation: Arc<AtomicU64>,
    busy: AtomicBool,
    viewport: Mutex<Viewport>,
    #[cfg(windows)]
    proxy: std::sync::OnceLock<Result<winit::event_loop::EventLoopProxy<window::Event>, String>>,
}
#[derive(Clone, Copy, Default, serde::Deserialize)]
pub struct Viewport {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
    pub visible: bool,
    pub clip: Option<ViewportClip>,
    #[serde(default = "default_background")]
    pub background: [u8; 3],
    #[serde(default)]
    pub grid: bool,
}
fn default_background() -> [u8; 3] {
    [52, 59, 68]
}
#[derive(Clone, Copy, serde::Deserialize)]
pub struct ViewportClip {
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}
#[tauri::command]
pub fn model_preview_viewport(
    state: tauri::State<'_, PreviewState>,
    session: String,
    viewport: Viewport,
) -> Result<(), String> {
    if !state.current(&session) {
        return Ok(());
    }
    if viewport.x.unsigned_abs() > 32768
        || viewport.y.unsigned_abs() > 32768
        || viewport.width > 8192
        || viewport.height > 8192
        || viewport.clip.is_some_and(|clip| {
            clip.x > 8192 || clip.y > 8192 || clip.width > 8192 || clip.height > 8192
        })
    {
        return Err("KM-MODEL-UNSUPPORTED".into());
    }
    *state.viewport.lock().map_err(|_| "KM-MODEL-CANCELLED")? = viewport;
    #[cfg(windows)]
    if let Some(Ok(proxy)) = state.proxy.get() {
        let _ = proxy.send_event(window::Event::Viewport { session, viewport });
    }
    Ok(())
}
#[tauri::command]
pub fn model_preview_camera(
    state: tauri::State<'_, PreviewState>,
    session: String,
    action: String,
) -> Result<(), String> {
    if !state.current(&session) {
        return Ok(());
    }
    if !matches!(
        action.as_str(),
        "reset" | "frame" | "left" | "right" | "up" | "down" | "in" | "out" | "focus"
    ) {
        return Err("KM-MODEL-UNSUPPORTED".into());
    }
    #[cfg(windows)]
    if let Some(Ok(proxy)) = state.proxy.get() {
        let _ = proxy.send_event(window::Event::Camera { session, action });
    }
    Ok(())
}
#[tauri::command]
pub fn model_preview_playback(
    state: tauri::State<'_, PreviewState>,
    session: String,
    action: String,
    value: f32,
) -> Result<(), String> {
    if !state.current(&session) {
        return Ok(());
    }
    if !value.is_finite()
        || !matches!(
            action.as_str(),
            "play" | "pause" | "restart" | "seek" | "speed" | "loop"
        )
    {
        return Err("KM-MODEL-UNSUPPORTED".into());
    }
    #[cfg(windows)]
    if let Some(Ok(proxy)) = state.proxy.get() {
        let _ = proxy.send_event(window::Event::Playback {
            session,
            action,
            value,
        });
    }
    Ok(())
}
impl PreviewState {
    fn current(&self, session: &str) -> bool {
        self.session
            .lock()
            .is_ok_and(|value| value.as_deref() == Some(session))
    }
}
#[tauri::command]
pub fn model_preview_activate(
    state: tauri::State<'_, PreviewState>,
    session: String,
) -> Result<(), String> {
    if session.len() != 36 || !session.chars().all(|c| c.is_ascii_hexdigit() || c == '-') {
        return Err("KM-MODEL-CANCELLED".into());
    }
    let mut active = state.session.lock().map_err(|_| "KM-MODEL-CANCELLED")?;
    let through = state.generation.fetch_add(1, Ordering::AcqRel);
    *active = Some(session);
    #[cfg(windows)]
    if let Some(Ok(proxy)) = state.proxy.get() {
        let _ = proxy.send_event(window::Event::Close { through });
    }
    Ok(())
}
#[tauri::command]
pub fn model_preview_close(
    state: tauri::State<'_, PreviewState>,
    session: String,
) -> Result<(), String> {
    let mut active = state.session.lock().map_err(|_| "KM-MODEL-CANCELLED")?;
    if active.as_deref() == Some(&session) {
        let through = state.generation.fetch_add(1, Ordering::AcqRel);
        *active = None;
        #[cfg(windows)]
        if let Some(Ok(proxy)) = state.proxy.get() {
            let _ = proxy.send_event(window::Event::Close { through });
        }
    }
    Ok(())
}
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PreviewInfo {
    adapter: String,
    backend: &'static str,
    selection: &'static str,
    clips: Vec<String>,
    clip: Option<String>,
    duration: f32,
    looped: bool,
    warnings: Vec<String>,
}

#[tauri::command]
pub async fn model_preview_open(
    app: tauri::AppHandle,
    state: tauri::State<'_, PreviewState>,
    bridge: tauri::State<'_, super::ProjectBridgeState>,
    trace: tauri::State<'_, super::ProjectBridgeTraceState>,
    paths: serde_json::Value,
    id: String,
    title: String,
    session: String,
    animation: Option<String>,
) -> Result<PreviewInfo, String> {
    #[cfg(not(windows))]
    {
        let _ = (
            app, state, bridge, trace, paths, id, title, session, animation,
        );
        Err("KM-MODEL-GPU-UNAVAILABLE".into())
    }
    #[cfg(windows)]
    {
        if !state.current(&session) {
            return Err("KM-MODEL-CANCELLED".into());
        }
        if state.busy.swap(true, Ordering::AcqRel) {
            return Err("KM-MODEL-BUSY".into());
        }
        struct Busy<'a>(&'a AtomicBool);
        impl Drop for Busy<'_> {
            fn drop(&mut self) {
                self.0.store(false, Ordering::Release);
            }
        }
        let _busy = Busy(&state.busy);
        let generation = state.generation.load(Ordering::Acquire);
        // One bounded transfer per process. No game paths or binary payload cross into a web renderer.
        let token = format!(
            "{:08x}{:024x}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .map_err(|_| "KM-MODEL-UNSUPPORTED")?
                .as_nanos()
        );
        let file = std::env::temp_dir()
            .join("km-editor-model-preview")
            .join(format!("{token}.kmv"));
        use std::os::windows::fs::OpenOptionsExt;
        std::fs::create_dir_all(file.parent().unwrap()).map_err(|_| "KM-MODEL-UNSUPPORTED")?;
        let transfer = std::fs::OpenOptions::new()
            .read(true)
            .write(true)
            .create_new(true)
            .custom_flags(0x0400_0000) // FILE_FLAG_DELETE_ON_CLOSE: kernel-owned cleanup, including crashes.
            .open(file)
            .map_err(|_| "KM-MODEL-UNSUPPORTED")?;
        let request =
            serde_json::json!({ "command": "models.prepare", "requestId": token, "payload": {
            "paths": paths, "id": id, "transferId": token, "animation": animation
        } })
            .to_string();
        let response = super::project_bridge(app.clone(), bridge, trace, request).await?;
        let response: serde_json::Value =
            serde_json::from_str(&response).map_err(|_| "KM-MODEL-UNSUPPORTED")?;
        if let Some(code) = response["error"]["code"].as_str() {
            return Err(code.to_owned());
        }
        if response["requestId"].as_str() != Some(&token) || response["payload"]["ready"] != true {
            return Err("KM-MODEL-UNSUPPORTED".into());
        }
        if !state.current(&session) || state.generation.load(Ordering::Acquire) != generation {
            return Err("KM-MODEL-CANCELLED".into());
        }
        let scene = tauri::async_runtime::spawn_blocking(move || {
            use std::io::Read;
            let mut bytes = Vec::new();
            transfer
                .take(96 * 1024 * 1024 + 1)
                .read_to_end(&mut bytes)
                .map_err(|_| "KM-MODEL-UNSUPPORTED")?;
            scene::Scene::read(&bytes)
        })
        .await
        .map_err(|_| "KM-MODEL-UNSUPPORTED")??;
        let owner = app
            .get_webview_window("main")
            .ok_or("KM-MODEL-GPU-UNAVAILABLE")?
            .hwnd()
            .map_err(|_| "KM-MODEL-GPU-UNAVAILABLE")?
            .0 as isize;
        let generation_state = state.generation.clone();
        let proxy = state
            .proxy
            .get_or_init(|| window::start(app.clone(), generation_state))
            .as_ref()
            .map_err(Clone::clone)?;
        if state.generation.load(Ordering::Acquire) != generation {
            return Err("KM-MODEL-CANCELLED".into());
        }
        let (send, receive) = tokio::sync::oneshot::channel();
        let title: String = title
            .chars()
            .filter(|c| !c.is_control())
            .take(160)
            .collect();
        proxy
            .send_event(window::Event::Open {
                scene,
                title,
                owner,
                viewport: *state.viewport.lock().map_err(|_| "KM-MODEL-CANCELLED")?,
                session,
                generation,
                result: send,
            })
            .map_err(|_| "KM-MODEL-GPU-UNAVAILABLE")?;
        receive.await.map_err(|_| "KM-MODEL-GPU-UNAVAILABLE")?
    }
}
