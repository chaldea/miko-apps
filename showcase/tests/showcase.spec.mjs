import { expect, test } from "@playwright/test";
import { readCatalog, sourceUrl } from "../scripts/catalog.mjs";

const catalog = await readCatalog();
test("project Pages subpath loads all covers and correct source links", async ({
  page,
}) => {
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("response", (response) => {
    if (response.status() >= 400) errors.push(response.url());
  });
  await page.goto("./");
  await expect(page.locator(".app-card:visible")).toHaveCount(
    catalog.apps.length,
  );
  for (const app of catalog.apps) {
    const card = page.locator(`[data-id="${app.id}"]`);
    await expect(card.getByRole("link")).toHaveAttribute(
      "href",
      sourceUrl(catalog, app),
    );
    const image = card.getByRole("img");
    await image.scrollIntoViewIfNeeded();
    await expect
      .poll(() =>
        image.evaluate((element) => element.complete && element.naturalWidth),
      )
      .toBe(1440);
  }
  expect(errors).toEqual([]);
});

test("category, platform and normalized keyword filters combine; empty state resets", async ({
  page,
}) => {
  await page.goto("./");
  await page.getByRole("button", { name: "影音娱乐" }).click();
  await page.getByLabel("按平台筛选").selectOption("Android");
  await page.getByRole("searchbox").fill("ＡＮＩＭＥ 视频");
  await expect(page.locator(".app-card:visible")).toHaveCount(1);
  await expect(page.locator(".app-card:visible h3")).toHaveText("Anime");
  await page.getByRole("searchbox").fill("IoT");
  await expect(page.locator(".app-card:visible")).toHaveCount(0);
  await expect(page.getByText("还没有找到这个应用")).toBeVisible();
  await expect(page.getByRole("status")).toHaveText(
    `找到 0 个应用，共 ${catalog.apps.length} 个`,
  );
  await page.getByRole("button", { name: "查看全部应用" }).click();
  await expect(page.locator(".app-card:visible")).toHaveCount(
    catalog.apps.length,
  );
  await expect(page.getByRole("searchbox")).toBeFocused();
  await expect(page).not.toHaveURL(/q=|category=|platform=/);
});

test("filter URL survives reload; clearing search preserves other filters", async ({
  page,
}) => {
  await page.goto("./?category=物联网&platform=iOS&q=fbox");
  await expect(page.locator(".app-card:visible h3")).toHaveText("FBox");
  await page.reload();
  await expect(page.getByRole("button", { name: "物联网" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  await expect(page.getByLabel("按平台筛选")).toHaveValue("iOS");
  await page.getByRole("button", { name: "清除搜索" }).click();
  await expect(page.getByRole("searchbox")).toHaveValue("");
  await expect(page.locator(".app-card:visible h3")).toHaveText("FBox");
  await page.goto("./?category=unknown&platform=unknown");
  await expect(page.locator(".app-card:visible")).toHaveCount(
    catalog.apps.length,
  );
});

test("keyboard can filter and follow a source link", async ({ page }) => {
  await page.goto("./");
  const category = page.getByRole("button", { name: "物联网" });
  await category.focus();
  await page.keyboard.press("Enter");
  await expect(page.locator(".app-card:visible h3")).toHaveText("FBox");
  const app = catalog.apps.find((app) => app.id === "fbox");
  const source = sourceUrl(catalog, app);
  await page.route(source, (route) =>
    route.fulfill({
      contentType: "text/html",
      body: "<h1>Source directory</h1>",
    }),
  );
  await page.getByRole("link", { name: "查看 FBox 源码" }).focus();
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(source);
});

for (const width of [360, 768, 1440]) {
  test(`responsive layout at ${width}px has no overflow or clipped controls`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("./");
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    const search = page.getByRole("searchbox");
    await search.fill("实时监控");
    await expect(page.locator(".app-card:visible h3")).toHaveText("FBox");
    await page.getByRole("button", { name: "重置筛选" }).click();
    await expect(page.locator(".app-card:visible")).toHaveCount(
      catalog.apps.length,
    );
  });
}

test("source cards remain available with JavaScript disabled", async ({
  browser,
}) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  try {
    const page = await context.newPage();
    await page.goto("http://127.0.0.1:4174/miko-apps/");
    await expect(page.locator(".app-card")).toHaveCount(catalog.apps.length);
    await expect(
      page.getByRole("link", { name: "查看 Anime 源码" }),
    ).toBeVisible();
  } finally {
    await context.close();
  }
});
