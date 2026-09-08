// SPDX-License-Identifier: GPL-3.0-only
use glam::{Mat4, Quat, Vec3};
use serde::Deserialize;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Bone {
    name: String,
    parent: i32,
    joint: i32,
    compensate_scale: bool,
    scale: [f32; 3],
    rotation: [f32; 4],
    translation: [f32; 3],
    inverse_bind: [f32; 16],
}
#[derive(Deserialize)]
pub struct Key {
    frame: f32,
    value: Vec<f32>,
}
#[derive(Deserialize)]
pub struct Track {
    bone: usize,
    scale: Vec<Key>,
    rotation: Vec<Key>,
    translation: Vec<Key>,
}
#[derive(Deserialize, serde::Serialize)]
pub struct ClipReference {
    pub id: String,
}
#[derive(Deserialize)]
pub struct Clip {
    pub id: String,
    pub frames: u32,
    pub rate: u32,
    pub r#loop: bool,
    tracks: Vec<Track>,
    visibility: Vec<Visibility>,
    materials: Vec<MaterialTrack>,
}
#[derive(Deserialize)]
pub struct Visibility {
    name: String,
    keys: Vec<Key>,
}
#[derive(Deserialize)]
pub struct MaterialTrack {
    material: String,
    parameter: String,
    channels: [Vec<Key>; 4],
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MeshInfo {
    pub name: String,
    pub material: String,
    #[serde(default)]
    pub top_origin_uv: bool,
    #[serde(default)]
    pub mask_uv: Option<[f32; 4]>,
    #[serde(default)]
    pub highlight: Option<usize>,
    #[serde(default)]
    pub highlight_color: [f32; 4],
    #[serde(default = "unit_uv")]
    pub highlight_uv: [f32; 4],
    #[serde(default)]
    pub highlight_wrap: [f32; 4],
    #[serde(default)]
    pub underlay: Option<usize>,
    #[serde(default = "unit_uv")]
    pub underlay_uv: [f32; 4],
    #[serde(default)]
    pub underlay_wrap: [f32; 4],
    #[serde(default = "all_channels")]
    pub mask_channels: [f32; 4],
    #[serde(default)]
    pub uv_origins: [f32; 4],
    #[serde(default)]
    pub surface: Option<SurfaceInfo>,
}
#[derive(Deserialize)]
pub struct SurfaceInfo {
    pub values: Vec<[f32; 4]>,
    pub textures: Vec<i32>,
    pub wraps: Vec<[f32; 4]>,
    #[serde(default, rename = "tintSubsurfaceByBaseColor")]
    pub tint_subsurface_by_base_color: bool,
}
fn all_channels() -> [f32; 4] {
    [1.0; 4]
}
fn unit_uv() -> [f32; 4] {
    [1.0, 1.0, 0.0, 0.0]
}
#[derive(Deserialize)]
pub struct Rig {
    pub bones: Vec<Bone>,
    pub clips: Vec<ClipReference>,
    pub clip: Option<Clip>,
    pub warnings: Vec<String>,
    pub meshes: Vec<MeshInfo>,
}
impl Rig {
    pub fn visible(&self, index: usize, frame: f32) -> bool {
        let Some(clip) = &self.clip else {
            return true;
        };
        clip.visibility
            .iter()
            .find(|track| track.name == self.meshes[index].name)
            .and_then(|track| {
                track
                    .keys
                    .iter()
                    .rev()
                    .find(|key| key.frame <= frame)
                    .or(track.keys.first())
            })
            .and_then(|key| key.value.first())
            .is_none_or(|value| *value != 0.0)
    }
    pub fn material(&self, index: usize, frame: f32, base: &[f32]) -> Vec<f32> {
        let mut values = base.to_vec();
        let mut origins = self.meshes[index].uv_origins;
        if values.len() >= 48 && self.meshes[index].mask_uv.is_none() {
            let uv = [values[20], values[21], values[22], values[23]];
            values[32..36].copy_from_slice(&uv);
        }
        if let Some(clip) = &self.clip {
            for track in clip
                .materials
                .iter()
                .filter(|track| track.material == self.meshes[index].material)
            {
                if track.parameter == "UvOrigins" {
                    for (channel, keys) in track.channels.iter().enumerate() {
                        if let Some(value) = scalar(keys, frame) {
                            origins[channel] = value;
                        }
                    }
                    continue;
                }
                let offset = match track.parameter.as_str() {
                    "BaseColor" => 0,
                    "BaseColorLayer1" => 4,
                    "BaseColorLayer2" => 8,
                    "BaseColorLayer3" => 12,
                    "BaseColorLayer4" => 16,
                    "UVScaleOffset" => 20,
                    "UVScaleOffset1" if values.len() >= 44 => 40,
                    "UnderlayUV" if values.len() >= 60 => 48,
                    _ => continue,
                };
                for (channel, keys) in track.channels.iter().enumerate() {
                    if let Some(value) = scalar(keys, frame) {
                        values[offset + channel] = value;
                        if offset == 20
                            && values.len() >= 48
                            && self.meshes[index].mask_uv.is_none()
                        {
                            values[32 + channel] = value;
                        }
                    }
                }
            }
        }
        // Asset origins remain independent of animated translations. Apply them
        // once after evaluating the channels so rest and animated poses agree.
        values[22] += origins[0];
        values[23] += origins[1];
        if values.len() >= 48 && self.meshes[index].mask_uv.is_none() {
            values[34] += origins[0];
            values[35] += origins[1];
        }
        if values.len() >= 60 {
            values[50] += origins[2];
            values[51] += origins[3];
        }
        values
    }
    pub fn validate(&self, meshes: usize) -> Result<(), String> {
        if self.bones.len() > 512
            || self.clips.len() > 2048
            || self.meshes.len() != meshes
            || self.warnings.len() > 32
        {
            return Err("KM-MODEL-UNSUPPORTED".into());
        }
        for (i, bone) in self.bones.iter().enumerate() {
            if bone.parent < -1
                || bone.parent >= i as i32
                || bone.joint < -1
                || bone.joint >= 512
                || bone.name.len() > 4096
                || bone
                    .scale
                    .iter()
                    .chain(&bone.rotation)
                    .chain(&bone.translation)
                    .chain(&bone.inverse_bind)
                    .any(|v| !v.is_finite() || v.abs() > 1_000_000.0)
            {
                return Err("KM-MODEL-UNSUPPORTED".into());
            }
        }
        if let Some(clip) = &self.clip {
            if clip.frames == 0
                || clip.frames > 18000
                || clip.rate == 0
                || clip.rate > 240
                || clip.tracks.len() > self.bones.len()
            {
                return Err("KM-MODEL-UNSUPPORTED".into());
            }
            let mut keys = 0;
            for track in &clip.tracks {
                if track.bone >= self.bones.len() {
                    return Err("KM-MODEL-UNSUPPORTED".into());
                }
                for (curve, width) in [
                    (&track.scale, 3),
                    (&track.rotation, 4),
                    (&track.translation, 3),
                ] {
                    keys += curve.len();
                    if keys > 250_000 {
                        return Err("KM-MODEL-UNSUPPORTED".into());
                    }
                    let mut previous = -1.0;
                    for key in curve {
                        if key.frame < previous
                            || key.frame >= clip.frames as f32
                            || key.value.len() != width
                            || key
                                .value
                                .iter()
                                .any(|v| !v.is_finite() || v.abs() > 1_000_000.0)
                        {
                            return Err("KM-MODEL-UNSUPPORTED".into());
                        }
                        previous = key.frame;
                    }
                }
            }
            if clip.visibility.len() > 512 || clip.materials.len() > 4096 {
                return Err("KM-MODEL-UNSUPPORTED".into());
            }
            for curve in clip.visibility.iter().map(|track| &track.keys).chain(
                clip.materials
                    .iter()
                    .flat_map(|track| track.channels.iter()),
            ) {
                keys += curve.len();
                if keys > 250_000 {
                    return Err("KM-MODEL-UNSUPPORTED".into());
                }
                let mut previous = -1.0;
                for key in curve {
                    if !key.frame.is_finite()
                        || key.frame < 0.0
                        || key.frame < previous
                        || key.frame > 18000.0
                        || key.value.len() != 1
                        || key
                            .value
                            .iter()
                            .any(|v| !v.is_finite() || v.abs() > 1_000_000.0)
                    {
                        return Err("KM-MODEL-UNSUPPORTED".into());
                    }
                    previous = key.frame;
                }
            }
        }
        Ok(())
    }
    pub fn matrices(&self, frame: f32) -> Vec<[f32; 16]> {
        let mut scales: Vec<Vec3> = self
            .bones
            .iter()
            .map(|b| Vec3::from_array(b.scale))
            .collect();
        let mut rotations: Vec<Quat> = self
            .bones
            .iter()
            .map(|b| Quat::from_array(b.rotation))
            .collect();
        let mut translations: Vec<Vec3> = self
            .bones
            .iter()
            .map(|b| Vec3::from_array(b.translation))
            .collect();
        if let Some(clip) = &self.clip {
            for track in &clip.tracks {
                if let Some(v) = sample(&track.scale, frame, false) {
                    scales[track.bone] = Vec3::new(v[0], v[1], v[2]);
                }
                if let Some(v) = sample(&track.translation, frame, false) {
                    translations[track.bone] = Vec3::new(v[0], v[1], v[2]);
                }
                if let Some(v) = sample(&track.rotation, frame, true) {
                    rotations[track.bone] = Quat::from_array(v);
                }
            }
        }
        let mut global = vec![Mat4::IDENTITY; self.bones.len()];
        let mut joints = vec![Mat4::IDENTITY.to_cols_array(); 512];
        for (i, bone) in self.bones.iter().enumerate() {
            let rotation_scale = Mat4::from_quat(rotations[i]) * Mat4::from_scale(scales[i]);
            global[i] = if bone.parent >= 0 {
                let parent = bone.parent as usize;
                let compensation = if bone.compensate_scale {
                    Mat4::from_scale(scales[parent].map(|v| {
                        if v.abs() > 0.000001 {
                            1.0 / v
                        } else {
                            0.0
                        }
                    }))
                } else {
                    Mat4::IDENTITY
                };
                global[parent]
                    * Mat4::from_translation(translations[i])
                    * compensation
                    * rotation_scale
            } else {
                Mat4::from_translation(translations[i]) * rotation_scale
            };
            if bone.joint >= 0 {
                joints[bone.joint as usize] =
                    (global[i] * Mat4::from_cols_array(&bone.inverse_bind)).to_cols_array();
            }
        }
        joints
    }
}
fn scalar(keys: &[Key], frame: f32) -> Option<f32> {
    let first = keys.first()?;
    let right = keys
        .partition_point(|key| key.frame <= frame)
        .min(keys.len() - 1);
    let b = &keys[right];
    let a = if b.frame <= frame {
        b
    } else if right > 0 {
        &keys[right - 1]
    } else {
        first
    };
    let t = if b.frame > a.frame {
        ((frame - a.frame) / (b.frame - a.frame)).clamp(0.0, 1.0)
    } else {
        0.0
    };
    Some(a.value.first()? + (b.value.first()? - a.value.first()?) * t)
}
fn sample(keys: &[Key], frame: f32, rotation: bool) -> Option<[f32; 4]> {
    let first = keys.first()?;
    let right = keys
        .partition_point(|key| key.frame <= frame)
        .min(keys.len() - 1);
    let b = &keys[right];
    let a = if b.frame <= frame {
        b
    } else if right > 0 {
        &keys[right - 1]
    } else {
        first
    };
    let amount = if b.frame > a.frame {
        ((frame - a.frame) / (b.frame - a.frame)).clamp(0.0, 1.0)
    } else {
        0.0
    };
    let mut output = [0.0; 4];
    if rotation {
        let qa = Quat::from_xyzw(a.value[0], a.value[1], a.value[2], a.value[3]).normalize();
        let qb = Quat::from_xyzw(b.value[0], b.value[1], b.value[2], b.value[3]).normalize();
        output = qa.slerp(qb, amount).to_array();
    } else {
        for (i, value) in output.iter_mut().enumerate().take(3) {
            *value = a.value[i] + (b.value[i] - a.value[i]) * amount;
        }
    }
    Some(output)
}
