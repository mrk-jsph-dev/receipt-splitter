// Photos straight from a phone are often 5 to 12 MB. The free reader takes 4 MB at most, so
// JPEG and PNG photos are scaled down in the browser first. HEIC and PDF go as they are when they
// already fit, because most browsers can't redraw them.

export const MAX_BYTES = 4 * 1024 * 1024;
const LONG_SIDE = 2400;

export type Prepared = { ok: true; blob: Blob } | { ok: false; reason: 'too-large' | 'unsupported' };

export async function prepare(file: File): Promise<Prepared> {
  const drawable = /^image\/(jpeg|png|webp)$/.test(file.type);
  const readable = drawable || /^image\/(heic|heif|tiff|bmp)$/.test(file.type) || file.type === 'application/pdf' || file.type === '';
  if (!readable) return { ok: false, reason: 'unsupported' };
  if (!drawable) return file.size <= MAX_BYTES ? { ok: true, blob: file } : { ok: false, reason: 'too-large' };
  if (file.size <= MAX_BYTES / 2) return { ok: true, blob: file };

  try {
    const image = await createImageBitmap(file);
    const scale = Math.min(1, LONG_SIDE / Math.max(image.width, image.height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(image.width * scale);
    canvas.height = Math.round(image.height * scale);
    canvas.getContext('2d')!.drawImage(image, 0, 0, canvas.width, canvas.height);
    image.close();
    for (const quality of [0.85, 0.7, 0.55]) {
      const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
      if (blob && blob.size <= MAX_BYTES) return { ok: true, blob };
    }
  } catch {
    // Fall through: send the original if it fits.
  }
  return file.size <= MAX_BYTES ? { ok: true, blob: file } : { ok: false, reason: 'too-large' };
}
