// SPDX-License-Identifier: GPL-3.0-only
use super::{
    animation::Rig,
    scene::{Primitive, Scene},
};
use glam::{Mat4, Vec3};

pub struct Inspection {
    pub geometry: Vec<Primitive>,
    pub textures: Vec<[u32; 2]>,
    pub texture_bytes: u64,
}
impl Inspection {
    pub fn take(scene: &mut Scene) -> Self {
        Self {
            geometry: std::mem::take(&mut scene.primitives),
            textures: scene.textures.iter().map(|t| [t.width, t.height]).collect(),
            texture_bytes: scene
                .textures
                .iter()
                .map(|t| (t.bytes.len() + t.mips.iter().map(Vec::len).sum::<usize>()) as u64)
                .sum(),
        }
    }
    pub fn parts(&self, rig: &Rig) -> Vec<serde_json::Value> {
        self.geometry.iter().zip(&rig.meshes).enumerate().map(|(id, (p, m))|
            serde_json::json!({"id": id, "name": m.name, "material": m.material, "triangles": p.indices.len() / 3})).collect()
    }
    pub fn uv(&self, part: usize) -> Result<serde_json::Value, String> {
        let p = self.geometry.get(part).ok_or("KM-MODEL-UNSUPPORTED")?;
        Ok(
            serde_json::json!({"vertices": p.vertices.chunks_exact(16).map(|v| [v[6], 1.0-v[7]]).collect::<Vec<_>>(), "indices": p.indices}),
        )
    }
    pub fn pick(
        &self,
        rig: &Rig,
        frame: f32,
        origin: Vec3,
        direction: Vec3,
        hidden: &[usize],
    ) -> Option<usize> {
        let matrices: Vec<Mat4> = rig
            .matrices(frame)
            .iter()
            .map(Mat4::from_cols_array)
            .collect();
        let mut nearest = f32::INFINITY;
        let mut selected = None;
        for (index, p) in self.geometry.iter().enumerate() {
            if hidden.contains(&index) || !rig.visible(index, frame) {
                continue;
            }
            let points: Vec<Vec3> = p
                .vertices
                .chunks_exact(16)
                .map(|v| {
                    let point = Vec3::new(v[0], v[1], v[2]);
                    if v[12..16].iter().sum::<f32>() <= 0.0 {
                        return point;
                    }
                    (0..4)
                        .map(|i| matrices[v[8 + i] as usize].transform_point3(point) * v[12 + i])
                        .sum()
                })
                .collect();
            for triangle in p.indices.chunks_exact(3) {
                if let Some(distance) = ray_triangle(
                    origin,
                    direction,
                    points[triangle[0] as usize],
                    points[triangle[1] as usize],
                    points[triangle[2] as usize],
                ) {
                    if distance < nearest {
                        nearest = distance;
                        selected = Some(index);
                    }
                }
            }
        }
        selected
    }
}
fn ray_triangle(origin: Vec3, direction: Vec3, a: Vec3, b: Vec3, c: Vec3) -> Option<f32> {
    let e1 = b - a;
    let e2 = c - a;
    let h = direction.cross(e2);
    let det = e1.dot(h);
    if det.abs() < 1e-9 {
        return None;
    }
    let s = origin - a;
    let u = s.dot(h) / det;
    if !(0.0..=1.0).contains(&u) {
        return None;
    }
    let q = s.cross(e1);
    let v = direction.dot(q) / det;
    if v < 0.0 || u + v > 1.0 {
        return None;
    }
    let distance = e2.dot(q) / det;
    (distance >= 0.0).then_some(distance)
}
