// SPDX-License-Identifier: GPL-3.0-only
use super::scene::Scene;
use glam::{Mat4, Vec3};
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc,
};
use wgpu::util::DeviceExt;
use winit::window::Window;

struct Mesh {
    vertices: wgpu::Buffer,
    indices: wgpu::Buffer,
    count: u32,
    material: wgpu::BindGroup,
    blend: bool,
    colors: wgpu::Buffer,
    values: Vec<f32>,
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
    background: super::background::Background,
    background_color: [u8; 3],
    grid: bool,
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
    }

    pub async fn new(window: Arc<Window>, scene: Scene) -> Result<Self, String> {
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
            size: 64,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let camera_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("Camera"),
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::VERTEX,
                    ty: wgpu::BindingType::Buffer {
                        ty: wgpu::BufferBindingType::Uniform,
                        has_dynamic_offset: false,
                        min_binding_size: wgpu::BufferSize::new(64),
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
        let material_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("Base color"),
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
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
                        min_binding_size: wgpu::BufferSize::new(256),
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 3,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 4,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 5,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: true },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
            ],
        });
        let sampler = device.create_sampler(&wgpu::SamplerDescriptor {
            label: Some("Base color"),
            address_mode_u: wgpu::AddressMode::ClampToEdge,
            address_mode_v: wgpu::AddressMode::ClampToEdge,
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            ..Default::default()
        });
        let mut textures = Vec::new();
        for texture in &scene.textures {
            let (block_width, block_height) = texture.format.block_dimensions();
            textures.push(Self::texture(
                &device,
                &queue,
                texture.width.div_ceil(block_width) * block_width,
                texture.height.div_ceil(block_height) * block_height,
                texture.format,
                &texture.bytes,
            ));
        }
        let white = Self::texture(
            &device,
            &queue,
            1,
            1,
            wgpu::TextureFormat::Rgba8UnormSrgb,
            &[255; 4],
        );
        let black = Self::texture(
            &device,
            &queue,
            1,
            1,
            wgpu::TextureFormat::Rgba8Unorm,
            &[0; 4],
        );
        let mut meshes = Vec::new();
        for (index, primitive) in scene.primitives.into_iter().enumerate() {
            let mut values = primitive.material.to_vec();
            let ratio = |index: Option<usize>| {
                index
                    .map(|i| {
                        let t = &scene.textures[i];
                        let (width, height) = t.format.block_dimensions();
                        [
                            t.width as f32 / (t.width.div_ceil(width) * width) as f32,
                            t.height as f32 / (t.height.div_ceil(height) * height) as f32,
                        ]
                    })
                    .unwrap_or([1.0, 1.0])
            };
            values.extend_from_slice(&ratio(primitive.texture));
            values.extend_from_slice(&ratio(primitive.mask));
            let appearance = &scene.rig.meshes[index];
            values.extend_from_slice(
                &appearance
                    .mask_uv
                    .unwrap_or(primitive.material[20..24].try_into().unwrap()),
            );
            values.extend_from_slice(&appearance.highlight_color);
            values.extend_from_slice(&appearance.highlight_uv);
            values.extend_from_slice(&ratio(appearance.highlight));
            values.extend_from_slice(&[if appearance.top_origin_uv { 1.0 } else { 0.0 }, 0.0]);
            values.extend_from_slice(&appearance.underlay_uv);
            values.extend_from_slice(&appearance.underlay_wrap);
            values.extend_from_slice(&ratio(appearance.underlay));
            values.extend_from_slice(&[0.0, 0.0]);
            values.extend_from_slice(&appearance.mask_channels);
            let color = device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
                label: Some("Material color"),
                contents: bytemuck::cast_slice(&values),
                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            });
            let material = device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("Material"),
                layout: &material_layout,
                entries: &[
                    wgpu::BindGroupEntry {
                        binding: 0,
                        resource: wgpu::BindingResource::TextureView(
                            primitive.texture.map(|i| &textures[i]).unwrap_or(&white),
                        ),
                    },
                    wgpu::BindGroupEntry {
                        binding: 1,
                        resource: wgpu::BindingResource::Sampler(&sampler),
                    },
                    wgpu::BindGroupEntry {
                        binding: 2,
                        resource: color.as_entire_binding(),
                    },
                    wgpu::BindGroupEntry {
                        binding: 3,
                        resource: wgpu::BindingResource::TextureView(
                            primitive.mask.map(|i| &textures[i]).unwrap_or(&black),
                        ),
                    },
                    wgpu::BindGroupEntry {
                        binding: 4,
                        resource: wgpu::BindingResource::TextureView(
                            appearance.highlight.map(|i| &textures[i]).unwrap_or(&black),
                        ),
                    },
                    wgpu::BindGroupEntry {
                        binding: 5,
                        resource: wgpu::BindingResource::TextureView(
                            appearance.underlay.map(|i| &textures[i]).unwrap_or(&black),
                        ),
                    },
                ],
            });
            meshes.push(Mesh {
                vertices: device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
                    label: Some("Model vertices"),
                    contents: bytemuck::cast_slice(&primitive.vertices),
                    usage: wgpu::BufferUsages::VERTEX,
                }),
                indices: device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
                    label: Some("Model triangles"),
                    contents: bytemuck::cast_slice(&primitive.indices),
                    usage: wgpu::BufferUsages::INDEX,
                }),
                count: primitive.indices.len() as u32,
                material,
                blend: primitive.blend,
                colors: color,
                values,
            });
        }
        let shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("Static model"),
            source: wgpu::ShaderSource::Wgsl(include_str!("preview.wgsl").into()),
        });
        let layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("Static model"),
            bind_group_layouts: &[&camera_layout, &material_layout],
            push_constant_ranges: &[],
        });
        let create_pipeline = |blend: bool| {
            device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("Static model"), layout: Some(&layout),
            vertex: wgpu::VertexState { module: &shader, entry_point: Some("vs_main"), compilation_options: Default::default(),
                buffers: &[wgpu::VertexBufferLayout { array_stride: 64, step_mode: wgpu::VertexStepMode::Vertex,
                    attributes: &wgpu::vertex_attr_array![0 => Float32x3, 1 => Float32x3, 2 => Float32x2, 3 => Float32x4, 4 => Float32x4] }] },
            fragment: Some(wgpu::FragmentState { module: &shader, entry_point: Some("fs_main"), compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState { format: config.format, blend: if blend { Some(wgpu::BlendState::ALPHA_BLENDING) } else { None }, write_mask: wgpu::ColorWrites::ALL })] }),
            primitive: wgpu::PrimitiveState { cull_mode: None, ..Default::default() },
            depth_stencil: Some(wgpu::DepthStencilState { format: wgpu::TextureFormat::Depth32Float, depth_write_enabled: !blend,
                depth_compare: wgpu::CompareFunction::LessEqual, stencil: Default::default(), bias: Default::default() }),
            multisample: Default::default(), multiview: None, cache: None
        })
        };
        let pipeline = create_pipeline(false);
        let blend_pipeline = create_pipeline(true);
        let background = super::background::Background::new(&device, config.format);
        let mut result = Self {
            surface,
            window,
            device,
            queue,
            config,
            pipeline,
            blend_pipeline,
            background,
            background_color: super::default_background(),
            grid: false,
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
    fn texture(
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        width: u32,
        height: u32,
        format: wgpu::TextureFormat,
        bytes: &[u8],
    ) -> wgpu::TextureView {
        let size = wgpu::Extent3d {
            width,
            height,
            depth_or_array_layers: 1,
        };
        let texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("Preview texture"),
            size,
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
            view_formats: &[],
        });
        let (bw, bh) = format.block_dimensions();
        queue.write_texture(
            wgpu::TexelCopyTextureInfo {
                texture: &texture,
                mip_level: 0,
                origin: wgpu::Origin3d::ZERO,
                aspect: wgpu::TextureAspect::All,
            },
            bytes,
            wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(width.div_ceil(bw) * format.block_copy_size(None).unwrap()),
                rows_per_image: Some(height.div_ceil(bh)),
            },
            size,
        );
        texture.create_view(&Default::default())
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
            let duration = self.duration();
            if self.position >= duration {
                if self.looping && duration > 0.0 {
                    self.position %= duration;
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
                if self.position >= self.duration() {
                    self.position = 0.0;
                }
                self.playing = self.rig.clip.is_some();
            }
            "pause" => self.playing = false,
            "restart" => {
                self.position = 0.0;
                self.playing = self.rig.clip.is_some();
            }
            "seek" => self.position = value.clamp(0.0, self.duration()),
            "speed" => self.speed = value.clamp(0.1, 4.0),
            "loop" => self.looping = value != 0.0,
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
        if self.failed.load(Ordering::Acquire) {
            return Err("KM-MODEL-GPU-UNAVAILABLE".into());
        }
        if self.minimized || !self.parent_visible() {
            return Ok(());
        }
        let frame_position =
            self.position * self.rig.clip.as_ref().map(|c| c.rate).unwrap_or(1) as f32;
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
        let target = self.center + self.pan;
        let direction = Vec3::new(
            self.yaw.sin() * self.pitch.cos(),
            self.pitch.sin(),
            self.yaw.cos() * self.pitch.cos(),
        );
        let view = Mat4::look_at_rh(target + direction * self.distance, target, Vec3::Y);
        let projection = Mat4::perspective_rh(
            45_f32.to_radians(),
            self.config.width as f32 / self.config.height as f32,
            self.radius * 0.005,
            self.radius * 100.0,
        );
        self.queue.write_buffer(
            &self.camera,
            0,
            bytemuck::cast_slice(&(projection * view).to_cols_array()),
        );
        self.background.update(
            &self.queue,
            projection * view,
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
                    if !self.rig.visible(i, frame_position) {
                        continue;
                    }
                    pass.set_bind_group(1, &mesh.material, &[]);
                    pass.set_vertex_buffer(0, mesh.vertices.slice(..));
                    pass.set_index_buffer(mesh.indices.slice(..), wgpu::IndexFormat::Uint32);
                    pass.draw_indexed(0..mesh.count, 0, 0..1);
                }
            }
        }
        self.queue.submit(Some(encoder.finish()));
        frame.present();
        Ok(())
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
