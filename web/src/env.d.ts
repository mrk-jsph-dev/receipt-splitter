/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The API's address in production. Empty locally, where Vite proxies /api. */
  readonly VITE_API_BASE_URL?: string;
}
