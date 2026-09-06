// SPDX-License-Identifier: GPL-3.0-only
struct Background {
    inverse_view_projection: mat4x4<f32>,
    color: vec4<f32>,
    padding: vec4<f32>,
}
@group(0) @binding(0) var<uniform> background: Background;
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) ndc: vec2<f32>,
}
@vertex fn vs_main(@builtin(vertex_index) index: u32) -> VertexOutput {
    let positions = array<vec2<f32>, 3>(vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
    var output: VertexOutput;
    output.position = vec4(positions[index], 0.0, 1.0);
    output.ndc = positions[index];
    return output;
}
@fragment fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let base = background.color.rgb;
    if background.color.a < 0.5 { return vec4(base, 1.0); }
    let near4 = background.inverse_view_projection * vec4(input.ndc, 0.0, 1.0);
    let far4 = background.inverse_view_projection * vec4(input.ndc, 1.0, 1.0);
    let origin = near4.xyz / near4.w;
    let direction = far4.xyz / far4.w - origin;
    let denominator = select(-max(abs(direction.y), 0.00001), max(abs(direction.y), 0.00001), direction.y >= 0.0);
    let distance = (-1.0 - origin.y) / denominator;
    let point = origin + direction * distance;
    let grid = point.xz * 4.0;
    let width = max(fwidth(grid), vec2(0.0001));
    let cell = abs(fract(grid - 0.5) - 0.5) / width;
    let line = 1.0 - min(min(cell.x, cell.y), 1.0);
    let majorCell = abs(fract(grid / 4.0 - 0.5) - 0.5) / (width / 4.0);
    let major = 1.0 - min(min(majorCell.x, majorCell.y), 1.0);
    let fade = (1.0 - smoothstep(4.0, 30.0, length(point.xz))) * (1.0 - smoothstep(0.5, 2.0, max(width.x, width.y)));
    var color = base * mix(0.75, 1.25, clamp(input.ndc.y * 0.5 + 0.5, 0.0, 1.0));
    let contrast = select(vec3(0.7), vec3(0.015), dot(base, vec3(0.2126, 0.7152, 0.0722)) > 0.08);
    let axes = 1.0 - min(abs(grid) / width, vec2(1.0));
    if distance > 0.0 && distance < 1.0 {
        color = mix(color, contrast, max(line * 0.22, major * 0.4) * fade);
        color = mix(color, vec3(0.35, 0.045, 0.035), axes.y * fade * 0.75);
        color = mix(color, vec3(0.035, 0.25, 0.07), axes.x * fade * 0.75);
    }
    return vec4(color, 1.0);
}
