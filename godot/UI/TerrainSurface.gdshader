shader_type canvas_item;

// T148 "The map's surface is painted, not tiled": draws the whole terrain rectangle in one pass by
// blending the six seamless surface textures through the two RGBA8 splat textures baked by
// godot/Assets/TerrainSplatMap.cs. The weights are computed in C# and only sampled here, so the tested
// code is the drawn code; this shader adds only the world-space texture sampling and the shore band,
// neither of which Done-when 1 asserts. The draw sets texture_repeat (world-space tiling) and a
// mipmapped linear filter on the owning CanvasItem, so the far zoom stays calm.

uniform sampler2D splat_a; // R plain, G desert, B forest, A mountain
uniform sampler2D splat_b; // R shallow sea, G deep sea (B, A unused)
uniform sampler2D tex_plain;
uniform sampler2D tex_desert;
uniform sampler2D tex_forest;
uniform sampler2D tex_mountain;
uniform sampler2D tex_shallow;
uniform sampler2D tex_deep;

uniform vec2 world_size = vec2(320.0, 140.0); // map size in cells
uniform float surface_scale = 6.0;            // cells spanned by one surface-texture repeat

// The shore band: where land and sea weights are both present, a narrow sand band toward the land and
// surf toward the sea, so coasts read without shore tiles.
uniform float shore_threshold = 0.25;
uniform float shore_strength = 0.6;
uniform vec3 sand_color = vec3(0.82, 0.72, 0.50);
uniform vec3 surf_color = vec3(0.93, 0.97, 1.0);

void fragment() {
    vec4 first = texture(splat_a, UV);
    vec4 second = texture(splat_b, UV);

    vec2 world = UV * world_size;      // in cells
    vec2 surface_uv = world / surface_scale;

    vec3 colour =
        texture(tex_plain, surface_uv).rgb * first.r +
        texture(tex_desert, surface_uv).rgb * first.g +
        texture(tex_forest, surface_uv).rgb * first.b +
        texture(tex_mountain, surface_uv).rgb * first.a +
        texture(tex_shallow, surface_uv).rgb * second.r +
        texture(tex_deep, surface_uv).rgb * second.g;

    float land = first.r + first.g + first.b + first.a;
    float sea = second.r + second.g;
    float shore = clamp(min(land, sea) / shore_threshold, 0.0, 1.0);
    colour = mix(colour, sand_color, shore * clamp(land - sea, 0.0, 1.0) * shore_strength);
    colour = mix(colour, surf_color, shore * clamp(sea - land, 0.0, 1.0) * shore_strength);

    COLOR = vec4(colour, 1.0);
}
