import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { parse } from "yaml";
import { checkDocumentation, hasInvalidParagraphNesting, htmlAssetUrls, pageRoute } from "./check.mjs";
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

test("publishes focused guides with valid navigation and README links", async () => {
  const root = fileURLToPath(new URL(".", import.meta.url));
  const readme = fs.readFileSync(new URL("../README.md", import.meta.url), "utf8");
  const result = await checkDocumentation(root, readme);
  assert.deepEqual(result.errors, []);
  const routes = new Set(result.pages.map(pageRoute));
  for (const route of [
    "agent-mode", "usage/terminal", "usage/tools", "usage/permissions",
    "usage/sessions", "usage/context", "providers", "providers/models",
    "providers/pricing", "customization/configuration", "customization/instructions",
    "customization/commands", "resources/cli", "resources/troubleshooting",
  ]) {
    assert.ok(routes.has(route), `${route} must be reachable through navigation`);
  }
});

test("preserves published Agent mode section anchors", () => {
  const source = fs.readFileSync(new URL("./content/agent-mode.mdx", import.meta.url), "utf8");
  const anchors = new Set([...source.matchAll(/^#{2,3} (.+)$/gm)]
    .map(([, heading]) => heading.toLowerCase().replaceAll(" ", "-")));
  for (const anchor of [
    "modes", "session-startup-and-context", "terminal-input", "connection-setup",
    "provider-models", "api-cost-estimates", "composer", "approvals",
    "configuration-controls", "conversation-controls", "plan-and-act",
    "checkpoints", "context-and-delegation",
  ]) {
    assert.ok(anchors.has(anchor), `published #${anchor} must remain available`);
  }
});

test("documents every named built-in tool", () => {
  const directory = new URL("../src/Codai.Adapters/Tools/Tools/", import.meta.url);
  const reference = fs.readFileSync(new URL("./content/usage/tools.mdx", import.meta.url), "utf8");
  const tools = ["task"];
  for (const name of fs.readdirSync(directory).filter(name => name.endsWith(".cs"))) {
    const source = fs.readFileSync(new URL(name, directory), "utf8");
    const match = /public override string Name => "([^"]+)";/.exec(source);
    if (match) tools.push(match[1]);
  }
  assert.ok(tools.length > 1, "the tool source directory must not be empty");
  for (const name of tools) {
    assert.ok(reference.includes(`\`${name}\``), `${name} must appear in the tool reference`);
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

test("detects block elements nested directly inside MDX paragraph wrappers", () => {
  for (const tag of ["p", "div", "h2", "pre", "table", "ul", "ol"]) {
    assert.equal(hasInvalidParagraphNesting(`<p class="lead">\n<${tag}>Content</${tag}></p>`), true);
  }
  assert.equal(hasInvalidParagraphNesting('<div class="lead"><p>Valid introduction.</p></div>'), false);
  assert.equal(hasInvalidParagraphNesting('<p>Text with <code>inline code</code> and <a href="/">a link</a>.</p>'), false);
});
