import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  fullyParallel: true,
  workers: 2,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: "list",
  outputDir: "./test-results",
  use: {
    channel: process.env.PLAYWRIGHT_CHANNEL,
    baseURL: "http://127.0.0.1:4174/miko-apps/",
    trace: "retain-on-failure",
  },
  webServer: {
    command:
      "npm run build && node scripts/serve.mjs --port 4174 --base /miko-apps/",
    url: "http://127.0.0.1:4174/miko-apps/",
    reuseExistingServer: false,
  },
});
