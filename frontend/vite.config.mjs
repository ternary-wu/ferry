import { defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  base: './',
  plugins: [vue(), tailwindcss()],
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts']
  }
});
