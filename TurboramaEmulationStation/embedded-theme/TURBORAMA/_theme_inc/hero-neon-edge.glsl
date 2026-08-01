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

float luminance(vec3 color)
{
	return dot(color, vec3(0.299, 0.587, 0.114));
}

void main(void)
{
	vec2 safeSize = max(textureSize, vec2(1.0));
	vec2 pixel = 1.0 / safeSize;

	vec4 source = COMPAT_TEXTURE(u_tex, v_tex);
	vec3 leftColor = COMPAT_TEXTURE(u_tex, v_tex - vec2(pixel.x, 0.0)).rgb;
	vec3 rightColor = COMPAT_TEXTURE(u_tex, v_tex + vec2(pixel.x, 0.0)).rgb;
	vec3 topColor = COMPAT_TEXTURE(u_tex, v_tex - vec2(0.0, pixel.y)).rgb;
	vec3 bottomColor = COMPAT_TEXTURE(u_tex, v_tex + vec2(0.0, pixel.y)).rgb;

	vec3 farLeft = COMPAT_TEXTURE(u_tex, v_tex - vec2(pixel.x * 2.5, 0.0)).rgb;
	vec3 farRight = COMPAT_TEXTURE(u_tex, v_tex + vec2(pixel.x * 2.5, 0.0)).rgb;
	vec3 farTop = COMPAT_TEXTURE(u_tex, v_tex - vec2(0.0, pixel.y * 2.5)).rgb;
	vec3 farBottom = COMPAT_TEXTURE(u_tex, v_tex + vec2(0.0, pixel.y * 2.5)).rgb;

	float nearEdge = abs(luminance(rightColor) - luminance(leftColor)) +
	                 abs(luminance(bottomColor) - luminance(topColor));
	float wideEdge = abs(luminance(farRight) - luminance(farLeft)) +
	                 abs(luminance(farBottom) - luminance(farTop));

	float edge = smoothstep(0.12, 0.52, nearEdge);
	float halo = smoothstep(0.08, 0.44, wideEdge) * 0.40;

	float headPosition = fract(float(FrameCount) * 0.0048);
	float distanceToHead = abs(v_tex.y - headPosition);
	distanceToHead = min(distanceToHead, 1.0 - distanceToHead);
	float movingHead = 1.0 - smoothstep(0.016, 0.145, distanceToHead);
	float softTail = 1.0 - smoothstep(0.040, 0.330, distanceToHead);

	vec3 softWhite = vec3(0.68, 0.72, 0.78);
	vec3 pureWhite = vec3(1.00, 1.00, 1.00);
	float colorFlow = 0.5 + 0.5 *
	                  sin((v_tex.y + float(FrameCount) * 0.0018) *
	                      6.28318530718);
	vec3 neonColor = mix(softWhite, pureWhite, colorFlow);
	neonColor = mix(neonColor, vec3(1.00, 1.00, 1.00),
	                movingHead * 0.82);

	float strength = edge * (0.14 + softTail * 0.22 +
	                          movingHead * 0.50) +
	                 halo * (0.08 + movingHead * 0.16);
	vec3 finalColor = source.rgb + neonColor * strength;

	FragColor = vec4(clamp(finalColor, 0.0, 1.0), source.a) * v_col;
}

#endif
