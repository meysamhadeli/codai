import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// Keep legacy README links valid until a public Mintlify URL is configured.
const siteUrl = "https://meysamhadeli.github.io/codai";

function navigationPages(node) {
  if (typeof node === "string") return [node];
  if (Array.isArray(node)) return node.flatMap(navigationPages);
  if (!node || typeof node !== "object") return [];
  return ["pages", "groups", "tabs"].flatMap((key) => navigationPages(node[key]));
}

function sourceFiles(directory, prefix = "") {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    if (["node_modules", ".mintlify", "_site", ".git"].includes(entry.name)) return [];
    const relative = prefix + entry.name;
    if (entry.isDirectory()) return sourceFiles(path.join(directory, entry.name), `${relative}/`);
    return entry.isFile() && entry.name.endsWith(".mdx") ? [relative] : [];
  });
}

export function checkDocumentation(root, readme) {
  const config = JSON.parse(fs.readFileSync(path.join(root, "docs.json"), "utf8"));
  const registered = navigationPages(config.navigation);
  const errors = [];
  const files = sourceFiles(root);

  for (const page of registered) {
    if (!fs.existsSync(path.join(root, `${page}.mdx`))) {
      errors.push(`docs.json lists "${page}" but docs/${page}.mdx does not exist.`);
    }
  }

  for (const file of files) {
    const page = file.replace(/\.mdx$/, "");
    const source = fs.readFileSync(path.join(root, file), "utf8");
    const match = /^---\r?\n([\s\S]*?)\r?\n---\r?\n/.exec(source);
    const meta = Object.fromEntries(
      (match?.[1] ?? "").split(/\r?\n/)
        .map((line) => /^([A-Za-z][\w-]*):\s*(.*)$/.exec(line))
        .filter(Boolean)
        .map(([, key, value]) => [key, value.replace(/^["']|["']$/g, "").trim()]),
    );

    if (!registered.includes(page)) {
      errors.push(`docs/${file} is not registered in the navigation in docs/docs.json.`);
    }
    if (!meta.title) errors.push(`docs/${file} has no "title" in its frontmatter.`);
    if (!meta.description) errors.push(`docs/${file} has no "description" in its frontmatter.`);
  }

  const escapedUrl = siteUrl.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  for (const [, page] of readme.matchAll(new RegExp(`${escapedUrl}/([a-z0-9/-]+)`, "g"))) {
    const slug = page.replace(/\/$/, "");
    if (!registered.includes(slug)) {
      errors.push(`README.md links to ${siteUrl}/${slug}, which is not a registered page.`);
    }
  }

  return { errors, pageCount: files.length };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.dirname(fileURLToPath(import.meta.url));
  const readme = fs.readFileSync(path.join(root, "..", "README.md"), "utf8");
  const { errors, pageCount } = checkDocumentation(root, readme);
  if (errors.length > 0) {
    for (const error of errors) console.error(`::error::${error}`);
    process.exitCode = 1;
  } else {
    console.log(`docs ok: ${pageCount} pages`);
  }
}
