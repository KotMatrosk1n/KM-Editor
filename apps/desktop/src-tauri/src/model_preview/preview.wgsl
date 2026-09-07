// SPDX-License-Identifier: GPL-3.0-only
struct Camera { view_projection: mat4x4<f32>, eye: vec4<f32>, light: vec4<f32> };
struct Material { color: vec4<f32>, layers: array<vec4<f32>, 4>, uv: vec4<f32>, wrap: vec4<f32>, texture_scale: vec4<f32>, mask_uv: vec4<f32>, highlight_color: vec4<f32>, highlight_uv: vec4<f32>, highlight_scale: vec4<f32>, underlay_uv: vec4<f32>, underlay_wrap: vec4<f32>, underlay_scale: vec4<f32>, mask_channels: vec4<f32>, surface: array<vec4<f32>, 41>, map_wrap: array<vec4<f32>, 12>, map_scale: array<vec4<f32>, 12>, flags: vec4<f32> };
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
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) normal: vec3<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) world: vec3<f32>,
    @location(3) skin_x: vec3<f32>,
    @location(4) skin_y: vec3<f32>,
    @location(5) skin_z: vec3<f32>,
};
fn vertex_data(position: vec3<f32>, normal: vec3<f32>, uv: vec2<f32>, indices: vec4<f32>, weights: vec4<f32>) -> VertexOutput {
    var output: VertexOutput;
    var point = vec4<f32>(position, 1.0);
    var n = normal;
    output.skin_x = vec3<f32>(1.0, 0.0, 0.0);
    output.skin_y = vec3<f32>(0.0, 1.0, 0.0);
    output.skin_z = vec3<f32>(0.0, 0.0, 1.0);
    if dot(weights, vec4<f32>(1.0)) > 0.0 {
        let skin = joints[u32(indices.x)] * weights.x + joints[u32(indices.y)] * weights.y + joints[u32(indices.z)] * weights.z + joints[u32(indices.w)] * weights.w;
        point = skin * point;
        n = (skin * vec4<f32>(normal, 0.0)).xyz;
        output.skin_x = skin[0].xyz;
        output.skin_y = skin[1].xyz;
        output.skin_z = skin[2].xyz;
    }
    output.position = camera.view_projection * point;
    output.normal = n;
    output.uv = uv;
    output.world = point.xyz;
    return output;
}
@vertex fn vs_main(@location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>, @location(3) indices: vec4<f32>, @location(4) weights: vec4<f32>) -> VertexOutput {
    return vertex_data(position, normal, uv, indices, weights);
}
@vertex fn vs_wire(@location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>, @location(3) indices: vec4<f32>, @location(4) weights: vec4<f32>) -> VertexOutput {
    var output = vertex_data(position, normal, uv, indices, weights);
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
@fragment fn fs_main(input: VertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4<f32> {
    // Source tiles share a material texture; animated offsets apply within that tile.
    let uv = rotate_uv(transform_uv(input.uv, material.uv), material.surface[10].x, material.surface[36].xy);
    var base = textureSample(base_color, base_sampler, coordinates(uv, material.wrap.xy, material.texture_scale.xy, textureDimensions(base_color))) * material.color;
    let underlay_uv = transform_uv(input.uv, material.underlay_uv);
    var underlay = textureSample(underlay_texture, base_sampler, coordinates(underlay_uv, material.underlay_wrap.xy, material.underlay_scale.xy, textureDimensions(underlay_texture)));
    if material.flags.x > 0.0 {
        underlay = vec4<f32>(underlay.rgb * material.surface[32].rgb + material.surface[33].rgb, underlay.a * material.surface[34].y);
        base.a *= material.surface[34].x;
    }
    let alpha = base.a + underlay.a * (1.0 - base.a);
    base = vec4<f32>((base.rgb * base.a + underlay.rgb * underlay.a * (1.0 - base.a)) / max(alpha, 0.00001), alpha);
    let mask_uv = transform_uv(input.uv, material.mask_uv);
    let mask = textureSample(layer_mask, base_sampler, coordinates(mask_uv, material.wrap.zw, material.texture_scale.zw, textureDimensions(layer_mask))) * material.mask_channels;
    let highlight_uv = transform_uv(input.uv, material.highlight_uv);
    let highlight = textureSample(highlight_mask, base_sampler, coordinates(highlight_uv, material.underlay_scale.zw, material.highlight_scale.xy, textureDimensions(highlight_mask)));
    var rgb = base.rgb;
    rgb = mix(rgb, base.rgb * material.layers[0].rgb, mask.r);
    rgb = mix(rgb, base.rgb * material.layers[1].rgb, mask.g);
    rgb = mix(rgb, base.rgb * material.layers[2].rgb, mask.b);
    rgb = mix(rgb, base.rgb * material.layers[3].rgb, mask.a);
    var n = normalize(input.normal + vec3<f32>(0.00001));
    let dp_x = dpdx(input.world); let dp_y = dpdy(input.world);
    let normal_uv = transform_uv(input.uv, material.surface[9]);
    let duv_x = dpdx(normal_uv); let duv_y = dpdy(normal_uv);
    let tangent = dp_x * duv_y.y - dp_y * duv_x.y;
    let bitangent = dp_y * duv_x.x - dp_x * duv_y.x;
    let inverse_scale = inverseSqrt(max(max(dot(tangent, tangent), dot(bitangent, bitangent)), 1e-20));
    let normal_texel = textureSample(normal_map, base_sampler, map_uv(normal_uv, 0u, textureDimensions(normal_map)));
    let detail_texel = textureSample(detail_normal_map, base_sampler, map_uv(highlight_uv, 7u, textureDimensions(detail_normal_map)));
    let rough_texel = textureSample(roughness_map, base_sampler, map_uv(uv, 1u, textureDimensions(roughness_map)));
    let occlusion_texel = textureSample(occlusion_map, base_sampler, map_uv(uv, 2u, textureDimensions(occlusion_map)));
    let emission = textureSample(emission_map, base_sampler, map_uv(uv, 3u, textureDimensions(emission_map))).rgb;
    let specular_mask = textureSample(specular_map, base_sampler, map_uv(uv, 4u, textureDimensions(specular_map))).r;
    let rim_mask = textureSample(rim_map, base_sampler, map_uv(uv, 5u, textureDimensions(rim_map))).r;
    let shadow_tint = textureSample(shadow_map, base_sampler, map_uv(uv, 6u, textureDimensions(shadow_map))).rgb;
    let shadow_weight = textureSample(shadow_color_mask, base_sampler, map_uv(uv, 8u, textureDimensions(shadow_color_mask))).r;
    let subsurface_weight = textureSample(subsurface_mask, base_sampler, map_uv(uv, 9u, textureDimensions(subsurface_mask))).r;
    let eyelid_uv = rotate_uv(transform_uv(input.uv, material.surface[38]), material.surface[10].y, material.surface[37].xy);
    let eyelid = textureSample(eyelid_mask, base_sampler, map_uv(eyelid_uv, 10u, textureDimensions(eyelid_mask))).r;
    let cutout = textureSample(discard_mask, base_sampler, map_uv(uv, 11u, textureDimensions(discard_mask)));
    let to_light = camera.light.xyz - input.world;
    let light_distance_squared = max(dot(to_light, to_light), 0.000001);
    let l = to_light * inverseSqrt(light_distance_squared);
    let light_energy = camera.light.w / light_distance_squared;
    if material.flags.x > 0.0 {
        if material.map_scale[0].z > 0.0 {
            var mapped = mapped_normal(normal_texel.rgb, material.map_scale[0].w, material.surface[0].z);
            if material.map_scale[7].z > 0.0 {
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
        let shadow_map_color = mix(shadow_tint, vec3<f32>(1.0), clamp((1.0 - shadow_weight) * material.surface[25].w, 0.0, 1.0));
        var shadow_color = material.surface[6].rgb * shadow_map_color;
        for (var i = 0u; i < 4u; i++) {
            let weight = clamp(mask[i], 0.0, 1.0);
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
        let direct = max(nl, 0.0) * light_energy;
        let light = mix(vec3<f32>(1.0), mix(shadow_color, vec3<f32>(1.0), shade), clamp(material.surface[3].x, 0.0, 1.0)) * direct * occlusion * max(material.surface[31].z, 0.0);
        let exponent = clamp((2.0 / max(roughness * roughness, 0.0001) - 2.0) * max(material.surface[2].w / 32.0, .01), 1.0, 2048.0);
        let shine = pow(clamp(dot(n, h) + specular.y, 0.0, 1.0), exponent * max(1.0 + specular.z, 0.01));
        let f0 = mix(material.surface[7].rgb * max(material.surface[10].w, 0.0), rgb, clamp(metallic, 0.0, 1.0));
        let reflection = (f0 + (vec3<f32>(1.0) - f0) * pow(1.0 - nv, 5.0)) * shine * max(specular.x, 0.0) * specular_mask;
        let rim = pow(clamp(1.0 - nv + material.surface[4].y, 0.0, 1.0), max(material.surface[4].z, 0.01));
        let rim_color = mix(material.surface[35].rgb, material.surface[8].rgb, shade) * rim_mask * (rim * (material.surface[4].x + material.surface[4].w * max(-nl, 0.0)) + pow(1.0 - nv, max(material.surface[34].z, .01)) * material.surface[34].w);
        let coat = mix(vec3<f32>(.04), material.surface[28].rgb, clamp(material.surface[29].x, 0.0, 1.0)) * pow(max(dot(n, h), 0.0), clamp(2.0 / max(material.surface[29].y * material.surface[29].y, .0001), 1.0, 2048.0)) * material.surface[28].a;
        let highlight_reflection = highlight.r * mix(vec3<f32>(.04), rgb, clamp(material.surface[29].z, 0.0, 1.0)) * pow(max(dot(n, h), 0.0), clamp(2.0 / max(material.surface[29].w * material.surface[29].w, .0001), 1.0, 2048.0));
        let subsurface = material.surface[30].rgb * max(-nl, 0.0) * light_energy * clamp(subsurface_weight * material.surface[31].x + material.surface[31].y, 0.0, 1.0);
        let eyelid_color = mix(vec3<f32>(1.0), material.surface[39].rgb, clamp(eyelid * material.map_scale[10].z, 0.0, 1.0));
        let shaded = rgb * light * eyelid_color + (reflection + rim_color + coat + highlight_reflection) * direct + subsurface;
        let lit = mix(rgb * direct, shaded, clamp(material.surface[31].w, 0.0, 1.0));
        let albedo = rgb;
        rgb = lit + emission_color + highlight.r * material.highlight_color.rgb;
        if material.surface[39].w > 0.0 {
            let emission_weight = emission * max(material.surface[1].z, 0.0);
            rgb = lit * (vec3<f32>(1.0) - clamp(emission_weight, vec3<f32>(0.0), vec3<f32>(1.0))) + albedo * emission_weight;
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
    let light = light_energy * max(dot(select(-n, n, front), l), 0.0);
    var inspected = rgb * light + highlight.r * material.highlight_color.rgb;
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
@fragment fn fs_wire() -> @location(0) vec4<f32> { return vec4<f32>(1.0, .68, .12, 1.0); }
