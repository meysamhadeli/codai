import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// Structural checks for the documentation site. Runs in CI before the build, so a page
// that is missing, unregistered or unreachable fails the pull request instead of
// publishing a broken site.
const root = path.dirname(fileURLToPath(import.meta.url));
const siteUrl = "https://meysamhadeli.github.io/codai";

const read = (file) => fs.readFileSync(path.join(root, file), "utf8");
const exists = (file) => fs.existsSync(path.join(root, file));

const config = JSON.parse(read("docs.json"));
const registered = config.navigation.groups.flatMap((group) => group.pages);

const errors = [];

function frontmatter(source) {
  const match = /^---\r?\n([\s\S]*?)\r?\n---\r?\n/.exec(source);
  if (!match) return null;

  return Object.fromEntries(
    match[1]
      .split(/\r?\n/)
      .map((line) => /^([A-Za-z][\w-]*):\s*(.*)$/.exec(line))
      .filter(Boolean)
      .map(([, key, value]) => [key, value.replace(/^["']|["']$/g, "").trim()]),
  );
}

for (const page of registered) {
  if (!exists(`${page}.mdx`)) {
    errors.push(`docs.json lists "${page}" but docs/${page}.mdx does not exist.`);
  }
}

for (const file of fs.readdirSync(root).filter((name) => name.endsWith(".mdx"))) {
  const page = file.replace(/\.mdx$/, "");
  const meta = frontmatter(read(file));

  if (page !== "index" && !registered.includes(page)) {
    errors.push(`docs/${file} is not registered in the navigation in docs/docs.json.`);
  }
  if (!meta?.title) errors.push(`docs/${file} has no "title" in its frontmatter.`);
  if (!meta?.description) errors.push(`docs/${file} has no "description" in its frontmatter.`);
}

// Every docs link in the README has to land on a page that exists, otherwise the landing
// page sends a reader to a 404.
const readme = fs.readFileSync(path.join(root, "..", "README.md"), "utf8");
for (const [, url] of readme.matchAll(new RegExp(`${siteUrl}/([a-z0-9-]+)`, "g"))) {
  if (!registered.includes(url)) {
    errors.push(`README.md links to ${siteUrl}/${url}, which is not a registered page.`);
  }
}

if (errors.length > 0) {
  for (const error of errors) console.error(`::error::${error}`);
  process.exit(1);
}

console.log(`docs ok: ${registered.length} pages, ${fs.readdirSync(root).length} files`);
