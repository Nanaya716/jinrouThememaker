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
      '/postimages-api': {
        target: 'https://api.postimage.org',
        changeOrigin: true,
        rewrite: path => path.replace(/^\/postimages-api/, '')
      },
      '/postimages-page': {
        target: 'https://postimg.cc',
        changeOrigin: true,
        rewrite: path => path.replace(/^\/postimages-page/, '')
      }
    }
  },
  base: '/thememaker'
})
