const cards = [...document.querySelectorAll(".app-card")];
const categories = [...document.querySelectorAll(".filter-chip")];
const search = document.querySelector("#search");
const platform = document.querySelector("#platform");
const clear = document.querySelector("#clear-search");
const reset = document.querySelector("#reset-filters");
const normalize = (text) => text.normalize("NFKC").toLocaleLowerCase().trim();
let category = "";

function readLocation() {
  const params = new URLSearchParams(location.search);
  search.value = params.get("q") || "";
  category = categories.some(
    (button) => button.dataset.category === params.get("category"),
  )
    ? params.get("category")
    : "";
  const requestedPlatform = params.get("platform");
  platform.value = [...platform.options].some(
    (option) => option.value === requestedPlatform,
  )
    ? requestedPlatform
    : "";
}

function render(updateUrl = true) {
  const words = normalize(search.value).split(/\s+/).filter(Boolean);
  let visible = 0;
  for (const card of cards) {
    const matches =
      (!category || card.dataset.category === category) &&
      (!platform.value ||
        JSON.parse(card.dataset.platforms).includes(platform.value)) &&
      words.every((word) => normalize(card.dataset.search).includes(word));
    card.hidden = !matches;
    if (matches) visible++;
  }
  for (const button of categories)
    button.setAttribute(
      "aria-pressed",
      String(button.dataset.category === category),
    );
  const filtered = Boolean(words.length || category || platform.value);
  document.querySelector("#result-count").textContent = filtered
    ? `找到 ${visible} 个应用，共 ${cards.length} 个`
    : `显示全部 ${cards.length} 个应用`;
  document.querySelector("#empty-state").hidden = visible > 0;
  reset.hidden = !filtered;
  clear.hidden = !search.value;
  if (updateUrl) {
    const url = new URL(location.href);
    for (const [key, value] of Object.entries({
      q: search.value.trim(),
      category,
      platform: platform.value,
    })) {
      if (value) url.searchParams.set(key, value);
      else url.searchParams.delete(key);
    }
    // Keep search shareable without adding a history entry for every keystroke.
    history.replaceState(null, "", url);
  }
}

function resetFilters() {
  search.value = "";
  platform.value = "";
  category = "";
  render();
  search.focus();
}

categories.forEach((button) =>
  button.addEventListener("click", () => {
    category = button.dataset.category;
    render();
  }),
);
search.addEventListener("input", () => render());
platform.addEventListener("change", () => render());
clear.addEventListener("click", () => {
  search.value = "";
  render();
  search.focus();
});
reset.addEventListener("click", resetFilters);
document.querySelector("#empty-reset").addEventListener("click", resetFilters);
window.addEventListener("popstate", () => {
  readLocation();
  render(false);
});
readLocation();
render(false);
