// SPDX-License-Identifier: GPL-3.0-only
use glam::Vec3;

pub struct Texture {
    pub width: u32,
    pub height: u32,
    pub format: wgpu::TextureFormat,
    pub bytes: Vec<u8>,
}
pub struct Primitive {
    pub vertices: Vec<f32>,
    pub indices: Vec<u32>,
    pub texture: Option<usize>,
    pub mask: Option<usize>,
    pub material: [f32; 28],
    pub blend: bool,
}
pub struct Scene {
    pub textures: Vec<Texture>,
    pub primitives: Vec<Primitive>,
    pub center: Vec3,
    pub radius: f32,
    pub floor: f32,
    pub rig: super::animation::Rig,
}
struct Reader<'a> {
    bytes: &'a [u8],
    at: usize,
}
impl<'a> Reader<'a> {
    fn take(&mut self, n: usize) -> Result<&'a [u8], String> {
        let end = self.at.checked_add(n).ok_or("KM-MODEL-UNSUPPORTED")?;
        let result = self.bytes.get(self.at..end).ok_or("KM-MODEL-UNSUPPORTED")?;
        self.at = end;
        Ok(result)
    }
    fn u32(&mut self) -> Result<u32, String> {
        Ok(u32::from_le_bytes(self.take(4)?.try_into().unwrap()))
    }
    fn count(&mut self, max: usize) -> Result<usize, String> {
        let n = self.u32()? as usize;
        if n > max {
            return Err("KM-MODEL-UNSUPPORTED".into());
        }
        Ok(n)
    }
    fn float(&mut self) -> Result<f32, String> {
        let v = f32::from_bits(self.u32()?);
        if !v.is_finite() || v.abs() > 1_000_000.0 {
            return Err("KM-MODEL-UNSUPPORTED".into());
        }
        Ok(v)
    }
}
impl Scene {
    pub fn read(bytes: &[u8]) -> Result<Self, String> {
        if bytes.len() > 96 * 1024 * 1024 {
            return Err("KM-MODEL-UNSUPPORTED".into());
        }
        let mut r = Reader { bytes, at: 0 };
        if r.take(4)? != b"KMV2" {
            return Err("KM-MODEL-UNSUPPORTED".into());
        }
        let mesh_count = r.count(256)?;
        let texture_count = r.count(32)?;
        if mesh_count == 0 {
            return Err("KM-MODEL-UNSUPPORTED".into());
        }
        let mut textures = Vec::new();
        let mut texture_bytes = 0;
        for _ in 0..texture_count {
            let width = r.count(4096)? as u32;
            let height = r.count(4096)? as u32;
            let (format, block) = match r.u32()? {
                0x0b01 => (wgpu::TextureFormat::Rgba8Unorm, 4),
                0x0b06 => (wgpu::TextureFormat::Rgba8UnormSrgb, 4),
                0x1a01 => (wgpu::TextureFormat::Bc1RgbaUnorm, 8),
                0x1a06 => (wgpu::TextureFormat::Bc1RgbaUnormSrgb, 8),
                0x1b01 => (wgpu::TextureFormat::Bc2RgbaUnorm, 16),
                0x1b06 => (wgpu::TextureFormat::Bc2RgbaUnormSrgb, 16),
                0x1c01 => (wgpu::TextureFormat::Bc3RgbaUnorm, 16),
                0x1c06 => (wgpu::TextureFormat::Bc3RgbaUnormSrgb, 16),
                0x1d01 => (wgpu::TextureFormat::Bc4RUnorm, 8),
                0x1e01 => (wgpu::TextureFormat::Bc5RgUnorm, 16),
                0x2001 => (wgpu::TextureFormat::Bc7RgbaUnorm, 16),
                0x2006 => (wgpu::TextureFormat::Bc7RgbaUnormSrgb, 16),
                _ => return Err("KM-MODEL-UNSUPPORTED".into()),
            };
            let count = r.count(16 * 1024 * 1024)?;
            texture_bytes += count;
            let (block_width, block_height) = format.block_dimensions();
            if width == 0
                || height == 0
                || count
                    != (width.div_ceil(block_width) * height.div_ceil(block_height) * block)
                        as usize
                || texture_bytes > 48 * 1024 * 1024
            {
                return Err("KM-MODEL-UNSUPPORTED".into());
            }
            textures.push(Texture {
                width,
                height,
                format,
                bytes: r.take(count)?.to_vec(),
            });
        }
        let mut primitives = Vec::new();
        let mut total_vertices = 0;
        let mut total_indices = 0;
        let mut min = Vec3::splat(f32::MAX);
        let mut max = Vec3::splat(f32::MIN);
        for _ in 0..mesh_count {
            let vertex_count = r.count(500_000)?;
            let index_count = r.count(3_000_000)?;
            total_vertices += vertex_count;
            total_indices += index_count;
            if vertex_count == 0
                || index_count == 0
                || index_count % 3 != 0
                || total_vertices > 500_000
                || total_indices > 3_000_000
            {
                return Err("KM-MODEL-UNSUPPORTED".into());
            }
            let material = r.u32()?;
            let texture = if material == u32::MAX {
                None
            } else if material < texture_count as u32 {
                Some(material as usize)
            } else {
                return Err("KM-MODEL-UNSUPPORTED".into());
            };
            let mask = r.u32()?;
            let mask = if mask == u32::MAX {
                None
            } else if mask < texture_count as u32 {
                Some(mask as usize)
            } else {
                return Err("KM-MODEL-UNSUPPORTED".into());
            };
            let mut material = [0.0; 28];
            for value in &mut material {
                *value = r.float()?;
            }
            let blend = r.u32()? != 0;
            let mut vertices = Vec::with_capacity(vertex_count * 16);
            for _ in 0..vertex_count {
                let pos = Vec3::new(r.float()?, r.float()?, r.float()?);
                min = min.min(pos);
                max = max.max(pos);
                vertices.extend_from_slice(&pos.to_array());
                for _ in 0..5 {
                    vertices.push(r.float()?);
                }
                for _ in 0..4 {
                    let joint = r.float()?;
                    if !(0.0..512.0).contains(&joint) || joint.fract() != 0.0 {
                        return Err("KM-MODEL-UNSUPPORTED".into());
                    }
                    vertices.push(joint);
                }
                for _ in 0..4 {
                    let weight = r.float()?;
                    if !(0.0..=1.0).contains(&weight) {
                        return Err("KM-MODEL-UNSUPPORTED".into());
                    }
                    vertices.push(weight);
                }
            }
            let mut indices = Vec::with_capacity(index_count);
            for _ in 0..index_count {
                let index = r.u32()?;
                if index >= vertex_count as u32 {
                    return Err("KM-MODEL-UNSUPPORTED".into());
                }
                indices.push(index);
            }
            primitives.push(Primitive {
                vertices,
                indices,
                texture,
                mask,
                material,
                blend,
            });
        }
        let metadata_size = r.count(16 * 1024 * 1024)?;
        let rig: super::animation::Rig =
            serde_json::from_slice(r.take(metadata_size)?).map_err(|_| "KM-MODEL-UNSUPPORTED")?;
        rig.validate(mesh_count)?;
        for mesh in &rig.meshes {
            if mesh.highlight.is_some_and(|index| index >= texture_count)
                || mesh.underlay.is_some_and(|index| index >= texture_count)
                || mesh
                    .mask_uv
                    .iter()
                    .flatten()
                    .chain(&mesh.highlight_uv)
                    .chain(&mesh.highlight_color)
                    .chain(&mesh.underlay_uv)
                    .chain(&mesh.underlay_wrap)
                    .chain(&mesh.mask_channels)
                    .chain(&mesh.uv_origins)
                    .any(|v| !v.is_finite() || v.abs() > 1_000_000.0)
            {
                return Err("KM-MODEL-UNSUPPORTED".into());
            }
        }
        let joints = rig.matrices(0.0);
        let stored_bounds = (min, max);
        min = Vec3::splat(f32::MAX);
        max = Vec3::splat(f32::MIN);
        for (i, primitive) in primitives.iter().enumerate() {
            if !rig.visible(i, 0.0) {
                continue;
            }
            for index in &primitive.indices {
                let vertex = &primitive.vertices[*index as usize * 16..][..16];
                let position = Vec3::new(vertex[0], vertex[1], vertex[2]);
                let mut transformed = Vec3::ZERO;
                let mut weight = 0.0;
                for j in 0..4 {
                    if vertex[12 + j] > 0.0 {
                        transformed += glam::Mat4::from_cols_array(&joints[vertex[8 + j] as usize])
                            .transform_point3(position)
                            * vertex[12 + j];
                        weight += vertex[12 + j];
                    }
                }
                let point = if weight > 0.0 { transformed } else { position };
                min = min.min(point);
                max = max.max(point);
            }
        }
        if min.cmpgt(max).any() {
            (min, max) = stored_bounds;
        }
        if r.at != bytes.len() {
            return Err("KM-MODEL-UNSUPPORTED".into());
        }
        let radius = (max - min).length() * 0.5;
        if !radius.is_finite() || radius < 0.00001 {
            return Err("KM-MODEL-UNSUPPORTED".into());
        }
        Ok(Self {
            textures,
            primitives,
            center: (min + max) * 0.5,
            radius,
            floor: min.y,
            rig,
        })
    }
}
