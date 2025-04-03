import type {Router} from "vue-router";
import {defineClientConfig} from 'vuepress/client';
import CloudBanner from "./components/CloudBanner.vue";

interface ClientConfig {
    enhance?: (context: {
        app: any;
        router: Router;
        siteData: any;
    }) => void | Promise<void>;
    setup?: () => void;
}

export default defineClientConfig({
    enhance({app, router, siteData}) {
        app.component("CloudBanner", CloudBanner);
    }
} satisfies ClientConfig);