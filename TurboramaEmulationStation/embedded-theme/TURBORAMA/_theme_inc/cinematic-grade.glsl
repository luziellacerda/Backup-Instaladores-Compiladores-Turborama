#if defined(VERTEX)

#if __VERSION__ >= 130
#define COMPAT_VARYING out
#define COMPAT_ATTRIBUTE in
#else
#define COMPAT_VARYING varying
#define COMPAT_ATTRIBUTE attribute
#endif

#ifdef GL_ES
#define COMPAT_PRECISION mediump
#else
#define COMPAT_PRECISION
#endif

uniform mat4 MVPMatrix;
COMPAT_ATTRIBUTE vec2 VertexCoord;
COMPAT_ATTRIBUTE vec2 TexCoord;
COMPAT_ATTRIBUTE vec4 COLOR;
COMPAT_VARYING vec2 v_tex;
COMPAT_VARYING vec4 v_col;

void main(void)
{
	gl_Position = MVPMatrix * vec4(VertexCoord.xy, 0.0, 1.0);
	v_tex = TexCoord;
	v_col = COLOR;
}

#elif defined(FRAGMENT)

#if __VERSION__ >= 130
#define COMPAT_VARYING in
#define COMPAT_TEXTURE texture
out vec4 FragColor;
#else
#define COMPAT_VARYING varying
#define COMPAT_TEXTURE texture2D
#define FragColor gl_FragColor
#endif

#ifdef GL_ES
#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif
#define COMPAT_PRECISION mediump
#else
#define COMPAT_PRECISION
#endif

COMPAT_VARYING vec2 v_tex;
COMPAT_VARYING vec4 v_col;

uniform sampler2D u_tex;
uniform COMPAT_PRECISION int FrameCount;
uniform COMPAT_PRECISION float exposure;
uniform COMPAT_PRECISION float saturation;

float hash21(vec2 p)
{
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

void main(void)
{
	vec4 source = COMPAT_TEXTURE(u_tex, v_tex);
	vec3 rgb = source.rgb;

	float luma = dot(rgb, vec3(0.2126, 0.7152, 0.0722));
	rgb = mix(vec3(luma), rgb, saturation);
	rgb = (rgb - 0.5) * 1.10 + 0.5;
	rgb *= vec3(0.80, 0.90, 1.05) * exposure;

	vec2 centered = v_tex - vec2(0.5);
	float vignette = 1.0 - smoothstep(0.22, 0.78, dot(centered, centered) * 1.45);
	float edgeDarken = mix(0.32, 0.88, vignette);
	float topShade = mix(0.70, 1.0, smoothstep(0.0, 0.34, v_tex.y));
	rgb *= edgeDarken * topShade;

	float grain = hash21(v_tex * vec2(1920.0, 1080.0) + float(FrameCount) * 0.071) - 0.5;
	rgb += grain * 0.010;

	FragColor = vec4(max(rgb, vec3(0.0)), source.a) * v_col;
}

#endif
