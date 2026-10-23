import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { marked } from "marked";

// Renders the .mdx pages into a static site in docs/_site, which the Pages workflow
// uploads as-is. The navigation order and grouping come from docs/docs.json, so that file
// stays the single source of truth for what the site contains.
const root = path.dirname(fileURLToPath(import.meta.url));
const output = path.join(root, "_site");
const repository = "https://github.com/meysamhadeli/codai";
const siteTitle = "Codai";

const read = (file) => fs.readFileSync(path.join(root, file), "utf8");

function parse(source) {
  const match = /^---\r?\n([\s\S]*?)\r?\n---\r?\n/.exec(source);
  const meta = {};

  if (match) {
    for (const line of match[1].split(/\r?\n/)) {
      const entry = /^([A-Za-z][\w-]*):\s*(.*)$/.exec(line);
      if (entry) meta[entry[1]] = entry[2].replace(/^["']|["']$/g, "").trim();
    }
  }

  return { meta, body: match ? source.slice(match[0].length) : source };
}

const escapeHtml = (value) =>
  value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

const slugify = (value) =>
  value
    .toLowerCase()
    .replace(/`/g, "")
    .replace(/[^a-z0-9\s-]/g, "")
    .trim()
    .replace(/\s+/g, "-");

function render(body) {
  const html = marked.parse(body, { gfm: true, async: false });

  // marked no longer adds heading ids, so the table of contents needs them added here.
  return html.replace(/<h([23])>([\s\S]*?)<\/h\1>/g, (_, level, inner) => {
    const text = inner.replace(/<[^>]+>/g, "");
    return `<h${level} id="${slugify(text)}">${inner}</h${level}>`;
  });
}

function tableOfContents(html) {
  const entries = [...html.matchAll(/<h([23]) id="([^"]+)">([\s\S]*?)<\/h\1>/g)].map(
    ([, level, id, inner]) => ({
      level: Number(level),
      id,
      text: inner.replace(/<[^>]+>/g, ""),
    }),
  );

  if (entries.length < 2) return "";

  const items = entries
    .map(
      (entry) =>
        `<li class="toc-${entry.level}"><a href="#${entry.id}">${escapeHtml(entry.text)}</a></li>`,
    )
    .join("");

  return `<nav class="toc" aria-label="On this page"><p class="toc-title">On this page</p><ul>${items}</ul></nav>`;
}

const styles = `
:root {
  color-scheme: light dark;
  --bg: #ffffff;
  --fg: #1b1f24;
  --muted: #5b6773;
  --line: #e3e8ee;
  --accent: #0b6bcb;
  --code-bg: #f5f7fa;
}
@media (prefers-color-scheme: dark) {
  :root {
    --bg: #0f1318;
    --fg: #e6edf3;
    --muted: #9aa7b4;
    --line: #222b36;
    --accent: #6cb6ff;
    --code-bg: #161d25;
  }
}
* { box-sizing: border-box; }
body {
  margin: 0;
  background: var(--bg);
  color: var(--fg);
  font: 16px/1.65 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
}
a { color: var(--accent); text-decoration: none; }
a:hover { text-decoration: underline; }
.layout { display: flex; max-width: 1180px; margin: 0 auto; padding: 0 24px; gap: 48px; }
header.site { border-bottom: 1px solid var(--line); }
header.site .bar { max-width: 1180px; margin: 0 auto; padding: 16px 24px; display: flex; align-items: baseline; gap: 16px; }
header.site .name { font-weight: 700; font-size: 18px; color: var(--fg); }
header.site .tag { color: var(--muted); font-size: 14px; }
nav.side { width: 220px; flex: none; padding: 32px 0; font-size: 14px; }
nav.side .group { margin-bottom: 24px; }
nav.side .group-title { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: .06em; margin: 0 0 8px; }
nav.side ul { list-style: none; margin: 0; padding: 0; }
nav.side li { margin: 2px 0; }
nav.side a { color: var(--fg); display: block; padding: 4px 8px; border-radius: 6px; }
nav.side a:hover { background: var(--code-bg); text-decoration: none; }
nav.side a[aria-current="page"] { color: var(--accent); font-weight: 600; background: var(--code-bg); }
main { flex: 1; min-width: 0; padding: 32px 0 80px; }
main h1 { font-size: 32px; line-height: 1.25; margin: 0 0 8px; }
main h2 { font-size: 22px; margin: 40px 0 12px; padding-top: 8px; border-top: 1px solid var(--line); }
main h3 { font-size: 17px; margin: 28px 0 8px; }
main p, main li { color: var(--fg); }
main code { background: var(--code-bg); padding: 2px 5px; border-radius: 4px; font-size: .9em; }
main pre { background: var(--code-bg); padding: 16px; border-radius: 8px; overflow-x: auto; border: 1px solid var(--line); }
main pre code { background: none; padding: 0; }
main table { border-collapse: collapse; width: 100%; margin: 16px 0; font-size: 14px; }
main th, main td { border: 1px solid var(--line); padding: 8px 10px; text-align: left; vertical-align: top; }
main th { background: var(--code-bg); }
main blockquote { margin: 16px 0; padding: 4px 16px; border-left: 3px solid var(--line); color: var(--muted); }
.lead { color: var(--muted); font-size: 17px; margin: 0 0 8px; }
.toc { border: 1px solid var(--line); border-radius: 8px; padding: 12px 16px; margin: 24px 0; font-size: 14px; }
.toc-title { margin: 0 0 8px; color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: .06em; }
.toc ul { list-style: none; margin: 0; padding: 0; }
.toc li { margin: 3px 0; }
.toc-3 { padding-left: 16px; }
footer.page { border-top: 1px solid var(--line); margin-top: 48px; padding-top: 16px; font-size: 14px; color: var(--muted); display: flex; justify-content: space-between; gap: 16px; }
.cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px; margin: 24px 0; padding: 0; list-style: none; }
.cards li { border: 1px solid var(--line); border-radius: 10px; padding: 16px; }
.cards a { font-weight: 600; }
.cards p { margin: 6px 0 0; color: var(--muted); font-size: 14px; }
@media (max-width: 860px) {
  .layout { flex-direction: column; gap: 0; }
  nav.side { width: auto; padding: 24px 0 0; border-bottom: 1px solid var(--line); }
}
`;

function layout({ title, description, body, current }) {
  const config = JSON.parse(read("docs.json"));
  const groups = config.navigation.groups;

  // A page lives one directory down (slug/index.html); the landing page is at the root,
  // so links are built from a base that both depths resolve correctly.
  const base = current === null ? "" : "../";

  const sidebar = groups
    .map((group) => {
      const items = group.pages
        .map((page) => {
          const aria = page === current ? ' aria-current="page"' : "";
          const label = pageLabel(page);
          return `<li><a href="${base}${page}/"${aria}>${escapeHtml(label)}</a></li>`;
        })
        .join("");
      return `<div class="group"><p class="group-title">${escapeHtml(group.group)}</p><ul>${items}</ul></div>`;
    })
    .join("");

  const home = current === null ? ' aria-current="page"' : "";

  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escapeHtml(title)} · ${siteTitle}</title>
<meta name="description" content="${escapeHtml(description)}">
<style>${styles}</style>
</head>
<body>
<header class="site">
  <div class="bar">
    <a class="name" href="${base}./"${home}>${siteTitle}</a>
    <span class="tag">Documentation</span>
  </div>
</header>
<div class="layout">
  <nav class="side" aria-label="Documentation">
    <div class="group"><p class="group-title">Home</p><ul><li><a href="${base}./"${home}>Overview</a></li></ul></div>
    ${sidebar}
  </nav>
  <main>
${body}
  </main>
</div>
</body>
</html>`;
}

const pages = [];

function pageLabel(page) {
  const cached = pages.find((entry) => entry.slug === page);
  if (cached) return cached.title;
  // The sidebar is built before every page is known, so fall back to the file itself.
  const meta = parse(read(`${page}.mdx`)).meta;
  return meta.title ?? page;
}

// Render the pages first so the sidebar can use real titles.
const entries = JSON.parse(read("docs.json")).navigation.groups.flatMap((group) => group.pages);
for (const slug of entries) {
  const { meta, body } = parse(read(`${slug}.mdx`));
  pages.push({ slug, title: meta.title ?? slug, description: meta.description ?? "", body });
}

fs.rmSync(output, { recursive: true, force: true });
fs.mkdirSync(output, { recursive: true });

for (const [index, page] of pages.entries()) {
  const html = render(page.body);
  const previous = pages[index - 1];
  const next = pages[index + 1];

  const navigation = [
    previous ? `<a href="../${previous.slug}/">← ${escapeHtml(previous.title)}</a>` : "<span></span>",
    `<a href="${repository}/blob/main/docs/${page.slug}.mdx">Edit this page</a>`,
    next ? `<a href="../${next.slug}/">${escapeHtml(next.title)} →</a>` : "<span></span>",
  ].join("");

  const content = `
    <h1>${escapeHtml(page.title)}</h1>
    <p class="lead">${escapeHtml(page.description)}</p>
    ${tableOfContents(html)}
    ${html}
    <footer class="page">${navigation}</footer>`;

  const target = path.join(output, page.slug);
  fs.mkdirSync(target, { recursive: true });
  fs.writeFileSync(
    path.join(target, "index.html"),
    layout({ title: page.title, description: page.description, body: content, current: page.slug }),
  );
}

// The landing page is the site index: it lists every group so a reader can find a page
// without knowing a URL.
const config = JSON.parse(read("docs.json"));
const homeMeta = fs.existsSync(path.join(root, "index.mdx"))
  ? parse(read("index.mdx"))
  : { meta: { title: "Codai documentation", description: "Guides for the Codai coding agent." }, body: "" };

const cards = config.navigation.groups
  .map((group) => {
    const items = group.pages
      .map((page) => {
        const entry = pages.find((item) => item.slug === page);
        return `<li><a href="./${page}/">${escapeHtml(entry?.title ?? page)}</a><p>${escapeHtml(entry?.description ?? "")}</p></li>`;
      })
      .join("");
    return `<h2>${escapeHtml(group.group)}</h2><ul class="cards">${items}</ul>`;
  })
  .join("");

const homeBody = `<h1>${escapeHtml(homeMeta.meta.title)}</h1><p class="lead">${escapeHtml(homeMeta.meta.description)}</p>${render(homeMeta.body)}${cards}<footer class="page"><span></span><a href="${repository}">Source on GitHub</a><span></span></footer>`;

fs.writeFileSync(
  path.join(output, "index.html"),
  layout({
    title: homeMeta.meta.title,
    description: homeMeta.meta.description,
    body: homeBody,
    current: null,
  }),
);

if (fs.existsSync(path.join(root, "assets"))) {
  fs.cpSync(path.join(root, "assets"), path.join(output, "assets"), { recursive: true });
}

// Pages serves the site from a subpath, so relative links are resolved with <base>-free
// paths above; this file keeps GitHub from running Jekyll over the output.
fs.writeFileSync(path.join(output, ".nojekyll"), "");

console.log(`built ${pages.length + 1} pages into docs/_site`);
