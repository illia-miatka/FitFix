import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// One image, one origin (Epic 2): in production the API serves the built SPA and /api/*
// from the same origin, so no base URL is ever configured in the client and CORS never is
// either. In development Vite stands in for that single origin by proxying /api to Kestrel,
// which listens on 5000 with no launch profile of its own. Story 2.8 settles the image.
// The target is 127.0.0.1 and not localhost on purpose: Node resolves localhost to ::1 first,
// and on macOS AirPlay Receiver listens on ::1:5000, which answers 403 to everything — so the
// proxy reached AirPlay instead of Kestrel and every call read as an unreachable service.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5000',
        changeOrigin: false,
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
