// Optional artwork regeneration; requires the existing UI Playwright dev tools.
// Windows consumes the generated ICO. No SVG renderer ships with the app.
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const root = fileURLToPath(new URL("../", import.meta.url));
const require = createRequire(path.join(root, "ui/package.json"));
const { chromium } = require("@playwright/test");
const svg = await fs.readFile(path.join(root, "assets/branding/setuppulse-tray.svg"), "utf8");
const sizes = [16, 20, 24, 32, 48, 64];
const frames = [];
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ deviceScaleFactor: 1 });
  for (const size of sizes) {
    await page.setContent(`<style>body{margin:0;background:transparent}svg{display:block;width:${size}px;height:${size}px}</style>${svg}`);
    const valid = await page.evaluate((source) => !new DOMParser().parseFromString(source, "image/svg+xml").querySelector("parsererror"), svg);
    if (!valid) throw new Error("Invalid tray SVG.");
    frames.push(await page.locator("svg").screenshot({ omitBackground: true }));
  }
} finally { await browser.close(); }

const header = Buffer.alloc(6 + 16 * sizes.length);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(sizes.length, 4);
let offset = header.length;
for (let index = 0; index < sizes.length; index++) {
  const entry = 6 + 16 * index;
  header[entry] = sizes[index]; header[entry + 1] = sizes[index];
  header.writeUInt16LE(1, entry + 4); header.writeUInt16LE(32, entry + 6);
  header.writeUInt32LE(frames[index].length, entry + 8);
  header.writeUInt32LE(offset, entry + 12);
  offset += frames[index].length;
}
await fs.writeFile(path.join(root, "src/SetupPulse.App/tray.ico"), Buffer.concat([header, ...frames]));
console.log(`Exported tray SVG to ICO (${sizes.join("/")}px).`);
