import { defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  plugins: [vue()],
  server: {
    // Local API from `dotnet run --project src/ReceiptSplitter.Api`.
    proxy: { '/api': 'http://localhost:5080', '/healthz': 'http://localhost:5080' },
  },
  test: {
    environment: 'happy-dom',
  },
});
