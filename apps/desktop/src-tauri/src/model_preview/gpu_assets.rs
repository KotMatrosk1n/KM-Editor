// SPDX-License-Identifier: GPL-3.0-only
use super::{
    gpu::Mesh,
    scene::{Scene, Texture},
};
use std::collections::VecDeque;
use wgpu::util::DeviceExt;

pub(super) fn material_values(scene: &Scene, index: usize) -> Vec<f32> {
    let primitive = &scene.primitives[index];
    let mut values = Vec::with_capacity(388);
    values.extend_from_slice(&primitive.material);
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
    values.extend_from_slice(&appearance.highlight_wrap[..2]);
    values.extend_from_slice(&appearance.mask_channels);
    let surface = appearance.surface.as_ref();
    let surface_textures: Vec<Option<usize>> = (0..12)
        .map(|i| surface.and_then(|s| usize::try_from(s.textures[i]).ok()))
        .collect();
    for i in 0..41 {
        values.extend_from_slice(&surface.map(|s| s.values[i]).unwrap_or([0.0; 4]));
    }
    for i in 0..12 {
        values.extend_from_slice(&surface.map(|s| s.wraps[i]).unwrap_or([0.0; 4]));
    }
    for index in &surface_textures {
        values.extend_from_slice(&ratio(*index));
        let two_channel = index
            .is_some_and(|i| scene.textures[i].format == wgpu::TextureFormat::Bc5RgUnorm);
        values.extend_from_slice(&[
            if index.is_some() { 1.0 } else { 0.0 },
            if two_channel { 1.0 } else { 0.0 },
        ]);
    }
    for i in 0..15 {
        values.extend_from_slice(&surface.and_then(|s| s.restoration_values.get(i)).copied().unwrap_or([0.0; 4]));
    }
    values.extend_from_slice(&[
        if surface.is_some() { 1.0 } else { 0.0 },
        if primitive.blend { 1.0 } else { 0.0 },
        if surface.is_some_and(|s| s.tint_subsurface_by_base_color) {
            1.0
        } else {
            0.0
        },
        surface.map(|s| if s.restoration_effect >= 7 { 5.0 + s.restoration_effect as f32 } else if s.restoration_effect == 6 { 11.0 } else if s.restoration_effect == 5 { 10.0 } else if s.restoration_effect > 0 { 4.0 + s.restoration_effect as f32 } else if s.surface_layers { 4.0 } else if s.layer_emission { 3.0 } else { s.effect as f32 }).unwrap_or(0.0),
    ]);
    values
}

// Same filtering and gradient path as existing gem samplers, independent axes.
pub(super) fn gem_repeat_u_edge_v_descriptor() -> wgpu::SamplerDescriptor<'static> {
    wgpu::SamplerDescriptor {
        label: Some("Source gem U repeat V edge"),
        address_mode_u: wgpu::AddressMode::Repeat,
        address_mode_v: wgpu::AddressMode::ClampToEdge,
        address_mode_w: wgpu::AddressMode::ClampToEdge,
        mag_filter: wgpu::FilterMode::Linear,
        min_filter: wgpu::FilterMode::Linear,
        mipmap_filter: wgpu::FilterMode::Linear,
        ..Default::default()
    }
}

pub(super) fn gem_edge_u_repeat_v_descriptor() -> wgpu::SamplerDescriptor<'static> {
    wgpu::SamplerDescriptor {
        label: Some("Source gem U edge V repeat"),
        address_mode_u: wgpu::AddressMode::ClampToEdge,
        address_mode_v: wgpu::AddressMode::Repeat,
        address_mode_w: wgpu::AddressMode::ClampToEdge,
        mag_filter: wgpu::FilterMode::Linear,
        min_filter: wgpu::FilterMode::Linear,
        mipmap_filter: wgpu::FilterMode::Linear,
        ..Default::default()
    }
}

// Native U2/V0 uses independent hardware addressing; UVs and gradients remain unwrapped.
pub(super) fn gem_mirror_u_repeat_v_descriptor() -> wgpu::SamplerDescriptor<'static> {
    wgpu::SamplerDescriptor {
        label: Some("Source gem U mirrored repeat V repeat"),
        address_mode_u: wgpu::AddressMode::MirrorRepeat,
        address_mode_v: wgpu::AddressMode::Repeat,
        address_mode_w: wgpu::AddressMode::ClampToEdge,
        mag_filter: wgpu::FilterMode::Linear,
        min_filter: wgpu::FilterMode::Linear,
        mipmap_filter: wgpu::FilterMode::Linear,
        ..Default::default()
    }
}

pub(super) struct Assets {
    layout: wgpu::BindGroupLayout,
    gem_layout: wgpu::BindGroupLayout,
    sampler: wgpu::Sampler,
    repeat_sampler: wgpu::Sampler,
    gem_repeat_u_edge_v_sampler: wgpu::Sampler,
    gem_edge_u_repeat_v_sampler: wgpu::Sampler,
    gem_mirror_u_repeat_v_sampler: wgpu::Sampler,
    textures: VecDeque<(Texture, wgpu::TextureView)>,
    buffers: VecDeque<(Vec<u8>, wgpu::BufferUsages, wgpu::Buffer)>,
    resolution: u32,
    texture_bytes: usize,
    buffer_bytes: usize,
}
impl Assets {
    pub fn new(device: &wgpu::Device, layout: wgpu::BindGroupLayout, gem_layout: wgpu::BindGroupLayout) -> Self {
        let sampler = device.create_sampler(&wgpu::SamplerDescriptor {
            label: Some("Model filtering"),
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            mipmap_filter: wgpu::FilterMode::Linear,
            ..Default::default()
        });
        let repeat_sampler = device.create_sampler(&wgpu::SamplerDescriptor {
            label: Some("Source gem filtering"),
            address_mode_u: wgpu::AddressMode::Repeat,
            address_mode_v: wgpu::AddressMode::Repeat,
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            mipmap_filter: wgpu::FilterMode::Linear,
            ..Default::default()
        });
        let gem_repeat_u_edge_v_sampler = device.create_sampler(&gem_repeat_u_edge_v_descriptor());
        let gem_edge_u_repeat_v_sampler = device.create_sampler(&gem_edge_u_repeat_v_descriptor());
        let gem_mirror_u_repeat_v_sampler = device.create_sampler(&gem_mirror_u_repeat_v_descriptor());
        Self {
            layout,
            gem_layout,
            sampler,
            repeat_sampler,
            gem_repeat_u_edge_v_sampler,
            gem_edge_u_repeat_v_sampler,
            gem_mirror_u_repeat_v_sampler,
            textures: VecDeque::new(),
            buffers: VecDeque::new(),
            resolution: 1,
            texture_bytes: 0,
            buffer_bytes: 0,
        }
    }
    fn buffer(
        &mut self,
        device: &wgpu::Device,
        bytes: &[u8],
        usage: wgpu::BufferUsages,
    ) -> wgpu::Buffer {
        if let Some(index) = self
            .buffers
            .iter()
            .position(|(data, kind, _)| *kind == usage && data == bytes)
        {
            let entry = self.buffers.remove(index).unwrap();
            let result = entry.2.clone();
            self.buffers.push_back(entry);
            return result;
        }
        let buffer = device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: Some("Model geometry"),
            contents: bytes,
            usage,
        });
        let size = bytes.len() * 2;
        if size <= 64 * 1024 * 1024 {
            while self.buffer_bytes + size > 64 * 1024 * 1024 || self.buffers.len() >= 512 {
                if let Some((old, _, _)) = self.buffers.pop_front() {
                    self.buffer_bytes -= old.len() * 2;
                } else {
                    break;
                }
            }
            self.buffers
                .push_back((bytes.to_vec(), usage, buffer.clone()));
            self.buffer_bytes += size;
        }
        buffer
    }
    fn texture(
        &mut self,
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        source: &Texture,
    ) -> wgpu::TextureView {
        if let Some(index) = self.textures.iter().position(|(data, _)| data == source) {
            let entry = self.textures.remove(index).unwrap();
            let result = entry.1.clone();
            self.textures.push_back(entry);
            return result;
        }
        let (bw, bh) = source.format.block_dimensions();
        let width = source.width.div_ceil(bw) * bw;
        let height = source.height.div_ceil(bh) * bh;
        let texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("Model texture"),
            size: wgpu::Extent3d {
                width,
                height,
                depth_or_array_layers: source.layers,
            },
            mip_level_count: 1 + source.mips.len() as u32,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: source.format,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
            view_formats: &[],
        });
        for (level, bytes) in std::iter::once(&source.bytes)
            .chain(&source.mips)
            .enumerate()
        {
            let w = (width >> level).max(1).div_ceil(bw) * bw;
            let h = (height >> level).max(1).div_ceil(bh) * bh;
            queue.write_texture(
                wgpu::TexelCopyTextureInfo {
                    texture: &texture,
                    mip_level: level as u32,
                    origin: wgpu::Origin3d::ZERO,
                    aspect: wgpu::TextureAspect::All,
                },
                bytes,
                wgpu::TexelCopyBufferLayout {
                    offset: 0,
                    bytes_per_row: Some(
                        w.div_ceil(bw) * source.format.block_copy_size(None).unwrap(),
                    ),
                    rows_per_image: Some(h.div_ceil(bh)),
                },
                wgpu::Extent3d {
                    width: w,
                    height: h,
                    depth_or_array_layers: source.layers,
                },
            );
        }
        let view = texture.create_view(&wgpu::TextureViewDescriptor {
            dimension: Some(if source.layers == 6 { wgpu::TextureViewDimension::Cube } else { wgpu::TextureViewDimension::D2 }),
            ..Default::default()
        });
        let size = (source.bytes.len() + source.mips.iter().map(Vec::len).sum::<usize>()) * 2;
        if size <= 64 * 1024 * 1024 {
            while self.texture_bytes + size > 64 * 1024 * 1024 || self.textures.len() >= 128 {
                if let Some((old, _)) = self.textures.pop_front() {
                    self.texture_bytes -=
                        (old.bytes.len() + old.mips.iter().map(Vec::len).sum::<usize>()) * 2;
                } else {
                    break;
                }
            }
            self.textures.push_back((source.clone(), view.clone()));
            self.texture_bytes += size;
        }
        view
    }
    pub fn edges(&mut self, device: &wgpu::Device, indices: &[u32]) -> wgpu::Buffer {
        let edges: Vec<u32> = indices
            .chunks_exact(3)
            .flat_map(|t| [t[0], t[1], t[1], t[2], t[2], t[0]])
            .collect();
        self.buffer(
            device,
            bytemuck::cast_slice(&edges),
            wgpu::BufferUsages::INDEX,
        )
    }
    pub fn meshes(
        &mut self,
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        scene: &Scene,
    ) -> Vec<Mesh> {
        if self.resolution != scene.resolution {
            self.textures.clear();
            self.texture_bytes = 0;
            self.resolution = scene.resolution;
        }
        let textures: Vec<_> = scene
            .textures
            .iter()
            .map(|t| self.texture(device, queue, t))
            .collect();
        let white = self.texture(
            device,
            queue,
            &Texture {
                width: 1,
                height: 1,
                layers: 1,
                format: wgpu::TextureFormat::Rgba8UnormSrgb,
                bytes: vec![255; 4],
                mips: vec![],
            },
        );
        let black = self.texture(
            device,
            queue,
            &Texture {
                width: 1,
                height: 1,
                layers: 1,
                format: wgpu::TextureFormat::Rgba8Unorm,
                bytes: vec![0; 4],
                mips: vec![],
            },
        );
        let mut meshes = Vec::new();
        for (index, primitive) in scene.primitives.iter().enumerate() {
            let values = material_values(scene, index);
            let appearance = &scene.rig.meshes[index];
            let surface = appearance.surface.as_ref();
            let gem = surface.is_some_and(|s| s.restoration_effect == 10);
            let surface_textures: Vec<Option<usize>> = (0..12)
                .map(|i| surface.and_then(|s| usize::try_from(s.textures[i]).ok()))
                .collect();
            let color = device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
                label: Some("Material color"),
                contents: bytemuck::cast_slice(&values),
                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            });
            let mut entries = vec![
                wgpu::BindGroupEntry {
                    binding: 18,
                    resource: wgpu::BindingResource::Sampler(&self.repeat_sampler),
                },
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: wgpu::BindingResource::TextureView(
                        primitive.texture.map(|i| &textures[i]).unwrap_or(&white),
                    ),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: wgpu::BindingResource::Sampler(&self.sampler),
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
            ];
            for (slot, index) in surface_textures.iter().enumerate() {
                entries.push(wgpu::BindGroupEntry {
                    binding: 6 + slot as u32,
                    resource: wgpu::BindingResource::TextureView(
                        index.map(|i| &textures[i]).unwrap_or(if slot == 3 {
                            &black
                        } else {
                            &white
                        }),
                    ),
                });
            }
            if gem {
                entries.retain(|entry| [0, 1, 2, 3, 5, 6, 8, 13, 18].contains(&entry.binding));
                entries.push(wgpu::BindGroupEntry {
                    binding: 20,
                    resource: wgpu::BindingResource::Sampler(&self.gem_repeat_u_edge_v_sampler),
                });
                entries.push(wgpu::BindGroupEntry {
                    binding: 21,
                    resource: wgpu::BindingResource::Sampler(&self.gem_edge_u_repeat_v_sampler),
                });
                entries.push(wgpu::BindGroupEntry {
                    binding: 22,
                    resource: wgpu::BindingResource::Sampler(&self.gem_mirror_u_repeat_v_sampler),
                });
                // Scene validation requires this actual authored cube; there is no probe fallback.
                entries.push(wgpu::BindGroupEntry {
                    binding: 19,
                    resource: wgpu::BindingResource::TextureView(&textures[surface.unwrap().gem_probe_texture.unwrap()]),
                });
            }
            let material = device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("Material"),
                layout: if gem { &self.gem_layout } else { &self.layout },
                entries: &entries,
            });
            let mut vertex_data = Vec::with_capacity(primitive.vertices.len() / 16 * 22);
            for (vertex, source) in primitive.vertices.chunks_exact(16).enumerate() {
                vertex_data.extend_from_slice(source);
                let secondary = appearance.secondary_uv.as_ref()
                    .map(|uv| &uv[vertex * 2..vertex * 2 + 2])
                    .unwrap_or(&source[6..8]);
                vertex_data.extend_from_slice(secondary);
                vertex_data.extend_from_slice(appearance.tangent0.as_ref()
                    .map(|t| &t[vertex * 4..vertex * 4 + 4]).unwrap_or(&[0.0; 4]));
            }
            meshes.push(Mesh {
                edges: None,
                vertices: self.buffer(
                    device,
                    bytemuck::cast_slice(&vertex_data),
                    wgpu::BufferUsages::VERTEX,
                ),
                indices: self.buffer(
                    device,
                    bytemuck::cast_slice(&primitive.indices),
                    wgpu::BufferUsages::INDEX,
                ),
                count: primitive.indices.len() as u32,
                material,
                gem,
                blend: primitive.blend,
                additive: primitive.additive,
                colors: color,
                values,
            });
        }
        meshes
    }
}
