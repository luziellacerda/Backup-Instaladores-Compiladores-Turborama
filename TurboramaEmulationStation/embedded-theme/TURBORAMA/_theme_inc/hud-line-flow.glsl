#if defined(VERTEX)

#if __VERSION__ >= 130
#define COMPAT_VARYING out
#define COMPAT_ATTRIBUTE in
#else
#define COMPAT_VARYING varying
#define COMPAT_ATTRIBUTE attribute
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
out vec4 FragColor;
#else
#define COMPAT_VARYING varying
#define FragColor gl_FragColor
#endif

#ifdef GL_ES
#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif
#endif

COMPAT_VARYING vec2 v_tex;
COMPAT_VARYING vec4 v_col;

uniform vec2 outputSize;
uniform int FrameCount;
uniform float speed;
uniform float intensity;
uniform vec4 colorA;
uniform vec4 colorB;

void main(void)
{
	bool horizontal = outputSize.x >= outputSize.y;
	float axis = horizontal ? v_tex.x : v_tex.y;
	float crossAxis = horizontal ? v_tex.y : v_tex.x;

	float headPosition = fract(float(FrameCount) * speed);
	float distanceToHead = abs(axis - headPosition);
	distanceToHead = min(distanceToHead, 1.0 - distanceToHead);

	float head = 1.0 - smoothstep(0.008, 0.065, distanceToHead);
	float tail = 1.0 - smoothstep(0.035, 0.220, distanceToHead);
	float core = 1.0 - smoothstep(0.18, 0.50, abs(crossAxis - 0.5));

	vec4 baseColor = mix(colorA, colorB, axis);
	vec3 movingColor = mix(baseColor.rgb, vec3(0.96, 0.99, 1.0), head * 0.88);
	float pulse = 0.72 + 0.18 * sin(float(FrameCount) * speed * 4.0);
	float alpha = baseColor.a * core * (0.42 + tail * 0.34 + head * 0.50) * pulse;

	FragColor = vec4(movingColor, clamp(alpha * intensity, 0.0, 1.0)) * v_col;
}

#endif
