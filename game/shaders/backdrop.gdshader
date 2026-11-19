// The field backdrop picture (FieldBackdrop), drawn on the canvas layer the environment uses as its background.
// The field's colour buffer holds gamma values (field_lit.gdshader), but Godot converts the background canvas from sRGB
// to linear when it copies it under the 3D scene. So the canvas carries the picture's gamma value encoded once more
// (linear -> sRGB), and the copy leaves the gamma value itself in the buffer.
shader_type canvas_item;

void fragment() {
	vec3 g = COLOR.rgb;
	COLOR.rgb = mix(g * 12.92, 1.055 * pow(g, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), g));
}
