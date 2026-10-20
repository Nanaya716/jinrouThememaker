import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
  },
  server: {
    proxy: {
      '/thememaker-api': {
        target: `http://127.0.0.1:${process.env.PORT || 4318}`,
        changeOrigin: false,
      }
    }
  },
  base: '/thememaker/'
})
