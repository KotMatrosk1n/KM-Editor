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
}
pub struct Renderer {
    // The surface owns a window reference; it must be dropped before the window.
    surface: wgpu::Surface<'static>,
    pub window: Arc<Window>,
    device: wgpu::Device,
    queue: wgpu::Queue,
    config: wgpu::SurfaceConfiguration,
    pipeline: wgpu::RenderPipeline,
    depth: wgpu::TextureView,
    camera: wgpu::Buffer,
    camera_group: wgpu::BindGroup,
    meshes: Vec<Mesh>,
    pub adapter: String,
    failed: Arc<AtomicBool>,
    minimized: bool,
    center: Vec3,
    radius: f32,
    yaw: f32,
    pitch: f32,
    distance: f32,
    pan: Vec3,
}
impl Renderer {
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
            entries: &[wgpu::BindGroupLayoutEntry {
                binding: 0,
                visibility: wgpu::ShaderStages::VERTEX,
                ty: wgpu::BindingType::Buffer {
                    ty: wgpu::BufferBindingType::Uniform,
                    has_dynamic_offset: false,
                    min_binding_size: wgpu::BufferSize::new(64),
                },
                count: None,
            }],
        });
        let camera_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("Camera"),
            layout: &camera_layout,
            entries: &[wgpu::BindGroupEntry {
                binding: 0,
                resource: camera.as_entire_binding(),
            }],
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
                        min_binding_size: wgpu::BufferSize::new(16),
                    },
                    count: None,
                },
            ],
        });
        let sampler = device.create_sampler(&wgpu::SamplerDescriptor {
            label: Some("Base color"),
            address_mode_u: wgpu::AddressMode::Repeat,
            address_mode_v: wgpu::AddressMode::Repeat,
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            ..Default::default()
        });
        let mut textures = Vec::new();
        for texture in &scene.textures {
            textures.push(Self::texture(
                &device,
                &queue,
                texture.width,
                texture.height,
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
        let mut meshes = Vec::new();
        for primitive in scene.primitives {
            let color = device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
                label: Some("Material color"),
                contents: bytemuck::cast_slice(&primitive.color),
                usage: wgpu::BufferUsages::UNIFORM,
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
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("Static model"), layout: Some(&layout),
            vertex: wgpu::VertexState { module: &shader, entry_point: Some("vs_main"), compilation_options: Default::default(),
                buffers: &[wgpu::VertexBufferLayout { array_stride: 32, step_mode: wgpu::VertexStepMode::Vertex,
                    attributes: &wgpu::vertex_attr_array![0 => Float32x3, 1 => Float32x3, 2 => Float32x2] }] },
            fragment: Some(wgpu::FragmentState { module: &shader, entry_point: Some("fs_main"), compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState { format: config.format, blend: None, write_mask: wgpu::ColorWrites::ALL })] }),
            primitive: wgpu::PrimitiveState { cull_mode: None, ..Default::default() },
            depth_stencil: Some(wgpu::DepthStencilState { format: wgpu::TextureFormat::Depth32Float, depth_write_enabled: true,
                depth_compare: wgpu::CompareFunction::LessEqual, stencil: Default::default(), bias: Default::default() }),
            multisample: Default::default(), multiview: None, cache: None
        });
        let mut result = Self {
            surface,
            window,
            device,
            queue,
            config,
            pipeline,
            depth,
            camera,
            camera_group,
            meshes,
            adapter: info.name,
            failed,
            minimized: false,
            center: scene.center,
            radius: scene.radius,
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
        self.minimized = width == 0 || height == 0;
        if self.minimized {
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
        if self.minimized {
            return Ok(());
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
            pass.set_pipeline(&self.pipeline);
            pass.set_bind_group(0, &self.camera_group, &[]);
            for mesh in &self.meshes {
                pass.set_bind_group(1, &mesh.material, &[]);
                pass.set_vertex_buffer(0, mesh.vertices.slice(..));
                pass.set_index_buffer(mesh.indices.slice(..), wgpu::IndexFormat::Uint32);
                pass.draw_indexed(0..mesh.count, 0, 0..1);
            }
        }
        self.queue.submit(Some(encoder.finish()));
        frame.present();
        Ok(())
    }
}
