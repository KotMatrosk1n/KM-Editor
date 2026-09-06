// SPDX-License-Identifier: GPL-3.0-only
struct Camera { view_projection: mat4x4<f32> };
struct Material { color: vec4<f32> };
@group(0) @binding(0) var<uniform> camera: Camera;
@group(1) @binding(0) var base_color: texture_2d<f32>;
@group(1) @binding(1) var base_sampler: sampler;
@group(1) @binding(2) var<uniform> material: Material;
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) normal: vec3<f32>,
    @location(1) uv: vec2<f32>,
};
@vertex fn vs_main(@location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>) -> VertexOutput {
    var output: VertexOutput;
    output.position = camera.view_projection * vec4<f32>(position, 1.0);
    output.normal = normal;
    // Model UVs have a bottom-left origin; the decoded texture rows start at the top.
    output.uv = vec2<f32>(uv.x, 1.0 - uv.y);
    return output;
}
@fragment fn fs_main(input: VertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4<f32> {
    let color = textureSample(base_color, base_sampler, input.uv) * material.color;
    if color.a < 0.2 { discard; }
    let n = normalize(input.normal + vec3<f32>(0.00001));
    let light = 0.48 + 0.52 * max(dot(select(-n, n, front), normalize(vec3<f32>(-0.4, 0.8, 0.6))), 0.0);
    return vec4<f32>(color.rgb * light, 1.0);
}
