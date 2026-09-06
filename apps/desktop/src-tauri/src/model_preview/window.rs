// SPDX-License-Identifier: GPL-3.0-only
use super::{gpu::Renderer, scene::Scene, PreviewInfo};
use std::sync::{
    atomic::{AtomicU64, Ordering},
    Arc,
};
use tauri::Emitter;
use winit::{
    application::ApplicationHandler,
    event::{ElementState, MouseButton, MouseScrollDelta, WindowEvent},
    event_loop::{ActiveEventLoop, ControlFlow, EventLoop, EventLoopProxy},
    keyboard::{KeyCode, PhysicalKey},
    platform::windows::{EventLoopBuilderExtWindows, WindowAttributesExtWindows},
    window::{Window, WindowId},
};

pub enum Event {
    Open {
        scene: Scene,
        title: String,
        owner: isize,
        session: String,
        generation: u64,
        result: tokio::sync::oneshot::Sender<Result<PreviewInfo, String>>,
    },
    Close {
        through: u64,
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
    fn user_event(&mut self, event_loop: &ActiveEventLoop, event: Event) {
        match event {
            Event::Close { through } => {
                if self.opened_generation <= through {
                    self.close(None);
                }
            }
            Event::Open {
                scene,
                title,
                owner,
                session,
                generation,
                result,
            } => {
                if self.generation.load(Ordering::Acquire) != generation {
                    let _ = result.send(Err("KM-MODEL-CANCELLED".into()));
                    return;
                }
                self.close(None);
                let loaded = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    let window = event_loop
                        .create_window(
                            Window::default_attributes()
                                .with_title(title)
                                .with_visible(false)
                                .with_inner_size(winit::dpi::LogicalSize::new(900.0, 720.0))
                                .with_min_inner_size(winit::dpi::LogicalSize::new(320.0, 240.0))
                                .with_max_inner_size(winit::dpi::PhysicalSize::new(4096, 4096))
                                .with_owner_window(owner),
                        )
                        .map_err(|_| "KM-MODEL-GPU-UNAVAILABLE".to_owned())?;
                    pollster::block_on(Renderer::new(Arc::new(window), scene))
                }))
                .unwrap_or_else(|_| Err("KM-MODEL-GPU-UNAVAILABLE".into()));
                match loaded {
                    Ok(mut renderer) => {
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
                        };
                        renderer.window.set_visible(true);
                        renderer.window.focus_window();
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
                    PhysicalKey::Code(KeyCode::Escape) => self.close(None),
                    _ => {}
                }
            }
            _ => {}
        }
    }
}
