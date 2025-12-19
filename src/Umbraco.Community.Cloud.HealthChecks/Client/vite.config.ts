import { defineConfig } from "vite";

export default defineConfig({
  build: {
    lib: {
      entry: "src/bundle.manifests.ts", // Bundle registers one or more manifests
      formats: ["es"],
      fileName: "umbraco-community-cloud-health-checks",
    },
    outDir: "../wwwroot/App_Plugins/UmbracoCommunityCloudHealthChecks", // your web component will be saved in this location
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      external: [/^@umbraco/],
    },
  },
  publicDir: "public", // Ensures the public folder (including umbraco-package.json) is copied to outDir
});
