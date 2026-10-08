import { defineConfig } from 'vitest/config';

export default defineConfig({
  server: { host: 'localhost', port: 5173 },
  test: { include: ['tests/**/*.test.ts'] },
});
