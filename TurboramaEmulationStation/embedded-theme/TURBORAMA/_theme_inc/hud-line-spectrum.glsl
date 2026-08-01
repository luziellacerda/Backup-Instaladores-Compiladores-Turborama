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
uniform vec4 colorC;

vec4 movingPalette(float phase)
{
	float section = fract(phase) * 3.0;

	if (section < 1.0)
		return mix(colorA, colorB, smoothstep(0.0, 1.0, section));

	if (section < 2.0)
		return mix(colorB, colorC, smoothstep(0.0, 1.0, section - 1.0));

	return mix(colorC, colorA, smoothstep(0.0, 1.0, section - 2.0));
}

void main(void)
{
	bool horizontal = outputSize.x >= outputSize.y;
	float axis = horizontal ? v_tex.x : v_tex.y;
	float crossAxis = horizontal ? v_tex.y : v_tex.x;
	float time = float(FrameCount) * speed;

	// A paleta inteira atravessa a linha continuamente.
	float primaryWave = sin(axis * 25.132741 - time * 6.283185);
	float detailWave = sin(axis * 50.265482 + time * 4.398230);
	float wave = (primaryWave + detailWave * 0.35) / 1.35;
	float waveCenter = 0.5 + wave * 0.24;
	float distanceToWave = abs(crossAxis - waveCenter);

	float core = 1.0 - smoothstep(0.025, 0.110, distanceToWave);
	float glow = 1.0 - smoothstep(0.10, 0.26, distanceToWave);
	vec4 spectrum = movingPalette(axis * 1.50 - time * 0.85 + wave * 0.08);

	// Um brilho estreito acompanha a transição e reforça a sensação de movimento.
	float headPosition = fract(time * 0.92);
	float distanceToHead = abs(axis - headPosition);
	distanceToHead = min(distanceToHead, 1.0 - distanceToHead);
	float head = 1.0 - smoothstep(0.012, 0.075, distanceToHead);
	float trail = 1.0 - smoothstep(0.045, 0.245, distanceToHead);

	float pulse = 0.88 + 0.12 * sin(axis * 18.849556 - time * 8.0);
	vec3 finalColor = spectrum.rgb * (0.96 + glow * 0.12);
	finalColor = mix(finalColor, vec3(1.0, 0.98, 0.86), head * 0.52);
	float alphaShape = max(core, glow * 0.14);
	float alpha = spectrum.a * alphaShape * (0.78 + trail * 0.10 + head * 0.22) * pulse;

	FragColor = vec4(finalColor, clamp(alpha * intensity, 0.0, 1.0)) * v_col;
}

#endif
