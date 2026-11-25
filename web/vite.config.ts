import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// In development the app runs on :5173 and forwards API calls to the .NET API, so the browser sees one origin
// (exactly like production, where the API serves these files itself).
const api = 'http://localhost:5091';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': api, '/health': api },
  },
  build: { outDir: 'dist' },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.ts',
    css: false,
  },
});
