shader_type canvas_item;

// T148 "The map's surface is painted, not tiled": draws the whole terrain rectangle in one pass by
// blending the six seamless surface textures through the two RGBA8 splat textures baked by
// godot/Assets/TerrainSplatMap.cs. The weights are computed in C# and only sampled here, so the tested
// code is the drawn code; this shader adds only the world-space texture sampling and the shore band,
// neither of which Done-when 1 asserts. Each surface sampler declares its own repeat and filter hints
// (the Opus review of the splatting round, R1): without repeat_enable the sampler clamps at the edge
// texel and every terrain past the first repeat is one flat colour — the blur the visual review saw.
// The splat samplers must NOT repeat: they map UV once over the world rect, and a repeat would blend
// the opposite edge of the map into it.

uniform sampler2D splat_a : repeat_disable, filter_linear; // R plain, G desert, B forest, A mountain
uniform sampler2D splat_b : repeat_disable, filter_linear; // R shallow sea, G deep sea (B, A unused)
uniform sampler2D tex_plain : repeat_enable, filter_linear_mipmap;
uniform sampler2D tex_desert : repeat_enable, filter_linear_mipmap;
uniform sampler2D tex_forest : repeat_enable, filter_linear_mipmap;
uniform sampler2D tex_mountain : repeat_enable, filter_linear_mipmap;
uniform sampler2D tex_shallow : repeat_enable, filter_linear_mipmap;
uniform sampler2D tex_deep : repeat_enable, filter_linear_mipmap;

uniform vec2 world_size = vec2(320.0, 140.0); // map size in cells
uniform float surface_scale = 2.5;            // cells spanned by one surface-texture repeat (2 to 3,
                                              // the user's decision of 2026-10-06; GameMapView binds it)

// The shore band: where land and sea weights are both present, a narrow sand band toward the land and
// surf toward the sea, so coasts read without shore tiles. The surf is toned down — a light rim, not
// bright cyan (the user's decision of 2026-10-06 at the visual review of the splatting round).
uniform float shore_threshold = 0.25;
uniform float shore_strength = 0.35;
uniform vec3 sand_color = vec3(0.82, 0.72, 0.50);
uniform vec3 surf_color = vec3(0.88, 0.94, 0.95);

// The shallow sea is toned down the same way: pulled toward this muted aqua before blending, so a thin
// rim reads light rather than the bright cyan the review rejected.
uniform float shallow_tone_down = 0.35;
uniform vec3 shallow_tint = vec3(0.62, 0.78, 0.80);

void fragment() {
    vec4 first = texture(splat_a, UV);
    vec4 second = texture(splat_b, UV);

    vec2 world = UV * world_size;      // in cells
    vec2 surface_uv = world / surface_scale;

    vec3 shallow_rgb = mix(texture(tex_shallow, surface_uv).rgb, shallow_tint, shallow_tone_down);
    vec3 colour =
        texture(tex_plain, surface_uv).rgb * first.r +
        texture(tex_desert, surface_uv).rgb * first.g +
        texture(tex_forest, surface_uv).rgb * first.b +
        texture(tex_mountain, surface_uv).rgb * first.a +
        shallow_rgb * second.r +
        texture(tex_deep, surface_uv).rgb * second.g;

    float land = first.r + first.g + first.b + first.a;
    float sea = second.r + second.g;
    float shore = clamp(min(land, sea) / shore_threshold, 0.0, 1.0);
    colour = mix(colour, sand_color, shore * clamp(land - sea, 0.0, 1.0) * shore_strength);
    colour = mix(colour, surf_color, shore * clamp(sea - land, 0.0, 1.0) * shore_strength);

    COLOR = vec4(colour, 1.0);
}
