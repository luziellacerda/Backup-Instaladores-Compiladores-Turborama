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
#endif

COMPAT_VARYING vec2 v_tex;
COMPAT_VARYING vec4 v_col;

uniform sampler2D u_tex;
uniform int FrameCount;
uniform float speed;
uniform float intensity;
uniform vec4 colorA;
uniform vec4 colorB;

void main(void)
{
    vec2 uv = v_tex;
    float sourceAlpha = COMPAT_TEXTURE(u_tex, uv).a;
    float time = float(FrameCount) * speed;

    // Moldura fina limitada ao contorno da celula.
    float edgeDistance = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
    float frame = 1.0 - smoothstep(0.006, 0.021, edgeDistance);

    // Cantoneira tecnica no quadrante superior direito.
    float topLine = (1.0 - smoothstep(0.006, 0.018, abs(uv.y - 0.045)))
                  * smoothstep(0.48, 0.57, uv.x)
                  * (1.0 - smoothstep(0.94, 0.98, uv.x));
    float rightLine = (1.0 - smoothstep(0.006, 0.018, abs(uv.x - 0.955)))
                    * smoothstep(0.035, 0.075, uv.y)
                    * (1.0 - smoothstep(0.36, 0.43, uv.y));
    float bracket = max(topLine, rightLine);

    // Scanner vertical, com nucleo branco e rastro azul.
    float scanPosition = fract(time * 0.42);
    float scanDistance = abs(uv.y - scanPosition);
    float scanCore = 1.0 - smoothstep(0.004, 0.016, scanDistance);
    float scanGlow = 1.0 - smoothstep(0.018, 0.095, scanDistance);
    float scanMask = mix(0.55, 1.0, smoothstep(0.05, 0.45, sourceAlpha));

    // Reticulo pulsante no canto, como leitura de alvo do HUD.
    vec2 targetDelta = (uv - vec2(0.900, 0.105)) * vec2(1.0, 1.35);
    float targetRadius = 0.047 + sin(time * 5.1) * 0.006;
    float targetRing = 1.0 - smoothstep(0.006, 0.017, abs(length(targetDelta) - targetRadius));
    float targetDot = 1.0 - smoothstep(0.006, 0.020, length(targetDelta));

    // Ponto de energia percorre a barra superior.
    float runnerX = 0.54 + fract(time * 0.78) * 0.39;
    float runner = (1.0 - smoothstep(0.012, 0.060, abs(uv.x - runnerX)))
                 * (1.0 - smoothstep(0.010, 0.038, abs(uv.y - 0.045)));

    float pulse = 0.78 + 0.22 * sin(time * 4.4);
    float blueEnergy = max(frame * 0.24, max(bracket * pulse, scanGlow * 0.34 * scanMask));
    float whiteEnergy = max(runner, max(scanCore * scanMask, targetRing * 0.90 + targetDot));
    float alpha = clamp((blueEnergy + whiteEnergy) * intensity, 0.0, 1.0);
    vec3 hudColor = mix(colorA.rgb, colorB.rgb, clamp(whiteEnergy, 0.0, 1.0));

    FragColor = vec4(hudColor, alpha) * v_col;
}

#endif
