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
// surf toward the sea, so coasts read without shore tiles. The surf lightens the sea by at most 15%
// (the user's decision of 2026-10-06 at the escalation of rework round 2; the round-2 surf mixed up to
// 35% toward near-white). These three values are public constants of TerrainSplatMap bound by
// GameMapView; the defaults here are only the shader's own fallback.
uniform float shore_threshold = 0.25;
uniform float shore_strength = 0.35;
uniform float surf_strength = 0.15;
uniform vec3 sand_color = vec3(0.82, 0.72, 0.50);
uniform vec3 surf_color = vec3(0.88, 0.94, 0.95);

// The shallow rim is mixed toward a deep teal rather than a light tint, so it reads darker, not as the
// bright cyan the round-2 review measured (the user's decision of 2026-10-06 at the escalation of
// rework round 2). GameMapView binds these from TerrainSplatMap's public constants.
uniform float shallow_tone_down = 0.85;
uniform vec3 shallow_tint = vec3(0.07, 0.24, 0.32);

// The Catmull-Rom cubic weights for the four samples around t in [0, 1), in tap order
// base - 1, base, base + 1, base + 2. At t = 0.5 they are the kernel the entry names:
// -1/16, 9/16, 9/16, -1/16.
vec4 cubic_weights(float t) {
    float t2 = t * t;
    float t3 = t2 * t;
    return vec4(
        -0.5 * t3 + t2 - 0.5 * t,
         1.5 * t3 - 2.5 * t2 + 1.0,
        -1.5 * t3 + 2.0 * t2 + 0.5 * t,
         0.5 * t3 - 0.5 * t2);
}

// The separable Catmull-Rom cubic over the 4x4 lattice, mirroring TerrainSplatMap.SampleBicubic
// exactly: sample i's texel centre is at (i + 0.5) / size, so the four taps are base + (-1,0,1,2)
// clamped to the edge (repeat_disable). Sampling at the texel centre makes the linear filter return
// the exact texel, so the manual cubic is the whole interpolation — no bilinear stair-step.
vec4 sample_splat_bicubic(sampler2D tex, vec2 uv) {
    vec2 size = vec2(textureSize(tex, 0));
    vec2 pos = (uv * size) - vec2(0.5);
    vec2 base = floor(pos);
    vec4 wx = cubic_weights(pos.x - base.x);
    vec4 wy = cubic_weights(pos.y - base.y);
    vec2 lo = vec2(0.5) / size;
    vec2 hi = vec2(1.0) - lo;
    vec2 o = (base + vec2(0.5)) / size;
    vec2 px = vec2(1.0) / size;

    vec4 r0 = texture(tex, clamp(o + vec2(-1.0, -1.0) * px, lo, hi)) * wx.x
            + texture(tex, clamp(o + vec2( 0.0, -1.0) * px, lo, hi)) * wx.y
            + texture(tex, clamp(o + vec2( 1.0, -1.0) * px, lo, hi)) * wx.z
            + texture(tex, clamp(o + vec2( 2.0, -1.0) * px, lo, hi)) * wx.w;
    vec4 r1 = texture(tex, clamp(o + vec2(-1.0,  0.0) * px, lo, hi)) * wx.x
            + texture(tex, clamp(o + vec2( 0.0,  0.0) * px, lo, hi)) * wx.y
            + texture(tex, clamp(o + vec2( 1.0,  0.0) * px, lo, hi)) * wx.z
            + texture(tex, clamp(o + vec2( 2.0,  0.0) * px, lo, hi)) * wx.w;
    vec4 r2 = texture(tex, clamp(o + vec2(-1.0,  1.0) * px, lo, hi)) * wx.x
            + texture(tex, clamp(o + vec2( 0.0,  1.0) * px, lo, hi)) * wx.y
            + texture(tex, clamp(o + vec2( 1.0,  1.0) * px, lo, hi)) * wx.z
            + texture(tex, clamp(o + vec2( 2.0,  1.0) * px, lo, hi)) * wx.w;
    vec4 r3 = texture(tex, clamp(o + vec2(-1.0,  2.0) * px, lo, hi)) * wx.x
            + texture(tex, clamp(o + vec2( 0.0,  2.0) * px, lo, hi)) * wx.y
            + texture(tex, clamp(o + vec2( 1.0,  2.0) * px, lo, hi)) * wx.z
            + texture(tex, clamp(o + vec2( 2.0,  2.0) * px, lo, hi)) * wx.w;

    return (r0 * wy.x) + (r1 * wy.y) + (r2 * wy.z) + (r3 * wy.w);
}

void fragment() {
    // The splat sampling is the bicubic function above (the user's decision of 2026-10-06 at the
    // second escalation), so the lattice's zig-zag along a crisp border is smoothed away; the surface
    // textures keep their world-space linear+mipmap sampling.
    vec4 first = sample_splat_bicubic(splat_a, UV);
    vec4 second = sample_splat_bicubic(splat_b, UV);

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
    colour = mix(colour, surf_color, shore * clamp(sea - land, 0.0, 1.0) * surf_strength);

    COLOR = vec4(colour, 1.0);
}
