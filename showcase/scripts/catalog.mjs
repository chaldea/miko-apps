import { readFile, stat } from "node:fs/promises";
import { resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

export const siteRoot = fileURLToPath(new URL("../", import.meta.url));
export const repoRoot = resolve(siteRoot, "..");

export function escapeHtml(value) {
  return String(value).replace(
    /[&<>"']/g,
    (character) =>
      ({
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': "&quot;",
        "'": "&#39;",
      })[character],
  );
}

export function localPath(root, value) {
  if (
    typeof value !== "string" ||
    !value ||
    value.includes("\\") ||
    value.startsWith("/") ||
    value.split("/").some((part) => !part || part === "." || part === "..") ||
    /[:?#]/.test(value)
  ) {
    throw new Error(`Expected a relative local path: ${value}`);
  }
  const path = resolve(root, value);
  if (!path.startsWith(resolve(root) + sep))
    throw new Error(`Path is outside its root: ${value}`);
  return path;
}

export function validateCatalog(catalog) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(catalog.repository))
    throw new Error("Invalid GitHub repository.");
  if (typeof catalog.sourceRef !== "string" || !catalog.sourceRef.trim())
    throw new Error("Missing sourceRef.");
  if (!Array.isArray(catalog.apps) || !catalog.apps.length)
    throw new Error("The catalog needs at least one app.");
  const ids = new Set();
  for (const app of catalog.apps) {
    if (typeof app.id !== "string" || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(app.id) || ids.has(app.id))
      throw new Error(`Invalid or duplicate app id: ${app.id}`);
    ids.add(app.id);
    for (const field of ["name", "description", "category"]) {
      if (typeof app[field] !== "string" || !app[field].trim())
        throw new Error(`${app.id}: missing ${field}`);
    }
    for (const field of ["platforms", "tags"]) {
      if (
        !Array.isArray(app[field]) ||
        !app[field].length ||
        app[field].some((item) => typeof item !== "string" || !item.trim()) ||
        new Set(app[field]).size !== app[field].length
      ) {
        throw new Error(`${app.id}: invalid ${field}`);
      }
    }
    localPath(repoRoot, app.sourcePath);
    if (
      !app.cover ||
      !/^#[a-f\d]{6}$/i.test(app.cover.background) ||
      !/^#[a-f\d]{6}$/i.test(app.cover.accent)
    )
      throw new Error(`${app.id}: invalid cover colors`);
    for (const field of ["eyebrow", "tagline"]) {
      if (typeof app.cover[field] !== "string" || !app.cover[field].trim())
        throw new Error(`${app.id}: missing cover ${field}`);
    }
    if (
      !Array.isArray(app.cover.screenshots) ||
      app.cover.screenshots.length !== 2
    )
      throw new Error(`${app.id}: provide two cover screenshots`);
    for (const screenshot of app.cover.screenshots) {
      localPath(siteRoot, screenshot);
      if (
        !screenshot.startsWith("assets/screenshots/") ||
        !screenshot.endsWith(".png")
      )
        throw new Error(
          `${app.id}: screenshot must be a PNG under assets/screenshots/`,
        );
    }
    if (app.capture) {
      localPath(repoRoot, app.capture.project);
      if (!Array.isArray(app.capture.files) || app.capture.files.length !== 2)
        throw new Error(`${app.id}: provide two capture filenames`);
      for (const file of app.capture.files) localPath(siteRoot, file);
    }
  }
  return catalog;
}

export async function readCatalog() {
  const catalog = JSON.parse(
    await readFile(resolve(siteRoot, "apps.json"), "utf8"),
  );
  catalog.repository = process.env.GITHUB_REPOSITORY || catalog.repository;
  catalog.sourceRef = process.env.SOURCE_REF || catalog.sourceRef;
  return validateCatalog(catalog);
}

export function sourceUrl(catalog, app) {
  return `https://github.com/${catalog.repository}/tree/${encodeURIComponent(catalog.sourceRef)}/${app.sourcePath.split("/").map(encodeURIComponent).join("/")}`;
}

export async function requireFile(path) {
  if (!(await stat(path)).isFile()) throw new Error(`Expected a file: ${path}`);
}
