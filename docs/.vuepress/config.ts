import { dl } from "@mdit/plugin-dl";
import viteBundler from "@vuepress/bundler-vite";
import vueDevTools from 'vite-plugin-vue-devtools'
import {defineUserConfig} from "vuepress";
import {fs} from "vuepress/utils";
import {hopeTheme} from "vuepress-theme-hope";
import {resolveMultiSamplesPath} from "./lib/samples";
import {linkCheckPlugin} from "./markdown/linkCheck";
import {replaceLinkPlugin} from "./markdown/replaceLink";
import {importCodePlugin} from "./markdown/xode/importCodePlugin";

const projectionSamplesPath = "https://raw.githubusercontent.com/EventStore/EventStore/53f84e55ea56ccfb981aff0e432581d72c23fbf6/samples/http-api/data/";

export default defineUserConfig({
    base: "/",
    dest: "public",
    title: "KurrentDB Docs",
    description: "Event-native database",
    bundler: viteBundler({viteOptions: {plugins: [vueDevTools(),],}}),
    markdown: {importCode: false},
    extendsMarkdown: md => {
        md.use(importCodePlugin, {
            handleImportPath: s => resolveMultiSamplesPath(s)
        });
        md.use(linkCheckPlugin);
        md.use(replaceLinkPlugin, {
            replaceLink: (link: string, _) => link
                .replace("@server", "/server")
                .replace("@clients/http-api/", "/http-api/")
                .replace("@clients/httpapi/", "/http-api/")
                .replace("@httpapi/data/", projectionSamplesPath)
                .replace("@httpapi", "/http-api")
        });
        md.use(dl);
    },
    theme: hopeTheme({
        logo: "/eventstore-dev-logo-dark.svg",
        logoDark: "/eventstore-logo-alt.svg",
        docsDir: ".",
        toc: true,
        sidebar: {
            "/server/": "structure",
            "/http-api/": "structure"
        },
        navbar: [
            {
                text: "Server",
                link: "/server/quick-start/",
            },
            {
                text: "HTTP API",
                link: "/http-api/"
            }
        ],
        markdown: {
            figure: true,
            imgLazyload: true,
            imgMark: true,
            imgSize: true,
            tabs: true,
            codeTabs: true,
            component: true,
            mermaid: true,
            highlighter: {
                type: "shiki",
                themes: {
                    light: "one-light",
                    dark: "one-dark-pro",
                }
            }
        },
        plugins: {
            search: {},
            sitemap:{
                devServer: process.env.NODE_ENV === 'development',
                modifyTimeGetter: (page, app) =>
                    fs.statSync(app.dir.source(page.filePathRelative!)).mtime.toISOString()
            },
            components: {
                components: ["Badge", "VPBanner", "VPCard", "VidStack"]
            },
        }
    }),
});
