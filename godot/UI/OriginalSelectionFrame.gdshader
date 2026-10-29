shader_type canvas_item;
render_mode unshaded;

// T242 (#1010): the original's selection frame over the selected cell, for the classic look. The
// original draws its unit map's image 12 (a black rhombus outline) with SRCERASE, source AND NOT
// destination: where the image is white the screen underneath (terrain and marker) is inverted, and on
// the black outline it becomes black. The mask is not the original image: OriginalSelectionFrame.cs
// reconstructs it from the research description, and GameMapView binds its constants here. UV spans
// the selected cell, sampled nearest on the 32-px grid, so the frame scales with the tile.

uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;
uniform float mask_size = 32.0;
uniform float line_inner = 14.0;
uniform float line_outer = 17.0;

void fragment() {
    vec2 pixel = clamp(floor(UV * mask_size), vec2(0.0), vec2(mask_size - 1.0));
    // Twice the taxicab distance of the pixel's centre from the tile's centre, an integer.
    vec2 offset = abs(pixel * 2.0 + 1.0 - mask_size);
    float distance2 = offset.x + offset.y;
    bool outline = distance2 >= 2.0 * line_inner - 0.5 && distance2 <= 2.0 * line_outer + 0.5;
    vec3 under = texture(screen_texture, SCREEN_UV).rgb;
    COLOR = vec4(outline ? vec3(0.0) : vec3(1.0) - under, 1.0);
}
