import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "path";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: "http://localhost:5000",
        changeOrigin: true,
      },
      "/connect": {
        target: "http://localhost:5000",
        changeOrigin: true,
      },
      "/order-api": {
        target: "http://localhost:5010",
        changeOrigin: true,
        rewrite: (p) => p.replace(/^\/order-api/, "/api"),
      },
    },
  },
});
