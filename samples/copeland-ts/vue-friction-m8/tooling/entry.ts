import { createApp, h } from "vue";
import * as demoModule from "@demo";

interface Demo {
    Application(width: number): unknown;
    Directory(): unknown;
}
const demo = demoModule as Demo;
await Reflect.get(globalThis, "__copelandFrameArtifactsReady");
const lifecycle = new URL(location.href).pathname.includes("lifecycle");
const app = createApp({
    render() {
        const node = lifecycle ? demo.Directory() : demo.Application(window.innerWidth);
        return node as ReturnType<typeof h>;
    },
});
app.mount("#app");
Object.defineProperty(globalThis, "__vueDemo", { value: { app, demo } });
