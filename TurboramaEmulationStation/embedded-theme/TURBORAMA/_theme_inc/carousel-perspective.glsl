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
uniform float turn;
uniform float depth;

void main(void)
{
   float amount = clamp(abs(turn), 0.0, 0.98);

   if (amount < 0.001)
   {
      FragColor = COMPAT_TEXTURE(u_tex, v_tex) * v_col;
      return;
   }

   // A borda de entrada funciona como a espiral de um caderno. O restante
   // da capa e projetado sobre um cilindro e volta a ficar plano no final.
   float direction = turn < 0.0 ? -1.0 : 1.0;
   float localX = turn < 0.0 ? v_tex.x : 1.0 - v_tex.x;
   float angle = amount * 1.46;
   float projectedWidth = sin(angle) / angle;

   // A folha ocupa menos largura quanto mais gira. A lateral presa permanece
   // parada, enquanto a borda livre se aproxima dela como numa pagina real.
   if (localX > projectedWidth)
   {
      FragColor = vec4(0.0);
      return;
   }

   float sourceX = asin(clamp(localX * angle, 0.0, 0.999)) / angle;
   float theta = sourceX * angle;
   float cylinderDepth = 1.0 - cos(theta);

   vec2 uv = v_tex;
   uv.x = turn < 0.0 ? sourceX : 1.0 - sourceX;

   // A profundidade comprime a folha verticalmente e levanta levemente a
   // borda livre, produzindo o volume que uma rotacao plana nao possui.
   float verticalScale = max(0.70, 1.0 - cylinderDepth * depth);
   uv.y = (uv.y - 0.5) / verticalScale + 0.5;
   uv.y += direction * sin(sourceX * 3.14159265) * amount * 0.032;

   if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
   {
      FragColor = vec4(0.0);
      return;
   }

   vec4 paper = COMPAT_TEXTURE(u_tex, uv);
   float curvedNormal = cos(theta);
   float ridge = pow(max(sin(theta), 0.0), 4.0);
   float edgeShadow = cylinderDepth * amount;
   float lighting = 0.74 + curvedNormal * 0.26 - edgeShadow * 0.30;

   paper.rgb *= lighting;
   paper.rgb += vec3(1.0, 0.97, 0.90) * ridge * amount * 0.34;
   FragColor = vec4(clamp(paper.rgb, 0.0, 1.0), paper.a) * v_col;
}

#endif
