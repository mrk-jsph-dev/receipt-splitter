import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    // Local API from `dotnet run --project src/ReceiptSplitter.Api`.
    proxy: { '/api': 'http://localhost:5080', '/healthz': 'http://localhost:5080' },
  },
});
