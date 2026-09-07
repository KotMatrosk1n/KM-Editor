// SPDX-License-Identifier: GPL-3.0-only
use super::{gpu::Renderer, scene::Scene, PreviewInfo, Viewport};
use std::sync::{
    atomic::{AtomicU64, Ordering},
    Arc,
};
use tauri::{Emitter, Manager};
use winit::{
    application::ApplicationHandler,
    event::{ElementState, MouseButton, MouseScrollDelta, WindowEvent},
    event_loop::{ActiveEventLoop, ControlFlow, EventLoop, EventLoopProxy},
    keyboard::{KeyCode, PhysicalKey},
    platform::windows::EventLoopBuilderExtWindows,
    window::{Window, WindowId},
};

pub enum Event {
    Open {
        scene: Scene,
        title: String,
        owner: isize,
        viewport: Viewport,
        session: String,
        generation: u64,
        result: tokio::sync::oneshot::Sender<Result<PreviewInfo, String>>,
    },
    Close {
        through: u64,
    },
    Viewport {
        session: String,
        viewport: Viewport,
    },
    Camera {
        session: String,
        action: String,
    },
    Playback {
        session: String,
        action: String,
        value: f32,
    },
}
pub fn start(
    app: tauri::AppHandle,
    generation: Arc<AtomicU64>,
) -> Result<EventLoopProxy<Event>, String> {
    let (send, receive) = std::sync::mpsc::sync_channel(1);
    std::thread::Builder::new()
        .name("km-model-preview".into())
        .spawn(move || {
            let event_loop = match EventLoop::<Event>::with_user_event()
                .with_any_thread(true)
                .build()
            {
                Ok(event_loop) => event_loop,
                Err(_) => {
                    let _ = send.send(Err("KM-MODEL-GPU-UNAVAILABLE".into()));
                    return;
                }
            };
            event_loop.set_control_flow(ControlFlow::Wait);
            let _ = send.send(Ok(event_loop.create_proxy()));
            let mut host = Host {
                app,
                generation,
                opened_generation: 0,
                renderer: None,
                session: None,
                left: false,
                right: false,
                cursor: None,
                status: std::time::Instant::now(),
            };
            let _ = event_loop.run_app(&mut host);
        })
        .map_err(|_| "KM-MODEL-GPU-UNAVAILABLE")?;
    receive
        .recv_timeout(std::time::Duration::from_secs(10))
        .map_err(|_| "KM-MODEL-GPU-UNAVAILABLE")?
}
struct Host {
    app: tauri::AppHandle,
    generation: Arc<AtomicU64>,
    renderer: Option<Renderer>,
    session: Option<String>,
    opened_generation: u64,
    left: bool,
    right: bool,
    cursor: Option<(f64, f64)>,
    status: std::time::Instant,
}
impl Host {
    fn close(&mut self, error: Option<String>) {
        self.renderer = None;
        self.left = false;
        self.right = false;
        self.cursor = None;
        if let Some(session) = self.session.take() {
            let _ = self.app.emit(
                "model-preview-status",
                serde_json::json!({ "session": session, "error": error, "closed": true }),
            );
        }
    }
}
impl ApplicationHandler<Event> for Host {
    fn resumed(&mut self, _: &ActiveEventLoop) {}
    fn about_to_wait(&mut self, event_loop: &ActiveEventLoop) {
        let active = self
            .renderer
            .as_mut()
            .is_some_and(|renderer| renderer.animate());
        event_loop.set_control_flow(if active {
            ControlFlow::WaitUntil(std::time::Instant::now() + std::time::Duration::from_millis(16))
        } else if self
            .renderer
            .as_ref()
            .is_some_and(|renderer| renderer.playing)
        {
            ControlFlow::WaitUntil(
                std::time::Instant::now() + std::time::Duration::from_millis(250),
            )
        } else {
            ControlFlow::Wait
        });
        if self.status.elapsed().as_millis() >= 100 {
            if let (Some(renderer), Some(session)) = (&self.renderer, &self.session) {
                let _ = self.app.emit("model-preview-playback", serde_json::json!({ "session": session, "position": renderer.position, "playing": renderer.playing }));
            }
            self.status = std::time::Instant::now();
        }
    }
    fn user_event(&mut self, event_loop: &ActiveEventLoop, event: Event) {
        match event {
            Event::Playback {
                session,
                action,
                value,
            } => {
                if self.session.as_deref() == Some(&session) {
                    if let Some(renderer) = &mut self.renderer {
                        renderer.playback(&action, value);
                    }
                }
            }
            Event::Viewport { session, viewport } => {
                if self.session.as_deref() == Some(&session) {
                    if let Some(renderer) = &mut self.renderer {
                        renderer.viewport(viewport);
                    }
                }
            }
            Event::Camera { session, action } => {
                if self.session.as_deref() == Some(&session) {
                    if let Some(renderer) = &mut self.renderer {
                        match action.as_str() {
                            "reset" => renderer.reset(),
                            "frame" => renderer.frame(),
                            "left" => renderer.orbit(-20.0, 0.0),
                            "right" => renderer.orbit(20.0, 0.0),
                            "up" => renderer.orbit(0.0, -20.0),
                            "down" => renderer.orbit(0.0, 20.0),
                            "in" => renderer.zoom(1.0),
                            "out" => renderer.zoom(-1.0),
                            "focus" => renderer.window.focus_window(),
                            _ => {}
                        }
                    }
                }
            }
            Event::Close { through } => {
                if self.opened_generation <= through {
                    self.close(None);
                }
            }
            Event::Open {
                scene,
                title,
                owner,
                viewport,
                session,
                generation,
                result,
            } => {
                if self.generation.load(Ordering::Acquire) != generation {
                    let _ = result.send(Err("KM-MODEL-CANCELLED".into()));
                    return;
                }
                let replacing = self.session.as_deref() == Some(&session);
                if !replacing {
                    self.close(None);
                }
                let loaded = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    if replacing {
                        if let Some(mut renderer) = self.renderer.take() {
                            renderer.replace(scene);
                            return Ok(renderer);
                        }
                    }
                    let parent = winit::raw_window_handle::RawWindowHandle::Win32(
                        winit::raw_window_handle::Win32WindowHandle::new(
                            std::num::NonZeroIsize::new(owner).ok_or("KM-MODEL-GPU-UNAVAILABLE")?,
                        ),
                    );
                    // The application owns the parent HWND for the lifetime of this child.
                    let attributes =
                        unsafe { Window::default_attributes().with_parent_window(Some(parent)) };
                    let window = event_loop
                        .create_window(
                            attributes
                                .with_title(title)
                                .with_visible(false)
                                .with_decorations(false)
                                .with_position(winit::dpi::PhysicalPosition::new(
                                    viewport.x, viewport.y,
                                ))
                                .with_inner_size(winit::dpi::PhysicalSize::new(
                                    viewport.width.max(1),
                                    viewport.height.max(1),
                                )),
                        )
                        .map_err(|_| "KM-MODEL-GPU-UNAVAILABLE".to_owned())?;
                    pollster::block_on(Renderer::new(Arc::new(window), scene))
                }))
                .unwrap_or_else(|_| Err("KM-MODEL-GPU-UNAVAILABLE".into()));
                match loaded {
                    Ok(mut renderer) => {
                        if replacing {
                            if let Some(previous) = &self.renderer {
                                renderer.retain_view(previous);
                            }
                        }
                        if self.generation.load(Ordering::Acquire) != generation {
                            let _ = result.send(Err("KM-MODEL-CANCELLED".into()));
                            return;
                        }
                        if let Err(error) = renderer.render() {
                            let _ = result.send(Err(error));
                            return;
                        }
                        let info = PreviewInfo {
                            adapter: renderer.adapter.clone(),
                            backend: "DX12",
                            selection: "Auto",
                            clips: renderer.rig.clips.iter().map(|c| c.id.clone()).collect(),
                            clip: renderer.rig.clip.as_ref().map(|c| c.id.clone()),
                            duration: renderer.duration(),
                            looped: renderer.looping,
                            warnings: renderer.rig.warnings.clone(),
                        };
                        renderer.viewport(viewport);
                        renderer.window.request_redraw();
                        self.renderer = Some(renderer);
                        self.session = Some(session);
                        self.opened_generation = generation;
                        let _ = result.send(Ok(info));
                    }
                    Err(error) => {
                        let _ = result.send(Err(error));
                    }
                }
            }
        }
    }
    fn window_event(&mut self, _: &ActiveEventLoop, id: WindowId, event: WindowEvent) {
        let Some(renderer) = self.renderer.as_mut().filter(|r| r.window.id() == id) else {
            return;
        };
        match event {
            WindowEvent::CloseRequested => self.close(None),
            WindowEvent::Resized(size) => renderer.resize(size.width, size.height),
            WindowEvent::RedrawRequested => {
                if let Err(error) = renderer.render() {
                    self.close(Some(error));
                }
            }
            WindowEvent::Focused(false) | WindowEvent::CursorLeft { .. } => {
                self.left = false;
                self.right = false;
                self.cursor = None;
            }
            WindowEvent::MouseInput { state, button, .. } => match button {
                MouseButton::Left => self.left = state == ElementState::Pressed,
                MouseButton::Right => self.right = state == ElementState::Pressed,
                _ => {}
            },
            WindowEvent::CursorMoved { position, .. } => {
                if let Some((x, y)) = self.cursor {
                    let dx = (position.x - x) as f32;
                    let dy = (position.y - y) as f32;
                    if self.left {
                        renderer.orbit(dx, dy);
                    } else if self.right {
                        renderer.pan(dx, dy);
                    }
                }
                self.cursor = Some((position.x, position.y));
            }
            WindowEvent::MouseWheel { delta, .. } => renderer.zoom(match delta {
                MouseScrollDelta::LineDelta(_, y) => y,
                MouseScrollDelta::PixelDelta(p) => p.y as f32 / 100.0,
            }),
            WindowEvent::KeyboardInput { event, .. } if event.state == ElementState::Pressed => {
                match event.physical_key {
                    PhysicalKey::Code(KeyCode::KeyR) => renderer.reset(),
                    PhysicalKey::Code(KeyCode::KeyF) => renderer.frame(),
                    PhysicalKey::Code(KeyCode::ArrowLeft) => renderer.orbit(-20.0, 0.0),
                    PhysicalKey::Code(KeyCode::ArrowRight) => renderer.orbit(20.0, 0.0),
                    PhysicalKey::Code(KeyCode::ArrowUp) => renderer.orbit(0.0, -20.0),
                    PhysicalKey::Code(KeyCode::ArrowDown) => renderer.orbit(0.0, 20.0),
                    PhysicalKey::Code(KeyCode::Escape) | PhysicalKey::Code(KeyCode::Tab) => {
                        if let Some(parent) = self.app.get_webview_window("main") {
                            let _ = parent.set_focus();
                        }
                        if let Some(session) = &self.session {
                            let _ = self.app.emit(
                                "model-preview-return-focus",
                                serde_json::json!({ "session": session }),
                            );
                        }
                    }
                    _ => {}
                }
            }
            _ => {}
        }
    }
}
