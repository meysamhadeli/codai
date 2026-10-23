import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { basePath } from "./site.config.mjs";
import { checkDocumentation, htmlAssetUrls, pageRoute } from "./check.mjs";

const root = path.dirname(fileURLToPath(import.meta.url));
const output = path.join(root, "out");
const readme = fs.readFileSync(path.join(root, "..", "README.md"), "utf8");
const { errors, pages } = await checkDocumentation(root, readme);

for (const page of pages) {
  const file = path.join(output, pageRoute(page), "index.html");
  if (!fs.existsSync(file)) {
    errors.push(`Static export is missing ${pageRoute(page) || "/"}.`);
    continue;
  }
  const html = fs.readFileSync(file, "utf8");
  for (const url of htmlAssetUrls(html)) {
    if (!url.startsWith("/") || url.startsWith("//")) continue;
    const pathname = decodeURIComponent(url.split(/[?#]/)[0]);
    if (pathname !== basePath && !pathname.startsWith(`${basePath}/`)) {
      errors.push(`${page}: "${url}" escapes GitHub Pages base path ${basePath}.`);
      continue;
    }
    const target = path.join(output, pathname.slice(basePath.length));
    if (!fs.existsSync(target) && !fs.existsSync(path.join(target, "index.html"))) {
      errors.push(`${page}: exported link or asset "${url}" is missing.`);
    }
  }
}

for (const asset of ["_pagefind/pagefind.js", ".nojekyll"]) {
  if (!fs.existsSync(path.join(output, asset))) errors.push(`Static export is missing ${asset}.`);
}

if (errors.length > 0) {
  for (const error of [...new Set(errors)]) console.error(`::error::${error}`);
  process.exitCode = 1;
} else {
  console.log(`export ok: ${pages.length} pages, base path ${basePath}, links, assets and search`);
}
