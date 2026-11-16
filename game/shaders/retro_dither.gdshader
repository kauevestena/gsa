shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform float dither_strength : hint_range(0.0, 1.0) = 0.65;
uniform float color_steps : hint_range(8.0, 64.0) = 32.0;
uniform float sepia_amount : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec4 tex = texture(screen_texture, SCREEN_UV);
	ivec2 p = ivec2(FRAGCOORD.xy) % 4;
	int idx = p.x + p.y * 4;
	float bayer = 0.0;
	if (idx == 0) bayer = 0.0;
	else if (idx == 1) bayer = 8.0;
	else if (idx == 2) bayer = 2.0;
	else if (idx == 3) bayer = 10.0;
	else if (idx == 4) bayer = 12.0;
	else if (idx == 5) bayer = 4.0;
	else if (idx == 6) bayer = 14.0;
	else if (idx == 7) bayer = 6.0;
	else if (idx == 8) bayer = 3.0;
	else if (idx == 9) bayer = 11.0;
	else if (idx == 10) bayer = 1.0;
	else if (idx == 11) bayer = 9.0;
	else if (idx == 12) bayer = 15.0;
	else if (idx == 13) bayer = 7.0;
	else if (idx == 14) bayer = 13.0;
	else bayer = 5.0;

	float threshold = (bayer / 16.0) - 0.5;
	vec3 quantized = floor(tex.rgb * color_steps + threshold * dither_strength + 0.5) / color_steps;
	vec3 dithered = mix(tex.rgb, clamp(quantized, 0.0, 1.0), dither_strength);
	// Daguerreotype sepia survey print filter when active
	float lum = dot(dithered, vec3(0.299, 0.587, 0.114));
	vec3 sepia = vec3(lum * 1.12, lum * 0.94, lum * 0.68);
	// Subtle PS2 cathode vignette
	vec2 uv_centered = SCREEN_UV - vec2(0.5);
	float vig = 1.0 - dot(uv_centered, uv_centered) * (0.35 * dither_strength + 0.45 * sepia_amount);
	vec3 final_rgb = mix(dithered, clamp(sepia, 0.0, 1.0), sepia_amount) * clamp(vig, 0.65, 1.0);
	COLOR = vec4(final_rgb, tex.a);
}

