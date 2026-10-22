import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./tests",
  outputDir: "../dev/test-results/ui",
  fullyParallel: true,
  use: {
    baseURL: "http://127.0.0.1:5173",
    viewport: { width: 1240, height: 860 },
  },
  webServer: {
    command: "npm run dev",
    url: "http://127.0.0.1:5173",
    reuseExistingServer: !process.env.CI,
  },
  reporter: "list",
});
