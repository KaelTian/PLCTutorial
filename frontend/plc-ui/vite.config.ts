import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5008',
      '/hubs': {
        target: 'http://localhost:5008',
        ws: true,
      },
    },
  },
})
