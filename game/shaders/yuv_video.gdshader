// A decoded movie frame: luma in TEXTURE, the half-size chroma planes in tex_cb / tex_cr (same UV, as the planes cover
// the same padded picture). BT.601 studio range to RGB.
shader_type canvas_item;

uniform sampler2D tex_cb : filter_linear;
uniform sampler2D tex_cr : filter_linear;

void fragment() {
	float y = texture(TEXTURE, UV).r;
	float cb = texture(tex_cb, UV).r - 0.5;
	float cr = texture(tex_cr, UV).r - 0.5;
	float l = 1.164383 * (y - 0.062745);
	COLOR = vec4(l + 1.596027 * cr, l - 0.391762 * cb - 0.812968 * cr, l + 2.017232 * cb, COLOR.a);
}
