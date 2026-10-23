import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { parse } from "yaml";
import { checkDocumentation, htmlAssetUrls, pageRoute } from "./check.mjs";
import { basePath, brandColor, brandTheme } from "./site.config.mjs";

test("validates documentation deployment inputs on pull requests and pushes", () => {
  const workflow = parse(fs.readFileSync(new URL("../.github/workflows/docs.yml", import.meta.url), "utf8"));
  assert.deepEqual(workflow.on.pull_request.paths, workflow.on.push.paths);
  assert.ok(workflow.on.pull_request.paths.includes("README.md"));
  assert.ok(workflow.on.pull_request.paths.includes(".github/workflows/docs.yml"));
});

function fixture(t, metadata, files) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "codai-docs-test-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.writeFileSync(path.join(root, "package.json"), '{"type":"module"}');
  fs.mkdirSync(path.join(root, "content"));
  fs.writeFileSync(path.join(root, "content", "_meta.js"), `export default ${JSON.stringify(metadata)};`);
  for (const [file, source] of Object.entries(files)) {
    const target = path.join(root, "content", file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, source);
  }
  return root;
}

const page = '---\ntitle: "Guide"\ndescription: "A guide."\n---\n\n# Guide\n';

test("uses terminal blue and GitHub Pages base path", () => {
  assert.equal(basePath, "/codai");
  assert.equal(brandColor, "#00AFFF");
  assert.equal(brandTheme.saturation, 100);
  assert.equal(brandTheme.lightness, 50);
  assert.equal(Math.round(255 * (4 - brandTheme.hue / 60)), 175);
  for (const asset of ["logo-light.svg", "logo-dark.svg", "favicon.svg"]) {
    const svg = fs.readFileSync(new URL(`./public/assets/${asset}`, import.meta.url), "utf8");
    assert.ok(svg.includes(`fill="${brandColor}"`), `${asset} must use the terminal accent`);
  }
});

test("accepts native nested Nextra navigation and README links", async (t) => {
  const root = fixture(t, {
    start: { type: "separator", title: "Start" },
    index: "Overview",
    "getting-started": "Quickstart",
  }, {
    "index.mdx": page + '\n[Start](/getting-started/quickstart)\n',
    "getting-started/_meta.js": 'export default {quickstart: "Quickstart"};',
    "getting-started/quickstart.mdx": page,
  });
  const result = await checkDocumentation(root,
    "[Start](https://meysamhadeli.github.io/codai/getting-started/quickstart)");
  assert.deepEqual(result.errors, []);
  assert.equal(result.pageCount, 2);
  assert.deepEqual(result.pages, ["index", "getting-started/quickstart"]);
});

test("rejects unregistered pages in nested directories", async (t) => {
  const root = fixture(t, { index: "Overview" }, { "index.mdx": page, "guides/hidden.mdx": page });
  assert.match((await checkDocumentation(root, "")).errors.join("\n"), /guides\/hidden.mdx is not registered/);
});

test("rejects missing pages and empty frontmatter fields", async (t) => {
  const root = fixture(t, { index: "Overview", missing: "Missing" }, {
    "index.mdx": '---\ntitle: ""\ndescription: ""\n---\n',
  });
  const { errors } = await checkDocumentation(root, "");
  assert.equal(errors.length, 3);
  assert.match(errors.join("\n"), /missing.mdx does not exist/);
  assert.match(errors.join("\n"), /no "title"/);
  assert.match(errors.join("\n"), /no "description"/);
});

test("rejects missing README and MDX routes including card links", async (t) => {
  const root = fixture(t, { index: "Overview" }, {
    "index.mdx": page + '\n[Missing](/missing)\n\n<Cards.Card href="/also-missing" title="Missing" />\n',
  });
  const { errors } = await checkDocumentation(root,
    "[Missing](https://meysamhadeli.github.io/codai/guides/missing)");
  assert.equal(errors.length, 3);
  assert.match(errors.join("\n"), /links to missing page "\/missing"/);
  assert.match(errors.join("\n"), /links to missing page "\/also-missing"/);
  assert.match(errors.join("\n"), /guides\/missing, which is not a registered page/);
});

test("ignores examples in fenced code blocks and normalizes index routes", async (t) => {
  const root = fixture(t, { index: "Overview" }, {
    "index.mdx": page + '\n```mdx\n[Example](/not-a-real-page)\n```\n',
  });
  assert.deepEqual((await checkDocumentation(root, "")).errors, []);
  assert.equal(pageRoute("index"), "");
  assert.equal(pageRoute("guides/index"), "guides");
});

test("checks real exported asset attributes without mistaking Nextra data attributes for links", () => {
  assert.deepEqual(htmlAssetUrls(
    '<button data-href="/getting-started">Start</button><a href="/codai/skills/">Skills</a>' +
    '<img src="/codai/assets/logo-light.svg" data-src="/not-an-asset" />',
  ), ["/codai/skills/", "/codai/assets/logo-light.svg"]);
});
