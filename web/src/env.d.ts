/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The API's address in production. Empty locally, where Vite proxies /api. */
  readonly VITE_API_BASE_URL?: string;
  /** "true" turns automatic sync (polling) back on. Off by default: bills sync on Refresh. */
  readonly VITE_AUTO_SYNC?: string;
  /** The release version ("1.2.3") or an unreleased build ("1.2.3+abc1234"), set by ci-cd. */
  readonly VITE_APP_VERSION?: string;
}
