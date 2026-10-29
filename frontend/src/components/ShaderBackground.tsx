import { useEffect, useRef } from 'react';

/**
 * Fundo animado por shader (WebGL) usado nas telas públicas.
 *
 * Degrada em silêncio quando a plataforma não oferece WebGL ou quando o shader
 * não compila: o canvas fica transparente e a cor de fundo do contêiner aparece.
 * Não é um erro de carregamento de dados, por isso não usa `LoadError`.
 */

const VERTEX_SHADER = `attribute vec2 a_position;
varying vec2 v_texCoord;
void main() {
  v_texCoord = a_position * 0.5 + 0.5;
  gl_Position = vec4(a_position, 0.0, 1.0);
}`;

const FRAGMENT_SHADER = `precision highp float;
uniform float u_time;
uniform vec2 u_resolution;

// Simplex noise implementation
vec3 permute(vec3 x) { return mod(((x*34.0)+1.0)*x, 289.0); }

float snoise(vec2 v){
  const vec4 C = vec4(0.211324865405187, 0.366025403784439,
           -0.577350269189626, 0.024390243902439);
  vec2 i  = floor(v + dot(v, C.yy) );
  vec2 x0 = v -   i + dot(i, C.xx);
  vec2 i1;
  i1 = (x0.x > x0.y) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
  vec4 x12 = x0.xyxy + C.xxzz;
  x12.xy -= i1;
  i = mod(i, 289.0);
  vec3 p = permute( permute( i.y + vec3(0.0, i1.y, 1.0 ))
  + i.x + vec3(0.0, i1.x, 1.0 ));
  vec3 m = max(0.5 - vec3(dot(x0,x0), dot(x12.xy,x12.xy),
    dot(x12.zw,x12.zw)), 0.0);
  m = m*m ;
  m = m*m ;
  vec3 x = 2.0 * fract(p * C.www) - 1.0;
  vec3 h = abs(x) - 0.5;
  vec3 ox = floor(x + 0.5);
  vec3 a0 = x - ox;
  m *= 1.79284291400159 - 0.85373472095314 * ( a0*a0 + h*h );
  vec3 g;
  g.x  = a0.x  * x0.x  + h.x  * x0.y;
  g.yz = a0.yz * x12.xz + h.yz * x12.yw;
  return 130.0 * dot(m, g);
}

void main() {
    vec2 st = gl_FragCoord.xy / u_resolution.xy;
    float aspect = u_resolution.x / u_resolution.y;
    vec2 p = st;
    p.x *= aspect;

    // Velocidade serena e sutil
    float t = u_time * 0.05;

    // Fluxo orgânico multicamadas
    float n1 = snoise(p * 1.3 + vec2(t * 0.4, -t * 0.3));
    float n2 = snoise(p * 2.1 + vec2(-t * 0.45, t * 0.35) + n1 * 0.7);
    float n3 = snoise(p * 1.1 + vec2(t * 0.25, t * 0.2) - n2 * 0.5);
    float n4 = snoise(p * 2.6 + vec2(sin(t * 0.25), cos(t * 0.25)));

    // Paleta refinada em tons pastéis e cinza claro acetinado
    vec3 colLightGrayWarm = vec3(0.93, 0.94, 0.95); // Cinza claro neutro suave (#eceff2)
    vec3 colPastelSlate   = vec3(0.86, 0.88, 0.91); // Cinza sutil com leve profundidade (#dbe0e8)
    vec3 colPastelPeach   = vec3(0.97, 0.82, 0.72); // Pêssego / açafrão pastel desaturado suave
    vec3 colPastelCoral   = vec3(0.96, 0.78, 0.76); // Coral pastel acolhedor
    vec3 colPastelMint    = vec3(0.76, 0.90, 0.84); // Menta pastel sutil e refrescante
    vec3 colPastelLilac   = vec3(0.87, 0.80, 0.92); // Lavanda / lilás pastel sutil

    // Máscaras de mistura orgânica com transição aveludada
    float w1 = smoothstep(-0.4, 0.6, n1);
    float w2 = smoothstep(-0.5, 0.7, n2);
    float w3 = smoothstep(-0.3, 0.8, n3);
    float w4 = smoothstep(-0.4, 0.7, n4);

    // Composição com base predominante em cinza claro e nuances pastéis delicadas
    vec3 col = mix(colLightGrayWarm, colPastelSlate, w1 * 0.5);
    col = mix(col, colPastelPeach, w3 * 0.45);
    col = mix(col, colPastelMint, w4 * 0.40);
    col = mix(col, colPastelCoral, w2 * (1.0 - w4 * 0.3) * 0.35);
    col = mix(col, colPastelLilac, (1.0 - w3) * w1 * 0.30);

    // Iluminação central e vinheta muito discreta
    float centerGlow = 1.0 - length((st - vec2(0.5, 0.45)) * vec2(0.85, 1.1)) * 0.25;
    col *= clamp(centerGlow, 0.88, 1.06);

    gl_FragColor = vec4(col, 1.0);
}`;

function compileShader(
  gl: WebGLRenderingContext,
  type: number,
  source: string,
): WebGLShader | null {
  const shader = gl.createShader(type);
  if (!shader) return null;
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (gl.getShaderParameter(shader, gl.COMPILE_STATUS)) return shader;

  console.warn(
    `ShaderBackground: falha ao compilar ${type === gl.VERTEX_SHADER ? 'vertex' : 'fragment'} shader.`,
    gl.getShaderInfoLog(shader),
  );
  gl.deleteShader(shader);
  return null;
}

function createProgram(gl: WebGLRenderingContext): WebGLProgram | null {
  const vertex = compileShader(gl, gl.VERTEX_SHADER, VERTEX_SHADER);
  const fragment = compileShader(gl, gl.FRAGMENT_SHADER, FRAGMENT_SHADER);
  if (!vertex || !fragment) {
    if (vertex) gl.deleteShader(vertex);
    if (fragment) gl.deleteShader(fragment);
    return null;
  }

  const program = gl.createProgram();
  if (!program) return null;
  gl.attachShader(program, vertex);
  gl.attachShader(program, fragment);
  gl.linkProgram(program);
  // Os shaders continuam válidos enquanto anexados ao programa.
  gl.deleteShader(vertex);
  gl.deleteShader(fragment);

  if (gl.getProgramParameter(program, gl.LINK_STATUS)) return program;

  console.warn('ShaderBackground: falha ao linkar programa.', gl.getProgramInfoLog(program));
  gl.deleteProgram(program);
  return null;
}

export function ShaderBackground() {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    // `alpha: true` deixa o canvas transparente quando nada é desenhado — é o
    // fallback visual quando WebGL/shader não é utilizável.
    const gl = canvas.getContext('webgl', { alpha: true, antialias: false });
    // Contexto perdido (reset de driver, GPU reiniciada): fundo decorativo, sai sem ruído.
    // `getShaderInfoLog` devolve null nesse estado, o que faria parecer erro de shader.
    if (!gl || gl.isContextLost()) return;

    const program = createProgram(gl);
    if (!program) return;

    gl.useProgram(program);

    const buffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(
      gl.ARRAY_BUFFER,
      new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]),
      gl.STATIC_DRAW,
    );
    const position = gl.getAttribLocation(program, 'a_position');
    gl.enableVertexAttribArray(position);
    gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);

    const uTime = gl.getUniformLocation(program, 'u_time');
    const uResolution = gl.getUniformLocation(program, 'u_resolution');

    // O buffer acompanha o tamanho CSS do canvas (1x): nitidez suficiente para um
    // fundo desfocado e custo baixo em telas de alta densidade.
    // Redefinir `canvas.width/height` ZERA o drawing buffer do WebGL, por isso o
    // tamanho e o desenho vivem juntos: mudar o tamanho obriga a repintar.
    const syncSize = () => {
      const width = canvas.clientWidth || 1280;
      const height = canvas.clientHeight || 720;
      if (canvas.width === width && canvas.height === height) return false;
      canvas.width = width;
      canvas.height = height;
      return true;
    };

    let frame = 0;
    let startedAt = 0;
    let elapsedMs = 0;
    let disposed = false;

    // Velocidade do fundo. O GLSL fica intacto: o fator age sobre o tempo informado
    // a `u_time`. Com "menos movimento" a animação continua, porém na metade da
    // velocidade — o fundo é decorativo, não um conteúdo que precise parar.
    const motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
    let timeScale = motionQuery.matches ? 1.5 : 3;

    const draw = (elapsed: number) => {
      elapsedMs = elapsed;
      gl.viewport(0, 0, canvas.width, canvas.height);
      if (uTime) gl.uniform1f(uTime, elapsed * 0.001);
      if (uResolution) gl.uniform2f(uResolution, canvas.width, canvas.height);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    };

    const loop = (timestamp: number) => {
      if (disposed) return;
      if (!startedAt) startedAt = timestamp;
      draw((timestamp - startedAt) * timeScale);
      frame = requestAnimationFrame(loop);
    };

    // Redesenha após um resize: sem isso o quadro fica vazio até o próximo rAF —
    // que não chega com a aba oculta.
    const observer = new ResizeObserver(() => {
      if (syncSize()) draw(elapsedMs);
    });

    // Medir e pintar antes de observar: o primeiro quadro nunca depende do rAF.
    syncSize();
    draw(0);
    observer.observe(canvas);
    frame = requestAnimationFrame(loop);

    // A preferência do sistema pode mudar com a página aberta: reajusta a velocidade.
    const onMotionPreferenceChange = () => {
      timeScale = motionQuery.matches ? 1.5 : 3;
    };
    motionQuery.addEventListener('change', onMotionPreferenceChange);

    return () => {
      disposed = true;
      cancelAnimationFrame(frame);
      observer.disconnect();
      motionQuery.removeEventListener('change', onMotionPreferenceChange);
      gl.deleteBuffer(buffer);
      gl.deleteProgram(program);
      // Não usar WEBGL_lose_context aqui: em StrictMode o efeito roda duas vezes no
      // MESMO canvas, e um contexto perdido não pode ser recompilado — a segunda
      // montagem falharia com log nulo. Os recursos acima já devolvem a GPU.
    };
  }, []);

  return (
    <canvas
      ref={canvasRef}
      aria-hidden="true"
      className="pointer-events-none absolute inset-0 h-full w-full"
    />
  );
}
