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
#define COMPAT_PRECISION mediump
#else
#define COMPAT_PRECISION
#endif

COMPAT_VARYING vec2 v_tex;
COMPAT_VARYING vec4 v_col;

uniform COMPAT_PRECISION vec2 outputSize;
uniform COMPAT_PRECISION int FrameCount;
uniform COMPAT_PRECISION float intensity;
uniform COMPAT_PRECISION float speed;

float thinGridLine(float value)
{
	float cell = fract(value);
	float distanceToLine = min(cell, 1.0 - cell);
	return 1.0 - smoothstep(0.0, 0.025, distanceToLine);
}

void main(void)
{
	float time = float(FrameCount) * speed;
	float aspect = max(outputSize.x, 1.0) / max(outputSize.y, 1.0);
	vec2 p = v_tex - vec2(0.5);
	p.x *= aspect;

	vec2 redCenter = vec2(-0.74 + sin(time * 0.34) * 0.08, -0.28);
	vec2 softCenter = vec2(0.62 + cos(time * 0.27) * 0.10, 0.30);
	float redGlow = exp(-3.5 * dot(p - redCenter, p - redCenter));
	float softGlow = exp(-4.8 * dot(p - softCenter, p - softCenter));

	float horizon = exp(-48.0 * pow(v_tex.y - 0.42, 2.0));
	float sweepPosition = fract(time * 0.045);
	float sweepDistance = abs(v_tex.x - sweepPosition);
	float sweep = 1.0 - smoothstep(0.0, 0.16, sweepDistance);
	sweep *= smoothstep(0.12, 0.42, v_tex.y) *
	         (1.0 - smoothstep(0.58, 0.90, v_tex.y));

	float filmLine = 0.5 + 0.5 *
	                 sin((v_tex.y * max(outputSize.y, 1.0) +
	                      float(FrameCount) * 0.025) * 3.14159265);
	filmLine *= 0.012;

	vec3 color = vec3(0.0);
	color += vec3(0.90, 0.015, 0.025) * redGlow * 0.24;
	color += vec3(0.18, 0.18, 0.19) * softGlow * 0.11;
	color += vec3(0.72, 0.01, 0.02) * horizon * 0.025;
	color += vec3(0.85, 0.03, 0.04) * sweep * 0.018;
	color += vec3(filmLine);

	float alpha = clamp(redGlow * 0.18 +
	                    softGlow * 0.08 +
	                    horizon * 0.018 +
	                    sweep * 0.012 +
	                    filmLine, 0.0, 0.30);
	FragColor = vec4(color * intensity, alpha * intensity) * v_col;
}

#endif
