// SPDX-License-Identifier: GPL-3.0-only
struct Camera { view_projection: mat4x4<f32>, eye: vec4<f32>, light: vec4<f32>, environment: vec4<f32> };
struct Material { color: vec4<f32>, layers: array<vec4<f32>, 4>, uv: vec4<f32>, wrap: vec4<f32>, texture_scale: vec4<f32>, mask_uv: vec4<f32>, highlight_color: vec4<f32>, highlight_uv: vec4<f32>, highlight_scale: vec4<f32>, underlay_uv: vec4<f32>, underlay_wrap: vec4<f32>, underlay_scale: vec4<f32>, mask_channels: vec4<f32>, surface: array<vec4<f32>, 41>, map_wrap: array<vec4<f32>, 12>, map_scale: array<vec4<f32>, 12>, restoration: array<vec4<f32>, 15>, flags: vec4<f32> };
@group(0) @binding(0) var<uniform> camera: Camera;
@group(0) @binding(1) var<storage, read> joints: array<mat4x4<f32>>;
@group(1) @binding(0) var base_color: texture_2d<f32>;
@group(1) @binding(1) var base_sampler: sampler;
@group(1) @binding(2) var<uniform> material: Material;
@group(1) @binding(3) var layer_mask: texture_2d<f32>;
@group(1) @binding(4) var highlight_mask: texture_2d<f32>;
@group(1) @binding(5) var underlay_texture: texture_2d<f32>;
@group(1) @binding(6) var normal_map: texture_2d<f32>;
@group(1) @binding(7) var roughness_map: texture_2d<f32>;
@group(1) @binding(8) var occlusion_map: texture_2d<f32>;
@group(1) @binding(9) var emission_map: texture_2d<f32>;
@group(1) @binding(10) var specular_map: texture_2d<f32>;
@group(1) @binding(11) var rim_map: texture_2d<f32>;
@group(1) @binding(12) var shadow_map: texture_2d<f32>;
@group(1) @binding(13) var detail_normal_map: texture_2d<f32>;
@group(1) @binding(14) var shadow_color_mask: texture_2d<f32>;
@group(1) @binding(15) var subsurface_mask: texture_2d<f32>;
@group(1) @binding(16) var eyelid_mask: texture_2d<f32>;
@group(1) @binding(17) var discard_mask: texture_2d<f32>;
@group(1) @binding(18) var repeat_sampler: sampler;
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) normal: vec3<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) world: vec3<f32>,
    @location(3) skin_x: vec3<f32>,
    @location(4) skin_y: vec3<f32>,
    @location(5) skin_z: vec3<f32>,
    @location(6) @interpolate(flat) selected: u32,
    @location(7) secondary_uv: vec2<f32>,
    @location(8) tangent: vec4<f32>,
};
// XYZ squared length and normalization order of the paired source frame stage.
fn source_unit(v: vec3<f32>) -> vec3<f32> {
    return v * inverseSqrt(fma(v.z, v.z, fma(v.y, v.y, v.x * v.x)));
}
fn source_frame(v: vec3<f32>, x: vec3<f32>, y: vec3<f32>, z: vec3<f32>, inverse_lengths: vec3<f32>) -> vec3<f32> {
    let corrected = fma(z * inverse_lengths.z, vec3<f32>(v.z), fma(x * inverse_lengths.x, vec3<f32>(v.x), (y * inverse_lengths.y) * v.y));
    let packed = vec3<f32>(vec3<i32>(source_unit(corrected) * 511.0)) * (1.0 / 511.0);
    // The preview world matrix is identity. Preserve the native X,Y,Z multiply/FMA boundary.
    let world = fma(vec3<f32>(0.0, 0.0, 1.0), vec3<f32>(packed.z), fma(vec3<f32>(0.0, 1.0, 0.0), vec3<f32>(packed.y), vec3<f32>(1.0, 0.0, 0.0) * packed.x));
    return source_unit(world);
}
fn vertex_data(position: vec3<f32>, normal: vec3<f32>, uv: vec2<f32>, indices: vec4<f32>, weights: vec4<f32>, secondary_uv: vec2<f32>, tangent0: vec4<f32>) -> VertexOutput {
    var output: VertexOutput;
    output.selected = 0u;
    output.tangent = tangent0;
    var point = vec4<f32>(position, 1.0);
    var n = normal;
    output.skin_x = vec3<f32>(1.0, 0.0, 0.0);
    output.skin_y = vec3<f32>(0.0, 1.0, 0.0);
    output.skin_z = vec3<f32>(0.0, 0.0, 1.0);
    if dot(weights, vec4<f32>(1.0)) > 0.0 {
        var skin = joints[u32(indices.x)] * weights.x + joints[u32(indices.y)] * weights.y + joints[u32(indices.z)] * weights.z + joints[u32(indices.w)] * weights.w;
        if material.flags.w == 15.0 {
            // Preserve the source skinning pass's four-influence accumulation order.
            for (var c = 0u; c < 4u; c++) {
                skin[c] = fma(joints[u32(indices.w)][c], vec4<f32>(weights.w), fma(joints[u32(indices.z)][c], vec4<f32>(weights.z), fma(joints[u32(indices.x)][c], vec4<f32>(weights.x), joints[u32(indices.y)][c] * weights.y)));
            }
        }
        point = skin * point;
        if material.flags.w == 15.0 {
            point = fma(skin[2], vec4<f32>(position.z), fma(skin[0], vec4<f32>(position.x), skin[1] * position.y)) + skin[3];
        }
        n = (skin * vec4<f32>(normal, 0.0)).xyz;
        output.skin_x = skin[0].xyz;
        output.skin_y = skin[1].xyz;
        output.skin_z = skin[2].xyz;
    }
    if material.flags.w == 15.0 {
        let x = output.skin_x; let y = output.skin_y; let z = output.skin_z;
        let inverse_lengths = vec3<f32>(1.0 / fma(x.z, x.z, fma(x.y, x.y, x.x * x.x)), 1.0 / fma(y.z, y.z, fma(y.y, y.y, y.x * y.x)), 1.0 / fma(z.z, z.z, fma(z.y, z.y, z.x * z.x)));
        n = source_frame(normal, x, y, z, inverse_lengths);
        output.tangent = vec4<f32>(source_frame(tangent0.xyz, x, y, z, inverse_lengths), tangent0.w);
    }
    output.position = camera.view_projection * point;
    output.normal = n;
    output.uv = uv;
    output.secondary_uv = secondary_uv;
    output.world = point.xyz;
    return output;
}
@vertex fn vs_main(@location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>, @location(3) indices: vec4<f32>, @location(4) weights: vec4<f32>, @location(5) secondary_uv: vec2<f32>, @location(6) tangent0: vec4<f32>) -> VertexOutput {
    return vertex_data(position, normal, uv, indices, weights, secondary_uv, tangent0);
}
@vertex fn vs_wire(@location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>, @location(3) indices: vec4<f32>, @location(4) weights: vec4<f32>, @location(5) secondary_uv: vec2<f32>, @location(6) tangent0: vec4<f32>, @builtin(instance_index) selected: u32) -> VertexOutput {
    var output = vertex_data(position, normal, uv, indices, weights, secondary_uv, tangent0);
    output.selected = selected;
    output.position.z -= output.position.w * .00005;
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
fn rotate_uv(uv: vec2<f32>, angle: f32, center: vec2<f32>) -> vec2<f32> {
    let p = uv - center;
    return vec2<f32>(cos(angle) * p.x - sin(angle) * p.y, sin(angle) * p.x + cos(angle) * p.y) + center;
}
fn restoration_eye_uv(uv: vec2<f32>, row: u32) -> vec2<f32> {
    let p = vec3<f32>(uv, 1.0);
    let result = vec2<f32>(dot(material.restoration[row].xyz, p), dot(material.restoration[row + 1u].xyz, p));
    let mirrored = vec2<f32>(abs(result.x), 1.0 - abs(1.0 - result.y));
    let addressed = mix(result, mirrored, vec2<f32>(material.restoration[row].w, material.restoration[row + 1u].w));
    // Eyelid matrices produce image coordinates; coordinates() expects model UVs.
    return vec2<f32>(addressed.x, 1.0 - addressed.y);
}
fn map_uv(uv: vec2<f32>, index: u32, size: vec2<u32>) -> vec2<f32> {
    return coordinates(uv, material.map_wrap[index].xy, material.map_scale[index].xy, size);
}
fn mapped_normal(sample: vec3<f32>, two_channel: f32, strength: f32) -> vec3<f32> {
    let xy = (sample.xy * 2.0 - 1.0) * strength;
    let z = select(sample.z * 2.0 - 1.0, sqrt(max(0.0001, 1.0 - dot(xy, xy))), two_channel > 0.0);
    return normalize(vec3<f32>(xy, z) + vec3<f32>(0.0, 0.0, 0.00001));
}
fn hue_rotate(rgb: vec3<f32>, degrees: f32) -> vec3<f32> {
    let axis = normalize(vec3<f32>(1.0)); let a = radians(degrees);
    return max(vec3<f32>(0.0), rgb * cos(a) + cross(axis, rgb) * sin(a) + axis * dot(axis, rgb) * (1.0 - cos(a)));
}
// Neutral outdoor approximation: a broad key, sky/ground fill and a soft edge highlight.
// Material colors and maps remain authored inputs; this does not bake lighting into textures.
fn game_fill(n: vec3<f32>) -> vec3<f32> {
    return mix(vec3<f32>(.30, .28, .25), vec3<f32>(.48, .53, .59), n.y * .5 + .5);
}
fn game_key(nl: f32) -> f32 { return .64 * smoothstep(-.18, .28, nl); }
fn game_rim(nv: f32, nl: f32) -> vec3<f32> {
    return vec3<f32>(.15, .18, .20) * pow(1.0 - nv, 3.5) * smoothstep(-.25, .6, nl);
}
fn game_highlight(value: vec3<f32>) -> vec3<f32> {
    let peak = max(max(value.r, value.g), value.b);
    return value / (1.0 + peak / .22);
}
@fragment fn fs_main(input: VertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4<f32> {
    if material.flags.w > 0.0 && material.flags.w < 3.0 {
        // Fire masks write only stencil coverage. The linked core uses its own
        // color textures; the mask mesh must never become an opaque white shell.
        let first_uv = transform_uv(input.uv, material.uv);
        let second_uv = transform_uv(input.uv, material.mask_uv);
        let control = textureSample(base_color, base_sampler, coordinates(first_uv, material.wrap.xy, material.texture_scale.xy, textureDimensions(base_color)));
        let first = control.r;
        let second = textureSample(layer_mask, base_sampler, coordinates(second_uv, material.wrap.zw, material.texture_scale.zw, textureDimensions(layer_mask))).r;
        if material.flags.w == 2.0 {
            if first + second < material.surface[7].y { discard; }
            return vec4<f32>(0.0);
        }
        let third_uv = transform_uv(input.uv, material.underlay_uv);
        let third = textureSample(underlay_texture, base_sampler, coordinates(third_uv, material.underlay_wrap.xy, material.underlay_scale.xy, textureDimensions(underlay_texture))).r;
        let layer = clamp(first + (1.0 - control.a) * (second + third), 0.0, 1.0);
        let color_lerp = clamp(material.surface[6].x, 0.0, 1.0);
        var n = normalize(input.normal + vec3<f32>(0.00001));
        n = select(-n, n, front);
        let v = normalize(camera.eye.xyz - input.world + vec3<f32>(0.00001));
        let rim = pow(1.0 - abs(dot(n, v)), max(material.surface[8].x, .01)) * material.surface[8].y;
        let outer = mix(material.surface[0].rgb, material.surface[1].rgb, color_lerp) * (1.0 + rim * material.surface[7].z);
        let inner = mix(material.surface[2].rgb, material.surface[3].rgb, color_lerp) * (1.0 + rim * material.surface[7].w);
        let albedo = mix(outer, inner, layer) * mix(vec3<f32>(1.0), material.surface[5].rgb, clamp(material.surface[6].y, 0.0, 1.0)) * material.surface[4].rgb * material.surface[6].z;
        let to_light = camera.light.xyz - input.world;
        let d2 = max(dot(to_light, to_light), .000001);
        let in_game = camera.environment.x > 0.0;
        let l = select(to_light * inverseSqrt(d2), normalize(vec3<f32>(-.5, .75, .45)), in_game);
        let illumination = select(vec3<f32>(max(dot(n, l), 0.0) * camera.light.w / d2), game_fill(n) + vec3<f32>(game_key(dot(n, l))), in_game);
        let gain = max(material.surface[6].w, 0.0);
        var rgb = albedo * (illumination * (1.0 - clamp(gain, 0.0, 1.0)) + vec3<f32>(gain));
        switch u32(camera.eye.w) {
            case 1u: { rgb = albedo; }
            case 2u: { rgb = n * .5 + .5; }
            case 3u, 5u: { rgb = vec3<f32>(1.0); }
            case 4u: { rgb = vec3<f32>(0.0); }
            case 6u: { rgb = vec3<f32>(layer); }
            default: {}
        }
        let alpha = clamp(material.surface[7].x, 0.0, 1.0);
        if alpha < .001 { discard; }
        return vec4<f32>(rgb, alpha);
    }
    // Source tiles share a material texture; animated offsets apply within that tile.
    var uv = rotate_uv(transform_uv(input.uv, material.uv), material.surface[10].x, material.surface[36].xy);
    if material.flags.w == 7.0 || material.flags.w == 8.0 || material.flags.w == 10.0 || material.flags.w == 11.0 {
        let origin = vec2<f32>(material.restoration[2].w, material.restoration[3].w);
        uv = input.uv * material.uv.xy + material.uv.zw - origin * material.uv.xy;
    }
    var restoration_glow = vec3<f32>(0.0);
    var lava_detail = 0.0;
    if material.flags.w == 6.0 || material.flags.w == 7.0 {
        let detail_uv = input.uv * material.restoration[1].xy + material.restoration[1].zw;
        let flipped_detail_uv = vec2<f32>(detail_uv.x, 1.0 - detail_uv.y);
        lava_detail = textureSample(roughness_map, base_sampler, map_uv(flipped_detail_uv, 1u, textureDimensions(roughness_map))).r;
        if material.flags.w == 6.0 { uv += vec2<f32>((lava_detail - .5) * material.restoration[0].y); }
        if material.flags.w == 7.0 {
            let mirrored = vec2<f32>(abs(uv.x), 1.0 - abs(1.0 - uv.y));
            uv = mix(uv, mirrored, material.restoration[0].z);
        }
    }
    var base = textureSample(base_color, base_sampler, coordinates(uv, material.wrap.xy, material.texture_scale.xy, textureDimensions(base_color))) * material.color;
    if material.flags.w == 5.0 {
        var refracted = uv;
        let first_normal = textureSample(detail_normal_map, base_sampler, map_uv(refracted, 7u, textureDimensions(detail_normal_map))).rg * 2.0 - 1.0;
        refracted += first_normal * vec2<f32>(-.35, .35);
        let second_normal = textureSample(roughness_map, base_sampler, map_uv(refracted, 1u, textureDimensions(roughness_map))).rg * 2.0 - 1.0;
        refracted += second_normal * vec2<f32>(-.35, .35);
        if material.restoration[0].z > 0.0 {
            refracted = vec2<f32>(abs(refracted.x), 1.0 - abs(1.0 - refracted.y));
        }
        let secondary = textureSample(layer_mask, base_sampler, coordinates(refracted, material.wrap.zw, material.texture_scale.zw, textureDimensions(layer_mask))).rgb * material.surface[15].rgb;
        let view = normalize(camera.eye.xyz - input.world + vec3<f32>(.00001));
        let incidence = max(dot(view, normalize(input.normal + vec3<f32>(.00001))), 0.0);
        let opacity = .8 - .8 * sqrt(max(1.0 - .999999 * incidence, 0.0));
        restoration_glow = base.rgb * material.surface[14].rgb * material.surface[13].x + secondary * material.surface[13].y * opacity;
        base = vec4<f32>(mix(base.rgb, secondary, opacity), base.a);
    }
    if material.flags.w == 6.0 {
        base = vec4<f32>(base.rgb * material.restoration[0].w * mix(vec3<f32>(1.0), material.restoration[3].rgb, lava_detail), base.a);
        restoration_glow = base.rgb * material.restoration[0].z + lava_detail * material.restoration[0].x * material.restoration[2].rgb;
        base = vec4<f32>(base.rgb * (1.0 - material.restoration[0].z), base.a);
    }
    if material.flags.w == 7.0 || material.flags.w == 8.0 || material.flags.w == 10.0 || material.flags.w == 11.0 {
        let upper_uv = restoration_eye_uv(input.uv, 4u);
        let lower_uv = restoration_eye_uv(input.uv, 6u);
        let shadow_uv = restoration_eye_uv(input.uv, 8u);
        let upper = textureSample(detail_normal_map, base_sampler, map_uv(upper_uv, 7u, textureDimensions(detail_normal_map)));
        let lower = textureSample(specular_map, base_sampler, map_uv(lower_uv, 4u, textureDimensions(specular_map)));
        let shadow = textureSample(eyelid_mask, base_sampler, map_uv(shadow_uv, 10u, textureDimensions(eyelid_mask))).rgb;
        let coverage = (1.0 - upper.a) * (1.0 - lower.a);
        if material.flags.w == 10.0 {
            var emission_uv = uv;
            if material.restoration[0].z > 0.0 {
                emission_uv = vec2<f32>(abs(uv.x), 1.0 - abs(1.0 - uv.y));
            }
            let mask = textureSample(roughness_map, base_sampler, map_uv(emission_uv, 1u, textureDimensions(roughness_map)));
            // Match the native eye program's ordered layer compositing.
            for (var layer = 0u; layer < 4u; layer++) {
                restoration_glow = mix(restoration_glow, material.restoration[10u + layer].rgb * material.restoration[14u][layer], mask[layer]);
            }
            restoration_glow *= shadow * coverage;
        }
        var iris = base.rgb;
        if material.flags.w == 11.0 {
            let tint_mask = textureSample(roughness_map, base_sampler, map_uv(uv, 1u, textureDimensions(roughness_map)));
            var tint = vec3<f32>(1.0);
            for (var layer = 0u; layer < 4u; layer++) {
                tint = mix(tint, material.restoration[10u + layer].rgb, tint_mask[layer]);
                let glow_color = select(vec3<f32>(0.0, 0.401975, 1.0000241), vec3<f32>(1.0000241), (layer & 1u) != 0u);
                restoration_glow = mix(restoration_glow, glow_color * material.restoration[14u][layer], tint_mask[layer]);
            }
            iris *= tint;
            restoration_glow *= shadow * coverage;
        }
        if material.flags.w == 8.0 && material.restoration[0].y > 0.0 {
            var highlight_uv = input.uv * material.restoration[1].xy + material.restoration[1].zw;
            if material.restoration[0].z > 0.0 { highlight_uv = abs(highlight_uv); }
            let mask = textureSample(roughness_map, base_sampler, map_uv(highlight_uv, 1u, textureDimensions(roughness_map))).r;
            iris += mask * material.restoration[2].rgb;
        }
        let eye_color = mix(mix(iris * shadow, upper.rgb, upper.a), lower.rgb, lower.a);
        base = vec4<f32>(eye_color * mix(vec3<f32>(1.0), material.restoration[3].rgb, lava_detail), base.a);
        if material.flags.w == 7.0 {
            restoration_glow = (textureSample(emission_map, base_sampler, map_uv(uv, 3u, textureDimensions(emission_map))).rgb * 4.0 + lava_detail * material.restoration[0].x * material.restoration[2].rgb) * coverage;
        }
    }
    let underlay_uv = transform_uv(input.uv, material.underlay_uv);
    var underlay = textureSample(underlay_texture, base_sampler, coordinates(underlay_uv, material.underlay_wrap.xy, material.underlay_scale.xy, textureDimensions(underlay_texture)));
    if material.flags.x > 0.0 {
        underlay = vec4<f32>(underlay.rgb * material.surface[32].rgb + material.surface[33].rgb, underlay.a * material.surface[34].y);
        base.a *= material.surface[34].x;
    }
    if material.flags.w == 12.0 { underlay = vec4<f32>(0.0); }
    let alpha = base.a + underlay.a * (1.0 - base.a);
    base = vec4<f32>((base.rgb * base.a + underlay.rgb * underlay.a * (1.0 - base.a)) / max(alpha, 0.00001), alpha);
    var mask_uv = transform_uv(input.uv, material.mask_uv);
    if material.flags.w == 6.0 { mask_uv = uv; }
    if material.flags.w == 4.0 {
        mask_uv = fract(transform_uv(input.secondary_uv, material.mask_uv));
    }
    var mask = textureSample(layer_mask, base_sampler, coordinates(mask_uv, material.wrap.zw, material.texture_scale.zw, textureDimensions(layer_mask))) * material.mask_channels;
    let highlight_uv = transform_uv(input.uv, material.highlight_uv);
    let highlight = textureSample(highlight_mask, base_sampler, coordinates(highlight_uv, material.underlay_scale.zw, material.highlight_scale.xy, textureDimensions(highlight_mask)));
    var rgb = base.rgb;
    rgb = mix(rgb, base.rgb * material.layers[0].rgb, mask.r);
    rgb = mix(rgb, base.rgb * material.layers[1].rgb, mask.g);
    rgb = mix(rgb, base.rgb * material.layers[2].rgb, mask.b);
    rgb = mix(rgb, base.rgb * material.layers[3].rgb, mask.a);
    if material.flags.w == 12.0 {
        let uncovered = clamp(1.0 - dot(mask, vec4<f32>(1.0)), 0.0, 1.0);
        var coverage = uncovered;
        rgb = base.rgb * uncovered * max(1.0 - material.restoration[0].x, 0.0);
        restoration_glow = material.restoration[1].rgb * material.restoration[0].x * uncovered;
        for (var i = 0u; i < 4u; i++) {
            rgb = mix(rgb, base.rgb * material.layers[i].rgb * max(1.0 - material.surface[13][i], 0.0), mask[i]);
            restoration_glow = mix(restoration_glow, material.surface[14u + i].rgb * material.surface[13][i], mask[i]);
            coverage = mix(coverage, 1.0, mask[i]);
        }
        rgb /= max(coverage, 0.00001);
        restoration_glow /= max(coverage, 0.00001);
    }
    if material.flags.w == 13.0 { restoration_glow = material.surface[14].rgb * material.surface[13].x; }
    var n = normalize(input.normal + vec3<f32>(0.00001));
    let dp_x = dpdx(input.world); let dp_y = dpdy(input.world);
    let normal_uv = transform_uv(input.uv, material.surface[9]);
    let duv_x = dpdx(normal_uv); let duv_y = dpdy(normal_uv);
    let tangent = dp_x * duv_y.y - dp_y * duv_x.y;
    let bitangent = dp_y * duv_x.x - dp_x * duv_y.x;
    let inverse_scale = inverseSqrt(max(max(dot(tangent, tangent), dot(bitangent, bitangent)), 1e-20));
    let normal_texel = textureSample(normal_map, base_sampler, map_uv(normal_uv, 0u, textureDimensions(normal_map)));
    let detail_texel = textureSample(detail_normal_map, base_sampler, map_uv(highlight_uv, 7u, textureDimensions(detail_normal_map)));
    let rough_texel = select(textureSample(roughness_map, base_sampler, map_uv(uv, 1u, textureDimensions(roughness_map))), vec4<f32>(1.0), material.flags.w >= 5.0);
    let occlusion_texel = textureSample(occlusion_map, base_sampler, map_uv(uv, 2u, textureDimensions(occlusion_map)));
    let emission = textureSample(emission_map, base_sampler, map_uv(uv, 3u, textureDimensions(emission_map))).rgb;
    let specular_mask = select(textureSample(specular_map, base_sampler, map_uv(uv, 4u, textureDimensions(specular_map))).r, 1.0, material.flags.w == 7.0 || material.flags.w == 8.0 || material.flags.w == 10.0 || material.flags.w == 11.0);
    let rim_mask = textureSample(rim_map, base_sampler, map_uv(uv, 5u, textureDimensions(rim_map))).r;
    let shadow_tint = textureSample(shadow_map, base_sampler, map_uv(uv, 6u, textureDimensions(shadow_map))).rgb;
    let shadow_weight = textureSample(shadow_color_mask, base_sampler, map_uv(uv, 8u, textureDimensions(shadow_color_mask))).r;
    let subsurface_weight = textureSample(subsurface_mask, base_sampler, map_uv(uv, 9u, textureDimensions(subsurface_mask))).r;
    let eyelid_uv = rotate_uv(transform_uv(input.uv, material.surface[38]), material.surface[10].y, material.surface[37].xy);
    let eyelid = select(textureSample(eyelid_mask, base_sampler, map_uv(eyelid_uv, 10u, textureDimensions(eyelid_mask))).r, 0.0, material.flags.w == 7.0 || material.flags.w == 8.0 || material.flags.w == 10.0 || material.flags.w == 11.0);
    let cutout = textureSample(discard_mask, base_sampler, map_uv(uv, 11u, textureDimensions(discard_mask)));
    let to_light = camera.light.xyz - input.world;
    let light_distance_squared = max(dot(to_light, to_light), 0.000001);
    let in_game = camera.environment.x > 0.0;
    let l = select(to_light * inverseSqrt(light_distance_squared), normalize(vec3<f32>(-.5, .75, .45)), in_game);
    let light_energy = select(camera.light.w / light_distance_squared, 1.0, in_game);
    if material.flags.x > 0.0 {
        if material.map_scale[0].z > 0.0 {
            var mapped = mapped_normal(normal_texel.rgb, material.map_scale[0].w, material.surface[0].z);
            if material.map_scale[7].z > 0.0 && material.flags.w < 5.0 {
                let detail = mapped_normal(detail_texel.rgb, material.map_scale[7].w, material.surface[10].z);
                mapped = normalize(vec3<f32>(mapped.xy + detail.xy, mapped.z * detail.z));
            }
            n = normalize(tangent * inverse_scale * mapped.x + bitangent * inverse_scale * mapped.y + n * mapped.z);
            if material.surface[39].w > 0.0 && material.map_scale[0].w == 0.0 {
                n = normalize(mat3x3<f32>(input.skin_x, input.skin_y, input.skin_z) * (normal_texel.rgb * 2.0 - 1.0) + vec3<f32>(0.00001));
            }
        }
        n = select(-n, n, front);
        let v = normalize(camera.eye.xyz - input.world + vec3<f32>(0.00001));
        let h = normalize(l + v);
        let nl = dot(n, l); let nv = max(dot(n, v), 0.0);
        var roughness = material.surface[1].x * rough_texel.r;
        var metallic = material.surface[0].w;
        var ao = occlusion_texel.r;
        var specular = material.surface[2].xyz;
        var emission_color = material.surface[5].rgb * material.surface[1].z * select(vec3<f32>(1.0), emission, material.map_scale[3].z > 0.0);
        if material.flags.w >= 5.0 { emission_color = restoration_glow; }
        let shadow_map_color = mix(shadow_tint, vec3<f32>(1.0), clamp((1.0 - shadow_weight) * material.surface[25].w, 0.0, 1.0));
        var shadow_color = material.surface[6].rgb * shadow_map_color;
        for (var i = 0u; i < 4u; i++) {
            let weight = select(clamp(mask[i], 0.0, 1.0), 0.0, material.flags.w == 3.0 || material.flags.w >= 5.0);
            roughness = mix(roughness, material.surface[12][i] * rough_texel.r, weight);
            metallic = mix(metallic, material.surface[11][i], weight);
            specular = mix(specular, vec3<f32>(material.surface[22][i], material.surface[23][i], material.surface[24][i]), weight);
            emission_color += material.surface[14u + i].rgb * material.surface[13][i] * weight;
            shadow_color = mix(shadow_color, material.surface[18u + i].rgb * shadow_map_color, weight);
        }
        let saturation = max(material.surface[0].x, 0.0);
        rgb = max(mix(vec3<f32>(dot(rgb, vec3<f32>(0.2126, 0.7152, 0.0722))), rgb, saturation), vec3<f32>(0.0)) * max(1.0 - material.surface[0].y, 0.0);
        let mid = clamp((1.0 - abs(nl) + material.surface[26].x) * (1.0 + material.surface[26].y), 0.0, 1.0);
        let dark = clamp((-nl + material.surface[27].x) * (1.0 + material.surface[27].y), 0.0, 1.0);
        let hue_weight = clamp(material.surface[26].w * material.surface[25].z * 2.0, 0.0, 1.0);
        rgb = mix(rgb, hue_rotate(rgb, material.surface[26].z), mid * hue_weight);
        rgb = mix(rgb, hue_rotate(rgb, material.surface[27].z), dark * hue_weight);
        let shade = clamp((nl + material.surface[3].z + material.surface[25].x) * material.surface[3].y * material.surface[27].w * (1.0 + material.surface[25].y), 0.0, 1.0);
        let occlusion = clamp(1.0 - (1.0 - ao) * material.surface[1].y, 0.0, 1.0);
        let direct = select(max(nl, 0.0) * light_energy, game_key(nl), in_game);
        let fill = select(vec3<f32>(0.0), game_fill(n), in_game);
        let illumination = fill + vec3<f32>(direct);
        let light = mix(vec3<f32>(1.0), mix(shadow_color, vec3<f32>(1.0), shade), clamp(material.surface[3].x, 0.0, 1.0)) * illumination * occlusion * max(material.surface[31].z, 0.0);
        let exponent = clamp((2.0 / max(roughness * roughness, 0.0001) - 2.0) * max(material.surface[2].w / 32.0, .01), 1.0, 2048.0);
        let shine = pow(clamp(dot(n, h) + specular.y, 0.0, 1.0), exponent * max(1.0 + specular.z, 0.01));
        let f0 = mix(material.surface[7].rgb * max(material.surface[10].w, 0.0), rgb, clamp(metallic, 0.0, 1.0));
        let reflection = (f0 + (vec3<f32>(1.0) - f0) * pow(1.0 - nv, 5.0)) * shine * max(specular.x, 0.0) * specular_mask;
        let rim = pow(clamp(1.0 - nv + material.surface[4].y, 0.0, 1.0), max(material.surface[4].z, 0.01));
        let rim_color = mix(material.surface[35].rgb, material.surface[8].rgb, shade) * rim_mask * (rim * (material.surface[4].x + material.surface[4].w * max(-nl, 0.0)) + pow(1.0 - nv, max(material.surface[34].z, .01)) * material.surface[34].w);
        let coat = mix(vec3<f32>(.04), material.surface[28].rgb, clamp(material.surface[29].x, 0.0, 1.0)) * pow(max(dot(n, h), 0.0), clamp(2.0 / max(material.surface[29].y * material.surface[29].y, .0001), 1.0, 2048.0)) * material.surface[28].a;
        let highlight_reflection = highlight.r * mix(vec3<f32>(.04), rgb, clamp(material.surface[29].z, 0.0, 1.0)) * pow(max(dot(n, h), 0.0), clamp(2.0 / max(material.surface[29].w * material.surface[29].w, .0001), 1.0, 2048.0));
        let subsurface_tint = select(vec3<f32>(1.0), rgb, material.flags.z > 0.0);
        let subsurface = subsurface_tint * material.surface[30].rgb * max(-nl, 0.0) * light_energy * clamp(subsurface_weight * material.surface[31].x + material.surface[31].y, 0.0, 1.0);
        let eyelid_color = mix(vec3<f32>(1.0), material.surface[39].rgb, clamp(eyelid * material.map_scale[10].z, 0.0, 1.0));
        let accents = (reflection + rim_color + coat + highlight_reflection) * direct;
        let shaded = rgb * light * eyelid_color + select(accents, game_highlight(accents), in_game) + subsurface;
        let lit = mix(rgb * illumination, shaded, clamp(material.surface[31].w, 0.0, 1.0)) + select(vec3<f32>(0.0), game_rim(nv, nl) * occlusion, in_game);
        let albedo = rgb;
        rgb = lit + emission_color + highlight.r * material.highlight_color.rgb;
        if material.surface[39].w > 0.0 && material.flags.w < 5.0 {
            let emission_weight = emission * max(material.surface[1].z * material.surface[5].w, 0.0);
            rgb = lit * (vec3<f32>(1.0) - clamp(emission_weight, vec3<f32>(0.0), vec3<f32>(1.0))) + albedo * emission_weight;
        }
        if material.flags.w == 3.0 {
            var remaining = 1.0;
            for (var layer = 4u; layer > 0u; layer--) {
                let i = layer - 1u;
                let weight = clamp(mask[i], 0.0, 1.0);
                rgb += (albedo + material.surface[14u + i].rgb) * material.surface[13][i] * weight * remaining;
                remaining *= 1.0 - weight;
            }
        }
        if material.flags.w == 6.0 {
            var remaining = 1.0;
            for (var layer = 4u; layer > 0u; layer--) {
                let i = layer - 1u;
                let weight = clamp(mask[i], 0.0, 1.0);
                rgb += material.surface[14u + i].rgb * material.surface[13][i] * weight * remaining;
                remaining *= 1.0 - weight;
            }
        }
        if base.a < max(.001, material.surface[1].w) || any(cutout < material.surface[40]) { discard; }
        var inspected = rgb;
        switch u32(camera.eye.w) {
            case 1u: { inspected = albedo; }
            case 2u: { inspected = n * .5 + .5; }
            case 3u: { inspected = vec3<f32>(roughness); }
            case 4u: { inspected = vec3<f32>(metallic); }
            case 5u: { inspected = vec3<f32>(occlusion); }
            case 6u: { inspected = mask.rgb; }
            default: {}
        }
        return vec4<f32>(inspected, base.a);
    }
    if base.a < 0.01 { discard; }
    n = select(-n, n, front);
    let nl = dot(n, l);
    let nv = clamp(dot(n, normalize(camera.eye.xyz - input.world)), 0.0, 1.0);
    let light = select(vec3<f32>(light_energy * max(nl, 0.0)), game_fill(n) + vec3<f32>(game_key(nl)), in_game);
    var inspected = rgb * light + highlight.r * material.highlight_color.rgb + select(vec3<f32>(0.0), game_rim(nv, nl), in_game);
    switch u32(camera.eye.w) {
        case 1u: { inspected = rgb; }
        case 2u: { inspected = n*.5 + .5; }
        case 3u: { inspected = rough_texel.rgb; }
        case 4u: { inspected = vec3<f32>(0.0); }
        case 5u: { inspected = occlusion_texel.rgb; }
        case 6u: { inspected = mask.rgb; }
        default: {}
    }
    return vec4<f32>(inspected, base.a);
}
@fragment fn fs_wire(input: VertexOutput) -> @location(0) vec4<f32> {
    // Linear values for soft blue #78A9D4 and selected amber #E2AD68.
    return vec4<f32>(select(vec3<f32>(.188, .397, .658), vec3<f32>(.761, .418, .138), input.selected != 0u), 1.0);
}
