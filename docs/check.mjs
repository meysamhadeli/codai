import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { siteUrl } from "./site.config.mjs";

function sourceFiles(directory, prefix = "") {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const relative = prefix + entry.name;
    if (entry.isDirectory()) return sourceFiles(path.join(directory, entry.name), `${relative}/`);
    return entry.isFile() && entry.name.endsWith(".mdx") ? [relative] : [];
  });
}

async function navigationPages(directory, errors, prefix = "") {
  const metaFile = path.join(directory, "_meta.js");
  if (!fs.existsSync(metaFile)) {
    errors.push(`content/${prefix}_meta.js is missing.`);
    return [];
  }
  const { default: metadata } = await import(pathToFileURL(metaFile).href);
  const pages = [];
  for (const [name, entry] of Object.entries(metadata)) {
    if (entry?.type === "separator") continue;
    const slug = prefix + name;
    if (fs.existsSync(path.join(directory, `${name}.mdx`))) {
      pages.push(slug);
    } else if (fs.existsSync(path.join(directory, name)) && fs.statSync(path.join(directory, name)).isDirectory()) {
      pages.push(...await navigationPages(path.join(directory, name), errors, `${slug}/`));
    } else {
      errors.push(`Navigation lists "${slug}" but content/${slug}.mdx does not exist.`);
    }
  }
  return pages;
}

export const pageRoute = (page) => page.replace(/(^|\/)index$/, "").replace(/\/$/, "");

export const htmlAssetUrls = (html) =>
  [...html.matchAll(/\s(?:href|src)="([^"]+)"/g)].map((match) => match[1]);

export async function checkDocumentation(root, readme) {
  const content = path.join(root, "content");
  const errors = [];
  const registered = await navigationPages(content, errors);
  const routes = new Set(registered.map(pageRoute));
  const files = sourceFiles(content);

  for (const file of files) {
    const page = file.replace(/\.mdx$/, "");
    const source = fs.readFileSync(path.join(content, file), "utf8");
    const match = /^---\r?\n([\s\S]*?)\r?\n---\r?\n/.exec(source);
    const meta = Object.fromEntries(
      (match?.[1] ?? "").split(/\r?\n/)
        .map((line) => /^([A-Za-z][\w-]*):\s*(.*)$/.exec(line))
        .filter(Boolean)
        .map(([, key, value]) => [key, value.replace(/^["']|["']$/g, "").trim()]),
    );

    if (!registered.includes(page)) {
      errors.push(`content/${file} is not registered in its _meta.js navigation.`);
    }
    if (!meta.title) errors.push(`content/${file} has no "title" in its frontmatter.`);
    if (!meta.description) errors.push(`content/${file} has no "description" in its frontmatter.`);

    const body = source.replace(/^```[\s\S]*?^```/gm, "");
    const links = [
      ...[...body.matchAll(/\[[^\]]*\]\(([^)\s]+)\)/g)].map((match) => match[1]),
      ...[...body.matchAll(/\bhref="([^"]+)"/g)].map((match) => match[1]),
    ];
    for (const link of links) {
      if (/^(?:[a-z]+:|\/\/|#)/i.test(link)) continue;
      const target = new URL(link, `https://docs.invalid/${pageRoute(page)}/`);
      const route = decodeURIComponent(target.pathname).replace(/^\/|\/$/g, "");
      if (!routes.has(route)) errors.push(`content/${file} links to missing page "${link}".`);
    }
  }

  const escapedUrl = siteUrl.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  for (const [, page] of readme.matchAll(new RegExp(`${escapedUrl}/([a-z0-9/-]+)`, "g"))) {
    const slug = page.replace(/\/$/, "");
    if (!routes.has(slug)) {
      errors.push(`README.md links to ${siteUrl}/${slug}, which is not a registered page.`);
    }
  }

  return { errors, pageCount: files.length, pages: registered };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.dirname(fileURLToPath(import.meta.url));
  const readme = fs.readFileSync(path.join(root, "..", "README.md"), "utf8");
  const { errors, pageCount } = await checkDocumentation(root, readme);
  if (errors.length > 0) {
    for (const error of errors) console.error(`::error::${error}`);
    process.exitCode = 1;
  } else {
    console.log(`docs ok: ${pageCount} pages`);
  }
}
