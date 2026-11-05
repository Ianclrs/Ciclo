/**
 * Preparação da foto de perfil no navegador.
 *
 * A foto é guardada como texto (data URL) no banco, então o tamanho do payload importa: reduzir
 * aqui evita enviar megabytes e ser recusado pela API. O servidor valida de novo
 * (`ImageDataUrl`): limite de tamanho e assinatura PNG/JPEG/WebP.
 *
 * Dono único da redução — a foto do aluno e a foto de perfil do usuário usam esta mesma função.
 */

/** Lado máximo, em pixels. Acima disso a imagem é reduzida mantendo a proporção. */
export const PHOTO_MAX_SIDE = 256;

/** Qualidade do JPEG gerado. 0.82 é o ponto em que a foto ainda parece nítida e cabe no limite. */
export const PHOTO_JPEG_QUALITY = 0.82;

export function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('image_load_failed'));
    image.src = url;
  });
}

/** Lê o arquivo, reduz ao lado máximo e devolve um data URL JPEG pronto para envio. */
export async function toResizedDataUrl(file: File): Promise<string> {
  const objectUrl = URL.createObjectURL(file);
  try {
    const image = await loadImage(objectUrl);
    const scale = Math.min(1, PHOTO_MAX_SIDE / Math.max(image.width, image.height));
    const width = Math.max(1, Math.round(image.width * scale));
    const height = Math.max(1, Math.round(image.height * scale));

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('canvas_unavailable');
    context.drawImage(image, 0, 0, width, height);

    return canvas.toDataURL('image/jpeg', PHOTO_JPEG_QUALITY);
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}
