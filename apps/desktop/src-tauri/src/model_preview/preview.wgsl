// SPDX-License-Identifier: GPL-3.0-only
struct Camera { view_projection: mat4x4<f32> };
struct Material { color: vec4<f32>, layers: array<vec4<f32>, 4>, uv: vec4<f32>, wrap: vec4<f32>, texture_scale: vec4<f32>, mask_uv: vec4<f32>, highlight_color: vec4<f32>, highlight_uv: vec4<f32>, highlight_scale: vec4<f32>, underlay_uv: vec4<f32>, underlay_wrap: vec4<f32>, underlay_scale: vec4<f32>, mask_channels: vec4<f32> };
@group(0) @binding(0) var<uniform> camera: Camera;
@group(0) @binding(1) var<storage, read> joints: array<mat4x4<f32>>;
@group(1) @binding(0) var base_color: texture_2d<f32>;
@group(1) @binding(1) var base_sampler: sampler;
@group(1) @binding(2) var<uniform> material: Material;
@group(1) @binding(3) var layer_mask: texture_2d<f32>;
@group(1) @binding(4) var highlight_mask: texture_2d<f32>;
@group(1) @binding(5) var underlay_texture: texture_2d<f32>;
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) normal: vec3<f32>,
    @location(1) uv: vec2<f32>,
};
@vertex fn vs_main(@location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>, @location(3) indices: vec4<f32>, @location(4) weights: vec4<f32>) -> VertexOutput {
    var output: VertexOutput;
    var point = vec4<f32>(position, 1.0);
    var n = normal;
    if dot(weights, vec4<f32>(1.0)) > 0.0 {
        let skin = joints[u32(indices.x)] * weights.x + joints[u32(indices.y)] * weights.y + joints[u32(indices.z)] * weights.z + joints[u32(indices.w)] * weights.w;
        point = skin * point;
        n = (skin * vec4<f32>(normal, 0.0)).xyz;
    }
    output.position = camera.view_projection * point;
    output.normal = n;
    output.uv = uv;
    return output;
}
fn address(value: f32, mode: f32) -> f32 {
    if mode == 1.0 { return clamp(value, 0.0, 1.0); }
    if mode == 6.0 { return 1.0 - abs(fract(value * 0.5) * 2.0 - 1.0); }
    if mode == 7.0 { return clamp(abs(value), 0.0, 1.0); }
    return fract(value);
}
fn coordinates(uv: vec2<f32>, wrap: vec2<f32>, scale: vec2<f32>, size: vec2<u32>) -> vec2<f32> {
    // Apply the asset transform/addressing before converting to decoded top-origin rows.
    let mapped = vec2<f32>(address(uv.x, wrap.x), 1.0 - address(uv.y, wrap.y));
    let half_texel = vec2<f32>(0.5) / vec2<f32>(size);
    return clamp(mapped * scale, half_texel, scale - half_texel);
}
fn transform_uv(uv: vec2<f32>, transform: vec4<f32>) -> vec2<f32> {
    var offset = transform.zw;
    // Top-origin transforms must be conjugated with the source UV-to-texture flip.
    // Applying the flip after a scale instead would clamp an entire atlas tile.
    if material.highlight_scale.z > 0.0 { offset.y = 1.0 - transform.y - transform.w; }
    return fract(uv) * transform.xy + offset;
}
@fragment fn fs_main(input: VertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4<f32> {
    // Source tiles share a material texture; animated offsets apply within that tile.
    let uv = transform_uv(input.uv, material.uv);
    var base = textureSample(base_color, base_sampler, coordinates(uv, material.wrap.xy, material.texture_scale.xy, textureDimensions(base_color))) * material.color;
    let underlay_uv = transform_uv(input.uv, material.underlay_uv);
    let underlay = textureSample(underlay_texture, base_sampler, coordinates(underlay_uv, material.underlay_wrap.xy, material.underlay_scale.xy, textureDimensions(underlay_texture)));
    let alpha = base.a + underlay.a * (1.0 - base.a);
    base = vec4<f32>((base.rgb * base.a + underlay.rgb * underlay.a * (1.0 - base.a)) / max(alpha, 0.00001), alpha);
    let mask_uv = transform_uv(input.uv, material.mask_uv);
    let mask = textureSample(layer_mask, base_sampler, coordinates(mask_uv, material.wrap.zw, material.texture_scale.zw, textureDimensions(layer_mask))) * material.mask_channels;
    let highlight_uv = transform_uv(input.uv, material.highlight_uv);
    let highlight = textureSample(highlight_mask, base_sampler, coordinates(highlight_uv, material.wrap.xy, material.highlight_scale.xy, textureDimensions(highlight_mask)));
    var rgb = base.rgb;
    rgb = mix(rgb, base.rgb * material.layers[0].rgb, mask.r);
    rgb = mix(rgb, base.rgb * material.layers[1].rgb, mask.g);
    rgb = mix(rgb, base.rgb * material.layers[2].rgb, mask.b);
    rgb = mix(rgb, base.rgb * material.layers[3].rgb, mask.a);
    if base.a < 0.01 { discard; }
    let n = normalize(input.normal + vec3<f32>(0.00001));
    let light = 0.48 + 0.52 * max(dot(select(-n, n, front), normalize(vec3<f32>(-0.4, 0.8, 0.6))), 0.0);
    return vec4<f32>(rgb * light + highlight.r * material.highlight_color.rgb, base.a);
}
