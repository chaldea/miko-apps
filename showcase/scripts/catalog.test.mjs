import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import test from "node:test";
import {
  escapeHtml,
  localPath,
  siteRoot,
  sourceUrl,
  validateCatalog,
} from "./catalog.mjs";

const original = JSON.parse(
  await readFile(resolve(siteRoot, "apps.json"), "utf8"),
);
test("catalog rejects duplicate IDs and paths outside the repository", () => {
  const duplicate = structuredClone(original);
  duplicate.apps.push(duplicate.apps[0]);
  assert.throws(() => validateCatalog(duplicate), /duplicate/);
  for (const path of [
    "../secret",
    "/absolute",
    "C:/secret",
    "a/../../b",
    "a\\b",
  ]) {
    assert.throws(() => localPath(siteRoot, path));
  }
});
test("source links support forks, nested directories and branch names", () => {
  assert.equal(
    sourceUrl(
      { repository: "example/fork", sourceRef: "feature/showcase" },
      { sourcePath: "Samples/My App" },
    ),
    "https://github.com/example/fork/tree/feature%2Fshowcase/Samples/My%20App",
  );
});
test("metadata cannot inject markup into generated HTML", () => {
  assert.equal(
    escapeHtml("<img src=\"x\" onerror='alert(1)'>&"),
    "&lt;img src=&quot;x&quot; onerror=&#39;alert(1)&#39;&gt;&amp;",
  );
});
