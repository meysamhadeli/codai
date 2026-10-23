import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { checkDocumentation } from "./check.mjs";

function fixture(t, pages, files) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "codai-docs-test-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.writeFileSync(path.join(root, "docs.json"), JSON.stringify({
    navigation: { tabs: [{ tab: "Docs", groups: [{ group: "Guides", pages }] }] },
  }));
  for (const [file, source] of Object.entries(files)) {
    const target = path.join(root, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, source);
  }
  return root;
}

const page = '---\ntitle: "Guide"\ndescription: "A guide."\n---\n\nHello.\n';

test("uses the default terminal blue consistently across documentation branding", () => {
  const blue = "#00AFFF"; // Spectre.Console deepskyblue1.
  const config = JSON.parse(fs.readFileSync(new URL("./docs.json", import.meta.url), "utf8"));
  assert.deepEqual(config.colors, { primary: blue, light: blue, dark: blue });
  for (const asset of [config.logo.light, config.logo.dark, config.favicon]) {
    const svg = fs.readFileSync(new URL(`.${asset}`, import.meta.url), "utf8");
    assert.ok(svg.includes(`fill="${blue}"`), `${asset} must use the terminal accent`);
  }
});

test("accepts nested navigation and nested README links", (t) => {
  const root = fixture(t, ["index", { group: "Start", pages: ["getting-started/quickstart"] }], {
    "index.mdx": page,
    "getting-started/quickstart.mdx": page,
    "_site/ignored.mdx": "",
    "node_modules/ignored.mdx": "",
  });
  assert.deepEqual(checkDocumentation(root,
    "[Start](https://meysamhadeli.github.io/codai/getting-started/quickstart)"),
  { errors: [], pageCount: 2 });
});

test("rejects unregistered pages in nested directories", (t) => {
  const root = fixture(t, ["index"], { "index.mdx": page, "guides/hidden.mdx": page });
  assert.match(checkDocumentation(root, "").errors.join("\n"), /guides\/hidden.mdx is not registered/);
});

test("rejects missing pages and empty frontmatter fields", (t) => {
  const root = fixture(t, ["index", "missing"], {
    "index.mdx": '---\ntitle: ""\ndescription: ""\n---\n',
  });
  const { errors } = checkDocumentation(root, "");
  assert.equal(errors.length, 3);
  assert.match(errors.join("\n"), /missing.mdx does not exist/);
  assert.match(errors.join("\n"), /no "title"/);
  assert.match(errors.join("\n"), /no "description"/);
});

test("rejects README links to missing nested pages", (t) => {
  const root = fixture(t, ["index"], { "index.mdx": page });
  assert.match(checkDocumentation(root,
    "[Missing](https://meysamhadeli.github.io/codai/guides/missing)").errors.join("\n"),
  /guides\/missing, which is not a registered page/);
});
