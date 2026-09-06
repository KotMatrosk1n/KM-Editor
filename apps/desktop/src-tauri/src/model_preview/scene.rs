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
    pub color: [f32; 4],
}
pub struct Scene {
    pub textures: Vec<Texture>,
    pub primitives: Vec<Primitive>,
    pub center: Vec3,
    pub radius: f32,
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
        if r.take(4)? != b"KMV1" {
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
                0x1d01 => (wgpu::TextureFormat::Bc4RUnorm, 8),
                0x1e01 => (wgpu::TextureFormat::Bc5RgUnorm, 16),
                0x2001 => (wgpu::TextureFormat::Bc7RgbaUnorm, 16),
                0x2006 => (wgpu::TextureFormat::Bc7RgbaUnormSrgb, 16),
                _ => return Err("KM-MODEL-UNSUPPORTED".into()),
            };
            let count = r.count(16 * 1024 * 1024)?;
            texture_bytes += count;
            if width < 4
                || height < 4
                || width % 4 != 0
                || height % 4 != 0
                || count != (width * height / 16 * block) as usize
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
            let mut color = [0.0; 4];
            for value in &mut color {
                *value = r.float()?.clamp(0.0, 1.0);
            }
            let mut vertices = Vec::with_capacity(vertex_count * 8);
            for _ in 0..vertex_count {
                let pos = Vec3::new(r.float()?, r.float()?, r.float()?);
                min = min.min(pos);
                max = max.max(pos);
                vertices.extend_from_slice(&pos.to_array());
                for _ in 0..5 {
                    vertices.push(r.float()?);
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
                color,
            });
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
        })
    }
}
