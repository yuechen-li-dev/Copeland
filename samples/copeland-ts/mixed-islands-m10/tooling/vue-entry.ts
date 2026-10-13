import { createApp, type VNode } from "vue";
import * as demo from "@demo";
import * as runtime from "@copeland/browser-v1";

await Reflect.get(globalThis, "__copelandFrameArtifactsReady");
const present = Reflect.get(demo, "Application") as () => VNode;
const app = createApp({ render: () => present() });
app.mount("#app");
Object.defineProperty(globalThis, "__mixedDemo", {
    value: { runtime, unmount: () => app.unmount() },
});
