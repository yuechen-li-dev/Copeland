import { createApp, h } from "vue";
import * as demo from "@demo";

await Reflect.get(globalThis, "__copelandFrameArtifactsReady");
const lifecycle = new URL(location.href).pathname.includes("lifecycle");
const present = Reflect.get(demo, lifecycle ? "Directory" : "Application") as (
    ...inputs: number[]
) => unknown;
if (typeof present !== "function")
    throw new Error("Missing generated presentation export");
const app = createApp({
    render() {
        const node = lifecycle ? present() : present(window.innerWidth);
        return node as ReturnType<typeof h>;
    },
});
app.mount("#app");
Object.defineProperty(globalThis, "__presentationDemo", {
    value: { unmount: () => app.unmount(), demo },
});
