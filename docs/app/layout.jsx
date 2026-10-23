import Link from "next/link";
import { Head } from "nextra/components";
import { getPageMap } from "nextra/page-map";
import { Footer, Layout, Navbar } from "nextra-theme-docs";
import { basePath, brandColor, brandTheme, siteUrl } from "../site.config.mjs";
import "nextra-theme-docs/style.css";
import "./globals.css";

export const metadata = {
  metadataBase: new URL(`${siteUrl}/`),
  title: { default: "Codai documentation", template: "%s - Codai" },
  description: "The open source coding agent for your terminal.",
  icons: { icon: `${basePath}/assets/favicon.svg` },
};

const navbar = (
  <Navbar
    logo={
      <span className="codai-logo">
        <img className="codai-logo-light" src={`${basePath}/assets/logo-light.svg`} width="154" height="35" alt="Codai" />
        <img className="codai-logo-dark" src={`${basePath}/assets/logo-dark.svg`} width="154" height="35" alt="Codai" />
      </span>
    }
    projectLink="https://github.com/meysamhadeli/codai"
  >
    <Link className="codai-start" href="/getting-started/quickstart">Get started</Link>
  </Navbar>
);

export default async function RootLayout({ children }) {
  return (
    <html lang="en" dir="ltr" suppressHydrationWarning style={{ "--codai-blue": brandColor }}>
      <Head color={brandTheme} backgroundColor={{ light: "#FAFAF9", dark: "#0F0F0F" }} />
      <body>
        <Layout
          navbar={navbar}
          pageMap={await getPageMap()}
          docsRepositoryBase="https://github.com/meysamhadeli/codai/tree/main/docs/content"
          sidebar={{ defaultMenuCollapseLevel: 2 }}
          footer={<Footer>Codai. Open source. Built for your terminal.</Footer>}
        >
          {children}
        </Layout>
      </body>
    </html>
  );
}
