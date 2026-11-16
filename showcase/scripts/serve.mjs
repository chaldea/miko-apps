import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import { extname, resolve, sep } from "node:path";
import { siteRoot } from "./catalog.mjs";

const args = process.argv.slice(2);
const option = (name, fallback) =>
  args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const port = Number(option("--port", "4173"));
const base = option("--base", "/");
if (!Number.isInteger(port) || port < 1 || port > 65535)
  throw new Error("Invalid port.");
if (!/^\/(?:[\w-]+\/)*$/.test(base))
  throw new Error("Base must start and end with /, e.g. /miko-apps/.");
const root = resolve(siteRoot, "dist");
const types = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
};
const server = createServer(async (request, response) => {
  try {
    if (!["GET", "HEAD"].includes(request.method)) {
      response.writeHead(405).end();
      return;
    }
    const pathname = decodeURIComponent(
      new URL(request.url, "http://localhost").pathname,
    );
    if (!pathname.startsWith(base)) {
      response.writeHead(404).end("Not found");
      return;
    }
    const path = resolve(root, pathname.slice(base.length) || "index.html");
    if (!path.startsWith(root + sep) || !(await stat(path)).isFile()) {
      response.writeHead(404).end("Not found");
      return;
    }
    response.writeHead(200, {
      "Content-Type": types[extname(path)] || "application/octet-stream",
      "Cache-Control": "no-cache",
    });
    if (request.method === "HEAD") response.end();
    else
      createReadStream(path)
        .on("error", () => response.destroy())
        .pipe(response);
  } catch {
    response.writeHead(404).end("Not found");
  }
});
server.listen(port, "127.0.0.1", () =>
  console.log(`Showcase preview: http://127.0.0.1:${port}${base}`),
);
