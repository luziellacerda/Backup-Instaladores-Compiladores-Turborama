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
uniform COMPAT_PRECISION vec2 textureSize;
uniform COMPAT_PRECISION int FrameCount;
uniform COMPAT_PRECISION float outlineWidth;
uniform COMPAT_PRECISION float speed;
uniform COMPAT_PRECISION vec4 outlineColorA;
uniform COMPAT_PRECISION vec4 outlineColorB;

const float PI2 = 6.28318530718;
// A quadra do contorno é 5,2% maior que a capa. A margem abaixo
// mantém a arte na mesma escala e reserva apenas os pixels do brilho.
const float PADDING = 0.025;

float sampleAlpha(vec2 uv)
{
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
		return 0.0;

	return COMPAT_TEXTURE(u_tex, uv).a;
}

void main(void)
{
	// Reserva uma pequena margem transparente sem alterar a imagem principal.
	vec2 sourceUv = (v_tex - vec2(PADDING)) / (1.0 - 2.0 * PADDING);
	vec2 safeTextureSize = max(textureSize, vec2(1.0));
	vec2 radius = vec2(outlineWidth) / safeTextureSize;
	vec2 wideRadius = radius * 1.8;

	float centerAlpha = sampleAlpha(sourceUv);
	float expandedAlpha = centerAlpha;
	float expandedWideAlpha = centerAlpha;

	expandedAlpha = max(expandedAlpha, sampleAlpha(sourceUv + vec2( radius.x, 0.0)));
	expandedAlpha = max(expandedAlpha, sampleAlpha(sourceUv + vec2(-radius.x, 0.0)));
	expandedAlpha = max(expandedAlpha, sampleAlpha(sourceUv + vec2(0.0,  radius.y)));
	expandedAlpha = max(expandedAlpha, sampleAlpha(sourceUv + vec2(0.0, -radius.y)));
	expandedAlpha = max(expandedAlpha, sampleAlpha(sourceUv + vec2( radius.x,  radius.y)));
	expandedAlpha = max(expandedAlpha, sampleAlpha(sourceUv + vec2(-radius.x,  radius.y)));
	expandedAlpha = max(expandedAlpha, sampleAlpha(sourceUv + vec2( radius.x, -radius.y)));
	expandedAlpha = max(expandedAlpha, sampleAlpha(sourceUv + vec2(-radius.x, -radius.y)));

	expandedWideAlpha = max(expandedWideAlpha, sampleAlpha(sourceUv + vec2( wideRadius.x, 0.0)));
	expandedWideAlpha = max(expandedWideAlpha, sampleAlpha(sourceUv + vec2(-wideRadius.x, 0.0)));
	expandedWideAlpha = max(expandedWideAlpha, sampleAlpha(sourceUv + vec2(0.0,  wideRadius.y)));
	expandedWideAlpha = max(expandedWideAlpha, sampleAlpha(sourceUv + vec2(0.0, -wideRadius.y)));
	expandedWideAlpha = max(expandedWideAlpha, sampleAlpha(sourceUv + vec2( wideRadius.x,  wideRadius.y)));
	expandedWideAlpha = max(expandedWideAlpha, sampleAlpha(sourceUv + vec2(-wideRadius.x,  wideRadius.y)));
	expandedWideAlpha = max(expandedWideAlpha, sampleAlpha(sourceUv + vec2( wideRadius.x, -wideRadius.y)));
	expandedWideAlpha = max(expandedWideAlpha, sampleAlpha(sourceUv + vec2(-wideRadius.x, -wideRadius.y)));

	// Mostra somente o contorno externo da silhueta transparente.
	float outline = smoothstep(0.05, 0.72, expandedAlpha) *
	                (1.0 - smoothstep(0.03, 0.48, centerAlpha));
	float outerGlow = smoothstep(0.04, 0.66, expandedWideAlpha) *
	                  (1.0 - smoothstep(0.03, 0.42, centerAlpha));

	vec2 centered = v_tex - vec2(0.5);
	float angle = atan(centered.y, centered.x) / PI2 + 0.5;
	float loadingHead = fract(float(FrameCount) * speed);
	float distanceToHead = abs(angle - loadingHead);
	distanceToHead = min(distanceToHead, 1.0 - distanceToHead);

	// O ponto mais claro percorre continuamente o contorno como carregamento.
	float movingHighlight = 1.0 - smoothstep(0.012, 0.105, distanceToHead);
	float softTail = 1.0 - smoothstep(0.035, 0.24, distanceToHead);

	vec4 baseColor = mix(outlineColorA, outlineColorB, clamp(v_tex.y, 0.0, 1.0));
	vec3 finalRgb = mix(baseColor.rgb, vec3(0.94, 0.99, 1.0), movingHighlight * 0.78);
	float lineAlpha = outline * baseColor.a * (0.52 + softTail * 0.23 + movingHighlight * 0.32);
	float glowAlpha = outerGlow * baseColor.a * (0.070 + softTail * 0.095);
	float finalAlpha = max(lineAlpha, glowAlpha);

	FragColor = vec4(finalRgb, min(finalAlpha, 1.0)) * v_col;
}

#endif
