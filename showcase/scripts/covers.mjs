import { spawnSync } from "node:child_process";
import { copyFile, mkdir, mkdtemp, readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { chromium } from "@playwright/test";
import {
  escapeHtml as e,
  localPath,
  readCatalog,
  repoRoot,
  siteRoot,
} from "./catalog.mjs";

const args = process.argv.slice(2);
if (args.includes("--help")) {
  console.log(
    "npm run covers -- [app-id] [--capture]\nCompose 1440×1000 PNG covers from the two native screenshots in apps.json.\n--capture first runs each app’s offline verification project to refresh its screenshots.",
  );
  process.exit(0);
}
const id = args.find((arg) => !arg.startsWith("--"));
if (
  args.some((arg) => arg.startsWith("--") && arg !== "--capture") ||
  args.filter((arg) => !arg.startsWith("--")).length > 1
)
  throw new Error("Usage: npm run covers -- [app-id] [--capture]");
const catalog = await readCatalog();
const apps = catalog.apps.filter((app) => !id || app.id === id);
if (!apps.length) throw new Error(`Unknown app: ${id}`);

if (args.includes("--capture")) {
  // Every run has an isolated state directory; verification never reuses personal app settings.
  const captureRoot = resolve(repoRoot, "artifacts/showcase-capture");
  await mkdir(captureRoot, { recursive: true });
  for (const app of apps) {
    if (!app.capture)
      throw new Error(
        `${app.id}: add capture.project and capture.files, or supply screenshots manually.`,
      );
    const output = await mkdtemp(resolve(captureRoot, `${app.id}-`));
    console.log(
      `Capturing ${app.name} with its native offline verification project…`,
    );
    const result = spawnSync(
      "dotnet",
      [
        "run",
        "--project",
        localPath(repoRoot, app.capture.project),
        "--",
        output,
      ],
      { cwd: repoRoot, stdio: "inherit", shell: false },
    );
    if (result.error) throw result.error;
    if (result.status !== 0)
      throw new Error(`${app.id}: native capture failed (${result.status}).`);
    for (let index = 0; index < 2; index++) {
      const destination = localPath(siteRoot, app.cover.screenshots[index]);
      await mkdir(dirname(destination), { recursive: true });
      await copyFile(localPath(output, app.capture.files[index]), destination);
    }
  }
}

const browser = await chromium.launch({ channel: process.env.PLAYWRIGHT_CHANNEL });
try {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1000 },
    deviceScaleFactor: 1,
  });
  // Covers are built entirely from local, native-rendered screenshots. No external assets.
  await page.route("http://**/*", (route) => route.abort());
  await page.route("https://**/*", (route) => route.abort());
  for (const app of apps) {
    const screenshots = await Promise.all(
      app.cover.screenshots.map(
        async (path) =>
          `data:image/png;base64,${(await readFile(localPath(siteRoot, path))).toString("base64")}`,
      ),
    );
    await page.setContent(`<!doctype html><html lang="en"><head><meta charset="utf-8"><style>
      *{box-sizing:border-box}body{margin:0;width:1440px;height:1000px;overflow:hidden;background:${app.cover.background};color:${app.cover.accent};font-family:Arial,sans-serif}
      .halo{position:absolute;width:920px;height:920px;left:660px;top:70px;border:1px solid #ffffff70;border-radius:50%;box-shadow:0 0 0 95px #ffffff20,0 0 0 190px #ffffff18}
      .grid{position:absolute;inset:0;background-image:radial-gradient(${app.cover.accent}22 1px,transparent 1px);background-size:28px 28px;mask-image:linear-gradient(120deg,#000,transparent 65%)}
      .eyebrow{position:absolute;left:76px;top:73px;font-size:15px;letter-spacing:3px;font-weight:bold}
      .title{position:absolute;left:72px;top:200px;font-size:100px;line-height:1;letter-spacing:-5px;font-weight:600;margin:0}
      .tagline{position:absolute;left:78px;top:316px;font-size:27px;line-height:1.5;white-space:pre-line;letter-spacing:-.6px;opacity:.8}
      .star{position:absolute;left:76px;top:509px;width:100px;height:100px;opacity:.5}
      .phone{position:absolute;border:8px solid #fcfcfc;border-radius:39px;overflow:hidden;background:#fff;box-shadow:12px 24px 45px #26314223,0 0 0 1px #29322e09}
      .phone img{display:block;width:100%;height:auto}.back{width:314px;left:584px;top:288px;transform:rotate(-9deg)}.front{width:346px;left:938px;top:130px;transform:rotate(8deg)}
      .footer{position:absolute;left:78px;bottom:65px;display:flex;align-items:center;gap:16px;font-size:13px;letter-spacing:2px;font-weight:bold}.mark{display:grid;place-items:center;width:40px;height:40px;border:1px solid ${app.cover.accent}55;border-radius:12px;font-size:28px;letter-spacing:-2px;padding-bottom:3px}.label{position:absolute;right:65px;bottom:63px;font-size:12px;letter-spacing:3px;opacity:.65}
    </style></head><body><div class="halo"></div><div class="grid"></div><div class="eyebrow">${e(app.cover.eyebrow)}</div><h1 class="title">${e(app.name)}</h1><p class="tagline">${e(app.cover.tagline)}</p><svg class="star" viewBox="0 0 100 100" aria-hidden="true"><g stroke="currentColor" stroke-width="10"><path d="M50 2v96M2 50h96M16 16l68 68M16 84l68-68"/></g></svg><div class="phone back"><img src="${screenshots[1]}" alt=""></div><div class="phone front"><img src="${screenshots[0]}" alt=""></div><div class="footer"><span class="mark">m·</span> BUILT WITH MIKO</div><span class="label">NATIVE APP COLLECTION</span></body></html>`);
    await page.evaluate(async () => {
      await document.fonts.ready;
      await Promise.all([...document.images].map((image) => image.decode()));
    });
    const output = resolve(siteRoot, `assets/covers/${app.id}.png`);
    await mkdir(dirname(output), { recursive: true });
    await page.screenshot({ path: output, animations: "disabled" });
    console.log(`Generated ${output}`);
  }
} finally {
  await browser.close();
}
