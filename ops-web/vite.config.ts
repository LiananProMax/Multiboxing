import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      // 不改写 Host：后端的 requireSameOrigin 会比对 Origin 与 Host，
      // 字符串简写默认 changeOrigin: true，会导致开发模式下 POST 被判为跨站请求
      '/api': {
        target: 'http://127.0.0.1:4170',
        changeOrigin: false,
      },
    },
  },
})
