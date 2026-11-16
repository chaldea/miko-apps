import { cp, mkdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import {
  escapeHtml as e,
  localPath,
  readCatalog,
  repoRoot,
  requireFile,
  siteRoot,
  sourceUrl,
} from "./catalog.mjs";

const catalog = await readCatalog();
const categories = [...new Set(catalog.apps.map((app) => app.category))];
const platforms = [...new Set(catalog.apps.flatMap((app) => app.platforms))];
for (const app of catalog.apps) {
  if (!(await stat(localPath(repoRoot, app.sourcePath))).isDirectory())
    throw new Error(`Missing source directory: ${app.sourcePath}`);
  await requireFile(resolve(siteRoot, `assets/covers/${app.id}.png`));
}

const arrow =
  '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M6 18 18 6M6 6h12v12" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const cards = catalog.apps
  .map(
    (app, index) => `
  <article class="app-card" data-id="${e(app.id)}" data-category="${e(app.category)}" data-platforms="${e(JSON.stringify(app.platforms))}" data-search="${e([app.name, app.description, app.category, ...app.tags, ...app.platforms].join(" "))}">
    <a class="app-link" href="${e(sourceUrl(catalog, app))}" aria-label="查看 ${e(app.name)} 源码">
      <div class="cover-wrap"><img class="app-cover" src="./assets/covers/${e(app.id)}.png" alt="${e(app.name)} 原生应用界面预览" width="1440" height="1000" ${index ? 'loading="lazy"' : 'fetchpriority="high"'}><span class="cover-link">探索应用 ${arrow}</span></div>
      <div class="card-heading"><div><span class="category-label">${e(app.category)}</span><h3>${e(app.name)}</h3></div><span class="source-arrow">${arrow}</span></div>
      <p class="card-description">${e(app.description)}</p>
      <ul class="tags" aria-label="应用标签">${app.tags.map((tag) => `<li>${e(tag)}</li>`).join("")}</ul>
      <div class="card-bottom"><span>${app.platforms.map(e).join('<span class="platform-dot" aria-hidden="true">·</span>')}</span><span class="source-label">查看源码 ${arrow}</span></div>
    </a>
  </article>`,
  )
  .join("\n");

const values = {
  repository: `https://github.com/${catalog.repository}`,
  contributeUrl: `https://github.com/${catalog.repository}/blob/${encodeURIComponent(catalog.sourceRef)}/showcase/README.md`,
  licenseUrl: `https://github.com/${catalog.repository}/blob/${encodeURIComponent(catalog.sourceRef)}/LICENSE`,
  count: String(catalog.apps.length).padStart(2, "0"),
  platformCount: String(platforms.length).padStart(2, "0"),
  categories: categories
    .map(
      (category) =>
        `<button type="button" class="filter-chip" data-category="${e(category)}" aria-pressed="false">${e(category)}<span>${catalog.apps.filter((app) => app.category === category).length}</span></button>`,
    )
    .join(""),
  platforms: platforms
    .map((platform) => `<option value="${e(platform)}">${e(platform)}</option>`)
    .join(""),
  cards,
};
const template = await readFile(resolve(siteRoot, "src/index.html"), "utf8");
const html = template.replace(/\{\{(\w+)\}\}/g, (_, key) => {
  if (!(key in values)) throw new Error(`Unknown template value: ${key}`);
  return values[key];
});
// The output path is fixed; never remove a caller-provided directory.
const output = resolve(siteRoot, "dist");
await rm(output, { recursive: true, force: true });
await mkdir(resolve(output, "assets/covers"), { recursive: true });
for (const app of catalog.apps)
  await cp(
    resolve(siteRoot, `assets/covers/${app.id}.png`),
    resolve(output, `assets/covers/${app.id}.png`),
  );
for (const file of ["styles.css", "app.js", "favicon.svg"])
  await cp(resolve(siteRoot, `src/${file}`), resolve(output, file));
await writeFile(resolve(output, "index.html"), html);
await writeFile(resolve(output, ".nojekyll"), "");
console.log(`Built ${catalog.apps.length} apps → ${output}`);
