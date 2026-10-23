import nextra from "nextra";
import { fileURLToPath } from "node:url";
import { basePath } from "./site.config.mjs";

const withNextra = nextra({});

export default withNextra({
  output: "export",
  basePath,
  trailingSlash: true,
  images: { unoptimized: true },
  poweredByHeader: false,
  outputFileTracingRoot: fileURLToPath(new URL(".", import.meta.url)),
});
