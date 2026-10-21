shader_type canvas_item;

// The "Classic tiles" map style (the owner's request of 2026-10-21): one 32-px terrain tile per cell,
// as the original draws its map, instead of TerrainSurface.gdshader's blended painting. GameMapView
// binds the cells' tile layers (one byte per cell) and the tiles as a texture array.
// texelFetch reads a cell's layer exactly; textureGrad takes its derivatives from the continuous
// world position, so the mipmap level does not jump at every cell edge where fract() wraps.

uniform sampler2D cell_layers : filter_nearest, repeat_disable; // R: the cell's tile layer / 255
uniform sampler2DArray tiles : filter_nearest_mipmap, repeat_enable;
uniform vec2 world_size = vec2(320.0, 140.0); // map size in cells

void fragment() {
    vec2 world = UV * world_size;
    ivec2 cell = clamp(ivec2(floor(world)), ivec2(0), ivec2(world_size) - ivec2(1));
    float layer = floor(texelFetch(cell_layers, cell, 0).r * 255.0 + 0.5);
    COLOR = textureGrad(tiles, vec3(fract(world), layer), dFdx(world), dFdy(world));
}
