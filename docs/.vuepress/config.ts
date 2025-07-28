import {defineUserConfig} from "vuepress";
import {importCodePlugin} from "./markdown/xode/importCodePlugin";
import {resolveMultiSamplesPath} from "./lib/samples";
import {linkCheckPlugin} from "./markdown/linkCheck";
import {replaceLinkPlugin} from "./markdown/replaceLink";
import viteBundler from "@vuepress/bundler-vite";
import {hopeTheme} from "vuepress-theme-hope";
import {fs} from "vuepress/utils";
import { dl } from "@mdit/plugin-dl";

const projectionSamplesPath = "https://raw.githubusercontent.com/EventStore/EventStore/53f84e55ea56ccfb981aff0e432581d72c23fbf6/samples/http-api/data/";

export default defineUserConfig({
    base: "/",
    dest: "public",
    title: "EventStoreDB Docs",
    description: "Event-native database",
    bundler: viteBundler(),
    markdown: {importCode: false},
    extendsMarkdown: md => {
        md.use(importCodePlugin, {
            handleImportPath: s => resolveMultiSamplesPath(s)
        });
        md.use(linkCheckPlugin);
        md.use(replaceLinkPlugin, {
            replaceLink: (link: string, _) => link
                .replace("@httpapi/data/", projectionSamplesPath)
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
        },
        navbar: [
            {
                text: "Server",
                link: "/server/quick-start/",
            },
        ],
        iconAssets: "iconify",
        plugins: {
            search: {},
            mdEnhance: {
                figure: true,
                imgLazyload: true,
                imgMark: true,
                imgSize: true,
                tabs: true,
                codetabs: true,
            },
            sitemap:{
                devServer: process.env.NODE_ENV === 'development',
                modifyTimeGetter: (page, app) =>
                    fs.statSync(app.dir.source(page.filePathRelative!)).mtime.toISOString()
            },
            shiki: {
                themes: {
                    light: "one-light",
                    dark: "one-dark-pro",
                },
            },
        }
    }),
});
