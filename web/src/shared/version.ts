/** The app version: set at build time by the ci-cd workflow (VITE_APP_VERSION). */
export function appVersion(value: string | undefined = import.meta.env.VITE_APP_VERSION): string {
  return value?.trim() || 'dev';
}
