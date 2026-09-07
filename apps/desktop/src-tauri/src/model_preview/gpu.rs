// SPDX-License-Identifier: GPL-3.0-only
use super::scene::Scene;
use glam::{Mat4, Vec3};
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc,
};
use wgpu::util::DeviceExt;
use winit::window::Window;

pub(super) struct Mesh {
    pub(super) vertices: wgpu::Buffer,
    pub(super) indices: wgpu::Buffer,
    pub(super) count: u32,
    pub(super) material: wgpu::BindGroup,
    pub(super) blend: bool,
    pub(super) colors: wgpu::Buffer,
    pub(super) values: Vec<f32>,
    pub(super) edges: Option<wgpu::Buffer>,
}
pub struct Renderer {
    // The surface owns a window reference; it must be dropped before the window.
    surface: wgpu::Surface<'static>,
    pub window: Arc<Window>,
    device: wgpu::Device,
    queue: wgpu::Queue,
    config: wgpu::SurfaceConfiguration,
    pipeline: wgpu::RenderPipeline,
    blend_pipeline: wgpu::RenderPipeline,
    wire_pipeline: wgpu::RenderPipeline,
    pub inspection: super::inspection::Inspection,
    display: u32,
    wireframe: bool,
    hidden: Vec<usize>,
    selected: Option<usize>,
    orthographic: bool,
    range_start: f32,
    range_end: f32,
    pub frame_ms: f32,
    pub statistics: bool,
    samples: u32,
    measured: std::time::Instant,
    background: super::background::Background,
    background_color: [u8; 3],
    grid: bool,
    light: [f32; 4],
    depth: wgpu::TextureView,
    camera: wgpu::Buffer,
    camera_group: wgpu::BindGroup,
    joints: wgpu::Buffer,
    pub rig: super::animation::Rig,
    pub position: f32,
    pub playing: bool,
    pub looping: bool,
    pub speed: f32,
    tick: std::time::Instant,
    meshes: Vec<Mesh>,
    assets: super::gpu_assets::Assets,
    pub adapter: String,
    failed: Arc<AtomicBool>,
    minimized: bool,
    viewport_visible: bool,
    center: Vec3,
    radius: f32,
    floor: f32,
    yaw: f32,
    pitch: f32,
    distance: f32,
    pan: Vec3,
}
impl Renderer {
    pub fn retain_view(&mut self, previous: &Self) {
        self.yaw = previous.yaw;
        self.pitch = previous.pitch;
        self.distance = previous.distance;
        self.pan = previous.pan;
        self.position = previous.position.min(self.duration());
        self.playing = previous.playing;
        self.looping = previous.looping;
        self.speed = previous.speed;
        self.light = previous.light;
    }

    pub async fn new(window: Arc<Window>, mut scene: Scene) -> Result<Self, String> {
        let instance = wgpu::Instance::new(&wgpu::InstanceDescriptor {
            backends: wgpu::Backends::DX12,
            ..Default::default()
        });
        let surface = instance
            .create_surface(window.clone())
            .map_err(|_| "KM-MODEL-GPU-UNAVAILABLE")?;
        // Auto is intentionally internal. No vendor-specific code, backend override, or CPU fallback.
        let adapter = instance
            .request_adapter(&wgpu::RequestAdapterOptions {
                compatible_surface: Some(&surface),
                ..Default::default()
            })
            .await
            .map_err(|_| "KM-MODEL-GPU-UNAVAILABLE")?;
        let info = adapter.get_info();
        if info.backend != wgpu::Backend::Dx12
            || info.device_type == wgpu::DeviceType::Cpu
            || !adapter
                .features()
                .contains(wgpu::Features::TEXTURE_COMPRESSION_BC)
        {
            return Err("KM-MODEL-GPU-UNAVAILABLE".into());
        }
        let (device, queue) = adapter
            .request_device(&wgpu::DeviceDescriptor {
                label: Some("KM Model Preview"),
                required_features: wgpu::Features::TEXTURE_COMPRESSION_BC,
                required_limits: wgpu::Limits::default(),
                memory_hints: wgpu::MemoryHints::MemoryUsage,
                trace: wgpu::Trace::Off,
            })
            .await
            .map_err(|_| "KM-MODEL-GPU-UNAVAILABLE")?;
        let failed = Arc::new(AtomicBool::new(false));
        let flag = failed.clone();
        let redraw = window.clone();
        device.on_uncaptured_error(Box::new(move |_| {
            flag.store(true, Ordering::Release);
            redraw.request_redraw();
        }));
        let flag = failed.clone();
        let redraw = window.clone();
        device.set_device_lost_callback(move |_, _| {
            flag.store(true, Ordering::Release);
            redraw.request_redraw();
        });
        let size = window.inner_size();
        let mut config = surface
            .get_default_config(&adapter, size.width.max(1), size.height.max(1))
            .ok_or("KM-MODEL-GPU-UNAVAILABLE")?;
        let capabilities = surface.get_capabilities(&adapter);
        config.format = capabilities
            .formats
            .iter()
            .copied()
            .find(|f| f.is_srgb())
            .ok_or("KM-MODEL-GPU-UNAVAILABLE")?;
        config.present_mode = wgpu::PresentMode::Fifo;
        config.desired_maximum_frame_latency = 2;
        surface.configure(&device, &config);
        let depth = Self::depth(&device, &config);
        let camera = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("Camera"),
            size: 96,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let camera_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("Camera"),
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::VERTEX_FRAGMENT,
                    ty: wgpu::BindingType::Buffer {
                        ty: wgpu::BufferBindingType::Uniform,
                        has_dynamic_offset: false,
                        min_binding_size: wgpu::BufferSize::new(96),
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 1,
                    visibility: wgpu::ShaderStages::VERTEX,
                    ty: wgpu::BindingType::Buffer {
                        ty: wgpu::BufferBindingType::Storage { read_only: true },
                        has_dynamic_offset: false,
                        min_binding_size: wgpu::BufferSize::new(512 * 64),
                    },
                    count: None,
                },
            ],
        });
        let joints = device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: Some("Skeleton transforms"),
            contents: bytemuck::cast_slice(&scene.rig.matrices(0.0)),
            usage: wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST,
        });
        let camera_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("Camera"),
            layout: &camera_layout,
            entries: &[
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: camera.as_entire_binding(),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: joints.as_entire_binding(),
                },
            ],
        });
        let mut material_entries = vec![
            wgpu::BindGroupLayoutEntry {
                binding: 1,
                visibility: wgpu::ShaderStages::FRAGMENT,
                ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
                count: None,
            },
            wgpu::BindGroupLayoutEntry {
                binding: 2,
                visibility: wgpu::ShaderStages::FRAGMENT,
                ty: wgpu::BindingType::Buffer {
                    ty: wgpu::BufferBindingType::Uniform,
                    has_dynamic_offset: false,
                    min_binding_size: None,
                },
                count: None,
            },
        ];
        for binding in [0, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17] {
            material_entries.push(wgpu::BindGroupLayoutEntry {
                binding,
                visibility: wgpu::ShaderStages::FRAGMENT,
                ty: wgpu::BindingType::Texture {
                    sample_type: wgpu::TextureSampleType::Float { filterable: true },
                    view_dimension: wgpu::TextureViewDimension::D2,
                    multisampled: false,
                },
                count: None,
            });
        }
        let material_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("Base color"),
            entries: &material_entries,
        });
        let mut assets = super::gpu_assets::Assets::new(&device, material_layout.clone());
        let meshes = assets.meshes(&device, &queue, &scene);
        let shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("Static model"),
            source: wgpu::ShaderSource::Wgsl(include_str!("preview.wgsl").into()),
        });
        let layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("Static model"),
            bind_group_layouts: &[&camera_layout, &material_layout],
            push_constant_ranges: &[],
        });
        let create_pipeline = |blend: bool, wire: bool| {
            device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("Static model"), layout: Some(&layout),
            vertex: wgpu::VertexState { module: &shader, entry_point: Some(if wire { "vs_wire" } else { "vs_main" }), compilation_options: Default::default(),
                buffers: &[wgpu::VertexBufferLayout { array_stride: 64, step_mode: wgpu::VertexStepMode::Vertex,
                    attributes: &wgpu::vertex_attr_array![0 => Float32x3, 1 => Float32x3, 2 => Float32x2, 3 => Float32x4, 4 => Float32x4] }] },
            fragment: Some(wgpu::FragmentState { module: &shader, entry_point: Some(if wire { "fs_wire" } else { "fs_main" }), compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState { format: config.format, blend: if blend { Some(wgpu::BlendState::ALPHA_BLENDING) } else { None }, write_mask: wgpu::ColorWrites::ALL })] }),
            primitive: wgpu::PrimitiveState { topology: if wire { wgpu::PrimitiveTopology::LineList } else { wgpu::PrimitiveTopology::TriangleList }, cull_mode: None, ..Default::default() },
            depth_stencil: Some(wgpu::DepthStencilState { format: wgpu::TextureFormat::Depth32Float, depth_write_enabled: !blend && !wire,
                depth_compare: wgpu::CompareFunction::LessEqual, stencil: Default::default(), bias: wgpu::DepthBiasState { constant: if wire { -2 } else { 0 }, ..Default::default() } }),
            multisample: Default::default(), multiview: None, cache: None
        })
        };
        let pipeline = create_pipeline(false, false);
        let blend_pipeline = create_pipeline(true, false);
        let wire_pipeline = create_pipeline(false, true);
        let inspection = super::inspection::Inspection::take(&mut scene);
        let background = super::background::Background::new(&device, config.format);
        let mut result = Self {
            surface,
            window,
            device,
            queue,
            config,
            pipeline,
            blend_pipeline,
            wire_pipeline,
            inspection,
            display: 0,
            wireframe: false,
            hidden: vec![],
            selected: None,
            orthographic: false,
            range_start: 0.0,
            range_end: f32::MAX,
            frame_ms: 0.0,
            statistics: false,
            samples: 0,
            measured: std::time::Instant::now(),
            background,
            background_color: super::default_background(),
            grid: false,
            light: super::default_light(),
            depth,
            camera,
            camera_group,
            joints,
            looping: scene.rig.clip.as_ref().is_some_and(|c| c.r#loop),
            playing: false,
            position: 0.0,
            speed: 1.0,
            tick: std::time::Instant::now(),
            rig: scene.rig,
            meshes,
            assets,
            adapter: info.name,
            failed,
            minimized: false,
            viewport_visible: true,
            center: scene.center,
            radius: scene.radius,
            floor: scene.floor,
            yaw: 0.0,
            pitch: 0.0,
            distance: 0.0,
            pan: Vec3::ZERO,
        };
        result.reset();
        Ok(result)
    }
    pub fn replace(&mut self, mut scene: Scene) {
        let clip_changed =
            self.rig.clip.as_ref().map(|c| &c.id) != scene.rig.clip.as_ref().map(|c| &c.id);
        let meshes = self.assets.meshes(&self.device, &self.queue, &scene);
        self.meshes = meshes;
        self.inspection = super::inspection::Inspection::take(&mut scene);
        self.rig = scene.rig;
        self.center = scene.center;
        self.radius = scene.radius;
        self.floor = scene.floor;
        if clip_changed {
            self.range_start = 0.0;
            self.range_end = f32::MAX;
            self.position = 0.0;
            self.playing = false;
            self.looping = self.rig.clip.as_ref().is_some_and(|c| c.r#loop);
        }
        self.position = self.position.min(self.duration());
        self.tick = std::time::Instant::now();
    }
    fn depth(device: &wgpu::Device, config: &wgpu::SurfaceConfiguration) -> wgpu::TextureView {
        device
            .create_texture(&wgpu::TextureDescriptor {
                label: Some("Preview depth"),
                size: wgpu::Extent3d {
                    width: config.width,
                    height: config.height,
                    depth_or_array_layers: 1,
                },
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format: wgpu::TextureFormat::Depth32Float,
                usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
                view_formats: &[],
            })
            .create_view(&Default::default())
    }
    pub fn resize(&mut self, width: u32, height: u32) {
        self.minimized = !self.viewport_visible || width == 0 || height == 0;
        if self.minimized {
            return;
        }
        if self.config.width == width.min(4096) && self.config.height == height.min(4096) {
            return;
        }
        self.config.width = width.min(4096);
        self.config.height = height.min(4096);
        self.surface.configure(&self.device, &self.config);
        self.depth = Self::depth(&self.device, &self.config);
        self.window.request_redraw();
    }
    pub fn reset(&mut self) {
        self.yaw = 0.35;
        self.pitch = 0.15;
        self.distance = self.radius * 3.2;
        self.pan = Vec3::ZERO;
        self.window.request_redraw();
    }
    pub fn frame(&mut self) {
        self.pan = Vec3::ZERO;
        let aspect = self.config.width as f32 / self.config.height.max(1) as f32;
        self.distance = self.radius * 1.1 / (22.5_f32.to_radians().sin() * aspect.min(1.0));
        self.window.request_redraw();
    }
    pub fn viewport(&mut self, viewport: super::Viewport) {
        let visible = viewport.visible && viewport.width > 0 && viewport.height > 0;
        self.viewport_visible = visible;
        self.background_color = viewport.background;
        self.grid = viewport.grid;
        self.light = viewport.light;
        self.display = viewport.display;
        self.wireframe = viewport.wireframe;
        self.hidden = viewport.hidden;
        self.selected = viewport.selected;
        self.statistics = viewport.statistics;
        let was_hidden = self.minimized;
        if !visible {
            self.window.set_visible(false);
        }
        if visible {
            use winit::raw_window_handle::HasWindowHandle;
            if let Ok(handle) = self.window.window_handle() {
                if let winit::raw_window_handle::RawWindowHandle::Win32(handle) = handle.as_raw() {
                    // Keep the viewport above the sibling webview without activating it.
                    unsafe {
                        if let Some(clip) = viewport.clip {
                            let region = CreateRectRgn(
                                clip.x as i32,
                                clip.y as i32,
                                (clip.x + clip.width) as i32,
                                (clip.y + clip.height) as i32,
                            );
                            if region != 0 && SetWindowRgn(handle.hwnd.get(), region, 0) == 0 {
                                DeleteObject(region);
                            }
                        } else {
                            SetWindowRgn(handle.hwnd.get(), 0, 0);
                        }
                        SetWindowPos(
                            handle.hwnd.get(),
                            0,
                            viewport.x,
                            viewport.y,
                            viewport.width.min(4096) as i32,
                            viewport.height.min(4096) as i32,
                            // NOACTIVATE | NOCOPYBITS: never copy stale pixels while moving.
                            0x0010 | 0x0100,
                        );
                    }
                }
            }
            self.resize(viewport.width, viewport.height);
            self.window.set_visible(true);
            self.window.request_redraw();
        } else {
            self.minimized = true;
        }
        if was_hidden != self.minimized {
            self.tick = std::time::Instant::now();
        }
    }
    pub fn duration(&self) -> f32 {
        self.rig
            .clip
            .as_ref()
            .map(|c| c.frames.saturating_sub(1) as f32 / c.rate as f32)
            .unwrap_or(0.0)
    }
    pub fn animate(&mut self) -> bool {
        let now = std::time::Instant::now();
        let elapsed = now.duration_since(self.tick).as_secs_f32();
        self.tick = now;
        let visible = !self.minimized && self.parent_visible();
        if self.playing && visible {
            self.position += elapsed * self.speed;
            let duration = self.range_end.min(self.duration());
            if self.position >= duration {
                if self.looping && duration > self.range_start {
                    self.position = self.range_start
                        + (self.position - self.range_start) % (duration - self.range_start);
                } else {
                    self.position = duration;
                    self.playing = false;
                }
            }
            self.window.request_redraw();
        }
        self.playing && visible
    }
    fn parent_visible(&self) -> bool {
        use winit::raw_window_handle::HasWindowHandle;
        let Ok(handle) = self.window.window_handle() else {
            return false;
        };
        let winit::raw_window_handle::RawWindowHandle::Win32(handle) = handle.as_raw() else {
            return false;
        };
        unsafe {
            let parent = GetAncestor(handle.hwnd.get(), 2);
            IsIconic(parent) == 0 && IsWindowVisible(parent) != 0
        }
    }
    pub fn playback(&mut self, action: &str, value: f32) {
        match action {
            "play" => {
                if self.position >= self.range_end.min(self.duration())
                    || self.position < self.range_start
                {
                    self.position = self.range_start;
                }
                self.playing = self.rig.clip.is_some();
            }
            "pause" => self.playing = false,
            "restart" => {
                self.position = self.range_start;
                self.playing = self.rig.clip.is_some();
            }
            "seek" => self.position = value.clamp(0.0, self.duration()),
            "speed" => self.speed = value.clamp(0.1, 4.0),
            "loop" => self.looping = value != 0.0,
            "previousFrame" | "nextFrame" => {
                let rate = self.rig.clip.as_ref().map(|c| c.rate).unwrap_or(30) as f32;
                self.position = ((self.position * rate).round()
                    + if action == "nextFrame" { 1.0 } else { -1.0 })
                .clamp(0.0, self.duration() * rate)
                    / rate;
                self.playing = false;
            }
            "rangeStart" => {
                self.range_start = value.clamp(0.0, self.range_end.min(self.duration()))
            }
            "rangeEnd" => self.range_end = value.clamp(self.range_start, self.duration()),
            _ => {}
        }
        self.tick = std::time::Instant::now();
        self.window.request_redraw();
    }
    pub fn orbit(&mut self, dx: f32, dy: f32) {
        self.yaw -= dx * 0.008;
        self.pitch = (self.pitch + dy * 0.008).clamp(-1.45, 1.45);
        self.window.request_redraw();
    }
    pub fn pan(&mut self, dx: f32, dy: f32) {
        let right = Vec3::new(self.yaw.cos(), 0.0, -self.yaw.sin());
        let direction = Vec3::new(
            self.yaw.sin() * self.pitch.cos(),
            self.pitch.sin(),
            self.yaw.cos() * self.pitch.cos(),
        );
        let up = direction.cross(right);
        self.pan += (-right * dx + up * dy) * self.distance * 0.001;
        self.pan = self.pan.clamp(
            Vec3::splat(-self.radius * 10.0),
            Vec3::splat(self.radius * 10.0),
        );
        self.window.request_redraw();
    }
    pub fn zoom(&mut self, delta: f32) {
        self.distance =
            (self.distance * (-delta * 0.12).exp()).clamp(self.radius * 0.2, self.radius * 30.0);
        self.window.request_redraw();
    }
    pub fn render(&mut self) -> Result<(), String> {
        let started = std::time::Instant::now();
        if self.failed.load(Ordering::Acquire) {
            return Err("KM-MODEL-GPU-UNAVAILABLE".into());
        }
        if self.minimized || !self.parent_visible() {
            return Ok(());
        }
        let frame_position =
            self.position * self.rig.clip.as_ref().map(|c| c.rate).unwrap_or(1) as f32;
        for (i, mesh) in self.meshes.iter_mut().enumerate() {
            if (self.wireframe || self.selected == Some(i))
                && mesh.edges.is_none()
                && !self.hidden.contains(&i)
            {
                mesh.edges = Some(
                    self.assets
                        .edges(&self.device, &self.inspection.geometry[i].indices),
                );
            }
        }
        self.queue.write_buffer(
            &self.joints,
            0,
            bytemuck::cast_slice(&self.rig.matrices(frame_position)),
        );
        for (i, mesh) in self.meshes.iter().enumerate() {
            self.queue.write_buffer(
                &mesh.colors,
                0,
                bytemuck::cast_slice(&self.rig.material(i, frame_position, &mesh.values)),
            );
        }
        let frame = match self.surface.get_current_texture() {
            Ok(frame) => frame,
            Err(wgpu::SurfaceError::Lost | wgpu::SurfaceError::Outdated) => {
                self.surface.configure(&self.device, &self.config);
                self.window.request_redraw();
                return Ok(());
            }
            Err(wgpu::SurfaceError::Timeout) => {
                return Ok(());
            }
            Err(_) => return Err("KM-MODEL-GPU-UNAVAILABLE".into()),
        };
        let (view_projection, eye) = self.view_projection();
        let mut camera_values = view_projection.to_cols_array().to_vec();
        camera_values.extend_from_slice(&eye.extend(self.display as f32).to_array());
        let azimuth = self.light[0].to_radians();
        let elevation = self.light[1].to_radians();
        let light_direction = Vec3::new(
            azimuth.sin() * elevation.cos(),
            elevation.sin(),
            azimuth.cos() * elevation.cos(),
        );
        let light_position = self.center + light_direction * self.radius * self.light[2];
        camera_values.extend_from_slice(
            &light_position
                .extend(self.light[3] * self.radius * self.radius * 16.0)
                .to_array(),
        );
        self.queue
            .write_buffer(&self.camera, 0, bytemuck::cast_slice(&camera_values));
        self.background.update(
            &self.queue,
            view_projection,
            Vec3::new(self.center.x, self.floor + self.radius, self.center.z),
            self.radius,
            self.background_color,
            self.grid,
        );
        let color = frame.texture.create_view(&Default::default());
        let mut encoder = self.device.create_command_encoder(&Default::default());
        {
            let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("Static model preview"),
                color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                    view: &color,
                    resolve_target: None,
                    depth_slice: None,
                    ops: wgpu::Operations {
                        load: wgpu::LoadOp::Clear(wgpu::Color {
                            r: 0.035,
                            g: 0.045,
                            b: 0.06,
                            a: 1.0,
                        }),
                        store: wgpu::StoreOp::Store,
                    },
                })],
                depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
                    view: &self.depth,
                    depth_ops: Some(wgpu::Operations {
                        load: wgpu::LoadOp::Clear(1.0),
                        store: wgpu::StoreOp::Discard,
                    }),
                    stencil_ops: None,
                }),
                timestamp_writes: None,
                occlusion_query_set: None,
            });
            self.background.draw(&mut pass);
            pass.set_bind_group(0, &self.camera_group, &[]);
            for blend in [false, true] {
                pass.set_pipeline(if blend {
                    &self.blend_pipeline
                } else {
                    &self.pipeline
                });
                for (i, mesh) in self
                    .meshes
                    .iter()
                    .enumerate()
                    .filter(|(_, mesh)| mesh.blend == blend)
                {
                    if self.hidden.contains(&i) || !self.rig.visible(i, frame_position) {
                        continue;
                    }
                    pass.set_bind_group(1, &mesh.material, &[]);
                    pass.set_vertex_buffer(0, mesh.vertices.slice(..));
                    pass.set_index_buffer(mesh.indices.slice(..), wgpu::IndexFormat::Uint32);
                    pass.draw_indexed(0..mesh.count, 0, 0..1);
                }
            }
            pass.set_pipeline(&self.wire_pipeline);
            for (i, mesh) in self.meshes.iter().enumerate() {
                if (self.wireframe || self.selected == Some(i))
                    && !self.hidden.contains(&i)
                    && self.rig.visible(i, frame_position)
                {
                    pass.set_bind_group(1, &mesh.material, &[]);
                    pass.set_vertex_buffer(0, mesh.vertices.slice(..));
                    let Some(edges) = &mesh.edges else {
                        continue;
                    };
                    pass.set_index_buffer(edges.slice(..), wgpu::IndexFormat::Uint32);
                    pass.draw_indexed(0..mesh.count * 2, 0, 0..1);
                }
            }
        }
        self.queue.submit(Some(encoder.finish()));
        frame.present();
        self.samples += 1;
        self.frame_ms = started.elapsed().as_secs_f32() * 1000.0;
        Ok(())
    }
    fn view_projection(&self) -> (Mat4, Vec3) {
        let target = self.center + self.pan;
        let direction = Vec3::new(
            self.yaw.sin() * self.pitch.cos(),
            self.pitch.sin(),
            self.yaw.cos() * self.pitch.cos(),
        );
        let eye = target + direction * self.distance;
        let up = if self.pitch.cos().abs() < 0.001 {
            Vec3::new(0.0, 0.0, -self.pitch.signum())
        } else {
            Vec3::Y
        };
        let view = Mat4::look_at_rh(eye, target, up);
        let aspect = self.config.width as f32 / self.config.height.max(1) as f32;
        let size = self.distance * 22.5_f32.to_radians().tan();
        let projection = if self.orthographic {
            Mat4::orthographic_rh(
                -size * aspect,
                size * aspect,
                -size,
                size,
                self.radius * 0.005,
                self.radius * 100.0,
            )
        } else {
            Mat4::perspective_rh(
                45_f32.to_radians(),
                aspect,
                self.radius * 0.005,
                self.radius * 100.0,
            )
        };
        (projection * view, eye)
    }
    pub fn stats(&mut self) -> serde_json::Value {
        let elapsed = self.measured.elapsed().as_secs_f32().max(0.001);
        let fps = self.samples as f32 / elapsed;
        self.samples = 0;
        self.measured = std::time::Instant::now();
        serde_json::json!({"fps": fps, "yaw": self.yaw, "pitch": self.pitch})
    }
    pub fn viewpoint(&mut self, action: &str) {
        use std::f32::consts::{FRAC_PI_2, PI};
        match action {
            "front" => {
                self.yaw = 0.0;
                self.pitch = 0.0;
            }
            "back" => {
                self.yaw = PI;
                self.pitch = 0.0;
            }
            "sideLeft" => {
                self.yaw = -FRAC_PI_2;
                self.pitch = 0.0;
            }
            "sideRight" => {
                self.yaw = FRAC_PI_2;
                self.pitch = 0.0;
            }
            "top" => {
                self.pitch = FRAC_PI_2;
                self.yaw = 0.0;
            }
            "bottom" => {
                self.pitch = -FRAC_PI_2;
                self.yaw = 0.0;
            }
            "orthographic" => self.orthographic = true,
            "perspective" => self.orthographic = false,
            _ => return,
        }
        self.window.request_redraw();
    }
    pub fn pick(&mut self, x: f32, y: f32) -> Option<usize> {
        let inverse = self.view_projection().0.inverse();
        let screen = glam::Vec2::new(
            x / self.config.width as f32 * 2.0 - 1.0,
            1.0 - y / self.config.height as f32 * 2.0,
        );
        let near = inverse.project_point3(screen.extend(0.0));
        let far = inverse.project_point3(screen.extend(1.0));
        let frame = self.position * self.rig.clip.as_ref().map(|c| c.rate).unwrap_or(1) as f32;
        self.selected = self.inspection.pick(
            &self.rig,
            frame,
            near,
            (far - near).normalize(),
            &self.hidden,
        );
        self.window.request_redraw();
        self.selected
    }
}
#[link(name = "user32")]
unsafe extern "system" {
    fn SetWindowRgn(window: isize, region: isize, redraw: i32) -> i32;
    fn GetAncestor(window: isize, flags: u32) -> isize;
    fn IsIconic(window: isize) -> i32;
    fn IsWindowVisible(window: isize) -> i32;
    fn SetWindowPos(
        window: isize,
        after: isize,
        x: i32,
        y: i32,
        width: i32,
        height: i32,
        flags: u32,
    ) -> i32;
}
#[link(name = "gdi32")]
unsafe extern "system" {
    fn CreateRectRgn(left: i32, top: i32, right: i32, bottom: i32) -> isize;
    fn DeleteObject(object: isize) -> i32;
}
